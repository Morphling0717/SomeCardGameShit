"""Contract tests for the bounded subprocess runner."""

from __future__ import annotations

import subprocess
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
RUNNER = ROOT / "scripts/ci/run_with_timeout.py"


class RunWithTimeoutTests(unittest.TestCase):
    def run_runner(self, child_source: str, *arguments: str) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [
                sys.executable,
                str(RUNNER),
                "--timeout",
                "10",
                *arguments,
                "--",
                sys.executable,
                "-c",
                child_source,
            ],
            cwd=ROOT,
            capture_output=True,
            text=True,
            check=False,
        )

    def test_expected_marker_can_be_required_exactly_once(self) -> None:
        result = self.run_runner(
            "print('SCGS_OK')",
            "--expect-output",
            "SCGS_OK",
            "--expect-output-count",
            "1",
        )

        self.assertEqual(0, result.returncode, result.stderr)

    def test_duplicate_marker_is_rejected(self) -> None:
        result = self.run_runner(
            "print('SCGS_OK'); print('SCGS_OK')",
            "--expect-output",
            "SCGS_OK",
            "--expect-output-count",
            "1",
        )

        self.assertNotEqual(0, result.returncode)
        self.assertIn("expected 1, found 2", result.stderr)

    def test_count_requires_a_marker(self) -> None:
        result = self.run_runner(
            "print('anything')",
            "--expect-output-count",
            "1",
        )

        self.assertNotEqual(0, result.returncode)
        self.assertIn("requires --expect-output", result.stderr)


if __name__ == "__main__":
    unittest.main()
