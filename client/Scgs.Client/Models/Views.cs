// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.Client;

public sealed class EffectRecord
{
    public required EffectTrigger Trigger { get; init; }

    public required EffectKind Kind { get; init; }

    public required int Amount { get; init; }

    public required TargetSpec TargetSpec { get; init; }
}

public sealed class ComponentSpec
{
    public required bool HasComponent { get; init; }

    public required EffectKind GrantedKind { get; init; }

    public required int GrantedAmount { get; init; }
}

public sealed class DeploymentSpec
{
    public required DeploymentCondition Condition { get; init; }

    public required int ConditionAmount { get; init; }

    public required int PpCost { get; init; }

    public required bool ArchiveOneFriendlyUnit { get; init; }
}

public sealed class AdditionalCost
{
    public required int BurnPpCapacity { get; init; }
}

public sealed class CardDefinition
{
    public required uint Id { get; init; }

    public required string Name { get; init; }

    public required CardKind Kind { get; init; }

    public required int Cost { get; init; }

    public required int Attack { get; init; }

    public required int Health { get; init; }

    public required int Countdown { get; init; }

    public required bool PrintedGuard { get; init; }

    public required bool PrintedRush { get; init; }

    public required bool PrintedStorm { get; init; }

    public required bool PrintedBarrier { get; init; }

    public required bool PrintedLifesteal { get; init; }

    public required bool PrintedBane { get; init; }

    public required int EvolvedAttack { get; init; }

    public required int EvolvedHealth { get; init; }

    public required AdditionalCost AdditionalCost { get; init; }

    public DeploymentSpec? Deployment { get; init; }

    public required ComponentSpec Component { get; init; }

    public required EffectRecord[] Effects { get; init; }
}

public sealed class LeaderSkillDefinition
{
    public required string Name { get; init; }

    public required int Cost { get; init; }

    public required EffectRecord[] Effects { get; init; }
}

public sealed class CardView
{
    public ulong? InstanceId { get; init; }

    public uint? DefinitionId { get; init; }

    public CardDefinition? Definition { get; init; }

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

    public required Keyword Keywords { get; init; }

    public required bool Evolved { get; init; }

    public required bool AttackedThisTurn { get; init; }

    public required bool EnteredThisTurn { get; init; }

    public required bool TemporaryRush { get; init; }

    public required bool DeployedFromStandby { get; init; }

    public required bool FaceDown { get; init; }

    public required int Countdown { get; init; }

    public required ComponentSpec GrantedComponent { get; init; }
}

public sealed class PlayerView
{
    public required PlayerId Player { get; init; }

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

    public required bool LeaderSkillUsed { get; init; }

    public required bool ChargeGrantedThisCycle { get; init; }

    public required int FriendlyDeathsThisCycle { get; init; }

    public required int SpellsUsedThisTurn { get; init; }

    public required int UnitsPlayedThisTurn { get; init; }

    public required LeaderSkillDefinition LeaderSkill { get; init; }

    public required ulong DeckCount { get; init; }

    public required ulong HandCount { get; init; }

    public required CardView[] Hand { get; init; }

    public required CardView?[] Units { get; init; }

    public required CardView?[] Tactics { get; init; }

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

    public required ulong Depth { get; init; }

    public required ulong EligibleCount { get; init; }

    public required CardView[] EligibleTraps { get; init; }

    public required ulong Revision { get; init; }
}

public sealed class MatchView
{
    public required PlayerId Viewer { get; init; }

    public required PlayerId ActivePlayer { get; init; }

    public required PlayerId FirstPlayer { get; init; }

    public required uint RandomSeed { get; init; }

    public required MatchPhase Phase { get; init; }

    public required GameResult Result { get; init; }

    public required ulong Revision { get; init; }

    public required PlayerView[] Players { get; init; }

    public required ReactionContext Reaction { get; init; }
}
