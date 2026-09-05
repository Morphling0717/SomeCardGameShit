#!/usr/bin/env python3
"""Strictly audit the AnimeV1 product inventory and reject retired product art."""

# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import argparse
import hashlib
import json
import re
import struct
import sys
import zlib
from datetime import date
from pathlib import Path, PurePosixPath
from typing import Any


MANIFEST_RELATIVE_PATH = Path("client/godot/assets/visual/ASSET_MANIFEST.json")
R3_CANDIDATE_MANIFEST_RELATIVE_PATH = Path(
    "client/godot/assets/visual/arena/R3_ASSET_MANIFEST.json"
)
ANIME_V1_MANIFEST_RELATIVE_PATH = Path(
    "client/godot/assets/visual/anime_v1/slice/ASSET_MANIFEST.json"
)
CARD_BODY_MANIFEST_RELATIVE_PATH = Path(
    "client/godot/assets/visual/anime_v1/card_body/CARD_BODY_ASSET_MANIFEST.json"
)
PRODUCT_CARD_ART_MANIFEST_RELATIVE_PATH = Path(
    "client/godot/assets/visual/anime_v1/cards/PRODUCT_CARD_ART_ASSET_MANIFEST.json"
)
VISUAL_ROOT = Path("client/godot/assets/visual")
ANIME_V1_ROOT = VISUAL_ROOT / "anime_v1/slice"
CARD_BODY_ROOT = VISUAL_ROOT / "anime_v1/card_body"
PRODUCT_CARD_ART_ROOT = VISUAL_ROOT / "anime_v1/cards"
MEDIA_SUFFIXES = {".png", ".webp", ".svg"}
RETIRED_PRODUCT_ROOTS = tuple((VISUAL_ROOT / suffix).as_posix() + "/"
    for suffix in ("cards/art", "cards/shared", "shared", "menu", "portraits", "arena", "r3"))
PRODUCT_FALLBACK = (VISUAL_ROOT / "anime_v1/shared/fallback_front.svg").as_posix()
PRESENTATION_V2_ROOT = VISUAL_ROOT / "anime_v1/presentation_v2"
PRESENTATION_V2_RECORD = (PRESENTATION_V2_ROOT / "GENERATION_RECORD.json").as_posix()
EXPECTED_PRESENTATION_V2_PATHS = {
    (PRESENTATION_V2_ROOT / name).as_posix()
    for name in ("engraved-platinum.png", "LO-11-cutin.png", "AP-11-cutin.png")
}
R3_CANDIDATE_FLOOR = (
    VISUAL_ROOT / "arena/r3_industrial_floor_albedo.png"
).as_posix()
ANIME_V1_CARD_NAMES = {
    "AP-03.png",
    "AP-05.png",
    "AP-11-evolved.png",
    "AP-11.png",
    "LO-03.png",
    "LO-07.png",
    "LO-11-evolved.png",
    "LO-11.png",
    "NT-04.png",
}
ANIME_V1_BASE_CARD_NAMES = {
    "AP-03.png",
    "AP-05.png",
    "AP-11.png",
    "LO-03.png",
    "LO-07.png",
    "LO-11.png",
    "NT-04.png",
}
ANIME_V1_CARD_PATHS = {
    (ANIME_V1_ROOT / "cards" / name).as_posix()
    for name in ANIME_V1_CARD_NAMES
}
ANIME_V1_LEADER_PATHS = {
    (ANIME_V1_ROOT / "leaders/aurelia-master.png").as_posix(),
    (ANIME_V1_ROOT / "leaders/theraea-master.png").as_posix(),
}
ANIME_V1_CARD_BACK = (ANIME_V1_ROOT / "shared/card-back.png").as_posix()
ANIME_V1_MENU = (ANIME_V1_ROOT / "menu/menu-key-art.png").as_posix()
ANIME_V1_ARENA = (ANIME_V1_ROOT / "arena/open-fantasy-arena.png").as_posix()
EXPECTED_ANIME_V1_PATHS = (
    ANIME_V1_CARD_PATHS
    | ANIME_V1_LEADER_PATHS
    | {ANIME_V1_CARD_BACK, ANIME_V1_MENU, ANIME_V1_ARENA}
)
EXPECTED_CARD_BODY_PATHS = {
    (CARD_BODY_ROOT / "crests" / name).as_posix()
    for name in ("neutral.svg", "oathguard.svg", "pactmage.svg")
} | {
    (CARD_BODY_ROOT / "frames" / name).as_posix()
    for name in ("amulet.svg", "field.svg", "follower.svg", "spell.svg", "trap.svg")
} | {
    (CARD_BODY_ROOT / "gems" / name).as_posix()
    for name in ("attack.svg", "cost.svg", "countdown.svg", "health.svg")
} | {
    (CARD_BODY_ROOT / "materials" / name).as_posix()
    for name in ("engraved-metal-v1.png", "legendary-foil-v1.png")
} | {
    (CARD_BODY_ROOT / "nameplates" / name).as_posix()
    for name in ("neutral.svg", "oathguard.svg", "pactmage.svg")
} | {
    (CARD_BODY_ROOT / "rarity" / name).as_posix()
    for name in ("common.svg", "epic.svg", "legendary.svg", "rare.svg")
} | {
    (CARD_BODY_ROOT / "variants" / name).as_posix()
    for name in ("evolved.svg", "token.svg")
}
CARD_BODY_RASTER_PATHS = {
    (CARD_BODY_ROOT / "materials" / name).as_posix()
    for name in ("engraved-metal-v1.png", "legendary-foil-v1.png")
}
PRODUCT_CARD_ART_NAMES = {
    "AP-01.png",
    "AP-02.png",
    "AP-04.png",
    "AP-06.png",
    "AP-07.png",
    "AP-08.png",
    "AP-09.png",
    "AP-10.png",
    "AP-S01.png",
    "AP-S02.png",
    "AP-S03.png",
    "AP-S04.png",
    "LO-01.png",
    "LO-02.png",
    "LO-04.png",
    "LO-05.png",
    "LO-06.png",
    "LO-08.png",
    "LO-09.png",
    "LO-10.png",
    "LO-S01.png",
    "LO-S02.png",
    "LO-S03.png",
    "LO-S04.png",
    "LO-T01.png",
    "NT-01.png",
    "NT-02.png",
    "NT-03.png",
}
EXPECTED_PRODUCT_CARD_ART_PATHS = {
    (PRODUCT_CARD_ART_ROOT / name).as_posix()
    for name in PRODUCT_CARD_ART_NAMES
}
EXPECTED_PRODUCT_BASE_CARD_NAMES = PRODUCT_CARD_ART_NAMES | ANIME_V1_BASE_CARD_NAMES
ANIME_V1_MAX_RESIDENT_TEXTURES = 24
ANIME_V1_MAX_ESTIMATED_VRAM_BYTES = 96 * 1024 * 1024
ANIME_V1_MAX_SOURCE_PAYLOAD_BYTES = 64 * 1024 * 1024
PRODUCT_CARD_ART_MAX_SOURCE_PAYLOAD_BYTES = 96 * 1024 * 1024
PRODUCT_CARD_ART_MAX_ESTIMATED_VRAM_BYTES = 64 * 1024 * 1024
PRODUCT_CARD_RUNTIME_MAX_RESIDENT_TEXTURES = 24
EXPECTED_TOP_LEVEL_FIELDS = {"schema_version", "gate", "assets"}
EXPECTED_PRODUCT_CARD_TOP_LEVEL_FIELDS = EXPECTED_TOP_LEVEL_FIELDS | {"budget"}
EXPECTED_PRODUCT_CARD_BUDGET_FIELDS = {
    "source_payload_bytes_max",
    "estimated_vram_bytes_max",
    "runtime_resident_identity_texture_limit",
}
EXPECTED_ASSET_FIELDS = {
    "path",
    "sha256",
    "purpose",
    "generation_method",
    "date",
    "prompt_summary",
}
EXPECTED_PRESENTATION_V2_FIELDS = EXPECTED_ASSET_FIELDS | {
    "source_images", "modification_history", "generation_record", "authorization",
    "transparency",
}
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")


class VisualAssetAuditError(ValueError):
    """Raised when the committed visual inventory is incomplete or unsafe."""


def _audit_presentation_v2_entry(repo_root: Path, entry: dict[str, Any]) -> None:
    """Keep the three review assets truthful without relaxing older inventories."""
    relative = entry["path"]
    if set(entry) != EXPECTED_PRESENTATION_V2_FIELDS or entry["date"] != "2026-09-06":
        raise VisualAssetAuditError("Presentation V2 requires exact provenance fields and date")
    if entry["generation_record"] != PRESENTATION_V2_RECORD:
        raise VisualAssetAuditError("Presentation V2 must refer to its complete generation record")
    record = _load_json(repo_root / PRESENTATION_V2_RECORD)
    if not isinstance(record, dict) or not isinstance(record.get("material"), str):
        raise VisualAssetAuditError("Presentation V2 is missing its complete material prompt")
    if not isinstance(entry["authorization"], str) or not entry["authorization"].strip():
        raise VisualAssetAuditError("Presentation V2 authorization/review boundary is missing")
    history = entry["modification_history"]
    if not isinstance(history, list) or not history or any(
        not isinstance(item, str) or not item.startswith("2026-09-06: ") for item in history
    ):
        raise VisualAssetAuditError("Presentation V2 dated modification history is missing")
    is_cutin = relative.endswith("-cutin.png")
    expected_transparency = "rgb_chroma_key_runtime" if is_cutin else "opaque_rgb_material"
    if entry["transparency"] != expected_transparency:
        raise VisualAssetAuditError("Presentation V2 RGB sources must not claim native alpha")
    sources = entry["source_images"]
    if not isinstance(sources, list) or len(sources) != int(is_cutin):
        raise VisualAssetAuditError("Presentation V2 must identify the exact original input image")
    if is_cutin:
        design_id = Path(relative).stem.removesuffix("-cutin")
        expected_source = (ANIME_V1_ROOT / "cards" / f"{design_id}-evolved.png").as_posix()
        source = sources[0]
        if not isinstance(source, dict) or set(source) != {"path", "sha256"} or source["path"] != expected_source:
            raise VisualAssetAuditError("Presentation V2 source must be its original evolved artwork")
        source_path = repo_root / expected_source
        if not source_path.is_file() or hashlib.sha256(source_path.read_bytes()).hexdigest() != source["sha256"]:
            raise VisualAssetAuditError("Presentation V2 original input SHA-256 mismatch")
        cutouts, chroma = record.get("cutouts"), record.get("chroma")
        if not isinstance(cutouts, list) or not any(
            isinstance(item, list) and len(item) == 2 and item[0] == design_id
            and isinstance(item[1], str) and item[1].strip() for item in cutouts
        ) or not isinstance(chroma, list) or not any(
            isinstance(item, dict) and item.get("id") == design_id
            and isinstance(item.get("prompt"), str) and item["prompt"].strip() for item in chroma
        ):
            raise VisualAssetAuditError("Presentation V2 extraction and final chroma prompts are required")
    asset_path = repo_root / relative
    expected_dimensions = (1024, 1536) if is_cutin else (1254, 1254)
    if _png_dimensions(asset_path) != expected_dimensions or asset_path.read_bytes()[24:26] != b"\x08\x02":
        raise VisualAssetAuditError("Presentation V2 expected RGB8 dimensions do not match the source")
    import_path = repo_root / f"{relative}.import"
    try:
        import_text = import_path.read_text(encoding="utf-8")
    except (FileNotFoundError, UnicodeDecodeError) as error:
        raise VisualAssetAuditError("Presentation V2 Godot import sidecar is missing") from error
    expected_source = "res://" + relative.removeprefix("client/godot/")
    required = {'"vram_texture": true', "compress/mode=2", "compress/high_quality=true",
                "mipmaps/generate=true", "process/premult_alpha=false", f'source_file="{expected_source}"'}
    if missing := sorted(setting for setting in required if setting not in import_text):
        raise VisualAssetAuditError(f"Presentation V2 requires desktop compression and mipmaps: {missing}")


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


def _estimated_desktop_vram_bytes(width: int, height: int) -> int:
    """Conservative BC3/BC7-size estimate including the complete mip chain."""
    if width <= 0 or height <= 0:
        raise VisualAssetAuditError("texture dimensions must be positive")
    total = 0
    while True:
        total += ((width + 3) // 4) * ((height + 3) // 4) * 16
        if width == 1 and height == 1:
            return total
        width = max(1, width // 2)
        height = max(1, height // 2)


def _rgba_alpha_extrema(path: Path) -> tuple[int, int]:
    """Return alpha extrema for a non-interlaced, 8-bit RGBA PNG."""
    payload = path.read_bytes()
    if payload[:8] != b"\x89PNG\r\n\x1a\n":
        raise VisualAssetAuditError(f"invalid PNG signature: {path}")
    offset = 8
    width = height = 0
    compressed = bytearray()
    while offset + 12 <= len(payload):
        length = struct.unpack(">I", payload[offset : offset + 4])[0]
        kind = payload[offset + 4 : offset + 8]
        data_start = offset + 8
        data_end = data_start + length
        if data_end + 4 > len(payload):
            raise VisualAssetAuditError(f"truncated PNG chunk: {path}")
        data = payload[data_start:data_end]
        if kind == b"IHDR":
            if len(data) != 13:
                raise VisualAssetAuditError(f"invalid PNG IHDR: {path}")
            width, height, bit_depth, color_type, compression, filtering, interlace = (
                struct.unpack(">IIBBBBB", data)
            )
            if (
                bit_depth != 8
                or color_type != 6
                or compression != 0
                or filtering != 0
                or interlace != 0
            ):
                raise VisualAssetAuditError(
                    "AnimeV1 leader master must be a non-interlaced 8-bit RGBA PNG: "
                    f"{path}"
                )
        elif kind == b"IDAT":
            compressed.extend(data)
        elif kind == b"IEND":
            break
        offset = data_end + 4
    if width <= 0 or height <= 0 or not compressed:
        raise VisualAssetAuditError(f"incomplete RGBA PNG: {path}")

    try:
        decoded = zlib.decompress(bytes(compressed))
    except zlib.error as error:
        raise VisualAssetAuditError(f"invalid PNG image data: {path}") from error
    bytes_per_pixel = 4
    stride = width * bytes_per_pixel
    if len(decoded) != (stride + 1) * height:
        raise VisualAssetAuditError(f"unexpected RGBA scanline size: {path}")

    previous = bytearray(stride)
    alpha_min = 255
    alpha_max = 0
    source_offset = 0
    for _ in range(height):
        filter_type = decoded[source_offset]
        source_offset += 1
        filtered = decoded[source_offset : source_offset + stride]
        source_offset += stride
        row = bytearray(stride)
        for index, value in enumerate(filtered):
            left = row[index - bytes_per_pixel] if index >= bytes_per_pixel else 0
            above = previous[index]
            upper_left = (
                previous[index - bytes_per_pixel]
                if index >= bytes_per_pixel
                else 0
            )
            if filter_type == 0:
                predictor = 0
            elif filter_type == 1:
                predictor = left
            elif filter_type == 2:
                predictor = above
            elif filter_type == 3:
                predictor = (left + above) // 2
            elif filter_type == 4:
                estimate = left + above - upper_left
                distance_left = abs(estimate - left)
                distance_above = abs(estimate - above)
                distance_upper_left = abs(estimate - upper_left)
                predictor = (
                    left
                    if distance_left <= distance_above
                    and distance_left <= distance_upper_left
                    else above
                    if distance_above <= distance_upper_left
                    else upper_left
                )
            else:
                raise VisualAssetAuditError(
                    f"unsupported PNG row filter {filter_type}: {path}"
                )
            row[index] = (value + predictor) & 0xFF
        row_alpha = row[3::4]
        alpha_min = min(alpha_min, min(row_alpha))
        alpha_max = max(alpha_max, max(row_alpha))
        previous = row
    return alpha_min, alpha_max


def audit(
    repo_root: Path,
    manifest_path: Path | None = None,
    *,
    enforce_product_set: bool = True,
) -> dict[str, Any]:
    repo_root = repo_root.resolve()
    explicit_manifest = manifest_path is not None
    product_manifest_path = (
        manifest_path or repo_root / MANIFEST_RELATIVE_PATH
    ).resolve()
    manifest_paths = [product_manifest_path]
    candidate_manifest_path = (
        repo_root / R3_CANDIDATE_MANIFEST_RELATIVE_PATH
    ).resolve()
    if not explicit_manifest and candidate_manifest_path.is_file():
        manifest_paths.append(candidate_manifest_path)
    anime_manifest_path = (
        repo_root / ANIME_V1_MANIFEST_RELATIVE_PATH
    ).resolve()
    if not explicit_manifest and anime_manifest_path.is_file():
        manifest_paths.append(anime_manifest_path)
    card_body_manifest_path = (
        repo_root / CARD_BODY_MANIFEST_RELATIVE_PATH
    ).resolve()
    if not explicit_manifest and card_body_manifest_path.is_file():
        manifest_paths.append(card_body_manifest_path)
    product_card_art_manifest_path = (
        repo_root / PRODUCT_CARD_ART_MANIFEST_RELATIVE_PATH
    ).resolve()
    if not explicit_manifest and product_card_art_manifest_path.is_file():
        manifest_paths.append(product_card_art_manifest_path)
    if not explicit_manifest:
        discovered_manifest_paths = {
            path.resolve()
            for path in (repo_root / VISUAL_ROOT).rglob("*ASSET_MANIFEST.json")
            if path.is_file()
        }
        expected_manifest_paths = set(manifest_paths)
        if discovered_manifest_paths != expected_manifest_paths:
            raise VisualAssetAuditError(
                "visual manifest set differs from the frozen product manifests plus "
                "the isolated R3, AnimeV1, card-body and product-card manifests; "
                "missing="
                f"{sorted(str(path) for path in expected_manifest_paths - discovered_manifest_paths)}, "
                "unexpected="
                f"{sorted(str(path) for path in discovered_manifest_paths - expected_manifest_paths)}"
            )

    for current_manifest_path in manifest_paths:
        try:
            current_manifest_path.relative_to(repo_root)
        except ValueError as error:
            raise VisualAssetAuditError(
                "asset manifest must be inside the repository"
            ) from error

    registered: set[str] = set()
    hashes: set[str] = set()
    registered_by_manifest: dict[Path, set[str]] = {}
    manifest_hashes: dict[Path, str] = {}
    product_card_art_budget: dict[str, int] | None = None
    entry_count = 0
    for manifest_index, current_manifest_path in enumerate(manifest_paths):
        manifest = _load_json(current_manifest_path)
        expected_top_level_fields = (
            EXPECTED_PRODUCT_CARD_TOP_LEVEL_FIELDS
            if current_manifest_path == product_card_art_manifest_path
            else EXPECTED_TOP_LEVEL_FIELDS
        )
        if not isinstance(manifest, dict) or set(manifest) != expected_top_level_fields:
            raise VisualAssetAuditError(
                f"manifest fields must be exactly {sorted(expected_top_level_fields)}"
            )
        expected_gate = (
            "4B-R3.1"
            if current_manifest_path == candidate_manifest_path
            else "6A-R1"
            if current_manifest_path == card_body_manifest_path
            else "5C-6C"
            if current_manifest_path == product_card_art_manifest_path
            else "6A"
            if current_manifest_path == anime_manifest_path
            else "5C-6C"
        )
        if manifest["schema_version"] != 1 or manifest["gate"] != expected_gate:
            raise VisualAssetAuditError(
                f"asset manifest must identify {expected_gate} schema 1"
            )
        if current_manifest_path == product_card_art_manifest_path:
            raw_budget = manifest["budget"]
            if (
                not isinstance(raw_budget, dict)
                or set(raw_budget) != EXPECTED_PRODUCT_CARD_BUDGET_FIELDS
            ):
                raise VisualAssetAuditError(
                    "product-card budget fields must be exactly "
                    f"{sorted(EXPECTED_PRODUCT_CARD_BUDGET_FIELDS)}"
                )
            expected_budget = {
                "source_payload_bytes_max": PRODUCT_CARD_ART_MAX_SOURCE_PAYLOAD_BYTES,
                "estimated_vram_bytes_max": PRODUCT_CARD_ART_MAX_ESTIMATED_VRAM_BYTES,
                "runtime_resident_identity_texture_limit": (
                    PRODUCT_CARD_RUNTIME_MAX_RESIDENT_TEXTURES
                ),
            }
            if raw_budget != expected_budget:
                raise VisualAssetAuditError(
                    "product-card budget does not match the locked package and "
                    f"runtime contract: expected={expected_budget}, got={raw_budget}"
                )
            product_card_art_budget = raw_budget
        entries = manifest["assets"]
        if not isinstance(entries, list) or not entries:
            raise VisualAssetAuditError("asset manifest must contain at least one asset")

        current_registered: set[str] = set()
        for index, entry in enumerate(entries):
            expected_asset_fields = (
                EXPECTED_PRESENTATION_V2_FIELDS
                if isinstance(entry, dict) and isinstance(entry.get("path"), str)
                and entry["path"] in EXPECTED_PRESENTATION_V2_PATHS
                else EXPECTED_ASSET_FIELDS
            )
            if not isinstance(entry, dict) or set(entry) != expected_asset_fields:
                raise VisualAssetAuditError(
                    f"asset[{index}] fields must be exactly {sorted(expected_asset_fields)}"
                )
            relative = _validate_relative_path(entry["path"])
            if relative in registered:
                raise VisualAssetAuditError(
                    f"asset path must appear in exactly one manifest: {relative}"
                )
            registered.add(relative)
            current_registered.add(relative)

            expected_hash = entry["sha256"]
            if not isinstance(expected_hash, str) or not SHA256_RE.fullmatch(expected_hash):
                raise VisualAssetAuditError(f"asset[{index}] has an invalid SHA-256")
            if expected_hash in hashes:
                raise VisualAssetAuditError(
                    "assets must be visually unique at the byte level across manifests; "
                    f"duplicate SHA-256: {expected_hash}"
                )
            hashes.add(expected_hash)

            asset_path = (repo_root / Path(relative)).resolve()
            try:
                asset_path.relative_to((repo_root / VISUAL_ROOT).resolve())
            except ValueError as error:
                raise VisualAssetAuditError(
                    f"asset escapes the visual root: {relative}"
                ) from error
            if not asset_path.is_file():
                raise VisualAssetAuditError(
                    f"registered asset does not exist: {relative}"
                )
            actual_hash = hashlib.sha256(asset_path.read_bytes()).hexdigest()
            if actual_hash != expected_hash:
                raise VisualAssetAuditError(
                    f"SHA-256 mismatch for {relative}: expected {expected_hash}, "
                    f"got {actual_hash}"
                )

            for field in ("purpose", "generation_method", "prompt_summary"):
                value = entry[field]
                if not isinstance(value, str) or not value.strip():
                    raise VisualAssetAuditError(
                        f"asset[{index}].{field} must be non-empty"
                    )
            generated_on = entry["date"]
            if not isinstance(generated_on, str):
                raise VisualAssetAuditError(
                    f"asset[{index}].date must be ISO YYYY-MM-DD"
                )
            try:
                date.fromisoformat(generated_on)
            except ValueError as error:
                raise VisualAssetAuditError(
                    f"asset[{index}].date must be ISO YYYY-MM-DD"
                ) from error
            if relative in EXPECTED_PRESENTATION_V2_PATHS:
                _audit_presentation_v2_entry(repo_root, entry)

        entry_count += len(entries)
        registered_by_manifest[current_manifest_path] = current_registered
        manifest_hashes[current_manifest_path] = hashlib.sha256(
            current_manifest_path.read_bytes()
        ).hexdigest()

    product_registered = registered_by_manifest[product_manifest_path]
    candidate_registered = registered_by_manifest.get(candidate_manifest_path, set())
    anime_registered = registered_by_manifest.get(anime_manifest_path, set())
    card_body_registered = registered_by_manifest.get(
        card_body_manifest_path, set()
    )
    product_card_art_registered = registered_by_manifest.get(
        product_card_art_manifest_path, set()
    )

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

    # Reject retired model/shader/import resources too, not just registered images.
    retired = sorted(
        path.relative_to(repo_root).as_posix()
        for path in (repo_root / VISUAL_ROOT).rglob("*")
        if path.is_file() and path.relative_to(repo_root).as_posix().startswith(RETIRED_PRODUCT_ROOTS)
    )
    if enforce_product_set and retired:
        raise VisualAssetAuditError(
            f"Retired industrial product assets must not remain: {retired}"
        )
    anime_estimated_vram_bytes = 0
    anime_source_payload_bytes = 0
    product_card_art_estimated_vram_bytes = 0
    product_card_art_source_payload_bytes = 0
    if enforce_product_set:
        expected_product_paths = {PRODUCT_FALLBACK} | EXPECTED_PRESENTATION_V2_PATHS
        if product_registered != expected_product_paths:
            raise VisualAssetAuditError(
                "Product shared manifest must contain the AnimeV1 fallback and exact three Presentation V2 candidates; "
                f"missing={sorted(expected_product_paths - product_registered)}, "
                f"unexpected={sorted(product_registered - expected_product_paths)}"
            )
        if candidate_registered and candidate_registered != {R3_CANDIDATE_FLOOR}:
            raise VisualAssetAuditError(
                "Gate 4B-R3.1 candidate manifest must contain only the candidate floor; "
                f"got={sorted(candidate_registered)}"
            )
        if anime_registered != EXPECTED_ANIME_V1_PATHS:
            raise VisualAssetAuditError(
                "Gate 6A AnimeV1 manifest must contain the exact 14-item visual slice; "
                f"missing={sorted(EXPECTED_ANIME_V1_PATHS - anime_registered)}, "
                f"unexpected={sorted(anime_registered - EXPECTED_ANIME_V1_PATHS)}"
            )
        if card_body_registered != EXPECTED_CARD_BODY_PATHS:
            raise VisualAssetAuditError(
                "Gate 6A-R1 card-body manifest must contain the exact 23-item "
                "approval asset set; "
                f"missing={sorted(EXPECTED_CARD_BODY_PATHS - card_body_registered)}, "
                f"unexpected={sorted(card_body_registered - EXPECTED_CARD_BODY_PATHS)}"
            )
        if product_card_art_registered != EXPECTED_PRODUCT_CARD_ART_PATHS:
            raise VisualAssetAuditError(
                "Gate 5C-6C product-card manifest must contain the exact 28-item "
                "illustration batch; "
                f"missing={sorted(EXPECTED_PRODUCT_CARD_ART_PATHS - product_card_art_registered)}, "
                f"unexpected={sorted(product_card_art_registered - EXPECTED_PRODUCT_CARD_ART_PATHS)}"
            )
        if len(EXPECTED_PRODUCT_BASE_CARD_NAMES) != 35:
            raise VisualAssetAuditError(
                "product art inventory must provide exactly 34 constructible base "
                "illustrations and one derived-token illustration"
            )
        for relative in sorted(CARD_BODY_RASTER_PATHS):
            import_path = repo_root / f"{relative}.import"
            try:
                import_text = import_path.read_text(encoding="utf-8")
            except (FileNotFoundError, UnicodeDecodeError) as error:
                raise VisualAssetAuditError(
                    f"Gate 6A-R1 material is missing a valid Godot import sidecar: {relative}"
                ) from error
            expected_source = "res://" + relative.removeprefix("client/godot/")
            required_import_settings = {
                '"vram_texture": true',
                "compress/mode=2",
                "compress/high_quality=true",
                "mipmaps/generate=true",
                f'source_file="{expected_source}"',
            }
            missing_settings = sorted(
                setting
                for setting in required_import_settings
                if setting not in import_text
            )
            if missing_settings:
                raise VisualAssetAuditError(
                    "Gate 6A-R1 card-frame materials must use desktop VRAM "
                    f"compression and mipmaps for {relative}; missing={missing_settings}"
                )
        for relative in sorted(ANIME_V1_CARD_PATHS | {ANIME_V1_CARD_BACK}):
            width, height = _png_dimensions(repo_root / relative)
            if width * 3 != height * 2:
                raise VisualAssetAuditError(
                    f"AnimeV1 card illustration/back must use an exact 2:3 canvas: "
                    f"{relative} is {width}x{height}"
                )
        for relative in sorted(ANIME_V1_LEADER_PATHS):
            width, height = _png_dimensions(repo_root / relative)
            if width * 3 != height * 2:
                raise VisualAssetAuditError(
                    f"AnimeV1 leader master must use an exact 2:3 canvas: "
                    f"{relative} is {width}x{height}"
                )
            alpha_min, alpha_max = _rgba_alpha_extrema(repo_root / relative)
            if alpha_min != 0 or alpha_max == 0:
                raise VisualAssetAuditError(
                    "AnimeV1 leader master must contain real transparent and visible "
                    f"pixels: {relative} alpha={alpha_min}..{alpha_max}"
                )
        for relative in (ANIME_V1_MENU, ANIME_V1_ARENA):
            width, height = _png_dimensions(repo_root / relative)
            if abs(width / height - 16 / 9) > 0.01:
                raise VisualAssetAuditError(
                    f"AnimeV1 widescreen art must be approximately 16:9: "
                    f"{relative} is {width}x{height}"
                )
        for relative in sorted(EXPECTED_ANIME_V1_PATHS):
            width, height = _png_dimensions(repo_root / relative)
            anime_estimated_vram_bytes += _estimated_desktop_vram_bytes(
                width, height
            )
            anime_source_payload_bytes += (repo_root / relative).stat().st_size
            import_path = repo_root / f"{relative}.import"
            try:
                import_text = import_path.read_text(encoding="utf-8")
            except (FileNotFoundError, UnicodeDecodeError) as error:
                raise VisualAssetAuditError(
                    f"AnimeV1 asset is missing a valid Godot import sidecar: {relative}"
                ) from error
            expected_source = "res://" + relative.removeprefix("client/godot/")
            required_import_settings = {
                '"vram_texture": true',
                "compress/mode=2",
                "compress/high_quality=true",
                "mipmaps/generate=true",
                f'source_file="{expected_source}"',
            }
            missing_settings = sorted(
                setting
                for setting in required_import_settings
                if setting not in import_text
            )
            if missing_settings:
                raise VisualAssetAuditError(
                    "AnimeV1 Godot import must use desktop VRAM compression and "
                    f"mipmaps for {relative}; missing={missing_settings}"
                )
        for relative in sorted(EXPECTED_PRODUCT_CARD_ART_PATHS):
            width, height = _png_dimensions(repo_root / relative)
            if (width, height) != (1024, 1536):
                raise VisualAssetAuditError(
                    "product-card illustration must use the locked 1024x1536 "
                    f"2:3 canvas: {relative} is {width}x{height}"
                )
            product_card_art_estimated_vram_bytes += _estimated_desktop_vram_bytes(
                width, height
            )
            product_card_art_source_payload_bytes += (repo_root / relative).stat().st_size
            import_path = repo_root / f"{relative}.import"
            try:
                import_text = import_path.read_text(encoding="utf-8")
            except (FileNotFoundError, UnicodeDecodeError) as error:
                raise VisualAssetAuditError(
                    f"product-card art is missing a valid Godot import sidecar: {relative}"
                ) from error
            expected_source = "res://" + relative.removeprefix("client/godot/")
            required_import_settings = {
                '"vram_texture": true',
                "compress/mode=2",
                "compress/high_quality=true",
                "mipmaps/generate=true",
                f'source_file="{expected_source}"',
            }
            missing_settings = sorted(
                setting
                for setting in required_import_settings
                if setting not in import_text
            )
            if missing_settings:
                raise VisualAssetAuditError(
                    "product-card Godot import must use desktop VRAM compression "
                    f"and mipmaps for {relative}; missing={missing_settings}"
                )
        if len(anime_registered) > ANIME_V1_MAX_RESIDENT_TEXTURES:
            raise VisualAssetAuditError(
                "Gate 6A AnimeV1 identity texture count exceeds the 24-texture "
                f"residency ceiling: {len(anime_registered)}"
            )
        if anime_estimated_vram_bytes > ANIME_V1_MAX_ESTIMATED_VRAM_BYTES:
            raise VisualAssetAuditError(
                "Gate 6A AnimeV1 conservative desktop VRAM estimate exceeds 96 MiB: "
                f"{anime_estimated_vram_bytes} bytes"
            )
        if anime_source_payload_bytes > ANIME_V1_MAX_SOURCE_PAYLOAD_BYTES:
            raise VisualAssetAuditError(
                "Gate 6A AnimeV1 source payload exceeds the 64 MiB package ceiling: "
                f"{anime_source_payload_bytes} bytes"
            )
        if product_card_art_budget is None:
            raise VisualAssetAuditError("product-card budget contract is missing")
        if (
            product_card_art_estimated_vram_bytes
            > product_card_art_budget["estimated_vram_bytes_max"]
        ):
            raise VisualAssetAuditError(
                "Gate 5C-6C product-card conservative desktop VRAM estimate exceeds "
                f"its 64 MiB batch ceiling: {product_card_art_estimated_vram_bytes} bytes"
            )
        if (
            product_card_art_source_payload_bytes
            > product_card_art_budget["source_payload_bytes_max"]
        ):
            raise VisualAssetAuditError(
                "Gate 5C-6C product-card source payload exceeds its 96 MiB batch "
                f"ceiling: {product_card_art_source_payload_bytes} bytes"
            )

    return {
        "asset_count": entry_count,
        "product_asset_count": len(product_registered),
        "presentation_v2_asset_count": len(product_registered & EXPECTED_PRESENTATION_V2_PATHS),
        "presentation_v2_source_payload_bytes": sum(
            (repo_root / path).stat().st_size for path in product_registered & EXPECTED_PRESENTATION_V2_PATHS
        ),
        "candidate_asset_count": len(candidate_registered),
        "anime_asset_count": len(anime_registered),
        "card_body_asset_count": len(card_body_registered),
        "product_card_art_asset_count": len(product_card_art_registered),
        "anime_estimated_vram_bytes": anime_estimated_vram_bytes,
        "anime_source_payload_bytes": anime_source_payload_bytes,
        "product_card_art_estimated_vram_bytes": (
            product_card_art_estimated_vram_bytes
        ),
        "product_card_art_source_payload_bytes": product_card_art_source_payload_bytes,
        "product_card_runtime_max_resident_textures": (
            product_card_art_budget[
                "runtime_resident_identity_texture_limit"
            ]
            if product_card_art_budget is not None
            else 0
        ),
        "manifest_sha256": manifest_hashes[product_manifest_path],
        "product_manifest_sha256": manifest_hashes[product_manifest_path],
        "candidate_manifest_sha256": manifest_hashes.get(candidate_manifest_path),
        "anime_manifest_sha256": manifest_hashes.get(anime_manifest_path),
        "card_body_manifest_sha256": manifest_hashes.get(card_body_manifest_path),
        "product_card_art_manifest_sha256": manifest_hashes.get(
            product_card_art_manifest_path
        ),
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
        f"audited {result['product_asset_count']} AnimeV1 shared/presentation assets "
        f"({result['presentation_v2_asset_count']} first-stage Presentation V2 candidates) and "
        f"{result['candidate_asset_count']} R3 candidate assets and "
        f"{result['anime_asset_count']} Gate 6A AnimeV1 assets; "
        f"{result['card_body_asset_count']} Gate 6A-R1 card-body assets; "
        f"{result['product_card_art_asset_count']} Gate 5C-6C product-card assets; "
        f"anime_estimated_vram_bytes={result['anime_estimated_vram_bytes']}; "
        f"anime_source_payload_bytes={result['anime_source_payload_bytes']}; "
        "product_card_art_estimated_vram_bytes="
        f"{result['product_card_art_estimated_vram_bytes']}; "
        "product_card_art_source_payload_bytes="
        f"{result['product_card_art_source_payload_bytes']}; "
        f"presentation_v2_source_payload_bytes={result['presentation_v2_source_payload_bytes']}; "
        "product_card_runtime_max_resident_textures="
        f"{result['product_card_runtime_max_resident_textures']}; "
        f"product_manifest_sha256={result['product_manifest_sha256']}; "
        f"candidate_manifest_sha256={result['candidate_manifest_sha256'] or 'none'}; "
        f"anime_manifest_sha256={result['anime_manifest_sha256'] or 'none'}"
        f"; card_body_manifest_sha256="
        f"{result['card_body_manifest_sha256'] or 'none'}"
        f"; product_card_art_manifest_sha256="
        f"{result['product_card_art_manifest_sha256'] or 'none'}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
