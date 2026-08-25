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

internal readonly record struct AnimeSliceViewportSize(int Width, int Height);

/// <summary>
/// Pure managed policy for the screenshot viewport boundary. The hosted
/// macOS runner exception stays explicit and cannot widen product support.
/// </summary>
internal static class AnimeVisualSliceViewportPolicy
{
    internal const string ViewportPrefix = "--ci-visual-viewport=";
    internal const string CiRunnerOption = "--ci-anime-runner-viewport";
    internal static AnimeSliceViewportSize CiRunnerViewport { get; } = new(1024, 684);

    private static readonly HashSet<AnimeSliceViewportSize> ProductViewports =
    [
        new(1280, 720),
        new(1600, 900),
        new(2560, 1440),
        new(2560, 1600),
    ];

    internal static AnimeSliceViewportSize? Resolve(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string[] values = arguments
            .Where(argument => argument.StartsWith(ViewportPrefix, StringComparison.Ordinal))
            .Select(argument => argument[ViewportPrefix.Length..])
            .ToArray();
        int runnerOptionCount = arguments.Count(argument => argument == CiRunnerOption);
        if (runnerOptionCount > 1)
        {
            throw new InvalidOperationException($"{CiRunnerOption} may be specified only once.");
        }
        if (values.Length == 0)
        {
            if (runnerOptionCount != 0)
            {
                throw new InvalidOperationException($"{CiRunnerOption} requires {ViewportPrefix}1024x684.");
            }
            return null;
        }
        if (values.Length != 1)
        {
            throw new InvalidOperationException("--ci-visual-viewport may be specified only once.");
        }

        string[] parts = values[0].Split('x', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out int width) ||
            !int.TryParse(parts[1], out int height))
        {
            throw new InvalidOperationException("--ci-visual-viewport must be WIDTHxHEIGHT.");
        }
        var requested = new AnimeSliceViewportSize(width, height);
        if (runnerOptionCount == 1)
        {
            if (requested != CiRunnerViewport)
            {
                throw new InvalidOperationException($"{CiRunnerOption} permits only 1024x684.");
            }
            return requested;
        }
        if (!ProductViewports.Contains(requested))
        {
            throw new InvalidOperationException(
                "AnimeV1 screenshots support 1280x720, 1600x900, 2560x1440, or 2560x1600.");
        }
        return requested;
    }
}
