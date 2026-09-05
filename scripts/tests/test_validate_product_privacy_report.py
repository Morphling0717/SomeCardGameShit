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

from scripts.ci.validate_product_privacy_report import ProductPrivacyError, validate_directory
from scripts.tests.test_validate_product_visual_report import _png


def fixture(root: Path, display: bool = False) -> dict:
    # Synthetic parser fixtures only, never submitted as real GPU acceptance.
    samples = []
    for state in ("resolving", "covered"):
        for ordinal in (1, 2):
            image = _png()
            if display: (root / f"privacy-{state}-{ordinal}.png").write_bytes(image)
            samples.append({"state": state, "frame_ordinal": ordinal, "viewer": None, "revision": 0 if state == "resolving" else 1,
                "frame_clock": "frame-post-draw" if display else "process-frame", "viewer_reads_delta": 0,
                "private_queries_delta": 0, "forbidden_tokens": 0, "identity_resource_leaks": 0,
                "collisions": 0, "drag_tokens": 0, "private_callbacks": 0, "hidden_identity_leaks": 0,
                "input_enabled": False, "opaque_cover": state == "covered", "gpu_checked": display,
                "magenta_pixels": 0, "width": 1280, "height": 720,
                "sha256": hashlib.sha256(image).hexdigest() if display else None})
    report = {"schema_version": 1, "suite": "product-v05-privacy", "api": "scgs_v05",
        "evidence_kind": "display-gpu" if display else "structural-only",
        "injection_source": "real-revealed-product-hand", "injection_verified": True,
        "detector_self_test_passed": True, "gpu_injection_verified": display,
        "injection_magenta_pixels": 100 if display else 0, "samples": samples, "success": True}
    save(root, report)
    return report


def save(root: Path, report: dict) -> None:
    (root / "product-privacy.json").write_text(json.dumps(report), encoding="utf-8")


class ProductPrivacyTests(unittest.TestCase):
    def test_probe_uses_real_product_layer_coalesces_waits_and_checks_actual_cover(self) -> None:
        root = Path(__file__).resolve().parents[2]
        probe = (root / "client/godot/scripts/Ci/ProductPrivacyProbe.cs").read_text(encoding="utf-8")
        partial = (root / "client/godot/scripts/Match/ProductMatchScreen.PrivacyCi.cs").read_text(encoding="utf-8")
        actor = (root / "client/godot/scripts/Battlefield/CardActor3D.cs").read_text(encoding="utf-8")
        self.assertIn("await observationTask", probe)
        self.assertIn("gpuInjectionVerified = display", probe)
        self.assertIn("_productArtwork.MaterialOverride = _ciPrivacyMaterial", actor)
        self.assertIn("CiProductFace is not null", partial)
        self.assertIn("gradient.Colors.Any(color => color.A < 0.999f)", partial)
        self.assertIn("background.GetGlobalRect()", partial)
        self.assertIn("canvas.Modulate.A", partial)
        self.assertNotIn("new V05.CardView", probe + partial)
        self.assertNotIn("inner.GetView", probe + partial)

    def test_structure_and_display_have_separate_proof_levels(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); fixture(root)
            validate_directory(root)
            with self.assertRaises(ProductPrivacyError): validate_directory(root, require_gpu=True)
            fixture(root, True); validate_directory(root, require_gpu=True)

    def test_missing_positive_control_injection_or_frames_cannot_pass(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); original = fixture(root)
            for field, value in (("success", False), ("injection_verified", False), ("detector_self_test_passed", False),
                                 ("injection_source", "v04-fixture"), ("gpu_injection_verified", True), ("samples", [])):
                report = copy.deepcopy(original); report[field] = value; save(root, report)
                with self.subTest(field=field), self.assertRaises(ProductPrivacyError): validate_directory(root)

    def test_any_identity_interaction_callback_or_viewer_read_leak_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); original = fixture(root)
            for field in ("viewer_reads_delta", "private_queries_delta", "forbidden_tokens", "identity_resource_leaks",
                          "collisions", "drag_tokens", "private_callbacks", "hidden_identity_leaks", "magenta_pixels"):
                report = copy.deepcopy(original); report["samples"][0][field] = 1; save(root, report)
                with self.subTest(field=field), self.assertRaises(ProductPrivacyError): validate_directory(root)
            for field, value in (("viewer", 0), ("input_enabled", True), ("source_card", "private"),
                                 ("token", "private"), ("frame_clock", "frame-post-draw")):
                report = copy.deepcopy(original); report["samples"][0][field] = value; save(root, report)
                with self.subTest(field=field), self.assertRaises(ProductPrivacyError): validate_directory(root)

    def test_duplicate_frames_and_translucent_handoff_fail(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); report = fixture(root); report["samples"][1] = report["samples"][0]; save(root, report)
            with self.assertRaises(ProductPrivacyError): validate_directory(root)
            report = fixture(root); report["samples"][2]["opaque_cover"] = False; save(root, report)
            with self.assertRaises(ProductPrivacyError): validate_directory(root)

    def test_revision_drift_and_handoff_without_submission_fail(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); report = fixture(root); report["samples"][1]["revision"] = 1; save(root, report)
            with self.assertRaises(ProductPrivacyError): validate_directory(root)
            report = fixture(root)
            report["samples"][2]["revision"] = report["samples"][3]["revision"] = 0
            save(root, report)
            with self.assertRaises(ProductPrivacyError): validate_directory(root)

    def test_gpu_pixels_are_verified_not_only_the_zero_count(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); report = fixture(root, True)
            def chunk(kind: bytes, payload: bytes) -> bytes:
                return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload) & 0xffffffff)
            row = b"\0" + bytes((255, 0, 255, 255)) * 640 + bytes((35, 40, 60, 255)) * 640
            png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 1280, 720, 8, 6, 0, 0, 0)) + \
                  chunk(b"IDAT", zlib.compress(row * 720)) + chunk(b"IEND", b"")
            (root / "privacy-resolving-1.png").write_bytes(png)
            report["samples"][0]["sha256"] = hashlib.sha256(png).hexdigest(); save(root, report)
            with self.assertRaisesRegex(ProductPrivacyError, "magenta"): validate_directory(root, True)

    def test_stale_files_and_missing_gpu_hash_fail(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); report = fixture(root, True); report["samples"][0]["sha256"] = None; save(root, report)
            with self.assertRaises(ProductPrivacyError): validate_directory(root, True)
            fixture(root); (root / "privacy-old.png").write_bytes(_png())
            with self.assertRaises(ProductPrivacyError): validate_directory(root)


if __name__ == "__main__":
    unittest.main()
