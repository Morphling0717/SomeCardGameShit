"""Product UI evidence cannot be replaced by native, legacy or preview reports."""
from __future__ import annotations

import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import unittest

from scripts.ci.validate_product_smoke_report import EXPECTED_FIELDS, ReportError, validate

ROOT = Path(__file__).resolve().parents[2]


def valid_report(full: bool = True) -> dict[str, object]:
    actions = [2] * 14 if full else [2, 3, 2, 0, 4, 0, 0, 0, 0, 12, 0, 0, 0, 0]
    actions[10] = 2 if full else 0
    return {
        "schema_version": 1, "suite": "product-v05-ui", "api": "scgs_v05",
        "abi_major": 2, "engine_schema": 2,
        "product_scene": "res://scenes/match/ProductMatch.tscn", "visual_profile": "anime-v1",
        "run_kind": "source" if full else "zip", "coverage": "full-ui" if full else "natural-ui",
        "frame_clock": "frame-post-draw", "viewport_width": 1600, "viewport_height": 900,
        "player0_deck": "oathguard_luminous_oath_v1", "player1_deck": "pactmage_abyssal_pact_v1",
        "pointer_inputs": 70, "spatial_inputs": 40, "keyboard_inputs": 10,
        "invalid_drag_owner_checks": 1 if full else 0, "invalid_drag_zone_checks": 1 if full else 0,
        "selection_back_checks": 2 if full else 0,
        "reaction_surrender_checks": 1 if full else 0,
        "choice_surrender_checks": 1 if full else 0,
        "commands": sum(actions), "action_counts": actions,
        "natural_terminals": 1, "surrender_terminals": 2 if full else 0,
        "restarts": 2 if full else 0, "disposed_sessions": 3 if full else 1,
        "covered_samples": 15, "resolving_samples": 80, "minimum_public_frames": 2,
        "premature_view_reads": 0, "private_state_leaks": 0, "unattributed_commands": 0,
        "unauthorized_private_queries": 0, "scheduling_queries": 0,
        "engine_failures": 0, "terminal_event_checks": 3 if full else 1,
        "terminal_result": 1, "final_revision": 80, "success": True,
    }


class ProductSmokeReportTests(unittest.TestCase):
    def test_full_source_and_natural_zip_are_distinct_valid_contracts(self) -> None:
        validate(valid_report(), "source", "full-ui", True)
        validate(valid_report(False), "zip", "natural-ui")

    def test_csharp_report_matches_exact_identity_free_whitelist(self) -> None:
        source = (ROOT / "client/godot/scripts/Ci/ProductSmokeReport.cs").read_text(encoding="utf-8")
        self.assertEqual(EXPECTED_FIELDS, set(re.findall(r'JsonPropertyName\("([^"]+)"\)', source)))
        self.assertNotIn("seed", EXPECTED_FIELDS)

    def test_all_unknown_or_private_fields_are_rejected(self) -> None:
        for field in ("seed", "hand", "card", "instance_id", "selected_option_ids", "legacy_evidence"):
            with self.subTest(field=field):
                report = valid_report()
                report[field] = "private sentinel"
                with self.assertRaises(ReportError): validate(report)

    def test_every_missing_field_is_rejected(self) -> None:
        for field in EXPECTED_FIELDS:
            with self.subTest(field=field):
                report = valid_report()
                del report[field]
                with self.assertRaises(ReportError): validate(report)

    def test_v04_preview_and_industrial_visuals_cannot_pass(self) -> None:
        for field, value in (("api", "scgs_v04"), ("engine_schema", 1), ("abi_major", 1),
                             ("product_scene", "res://scenes/preview/AnimeStyleSlice.tscn"),
                             ("visual_profile", "r3-candidate"), ("player0_deck", "midrange")):
            with self.subTest(field=field):
                report = valid_report(); report[field] = value
                with self.assertRaises(ReportError): validate(report)

    def test_each_of_fourteen_actions_is_required_for_full_ui(self) -> None:
        for kind in range(14):
            with self.subTest(kind=kind):
                report = valid_report(); report["action_counts"][kind] = 0
                report["commands"] = sum(report["action_counts"])
            with self.assertRaises(ReportError): validate(report)

    def test_invalid_drop_and_esc_evidence_is_required_for_full_ui(self) -> None:
        for field in ("invalid_drag_owner_checks", "invalid_drag_zone_checks", "selection_back_checks", "reaction_surrender_checks", "choice_surrender_checks"):
            with self.subTest(field=field):
                report = valid_report(); report[field] = 0
                with self.assertRaises(ReportError): validate(report)

    def test_counts_must_be_real_integers_not_booleans_or_strings(self) -> None:
        for value in (True, "2", 2.0, -1, None):
            report = valid_report(); report["commands"] = value
            with self.subTest(value=value), self.assertRaises(ReportError): validate(report)
        report = valid_report(); report["schema_version"] = True
        with self.assertRaises(ReportError): validate(report)

    def test_action_vector_length_and_individual_types_are_checked(self) -> None:
        for actions in ([1] * 11, [1] * 15, [True] * 14, {}, "all"):
            report = valid_report(); report["action_counts"] = actions
            with self.subTest(actions=actions), self.assertRaises(ReportError): validate(report)

    def test_counter_consistency_prevents_claiming_unobserved_ui_commands(self) -> None:
        for field, value in (("commands", 900), ("pointer_inputs", 0), ("spatial_inputs", 0),
                             ("keyboard_inputs", 0), ("restarts", 0), ("disposed_sessions", 0),
                             ("terminal_event_checks", 0), ("natural_terminals", 0)):
            report = valid_report(); report[field] = value
            with self.subTest(field=field), self.assertRaises(ReportError): validate(report)

    def test_every_privacy_error_is_fatal(self) -> None:
        for field in ("premature_view_reads", "unauthorized_private_queries", "private_state_leaks", "unattributed_commands", "engine_failures"):
            report = valid_report(); report[field] = 1
            with self.subTest(field=field), self.assertRaises(ReportError): validate(report)

    def test_public_frames_and_reveal_samples_are_mandatory(self) -> None:
        for field in ("minimum_public_frames", "covered_samples", "resolving_samples"):
            report = valid_report(); report[field] = 1
            with self.subTest(field=field), self.assertRaises(ReportError): validate(report)

    def test_headless_result_is_not_gpu_evidence(self) -> None:
        report = valid_report(); report["frame_clock"] = "process-frame"
        validate(report)
        with self.assertRaises(ReportError): validate(report, require_display=True)

    def test_wrong_artifact_or_coverage_cannot_reuse_report(self) -> None:
        with self.assertRaises(ReportError): validate(valid_report(False), "source", "full-ui")
        with self.assertRaises(ReportError): validate(valid_report(), "zip", "natural-ui")

    def test_terminal_and_success_must_be_real(self) -> None:
        for field, value in (("terminal_result", 0), ("final_revision", 0), ("success", 1), ("success", False)):
            report = valid_report(); report[field] = value
            with self.subTest(field=field), self.assertRaises(ReportError): validate(report)

    def test_ui_adapter_never_invokes_game_selection_or_synthetic_signals(self) -> None:
        source = (ROOT / "client/godot/scripts/Match/ProductMatchScreen.Ci.cs").read_text(encoding="utf-8")
        for forbidden in (".SelectLegalAction(", ".SubmitCommand(", ".SubmitPreparedCommand(",
                          ".BeginSourceSelection(", ".BeginActionSelection(", ".EmitSignal("):
            self.assertNotIn(forbidden, source)
        runner = (ROOT / "client/godot/scripts/Ci/ProductSmokeRunner.cs").read_text(encoding="utf-8")
        self.assertIn("Input.ParseInputEvent", runner)
        self.assertIn("CiTryGetScreenAnchor", source)
        self.assertIn("GetFocusModeWithOverride() != Control.FocusModeEnum.None", runner)
        self.assertIn("focused.HasFocus()", runner)
        self.assertIn("product-smoke-failure.json", runner)
        self.assertIn("ShuffleDecks = matchIndex != 0", runner)

    def test_cli_rejects_duplicate_fields_and_oversized_reports(self) -> None:
        script = ROOT / "scripts/ci/validate_product_smoke_report.py"
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "report.json"
            payloads = [json.dumps(valid_report())[:-1] + ', "success": true}', " " * 65537]
            for payload in payloads:
                path.write_text(payload, encoding="utf-8")
                result = subprocess.run([sys.executable, str(script), "--report", str(path)], capture_output=True)
                self.assertNotEqual(0, result.returncode)


if __name__ == "__main__":
    unittest.main()
