// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;
using Scgs.GodotClient.Match;

namespace Scgs.GodotClient.Ci;

/// <summary>
/// Historical R3 entrypoint kept only to fail closed for stale internal callers.
/// It must not create sessions, load retired artwork, or emit old acceptance evidence.
/// </summary>
internal sealed class GateR3VisualSlice
{
    internal GateR3VisualSlice(
        Node root,
        MatchScreen match,
        IScgsGameSession session,
        string outputDirectory,
        Func<Task> nextFrame)
    {
        throw new NotSupportedException(
            "The industrial R3 visual slice is retired. Use the AnimeV1 product v05 visual suite.");
    }

    internal Task<string> RunAsync() => throw new NotSupportedException(
        "The industrial R3 visual slice is retired; no legacy assets or session will be loaded.");
}
