// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.GodotClient.Preview;

internal sealed record AnimeCardBodySliceLaunch(
    bool Requested,
    string? OutputDirectory,
    bool ExitWhenComplete,
    string InitialState)
{
    internal const string Option = "--anime-card-body-slice";
    internal const string OutputPrefix = "--anime-card-body-slice=";
    internal const string ExitOption = "--anime-card-body-slice-exit";
    internal const string StatePrefix = "--anime-card-body-state=";
    internal const string StateContact = "contact-sheet";
    internal const string StateRepresentatives = "representatives";
    internal const string StateContexts = "contexts";
    internal const string StateHandOne = "hand-one";
    internal const string StateHandFive = "hand-five";
    internal const string StateHandTen = "hand-ten";
    internal const string StateHandHover = "hand-hover";
    internal const string StateValues = "values";

    internal static IReadOnlyList<string> States { get; } =
    [
        StateContact,
        StateRepresentatives,
        StateContexts,
        StateHandOne,
        StateHandFive,
        StateHandTen,
        StateHandHover,
        StateValues,
    ];

    internal static AnimeCardBodySliceLaunch Parse(IReadOnlyList<string> arguments)
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
                    "Anime card-body exit and state options require --anime-card-body-slice.");
            }
            return new AnimeCardBodySliceLaunch(false, null, false, StateRepresentatives);
        }
        if (modes.Length != 1 || exitCount > 1 || states.Length > 1)
        {
            throw new InvalidOperationException(
                "Anime card-body mode, exit and state options may each be specified only once.");
        }
        if (arguments.Any(argument =>
                argument == "--anime-style-slice" ||
                argument.StartsWith("--anime-style-slice=", StringComparison.Ordinal) ||
                argument == "--ci-smoke" ||
                argument == "--legacy-2d-board" ||
                argument == "--r3-visual-slice" ||
                argument.StartsWith("--r3-visual-slice=", StringComparison.Ordinal) ||
                argument.StartsWith("--ci-visual-suite=", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "--anime-card-body-slice is a standalone no-native approval mode.");
        }

        string state = states.Length == 0 ? StateRepresentatives : states[0];
        if (!States.Contains(state, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Unknown AnimeV1 card-body state '{state}'.");
        }

        string? outputDirectory = null;
        if (modes[0] != Option)
        {
            string requested = modes[0][OutputPrefix.Length..];
            if (string.IsNullOrWhiteSpace(requested) || !Path.IsPathFullyQualified(requested))
            {
                throw new InvalidOperationException(
                    "--anime-card-body-slice=<directory> requires an absolute output directory.");
            }
            outputDirectory = Path.GetFullPath(requested);
        }
        if (exitCount == 1 && outputDirectory is null)
        {
            throw new InvalidOperationException(
                "--anime-card-body-slice-exit requires the screenshot form.");
        }

        return new AnimeCardBodySliceLaunch(true, outputDirectory, exitCount == 1, state);
    }
}
