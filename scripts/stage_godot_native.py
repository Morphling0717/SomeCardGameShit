#!/usr/bin/env python3
"""Validate and stage one scgs_v04 library for a Godot desktop target."""

from __future__ import annotations

import argparse
import os
import shutil
import sys
import tempfile
from pathlib import Path

from audit_native_artifact import AuditError, audit


TARGETS = {
    "windows-x86_64": ("x86_64", "scgs_v04.dll"),
    "macos-arm64": ("arm64", "libscgs_v04.dylib"),
}


def stage(library: Path, destination_root: Path, target: str) -> Path:
    architecture, output_name = TARGETS[target]
    source = library.resolve(strict=True)
    audit(source, architecture)

    destination_directory = destination_root.resolve() / target
    destination_directory.mkdir(parents=True, exist_ok=True)
    destination = destination_directory / output_name

    # Copy through a sibling temporary file so readers never observe a partial
    # native library. copy2 follows macOS install symlinks by default.
    descriptor, temporary_name = tempfile.mkstemp(
        prefix=f".{output_name}.", suffix=".tmp", dir=destination_directory
    )
    os.close(descriptor)
    temporary = Path(temporary_name)
    try:
        shutil.copy2(source, temporary)
        os.replace(temporary, destination)
    finally:
        temporary.unlink(missing_ok=True)

    return destination


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--library", required=True, type=Path)
    parser.add_argument("--destination-root", required=True, type=Path)
    parser.add_argument("--target", required=True, choices=tuple(TARGETS))
    args = parser.parse_args()

    try:
        destination = stage(args.library, args.destination_root, args.target)
    except (AuditError, OSError, ValueError) as error:
        print(f"native staging failed: {error}", file=sys.stderr)
        return 1

    print(f"staged {args.target} native library: {destination}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
