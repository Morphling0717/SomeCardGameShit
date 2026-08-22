// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.Client;

public static class ScgsV04Contract
{
    public const uint AbiVersion = 0x0001_0000U;
    public const uint SchemaVersion = 1U;
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
    UnitZoneFull = 10,
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
    LeaderSkillLocked = 28,
    LeaderSkillAlreadyUsed = 29,
    MatchAlreadyStarted = 30,
    MatchNotStarted = 31,
    MulliganAlreadyDone = 32,
    DuplicateSelection = 33,
    GameOver = 34,
    StaleRevision = 35,
}
