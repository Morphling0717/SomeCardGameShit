# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import copy
import hashlib
import json
import struct
import tempfile
import unittest
import zlib
from pathlib import Path

from scripts.ci.validate_r3_visual_slice import (
    R3VisualSliceError,
    _expected_build_identity,
    validate_report,
)


ROOT = Path(__file__).resolve().parents[2]
STATES = ("action-idle", "hand-hover", "source-selected")


def _chunk(kind: bytes, payload: bytes) -> bytes:
    return (
        struct.pack(">I", len(payload))
        + kind
        + payload
        + struct.pack(">I", zlib.crc32(kind + payload) & 0xFFFFFFFF)
    )


def _write_png(path: Path, color: tuple[int, int, int], width: int = 4, height: int = 3) -> None:
    row = bytes(color) * width
    encoded = b"".join(b"\0" + row for _ in range(height))
    path.write_bytes(
        b"\x89PNG\r\n\x1a\n"
        + _chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
        + _chunk(b"IDAT", zlib.compress(encoded))
        + _chunk(b"IEND", b"")
    )


def _valid_fixture(directory: Path) -> tuple[Path, dict[str, object]]:
    colors = ((32, 40, 48), (72, 81, 93), (114, 92, 61))
    captures: list[dict[str, object]] = []
    for state, color in zip(STATES, colors, strict=True):
        filename = f"{state}.png"
        path = directory / filename
        _write_png(path, color)
        captures.append(
            {
                "state": state,
                "viewer": 0,
                "revision": 2,
                "width": 4,
                "height": 3,
                "file": filename,
                "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                "stable_frame_post_draws": 2,
                "frame_pair_mae": 0.0,
                "privacy_sentinel_absent": True,
            }
        )
    _write_png(directory / "privacy-resolving.png", (26, 33, 41))
    _write_png(directory / "privacy-covered.png", (18, 24, 31))
    commit, commit_source, dirty = _expected_build_identity()
    assert dirty is not None
    product_manifest = ROOT / "client/godot/assets/visual/ASSET_MANIFEST.json"
    candidate_manifest = (
        ROOT / "client/godot/assets/visual/arena/R3_ASSET_MANIFEST.json"
    )
    floor = ROOT / "client/godot/assets/visual/arena/r3_industrial_floor_albedo.png"
    glb = ROOT / "client/godot/assets/visual/arena/r3_arena_machinery.glb"
    shader = ROOT / "client/godot/assets/visual/r3/r3_industrial_floor.gdshader"
    launcher = ROOT / "scripts/ci/PLAY_R3_VISUAL_SLICE.cmd"
    report: dict[str, object] = {
        "schema_version": 1,
        "gate": "R3",
        "scenario": "visual-slice",
        "arena_profile": "r3-candidate",
        "approval_status": "pending_user_approval",
        "session_setup": {
            "seed": 0xC0DEC0DE,
            "first_player": 0,
            "shuffle_decks": False,
        },
        "final_revision": 2,
        "provenance": {
            "commit_sha": commit,
            "commit_source": commit_source,
            "working_tree_dirty": dirty,
            "product_asset_manifest": {
                "resource_path": "res://assets/visual/ASSET_MANIFEST.json",
                "sha256": hashlib.sha256(product_manifest.read_bytes()).hexdigest(),
                "asset_count": 34,
            },
            "candidate_asset_manifest": {
                "resource_path": "res://assets/visual/arena/R3_ASSET_MANIFEST.json",
                "sha256": hashlib.sha256(candidate_manifest.read_bytes()).hexdigest(),
                "asset_count": 1,
            },
            "candidate_floor_sha256": hashlib.sha256(floor.read_bytes()).hexdigest(),
            "candidate_glb_sha256": hashlib.sha256(glb.read_bytes()).hexdigest(),
            "candidate_shader_sha256": hashlib.sha256(shader.read_bytes()).hexdigest(),
            "launcher_sha256": hashlib.sha256(launcher.read_bytes()).hexdigest(),
        },
        "capture_contract": {
            "frame_post_draws": 2,
            "pixel_space": "srgb8",
            "maximum_frame_pair_mae": 0.01,
        },
        "session_evidence": {
            "session_interface": "IScgsGameSession",
            "session_runtime_type": "Scgs.Client.ScgsGameSession",
            "state_source": "HotseatUiState",
            "legal_actions_source": "HotseatUiState.LegalActions",
            "successful_mulligan_submissions": 2,
            "final_legal_action_count": 12,
            "selected_action_kind": 1,
            "selected_source": 101,
        },
        "privacy_evidence": {
            "opaque_cover_before_first_view": True,
            "viewer_request_order": [0, 1, 0],
            "explicit_reveal_count": 3,
            "snapshot_request_count": 5,
            "viewer_read_request_count": 20,
            "premature_view_calls": 0,
            "gpu_sentinel_detector_self_test_passed": True,
            "injected_sentinel_exercised": True,
            "injected_sentinel_runtime_scrub_verified": True,
            "candidate_captures_sentinel_absent": True,
            "hidden_card_shared_back": True,
            "hidden_card_count": 5,
            "injected_transition": {
                "source_action_kind": 0,
                "source_viewer": 0,
                "source_revision": 0,
                "result_revision": 1,
                "resolving": {
                    "mode": "Resolving",
                    "revision": 0,
                    "width": 4,
                    "height": 3,
                    "file": "privacy-resolving.png",
                    "sha256": hashlib.sha256(
                        (directory / "privacy-resolving.png").read_bytes()
                    ).hexdigest(),
                    "complete_frame_post_draws": 1,
                    "snapshot_requests_before": 1,
                    "snapshot_requests_after": 1,
                    "viewer_reads_before": 4,
                    "viewer_reads_after": 4,
                    "privacy_sentinel_absent": True,
                },
                "covered": {
                    "mode": "Covered",
                    "revision": 1,
                    "width": 4,
                    "height": 3,
                    "file": "privacy-covered.png",
                    "sha256": hashlib.sha256(
                        (directory / "privacy-covered.png").read_bytes()
                    ).hexdigest(),
                    "complete_frame_post_draws": 1,
                    "snapshot_requests_before": 2,
                    "snapshot_requests_after": 2,
                    "viewer_reads_before": 9,
                    "viewer_reads_after": 9,
                    "privacy_sentinel_absent": True,
                },
            },
            "scrub": {
                "private_text_cleared": True,
                "private_metadata_cleared": True,
                "private_material_cleared": True,
                "collisions_disabled": True,
                "drag_tokens_cleared": True,
                "tweens_cancelled": True,
                "callbacks_cleared": True,
                "resolving_private_leak_count": 0,
                "spatial_private_leak_count": 0,
                "forbidden_sentinel_token_count": 0,
            },
        },
        "viewport": {"width": 4, "height": 3},
        "captures": captures,
    }
    report_path = directory / "r3-visual-slice.json"
    report_path.write_text(json.dumps(report), encoding="utf-8")
    return report_path, report


class R3VisualSliceTests(unittest.TestCase):
    def test_candidate_arena_is_open_original_and_not_the_product_default(self) -> None:
        scene_path = ROOT / "client/godot/scenes/battlefield/R3ArenaCandidate.tscn"
        scene = scene_path.read_text(encoding="utf-8")
        profile = (
            ROOT / "client/godot/scripts/Battlefield/BattlefieldVisualProfile.cs"
        ).read_text(encoding="utf-8")
        match = (ROOT / "client/godot/scripts/Match/MatchScreen.cs").read_text(
            encoding="utf-8"
        )
        self.assertEqual(1, scene.count('[node name="AuthoredArena" type="Node3D"]'))
        self.assertIn("size = Vector2(80, 60)", scene)
        self.assertIn("r3_arena_machinery.glb", scene)
        for forbidden in (
            "TableBase",
            "Territory",
            "ZoneBay",
            "ArenaRim",
            "Perimeter",
            'type="Camera3D"',
            'type="CollisionShape3D"',
        ):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, scene)
        self.assertIn("R3CandidateScenePath", profile)
        self.assertIn("UsesOpenArena: true", profile)
        self.assertIn(
            "private BattlefieldVisualProfile _visualProfile = BattlefieldVisualProfile.Gate4BR2;",
            match,
        )

        arena = ROOT / "client/godot/assets/visual/arena"
        glb = arena / "r3_arena_machinery.glb"
        generator = arena / "source/generate_r3_arena_machinery.py"
        self.assertEqual(
            "4ce416e3828dbcdbdf94b407c7f800144497af5afb5f2801bd08b35b267c9108",
            hashlib.sha256(glb.read_bytes()).hexdigest(),
        )
        self.assertEqual(
            "927b099463c0a0c634a25bd75fe40c780f3b83ffadfd91be61291a34038395f3",
            hashlib.sha256(generator.read_bytes()).hexdigest(),
        )

    def test_candidate_floor_is_isolated_from_approved_product_manifest(self) -> None:
        product_path = ROOT / "client/godot/assets/visual/ASSET_MANIFEST.json"
        candidate_path = (
            ROOT / "client/godot/assets/visual/arena/R3_ASSET_MANIFEST.json"
        )
        product = json.loads(product_path.read_text(encoding="utf-8"))
        candidate_manifest = json.loads(candidate_path.read_text(encoding="utf-8"))
        self.assertEqual(34, len(product["assets"]))
        self.assertNotIn(
            "client/godot/assets/visual/arena/r3_industrial_floor_albedo.png",
            {entry["path"] for entry in product["assets"]},
        )
        self.assertEqual(1, candidate_manifest["schema_version"])
        self.assertEqual("4B-R3.1", candidate_manifest["gate"])
        self.assertEqual(1, len(candidate_manifest["assets"]))
        candidate = candidate_manifest["assets"][0]
        floor = ROOT / candidate["path"]
        self.assertEqual(
            candidate["sha256"],
            hashlib.sha256(floor.read_bytes()).hexdigest(),
        )

    def test_accepts_real_session_candidate_contract(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            report_path, _ = _valid_fixture(Path(temporary))
            report = validate_report(report_path, expected_width=4, expected_height=3)
            self.assertEqual("pending_user_approval", report["approval_status"])

    def test_rejects_duplicate_state_pngs_even_when_hash_metadata_matches(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path, report = _valid_fixture(directory)
            duplicate = (directory / "action-idle.png").read_bytes()
            (directory / "hand-hover.png").write_bytes(duplicate)
            captures = report["captures"]
            assert isinstance(captures, list)
            captures[1]["sha256"] = hashlib.sha256(duplicate).hexdigest()
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(R3VisualSliceError, "distinct PNG"):
                validate_report(report_path)

    def test_rejects_unstable_frame_pair(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            report_path, report = _valid_fixture(Path(temporary))
            captures = report["captures"]
            assert isinstance(captures, list)
            captures[2]["frame_pair_mae"] = 0.010001
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(R3VisualSliceError, "frame_pair_mae"):
                validate_report(report_path)

    def test_rejects_gpu_privacy_sentinel_even_if_report_claims_absent(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path, report = _valid_fixture(directory)
            path = directory / "source-selected.png"
            _write_png(path, (255, 0, 255))
            captures = report["captures"]
            assert isinstance(captures, list)
            captures[2]["sha256"] = hashlib.sha256(path.read_bytes()).hexdigest()
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(R3VisualSliceError, "#ff00ff"):
                validate_report(report_path)

    def test_rejects_missing_real_injection_even_when_detector_self_test_passes(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            report_path, report = _valid_fixture(Path(temporary))
            privacy = report["privacy_evidence"]
            assert isinstance(privacy, dict)
            privacy["injected_sentinel_exercised"] = False
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(R3VisualSliceError, "injected_sentinel_exercised"):
                validate_report(report_path)

    def test_rejects_injected_sentinel_in_resolving_gpu_frame(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path, report = _valid_fixture(directory)
            path = directory / "privacy-resolving.png"
            _write_png(path, (255, 0, 255))
            privacy = report["privacy_evidence"]
            assert isinstance(privacy, dict)
            transition = privacy["injected_transition"]
            assert isinstance(transition, dict)
            resolving = transition["resolving"]
            assert isinstance(resolving, dict)
            resolving["sha256"] = hashlib.sha256(path.read_bytes()).hexdigest()
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(R3VisualSliceError, "injected GPU sentinel"):
                validate_report(report_path)

    def test_rejects_viewer_read_during_resolving_or_covered(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            for state in ("resolving", "covered"):
                for metric in ("snapshot_requests", "viewer_reads"):
                    with self.subTest(state=state, metric=metric):
                        report_path, report = _valid_fixture(directory)
                        privacy = report["privacy_evidence"]
                        assert isinstance(privacy, dict)
                        transition = privacy["injected_transition"]
                        assert isinstance(transition, dict)
                        frame = transition[state]
                        assert isinstance(frame, dict)
                        frame[f"{metric}_after"] += 1
                        report_path.write_text(json.dumps(report), encoding="utf-8")
                        with self.assertRaisesRegex(
                            R3VisualSliceError, f"{metric}_after"
                        ):
                            validate_report(report_path)

    def test_rejects_revision_drift_or_unbound_provenance(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            for mutation, message in (
                (("final_revision",), "exactly 2"),
                (("provenance", "candidate_shader_sha256"), "candidate_shader_sha256"),
            ):
                with self.subTest(field=mutation[-1]):
                    report_path, report = _valid_fixture(directory)
                    if len(mutation) == 1:
                        report[mutation[0]] = 3
                    else:
                        section = report[mutation[0]]
                        assert isinstance(section, dict)
                        section[mutation[1]] = "0" * 64
                    report_path.write_text(json.dumps(report), encoding="utf-8")
                    with self.assertRaisesRegex(R3VisualSliceError, message):
                        validate_report(report_path)

    def test_rejects_black_finite_arena_edge(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path, report = _valid_fixture(directory)
            path = directory / "action-idle.png"
            _write_png(path, (0, 0, 0))
            captures = report["captures"]
            assert isinstance(captures, list)
            captures[0]["sha256"] = hashlib.sha256(path.read_bytes()).hexdigest()
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(R3VisualSliceError, "black finite arena edge"):
                validate_report(report_path)

    def test_rejects_fake_viewer_order_and_legal_action_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            for mutation, message in (
                (("privacy_evidence", "viewer_request_order", [0, 0, 1]), "viewer_request_order"),
                (("session_evidence", "legal_actions_source", "fixture"), "legal_actions_source"),
                (("session_evidence", "successful_mulligan_submissions", 1), "successful_mulligan"),
            ):
                with self.subTest(field=mutation[1]):
                    report_path, original = _valid_fixture(directory)
                    report = copy.deepcopy(original)
                    section = report[mutation[0]]
                    assert isinstance(section, dict)
                    section[mutation[1]] = mutation[2]
                    report_path.write_text(json.dumps(report), encoding="utf-8")
                    with self.assertRaisesRegex(R3VisualSliceError, message):
                        validate_report(report_path)

    def test_rejects_approval_or_profile_promotion(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            report_path, original = _valid_fixture(Path(temporary))
            for field, value, message in (
                ("approval_status", "approved", "pending_user_approval"),
                ("arena_profile", "gate4b-r2", "r3-candidate"),
            ):
                with self.subTest(field=field):
                    report = copy.deepcopy(original)
                    report[field] = value
                    report_path.write_text(json.dumps(report), encoding="utf-8")
                    with self.assertRaisesRegex(R3VisualSliceError, message):
                        validate_report(report_path)

    def test_wiring_is_separate_from_gate4b_schema_and_goldens(self) -> None:
        bootstrap = (ROOT / "client/godot/scripts/Bootstrap/BootstrapController.cs").read_text(
            encoding="utf-8"
        )
        collector = (ROOT / "client/godot/scripts/Ci/GateR3VisualSlice.cs").read_text(
            encoding="utf-8"
        )
        match_screen = (ROOT / "client/godot/scripts/Match/MatchScreen.cs").read_text(
            encoding="utf-8"
        )
        workflow = (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        finalizer = (ROOT / "scripts/ci/finalize_godot_export.py").read_text(
            encoding="utf-8"
        )
        launcher = (ROOT / "scripts/ci/PLAY_R3_VISUAL_SLICE.cmd").read_text(
            encoding="utf-8"
        )
        self.assertIn("--r3-visual-slice", bootstrap)
        self.assertIn("IScgsGameSession", collector)
        self.assertIn("HotseatUiState.LegalActions", collector)
        self.assertIn("pending_user_approval", collector)
        self.assertIn("ArmResolvingPrivacySentinelForCi", collector)
        self.assertIn("privacy-resolving.png", collector)
        self.assertIn("privacy-covered.png", collector)
        self.assertIn("InjectedSentinelRuntimeScrubVerified", collector)
        self.assertIn("ViewerReadRequestCount", collector)
        self.assertIn("ViewerReadRequestCount", match_screen)
        self.assertIn("WindowGetVsyncMode", collector)
        self.assertIn("WindowSetVsyncMode(previousVsyncMode)", collector)
        self.assertIn("_r3VisualSliceCaptureComplete", bootstrap)
        self.assertNotIn("TacticalHudTheme.RestoreGate4BR2(this)", match_screen)
        self.assertIn("ExpectedFinalRevision = 2", collector)
        self.assertIn("R3_ASSET_MANIFEST.json", collector)
        self.assertIn("CandidateShaderSha256", collector)
        self.assertIn("LauncherSha256", collector)
        self.assertIn("WaitForSafeFxToSettleAsync", collector)
        self.assertIn("SetNearHandHoverForR3", collector)
        self.assertIn("NearHandActorsMatchTargetPoses", collector)
        self.assertIn("TransformsApproximatelyEqual", collector)
        self.assertIn("consecutiveCompletedDraws == 2", collector)
        self.assertIn("actor.Transform, pose.Transform", collector)
        self.assertNotIn("Task.Delay", collector)
        self.assertIn("OnBattlefieldSurfaceHovered", match_screen)
        self.assertIn("ShowsKnownCardForSmoke", match_screen)
        self.assertIn("validate_gate4b_visual_suite.py", workflow)
        self.assertIn("tests/visual_goldens/gate4b/windows-1600x900", workflow)
        self.assertIn("validate_r3_visual_slice.py", workflow)
        self.assertIn("SomeCardGameShit-r3-candidate-windows-visual-slice", workflow)
        self.assertIn(
            "SomeCardGameShit-gate4b-r3-visual-slice-windows-x86_64.zip",
            workflow,
        )
        self.assertIn("Copy-Item -LiteralPath $r2Package -Destination $r3Package", workflow)
        self.assertIn("SomeCardGameShit-gate4b-r2-windows-x86_64.zip", workflow)
        self.assertEqual(
            3,
            workflow.count("--expect-output SCGS_R3_VISUAL_SLICE_READY"),
        )
        self.assertIn("artifacts/r3-exported-visual-slice/windows-1600x900", workflow)
        self.assertIn(
            "SomeCardGameShit-r3-candidate-windows-exported-visual-slice",
            workflow,
        )
        self.assertIn("artifacts/r3-packaged-visual-slice/windows-1600x900", workflow)
        self.assertIn(
            "SomeCardGameShit-r3-candidate-windows-packaged-visual-slice",
            workflow,
        )
        self.assertIn("$packagedLauncherHash -ne $expectedLauncherHash", workflow)
        packaged_launch = workflow.split(
            "- name: Round-trip launch packaged Windows R3 visual slice", 1
        )[1].split("- name: Round-trip audit and launch packaged Windows default 3D client", 1)[0]
        self.assertNotIn("--native-library", packaged_launch)
        self.assertIn("SCGS_R3_LAUNCHER_CI", packaged_launch)
        self.assertIn('--resolution "1600x900"', launcher)
        self.assertIn("--r3-visual-slice-exit", launcher)
        self.assertIn("PLAY_R3_VISUAL_SLICE.cmd", finalizer)
        self.assertIn('"--r3-visual-slice=%SCGS_R3_OUTPUT%"', launcher)


if __name__ == "__main__":
    unittest.main()
