#!/usr/bin/env python3
"""Validate and stage the frozen v04 and product v05 Godot libraries."""

from __future__ import annotations

import argparse
import os
import shutil
import sys
import tempfile
from pathlib import Path

from audit_native_artifact import AuditError, audit


TARGETS = {
    "windows-x86_64": (
        "x86_64",
        {"v04": "scgs_v04.dll", "v05": "scgs_v05.dll"},
    ),
    "macos-arm64": (
        "arm64",
        {"v04": "libscgs_v04.dylib", "v05": "libscgs_v05.dylib"},
    ),
}


def _copy_atomic(source: Path, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)

    # Copy through a sibling temporary file so readers never observe a partial
    # native library. copy2 follows macOS install symlinks by default.
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


def stage(
    library: Path,
    destination_root: Path,
    target: str,
    api_version: str = "v04",
) -> Path:
    """Stage one audited API library; the default preserves the old API."""

    architecture, output_names = TARGETS[target]
    if api_version not in output_names:
        raise ValueError(f"unsupported native API version: {api_version}")
    output_name = output_names[api_version]
    source = library.resolve(strict=True)
    audit(source, architecture, api_version)

    destination_directory = destination_root.resolve() / target
    destination = destination_directory / output_name
    _copy_atomic(source, destination)

    return destination


def stage_pair(
    v04_library: Path,
    v05_library: Path,
    destination_root: Path,
    target: str,
) -> tuple[Path, Path]:
    """Audit both APIs before replacing either editor library."""

    architecture, output_names = TARGETS[target]
    sources = {
        "v04": v04_library.resolve(strict=True),
        "v05": v05_library.resolve(strict=True),
    }
    for api_version, source in sources.items():
        audit(source, architecture, api_version)

    destination_directory = destination_root.resolve() / target
    destinations = {
        api_version: destination_directory / output_names[api_version]
        for api_version in ("v04", "v05")
    }
    for api_version in ("v04", "v05"):
        _copy_atomic(sources[api_version], destinations[api_version])
    return destinations["v04"], destinations["v05"]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--library",
        type=Path,
        help="legacy alias for --v04-library",
    )
    parser.add_argument("--v04-library", type=Path)
    parser.add_argument("--v05-library", type=Path)
    parser.add_argument("--destination-root", required=True, type=Path)
    parser.add_argument("--target", required=True, choices=tuple(TARGETS))
    args = parser.parse_args()

    if args.library is not None and args.v04_library is not None:
        parser.error("use only one of --library and --v04-library")
    v04_library = args.v04_library or args.library
    if v04_library is None and args.v05_library is None:
        parser.error("--v05-library is required for product builds")

    try:
        if v04_library is None:
            destination = stage(args.v05_library, args.destination_root, args.target, "v05")
            print(f"staged {args.target} product v05 library: {destination}")
            return 0
        if args.v05_library is None:
            # Retain the old one-library invocation for legacy editor work.
            destination = stage(
                v04_library,
                args.destination_root,
                args.target,
                "v04",
            )
            print(f"staged {args.target} frozen v04 library: {destination}")
            return 0

        v04_destination, v05_destination = stage_pair(
            v04_library,
            args.v05_library,
            args.destination_root,
            args.target,
        )
    except (AuditError, OSError, ValueError) as error:
        print(f"native staging failed: {error}", file=sys.stderr)
        return 1

    print(
        f"staged {args.target} native libraries: "
        f"v04={v04_destination}, v05={v05_destination}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
