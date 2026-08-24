#!/usr/bin/env python3
"""Validate a display-backed Gate 4B visual-suite manifest and screenshots."""

# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import argparse
import hashlib
import json
import math
import struct
import sys
import zlib
from pathlib import Path
from typing import Any


LEGACY_EXPECTED_STATES = {
    "menu",
    "match-setup",
    "covered",
    "mulligan",
    "action",
    "source-selection",
    "slot-or-target-selection",
    "reaction",
    "resolving",
    "result",
    "error",
}
EXPECTED_STATES = LEGACY_EXPECTED_STATES | {
    "hand-one",
    "hand-five",
    "hand-ten",
    "hand-hover",
    "field-readability",
}
EXPECTED_TOP_LEVEL_V3 = {
    "schema_version",
    "gate",
    "scenario",
    "asset_manifest_sha256",
    "viewport",
    "captures",
    "performance",
}
EXPECTED_TOP_LEVEL_V4 = EXPECTED_TOP_LEVEL_V3 | {"capture_contract"}
EXPECTED_CAPTURE_V3 = {
    "state",
    "viewer",
    "revision",
    "width",
    "height",
    "file",
    "sha256",
    "asset_manifest_sha256",
    "layout",
}
EXPECTED_CAPTURE_V4 = EXPECTED_CAPTURE_V3 | {
    "stable_frame_post_draws",
    "frame_pair_mae",
    "pixel_evidence",
}
EXPECTED_LAYOUT = {
    "controls_inside_viewport",
    "hud_regions_overlap_free",
    "opaque_full_height_panel_count",
    "glass_surface_count",
    "visible_debug_label_count",
    "battlefield_width_ratio",
    "battlefield_height_ratio",
}
BOARD_STATES = {
    "mulligan",
    "action",
    "source-selection",
    "slot-or-target-selection",
    "reaction",
    "resolving",
    "result",
    "hand-one",
    "hand-five",
    "hand-ten",
    "hand-hover",
    "field-readability",
}
EXPECTED_CAPTURE_CONTRACT = {
    "frame_post_draws",
    "pixel_space",
    "maximum_frame_pair_mae",
    "maximum_region_frame_pair_mae",
    "maximum_region_channel_delta",
}
EXPECTED_PIXEL_EVIDENCE = {"anchors", "regions"}
EXPECTED_PIXEL_ANCHOR = {"name", "x", "y", "r", "g", "b"}
EXPECTED_PIXEL_REGION = {
    "name",
    "x",
    "y",
    "width",
    "height",
    "sha256",
    "mean_luma",
    "edge_ratio",
    "frame_pair_mae",
    "max_channel_delta",
}
HAND_STATES = {"hand-one", "hand-five", "hand-ten", "hand-hover"}
EXPECTED_HAND_EVIDENCE = {
    "card_count",
    "hovered_count",
    "selected_count",
    "minimum_pixel_height",
    "maximum_abs_roll_degrees",
}
EXPECTED_HAND_COUNTS = {
    "hand-one": (1, 0, 0),
    "hand-five": (5, 0, 0),
    "hand-ten": (10, 0, 0),
    "hand-hover": (5, 1, 0),
}
FIELD_READABILITY_REGIONS = {
    "near_hand",
    "cost",
    "attack",
    "health",
    "countdown",
    "battlefield",
    "own_leader",
    "opponent_leader",
    "hud",
}
EXPECTED_PERFORMANCE = {
    "adapter_name",
    "adapter_type",
    "timing_budget_applicable",
    "warmup_frames",
    "measured_frames",
    "p95_frame_ms",
    "max_frame_ms",
    "actor_count_before",
    "actor_count_after",
    "material_count_before",
    "material_count_after",
    "texture_count_before",
    "texture_count_after",
}

SOFTWARE_ADAPTER_NAME_MARKERS = (
    "microsoft basic render driver",
    "llvmpipe",
    "swiftshader",
    "software renderer",
)


class VisualSuiteError(ValueError):
    """Raised when a visual-suite report is incomplete or inconsistent."""


def _png_dimensions(path: Path) -> tuple[int, int]:
    data = path.read_bytes()[:24]
    if len(data) != 24 or data[:8] != b"\x89PNG\r\n\x1a\n" or data[12:16] != b"IHDR":
        raise VisualSuiteError(f"capture is not a valid PNG: {path}")
    width, height = struct.unpack(">II", data[16:24])
    if width == 0 or height == 0:
        raise VisualSuiteError(f"capture has an invalid PNG size: {path}")
    return width, height


def _paeth(left: int, up: int, upper_left: int) -> int:
    estimate = left + up - upper_left
    left_distance = abs(estimate - left)
    up_distance = abs(estimate - up)
    diagonal_distance = abs(estimate - upper_left)
    if left_distance <= up_distance and left_distance <= diagonal_distance:
        return left
    return up if up_distance <= diagonal_distance else upper_left


def _read_png_rgb(path: Path) -> tuple[int, int, bytes]:
    """Decode the 8-bit non-interlaced PNG formats emitted by Godot."""
    data = path.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise VisualSuiteError(f"capture is not a valid PNG: {path}")
    offset = 8
    compressed = bytearray()
    width = height = bit_depth = color_type = interlace = -1
    while offset + 12 <= len(data):
        length = struct.unpack(">I", data[offset : offset + 4])[0]
        kind = data[offset + 4 : offset + 8]
        payload_end = offset + 8 + length
        if payload_end + 4 > len(data):
            raise VisualSuiteError(f"capture has a truncated PNG chunk: {path}")
        payload = data[offset + 8 : payload_end]
        if kind == b"IHDR":
            width, height, bit_depth, color_type, _, _, interlace = struct.unpack(
                ">IIBBBBB", payload
            )
        elif kind == b"IDAT":
            compressed.extend(payload)
        elif kind == b"IEND":
            break
        offset = payload_end + 4
    channels = {0: 1, 2: 3, 4: 2, 6: 4}.get(color_type)
    if (
        width <= 0
        or height <= 0
        or bit_depth != 8
        or channels is None
        or interlace != 0
    ):
        raise VisualSuiteError(
            f"capture must be a non-interlaced 8-bit gray/RGB/RGBA PNG: {path}"
        )
    try:
        encoded = zlib.decompress(bytes(compressed))
    except zlib.error as error:
        raise VisualSuiteError(f"capture has an invalid PNG stream: {path}") from error
    row_bytes = width * channels
    if len(encoded) != height * (row_bytes + 1):
        raise VisualSuiteError(f"capture has an invalid PNG scanline length: {path}")

    rgb = bytearray(width * height * 3)
    previous = bytearray(row_bytes)
    source = 0
    destination = 0
    for _ in range(height):
        filter_type = encoded[source]
        source += 1
        filtered = encoded[source : source + row_bytes]
        source += row_bytes
        if filter_type == 0:
            row = bytearray(filtered)
        else:
            row = bytearray(row_bytes)
            for index, value in enumerate(filtered):
                left = row[index - channels] if index >= channels else 0
                up = previous[index]
                upper_left = previous[index - channels] if index >= channels else 0
                predictor = {
                    1: left,
                    2: up,
                    3: (left + up) // 2,
                    4: _paeth(left, up, upper_left),
                }.get(filter_type)
                if predictor is None:
                    raise VisualSuiteError(
                        f"capture uses unsupported PNG filter {filter_type}: {path}"
                    )
                row[index] = (value + predictor) & 0xFF
        if color_type == 2:
            rgb[destination : destination + row_bytes] = row
            destination += row_bytes
            previous = row
            continue
        for x in range(width):
            at = x * channels
            if color_type in (0, 4):
                red = green = blue = row[at]
            else:
                red, green, blue = row[at], row[at + 1], row[at + 2]
            rgb[destination] = red
            rgb[destination + 1] = green
            rgb[destination + 2] = blue
            destination += 3
        previous = row
    return width, height, bytes(rgb)


def _validate_pixel_evidence(
    evidence: object,
    *,
    state: str,
    index: int,
    width: int,
    height: int,
    pixels: bytes,
    maximum_region_frame_pair_mae: float,
    maximum_region_channel_delta: int,
    enforce_structure: bool,
) -> dict[str, tuple[int, int, int, int, str]]:
    if not isinstance(evidence, dict) or set(evidence) != EXPECTED_PIXEL_EVIDENCE:
        raise VisualSuiteError(
            f"capture[{index}].pixel_evidence fields must be exactly "
            f"{sorted(EXPECTED_PIXEL_EVIDENCE)}"
        )
    anchors = evidence["anchors"]
    regions = evidence["regions"]
    if not isinstance(anchors, list) or not isinstance(regions, list):
        raise VisualSuiteError(
            f"capture[{index}].pixel_evidence anchors and regions must be arrays"
        )
    anchor_names: set[str] = set()
    for anchor_index, anchor in enumerate(anchors):
        if not isinstance(anchor, dict) or set(anchor) != EXPECTED_PIXEL_ANCHOR:
            raise VisualSuiteError(
                f"capture[{index}].pixel_evidence.anchors[{anchor_index}] fields "
                f"must be exactly {sorted(EXPECTED_PIXEL_ANCHOR)}"
            )
        name = anchor["name"]
        if not isinstance(name, str) or not name or name in anchor_names:
            raise VisualSuiteError(
                f"capture[{index}] has an invalid or duplicate pixel anchor name"
            )
        anchor_names.add(name)
        x = int(_number(anchor["x"], f"capture[{index}].anchor.x"))
        y = int(_number(anchor["y"], f"capture[{index}].anchor.y"))
        if not (0 <= x < width and 0 <= y < height):
            raise VisualSuiteError(f"capture[{index}] pixel anchor {name!r} is outside the PNG")
        at = (y * width + x) * 3
        expected_rgb = tuple(pixels[at : at + 3])
        actual_rgb = tuple(anchor[channel] for channel in ("r", "g", "b"))
        if any(isinstance(value, bool) or not isinstance(value, int) for value in actual_rgb):
            raise VisualSuiteError(f"capture[{index}] pixel anchor {name!r} RGB must be integers")
        if actual_rgb != expected_rgb:
            raise VisualSuiteError(
                f"capture[{index}] pixel anchor {name!r} disagrees with the PNG"
            )

    region_names: set[str] = set()
    measured_region_quality: dict[str, tuple[int, float]] = {}
    measured_regions: dict[str, tuple[int, int, int, int, str]] = {}
    for region_index, region in enumerate(regions):
        if not isinstance(region, dict) or set(region) != EXPECTED_PIXEL_REGION:
            raise VisualSuiteError(
                f"capture[{index}].pixel_evidence.regions[{region_index}] fields "
                f"must be exactly {sorted(EXPECTED_PIXEL_REGION)}"
            )
        name = region["name"]
        if not isinstance(name, str) or not name or name in region_names:
            raise VisualSuiteError(
                f"capture[{index}] has an invalid or duplicate pixel region name"
            )
        region_names.add(name)
        x = int(_number(region["x"], f"capture[{index}].region.x"))
        y = int(_number(region["y"], f"capture[{index}].region.y"))
        region_width = int(_number(
            region["width"], f"capture[{index}].region.width", minimum=1
        ))
        region_height = int(_number(
            region["height"], f"capture[{index}].region.height", minimum=1
        ))
        if x + region_width > width or y + region_height > height:
            raise VisualSuiteError(f"capture[{index}] pixel region {name!r} is outside the PNG")
        region_rgb = bytearray(region_width * region_height * 3)
        destination = 0
        luma = 0
        for pixel_y in range(y, y + region_height):
            start = (pixel_y * width + x) * 3
            row = pixels[start : start + region_width * 3]
            region_rgb[destination : destination + len(row)] = row
            destination += len(row)
            for pixel in range(0, len(row), 3):
                red, green, blue = row[pixel : pixel + 3]
                luma += (54 * red + 183 * green + 19 * blue + 128) >> 8
        expected_hash = hashlib.sha256(region_rgb).hexdigest()
        if region["sha256"] != expected_hash:
            raise VisualSuiteError(
                f"capture[{index}] pixel region {name!r} SHA-256 disagrees with the PNG"
            )
        pixel_count = region_width * region_height
        expected_luma = (luma + pixel_count // 2) // pixel_count
        if region["mean_luma"] != expected_luma:
            raise VisualSuiteError(
                f"capture[{index}] pixel region {name!r} mean_luma disagrees with the PNG"
            )
        edge_pixels = 0
        for pixel_y in range(region_height):
            for pixel_x in range(region_width):
                at = (pixel_y * region_width + pixel_x) * 3
                red, green, blue = region_rgb[at : at + 3]
                current_luma = (54 * red + 183 * green + 19 * blue + 128) >> 8
                edge = False
                if pixel_x > 0:
                    left = region_rgb[at - 3 : at]
                    left_luma = (54 * left[0] + 183 * left[1] + 19 * left[2] + 128) >> 8
                    edge = abs(current_luma - left_luma) >= 24
                if not edge and pixel_y > 0:
                    above_at = at - region_width * 3
                    above = region_rgb[above_at : above_at + 3]
                    above_luma = (
                        54 * above[0] + 183 * above[1] + 19 * above[2] + 128
                    ) >> 8
                    edge = abs(current_luma - above_luma) >= 24
                edge_pixels += int(edge)
        expected_edge_ratio = edge_pixels / pixel_count
        edge_ratio = _number(
            region["edge_ratio"], f"capture[{index}].region.edge_ratio"
        )
        if edge_ratio > 1.0 or abs(edge_ratio - expected_edge_ratio) > 1e-12:
            raise VisualSuiteError(
                f"capture[{index}] pixel region {name!r} edge_ratio disagrees with the PNG"
            )
        region_frame_pair_mae = _number(
            region["frame_pair_mae"],
            f"capture[{index}] pixel region {name!r} frame_pair_mae",
        )
        if region_frame_pair_mae > maximum_region_frame_pair_mae:
            raise VisualSuiteError(
                f"capture[{index}] pixel region {name!r} frame_pair_mae exceeds "
                "the capture contract"
            )
        max_channel_delta = region["max_channel_delta"]
        if isinstance(max_channel_delta, bool) or not isinstance(max_channel_delta, int):
            raise VisualSuiteError(
                f"capture[{index}] pixel region {name!r} max_channel_delta "
                "must be an integer"
            )
        if not 0 <= max_channel_delta <= maximum_region_channel_delta:
            raise VisualSuiteError(
                f"capture[{index}] pixel region {name!r} max_channel_delta exceeds "
                "the capture contract"
            )
        measured_region_quality[name] = (expected_luma, expected_edge_ratio)
        measured_regions[name] = (
            x,
            y,
            region_width,
            region_height,
            expected_hash,
        )

    if anchor_names != region_names or "viewport_center" not in region_names:
        raise VisualSuiteError(
            f"capture[{index}] pixel anchors must correspond one-to-one with regions "
            "and include viewport_center"
        )
    if state in BOARD_STATES and not {"battlefield", "hud"}.issubset(region_names):
        raise VisualSuiteError(
            f"capture[{index}] board state lacks battlefield or HUD pixel evidence"
        )
    if state in HAND_STATES and "near_hand" not in region_names:
        raise VisualSuiteError(f"capture[{index}] hand state lacks a near_hand pixel region")
    if state == "field-readability" and not FIELD_READABILITY_REGIONS.issubset(region_names):
        missing = sorted(FIELD_READABILITY_REGIONS - region_names)
        raise VisualSuiteError(
            f"capture[{index}] field-readability lacks pixel regions: {missing}"
        )
    if state == "field-readability":
        unreadable = [
            f"{name}(luma={measured_region_quality[name][0]},"
            f"edge={measured_region_quality[name][1]:.4f})"
            for name in ("cost", "attack", "health", "countdown")
            if measured_region_quality[name][0] < 18
            or measured_region_quality[name][1] < 0.008
        ]
        if unreadable:
            raise VisualSuiteError(
                f"capture[{index}] field badge GPU ROIs are blank: {unreadable}"
            )
        if enforce_structure:
            _validate_field_badge_structure(
                measured_regions,
                index=index,
                viewport_width=width,
                viewport_height=height,
            )
    return measured_regions


def _validate_field_badge_structure(
    regions: dict[str, tuple[int, int, int, int, str]],
    *,
    index: int,
    viewport_width: int,
    viewport_height: int,
) -> None:
    badge_names = ("cost", "attack", "health", "countdown")
    badges = {name: regions[name] for name in badge_names}
    for name, (_, _, region_width, region_height, _) in badges.items():
        minimum_width = 56 if name == "countdown" else 40
        if region_width < minimum_width or region_height < 40:
            raise VisualSuiteError(
                f"capture[{index}] field badge ROI {name!r} is too small: "
                f"{region_width}x{region_height}"
            )

    centers: dict[str, tuple[float, float]] = {}
    for name, (x, y, region_width, region_height, _) in badges.items():
        normalized_x = (x + region_width / 2.0) / viewport_width
        normalized_y = (y + region_height / 2.0) / viewport_height
        if not (0.20 <= normalized_x <= 0.80 and 0.25 <= normalized_y <= 0.75):
            raise VisualSuiteError(
                f"capture[{index}] field badge ROI {name!r} is outside the "
                "reasonable central viewport range"
            )
        centers[name] = (normalized_x, normalized_y)

    rectangles = {
        (x, y, region_width, region_height)
        for x, y, region_width, region_height, _ in badges.values()
    }
    if len(rectangles) != len(badge_names):
        raise VisualSuiteError(
            f"capture[{index}] field badge ROIs must use distinct rectangles"
        )

    for first_index, first_name in enumerate(badge_names):
        first_x, first_y, first_width, first_height, _ = badges[first_name]
        for second_name in badge_names[first_index + 1 :]:
            second_x, second_y, second_width, second_height, _ = badges[second_name]
            overlap_width = max(
                0,
                min(first_x + first_width, second_x + second_width)
                - max(first_x, second_x),
            )
            overlap_height = max(
                0,
                min(first_y + first_height, second_y + second_height)
                - max(first_y, second_y),
            )
            overlap_area = overlap_width * overlap_height
            smaller_area = min(first_width * first_height, second_width * second_height)
            if overlap_area / smaller_area > 0.20:
                raise VisualSuiteError(
                    f"capture[{index}] field badge ROIs {first_name!r} and "
                    f"{second_name!r} overlap by more than 20%"
                )

    if not (
        centers["cost"][1] < centers["attack"][1]
        and centers["cost"][1] < centers["health"][1]
    ):
        raise VisualSuiteError(
            f"capture[{index}] field cost badge ROI must be above attack and health"
        )
    if centers["attack"][0] >= centers["health"][0]:
        raise VisualSuiteError(
            f"capture[{index}] field attack badge ROI must be left of health"
        )
    if len({badge[4] for badge in badges.values()}) == 1:
        raise VisualSuiteError(
            f"capture[{index}] field badge ROIs reuse identical checker pixels"
        )


def _validate_hand_evidence(
    evidence: object,
    *,
    state: str,
    index: int,
    viewport_height: int,
) -> None:
    if not isinstance(evidence, dict) or set(evidence) != EXPECTED_HAND_EVIDENCE:
        raise VisualSuiteError(
            f"capture[{index}].hand_evidence fields must be exactly "
            f"{sorted(EXPECTED_HAND_EVIDENCE)}"
        )
    expected_counts = EXPECTED_HAND_COUNTS[state]
    for field, expected in zip(
        ("card_count", "hovered_count", "selected_count"),
        expected_counts,
    ):
        value = evidence[field]
        if isinstance(value, bool) or not isinstance(value, int) or value != expected:
            raise VisualSuiteError(
                f"capture[{index}].hand_evidence.{field} must be {expected} "
                f"for {state}"
            )
    minimum_pixel_height = _number(
        evidence["minimum_pixel_height"],
        f"capture[{index}].hand_evidence.minimum_pixel_height",
    )
    required_pixel_height = 170.0 if viewport_height >= 900 else 142.0
    if minimum_pixel_height < required_pixel_height:
        raise VisualSuiteError(
            f"capture[{index}] hand cards are too short: {minimum_pixel_height:.3f}px "
            f"< {required_pixel_height:.0f}px"
        )
    maximum_abs_roll = _number(
        evidence["maximum_abs_roll_degrees"],
        f"capture[{index}].hand_evidence.maximum_abs_roll_degrees",
    )
    if maximum_abs_roll > 8.0:
        raise VisualSuiteError(
            f"capture[{index}] hand card roll exceeds 8 degrees"
        )


def _number(value: object, name: str, *, minimum: float = 0.0) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise VisualSuiteError(f"{name} must be numeric")
    result = float(value)
    if not math.isfinite(result):
        raise VisualSuiteError(f"{name} must be finite")
    if result < minimum:
        raise VisualSuiteError(f"{name} must be >= {minimum}")
    return result


def validate(report_path: Path, *, expected_width: int | None = None,
             expected_height: int | None = None, enforce_budget: bool = True,
             asset_manifest_path: Path | None = None,
             enforce_structure: bool = True) -> dict[str, Any]:
    report_path = report_path.resolve()
    try:
        report = json.loads(report_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as error:
        raise VisualSuiteError(f"cannot read visual suite manifest: {error}") from error
    if not isinstance(report, dict):
        raise VisualSuiteError("report must be an object")
    schema_version = report.get("schema_version")
    if schema_version == 3:
        expected_top_level = EXPECTED_TOP_LEVEL_V3
        expected_captures = EXPECTED_CAPTURE_V3
        expected_states = LEGACY_EXPECTED_STATES
        expected_gate = "4B-R1"
    elif schema_version == 4:
        expected_top_level = EXPECTED_TOP_LEVEL_V4
        expected_captures = EXPECTED_CAPTURE_V4
        expected_states = EXPECTED_STATES
        expected_gate = "4B-R2"
    else:
        raise VisualSuiteError("report schema_version must be historical 3 or current 4")
    if set(report) != expected_top_level:
        raise VisualSuiteError(
            f"schema {schema_version} report fields must be exactly "
            f"{sorted(expected_top_level)}"
        )
    if report["gate"] != expected_gate or report["scenario"] != "visual-suite":
        raise VisualSuiteError(
            f"schema {schema_version} report must identify Gate {expected_gate} visual-suite"
        )
    if schema_version == 4:
        capture_contract = report["capture_contract"]
        if (
            not isinstance(capture_contract, dict)
            or set(capture_contract) != EXPECTED_CAPTURE_CONTRACT
            or isinstance(capture_contract.get("frame_post_draws"), bool)
            or not isinstance(capture_contract.get("frame_post_draws"), int)
            or capture_contract["frame_post_draws"] != 2
            or capture_contract["pixel_space"] != "srgb8"
            or capture_contract["maximum_frame_pair_mae"] != 0.01
            or capture_contract["maximum_region_frame_pair_mae"] != 0.01
            or isinstance(capture_contract.get("maximum_region_channel_delta"), bool)
            or not isinstance(capture_contract.get("maximum_region_channel_delta"), int)
            or capture_contract["maximum_region_channel_delta"] != 64
        ):
            raise VisualSuiteError(
                "schema 4 capture_contract must strictly require two FramePostDraws "
                "and srgb8 frame/region stability evidence"
            )
    asset_hash = report["asset_manifest_sha256"]
    if (
        not isinstance(asset_hash, str)
        or len(asset_hash) != 64
        or any(character not in "0123456789abcdef" for character in asset_hash)
    ):
        raise VisualSuiteError("asset_manifest_sha256 must be a lowercase SHA-256")
    if asset_manifest_path is not None:
        try:
            committed_asset_hash = hashlib.sha256(asset_manifest_path.read_bytes()).hexdigest()
        except OSError as error:
            raise VisualSuiteError(f"cannot read committed asset manifest: {error}") from error
        if asset_hash != committed_asset_hash:
            raise VisualSuiteError(
                "visual suite used a different asset manifest than the current checkout"
            )

    viewport = report["viewport"]
    if not isinstance(viewport, dict) or set(viewport) != {"width", "height"}:
        raise VisualSuiteError("viewport must contain only width and height")
    width = int(_number(viewport["width"], "viewport.width", minimum=1))
    height = int(_number(viewport["height"], "viewport.height", minimum=1))
    if expected_width is not None and width != expected_width:
        raise VisualSuiteError(f"expected viewport width {expected_width}, got {width}")
    if expected_height is not None and height != expected_height:
        raise VisualSuiteError(f"expected viewport height {expected_height}, got {height}")
    ratio = width / height
    if ratio < 1.59 or ratio > 1.79:
        raise VisualSuiteError(f"viewport must be 16:10 through 16:9, got {width}x{height}")

    captures = report["captures"]
    if not isinstance(captures, list):
        raise VisualSuiteError("captures must be an array")
    states: set[str] = set()
    screenshot_hashes: set[str] = set()
    hand_screenshot_hashes: dict[str, str] = {}
    hand_region_hashes: dict[str, str] = {}
    suite_root = report_path.parent
    for index, capture in enumerate(captures):
        if not isinstance(capture, dict):
            raise VisualSuiteError(f"capture[{index}] must be an object")
        state = capture.get("state")
        if not isinstance(state, str) or state not in expected_states or state in states:
            raise VisualSuiteError(f"capture[{index}] has an invalid or duplicate state: {state!r}")
        expected_capture_fields = expected_captures
        if schema_version == 4 and state in HAND_STATES:
            expected_capture_fields = expected_captures | {"hand_evidence"}
        if set(capture) != expected_capture_fields:
            raise VisualSuiteError(
                f"capture[{index}] fields must be exactly {sorted(expected_capture_fields)}"
            )
        states.add(state)
        if schema_version == 4 and state in HAND_STATES:
            _validate_hand_evidence(
                capture["hand_evidence"],
                state=state,
                index=index,
                viewport_height=height,
            )
        viewer = capture["viewer"]
        if viewer is not None and viewer not in (0, 1):
            raise VisualSuiteError(f"capture[{index}].viewer must be null, 0, or 1")
        revision = capture["revision"]
        if revision is not None and (isinstance(revision, bool) or not isinstance(revision, int) or revision < 0):
            raise VisualSuiteError(f"capture[{index}].revision must be null or a non-negative integer")
        if capture["asset_manifest_sha256"] != asset_hash:
            raise VisualSuiteError(f"capture[{index}] used a different asset manifest")
        layout = capture["layout"]
        if not isinstance(layout, dict) or set(layout) != EXPECTED_LAYOUT:
            raise VisualSuiteError(
                f"capture[{index}].layout fields must be exactly {sorted(EXPECTED_LAYOUT)}"
            )
        if layout["controls_inside_viewport"] is not True:
            raise VisualSuiteError(f"capture[{index}] has controls outside the viewport")
        if layout["hud_regions_overlap_free"] is not True:
            raise VisualSuiteError(f"capture[{index}] has overlapping HUD regions")
        opaque_panels = int(_number(
            layout["opaque_full_height_panel_count"],
            f"capture[{index}].layout.opaque_full_height_panel_count",
        ))
        if opaque_panels != 0:
            raise VisualSuiteError(
                f"capture[{index}] contains {opaque_panels} full-height opaque dark panels"
            )
        glass_surfaces = int(_number(
            layout["glass_surface_count"],
            f"capture[{index}].layout.glass_surface_count",
        ))
        if glass_surfaces < 1:
            raise VisualSuiteError(f"capture[{index}] does not render a glass HUD surface")
        debug_labels = int(_number(
            layout["visible_debug_label_count"],
            f"capture[{index}].layout.visible_debug_label_count",
        ))
        if debug_labels != 0:
            raise VisualSuiteError(
                f"capture[{index}] exposes {debug_labels} normal-mode debug labels"
            )
        battlefield_width = _number(
            layout["battlefield_width_ratio"],
            f"capture[{index}].layout.battlefield_width_ratio",
        )
        battlefield_height = _number(
            layout["battlefield_height_ratio"],
            f"capture[{index}].layout.battlefield_height_ratio",
        )
        width_minimum = 0.92 if schema_version == 4 else 0.68
        height_minimum = 0.78 if schema_version == 4 else 0.72
        if state in BOARD_STATES and (
            battlefield_width < width_minimum or battlefield_height < height_minimum
        ):
            raise VisualSuiteError(
                f"capture[{index}] battlefield coverage is too small: "
                f"{battlefield_width:.3f}x{battlefield_height:.3f}"
            )
        relative = capture["file"]
        if not isinstance(relative, str) or Path(relative).is_absolute() or ".." in Path(relative).parts:
            raise VisualSuiteError(f"capture[{index}].file must be a safe relative path")
        path = (suite_root / relative).resolve()
        try:
            path.relative_to(suite_root)
        except ValueError as error:
            raise VisualSuiteError(f"capture[{index}] escapes the suite directory") from error
        if not path.is_file():
            raise VisualSuiteError(f"capture[{index}] is missing: {relative}")
        if schema_version == 4:
            png_width, png_height, pixels = _read_png_rgb(path)
        else:
            png_width, png_height = _png_dimensions(path)
            pixels = b""
        if (capture["width"], capture["height"]) != (png_width, png_height):
            raise VisualSuiteError(f"capture[{index}] PNG dimensions disagree with metadata")
        if (png_width, png_height) != (width, height):
            raise VisualSuiteError(f"capture[{index}] does not fill the configured viewport")
        actual_hash = hashlib.sha256(path.read_bytes()).hexdigest()
        if capture["sha256"] != actual_hash:
            raise VisualSuiteError(f"capture[{index}] SHA-256 mismatch")
        if schema_version == 4:
            if capture["stable_frame_post_draws"] != 2:
                raise VisualSuiteError(
                    f"capture[{index}] must follow two consecutive stable FramePostDraws"
                )
            frame_pair_mae = _number(
                capture["frame_pair_mae"], f"capture[{index}].frame_pair_mae"
            )
            if frame_pair_mae > report["capture_contract"]["maximum_frame_pair_mae"]:
                raise VisualSuiteError(
                    f"capture[{index}] consecutive FramePostDraws are not stable"
                )
            measured_regions = _validate_pixel_evidence(
                capture["pixel_evidence"],
                state=state,
                index=index,
                width=width,
                height=height,
                pixels=pixels,
                maximum_region_frame_pair_mae=report["capture_contract"][
                    "maximum_region_frame_pair_mae"
                ],
                maximum_region_channel_delta=report["capture_contract"][
                    "maximum_region_channel_delta"
                ],
                enforce_structure=enforce_structure,
            )
            if state in HAND_STATES:
                hand_screenshot_hashes[state] = actual_hash
                hand_region_hashes[state] = measured_regions["near_hand"][4]
        screenshot_hashes.add(actual_hash)
        if enforce_structure and path.stat().st_size < 16 * 1024:
            raise VisualSuiteError(
                f"capture[{index}] is implausibly sparse/blank ({path.stat().st_size} bytes)"
            )

    missing = expected_states - states
    if missing:
        raise VisualSuiteError(f"visual suite is missing states: {sorted(missing)}")
    if schema_version == 4:
        if len(set(hand_screenshot_hashes.values())) != len(HAND_STATES):
            raise VisualSuiteError(
                "the four hand captures must use distinct PNG SHA-256 values"
            )
        if len(set(hand_region_hashes.values())) != len(HAND_STATES):
            raise VisualSuiteError(
                "the four hand captures must use distinct near_hand ROI SHA-256 values"
            )
    minimum_distinct = 12 if schema_version == 4 else 8
    if enforce_structure and len(screenshot_hashes) < minimum_distinct:
        raise VisualSuiteError(
            "visual suite did not render enough structurally distinct UI states"
        )

    performance = report["performance"]
    if not isinstance(performance, dict) or set(performance) != EXPECTED_PERFORMANCE:
        raise VisualSuiteError(
            f"performance fields must be exactly {sorted(EXPECTED_PERFORMANCE)}"
        )
    adapter_name = performance["adapter_name"]
    adapter_type = performance["adapter_type"]
    timing_budget_applicable = performance["timing_budget_applicable"]
    if not isinstance(adapter_name, str) or not adapter_name.strip():
        raise VisualSuiteError("performance.adapter_name must be a non-empty string")
    if not isinstance(adapter_type, str) or not adapter_type.strip():
        raise VisualSuiteError("performance.adapter_type must be a non-empty string")
    if not isinstance(timing_budget_applicable, bool):
        raise VisualSuiteError("performance.timing_budget_applicable must be boolean")
    software_adapter = (
        adapter_type.strip().casefold() == "cpu"
        or any(
            marker in adapter_name.casefold()
            for marker in SOFTWARE_ADAPTER_NAME_MARKERS
        )
    )
    if not timing_budget_applicable and not software_adapter:
        raise VisualSuiteError(
            "performance.timing_budget_applicable may be false only for a CPU "
            "or recognized software renderer"
        )
    if performance["warmup_frames"] != 300 or performance["measured_frames"] != 300:
        raise VisualSuiteError("performance smoke must contain 300 warmup and 300 measured frames")
    for resource in ("actor", "material", "texture"):
        before = int(_number(performance[f"{resource}_count_before"], f"{resource}_count_before"))
        after = int(_number(performance[f"{resource}_count_after"], f"{resource}_count_after"))
        if before != after:
            raise VisualSuiteError(f"{resource} count grew after warmup: {before} -> {after}")
    p95 = _number(performance["p95_frame_ms"], "p95_frame_ms")
    maximum = _number(performance["max_frame_ms"], "max_frame_ms")
    if maximum < p95:
        raise VisualSuiteError("max_frame_ms cannot be lower than p95_frame_ms")
    if enforce_budget and timing_budget_applicable and (p95 > 33.3 or maximum >= 100.0):
        raise VisualSuiteError(
            f"frame budget exceeded: p95={p95:.3f}ms, max={maximum:.3f}ms"
        )
    return report


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--width", type=int)
    parser.add_argument("--height", type=int)
    parser.add_argument("--asset-manifest", type=Path)
    parser.add_argument("--skip-performance-budget", action="store_true")
    parser.add_argument("--skip-structure", action="store_true")
    args = parser.parse_args()
    try:
        validate(
            args.report,
            expected_width=args.width,
            expected_height=args.height,
            enforce_budget=not args.skip_performance_budget,
            asset_manifest_path=args.asset_manifest,
            enforce_structure=not args.skip_structure,
        )
    except VisualSuiteError as error:
        print(f"Gate 4B visual-suite validation failed: {error}", file=sys.stderr)
        return 1
    print(f"validated Gate 4B visual suite: {args.report.resolve()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
