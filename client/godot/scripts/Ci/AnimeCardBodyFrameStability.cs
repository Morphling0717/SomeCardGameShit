// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;

namespace Scgs.GodotClient.Ci;

/// <summary>
/// Content fingerprint used to prove that two adjacent FramePostDraw samples
/// contain the exact same pixels. Keeping the byte-only comparison separate
/// from Godot makes the contract deterministic and unit-testable.
/// </summary>
internal readonly record struct AnimeCardBodyFrameFingerprint(
    int Width,
    int Height,
    string PixelFormat,
    int PixelByteLength,
    string PixelSha256)
{
    internal static AnimeCardBodyFrameFingerprint Create(
        int width,
        int height,
        string pixelFormat,
        ReadOnlySpan<byte> pixels)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                $"Frame dimensions must be positive, found {width}x{height}.");
        }
        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                $"Frame dimensions must be positive, found {width}x{height}.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(pixelFormat);
        if (pixels.IsEmpty)
        {
            throw new ArgumentException("A completed frame cannot have an empty pixel buffer.", nameof(pixels));
        }

        return new AnimeCardBodyFrameFingerprint(
            width,
            height,
            pixelFormat,
            pixels.Length,
            Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant());
    }

    internal bool HasIdenticalPixels(AnimeCardBodyFrameFingerprint other) =>
        Width == other.Width &&
        Height == other.Height &&
        string.Equals(PixelFormat, other.PixelFormat, StringComparison.Ordinal) &&
        PixelByteLength == other.PixelByteLength &&
        string.Equals(PixelSha256, other.PixelSha256, StringComparison.Ordinal);

    internal string DescribeDifference(AnimeCardBodyFrameFingerprint other)
    {
        if (Width != other.Width || Height != other.Height)
        {
            return $"viewport changed from {Width}x{Height} to {other.Width}x{other.Height}";
        }
        if (!string.Equals(PixelFormat, other.PixelFormat, StringComparison.Ordinal))
        {
            return $"pixel format changed from {PixelFormat} to {other.PixelFormat}";
        }
        if (PixelByteLength != other.PixelByteLength)
        {
            return $"pixel buffer changed from {PixelByteLength} to {other.PixelByteLength} bytes";
        }
        return $"pixel SHA-256 changed from {PixelSha256} to {other.PixelSha256}";
    }
}

internal sealed record AnimeCardBodyFrameSample(
    AnimeCardBodyFrameFingerprint Fingerprint,
    byte[] Pixels)
{
    internal static AnimeCardBodyFrameSample Create(
        int width,
        int height,
        string pixelFormat,
        byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        return new AnimeCardBodyFrameSample(
            AnimeCardBodyFrameFingerprint.Create(width, height, pixelFormat, pixels),
            pixels);
    }

    internal bool HasIdenticalPixels(AnimeCardBodyFrameSample other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Fingerprint.HasIdenticalPixels(other.Fingerprint) &&
            Pixels.AsSpan().SequenceEqual(other.Pixels);
    }
}
