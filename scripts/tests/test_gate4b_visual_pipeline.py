"""Contract tests for Gate 4B assets, screenshots, and performance evidence."""

# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import hashlib
import copy
import json
import shutil
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

from audit_visual_assets import (  # noqa: E402
    ANIME_V1_MANIFEST_RELATIVE_PATH,
    CARD_BODY_RASTER_PATHS,
    CARD_BODY_MANIFEST_RELATIVE_PATH,
    EXPECTED_PRODUCT_CARD_ART_PATHS,
    EXPECTED_ANIME_V1_PATHS,
    EXPECTED_CARD_BODY_PATHS,
    EXPECTED_PRESENTATION_V2_PATHS,
    PRODUCT_CARD_ART_MANIFEST_RELATIVE_PATH,
    VisualAssetAuditError,
    _rgba_alpha_extrema,
    _audit_presentation_v2_entry,
    audit,
)
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


def _write_rgba_png(
    path: Path,
    width: int,
    height: int,
    rgba: tuple[int, int, int, int],
) -> None:
    rows = b"".join(b"\0" + bytes(rgba) * width for _ in range(height))
    header = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    path.write_bytes(
        b"\x89PNG\r\n\x1a\n"
        + _chunk(b"IHDR", header)
        + _chunk(b"IDAT", zlib.compress(rows))
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
    def _presentation_entries(self) -> list[dict]:
        manifest = json.loads((SCRIPTS.parent / "client/godot/assets/visual/ASSET_MANIFEST.json").read_text(encoding="utf-8"))
        return [entry for entry in manifest["assets"] if entry["path"] in EXPECTED_PRESENTATION_V2_PATHS]

    def test_presentation_v2_registers_exact_three_candidates_without_changing_old_sets(self) -> None:
        entries = self._presentation_entries()
        self.assertEqual(EXPECTED_PRESENTATION_V2_PATHS, {entry["path"] for entry in entries})
        for entry in entries:
            _audit_presentation_v2_entry(SCRIPTS.parent, entry)
        self.assertEqual(14, len(EXPECTED_ANIME_V1_PATHS))
        self.assertEqual(23, len(EXPECTED_CARD_BODY_PATHS))
        self.assertEqual(28, len(EXPECTED_PRODUCT_CARD_ART_PATHS))

    def test_presentation_v2_rejects_false_native_alpha_claim(self) -> None:
        for original in self._presentation_entries():
            entry = copy.deepcopy(original)
            entry["transparency"] = "native_alpha"
            with self.subTest(path=entry["path"]), self.assertRaisesRegex(VisualAssetAuditError, "must not claim native alpha"):
                _audit_presentation_v2_entry(SCRIPTS.parent, entry)

    def test_presentation_v2_requires_exact_evolved_source_hash(self) -> None:
        for original in self._presentation_entries():
            if not original["source_images"]:
                continue
            entry = copy.deepcopy(original)
            entry["source_images"][0]["sha256"] = "0" * 64
            with self.subTest(path=entry["path"]), self.assertRaisesRegex(VisualAssetAuditError, "input SHA-256 mismatch"):
                _audit_presentation_v2_entry(SCRIPTS.parent, entry)

    def test_presentation_v2_requires_dated_history_and_review_boundary(self) -> None:
        for field, value, expected in (("modification_history", [], "modification history"),
                                        ("authorization", "", "authorization/review"),
                                        ("date", "2026-09-05", "exact provenance fields and date")):
            entry = copy.deepcopy(self._presentation_entries()[0])
            entry[field] = value
            with self.subTest(field=field), self.assertRaisesRegex(VisualAssetAuditError, expected):
                _audit_presentation_v2_entry(SCRIPTS.parent, entry)

    def test_presentation_v2_requires_extraction_and_final_chroma_prompts(self) -> None:
        entry = next(item for item in self._presentation_entries() if item["source_images"])
        with patch("audit_visual_assets._load_json", return_value={"material": "synthetic material prompt"}):
            with self.assertRaisesRegex(VisualAssetAuditError, "extraction and final chroma prompts"):
                _audit_presentation_v2_entry(SCRIPTS.parent, entry)

    def test_presentation_v2_requires_compressed_mipmapped_import(self) -> None:
        entry = next(item for item in self._presentation_entries() if not item["source_images"])
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for relative in (entry["path"], entry["path"] + ".import", entry["generation_record"]):
                destination = root / relative
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(SCRIPTS.parent / relative, destination)
            sidecar = root / (entry["path"] + ".import")
            sidecar.write_text(sidecar.read_text(encoding="utf-8").replace("mipmaps/generate=true", "mipmaps/generate=false"), encoding="utf-8")
            with self.assertRaisesRegex(VisualAssetAuditError, "desktop compression and mipmaps"):
                _audit_presentation_v2_entry(root, entry)

    def test_retired_models_shaders_and_imports_are_rejected_even_without_images(self) -> None:
        for relative in ("arena/retired.glb", "r3/retired.gdshader", "cards/art/retired.png.import"):
            with self.subTest(relative=relative), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                for source_path in ("client/godot/assets/visual/ASSET_MANIFEST.json",
                                    "client/godot/assets/visual/anime_v1/shared/fallback_front.svg"):
                    target = root / source_path
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes((SCRIPTS.parent / source_path).read_bytes())
                # This isolated retirement fixture only needs the neutral front.
                manifest_path = root / "client/godot/assets/visual/ASSET_MANIFEST.json"
                manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
                manifest["assets"] = [entry for entry in manifest["assets"]
                                      if entry["path"].endswith("fallback_front.svg")]
                manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
                retired = root / "client/godot/assets/visual" / relative
                retired.parent.mkdir(parents=True, exist_ok=True)
                retired.write_bytes(b"synthetic retired resource")
                with self.assertRaisesRegex(VisualAssetAuditError, "Retired industrial product assets"):
                    audit(root)

    def test_repo_retires_industrial_art_and_audits_the_only_anime_product(self) -> None:
        root = SCRIPTS.parent
        result = audit(root)
        product_manifest = root / "client/godot/assets/visual/ASSET_MANIFEST.json"
        candidate_manifest = (
            root / "client/godot/assets/visual/arena/R3_ASSET_MANIFEST.json"
        )
        anime_manifest = root / ANIME_V1_MANIFEST_RELATIVE_PATH
        card_body_manifest = root / CARD_BODY_MANIFEST_RELATIVE_PATH
        product_card_art_manifest = root / PRODUCT_CARD_ART_MANIFEST_RELATIVE_PATH
        self.assertEqual(69, result["asset_count"])
        self.assertEqual(4, result["product_asset_count"])
        self.assertEqual(3, result["presentation_v2_asset_count"])
        self.assertEqual(8_302_778, result["presentation_v2_source_payload_bytes"])
        self.assertEqual(0, result["candidate_asset_count"])
        self.assertEqual(14, result["anime_asset_count"])
        self.assertEqual(23, result["card_body_asset_count"])
        self.assertEqual(28, result["product_card_art_asset_count"])
        self.assertLessEqual(result["anime_asset_count"], 24)
        self.assertGreater(result["anime_estimated_vram_bytes"], 0)
        self.assertLessEqual(
            result["anime_estimated_vram_bytes"],
            96 * 1024 * 1024,
        )
        self.assertGreater(result["anime_source_payload_bytes"], 0)
        self.assertLessEqual(
            result["anime_source_payload_bytes"],
            64 * 1024 * 1024,
        )
        self.assertGreater(result["product_card_art_estimated_vram_bytes"], 0)
        self.assertLessEqual(
            result["product_card_art_estimated_vram_bytes"],
            64 * 1024 * 1024,
        )
        self.assertGreater(result["product_card_art_source_payload_bytes"], 0)
        self.assertLessEqual(
            result["product_card_art_source_payload_bytes"],
            96 * 1024 * 1024,
        )
        self.assertEqual(24, result["product_card_runtime_max_resident_textures"])
        self.assertEqual(
            hashlib.sha256(product_manifest.read_bytes()).hexdigest(),
            result["product_manifest_sha256"],
        )
        self.assertFalse(candidate_manifest.exists())
        self.assertIsNone(result["candidate_manifest_sha256"])
        visual_root = root / "client/godot/assets/visual"
        for retired in ("cards", "shared", "menu", "portraits", "arena", "r3"):
            self.assertFalse(any(path.is_file() for path in (visual_root / retired).rglob("*")))
        self.assertTrue(all("/anime_v1/" in path for path in result["paths"]))
        self.assertEqual(
            hashlib.sha256(anime_manifest.read_bytes()).hexdigest(),
            result["anime_manifest_sha256"],
        )
        self.assertEqual(
            hashlib.sha256(card_body_manifest.read_bytes()).hexdigest(),
            result["card_body_manifest_sha256"],
        )
        self.assertEqual(
            hashlib.sha256(product_card_art_manifest.read_bytes()).hexdigest(),
            result["product_card_art_manifest_sha256"],
        )
        self.assertTrue(EXPECTED_ANIME_V1_PATHS.issubset(result["paths"]))
        self.assertTrue(EXPECTED_CARD_BODY_PATHS.issubset(result["paths"]))
        self.assertTrue(EXPECTED_PRODUCT_CARD_ART_PATHS.issubset(result["paths"]))

    def test_product_card_batch_is_exact_unique_and_godot_imported(self) -> None:
        root = SCRIPTS.parent
        manifest_path = root / PRODUCT_CARD_ART_MANIFEST_RELATIVE_PATH
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        registered = {entry["path"] for entry in manifest["assets"]}

        self.assertEqual(1, manifest["schema_version"])
        self.assertEqual("5C-6C", manifest["gate"])
        self.assertEqual(EXPECTED_PRODUCT_CARD_ART_PATHS, registered)
        self.assertEqual(28, len(manifest["assets"]))
        self.assertEqual(28, len({entry["sha256"] for entry in manifest["assets"]}))
        self.assertEqual(
            {
                "source_payload_bytes_max": 96 * 1024 * 1024,
                "estimated_vram_bytes_max": 64 * 1024 * 1024,
                "runtime_resident_identity_texture_limit": 24,
            },
            manifest["budget"],
        )

        for entry in manifest["assets"]:
            path = root / entry["path"]
            self.assertEqual(
                entry["sha256"],
                hashlib.sha256(path.read_bytes()).hexdigest(),
            )
            sidecar = (root / f"{entry['path']}.import").read_text(encoding="utf-8")
            self.assertIn('"vram_texture": true', sidecar)
            self.assertIn("compress/mode=2", sidecar)
            self.assertIn("compress/high_quality=true", sidecar)
            self.assertIn("mipmaps/generate=true", sidecar)
            expected_source = "res://" + entry["path"].removeprefix("client/godot/")
            self.assertIn(f'source_file="{expected_source}"', sidecar)

    def test_card_body_candidate_is_exact_and_separate_from_frozen_anime_slice(self) -> None:
        root = SCRIPTS.parent
        manifest_path = root / CARD_BODY_MANIFEST_RELATIVE_PATH
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        registered = {entry["path"] for entry in manifest["assets"]}

        self.assertEqual(1, manifest["schema_version"])
        self.assertEqual("6A-R1", manifest["gate"])
        self.assertEqual(23, len(manifest["assets"]))
        self.assertEqual(EXPECTED_CARD_BODY_PATHS, registered)
        self.assertTrue(registered.isdisjoint(EXPECTED_ANIME_V1_PATHS))
        self.assertEqual(23, len({entry["sha256"] for entry in manifest["assets"]}))
        for entry in manifest["assets"]:
            path = root / entry["path"]
            self.assertEqual(
                entry["sha256"],
                hashlib.sha256(path.read_bytes()).hexdigest(),
            )

        nameplates = sorted(path for path in registered if "/nameplates/" in path)
        self.assertEqual(
            [
                "client/godot/assets/visual/anime_v1/card_body/nameplates/neutral.svg",
                "client/godot/assets/visual/anime_v1/card_body/nameplates/oathguard.svg",
                "client/godot/assets/visual/anime_v1/card_body/nameplates/pactmage.svg",
            ],
            nameplates,
        )
        for relative in nameplates:
            source = (root / relative).read_text(encoding="utf-8")
            self.assertNotIn("fill-opacity", source)

        for relative in sorted(path for path in registered if "/gems/" in path):
            source = (root / relative).read_text(encoding="utf-8")
            self.assertNotIn('fill="#151127"', source)

    def test_anime_v1_imports_use_vram_compression_and_mipmaps(self) -> None:
        root = SCRIPTS.parent
        for relative in EXPECTED_ANIME_V1_PATHS:
            sidecar = (root / f"{relative}.import").read_text(encoding="utf-8")
            self.assertIn('"vram_texture": true', sidecar)
            self.assertIn("compress/mode=2", sidecar)
            self.assertIn("compress/high_quality=true", sidecar)
            self.assertIn("mipmaps/generate=true", sidecar)

    def test_card_body_rasters_use_vram_compression_and_mipmaps(self) -> None:
        root = SCRIPTS.parent
        for relative in CARD_BODY_RASTER_PATHS:
            sidecar = (root / f"{relative}.import").read_text(encoding="utf-8")
            self.assertIn('"vram_texture": true', sidecar)
            self.assertIn("compress/mode=2", sidecar)
            self.assertIn("compress/high_quality=true", sidecar)
            self.assertIn("mipmaps/generate=true", sidecar)

    def test_rgba_alpha_reader_rejects_fake_transparency(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            transparent = directory / "transparent.png"
            opaque_rgba = directory / "opaque-rgba.png"
            fake = directory / "fake.png"
            _write_rgba_png(transparent, 2, 2, (20, 30, 40, 0))
            _write_rgba_png(opaque_rgba, 2, 2, (20, 30, 40, 255))
            _write_png(fake, 2, 2, (20, 30, 40))
            self.assertEqual((0, 0), _rgba_alpha_extrema(transparent))
            self.assertEqual((255, 255), _rgba_alpha_extrema(opaque_rgba))
            with self.assertRaisesRegex(VisualAssetAuditError, "8-bit RGBA"):
                _rgba_alpha_extrema(fake)

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
                "gate": "5C-6C",
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
                "gate": "5C-6C",
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
                "gate": "5C-6C",
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
            self.assertEqual(0, result["anime_asset_count"])
            self.assertEqual(0, result["card_body_asset_count"])
            self.assertEqual(0, result["product_card_art_asset_count"])

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

    def test_repo_registers_real_product_visuals_and_never_auto_updates_goldens(self) -> None:
        root = SCRIPTS.parent
        read = lambda path: (root / path).read_text(encoding="utf-8")
        workflow = read(".github/workflows/ci.yml") + read(".github/workflows/windows-visual-heavy.yml")
        self.assertIn("audit_visual_assets.py", workflow)
        self.assertIn("run_product_smoke.py", workflow)
        self.assertIn("--ci-product-capture", read("scripts/ci/run_product_smoke.py"))
        self.assertNotIn("--performance", workflow)
        hardware_validator = read("scripts/dev/validate_hardware_gpu_acceptance.py")
        self.assertIn("validate_performance", hardware_validator)
        self.assertIn("llvmpipe", hardware_validator)
        self.assertNotIn("update_gate4b_goldens.py", workflow)
        self.assertNotIn("SCGS_R3_VISUAL_SLICE_READY", workflow)
        self.assertNotIn("SomeCardGameShit-gate4b-r2", workflow)
        producer = read("client/godot/scripts/Ci/ProductVisualCapture.cs")
        for contract in ("FramePostDraw", "StableContent", "Engine.GetFramesDrawn",
                         "WarmupFrames = 300", "p95 <= 33.3", "maximum < 100",
                         "heavy_board_not_observed", "after.Resources <= before.Resources"):
            self.assertIn(contract, producer)
        for state in ("menu", "setup", "covered", "mulligan", "action", "choice",
                      "reaction", "resolving", "finished"):
            self.assertIn(f'"{state}"', producer)
        match = read("client/godot/scripts/Match/ProductMatchScreen.cs")
        self.assertIn("BattlefieldVisualProfile.AnimeV1", match)
        self.assertIn("CiCaptureResolvingIfRequestedAsync", match)
        bootstrap = read("client/godot/scripts/Bootstrap/BootstrapController.cs")
        self.assertIn("new ProductVisualCapture", bootstrap)
        self.assertIn("CaptureShellAsync", bootstrap)
        catalog = read("client/godot/scripts/Visuals/CardVisualCatalog.cs")
        self.assertIn("Array.Empty<CardVisualEntry>()", catalog)
        self.assertIn("anime_v1/shared/fallback_front.svg", catalog)
        self.assertIn("anime_v1/slice/shared/card-back.png", catalog)
        self.assertNotIn("cards/art/", catalog)
        self.assertNotIn("assets/visual/shared/", catalog)
        portraits = read("client/godot/scripts/Visuals/LeaderPortraitCatalog.cs")
        self.assertIn("new AtlasTexture", portraits)
        self.assertIn("FilterClip = true", portraits)
        self.assertIn("_textureCache[entry.DeckId]", portraits)
        self.assertIn("aurelia-master.png", portraits)
        self.assertIn("theraea-master.png", portraits)
        self.assertNotIn("midrange_commander.png", portraits)
        self.assertNotIn("advance_technarch.png", portraits)
        product = read("client/godot/scripts/CardFaces/ProductCardVisualCatalog.cs")
        self.assertIn("anime_v1/shared/fallback_front.svg", product)
        self.assertNotIn("cards/shared/", product)
        profile = read("client/godot/scripts/Battlefield/BattlefieldVisualProfile.cs")
        self.assertIn("AnimeV1Arena.tscn", profile)
        self.assertNotIn("R3ArenaCandidate.tscn", profile)
        self.assertNotIn("ArenaVisualProfile Gate4BR2", profile)


if __name__ == "__main__":
    unittest.main()
