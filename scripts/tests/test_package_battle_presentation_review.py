# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import json
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch

import scripts.dev.package_battle_presentation_review as review


class BattlePresentationReviewPackageTests(unittest.TestCase):
    def _export(self, root: Path) -> tuple[Path, Path]:
        export = root / "export" / "SomeCardGameShit.exe"
        export.parent.mkdir()
        export.write_bytes(b"synthetic executable (auditor mocked in this unit test)")
        export.with_suffix(".pck").write_bytes(b"synthetic pack")
        native = root / "build" / "scgs_v05.dll"
        native.parent.mkdir()
        native.write_bytes(b"synthetic v05 DLL")
        (export.parent / native.name).write_bytes(native.read_bytes())
        (export.parent / "data_Scgs_windows_x86_64").mkdir()
        (export.parent / "data_Scgs_windows_x86_64" / "Scgs.dll").write_bytes(b"managed bytes")
        (export.parent / "licenses").mkdir()
        (export.parent / "licenses" / "notice.txt").write_text("synthetic license", encoding="utf-8")
        return export, native

    def test_launcher_uses_relative_quoted_executable_and_only_explicit_review_entry(self) -> None:
        source = {"base_commit": "a" * 40, "worktree_dirty": False}
        launcher = review._launcher("SomeCardGameShit.exe", source)
        self.assertIn('"%~dp0SomeCardGameShit.exe" -- --battle-presentation-review', launcher)
        self.assertIn("--review-source-sha=" + "a" * 40, launcher)
        self.assertNotIn("--ci-", launcher)
        self.assertNotIn("%*", launcher)
        source["worktree_dirty"] = True
        self.assertNotIn("--review-source-sha", review._launcher("SomeCardGameShit.exe", source))
        with self.assertRaises(review.ReviewPackageError):
            review._launcher("unsafe&name.exe", source)

    def test_dirty_source_requires_explicit_worktree_label(self) -> None:
        with patch.object(review.subprocess, "run") as run:
            run.side_effect = [type("Output", (), {"stdout": "a" * 40})(),
                               type("Output", (), {"stdout": " M client/godot/project.godot"})()]
            with self.assertRaisesRegex(review.ReviewPackageError, "--allow-worktree"):
                review._source_identity(Path("."), False)

    def test_roundtrip_keeps_default_executable_and_reports_unverified_dirty_provenance(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            export, native = self._export(root)
            original_bytes = export.read_bytes()
            output = root / "packages" / "review.zip"
            identity = {"base_commit": "a" * 40, "worktree_dirty": True,
                        "export_provenance": "operator_supplied_export_not_rebuilt_by_packager"}
            with patch.object(review, "_source_identity", return_value=identity), \
                 patch.object(review, "_audit_finalized") as audit, \
                 patch.object(review.subprocess, "run", side_effect=AssertionError("must not launch anything")):
                result = review.package(export, native, output, allow_worktree=True)
            self.assertEqual(3, audit.call_count)  # Source, staged, actual ZIP roundtrip.
            self.assertFalse(result["runtime_launched"])
            self.assertEqual(original_bytes, export.read_bytes())
            self.assertFalse((export.parent / review.LAUNCHER).exists())
            with zipfile.ZipFile(output) as archive:
                self.assertEqual(original_bytes, archive.read(export.name))
                self.assertIn("--battle-presentation-review", archive.read(review.LAUNCHER).decode("ascii"))
                manifest = json.loads(archive.read(review.PACKAGE_MANIFEST))
                self.assertEqual(identity, manifest["source"])
                self.assertFalse(manifest["runtime_launched_by_packager"])
                self.assertEqual(len(archive.namelist()) - 1, len(manifest["files"]))
                self.assertNotIn("--review-source-sha", " ".join(manifest["launch_arguments"]))
            with self.assertRaisesRegex(review.ReviewPackageError, "never overwritten"):
                review.package(export, native, output, allow_worktree=True)

    def test_unexpected_export_root_files_are_not_silently_packaged(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            export, _ = self._export(Path(temporary))
            (export.parent / "runtime.log").write_text("possibly private", encoding="utf-8")
            with self.assertRaisesRegex(review.ReviewPackageError, "unexpected export-root"):
                review._source_files(export)

    def test_nested_private_review_evidence_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            export, _ = self._export(Path(temporary))
            private = export.parent / "data_Scgs_windows_x86_64" / "review-evidence"
            private.mkdir()
            (private / "trace.json").write_text("private", encoding="utf-8")
            with self.assertRaisesRegex(review.ReviewPackageError, "private review data"):
                review._source_files(export)

    def test_dll_mismatch_fails_before_creating_package(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            export, native = self._export(root)
            native.write_bytes(b"different newly built DLL")
            output = root / "review.zip"
            with patch.object(review, "_source_identity", return_value={}), patch.object(review, "_audit_finalized"):
                with self.assertRaisesRegex(review.ReviewPackageError, "differs from the explicit current build"):
                    review.package(export, native, output)
            self.assertFalse(output.exists())

    def test_output_inside_export_is_rejected_without_auditing_or_writing(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            export, native = self._export(Path(temporary))
            with self.assertRaisesRegex(review.ReviewPackageError, "outside the source export"):
                review.package(export, native, export.parent / "bad.zip")

    def test_audit_failure_never_creates_success_package(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            export, native = self._export(root)
            with patch.object(review, "_source_identity", return_value={}), \
                 patch.object(review, "_audit_finalized", side_effect=ValueError("MCP autoload leak")):
                with self.assertRaisesRegex(ValueError, "MCP autoload"):
                    review.package(export, native, root / "review.zip")
            self.assertFalse((root / "review.zip").exists())


if __name__ == "__main__":
    unittest.main()
