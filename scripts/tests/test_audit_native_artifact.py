# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import struct
import sys
import unittest
from pathlib import Path


SCRIPTS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS))

from audit_native_artifact import (  # noqa: E402
    AuditError,
    _audit_pe,
    _expected_exports,
    _is_dynamic_msvc_runtime,
    _validate_windows_runtime_imports,
)


def _minimal_pe(import_name: str, *, terminate_imports: bool = True) -> bytes:
    data = bytearray(0x1200)
    data[:2] = b"MZ"
    struct.pack_into("<I", data, 0x3C, 0x80)
    data[0x80:0x84] = b"PE\0\0"

    coff = 0x84
    struct.pack_into("<H", data, coff, 0x8664)
    struct.pack_into("<H", data, coff + 2, 1)
    struct.pack_into("<H", data, coff + 16, 240)

    optional = coff + 20
    struct.pack_into("<H", data, optional, 0x20B)
    directories = optional + 112
    struct.pack_into("<II", data, directories, 0x1000, 40)
    import_size = 40 if terminate_imports else 20
    struct.pack_into("<II", data, directories + 8, 0x1100, import_size)

    section = optional + 240
    data[section:section + 8] = b".rdata\0\0"
    struct.pack_into("<IIII", data, section + 8, 0x1000, 0x1000, 0x1000, 0x200)

    export_offset = 0x200
    struct.pack_into("<I", data, export_offset + 24, 0)
    struct.pack_into("<I", data, export_offset + 32, 0x1180)

    import_offset = 0x300
    struct.pack_into("<IIIII", data, import_offset, 0, 0, 0, 0x1140, 0)
    encoded_name = import_name.encode("ascii") + b"\0"
    data[0x340:0x340 + len(encoded_name)] = encoded_name
    return bytes(data)


class NativeArtifactAuditTests(unittest.TestCase):
    def test_v04_and_v05_export_sets_are_parallel_and_exact(self) -> None:
        v04 = _expected_exports("v04")
        v05 = _expected_exports("v05")
        self.assertEqual(14, len(v04))
        self.assertEqual(14, len(v05))
        self.assertEqual(
            {name.replace("scgs_v04_", "") for name in v04},
            {name.replace("scgs_v05_", "") for name in v05},
        )

    def test_dynamic_runtime_names_are_case_insensitive(self) -> None:
        for name in (
            "MSVCP140.dll",
            "msvcp140_1.DLL",
            "VCRUNTIME140.dll",
            "vcruntime140_1d.dll",
        ):
            with self.subTest(name=name):
                self.assertTrue(_is_dynamic_msvc_runtime(name))

        self.assertFalse(_is_dynamic_msvc_runtime("api-ms-win-crt-runtime-l1-1-0.dll"))
        self.assertFalse(_is_dynamic_msvc_runtime("KERNEL32.dll"))

    def test_pe_import_directory_is_parsed(self) -> None:
        architectures, exports, imports = _audit_pe(_minimal_pe("VCRUNTIME140_1.dll"))
        self.assertEqual({"x86_64"}, architectures)
        self.assertEqual(set(), exports)
        self.assertEqual({"VCRUNTIME140_1.dll"}, imports)

    def test_dynamic_runtime_import_is_rejected(self) -> None:
        with self.assertRaisesRegex(AuditError, "dynamic MSVC runtime"):
            _validate_windows_runtime_imports({"KERNEL32.dll", "MSVCP140.dll"})

    def test_static_runtime_import_set_is_accepted(self) -> None:
        _validate_windows_runtime_imports(
            {"KERNEL32.dll", "api-ms-win-crt-runtime-l1-1-0.dll"}
        )

    def test_unterminated_import_directory_is_rejected(self) -> None:
        with self.assertRaisesRegex(AuditError, "unterminated PE import directory"):
            _audit_pe(_minimal_pe("KERNEL32.dll", terminate_imports=False))


if __name__ == "__main__":
    unittest.main()
