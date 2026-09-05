#!/usr/bin/env python3
"""Run one real v05 Godot source/export/ZIP UI smoke, with strict fresh evidence."""
from __future__ import annotations

import argparse
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
from scripts.ci.validate_product_smoke_report import load_report, validate
from scripts.ci.validate_product_visual_report import validate_directory
from scripts.ci.validate_product_privacy_report import validate_directory as validate_privacy_directory


def subprocess_environment() -> dict[str, str]:
    # A caller's managed-test/editor override must never make an exported or
    # unzipped game silently load a DLL from the build tree. Source smoke uses
    # its explicit --native-library argument, not inherited integration state.
    forbidden = {"SCGS_NATIVE_LIBRARY", "SCGS_V04_NATIVE_PATH", "SCGS_NATIVE_V05_LIBRARY"}
    return {key: value for key, value in os.environ.items() if key.upper() not in forbidden}


def build_command(args: argparse.Namespace, report: Path) -> list[str]:
    if args.artifact == "source" and args.project is None:
        raise ValueError("source smoke requires --project")
    if args.artifact != "source" and (args.project is not None or args.native_library is not None):
        raise ValueError("export/ZIP smoke must resolve its packaged library without overrides")
    if args.performance and not args.capture:
        raise ValueError("performance evidence requires --capture")
    if args.capture and not args.display:
        raise ValueError("GPU capture cannot run headless")
    command = [str(args.executable.resolve(strict=True))]
    command += ["--windowed"] if args.display else ["--headless"]
    command += ["--resolution", args.viewport, "--audio-driver", "Dummy"]
    if args.project is not None:
        command += ["--path", str(args.project.resolve(strict=True))]
    command += ["--", "--ci-product-smoke", f"--ci-product-report={report}",
                f"--ci-product-artifact={args.artifact}", f"--ci-product-coverage={args.coverage}",
                f"--ci-visual-viewport={args.viewport}"]
    if args.native_library is not None:
        command.append(f"--native-library={args.native_library.resolve(strict=True)}")
    if args.capture:
        command.append(f"--ci-product-capture={report.parent / 'visuals'}")
    if args.performance:
        command.append("--ci-product-performance")
    return command


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--executable", type=Path, required=True)
    parser.add_argument("--project", type=Path)
    parser.add_argument("--native-library", type=Path)
    parser.add_argument("--artifact", choices=("source", "export", "zip"), required=True)
    parser.add_argument("--coverage", choices=("full-ui", "natural-ui"), required=True)
    parser.add_argument("--viewport", choices=("1280x720", "1600x900", "2560x1440", "2560x1600"), default="1600x900")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--display", action="store_true")
    parser.add_argument("--capture", action="store_true")
    parser.add_argument("--performance", action="store_true")
    args = parser.parse_args()
    try:
        output = args.output.resolve()
        report = output / "product-smoke.json"
        if report.exists():
            raise ValueError("refusing to reuse an existing product report; use a fresh evidence directory")
        command = build_command(args, report)
        output.mkdir(parents=True, exist_ok=True)
        wrapper = [sys.executable, str(ROOT / "scripts/ci/run_with_timeout.py"),
                   "--timeout", "600", "--expect-output", "SCGS_PRODUCT_V05_UI_SMOKE_OK",
                   "--expect-output-count", "1", "--forbid-output", "SCRIPT ERROR:",
                   "--forbid-output", "ERROR:", "--forbid-output", "Unhandled exception",
                   "--", *command]
        result = subprocess.run(wrapper, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                cwd=ROOT if args.project is not None else args.executable.resolve().parent,
                                env=subprocess_environment())
        (output / "runtime.log").write_bytes(result.stdout)
        sys.stdout.buffer.write(result.stdout)
        if result.returncode != 0:
            return result.returncode
        payload = load_report(report)
        validate(payload, args.artifact, args.coverage, require_display=args.display)
        width, height = map(int, args.viewport.split("x"))
        if (payload["viewport_width"], payload["viewport_height"]) != (width, height):
            raise ValueError("real product viewport differs from requested dimensions")
        if args.capture:
            validate_directory(output / "visuals", require_performance=args.performance)
        validate_privacy_directory(output, require_gpu=args.display)
    except (OSError, ValueError) as error:
        print(f"Product UI smoke failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
