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
    LEGACY_EXPECTED_STATES,
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
    checker: bool = False,
) -> None:
    if checker:
        base = bytes(rgb)
        bright = bytes(tuple(min(255, channel + 72) for channel in rgb))
        even_row = (base + bright) * (width // 2) + (base if width % 2 else b"")
        odd_row = (bright + base) * (width // 2) + (bright if width % 2 else b"")
        rows = b"".join(
            b"\0" + (even_row if y % 2 == 0 else odd_row)
            for y in range(height)
        )
    else:
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


def _fixture_pixel(
    base: tuple[int, int, int],
    x: int,
    y: int,
    checker: bool,
) -> tuple[int, int, int]:
    if not checker or (x + y) % 2 == 0:
        return base
    return tuple(min(255, channel + 72) for channel in base)


def _fixture_region(
    name: str,
    base: tuple[int, int, int],
    x: int,
    y: int,
    width: int,
    height: int,
    *,
    checker: bool,
) -> tuple[dict[str, object], dict[str, object]]:
    center_x = x + width // 2
    center_y = y + height // 2
    anchor_rgb = _fixture_pixel(base, center_x, center_y, checker=checker)
    anchor: dict[str, object] = {
        "name": name,
        "x": center_x,
        "y": center_y,
        "r": anchor_rgb[0],
        "g": anchor_rgb[1],
        "b": anchor_rgb[2],
    }
    region_rgb = b"".join(
        bytes(_fixture_pixel(base, pixel_x, pixel_y, checker=checker))
        for pixel_y in range(y, y + height)
        for pixel_x in range(x, x + width)
    )
    lumas = [
        (
            54 * region_rgb[offset]
            + 183 * region_rgb[offset + 1]
            + 19 * region_rgb[offset + 2]
            + 128
        ) >> 8
        for offset in range(0, len(region_rgb), 3)
    ]
    edge_pixels = sum(
        1
        for pixel_y in range(height)
        for pixel_x in range(width)
        if (
            pixel_x > 0
            and abs(
                lumas[pixel_y * width + pixel_x]
                - lumas[pixel_y * width + pixel_x - 1]
            ) >= 24
        )
        or (
            pixel_y > 0
            and abs(
                lumas[pixel_y * width + pixel_x]
                - lumas[(pixel_y - 1) * width + pixel_x]
            ) >= 24
        )
    )
    region: dict[str, object] = {
        "name": name,
        "x": x,
        "y": y,
        "width": width,
        "height": height,
        "sha256": hashlib.sha256(region_rgb).hexdigest(),
        "mean_luma": (sum(lumas) + len(lumas) // 2) // len(lumas),
        "edge_ratio": edge_pixels / len(lumas),
        "frame_pair_mae": 0.0,
        "max_channel_delta": 0,
    }
    return anchor, region


class VisualAssetAuditTests(unittest.TestCase):
    def test_repo_keeps_r2_manifest_frozen_and_audits_r3_separately(self) -> None:
        root = SCRIPTS.parent
        result = audit(root)
        product_manifest = root / "client/godot/assets/visual/ASSET_MANIFEST.json"
        candidate_manifest = (
            root / "client/godot/assets/visual/arena/R3_ASSET_MANIFEST.json"
        )
        golden_metadata = json.loads((
            root
            / "client/godot/tests/visual_goldens/gate4b/windows-1600x900/GOLDEN_METADATA.json"
        ).read_text(encoding="utf-8"))

        self.assertEqual(35, result["asset_count"])
        self.assertEqual(34, result["product_asset_count"])
        self.assertEqual(1, result["candidate_asset_count"])
        self.assertEqual(
            "550cee89ccb1b384149d85aa45725474371b022a646fbef8de28d4c9bbae8eac",
            hashlib.sha256(product_manifest.read_bytes()).hexdigest(),
        )
        self.assertEqual(
            golden_metadata["asset_manifest_sha256"],
            result["product_manifest_sha256"],
        )
        self.assertEqual(
            hashlib.sha256(candidate_manifest.read_bytes()).hexdigest(),
            result["candidate_manifest_sha256"],
        )

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

    def test_asset_inventory_requires_cross_manifest_unique_registration(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            visual = root / "client/godot/assets/visual"
            arena = visual / "arena"
            arena.mkdir(parents=True)
            product = visual / "product.png"
            candidate = arena / "r3_industrial_floor_albedo.png"
            product.write_bytes(b"product")
            candidate.write_bytes(b"candidate")

            def entry(path: Path) -> dict[str, str]:
                return {
                    "path": path.relative_to(root).as_posix(),
                    "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                    "purpose": "test asset",
                    "generation_method": "test generator",
                    "date": "2026-08-25",
                    "prompt_summary": "test prompt",
                }

            product_manifest = {
                "schema_version": 1,
                "gate": "4B",
                "assets": [entry(product)],
            }
            candidate_manifest = {
                "schema_version": 1,
                "gate": "4B-R3.1",
                "assets": [entry(candidate)],
            }
            product_manifest_path = visual / "ASSET_MANIFEST.json"
            candidate_manifest_path = arena / "R3_ASSET_MANIFEST.json"
            product_manifest_path.write_text(
                json.dumps(product_manifest), encoding="utf-8"
            )
            candidate_manifest_path.write_text(
                json.dumps(candidate_manifest), encoding="utf-8"
            )
            result = audit(root, enforce_product_set=False)
            self.assertEqual(2, result["asset_count"])
            self.assertEqual(1, result["product_asset_count"])
            self.assertEqual(1, result["candidate_asset_count"])

            rogue_manifest_path = visual / "ROGUE_ASSET_MANIFEST.json"
            rogue_manifest_path.write_text(
                json.dumps(candidate_manifest), encoding="utf-8"
            )
            with self.assertRaisesRegex(VisualAssetAuditError, "manifest set differs"):
                audit(root, enforce_product_set=False)
            rogue_manifest_path.unlink()

            candidate_manifest["assets"] = [entry(product)]
            candidate_manifest_path.write_text(
                json.dumps(candidate_manifest), encoding="utf-8"
            )
            with self.assertRaisesRegex(
                VisualAssetAuditError, "exactly one manifest"
            ):
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
    def _write_report(
        self,
        directory: Path,
        *,
        structural_pngs: bool = False,
        schema_version: int = 3,
        readable_pixels: bool = True,
        viewport_width: int = 1600,
        viewport_height: int = 900,
    ) -> Path:
        asset_hash = "a" * 64
        captures = []
        states = EXPECTED_STATES if schema_version == 4 else LEGACY_EXPECTED_STATES
        for index, state in enumerate(sorted(states)):
            filename = f"{index:02d}-{state}.png"
            screenshot = directory / filename
            color = (index, index + 1, index + 2)
            _write_png(
                screenshot,
                viewport_width,
                viewport_height,
                color,
                ancillary_bytes=20 * 1024 if structural_pngs else 0,
                checker=schema_version == 4 and readable_pixels,
            )
            capture = {
                "state": state,
                "viewer": None if state in {"menu", "match-setup", "covered", "error"} else 0,
                "revision": None if state in {"menu", "match-setup", "covered", "error"} else index,
                "width": viewport_width,
                "height": viewport_height,
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
            if schema_version == 4:
                names = {"viewport_center"}
                if state not in {"menu", "match-setup", "covered", "error"}:
                    names.update({"battlefield", "hud"})
                if state in {"hand-one", "hand-five", "hand-ten", "hand-hover"}:
                    names.add("near_hand")
                if state == "field-readability":
                    names.update(
                        {
                            "near_hand",
                            "cost",
                            "attack",
                            "health",
                            "countdown",
                            "battlefield",
                            "own_leader",
                            "opponent_leader",
                            "hud",
                        }
                    )
                anchors = []
                regions = []
                badge_geometry = {
                    "cost": (600, 350, 40, 40),
                    "attack": (621, 510, 40, 40),
                    "health": (720, 510, 42, 40),
                    "countdown": (680, 350, 56, 40),
                }
                for evidence_index, name in enumerate(sorted(names)):
                    x, y, region_width, region_height = badge_geometry.get(
                        name,
                        (100 + evidence_index * 8, 100, 4, 4),
                    )
                    anchor, region = _fixture_region(
                        name,
                        color,
                        x,
                        y,
                        region_width,
                        region_height,
                        checker=readable_pixels,
                    )
                    anchors.append(anchor)
                    regions.append(region)
                capture.update(
                    {
                        "stable_frame_post_draws": 2,
                        "frame_pair_mae": 0.0,
                        "pixel_evidence": {"anchors": anchors, "regions": regions},
                    }
                )
                hand_counts = {
                    "hand-one": (1, 0, 0),
                    "hand-five": (5, 0, 0),
                    "hand-ten": (10, 0, 0),
                    "hand-hover": (5, 1, 0),
                }
                if state in hand_counts:
                    card_count, hovered_count, selected_count = hand_counts[state]
                    capture["hand_evidence"] = {
                        "card_count": card_count,
                        "hovered_count": hovered_count,
                        "selected_count": selected_count,
                        "minimum_pixel_height": (
                            170.0 if viewport_height >= 900 else 142.0
                        ),
                        "maximum_abs_roll_degrees": 7.5,
                    }
            captures.append(capture)
        report = {
            "schema_version": schema_version,
            "gate": "4B-R2" if schema_version == 4 else "4B-R1",
            "scenario": "visual-suite",
            "asset_manifest_sha256": asset_hash,
            "viewport": {"width": viewport_width, "height": viewport_height},
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
        if schema_version == 4:
            report["capture_contract"] = {
                "frame_post_draws": 2,
                "pixel_space": "srgb8",
                "maximum_frame_pair_mae": 0.01,
                "maximum_region_frame_pair_mae": 0.01,
                "maximum_region_channel_delta": 64,
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

    def test_schema_four_checks_sixteen_states_and_real_pixel_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            report_path = self._write_report(Path(temporary), schema_version=4)
            report = validate(report_path, enforce_structure=False)
            self.assertEqual("4B-R2", report["gate"])
            self.assertEqual(16, len(report["captures"]))

            mutated = json.loads(report_path.read_text(encoding="utf-8"))
            field = next(
                capture for capture in mutated["captures"]
                if capture["state"] == "field-readability"
            )
            field["pixel_evidence"]["regions"][0]["sha256"] = "0" * 64
            report_path.write_text(json.dumps(mutated), encoding="utf-8")
            with self.assertRaisesRegex(VisualSuiteError, "disagrees with the PNG"):
                validate(report_path, enforce_structure=False)

    def test_explicit_golden_update_requires_schema_four_metadata(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path = self._write_report(
                directory,
                structural_pngs=True,
                schema_version=4,
            )
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
            self.assertEqual(4, metadata["schema_version"])
            self.assertEqual("4B-R2", metadata["gate"])
            self.assertEqual(2, metadata["capture_contract"]["frame_post_draws"])
            self.assertEqual(sorted(EXPECTED_STATES), metadata["states"])
            for state in EXPECTED_STATES:
                self.assertTrue((destination / f"{state}.png").is_file())

    def test_explicit_golden_update_refuses_historical_schema_three(self) -> None:
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
            with (
                patch.object(sys, "argv", arguments),
                redirect_stderr(StringIO()),
            ):
                self.assertEqual(1, update_goldens_main())

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

    def test_schema_four_is_strict_and_requires_two_stable_frames_and_all_rois(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path = self._write_report(directory, schema_version=4)
            report = json.loads(report_path.read_text(encoding="utf-8"))
            report["captures"][0]["unexpected"] = True
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(VisualSuiteError, "fields must be exactly"):
                validate(report_path, enforce_structure=False)

            report_path = self._write_report(directory, schema_version=4)
            report = json.loads(report_path.read_text(encoding="utf-8"))
            report["captures"][0]["stable_frame_post_draws"] = 1
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(VisualSuiteError, "two consecutive"):
                validate(report_path, enforce_structure=False)

            report_path = self._write_report(directory, schema_version=4)
            report = json.loads(report_path.read_text(encoding="utf-8"))
            field = next(
                capture for capture in report["captures"]
                if capture["state"] == "field-readability"
            )
            field["pixel_evidence"]["anchors"] = [
                anchor for anchor in field["pixel_evidence"]["anchors"]
                if anchor["name"] != "countdown"
            ]
            field["pixel_evidence"]["regions"] = [
                region for region in field["pixel_evidence"]["regions"]
                if region["name"] != "countdown"
            ]
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(VisualSuiteError, "field-readability lacks"):
                validate(report_path, enforce_structure=False)

            report_path = self._write_report(
                directory,
                schema_version=4,
                readable_pixels=False,
            )
            with self.assertRaisesRegex(VisualSuiteError, "badge GPU ROIs are blank"):
                validate(report_path, enforce_structure=False)

    def test_schema_four_rejects_local_region_frame_instability(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path = self._write_report(directory, schema_version=4)
            baseline = json.loads(report_path.read_text(encoding="utf-8"))
            mutations = (
                ("frame_pair_mae", 0.010001, "region .*frame_pair_mae exceeds"),
                ("max_channel_delta", 65, "region .*max_channel_delta exceeds"),
                ("max_channel_delta", 64.0, "max_channel_delta must be an integer"),
            )
            for field, value, message in mutations:
                with self.subTest(field=field, value=value):
                    report = json.loads(json.dumps(baseline))
                    report["captures"][0]["pixel_evidence"]["regions"][0][field] = value
                    report_path.write_text(json.dumps(report), encoding="utf-8")
                    with self.assertRaisesRegex(VisualSuiteError, message):
                        validate(report_path, enforce_structure=False)

    def test_schema_four_capture_contract_requires_exact_region_limits(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path = self._write_report(directory, schema_version=4)
            baseline = json.loads(report_path.read_text(encoding="utf-8"))
            mutations = (
                ("maximum_region_frame_pair_mae", 0.02),
                ("maximum_region_channel_delta", 65),
                ("maximum_region_channel_delta", 64.0),
            )
            for field, value in mutations:
                with self.subTest(field=field, value=value):
                    report = json.loads(json.dumps(baseline))
                    report["capture_contract"][field] = value
                    report_path.write_text(json.dumps(report), encoding="utf-8")
                    with self.assertRaisesRegex(
                        VisualSuiteError,
                        "capture_contract must strictly require",
                    ):
                        validate(report_path, enforce_structure=False)

            report = json.loads(json.dumps(baseline))
            del report["capture_contract"]["maximum_region_frame_pair_mae"]
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(
                VisualSuiteError,
                "capture_contract must strictly require",
            ):
                validate(report_path, enforce_structure=False)

    def test_hand_evidence_is_state_specific_and_meets_geometry_bounds(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path = self._write_report(directory, schema_version=4)
            baseline = json.loads(report_path.read_text(encoding="utf-8"))
            mutations = (
                ("hand-one", "card_count", 5, "card_count must be 1"),
                ("hand-hover", "hovered_count", 0, "hovered_count must be 1"),
                ("hand-five", "selected_count", 1, "selected_count must be 0"),
                (
                    "hand-ten",
                    "minimum_pixel_height",
                    169.99,
                    "hand cards are too short",
                ),
                (
                    "hand-five",
                    "maximum_abs_roll_degrees",
                    8.01,
                    "roll exceeds 8 degrees",
                ),
            )
            for state, field, value, message in mutations:
                with self.subTest(state=state, field=field):
                    report = json.loads(json.dumps(baseline))
                    capture = next(
                        item for item in report["captures"] if item["state"] == state
                    )
                    capture["hand_evidence"][field] = value
                    report_path.write_text(json.dumps(report), encoding="utf-8")
                    with self.assertRaisesRegex(VisualSuiteError, message):
                        validate(report_path, enforce_structure=False)

            report = json.loads(json.dumps(baseline))
            hand_one = next(
                item for item in report["captures"] if item["state"] == "hand-one"
            )
            del hand_one["hand_evidence"]
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(VisualSuiteError, "fields must be exactly"):
                validate(report_path, enforce_structure=False)

            report = json.loads(json.dumps(baseline))
            action = next(
                item for item in report["captures"] if item["state"] == "action"
            )
            action["hand_evidence"] = json.loads(
                json.dumps(next(
                    item["hand_evidence"]
                    for item in report["captures"]
                    if item["state"] == "hand-one"
                ))
            )
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(VisualSuiteError, "fields must be exactly"):
                validate(report_path, enforce_structure=False)

            report_path = self._write_report(
                directory,
                schema_version=4,
                viewport_width=1280,
                viewport_height=720,
            )
            report = validate(report_path, enforce_structure=False)
            hand_one = next(
                item for item in report["captures"] if item["state"] == "hand-one"
            )
            self.assertEqual(142.0, hand_one["hand_evidence"]["minimum_pixel_height"])
            hand_one["hand_evidence"]["minimum_pixel_height"] = 141.99
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(VisualSuiteError, "hand cards are too short"):
                validate(report_path, enforce_structure=False)

    def test_hand_captures_require_distinct_png_and_near_hand_hashes(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path = self._write_report(directory, schema_version=4)
            report = json.loads(report_path.read_text(encoding="utf-8"))
            hand_one = next(
                item for item in report["captures"] if item["state"] == "hand-one"
            )
            hand_five = next(
                item for item in report["captures"] if item["state"] == "hand-five"
            )
            hand_one["file"] = hand_five["file"]
            hand_one["sha256"] = hand_five["sha256"]
            hand_one["pixel_evidence"] = json.loads(
                json.dumps(hand_five["pixel_evidence"])
            )
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(VisualSuiteError, "distinct PNG SHA-256"):
                validate(report_path, enforce_structure=False)

            report_path = self._write_report(directory, schema_version=4)
            report = json.loads(report_path.read_text(encoding="utf-8"))
            hand_one = next(
                item for item in report["captures"] if item["state"] == "hand-one"
            )
            hand_five = next(
                item for item in report["captures"] if item["state"] == "hand-five"
            )
            five_index = int(hand_five["file"].split("-", 1)[0])
            hand_one_path = directory / hand_one["file"]
            _write_png(
                hand_one_path,
                1600,
                900,
                (five_index, five_index + 1, five_index + 2),
                ancillary_bytes=1,
                checker=True,
            )
            hand_one["sha256"] = hashlib.sha256(hand_one_path.read_bytes()).hexdigest()
            hand_one["pixel_evidence"] = json.loads(
                json.dumps(hand_five["pixel_evidence"])
            )
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(VisualSuiteError, "distinct near_hand ROI"):
                validate(report_path, enforce_structure=False)

    def test_field_badge_rois_reject_reused_rectangles_and_checker_pixels(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path = self._write_report(
                directory,
                structural_pngs=True,
                schema_version=4,
            )
            baseline = json.loads(report_path.read_text(encoding="utf-8"))
            field = next(
                item
                for item in baseline["captures"]
                if item["state"] == "field-readability"
            )
            badge_names = {"cost", "attack", "health", "countdown"}
            countdown_region = json.loads(json.dumps(next(
                item
                for item in field["pixel_evidence"]["regions"]
                if item["name"] == "countdown"
            )))
            countdown_anchor = json.loads(json.dumps(next(
                item
                for item in field["pixel_evidence"]["anchors"]
                if item["name"] == "countdown"
            )))
            for item in field["pixel_evidence"]["regions"]:
                if item["name"] in badge_names:
                    name = item["name"]
                    item.clear()
                    item.update(json.loads(json.dumps(countdown_region)))
                    item["name"] = name
            for item in field["pixel_evidence"]["anchors"]:
                if item["name"] in badge_names:
                    name = item["name"]
                    item.clear()
                    item.update(json.loads(json.dumps(countdown_anchor)))
                    item["name"] = name
            report_path.write_text(json.dumps(baseline), encoding="utf-8")
            with self.assertRaisesRegex(VisualSuiteError, "distinct rectangles"):
                validate(report_path)

            report_path = self._write_report(
                directory,
                structural_pngs=True,
                schema_version=4,
            )
            report = json.loads(report_path.read_text(encoding="utf-8"))
            field = next(
                item
                for item in report["captures"]
                if item["state"] == "field-readability"
            )
            state_index = int(field["file"].split("-", 1)[0])
            color = (state_index, state_index + 1, state_index + 2)
            geometry = {
                "cost": (600, 350, 56, 40),
                "countdown": (720, 350, 56, 40),
                "attack": (620, 510, 56, 40),
                "health": (740, 510, 56, 40),
            }
            anchors_by_name = {
                item["name"]: item for item in field["pixel_evidence"]["anchors"]
            }
            regions_by_name = {
                item["name"]: item for item in field["pixel_evidence"]["regions"]
            }
            for name, (x, y, region_width, region_height) in geometry.items():
                anchor, region = _fixture_region(
                    name,
                    color,
                    x,
                    y,
                    region_width,
                    region_height,
                    checker=True,
                )
                anchors_by_name[name].clear()
                anchors_by_name[name].update(anchor)
                regions_by_name[name].clear()
                regions_by_name[name].update(region)
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(VisualSuiteError, "identical checker pixels"):
                validate(report_path)

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
        attributes = (root / ".gitattributes").read_text(encoding="utf-8")
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
        hud_presenter = (
            root / "client/godot/scripts/UI/MatchHudPresenter.cs"
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
        self.assertIn("*.svg text eol=lf", attributes.splitlines())
        self.assertIn("audit_visual_assets.py", workflow)
        self.assertIn("validate_gate4b_visual_suite.py", workflow)
        self.assertIn("--audio-driver Dummy", workflow)
        self.assertIn("if ($width -eq 2560) { 2400 } else { 1200 }", workflow)
        self.assertIn("--timeout $suiteTimeout", workflow)
        self.assertIn("compare_visual_golden.py", workflow)
        self.assertIn("$goldenMetadata.states", workflow)
        self.assertIn("exactly one capture for approved state", workflow)
        self.assertIn("metadata references missing capture", workflow)
        self.assertNotIn("update_gate4b_goldens.py", workflow)
        self.assertIn("SomeCardGameShit-gate4b-r2-windows-x86_64", workflow)
        self.assertIn("SomeCardGameShit-gate4b-r2-macos-arm64", workflow)
        self.assertIn("SomeCardGameShit-gate4b-r2-windows-visual-suite", workflow)
        self.assertIn('"--ci-visual-suite="', bootstrap)
        self.assertIn('"--ci-visual-viewport="', producer)
        self.assertIn("DisplayServer.VSyncMode.Disabled", producer)
        self.assertIn("warmupFrames = 300", producer)
        self.assertIn("measuredFrames = 300", producer)
        self.assertIn('public int SchemaVersion { get; init; } = 4;', producer)
        self.assertIn('public string Gate { get; init; } = "4B-R2";', producer)
        self.assertIn("StableFramePostDraws = 2", producer)
        self.assertIn("ReadCompletedFrameAsync", producer)
        self.assertIn("MeasurePixelEvidence", producer)
        self.assertIn('"field-readability"', producer)
        self.assertIn("AdapterName", producer)
        self.assertIn("AdapterType", producer)
        self.assertIn("TimingBudgetApplicable", producer)
        self.assertIn("OpaqueFullHeightPanelCount", producer)
        self.assertIn("HudRegionsOverlapFree", producer)
        self.assertIn("BattlefieldWidthRatio", producer)
        self.assertIn("CiOwnHandScreenRect", producer)
        self.assertIn("projectedBoard.Intersection(safeBattlefieldRect)", producer)
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
        self.assertIn("VerifyMaximumHudStateForCiAsync", match)
        self.assertIn("MeasureMaximumStateForCi", hud_presenter)
        self.assertIn("PP {currentPp}/{ppCapacity}  裂{cracks}  进{evolutionEnergy}", hud_presenter)
        self.assertNotIn("FormatPpPips", match)
        self.assertEqual(2, match_scene.count('text = "PP 10/10  裂99  进99"'))
        self.assertIn("25/25, PP 10/10, double-digit crack/evolution", runner)
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
                self.assertIn(f'"{state}"', bootstrap + runner + match + producer)


if __name__ == "__main__":
    unittest.main()
