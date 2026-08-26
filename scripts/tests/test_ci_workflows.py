"""Contracts for fast-vs-nightly CI routing."""

from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from scripts.ci.classify_ci_changes import (
    EMPTY_TREE_SHA,
    ZERO_SHA,
    _git_command,
    classify_paths,
    write_github_outputs,
)


ROOT = Path(__file__).resolve().parents[2]
FAST_WORKFLOW = ROOT / ".github/workflows/ci.yml"
HEAVY_WORKFLOW = ROOT / ".github/workflows/windows-visual-heavy.yml"


class ChangeClassifierTests(unittest.TestCase):
    def test_documentation_only_requires_a_nonempty_all_document_change_set(self) -> None:
        docs_only, paths = classify_paths(
            ["README.md", "docs/toolchain.md", "client/godot/README.md"]
        )
        self.assertTrue(docs_only)
        self.assertEqual(3, len(paths))
        self.assertFalse(classify_paths([])[0])
        self.assertFalse(classify_paths(["docs/toolchain.md", "CMakeLists.txt"])[0])
        self.assertFalse(classify_paths([".github/workflows/ci.yml"])[0])
        self.assertFalse(classify_paths(["../README.md"])[0])

    def test_exported_notices_and_slice_readmes_require_full_ci(self) -> None:
        for path in (
            "LICENSE",
            "THIRD_PARTY_NOTICES.md",
            "client/godot/ASSET_NOTICES.md",
            "client/godot/assets/fonts/NOTICE.md",
            "client/godot/assets/visual/anime_v1/slice/PROVENANCE.md",
            "client/godot/assets/visual/anime_v1/card_body/PROVENANCE.md",
            "docs/anime-v1-visual-slice.md",
            "docs/anime-v1-card-body-r1.md",
            "docs/native-api-v04.md",
            "docs/native-api-v05.md",
        ):
            with self.subTest(path=path):
                self.assertFalse(classify_paths([path])[0])

    def test_github_outputs_are_scalar_and_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "github-output.txt"
            write_github_outputs(output, False, ("engine/game.cpp",))
            self.assertEqual(
                "docs_only=false\nfull_ci=true\nchanged_count=1\n",
                output.read_text(encoding="utf-8"),
            )

    def test_new_ref_lists_the_whole_tree_instead_of_trusting_tip_diff(self) -> None:
        self.assertEqual(
            ["git", "ls-tree", "-r", "--name-only", "HEAD"],
            _git_command(base=ZERO_SHA, head="HEAD", name_only=True),
        )

    def test_changed_paths_disable_rename_detection_to_keep_deleted_package_inputs(self) -> None:
        self.assertEqual(
            ["git", "diff", "--no-renames", "--name-only", "BASE", "HEAD"],
            _git_command(base="BASE", head="HEAD", name_only=True),
        )

    def test_new_ref_whitespace_checks_the_final_tree_against_empty_tree(self) -> None:
        self.assertEqual(
            ["git", "diff", "--check", EMPTY_TREE_SHA, "HEAD"],
            _git_command(base=ZERO_SHA, head="HEAD", check=True),
        )


class WorkflowTieringContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.fast = FAST_WORKFLOW.read_text(encoding="utf-8")
        cls.heavy = HEAVY_WORKFLOW.read_text(encoding="utf-8")

    def test_fast_workflow_classifies_docs_and_isolates_duplicate_runs(self) -> None:
        windows = self.fast.split("  windows-msvc:", 1)[1].split(
            "  macos-arm64:", 1
        )[0]
        self.assertIn("classify_ci_changes.py", self.fast)
        self.assertIn("docs_only", self.fast)
        self.assertIn("cancel-in-progress: true", self.fast)
        self.assertIn("github.event_name", self.fast)
        self.assertIn("github.event.pull_request.head.sha || github.sha", self.fast)
        self.assertNotIn("dorny/paths-filter", self.fast)
        self.assertNotIn("Gate 4B-R2 display-backed visual and performance suite", windows)
        self.assertNotIn("legacy 2D source regression smoke", windows)
        self.assertNotIn("R3 candidate", windows)
        self.assertEqual(
            4,
            self.fast.count(
                "(github.event_name == 'push' ||\n"
                "       needs.classify-changes.outputs.full_ci == 'true')"
            ),
        )

    def test_fast_windows_keeps_current_product_gates_and_one_package(self) -> None:
        for marker in (
            "ctest --test-dir build/ci-msvc",
            "Audit v04 native artifact",
            "Audit v05 native artifact",
            "Locked managed restore, build, and tests",
            "Godot headless import",
            "Godot AnimeV1 display-backed structural screenshot matrix",
            "Godot AnimeV1 integrated card-body real-actor slice",
            "SCGS_ANIME_CARD_BODY_SLICE_OK",
            "validate_anime_card_body_slice.py",
            "Godot default 3D current-project native smoke",
            "Export and finalize Windows client",
            "Audit and launch exported Windows default 3D client",
            "Round-trip audit and launch packaged Windows default 3D client",
            "--require-anime-card-body-launcher",
        ):
            self.assertIn(marker, self.fast)
        self.assertEqual(1, self.fast.count("Compress-Archive"))
        self.assertNotIn("Copy-Item -LiteralPath $r2Package", self.fast)

    def test_heavy_workflow_is_scheduled_manual_and_never_claims_warp_timing(self) -> None:
        self.assertIn('cron: "17 20 * * *"', self.heavy)
        self.assertIn("workflow_dispatch:", self.heavy)
        self.assertIn('".github/workflows/windows-visual-heavy.yml"', self.heavy)
        self.assertIn("legacy 2D source regression smoke", self.heavy)
        self.assertIn("Gate 4B-R2 1600x900 visual and resource suite", self.heavy)
        self.assertIn("R3 candidate", self.heavy)
        self.assertIn("Round-trip launch packaged Windows migration launchers", self.heavy)
        self.assertIn("--skip-performance-budget", self.heavy)
        self.assertNotIn("p95 <=", self.heavy)
        self.assertNotIn("p95 ≤", self.heavy)
        self.assertNotIn("2560x1440", self.heavy)
        self.assertNotIn("2560x1600", self.heavy)


if __name__ == "__main__":
    unittest.main()
