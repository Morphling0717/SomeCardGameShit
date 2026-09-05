#!/usr/bin/env python3
"""Validate R1 GPU samples and encode their observed timestamps without new frames.

Only standard-library Python is needed for validation. Encoding requires FFmpeg
and its adjacent FFprobe. This is a labelled public-design turntable, not a native
gameplay recording. The MP4 is VFR: no frame interpolation, repetition or speed-up.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
import hashlib
import json
import math
from pathlib import Path
import struct
import subprocess
import sys
import zlib


class ValidationError(ValueError):
    pass


@dataclass(frozen=True)
class Frame:
    index: int
    image: Path
    sha256: str
    time_seconds: float
    timestamp_ticks: int


@dataclass(frozen=True)
class Capture:
    manifest: Path
    manifest_sha256: str
    capture_id: str
    design_id: str
    width: int
    height: int
    frequency: int
    frames: tuple[Frame, ...]


def _require(condition: bool, message: str) -> None:
    if not condition:
        raise ValidationError(message)


def _object(pairs: list[tuple[str, object]]) -> dict:
    result = {}
    for key, value in pairs:
        _require(key not in result, f"duplicate JSON field: {key}")
        result[key] = value
    return result


def _finite(value: object, name: str) -> float:
    _require(type(value) in (int, float) and math.isfinite(value), f"{name}: finite number required")
    return float(value)


def _integer(value: object, name: str) -> int:
    _require(type(value) is int, f"{name}: integer required")
    return value


def png_dimensions(data: bytes) -> tuple[int, int]:
    _require(len(data) >= 33 and data[:8] == b"\x89PNG\r\n\x1a\n", "invalid PNG signature")
    _require(data[8:16] == b"\x00\x00\x00\rIHDR", "invalid PNG IHDR")
    _require(zlib.crc32(data[12:29]) == struct.unpack(">I", data[29:33])[0], "invalid PNG IHDR CRC")
    width, height = struct.unpack(">II", data[16:24])
    _require(0 < width <= 8192 and 0 < height <= 8192, "PNG dimensions out of bounds")
    return width, height


def validate_capture(manifest: Path) -> Capture:
    manifest = manifest.resolve(strict=True)
    raw = manifest.read_bytes()
    _require(len(raw) <= 1024 * 1024, "manifest exceeds 1 MiB")
    try:
        data = json.loads(raw.decode("utf-8-sig"), object_pairs_hook=_object,
                          parse_constant=lambda value: (_ for _ in ()).throw(ValidationError(f"nonfinite JSON {value}")))
    except (UnicodeError, json.JSONDecodeError) as failure:
        raise ValidationError("invalid UTF-8 JSON manifest") from failure
    _require(isinstance(data, dict), "manifest must be an object")
    _require(data.get("schema_version") == 1 and type(data.get("schema_version")) is int,
             "unsupported turntable schema")
    _require(data.get("suite") == "card-frame-r1-public-design-turntable", "wrong capture suite")
    _require(data.get("status") == "recorded" and data.get("available") is True, "capture not completed")
    _require(data.get("design_display") is True and data.get("gameplay_recording") is False,
             "must remain a labelled public-design display")
    _require(data.get("not_fixed_fps") is True, "VFR source declaration required")
    _require(data.get("required_pixel_label") == "卡框设计展示 · 非对局状态", "design-display pixel label contract missing")
    _require(data.get("design_id") in ("LO-11", "AP-11", "NT-04"), "unknown representative design")
    capture_id = data.get("capture_id")
    _require(isinstance(capture_id, str) and 1 <= len(capture_id) <= 80 and
             all(char.isascii() and (char.isalnum() or char in "-_") for char in capture_id), "unsafe capture ID")
    dimensions = data.get("captured_image_size")
    _require(isinstance(dimensions, list) and len(dimensions) == 2, "captured image size required")
    width, height = (_integer(value, "capture dimension") for value in dimensions)
    _require(width > 0 and height > 0 and width % 2 == height % 2 == 0, "positive even dimensions required for yuv420p")
    frequency = _integer(data.get("timestamp_frequency"), "timestamp_frequency")
    _require(0 < frequency <= 10**12, "monotonic clock frequency out of bounds")
    entries = data.get("frames")
    _require(isinstance(entries, list) and 2 <= len(entries) <= 180, "capture must contain 2..180 real frames")
    _require(_integer(data.get("actual_frame_count"), "actual_frame_count") == len(entries), "actual frame count mismatch")
    result = []
    seen = set()
    for index, item in enumerate(entries):
        _require(isinstance(item, dict), "frame must be an object")
        _require(_integer(item.get("index"), "frame index") == index, "nonconsecutive frame index")
        path_text = item.get("image")
        _require(isinstance(path_text, str) and not any(char in path_text for char in "\r\n\0"), "unsafe frame path")
        path = Path(path_text)
        _require(path.is_absolute(), "frame path must be absolute")
        path = path.resolve(strict=True)
        _require(path.suffix.lower() == ".png" and path.is_file(), "frame must be a PNG file")
        _require(path.is_relative_to(manifest.parent), "frame must stay inside its capture directory")
        _require(path not in seen, "duplicate frame path")
        seen.add(path)
        _require(path.stat().st_size <= 20 * 1024 * 1024, "frame PNG exceeds 20 MiB")
        pixels = path.read_bytes()
        digest = hashlib.sha256(pixels).hexdigest()
        _require(item.get("sha256") == digest, "frame SHA-256 mismatch")
        _require(png_dimensions(pixels) == (width, height), "PNG dimensions mismatch")
        _require(type(item.get("width")) is int and type(item.get("height")) is int and
                 (item["width"], item["height"]) == (width, height), "frame dimensions mismatch")
        seconds = _finite(item.get("time_seconds"), "time_seconds")
        ticks = _integer(item.get("timestamp_ticks"), "timestamp_ticks")
        _require(seconds >= 0 and ticks > 0, "negative/zero monotonic time")
        if result:
            _require(seconds > result[-1].time_seconds and ticks > result[-1].timestamp_ticks,
                     "frame timestamps must be strictly increasing")
            expected = (ticks - result[0].timestamp_ticks) / frequency
            _require(abs((seconds - result[0].time_seconds) - expected) <= 2 / frequency + 1e-8,
                     "seconds disagree with monotonic ticks")
        result.append(Frame(index, path, digest, seconds, ticks))
    _require(result[-1].time_seconds - result[0].time_seconds <= 15, "capture exceeds bounded observation period")
    _require(abs(_finite(data.get("last_frame_seconds"), "last_frame_seconds") - result[-1].time_seconds) <= 1e-7,
             "last frame time mismatch")
    return Capture(manifest, hashlib.sha256(raw).hexdigest(), capture_id, data["design_id"],
                   width, height, frequency, tuple(result))


def presentation_microseconds(capture: Capture) -> list[int]:
    first = capture.frames[0].timestamp_ticks
    # Integer rounding preserves the measured clock without cumulative float drift.
    values = [((frame.timestamp_ticks - first) * 1_000_000 + capture.frequency // 2) // capture.frequency
              for frame in capture.frames]
    _require(all(right > left for left, right in zip(values, values[1:])), "timestamps collapse at 1us encoding precision")
    return values


def concat_text(capture: Capture) -> str:
    times = presentation_microseconds(capture)
    lines = ["ffconcat version 1.0"]
    for index, frame in enumerate(capture.frames):
        escaped = frame.image.as_posix().replace("'", "'\\''")
        lines += [f"file '{escaped}'", "option framerate 1000000"]
        if index + 1 < len(times):
            duration = times[index + 1] - times[index]
            lines.append(f"duration {duration // 1_000_000}.{duration % 1_000_000:06d}")
    # No repeated last image to extend an unobserved tail. Every encoded frame
    # corresponds one-to-one to a captured PNG.
    return "\n".join(lines) + "\n"


def verify_encoded(capture: Capture, probe: dict) -> dict:
    video = [frame for frame in probe.get("frames", []) if frame.get("media_type") == "video"]
    expected = presentation_microseconds(capture)
    _require(len(video) == len(expected), "encoded frame count changed (dropped or duplicated sample)")
    actual = [_finite(float(frame["best_effort_timestamp_time"]), "encoded PTS") for frame in video]
    _require(all(right > left for left, right in zip(actual, actual[1:])), "encoded PTS not strictly increasing")
    errors = [abs(value - wanted / 1_000_000) for value, wanted in zip(actual, expected)]
    _require(max(errors) <= 0.000002, "encoded PTS differs from observed time by more than 2us")
    return {"encoded_frame_count": len(video), "max_timestamp_error_seconds": max(errors),
            "first_frame_pts_seconds": actual[0], "last_frame_pts_seconds": actual[-1],
            "one_to_one_captured_frames": True, "fixed_fps_claim": False}


def encode(capture: Capture, output_dir: Path, ffmpeg: Path, overwrite: bool = False) -> Path:
    output_dir = output_dir.resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    ffmpeg = ffmpeg.resolve(strict=True)
    ffprobe = ffmpeg.with_name("ffprobe.exe" if ffmpeg.suffix.lower() == ".exe" else "ffprobe")
    _require(ffprobe.is_file(), "adjacent FFprobe is required to verify actual output timestamps")
    name = f"{capture.design_id}-{capture.capture_id}-design-turntable"
    video = output_dir / (name + ".mp4")
    concat = output_dir / (name + ".ffconcat")
    report_path = output_dir / (name + ".encoding.json")
    _require(overwrite or not any(path.exists() for path in (video, concat, report_path)),
             "output exists; use explicit --overwrite to replace this capture's outputs")
    concat.write_text(concat_text(capture), encoding="utf-8", newline="\n")
    # Give x264 a representative *measured* rate for codec level/bitrate selection
    # instead of mistaking the 1us timestamp timebase for one million fps. This
    # private encoder hint does not retime the FFmpeg packets; verify every PTS.
    ticks = presentation_microseconds(capture)
    rate_num, rate_den = (len(ticks) - 1) * 1_000_000, ticks[-1]
    divisor = math.gcd(rate_num, rate_den)
    measured_rate_hint = f"{rate_num // divisor}/{rate_den // divisor}"
    command = [str(ffmpeg), "-hide_banner", "-loglevel", "warning", "-y" if overwrite else "-n",
               "-f", "concat", "-safe", "0", "-i", str(concat), "-map", "0:v:0", "-an",
               "-c:v", "libx264", "-preset", "medium", "-crf", "17", "-pix_fmt", "yuv420p",
               "-x264-params", f"fps={measured_rate_hint}:force-cfr=0",
               # No -r: recent FFmpeg correctly rejects a forced frame rate with
               # non-CFR passthrough. The 1us input timebase is not a fps claim.
               "-fps_mode", "passthrough", "-enc_time_base", "1:1000000",
               "-video_track_timescale", "1000000", "-movflags", "+faststart", str(video)]
    completed = subprocess.run(command, check=True, capture_output=True, text=True, timeout=120)
    probe_command = [str(ffprobe), "-v", "error", "-select_streams", "v:0", "-show_frames",
                     "-show_entries", "frame=media_type,best_effort_timestamp_time", "-of", "json", str(video)]
    probe = json.loads(subprocess.run(probe_command, check=True, capture_output=True, text=True, timeout=60).stdout)
    measured = verify_encoded(capture, probe)
    report = {"schema_version": 1, "suite": "card-frame-r1-design-turntable-vfr-encoding",
              "design_display": True, "gameplay_recording": False, "design_id": capture.design_id,
              "source_manifest": str(capture.manifest), "source_manifest_sha256": capture.manifest_sha256,
              "source_frame_count": len(capture.frames), "source_observed_sample_fps":
                  (len(capture.frames) - 1) / (capture.frames[-1].time_seconds - capture.frames[0].time_seconds),
              "output_video": str(video), "output_sha256": hashlib.sha256(video.read_bytes()).hexdigest(),
              "pixel_size": [capture.width, capture.height], "vfr": True,
              "input_timebase_hz": 1000000, "timebase_is_not_capture_fps": True,
              "x264_measured_rate_hint": measured_rate_hint, "encoder_hint_is_not_constant_frame_rate": True,
              "no_interpolation_or_duplicate_frames": True, "unobserved_tail_extended": False,
              "timeline": "Actual monotonic sample offsets, quantized to 1us; no duplicated last PNG to pad time.",
              "verified": measured, "command": command, "ffmpeg_warnings": completed.stderr.strip(),
              "boundary": "Labelled public card-design inspection, not native match actions, performance or visual approval."}
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return report_path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifests", type=Path, nargs="+")
    parser.add_argument("--ffmpeg", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--overwrite", action="store_true")
    args = parser.parse_args(argv)
    try:
        # Validate every capture before starting any encoder.
        captures = [validate_capture(path) for path in args.manifests]
        for capture in captures:
            print(encode(capture, args.output_dir, args.ffmpeg, args.overwrite))
    except (ValidationError, OSError, subprocess.SubprocessError) as failure:
        print(f"Turntable encoding rejected: {failure}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
