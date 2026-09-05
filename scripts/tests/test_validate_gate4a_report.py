"""Tests for the Gate 4A 3D/legacy presentation smoke report contract."""

from __future__ import annotations

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from scripts.ci.validate_gate4a_report import EXPECTED_FIELDS, ReportError, validate


ROOT = Path(__file__).resolve().parents[2]


def valid_report(presentation: str = "3d") -> dict[str, object]:
    is_3d = presentation == "3d"
    return {
        "schema_version": 3,
        "gate": "4A",
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
        "presentation_mode": presentation,
        "surface_intent_e2e": True,
        "raycast_e2e": is_3d,
        "hud_raycast_blocks": 1 if is_3d else 0,
        "drag_threshold_pixels": 8 if is_3d else 0,
        "camera_fov_degrees": 70 if is_3d else 0,
        "camera_pitch_degrees": 58 if is_3d else 0,
        "perspective_rebuilds": 2 if is_3d else 0,
        "actor_pool_reuses": 1 if is_3d else 0,
        "blocked_spatial_inputs": 3 if is_3d else 0,
        "spatial_private_leaks": 0,
    }


class Gate4aReportTests(unittest.TestCase):
    def test_schema_v3_has_exactly_33_fields(self) -> None:
        self.assertEqual(33, len(EXPECTED_FIELDS))

    def test_cmake_preserves_gate3b_gate3c_and_adds_gate4a_contracts(self) -> None:
        cmake = (ROOT / "CMakeLists.txt").read_text(encoding="utf-8")
        for gate in ("gate3b", "gate3c", "gate4a"):
            with self.subTest(gate=gate):
                self.assertEqual(1, cmake.count(f"NAME scgs_{gate}_report_contract"))
                self.assertIn(
                    "COMMAND ${Python3_EXECUTABLE} -m unittest "
                    f"scripts.tests.test_validate_{gate}_report",
                    cmake,
                )

    def test_testing_guide_names_every_gate4a_report_field(self) -> None:
        guide = (ROOT / "docs/testing.md").read_text(encoding="utf-8")
        for field in EXPECTED_FIELDS:
            with self.subTest(field=field):
                self.assertIn(f"`{field}`", guide)
        self.assertIn("`--legacy-2d-board`", guide)
        self.assertIn("`presentation_mode=\"3d\"`", guide)
        self.assertIn("`presentation_mode=\"legacy-2d\"`", guide)

    def test_historical_fixture_preserves_the_exact_frozen_whitelist(self) -> None:
        fixture = json.loads((ROOT / "scripts/tests/fixtures/legacy-reports/gate4a.example.json").read_text(encoding="utf-8"))
        self.assertEqual(EXPECTED_FIELDS, set(fixture))
        self.assertEqual(valid_report(), fixture)
        validate(fixture, "full-match", "3d")
        source = (ROOT / "client/godot/scripts/Bootstrap/BootstrapController.cs").read_text(encoding="utf-8")
        self.assertNotIn("record Gate4ASmokeReport", source)

    def test_valid_default_3d_full_match(self) -> None:
        validate(valid_report(), "full-match", "3d")

        gate4b_framing = valid_report()
        gate4b_framing["camera_fov_degrees"] = 58
        validate(gate4b_framing, "full-match", "3d")

    def test_valid_legacy_2d_full_match(self) -> None:
        validate(valid_report("legacy-2d"), "full-match", "legacy-2d")

    def test_command_line_entrypoint_runs_from_repository_root(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            report_path = Path(temporary) / "gate4a.json"
            report_path.write_text(
                json.dumps(valid_report()),
                encoding="utf-8",
            )
            completed = subprocess.run(
                (
                    sys.executable,
                    "scripts/ci/validate_gate4a_report.py",
                    "--report",
                    str(report_path),
                    "--scenario",
                    "full-match",
                    "--presentation",
                    "3d",
                ),
                cwd=ROOT,
                capture_output=True,
                check=False,
                text=True,
                timeout=10,
            )
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertIn("validated Gate 4A report", completed.stdout)

    def test_rejects_sensitive_or_unexpected_fields(self) -> None:
        report = valid_report()
        report["card_name"] = "secret"
        with self.assertRaisesRegex(ReportError, "unexpected"):
            validate(report)

    def test_rejects_wrong_gate_schema_or_presentation(self) -> None:
        for field, value, message in (
            ("schema_version", 2, "schema_version"),
            ("schema_version", 3.0, "schema_version"),
            ("gate", "3C", "gate"),
            ("presentation_mode", "2d", "presentation_mode"),
        ):
            with self.subTest(field=field):
                report = valid_report()
                report[field] = value
                with self.assertRaisesRegex(ReportError, message):
                    validate(report)

        with self.assertRaisesRegex(ReportError, "presentation mismatch"):
            validate(valid_report(), "full-match", "legacy-2d")

    def test_preserves_every_gate3c_full_match_invariant(self) -> None:
        for field, value, message in (
            ("action_kinds", list(range(10)), "every frozen"),
            ("premature_view_calls", 1, "before reveal"),
            ("resolving_private_leaks", 1, "private data"),
            ("resolving_public_frames", 1, "two frames"),
            ("disposed_sessions", 1, "both"),
        ):
            with self.subTest(field=field):
                report = valid_report()
                report[field] = value
                with self.assertRaisesRegex(ReportError, message):
                    validate(report, "full-match", "3d")

    def test_3d_requires_surface_raycast_hud_pool_perspective_and_lock_evidence(
        self,
    ) -> None:
        for field, value, message in (
            ("surface_intent_e2e", False, "surface-intent"),
            ("raycast_e2e", False, "raycast"),
            ("hud_raycast_blocks", 0, "HUD"),
            ("perspective_rebuilds", 0, "perspective"),
            ("actor_pool_reuses", 0, "actor-pool"),
            ("blocked_spatial_inputs", 0, "locked spatial"),
        ):
            with self.subTest(field=field):
                report = valid_report()
                report[field] = value
                with self.assertRaisesRegex(ReportError, message):
                    validate(report, "full-match", "3d")

    def test_3d_camera_and_drag_constants_are_frozen(self) -> None:
        for field, value, message in (
            ("drag_threshold_pixels", 7, "8 pixels"),
            ("camera_fov_degrees", 69, "70 FOV"),
            ("camera_pitch_degrees", 57, "58 pitch"),
        ):
            with self.subTest(field=field):
                report = valid_report()
                report[field] = value
                with self.assertRaisesRegex(ReportError, message):
                    validate(report, "full-match", "3d")

    def test_legacy_2d_rejects_3d_only_evidence(self) -> None:
        for field, value in (
            ("raycast_e2e", True),
            ("hud_raycast_blocks", 1),
            ("drag_threshold_pixels", 8),
            ("camera_fov_degrees", 70),
            ("camera_pitch_degrees", 58),
            ("perspective_rebuilds", 1),
            ("actor_pool_reuses", 1),
            ("blocked_spatial_inputs", 1),
        ):
            with self.subTest(field=field):
                report = valid_report("legacy-2d")
                report[field] = value
                with self.assertRaises(ReportError):
                    validate(report, "full-match", "legacy-2d")

        report = valid_report("legacy-2d")
        report["surface_intent_e2e"] = False
        with self.assertRaisesRegex(ReportError, "surface-intent"):
            validate(report, "full-match", "legacy-2d")

    def test_spatial_privacy_and_numeric_shapes_are_strict(self) -> None:
        report = valid_report()
        report["spatial_private_leaks"] = 1
        with self.assertRaisesRegex(ReportError, "spatial presentation"):
            validate(report)

        report = valid_report()
        report["hud_raycast_blocks"] = True
        with self.assertRaisesRegex(ReportError, "integer"):
            validate(report)

        report = valid_report()
        report["raycast_e2e"] = 1
        with self.assertRaisesRegex(ReportError, "boolean"):
            validate(report)


if __name__ == "__main__":
    unittest.main()
