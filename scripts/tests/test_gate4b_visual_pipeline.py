"""Contract tests for Gate 4B assets, screenshots, and performance evidence."""

# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import hashlib
import json
import struct
import sys
import tempfile
import unittest
import zlib
from contextlib import redirect_stderr, redirect_stdout
from io import StringIO
from pathlib import Path
from unittest.mock import patch


SCRIPTS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS))
sys.path.insert(0, str(SCRIPTS / "ci"))

from audit_visual_assets import VisualAssetAuditError, audit  # noqa: E402
from compare_visual_golden import (  # noqa: E402
    GoldenComparisonError,
    compare,
    read_png,
)
from validate_gate4b_visual_suite import (  # noqa: E402
    EXPECTED_STATES,
    VisualSuiteError,
    main as validate_main,
    validate,
)
from update_gate4b_goldens import main as update_goldens_main  # noqa: E402


def _chunk(kind: bytes, payload: bytes) -> bytes:
    return (
        struct.pack(">I", len(payload))
        + kind
        + payload
        + struct.pack(">I", zlib.crc32(kind + payload) & 0xFFFFFFFF)
    )


def _write_png(
    path: Path,
    width: int,
    height: int,
    rgb: tuple[int, int, int],
    *,
    ancillary_bytes: int = 0,
) -> None:
    rows = b"".join(b"\0" + bytes(rgb) * width for _ in range(height))
    header = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    fixture_padding = (
        _chunk(b"tEXt", b"fixture-padding\0" + b"x" * ancillary_bytes)
        if ancillary_bytes
        else b""
    )
    path.write_bytes(
        b"\x89PNG\r\n\x1a\n"
        + _chunk(b"IHDR", header)
        + _chunk(b"IDAT", zlib.compress(rows))
        + fixture_padding
        + _chunk(b"IEND", b"")
    )


class VisualAssetAuditTests(unittest.TestCase):
    def test_asset_inventory_rejects_unregistered_and_hash_drift(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            visual = root / "client/godot/assets/visual"
            visual.mkdir(parents=True)
            asset = visual / "card.png"
            asset.write_bytes(b"generated card")
            relative = asset.relative_to(root).as_posix()
            entry = {
                "path": relative,
                "sha256": hashlib.sha256(asset.read_bytes()).hexdigest(),
                "purpose": "test card",
                "generation_method": "built-in image generation",
                "date": "2026-08-24",
                "prompt_summary": "original science-fiction card art",
            }
            manifest = {
                "schema_version": 1,
                "gate": "4B",
                "assets": [entry],
            }
            manifest_path = visual / "ASSET_MANIFEST.json"
            manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
            self.assertEqual(1, audit(root, enforce_product_set=False)["asset_count"])

            (visual / "forgotten.svg").write_text("<svg/>", encoding="utf-8")
            with self.assertRaisesRegex(VisualAssetAuditError, "unregistered"):
                audit(root, enforce_product_set=False)
            (visual / "forgotten.svg").unlink()
            asset.write_bytes(b"changed")
            with self.assertRaisesRegex(VisualAssetAuditError, "mismatch"):
                audit(root, enforce_product_set=False)

    def test_asset_inventory_rejects_duplicate_hashes_and_escape(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            visual = root / "client/godot/assets/visual"
            visual.mkdir(parents=True)
            first = visual / "a.png"
            second = visual / "b.png"
            first.write_bytes(b"same")
            second.write_bytes(b"same")
            digest = hashlib.sha256(b"same").hexdigest()
            base = {
                "sha256": digest,
                "purpose": "test",
                "generation_method": "test generator",
                "date": "2026-08-24",
                "prompt_summary": "test prompt",
            }
            manifest = {
                "schema_version": 1,
                "gate": "4B",
                "assets": [
                    {**base, "path": first.relative_to(root).as_posix()},
                    {**base, "path": second.relative_to(root).as_posix()},
                ],
            }
            manifest_path = visual / "ASSET_MANIFEST.json"
            manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
            with self.assertRaisesRegex(VisualAssetAuditError, "duplicate SHA-256"):
                audit(root, enforce_product_set=False)

            manifest["assets"] = [{**base, "path": "../escape.png"}]
            manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
            with self.assertRaisesRegex(VisualAssetAuditError, "normalized"):
                audit(root, enforce_product_set=False)


class VisualGoldenTests(unittest.TestCase):
    def test_golden_compare_accepts_identical_and_writes_heatmap(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            expected = directory / "expected.png"
            actual = directory / "actual.png"
            heatmap = directory / "heatmap.png"
            _write_png(expected, 320, 180, (10, 20, 30))
            _write_png(actual, 320, 180, (10, 20, 30))
            self.assertEqual((0.0, 0.0), compare(actual, expected, heatmap_path=heatmap))
            self.assertEqual((320, 180), read_png(heatmap)[:2])

    def test_golden_compare_rejects_large_drift(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            expected = directory / "expected.png"
            actual = directory / "actual.png"
            _write_png(expected, 320, 180, (0, 0, 0))
            _write_png(actual, 320, 180, (255, 255, 255))
            with self.assertRaisesRegex(GoldenComparisonError, "threshold"):
                compare(actual, expected)


class VisualSuiteReportTests(unittest.TestCase):
    def _write_report(self, directory: Path, *, structural_pngs: bool = False) -> Path:
        asset_hash = "a" * 64
        captures = []
        for index, state in enumerate(sorted(EXPECTED_STATES)):
            filename = f"{index:02d}-{state}.png"
            screenshot = directory / filename
            _write_png(
                screenshot,
                1600,
                900,
                (index, index + 1, index + 2),
                ancillary_bytes=20 * 1024 if structural_pngs else 0,
            )
            captures.append(
                {
                    "state": state,
                    "viewer": None if state in {"menu", "match-setup", "covered", "error"} else 0,
                    "revision": None if state in {"menu", "match-setup", "covered", "error"} else index,
                    "width": 1600,
                    "height": 900,
                    "file": filename,
                    "sha256": hashlib.sha256(screenshot.read_bytes()).hexdigest(),
                    "asset_manifest_sha256": asset_hash,
                    "layout": {
                        "controls_inside_viewport": True,
                        "hud_regions_overlap_free": True,
                        "opaque_full_height_panel_count": 0,
                        "glass_surface_count": 3,
                        "visible_debug_label_count": 0,
                        "battlefield_width_ratio": 1.0 if state not in {"menu", "match-setup", "error"} else 0.0,
                        "battlefield_height_ratio": 1.0 if state not in {"menu", "match-setup", "error"} else 0.0,
                    },
                }
            )
        report = {
            "schema_version": 3,
            "gate": "4B-R1",
            "scenario": "visual-suite",
            "asset_manifest_sha256": asset_hash,
            "viewport": {"width": 1600, "height": 900},
            "captures": captures,
            "performance": {
                "adapter_name": "Test Hardware Adapter",
                "adapter_type": "discrete_gpu",
                "timing_budget_applicable": True,
                "warmup_frames": 300,
                "measured_frames": 300,
                "p95_frame_ms": 16.0,
                "max_frame_ms": 31.0,
                "actor_count_before": 20,
                "actor_count_after": 20,
                "material_count_before": 8,
                "material_count_after": 8,
                "texture_count_before": 31,
                "texture_count_after": 31,
            },
        }
        path = directory / "visual-suite.json"
        path.write_text(json.dumps(report), encoding="utf-8")
        return path

    def test_report_checks_all_states_files_hashes_and_performance(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            report_path = self._write_report(Path(temporary))
            self.assertEqual(
                "4B-R1",
                validate(
                    report_path,
                    expected_width=1600,
                    expected_height=900,
                    enforce_structure=False,
                )["gate"],
            )

    def test_explicit_golden_update_preserves_schema_three_metadata(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path = self._write_report(directory, structural_pngs=True)
            destination = directory / "goldens"
            arguments = [
                "update_gate4b_goldens.py",
                "--report",
                str(report_path),
                "--destination",
                str(destination),
                "--accept",
            ]
            with patch.object(sys, "argv", arguments), redirect_stdout(StringIO()):
                self.assertEqual(0, update_goldens_main())
            metadata = json.loads(
                (destination / "GOLDEN_METADATA.json").read_text(encoding="utf-8")
            )
            self.assertEqual(3, metadata["schema_version"])
            self.assertEqual(sorted(EXPECTED_STATES), metadata["states"])
            for state in EXPECTED_STATES:
                self.assertTrue((destination / f"{state}.png").is_file())

    def test_report_rejects_black_bars_overlap_debug_ui_and_small_board(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            mutations = (
                ("opaque_full_height_panel_count", 1, "full-height opaque"),
                ("hud_regions_overlap_free", False, "overlapping HUD"),
                ("visible_debug_label_count", 1, "debug labels"),
                ("battlefield_width_ratio", 0.5, "coverage is too small"),
            )
            for field, value, message in mutations:
                with self.subTest(field=field):
                    report_path = self._write_report(directory)
                    report = json.loads(report_path.read_text(encoding="utf-8"))
                    action = next(
                        capture for capture in report["captures"]
                        if capture["state"] == "action"
                    )
                    action["layout"][field] = value
                    report_path.write_text(json.dumps(report), encoding="utf-8")
                    with self.assertRaisesRegex(VisualSuiteError, message):
                        validate(report_path, enforce_structure=False)

    def test_report_rejects_missing_state_hash_drift_and_growth(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path = self._write_report(directory)
            report = json.loads(report_path.read_text(encoding="utf-8"))
            report["captures"].pop()
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(VisualSuiteError, "missing states"):
                validate(report_path, enforce_structure=False)

            report_path = self._write_report(directory)
            report = json.loads(report_path.read_text(encoding="utf-8"))
            report["captures"][0]["sha256"] = "0" * 64
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(VisualSuiteError, "SHA-256 mismatch"):
                validate(report_path, enforce_structure=False)

            report_path = self._write_report(directory)
            report = json.loads(report_path.read_text(encoding="utf-8"))
            report["performance"]["actor_count_after"] = 21
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(VisualSuiteError, "actor count grew"):
                validate(report_path, enforce_structure=False)

    def test_software_renderers_may_skip_only_the_timing_budget(self) -> None:
        software_adapters = (
            ("CPU rasterizer", "cpu"),
            ("Microsoft Basic Render Driver", "other"),
            ("llvmpipe (LLVM 19.1.0, 256 bits)", "virtual_gpu"),
            ("Google SwiftShader", "other"),
            ("Compatibility software renderer", "other"),
        )
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            for adapter_name, adapter_type in software_adapters:
                with self.subTest(adapter_name=adapter_name, adapter_type=adapter_type):
                    report_path = self._write_report(directory)
                    report = json.loads(report_path.read_text(encoding="utf-8"))
                    performance = report["performance"]
                    performance["adapter_name"] = adapter_name
                    performance["adapter_type"] = adapter_type
                    performance["timing_budget_applicable"] = False
                    performance["p95_frame_ms"] = 120.0
                    performance["max_frame_ms"] = 180.0
                    report_path.write_text(json.dumps(report), encoding="utf-8")
                    validate(report_path, enforce_structure=False)

            report_path = self._write_report(directory)
            report = json.loads(report_path.read_text(encoding="utf-8"))
            performance = report["performance"]
            performance["adapter_type"] = "cpu"
            performance["timing_budget_applicable"] = False
            performance["actor_count_after"] = 21
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(VisualSuiteError, "actor count grew"):
                validate(report_path, enforce_structure=False)

            report_path = self._write_report(directory)
            report = json.loads(report_path.read_text(encoding="utf-8"))
            performance = report["performance"]
            performance["adapter_type"] = "cpu"
            performance["timing_budget_applicable"] = False
            performance["warmup_frames"] = 299
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(VisualSuiteError, "300 warmup"):
                validate(report_path, enforce_structure=False)

    def test_hardware_adapter_cannot_opt_out_and_must_meet_timing_budget(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path = self._write_report(directory)
            report = json.loads(report_path.read_text(encoding="utf-8"))
            report["performance"]["timing_budget_applicable"] = False
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(VisualSuiteError, "may be false only"):
                validate(report_path, enforce_structure=False)

            report_path = self._write_report(directory)
            report = json.loads(report_path.read_text(encoding="utf-8"))
            performance = report["performance"]
            performance["p95_frame_ms"] = 34.0
            performance["max_frame_ms"] = 60.0
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(VisualSuiteError, "frame budget exceeded"):
                validate(report_path, enforce_structure=False)

    def test_adapter_metadata_is_strictly_typed(self) -> None:
        mutations = (
            ("adapter_name", "", "adapter_name must be a non-empty string"),
            ("adapter_type", "", "adapter_type must be a non-empty string"),
            (
                "timing_budget_applicable",
                "false",
                "timing_budget_applicable must be boolean",
            ),
        )
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            for field, value, message in mutations:
                with self.subTest(field=field):
                    report_path = self._write_report(directory)
                    report = json.loads(report_path.read_text(encoding="utf-8"))
                    report["performance"][field] = value
                    report_path.write_text(json.dumps(report), encoding="utf-8")
                    with self.assertRaisesRegex(VisualSuiteError, message):
                        validate(report_path, enforce_structure=False)

    def test_cli_skip_performance_budget_remains_an_explicit_override(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path = self._write_report(directory)
            report = json.loads(report_path.read_text(encoding="utf-8"))
            performance = report["performance"]
            performance["p95_frame_ms"] = 34.0
            performance["max_frame_ms"] = 101.0
            report_path.write_text(json.dumps(report), encoding="utf-8")

            base_args = [
                "validate_gate4b_visual_suite.py",
                "--report",
                str(report_path),
                "--skip-structure",
            ]
            with patch.object(sys, "argv", base_args), redirect_stderr(StringIO()):
                self.assertEqual(1, validate_main())
            with (
                patch.object(sys, "argv", [*base_args, "--skip-performance-budget"]),
                redirect_stdout(StringIO()),
            ):
                self.assertEqual(0, validate_main())

            report["performance"]["timing_budget_applicable"] = False
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with (
                patch.object(sys, "argv", [*base_args, "--skip-performance-budget"]),
                redirect_stderr(StringIO()),
            ):
                self.assertEqual(1, validate_main())

    def test_repo_registers_visual_pipeline_and_never_auto_updates_goldens(self) -> None:
        root = SCRIPTS.parent
        cmake = (root / "CMakeLists.txt").read_text(encoding="utf-8")
        workflow = (root / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        bootstrap = (
            root / "client/godot/scripts/Bootstrap/BootstrapController.cs"
        ).read_text(encoding="utf-8")
        runner = (
            root / "client/godot/scripts/Ci/Gate3CFullMatchSmoke.cs"
        ).read_text(encoding="utf-8")
        match = (
            root / "client/godot/scripts/Match/MatchScreen.cs"
        ).read_text(encoding="utf-8")
        producer = (
            root / "client/godot/scripts/Ci/Gate4BVisualSuite.cs"
        ).read_text(encoding="utf-8")
        theme = (
            root / "client/godot/assets/themes/default_theme.tres"
        ).read_text(encoding="utf-8")
        glass_material = (
            root / "client/godot/assets/themes/glass_panel_material.tres"
        ).read_text(encoding="utf-8")
        glass_surface = (
            root / "client/godot/scenes/components/GlassSurface.tscn"
        ).read_text(encoding="utf-8")
        match_scene = (
            root / "client/godot/scenes/match/Match.tscn"
        ).read_text(encoding="utf-8")
        overlays = "\n".join(
            (root / path).read_text(encoding="utf-8")
            for path in (
                "client/godot/scenes/panels/ActionPromptPanel.tscn",
                "client/godot/scenes/panels/ConfirmationPanel.tscn",
                "client/godot/scenes/panels/DirectActionPanel.tscn",
                "client/godot/scenes/panels/EventLogPanel.tscn",
                "client/godot/scenes/panels/MatchInteractionDock.tscn",
                "client/godot/scenes/panels/MulliganPanel.tscn",
                "client/godot/scenes/panels/ReactionPanel.tscn",
                "client/godot/scenes/overlays/PassDeviceOverlay.tscn",
                "client/godot/scenes/overlays/ResultOverlay.tscn",
                "client/godot/scenes/overlays/ErrorOverlay.tscn",
            )
        )
        self.assertIn("scgs_gate4b_visual_pipeline_contract", cmake)
        self.assertIn("audit_visual_assets.py", workflow)
        self.assertIn("validate_gate4b_visual_suite.py", workflow)
        self.assertIn("--audio-driver Dummy", workflow)
        self.assertIn("--timeout 1200", workflow)
        self.assertIn("compare_visual_golden.py", workflow)
        self.assertNotIn("update_gate4b_goldens.py", workflow)
        self.assertIn("SomeCardGameShit-gate4b-windows-x86_64", workflow)
        self.assertIn("SomeCardGameShit-gate4b-macos-arm64", workflow)
        self.assertIn('"--ci-visual-suite="', bootstrap)
        self.assertIn('"--ci-visual-viewport="', producer)
        self.assertIn("DisplayServer.VSyncMode.Disabled", producer)
        self.assertIn("warmupFrames = 300", producer)
        self.assertIn("measuredFrames = 300", producer)
        self.assertIn('public int SchemaVersion { get; init; } = 3;', producer)
        self.assertIn('public string Gate { get; init; } = "4B-R1";', producer)
        self.assertIn("AdapterName", producer)
        self.assertIn("AdapterType", producer)
        self.assertIn("TimingBudgetApplicable", producer)
        self.assertIn("OpaqueFullHeightPanelCount", producer)
        self.assertIn("HudRegionsOverlapFree", producer)
        self.assertIn("BattlefieldWidthRatio", producer)
        self.assertIn("CiOwnHandScreenRect", producer)
        self.assertIn("projectedBoard.Intersection(viewportRect)", producer)
        self.assertIn("ContainsGpuPrivacySentinel", producer)
        self.assertIn("private GPU sentinel (#ff00ff)", producer)
        self.assertIn("VerifyGpuPrivacySentinelDetector", producer)
        self.assertIn("VerifyLeaderPortraitContract", producer)
        self.assertIn("MatchVisualIdentity.FromDecks", producer)
        self.assertIn("match.CiPrivacySentinelVerified", runner)
        card_actor = (
            root / "client/godot/scripts/Battlefield/CardActor3D.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("CiHasPrivacyTextureSentinel", card_actor)
        self.assertIn("DisposeCiPrivacyResources", card_actor)
        self.assertNotIn("ArtworkMaterials[key] = _ciPrivacyMaterial", card_actor)
        for variation in (
            "GlassPanel",
            "GlassModal",
            "GlassChip",
            "GlassStatusPod",
            "GlassDrawer",
            "GlassActionChip",
            "PhaseChip",
        ):
            with self.subTest(variation=variation):
                self.assertIn(f'{variation}/base_type', theme)
        self.assertIn('path="res://assets/shaders/glass_panel.gdshader"', glass_material)
        self.assertIn("glass_panel_material.tres", glass_surface)
        self.assertIn('[node name="GlassBackBuffer" type="BackBufferCopy"', match_scene)
        self.assertIn('theme_type_variation = &"GlassModal"', overlays)
        self.assertIn('theme_type_variation = &"GlassActionChip"', overlays)
        self.assertIn('theme_type_variation = &"GlassPanel"', overlays)
        self.assertGreaterEqual(overlays.count("GlassSurface.tscn"), 10)
        self.assertIn('theme_type_variation = &"GlassStatusPod"', match_scene)
        self.assertIn('unique_name_in_owner = true', match_scene)
        self.assertNotIn('text = "GATE 4B"', match_scene + overlays)
        self.assertIn("SetLegacyBoardPanelsVisible(_legacy2dBoard);", match)
        self.assertIn(
            'GetNode<Control>("SafeMargin/Layout/HandPanel").Visible = showLegacyBoard;',
            match,
        )
        self.assertIn("_dock.SetMulliganTray(true);", match)
        self.assertIn("viewport.Y * 0.34f", (
            root / "client/godot/scripts/UI/MatchInteractionDock.cs"
        ).read_text(encoding="utf-8"))
        portrait_catalog = (
            root / "client/godot/scripts/Visuals/LeaderPortraitCatalog.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("interface ILeaderPortraitCatalog", portrait_catalog)
        self.assertIn("midrange_commander.png", portrait_catalog)
        self.assertIn("advance_technarch.png", portrait_catalog)
        self.assertIn("34", producer)
        for state in EXPECTED_STATES:
            with self.subTest(state=state):
                self.assertIn(f'"{state}"', bootstrap + runner + match)


if __name__ == "__main__":
    unittest.main()
