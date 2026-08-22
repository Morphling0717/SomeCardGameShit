#!/usr/bin/env python3
"""Validate the privacy-safe structured report emitted by Gate 3C UI smoke tests."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


SCENARIOS = {
    "privacy-mulligan",
    "full-match",
    "resources",
    "evolve-deploy",
    "reaction",
    "terminal-restart",
}
DECKS = {"midrange", "advance"}
FULL_MATCH_ACTIONS = list(range(11))
EXPECTED_FIELDS = {
    "schema_version",
    "gate",
    "scenario",
    "seed",
    "player0_deck",
    "player1_deck",
    "first_player",
    "steps",
    "turns",
    "action_kinds",
    "covers",
    "reveals",
    "premature_view_calls",
    "signal_e2e",
    "click_drag_canonical_parity",
    "selection_commit_without_confirmation",
    "resolving_public_frames",
    "resolving_private_leaks",
    "restarts",
    "surrender_terminals",
    "result",
    "disposed_sessions",
}


class ReportError(RuntimeError):
    pass


def _integer(report: dict[str, object], field: str, minimum: int, maximum: int) -> int:
    value = report[field]
    if isinstance(value, bool) or not isinstance(value, int):
        raise ReportError(f"{field} must be an integer")
    if value < minimum or value > maximum:
        raise ReportError(f"{field} is outside [{minimum}, {maximum}]")
    return value


def _true(report: dict[str, object], field: str) -> None:
    if report[field] is not True:
        raise ReportError(f"{field} must be true")


def _boolean(report: dict[str, object], field: str) -> bool:
    value = report[field]
    if not isinstance(value, bool):
        raise ReportError(f"{field} must be a boolean")
    return value


def validate(report: object, expected_scenario: str | None = None) -> None:
    if not isinstance(report, dict):
        raise ReportError("report root must be an object")
    fields = set(report)
    if fields != EXPECTED_FIELDS:
        raise ReportError(
            f"report fields differ: missing={sorted(EXPECTED_FIELDS - fields)}, "
            f"unexpected={sorted(fields - EXPECTED_FIELDS)}"
        )

    _integer(report, "schema_version", 2, 2)
    if report["gate"] != "3C":
        raise ReportError('gate must be "3C"')
    scenario = report["scenario"]
    if not isinstance(scenario, str) or scenario not in SCENARIOS:
        raise ReportError(f"unsupported scenario: {scenario}")
    if expected_scenario is not None and scenario != expected_scenario:
        raise ReportError(
            f"scenario mismatch: expected {expected_scenario}, found {scenario}"
        )
    if (
        not isinstance(report["player0_deck"], str)
        or report["player0_deck"] not in DECKS
        or not isinstance(report["player1_deck"], str)
        or report["player1_deck"] not in DECKS
    ):
        raise ReportError("report contains an unsupported deck")

    _integer(report, "seed", 0, 0xFFFF_FFFF)
    _integer(report, "first_player", 0, 1)
    steps = _integer(report, "steps", 0, 1_000_000)
    turns = _integer(report, "turns", 0, 1_000_000)
    covers = _integer(report, "covers", 0, 1_000_000)
    reveals = _integer(report, "reveals", 0, 1_000_000)
    premature = _integer(report, "premature_view_calls", 0, 1_000_000)
    # The smoke runner reports the minimum complete frame count observed over
    # every command transition, not a total.  A value of two therefore proves
    # that no submission bypassed the public resolving projection barrier.
    resolving_frames = _integer(
        report, "resolving_public_frames", 0, 1_000_000
    )
    resolving_leaks = _integer(
        report, "resolving_private_leaks", 0, 1_000_000
    )
    for field in (
        "signal_e2e",
        "click_drag_canonical_parity",
        "selection_commit_without_confirmation",
    ):
        _boolean(report, field)
    restarts = _integer(report, "restarts", 0, 1_000_000)
    surrender_terminals = _integer(
        report, "surrender_terminals", 0, 1_000_000
    )
    result = _integer(report, "result", 0, 3)
    disposed = _integer(report, "disposed_sessions", 0, 1_000_000)

    actions = report["action_kinds"]
    if not isinstance(actions, list) or any(
        isinstance(action, bool) or not isinstance(action, int) or action < 0 or action > 10
        for action in actions
    ):
        raise ReportError("action_kinds must contain only frozen ActionKind values")
    if len(actions) != len(set(actions)):
        raise ReportError("action_kinds contains duplicates")
    if actions != sorted(actions):
        raise ReportError("action_kinds must be sorted")
    if premature != 0:
        raise ReportError("a viewer-scoped native call occurred before reveal")
    if resolving_leaks != 0:
        raise ReportError("the neutral resolving projection exposed private data")
    if reveals > covers:
        raise ReportError("reveals cannot exceed opaque handoff covers")

    if scenario == "privacy-mulligan":
        if not {0}.issubset(actions) or covers < 2 or reveals < 2:
            raise ReportError("privacy-mulligan did not exercise both handoffs")
    if scenario in {"full-match", "terminal-restart"}:
        if steps == 0 or result == 0 or disposed == 0:
            raise ReportError(f"{scenario} did not reach and dispose a terminal match")
    if scenario == "full-match":
        if actions != FULL_MATCH_ACTIONS:
            raise ReportError(
                "full-match must successfully submit every frozen ActionKind"
            )
        if turns == 0:
            raise ReportError("full-match did not complete an end-turn transition")
        if reveals < 2:
            raise ReportError("full-match did not exercise a hot-seat handoff")
        if resolving_frames < 2:
            raise ReportError(
                "every command must keep the neutral resolving projection visible for two frames"
            )
        if restarts < 1:
            raise ReportError("full-match did not restart through the result overlay")
        if surrender_terminals < 1:
            raise ReportError("full-match did not reach a terminal result by surrender")
        if disposed < 2:
            raise ReportError("full-match did not dispose both the natural and restarted sessions")
        _true(report, "signal_e2e")
        _true(report, "click_drag_canonical_parity")
        _true(report, "selection_commit_without_confirmation")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--report", required=True, type=Path)
    parser.add_argument("--scenario", choices=sorted(SCENARIOS))
    args = parser.parse_args()

    try:
        report_path = args.report.resolve(strict=True)
        report = json.loads(report_path.read_text(encoding="utf-8"))
        validate(report, args.scenario)
    except (OSError, UnicodeError, json.JSONDecodeError, ReportError) as error:
        print(f"Gate 3C report validation failed: {error}", file=sys.stderr)
        return 1

    print(f"validated Gate 3C report: {report_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
