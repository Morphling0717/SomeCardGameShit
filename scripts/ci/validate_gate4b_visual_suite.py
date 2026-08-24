#!/usr/bin/env python3
"""Validate a display-backed Gate 4B visual-suite manifest and screenshots."""

# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import argparse
import hashlib
import json
import struct
import sys
from pathlib import Path
from typing import Any


EXPECTED_STATES = {
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
EXPECTED_TOP_LEVEL = {
    "schema_version",
    "gate",
    "scenario",
    "asset_manifest_sha256",
    "viewport",
    "captures",
    "performance",
}
EXPECTED_CAPTURE = {
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
}
EXPECTED_PERFORMANCE = {
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


def _number(value: object, name: str, *, minimum: float = 0.0) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise VisualSuiteError(f"{name} must be numeric")
    result = float(value)
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
    if not isinstance(report, dict) or set(report) != EXPECTED_TOP_LEVEL:
        raise VisualSuiteError(f"report fields must be exactly {sorted(EXPECTED_TOP_LEVEL)}")
    if (
        report["schema_version"] != 2
        or report["gate"] != "4B-R1"
        or report["scenario"] != "visual-suite"
    ):
        raise VisualSuiteError("report must identify Gate 4B-R1 visual-suite schema 2")
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
    suite_root = report_path.parent
    for index, capture in enumerate(captures):
        if not isinstance(capture, dict) or set(capture) != EXPECTED_CAPTURE:
            raise VisualSuiteError(
                f"capture[{index}] fields must be exactly {sorted(EXPECTED_CAPTURE)}"
            )
        state = capture["state"]
        if not isinstance(state, str) or state not in EXPECTED_STATES or state in states:
            raise VisualSuiteError(f"capture[{index}] has an invalid or duplicate state: {state!r}")
        states.add(state)
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
        if state in BOARD_STATES and (
            battlefield_width < 0.68 or battlefield_height < 0.72
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
        png_width, png_height = _png_dimensions(path)
        if (capture["width"], capture["height"]) != (png_width, png_height):
            raise VisualSuiteError(f"capture[{index}] PNG dimensions disagree with metadata")
        if (png_width, png_height) != (width, height):
            raise VisualSuiteError(f"capture[{index}] does not fill the configured viewport")
        actual_hash = hashlib.sha256(path.read_bytes()).hexdigest()
        if capture["sha256"] != actual_hash:
            raise VisualSuiteError(f"capture[{index}] SHA-256 mismatch")
        screenshot_hashes.add(actual_hash)
        if enforce_structure and path.stat().st_size < 16 * 1024:
            raise VisualSuiteError(
                f"capture[{index}] is implausibly sparse/blank ({path.stat().st_size} bytes)"
            )

    missing = EXPECTED_STATES - states
    if missing:
        raise VisualSuiteError(f"visual suite is missing states: {sorted(missing)}")
    if enforce_structure and len(screenshot_hashes) < 8:
        raise VisualSuiteError(
            "visual suite did not render enough structurally distinct UI states"
        )

    performance = report["performance"]
    if not isinstance(performance, dict) or set(performance) != EXPECTED_PERFORMANCE:
        raise VisualSuiteError(
            f"performance fields must be exactly {sorted(EXPECTED_PERFORMANCE)}"
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
    if enforce_budget and (p95 > 33.3 or maximum >= 100.0):
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
