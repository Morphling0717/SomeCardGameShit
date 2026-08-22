# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import struct
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPTS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS))

from audit_godot_export import (  # noqa: E402
    ExportAuditError,
    LICENSE_MARKERS,
    _audit_licenses,
    _audit_macos_bundle_architectures,
)


def _thin_mach_o(cpu: int) -> bytes:
    return b"\xcf\xfa\xed\xfe" + struct.pack("<I", cpu) + bytes(64)


class GodotExportAuditTests(unittest.TestCase):
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

        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            for filename, marker in LICENSE_MARKERS.items():
                content = marker
                if filename == "BUILD_INFO.txt":
                    content += "\ndotnet_runtime=8.0.30"
                (directory / filename).write_text(content, encoding="utf-8")
            _audit_licenses(directory)

            (directory / "Godot-COPYRIGHT.txt").unlink()
            with self.assertRaisesRegex(ExportAuditError, "missing packaged"):
                _audit_licenses(directory)


if __name__ == "__main__":
    unittest.main()
