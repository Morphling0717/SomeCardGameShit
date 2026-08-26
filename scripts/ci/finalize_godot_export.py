#!/usr/bin/env python3
"""Place native/runtime notices in a completed Godot desktop export."""

from __future__ import annotations

import argparse
import os
import shutil
import sys
import tempfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))

from audit_native_artifact import audit  # noqa: E402


LICENSES = {
    ROOT / "LICENSE": "GPL-3.0-or-later.txt",
    ROOT / "THIRD_PARTY_NOTICES.md": "THIRD_PARTY_NOTICES.md",
    ROOT / "client/godot/licenses/Godot-LICENSE.txt": "Godot-LICENSE.txt",
    ROOT / "client/godot/licenses/Godot-COPYRIGHT.txt":
        "Godot-COPYRIGHT.txt",
    ROOT / "client/godot/licenses/Dotnet-LICENSE.txt":
        "Dotnet-Runtime-LICENSE.txt",
    ROOT / "client/godot/licenses/Dotnet-THIRD-PARTY-NOTICES.txt":
        "Dotnet-Runtime-THIRD-PARTY-NOTICES.txt",
    ROOT / "scripts/ci/licenses/nlohmann-json-LICENSE.MIT":
        "nlohmann-json-LICENSE.MIT",
    ROOT / "client/godot/assets/fonts/OFL.txt": "NotoSansCJKsc-OFL.txt",
    ROOT / "client/godot/assets/fonts/NOTICE.md": "NotoSansCJKsc-NOTICE.md",
    ROOT / "client/godot/ASSET_NOTICES.md": "ASSET_NOTICES.md",
    ROOT / "client/godot/assets/visual/ASSET_MANIFEST.json":
        "ASSET_MANIFEST.json",
    ROOT / "client/godot/assets/visual/arena/R3_ASSET_MANIFEST.json":
        "R3_ASSET_MANIFEST.json",
    ROOT / "client/godot/assets/visual/anime_v1/slice/ASSET_MANIFEST.json":
        "ANIME_V1_ASSET_MANIFEST.json",
    ROOT / "client/godot/assets/visual/anime_v1/slice/PROVENANCE.md":
        "ANIME_V1_PROVENANCE.md",
    ROOT / "docs/anime-v1-visual-slice.md":
        "ANIME_V1_SLICE_README.md",
    ROOT / "client/godot/assets/visual/anime_v1/card_body/CARD_BODY_ASSET_MANIFEST.json":
        "ANIME_V1_CARD_BODY_ASSET_MANIFEST.json",
    ROOT / "client/godot/assets/visual/anime_v1/card_body/PROVENANCE.md":
        "ANIME_V1_CARD_BODY_PROVENANCE.md",
    ROOT / "docs/anime-v1-card-body-r1.md":
        "ANIME_V1_CARD_BODY_README.md",
}

WINDOWS_R3_LAUNCHER = ROOT / "scripts/ci/PLAY_R3_VISUAL_SLICE.cmd"
WINDOWS_ANIME_LAUNCHER = ROOT / "scripts/ci/PLAY_ANIME_STYLE_SLICE.cmd"
MACOS_ANIME_LAUNCHER = ROOT / "scripts/ci/PLAY_ANIME_STYLE_SLICE.command"
WINDOWS_CARD_BODY_LAUNCHER = ROOT / "scripts/ci/PLAY_ANIME_CARD_BODY_SLICE.cmd"
MACOS_CARD_BODY_LAUNCHER = ROOT / "scripts/ci/PLAY_ANIME_CARD_BODY_SLICE.command"


def _copy_atomic(source: Path, destination: Path) -> None:
    source = source.resolve(strict=True)
    destination.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(
        prefix=f".{destination.name}.", suffix=".tmp", dir=destination.parent
    )
    os.close(descriptor)
    temporary = Path(temporary_name)
    try:
        shutil.copy2(source, temporary)
        os.replace(temporary, destination)
    finally:
        temporary.unlink(missing_ok=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--platform", required=True, choices=("windows-x86_64", "macos-arm64")
    )
    parser.add_argument("--export", required=True, type=Path)
    parser.add_argument("--native-library", required=True, type=Path)
    args = parser.parse_args()

    export = args.export.resolve(strict=True)
    native = args.native_library.resolve(strict=True)
    if args.platform == "windows-x86_64":
        if not export.is_file() or export.suffix.lower() != ".exe":
            parser.error("the Windows export must be an existing .exe")
        audit(native, "x86_64")
        native_destination = export.parent / "scgs_v04.dll"
        license_directory = export.parent / "licenses"
    else:
        if not export.is_dir() or export.suffix != ".app":
            parser.error("the macOS export must be an existing .app bundle")
        audit(native, "arm64")
        native_destination = export / "Contents/Frameworks/libscgs_v04.dylib"
        license_directory = export / "Contents/Resources/licenses"

    _copy_atomic(native, native_destination)
    if args.platform == "windows-x86_64":
        _copy_atomic(
            WINDOWS_R3_LAUNCHER,
            export.parent / "PLAY_R3_VISUAL_SLICE.cmd",
        )
        _copy_atomic(
            WINDOWS_ANIME_LAUNCHER,
            export.parent / WINDOWS_ANIME_LAUNCHER.name,
        )
        _copy_atomic(
            WINDOWS_CARD_BODY_LAUNCHER,
            export.parent / WINDOWS_CARD_BODY_LAUNCHER.name,
        )
    else:
        macos_launcher = export.parent / MACOS_ANIME_LAUNCHER.name
        _copy_atomic(MACOS_ANIME_LAUNCHER, macos_launcher)
        macos_launcher.chmod(0o755)
        card_body_launcher = export.parent / MACOS_CARD_BODY_LAUNCHER.name
        _copy_atomic(MACOS_CARD_BODY_LAUNCHER, card_body_launcher)
        card_body_launcher.chmod(0o755)
    for source, output_name in LICENSES.items():
        _copy_atomic(source, license_directory / output_name)

    build_info = license_directory / "BUILD_INFO.txt"
    build_info.write_text(
        "SomeCardGameShit Gate 4B-R2\n"
        f"commit={os.environ.get('GITHUB_SHA', 'local')}\n"
        "godot=4.7.2.stable.mono\n"
        "dotnet_sdk=10.0.400\n"
        "dotnet_runtime=8.0.30\n",
        encoding="utf-8",
        newline="\n",
    )
    print(f"finalized {args.platform} export: {export}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
