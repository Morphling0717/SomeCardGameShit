#!/usr/bin/env python3
"""Explicitly replace Gate 4B 1600x900 goldens from a validated visual suite."""

# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import argparse
import json
import shutil
import sys
from pathlib import Path

from validate_gate4b_visual_suite import VisualSuiteError, validate


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--destination", type=Path, required=True)
    parser.add_argument(
        "--accept",
        action="store_true",
        help="required acknowledgement; CI must never pass this option",
    )
    args = parser.parse_args()
    if not args.accept:
        print("golden update refused: pass --accept after reviewing every screenshot", file=sys.stderr)
        return 2
    try:
        report = validate(
            args.report,
            expected_width=1600,
            expected_height=900,
            enforce_budget=False,
        )
    except VisualSuiteError as error:
        print(f"golden update refused: {error}", file=sys.stderr)
        return 1
    if report["schema_version"] != 4 or report["gate"] != "4B-R2":
        print(
            "golden update refused: historical schema 3 reports remain valid for "
            "audit but cannot replace the Gate 4B-R2 goldens",
            file=sys.stderr,
        )
        return 1
    source_root = args.report.resolve().parent
    destination = args.destination.resolve()
    destination.mkdir(parents=True, exist_ok=True)
    for capture in report["captures"]:
        source = source_root / capture["file"]
        target = destination / f"{capture['state']}.png"
        shutil.copyfile(source, target)
        print(f"updated {target}")
    metadata = {
        "schema_version": 4,
        "gate": "4B-R2",
        "source_manifest": args.report.resolve().name,
        "source_report_schema_version": report["schema_version"],
        "asset_manifest_sha256": report["asset_manifest_sha256"],
        "capture_contract": report["capture_contract"],
        "states": sorted(capture["state"] for capture in report["captures"]),
    }
    (destination / "GOLDEN_METADATA.json").write_text(
        json.dumps(metadata, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
