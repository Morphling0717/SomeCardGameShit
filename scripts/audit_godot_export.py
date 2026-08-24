#!/usr/bin/env python3
"""Audit a finalized Gate 4B Windows or macOS Godot export."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import plistlib
import re
import struct
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))

from audit_native_artifact import AuditError, _mach_architectures, audit  # noqa: E402
from audit_visual_assets import audit as audit_visual_assets  # noqa: E402


class ExportAuditError(RuntimeError):
    pass


MACH_O_MAGICS = {
    b"\xcf\xfa\xed\xfe",
    b"\xfe\xed\xfa\xcf",
    b"\xca\xfe\xba\xbe",
    b"\xca\xfe\xba\xbf",
}


LICENSE_MARKERS = {
    "GPL-3.0-or-later.txt": "GNU GENERAL PUBLIC LICENSE",
    "THIRD_PARTY_NOTICES.md": "JSON for Modern C++",
    "Godot-LICENSE.txt": "Godot Engine contributors",
    "Godot-COPYRIGHT.txt": "Exhaustive licensing information for files in the Godot Engine repository",
    "Dotnet-Runtime-LICENSE.txt": ".NET Foundation and Contributors",
    "Dotnet-Runtime-THIRD-PARTY-NOTICES.txt":
        ".NET Runtime uses third-party libraries",
    "nlohmann-json-LICENSE.MIT": "Niels Lohmann",
    "NotoSansCJKsc-OFL.txt": "SIL OPEN FONT LICENSE",
    "NotoSansCJKsc-NOTICE.md": "Noto Sans CJK SC",
    "ASSET_NOTICES.md": "OpenAI's built-in image generation workflow",
    "ASSET_MANIFEST.json": "schema_version",
    "BUILD_INFO.txt": "godot=4.7.2.stable.mono",
}


def _pe_architecture(path: Path) -> str:
    data = path.read_bytes()
    if len(data) < 0x40 or data[:2] != b"MZ":
        raise ExportAuditError(f"invalid PE executable: {path}")
    pe_offset = struct.unpack_from("<I", data, 0x3C)[0]
    if data[pe_offset:pe_offset + 4] != b"PE\0\0":
        raise ExportAuditError(f"invalid PE signature: {path}")
    machine = struct.unpack_from("<H", data, pe_offset + 4)[0]
    architectures = {0x8664: "x86_64", 0xAA64: "arm64"}
    if machine not in architectures:
        raise ExportAuditError(f"unsupported PE machine 0x{machine:04x}")
    return architectures[machine]


def _require_managed_data(directory: Path) -> Path:
    data_directories = [path for path in directory.glob("data_*") if path.is_dir()]
    if len(data_directories) != 1:
        raise ExportAuditError(
            f"expected exactly one managed data directory in {directory}, "
            f"found {data_directories}"
        )
    return data_directories[0]


def _audit_dotnet_runtime(directory: Path) -> None:
    runtime_configs = list(directory.glob("*.runtimeconfig.json"))
    if len(runtime_configs) != 1:
        raise ExportAuditError(
            f"expected exactly one .NET runtimeconfig in {directory}, "
            f"found {runtime_configs}"
        )
    config = json.loads(runtime_configs[0].read_text(encoding="utf-8"))
    frameworks = config.get("runtimeOptions", {}).get("includedFrameworks", [])
    versions = {
        framework.get("version")
        for framework in frameworks
        if framework.get("name") == "Microsoft.NETCore.App"
    }
    if versions != {"8.0.30"}:
        raise ExportAuditError(
            f"unexpected embedded Microsoft.NETCore.App versions: {sorted(versions)}"
        )


def _audit_licenses(directory: Path, expected_commit: str | None = None) -> None:
    for filename, marker in LICENSE_MARKERS.items():
        path = directory / filename
        if not path.is_file():
            raise ExportAuditError(f"missing packaged license/notice: {path}")
        if marker not in path.read_text(encoding="utf-8"):
            raise ExportAuditError(f"packaged notice has unexpected content: {path}")
    build_info = (directory / "BUILD_INFO.txt").read_text(encoding="utf-8")
    lines = build_info.splitlines()
    if not lines or lines[0] != "SomeCardGameShit Gate 4B":
        raise ExportAuditError("packaged build info does not identify Gate 4B")
    entries: dict[str, str] = {}
    for line in lines[1:]:
        if line.count("=") != 1:
            raise ExportAuditError("packaged build info contains a malformed field")
        key, value = line.split("=", 1)
        if not key or not value or key in entries:
            raise ExportAuditError("packaged build info contains an empty or duplicate field")
        entries[key] = value
    expected_keys = {"commit", "godot", "dotnet_sdk", "dotnet_runtime"}
    if set(entries) != expected_keys:
        raise ExportAuditError(
            f"packaged build info fields differ: {sorted(entries)}"
        )
    if entries["godot"] != "4.7.2.stable.mono":
        raise ExportAuditError("packaged build info has an unexpected Godot version")
    if entries["dotnet_sdk"] != "10.0.400":
        raise ExportAuditError("packaged build info has an unexpected .NET SDK")
    if entries["dotnet_runtime"] != "8.0.30":
        raise ExportAuditError("packaged build info has an unexpected .NET runtime")
    commit = entries["commit"]
    if commit != "local" and re.fullmatch(r"[0-9a-fA-F]{40}", commit) is None:
        raise ExportAuditError("packaged build info has an invalid commit")
    if expected_commit is not None and commit.lower() != expected_commit.lower():
        raise ExportAuditError(
            "packaged build info commit does not match the current GitHub checkout"
        )


def _audit_font_source() -> None:
    font_directory = ROOT / "client/godot/assets/fonts"
    checksum_line = (font_directory / "SHA256SUMS").read_text(encoding="utf-8").strip()
    expected, filename = checksum_line.split(maxsplit=1)
    font = font_directory / filename
    actual = hashlib.sha256(font.read_bytes()).hexdigest()
    if actual != expected.lower():
        raise ExportAuditError(
            f"font SHA-256 mismatch: expected {expected.lower()}, found {actual}"
        )


def _audit_macos_bundle_architectures(export: Path) -> int:
    checked = 0
    for path in sorted(candidate for candidate in export.rglob("*") if candidate.is_file()):
        with path.open("rb") as stream:
            header = stream.read(4096)
        if header[:4] not in MACH_O_MAGICS:
            continue

        architectures = _mach_architectures(header)
        if architectures != {"arm64"}:
            relative = path.relative_to(export)
            raise ExportAuditError(
                f"macOS bundle Mach-O is not arm64-only: {relative} has "
                f"{sorted(architectures)}"
            )
        checked += 1

    if checked == 0:
        raise ExportAuditError("macOS bundle contains no Mach-O files")
    return checked


def _audit_windows(export: Path, expected_commit: str | None = None) -> None:
    if not export.is_file() or export.suffix.lower() != ".exe":
        raise ExportAuditError(f"missing Windows executable: {export}")
    if _pe_architecture(export) != "x86_64":
        raise ExportAuditError("Windows executable is not x86-64")
    pck = export.with_suffix(".pck")
    if not pck.is_file():
        raise ExportAuditError(f"missing Windows project pack: {pck}")
    managed_data = _require_managed_data(export.parent)
    _audit_dotnet_runtime(managed_data)

    native = export.parent / "scgs_v04.dll"
    audit(native, "x86_64")
    native_matches = list(export.parent.rglob("scgs_v04.dll"))
    if native_matches != [native]:
        raise ExportAuditError(f"unexpected Windows native layout: {native_matches}")
    foreign = list(export.parent.rglob("libscgs_v04.dylib"))
    if foreign:
        raise ExportAuditError(f"macOS native library leaked into Windows export: {foreign}")
    _audit_licenses(export.parent / "licenses", expected_commit)


def _audit_macos(export: Path, expected_commit: str | None = None) -> None:
    if not export.is_dir() or export.suffix != ".app":
        raise ExportAuditError(f"missing macOS app bundle: {export}")
    contents = export / "Contents"
    with (contents / "Info.plist").open("rb") as stream:
        info = plistlib.load(stream)
    executable_name = info.get("CFBundleExecutable")
    if not isinstance(executable_name, str) or not executable_name:
        raise ExportAuditError("macOS Info.plist has no CFBundleExecutable")
    executable = contents / "MacOS" / executable_name
    if _mach_architectures(executable.read_bytes()) != {"arm64"}:
        raise ExportAuditError("macOS executable is not arm64-only")
    _audit_macos_bundle_architectures(export)

    resources = contents / "Resources"
    pcks = list(resources.glob("*.pck"))
    if len(pcks) != 1:
        raise ExportAuditError(f"expected exactly one macOS project pack, found {pcks}")
    managed_data = _require_managed_data(resources)
    _audit_dotnet_runtime(managed_data)

    native = contents / "Frameworks/libscgs_v04.dylib"
    audit(native, "arm64")
    native_matches = list(export.rglob("libscgs_v04.dylib"))
    if native_matches != [native]:
        raise ExportAuditError(f"unexpected macOS native layout: {native_matches}")
    foreign = list(export.rglob("scgs_v04.dll"))
    if foreign:
        raise ExportAuditError(f"Windows native library leaked into macOS export: {foreign}")
    _audit_licenses(resources / "licenses", expected_commit)

    subprocess.run(
        ["/usr/bin/codesign", "--verify", "--deep", "--strict", str(export)],
        check=True,
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--platform", required=True, choices=("windows-x86_64", "macos-arm64")
    )
    parser.add_argument("--export", required=True, type=Path)
    args = parser.parse_args()

    try:
        export = args.export.resolve(strict=True)
        _audit_font_source()
        audit_visual_assets(ROOT)
        expected_commit = os.environ.get("GITHUB_SHA")
        if args.platform == "windows-x86_64":
            _audit_windows(export, expected_commit)
        else:
            _audit_macos(export, expected_commit)
    except (
        AuditError,
        ExportAuditError,
        OSError,
        plistlib.InvalidFileException,
        json.JSONDecodeError,
        struct.error,
        subprocess.CalledProcessError,
        ValueError,
    ) as error:
        print(f"Godot export audit failed: {error}", file=sys.stderr)
        return 1

    print(f"audited finalized {args.platform} Godot export: {export}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
