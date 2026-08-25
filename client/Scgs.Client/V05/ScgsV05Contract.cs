// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.Client.V05;

public static class ScgsV05Contract
{
    public const uint AbiVersion = 0x0002_0000U;
    public const uint SchemaVersion = 2U;
    public const uint NoEngineCode = 0xFFFF_FFFFU;
    public const int MaximumInputBytes = 1024 * 1024;
}

public enum NativeCode : uint
{
    Ok = 0,
    InvalidArgument = 1,
    AbiMismatch = 2,
    InvalidHandle = 3,
    InvalidUtf8 = 4,
    InvalidJson = 5,
    SchemaMismatch = 6,
    BufferTooSmall = 7,
    PayloadTooLarge = 8,
    OutOfMemory = 9,
    InternalError = 10,
}

public enum EngineCode : uint
{
    Ok = 0,
    InvalidPhase = 1,
    NotActivePlayer = 2,
    InvalidPlayer = 3,
    InvalidCard = 4,
    InvalidZone = 5,
    InvalidTarget = 6,
    InvalidSlot = 7,
    InsufficientPp = 8,
    HandLimit = 9,
    MainBoardFull = 10,
    TacticZoneFull = 11,
    SummoningSickness = 12,
    AlreadyAttacked = 13,
    GuardBlocksTarget = 14,
    EvolutionLocked = 15,
    NoEvolutionPoints = 16,
    EvolutionAlreadyUsed = 17,
    AlreadyEvolved = 18,
    AdvanceAlreadyUsed = 19,
    AdvanceWouldExceedCap = 20,
    DeployAlreadyUsed = 21,
    DeployConditionNotMet = 22,
    InvalidDeployment = 23,
    ResponseDepthExceeded = 24,
    TrapAlreadySetThisTurn = 25,
    NoPendingReaction = 26,
    TrapNotEligible = 27,
    ReservedLeaderSkillLocked = 28,
    ReservedLeaderSkillAlreadyUsed = 29,
    MatchAlreadyStarted = 30,
    MatchNotStarted = 31,
    MulliganAlreadyDone = 32,
    DuplicateSelection = 33,
    GameOver = 34,
    StaleRevision = 35,
    ChoicePending = 36,
    NoPendingChoice = 37,
    InvalidChoice = 38,
    ChoiceNotOwned = 39,
    InvalidMode = 40,
    InvalidAdditionalCost = 41,
}

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
    PlayTrap = 3,
    Attack = 4,
    Evolve = 5,
    Deploy = 6,
    ActivateTrap = 7,
    PassReaction = 8,
    EndTurn = 9,
    Surrender = 10,
    PlayAmulet = 11,
    PlayField = 12,
    ResolveChoice = 13,
}

public enum TargetKind : uint
{
    Leader = 0,
    Permanent = 1,
}

public enum CardKind : uint
{
    Follower = 0,
    Spell = 1,
    Amulet = 2,
    Trap = 3,
    Field = 4,
}

public enum Zone : uint
{
    None = 0,
    Deck = 1,
    Hand = 2,
    MainBoard = 3,
    Tactic = 4,
    Graveyard = 5,
    Archive = 6,
    Standby = 7,
    Field = 8,
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

public enum PendingChoiceKind : uint
{
    Mode = 0,
    Cards = 1,
    TriggerOrder = 2,
    AdditionalCost = 3,
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
    PermanentEntered = 9,
    PermanentDamaged = 10,
    LeaderDamaged = 11,
    LeaderHealed = 12,
    PermanentDestroyed = 13,
    AttackDeclared = 14,
    AttackCancelled = 15,
    FollowerEvolved = 16,
    EvolutionEnergyChanged = 17,
    CardDeployed = 18,
    ReactionWindowOpened = 19,
    TrapActivated = 20,
    ReservedLeaderSkillUsed = 21,
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

public sealed class ScgsV05NativeException : Exception
{
    public ScgsV05NativeException(uint rawCode, string diagnostic)
        : base(string.IsNullOrWhiteSpace(diagnostic)
            ? $"The scgs_v05 native call failed with code {rawCode}."
            : diagnostic)
    {
        RawCode = rawCode;
    }

    public uint RawCode { get; }

    public NativeCode Code => (NativeCode)RawCode;

    public bool IsKnown => RawCode <= (uint)NativeCode.InternalError;
}

public sealed class ScgsV05AbiMismatchException : Exception
{
    public ScgsV05AbiMismatchException(uint requested, uint reported)
        : base($"The scgs_v05 ABI is incompatible: requested 0x{requested:X8}, reported 0x{reported:X8}.")
    {
        Requested = requested;
        Reported = reported;
    }

    public uint Requested { get; }

    public uint Reported { get; }
}
