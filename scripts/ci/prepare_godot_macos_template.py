#!/usr/bin/env python3
"""Derive an arm64-only Godot macOS release template from the official archive."""

from __future__ import annotations

import argparse
import hashlib
import os
import shutil
import stat
import struct
import subprocess
import sys
import tempfile
import zipfile
from collections.abc import Callable
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))

from audit_native_artifact import AuditError, _mach_architectures  # noqa: E402


OFFICIAL_MACOS_ZIP_SHA512 = (
    "17a3076d3d1b0a8172d781099c60dc375cbba6588148a4fe996906244957e6e"
    "f99c96dcd11a3fe552b55c0ea04bc784e3d16270f8d017deba08a2abe194b232b"
)
UNIVERSAL_ENTRY = (
    "macos_template.app/Contents/MacOS/godot_macos_release.universal"
)
ARM64_ENTRY = "macos_template.app/Contents/MacOS/godot_macos_release.arm64"
MAX_TEMPLATE_BINARY_BYTES = 256 * 1024 * 1024


class TemplatePreparationError(RuntimeError):
    pass


ThinRunner = Callable[[Path, Path], None]


def _sha512(path: Path) -> str:
    digest = hashlib.sha512()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _architectures(path: Path) -> set[str]:
    with path.open("rb") as stream:
        header = stream.read(4096)
    try:
        return _mach_architectures(header)
    except (AuditError, struct.error, ValueError) as error:
        raise TemplatePreparationError(f"invalid Mach-O file: {path}") from error


def _run_lipo(source: Path, destination: Path) -> None:
    subprocess.run(
        [
            "/usr/bin/lipo",
            str(source),
            "-thin",
            "arm64",
            "-output",
            str(destination),
        ],
        check=True,
    )


def _single_entry(archive: zipfile.ZipFile, name: str) -> zipfile.ZipInfo:
    matches = [entry for entry in archive.infolist() if entry.filename == name]
    if len(matches) != 1:
        raise TemplatePreparationError(
            f"expected exactly one {name!r} entry, found {len(matches)}"
        )
    return matches[0]


def _extract_member(
    archive: zipfile.ZipFile, entry: zipfile.ZipInfo, destination: Path
) -> None:
    if entry.flag_bits & 0x1:
        raise TemplatePreparationError(f"encrypted template entry: {entry.filename}")
    if entry.file_size <= 0 or entry.file_size > MAX_TEMPLATE_BINARY_BYTES:
        raise TemplatePreparationError(
            f"unexpected template entry size for {entry.filename}: {entry.file_size}"
        )
    with archive.open(entry, "r") as source, destination.open("wb") as output:
        shutil.copyfileobj(source, output, length=1024 * 1024)


def _verify_derived_archive(archive_path: Path, scratch: Path) -> None:
    with zipfile.ZipFile(archive_path, "r") as archive:
        entry = _single_entry(archive, ARM64_ENTRY)
        mode = (entry.external_attr >> 16) & 0xFFFF
        if not mode & 0o111:
            raise TemplatePreparationError("derived arm64 template is not executable")
        extracted = scratch / "verify-arm64"
        _extract_member(archive, entry, extracted)
    if _architectures(extracted) != {"arm64"}:
        raise TemplatePreparationError("derived template is not arm64-only")


def prepare_template(
    source: Path,
    output: Path,
    *,
    expected_source_sha512: str = OFFICIAL_MACOS_ZIP_SHA512,
    thin_runner: ThinRunner = _run_lipo,
) -> str:
    source = source.resolve(strict=True)
    output = output.resolve(strict=False)
    if source == output:
        raise TemplatePreparationError("source and output template paths must differ")

    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(
        prefix=".scgs-arm64-template-", dir=output.parent
    ) as temporary_name:
        temporary = Path(temporary_name)
        trusted_source = temporary / "official-macos.zip"
        universal = temporary / "godot_macos_release.universal"
        arm64 = temporary / "godot_macos_release.arm64"
        derived = temporary / "godot_macos_release.arm64.zip"

        shutil.copy2(source, trusted_source)
        actual_hash = _sha512(trusted_source)
        if actual_hash != expected_source_sha512.lower():
            raise TemplatePreparationError(
                f"official macos.zip SHA-512 mismatch: expected "
                f"{expected_source_sha512.lower()}, found {actual_hash}"
            )

        with zipfile.ZipFile(trusted_source, "r") as archive:
            entry = _single_entry(archive, UNIVERSAL_ENTRY)
            if any(item.filename == ARM64_ENTRY for item in archive.infolist()):
                raise TemplatePreparationError(
                    "official archive unexpectedly already contains the derived entry"
                )
            _extract_member(archive, entry, universal)
            source_timestamp = entry.date_time
            source_compression = entry.compress_type

        if _architectures(universal) != {"arm64", "x86_64"}:
            raise TemplatePreparationError(
                "official release template is not an x86_64+arm64 universal binary"
            )

        thin_runner(universal, arm64)
        if not arm64.is_file() or _architectures(arm64) != {"arm64"}:
            raise TemplatePreparationError(
                "lipo did not produce an arm64-only template"
            )
        arm64.chmod(0o755)

        shutil.copy2(trusted_source, derived)
        derived_entry = zipfile.ZipInfo(ARM64_ENTRY, date_time=source_timestamp)
        derived_entry.create_system = 3
        derived_entry.compress_type = source_compression
        derived_entry.external_attr = (stat.S_IFREG | 0o755) << 16
        with zipfile.ZipFile(derived, "a", allowZip64=True) as archive:
            with archive.open(derived_entry, "w") as destination:
                with arm64.open("rb") as input_stream:
                    shutil.copyfileobj(input_stream, destination, length=1024 * 1024)

        _verify_derived_archive(derived, temporary)
        os.replace(derived, output)

    return _sha512(output)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()

    try:
        digest = prepare_template(args.source, args.output)
    except (
        AuditError,
        OSError,
        subprocess.CalledProcessError,
        struct.error,
        TemplatePreparationError,
        ValueError,
        zipfile.BadZipFile,
    ) as error:
        print(f"Godot macOS template preparation failed: {error}", file=sys.stderr)
        return 1

    print(f"prepared arm64 Godot macOS template: {args.output} sha512={digest}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
