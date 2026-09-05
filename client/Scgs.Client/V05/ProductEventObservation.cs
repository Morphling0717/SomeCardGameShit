// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json.Serialization;

namespace Scgs.Client.V05;

/// <summary>
/// An occurrence-time, viewer-safe fact, not a reconstructed rule result. Unknown
/// kinds must use a generic presentation; unsupported versions are rejected.
/// </summary>
public sealed class ProductEventObservation
{
    public required uint Version { get; init; }
    public required ulong Revision { get; init; }
    public required string Kind { get; init; }
    public required ulong CauseSequence { get; init; }

    // Native freezes this at occurrence time. Do not infer public visibility from
    // a card's current zone, or retain private observations across viewer changes.
    public required bool PublicToAll { get; init; }
    public EventObservationEndpoint? Source { get; init; }
    public EventObservationEndpoint? Subject { get; init; }
    public EventObservationEndpoint? Target { get; init; }
    public EventObservationLocation? From { get; init; }
    public EventObservationLocation? To { get; init; }
    public string? MoveReason { get; init; }
    public int? ActualAmount { get; init; }
    public string? DamageKind { get; init; }
    public string? DeclarationKind { get; init; }
    public bool? BarrierConsumed { get; init; }
    public EventObservationState? Before { get; init; }
    public EventObservationState? After { get; init; }

    [JsonIgnore]
    public bool IsKnownKind => Kind is
        "move" or "damage" or "heal" or "evolve" or "state_change" or "declaration";
}

/// <summary>
/// Leader endpoints never carry card identities. Hidden card endpoints never
/// carry stable identifiers or definition-derived information.
/// </summary>
public sealed class EventObservationEndpoint
{
    public required string Kind { get; init; }
    public required PlayerId Player { get; init; }
    public required bool Hidden { get; init; }
    public ulong? Card { get; init; }
    public string? DesignId { get; init; }
}

public sealed class EventObservationLocation
{
    public required PlayerId Player { get; init; }
    public required Zone Zone { get; init; }

    // Only MainBoard and Tactic use slots. In particular a private hand/deck
    // position must never be serialized as a presentation location.
    public ulong? Slot { get; init; }
}

public sealed class EventObservationState
{
    public int? Health { get; init; }
    public int? MaxHealth { get; init; }
    public int? Attack { get; init; }
    public int? Countdown { get; init; }
    public bool? Evolved { get; init; }

    // Keep unknown keyword bits just as in CardView.
    public Keyword? Keywords { get; init; }
}
