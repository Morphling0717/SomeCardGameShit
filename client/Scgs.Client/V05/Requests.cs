// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json.Serialization;

namespace Scgs.Client.V05;

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
    public ulong? Permanent { get; init; }

    public static Target Leader(PlayerId player) => new(TargetKind.Leader, player);

    public static Target PermanentTarget(PlayerId player, ulong permanent) =>
        new(TargetKind.Permanent, player) { Permanent = permanent };
}

public sealed record GameCommandRequest(
    [property: JsonRequired] PlayerId Player,
    [property: JsonRequired] ActionKind Action,
    [property: JsonRequired] ulong ExpectedRevision)
{
    public ulong Source { get; init; }

    public Target? Target { get; init; }

    public ulong? Slot { get; init; }

    public string? ModeId { get; init; }

    public string? ChoiceId { get; init; }

    public bool UseAdvance { get; init; }

    public IReadOnlyList<ulong> MulliganCards { get; init; } = Array.Empty<ulong>();

    public IReadOnlyList<string> SelectedOptionIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ulong> AdditionalCostCards { get; init; } = Array.Empty<ulong>();
}

public sealed record ActionQueryRequest(PlayerId Player, ulong ExpectedRevision)
{
    public ActionKind? Action { get; init; }

    public ulong? Source { get; init; }

    public Target? Target { get; init; }

    public ulong? Slot { get; init; }

    public string? ModeId { get; init; }

    public string? ChoiceId { get; init; }

    public bool? UseAdvance { get; init; }

    public IReadOnlyList<ulong>? MulliganCards { get; init; }

    public IReadOnlyList<string>? SelectedOptionIds { get; init; }

    public IReadOnlyList<ulong>? AdditionalCostCards { get; init; }
}
