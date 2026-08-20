# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CS_PROTOCOL = ROOT / "client/YGOPro2Overlay/Assets/SomeCardGame/Protocol"
CPP_HEADER = ROOT / "engine/include/scgs/protocol.hpp"
PROTOCOL_DOC = ROOT / "docs/protocol.md"

EXPECTED_MESSAGES = {
    "GameMode": 210,
    "PlayerState": 211,
    "UnitState": 212,
    "EvolutionState": 213,
    "AdvancedSummonState": 214,
    "RequestEvolutionMode": 215,
    "RequestMaterials": 216,
    "RequestImprint": 217,
    "TacticWindow": 218,
    "MatchStatistics": 219,
}

EXPECTED_PLAYER = bytes(
    [
        0xD3,
        0x01,
        0x01,
        0x11,
        0x00,
        0x19,
        0x00,
        0x03,
        0x07,
        0x02,
        0x06,
        0x03,
    ]
)
EXPECTED_UNIT = bytes(
    [
        0xD4,
        0x01,
        0x00,
        0x03,
        0x08,
        0x07,
        0x06,
        0x05,
        0x04,
        0x03,
        0x02,
        0x01,
        0x07,
        0x00,
        0x05,
        0x00,
        0x08,
        0x00,
        0x03,
        0x00,
        0x00,
        0x00,
        0x01,
        0x09,
    ]
)


def parse_enum(source: str) -> dict[str, int]:
    return {
        name: int(value)
        for name, value in re.findall(r"^\s*([A-Za-z][A-Za-z0-9_]*)\s*=\s*(\d+)\s*,", source, re.MULTILINE)
        if name in EXPECTED_MESSAGES
    }


def parse_csharp_byte_array(source: str, name: str) -> bytes:
    match = re.search(
        rf"\b{name}\s*=\s*new byte\[\]\s*\{{(?P<body>.*?)\}};",
        source,
        re.DOTALL,
    )
    if match is None:
        raise AssertionError(f"could not locate C# byte array {name}")
    values = [int(token, 16) for token in re.findall(r"0x([0-9A-Fa-f]{2})", match.group("body"))]
    return bytes(values)


class ProtocolContractTests(unittest.TestCase):
    def test_cpp_and_csharp_reserve_the_same_message_ids(self) -> None:
        cpp = parse_enum(CPP_HEADER.read_text(encoding="utf-8"))
        csharp = parse_enum((CS_PROTOCOL / "ScgsGameMessage.cs").read_text(encoding="utf-8"))
        self.assertEqual(cpp, EXPECTED_MESSAGES)
        self.assertEqual(csharp, EXPECTED_MESSAGES)

    def test_csharp_golden_vectors_match_the_cpp_contract(self) -> None:
        source = (CS_PROTOCOL / "ScgsProtocolGoldenVectors.cs").read_text(encoding="utf-8")
        self.assertEqual(parse_csharp_byte_array(source, "PlayerState"), EXPECTED_PLAYER)
        self.assertEqual(parse_csharp_byte_array(source, "UnitState"), EXPECTED_UNIT)

    def test_message_and_ygo2_payload_lengths_are_explicit(self) -> None:
        reader = (CS_PROTOCOL / "ScgsProtocolReader.cs").read_text(encoding="utf-8")
        expected_constants = {
            "PlayerStateMessageLength": len(EXPECTED_PLAYER),
            "PlayerStatePayloadLength": len(EXPECTED_PLAYER) - 1,
            "UnitStateMessageLength": len(EXPECTED_UNIT),
            "UnitStatePayloadLength": len(EXPECTED_UNIT) - 1,
        }
        for name, value in expected_constants.items():
            self.assertRegex(reader, rf"public const int {name}\s*=\s*{value}\s*;")
        self.assertIn("DecodePlayerStatePayload", reader)
        self.assertIn("DecodeUnitStatePayload", reader)

    def test_ygo2_adapter_does_not_expect_the_function_byte_in_data_reader(self) -> None:
        adapter = (CS_PROTOCOL / "ScgsYgoProPackageAdapter.cs").read_text(encoding="utf-8")
        store = (CS_PROTOCOL / "ScgsStateStore.cs").read_text(encoding="utf-8")
        self.assertIn("payloadReader.BaseStream", adapter)
        self.assertIn("stateStore.TryApply((ScgsGameMessage)(byte)function, payload", adapter)
        self.assertIn("DecodePlayerStatePayload(payload)", store)
        self.assertIn("DecodeUnitStatePayload(payload)", store)

    def test_documented_vectors_match_the_contract(self) -> None:
        documentation = PROTOCOL_DOC.read_text(encoding="utf-8")
        normalized = " ".join(documentation.upper().split())
        player = " ".join(f"{value:02X}" for value in EXPECTED_PLAYER)
        unit_line_1 = " ".join(f"{value:02X}" for value in EXPECTED_UNIT[:12])
        unit_line_2 = " ".join(f"{value:02X}" for value in EXPECTED_UNIT[12:])
        self.assertIn(player, normalized)
        self.assertIn(unit_line_1, normalized)
        self.assertIn(unit_line_2, normalized)


if __name__ == "__main__":
    unittest.main()
