# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import hashlib
import struct
import sys
import tempfile
import unittest
from pathlib import Path


sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "dev"))
from check_godot_mcp_export import (  # noqa: E402
    IsolationError, check_export, check_pck, check_presets, check_project_binary,
)


def settings(**values: str) -> bytes:
    data = b"ECFG" + struct.pack("<I", len(values))
    for key, value in values.items():
        key_bytes, text = key.encode(), value.encode()
        # Godot Variant::STRING = 4; payload length excludes alignment padding.
        variant = struct.pack("<II", 4, len(text)) + text + bytes((-len(text)) % 4)
        data += struct.pack("<I", len(key_bytes)) + key_bytes
        data += struct.pack("<I", len(variant)) + variant
    return data


def pack(files: dict[str, bytes], *, version: int = 4, flags: int = 2) -> bytes:
    header_size = 96 if version == 2 else 112
    records = []
    bodies = b""
    for name, content in files.items():
        encoded = name.encode()
        encoded += bytes((-len(encoded)) % 4)
        record = struct.pack("<I", len(encoded)) + encoded
        record += struct.pack("<QQ", len(bodies), len(content))
        record += hashlib.md5(content).digest() + struct.pack("<I", 0)
        records.append(record)
        bodies += content
    directory = struct.pack("<I", len(records)) + b"".join(records)
    file_base = header_size + len(directory) if version == 2 else header_size
    header = b"GDPC" + struct.pack("<IIIIIQ", version, 4, 7, 2, flags, file_base)
    if version == 2:
        return header + bytes(64) + directory + bodies
    directory_offset = header_size + len(bodies)
    header += struct.pack("<Q", directory_offset)
    return header.ljust(header_size, b"\0") + bodies + directory


class GodotMcpExportIsolationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def write_pack(self, files: dict[str, bytes], **options: int) -> Path:
        path = self.root / "SomeCardGameShit.pck"
        path.write_bytes(pack(files, **options))
        return path

    def test_v2_v3_v4_real_directory_offsets_and_settings(self) -> None:
        for version in (2, 3, 4):
            with self.subTest(version=version):
                path = self.write_pack({
                    "res://assets/card.ctex": b"texture-placeholder",
                    "res://project.binary": settings(**{"application/config/name": "游戏"}),
                }, version=version)
                evidence = check_pck(path)
                self.assertEqual(evidence["format"], version)
                self.assertEqual(evidence["entries_checked"], 2)
                self.assertEqual(evidence["project_settings_checked"], 1)

    def test_compiled_addon_probe_and_config_entries_are_rejected(self) -> None:
        for name in (
            "res://addons/godot_mcp_toolkit/runtime/server.gdc",
            "res://__mcp_probe/Probe.gdc", "res://.mcp.json",
        ):
            with self.subTest(name=name):
                path = self.write_pack({"res://project.binary": settings(), name: b"dev"})
                with self.assertRaisesRegex(IsolationError, "tooling was packaged"):
                    check_pck(path)

    def test_missing_project_binary_never_counts_as_clean(self) -> None:
        with self.assertRaisesRegex(IsolationError, "no project.binary"):
            check_pck(self.write_pack({"res://main.tscn": b"scene"}))

    def test_autoload_reference_rejected_even_without_addon_files(self) -> None:
        data = settings(**{"autoload/MCPRuntimeServer": "*res://somewhere/renamed.gd"})
        with self.assertRaisesRegex(IsolationError, "autoload/MCPRuntimeServer"):
            check_pck(self.write_pack({"res://project.binary": data}))

    def test_addon_reference_under_renamed_setting_rejected(self) -> None:
        data = settings(**{"autoload/Renamed": "*res://addons/godot_mcp_toolkit/runtime/server.gd"})
        with self.assertRaisesRegex(IsolationError, "autoload/Renamed"):
            check_project_binary(data)

    def test_non_mcp_product_autoload_is_allowed(self) -> None:
        data = settings(**{"autoload/ProductState": "*res://scripts/ProductState.gd"})
        self.assertEqual(check_project_binary(data), 1)

    def test_global_script_class_cache_cannot_keep_excluded_addon_references(self) -> None:
        for text in ('class="MCPToolkitExtension"', 'path="res://addons/godot_mcp_toolkit/extension.gd"',
                     'path="res://__mcp_probe/Probe.gd"'):
            path = self.write_pack({"project.binary": settings(),
                ".godot/global_script_class_cache.cfg": text.encode()})
            with self.subTest(text=text), self.assertRaisesRegex(IsolationError, "class cache"):
                check_pck(path)
        path = self.write_pack({"project.binary": settings(),
            ".godot/global_script_class_cache.cfg": b'path="res://scripts/ProductState.gd"'})
        self.assertEqual(1, check_pck(path)["script_class_caches_checked"])

    def test_toolkit_development_setting_is_rejected(self) -> None:
        data = settings(**{"mcp_toolkit/status": "ready"})
        with self.assertRaisesRegex(IsolationError, "mcp_toolkit/status"):
            check_project_binary(data)

    def test_invalid_settings_header_truncation_and_trailing_data(self) -> None:
        for data in (b"nope", b"ECFG" + struct.pack("<I", 1), settings() + b"trailing"):
            with self.subTest(data=data), self.assertRaises(IsolationError):
                check_project_binary(data)

    def test_encrypted_and_sparse_pack_fail_closed(self) -> None:
        for flags in (1, 4, 8):
            with self.subTest(flags=flags), self.assertRaisesRegex(IsolationError, "flags"):
                check_pck(self.write_pack({"project.binary": settings()}, flags=flags))

    def test_truncated_and_unsupported_pack_fail_closed(self) -> None:
        path = self.write_pack({"project.binary": settings()})
        data = path.read_bytes()
        for malformed in (data[:20], data[:-1], data[:4] + struct.pack("<I", 99) + data[8:]):
            with self.subTest(length=len(malformed)), self.assertRaises(IsolationError):
                path.write_bytes(malformed)
                check_pck(path)

    def test_project_binary_checksum_must_match_directory(self) -> None:
        path = self.write_pack({"project.binary": settings()})
        data = bytearray(path.read_bytes())
        data[112] = ord("X")
        path.write_bytes(data)
        with self.assertRaisesRegex(IsolationError, "checksum"):
            check_pck(path)

    def test_traversal_entry_is_rejected(self) -> None:
        with self.assertRaisesRegex(IsolationError, "invalid package resource"):
            check_pck(self.write_pack({"res://../project.binary": settings()}))

    def test_windows_exe_and_macos_bundle_find_pck(self) -> None:
        path = self.write_pack({"project.binary": settings()})
        exe = path.with_suffix(".exe")
        exe.write_bytes(b"not-executed")
        self.assertEqual(len(check_export(exe)), 1)
        app = self.root / "Game.app"
        resources = app / "Contents/Resources"
        resources.mkdir(parents=True)
        path.rename(resources / path.name)
        self.assertEqual(len(check_export(app)), 1)

    def test_export_loose_addon_files_are_rejected(self) -> None:
        self.write_pack({"project.binary": settings()})
        leaked = self.root / "addons/godot_mcp_toolkit/server.gd"
        leaked.parent.mkdir(parents=True)
        leaked.write_text("dev", encoding="utf-8")
        with self.assertRaisesRegex(IsolationError, "tooling was packaged"):
            check_export(self.root)

    def test_no_pck_is_not_a_success(self) -> None:
        with self.assertRaisesRegex(IsolationError, "no PCK"):
            check_export(self.root)

    def test_presets_require_each_exact_exclusion_and_preserve_other_filters(self) -> None:
        path = self.root / "export_presets.cfg"
        valid = ('[preset.0]\nname="Windows"\nexclude_filter="old/*,'
                 'addons/godot_mcp_toolkit/*,__mcp_probe/*,.mcp.json"\n')
        path.write_text(valid, encoding="utf-8")
        self.assertEqual(check_presets(path), ["Windows"])
        path.write_text(valid.replace("__mcp_probe/*,", ""), encoding="utf-8")
        with self.assertRaisesRegex(IsolationError, "lacks exact MCP exclusions"):
            check_presets(path)


if __name__ == "__main__":
    unittest.main()
