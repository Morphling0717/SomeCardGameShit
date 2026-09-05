"""Tests for the Gate 3C direct-interaction smoke report contract."""

from __future__ import annotations

import copy
import json
import unittest
from pathlib import Path

from scripts.ci.validate_gate3c_report import EXPECTED_FIELDS, ReportError, validate


ROOT = Path(__file__).resolve().parents[2]


def valid_report() -> dict[str, object]:
    return {
        "schema_version": 2,
        "gate": "3C",
        "scenario": "full-match",
        "seed": 0xC0DEC0DE,
        "player0_deck": "midrange",
        "player1_deck": "advance",
        "first_player": 0,
        "steps": 42,
        "turns": 12,
        "action_kinds": list(range(11)),
        "covers": 14,
        "reveals": 14,
        "premature_view_calls": 0,
        "signal_e2e": True,
        "click_drag_canonical_parity": True,
        "selection_commit_without_confirmation": True,
        "resolving_public_frames": 2,
        "resolving_private_leaks": 0,
        "restarts": 1,
        "surrender_terminals": 1,
        "result": 1,
        "disposed_sessions": 2,
    }


class Gate3cReportTests(unittest.TestCase):
    def test_cmake_preserves_gate3b_and_adds_gate3c_contracts(self) -> None:
        cmake = (ROOT / "CMakeLists.txt").read_text(encoding="utf-8")
        self.assertEqual(1, cmake.count("NAME scgs_gate3b_report_contract"))
        self.assertEqual(1, cmake.count("NAME scgs_gate3c_report_contract"))
        self.assertIn(
            "COMMAND ${Python3_EXECUTABLE} -m unittest "
            "scripts.tests.test_validate_gate3b_report",
            cmake,
        )
        self.assertIn(
            "COMMAND ${Python3_EXECUTABLE} -m unittest "
            "scripts.tests.test_validate_gate3c_report",
            cmake,
        )

    def test_testing_guide_names_every_gate3c_report_field(self) -> None:
        guide = (ROOT / "docs/testing.md").read_text(encoding="utf-8")
        for field in EXPECTED_FIELDS:
            with self.subTest(field=field):
                self.assertIn(f"`{field}`", guide)
        self.assertIn(
            "`resolving_public_frames` 是所有命令中观察到的完整公共投影帧数最小值",
            guide,
        )

    def test_historical_fixture_preserves_the_exact_frozen_whitelist(self) -> None:
        fixture = json.loads((ROOT / "scripts/tests/fixtures/legacy-reports/gate3c.example.json").read_text(encoding="utf-8"))
        self.assertEqual(EXPECTED_FIELDS, set(fixture))
        self.assertEqual(valid_report(), fixture)
        validate(fixture, "full-match")
        # Product startup no longer has a frozen-v04 report producer.
        source = (ROOT / "client/godot/scripts/Bootstrap/BootstrapController.cs").read_text(encoding="utf-8")
        self.assertNotIn("record Gate3CSmokeReport", source)

    def test_valid_terminal_report(self) -> None:
        validate(valid_report(), "full-match")

    def test_rejects_sensitive_or_unexpected_fields(self) -> None:
        report = valid_report()
        report["card_name"] = "secret"
        with self.assertRaisesRegex(ReportError, "unexpected"):
            validate(report)

    def test_rejects_wrong_gate_or_schema(self) -> None:
        for field, value, message in (
            ("schema_version", 1, "schema_version"),
            ("schema_version", 2.0, "schema_version"),
            ("gate", "3B", "gate"),
        ):
            with self.subTest(field=field):
                report = valid_report()
                report[field] = value
                with self.assertRaisesRegex(ReportError, message):
                    validate(report)

    def test_rejects_non_string_scenario_and_decks(self) -> None:
        for field in ("scenario", "player0_deck", "player1_deck"):
            with self.subTest(field=field):
                report = valid_report()
                report[field] = []
                with self.assertRaises(ReportError):
                    validate(report)

    def test_rejects_viewer_or_resolving_privacy_leak(self) -> None:
        for field, message in (
            ("premature_view_calls", "before reveal"),
            ("resolving_private_leaks", "private data"),
        ):
            with self.subTest(field=field):
                report = valid_report()
                report[field] = 1
                with self.assertRaisesRegex(ReportError, message):
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
            resolving_public_frames=0,
        )
        with self.assertRaisesRegex(ReportError, "both handoffs"):
            validate(report)

    def test_full_match_requires_complete_action_coverage(self) -> None:
        report = valid_report()
        report["action_kinds"] = list(range(10))
        with self.assertRaisesRegex(ReportError, "every frozen"):
            validate(report, "full-match")

    def test_full_match_requires_turn_handoff_and_two_resolving_frames(self) -> None:
        for field, value, message in (
            ("turns", 0, "end-turn"),
            ("reveals", 1, "handoff"),
            ("resolving_public_frames", 1, "two frames"),
        ):
            with self.subTest(field=field):
                report = valid_report()
                report[field] = value
                with self.assertRaisesRegex(ReportError, message):
                    validate(report, "full-match")

    def test_full_match_requires_real_direct_interaction_evidence(self) -> None:
        for field in (
            "signal_e2e",
            "click_drag_canonical_parity",
            "selection_commit_without_confirmation",
        ):
            with self.subTest(field=field):
                report = valid_report()
                report[field] = False
                with self.assertRaisesRegex(ReportError, field):
                    validate(report, "full-match")

    def test_full_match_requires_restart_surrender_and_two_disposals(self) -> None:
        for field, value, message in (
            ("restarts", 0, "restart"),
            ("surrender_terminals", 0, "surrender"),
            ("disposed_sessions", 1, "both"),
        ):
            with self.subTest(field=field):
                report = valid_report()
                report[field] = value
                with self.assertRaisesRegex(ReportError, message):
                    validate(report, "full-match")

    def test_boolean_evidence_rejects_integer_one(self) -> None:
        report = valid_report()
        report["signal_e2e"] = 1
        with self.assertRaisesRegex(ReportError, "signal_e2e"):
            validate(report, "full-match")

    def test_boolean_shape_is_strict_for_every_scenario(self) -> None:
        report = valid_report()
        report.update(
            scenario="resources",
            signal_e2e="true",
            action_kinds=[],
            steps=0,
            turns=0,
            covers=0,
            reveals=0,
            resolving_public_frames=0,
            result=0,
            disposed_sessions=0,
            restarts=0,
            surrender_terminals=0,
        )
        with self.assertRaisesRegex(ReportError, "signal_e2e"):
            validate(report, "resources")


if __name__ == "__main__":
    unittest.main()
