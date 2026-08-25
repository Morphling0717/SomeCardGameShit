# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import hashlib
import os
import shutil
import struct
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path


SCRIPTS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS))
sys.path.insert(0, str(SCRIPTS / "ci"))

from audit_godot_export import (  # noqa: E402
    EXACT_PACKAGED_SOURCE_FILES,
    ExportAuditError,
    LICENSE_MARKERS,
    MACOS_ANIME_LAUNCHER,
    WINDOWS_ANIME_LAUNCHER,
    _audit_anime_slice_launcher,
    _audit_licenses,
    _audit_macos_bundle_architectures,
)
from finalize_godot_export import LICENSES as FINALIZED_LICENSES  # noqa: E402
from prepare_godot_macos_template import (  # noqa: E402
    ARM64_ENTRY,
    UNIVERSAL_ENTRY,
    TemplatePreparationError,
    prepare_template,
)
from run_managed_gate3 import GODOT_BUILD_CONFIGURATIONS  # noqa: E402


def _thin_mach_o(cpu: int) -> bytes:
    return b"\xcf\xfa\xed\xfe" + struct.pack("<I", cpu) + bytes(64)


def _fat_mach_o(*cpus: int) -> bytes:
    entries = b"".join(
        struct.pack(">IIIII", cpu, 0, 0, 0, 0) for cpu in cpus
    )
    return b"\xca\xfe\xba\xbe" + struct.pack(">I", len(cpus)) + entries


def _write_template_archive(path: Path, binary: bytes | None) -> str:
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("macos_template.app/Contents/Info.plist", "plist")
        if binary is not None:
            entry = zipfile.ZipInfo(UNIVERSAL_ENTRY, date_time=(2026, 8, 22, 0, 0, 0))
            entry.create_system = 3
            entry.compress_type = zipfile.ZIP_DEFLATED
            entry.external_attr = (0o100755) << 16
            archive.writestr(entry, binary)
    return hashlib.sha512(path.read_bytes()).hexdigest()


def _write_packaged_notices(directory: Path, *, commit: str = "local") -> None:
    for filename, marker in LICENSE_MARKERS.items():
        destination = directory / filename
        if filename in EXACT_PACKAGED_SOURCE_FILES:
            shutil.copy2(EXACT_PACKAGED_SOURCE_FILES[filename], destination)
        elif filename == "BUILD_INFO.txt":
            destination.write_text(
                "SomeCardGameShit Gate 4B-R2\n"
                f"commit={commit}\n"
                "godot=4.7.2.stable.mono\n"
                "dotnet_sdk=10.0.400\n"
                "dotnet_runtime=8.0.30\n",
                encoding="utf-8",
            )
        else:
            destination.write_text(marker, encoding="utf-8")


class GodotExportAuditTests(unittest.TestCase):
    def test_finalize_and_audit_lock_gate4b_build_info(self) -> None:
        root = SCRIPTS.parent
        finalize = (root / "scripts/ci/finalize_godot_export.py").read_text(
            encoding="utf-8"
        )
        audit_source = (root / "scripts/audit_godot_export.py").read_text(
            encoding="utf-8"
        )
        for source in (finalize, audit_source):
            with self.subTest(source=source.splitlines()[1]):
                self.assertIn("SomeCardGameShit Gate 4B-R2", source)
                self.assertNotIn("SomeCardGameShit Gate 3C", source)

    def test_packaged_r3_launcher_has_distinct_human_and_ci_modes(self) -> None:
        root = SCRIPTS.parent
        launcher_path = root / "scripts/ci/PLAY_R3_VISUAL_SLICE.cmd"
        launcher = launcher_path.read_text(encoding="utf-8")
        finalize = (root / "scripts/ci/finalize_godot_export.py").read_text(
            encoding="utf-8"
        )
        workflow = (root / ".github/workflows/ci.yml").read_text(
            encoding="utf-8"
        )

        self.assertIn('if /I "%SCGS_R3_LAUNCHER_CI%"=="1" goto ci_mode', launcher)
        self.assertIn(
            'set "SCGS_R3_OUTPUT=%~dp0r3-visual-slice-output"', launcher
        )
        self.assertEqual(1, launcher.count("--r3-visual-slice-exit"))
        self.assertIn("if not defined SCGS_R3_LAUNCHER_OUTPUT", launcher)
        self.assertIn(
            'set "SCGS_R3_OUTPUT=%SCGS_R3_LAUNCHER_OUTPUT%"', launcher
        )
        self.assertIn('"--r3-visual-slice=%SCGS_R3_OUTPUT%"', launcher)
        self.assertIn("DisableDelayedExpansion", launcher)
        self.assertIn("WINDOWS_R3_LAUNCHER", finalize)
        self.assertIn('export.parent / "PLAY_R3_VISUAL_SLICE.cmd"', finalize)

        packaged_step = workflow.split(
            "- name: Round-trip launch packaged Windows R3 visual slice", 1
        )[1].split(
            "- name: Round-trip audit and launch packaged Windows default 3D client",
            1,
        )[0]
        self.assertIn('$env:SCGS_R3_LAUNCHER_CI = "1"', packaged_step)
        self.assertIn('$env:SCGS_R3_LAUNCHER_OUTPUT = $slice', packaged_step)
        self.assertIn('-- "$env:ComSpec" /d /c call "$launcher"', packaged_step)
        self.assertNotIn('-- "$export" --windowed', packaged_step)
        self.assertIn("--expect-output-count 1", packaged_step)
        self.assertIn("validate_r3_visual_slice.py", packaged_step)

    def test_packaged_anime_launchers_are_safe_and_round_tripped_on_both_platforms(
        self,
    ) -> None:
        root = SCRIPTS.parent
        windows_launcher = WINDOWS_ANIME_LAUNCHER.read_text(encoding="utf-8")
        macos_launcher = MACOS_ANIME_LAUNCHER.read_text(encoding="utf-8")
        finalize = (root / "scripts/ci/finalize_godot_export.py").read_text(
            encoding="utf-8"
        )
        workflow = (root / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        attributes = (root / ".gitattributes").read_text(encoding="utf-8")

        self.assertIn("DisableDelayedExpansion", windows_launcher)
        self.assertIn('"%SCGS_ANIME_APP%" --windowed', windows_launcher)
        self.assertEqual(1, windows_launcher.count("--anime-style-slice-exit"))
        self.assertIn("SCGS_ANIME_LAUNCHER_OUTPUT", windows_launcher)
        self.assertNotIn("%*", windows_launcher)

        self.assertTrue(macos_launcher.startswith("#!/bin/sh\nset -eu\n"))
        self.assertIn('application="$launcher_directory/SomeCardGameShit.app"', macos_launcher)
        self.assertEqual(1, macos_launcher.count("--anime-style-slice-exit"))
        self.assertIn("SCGS_ANIME_LAUNCHER_OUTPUT", macos_launcher)
        self.assertNotIn('"$@"', macos_launcher)
        self.assertIn(
            "scripts/ci/PLAY_ANIME_STYLE_SLICE.command text eol=lf",
            attributes,
        )

        self.assertIn("WINDOWS_ANIME_LAUNCHER", finalize)
        self.assertIn("MACOS_ANIME_LAUNCHER", finalize)
        self.assertIn("macos_launcher.chmod(0o755)", finalize)
        self.assertIn("ANIME_V1_ASSET_MANIFEST.json", finalize)
        self.assertIn("ANIME_V1_PROVENANCE.md", finalize)
        self.assertIn("ANIME_V1_SLICE_README.md", finalize)

        artifact_stem = "SomeCardGameShit-product-runtime-foundation-anime-slice"
        self.assertIn(f"{artifact_stem}-windows-x86_64.zip", workflow)
        self.assertIn(f"{artifact_stem}-macos-arm64.zip", workflow)
        self.assertEqual(2, workflow.count("--require-anime-slice-launcher"))
        self.assertEqual(2, workflow.count("Round-trip launch packaged AnimeV1 visual slice"))

    def test_anime_launcher_audit_requires_exact_bytes_and_macos_execute_bit(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            windows_export = directory / "SomeCardGameShit.exe"
            windows_export.write_bytes(b"placeholder")
            shutil.copy2(WINDOWS_ANIME_LAUNCHER, directory / WINDOWS_ANIME_LAUNCHER.name)
            _audit_anime_slice_launcher(windows_export, "windows-x86_64")
            (directory / WINDOWS_ANIME_LAUNCHER.name).write_bytes(b"tampered")
            with self.assertRaisesRegex(ExportAuditError, "differs"):
                _audit_anime_slice_launcher(windows_export, "windows-x86_64")

        if os.name != "nt":
            with tempfile.TemporaryDirectory() as temporary:
                directory = Path(temporary)
                app = directory / "SomeCardGameShit.app"
                app.mkdir()
                packaged = directory / MACOS_ANIME_LAUNCHER.name
                shutil.copy2(MACOS_ANIME_LAUNCHER, packaged)
                packaged.chmod(0o755)
                _audit_anime_slice_launcher(app, "macos-arm64")
                packaged.chmod(0o644)
                with self.assertRaisesRegex(ExportAuditError, "not executable"):
                    _audit_anime_slice_launcher(app, "macos-arm64")

    def test_ci_waits_for_cold_import_and_uses_official_macos_template_shape(
        self,
    ) -> None:
        root = SCRIPTS.parent
        workflow = (root / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        preset = (root / "client/godot/export_presets.cfg").read_text(
            encoding="utf-8"
        )
        project = (root / "client/godot/project.godot").read_text(
            encoding="utf-8"
        )
        bootstrap = (
            root / "client/godot/scenes/bootstrap/Bootstrap.tscn"
        ).read_text(encoding="utf-8")

        self.assertEqual(2, workflow.count("--path client/godot --import"))
        self.assertNotIn("--quit-after 2", workflow)
        self.assertIn('binary_format/architecture="arm64"', preset)
        self.assertNotIn('binary_format/architecture="universal"', preset)
        self.assertIn(
            'custom_template/release="res://native/macos-arm64/'
            'godot_macos_release.arm64.zip"',
            preset,
        )
        self.assertIn("texture_format/etc2_astc=true", preset)
        self.assertEqual(
            2,
            preset.count("tests/visual_goldens/gate4b/windows-1600x900/*"),
        )
        self.assertIn(
            "textures/vram_compression/import_etc2_astc=true", project
        )
        self.assertNotIn('theme/custom="', project)
        self.assertIn(
            'path="res://assets/themes/default_theme.tres"', bootstrap
        )
        self.assertIn('theme = ExtResource("2_theme")', bootstrap)
        self.assertIn("prepare_godot_macos_template.py", workflow)
        self.assertEqual(("Debug", "Release"), GODOT_BUILD_CONFIGURATIONS)
        self.assertEqual(8, workflow.count("validate_gate4a_report.py"))
        self.assertEqual(8, workflow.count("--scenario full-match"))
        self.assertEqual(9, workflow.count("--expect-output SCGS_GODOT_CI_SMOKE_OK"))
        self.assertEqual(16, workflow.count("--expect-output-count 1"))
        self.assertEqual(16, workflow.count("Unhandled exception"))
        self.assertEqual(4, workflow.count("gate4a-current-project-"))
        self.assertEqual(2, workflow.count("gate4a-export-"))
        self.assertEqual(2, workflow.count("gate4a-roundtrip-"))
        self.assertEqual(6, workflow.count("--presentation 3d"))
        self.assertEqual(2, workflow.count("--presentation legacy-2d"))
        self.assertEqual(2, workflow.count("--legacy-2d-board"))
        self.assertEqual(2, workflow.count("Godot default 3D current-project native smoke"))
        self.assertEqual(2, workflow.count("Godot legacy 2D source regression smoke"))
        self.assertEqual(
            4,
            workflow.count("-DSCGS_ENABLE_LEGACY_YGO2_TESTS=ON"),
        )
        self.assertNotIn("validate_gate3b_report.py", workflow)
        self.assertNotIn("validate_gate3c_report.py", workflow)
        self.assertNotIn("SomeCardGameShit-gate3b-", workflow)
        self.assertNotIn("SomeCardGameShit-gate3c-", workflow)
        self.assertIn("SomeCardGameShit-gate4b-r2-windows-x86_64", workflow)
        self.assertIn("SomeCardGameShit-gate4b-r2-macos-arm64", workflow)

    def test_prepare_template_adds_executable_arm64_release(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            source = directory / "macos.zip"
            output = directory / "derived.zip"
            expected_hash = _write_template_archive(
                source, _fat_mach_o(0x01000007, 0x0100000C)
            )
            output.write_bytes(b"old output")

            def fake_lipo(_: Path, destination: Path) -> None:
                destination.write_bytes(_thin_mach_o(0x0100000C))

            digest = prepare_template(
                source,
                output,
                expected_source_sha512=expected_hash,
                thin_runner=fake_lipo,
            )

            self.assertEqual(hashlib.sha512(output.read_bytes()).hexdigest(), digest)
            self.assertNotEqual(b"old output", output.read_bytes())
            with zipfile.ZipFile(output, "r") as archive:
                self.assertIn(UNIVERSAL_ENTRY, archive.namelist())
                matches = [
                    item
                    for item in archive.infolist()
                    if item.filename == ARM64_ENTRY
                ]
                self.assertEqual(1, len(matches))
                self.assertEqual(3, matches[0].create_system)
                self.assertEqual(0o100755, matches[0].external_attr >> 16)
                self.assertEqual(
                    _thin_mach_o(0x0100000C), archive.read(matches[0])
                )

    def test_prepare_template_rejects_wrong_hash_and_missing_entry(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            source = directory / "macos.zip"
            output = directory / "derived.zip"
            expected_hash = _write_template_archive(source, None)

            with self.assertRaisesRegex(TemplatePreparationError, "SHA-512"):
                prepare_template(
                    source,
                    output,
                    expected_source_sha512="0" * 128,
                    thin_runner=lambda _source, _destination: None,
                )
            with self.assertRaisesRegex(TemplatePreparationError, "exactly one"):
                prepare_template(
                    source,
                    output,
                    expected_source_sha512=expected_hash,
                    thin_runner=lambda _source, _destination: None,
                )

    def test_prepare_template_rejects_non_universal_input(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            source = directory / "macos.zip"
            output = directory / "derived.zip"
            expected_hash = _write_template_archive(
                source, _thin_mach_o(0x0100000C)
            )

            with self.assertRaisesRegex(TemplatePreparationError, "not an x86_64"):
                prepare_template(
                    source,
                    output,
                    expected_source_sha512=expected_hash,
                    thin_runner=lambda _source, _destination: None,
                )

    def test_prepare_template_rejects_lipo_failure_and_wrong_output(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            source = directory / "macos.zip"
            output = directory / "derived.zip"
            expected_hash = _write_template_archive(
                source, _fat_mach_o(0x01000007, 0x0100000C)
            )
            output.write_bytes(b"sentinel")

            def fail_lipo(_: Path, __: Path) -> None:
                raise subprocess.CalledProcessError(1, ["lipo"])

            with self.assertRaises(subprocess.CalledProcessError):
                prepare_template(
                    source,
                    output,
                    expected_source_sha512=expected_hash,
                    thin_runner=fail_lipo,
                )
            self.assertEqual(b"sentinel", output.read_bytes())

            def wrong_lipo(_: Path, destination: Path) -> None:
                destination.write_bytes(_thin_mach_o(0x01000007))

            with self.assertRaisesRegex(TemplatePreparationError, "arm64-only"):
                prepare_template(
                    source,
                    output,
                    expected_source_sha512=expected_hash,
                    thin_runner=wrong_lipo,
                )
            self.assertEqual(b"sentinel", output.read_bytes())

            def malformed_lipo(_: Path, destination: Path) -> None:
                destination.write_bytes(b"\xca")

            with self.assertRaisesRegex(TemplatePreparationError, "invalid Mach-O"):
                prepare_template(
                    source,
                    output,
                    expected_source_sha512=expected_hash,
                    thin_runner=malformed_lipo,
                )
            self.assertEqual(b"sentinel", output.read_bytes())

    def test_all_mach_o_files_must_be_arm64_only(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            bundle = Path(temporary) / "Sample.app"
            (bundle / "Contents/MacOS").mkdir(parents=True)
            (bundle / "Contents/Frameworks").mkdir(parents=True)
            (bundle / "Contents/MacOS/Sample").write_bytes(
                _thin_mach_o(0x0100000C)
            )
            (bundle / "Contents/Frameworks/native.dylib").write_bytes(
                _thin_mach_o(0x0100000C)
            )
            (bundle / "Contents/Resources.txt").write_text(
                "not a Mach-O", encoding="utf-8"
            )

            self.assertEqual(2, _audit_macos_bundle_architectures(bundle))

            (bundle / "Contents/Frameworks/foreign.dylib").write_bytes(
                _thin_mach_o(0x01000007)
            )
            with self.assertRaisesRegex(ExportAuditError, "not arm64-only"):
                _audit_macos_bundle_architectures(bundle)

    def test_packaged_license_contract_includes_engine_and_runtime_notices(self) -> None:
        self.assertIn("Godot-COPYRIGHT.txt", LICENSE_MARKERS)
        self.assertIn("Dotnet-Runtime-LICENSE.txt", LICENSE_MARKERS)
        self.assertIn("Dotnet-Runtime-THIRD-PARTY-NOTICES.txt", LICENSE_MARKERS)
        self.assertIn("ASSET_NOTICES.md", LICENSE_MARKERS)
        self.assertIn("ASSET_MANIFEST.json", LICENSE_MARKERS)
        self.assertIn("R3_ASSET_MANIFEST.json", LICENSE_MARKERS)
        self.assertIn("ANIME_V1_ASSET_MANIFEST.json", LICENSE_MARKERS)
        self.assertIn("ANIME_V1_PROVENANCE.md", LICENSE_MARKERS)
        self.assertIn("ANIME_V1_SLICE_README.md", LICENSE_MARKERS)
        self.assertEqual(
            {
                "ANIME_V1_ASSET_MANIFEST.json",
                "ANIME_V1_PROVENANCE.md",
                "ANIME_V1_SLICE_README.md",
            },
            set(EXACT_PACKAGED_SOURCE_FILES),
        )
        for filename, source in EXACT_PACKAGED_SOURCE_FILES.items():
            self.assertEqual(filename, FINALIZED_LICENSES[source])

        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            _write_packaged_notices(
                directory,
                commit="0123456789abcdef0123456789abcdef01234567",
            )
            _audit_licenses(directory)

            (directory / "Godot-COPYRIGHT.txt").unlink()
            with self.assertRaisesRegex(ExportAuditError, "missing packaged"):
                _audit_licenses(directory)

    def test_packaged_anime_documents_must_match_reviewed_sources_exactly(self) -> None:
        for filename in EXACT_PACKAGED_SOURCE_FILES:
            with self.subTest(filename=filename), tempfile.TemporaryDirectory() as temporary:
                directory = Path(temporary)
                _write_packaged_notices(directory)
                packaged = directory / filename
                packaged.write_bytes(packaged.read_bytes() + b"\n<!-- tampered -->\n")
                with self.assertRaisesRegex(
                    ExportAuditError,
                    rf"packaged {filename} differs from the reviewed source",
                ):
                    _audit_licenses(directory)

    def test_packaged_build_info_is_a_strict_versioned_contract(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            _write_packaged_notices(directory)

            _audit_licenses(directory)
            build_info = directory / "BUILD_INFO.txt"
            for old, new, message in (
                ("dotnet_sdk=10.0.400", "dotnet_sdk=10.0.401", "SDK"),
                ("commit=local", "commit=", "empty"),
                ("godot=4.7.2.stable.mono", "godot=4.7.1.stable.mono", "unexpected"),
            ):
                with self.subTest(field=old):
                    valid = (
                        "SomeCardGameShit Gate 4B-R2\n"
                        "commit=local\n"
                        "godot=4.7.2.stable.mono\n"
                        "dotnet_sdk=10.0.400\n"
                        "dotnet_runtime=8.0.30\n"
                    )
                    build_info.write_text(valid.replace(old, new), encoding="utf-8")
                    with self.assertRaisesRegex(ExportAuditError, message):
                        _audit_licenses(directory)

            valid = (
                "SomeCardGameShit Gate 4B-R2\n"
                "commit=0123456789abcdef0123456789abcdef01234567\n"
                "godot=4.7.2.stable.mono\n"
                "dotnet_sdk=10.0.400\n"
                "dotnet_runtime=8.0.30\n"
            )
            build_info.write_text(valid, encoding="utf-8")
            with self.assertRaisesRegex(ExportAuditError, "GitHub checkout"):
                _audit_licenses(directory, "f" * 40)
            _audit_licenses(
                directory,
                "0123456789abcdef0123456789abcdef01234567",
            )


if __name__ == "__main__":
    unittest.main()
