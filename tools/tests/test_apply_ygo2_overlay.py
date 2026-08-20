# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import importlib.util
import tempfile
import unittest
from pathlib import Path

MODULE_PATH = Path(__file__).resolve().parents[1] / "apply_ygo2_overlay.py"
SPEC = importlib.util.spec_from_file_location("apply_ygo2_overlay", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)

BASE_ENUM = """namespace YGOSharp.OCGWrapper.Enums
{
    public enum GameMessage
    {
        DuelWinner = 200,
        sibyl_chat = 230,
        sibyl_replay = 231,
    }
}
"""


class OverlayPatcherTests(unittest.TestCase):
    def test_injects_the_entire_reserved_range(self) -> None:
        patched = MODULE.inject_message_ids(BASE_ENUM)
        for value in range(210, 220):
            self.assertIn(f"= {value},", patched)
        self.assertLess(patched.index("ScgsGameMode"), patched.index("sibyl_chat"))

    def test_is_idempotent(self) -> None:
        patched = MODULE.inject_message_ids(BASE_ENUM)
        self.assertEqual(MODULE.inject_message_ids(patched), patched)
        self.assertEqual(patched.count("ScgsGameMode"), 1)

    def test_rejects_an_upstream_collision(self) -> None:
        colliding = BASE_ENUM.replace("DuelWinner = 200,", "DuelWinner = 200,\n        OtherFeature = 214,")
        with self.assertRaisesRegex(ValueError, "already uses"):
            MODULE.inject_message_ids(colliding)

    def test_rejects_an_unexpected_enum_shape(self) -> None:
        with self.assertRaisesRegex(ValueError, "sibyl_chat"):
            MODULE.inject_message_ids(BASE_ENUM.replace("sibyl_chat = 230,", "SibylChat = 230,"))

    def test_copies_overlay_and_modifies_a_checkout_fixture(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            project = root / "project"
            checkout = root / "upstream"
            overlay = project / MODULE.OVERLAY_RELATIVE_PATH
            enum_path = checkout / MODULE.ENUM_RELATIVE_PATH
            overlay.mkdir(parents=True)
            enum_path.parent.mkdir(parents=True)
            (overlay / "Marker.cs").write_text("class Marker {}\n", encoding="utf-8")
            enum_path.write_text(BASE_ENUM, encoding="utf-8-sig")

            MODULE.apply_overlay(
                project,
                checkout,
                check_only=False,
                allow_revision_mismatch=True,
            )
            result = enum_path.read_text(encoding="utf-8-sig")
            self.assertIn("ScgsPlayerState = 211", result)
            self.assertTrue((checkout / MODULE.DESTINATION_RELATIVE_PATH / "Marker.cs").is_file())

            # Applying twice must neither duplicate ids nor fail.
            MODULE.apply_overlay(
                project,
                checkout,
                check_only=False,
                allow_revision_mismatch=True,
            )
            result = enum_path.read_text(encoding="utf-8-sig")
            self.assertEqual(result.count("ScgsGameMode"), 1)


if __name__ == "__main__":
    unittest.main()
