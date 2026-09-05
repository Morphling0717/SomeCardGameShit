#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Read-only isolation audit for local Godot MCP development tooling.

Examples:
  python scripts/dev/check_godot_mcp_export.py --check-presets-only
  python scripts/dev/check_godot_mcp_export.py --export artifacts/godot/windows-x86_64

Accepts an unpacked export directory (.app included), a standalone PCK, or an
EXE with a sibling PCK. It never loads the project or executes exported code.
The enabled Toolkit export hook must remove its autoload during export; file
exclusion alone is not sufficient. This audit checks the resulting settings.

PCK v2/v3/v4 and ECFG layouts follow Godot 4.7.2's file_access_pack.cpp and
project_settings.cpp. Encrypted, sparse and delta packs are deliberately refused
instead of reporting an uninspectable package as clean.
"""

from __future__ import annotations

import argparse
import configparser
import hashlib
import io
import json
import re
import struct
import sys
from pathlib import Path, PurePosixPath
from typing import BinaryIO
from collections.abc import Callable


ROOT = Path(__file__).resolve().parents[2]
REQUIRED_EXCLUDES = (
    "addons/godot_mcp_toolkit/*",
    "__mcp_probe/*",
    ".mcp.json",
)
FORBIDDEN_SEGMENTS = {"godot_mcp_toolkit", "__mcp_probe", ".mcp.json"}
MAX_ENTRIES = 200_000
MAX_PATH_BYTES = 16_384
MAX_SETTINGS_BYTES = 16 * 1024 * 1024


class IsolationError(ValueError):
    pass


def _read(stream: BinaryIO, size: int) -> bytes:
    value = stream.read(size)
    if len(value) != size:
        raise IsolationError("truncated export data")
    return value


def _u32(stream: BinaryIO) -> int:
    return struct.unpack("<I", _read(stream, 4))[0]


def _u64(stream: BinaryIO) -> int:
    return struct.unpack("<Q", _read(stream, 8))[0]


def _text(raw: bytes) -> str:
    try:
        return raw.decode("utf-8", errors="strict")
    except UnicodeDecodeError as error:
        raise IsolationError("export contains invalid UTF-8") from error


def _resource_path(value: str) -> str:
    path = value.replace("\\", "/")
    if path.startswith("res://"):
        path = path[6:]
    if path.startswith("/") or ":" in path or ".." in path.split("/"):
        raise IsolationError(f"invalid package resource path: {value!r}")
    return str(PurePosixPath(path))


def _check_path(path: str) -> None:
    if FORBIDDEN_SEGMENTS.intersection(path.lower().split("/")):
        raise IsolationError(f"development tooling was packaged: {path}")


def check_presets(path: Path) -> list[str]:
    parser = configparser.ConfigParser(interpolation=None)
    with path.open(encoding="utf-8") as source:
        parser.read_file(source)
    sections = [s for s in parser.sections() if re.fullmatch(r"preset\.\d+", s)]
    if not sections:
        raise IsolationError("no Godot export presets found")
    names = []
    for section in sections:
        excludes = {
            entry.strip().removeprefix("res://")
            for entry in parser.get(section, "exclude_filter", fallback="").strip('"').split(",")
        }
        missing = set(REQUIRED_EXCLUDES) - excludes
        if missing:
            raise IsolationError(f"{section} lacks exact MCP exclusions: {sorted(missing)}")
        names.append(parser.get(section, "name", fallback=section).strip('"'))
    return names


def check_project_binary(data: bytes) -> int:
    if len(data) > MAX_SETTINGS_BYTES:
        raise IsolationError("project.binary exceeds the audit size limit")
    stream = io.BytesIO(data)
    if _read(stream, 4) != b"ECFG":
        raise IsolationError("project.binary has no ECFG header")
    count = _u32(stream)
    if count > MAX_ENTRIES:
        raise IsolationError("too many project settings")
    keys: set[str] = set()
    for _ in range(count):
        length = _u32(stream)
        if length > MAX_PATH_BYTES:
            raise IsolationError("project setting key is too long")
        key = _text(_read(stream, length))
        if key in keys:
            raise IsolationError(f"duplicate project setting: {key}")
        keys.add(key)
        length = _u32(stream)
        if length < 4 or length > MAX_SETTINGS_BYTES:
            raise IsolationError(f"invalid encoded setting size: {key}")
        value = _read(stream, length)
        # Godot Variant string values retain UTF-8 bytes. Checking all blobs also
        # catches PackedStringArray references such as editor_plugins/enabled.
        combined = key.encode("utf-8").lower() + value.lower()
        if any(marker in combined for marker in (
            b"autoload/mcpruntimeserver", b"godot_mcp_toolkit", b"mcp_toolkit/",
            b"__mcp_probe", b".mcp.json",
        )):
            raise IsolationError(f"MCP/probe reference remains in project.binary setting: {key}")
    if stream.read(1):
        raise IsolationError("unexpected trailing project.binary data")
    return count


def check_pck(path: Path, *, check_entry: Callable[[str], None] | None = None) -> dict[str, object]:
    with path.open("rb") as stream:
        file_size = path.stat().st_size
        if _read(stream, 4) != b"GDPC":
            raise IsolationError(f"not a standalone Godot PCK: {path}")
        version = _u32(stream)
        engine_version = [_u32(stream) for _ in range(3)]
        if version not in (2, 3, 4):
            raise IsolationError(f"unsupported PCK format {version}")
        flags = _u32(stream)
        if flags & ~2:
            raise IsolationError("encrypted, sparse or unknown PCK flags cannot be audited")
        file_base = _u64(stream)
        if version in (3, 4):
            directory_offset = _u64(stream)
            if directory_offset < 40 or directory_offset > file_size - 4:
                raise IsolationError("invalid PCK directory offset")
            stream.seek(directory_offset)
        else:
            _read(stream, 64)
        count = _u32(stream)
        if not 1 <= count <= MAX_ENTRIES:
            raise IsolationError("invalid PCK entry count")
        entries: dict[str, tuple[int, int, bytes]] = {}
        for _ in range(count):
            length = _u32(stream)
            if not 1 <= length <= MAX_PATH_BYTES:
                raise IsolationError("invalid PCK path length")
            # PCK paths are UTF-8 padded with trailing NULs to four bytes.
            raw_path = _read(stream, length).rstrip(b"\0")
            if b"\0" in raw_path:
                raise IsolationError("embedded NUL in PCK path")
            name = _resource_path(_text(raw_path))
            _check_path(name)
            if check_entry is not None:
                check_entry(name)
            offset, size = _u64(stream), _u64(stream)
            digest = _read(stream, 16)
            entry_flags = _u32(stream)
            if entry_flags:
                raise IsolationError(f"encrypted, removal or delta entry cannot be audited: {name}")
            absolute = file_base + offset
            if absolute > file_size or size > file_size - absolute:
                raise IsolationError(f"PCK entry is outside the package: {name}")
            if name in entries:
                raise IsolationError(f"duplicate PCK path: {name}")
            entries[name] = (absolute, size, digest)
        if "project.binary" not in entries:
            raise IsolationError("PCK has no project.binary; autoload isolation was not checked")
        offset, size, digest = entries["project.binary"]
        if size > MAX_SETTINGS_BYTES:
            raise IsolationError("project.binary exceeds the audit size limit")
        stream.seek(offset)
        settings = _read(stream, size)
        if hashlib.md5(settings).digest() != digest:
            raise IsolationError("project.binary checksum mismatch")
        setting_count = check_project_binary(settings)
        # Godot can export the small global class-name cache independently of
        # project.binary. An excluded addon's class path must not survive there.
        class_caches = 0
        for name, (offset, size, digest) in entries.items():
            if not name.endswith("global_script_class_cache.cfg"):
                continue
            if size > MAX_SETTINGS_BYTES:
                raise IsolationError("global script class cache exceeds the audit size limit")
            stream.seek(offset)
            cache = _read(stream, size)
            if hashlib.md5(cache).digest() != digest:
                raise IsolationError("global script class cache checksum mismatch")
            text = _text(cache).lower()
            if any(marker in text for marker in ("godot_mcp_toolkit", "mcptoolkitextension", "__mcp_probe")):
                raise IsolationError("development tooling reference in global script class cache")
            class_caches += 1
    return {
        "path": str(path.resolve()), "format": version,
        "engine_version": engine_version, "entries_checked": count,
        "project_settings_checked": setting_count,
        "script_class_caches_checked": class_caches,
        "project_binary_sha256": hashlib.sha256(settings).hexdigest(),
    }


def check_export(path: Path) -> list[dict[str, object]]:
    if path.is_file() and path.suffix.lower() == ".exe":
        path = path.with_suffix(".pck")
    if path.is_file():
        if path.suffix.lower() != ".pck":
            raise IsolationError("use an unpacked export directory, standalone .pck or EXE with sibling .pck")
        return [check_pck(path)]
    if not path.is_dir():
        raise IsolationError(f"export does not exist: {path}")
    packs = []
    for item in path.rglob("*"):
        relative = item.relative_to(path).as_posix()
        _check_path(relative)
        if item.is_file() and item.suffix.lower() == ".pck":
            packs.append(item)
    if not packs:
        raise IsolationError("export contains no PCK; embedded/encrypted archives are not supported")
    return [check_pck(pack) for pack in sorted(packs)]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--presets", type=Path, default=ROOT / "client/godot/export_presets.cfg")
    target = parser.add_mutually_exclusive_group(required=True)
    target.add_argument("--check-presets-only", action="store_true")
    target.add_argument("--export", type=Path)
    args = parser.parse_args()
    try:
        names = check_presets(args.presets)
        packs = check_export(args.export) if args.export is not None else []
        print(json.dumps({
            "schema": "scgs.godot_mcp_export_isolation.v1", "status": "passed",
            "scope": "export_and_presets" if packs else "presets_only",
            "presets": names, "packs": packs,
        }, ensure_ascii=False, indent=2))
        return 0
    except (IsolationError, OSError, configparser.Error, UnicodeError) as error:
        print(f"Godot MCP export isolation FAILED: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
