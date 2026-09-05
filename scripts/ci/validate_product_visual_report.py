#!/usr/bin/env python3
"""Validate real-v05 capture files and measured heavy-board evidence, never preview reports."""
# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import struct
import sys
from pathlib import Path

try:
    from .validate_anime_card_body_slice import _decode_rgba8_png, AnimeCardBodySliceError
except ImportError:  # Direct CLI invocation.
    from validate_anime_card_body_slice import _decode_rgba8_png, AnimeCardBodySliceError

REQUIRED_STATES = {"menu", "setup", "covered", "mulligan", "action", "choice", "reaction", "resolving", "finished"}
OPTIONAL_STATES = {"mulligan-review", "source-selection", "mode-selection", "additional-cost",
                   "slot-selection", "target-selection", "advance-selection", "reaction-target", "error"}
CAPTURE_FIELDS = {"state", "viewer", "revision", "sha256", "width", "height"}
PERFORMANCE_FIELDS = {"schema_version", "suite", "status", "success", "state", "viewer", "revision",
                      "width", "height", "player0_main_board", "player1_main_board", "warmup_frames",
                      "measured_frames", "before", "after", "zero_growth", "p95_ms", "max_ms"}
RESOURCE_FIELDS = {"actors", "materials", "textures", "resources"}
RESOLUTIONS = {(1280, 720), (1600, 900), (2560, 1440), (2560, 1600)}


class ProductVisualError(ValueError):
    pass


def _keys(value: object, expected: set[str], label: str) -> dict:
    if type(value) is not dict or set(value) != expected:
        raise ProductVisualError(f"{label} must contain exactly its identity-free schema fields")
    return value


def _integer(value: object, low: int, high: int, label: str) -> int:
    if type(value) is not int or not low <= value <= high:
        raise ProductVisualError(f"{label} must be an integer in [{low}, {high}]")
    return value


def _load(path: Path) -> object:
    if not path.is_file() or path.stat().st_size > 1_048_576:
        raise ProductVisualError(f"Missing or oversized report: {path.name}")
    def unique(pairs: list[tuple[str, object]]) -> dict:
        result = {}
        for key, value in pairs:
            if key in result: raise ProductVisualError("Duplicate report key")
            result[key] = value
        return result
    try:
        return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique)
    except (UnicodeError, json.JSONDecodeError) as error:
        raise ProductVisualError(f"Invalid UTF-8 JSON: {path.name}") from error


def _validate_image(path: Path, width: int, height: int, digest: str) -> bytes:
    if path.is_symlink() or not path.is_file() or not 32 <= path.stat().st_size <= 64 * 1024 * 1024:
        raise ProductVisualError(f"Missing, linked or oversized screenshot: {path.name}")
    payload = path.read_bytes()
    if hashlib.sha256(payload).hexdigest() != digest:
        raise ProductVisualError(f"Screenshot hash mismatch: {path.name}")
    if payload[:8] != b"\x89PNG\r\n\x1a\n" or payload[12:16] != b"IHDR" or struct.unpack_from(">II", payload, 16) != (width, height):
        raise ProductVisualError(f"Screenshot dimensions disagree with the GPU report: {path.name}")
    try:
        actual_width, actual_height, pixels = _decode_rgba8_png(payload, path.name)
    except AnimeCardBodySliceError as error:
        raise ProductVisualError(str(error)) from error
    if (actual_width, actual_height) != (width, height):
        raise ProductVisualError("Decoded GPU dimensions differ")
    low, high, lit = 255, 0, 0
    for y in range(90):
        for x in range(160):
            offset = ((y * height // 90) * width + x * width // 160) * 4
            value = max(pixels[offset:offset + 3])
            low, high = min(low, value), max(high, value)
            lit += value > 24
    if high - low < 24 or lit < 288:
        raise ProductVisualError(f"Blank GPU evidence: {path.name}")
    return pixels


def validate_performance(report: object, resolution: tuple[int, int]) -> None:
    data = _keys(report, PERFORMANCE_FIELDS, "performance")
    if type(data["schema_version"]) is not int or data["schema_version"] != 1 or data["suite"] != "product-v05-heavy-board":
        raise ProductVisualError("Not the product heavy-board contract")
    if data["status"] != "passed" or data["success"] is not True or data["zero_growth"] is not True:
        raise ProductVisualError("Missing/failed heavy-board measurements cannot pass")
    if data["state"] != "action": raise ProductVisualError("Menu/terminal/covered frames are not heavy-board measurements")
    _integer(data["viewer"], 0, 1, "performance.viewer")
    _integer(data["revision"], 1, 2**64 - 1, "performance.revision")
    for field in ("width", "height"): _integer(data[field], 1, 16384, field)
    if (data["width"], data["height"]) != resolution: raise ProductVisualError("Performance used a different resolution")
    first = _integer(data["player0_main_board"], 3, 5, "player0_main_board")
    second = _integer(data["player1_main_board"], 3, 5, "player1_main_board")
    if first + second < 8: raise ProductVisualError("Fewer than eight occupied real main-board slots")
    for field in ("warmup_frames", "measured_frames"):
        _integer(data[field], 300, 300, field)
    for key in ("before", "after"):
        counts = _keys(data[key], RESOURCE_FIELDS, key)
        for field in RESOURCE_FIELDS: _integer(counts[field], 1, 1_000_000, f"{key}.{field}")
        if counts["actors"] < first + second: raise ProductVisualError("No actors rendered for the heavy board")
    if any(data["after"][key] > data["before"][key] for key in data["before"]):
        raise ProductVisualError("Visual resources grew after warmup")
    for field in ("p95_ms", "max_ms"):
        if type(data[field]) not in (int, float) or not math.isfinite(data[field]) or data[field] <= 0:
            raise ProductVisualError(f"Invalid frame timing: {field}")
    if data["p95_ms"] > 33.3 or data["max_ms"] >= 100 or data["p95_ms"] > data["max_ms"]:
        raise ProductVisualError("Product heavy-board frame budget exceeded")


def validate_directory(directory: Path | str, require_performance: bool = False) -> None:
    directory = Path(directory).resolve()
    data = _keys(_load(directory / "product-visual.json"),
                 {"schema_version", "suite", "success", "missing_states", "captures"}, "visual report")
    if type(data["schema_version"]) is not int or data["schema_version"] != 1 or data["suite"] != "product-v05-visual":
        raise ProductVisualError("Not a current product visual report")
    if data["success"] is not True or data["missing_states"] != []: raise ProductVisualError("Visual capture incomplete")
    if type(data["captures"]) is not list: raise ProductVisualError("captures must be an array")
    seen: set[str] = set()
    resolution = None
    for raw in data["captures"]:
        capture = _keys(raw, CAPTURE_FIELDS, "capture")
        state = capture["state"]
        if type(state) is not str or state not in REQUIRED_STATES | OPTIONAL_STATES or state in seen:
            raise ProductVisualError("Unknown or duplicate UI state")
        seen.add(state)
        width = _integer(capture["width"], 1280, 4096, "capture.width")
        height = _integer(capture["height"], 720, 4096, "capture.height")
        if (width, height) not in RESOLUTIONS: raise ProductVisualError("Unrequested visual resolution")
        if resolution is not None and resolution != (width, height): raise ProductVisualError("Resolution changed during capture")
        resolution = width, height
        if state in {"menu", "setup", "covered", "resolving", "error"}:
            if capture["viewer"] is not None: raise ProductVisualError("Covered/public capture cannot carry a private viewer")
        else:
            _integer(capture["viewer"], 0, 1, "capture.viewer")
        if state in {"menu", "setup"}:
            if capture["revision"] is not None: raise ProductVisualError("Shell capture cannot claim a native revision")
        elif state not in {"covered", "error"} or capture["revision"] is not None:
            _integer(capture["revision"], 1, 2**64 - 1, "capture.revision")
        digest = capture["sha256"]
        if type(digest) is not str or re.fullmatch(r"[a-f0-9]{64}", digest) is None:
            raise ProductVisualError("Invalid screenshot SHA-256")
        _validate_image(directory / f"{state}.png", width, height, digest)
    if not REQUIRED_STATES <= seen: raise ProductVisualError(f"Missing states: {sorted(REQUIRED_STATES - seen)}")
    if {path.name for path in directory.glob("*.png")} != {f"{state}.png" for state in seen}:
        raise ProductVisualError("Unmanifested/stale PNG remains in this capture directory")
    performance = directory / "product-performance.json"
    if require_performance or performance.exists():
        assert resolution is not None
        validate_performance(_load(performance), resolution)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--require-performance", action="store_true")
    arguments = parser.parse_args()
    try:
        validate_directory(arguments.directory, arguments.require_performance)
    except (OSError, ProductVisualError) as error:
        print(f"Product visual validation failed: {error}", file=sys.stderr)
        return 1
    print("Product visual report validated")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
