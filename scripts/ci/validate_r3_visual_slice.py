#!/usr/bin/env python3
"""Validate the standalone, unapproved R3 visual-slice report."""

# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import re
import struct
import subprocess
import sys
import zlib
from pathlib import Path
from typing import Any


EXPECTED_STATES = ("action-idle", "hand-hover", "source-selected")
REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
EXPECTED_TOP_LEVEL = {
    "schema_version",
    "gate",
    "scenario",
    "arena_profile",
    "approval_status",
    "session_setup",
    "final_revision",
    "provenance",
    "capture_contract",
    "session_evidence",
    "privacy_evidence",
    "viewport",
    "captures",
}
EXPECTED_PROVENANCE = {
    "commit_sha",
    "commit_source",
    "working_tree_dirty",
    "product_asset_manifest",
    "candidate_asset_manifest",
    "candidate_floor_sha256",
    "candidate_glb_sha256",
    "candidate_shader_sha256",
    "launcher_sha256",
}
EXPECTED_MANIFEST_IDENTITY = {"resource_path", "sha256", "asset_count"}
EXPECTED_SESSION_SETUP = {"seed", "first_player", "shuffle_decks"}
EXPECTED_CAPTURE_CONTRACT = {
    "frame_post_draws",
    "pixel_space",
    "maximum_frame_pair_mae",
}
EXPECTED_SESSION_EVIDENCE = {
    "session_interface",
    "session_runtime_type",
    "state_source",
    "legal_actions_source",
    "successful_mulligan_submissions",
    "final_legal_action_count",
    "selected_action_kind",
    "selected_source",
}
EXPECTED_PRIVACY_EVIDENCE = {
    "opaque_cover_before_first_view",
    "viewer_request_order",
    "explicit_reveal_count",
    "snapshot_request_count",
    "viewer_read_request_count",
    "premature_view_calls",
    "gpu_sentinel_detector_self_test_passed",
    "injected_sentinel_exercised",
    "injected_sentinel_runtime_scrub_verified",
    "candidate_captures_sentinel_absent",
    "hidden_card_shared_back",
    "hidden_card_count",
    "injected_transition",
    "scrub",
}
EXPECTED_INJECTED_TRANSITION = {
    "source_action_kind",
    "source_viewer",
    "source_revision",
    "result_revision",
    "resolving",
    "covered",
}
EXPECTED_TRANSITION_FRAME = {
    "mode",
    "revision",
    "width",
    "height",
    "file",
    "sha256",
    "complete_frame_post_draws",
    "snapshot_requests_before",
    "snapshot_requests_after",
    "viewer_reads_before",
    "viewer_reads_after",
    "privacy_sentinel_absent",
}
EXPECTED_PRIVACY_SCRUB = {
    "private_text_cleared",
    "private_metadata_cleared",
    "private_material_cleared",
    "collisions_disabled",
    "drag_tokens_cleared",
    "tweens_cancelled",
    "callbacks_cleared",
    "resolving_private_leak_count",
    "spatial_private_leak_count",
    "forbidden_sentinel_token_count",
}
EXPECTED_VIEWPORT = {"width", "height"}
EXPECTED_CAPTURE = {
    "state",
    "viewer",
    "revision",
    "width",
    "height",
    "file",
    "sha256",
    "stable_frame_post_draws",
    "frame_pair_mae",
    "privacy_sentinel_absent",
}


class R3VisualSliceError(ValueError):
    """Raised when the R3 candidate evidence is incomplete or inconsistent."""


def _require_keys(value: Any, expected: set[str], context: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise R3VisualSliceError(f"{context} must be an object")
    actual = set(value)
    if actual != expected:
        missing = sorted(expected - actual)
        extra = sorted(actual - expected)
        raise R3VisualSliceError(
            f"{context} fields differ: missing={missing}, extra={extra}"
        )
    return value


def _require_int(value: Any, context: str, *, minimum: int = 0) -> int:
    if type(value) is not int or value < minimum:
        raise R3VisualSliceError(f"{context} must be an integer >= {minimum}")
    return value


def _require_true(value: Any, context: str) -> None:
    if value is not True:
        raise R3VisualSliceError(f"{context} must be true")


def _require_sha256(value: Any, context: str) -> str:
    if not isinstance(value, str) or re.fullmatch(r"[0-9a-f]{64}", value) is None:
        raise R3VisualSliceError(f"{context} must be lowercase SHA-256")
    return value


def _file_sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _expected_build_identity() -> tuple[str, str, bool | None]:
    for variable in ("SCGS_BUILD_COMMIT", "GITHUB_SHA"):
        value = os.environ.get(variable, "").strip().lower()
        if re.fullmatch(r"[0-9a-f]{40}", value):
            dirty = None
            if variable == "SCGS_BUILD_COMMIT":
                dirty = os.environ.get("SCGS_BUILD_DIRTY", "false").lower() == "true"
            elif variable == "GITHUB_SHA":
                dirty = False
            return value, variable, dirty
    try:
        revision = subprocess.check_output(
            ["git", "-C", str(REPOSITORY_ROOT), "rev-parse", "HEAD"],
            text=True,
            stderr=subprocess.DEVNULL,
        ).strip().lower()
        status = subprocess.check_output(
            ["git", "-C", str(REPOSITORY_ROOT), "status", "--porcelain"],
            text=True,
            stderr=subprocess.DEVNULL,
        )
    except (OSError, subprocess.CalledProcessError) as error:
        raise R3VisualSliceError(
            "validator cannot resolve the expected commit SHA from environment or Git"
        ) from error
    if re.fullmatch(r"[0-9a-f]{40}", revision) is None:
        raise R3VisualSliceError("Git returned an invalid commit SHA")
    return revision, "git", bool(status.strip())


def _validate_provenance(raw: Any) -> None:
    provenance = _require_keys(raw, EXPECTED_PROVENANCE, "provenance")
    commit = provenance["commit_sha"]
    if not isinstance(commit, str) or re.fullmatch(r"[0-9a-f]{40}", commit) is None:
        raise R3VisualSliceError("provenance.commit_sha must be lowercase 40-character SHA")
    expected_commit, expected_source, expected_dirty = _expected_build_identity()
    if commit != expected_commit:
        raise R3VisualSliceError(
            f"provenance.commit_sha must bind tested checkout {expected_commit}"
        )
    if provenance["commit_source"] != expected_source:
        raise R3VisualSliceError(
            f"provenance.commit_source must be {expected_source}"
        )
    if type(provenance["working_tree_dirty"]) is not bool:
        raise R3VisualSliceError("provenance.working_tree_dirty must be boolean")
    if expected_dirty is not None and provenance["working_tree_dirty"] != expected_dirty:
        raise R3VisualSliceError(
            f"provenance.working_tree_dirty must be {expected_dirty}"
        )

    product_manifest_path = REPOSITORY_ROOT / "client/godot/assets/visual/ASSET_MANIFEST.json"
    candidate_manifest_path = (
        REPOSITORY_ROOT / "client/godot/assets/visual/arena/R3_ASSET_MANIFEST.json"
    )
    identities = (
        (
            "product_asset_manifest",
            "res://assets/visual/ASSET_MANIFEST.json",
            product_manifest_path,
            34,
        ),
        (
            "candidate_asset_manifest",
            "res://assets/visual/arena/R3_ASSET_MANIFEST.json",
            candidate_manifest_path,
            1,
        ),
    )
    for field, resource_path, source_path, count in identities:
        identity = _require_keys(
            provenance[field], EXPECTED_MANIFEST_IDENTITY, f"provenance.{field}"
        )
        if identity["resource_path"] != resource_path:
            raise R3VisualSliceError(
                f"provenance.{field}.resource_path must be {resource_path}"
            )
        if type(identity["asset_count"]) is not int or identity["asset_count"] != count:
            raise R3VisualSliceError(
                f"provenance.{field}.asset_count must be {count}"
            )
        expected_hash = _file_sha256(source_path)
        if _require_sha256(identity["sha256"], f"provenance.{field}.sha256") != expected_hash:
            raise R3VisualSliceError(f"provenance.{field}.sha256 disagrees with source")
        manifest = json.loads(source_path.read_text(encoding="utf-8"))
        if not isinstance(manifest.get("assets"), list) or len(manifest["assets"]) != count:
            raise R3VisualSliceError(f"{field} source asset count differs")

    bound_files = {
        "candidate_floor_sha256": REPOSITORY_ROOT
        / "client/godot/assets/visual/arena/r3_industrial_floor_albedo.png",
        "candidate_glb_sha256": REPOSITORY_ROOT
        / "client/godot/assets/visual/arena/r3_arena_machinery.glb",
        "candidate_shader_sha256": REPOSITORY_ROOT
        / "client/godot/assets/visual/r3/r3_industrial_floor.gdshader",
        "launcher_sha256": REPOSITORY_ROOT / "scripts/ci/PLAY_R3_VISUAL_SLICE.cmd",
    }
    for field, path in bound_files.items():
        if _require_sha256(provenance[field], f"provenance.{field}") != _file_sha256(path):
            raise R3VisualSliceError(f"provenance.{field} disagrees with source")

    candidate_manifest = json.loads(candidate_manifest_path.read_text(encoding="utf-8"))
    candidate = candidate_manifest["assets"][0]
    if (
        candidate_manifest.get("schema_version") != 1
        or candidate_manifest.get("gate") != "4B-R3.1"
        or candidate.get("path")
        != "client/godot/assets/visual/arena/r3_industrial_floor_albedo.png"
        or candidate.get("sha256") != provenance["candidate_floor_sha256"]
    ):
        raise R3VisualSliceError("candidate manifest does not bind the candidate floor")


def _paeth(left: int, up: int, upper_left: int) -> int:
    estimate = left + up - upper_left
    distances = (
        (abs(estimate - left), left),
        (abs(estimate - up), up),
        (abs(estimate - upper_left), upper_left),
    )
    return min(distances, key=lambda item: item[0])[1]


def _read_png_rgb(path: Path) -> tuple[int, int, bytes]:
    data = path.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise R3VisualSliceError(f"capture is not a PNG: {path}")
    offset = 8
    compressed = bytearray()
    width = height = bit_depth = color_type = interlace = -1
    while offset + 12 <= len(data):
        length = struct.unpack(">I", data[offset : offset + 4])[0]
        kind = data[offset + 4 : offset + 8]
        payload_end = offset + 8 + length
        if payload_end + 4 > len(data):
            raise R3VisualSliceError(f"capture has a truncated PNG chunk: {path}")
        payload = data[offset + 8 : payload_end]
        if kind == b"IHDR":
            if len(payload) != 13:
                raise R3VisualSliceError(f"capture has an invalid IHDR: {path}")
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
        raise R3VisualSliceError(
            f"capture must be a non-interlaced 8-bit gray/RGB/RGBA PNG: {path}"
        )
    try:
        encoded = zlib.decompress(bytes(compressed))
    except zlib.error as error:
        raise R3VisualSliceError(f"capture has an invalid PNG stream: {path}") from error
    row_bytes = width * channels
    if len(encoded) != height * (row_bytes + 1):
        raise R3VisualSliceError(f"capture has an invalid PNG scanline length: {path}")

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
                    raise R3VisualSliceError(
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
            rgb[destination : destination + 3] = bytes((red, green, blue))
            destination += 3
        previous = row
    return width, height, bytes(rgb)


def _contains_privacy_sentinel(rgb: bytes) -> bool:
    return any(
        rgb[index] >= 250
        and rgb[index + 1] <= 5
        and rgb[index + 2] >= 250
        for index in range(0, len(rgb), 3)
    )


def _corner_floor_luminance(rgb: bytes, width: int, height: int, *, right: bool) -> float:
    """Sample the open arena behind the HUD, not the extreme antialiased edge."""
    x_start = int(width * (0.94 if right else 0.01))
    x_end = max(x_start + 1, int(width * (0.99 if right else 0.06)))
    y_start = int(height * 0.01)
    y_end = max(y_start + 1, int(height * 0.06))
    total = 0
    samples = 0
    for y in range(y_start, min(height, y_end)):
        for x in range(x_start, min(width, x_end)):
            at = (y * width + x) * 3
            total += rgb[at] * 2126 + rgb[at + 1] * 7152 + rgb[at + 2] * 722
            samples += 10_000
    return total / samples if samples else 0.0


def _validate_transition_frame(
    raw: Any,
    *,
    context: str,
    mode: str,
    revision: int,
    filename: str,
    snapshot_requests: int,
    report_directory: Path,
    width: int,
    height: int,
) -> None:
    frame = _require_keys(raw, EXPECTED_TRANSITION_FRAME, context)
    expected_values = {
        "mode": mode,
        "revision": revision,
        "width": width,
        "height": height,
        "file": filename,
        "complete_frame_post_draws": 1,
        "snapshot_requests_before": snapshot_requests,
        "snapshot_requests_after": snapshot_requests,
        "privacy_sentinel_absent": True,
    }
    for field, expected in expected_values.items():
        if frame[field] != expected or (
            isinstance(expected, int)
            and not isinstance(expected, bool)
            and type(frame[field]) is not int
        ):
            raise R3VisualSliceError(f"{context}.{field} must be {expected!r}")
    viewer_reads_before = _require_int(
        frame["viewer_reads_before"],
        f"{context}.viewer_reads_before",
        minimum=snapshot_requests,
    )
    viewer_reads_after = _require_int(
        frame["viewer_reads_after"],
        f"{context}.viewer_reads_after",
        minimum=snapshot_requests,
    )
    if viewer_reads_after != viewer_reads_before:
        raise R3VisualSliceError(
            f"{context}.viewer_reads_after must equal viewer_reads_before"
        )
    path = report_directory / filename
    if not path.is_file():
        raise R3VisualSliceError(f"{context} evidence file is missing: {path}")
    digest = _require_sha256(frame["sha256"], f"{context}.sha256")
    if digest != _file_sha256(path):
        raise R3VisualSliceError(f"{context}.sha256 disagrees with PNG")
    png_width, png_height, rgb = _read_png_rgb(path)
    if (png_width, png_height) != (width, height):
        raise R3VisualSliceError(f"{context} PNG dimensions disagree with viewport")
    if _contains_privacy_sentinel(rgb):
        raise R3VisualSliceError(f"{context} contains injected GPU sentinel #ff00ff")


def validate_report(
    report_path: Path,
    *,
    expected_width: int | None = None,
    expected_height: int | None = None,
) -> dict[str, Any]:
    report_path = report_path.resolve(strict=True)
    try:
        report = json.loads(report_path.read_text(encoding="utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise R3VisualSliceError(f"report is not valid UTF-8 JSON: {error}") from error
    report = _require_keys(report, EXPECTED_TOP_LEVEL, "report")
    if type(report["schema_version"]) is not int or report["schema_version"] != 1:
        raise R3VisualSliceError("schema_version must be 1")
    if report["gate"] != "R3" or report["scenario"] != "visual-slice":
        raise R3VisualSliceError("report must identify the standalone R3 visual-slice")
    if report["arena_profile"] != "r3-candidate":
        raise R3VisualSliceError("arena_profile must be r3-candidate")
    if report["approval_status"] != "pending_user_approval":
        raise R3VisualSliceError("approval_status must be pending_user_approval")

    setup = _require_keys(report["session_setup"], EXPECTED_SESSION_SETUP, "session_setup")
    if (
        type(setup["seed"]) is not int
        or setup["seed"] != 0xC0DEC0DE
        or type(setup["first_player"]) is not int
        or setup["first_player"] != 0
        or setup["shuffle_decks"] is not False
    ):
        raise R3VisualSliceError(
            "session_setup must use seed 0xC0DEC0DE, Player0, and shuffle_decks=false"
        )
    final_revision = _require_int(report["final_revision"], "final_revision", minimum=1)
    if final_revision != 2:
        raise R3VisualSliceError("final_revision must be exactly 2")
    _validate_provenance(report["provenance"])

    contract = _require_keys(
        report["capture_contract"], EXPECTED_CAPTURE_CONTRACT, "capture_contract"
    )
    if contract != {
        "frame_post_draws": 2,
        "pixel_space": "srgb8",
        "maximum_frame_pair_mae": 0.01,
    }:
        raise R3VisualSliceError("capture_contract has unexpected values")

    session = _require_keys(
        report["session_evidence"], EXPECTED_SESSION_EVIDENCE, "session_evidence"
    )
    exact_session_fields = {
        "session_interface": "IScgsGameSession",
        "session_runtime_type": "Scgs.Client.ScgsGameSession",
        "state_source": "HotseatUiState",
        "legal_actions_source": "HotseatUiState.LegalActions",
        "successful_mulligan_submissions": 2,
    }
    for field, expected in exact_session_fields.items():
        if session[field] != expected or (
            isinstance(expected, int) and type(session[field]) is not int
        ):
            raise R3VisualSliceError(f"session_evidence.{field} must be {expected!r}")
    _require_int(
        session["final_legal_action_count"],
        "session_evidence.final_legal_action_count",
        minimum=1,
    )
    action_kind = _require_int(
        session["selected_action_kind"],
        "session_evidence.selected_action_kind",
        minimum=1,
    )
    if action_kind > 7:
        raise R3VisualSliceError("selected_action_kind is not a source-based Action command")
    _require_int(session["selected_source"], "session_evidence.selected_source", minimum=1)

    privacy = _require_keys(
        report["privacy_evidence"], EXPECTED_PRIVACY_EVIDENCE, "privacy_evidence"
    )
    for field in (
        "opaque_cover_before_first_view",
        "gpu_sentinel_detector_self_test_passed",
        "injected_sentinel_exercised",
        "injected_sentinel_runtime_scrub_verified",
        "candidate_captures_sentinel_absent",
        "hidden_card_shared_back",
    ):
        _require_true(privacy[field], f"privacy_evidence.{field}")
    if privacy["viewer_request_order"] != [0, 1, 0]:
        raise R3VisualSliceError("viewer_request_order must be [0, 1, 0]")
    if type(privacy["explicit_reveal_count"]) is not int or privacy["explicit_reveal_count"] != 3:
        raise R3VisualSliceError("explicit_reveal_count must be 3")
    if type(privacy["snapshot_request_count"]) is not int or privacy["snapshot_request_count"] != 5:
        raise R3VisualSliceError("snapshot_request_count must be 5")
    viewer_read_request_count = _require_int(
        privacy["viewer_read_request_count"],
        "privacy_evidence.viewer_read_request_count",
        minimum=privacy["snapshot_request_count"],
    )
    if type(privacy["premature_view_calls"]) is not int or privacy["premature_view_calls"] != 0:
        raise R3VisualSliceError("premature_view_calls must be 0")
    _require_int(privacy["hidden_card_count"], "privacy_evidence.hidden_card_count", minimum=1)
    transition = _require_keys(
        privacy["injected_transition"],
        EXPECTED_INJECTED_TRANSITION,
        "privacy_evidence.injected_transition",
    )
    expected_transition = {
        "source_action_kind": 0,
        "source_viewer": 0,
        "source_revision": 0,
        "result_revision": 1,
    }
    for field, expected in expected_transition.items():
        if type(transition[field]) is not int or transition[field] != expected:
            raise R3VisualSliceError(
                f"privacy_evidence.injected_transition.{field} must be {expected}"
            )
    scrub = _require_keys(
        privacy["scrub"], EXPECTED_PRIVACY_SCRUB, "privacy_evidence.scrub"
    )
    for field in (
        "private_text_cleared",
        "private_metadata_cleared",
        "private_material_cleared",
        "collisions_disabled",
        "drag_tokens_cleared",
        "tweens_cancelled",
        "callbacks_cleared",
    ):
        _require_true(scrub[field], f"privacy_evidence.scrub.{field}")
    for field in (
        "resolving_private_leak_count",
        "spatial_private_leak_count",
        "forbidden_sentinel_token_count",
    ):
        if type(scrub[field]) is not int or scrub[field] != 0:
            raise R3VisualSliceError(f"privacy_evidence.scrub.{field} must be 0")

    viewport = _require_keys(report["viewport"], EXPECTED_VIEWPORT, "viewport")
    width = _require_int(viewport["width"], "viewport.width", minimum=1)
    height = _require_int(viewport["height"], "viewport.height", minimum=1)
    if expected_width is not None and width != expected_width:
        raise R3VisualSliceError(f"viewport.width must be {expected_width}")
    if expected_height is not None and height != expected_height:
        raise R3VisualSliceError(f"viewport.height must be {expected_height}")
    _validate_transition_frame(
        transition["resolving"],
        context="privacy_evidence.injected_transition.resolving",
        mode="Resolving",
        revision=0,
        filename="privacy-resolving.png",
        snapshot_requests=1,
        report_directory=report_path.parent,
        width=width,
        height=height,
    )
    _validate_transition_frame(
        transition["covered"],
        context="privacy_evidence.injected_transition.covered",
        mode="Covered",
        revision=1,
        filename="privacy-covered.png",
        snapshot_requests=2,
        report_directory=report_path.parent,
        width=width,
        height=height,
    )
    for mode in ("resolving", "covered"):
        frame = transition[mode]
        if viewer_read_request_count < frame["viewer_reads_after"]:
            raise R3VisualSliceError(
                "viewer_read_request_count is smaller than injected-transition evidence"
            )

    captures = report["captures"]
    if not isinstance(captures, list) or len(captures) != len(EXPECTED_STATES):
        raise R3VisualSliceError("captures must contain exactly three entries")
    hashes: list[str] = []
    for index, (raw_capture, state) in enumerate(zip(captures, EXPECTED_STATES, strict=True)):
        capture = _require_keys(raw_capture, EXPECTED_CAPTURE, f"capture[{index}]")
        if capture["state"] != state:
            raise R3VisualSliceError(f"capture[{index}].state must be {state}")
        if (
            type(capture["viewer"]) is not int
            or capture["viewer"] != 0
            or type(capture["revision"]) is not int
            or capture["revision"] != final_revision
        ):
            raise R3VisualSliceError(
                f"capture[{index}] must retain Player0 and final_revision"
            )
        if (
            type(capture["width"]) is not int
            or capture["width"] != width
            or type(capture["height"]) is not int
            or capture["height"] != height
        ):
            raise R3VisualSliceError(f"capture[{index}] dimensions differ from viewport")
        expected_file = f"{state}.png"
        if capture["file"] != expected_file:
            raise R3VisualSliceError(f"capture[{index}].file must be {expected_file}")
        if (
            type(capture["stable_frame_post_draws"]) is not int
            or capture["stable_frame_post_draws"] != 2
        ):
            raise R3VisualSliceError(
                f"capture[{index}].stable_frame_post_draws must be 2"
            )
        mae = capture["frame_pair_mae"]
        if (
            type(mae) not in (int, float)
            or isinstance(mae, bool)
            or not math.isfinite(mae)
            or not 0.0 <= mae <= 0.01
        ):
            raise R3VisualSliceError(f"capture[{index}].frame_pair_mae is out of bounds")
        _require_true(
            capture["privacy_sentinel_absent"],
            f"capture[{index}].privacy_sentinel_absent",
        )
        digest = capture["sha256"]
        if (
            not isinstance(digest, str)
            or len(digest) != 64
            or any(character not in "0123456789abcdef" for character in digest)
        ):
            raise R3VisualSliceError(f"capture[{index}].sha256 must be lowercase SHA-256")
        path = report_path.parent / expected_file
        if not path.is_file():
            raise R3VisualSliceError(f"capture file is missing: {path}")
        actual_digest = hashlib.sha256(path.read_bytes()).hexdigest()
        if digest != actual_digest:
            raise R3VisualSliceError(f"capture[{index}] SHA-256 disagrees with PNG")
        png_width, png_height, rgb = _read_png_rgb(path)
        if (png_width, png_height) != (width, height):
            raise R3VisualSliceError(f"capture[{index}] PNG dimensions disagree with metadata")
        if _contains_privacy_sentinel(rgb):
            raise R3VisualSliceError(f"capture[{index}] contains GPU privacy sentinel #ff00ff")
        left_floor = _corner_floor_luminance(rgb, width, height, right=False)
        right_floor = _corner_floor_luminance(rgb, width, height, right=True)
        if min(left_floor, right_floor) < 12.0:
            raise R3VisualSliceError(
                f"capture[{index}] exposes a black finite arena edge "
                f"(left={left_floor:.2f}, right={right_floor:.2f})"
            )
        hashes.append(digest)
    if len(set(hashes)) != len(EXPECTED_STATES):
        raise R3VisualSliceError("the three R3 screenshots must have distinct PNG SHA-256 values")
    return report


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--report", required=True, type=Path)
    parser.add_argument("--width", type=int)
    parser.add_argument("--height", type=int)
    args = parser.parse_args()
    if (args.width is None) != (args.height is None):
        parser.error("--width and --height must be provided together")
    try:
        validate_report(
            args.report,
            expected_width=args.width,
            expected_height=args.height,
        )
    except (OSError, R3VisualSliceError) as error:
        print(f"R3 visual-slice validation failed: {error}", file=sys.stderr)
        return 1
    print(f"validated R3 visual slice: {args.report}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
