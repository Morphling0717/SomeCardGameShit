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
    def _frame_pack(self, export: Path, *, omit: str | None = None,
                    extra: str | None = None) -> None:
        from scripts.tests.test_check_godot_mcp_export import pack, settings
        entries = {"project.binary": settings()}
        for name in (*review.FRAME_MODELS, *review.FRAME_TEXTURES):
            if name == omit:
                continue
            extension = "scn" if name.endswith(".glb") else "s3tc.ctex"
            entries[review.FRAME_ROOT + name + ".remap"] = b"synthetic remap"
            entries[".godot/imported/" + name + "-" + "a" * 32 + "." + extension] = b"synthetic imported payload"
        if extra is not None:
            entries[extra] = b"not distributable"
        export.with_suffix(".pck").write_bytes(pack(entries))

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

    def test_r1_launcher_is_explicit_without_changing_v2_default(self) -> None:
        source = {"base_commit": "a" * 40, "worktree_dirty": False}
        launcher = review._launcher("SomeCardGameShit.exe", source, entry="card-frame-r1")
        self.assertIn('"%~dp0SomeCardGameShit.exe" -- --card-frame-review', launcher)
        self.assertNotIn("--battle-presentation-review", launcher)
        self.assertIn("--battle-presentation-review", review._launcher("SomeCardGameShit.exe", source))
        with self.assertRaisesRegex(review.ReviewPackageError, "unknown review entry"):
            review._launcher("SomeCardGameShit.exe", source, entry="arbitrary&argument")

    def test_r1_pack_has_unique_launcher_scope_and_source_model_provenance(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            export, native = self._export(root)
            output = root / review.ENTRIES["card-frame-r1"]["archive"]
            identity = {"base_commit": "a" * 40, "worktree_dirty": True}
            proof = {"source_model_manifest_sha256": "b" * 64,
                     "gpu_or_user_visual_approval_claimed": False}
            with patch.object(review, "_source_identity", return_value=identity), \
                 patch.object(review, "_audit_finalized") as common, \
                 patch.object(review, "_audit_card_frame_export", return_value=proof) as frame:
                review.package(export, native, output, allow_worktree=True, entry="card-frame-r1")
            self.assertEqual(3, common.call_count)
            self.assertEqual(3, frame.call_count)
            with zipfile.ZipFile(output) as archive:
                self.assertIn("PLAY_CARD_FRAME_R1_REVIEW.cmd", archive.namelist())
                self.assertNotIn(review.LAUNCHER, archive.namelist())
                self.assertEqual((review.ROOT / review.FRAME_MANIFEST).read_bytes(),
                                 archive.read("licenses/CARD_FRAME_R1_MODEL_MANIFEST.json"))
                manifest = json.loads(archive.read(review.PACKAGE_MANIFEST))
                self.assertEqual("card-frame-r1-windows-review", manifest["kind"])
                self.assertEqual(["--", "--card-frame-review"], manifest["launch_arguments"])
                self.assertEqual(proof, manifest["card_frame_r1_audit"])
                text = archive.read(review.PACKAGE_README).decode("utf-8-sig")
                self.assertIn("卡框精修 R1", text)
                self.assertIn("不代表所有卡牌换装", text)
                self.assertNotIn("本阶段不代表所有卡牌演出完成", text)
                self.assertFalse(manifest["runtime_launched_by_packager"])

    def test_v2_default_does_not_silently_require_r1_geometry(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            export, native = self._export(root)
            identity = {"base_commit": "a" * 40, "worktree_dirty": False}
            with patch.object(review, "_source_identity", return_value=identity), \
                 patch.object(review, "_audit_finalized"), \
                 patch.object(review, "_audit_card_frame_export", side_effect=AssertionError("R1 must be explicit")):
                review.package(export, native, root / "old-review.zip")

    def test_r1_checks_both_lods_all_maps_and_rejects_missing_model_payload(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            export, _ = self._export(root)
            source = {"manifest_sha256": "b" * 64, "models": {"high": {}, "low": {}}}
            with patch.object(review, "audit_card_frame", return_value=source):
                self._frame_pack(export)
                result = review._audit_card_frame_export(export)
                self.assertEqual(list(review.FRAME_MODELS), result["packed_model_entries"])
                self.assertEqual(list(review.FRAME_TEXTURES), result["packed_texture_entries"])
                self.assertFalse(result["packed_importer_bytes_match_source_claimed"])
                for missing in (*review.FRAME_MODELS, *review.FRAME_TEXTURES):
                    with self.subTest(missing=missing):
                        self._frame_pack(export, omit=missing)
                        with self.assertRaisesRegex(review.ReviewPackageError, "missing resource entry"):
                            review._audit_card_frame_export(export)

    def test_r1_refuses_import_alias_without_compiled_resource(self) -> None:
        from scripts.tests.test_check_godot_mcp_export import pack, settings
        with tempfile.TemporaryDirectory() as temporary:
            export, _ = self._export(Path(temporary))
            export.with_suffix(".pck").write_bytes(pack({"project.binary": settings(),
                review.FRAME_ROOT + review.FRAME_MODELS[0] + ".remap": b"dangling alias"}))
            with patch.object(review, "audit_card_frame", return_value={}):
                with self.assertRaisesRegex(review.ReviewPackageError, "no imported payload"):
                    review._audit_card_frame_export(export)

    def test_r1_rejects_editable_model_sources_in_pack_or_player_directory(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            export, _ = self._export(Path(temporary))
            with patch.object(review, "audit_card_frame", return_value={}):
                for path in ("art/card_frame_r1/concept.png", "scripts/art/build_frame.py",
                             "assets/frame.blend", "assets/frame.blend1"):
                    with self.subTest(path=path):
                        self._frame_pack(export, extra=path)
                        with self.assertRaisesRegex(review.ReviewPackageError, "source leaked"):
                            review._audit_card_frame_export(export)
                self._frame_pack(export)
                (export.parent / "licenses" / "frame.blend").write_bytes(b"source")
                with self.assertRaisesRegex(review.ReviewPackageError, "Blender source"):
                    review._audit_card_frame_export(export)

    def test_r1_bad_source_model_hash_fails_before_zip_creation(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            export, native = self._export(root)
            with patch.object(review, "_source_identity", return_value={}), \
                 patch.object(review, "_audit_finalized"), \
                 patch.object(review, "audit_card_frame", side_effect=ValueError("model SHA-256 mismatch")):
                with self.assertRaisesRegex(ValueError, "model SHA-256 mismatch"):
                    review.package(export, native, root / "r1.zip", entry="card-frame-r1")
            self.assertFalse((root / "r1.zip").exists())


if __name__ == "__main__":
    unittest.main()
