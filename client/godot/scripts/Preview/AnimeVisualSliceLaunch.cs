// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

namespace Scgs.GodotClient.Preview;

internal sealed record AnimeVisualSliceLaunch(
    bool Requested,
    string? OutputDirectory,
    bool ExitWhenComplete,
    string InitialState)
{
    internal const string Option = "--anime-style-slice";
    internal const string OutputPrefix = "--anime-style-slice=";
    internal const string ExitOption = "--anime-style-slice-exit";
    internal const string StatePrefix = "--anime-style-state=";

    internal static AnimeVisualSliceLaunch Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string[] modes = arguments
            .Where(argument => argument == Option ||
                               argument.StartsWith(OutputPrefix, StringComparison.Ordinal))
            .ToArray();
        int exitCount = arguments.Count(argument => argument == ExitOption);
        string[] states = arguments
            .Where(argument => argument.StartsWith(StatePrefix, StringComparison.Ordinal))
            .Select(argument => argument[StatePrefix.Length..])
            .ToArray();

        if (modes.Length == 0)
        {
            if (exitCount != 0 || states.Length != 0)
            {
                throw new InvalidOperationException(
                    "--anime-style-slice-exit and --anime-style-state require --anime-style-slice.");
            }
            return new AnimeVisualSliceLaunch(false, null, false, AnimeStyleSliceScreen.StateMenu);
        }
        if (modes.Length != 1)
        {
            throw new InvalidOperationException("--anime-style-slice may be specified only once.");
        }
        if (exitCount > 1 || states.Length > 1)
        {
            throw new InvalidOperationException(
                "Anime visual-slice exit and state options may be specified only once.");
        }
        if (arguments.Any(argument =>
                argument == "--ci-smoke" ||
                argument == "--legacy-2d-board" ||
                argument == "--r3-visual-slice" ||
                argument.StartsWith("--r3-visual-slice=", StringComparison.Ordinal) ||
                argument.StartsWith("--ci-visual-suite=", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "--anime-style-slice is a standalone no-native preview and cannot be combined with product, legacy, R3, or Gate 4B CI modes.");
        }

        string initialState = states.Length == 0 ? AnimeStyleSliceScreen.StateMenu : states[0];
        if (!AnimeStyleSliceScreen.States.Contains(initialState, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unknown anime visual-slice state '{initialState}'.");
        }

        string? outputDirectory = null;
        if (modes[0] != Option)
        {
            string requested = modes[0][OutputPrefix.Length..];
            if (string.IsNullOrWhiteSpace(requested) || !Path.IsPathFullyQualified(requested))
            {
                throw new InvalidOperationException(
                    "--anime-style-slice=<directory> requires one absolute output directory.");
            }
            outputDirectory = Path.GetFullPath(requested);
        }
        if (exitCount == 1 && outputDirectory is null)
        {
            throw new InvalidOperationException(
                "--anime-style-slice-exit requires the screenshot form --anime-style-slice=<directory>.");
        }

        return new AnimeVisualSliceLaunch(true, outputDirectory, exitCount == 1, initialState);
    }
}
