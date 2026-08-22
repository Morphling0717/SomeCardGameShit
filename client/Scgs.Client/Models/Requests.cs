// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json.Serialization;

namespace Scgs.Client;

public sealed record GameConfigRequest(string Player0Deck, string Player1Deck)
{
    public uint? RandomSeed { get; init; }

    public FirstPlayerMode FirstPlayerMode { get; init; } = FirstPlayerMode.Random;

    public bool ShuffleDecks { get; init; } = true;
}

public sealed record Target(
    [property: JsonRequired] TargetKind Kind,
    [property: JsonRequired] PlayerId Player)
{
    public ulong? Unit { get; init; }

    public static Target Leader(PlayerId player) => new(TargetKind.Leader, player);

    public static Target UnitTarget(PlayerId player, ulong unit) =>
        new(TargetKind.Unit, player) { Unit = unit };
}

public sealed record GameCommandRequest(
    [property: JsonRequired] PlayerId Player,
    [property: JsonRequired] ActionKind Action,
    [property: JsonRequired] ulong ExpectedRevision)
{
    [JsonRequired]
    public ulong Source { get; init; }

    public Target? Target { get; init; }

    public ulong? Slot { get; init; }

    public ulong? ComponentDonor { get; init; }

    [JsonRequired]
    public bool UseAdvance { get; init; }

    [JsonRequired]
    public IReadOnlyList<ulong> MulliganCards { get; init; } = Array.Empty<ulong>();
}

public sealed record ActionQueryRequest(PlayerId Player, ulong ExpectedRevision)
{
    public ActionKind? Action { get; init; }

    public ulong? Source { get; init; }

    public Target? Target { get; init; }

    public ulong? Slot { get; init; }

    public ulong? ComponentDonor { get; init; }

    public bool? UseAdvance { get; init; }

    public IReadOnlyList<ulong>? MulliganCards { get; init; }
}
