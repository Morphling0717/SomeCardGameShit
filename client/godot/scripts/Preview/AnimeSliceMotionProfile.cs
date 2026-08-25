// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.GodotClient.Preview;

/// <summary>
/// Pure managed limits for the optional AnimeV1 presentation motion.  Keeping
/// this separate from Godot makes the capture/interactive boundary testable
/// without constructing a scene tree.
/// </summary>
internal sealed record AnimeSliceMotionProfile(
    bool Enabled,
    float BreathPeriodSeconds,
    float BreathScaleAmplitude,
    float ParallaxPixels,
    float EntryDurationSeconds,
    float EntryDistancePixels,
    float HitDurationSeconds,
    float HitScaleAmplitude,
    float HitShakePixels)
{
    internal static AnimeSliceMotionProfile Disabled { get; } = new(
        Enabled: false,
        BreathPeriodSeconds: 0.0f,
        BreathScaleAmplitude: 0.0f,
        ParallaxPixels: 0.0f,
        EntryDurationSeconds: 0.0f,
        EntryDistancePixels: 0.0f,
        HitDurationSeconds: 0.0f,
        HitScaleAmplitude: 0.0f,
        HitShakePixels: 0.0f);

    internal static AnimeSliceMotionProfile Interactive { get; } = new(
        Enabled: true,
        BreathPeriodSeconds: 4.2f,
        BreathScaleAmplitude: 0.012f,
        ParallaxPixels: 10.0f,
        EntryDurationSeconds: 0.36f,
        EntryDistancePixels: 22.0f,
        HitDurationSeconds: 0.30f,
        HitScaleAmplitude: 0.045f,
        HitShakePixels: 7.0f);

    internal bool UsesBoundedLightweightMotion =>
        Enabled &&
        BreathPeriodSeconds is >= 3.0f and <= 6.0f &&
        BreathScaleAmplitude is > 0.0f and <= 0.02f &&
        ParallaxPixels is > 0.0f and <= 14.0f &&
        EntryDurationSeconds is >= 0.20f and <= 0.60f &&
        EntryDistancePixels is > 0.0f and <= 32.0f &&
        HitDurationSeconds is >= 0.15f and <= 0.35f &&
        HitScaleAmplitude is > 0.0f and <= 0.06f &&
        HitShakePixels is > 0.0f and <= 10.0f;
}

internal static class AnimeSliceMotionPolicy
{
    internal static AnimeSliceMotionProfile Select(string? outputDirectory) =>
        outputDirectory is null
            ? AnimeSliceMotionProfile.Interactive
            : AnimeSliceMotionProfile.Disabled;
}
