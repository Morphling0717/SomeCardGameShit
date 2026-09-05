# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import argparse
from pathlib import Path
import tempfile
import unittest
from unittest import mock

from scripts.ci.run_product_smoke import build_command, main, subprocess_environment


class ProductSmokeInvocationTests(unittest.TestCase):
    def test_child_environment_cannot_inherit_editor_or_fixture_native_overrides(self) -> None:
        environment = {"PATH": "locked-sdk-and-runtime", "DOTNET_ROOT": "locked-sdk",
                       "SCGS_NATIVE_LIBRARY": "old-v04.dll", "SCGS_V04_NATIVE_PATH": "fixture.dll",
                       "scgs_native_v05_library": "outside-export-v05.dll"}
        with mock.patch.dict("os.environ", environment, clear=True):
            self.assertEqual({"PATH": "locked-sdk-and-runtime", "DOTNET_ROOT": "locked-sdk"},
                             subprocess_environment())

    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.executable = self.root / "game.exe"
        self.executable.write_bytes(b"executable test fixture")
        self.library = self.root / "scgs_v05.dll"
        self.library.write_bytes(b"v05 test fixture")
        self.project = self.root / "project"
        self.project.mkdir()
        self.report = self.root / "evidence/product-smoke.json"

    def arguments(self, **overrides: object) -> argparse.Namespace:
        values = dict(executable=self.executable, project=self.project,
                      native_library=self.library, artifact="source", coverage="full-ui",
                      viewport="1600x900", display=True, capture=False, performance=False)
        values.update(overrides)
        return argparse.Namespace(**values)

    def test_source_is_explicit_v05_real_ui_not_legacy_smoke(self) -> None:
        command = build_command(self.arguments(), self.report)
        self.assertEqual(str(self.executable.resolve()), command[0])
        self.assertIn("--windowed", command)
        self.assertIn("--ci-product-smoke", command)
        self.assertIn("--ci-product-coverage=full-ui", command)
        self.assertIn(f"--native-library={self.library.resolve()}", command)
        self.assertIn(f"--ci-product-report={self.report}", command)
        self.assertNotIn("--ci-smoke", command)
        self.assertNotIn("--headless", command)

    def test_source_needs_project(self) -> None:
        with self.assertRaisesRegex(ValueError, "requires --project"):
            build_command(self.arguments(project=None), self.report)

    def test_export_and_zip_cannot_hide_broken_packaged_native_layout(self) -> None:
        for artifact in ("export", "zip"):
            for overrides in ({}, {"project": None}, {"native_library": None}):
                with self.subTest(artifact=artifact, overrides=overrides):
                    with self.assertRaisesRegex(ValueError, "without overrides"):
                        build_command(self.arguments(artifact=artifact, **overrides), self.report)
            command = build_command(self.arguments(artifact=artifact, project=None,
                native_library=None, coverage="natural-ui", display=False), self.report)
            self.assertIn("--headless", command)
            self.assertNotIn("--path", command)
            self.assertFalse(any(value.startswith("--native-library=") for value in command))

    def test_capture_and_performance_are_explicit_real_gpu_only(self) -> None:
        with self.assertRaisesRegex(ValueError, "requires --capture"):
            build_command(self.arguments(performance=True), self.report)
        with self.assertRaisesRegex(ValueError, "cannot run headless"):
            build_command(self.arguments(capture=True, display=False), self.report)
        command = build_command(self.arguments(capture=True, performance=True), self.report)
        self.assertIn(f"--ci-product-capture={self.report.parent / 'visuals'}", command)
        self.assertIn("--ci-product-performance", command)

    def test_missing_executable_or_library_fails_before_launch(self) -> None:
        for field in ("executable", "native_library", "project"):
            with self.subTest(field=field), self.assertRaises(FileNotFoundError):
                build_command(self.arguments(**{field: self.root / "does-not-exist"}), self.report)

    def test_stale_report_never_launches_or_overwrites_evidence(self) -> None:
        self.report.parent.mkdir()
        self.report.write_text("old evidence", encoding="utf-8")
        argv = ["run_product_smoke.py", "--executable", str(self.executable),
                "--project", str(self.project), "--artifact", "source", "--coverage", "full-ui",
                "--output", str(self.report.parent)]
        with mock.patch("sys.argv", argv), mock.patch("scripts.ci.run_product_smoke.subprocess.run") as run:
            self.assertEqual(1, main())
            run.assert_not_called()
        self.assertEqual("old evidence", self.report.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
