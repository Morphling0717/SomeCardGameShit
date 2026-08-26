#!/usr/bin/env python3
"""Validate the Gate 6A-R1 integrated AnimeV1 card-body approval report."""

from __future__ import annotations

import argparse
import binascii
import hashlib
import json
import math
import struct
import zlib
from pathlib import Path
from typing import Any


STATES = (
    "contact-sheet",
    "representatives",
    "contexts",
    "hand-one",
    "hand-five",
    "hand-ten",
    "hand-hover",
    "values",
)
EXPECTED_ACTORS = {
    "contact-sheet": 60,
    "representatives": 9,
    "contexts": 9,
    "hand-one": 1,
    "hand-five": 5,
    "hand-ten": 10,
    "hand-hover": 5,
    "values": 6,
}
EXPECTED_STYLES = {
    "contact-sheet": 60,
    "representatives": 9,
    "contexts": 6,
    "hand-one": 1,
    "hand-five": 5,
    "hand-ten": 9,
    "hand-hover": 5,
    "values": 6,
}
LOWER_HEX = frozenset("0123456789abcdef")
GPU_REQUIRED_STATES = frozenset(
    {"hand-one", "hand-five", "hand-ten", "hand-hover", "values"}
)
GPU_CAPTURE_FIELDS = {
    "State",
    "Required",
    "MinimumBadgePixelHeight",
    "ViewportWidth",
    "ViewportHeight",
    "ActorCount",
    "RequiredBadgeCount",
    "RequiredNameCount",
    "CompleteNameCount",
    "AllRequiredBadgesReadable",
    "AllRequiredNamesReadable",
    "Actors",
}
GPU_ACTOR_FIELDS = {
    "ActorName",
    "DesignId",
    "ProductKind",
    "LocalCompositionReadable",
    "RequiredBadgeCount",
    "AllRequiredBadgesReadable",
    "NameReadable",
    "Badges",
    "Name",
}
GPU_BADGE_FIELDS = {
    "Role",
    "Text",
    "Expected",
    "ReferenceActorName",
    "ScreenX",
    "ScreenY",
    "ScreenWidth",
    "ScreenHeight",
    "PixelHeight",
    "FullyInsideViewport",
    "RoiX",
    "RoiY",
    "RoiWidth",
    "RoiHeight",
    "BrightPixelCount",
    "ColorBucketCount",
    "GlyphDifferencePixelCount",
    "BrightGlyphDifferencePixelCount",
    "SocketScreenX",
    "SocketScreenY",
    "SocketScreenWidth",
    "SocketScreenHeight",
    "SocketFullyInsideViewport",
    "RequiredSocketInsetPixels",
    "GlyphSocketInsetLeft",
    "GlyphSocketInsetTop",
    "GlyphSocketInsetRight",
    "GlyphSocketInsetBottom",
    "GlyphInsideSocket",
    "GlyphRoiX",
    "GlyphRoiY",
    "GlyphPixelWidth",
    "GlyphPixelHeight",
    "HighContrastGlyphDifferencePixelCount",
    "MaximumGlyphContrast",
    "Readable",
}
GPU_NAME_FIELDS = {
    "Text",
    "SourceText",
    "FullNameMatchesSource",
    "ReferenceActorName",
    "Expected",
    "FontSize",
    "ScreenX",
    "ScreenY",
    "ScreenWidth",
    "ScreenHeight",
    "ScreenFullyInsideViewport",
    "TextSocketScreenX",
    "TextSocketScreenY",
    "TextSocketScreenWidth",
    "TextSocketScreenHeight",
    "TextSocketFullyInsideViewport",
    "NamePlateScreenX",
    "NamePlateScreenY",
    "NamePlateScreenWidth",
    "NamePlateScreenHeight",
    "NamePlateFullyInsideViewport",
    "RequiredSocketInsetPixels",
    "RequiredNamePlateHorizontalInsetPixels",
    "TextSocketNamePlateInsetLeft",
    "TextSocketNamePlateInsetTop",
    "TextSocketNamePlateInsetRight",
    "TextSocketNamePlateInsetBottom",
    "TextSocketInsideNamePlate",
    "RoiX",
    "RoiY",
    "RoiWidth",
    "RoiHeight",
    "GlyphDifferencePixelCount",
    "BrightGlyphDifferencePixelCount",
    "GlyphRoiX",
    "GlyphRoiY",
    "GlyphPixelWidth",
    "GlyphPixelHeight",
    "HighContrastGlyphDifferencePixelCount",
    "MaximumGlyphContrast",
    "GlyphSocketInsetLeft",
    "GlyphSocketInsetTop",
    "GlyphSocketInsetRight",
    "GlyphSocketInsetBottom",
    "GlyphInsideTextSocket",
    "MaximumGlyphSocketCenterDeltaPixels",
    "GlyphSocketCenterDeltaX",
    "GlyphSocketCenterDeltaY",
    "GlyphCenteredInTextSocket",
    "Readable",
}
SILHOUETTE_REQUIRED_STATES = frozenset({"representatives", "values"})
SILHOUETTE_FIELDS = {
    "State",
    "Required",
    "ActorCount",
    "ProbeCount",
    "InteriorProbeCount",
    "AllRectangularBasesHidden",
    "AllCornerProbesMatchBackground",
    "AllInteriorProbesShowProductFace",
    "Probes",
    "InteriorProbes",
}
SILHOUETTE_PROBE_FIELDS = {
    "ActorName",
    "Corner",
    "ScreenX",
    "ScreenY",
    "ReferenceX",
    "ReferenceY",
    "FullyInsideViewport",
    "CornerBackgroundColorDelta",
    "Passed",
}
SILHOUETTE_INTERIOR_FIELDS = {
    "ActorName",
    "ScreenX",
    "ScreenY",
    "FullyInsideViewport",
    "RoiX",
    "RoiY",
    "RoiWidth",
    "RoiHeight",
    "ProductLayerDifferencePixelCount",
    "Passed",
}


class AnimeCardBodySliceError(RuntimeError):
    """The report or one of its screenshots violates the approval contract."""


PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"


def _paeth_predictor(left: int, up: int, upper_left: int) -> int:
    prediction = left + up - upper_left
    distance_left = abs(prediction - left)
    distance_up = abs(prediction - up)
    distance_upper_left = abs(prediction - upper_left)
    if distance_left <= distance_up and distance_left <= distance_upper_left:
        return left
    if distance_up <= distance_upper_left:
        return up
    return upper_left


def _decode_rgba8_png(payload: bytes, label: str) -> tuple[int, int, bytes]:
    """Decode the exact PNG subset emitted by the Godot screenshot producer."""

    if not payload.startswith(PNG_SIGNATURE):
        raise AnimeCardBodySliceError(f"{label} is not a PNG file")

    offset = len(PNG_SIGNATURE)
    width = 0
    height = 0
    saw_ihdr = False
    saw_idat = False
    saw_iend = False
    idat_ended = False
    compressed = bytearray()
    while offset < len(payload):
        if len(payload) - offset < 12:
            raise AnimeCardBodySliceError(f"{label} has a truncated PNG chunk")
        chunk_length = struct.unpack_from(">I", payload, offset)[0]
        chunk_type = payload[offset + 4 : offset + 8]
        chunk_end = offset + 12 + chunk_length
        if chunk_end > len(payload):
            raise AnimeCardBodySliceError(f"{label} has a truncated PNG chunk")
        chunk_data = payload[offset + 8 : offset + 8 + chunk_length]
        expected_crc = struct.unpack_from(">I", payload, offset + 8 + chunk_length)[0]
        actual_crc = binascii.crc32(chunk_type)
        actual_crc = binascii.crc32(chunk_data, actual_crc) & 0xFFFFFFFF
        if actual_crc != expected_crc:
            name = chunk_type.decode("ascii", errors="replace")
            raise AnimeCardBodySliceError(f"{label} PNG chunk {name} has an invalid CRC")
        offset = chunk_end

        if not saw_ihdr and chunk_type != b"IHDR":
            raise AnimeCardBodySliceError(f"{label} PNG must begin with IHDR")
        if chunk_type == b"IHDR":
            if saw_ihdr or chunk_length != 13:
                raise AnimeCardBodySliceError(f"{label} has an invalid PNG IHDR")
            saw_ihdr = True
            (
                width,
                height,
                bit_depth,
                color_type,
                compression_method,
                filter_method,
                interlace_method,
            ) = struct.unpack(">IIBBBBB", chunk_data)
            if width == 0 or height == 0:
                raise AnimeCardBodySliceError(f"{label} has invalid PNG dimensions")
            if (
                bit_depth != 8
                or color_type != 6
                or compression_method != 0
                or filter_method != 0
                or interlace_method != 0
            ):
                raise AnimeCardBodySliceError(
                    f"{label} must be a non-interlaced 8-bit RGBA PNG"
                )
        elif chunk_type == b"IDAT":
            if not saw_ihdr or saw_iend or idat_ended:
                raise AnimeCardBodySliceError(f"{label} has invalid PNG IDAT ordering")
            saw_idat = True
            compressed.extend(chunk_data)
        elif chunk_type == b"IEND":
            if not saw_idat or saw_iend or chunk_length != 0:
                raise AnimeCardBodySliceError(f"{label} has an invalid PNG IEND")
            saw_iend = True
            if offset != len(payload):
                raise AnimeCardBodySliceError(f"{label} has trailing bytes after PNG IEND")
            break
        else:
            if saw_idat:
                idat_ended = True
            # Reject unknown critical chunks; ancillary metadata is safe to ignore.
            if chunk_type[:1].isalpha() and chunk_type[:1].isupper() and chunk_type != b"PLTE":
                name = chunk_type.decode("ascii", errors="replace")
                raise AnimeCardBodySliceError(
                    f"{label} contains unsupported critical PNG chunk {name}"
                )

    if not saw_ihdr or not saw_idat or not saw_iend:
        raise AnimeCardBodySliceError(f"{label} is missing required PNG chunks")

    row_byte_length = width * 4
    expected_filtered_length = (row_byte_length + 1) * height
    try:
        inflater = zlib.decompressobj()
        filtered = inflater.decompress(bytes(compressed), expected_filtered_length + 1)
        if len(filtered) <= expected_filtered_length:
            filtered += inflater.flush(expected_filtered_length + 1 - len(filtered))
    except zlib.error as error:
        raise AnimeCardBodySliceError(f"{label} contains invalid PNG image data") from error
    if (
        len(filtered) != expected_filtered_length
        or not inflater.eof
        or inflater.unused_data
        or inflater.unconsumed_tail
    ):
        raise AnimeCardBodySliceError(f"{label} has an invalid PNG pixel stream length")

    pixels = bytearray(row_byte_length * height)
    previous_row = bytearray(row_byte_length)
    source_offset = 0
    destination_offset = 0
    for row_index in range(height):
        filter_type = filtered[source_offset]
        source_offset += 1
        if filter_type > 4:
            raise AnimeCardBodySliceError(
                f"{label} row {row_index} uses unsupported PNG filter {filter_type}"
            )
        encoded_row = filtered[source_offset : source_offset + row_byte_length]
        source_offset += row_byte_length
        if filter_type == 0:
            pixels[destination_offset : destination_offset + row_byte_length] = encoded_row
            destination_offset += row_byte_length
            previous_row = bytearray(encoded_row)
            continue
        decoded_row = bytearray(row_byte_length)
        for column, encoded in enumerate(encoded_row):
            left = decoded_row[column - 4] if column >= 4 else 0
            up = previous_row[column]
            upper_left = previous_row[column - 4] if column >= 4 else 0
            if filter_type == 0:
                predictor = 0
            elif filter_type == 1:
                predictor = left
            elif filter_type == 2:
                predictor = up
            elif filter_type == 3:
                predictor = (left + up) // 2
            else:
                predictor = _paeth_predictor(left, up, upper_left)
            decoded_row[column] = (encoded + predictor) & 0xFF
        pixels[destination_offset : destination_offset + row_byte_length] = decoded_row
        destination_offset += row_byte_length
        previous_row = decoded_row

    return width, height, bytes(pixels)


def _mapping(value: Any, label: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise AnimeCardBodySliceError(f"{label} must be an object")
    return value


def _list(value: Any, label: str) -> list[Any]:
    if not isinstance(value, list):
        raise AnimeCardBodySliceError(f"{label} must be an array")
    return value


def _exact_keys(value: dict[str, Any], expected: set[str], label: str) -> None:
    if set(value) != expected:
        raise AnimeCardBodySliceError(
            f"{label} fields differ: expected {sorted(expected)}, found {sorted(value)}"
        )


def _integer(value: Any, label: str, minimum: int = 0) -> int:
    if type(value) is not int or value < minimum:
        raise AnimeCardBodySliceError(f"{label} must be an integer >= {minimum}")
    return value


def _number(value: Any, label: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise AnimeCardBodySliceError(f"{label} must be a finite number")
    result = float(value)
    if not math.isfinite(result):
        raise AnimeCardBodySliceError(f"{label} must be a finite number")
    return result


def _validate_name_gpu(
    value: Any,
    *,
    label: str,
    actor_name: str,
    width: int,
    height: int,
) -> bool:
    name = _mapping(value, label)
    _exact_keys(name, GPU_NAME_FIELDS, label)
    text = name["Text"]
    source_text = name["SourceText"]
    full_name_matches = name["FullNameMatchesSource"]
    if (
        not isinstance(text, str)
        or not text
        or not isinstance(source_text, str)
        or not source_text
        or full_name_matches is not True
        or text != source_text
        or "…" in text
        or "…" in source_text
        or name["ReferenceActorName"] != actor_name
        or name["Expected"] is not True
        or name["Readable"] is not True
        or name["ScreenFullyInsideViewport"] is not True
        or name["TextSocketFullyInsideViewport"] is not True
        or name["NamePlateFullyInsideViewport"] is not True
        or name["TextSocketInsideNamePlate"] is not True
        or name["GlyphInsideTextSocket"] is not True
        or name["GlyphCenteredInTextSocket"] is not True
    ):
        raise AnimeCardBodySliceError(f"{label} does not render its complete source name")
    _integer(name["FontSize"], f"{label}.FontSize", minimum=14)
    if _integer(name["RequiredSocketInsetPixels"], f"{label}.RequiredSocketInsetPixels") != 1:
        raise AnimeCardBodySliceError(f"{label} must require a one-pixel socket inset")
    required_horizontal_plate_inset = _number(
        name["RequiredNamePlateHorizontalInsetPixels"],
        f"{label}.RequiredNamePlateHorizontalInsetPixels",
    )

    screen_x = _number(name["ScreenX"], f"{label}.ScreenX")
    screen_y = _number(name["ScreenY"], f"{label}.ScreenY")
    screen_width = _number(name["ScreenWidth"], f"{label}.ScreenWidth")
    screen_height = _number(name["ScreenHeight"], f"{label}.ScreenHeight")
    socket_x = _number(name["TextSocketScreenX"], f"{label}.TextSocketScreenX")
    socket_y = _number(name["TextSocketScreenY"], f"{label}.TextSocketScreenY")
    socket_width = _number(name["TextSocketScreenWidth"], f"{label}.TextSocketScreenWidth")
    socket_height = _number(name["TextSocketScreenHeight"], f"{label}.TextSocketScreenHeight")
    plate_x = _number(name["NamePlateScreenX"], f"{label}.NamePlateScreenX")
    plate_y = _number(name["NamePlateScreenY"], f"{label}.NamePlateScreenY")
    plate_width = _number(name["NamePlateScreenWidth"], f"{label}.NamePlateScreenWidth")
    plate_height = _number(name["NamePlateScreenHeight"], f"{label}.NamePlateScreenHeight")
    roi_x = _integer(name["RoiX"], f"{label}.RoiX")
    roi_y = _integer(name["RoiY"], f"{label}.RoiY")
    roi_width = _integer(name["RoiWidth"], f"{label}.RoiWidth", minimum=1)
    roi_height = _integer(name["RoiHeight"], f"{label}.RoiHeight", minimum=1)
    glyph_x = _integer(name["GlyphRoiX"], f"{label}.GlyphRoiX")
    glyph_y = _integer(name["GlyphRoiY"], f"{label}.GlyphRoiY")
    glyph_width = _integer(name["GlyphPixelWidth"], f"{label}.GlyphPixelWidth", minimum=1)
    glyph_height = _integer(name["GlyphPixelHeight"], f"{label}.GlyphPixelHeight", minimum=1)
    difference = _integer(
        name["GlyphDifferencePixelCount"], f"{label}.GlyphDifferencePixelCount"
    )
    bright_difference = _integer(
        name["BrightGlyphDifferencePixelCount"],
        f"{label}.BrightGlyphDifferencePixelCount",
        minimum=2,
    )
    high_contrast = _integer(
        name["HighContrastGlyphDifferencePixelCount"],
        f"{label}.HighContrastGlyphDifferencePixelCount",
    )
    maximum_contrast = _number(name["MaximumGlyphContrast"], f"{label}.MaximumGlyphContrast")
    glyph_insets = [
        _number(name[field], f"{label}.{field}")
        for field in (
            "GlyphSocketInsetLeft",
            "GlyphSocketInsetTop",
            "GlyphSocketInsetRight",
            "GlyphSocketInsetBottom",
        )
    ]
    plate_insets = [
        _number(name[field], f"{label}.{field}")
        for field in (
            "TextSocketNamePlateInsetLeft",
            "TextSocketNamePlateInsetTop",
            "TextSocketNamePlateInsetRight",
            "TextSocketNamePlateInsetBottom",
        )
    ]
    expected_glyph_insets = [
        glyph_x - socket_x,
        glyph_y - socket_y,
        socket_x + socket_width - (glyph_x + glyph_width),
        socket_y + socket_height - (glyph_y + glyph_height),
    ]
    expected_plate_insets = [
        socket_x - plate_x,
        socket_y - plate_y,
        plate_x + plate_width - (socket_x + socket_width),
        plate_y + plate_height - (socket_y + socket_height),
    ]
    maximum_center_delta = _number(
        name["MaximumGlyphSocketCenterDeltaPixels"],
        f"{label}.MaximumGlyphSocketCenterDeltaPixels",
    )
    center_delta_x = _number(
        name["GlyphSocketCenterDeltaX"],
        f"{label}.GlyphSocketCenterDeltaX",
    )
    center_delta_y = _number(
        name["GlyphSocketCenterDeltaY"],
        f"{label}.GlyphSocketCenterDeltaY",
    )
    expected_maximum_center_delta = 2.0 * min(width / 1280.0, height / 720.0)
    expected_horizontal_plate_inset = 4.0 * min(width / 1280.0, height / 720.0)
    expected_center_delta_x = abs(
        glyph_x + (glyph_width * 0.5) - (socket_x + (socket_width * 0.5))
    )
    expected_center_delta_y = abs(
        glyph_y + (glyph_height * 0.5) - (socket_y + (socket_height * 0.5))
    )
    minimum_glyph_height = max(6, int(height * (7.0 / 720.0)))
    text_length = len(text.strip())
    minimum_glyph_width = max(3, text_length * 3)
    minimum_difference = max(12, glyph_height * 2, text_length * 6)
    minimum_bright_difference = max(2, (text_length + 1) // 2)
    minimum_high_contrast = max(4, glyph_height // 2, text_length)
    if (
        screen_x < 0
        or screen_y < 0
        or screen_width <= 0
        or screen_height <= 0
        or screen_x + screen_width > width + 0.01
        or screen_y + screen_height > height + 0.01
        or socket_x < 0
        or socket_y < 0
        or socket_width <= 0
        or socket_height <= 0
        or socket_x + socket_width > width + 0.01
        or socket_y + socket_height > height + 0.01
        or plate_x < 0
        or plate_y < 0
        or plate_width <= 0
        or plate_height <= 0
        or plate_x + plate_width > width + 0.01
        or plate_y + plate_height > height + 0.01
        or roi_x + roi_width > width
        or roi_y + roi_height > height
        or glyph_x < roi_x
        or glyph_y < roi_y
        or glyph_x + glyph_width > roi_x + roi_width
        or glyph_y + glyph_height > roi_y + roi_height
        or glyph_width < minimum_glyph_width
        or glyph_height < minimum_glyph_height
        or difference < minimum_difference
        or difference > glyph_width * glyph_height
        or bright_difference < minimum_bright_difference
        or bright_difference > difference
        or high_contrast < minimum_high_contrast
        or high_contrast > difference
        or not 0.18 <= maximum_contrast <= 1.0
        or any(inset < 1.0 for inset in glyph_insets + plate_insets)
        or abs(required_horizontal_plate_inset - expected_horizontal_plate_inset) > 0.01
        or plate_insets[0] < required_horizontal_plate_inset
        or plate_insets[2] < required_horizontal_plate_inset
        or any(abs(actual - expected) > 0.05 for actual, expected in zip(glyph_insets, expected_glyph_insets))
        or any(abs(actual - expected) > 0.05 for actual, expected in zip(plate_insets, expected_plate_insets))
        or abs(maximum_center_delta - expected_maximum_center_delta) > 0.01
        or center_delta_x < 0.0
        or center_delta_y < 0.0
        or center_delta_x > maximum_center_delta
        or center_delta_y > maximum_center_delta
        or abs(center_delta_x - expected_center_delta_x) > 0.05
        or abs(center_delta_y - expected_center_delta_y) > 0.05
    ):
        raise AnimeCardBodySliceError(
            f"{label} is off-screen, off-center, overflows its nameplate, or lacks final GPU pixels"
        )
    return full_name_matches


def _validate_gpu_readability(
    value: Any,
    *,
    state: str,
    width: int,
    height: int,
) -> None:
    evidence = _mapping(value, f"capture {state}.GpuReadability")
    _exact_keys(evidence, GPU_CAPTURE_FIELDS, f"capture {state}.GpuReadability")
    required = state in GPU_REQUIRED_STATES
    minimum_height = max(10, int(height * (14.0 / 720.0)))
    if (
        evidence["State"] != state
        or evidence["Required"] is not required
        or evidence["MinimumBadgePixelHeight"] != minimum_height
        or evidence["ViewportWidth"] != width
        or evidence["ViewportHeight"] != height
        or evidence["AllRequiredBadgesReadable"] is not True
        or evidence["AllRequiredNamesReadable"] is not True
    ):
        raise AnimeCardBodySliceError(
            f"capture {state} has invalid GPU readability policy evidence"
        )

    actors = _list(evidence["Actors"], f"capture {state}.GpuReadability.Actors")
    actor_count = _integer(
        evidence["ActorCount"], f"capture {state}.GpuReadability.ActorCount"
    )
    badge_count = _integer(
        evidence["RequiredBadgeCount"],
        f"capture {state}.GpuReadability.RequiredBadgeCount",
    )
    name_count = _integer(
        evidence["RequiredNameCount"],
        f"capture {state}.GpuReadability.RequiredNameCount",
    )
    complete_name_count = _integer(
        evidence["CompleteNameCount"],
        f"capture {state}.GpuReadability.CompleteNameCount",
    )
    if not required:
        if actor_count or badge_count or name_count or complete_name_count or actors:
            raise AnimeCardBodySliceError(
                f"capture {state} must not forge optional GPU badge evidence"
            )
        return
    if actor_count != EXPECTED_ACTORS[state] or actor_count != len(actors):
        raise AnimeCardBodySliceError(
            f"capture {state} GPU evidence must cover every real actor"
        )
    if name_count != actor_count:
        raise AnimeCardBodySliceError(
            f"capture {state} GPU name evidence must cover every real actor"
        )

    seen_actors: set[str] = set()
    measured_badges = 0
    measured_complete_names = 0
    role_counts: dict[str, int] = {}
    for actor_index, raw_actor in enumerate(actors):
        label = f"capture {state}.GpuReadability.Actors[{actor_index}]"
        actor = _mapping(raw_actor, label)
        _exact_keys(actor, GPU_ACTOR_FIELDS, label)
        actor_name = actor["ActorName"]
        design_id = actor["DesignId"]
        product_kind = actor["ProductKind"]
        if (
            not isinstance(actor_name, str)
            or not actor_name
            or actor_name in seen_actors
            or not isinstance(design_id, str)
            or not design_id
            or product_kind not in {"Follower", "Spell", "Amulet", "Trap", "Field"}
            or actor["LocalCompositionReadable"] is not True
            or actor["AllRequiredBadgesReadable"] is not True
            or actor["NameReadable"] is not True
        ):
            raise AnimeCardBodySliceError(f"{label} has invalid real-actor evidence")
        seen_actors.add(actor_name)
        badges = _list(actor["Badges"], f"{label}.Badges")
        actor_badge_count = _integer(
            actor["RequiredBadgeCount"], f"{label}.RequiredBadgeCount", minimum=1
        )
        if actor_badge_count != len(badges):
            raise AnimeCardBodySliceError(f"{label} badge count differs")
        if _validate_name_gpu(
            actor["Name"],
            label=f"{label}.Name",
            actor_name=actor_name,
            width=width,
            height=height,
        ):
            measured_complete_names += 1
        seen_roles: set[str] = set()
        for badge_index, raw_badge in enumerate(badges):
            badge_label = f"{label}.Badges[{badge_index}]"
            badge = _mapping(raw_badge, badge_label)
            _exact_keys(badge, GPU_BADGE_FIELDS, badge_label)
            role = badge["Role"]
            text = badge["Text"]
            if (
                role not in {"cost", "attack", "health", "countdown"}
                or role in seen_roles
                or not isinstance(text, str)
                or not text
                or badge["ReferenceActorName"] != actor_name
                or badge["Expected"] is not True
                or badge["FullyInsideViewport"] is not True
                or badge["Readable"] is not True
            ):
                raise AnimeCardBodySliceError(f"{badge_label} is not a required readable badge")
            seen_roles.add(role)
            role_counts[role] = role_counts.get(role, 0) + 1

            screen_x = _number(badge["ScreenX"], f"{badge_label}.ScreenX")
            screen_y = _number(badge["ScreenY"], f"{badge_label}.ScreenY")
            screen_width = _number(badge["ScreenWidth"], f"{badge_label}.ScreenWidth")
            screen_height = _number(badge["ScreenHeight"], f"{badge_label}.ScreenHeight")
            pixel_height = _integer(badge["PixelHeight"], f"{badge_label}.PixelHeight")
            roi_x = _integer(badge["RoiX"], f"{badge_label}.RoiX")
            roi_y = _integer(badge["RoiY"], f"{badge_label}.RoiY")
            roi_width = _integer(badge["RoiWidth"], f"{badge_label}.RoiWidth", minimum=1)
            roi_height = _integer(badge["RoiHeight"], f"{badge_label}.RoiHeight", minimum=1)
            bright = _integer(
                badge["BrightPixelCount"], f"{badge_label}.BrightPixelCount", minimum=2
            )
            colors = _integer(
                badge["ColorBucketCount"], f"{badge_label}.ColorBucketCount", minimum=2
            )
            glyph_difference = _integer(
                badge["GlyphDifferencePixelCount"],
                f"{badge_label}.GlyphDifferencePixelCount",
                minimum=2,
            )
            bright_glyph_difference = _integer(
                badge["BrightGlyphDifferencePixelCount"],
                f"{badge_label}.BrightGlyphDifferencePixelCount",
                minimum=2,
            )
            glyph_roi_x = _integer(badge["GlyphRoiX"], f"{badge_label}.GlyphRoiX")
            glyph_roi_y = _integer(badge["GlyphRoiY"], f"{badge_label}.GlyphRoiY")
            glyph_width = _integer(
                badge["GlyphPixelWidth"], f"{badge_label}.GlyphPixelWidth", minimum=2
            )
            glyph_height = _integer(
                badge["GlyphPixelHeight"], f"{badge_label}.GlyphPixelHeight", minimum=1
            )
            high_contrast_difference = _integer(
                badge["HighContrastGlyphDifferencePixelCount"],
                f"{badge_label}.HighContrastGlyphDifferencePixelCount",
            )
            maximum_glyph_contrast = _number(
                badge["MaximumGlyphContrast"], f"{badge_label}.MaximumGlyphContrast"
            )
            socket_x = _number(badge["SocketScreenX"], f"{badge_label}.SocketScreenX")
            socket_y = _number(badge["SocketScreenY"], f"{badge_label}.SocketScreenY")
            socket_width = _number(
                badge["SocketScreenWidth"], f"{badge_label}.SocketScreenWidth"
            )
            socket_height = _number(
                badge["SocketScreenHeight"], f"{badge_label}.SocketScreenHeight"
            )
            if (
                badge["SocketFullyInsideViewport"] is not True
                or badge["GlyphInsideSocket"] is not True
                or _integer(
                    badge["RequiredSocketInsetPixels"],
                    f"{badge_label}.RequiredSocketInsetPixels",
                )
                != 1
            ):
                raise AnimeCardBodySliceError(
                    f"{badge_label} is not contained by its decorative socket"
                )
            socket_insets = [
                _number(badge[field], f"{badge_label}.{field}")
                for field in (
                    "GlyphSocketInsetLeft",
                    "GlyphSocketInsetTop",
                    "GlyphSocketInsetRight",
                    "GlyphSocketInsetBottom",
                )
            ]
            expected_socket_insets = [
                glyph_roi_x - socket_x,
                glyph_roi_y - socket_y,
                socket_x + socket_width - (glyph_roi_x + glyph_width),
                socket_y + socket_height - (glyph_roi_y + glyph_height),
            ]
            minimum_glyph_height = max(6, int(minimum_height * 0.45))
            minimum_glyph_width = max(2, len(text.strip()) * 3)
            if (
                screen_x < 0
                or screen_y < 0
                or screen_width <= 0
                or screen_height <= 0
                or screen_x + screen_width > width + 0.01
                or screen_y + screen_height > height + 0.01
                or socket_x < 0
                or socket_y < 0
                or socket_width <= 0
                or socket_height <= 0
                or socket_x + socket_width > width + 0.01
                or socket_y + socket_height > height + 0.01
                or pixel_height < minimum_height
                or roi_x + roi_width > width
                or roi_y + roi_height > height
                or bright > roi_width * roi_height
                or glyph_difference > roi_width * roi_height
                or bright_glyph_difference > glyph_difference
                or colors > 512
                or glyph_roi_x < roi_x
                or glyph_roi_y < roi_y
                or glyph_roi_x + glyph_width > roi_x + roi_width
                or glyph_roi_y + glyph_height > roi_y + roi_height
                or glyph_width < minimum_glyph_width
                or glyph_height < minimum_glyph_height
                or glyph_difference < max(8, glyph_height)
                or glyph_difference > glyph_width * glyph_height
                or high_contrast_difference < max(3, glyph_height // 3)
                or high_contrast_difference > glyph_difference
                or not 0.18 <= maximum_glyph_contrast <= 1.0
                or any(inset < 1.0 for inset in socket_insets)
                or any(
                    abs(actual - expected) > 0.05
                    for actual, expected in zip(socket_insets, expected_socket_insets)
                )
            ):
                raise AnimeCardBodySliceError(
                    f"{badge_label} is off-screen, too small, or lacks final GPU pixels"
                )
        if "cost" not in seen_roles:
            raise AnimeCardBodySliceError(f"{label} lacks its cost ROI")
        if product_kind == "Follower" and seen_roles != {"cost", "attack", "health"}:
            raise AnimeCardBodySliceError(f"{label} lacks follower stat ROIs")
        if product_kind in {"Spell", "Field"} and seen_roles != {"cost"}:
            raise AnimeCardBodySliceError(f"{label} exposes forbidden stat ROIs")
        measured_badges += len(badges)

    if measured_badges != badge_count:
        raise AnimeCardBodySliceError(f"capture {state} GPU badge aggregate differs")
    if measured_complete_names != complete_name_count or complete_name_count != actor_count:
        raise AnimeCardBodySliceError(
            f"capture {state} must prove every complete card name in final GPU pixels"
        )
    if state == "values" and role_counts != {
        "cost": 6,
        "attack": 3,
        "health": 3,
        "countdown": 2,
    }:
        raise AnimeCardBodySliceError(
            "values must prove cost, follower stats, and countdown in final GPU ROIs"
        )


def _validate_silhouette_isolation(
    value: Any,
    *,
    state: str,
    width: int,
    height: int,
) -> None:
    evidence = _mapping(value, f"capture {state}.SilhouetteIsolation")
    _exact_keys(evidence, SILHOUETTE_FIELDS, f"capture {state}.SilhouetteIsolation")
    required = state in SILHOUETTE_REQUIRED_STATES
    if (
        evidence["State"] != state
        or evidence["Required"] is not required
        or evidence["AllRectangularBasesHidden"] is not True
        or evidence["AllCornerProbesMatchBackground"] is not True
        or evidence["AllInteriorProbesShowProductFace"] is not True
    ):
        raise AnimeCardBodySliceError(
            f"capture {state} retains a rectangular product-card body"
        )
    actors = _integer(
        evidence["ActorCount"], f"capture {state}.SilhouetteIsolation.ActorCount"
    )
    probe_count = _integer(
        evidence["ProbeCount"], f"capture {state}.SilhouetteIsolation.ProbeCount"
    )
    probes = _list(evidence["Probes"], f"capture {state}.SilhouetteIsolation.Probes")
    interior_count = _integer(
        evidence["InteriorProbeCount"],
        f"capture {state}.SilhouetteIsolation.InteriorProbeCount",
    )
    interior_probes = _list(
        evidence["InteriorProbes"],
        f"capture {state}.SilhouetteIsolation.InteriorProbes",
    )
    if not required:
        if actors or probe_count or probes or interior_count or interior_probes:
            raise AnimeCardBodySliceError(
                f"capture {state} must not forge optional silhouette evidence"
            )
        return
    if actors != EXPECTED_ACTORS[state] or probe_count != actors * 4 or len(probes) != probe_count:
        raise AnimeCardBodySliceError(
            f"capture {state} silhouette probes must cover four transparent side edges of every actor"
        )
    if interior_count != actors or len(interior_probes) != actors:
        raise AnimeCardBodySliceError(
            f"capture {state} must prove a visible product-face interior for every actor"
        )

    actor_corners: dict[str, set[str]] = {}
    for index, raw_probe in enumerate(probes):
        label = f"capture {state}.SilhouetteIsolation.Probes[{index}]"
        probe = _mapping(raw_probe, label)
        _exact_keys(probe, SILHOUETTE_PROBE_FIELDS, label)
        actor = probe["ActorName"]
        corner = probe["Corner"]
        if not isinstance(actor, str) or not actor or corner not in {
            "upper-left-edge",
            "upper-right-edge",
            "lower-left-edge",
            "lower-right-edge",
        }:
            raise AnimeCardBodySliceError(f"{label} has an invalid actor or corner")
        if corner in actor_corners.setdefault(actor, set()):
            raise AnimeCardBodySliceError(f"{label} duplicates a corner probe")
        actor_corners[actor].add(corner)
        screen_x = _number(probe["ScreenX"], f"{label}.ScreenX")
        screen_y = _number(probe["ScreenY"], f"{label}.ScreenY")
        reference_x = _number(probe["ReferenceX"], f"{label}.ReferenceX")
        reference_y = _number(probe["ReferenceY"], f"{label}.ReferenceY")
        delta = _number(
            probe["CornerBackgroundColorDelta"], f"{label}.CornerBackgroundColorDelta"
        )
        if (
            probe["FullyInsideViewport"] is not True
            or probe["Passed"] is not True
            or not 0 <= screen_x < width
            or not 0 <= screen_y < height
            or not 0 <= reference_x < width
            or not 0 <= reference_y < height
            or not 0.0 <= delta <= (2.0 / 255.0)
        ):
            raise AnimeCardBodySliceError(
                f"{label} detects an off-screen probe or rectangular silhouette residue"
            )
    if len(actor_corners) != actors or any(len(corners) != 4 for corners in actor_corners.values()):
        raise AnimeCardBodySliceError(
            f"capture {state} silhouette actor coverage differs"
        )
    seen_interior: set[str] = set()
    for index, raw_probe in enumerate(interior_probes):
        label = f"capture {state}.SilhouetteIsolation.InteriorProbes[{index}]"
        probe = _mapping(raw_probe, label)
        _exact_keys(probe, SILHOUETTE_INTERIOR_FIELDS, label)
        actor = probe["ActorName"]
        screen_x = _number(probe["ScreenX"], f"{label}.ScreenX")
        screen_y = _number(probe["ScreenY"], f"{label}.ScreenY")
        roi_x = _integer(probe["RoiX"], f"{label}.RoiX")
        roi_y = _integer(probe["RoiY"], f"{label}.RoiY")
        roi_width = _integer(probe["RoiWidth"], f"{label}.RoiWidth", minimum=1)
        roi_height = _integer(probe["RoiHeight"], f"{label}.RoiHeight", minimum=1)
        difference = _integer(
            probe["ProductLayerDifferencePixelCount"],
            f"{label}.ProductLayerDifferencePixelCount",
            minimum=4,
        )
        if (
            not isinstance(actor, str)
            or not actor
            or actor in seen_interior
            or actor not in actor_corners
            or probe["FullyInsideViewport"] is not True
            or probe["Passed"] is not True
            or not 0 <= screen_x < width
            or not 0 <= screen_y < height
            or roi_x + roi_width > width
            or roi_y + roi_height > height
            or difference > roi_width * roi_height
        ):
            raise AnimeCardBodySliceError(
                f"{label} lacks positive final-GPU product-face pixels"
            )
        seen_interior.add(actor)


def validate_report(report_path: Path, width: int, height: int) -> None:
    report_path = report_path.resolve(strict=True)
    try:
        report = _mapping(json.loads(report_path.read_text(encoding="utf-8")), "report")
    except json.JSONDecodeError as error:
        raise AnimeCardBodySliceError(f"invalid report JSON: {error}") from error

    _exact_keys(
        report,
        {
            "Schema",
            "SchemaVersion",
            "ApprovalStatus",
            "UsesRealCardActor3D",
            "UsesPerCardSubViewport",
            "Captures",
        },
        "report",
    )
    if report["Schema"] != "scgs-anime-card-body-slice" or report["SchemaVersion"] != 4:
        raise AnimeCardBodySliceError("report must identify Gate 6A-R1 schema 4")
    if report["ApprovalStatus"] != "pending_user_approval":
        raise AnimeCardBodySliceError("card-body candidate must remain pending user approval")
    if report["UsesRealCardActor3D"] is not True:
        raise AnimeCardBodySliceError("report must use real CardActor3D instances")
    if report["UsesPerCardSubViewport"] is not False:
        raise AnimeCardBodySliceError("per-card SubViewport composition is forbidden")

    captures = _list(report["Captures"], "report.Captures")
    if [capture.get("State") for capture in captures if isinstance(capture, dict)] != list(STATES):
        raise AnimeCardBodySliceError("captures must use the exact ordered eight-state inventory")

    seen_files: set[str] = set()
    seen_hashes: set[str] = set()
    for index, state in enumerate(STATES):
        capture = _mapping(captures[index], f"capture[{index}]")
        _exact_keys(
            capture,
            {
                "State",
                "File",
                "Sha256",
                "Width",
                "Height",
                "FrameStability",
                "Evidence",
                "GpuReadability",
                "SilhouetteIsolation",
            },
            f"capture {state}",
        )
        if capture["State"] != state or capture["Width"] != width or capture["Height"] != height:
            raise AnimeCardBodySliceError(f"capture {state} has the wrong state or dimensions")
        filename = capture["File"]
        digest = capture["Sha256"]
        if not isinstance(filename, str) or Path(filename).name != filename or not filename.endswith(".png"):
            raise AnimeCardBodySliceError(f"capture {state} has an unsafe screenshot path")
        if filename in seen_files or not isinstance(digest, str) or len(digest) != 64 or digest in seen_hashes:
            raise AnimeCardBodySliceError(f"capture {state} reuses a file or digest")
        screenshot = report_path.parent / filename
        if not screenshot.is_file():
            raise AnimeCardBodySliceError(f"capture {state} screenshot is missing")
        screenshot_payload = screenshot.read_bytes()
        actual_digest = hashlib.sha256(screenshot_payload).hexdigest()
        if actual_digest != digest:
            raise AnimeCardBodySliceError(f"capture {state} screenshot hash differs")
        seen_files.add(filename)
        seen_hashes.add(digest)

        stability = _mapping(
            capture["FrameStability"], f"capture {state}.FrameStability"
        )
        _exact_keys(
            stability,
            {
                "ConsecutiveFramePostDraws",
                "AttemptCount",
                "PixelFormat",
                "PixelByteLength",
                "FirstPixelSha256",
                "SecondPixelSha256",
            },
            f"capture {state}.FrameStability",
        )
        first_pixel_hash = stability["FirstPixelSha256"]
        second_pixel_hash = stability["SecondPixelSha256"]
        if (
            stability["ConsecutiveFramePostDraws"] != 2
            or type(stability["AttemptCount"]) is not int
            or not 1 <= stability["AttemptCount"] <= 30
            or stability["PixelFormat"] != "Rgba8"
            or type(stability["PixelByteLength"]) is not int
            or stability["PixelByteLength"] != width * height * 4
            or not isinstance(first_pixel_hash, str)
            or len(first_pixel_hash) != 64
            or not set(first_pixel_hash) <= LOWER_HEX
            or first_pixel_hash != second_pixel_hash
        ):
            raise AnimeCardBodySliceError(
                f"capture {state} lacks a pixel-identical consecutive FramePostDraw pair"
            )
        decoded_width, decoded_height, decoded_pixels = _decode_rgba8_png(
            screenshot_payload, f"capture {state} screenshot"
        )
        if decoded_width != width or decoded_height != height:
            raise AnimeCardBodySliceError(
                f"capture {state} decoded PNG dimensions differ from the viewport"
            )
        decoded_pixel_hash = hashlib.sha256(decoded_pixels).hexdigest()
        if decoded_pixel_hash != first_pixel_hash:
            raise AnimeCardBodySliceError(
                f"capture {state} decoded RGBA pixel hash differs from FrameStability"
            )

        evidence = _mapping(capture["Evidence"], f"capture {state}.Evidence")
        _exact_keys(
            evidence,
            {
                "State",
                "ActorCount",
                "IntegratedActorCount",
                "DistinctStyleCount",
                "Contexts",
                "DesignIds",
                "SubViewportCount",
                "UsesNativeSession",
            },
            f"capture {state}.Evidence",
        )
        actor_count = EXPECTED_ACTORS[state]
        if (
            evidence["State"] != state
            or evidence["ActorCount"] != actor_count
            or evidence["IntegratedActorCount"] != actor_count
            or evidence["DistinctStyleCount"] != EXPECTED_STYLES[state]
            or evidence["SubViewportCount"] != 0
            or evidence["UsesNativeSession"] is not False
        ):
            raise AnimeCardBodySliceError(f"capture {state} has invalid integration evidence")
        contexts = _list(evidence["Contexts"], f"capture {state}.Contexts")
        design_ids = _list(evidence["DesignIds"], f"capture {state}.DesignIds")
        if not all(isinstance(value, str) and value for value in contexts + design_ids):
            raise AnimeCardBodySliceError(f"capture {state} has malformed identity evidence")
        if state == "contexts" and contexts != ["Detail", "Field", "Hand"]:
            raise AnimeCardBodySliceError("contexts capture must exercise detail, field and hand")
        if state == "contact-sheet" and contexts != ["Field"]:
            raise AnimeCardBodySliceError("contact sheet must use the field context")
        _validate_gpu_readability(
            capture["GpuReadability"], state=state, width=width, height=height
        )
        _validate_silhouette_isolation(
            capture["SilhouetteIsolation"], state=state, width=width, height=height
        )


def _viewport(value: str) -> tuple[int, int]:
    try:
        width, height = (int(part) for part in value.lower().split("x", 1))
    except (TypeError, ValueError) as error:
        raise argparse.ArgumentTypeError("viewport must be WIDTHxHEIGHT") from error
    if width < 1024 or height < 684:
        raise argparse.ArgumentTypeError("viewport is below the desktop/CI-runner minimum")
    return width, height


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("report", type=Path)
    parser.add_argument("--expected-viewport", required=True, type=_viewport)
    args = parser.parse_args()
    try:
        validate_report(args.report, *args.expected_viewport)
    except (AnimeCardBodySliceError, OSError, ValueError) as error:
        print(f"Gate 6A-R1 card-body validation failed: {error}")
        return 1
    print("Gate 6A-R1 card-body validation passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
