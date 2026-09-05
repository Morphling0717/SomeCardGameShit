#!/usr/bin/env python3
"""Audit a finalized v05-only AnimeV1 Windows or macOS player export."""

from __future__ import annotations

import argparse
import configparser
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
from dev.check_godot_mcp_export import check_pck  # noqa: E402


class ExportAuditError(RuntimeError):
    pass


MACH_O_MAGICS = {
    b"\xcf\xfa\xed\xfe",
    b"\xfe\xed\xfa\xcf",
    b"\xca\xfe\xba\xbe",
    b"\xca\xfe\xba\xbf",
}

NATIVE_LAYOUTS = {
    "windows-x86_64": {
        "architecture": "x86_64",
        "libraries": {
            "v05": Path("scgs_v05.dll"),
        },
        "foreign": (
            "libscgs_v04.dylib",
            "libscgs_v05.dylib",
            "libscgs_v04.so",
            "libscgs_v05.so",
        ),
    },
    "macos-arm64": {
        "architecture": "arm64",
        "libraries": {
            "v05": Path("Contents/Frameworks/libscgs_v05.dylib"),
        },
        "foreign": (
            "scgs_v04.dll",
            "scgs_v05.dll",
            "libscgs_v04.so",
            "libscgs_v05.so",
        ),
    },
}


LICENSE_MARKERS = {
    "ANIME_V1_CARD_FRAME_R1_GENERATION_RECORD.json": '"card-frame-r1-generation-record"',
    "ANIME_V1_PRESENTATION_V2_GENERATION_RECORD.json": '"chroma"',
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
    "ANIME_V1_SHARED_ASSET_MANIFEST.json": '"gate": "5C-6C"',
    "ANIME_V1_ASSET_MANIFEST.json": '"gate": "6A"',
    "ANIME_V1_PROVENANCE.md": "project-bound visual candidate",
    "ANIME_V1_SLICE_README.md": "Gate 6A：AnimeV1 原创动漫视觉样片",
    "ANIME_V1_CARD_BODY_ASSET_MANIFEST.json": '"gate": "6A-R1"',
    "ANIME_V1_CARD_BODY_PROVENANCE.md": "Gate 6A-R1 approval candidate",
    "ANIME_V1_CARD_BODY_README.md": "Gate 6A-R1：AnimeV1 一体化卡体",
    "ANIME_V1_PRODUCT_CARD_ART_ASSET_MANIFEST.json": '"gate": "5C-6C"',
    "ANIME_V1_PRODUCT_CARD_ART_PROVENANCE.md":
        "twenty-eight PNG files in this directory",
    "BUILD_INFO.txt": "godot=4.7.2.stable.mono",
}

EXACT_PACKAGED_SOURCE_FILES = {
    "ANIME_V1_CARD_FRAME_R1_GENERATION_RECORD.json":
        ROOT / "client/godot/assets/visual/anime_v1/card_frame_r1/GENERATION_RECORD.json",
    "ANIME_V1_PRESENTATION_V2_GENERATION_RECORD.json":
        ROOT / "client/godot/assets/visual/anime_v1/presentation_v2/GENERATION_RECORD.json",
    "ANIME_V1_SHARED_ASSET_MANIFEST.json":
        ROOT / "client/godot/assets/visual/ASSET_MANIFEST.json",
    "ANIME_V1_ASSET_MANIFEST.json":
        ROOT / "client/godot/assets/visual/anime_v1/slice/ASSET_MANIFEST.json",
    "ANIME_V1_PROVENANCE.md":
        ROOT / "client/godot/assets/visual/anime_v1/slice/PROVENANCE.md",
    "ANIME_V1_SLICE_README.md": ROOT / "docs/anime-v1-visual-slice.md",
    "ANIME_V1_CARD_BODY_ASSET_MANIFEST.json":
        ROOT / "client/godot/assets/visual/anime_v1/card_body/CARD_BODY_ASSET_MANIFEST.json",
    "ANIME_V1_CARD_BODY_PROVENANCE.md":
        ROOT / "client/godot/assets/visual/anime_v1/card_body/PROVENANCE.md",
    "ANIME_V1_CARD_BODY_README.md": ROOT / "docs/anime-v1-card-body-r1.md",
    "ANIME_V1_PRODUCT_CARD_ART_ASSET_MANIFEST.json":
        ROOT / "client/godot/assets/visual/anime_v1/cards/PRODUCT_CARD_ART_ASSET_MANIFEST.json",
    "ANIME_V1_PRODUCT_CARD_ART_PROVENANCE.md":
        ROOT / "client/godot/assets/visual/anime_v1/cards/PROVENANCE.md",
}

WINDOWS_ANIME_LAUNCHER = ROOT / "scripts/ci/PLAY_ANIME_STYLE_SLICE.cmd"
MACOS_ANIME_LAUNCHER = ROOT / "scripts/ci/PLAY_ANIME_STYLE_SLICE.command"
WINDOWS_CARD_BODY_LAUNCHER = ROOT / "scripts/ci/PLAY_ANIME_CARD_BODY_SLICE.cmd"
MACOS_CARD_BODY_LAUNCHER = ROOT / "scripts/ci/PLAY_ANIME_CARD_BODY_SLICE.command"


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


def _audit_product_card_export_policy() -> None:
    presets_path = ROOT / "client/godot/export_presets.cfg"
    parser = configparser.ConfigParser(interpolation=None)
    try:
        with presets_path.open("r", encoding="utf-8") as stream:
            parser.read_file(stream)
    except (FileNotFoundError, UnicodeDecodeError, configparser.Error) as error:
        raise ExportAuditError("cannot read Godot export presets") from error

    preset_sections = sorted(
        section
        for section in parser.sections()
        if re.fullmatch(r"preset\.\d+", section)
    )
    if preset_sections != ["preset.0", "preset.1"]:
        raise ExportAuditError(
            f"expected the two desktop export presets, found {preset_sections}"
        )
    for section in preset_sections:
        if parser.get(section, "export_filter", fallback="") != '"all_resources"':
            raise ExportAuditError(
                "desktop exports must include all imported AnimeV1 product-card resources"
            )
        excluded = parser.get(section, "exclude_filter", fallback="")
        if "assets/visual/anime_v1/cards" in excluded:
            raise ExportAuditError(
                "AnimeV1 product-card resources must not be excluded from desktop exports"
            )


def _audit_licenses(directory: Path, expected_commit: str | None = None) -> None:
    for filename, marker in LICENSE_MARKERS.items():
        path = directory / filename
        if not path.is_file():
            raise ExportAuditError(f"missing packaged license/notice: {path}")
        if marker not in path.read_text(encoding="utf-8"):
            raise ExportAuditError(f"packaged notice has unexpected content: {path}")
    for filename, source in EXACT_PACKAGED_SOURCE_FILES.items():
        packaged = directory / filename
        expected_payload = source.read_bytes()
        actual_payload = packaged.read_bytes()
        if actual_payload != expected_payload:
            expected_hash = hashlib.sha256(expected_payload).hexdigest()
            actual_hash = hashlib.sha256(actual_payload).hexdigest()
            raise ExportAuditError(
                f"packaged {filename} differs from the reviewed source; "
                f"expected_sha256={expected_hash}, actual_sha256={actual_hash}"
            )
    build_info = (directory / "BUILD_INFO.txt").read_text(encoding="utf-8")
    lines = build_info.splitlines()
    if not lines or lines[0] != "SomeCardGameShit Product Playable v1":
        raise ExportAuditError("packaged build info does not identify Product Playable v1")
    entries: dict[str, str] = {}
    for line in lines[1:]:
        if line.count("=") != 1:
            raise ExportAuditError("packaged build info contains a malformed field")
        key, value = line.split("=", 1)
        if not key or not value or key in entries:
            raise ExportAuditError("packaged build info contains an empty or duplicate field")
        entries[key] = value
    expected_keys = {"commit", "godot", "dotnet_sdk", "dotnet_runtime", "api", "schema", "visual"}
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
    if entries["api"] != "scgs_v05" or entries["schema"] != "2" or entries["visual"] != "AnimeV1":
        raise ExportAuditError("packaged build info does not identify v05/schema 2/AnimeV1")
    commit = entries["commit"]
    if commit != "local" and re.fullmatch(r"[0-9a-fA-F]{40}", commit) is None:
        raise ExportAuditError("packaged build info has an invalid commit")
    if expected_commit is not None and commit.lower() != expected_commit.lower():
        raise ExportAuditError(
            "packaged build info commit does not match the current GitHub checkout"
        )


def _audit_font_source() -> None:
    font_directory = ROOT / "client/godot/assets/fonts"
    checksum_lines = (font_directory / "SHA256SUMS").read_text(
        encoding="utf-8"
    ).splitlines()
    entries = [line.strip() for line in checksum_lines if line.strip()]
    if not entries:
        raise ExportAuditError("font SHA256SUMS is empty")
    seen: set[str] = set()
    for checksum_line in entries:
        try:
            expected, filename = checksum_line.split(maxsplit=1)
        except ValueError as error:
            raise ExportAuditError(
                f"invalid font checksum entry: {checksum_line!r}"
            ) from error
        if filename in seen:
            raise ExportAuditError(f"duplicate font checksum entry: {filename}")
        seen.add(filename)
        font = font_directory / filename
        if not font.is_file():
            raise ExportAuditError(f"font checksum references a missing file: {filename}")
        actual = hashlib.sha256(font.read_bytes()).hexdigest()
        if actual != expected.lower():
            raise ExportAuditError(
                f"font SHA-256 mismatch for {filename}: "
                f"expected {expected.lower()}, found {actual}"
            )


def _audit_anime_slice_launcher(export: Path, platform: str) -> None:
    if platform == "windows-x86_64":
        expected = WINDOWS_ANIME_LAUNCHER
    elif platform == "macos-arm64":
        expected = MACOS_ANIME_LAUNCHER
    else:
        raise ExportAuditError(f"unsupported AnimeV1 launcher platform: {platform}")

    packaged = export.parent / expected.name
    if not packaged.is_file():
        raise ExportAuditError(
            f"missing packaged AnimeV1 player launcher: {packaged}"
        )
    expected_hash = hashlib.sha256(expected.read_bytes()).hexdigest()
    packaged_hash = hashlib.sha256(packaged.read_bytes()).hexdigest()
    if packaged_hash != expected_hash:
        raise ExportAuditError(
            "packaged AnimeV1 launcher differs from the reviewed source launcher"
        )
    if platform == "macos-arm64" and packaged.stat().st_mode & 0o111 == 0:
        raise ExportAuditError(
            f"packaged macOS AnimeV1 launcher is not executable: {packaged}"
        )


def _audit_anime_card_body_launcher(export: Path, platform: str) -> None:
    if platform == "windows-x86_64":
        expected = WINDOWS_CARD_BODY_LAUNCHER
    elif platform == "macos-arm64":
        expected = MACOS_CARD_BODY_LAUNCHER
    else:
        raise ExportAuditError(f"unsupported AnimeV1 card-body launcher platform: {platform}")

    packaged = export.parent / expected.name
    if not packaged.is_file():
        raise ExportAuditError(
            f"missing packaged AnimeV1 card-body launcher: {packaged}"
        )
    expected_hash = hashlib.sha256(expected.read_bytes()).hexdigest()
    packaged_hash = hashlib.sha256(packaged.read_bytes()).hexdigest()
    if packaged_hash != expected_hash:
        raise ExportAuditError(
            "packaged AnimeV1 card-body launcher differs from the reviewed source launcher"
        )
    if platform == "macos-arm64" and packaged.stat().st_mode & 0o111 == 0:
        raise ExportAuditError(
            f"packaged macOS AnimeV1 card-body launcher is not executable: {packaged}"
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


def _audit_native_layout(root: Path, platform: str) -> None:
    """Require exactly one product v05 library; reject every retired/fixture API."""

    layout = NATIVE_LAYOUTS[platform]
    architecture = str(layout["architecture"])
    libraries = layout["libraries"]
    if not isinstance(libraries, dict):
        raise ExportAuditError("invalid native library audit configuration")

    for api_version in ("v05",):
        relative = libraries[api_version]
        if not isinstance(relative, Path):
            raise ExportAuditError("invalid native library path configuration")
        native = root / relative
        audit(native, architecture, api_version)
        matches = sorted(root.rglob(relative.name), key=lambda path: str(path))
        if matches != [native]:
            raise ExportAuditError(
                f"unexpected {platform} {api_version} native layout: {matches}"
            )

    retired = [path for path in root.rglob("*") if path.is_file() and (
        re.fullmatch(r"(?:lib)?scgs_v04(?:_fixture)?(?:\.[0-9]+)*\.(?:dll|dylib|so)(?:\.[0-9]+)*", path.name, re.IGNORECASE)
        or re.fullmatch(r"PLAY_.*_SLICE\.(?:cmd|command)", path.name, re.IGNORECASE)
    )]
    if retired:
        raise ExportAuditError(f"retired native library or preview launcher in player export: {retired}")
    foreign_names = layout["foreign"]
    if not isinstance(foreign_names, tuple):
        raise ExportAuditError("invalid foreign library audit configuration")
    foreign = sorted(
        (
            path
            for filename in foreign_names
            for path in root.rglob(filename)
        ),
        key=lambda path: str(path),
    )
    if foreign:
        raise ExportAuditError(
            f"foreign native library leaked into {platform} export: {foreign}"
        )


def _audit_product_pck(pack: Path) -> None:
    def check_entry(name: str) -> None:
        # Native libraries belong only in the audited external platform layout.
        # In particular a previously staged editor v04 DLL cannot hide in PCK.
        if name.lower().startswith("native/") or re.fullmatch(
            r"(?:lib)?scgs_v0[45](?:_fixture)?(?:\.[0-9]+)*\.(?:dll|dylib|so)(?:\.[0-9]+)*",
            Path(name).name, re.IGNORECASE,
        ):
            raise ExportAuditError(f"native editor/fixture payload in product PCK: {name}")
    check_pck(pack, check_entry=check_entry)


def _audit_windows(export: Path, expected_commit: str | None = None) -> None:
    if not export.is_file() or export.suffix.lower() != ".exe":
        raise ExportAuditError(f"missing Windows executable: {export}")
    if _pe_architecture(export) != "x86_64":
        raise ExportAuditError("Windows executable is not x86-64")
    pck = export.with_suffix(".pck")
    if not pck.is_file():
        raise ExportAuditError(f"missing Windows project pack: {pck}")
    _audit_product_pck(pck)
    managed_data = _require_managed_data(export.parent)
    _audit_dotnet_runtime(managed_data)

    _audit_native_layout(export.parent, "windows-x86_64")
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
    _audit_product_pck(pcks[0])
    managed_data = _require_managed_data(resources)
    _audit_dotnet_runtime(managed_data)

    _audit_native_layout(export, "macos-arm64")
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
        _audit_product_card_export_policy()
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
