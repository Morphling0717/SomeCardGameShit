#!/usr/bin/env python3
"""Validate the standalone Gate 6A AnimeV1 visual-direction report."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import struct
import zlib
from pathlib import Path
from typing import Any


STATES = (
    "menu",
    "setup",
    "action",
    "hand-hover",
    "mixed-permanents-field",
    "reaction",
    "covered",
    "result",
)
BATTLE_STATES = {"action", "hand-hover", "mixed-permanents-field", "reaction"}
CARD_KINDS = ("Follower", "Spell", "Amulet", "Trap", "Field")
HAND_KINDS = ("Amulet", "Trap", "Follower", "Spell", "Amulet")
TYPE_MARKER_GLYPHS = {
    "Follower": "随",
    "Spell": "法",
    "Amulet": "护",
    "Trap": "伏",
    "Field": "场",
}
TYPE_MARKER_SHAPES = {
    "Follower": "shield",
    "Spell": "star",
    "Amulet": "ring",
    "Trap": "inverted_triangle",
    "Field": "gate",
}
BADGE_ROLES = ("cost", "attack", "health", "countdown")
BADGES_BY_KIND = {
    "Follower": {"cost", "attack", "health"},
    "Spell": {"cost"},
    "Amulet": {"cost", "countdown"},
    "Trap": {"cost"},
    "Field": {"cost"},
}
VIEWPORTS = {(1280, 720), (1600, 900), (2560, 1440), (2560, 1600)}
CI_RUNNER_VIEWPORTS = {(1024, 684)}
ROOT = "res://assets/visual/anime_v1/slice"
REQUIRED_ASSETS = tuple(
    sorted(
        (
            f"{ROOT}/leaders/aurelia-master.png",
            f"{ROOT}/leaders/theraea-master.png",
            f"{ROOT}/shared/card-back.png",
            f"{ROOT}/menu/menu-key-art.png",
            f"{ROOT}/arena/open-fantasy-arena.png",
            f"{ROOT}/cards/LO-03.png",
            f"{ROOT}/cards/LO-07.png",
            f"{ROOT}/cards/LO-11.png",
            f"{ROOT}/cards/LO-11-evolved.png",
            f"{ROOT}/cards/AP-03.png",
            f"{ROOT}/cards/AP-05.png",
            f"{ROOT}/cards/AP-11.png",
            f"{ROOT}/cards/AP-11-evolved.png",
            f"{ROOT}/cards/NT-04.png",
        )
    )
)


class AnimeVisualSliceError(ValueError):
    """The report or one of its screenshots violates the Gate 6A contract."""


def _mapping(value: Any, label: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise AnimeVisualSliceError(f"{label} must be an object")
    return value


def _list(value: Any, label: str) -> list[Any]:
    if not isinstance(value, list):
        raise AnimeVisualSliceError(f"{label} must be an array")
    return value


def _int(value: Any, label: str) -> int:
    if isinstance(value, bool) or not isinstance(value, int):
        raise AnimeVisualSliceError(f"{label} must be an integer")
    return value


def _number(value: Any, label: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise AnimeVisualSliceError(f"{label} must be numeric")
    return float(value)


def _png_dimensions(path: Path) -> tuple[int, int]:
    payload = path.read_bytes()
    if len(payload) < 24 or payload[:8] != b"\x89PNG\r\n\x1a\n" or payload[12:16] != b"IHDR":
        raise AnimeVisualSliceError(f"{path.name} is not a canonical PNG")
    return struct.unpack(">II", payload[16:24])


def _png_rgba(path: Path) -> tuple[int, int, bytes]:
    """Decode the RGB/RGBA subset emitted by Godot's PNG writer.

    Keeping this decoder in the validator makes ROI evidence independently
    reproducible in CI instead of trusting booleans written by the producer.
    """

    payload = path.read_bytes()
    if len(payload) < 33 or payload[:8] != b"\x89PNG\r\n\x1a\n":
        raise AnimeVisualSliceError(f"{path.name} is not a canonical PNG")
    offset = 8
    width = height = bit_depth = color_type = interlace = None
    compressed = bytearray()
    while offset + 12 <= len(payload):
        length = struct.unpack(">I", payload[offset : offset + 4])[0]
        kind = payload[offset + 4 : offset + 8]
        data_start = offset + 8
        data_end = data_start + length
        if data_end + 4 > len(payload):
            raise AnimeVisualSliceError(f"{path.name} has a truncated PNG chunk")
        data = payload[data_start:data_end]
        if kind == b"IHDR":
            width, height, bit_depth, color_type, _, _, interlace = struct.unpack(
                ">IIBBBBB", data
            )
        elif kind == b"IDAT":
            compressed.extend(data)
        elif kind == b"IEND":
            break
        offset = data_end + 4
    if (
        width is None
        or height is None
        or bit_depth != 8
        or color_type not in (2, 6)
        or interlace != 0
    ):
        raise AnimeVisualSliceError(
            f"{path.name} must be a non-interlaced 8-bit RGB/RGBA PNG"
        )
    channels = 3 if color_type == 2 else 4
    stride = width * channels
    try:
        raw = zlib.decompress(bytes(compressed))
    except zlib.error as error:
        raise AnimeVisualSliceError(f"{path.name} has invalid PNG image data") from error
    if len(raw) != height * (stride + 1):
        raise AnimeVisualSliceError(f"{path.name} has an unexpected PNG payload size")

    decoded = bytearray(height * stride)
    previous = bytearray(stride)
    cursor = 0
    for row_index in range(height):
        filter_kind = raw[cursor]
        cursor += 1
        filtered = raw[cursor : cursor + stride]
        cursor += stride
        row = bytearray(stride)
        for index, value in enumerate(filtered):
            left = row[index - channels] if index >= channels else 0
            above = previous[index]
            upper_left = previous[index - channels] if index >= channels else 0
            if filter_kind == 0:
                predictor = 0
            elif filter_kind == 1:
                predictor = left
            elif filter_kind == 2:
                predictor = above
            elif filter_kind == 3:
                predictor = (left + above) // 2
            elif filter_kind == 4:
                estimate = left + above - upper_left
                left_distance = abs(estimate - left)
                above_distance = abs(estimate - above)
                upper_left_distance = abs(estimate - upper_left)
                predictor = (
                    left
                    if left_distance <= above_distance and left_distance <= upper_left_distance
                    else above if above_distance <= upper_left_distance else upper_left
                )
            else:
                raise AnimeVisualSliceError(
                    f"{path.name} uses unsupported PNG filter {filter_kind}"
                )
            row[index] = (value + predictor) & 0xFF
        decoded[row_index * stride : (row_index + 1) * stride] = row
        previous = row

    if channels == 4:
        return width, height, bytes(decoded)
    rgba = bytearray(width * height * 4)
    for source in range(0, len(decoded), 3):
        target = (source // 3) * 4
        rgba[target : target + 3] = decoded[source : source + 3]
        rgba[target + 3] = 255
    return width, height, bytes(rgba)


def _rect(value: Any, label: str) -> dict[str, float]:
    rect = _mapping(value, label)
    required = {"x", "y", "width", "height"}
    if set(rect) != required:
        raise AnimeVisualSliceError(f"{label} must contain exactly {sorted(required)}")
    return {key: _number(rect[key], f"{label}.{key}") for key in required}


def _exact_keys(value: dict[str, Any], expected: set[str], label: str) -> None:
    if set(value) != expected:
        raise AnimeVisualSliceError(f"{label} must contain exactly {sorted(expected)}")


def _contains(container: dict[str, float], child: dict[str, float]) -> bool:
    tolerance = 0.25
    return (
        child["width"] > 0
        and child["height"] > 0
        and child["x"] >= container["x"] - tolerance
        and child["y"] >= container["y"] - tolerance
        and child["x"] + child["width"]
        <= container["x"] + container["width"] + tolerance
        and child["y"] + child["height"]
        <= container["y"] + container["height"] + tolerance
    )


def _physical_roi(
    rect: dict[str, float],
    logical_viewport: dict[str, float],
    physical_viewport: tuple[int, int],
) -> tuple[int, int, int, int]:
    logical_width = logical_viewport["width"]
    logical_height = logical_viewport["height"]
    if logical_width <= 0 or logical_height <= 0:
        raise AnimeVisualSliceError("logical viewport must have positive dimensions")
    scale_x = physical_viewport[0] / logical_width
    scale_y = physical_viewport[1] / logical_height
    x0 = math.floor((rect["x"] - logical_viewport["x"]) * scale_x)
    y0 = math.floor((rect["y"] - logical_viewport["y"]) * scale_y)
    x1 = math.ceil(
        (rect["x"] + rect["width"] - logical_viewport["x"]) * scale_x
    )
    y1 = math.ceil(
        (rect["y"] + rect["height"] - logical_viewport["y"]) * scale_y
    )
    return x0, y0, x1 - x0, y1 - y0


def _pixel_evidence(
    rgba: bytes,
    physical_viewport: tuple[int, int],
    logical_viewport: dict[str, float],
    rect: dict[str, float],
) -> dict[str, Any]:
    x, y, width, height = _physical_roi(rect, logical_viewport, physical_viewport)
    image_width, image_height = physical_viewport
    if x < 0 or y < 0 or width < 1 or height < 1 or x + width > image_width or y + height > image_height:
        raise AnimeVisualSliceError("ROI escapes the physical screenshot")
    cropped = bytearray(width * height * 4)
    luminance: list[int] = []
    quantized: set[int] = set()
    target = 0
    for row in range(y, y + height):
        source = ((row * image_width) + x) * 4
        row_payload = rgba[source : source + (width * 4)]
        cropped[target : target + len(row_payload)] = row_payload
        target += len(row_payload)
        for offset in range(0, len(row_payload), 4):
            red, green, blue, alpha = row_payload[offset : offset + 4]
            luminance.append((54 * red + 183 * green + 19 * blue + 128) >> 8)
            quantized.add(
                ((red >> 4) << 12)
                | ((green >> 4) << 8)
                | ((blue >> 4) << 4)
                | (alpha >> 4)
            )
    edge_count = 0
    for row in range(height):
        for column in range(width):
            index = (row * width) + column
            if column + 1 < width and abs(luminance[index] - luminance[index + 1]) >= 24:
                edge_count += 1
            if row + 1 < height and abs(luminance[index] - luminance[index + width]) >= 24:
                edge_count += 1
    minimum = min(luminance)
    maximum = max(luminance)
    return {
        "physical_x": x,
        "physical_y": y,
        "physical_width": width,
        "physical_height": height,
        "sample_count": width * height,
        "quantized_color_count": len(quantized),
        "luminance_min_8": minimum,
        "luminance_max_8": maximum,
        "luminance_range_8": maximum - minimum,
        "grayscale_edge_count": edge_count,
        "pixel_sha256": hashlib.sha256(cropped).hexdigest(),
    }


def _validate_pixel_evidence(
    raw: Any,
    *,
    label: str,
    rgba: bytes,
    physical_viewport: tuple[int, int],
    logical_viewport: dict[str, float],
    roi: dict[str, float],
) -> None:
    evidence = _mapping(raw, label)
    expected = _pixel_evidence(rgba, physical_viewport, logical_viewport, roi)
    _exact_keys(evidence, set(expected), label)
    if evidence != expected:
        raise AnimeVisualSliceError(f"{label} does not match the screenshot ROI")
    if expected["quantized_color_count"] < 4 or expected["luminance_range_8"] < 28:
        raise AnimeVisualSliceError(f"{label} lacks grayscale-readable contrast")
    if expected["grayscale_edge_count"] < max(8, expected["sample_count"] // 80):
        raise AnimeVisualSliceError(f"{label} lacks non-color edge evidence")


def _validate_readability(
    raw: Any,
    *,
    state: str,
    logical_viewport: dict[str, float],
    physical_viewport: tuple[int, int],
    rgba: bytes,
) -> None:
    readability = _mapping(raw, f"capture {state}.readability_evidence")
    _exact_keys(
        readability,
        {"safe_area", "hand_cards", "type_markers"},
        f"capture {state}.readability_evidence",
    )
    safe_area = _rect(
        readability.get("safe_area"), f"capture {state}.readability_evidence.safe_area"
    )
    expected_safe_area = {
        "x": logical_viewport["x"] + 4.0,
        "y": logical_viewport["y"] + 4.0,
        "width": logical_viewport["width"] - 8.0,
        "height": logical_viewport["height"] - 8.0,
    }
    if any(abs(safe_area[key] - expected_safe_area[key]) > 0.01 for key in expected_safe_area):
        raise AnimeVisualSliceError(f"capture {state} uses the wrong readability safe area")

    hand_cards = _list(
        readability.get("hand_cards"), f"capture {state}.readability_evidence.hand_cards"
    )
    if state not in BATTLE_STATES:
        if hand_cards:
            raise AnimeVisualSliceError(f"capture {state} must not claim hand-card evidence")
    else:
        if len(hand_cards) != len(HAND_KINDS):
            raise AnimeVisualSliceError(f"capture {state} must expose all five real hand cards")
        for index, (raw_card, expected_kind) in enumerate(zip(hand_cards, HAND_KINDS)):
            label = f"capture {state}.readability_evidence.hand_cards[{index}]"
            card = _mapping(raw_card, label)
            _exact_keys(
                card,
                {
                    "node_name",
                    "design_id",
                    "kind",
                    "card_rect",
                    "card_inside_safe_area",
                    "badge_font_pixel_size",
                    "badges",
                },
                label,
            )
            if card.get("node_name") != f"NearHand{index}" or card.get("kind") != expected_kind:
                raise AnimeVisualSliceError(f"{label} is not the canonical real hand card")
            expected_design = ("LO-03", "LO-07", "LO-11", "NT-04", "LO-03")[index]
            if card.get("design_id") != expected_design:
                raise AnimeVisualSliceError(f"{label} has the wrong design identity")
            card_rect = _rect(card.get("card_rect"), f"{label}.card_rect")
            inside = _contains(safe_area, card_rect)
            if card.get("card_inside_safe_area") is not inside or not inside:
                raise AnimeVisualSliceError(f"{label} escapes the readability safe area")
            if _number(card.get("badge_font_pixel_size"), f"{label}.badge_font_pixel_size") < 16.0:
                raise AnimeVisualSliceError(f"{label} badge font is smaller than 16 logical pixels")
            badges = _list(card.get("badges"), f"{label}.badges")
            if len(badges) != len(BADGE_ROLES):
                raise AnimeVisualSliceError(f"{label} must report all four badge roles")
            for badge_index, (raw_badge, role) in enumerate(zip(badges, BADGE_ROLES)):
                badge_label = f"{label}.badges[{badge_index}]"
                badge = _mapping(raw_badge, badge_label)
                _exact_keys(
                    badge,
                    {"role", "present", "roi", "inside_safe_area", "pixels"},
                    badge_label,
                )
                present = role in BADGES_BY_KIND[expected_kind]
                if badge.get("role") != role or badge.get("present") is not present:
                    raise AnimeVisualSliceError(f"{badge_label} has the wrong presence contract")
                if not present:
                    if badge.get("roi") is not None or badge.get("pixels") is not None:
                        raise AnimeVisualSliceError(f"{badge_label} invents evidence for an absent badge")
                    if badge.get("inside_safe_area") is not False:
                        raise AnimeVisualSliceError(f"{badge_label} absent badge cannot be safe")
                    continue
                roi = _rect(badge.get("roi"), f"{badge_label}.roi")
                roi_inside = _contains(safe_area, roi) and _contains(card_rect, roi)
                if badge.get("inside_safe_area") is not roi_inside or not roi_inside:
                    raise AnimeVisualSliceError(f"{badge_label} ROI escapes its card or safe area")
                _validate_pixel_evidence(
                    badge.get("pixels"),
                    label=f"{badge_label}.pixels",
                    rgba=rgba,
                    physical_viewport=physical_viewport,
                    logical_viewport=logical_viewport,
                    roi=roi,
                )

    markers = _list(
        readability.get("type_markers"),
        f"capture {state}.readability_evidence.type_markers",
    )
    if state != "mixed-permanents-field":
        if markers:
            raise AnimeVisualSliceError(f"capture {state} must not claim five-kind marker evidence")
        return
    if len(markers) != len(CARD_KINDS):
        raise AnimeVisualSliceError("mixed state must report exactly five type-marker ROIs")
    if [marker.get("kind") for marker in markers if isinstance(marker, dict)] != list(CARD_KINDS):
        raise AnimeVisualSliceError("mixed type-marker evidence must use canonical kind order")
    seen_nodes: set[str] = set()
    expected_designs = {
        "Follower": "LO-11",
        "Spell": "NT-04",
        "Amulet": "LO-03",
        "Trap": "LO-07",
        "Field": "AP-05",
    }
    for index, (raw_marker, kind) in enumerate(zip(markers, CARD_KINDS)):
        label = f"capture {state}.readability_evidence.type_markers[{index}]"
        marker = _mapping(raw_marker, label)
        _exact_keys(
            marker,
            {
                "kind",
                "node_name",
                "design_id",
                "glyph",
                "shape",
                "card_rect",
                "roi",
                "inside_safe_area",
                "pixels",
            },
            label,
        )
        node_name = marker.get("node_name")
        if not isinstance(node_name, str) or not node_name or node_name in seen_nodes:
            raise AnimeVisualSliceError(f"{label} must identify one unique real card node")
        seen_nodes.add(node_name)
        if marker.get("design_id") != expected_designs[kind]:
            raise AnimeVisualSliceError(f"{label} has the wrong representative card")
        if (
            marker.get("glyph") != TYPE_MARKER_GLYPHS[kind]
            or marker.get("shape") != TYPE_MARKER_SHAPES[kind]
        ):
            raise AnimeVisualSliceError(f"{label} lacks the frozen non-color kind marker")
        card_rect = _rect(marker.get("card_rect"), f"{label}.card_rect")
        roi = _rect(marker.get("roi"), f"{label}.roi")
        if roi["width"] < 24.0 or roi["height"] < 24.0:
            raise AnimeVisualSliceError(f"{label} marker is smaller than 24 logical pixels")
        roi_inside = _contains(safe_area, roi) and _contains(card_rect, roi)
        if marker.get("inside_safe_area") is not roi_inside or not roi_inside:
            raise AnimeVisualSliceError(f"{label} ROI escapes its card or safe area")
        _validate_pixel_evidence(
            marker.get("pixels"),
            label=f"{label}.pixels",
            rgba=rgba,
            physical_viewport=physical_viewport,
            logical_viewport=logical_viewport,
            roi=roi,
        )


def validate_report(
    report_path: Path,
    expected_viewport: tuple[int, int] | None = None,
    *,
    allow_missing_assets: bool = False,
    allow_ci_runner_viewport: bool = False,
) -> dict[str, Any]:
    report_path = report_path.resolve()
    report = _mapping(json.loads(report_path.read_text(encoding="utf-8")), "report")
    if report.get("schema_version") != 2 or report.get("gate") != "6A":
        raise AnimeVisualSliceError("report must identify Gate 6A schema 2")
    if report.get("scenario") != "anime-style-slice":
        raise AnimeVisualSliceError("scenario must be anime-style-slice")
    if report.get("visual_profile") != "anime-v1-proposal":
        raise AnimeVisualSliceError("visual_profile must be anime-v1-proposal")
    if report.get("approval_status") != "pending_user_approval":
        raise AnimeVisualSliceError("the slice cannot self-approve its art direction")
    if report.get("uses_native_session") is not False:
        raise AnimeVisualSliceError("the Gate 6A slice must not create a native session")
    if report.get("default_product_path_unchanged") is not True:
        raise AnimeVisualSliceError("the unapproved slice must not become the product default")

    viewport = _mapping(report.get("viewport"), "viewport")
    physical_viewport = (
        _int(viewport.get("width"), "viewport.width"),
        _int(viewport.get("height"), "viewport.height"),
    )
    allowed_viewports = VIEWPORTS | (CI_RUNNER_VIEWPORTS if allow_ci_runner_viewport else set())
    if physical_viewport not in allowed_viewports:
        raise AnimeVisualSliceError(f"unsupported viewport {physical_viewport}")
    if expected_viewport is not None and physical_viewport != expected_viewport:
        raise AnimeVisualSliceError(
            f"viewport {physical_viewport} does not match expected {expected_viewport}"
        )

    assets = _mapping(report.get("asset_contract"), "asset_contract")
    required = tuple(_list(assets.get("required_paths"), "asset_contract.required_paths"))
    loaded = tuple(_list(assets.get("loaded_paths"), "asset_contract.loaded_paths"))
    missing = tuple(_list(assets.get("missing_paths"), "asset_contract.missing_paths"))
    if required != REQUIRED_ASSETS:
        raise AnimeVisualSliceError("asset contract paths are not the frozen 14-item Gate 6A set")
    if len(set(loaded)) != len(loaded) or len(set(missing)) != len(missing):
        raise AnimeVisualSliceError("asset loaded/missing lists contain duplicates")
    if set(loaded) & set(missing) or set(loaded) | set(missing) != set(required):
        raise AnimeVisualSliceError("loaded and missing assets must partition required assets")
    complete = assets.get("complete")
    if complete is not (len(missing) == 0):
        raise AnimeVisualSliceError("asset_contract.complete disagrees with missing_paths")
    if not allow_missing_assets and missing:
        raise AnimeVisualSliceError(f"approved capture is missing {len(missing)} visual assets")

    captures = _list(report.get("captures"), "captures")
    if [capture.get("state") for capture in captures if isinstance(capture, dict)] != list(STATES):
        raise AnimeVisualSliceError("captures must contain the eight states in canonical order")
    seen_files: set[str] = set()
    decoded_by_sha256: dict[str, tuple[int, int, bytes]] = {}
    for index, raw_capture in enumerate(captures):
        capture = _mapping(raw_capture, f"captures[{index}]")
        state = capture.get("state")
        filename = capture.get("file")
        if not isinstance(filename, str) or Path(filename).name != filename or filename in seen_files:
            raise AnimeVisualSliceError(f"capture {state} has an unsafe or duplicate filename")
        seen_files.add(filename)
        screenshot = report_path.parent / filename
        if not screenshot.is_file():
            raise AnimeVisualSliceError(f"capture {state} screenshot is missing")
        width = _int(capture.get("width"), f"capture {state}.width")
        height = _int(capture.get("height"), f"capture {state}.height")
        if (width, height) != physical_viewport or _png_dimensions(screenshot) != physical_viewport:
            raise AnimeVisualSliceError(f"capture {state} dimensions do not match viewport")
        digest = hashlib.sha256(screenshot.read_bytes()).hexdigest()
        if capture.get("sha256") != digest:
            raise AnimeVisualSliceError(f"capture {state} SHA-256 mismatch")
        rgba = b""
        if state in BATTLE_STATES:
            decoded = decoded_by_sha256.get(digest)
            if decoded is None:
                decoded = _png_rgba(screenshot)
                decoded_by_sha256[digest] = decoded
            decoded_width, decoded_height, rgba = decoded
            if (decoded_width, decoded_height) != physical_viewport:
                raise AnimeVisualSliceError(f"capture {state} decoded dimensions do not match viewport")
        if capture.get("complete_frame_post_draws") != 2:
            raise AnimeVisualSliceError(f"capture {state} needs two complete FramePostDraws")

        layout = _mapping(capture.get("layout"), f"capture {state}.layout")
        if layout.get("state") != state or layout.get("has_outer_table_frame") is not False:
            raise AnimeVisualSliceError(f"capture {state} introduced an outer table frame")
        if layout.get("uses_native_session") is not False:
            raise AnimeVisualSliceError(f"capture {state} accessed native state")
        if layout.get("hidden_cards_with_identity") != 0:
            raise AnimeVisualSliceError(f"capture {state} leaked hidden identity")
        if layout.get("required_asset_count") != len(REQUIRED_ASSETS):
            raise AnimeVisualSliceError(f"capture {state} has the wrong asset contract size")
        logical = _rect(layout.get("viewport"), f"capture {state}.viewport")
        board = _rect(layout.get("board"), f"capture {state}.board")
        _rect(layout.get("left_panel"), f"capture {state}.left_panel")
        _rect(layout.get("right_panel"), f"capture {state}.right_panel")
        if state in BATTLE_STATES:
            if board["width"] < logical["width"] * 0.45 or board["height"] < logical["height"] * 0.78:
                raise AnimeVisualSliceError(f"capture {state} does not prioritize the battlefield")
            if (
                layout.get("main_board_slot_count") != 10
                or layout.get("tactic_slot_count") != 6
                or layout.get("field_slot_count") != 2
                or layout.get("hidden_card_count") != 5
            ):
                raise AnimeVisualSliceError(f"capture {state} has incomplete board surfaces")
        if state == "mixed-permanents-field" and set(layout.get("visible_card_kinds", [])) != {
            "Amulet",
            "Field",
            "Follower",
            "Spell",
            "Trap",
        }:
            raise AnimeVisualSliceError("mixed state must demonstrate all five card silhouettes")
        if state == "covered" and (
            layout.get("covered_opaque") is not True
            or layout.get("visible_card_count") != 0
            or layout.get("hidden_card_count") != 0
        ):
            raise AnimeVisualSliceError("Covered must be opaque and contain no card actors")
        _validate_readability(
            capture.get("readability_evidence"),
            state=state,
            logical_viewport=logical,
            physical_viewport=physical_viewport,
            rgba=rgba,
        )
    return report


def _parse_viewport(value: str) -> tuple[int, int]:
    try:
        width, height = (int(part) for part in value.lower().split("x", 1))
    except (ValueError, TypeError) as error:
        raise argparse.ArgumentTypeError("viewport must be WIDTHxHEIGHT") from error
    if (width, height) not in VIEWPORTS | CI_RUNNER_VIEWPORTS:
        raise argparse.ArgumentTypeError("viewport is outside the Gate 6A matrix and CI runner smoke set")
    return width, height


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("report", type=Path)
    parser.add_argument("--expected-viewport", type=_parse_viewport)
    parser.add_argument(
        "--allow-missing-assets",
        action="store_true",
        help="Permit code-native fallbacks while raster generation is still in progress.",
    )
    parser.add_argument(
        "--allow-ci-runner-viewport",
        action="store_true",
        help="Permit the macOS hosted runner's native 1024x684 display for structural smoke only.",
    )
    arguments = parser.parse_args()
    try:
        validate_report(
            arguments.report,
            arguments.expected_viewport,
            allow_missing_assets=arguments.allow_missing_assets,
            allow_ci_runner_viewport=arguments.allow_ci_runner_viewport,
        )
    except (AnimeVisualSliceError, OSError, json.JSONDecodeError) as error:
        print(f"Gate 6A anime visual-slice validation failed: {error}")
        return 1
    print("Gate 6A anime visual-slice validation passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
