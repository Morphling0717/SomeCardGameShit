# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import binascii
import hashlib
import json
import struct
import tempfile
import unittest
import zlib
from copy import deepcopy
from pathlib import Path

from scripts.ci.validate_anime_card_body_slice import (
    AnimeCardBodySliceError,
    EXPECTED_ACTORS,
    EXPECTED_STYLES,
    STATES,
    _decode_rgba8_png,
    validate_report,
)


def _paeth_predictor(left: int, up: int, upper_left: int) -> int:
    prediction = left + up - upper_left
    distance_left = abs(prediction - left)
    distance_up = abs(prediction - up)
    distance_upper_left = abs(prediction - upper_left)
    if distance_left <= distance_up and distance_left <= distance_upper_left:
        return left
    if distance_up <= distance_upper_left:
        return up
    return upper_left


def _png_chunk(chunk_type: bytes, data: bytes) -> bytes:
    checksum = binascii.crc32(chunk_type)
    checksum = binascii.crc32(data, checksum) & 0xFFFFFFFF
    return struct.pack(">I", len(data)) + chunk_type + data + struct.pack(">I", checksum)


def _rgba_png(
    width: int,
    height: int,
    color: tuple[int, int, int, int],
    *,
    filter_type: int = 0,
) -> tuple[bytes, str]:
    if filter_type not in range(5):
        raise ValueError("filter_type must be in the range 0..4")
    source_row = bytes(color) * width
    previous_row = bytes(len(source_row))
    filtered = bytearray()
    pixel_hasher = hashlib.sha256()
    for _ in range(height):
        pixel_hasher.update(source_row)
        filtered.append(filter_type)
        if filter_type == 0:
            filtered.extend(source_row)
            previous_row = source_row
            continue
        encoded_row = bytearray(len(source_row))
        for column, value in enumerate(source_row):
            left = source_row[column - 4] if column >= 4 else 0
            up = previous_row[column]
            upper_left = previous_row[column - 4] if column >= 4 else 0
            if filter_type == 0:
                predictor = 0
            elif filter_type == 1:
                predictor = left
            elif filter_type == 2:
                predictor = up
            elif filter_type == 3:
                predictor = (left + up) // 2
            else:
                predictor = _paeth_predictor(left, up, upper_left)
            encoded_row[column] = (value - predictor) & 0xFF
        filtered.extend(encoded_row)
        previous_row = source_row
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    payload = (
        b"\x89PNG\r\n\x1a\n"
        + _png_chunk(b"IHDR", ihdr)
        + _png_chunk(b"IDAT", zlib.compress(bytes(filtered), level=9))
        + _png_chunk(b"IEND", b"")
    )
    return payload, pixel_hasher.hexdigest()


class AnimeCardBodySliceValidatorTests(unittest.TestCase):
    def _write_report(self, directory: Path) -> tuple[Path, dict]:
        captures = []
        for index, state in enumerate(STATES):
            filename = f"{index:02}-{state}.png"
            payload, raw_pixel_hash = _rgba_png(
                1600,
                900,
                (24 + index * 17, 31 + index * 13, 47 + index * 11, 255),
            )
            (directory / filename).write_bytes(payload)
            contexts = ["Field"]
            if state.startswith("hand-") or state == "values":
                contexts = ["Hand"]
            elif state == "contexts":
                contexts = ["Detail", "Field", "Hand"]
            gpu_required = state in {
                "hand-one",
                "hand-five",
                "hand-ten",
                "hand-hover",
                "values",
            }
            gpu_actors = []
            gpu_badge_count = 0
            if gpu_required:
                if state == "values":
                    kinds = ["Follower", "Follower", "Follower", "Amulet", "Trap", "Spell"]
                    roles = [
                        ["cost", "attack", "health"],
                        ["cost", "attack", "health"],
                        ["cost", "attack", "health"],
                        ["cost", "countdown"],
                        ["cost", "countdown"],
                        ["cost"],
                    ]
                else:
                    kinds = ["Spell"] * EXPECTED_ACTORS[state]
                    roles = [["cost"] for _ in kinds]
                for actor_index, (kind, actor_roles) in enumerate(zip(kinds, roles, strict=True)):
                    actor_name = f"actor-{actor_index}"
                    badges = []
                    for role_index, role in enumerate(actor_roles):
                        screen_x = 100 + actor_index * 10
                        screen_y = 200 + role_index * 24
                        glyph_x = screen_x + 4
                        glyph_y = screen_y + 4
                        socket_x = screen_x + 2
                        socket_y = screen_y + 2
                        badges.append(
                            {
                                "Role": role,
                                "Text": str(role_index),
                                "Expected": True,
                                "ReferenceActorName": actor_name,
                                "ScreenX": float(screen_x),
                                "ScreenY": float(screen_y),
                                "ScreenWidth": 24.0,
                                "ScreenHeight": 20.0,
                                "PixelHeight": 20,
                                "FullyInsideViewport": True,
                                "RoiX": 100 + actor_index * 10,
                                "RoiY": 200 + role_index * 24,
                                "RoiWidth": 24,
                                "RoiHeight": 20,
                                "BrightPixelCount": 8,
                                "ColorBucketCount": 4,
                                "GlyphDifferencePixelCount": 12,
                                "BrightGlyphDifferencePixelCount": 4,
                                "SocketScreenX": float(socket_x),
                                "SocketScreenY": float(socket_y),
                                "SocketScreenWidth": 14.0,
                                "SocketScreenHeight": 14.0,
                                "SocketFullyInsideViewport": True,
                                "RequiredSocketInsetPixels": 1,
                                "GlyphSocketInsetLeft": 2.0,
                                "GlyphSocketInsetTop": 2.0,
                                "GlyphSocketInsetRight": 2.0,
                                "GlyphSocketInsetBottom": 2.0,
                                "GlyphInsideSocket": True,
                                "GlyphRoiX": glyph_x,
                                "GlyphRoiY": glyph_y,
                                "GlyphPixelWidth": 10,
                                "GlyphPixelHeight": 10,
                                "HighContrastGlyphDifferencePixelCount": 4,
                                "MaximumGlyphContrast": 0.5,
                                "Readable": True,
                            }
                        )
                    gpu_badge_count += len(badges)
                    name_text = (
                        "曜誓大团长·蕾奥妮" if actor_index == 0 else "测试卡牌"
                    )
                    name_screen_x = 300 + actor_index * 90
                    name_glyph_width = 60 if actor_index == 0 else 40
                    name_glyph_x = name_screen_x + ((80 - name_glyph_width) // 2)
                    gpu_actors.append(
                        {
                            "ActorName": actor_name,
                            "DesignId": f"TEST-{actor_index}",
                            "ProductKind": kind,
                            "LocalCompositionReadable": True,
                            "RequiredBadgeCount": len(badges),
                            "AllRequiredBadgesReadable": True,
                            "NameReadable": True,
                            "Badges": badges,
                            "Name": {
                                "Text": name_text,
                                "SourceText": name_text,
                                "FullNameMatchesSource": True,
                                "ReferenceActorName": actor_name,
                                "Expected": True,
                                "FontSize": 22,
                                "ScreenX": float(name_screen_x),
                                "ScreenY": 100.0,
                                "ScreenWidth": 80.0,
                                "ScreenHeight": 20.0,
                                "ScreenFullyInsideViewport": True,
                                "TextSocketScreenX": float(name_screen_x + 5),
                                "TextSocketScreenY": 102.0,
                                "TextSocketScreenWidth": 70.0,
                                "TextSocketScreenHeight": 16.0,
                                "TextSocketFullyInsideViewport": True,
                                "NamePlateScreenX": float(name_screen_x),
                                "NamePlateScreenY": 100.0,
                                "NamePlateScreenWidth": 80.0,
                                "NamePlateScreenHeight": 20.0,
                                "NamePlateFullyInsideViewport": True,
                                "RequiredSocketInsetPixels": 1,
                                "RequiredNamePlateHorizontalInsetPixels": 5.0,
                                "TextSocketNamePlateInsetLeft": 5.0,
                                "TextSocketNamePlateInsetTop": 2.0,
                                "TextSocketNamePlateInsetRight": 5.0,
                                "TextSocketNamePlateInsetBottom": 2.0,
                                "TextSocketInsideNamePlate": True,
                                "RoiX": name_screen_x,
                                "RoiY": 100,
                                "RoiWidth": 80,
                                "RoiHeight": 20,
                                "GlyphDifferencePixelCount": 80,
                                "BrightGlyphDifferencePixelCount": 20,
                                "GlyphRoiX": name_glyph_x,
                                "GlyphRoiY": 105,
                                "GlyphPixelWidth": name_glyph_width,
                                "GlyphPixelHeight": 10,
                                "HighContrastGlyphDifferencePixelCount": 20,
                                "MaximumGlyphContrast": 0.7,
                                "GlyphSocketInsetLeft": float(name_glyph_x - name_screen_x - 5),
                                "GlyphSocketInsetTop": 3.0,
                                "GlyphSocketInsetRight": float(
                                    name_screen_x + 75 - name_glyph_x - name_glyph_width
                                ),
                                "GlyphSocketInsetBottom": 3.0,
                                "GlyphInsideTextSocket": True,
                                "MaximumGlyphSocketCenterDeltaPixels": 2.5,
                                "GlyphSocketCenterDeltaX": 0.0,
                                "GlyphSocketCenterDeltaY": 0.0,
                                "GlyphCenteredInTextSocket": True,
                                "Readable": True,
                            },
                        }
                    )
            captures.append(
                {
                    "State": state,
                    "File": filename,
                    "Sha256": hashlib.sha256(payload).hexdigest(),
                    "Width": 1600,
                    "Height": 900,
                    "FrameStability": {
                        "ConsecutiveFramePostDraws": 2,
                        "AttemptCount": 1,
                        "PixelFormat": "Rgba8",
                        "PixelByteLength": 1600 * 900 * 4,
                        "FirstPixelSha256": raw_pixel_hash,
                        "SecondPixelSha256": raw_pixel_hash,
                    },
                    "Evidence": {
                        "State": state,
                        "ActorCount": EXPECTED_ACTORS[state],
                        "IntegratedActorCount": EXPECTED_ACTORS[state],
                        "DistinctStyleCount": EXPECTED_STYLES[state],
                        "Contexts": contexts,
                        "DesignIds": ["LO-11"],
                        "SubViewportCount": 0,
                        "UsesNativeSession": False,
                    },
                    "GpuReadability": {
                        "State": state,
                        "Required": gpu_required,
                        "MinimumBadgePixelHeight": 17,
                        "ViewportWidth": 1600,
                        "ViewportHeight": 900,
                        "ActorCount": len(gpu_actors),
                        "RequiredBadgeCount": gpu_badge_count,
                        "RequiredNameCount": len(gpu_actors),
                        "CompleteNameCount": len(gpu_actors),
                        "AllRequiredBadgesReadable": True,
                        "AllRequiredNamesReadable": True,
                        "Actors": gpu_actors,
                    },
                    "SilhouetteIsolation": self._silhouette_evidence(
                        state, EXPECTED_ACTORS[state]
                    ),
                }
            )
        report = {
            "Schema": "scgs-anime-card-body-slice",
            "SchemaVersion": 4,
            "ApprovalStatus": "pending_user_approval",
            "UsesRealCardActor3D": True,
            "UsesPerCardSubViewport": False,
            "Captures": captures,
        }
        path = directory / "anime-card-body-slice.json"
        path.write_text(json.dumps(report), encoding="utf-8")
        return path, report

    @staticmethod
    def _silhouette_evidence(state: str, actor_count: int) -> dict:
        required = state in {"representatives", "values"}
        probes = []
        interior_probes = []
        if required:
            corners = (
                "upper-left-edge",
                "upper-right-edge",
                "lower-left-edge",
                "lower-right-edge",
            )
            for actor_index in range(actor_count):
                for corner_index, corner in enumerate(corners):
                    probes.append(
                        {
                            "ActorName": f"actor-{actor_index}",
                            "Corner": corner,
                            "ScreenX": 200.0 + actor_index * 20 + corner_index,
                            "ScreenY": 300.0 + corner_index,
                            "ReferenceX": 190.0 + actor_index * 20 + corner_index,
                            "ReferenceY": 290.0 + corner_index,
                            "FullyInsideViewport": True,
                            "CornerBackgroundColorDelta": 0.005,
                            "Passed": True,
                        }
                    )
            interior_probes = [
                {
                    "ActorName": f"actor-{actor_index}",
                    "ScreenX": 300.0 + actor_index * 20,
                    "ScreenY": 400.0,
                    "FullyInsideViewport": True,
                    "RoiX": 296 + actor_index * 20,
                    "RoiY": 396,
                    "RoiWidth": 9,
                    "RoiHeight": 9,
                    "ProductLayerDifferencePixelCount": 20,
                    "Passed": True,
                }
                for actor_index in range(actor_count)
            ]
        return {
            "State": state,
            "Required": required,
            "ActorCount": actor_count if required else 0,
            "ProbeCount": len(probes),
            "InteriorProbeCount": len(interior_probes),
            "AllRectangularBasesHidden": True,
            "AllCornerProbesMatchBackground": True,
            "AllInteriorProbesShowProductFace": True,
            "Probes": probes,
            "InteriorProbes": interior_probes,
        }

    def test_accepts_exact_real_actor_eight_state_report(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            report, _ = self._write_report(Path(temporary))
            validate_report(report, 1600, 900)

    def test_standard_library_decoder_supports_all_png_filters_and_crc(self) -> None:
        for filter_type in range(5):
            with self.subTest(filter_type=filter_type):
                payload, expected_pixel_hash = _rgba_png(
                    7, 5, (31 + filter_type, 83, 149, 255), filter_type=filter_type
                )
                width, height, pixels = _decode_rgba8_png(
                    payload, f"filter-{filter_type} fixture"
                )
                self.assertEqual((width, height), (7, 5))
                self.assertEqual(hashlib.sha256(pixels).hexdigest(), expected_pixel_hash)

        payload, _ = _rgba_png(7, 5, (31, 83, 149, 255))
        damaged = bytearray(payload)
        damaged[-1] ^= 0x01
        with self.assertRaisesRegex(AnimeCardBodySliceError, "invalid CRC"):
            _decode_rgba8_png(bytes(damaged), "damaged fixture")

    def test_rejects_invalid_png_even_when_file_hash_matches(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path, report = self._write_report(directory)
            capture = report["Captures"][0]
            payload = b"this is not a PNG"
            (directory / capture["File"]).write_bytes(payload)
            capture["Sha256"] = hashlib.sha256(payload).hexdigest()
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(AnimeCardBodySliceError, "is not a PNG"):
                validate_report(report_path, 1600, 900)

    def test_rejects_decoded_png_dimensions_that_differ_from_report(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path, report = self._write_report(directory)
            capture = report["Captures"][0]
            payload, raw_pixel_hash = _rgba_png(1599, 900, (211, 71, 93, 255))
            (directory / capture["File"]).write_bytes(payload)
            capture["Sha256"] = hashlib.sha256(payload).hexdigest()
            capture["FrameStability"]["FirstPixelSha256"] = raw_pixel_hash
            capture["FrameStability"]["SecondPixelSha256"] = raw_pixel_hash
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(AnimeCardBodySliceError, "decoded PNG dimensions"):
                validate_report(report_path, 1600, 900)

    def test_rejects_raw_rgba_hash_that_differs_from_decoded_png(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path, report = self._write_report(directory)
            capture = report["Captures"][0]
            capture["FrameStability"]["FirstPixelSha256"] = "0" * 64
            capture["FrameStability"]["SecondPixelSha256"] = "0" * 64
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(AnimeCardBodySliceError, "decoded RGBA pixel hash"):
                validate_report(report_path, 1600, 900)

    def test_rejects_subviewport_native_or_approval_promotion(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path, report = self._write_report(directory)
            for key, value in (
                ("UsesPerCardSubViewport", True),
                ("ApprovalStatus", "approved"),
            ):
                damaged = deepcopy(report)
                damaged[key] = value
                report_path.write_text(json.dumps(damaged), encoding="utf-8")
                with self.assertRaises(AnimeCardBodySliceError):
                    validate_report(report_path, 1600, 900)

            damaged = deepcopy(report)
            damaged["Captures"][0]["Evidence"]["UsesNativeSession"] = True
            report_path.write_text(json.dumps(damaged), encoding="utf-8")
            with self.assertRaises(AnimeCardBodySliceError):
                validate_report(report_path, 1600, 900)

    def test_rejects_forged_hash_or_incomplete_matrix(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path, report = self._write_report(directory)
            damaged = deepcopy(report)
            damaged["Captures"][2]["Sha256"] = "0" * 64
            report_path.write_text(json.dumps(damaged), encoding="utf-8")
            with self.assertRaisesRegex(AnimeCardBodySliceError, "hash differs"):
                validate_report(report_path, 1600, 900)

            damaged = deepcopy(report)
            damaged["Captures"].pop()
            report_path.write_text(json.dumps(damaged), encoding="utf-8")
            with self.assertRaisesRegex(AnimeCardBodySliceError, "eight-state"):
                validate_report(report_path, 1600, 900)

    def test_rejects_unstable_or_unbounded_frame_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path, report = self._write_report(directory)
            damaged = deepcopy(report)
            damaged["Captures"][0]["FrameStability"]["SecondPixelSha256"] = "0" * 64
            report_path.write_text(json.dumps(damaged), encoding="utf-8")
            with self.assertRaisesRegex(AnimeCardBodySliceError, "pixel-identical"):
                validate_report(report_path, 1600, 900)

            damaged = deepcopy(report)
            damaged["Captures"][0]["FrameStability"]["AttemptCount"] = 31
            report_path.write_text(json.dumps(damaged), encoding="utf-8")
            with self.assertRaisesRegex(AnimeCardBodySliceError, "pixel-identical"):
                validate_report(report_path, 1600, 900)

    def test_producer_compares_adjacent_frame_hashes_with_bounded_retry(self) -> None:
        root = Path(__file__).resolve().parents[2]
        producer = (
            root / "client/godot/scripts/Ci/AnimeCardBodySliceSuite.cs"
        ).read_text(encoding="utf-8")
        helper = (
            root / "client/godot/scripts/Ci/AnimeCardBodyFrameStability.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("MaximumStableFramePairAttempts = 30", producer)
        self.assertIn("previousSample.HasIdenticalPixels(currentSample)", producer)
        self.assertIn("attempt <= MaximumStableFramePairAttempts", producer)
        self.assertIn("did not produce two pixel-identical consecutive", producer)
        self.assertIn("SHA256.HashData(pixels)", helper)
        self.assertIn("Pixels.AsSpan().SequenceEqual(other.Pixels)", helper)

    def test_rejects_offscreen_too_small_or_empty_gpu_badge_roi(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path, report = self._write_report(directory)
            hand = next(
                capture for capture in report["Captures"] if capture["State"] == "hand-five"
            )
            for field, value, message in (
                ("FullyInsideViewport", False, "readable badge"),
                ("PixelHeight", 16, "off-screen, too small"),
                ("BrightPixelCount", 1, "integer >= 2"),
                ("BrightGlyphDifferencePixelCount", 1, "integer >= 2"),
            ):
                damaged = deepcopy(report)
                target = next(
                    capture
                    for capture in damaged["Captures"]
                    if capture["State"] == hand["State"]
                )["GpuReadability"]["Actors"][0]["Badges"][0]
                target[field] = value
                report_path.write_text(json.dumps(damaged), encoding="utf-8")
                with self.assertRaisesRegex(AnimeCardBodySliceError, message):
                    validate_report(report_path, 1600, 900)

    def test_rejects_cross_actor_or_fragmentary_gpu_glyph_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path, report = self._write_report(directory)
            cases = (
                ("ReferenceActorName", "another-actor", "readable badge"),
                ("GlyphPixelHeight", 5, "lacks final GPU pixels"),
                ("GlyphDifferencePixelCount", 7, "lacks final GPU pixels"),
                (
                    "HighContrastGlyphDifferencePixelCount",
                    2,
                    "lacks final GPU pixels",
                ),
                ("MaximumGlyphContrast", 0.17, "lacks final GPU pixels"),
                ("GlyphRoiX", 123, "lacks final GPU pixels"),
            )
            for field, value, message in cases:
                with self.subTest(field=field):
                    damaged = deepcopy(report)
                    target = next(
                        capture
                        for capture in damaged["Captures"]
                        if capture["State"] == "hand-five"
                    )["GpuReadability"]["Actors"][0]["Badges"][0]
                    target[field] = value
                    report_path.write_text(json.dumps(damaged), encoding="utf-8")
                    with self.assertRaisesRegex(AnimeCardBodySliceError, message):
                        validate_report(report_path, 1600, 900)

    def test_rejects_two_digit_value_with_single_digit_pixel_width(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path, report = self._write_report(directory)
            target = next(
                capture
                for capture in report["Captures"]
                if capture["State"] == "hand-five"
            )["GpuReadability"]["Actors"][0]["Badges"][0]
            target["Text"] = "10"
            target["GlyphPixelWidth"] = 5
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(AnimeCardBodySliceError, "lacks final GPU pixels"):
                validate_report(report_path, 1600, 900)

    def test_rejects_badge_glyph_outside_its_decorative_socket(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path, report = self._write_report(directory)
            target = next(
                capture
                for capture in report["Captures"]
                if capture["State"] == "hand-five"
            )["GpuReadability"]["Actors"][0]["Badges"][0]
            target["GlyphSocketInsetLeft"] = 0.0
            target["GlyphInsideSocket"] = False
            report_path.write_text(json.dumps(report), encoding="utf-8")
            with self.assertRaisesRegex(
                AnimeCardBodySliceError,
                "decorative socket",
            ):
                validate_report(report_path, 1600, 900)

    def test_rejects_cross_actor_overflowing_or_fragmentary_gpu_name(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path, report = self._write_report(directory)
            cases = (
                ("ReferenceActorName", "another-actor", "complete source name"),
                ("GlyphPixelWidth", 17, "lacks final GPU pixels"),
                ("GlyphPixelHeight", 5, "lacks final GPU pixels"),
                ("GlyphDifferencePixelCount", 11, "lacks final GPU pixels"),
                ("HighContrastGlyphDifferencePixelCount", 3, "lacks final GPU pixels"),
                ("MaximumGlyphContrast", 0.17, "lacks final GPU pixels"),
                ("GlyphSocketInsetLeft", 0.0, "lacks final GPU pixels"),
                ("TextSocketNamePlateInsetLeft", 0.0, "lacks final GPU pixels"),
                (
                    "RequiredNamePlateHorizontalInsetPixels",
                    4.9,
                    "lacks final GPU pixels",
                ),
            )
            for field, value, message in cases:
                with self.subTest(field=field):
                    damaged = deepcopy(report)
                    target = next(
                        capture
                        for capture in damaged["Captures"]
                        if capture["State"] == "hand-five"
                    )["GpuReadability"]["Actors"][0]["Name"]
                    target[field] = value
                    report_path.write_text(json.dumps(damaged), encoding="utf-8")
                    with self.assertRaisesRegex(AnimeCardBodySliceError, message):
                        validate_report(report_path, 1600, 900)

    def test_rejects_truncated_tiny_or_off_center_card_name(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path, report = self._write_report(directory)
            cases = (
                ("Text", "曜誓大团长…", "complete source name"),
                ("FullNameMatchesSource", False, "complete source name"),
                ("FontSize", 13, "integer >= 14"),
                ("GlyphSocketCenterDeltaX", 2.51, "off-center"),
                ("GlyphCenteredInTextSocket", False, "complete source name"),
            )
            for field, value, message in cases:
                with self.subTest(field=field):
                    damaged = deepcopy(report)
                    target = next(
                        capture
                        for capture in damaged["Captures"]
                        if capture["State"] == "hand-five"
                    )["GpuReadability"]["Actors"][0]["Name"]
                    target[field] = value
                    report_path.write_text(json.dumps(damaged), encoding="utf-8")
                    with self.assertRaisesRegex(AnimeCardBodySliceError, message):
                        validate_report(report_path, 1600, 900)

            damaged = deepcopy(report)
            next(
                capture
                for capture in damaged["Captures"]
                if capture["State"] == "hand-five"
            )["GpuReadability"]["CompleteNameCount"] = 4
            report_path.write_text(json.dumps(damaged), encoding="utf-8")
            with self.assertRaisesRegex(AnimeCardBodySliceError, "every complete card name"):
                validate_report(report_path, 1600, 900)

    def test_real_actor_producer_uses_masked_local_layers_and_gpu_rois(self) -> None:
        root = Path(__file__).resolve().parents[2]
        actor = (root / "client/godot/scripts/Battlefield/CardActor3D.cs").read_text(
            encoding="utf-8"
        )
        screen = (
            root / "client/godot/scripts/Preview/AnimeCardBodySliceScreen.cs"
        ).read_text(encoding="utf-8")
        suite = (
            root / "client/godot/scripts/Ci/AnimeCardBodySliceSuite.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("ref _productSurfaceMaterial", actor)
        self.assertIn("ref _productFoilMaterial", actor)
        self.assertIn("_baseMesh.Visible = false", actor)
        self.assertGreaterEqual(actor.count('SetShaderParameter("art_texture", default(Variant))'), 1)
        self.assertGreaterEqual(actor.count('SetShaderParameter("frame_mask", default(Variant))'), 1)
        self.assertIn("actor.CiGpuReadabilityEvidence(_camera)", screen)
        self.assertIn("frame.GetPixel(x, y)", screen)
        self.assertIn("frameWithoutActorValueLabels.GetPixel(x, y)", screen)
        self.assertIn("SetGpuValueLabelsVisibleForActor(actorName, false)", suite)
        self.assertIn("SetGpuNameLabelVisibleForActor(actorName, false)", suite)
        self.assertIn("SetProductFaceLayersVisibleForActor(actorName, false)", suite)
        self.assertIn("framesWithoutActorProductLayers.TryGetValue", screen)
        self.assertIn("CiProductSilhouetteGpuProbes(_camera)", screen)
        self.assertIn("CiProductInteriorGpuPosition(_camera)", screen)
        self.assertIn("GpuReadability = gpuReadability", suite)

    def test_full_bleed_face_uses_opaque_nameplate_without_rectangular_front_shadow(self) -> None:
        root = Path(__file__).resolve().parents[2]
        contracts = (
            root / "client/godot/scripts/CardFaces/CardFaceContracts.cs"
        ).read_text(encoding="utf-8")
        actor = (
            root / "client/godot/scripts/Battlefield/CardActor3D.cs"
        ).read_text(encoding="utf-8")
        preview = (
            root / "client/godot/scripts/Preview/AnimeCardPreview.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("new CardFaceRect(0.0f, 0.0f, 1.0f, 1.0f)", contracts)
        self.assertIn("CardFaceRect NameText", contracts)
        self.assertIn("MinimumNameDecorationInset = 0.060f", contracts)
        self.assertIn("new CardFaceRect(0.200f, nameY + 0.012f, 0.600f", contracts)
        self.assertIn("NameText.X - NamePlate.X < MinimumNameDecorationInset", contracts)
        self.assertIn("CardFaceRect CostText", contracts)
        self.assertIn("string NamePlatePath", contracts)
        self.assertIn("_productNamePlate", actor)
        self.assertIn("label.NoDepthTest = noDepthTest", actor)
        self.assertIn("ProductCardFont.GetStringSize(", actor)
        self.assertIn("ProductCardFont.GetAscent(", actor)
        self.assertIn("ProductCardFont.GetDescent(", actor)
        self.assertIn("FitProductText(", actor)
        self.assertIn("composition.Layout.NameText", actor)
        self.assertIn("glyphBounds.GetCenter()", actor)
        self.assertIn("SocketScreenRect", actor)
        self.assertIn("CardDisplayFont.GetStringSize(", preview)
        self.assertIn("CardDisplayFont.GetAscent(", preview)
        self.assertIn("CardDisplayFont.GetDescent(", preview)
        self.assertIn("composition.Layout.NameText", preview)
        self.assertNotIn("ellipsize", preview.casefold())
        self.assertNotIn("ellipsize", actor.casefold())
        self.assertNotIn("FitName(", contracts)
        self.assertNotIn("EnumerateRunes", contracts)
        self.assertNotIn('"…"', preview)
        self.assertNotIn('"…"', actor)
        self.assertIn(
            "if (_hidden)\n        {\n            DrawCardShadow(bounds);",
            preview,
        )
        self.assertEqual(1, preview.count("DrawCardShadow(bounds);"))
        self.assertNotIn(
            "DrawCardShadow(bounds);\n        if (_hidden)",
            preview,
        )

    def test_numeric_gems_have_explicit_inner_bays_and_two_digit_countdown_width(self) -> None:
        root = Path(__file__).resolve().parents[2]
        contracts = (
            root / "client/godot/scripts/CardFaces/CardFaceContracts.cs"
        ).read_text(encoding="utf-8")
        attack = (
            root / "client/godot/assets/visual/anime_v1/card_body/gems/attack.svg"
        ).read_text(encoding="utf-8")
        cost = (
            root / "client/godot/assets/visual/anime_v1/card_body/gems/cost.svg"
        ).read_text(encoding="utf-8")
        health = (
            root / "client/godot/assets/visual/anime_v1/card_body/gems/health.svg"
        ).read_text(encoding="utf-8")
        countdown = (
            root / "client/godot/assets/visual/anime_v1/card_body/gems/countdown.svg"
        ).read_text(encoding="utf-8")

        self.assertIn("0.308f, 0.163f", contracts)
        self.assertIn("0.218f, 0.111f", contracts)
        self.assertIn('<ellipse cx="128" cy="132"', attack)
        self.assertIn('<ellipse cx="128" cy="128"', cost)
        self.assertIn('<ellipse cx="128" cy="126"', health)
        self.assertIn('<rect x="74" y="67" width="172"', countdown)

    def test_rejects_rectangular_base_or_corner_residue(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            report_path, report = self._write_report(directory)
            representatives = next(
                capture
                for capture in report["Captures"]
                if capture["State"] == "representatives"
            )
            for field, value in (
                ("AllRectangularBasesHidden", False),
                ("AllCornerProbesMatchBackground", False),
                ("AllInteriorProbesShowProductFace", False),
            ):
                damaged = deepcopy(report)
                target = next(
                    capture
                    for capture in damaged["Captures"]
                    if capture["State"] == representatives["State"]
                )["SilhouetteIsolation"]
                target[field] = value
                report_path.write_text(json.dumps(damaged), encoding="utf-8")
                with self.assertRaisesRegex(AnimeCardBodySliceError, "rectangular"):
                    validate_report(report_path, 1600, 900)


if __name__ == "__main__":
    unittest.main()
