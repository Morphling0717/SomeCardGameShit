# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import copy
import functools
import hashlib
import json
import struct
import tempfile
import unittest
import zlib
from pathlib import Path

from scripts.ci.validate_anime_visual_slice import (
    AnimeVisualSliceError,
    BADGE_ROLES,
    BADGES_BY_KIND,
    CARD_KINDS,
    HAND_KINDS,
    REQUIRED_ASSETS,
    STATES,
    TYPE_MARKER_GLYPHS,
    TYPE_MARKER_SHAPES,
    _pixel_evidence,
    _png_rgba,
    validate_report,
)


ROOT = Path(__file__).resolve().parents[2]


def _chunk(kind: bytes, payload: bytes) -> bytes:
    return (
        struct.pack(">I", len(payload))
        + kind
        + payload
        + struct.pack(">I", zlib.crc32(kind + payload) & 0xFFFFFFFF)
    )


@functools.lru_cache(maxsize=4)
def _png_fixture_bytes(width: int, height: int) -> bytes:
    colors = ((21, 17, 48), (232, 218, 174), (72, 128, 192), (174, 68, 132))
    rows = []
    for phase in range(4):
        rows.append(
            b"".join(
                bytes(colors[((column // 4) + phase) % len(colors)])
                for column in range(width)
            )
        )
    encoded = b"".join(b"\0" + rows[(row // 4) % len(rows)] for row in range(height))
    return (
        b"\x89PNG\r\n\x1a\n"
        + _chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
        + _chunk(b"IDAT", zlib.compress(encoded))
        + _chunk(b"IEND", b"")
    )


def _write_png(path: Path, width: int = 1280, height: int = 720) -> None:
    path.write_bytes(_png_fixture_bytes(width, height))


def _fixture(
    directory: Path,
    *,
    complete_assets: bool = True,
    physical_viewport: tuple[int, int] = (1280, 720),
) -> tuple[Path, dict[str, object]]:
    physical_width, physical_height = physical_viewport
    captures: list[dict[str, object]] = []
    fixture_rgba: bytes | None = None
    for index, state in enumerate(STATES):
        filename = f"{index:02d}-{state}.png"
        screenshot = directory / filename
        _write_png(screenshot, physical_width, physical_height)
        if fixture_rgba is None:
            _, _, fixture_rgba = _png_rgba(screenshot)
        rgba = fixture_rgba
        battle = state in {"action", "hand-hover", "mixed-permanents-field", "reaction"}
        kinds = (
            ["Amulet", "Field", "Follower", "Spell", "Trap"]
            if state == "mixed-permanents-field"
            else ["Amulet", "Follower", "Spell", "Trap"] if battle else []
        )
        logical_viewport = {"x": 0, "y": 0, "width": 1600, "height": 900}
        safe_area = {"x": 4, "y": 4, "width": 1592, "height": 892}
        hand_cards = []
        if battle:
            design_ids = ("LO-03", "LO-07", "LO-11", "NT-04", "LO-03")
            for hand_index, (kind, design_id) in enumerate(zip(HAND_KINDS, design_ids)):
                card_rect = {
                    "x": 380 + (hand_index * 150),
                    "y": 610,
                    "width": 120,
                    "height": 180,
                }
                badge_rects = {
                    "cost": {"x": card_rect["x"] + 4, "y": card_rect["y"] + 4, "width": 32, "height": 32},
                    "attack": {"x": card_rect["x"] + 4, "y": card_rect["y"] + 144, "width": 32, "height": 32},
                    "health": {"x": card_rect["x"] + 84, "y": card_rect["y"] + 144, "width": 32, "height": 32},
                    "countdown": {"x": card_rect["x"] + 84, "y": card_rect["y"] + 144, "width": 32, "height": 32},
                }
                badges = []
                for role in BADGE_ROLES:
                    present = role in BADGES_BY_KIND[kind]
                    roi = badge_rects[role] if present else None
                    badges.append(
                        {
                            "role": role,
                            "present": present,
                            "roi": roi,
                            "inside_safe_area": present,
                            "pixels": (
                                _pixel_evidence(rgba, physical_viewport, logical_viewport, roi)
                                if present
                                else None
                            ),
                        }
                    )
                hand_cards.append(
                    {
                        "node_name": f"NearHand{hand_index}",
                        "design_id": design_id,
                        "kind": kind,
                        "card_rect": card_rect,
                        "card_inside_safe_area": True,
                        "badge_font_pixel_size": 16,
                        "badges": badges,
                    }
                )
        type_markers = []
        if state == "mixed-permanents-field":
            design_ids = {
                "Follower": "LO-11",
                "Spell": "NT-04",
                "Amulet": "LO-03",
                "Trap": "LO-07",
                "Field": "AP-05",
            }
            for marker_index, kind in enumerate(CARD_KINDS):
                card_rect = {
                    "x": 380 + (marker_index * 150),
                    "y": 200,
                    "width": 120,
                    "height": 180,
                }
                roi = {
                    "x": card_rect["x"] + 84,
                    "y": card_rect["y"] + 6,
                    "width": 28,
                    "height": 28,
                }
                type_markers.append(
                    {
                        "kind": kind,
                        "node_name": f"Type{kind}",
                        "design_id": design_ids[kind],
                        "glyph": TYPE_MARKER_GLYPHS[kind],
                        "shape": TYPE_MARKER_SHAPES[kind],
                        "card_rect": card_rect,
                        "roi": roi,
                        "inside_safe_area": True,
                        "pixels": _pixel_evidence(
                            rgba, physical_viewport, logical_viewport, roi
                        ),
                    }
                )
        captures.append(
            {
                "state": state,
                "file": filename,
                "sha256": hashlib.sha256(screenshot.read_bytes()).hexdigest(),
                "width": physical_width,
                "height": physical_height,
                "complete_frame_post_draws": 2,
                "layout": {
                    "state": state,
                    "viewport": logical_viewport,
                    "board": {
                        "x": 260 if battle else 0,
                        "y": 30 if battle else 0,
                        "width": 920 if battle else 0,
                        "height": 800 if battle else 0,
                    },
                    "left_panel": {"x": 12, "y": 72, "width": 232, "height": 796} if battle else {"x": 0, "y": 0, "width": 0, "height": 0},
                    "right_panel": {"x": 1394, "y": 72, "width": 194, "height": 796} if battle else {"x": 0, "y": 0, "width": 0, "height": 0},
                    "has_outer_table_frame": False,
                    "uses_native_session": False,
                    "main_board_slot_count": 10 if battle else 0,
                    "tactic_slot_count": 6 if battle else 0,
                    "field_slot_count": 2 if battle else 0,
                    "visible_card_count": 9 if battle else 0,
                    "hidden_card_count": 5 if battle else 0,
                    "hidden_cards_with_identity": 0,
                    "visible_card_kinds": kinds,
                    "covered_opaque": state == "covered",
                    "loaded_asset_count": len(REQUIRED_ASSETS) if complete_assets else 0,
                    "required_asset_count": len(REQUIRED_ASSETS),
                },
                "readability_evidence": {
                    "safe_area": safe_area,
                    "hand_cards": hand_cards,
                    "type_markers": type_markers,
                },
            }
        )
    loaded = list(REQUIRED_ASSETS) if complete_assets else []
    missing = [] if complete_assets else list(REQUIRED_ASSETS)
    report: dict[str, object] = {
        "schema_version": 2,
        "gate": "6A",
        "scenario": "anime-style-slice",
        "visual_profile": "anime-v1-proposal",
        "approval_status": "pending_user_approval",
        "uses_native_session": False,
        "default_product_path_unchanged": True,
        "viewport": {"width": physical_width, "height": physical_height},
        "asset_contract": {
            "required_paths": list(REQUIRED_ASSETS),
            "loaded_paths": loaded,
            "missing_paths": missing,
            "complete": complete_assets,
        },
        "captures": captures,
    }
    path = directory / "anime-visual-slice.json"
    path.write_text(json.dumps(report), encoding="utf-8")
    return path, report


class AnimeVisualSliceValidatorTests(unittest.TestCase):
    def test_accepts_complete_eight_state_report(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            report_path, _ = _fixture(Path(temporary))
            report = validate_report(report_path, (1280, 720))
            self.assertEqual("anime-v1-proposal", report["visual_profile"])

    def test_ci_runner_viewport_requires_explicit_structural_smoke_opt_in(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            report_path, _ = _fixture(
                Path(temporary),
                physical_viewport=(1024, 684),
            )
            with self.assertRaisesRegex(AnimeVisualSliceError, "unsupported viewport"):
                validate_report(report_path, (1024, 684))
            validate_report(
                report_path,
                (1024, 684),
                allow_ci_runner_viewport=True,
            )

    def test_missing_rasters_require_explicit_infrastructure_override(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            report_path, _ = _fixture(Path(temporary), complete_assets=False)
            with self.assertRaisesRegex(AnimeVisualSliceError, "missing 14"):
                validate_report(report_path)
            validate_report(report_path, allow_missing_assets=True)

    def test_rejects_outer_table_frame_and_hidden_identity(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path, report = _fixture(directory)
            damaged = copy.deepcopy(report)
            damaged["captures"][2]["layout"]["has_outer_table_frame"] = True
            report_path.write_text(json.dumps(damaged), encoding="utf-8")
            with self.assertRaisesRegex(AnimeVisualSliceError, "outer table frame"):
                validate_report(report_path)

            damaged = copy.deepcopy(report)
            damaged["captures"][2]["layout"]["hidden_cards_with_identity"] = 1
            report_path.write_text(json.dumps(damaged), encoding="utf-8")
            with self.assertRaisesRegex(AnimeVisualSliceError, "hidden identity"):
                validate_report(report_path)

    def test_rejects_incomplete_mixed_permanent_surface(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path, report = _fixture(directory)
            report["captures"][4]["layout"]["visible_card_kinds"] = ["Follower", "Spell"]
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(AnimeVisualSliceError, "five card silhouettes"):
                validate_report(report_path)

    def test_rejects_hand_badge_outside_safe_area_and_forged_roi_pixels(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path, report = _fixture(directory)
            damaged = copy.deepcopy(report)
            hand = damaged["captures"][2]["readability_evidence"]["hand_cards"][2]
            hand["badges"][1]["roi"]["y"] = 890
            report_path.write_text(json.dumps(damaged), encoding="utf-8")
            with self.assertRaisesRegex(AnimeVisualSliceError, "ROI escapes"):
                validate_report(report_path)

            damaged = copy.deepcopy(report)
            pixels = damaged["captures"][2]["readability_evidence"]["hand_cards"][0]["badges"][0]["pixels"]
            pixels["pixel_sha256"] = "0" * 64
            report_path.write_text(json.dumps(damaged), encoding="utf-8")
            with self.assertRaisesRegex(AnimeVisualSliceError, "does not match the screenshot ROI"):
                validate_report(report_path)

    def test_rejects_small_or_color_only_type_marker_contract(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path, report = _fixture(directory)
            damaged = copy.deepcopy(report)
            marker = damaged["captures"][4]["readability_evidence"]["type_markers"][0]
            marker["roi"]["width"] = 20
            report_path.write_text(json.dumps(damaged), encoding="utf-8")
            with self.assertRaisesRegex(AnimeVisualSliceError, "smaller than 24"):
                validate_report(report_path)

            damaged = copy.deepcopy(report)
            marker = damaged["captures"][4]["readability_evidence"]["type_markers"][3]
            marker["glyph"] = ""
            report_path.write_text(json.dumps(damaged), encoding="utf-8")
            with self.assertRaisesRegex(AnimeVisualSliceError, "non-color kind marker"):
                validate_report(report_path)

    def test_rejects_old_schema_and_subminimum_hand_badge_font(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path, report = _fixture(directory)
            damaged = copy.deepcopy(report)
            damaged["schema_version"] = 1
            report_path.write_text(json.dumps(damaged), encoding="utf-8")
            with self.assertRaisesRegex(AnimeVisualSliceError, "schema 2"):
                validate_report(report_path)

            damaged = copy.deepcopy(report)
            damaged["captures"][3]["readability_evidence"]["hand_cards"][1]["badge_font_pixel_size"] = 15
            report_path.write_text(json.dumps(damaged), encoding="utf-8")
            with self.assertRaisesRegex(AnimeVisualSliceError, "smaller than 16"):
                validate_report(report_path)


class AnimeVisualSliceSourceContractTests(unittest.TestCase):
    def test_product_visual_lock_covers_exact_subjects_and_slice_boundary(self) -> None:
        visual_lock = json.loads(
            (ROOT / "design/product-decks-v1/anime-v1-visual.lock.json").read_text(
                encoding="utf-8"
            )
        )
        self.assertEqual(1, visual_lock["schema_version"])
        self.assertEqual("Gate 6A", visual_lock["gate"])
        self.assertEqual("slice_candidate_pending_user_approval", visual_lock["status"])
        self.assertEqual("AnimeV1", visual_lock["product_visual_profile"])

        subjects = visual_lock["subject_art"]["items"]
        self.assertEqual(38, visual_lock["subject_art"]["expected_count"])
        self.assertEqual(38, len(subjects))
        self.assertEqual(38, len({item["visual_id"] for item in subjects}))
        self.assertEqual(38, len({item["design_id"] for item in subjects}))
        self.assertEqual(
            34,
            sum(item["kind"] == "constructible" for item in subjects),
        )
        self.assertEqual(1, sum(item["kind"] == "token" for item in subjects))
        self.assertEqual(2, sum(item["kind"] == "leader" for item in subjects))
        self.assertEqual(1, sum(item["kind"] == "card_back" for item in subjects))

        slice_contract = visual_lock["slice_contract"]
        self.assertFalse(slice_contract["uses_native_session"])
        self.assertTrue(slice_contract["does_not_claim_product_decks_playable"])
        self.assertTrue(slice_contract["approval_required_before_batch_generation"])
        self.assertEqual(14, slice_contract["raster_count"])
        self.assertEqual(list(STATES), slice_contract["states"])

    def test_bootstrap_bypasses_native_preflight_before_menu(self) -> None:
        source = (ROOT / "client/godot/scripts/Bootstrap/BootstrapController.cs").read_text(encoding="utf-8")
        parse = source.index("AnimeVisualSliceLaunch.Parse(arguments)")
        replace = source.index("ReplaceScreen(animeSlice)")
        menu = source.index("ShowMainMenu();", replace)
        native = source.index("NativeLibraryLocator.ResolveAbsolutePath()", replace)
        self.assertLess(parse, replace)
        self.assertLess(replace, menu)
        self.assertLess(replace, native)

    def test_preview_has_no_client_or_native_dependency(self) -> None:
        preview = "\n".join(
            path.read_text(encoding="utf-8")
            for path in sorted((ROOT / "client/godot/scripts/Preview").glob("*.cs"))
        )
        suite = (ROOT / "client/godot/scripts/Ci/AnimeVisualSliceSuite.cs").read_text(encoding="utf-8")
        self.assertNotIn("using Scgs.Client", preview + suite)
        self.assertNotIn("ScgsGameSession", preview + suite)
        self.assertNotIn("NativeLibraryLocator", preview + suite)
        self.assertIn("HasOuterTableFrame = false", preview)

    def test_readability_suite_consumes_real_card_geometry(self) -> None:
        suite = (ROOT / "client/godot/scripts/Ci/AnimeVisualSliceSuite.cs").read_text(
            encoding="utf-8"
        )
        for property_name in (
            "VisualScreenRect",
            "CostBadgeScreenRect",
            "AttackBadgeScreenRect",
            "HealthBadgeScreenRect",
            "CountdownBadgeScreenRect",
            "TypeMarkerScreenRect",
            "BadgeFontPixelSize",
        ):
            self.assertIn(property_name, suite)
        self.assertNotIn("17.0f *", suite)
        self.assertNotIn("16.0f *", suite)

    def test_asset_and_state_contracts_are_explicit(self) -> None:
        catalog = (ROOT / "client/godot/scripts/Preview/AnimeVisualAssetCatalog.cs").read_text(encoding="utf-8")
        screen = (ROOT / "client/godot/scripts/Preview/AnimeStyleSliceScreen.cs").read_text(encoding="utf-8")
        for path in REQUIRED_ASSETS:
            relative = path.removeprefix("res://assets/visual/anime_v1/slice")
            self.assertIn(relative, catalog)
        for state in STATES:
            self.assertIn(f'"{state}"', screen)
        self.assertTrue((ROOT / "client/godot/scenes/preview/AnimeStyleSlice.tscn").is_file())

    def test_capture_motion_is_disabled_at_the_screen_boundary(self) -> None:
        profile = (
            ROOT / "client/godot/scripts/Preview/AnimeSliceMotionProfile.cs"
        ).read_text(encoding="utf-8")
        screen = (
            ROOT / "client/godot/scripts/Preview/AnimeStyleSliceScreen.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("AnimeSliceMotionPolicy.Select(_launch.OutputDirectory)", screen)
        self.assertIn("outputDirectory is null", profile)
        self.assertIn("AnimeSliceMotionProfile.Disabled", profile)
        self.assertIn("SetProcess(animate)", screen)
        self.assertIn("SetProcess(false)", screen)
        covered = screen[screen.index("private void BuildCovered()") : screen.index("private void BuildResult()")]
        self.assertNotIn("AnimePortraitPreview", covered)
        self.assertNotIn("TriggerHitPulse", covered)

    def test_matrix_runner_keeps_product_path_separate(self) -> None:
        runner = (ROOT / "scripts/ci/capture_anime_visual_slice.ps1").read_text(encoding="utf-8")
        for viewport in ("1280x720", "1600x900", "2560x1440", "2560x1600"):
            self.assertIn(viewport, runner)
        self.assertIn("--anime-style-slice=$captureDirectory", runner)
        self.assertIn("--anime-style-slice-exit", runner)
        self.assertIn("AllowCiRunnerViewport", runner)
        self.assertNotIn("--ci-visual-suite=", runner)

        workflow = (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        self.assertIn('-Viewports "1024x684"', workflow)
        self.assertIn("-AllowCiRunnerViewport", workflow)


if __name__ == "__main__":
    unittest.main()
