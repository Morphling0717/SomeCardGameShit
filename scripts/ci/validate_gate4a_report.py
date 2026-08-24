#!/usr/bin/env python3
"""Validate the structured Gate 4A 3D/legacy-board smoke report."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))

from scripts.ci.validate_gate3c_report import (
    EXPECTED_FIELDS as GATE3C_FIELDS,
    ReportError,
    validate as validate_gate3c,
)


PRESENTATIONS = {"3d", "legacy-2d"}
GATE4A_FIELDS = {
    "presentation_mode",
    "surface_intent_e2e",
    "raycast_e2e",
    "hud_raycast_blocks",
    "drag_threshold_pixels",
    "camera_fov_degrees",
    "camera_pitch_degrees",
    "perspective_rebuilds",
    "actor_pool_reuses",
    "blocked_spatial_inputs",
    "spatial_private_leaks",
}
EXPECTED_FIELDS = GATE3C_FIELDS | GATE4A_FIELDS


def _integer(report: dict[str, object], field: str, minimum: int, maximum: int) -> int:
    value = report[field]
    if isinstance(value, bool) or not isinstance(value, int):
        raise ReportError(f"{field} must be an integer")
    if value < minimum or value > maximum:
        raise ReportError(f"{field} is outside [{minimum}, {maximum}]")
    return value


def _boolean(report: dict[str, object], field: str) -> bool:
    value = report[field]
    if not isinstance(value, bool):
        raise ReportError(f"{field} must be a boolean")
    return value


def validate(
    report: object,
    expected_scenario: str | None = None,
    expected_presentation: str | None = None,
) -> None:
    if not isinstance(report, dict):
        raise ReportError("report root must be an object")
    fields = set(report)
    if fields != EXPECTED_FIELDS:
        raise ReportError(
            f"report fields differ: missing={sorted(EXPECTED_FIELDS - fields)}, "
            f"unexpected={sorted(fields - EXPECTED_FIELDS)}"
        )

    _integer(report, "schema_version", 3, 3)
    if report["gate"] != "4A":
        raise ReportError('gate must be "4A"')

    presentation = report["presentation_mode"]
    if not isinstance(presentation, str) or presentation not in PRESENTATIONS:
        raise ReportError(f"unsupported presentation_mode: {presentation}")
    if expected_presentation is not None and presentation != expected_presentation:
        raise ReportError(
            "presentation mismatch: "
            f"expected {expected_presentation}, found {presentation}"
        )

    # Gate 4A extends the proven Gate 3C full-match contract instead of
    # weakening or duplicating it. Project the shared fields back to schema v2
    # so every action, privacy, restart, surrender, and disposal invariant is
    # checked by the frozen Gate 3C validator as well.
    shared = {field: report[field] for field in GATE3C_FIELDS}
    shared["schema_version"] = 2
    shared["gate"] = "3C"
    validate_gate3c(shared, expected_scenario)

    surface_intent = _boolean(report, "surface_intent_e2e")
    raycast = _boolean(report, "raycast_e2e")
    hud_blocks = _integer(report, "hud_raycast_blocks", 0, 1_000_000)
    drag_threshold = _integer(report, "drag_threshold_pixels", 0, 1_000_000)
    camera_fov = _integer(report, "camera_fov_degrees", 0, 180)
    camera_pitch = _integer(report, "camera_pitch_degrees", 0, 90)
    perspective_rebuilds = _integer(
        report, "perspective_rebuilds", 0, 1_000_000
    )
    actor_pool_reuses = _integer(report, "actor_pool_reuses", 0, 1_000_000)
    blocked_inputs = _integer(
        report, "blocked_spatial_inputs", 0, 1_000_000
    )
    spatial_leaks = _integer(
        report, "spatial_private_leaks", 0, 1_000_000
    )
    if spatial_leaks != 0:
        raise ReportError("the spatial presentation exposed private data")

    if report["scenario"] != "full-match":
        return
    if not surface_intent:
        raise ReportError("full-match must use the shared surface-intent coordinator")

    if presentation == "3d":
        if not raycast:
            raise ReportError("the 3D full-match did not exercise raycast input")
        if hud_blocks < 1:
            raise ReportError("the 3D full-match did not verify HUD raycast blocking")
        if drag_threshold != 8:
            raise ReportError("the 3D drag threshold must remain exactly 8 pixels")
        # Gate 4A historical evidence used a 70-degree FOV. Gate 4B narrows
        # authored framing to 58 degrees while preserving this validator for
        # both already-published reports and current regression runs.
        if camera_fov not in (58, 70) or camera_pitch != 58:
            raise ReportError(
                "the 3D camera contract must be historical 70 FOV or Gate 4B 58 FOV / 58 pitch"
            )
        if perspective_rebuilds < 1:
            raise ReportError("the 3D full-match did not rebuild viewer perspective")
        if actor_pool_reuses < 1:
            raise ReportError("the 3D full-match did not demonstrate actor-pool reuse")
        if blocked_inputs < 1:
            raise ReportError("the 3D full-match did not exercise locked spatial input")
    else:
        if raycast:
            raise ReportError("the legacy 2D presenter must not report 3D raycast input")
        legacy_only_values = {
            "hud_raycast_blocks": hud_blocks,
            "drag_threshold_pixels": drag_threshold,
            "camera_fov_degrees": camera_fov,
            "camera_pitch_degrees": camera_pitch,
            "perspective_rebuilds": perspective_rebuilds,
            "actor_pool_reuses": actor_pool_reuses,
            "blocked_spatial_inputs": blocked_inputs,
        }
        non_zero = {field: value for field, value in legacy_only_values.items() if value != 0}
        if non_zero:
            raise ReportError(
                f"legacy 2D report contains 3D-only evidence: {non_zero}"
            )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--report", required=True, type=Path)
    parser.add_argument(
        "--scenario",
        choices=(
            "privacy-mulligan",
            "full-match",
            "resources",
            "evolve-deploy",
            "reaction",
            "terminal-restart",
        ),
    )
    parser.add_argument("--presentation", choices=sorted(PRESENTATIONS))
    args = parser.parse_args()

    try:
        report_path = args.report.resolve(strict=True)
        report = json.loads(report_path.read_text(encoding="utf-8"))
        validate(report, args.scenario, args.presentation)
    except (OSError, UnicodeError, json.JSONDecodeError, ReportError) as error:
        print(f"Gate 4A report validation failed: {error}", file=sys.stderr)
        return 1

    print(f"validated Gate 4A report: {report_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
