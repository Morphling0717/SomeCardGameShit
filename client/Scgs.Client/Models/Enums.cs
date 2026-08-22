// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.Client;

public enum PlayerId : uint
{
    Player0 = 0,
    Player1 = 1,
}

public enum FirstPlayerMode : uint
{
    Random = 0,
    Player0 = 1,
    Player1 = 2,
}

public enum ActionKind : uint
{
    Mulligan = 0,
    PlayUnit = 1,
    CastSpell = 2,
    PlayTactic = 3,
    Attack = 4,
    Evolve = 5,
    Deploy = 6,
    ActivateTrap = 7,
    PassReaction = 8,
    EndTurn = 9,
    Surrender = 10,
}

public enum TargetKind : uint
{
    Leader = 0,
    Unit = 1,
}

public enum CardKind : uint
{
    Unit = 0,
    Spell = 1,
    Relic = 2,
    Trap = 3,
}

public enum Zone : uint
{
    None = 0,
    Deck = 1,
    Hand = 2,
    Unit = 3,
    Tactic = 4,
    Graveyard = 5,
    Archive = 6,
    Standby = 7,
}

public enum MatchPhase : uint
{
    NotStarted = 0,
    Mulligan = 1,
    Action = 2,
    Reaction = 3,
    Finished = 4,
}

public enum ReactionWindow : uint
{
    None = 0,
    SpellDeclared = 1,
    EntryEffectPending = 2,
    AttackDeclared = 3,
}

public enum GameResult : uint
{
    Ongoing = 0,
    Player0Won = 1,
    Player1Won = 2,
    Draw = 3,
}

public enum EffectTrigger : uint
{
    OnPlay = 0,
    OnPlayIfAdvanced = 1,
    OnPlayIfNotAdvanced = 2,
    OnEntry = 3,
    OnEvolution = 4,
    OnLastWords = 5,
    OnCountdownExpire = 6,
    OnSpellDeclared = 7,
    OnAttackDeclared = 8,
    OnEntryEffectPending = 9,
}

public enum EffectKind : uint
{
    DrawCards = 0,
    DealDamageToEnemyUnit = 1,
    DealDamageToLeader = 2,
    HealLeader = 3,
    RepairCracks = 4,
    GainPpCapacity = 5,
    BuffFriendlyUnit = 6,
    GrantRush = 7,
    CancelAttack = 8,
    DamageEnteredUnit = 9,
}

public enum TargetSpec : uint
{
    None = 0,
    EnemyUnit = 1,
    FriendlyUnit = 2,
}

public enum DeploymentCondition : uint
{
    None = 0,
    FriendlyUnitsMin = 1,
    SpellsThisTurnMin = 2,
}

public enum EventType : uint
{
    MatchStarted = 0,
    TurnStarted = 1,
    TurnEnded = 2,
    CardDrawn = 3,
    FatigueDamage = 4,
    HandOverflowArchived = 5,
    PpChanged = 6,
    CracksChanged = 7,
    CardMoved = 8,
    UnitEntered = 9,
    UnitDamaged = 10,
    LeaderDamaged = 11,
    LeaderHealed = 12,
    UnitDestroyed = 13,
    AttackDeclared = 14,
    AttackCancelled = 15,
    UnitEvolved = 16,
    EvolutionEnergyChanged = 17,
    UnitDeployed = 18,
    TrapWindowOpened = 19,
    TrapActivated = 20,
    LeaderSkillUsed = 21,
    PlayerSurrendered = 22,
    MatchEnded = 23,
    MulliganCompleted = 24,
}

[Flags]
public enum Keyword : uint
{
    None = 0x0000_0000,
    Guard = 0x0000_0001,
    Rush = 0x0000_0002,
    Storm = 0x0000_0004,
    Barrier = 0x0000_0008,
    Bane = 0x0000_0010,
    Lifesteal = 0x0000_0020,
    Ambush = 0x0000_0040,
}
