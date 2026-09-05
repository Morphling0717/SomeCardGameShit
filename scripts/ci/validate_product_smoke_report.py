#!/usr/bin/env python3
"""Strictly validate identity-free evidence from the real v05 Godot product UI.

This is deliberately separate from frozen v04/Gate 4A and no-native visual
slice contracts. A process-frame smoke is NOT GPU screenshot evidence.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

EXPECTED_FIELDS = {
    "schema_version", "suite", "api", "abi_major", "engine_schema",
    "product_scene", "visual_profile", "run_kind", "coverage", "frame_clock",
    "viewport_width", "viewport_height", "player0_deck", "player1_deck",
    "pointer_inputs", "spatial_inputs", "keyboard_inputs", "commands",
    "invalid_drag_owner_checks", "invalid_drag_zone_checks", "selection_back_checks",
    "reaction_surrender_checks",
    "choice_surrender_checks",
    "action_counts", "natural_terminals", "surrender_terminals", "restarts",
    "disposed_sessions", "covered_samples", "resolving_samples",
    "minimum_public_frames", "premature_view_reads", "private_state_leaks",
    "unauthorized_private_queries", "scheduling_queries",
    "unattributed_commands", "engine_failures", "terminal_event_checks",
    "terminal_result", "final_revision", "success",
}
RUN_KINDS = {"source", "export", "zip"}
COVERAGES = {"full-ui", "natural-ui"}


class ReportError(ValueError):
    pass


def _integer(value: object, field: str, minimum: int = 0, maximum: int = 1_000_000) -> int:
    if type(value) is not int or not minimum <= value <= maximum:
        raise ReportError(f"{field} must be an integer in [{minimum}, {maximum}]")
    return value


def validate(report: object, expected_run_kind: str | None = None,
             expected_coverage: str | None = None, require_display: bool = False) -> None:
    if not isinstance(report, dict) or set(report) != EXPECTED_FIELDS:
        raise ReportError("product report must have exactly the identity-free schema fields")
    constants = {
        "schema_version": 1, "suite": "product-v05-ui", "api": "scgs_v05",
        "abi_major": 2, "engine_schema": 2,
        "product_scene": "res://scenes/match/ProductMatch.tscn",
        "visual_profile": "anime-v1",
        "player0_deck": "oathguard_luminous_oath_v1",
        "player1_deck": "pactmage_abyssal_pact_v1",
    }
    for field, expected in constants.items():
        actual = report[field]
        if type(actual) is not type(expected) or actual != expected:
            raise ReportError(f"{field} does not identify the current v05 product")
    for field, allowed, expected in (
        ("run_kind", RUN_KINDS, expected_run_kind),
        ("coverage", COVERAGES, expected_coverage),
    ):
        if not isinstance(report[field], str) or report[field] not in allowed:
            raise ReportError(f"invalid {field}")
        if expected is not None and report[field] != expected:
            raise ReportError(f"unexpected {field}: expected {expected}")
    if report["frame_clock"] not in ("process-frame", "frame-post-draw"):
        raise ReportError("unknown frame clock")
    if require_display and report["frame_clock"] != "frame-post-draw":
        raise ReportError("a headless process-frame run is not display-backed evidence")
    _integer(report["viewport_width"], "viewport_width", 1280, 16384)
    _integer(report["viewport_height"], "viewport_height", 720, 16384)
    _integer(report["final_revision"], "final_revision", 1, 2**64 - 1)
    _integer(report["terminal_result"], "terminal_result", 1, 3)
    if report["success"] is not True:
        raise ReportError("smoke was not successful")
    counters = EXPECTED_FIELDS - set(constants) - {
        "run_kind", "coverage", "frame_clock", "viewport_width", "viewport_height",
        "final_revision", "terminal_result", "success", "action_counts",
    }
    for field in counters:
        _integer(report[field], field)
    actions = report["action_counts"]
    if not isinstance(actions, list) or len(actions) != 14:
        raise ReportError("action_counts must contain all 14 frozen numeric ActionKind positions")
    for index, count in enumerate(actions):
        _integer(count, f"action_counts[{index}]")
    if report["commands"] != sum(actions) or report["commands"] < 1:
        raise ReportError("command count must equal observed successful action counts")
    if report["commands"] > report["pointer_inputs"] + report["keyboard_inputs"]:
        raise ReportError("commands lack real input attribution")
    if not 0 < report["spatial_inputs"] <= report["pointer_inputs"] or report["keyboard_inputs"] < 1:
        raise ReportError("both real spatial pointer and keyboard input are required")
    for field in ("premature_view_reads", "unauthorized_private_queries", "private_state_leaks", "unattributed_commands", "engine_failures"):
        if report[field] != 0:
            raise ReportError(f"unsafe product evidence: {field} is nonzero")
    if report["covered_samples"] < 2 or report["resolving_samples"] < 2 or report["minimum_public_frames"] < 2:
        raise ReportError("both reveal and two-frame public resolution gates must be exercised")
    terminals = report["natural_terminals"] + report["surrender_terminals"]
    if report["natural_terminals"] < 1:
        raise ReportError("a surrender-only run is not a natural product match")
    if report["surrender_terminals"] != actions[10]:
        raise ReportError("each surrender must be a separately verified terminal")
    if report["terminal_event_checks"] != terminals or report["disposed_sessions"] != terminals:
        raise ReportError("every match needs exactly one final MatchEnded and one dispose")
    if report["restarts"] != terminals - 1:
        raise ReportError("restart count does not match actual completed sessions")
    required = range(14) if report["coverage"] == "full-ui" else (0, 1, 2, 4, 9)
    if any(actions[kind] == 0 for kind in required):
        raise ReportError("requested actual UI action coverage is incomplete")
    if report["coverage"] == "full-ui" and (report["restarts"] < 1 or report["surrender_terminals"] < 1):
        raise ReportError("full UI coverage needs surrender and a real restart")
    if report["coverage"] == "full-ui" and any(report[field] < 1 for field in (
        "invalid_drag_owner_checks", "invalid_drag_zone_checks", "selection_back_checks", "reaction_surrender_checks", "choice_surrender_checks")):
        raise ReportError("full UI coverage needs rejected drags, unchanged-state Esc and reaction/choice surrender checks")
    if report["reaction_surrender_checks"] + report["choice_surrender_checks"] > report["surrender_terminals"]:
        raise ReportError("reaction/choice-surrender checks exceed actual separate surrender terminals")


def load_report(path: Path) -> object:
    """Read bounded strict UTF-8 JSON, rejecting duplicate evidence fields."""
    raw = path.read_bytes()
    if len(raw) > 65536:
        raise ReportError("product report exceeds 64 KiB")

    def unique_object(pairs: list[tuple[str, object]]) -> dict[str, object]:
        result: dict[str, object] = {}
        for key, value in pairs:
            if key in result:
                raise ReportError("duplicate product report field")
            result[key] = value
        return result

    return json.loads(raw.decode("utf-8"), object_pairs_hook=unique_object)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", required=True, type=Path)
    parser.add_argument("--run-kind", choices=sorted(RUN_KINDS))
    parser.add_argument("--coverage", choices=sorted(COVERAGES))
    parser.add_argument("--require-display", action="store_true")
    args = parser.parse_args()
    try:
        report = load_report(args.report)
        validate(report, args.run_kind, args.coverage, args.require_display)
    except (OSError, UnicodeError, ValueError) as error:
        print(f"Product v05 UI report validation failed: {error}", file=sys.stderr)
        return 1
    print(f"Validated product v05 UI report: {args.report}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
