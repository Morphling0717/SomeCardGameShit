// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.Client;

public interface IScgsEnvelope
{
    uint SchemaVersion { get; }

    ulong Revision { get; }
}

public sealed class ViewEnvelope : IScgsEnvelope
{
    public required uint SchemaVersion { get; init; }

    public required ulong Revision { get; init; }

    public required MatchView View { get; init; }
}

public sealed class ActionsEnvelope : IScgsEnvelope
{
    public required uint SchemaVersion { get; init; }

    public required ulong Revision { get; init; }

    public required LegalAction[] Actions { get; init; }
}

public sealed class TargetsEnvelope : IScgsEnvelope
{
    public required uint SchemaVersion { get; init; }

    public required ulong Revision { get; init; }

    public required Target[] Targets { get; init; }
}

public sealed class SlotsEnvelope : IScgsEnvelope
{
    public required uint SchemaVersion { get; init; }

    public required ulong Revision { get; init; }

    public required ulong[] Slots { get; init; }
}

public sealed class DonorsEnvelope : IScgsEnvelope
{
    public required uint SchemaVersion { get; init; }

    public required ulong Revision { get; init; }

    public required ulong[] Donors { get; init; }
}

public sealed class PaymentEnvelope : IScgsEnvelope
{
    public required uint SchemaVersion { get; init; }

    public required ulong Revision { get; init; }

    public required PaymentPreview Payment { get; init; }
}

public sealed class ReactionEnvelope : IScgsEnvelope
{
    public required uint SchemaVersion { get; init; }

    public required ulong Revision { get; init; }

    public required ReactionContext Reaction { get; init; }
}

public sealed class EventsEnvelope : IScgsEnvelope
{
    public required uint SchemaVersion { get; init; }

    public required ulong Revision { get; init; }

    public required ulong LastSequence { get; init; }

    public required GameEventView[] Events { get; init; }
}
