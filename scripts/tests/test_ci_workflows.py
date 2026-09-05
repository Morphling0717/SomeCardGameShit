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
            "client/godot/assets/visual/anime_v1/cards/PROVENANCE.md",
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
            "Product v05 current-project full UI and minimum-size smoke",
            "--artifact source --coverage full-ui --viewport 1600x900 --display",
            "--artifact source --coverage natural-ui --viewport 1280x720 --display",
            "Export and finalize v05-only Windows client",
            "Audit and launch exported v05 Windows client",
            "Round-trip audit and launch packaged v05 Windows client",
            "--artifact export --coverage natural-ui",
            "--artifact zip --coverage natural-ui",
            "SomeCardGameShit-product-playable-v1-windows-x86_64.zip",
        ):
            self.assertIn(marker, self.fast)
        self.assertEqual(1, self.fast.count("Compress-Archive"))
        self.assertNotIn("Copy-Item -LiteralPath $r2Package", self.fast)
        for marker in ("--ci-smoke", "--ci-anime", "--require-anime", "--capture", "--performance", "2560x1600"):
            self.assertNotIn(marker, self.fast)

    def test_desktop_player_exports_only_v05_and_managed_uses_explicit_fixture(self) -> None:
        self.assertNotIn("--v04-library", self.fast)
        self.assertEqual(2, self.fast.count("--v05-library"))
        self.assertEqual(2, self.fast.count("--product-native-library"))
        self.assertIn("build/ci-msvc/Release/scgs_v04_fixture.dll", self.fast)
        self.assertIn("build/ci-macos/libscgs_v04_fixture.dylib", self.fast)
        self.assertIn(
            'codesign --force --sign - "$app/Contents/Frameworks/libscgs_v05.dylib"',
            self.fast,
        )

        self.assertNotIn("--v04-library", self.heavy)
        self.assertEqual(1, self.heavy.count("--v05-library"))
        self.assertNotIn("--product-native-library", self.heavy)
        self.assertIn("build/ci-visual-heavy/Release/scgs_v04_fixture.dll", self.heavy)

    def test_heavy_workflow_is_explicit_four_resolution_real_product_acceptance(self) -> None:
        self.assertIn('cron: "17 20 * * *"', self.heavy)
        self.assertIn("workflow_dispatch:", self.heavy)
        self.assertIn('".github/workflows/windows-visual-heavy.yml"', self.heavy)
        self.assertIn("inputs.ref || github.sha", self.heavy)
        for marker in ("1280x720", "1600x900", "2560x1440", "2560x1600", "--capture", "--display", "--performance", '"--coverage", "full-ui"'):
            self.assertIn(marker, self.heavy)
        for marker in ("--skip-performance-budget", "--ci-smoke", "--legacy-2d-board", "--export-release", "Compress-Archive", "--require-anime"):
            self.assertNotIn(marker, self.heavy)
        self.assertIn("$failedViewports += $viewport", self.heavy)
        self.assertIn("if ($failedViewports.Count -gt 0)", self.heavy)
        self.assertIn('throw "Product visual acceptance failed:', self.heavy)

    def test_arm64_cold_import_budget_does_not_relax_gameplay_or_visual_gates(self) -> None:
        macos = self.fast.split("  macos-arm64:", 1)[1]
        preparation = macos.split("      - name: Godot headless import", 1)[1].split(
            "      - name: Product v05 current-project full UI smoke", 1
        )[0]
        self.assertIn("--timeout 1800", preparation)
        self.assertIn('--forbid-output "SCRIPT ERROR:" --forbid-output "ERROR:"', preparation)
        self.assertIn('-- "$GODOT4" --headless --path client/godot --import', preparation)
        self.assertEqual(1, self.fast.count("--timeout 1800"))
        self.assertNotIn("--timeout 1800", self.heavy)
        # Runtime has its own bounded process and stricter in-game deadline.
        runner = (ROOT / "scripts/ci/run_product_smoke.py").read_text(encoding="utf-8")
        self.assertIn('"--timeout", "600"', runner)
        self.assertNotIn('"--timeout", "1800"', runner)
        self.assertIn("validate_privacy_directory(output, require_gpu=args.display)", runner)

    def test_import_cache_is_exact_platform_locked_output_only_and_never_skips_import(self) -> None:
        windows = self.fast.split("  windows-msvc:", 1)[1].split("  macos-arm64:", 1)[0]
        macos = self.fast.split("  macos-arm64:", 1)[1]
        keys: list[str] = []
        for job, target, installer in (
            (windows, "windows-x86_64", "install_godot_windows.ps1"),
            (macos, "macos-arm64", "install_godot_macos.sh"),
            (self.heavy, "windows-x86_64", "install_godot_windows.ps1"),
        ):
            with self.subTest(target=target):
                marker = "      - name: Cache exact Godot imported assets\n"
                self.assertEqual(1, job.count(marker))
                cache, following = job.split(marker, 1)[1].split(
                    "      - name: Godot headless import\n", 1
                )
                self.assertIn("uses: actions/cache@v5", cache)
                self.assertIn("path: client/godot/.godot/imported\n", cache)
                self.assertEqual(1, cache.count("path:"))
                self.assertNotIn("restore-keys:", cache)
                self.assertNotIn("if:", cache)
                key = next(line.strip() for line in cache.splitlines() if line.strip().startswith("key:"))
                self.assertIn(f"godot-imported-{target}-4.7.2-mono-v1-", key)
                self.assertIn(f"hashFiles('scripts/ci/{installer}')", key)
                for input_path in ("client/godot/assets/**", "client/godot/**/*.import",
                                   "client/godot/project.godot", "client/godot/export_presets.cfg",
                                   "client/godot/addons/**"):
                    self.assertIn(f"'{input_path}'", key)
                keys.append(key)
                preparation = following.split("      - name:", 1)[0]
                self.assertNotIn("if:", preparation)
                self.assertNotIn("cache-hit", preparation)
                self.assertIn("--import", preparation)
        self.assertEqual(keys[0], keys[2])
        self.assertNotEqual(keys[0], keys[1])


if __name__ == "__main__":
    unittest.main()
