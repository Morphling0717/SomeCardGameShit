#!/usr/bin/env python3
"""Compare two 8-bit PNG screenshots using Gate 4B perceptual thresholds."""

# SPDX-License-Identifier: GPL-3.0-or-later
from __future__ import annotations

import argparse
import math
import struct
import sys
import zlib
from pathlib import Path


TARGET_WIDTH = 320
TARGET_HEIGHT = 180
DEFAULT_MAE_LIMIT = 0.025
DEFAULT_EDGE_LIMIT = 0.08


class GoldenComparisonError(ValueError):
    """Raised when PNG input is unsupported or visual drift exceeds the budget."""


def _paeth(left: int, up: int, upper_left: int) -> int:
    estimate = left + up - upper_left
    dl = abs(estimate - left)
    du = abs(estimate - up)
    dul = abs(estimate - upper_left)
    return left if dl <= du and dl <= dul else up if du <= dul else upper_left


def read_png(path: Path) -> tuple[int, int, list[tuple[int, int, int]]]:
    data = path.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise GoldenComparisonError(f"not a PNG: {path}")
    offset = 8
    compressed = bytearray()
    width = height = color_type = bit_depth = interlace = -1
    while offset + 12 <= len(data):
        length = struct.unpack(">I", data[offset : offset + 4])[0]
        kind = data[offset + 4 : offset + 8]
        payload = data[offset + 8 : offset + 8 + length]
        if offset + 12 + length > len(data):
            raise GoldenComparisonError(f"truncated PNG chunk: {path}")
        if kind == b"IHDR":
            width, height, bit_depth, color_type, _, _, interlace = struct.unpack(
                ">IIBBBBB", payload
            )
        elif kind == b"IDAT":
            compressed.extend(payload)
        elif kind == b"IEND":
            break
        offset += 12 + length
    channels = {0: 1, 2: 3, 4: 2, 6: 4}.get(color_type)
    if width <= 0 or height <= 0 or bit_depth != 8 or channels is None or interlace != 0:
        raise GoldenComparisonError(
            f"unsupported PNG (need non-interlaced 8-bit gray/RGB/RGBA): {path}"
        )
    try:
        raw = zlib.decompress(bytes(compressed))
    except zlib.error as error:
        raise GoldenComparisonError(f"invalid PNG deflate stream: {path}") from error
    row_bytes = width * channels
    expected = height * (row_bytes + 1)
    if len(raw) != expected:
        raise GoldenComparisonError(f"unexpected PNG scanline length: {path}")

    rows: list[bytearray] = []
    cursor = 0
    previous = bytearray(row_bytes)
    for _ in range(height):
        filter_type = raw[cursor]
        cursor += 1
        encoded = raw[cursor : cursor + row_bytes]
        cursor += row_bytes
        decoded = bytearray(row_bytes)
        for index, byte in enumerate(encoded):
            left = decoded[index - channels] if index >= channels else 0
            up = previous[index]
            upper_left = previous[index - channels] if index >= channels else 0
            predictor = {
                0: 0,
                1: left,
                2: up,
                3: (left + up) // 2,
                4: _paeth(left, up, upper_left),
            }.get(filter_type)
            if predictor is None:
                raise GoldenComparisonError(f"unsupported PNG filter {filter_type}: {path}")
            decoded[index] = (byte + predictor) & 0xFF
        rows.append(decoded)
        previous = decoded

    pixels: list[tuple[int, int, int]] = []
    for row in rows:
        for x in range(width):
            at = x * channels
            if color_type in (0, 4):
                value = row[at]
                pixels.append((value, value, value))
            else:
                pixels.append((row[at], row[at + 1], row[at + 2]))
    return width, height, pixels


def resize_area(
    width: int,
    height: int,
    pixels: list[tuple[int, int, int]],
    target_width: int = TARGET_WIDTH,
    target_height: int = TARGET_HEIGHT,
) -> list[tuple[int, int, int]]:
    if width < target_width or height < target_height:
        raise GoldenComparisonError(
            f"visual golden input must be at least {target_width}x{target_height}"
        )
    output: list[tuple[int, int, int]] = []
    for target_y in range(target_height):
        y0 = target_y * height // target_height
        y1 = max(y0 + 1, (target_y + 1) * height // target_height)
        for target_x in range(target_width):
            x0 = target_x * width // target_width
            x1 = max(x0 + 1, (target_x + 1) * width // target_width)
            red = green = blue = count = 0
            for y in range(y0, y1):
                row = y * width
                for x in range(x0, x1):
                    r, g, b = pixels[row + x]
                    red += r
                    green += g
                    blue += b
                    count += 1
            output.append((red // count, green // count, blue // count))
    return output


def _luminance(pixel: tuple[int, int, int]) -> float:
    red, green, blue = pixel
    return (0.2126 * red + 0.7152 * green + 0.0722 * blue) / 255.0


def _edge_map(pixels: list[tuple[int, int, int]]) -> list[float]:
    luminance = [_luminance(pixel) for pixel in pixels]
    output = [0.0] * len(luminance)
    width = TARGET_WIDTH
    for y in range(1, TARGET_HEIGHT - 1):
        for x in range(1, TARGET_WIDTH - 1):
            index = y * width + x
            gx = (
                luminance[index - width + 1]
                + 2.0 * luminance[index + 1]
                + luminance[index + width + 1]
                - luminance[index - width - 1]
                - 2.0 * luminance[index - 1]
                - luminance[index + width - 1]
            )
            gy = (
                luminance[index + width - 1]
                + 2.0 * luminance[index + width]
                + luminance[index + width + 1]
                - luminance[index - width - 1]
                - 2.0 * luminance[index - width]
                - luminance[index - width + 1]
            )
            output[index] = min(1.0, math.sqrt(gx * gx + gy * gy) / 4.0)
    return output


def _png_chunk(kind: bytes, payload: bytes) -> bytes:
    return (
        struct.pack(">I", len(payload))
        + kind
        + payload
        + struct.pack(">I", zlib.crc32(kind + payload) & 0xFFFFFFFF)
    )


def write_heatmap(path: Path, differences: list[float]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    scanlines = bytearray()
    for y in range(TARGET_HEIGHT):
        scanlines.append(0)
        for x in range(TARGET_WIDTH):
            value = max(0.0, min(1.0, differences[y * TARGET_WIDTH + x]))
            intensity = int(round(value * 255.0))
            scanlines.extend((intensity, max(0, intensity - 96), 0, 255))
    header = struct.pack(">IIBBBBB", TARGET_WIDTH, TARGET_HEIGHT, 8, 6, 0, 0, 0)
    encoded = (
        b"\x89PNG\r\n\x1a\n"
        + _png_chunk(b"IHDR", header)
        + _png_chunk(b"IDAT", zlib.compress(bytes(scanlines), 9))
        + _png_chunk(b"IEND", b"")
    )
    path.write_bytes(encoded)


def compare(
    actual_path: Path,
    expected_path: Path,
    *,
    mae_limit: float = DEFAULT_MAE_LIMIT,
    edge_limit: float = DEFAULT_EDGE_LIMIT,
    heatmap_path: Path | None = None,
) -> tuple[float, float]:
    actual = resize_area(*read_png(actual_path))
    expected = resize_area(*read_png(expected_path))
    channel_differences = [
        (abs(ar - er) + abs(ag - eg) + abs(ab - eb)) / (3.0 * 255.0)
        for (ar, ag, ab), (er, eg, eb) in zip(actual, expected, strict=True)
    ]
    mae = sum(channel_differences) / len(channel_differences)
    actual_edges = _edge_map(actual)
    expected_edges = _edge_map(expected)
    edge_differences = [
        abs(actual_edge - expected_edge)
        for actual_edge, expected_edge in zip(actual_edges, expected_edges, strict=True)
    ]
    edge_difference = sum(edge_differences) / len(edge_differences)
    if heatmap_path is not None:
        write_heatmap(heatmap_path, channel_differences)
    if mae > mae_limit or edge_difference > edge_limit:
        raise GoldenComparisonError(
            f"visual drift exceeded threshold: MAE={mae:.6f} (limit {mae_limit:.6f}), "
            f"edge={edge_difference:.6f} (limit {edge_limit:.6f})"
        )
    return mae, edge_difference


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--actual", type=Path, required=True)
    parser.add_argument("--expected", type=Path, required=True)
    parser.add_argument("--heatmap", type=Path)
    parser.add_argument("--mae-limit", type=float, default=DEFAULT_MAE_LIMIT)
    parser.add_argument("--edge-limit", type=float, default=DEFAULT_EDGE_LIMIT)
    args = parser.parse_args()
    try:
        mae, edge = compare(
            args.actual,
            args.expected,
            mae_limit=args.mae_limit,
            edge_limit=args.edge_limit,
            heatmap_path=args.heatmap,
        )
    except (OSError, GoldenComparisonError) as error:
        print(f"Gate 4B visual golden comparison failed: {error}", file=sys.stderr)
        return 1
    print(f"visual golden matched: mae={mae:.6f} edge={edge:.6f}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
