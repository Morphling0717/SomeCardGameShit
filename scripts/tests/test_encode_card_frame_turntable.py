"""Pure-standard-library contracts; do not launch FFmpeg, Godot or native."""
from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest
import zlib

MODULE_PATH = Path(__file__).resolve().parents[1] / "dev" / "encode_card_frame_turntable.py"
SPEC = importlib.util.spec_from_file_location("encode_card_frame_turntable", MODULE_PATH)
encoder = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = encoder
SPEC.loader.exec_module(encoder)


def tiny_png(width=2, height=2):
    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body))
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)) + \
        chunk(b"IDAT", zlib.compress((b"\0" + b"\xff\xff\xff\xff" * width) * height)) + chunk(b"IEND", b"")


class TurntableEncodingTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.manifest = self.root / "manifest.json"
        frames = []
        for index, ticks in enumerate((10000000, 10940000, 12210000)):
            image = self.root / f"frame-{index:04}.png"
            image.write_bytes(tiny_png())
            frames.append(dict(index=index, image=str(image), sha256=hashlib.sha256(image.read_bytes()).hexdigest(),
                               width=2, height=2, timestamp_ticks=ticks, time_seconds=(ticks - 10000000) / 10000000))
        self.data = dict(schema_version=1, suite="card-frame-r1-public-design-turntable", available=True,
                         status="recorded", design_display=True, gameplay_recording=False, not_fixed_fps=True,
                         design_id="LO-11", capture_id="example-1", captured_image_size=[2, 2],
                         required_pixel_label="卡框设计展示 · 非对局状态", timestamp_frequency=10000000,
                         actual_frame_count=3, last_frame_seconds=.221, frames=frames)

    def validate(self, data=None):
        self.manifest.write_text(json.dumps(data or self.data), encoding="utf-8")
        return encoder.validate_capture(self.manifest)

    def reject(self, mutate):
        data = copy.deepcopy(self.data)
        mutate(data)
        with self.assertRaises((encoder.ValidationError, OSError)):
            self.validate(data)

    def test_valid_actual_irregular_timestamps(self):
        capture = self.validate()
        self.assertEqual(encoder.presentation_microseconds(capture), [0, 94000, 221000])

    def test_concat_uses_differences_and_never_duplicates_last_frame(self):
        text = encoder.concat_text(self.validate())
        self.assertEqual(text.count("\nfile "), 3)
        self.assertEqual(text.count("\nduration "), 2)
        self.assertIn("duration 0.094000", text)
        self.assertIn("duration 0.127000", text)

    def test_rejects_count_above_bound(self):
        self.reject(lambda d: d.update(frames=d["frames"] * 61, actual_frame_count=183))

    def test_rejects_count_or_index_mismatch(self):
        self.reject(lambda d: d.update(actual_frame_count=2))
        self.reject(lambda d: d["frames"][1].update(index=2))

    def test_rejects_relative_or_outside_path(self):
        self.reject(lambda d: d["frames"][0].update(image="frame-0000.png"))
        self.reject(lambda d: d["frames"][0].update(image=str(self.root.parent / "not-captured.png")))

    def test_rejects_hash_and_header_or_dimensions(self):
        self.reject(lambda d: d["frames"][0].update(sha256="0" * 64))
        self.reject(lambda d: d["frames"][0].update(width=4))
        self.reject(lambda d: d.update(captured_image_size=[4, 2]))
        with self.assertRaises(encoder.ValidationError):
            encoder.png_dimensions(b"not a PNG")

    def test_rejects_nonmonotonic_and_inconsistent_clock(self):
        self.reject(lambda d: d["frames"][1].update(time_seconds=0))
        self.reject(lambda d: d["frames"][1].update(timestamp_ticks=10000000))
        self.reject(lambda d: d["frames"][1].update(time_seconds=.10))

    def test_rejects_nonfinite_and_boolean_times(self):
        self.reject(lambda d: d["frames"][1].update(time_seconds=float("nan")))
        self.reject(lambda d: d["frames"][1].update(time_seconds=True))

    def test_rejects_unlabelled_or_gameplay_claim(self):
        self.reject(lambda d: d.update(gameplay_recording=True))
        self.reject(lambda d: d.update(required_pixel_label=""))
        self.reject(lambda d: d.update(not_fixed_fps=False))

    def test_rejects_noncompleted_and_bad_design(self):
        self.reject(lambda d: d.update(status="aborted"))
        self.reject(lambda d: d.update(design_id="LO-01"))
        self.reject(lambda d: d.update(capture_id="../escape"))

    def test_rejects_duplicate_path_or_json_keys(self):
        self.reject(lambda d: d["frames"][1].update(image=d["frames"][0]["image"]))
        self.manifest.write_text('{"schema_version":1,"schema_version":1}', encoding="utf-8")
        with self.assertRaises(encoder.ValidationError):
            encoder.validate_capture(self.manifest)

    def test_verified_encoded_count_and_pts_must_match(self):
        capture = self.validate()
        frames = [dict(media_type="video", best_effort_timestamp_time=str(value)) for value in (0, .094, .221)]
        self.assertTrue(encoder.verify_encoded(capture, dict(frames=frames))["one_to_one_captured_frames"])
        with self.assertRaises(encoder.ValidationError):
            encoder.verify_encoded(capture, dict(frames=frames + [frames[-1]]))
        frames[1]["best_effort_timestamp_time"] = ".100"
        with self.assertRaises(encoder.ValidationError):
            encoder.verify_encoded(capture, dict(frames=frames))


if __name__ == "__main__":
    unittest.main()
