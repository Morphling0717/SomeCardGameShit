"""Tests for the Gate 3B UI smoke report contract."""

from __future__ import annotations

import copy
import unittest

from scripts.ci.validate_gate3b_report import ReportError, validate


def valid_report() -> dict[str, object]:
    return {
        "schema_version": 1,
        "scenario": "full-match",
        "seed": 0xC0DEC0DE,
        "player0_deck": "midrange",
        "player1_deck": "advance",
        "first_player": 0,
        "steps": 42,
        "turns": 12,
        "action_kinds": list(range(10)),
        "covers": 57,
        "reveals": 14,
        "premature_view_calls": 0,
        "result": 1,
        "disposed_sessions": 1,
    }


class Gate3bReportTests(unittest.TestCase):
    def test_valid_terminal_report(self) -> None:
        validate(valid_report(), "full-match")

    def test_rejects_sensitive_or_unexpected_fields(self) -> None:
        report = valid_report()
        report["card_name"] = "secret"
        with self.assertRaisesRegex(ReportError, "unexpected"):
            validate(report)

    def test_rejects_premature_view_call(self) -> None:
        report = valid_report()
        report["premature_view_calls"] = 1
        with self.assertRaisesRegex(ReportError, "before reveal"):
            validate(report)

    def test_privacy_scenario_requires_both_handoffs(self) -> None:
        report = copy.deepcopy(valid_report())
        report.update(
            scenario="privacy-mulligan",
            result=0,
            covers=1,
            reveals=1,
            disposed_sessions=0,
            action_kinds=[0],
        )
        with self.assertRaisesRegex(ReportError, "both handoffs"):
            validate(report)

    def test_full_match_requires_complete_non_surrender_coverage(self) -> None:
        report = valid_report()
        report["action_kinds"] = list(range(9))
        with self.assertRaisesRegex(ReportError, "every non-surrender"):
            validate(report, "full-match")

    def test_full_match_requires_turn_handoff_and_cover_per_command(self) -> None:
        for field, value, message in (
            ("turns", 0, "end-turn"),
            ("reveals", 1, "handoff"),
            ("covers", 42, "cover every"),
        ):
            with self.subTest(field=field):
                report = valid_report()
                report[field] = value
                with self.assertRaisesRegex(ReportError, message):
                    validate(report, "full-match")


if __name__ == "__main__":
    unittest.main()
