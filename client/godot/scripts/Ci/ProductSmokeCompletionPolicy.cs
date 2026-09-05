// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.GodotClient.Ci;

/// <summary>Pure scheduling policy; never creates a board or substitutes for GPU evidence.</summary>
internal static class ProductSmokeCompletionPolicy
{
    internal const int MaximumMatches = 12;

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
