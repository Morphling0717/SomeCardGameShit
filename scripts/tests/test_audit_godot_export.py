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
from unittest import mock


SCRIPTS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS))
sys.path.insert(0, str(SCRIPTS / "ci"))

from audit_godot_export import (  # noqa: E402
    EXACT_PACKAGED_SOURCE_FILES,
    ExportAuditError,
    LICENSE_MARKERS,
    MACOS_ANIME_LAUNCHER,
    MACOS_CARD_BODY_LAUNCHER,
    NATIVE_LAYOUTS,
    WINDOWS_ANIME_LAUNCHER,
    WINDOWS_CARD_BODY_LAUNCHER,
    _audit_anime_card_body_launcher,
    _audit_anime_slice_launcher,
    _audit_licenses,
    _audit_macos_bundle_architectures,
    _audit_native_layout,
    _audit_product_card_export_policy,
    _audit_product_pck,
)
from finalize_godot_export import (  # noqa: E402
    LICENSES as FINALIZED_LICENSES,
    _native_destinations,
)
from prepare_godot_macos_template import (  # noqa: E402
    ARM64_ENTRY,
    UNIVERSAL_ENTRY,
    TemplatePreparationError,
    prepare_template,
)
from run_managed_gate3 import GODOT_BUILD_CONFIGURATIONS  # noqa: E402
from stage_godot_native import TARGETS as STAGING_TARGETS, stage_pair, main as stage_main  # noqa: E402


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
                "SomeCardGameShit Product Playable v1\n"
                f"commit={commit}\n"
                "godot=4.7.2.stable.mono\n"
                "dotnet_sdk=10.0.400\n"
                "dotnet_runtime=8.0.30\n"
                "api=scgs_v05\n"
                "schema=2\n"
                "visual=AnimeV1\n",
                encoding="utf-8",
            )
        else:
            destination.write_text(marker, encoding="utf-8")


class GodotExportAuditTests(unittest.TestCase):
    def test_native_editor_and_fixture_payloads_cannot_hide_inside_pck(self) -> None:
        from scripts.tests.test_check_godot_mcp_export import pack, settings
        with tempfile.TemporaryDirectory() as temporary:
            pck = Path(temporary) / "product.pck"
            for name in ("native/windows-x86_64/scgs_v04.dll", "other/scgs_v04_fixture.dll",
                         "other/libscgs_v04.1.dylib", "other/scgs_v05.dll",
                         "native/macos-arm64/godot_template.zip"):
                with self.subTest(name=name):
                    pck.write_bytes(pack({"project.binary": settings(), name: b"not a player resource"}))
                    with self.assertRaisesRegex(ExportAuditError, "payload in product PCK"):
                        _audit_product_pck(pck)
            pck.write_bytes(pack({"project.binary": settings(), "assets/visual/anime_v1/card.ctex": b"art"}))
            _audit_product_pck(pck)

    def test_product_editor_stages_only_explicit_v05_library(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "source-product.dll"
            source.write_bytes(b"v05 payload")
            destination = root / "native"
            arguments = ["stage_godot_native.py", "--v05-library", str(source),
                         "--destination-root", str(destination), "--target", "windows-x86_64"]
            with mock.patch("stage_godot_native.audit") as audit_mock, mock.patch("sys.argv", arguments):
                self.assertEqual(0, stage_main())
            audit_mock.assert_called_once_with(source.resolve(), "x86_64", "v05")
            self.assertEqual(["scgs_v05.dll"], [path.name for path in destination.rglob("*.dll")])
            self.assertEqual(b"v05 payload", (destination / "windows-x86_64/scgs_v05.dll").read_bytes())

    def test_editor_staging_audits_and_copies_both_native_api_versions(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            source = directory / "source"
            destination = directory / "editor"
            source.mkdir()
            v04 = source / "frozen.dll"
            v05 = source / "product.dll"
            v04.write_bytes(b"v04 payload")
            v05.write_bytes(b"v05 payload")

            with mock.patch("stage_godot_native.audit") as native_audit:
                staged_v04, staged_v05 = stage_pair(
                    v04,
                    v05,
                    destination,
                    "windows-x86_64",
                )

            self.assertEqual(
                destination.resolve() / "windows-x86_64/scgs_v04.dll",
                staged_v04,
            )
            self.assertEqual(
                destination.resolve() / "windows-x86_64/scgs_v05.dll",
                staged_v05,
            )
            self.assertEqual(b"v04 payload", staged_v04.read_bytes())
            self.assertEqual(b"v05 payload", staged_v05.read_bytes())
            self.assertEqual(
                [
                    mock.call(v04.resolve(), "x86_64", "v04"),
                    mock.call(v05.resolve(), "x86_64", "v05"),
                ],
                native_audit.call_args_list,
            )
            self.assertEqual(
                "libscgs_v05.dylib",
                STAGING_TARGETS["macos-arm64"][1]["v05"],
            )

    def test_export_native_layout_requires_only_v05_and_rejects_duplicates(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            native = root / "scgs_v05.dll"
            native.write_bytes(b"product")
            with mock.patch("audit_godot_export.audit") as native_audit:
                _audit_native_layout(root, "windows-x86_64")
            native_audit.assert_called_once_with(native, "x86_64", "v05")
            duplicate = root / "nested/scgs_v05.dll"
            duplicate.parent.mkdir()
            duplicate.write_bytes(b"duplicate")
            with mock.patch("audit_godot_export.audit"), self.assertRaisesRegex(ExportAuditError, "v05 native layout"):
                _audit_native_layout(root, "windows-x86_64")

    def test_player_export_rejects_retired_fixture_and_preview_launchers(self) -> None:
        for filename in ("scgs_v04.dll", "scgs_v04_fixture.dll", "libscgs_v04.1.dylib",
                         "libscgs_v04_fixture.so", "PLAY_R3_VISUAL_SLICE.cmd",
                         "PLAY_ANIME_CARD_BODY_SLICE.command"):
            with self.subTest(filename=filename), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                (root / "scgs_v05.dll").write_bytes(b"product")
                (root / filename).write_bytes(b"retired")
                with mock.patch("audit_godot_export.audit"), self.assertRaisesRegex(ExportAuditError, "retired"):
                    _audit_native_layout(root, "windows-x86_64")

    def test_finalizer_places_only_v05_in_fixed_platform_locations(self) -> None:
        self.assertEqual({"v05": Path("C:/package/scgs_v05.dll")},
                         _native_destinations(Path("C:/package/SomeCardGameShit.exe"), "windows-x86_64"))
        self.assertEqual({"v05": Path("/package/SomeCardGameShit.app/Contents/Frameworks/libscgs_v05.dylib")},
                         _native_destinations(Path("/package/SomeCardGameShit.app"), "macos-arm64"))
        self.assertEqual({"v05"}, set(NATIVE_LAYOUTS["windows-x86_64"]["libraries"]))

    def test_finalize_and_audit_lock_product_build_info(self) -> None:
        root = SCRIPTS.parent
        finalize = (root / "scripts/ci/finalize_godot_export.py").read_text(
            encoding="utf-8"
        )
        audit_source = (root / "scripts/audit_godot_export.py").read_text(
            encoding="utf-8"
        )
        for source in (finalize, audit_source):
            with self.subTest(source=source.splitlines()[1]):
                self.assertIn("SomeCardGameShit Product Playable v1", source)
                self.assertNotIn("SomeCardGameShit Gate 3C", source)

    def test_product_pipeline_preserves_build_once_and_package_roundtrip(self) -> None:
        workflow = (SCRIPTS.parent / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        self.assertEqual(2, workflow.count("--path client/godot --import"))
        self.assertEqual(2, workflow.count("--export-release"))
        self.assertEqual(4, workflow.count("scripts/dev/check_godot_mcp_export.py"))
        self.assertNotIn("--ci-smoke", workflow)
        self.assertNotIn("validate_gate4a_report.py", workflow)
        self.assertNotIn("PLAY_R3", workflow)
        self.assertIn("run_product_smoke.py", workflow)
        self.assertIn("--coverage full-ui", workflow)
        self.assertIn("--coverage natural-ui", workflow)
        self.assertIn("scgs_v04_fixture", workflow)
        self.assertIn("--sequesterRsrc --keepParent", workflow)
        self.assertEqual(("Debug", "Release"), GODOT_BUILD_CONFIGURATIONS)
        self.assertIn("SomeCardGameShit-product-playable-v1", workflow)

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
        self.assertNotIn("ASSET_MANIFEST.json", LICENSE_MARKERS)
        self.assertNotIn("R3_ASSET_MANIFEST.json", LICENSE_MARKERS)
        self.assertIn("ANIME_V1_ASSET_MANIFEST.json", LICENSE_MARKERS)
        self.assertIn("ANIME_V1_PROVENANCE.md", LICENSE_MARKERS)
        self.assertIn("ANIME_V1_SLICE_README.md", LICENSE_MARKERS)
        self.assertIn("ANIME_V1_CARD_BODY_ASSET_MANIFEST.json", LICENSE_MARKERS)
        self.assertIn("ANIME_V1_CARD_BODY_PROVENANCE.md", LICENSE_MARKERS)
        self.assertIn("ANIME_V1_CARD_BODY_README.md", LICENSE_MARKERS)
        self.assertIn("ANIME_V1_PRODUCT_CARD_ART_ASSET_MANIFEST.json", LICENSE_MARKERS)
        self.assertIn("ANIME_V1_PRODUCT_CARD_ART_PROVENANCE.md", LICENSE_MARKERS)
        self.assertEqual(
            {
                "ANIME_V1_SHARED_ASSET_MANIFEST.json",
                "ANIME_V1_ASSET_MANIFEST.json",
                "ANIME_V1_PROVENANCE.md",
                "ANIME_V1_SLICE_README.md",
                "ANIME_V1_CARD_BODY_ASSET_MANIFEST.json",
                "ANIME_V1_CARD_BODY_PROVENANCE.md",
                "ANIME_V1_CARD_BODY_README.md",
                "ANIME_V1_PRODUCT_CARD_ART_ASSET_MANIFEST.json",
                "ANIME_V1_PRODUCT_CARD_ART_PROVENANCE.md",
            },
            set(EXACT_PACKAGED_SOURCE_FILES),
        )
        for filename, source in EXACT_PACKAGED_SOURCE_FILES.items():
            self.assertEqual(filename, FINALIZED_LICENSES[source])

        _audit_product_card_export_policy()

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
                        "SomeCardGameShit Product Playable v1\n"
                        "commit=local\n"
                        "godot=4.7.2.stable.mono\n"
                        "dotnet_sdk=10.0.400\n"
                        "dotnet_runtime=8.0.30\n"
                "api=scgs_v05\n"
                "schema=2\n"
                "visual=AnimeV1\n"
                    )
                    build_info.write_text(valid.replace(old, new), encoding="utf-8")
                    with self.assertRaisesRegex(ExportAuditError, message):
                        _audit_licenses(directory)

            valid = (
                "SomeCardGameShit Product Playable v1\n"
                "commit=0123456789abcdef0123456789abcdef01234567\n"
                "godot=4.7.2.stable.mono\n"
                "dotnet_sdk=10.0.400\n"
                "dotnet_runtime=8.0.30\n"
                "api=scgs_v05\n"
                "schema=2\n"
                "visual=AnimeV1\n"
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
