// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json.Serialization;

namespace Scgs.Client;

public sealed class EngineStatus
{
    [JsonPropertyName("engine_code")]
    public required uint RawCode { get; init; }

    public required string Message { get; init; }

    [JsonIgnore]
    public uint EngineCode => RawCode;

    [JsonIgnore]
    public EngineCode Code => (EngineCode)RawCode;

    [JsonIgnore]
    public bool IsSuccess => RawCode == (uint)Scgs.Client.EngineCode.Ok;

    [JsonIgnore]
    public bool IsKnown => RawCode <= (uint)Scgs.Client.EngineCode.StaleRevision;
}

public sealed class PaymentPreview
{
    public required EngineStatus Status { get; init; }

    public required int CurrentPpBefore { get; init; }

    public required int CurrentPpAfter { get; init; }

    public required int PpCapacityBefore { get; init; }

    public required int PpCapacityAfter { get; init; }

    public required int CracksBefore { get; init; }

    public required int CracksAfter { get; init; }

    public required int EvolutionEnergyBefore { get; init; }

    public required int EvolutionEnergyAfter { get; init; }

    public required int BaseCost { get; init; }

    public required int BurnCost { get; init; }

    public required int AdvanceCost { get; init; }

    public required bool UsedAdvance { get; init; }
}

public sealed class LegalAction
{
    public required GameCommandRequest Command { get; init; }

    public required PaymentPreview Payment { get; init; }
}

public sealed class GameEventView
{
    public required ulong Sequence { get; init; }

    public required EventType Type { get; init; }

    public required PlayerId Player { get; init; }

    public ulong? Card { get; init; }

    public uint? DefinitionId { get; init; }

    public required int Value { get; init; }

    public required int SecondaryValue { get; init; }

    public required bool HiddenCard { get; init; }

    public required string Text { get; init; }

    public uint? RandomSeed { get; init; }

    public PlayerId? FirstPlayer { get; init; }
}

public sealed record LegalActionsResult(ulong Revision, IReadOnlyList<LegalAction> Actions);

public sealed record ValidTargetsResult(ulong Revision, IReadOnlyList<Target> Targets);

public sealed record ValidSlotsResult(ulong Revision, IReadOnlyList<ulong> Slots);

public sealed record ValidDonorsResult(ulong Revision, IReadOnlyList<ulong> Donors);

public sealed record PaymentResult(ulong Revision, PaymentPreview Payment);

public sealed record EventBatch(
    ulong Revision,
    ulong LastSequence,
    IReadOnlyList<GameEventView> Events);
