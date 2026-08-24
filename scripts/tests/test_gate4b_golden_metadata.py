"""Tests for the Gate 4B-R2 committed golden inventory validator."""

# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
CI = ROOT / "scripts" / "ci"
sys.path.insert(0, str(CI))

from validate_gate4b_golden_metadata import (  # noqa: E402
    EXPECTED_CAPTURE_CONTRACT,
    GoldenMetadataError,
    validate,
)
from validate_gate4b_visual_suite import EXPECTED_STATES  # noqa: E402


class Gate4BGoldenMetadataTests(unittest.TestCase):
    def test_asset_manifest_checkout_is_locked_to_lf(self) -> None:
        attributes = (ROOT / ".gitattributes").read_text(encoding="utf-8")
        self.assertIn(
            "client/godot/assets/visual/ASSET_MANIFEST.json text eol=lf",
            attributes.splitlines(),
        )

    def _fixture(self, root: Path) -> tuple[Path, Path]:
        root.mkdir(parents=True, exist_ok=True)
        states = sorted(EXPECTED_STATES)
        report = {
            "schema_version": 4,
            "gate": "4B-R2",
            "asset_manifest_sha256": "a" * 64,
            "capture_contract": EXPECTED_CAPTURE_CONTRACT,
            "captures": [{"state": state} for state in states],
        }
        report_path = root / "visual-suite.json"
        report_path.write_text(json.dumps(report), encoding="utf-8")
        golden = root / "goldens"
        golden.mkdir()
        for state in states:
            (golden / f"{state}.png").write_bytes(b"golden")
        metadata = {
            "schema_version": 4,
            "gate": "4B-R2",
            "source_manifest": report_path.name,
            "source_report_schema_version": 4,
            "asset_manifest_sha256": report["asset_manifest_sha256"],
            "capture_contract": EXPECTED_CAPTURE_CONTRACT,
            "states": states,
        }
        metadata_path = golden / "GOLDEN_METADATA.json"
        metadata_path.write_text(json.dumps(metadata), encoding="utf-8")
        return metadata_path, report_path

    def test_accepts_exact_sixteen_state_inventory(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            metadata, report = self._fixture(Path(temporary))
            self.assertEqual(16, len(validate(metadata, report)["states"]))

    def test_rejects_metadata_state_subset(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            metadata, report = self._fixture(Path(temporary))
            value = json.loads(metadata.read_text(encoding="utf-8"))
            value["states"] = ["action"]
            metadata.write_text(json.dumps(value), encoding="utf-8")
            with self.assertRaisesRegex(GoldenMetadataError, "exact sorted"):
                validate(metadata, report)

    def test_rejects_stale_contract_and_missing_png(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            metadata, report = self._fixture(Path(temporary))
            value = json.loads(metadata.read_text(encoding="utf-8"))
            value["capture_contract"].pop("maximum_region_channel_delta")
            metadata.write_text(json.dumps(value), encoding="utf-8")
            with self.assertRaisesRegex(GoldenMetadataError, "contract"):
                validate(metadata, report)

            metadata, report = self._fixture(Path(temporary) / "second")
            (metadata.parent / "hand-ten.png").unlink()
            with self.assertRaisesRegex(GoldenMetadataError, "missing"):
                validate(metadata, report)


if __name__ == "__main__":
    unittest.main()
