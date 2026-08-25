// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.Client.V05;

public sealed class CardView
{
    public ulong? InstanceId { get; init; }

    public string? DesignId { get; init; }

    public string? ProfessionId { get; init; }

    public string? SeriesId { get; init; }

    public bool? Neutral { get; init; }

    public CardKind? Kind { get; init; }

    public required string Name { get; init; }

    public required PlayerId Owner { get; init; }

    public required PlayerId Controller { get; init; }

    public required Zone Zone { get; init; }

    public required ulong Sequence { get; init; }

    public required int Cost { get; init; }

    public required int CurrentAttack { get; init; }

    public required int CurrentHealth { get; init; }

    public required int MaximumHealth { get; init; }

    public required Keyword PrintedKeywords { get; init; }

    public required Keyword PermanentKeywords { get; init; }

    public required Keyword TurnKeywords { get; init; }

    public required Keyword Keywords { get; init; }

    public required bool Evolved { get; init; }

    public required bool AttackedThisTurn { get; init; }

    public required bool EnteredThisTurn { get; init; }

    public required bool FaceDown { get; init; }

    public required int Countdown { get; init; }
}

public sealed class PlayerView
{
    public required PlayerId Player { get; init; }

    public required string ProfessionId { get; init; }

    public required int LeaderHealth { get; init; }

    public required int MaximumLeaderHealth { get; init; }

    public required int CurrentPp { get; init; }

    public required int PpCapacity { get; init; }

    public required int Cracks { get; init; }

    public required int EvolutionEnergy { get; init; }

    public required int OwnTurnNumber { get; init; }

    public required int FatigueCount { get; init; }

    public required bool MulliganDone { get; init; }

    public required bool EvolutionUsedThisTurn { get; init; }

    public required bool AdvanceUsedThisTurn { get; init; }

    public required bool DeployUsedThisTurn { get; init; }

    public required bool TrapSetThisTurn { get; init; }

    public required ulong DeckCount { get; init; }

    public required ulong HandCount { get; init; }

    public required CardView[] Hand { get; init; }

    public required CardView?[] MainBoard { get; init; }

    public required CardView?[] Tactics { get; init; }

    public CardView? Field { get; init; }

    public required CardView[] Graveyard { get; init; }

    public required CardView[] Archive { get; init; }

    public required CardView[] Standby { get; init; }
}

public sealed class ReactionContext
{
    public required bool Pending { get; init; }

    public required ReactionWindow Window { get; init; }

    public required PlayerId Responder { get; init; }

    public required ulong Subject { get; init; }

    public ReactionOrigin? Origin { get; init; }

    public required ulong Depth { get; init; }

    public required ulong EligibleCount { get; init; }

    public required CardView[] EligibleTraps { get; init; }

    public required ulong Revision { get; init; }
}

public sealed class ReactionOrigin
{
    public required ActionKind Action { get; init; }

    public required PlayerId Player { get; init; }

    public required ulong Source { get; init; }

    public Target? Target { get; init; }
}

public sealed class PendingChoiceOptionView
{
    public required string OptionId { get; init; }

    public string? Label { get; init; }

    public CardView? Card { get; init; }
}

public sealed class PendingChoiceView
{
    public required bool Pending { get; init; }

    public PlayerId? Chooser { get; init; }

    public string? ChoiceId { get; init; }

    public PendingChoiceKind? Kind { get; init; }

    public ulong? MinimumSelections { get; init; }

    public ulong? MaximumSelections { get; init; }

    public bool? Ordered { get; init; }

    public PendingChoiceOptionView[] Options { get; init; } = Array.Empty<PendingChoiceOptionView>();

    public required ulong Revision { get; init; }
}

public sealed class MatchView
{
    public required PlayerId Viewer { get; init; }

    public required PlayerId ActivePlayer { get; init; }

    public required PlayerId FirstPlayer { get; init; }

    public required MatchPhase Phase { get; init; }

    public required GameResult Result { get; init; }

    public required ulong Revision { get; init; }

    public required PlayerView[] Players { get; init; }

    public required ReactionContext Reaction { get; init; }

    public required PendingChoiceView PendingChoice { get; init; }
}
