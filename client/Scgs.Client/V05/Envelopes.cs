// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.Client.V05;

internal interface IScgsV05Envelope
{
    uint SchemaVersion { get; }

    ulong Revision { get; }
}

internal sealed class ViewEnvelope : IScgsV05Envelope
{
    public required uint SchemaVersion { get; init; }

    public required ulong Revision { get; init; }

    public required MatchView View { get; init; }
}

internal sealed class ActionsEnvelope : IScgsV05Envelope
{
    public required uint SchemaVersion { get; init; }

    public required ulong Revision { get; init; }

    public required LegalAction[] Actions { get; init; }
}

internal sealed class TargetsEnvelope : IScgsV05Envelope
{
    public required uint SchemaVersion { get; init; }

    public required ulong Revision { get; init; }

    public required Target[] Targets { get; init; }
}

internal sealed class SlotsEnvelope : IScgsV05Envelope
{
    public required uint SchemaVersion { get; init; }

    public required ulong Revision { get; init; }

    public required ulong[] Slots { get; init; }
}

internal sealed class DonorsEnvelope : IScgsV05Envelope
{
    public required uint SchemaVersion { get; init; }

    public required ulong Revision { get; init; }

    public required ulong[] Donors { get; init; }
}

internal sealed class PaymentEnvelope : IScgsV05Envelope
{
    public required uint SchemaVersion { get; init; }

    public required ulong Revision { get; init; }

    public required PaymentPreview Payment { get; init; }
}

internal sealed class ReactionEnvelope : IScgsV05Envelope
{
    public required uint SchemaVersion { get; init; }

    public required ulong Revision { get; init; }

    public required ReactionContext Reaction { get; init; }

    public required PendingChoiceView PendingChoice { get; init; }
}

internal sealed class EventsEnvelope : IScgsV05Envelope
{
    public required uint SchemaVersion { get; init; }

    public required ulong Revision { get; init; }

    public required ulong LastSequence { get; init; }

    public required GameEventView[] Events { get; init; }
}
