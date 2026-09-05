"""The product capture validator rejects preview, private, blank and unmeasured evidence."""
# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import copy
import hashlib
import json
from pathlib import Path
import struct
import tempfile
import unittest
import zlib

from scripts.ci.validate_product_visual_report import (
    ProductVisualError, REQUIRED_STATES, validate_directory, validate_performance,
)


def _png(blank: bool = False) -> bytes:
    def chunk(kind: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xffffffff)
    width, height = 1280, 720
    dark, light = ((0, 0, 0, 255), (0, 0, 0, 255)) if blank else ((18, 24, 38, 255), (170, 140, 90, 255))
    row = b"\0" + bytes(dark) * (width // 2) + bytes(light) * (width // 2)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)) + \
        chunk(b"IDAT", zlib.compress(row * height)) + chunk(b"IEND", b"")


def performance() -> dict:
    counts = {"actors": 30, "materials": 70, "textures": 19, "resources": 800}
    return {
        "schema_version": 1, "suite": "product-v05-heavy-board", "status": "passed", "success": True,
        "state": "action", "viewer": 0, "revision": 67, "width": 1280, "height": 720,
        "player0_main_board": 4, "player1_main_board": 4, "warmup_frames": 300, "measured_frames": 300,
        "before": counts, "after": counts.copy(), "zero_growth": True, "p95_ms": 17.4, "max_ms": 30.2,
    }


def populate(directory: Path, blank: bool = False) -> dict:
    # Unit-test fixture images validate the report reader only, not product UI.
    image = _png(blank)
    captures = []
    for state in sorted(REQUIRED_STATES):
        (directory / f"{state}.png").write_bytes(image)
        captures.append({"state": state, "viewer": None if state in {"menu", "setup", "covered", "resolving"} else 0,
                         "revision": None if state in {"menu", "setup", "covered"} else 1,
                         "sha256": hashlib.sha256(image).hexdigest(), "width": 1280, "height": 720})
    report = {"schema_version": 1, "suite": "product-v05-visual", "success": True, "missing_states": [], "captures": captures}
    save(directory, report)
    return report


def save(directory: Path, report: dict) -> None:
    (directory / "product-visual.json").write_text(json.dumps(report), encoding="utf-8")


class ProductVisualReportTests(unittest.TestCase):
    def test_complete_files_and_measured_performance_pass(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            populate(root)
            (root / "product-performance.json").write_text(json.dumps(performance()), encoding="utf-8")
            validate_directory(root, require_performance=True)

    def test_private_fields_and_duplicate_states_are_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); original = populate(root)
            for field in ("seed", "card", "option_id", "token", "metadata", "hand"):
                report = copy.deepcopy(original); report["captures"][0][field] = "private"
                save(root, report)
                with self.subTest(field=field), self.assertRaises(ProductVisualError): validate_directory(root)
            report = copy.deepcopy(original); report["captures"].append(report["captures"][0]); save(root, report)
            with self.assertRaises(ProductVisualError): validate_directory(root)

    def test_public_capture_cannot_claim_private_viewer(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); report = populate(root)
            next(capture for capture in report["captures"] if capture["state"] == "covered")["viewer"] = 0
            save(root, report)
            with self.assertRaises(ProductVisualError): validate_directory(root)

    def test_hash_and_dimensions_must_match_real_png(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); original = populate(root)
            for field, value in (("sha256", "0" * 64), ("width", 1600), ("height", True)):
                report = copy.deepcopy(original); report["captures"][0][field] = value; save(root, report)
                with self.subTest(field=field), self.assertRaises(ProductVisualError): validate_directory(root)

    def test_blank_png_does_not_pass_even_with_correct_hash(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); populate(root, blank=True)
            with self.assertRaises(ProductVisualError): validate_directory(root)

    def test_missing_state_and_stale_png_are_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); original = populate(root)
            report = copy.deepcopy(original); report["captures"].pop(); save(root, report)
            with self.assertRaises(ProductVisualError): validate_directory(root)
            save(root, original); (root / "old-preview.png").write_bytes(_png())
            with self.assertRaises(ProductVisualError): validate_directory(root)

    def test_required_performance_cannot_be_absent(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); populate(root)
            with self.assertRaises(ProductVisualError): validate_directory(root, True)

    def test_each_heavy_board_and_frame_boundary_is_enforced(self) -> None:
        for field, value in (("state", "finished"), ("state", "menu"), ("player0_main_board", 2),
                             ("player0_main_board", 3), ("measured_frames", 299), ("warmup_frames", 299),
                             ("p95_ms", 33.31), ("max_ms", 100), ("p95_ms", float("nan")),
                             ("p95_ms", True), ("viewer", None), ("revision", 0),
                             ("zero_growth", False), ("status", "heavy_board_not_observed"), ("success", False)):
            report = performance(); report[field] = value
            with self.subTest(field=field, value=value), self.assertRaises(ProductVisualError):
                validate_performance(report, (1280, 720))

    def test_resource_growth_unknown_fields_and_wrong_resolution_fail(self) -> None:
        for field in ("actors", "materials", "textures", "resources"):
            report = performance(); report["after"][field] += 1
            with self.subTest(field=field), self.assertRaises(ProductVisualError): validate_performance(report, (1280, 720))
        report = performance(); report["source_cards"] = [123]
        with self.assertRaises(ProductVisualError): validate_performance(report, (1280, 720))
        with self.assertRaises(ProductVisualError): validate_performance(performance(), (1600, 900))

    def test_collection_may_reduce_counts_without_relaxing_frame_or_growth_budget(self) -> None:
        # Regression: an actual eight-card board retained 44 actors, 80 materials
        # and 34 textures while global ResourceCount fell from 131 to 122.
        report = performance()
        report["before"] = {"actors": 44, "materials": 80, "textures": 34, "resources": 131}
        report["after"] = {"actors": 44, "materials": 80, "textures": 34, "resources": 122}
        report["p95_ms"] = 5.0302
        report["max_ms"] = 56.4711
        validate_performance(report, (1280, 720))
        for field in ("actors", "materials", "textures", "resources"):
            reduced = copy.deepcopy(report)
            reduced["after"][field] -= 1
            validate_performance(reduced, (1280, 720))
        report["max_ms"] = 100
        with self.assertRaises(ProductVisualError): validate_performance(report, (1280, 720))

    def test_bool_schema_and_duplicate_json_keys_fail(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); report = populate(root); report["schema_version"] = True; save(root, report)
            with self.assertRaises(ProductVisualError): validate_directory(root)
            (root / "product-visual.json").write_text('{"success":true,"success":false}', encoding="utf-8")
            with self.assertRaises(ProductVisualError): validate_directory(root)


if __name__ == "__main__":
    unittest.main()
