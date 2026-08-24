#!/usr/bin/env python3
"""Strictly audit the Gate 4B generated-visual asset inventory."""

# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import argparse
import hashlib
import json
import re
import struct
import sys
from datetime import date
from pathlib import Path, PurePosixPath
from typing import Any


MANIFEST_RELATIVE_PATH = Path("client/godot/assets/visual/ASSET_MANIFEST.json")
VISUAL_ROOT = Path("client/godot/assets/visual")
MEDIA_SUFFIXES = {".png", ".webp", ".svg"}
CARD_ART_ROOT = VISUAL_ROOT / "cards/art"
EXPECTED_CARD_ART_NAMES = {
    *(f"{identifier}.png" for identifier in (
        1001, 1002, 1003, 1004, 1005, 1006, 1007, 1009,
        1010, 1011, 1012, 1013, 1014, 3001, 3002,
        2001, 2002, 2003, 2004, 2005, 2006, 2007, 2008,
        2009, 2010, 2011, 2012, 3011, 3012,
    ))
}
REQUIRED_SINGLETONS = {
    (VISUAL_ROOT / "shared/card_back.png").as_posix(),
    (VISUAL_ROOT / "menu/gate4b-menu-background.png").as_posix(),
    (VISUAL_ROOT / "portraits/midrange_commander.png").as_posix(),
    (VISUAL_ROOT / "portraits/advance_technarch.png").as_posix(),
}
EXPECTED_TOP_LEVEL_FIELDS = {"schema_version", "gate", "assets"}
EXPECTED_ASSET_FIELDS = {
    "path",
    "sha256",
    "purpose",
    "generation_method",
    "date",
    "prompt_summary",
}
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")


class VisualAssetAuditError(ValueError):
    """Raised when the committed visual inventory is incomplete or unsafe."""


def _load_json(path: Path) -> Any:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as error:
        raise VisualAssetAuditError(f"missing asset manifest: {path}") from error
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise VisualAssetAuditError(f"invalid UTF-8 JSON asset manifest: {error}") from error


def _validate_relative_path(raw: object) -> str:
    if not isinstance(raw, str) or not raw:
        raise VisualAssetAuditError("asset path must be a non-empty string")
    if "\\" in raw:
        raise VisualAssetAuditError(f"asset path must use '/' separators: {raw!r}")
    path = PurePosixPath(raw)
    if path.is_absolute() or ".." in path.parts or "." in path.parts:
        raise VisualAssetAuditError(f"asset path must be normalized and relative: {raw!r}")
    expected_prefix = PurePosixPath(VISUAL_ROOT.as_posix()).parts
    if path.parts[: len(expected_prefix)] != expected_prefix:
        raise VisualAssetAuditError(f"asset is outside {VISUAL_ROOT.as_posix()}: {raw!r}")
    if path.suffix.lower() not in MEDIA_SUFFIXES:
        raise VisualAssetAuditError(f"unsupported generated media type: {raw!r}")
    return raw


def _png_dimensions(path: Path) -> tuple[int, int]:
    header = path.read_bytes()[:24]
    if len(header) != 24 or header[:8] != b"\x89PNG\r\n\x1a\n" or header[12:16] != b"IHDR":
        raise VisualAssetAuditError(f"invalid PNG header: {path}")
    return struct.unpack(">II", header[16:24])


def audit(
    repo_root: Path,
    manifest_path: Path | None = None,
    *,
    enforce_product_set: bool = True,
) -> dict[str, Any]:
    repo_root = repo_root.resolve()
    manifest_path = (manifest_path or repo_root / MANIFEST_RELATIVE_PATH).resolve()
    try:
        manifest_path.relative_to(repo_root)
    except ValueError as error:
        raise VisualAssetAuditError("asset manifest must be inside the repository") from error

    manifest = _load_json(manifest_path)
    if not isinstance(manifest, dict) or set(manifest) != EXPECTED_TOP_LEVEL_FIELDS:
        raise VisualAssetAuditError(
            f"manifest fields must be exactly {sorted(EXPECTED_TOP_LEVEL_FIELDS)}"
        )
    if manifest["schema_version"] != 1 or manifest["gate"] != "4B":
        raise VisualAssetAuditError("asset manifest must identify Gate 4B schema 1")
    entries = manifest["assets"]
    if not isinstance(entries, list) or not entries:
        raise VisualAssetAuditError("asset manifest must contain at least one asset")

    registered: set[str] = set()
    hashes: set[str] = set()
    for index, entry in enumerate(entries):
        if not isinstance(entry, dict) or set(entry) != EXPECTED_ASSET_FIELDS:
            raise VisualAssetAuditError(
                f"asset[{index}] fields must be exactly {sorted(EXPECTED_ASSET_FIELDS)}"
            )
        relative = _validate_relative_path(entry["path"])
        if relative in registered:
            raise VisualAssetAuditError(f"duplicate asset path: {relative}")
        registered.add(relative)

        expected_hash = entry["sha256"]
        if not isinstance(expected_hash, str) or not SHA256_RE.fullmatch(expected_hash):
            raise VisualAssetAuditError(f"asset[{index}] has an invalid SHA-256")
        if expected_hash in hashes:
            raise VisualAssetAuditError(
                f"assets must be visually unique at the byte level; duplicate SHA-256: {expected_hash}"
            )
        hashes.add(expected_hash)

        asset_path = (repo_root / Path(relative)).resolve()
        try:
            asset_path.relative_to((repo_root / VISUAL_ROOT).resolve())
        except ValueError as error:
            raise VisualAssetAuditError(f"asset escapes the visual root: {relative}") from error
        if not asset_path.is_file():
            raise VisualAssetAuditError(f"registered asset does not exist: {relative}")
        actual_hash = hashlib.sha256(asset_path.read_bytes()).hexdigest()
        if actual_hash != expected_hash:
            raise VisualAssetAuditError(
                f"SHA-256 mismatch for {relative}: expected {expected_hash}, got {actual_hash}"
            )

        for field in ("purpose", "generation_method", "prompt_summary"):
            value = entry[field]
            if not isinstance(value, str) or not value.strip():
                raise VisualAssetAuditError(f"asset[{index}].{field} must be non-empty")
        generated_on = entry["date"]
        if not isinstance(generated_on, str):
            raise VisualAssetAuditError(f"asset[{index}].date must be ISO YYYY-MM-DD")
        try:
            date.fromisoformat(generated_on)
        except ValueError as error:
            raise VisualAssetAuditError(
                f"asset[{index}].date must be ISO YYYY-MM-DD"
            ) from error

    actual = {
        path.relative_to(repo_root).as_posix()
        for path in (repo_root / VISUAL_ROOT).rglob("*")
        if path.is_file() and path.suffix.lower() in MEDIA_SUFFIXES
    }
    missing = sorted(actual - registered)
    stale = sorted(registered - actual)
    if missing or stale:
        raise VisualAssetAuditError(
            f"visual inventory mismatch; unregistered={missing}, missing={stale}"
        )

    card_art = {
        path.relative_to(repo_root).as_posix()
        for path in (repo_root / CARD_ART_ROOT).glob("*.png")
        if path.is_file()
    }
    card_art_names = {Path(relative).name for relative in card_art}
    if enforce_product_set and card_art_names != EXPECTED_CARD_ART_NAMES:
        raise VisualAssetAuditError(
            "Gate 4B card illustration set differs; "
            f"missing={sorted(EXPECTED_CARD_ART_NAMES - card_art_names)}, "
            f"unexpected={sorted(card_art_names - EXPECTED_CARD_ART_NAMES)}"
        )
    absent_singletons = sorted(REQUIRED_SINGLETONS - registered) if enforce_product_set else []
    if absent_singletons:
        raise VisualAssetAuditError(
            "Gate 4B is missing a required generated card back, menu background, "
            f"or leader portrait: {absent_singletons}"
        )
    if enforce_product_set:
        for relative in sorted(card_art | {(VISUAL_ROOT / "shared/card_back.png").as_posix()}):
            width, height = _png_dimensions(repo_root / relative)
            if width * 3 != height * 2:
                raise VisualAssetAuditError(
                    f"card illustration/back must use an exact 2:3 canvas: {relative} is {width}x{height}"
                )
        menu_relative = (VISUAL_ROOT / "menu/gate4b-menu-background.png").as_posix()
        menu_width, menu_height = _png_dimensions(repo_root / menu_relative)
        if abs(menu_width / menu_height - 16 / 9) > 0.01:
            raise VisualAssetAuditError(
                f"menu background must use a 16:9 canvas: {menu_width}x{menu_height}"
            )
        for portrait_name in ("midrange_commander.png", "advance_technarch.png"):
            portrait_relative = (VISUAL_ROOT / "portraits" / portrait_name).as_posix()
            portrait_width, portrait_height = _png_dimensions(repo_root / portrait_relative)
            if portrait_width != portrait_height:
                raise VisualAssetAuditError(
                    "leader portrait must use a square canvas: "
                    f"{portrait_relative} is {portrait_width}x{portrait_height}"
                )

    return {
        "asset_count": len(entries),
        "manifest_sha256": hashlib.sha256(manifest_path.read_bytes()).hexdigest(),
        "paths": sorted(registered),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--manifest", type=Path)
    args = parser.parse_args()
    try:
        result = audit(args.repo_root, args.manifest)
    except VisualAssetAuditError as error:
        print(f"visual asset audit failed: {error}", file=sys.stderr)
        return 1
    print(
        f"audited {result['asset_count']} Gate 4B visual assets; "
        f"manifest_sha256={result['manifest_sha256']}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
