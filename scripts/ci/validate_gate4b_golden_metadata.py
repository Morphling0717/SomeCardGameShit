#!/usr/bin/env python3
"""Validate the explicitly approved Gate 4B-R2 golden inventory."""

# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

from validate_gate4b_visual_suite import EXPECTED_STATES


EXPECTED_FIELDS = {
    "schema_version",
    "gate",
    "source_manifest",
    "source_report_schema_version",
    "asset_manifest_sha256",
    "capture_contract",
    "states",
}
EXPECTED_CAPTURE_CONTRACT = {
    "frame_post_draws": 2,
    "pixel_space": "srgb8",
    "maximum_frame_pair_mae": 0.01,
    "maximum_region_frame_pair_mae": 0.01,
    "maximum_region_channel_delta": 64,
}


class GoldenMetadataError(RuntimeError):
    """Raised when the committed golden allowlist is incomplete or stale."""


def _read_object(path: Path, label: str) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise GoldenMetadataError(f"cannot read {label} {path}: {error}") from error
    if not isinstance(value, dict):
        raise GoldenMetadataError(f"{label} must contain a JSON object")
    return value


def validate(metadata_path: Path, report_path: Path) -> dict[str, Any]:
    metadata_path = metadata_path.resolve()
    report_path = report_path.resolve()
    metadata = _read_object(metadata_path, "golden metadata")
    report = _read_object(report_path, "visual report")

    if set(metadata) != EXPECTED_FIELDS:
        raise GoldenMetadataError(
            f"golden metadata fields must be exactly {sorted(EXPECTED_FIELDS)}"
        )
    if metadata["schema_version"] != 4 or metadata["gate"] != "4B-R2":
        raise GoldenMetadataError("golden metadata must identify Gate 4B-R2 schema 4")
    if report.get("schema_version") != 4 or report.get("gate") != "4B-R2":
        raise GoldenMetadataError("visual report must identify Gate 4B-R2 schema 4")
    if metadata["source_report_schema_version"] != 4:
        raise GoldenMetadataError("golden metadata source schema must be 4")
    if metadata["source_manifest"] != report_path.name:
        raise GoldenMetadataError("golden metadata source_manifest does not match the report")
    if metadata["asset_manifest_sha256"] != report.get("asset_manifest_sha256"):
        raise GoldenMetadataError("golden and visual-report asset hashes disagree")
    if metadata["capture_contract"] != EXPECTED_CAPTURE_CONTRACT:
        raise GoldenMetadataError("golden capture contract is incomplete or stale")
    if report.get("capture_contract") != EXPECTED_CAPTURE_CONTRACT:
        raise GoldenMetadataError("visual-report capture contract is incomplete or stale")

    expected = sorted(EXPECTED_STATES)
    states = metadata["states"]
    if not isinstance(states, list) or states != expected:
        raise GoldenMetadataError(
            "golden states must be the exact sorted Gate 4B-R2 16-state inventory"
        )
    captures = report.get("captures")
    if not isinstance(captures, list):
        raise GoldenMetadataError("visual report captures must be a list")
    report_states = [
        capture.get("state") if isinstance(capture, dict) else None
        for capture in captures
    ]
    if len(report_states) != len(expected) or sorted(report_states) != expected:
        raise GoldenMetadataError(
            "visual report must contain exactly one capture for every golden state"
        )

    missing = [
        state
        for state in expected
        if not (metadata_path.parent / f"{state}.png").is_file()
    ]
    if missing:
        raise GoldenMetadataError(f"golden PNGs are missing for states: {missing}")
    return metadata


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--metadata", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    try:
        validate(args.metadata, args.report)
    except GoldenMetadataError as error:
        print(f"Gate 4B golden metadata validation failed: {error}", file=sys.stderr)
        return 1
    print(f"validated Gate 4B golden metadata: {args.metadata.resolve()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
