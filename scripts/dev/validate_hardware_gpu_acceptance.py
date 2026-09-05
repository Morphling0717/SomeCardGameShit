#!/usr/bin/env python3
"""Validate local hardware-GPU evidence; hosted software rendering is not a timing verdict.

Run the existing real product smoke with --display --capture --performance into
a fresh directory, then pass that directory here. This does not launch Godot,
relax frame budgets, fabricate missing evidence, or modify reference screenshots.
The supplied commit is operator provenance, not proof of an unmodified checkout.
"""
# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
from scripts.ci.validate_product_smoke_report import load_report, validate as validate_smoke
from scripts.ci.validate_product_visual_report import _load, validate_directory as validate_visual, validate_performance
from scripts.ci.validate_product_privacy_report import validate_directory as validate_privacy

SOFTWARE_MARKERS = (
    "warp", "llvmpipe", "llvm pipe", "softpipe", "lavapipe", "swiftshader",
    "basic render", "basic display", "gdi generic", "software raster", "software render",
    "microsoft render", "cpu renderer", "cpu device", "virtualbox", "vmware", "svga3d",
)
HARDWARE_VENDOR = re.compile(r"\b(nvidia|amd|radeon|intel|apple|qualcomm|adreno)\b", re.IGNORECASE)
DEVICE_LINE = re.compile(
    r"^(?:OpenGL API|Vulkan(?: API)?|Metal(?: API)?|D3D12(?: API)?)[^\r\n]*"
    r"Using Device(?: #\d+)?:\s*(?P<adapter>[^\r\n]+)$", re.MULTILINE)


class HardwareGpuEvidenceError(ValueError):
    pass


def hardware_adapter(runtime_log: str) -> str:
    """Use the actual Godot renderer line, never an OS GPU inventory or label."""
    matches = list(DEVICE_LINE.finditer(runtime_log))
    if len(matches) != 1:
        raise HardwareGpuEvidenceError("Exactly one actual Godot renderer/device line is required")
    line = matches[0].group(0)
    adapter = matches[0].group("adapter").strip()
    if any(marker in line.casefold() for marker in SOFTWARE_MARKERS):
        raise HardwareGpuEvidenceError("Software/virtual renderer cannot pass hardware GPU acceptance")
    if not HARDWARE_VENDOR.search(adapter):
        raise HardwareGpuEvidenceError("Unrecognized hardware adapter; manual investigation required")
    return adapter


def validate_measurements(runtime_log: str, performance: object, resolution: tuple[int, int]) -> str:
    adapter = hardware_adapter(runtime_log)
    # Preserve all existing 8-real-card/300+300-frame/zero-growth and 33.3/100 ms
    # constraints. A software renderer never reaches the performance validator.
    validate_performance(performance, resolution)
    return adapter


def validate_evidence(directory: Path, implementation_sha: str) -> dict:
    directory = directory.resolve(strict=True)
    if re.fullmatch(r"[0-9a-fA-F]{40}", implementation_sha) is None:
        raise HardwareGpuEvidenceError("A full implementation commit SHA is required")
    log_path = directory / "runtime.log"
    if log_path.is_symlink() or not log_path.is_file() or log_path.stat().st_size > 32 * 1024 * 1024:
        raise HardwareGpuEvidenceError("Missing, linked or oversized actual runtime log")
    log = log_path.read_text(encoding="utf-8", errors="strict")
    # Check the device before considering an otherwise-green software report.
    adapter = hardware_adapter(log)
    if log.count("SCGS_PRODUCT_V05_UI_SMOKE_OK") != 1:
        raise HardwareGpuEvidenceError("The real product smoke must complete exactly once")
    if any(marker in log for marker in ("SCRIPT ERROR:", "ERROR:", "Unhandled exception")):
        raise HardwareGpuEvidenceError("Runtime error in hardware GPU evidence")
    smoke = load_report(directory / "product-smoke.json")
    validate_smoke(smoke, expected_coverage="full-ui", require_display=True)
    resolution = smoke["viewport_width"], smoke["viewport_height"]
    performance_path = directory / "visuals/product-performance.json"
    performance = _load(performance_path)
    validate_measurements(log, performance, resolution)
    validate_visual(directory / "visuals", require_performance=True)
    validate_privacy(directory, require_gpu=True)
    # Digests bind this summary to the actual evidence, including independently
    # manifested screenshots. No private card data is copied into the summary.
    files = ("runtime.log", "product-smoke.json", "product-privacy.json",
             "visuals/product-visual.json", "visuals/product-performance.json")
    for relative in files:
        if (directory / relative).is_symlink():
            raise HardwareGpuEvidenceError("Linked reports cannot be acceptance evidence")
    return {
        "schema_version": 1,
        "suite": "product-v05-local-hardware-gpu",
        "success": True,
        "implementation_sha": implementation_sha.lower(),
        "implementation_sha_source": "operator-supplied",
        "adapter": adapter,
        "run_kind": smoke["run_kind"],
        "width": resolution[0], "height": resolution[1],
        "warmup_frames": performance["warmup_frames"],
        "measured_frames": performance["measured_frames"],
        "p95_ms": performance["p95_ms"], "max_ms": performance["max_ms"],
        "zero_growth": True,
        "performance_scope": "static-heavy-board-not-dynamic-presentation",
        "evidence_sha256": {
            relative: hashlib.sha256((directory / relative).read_bytes()).hexdigest() for relative in files
        },
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--implementation-sha", required=True)
    parser.add_argument("--report", type=Path, help="Optional new summary; never overwrites an existing file")
    args = parser.parse_args()
    try:
        if args.report is not None and args.report.exists():
            raise HardwareGpuEvidenceError("Refusing to overwrite existing hardware evidence")
        report = validate_evidence(args.directory, args.implementation_sha)
        if args.report is not None:
            args.report.parent.mkdir(parents=True, exist_ok=True)
            with args.report.open("x", encoding="utf-8") as output:
                json.dump(report, output, ensure_ascii=False, indent=2, allow_nan=False)
                output.write("\n")
        print(json.dumps(report, ensure_ascii=False, allow_nan=False))
        return 0
    except (OSError, ValueError) as error:
        print(f"Hardware GPU acceptance failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
