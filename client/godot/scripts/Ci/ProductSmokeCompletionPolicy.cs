// SPDX-License-Identifier: GPL-3.0-or-later
using V05 = Scgs.Client.V05;

namespace Scgs.GodotClient.Ci;

/// <summary>Pure scheduling policy; never creates a board or substitutes for GPU evidence.</summary>
internal static class ProductSmokeCompletionPolicy
{
    internal const int MaximumMatches = 12;

    internal static bool ShouldAccumulateBoard(bool fullUi, bool requirePerformance, bool performanceCompleted,
        IReadOnlyList<int> actionCounts, bool reactionSurrenderCovered, bool choiceSurrenderCovered) =>
        fullUi && requirePerformance && !performanceCompleted && actionCounts.Count == 14 &&
        actionCounts.All(count => count > 0) && reactionSurrenderCovered && choiceSurrenderCovered;

    internal static int? AccumulationPriority(V05.GameCommandRequest command)
    {
        // These are already validated, viewer-safe legal commands. Do not
        // sacrifice existing permanents or proactively select an enemy target
        // while building a real board for performance measurement.
        if (command.AdditionalCostCards.Count != 0 ||
            command.Target is { } target && target.Player != command.Player) return null;
        return command.Action switch
        {
            V05.ActionKind.PlayUnit => 0,
            V05.ActionKind.PlayAmulet => 1,
            V05.ActionKind.Deploy => 2,
            V05.ActionKind.EndTurn => 3,
            V05.ActionKind.PassReaction => 0,
            _ => null,
        };
    }

    internal static ProductSmokeCompletionDecision Evaluate(bool fullUi, bool allNaturalActions,
        bool reactionSurrenderCovered, bool choiceSurrenderCovered, bool requirePerformance,
        bool performanceCompleted, int completedMatches)
    {
        if (completedMatches is < 1 or > MaximumMatches)
            throw new ArgumentOutOfRangeException(nameof(completedMatches));
        bool coverageComplete = !fullUi || allNaturalActions &&
            reactionSurrenderCovered && choiceSurrenderCovered;
        bool canComplete = coverageComplete && (!requirePerformance || performanceCompleted);
        // Once both surrender paths have real evidence, remaining fixed-seed
        // games must run naturally so they can reach a genuine heavy board.
        bool seekSurrender = fullUi && allNaturalActions &&
            (!reactionSurrenderCovered || !choiceSurrenderCovered);
        return new(canComplete, seekSurrender, !canComplete && completedMatches < MaximumMatches);
    }
}

internal readonly record struct ProductSmokeCompletionDecision(bool CanComplete, bool SeekSurrender, bool CanRestart);
