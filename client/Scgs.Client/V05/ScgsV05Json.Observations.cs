// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.Client.V05;

internal static partial class ScgsV05Json
{
    private static void ValidateObservation(
        ProductEventObservation observation,
        GameEventView gameEvent,
        ulong envelopeRevision)
    {
        if (observation.Version != 1)
        {
            throw new ScgsProtocolException("Unsupported v05 event observation version.");
        }

        if (observation.Revision > envelopeRevision ||
            observation.CauseSequence > gameEvent.Sequence)
        {
            throw new ScgsProtocolException("A v05 observation refers to a future revision or cause.");
        }

        RequireObservationToken(observation.Kind, "kind");
        ValidateObservationEndpoint(observation.Source);
        ValidateObservationEndpoint(observation.Subject);
        ValidateObservationEndpoint(observation.Target);
        ValidateObservationLocation(observation.From);
        ValidateObservationLocation(observation.To);
        if (observation.MoveReason is not null)
        {
            RequireObservationToken(observation.MoveReason, "move_reason");
        }

        if (observation.DamageKind is not null)
        {
            RequireObservationToken(observation.DamageKind, "damage_kind");
        }
        if (observation.DeclarationKind is not null)
        {
            RequireObservationToken(observation.DeclarationKind, "declaration_kind");
        }

        if (observation.ActualAmount < 0)
        {
            throw new ScgsProtocolException("A v05 observation has a negative actual amount.");
        }

        ValidateObservationState(observation.Before);
        ValidateObservationState(observation.After);
        bool hasHiddenEndpoint = observation.Source?.Hidden == true ||
            observation.Subject?.Hidden == true || observation.Target?.Hidden == true;
        if (observation.PublicToAll && hasHiddenEndpoint)
        {
            throw new ScgsProtocolException("A public v05 observation contains a private endpoint.");
        }
        if (gameEvent.HiddenCard &&
            (HasObservationIdentity(observation.Source) ||
             HasObservationIdentity(observation.Subject) ||
             HasObservationIdentity(observation.Target) ||
             observation.Before is not null || observation.After is not null))
        {
            throw new ScgsProtocolException("A hidden v05 observation leaked identity-derived data.");
        }

        if (observation.Subject?.Hidden == true &&
            (observation.Before is not null || observation.After is not null))
        {
            throw new ScgsProtocolException("A hidden observation subject leaked state.");
        }

        // A future kind is deliberately not interpreted, but its structural and
        // privacy boundaries still apply. Do not turn it into an existing cue.
        if (!observation.IsKnownKind)
        {
            return;
        }

        switch (observation.Kind)
        {
            case "move":
                if (observation.Subject?.Kind != "card" || observation.From is null ||
                    observation.To is null || observation.MoveReason is null)
                {
                    throw new ScgsProtocolException("A v05 move observation is incomplete.");
                }

                break;
            case "damage":
                if (observation.Subject is null || !observation.ActualAmount.HasValue ||
                    observation.DamageKind is null || !observation.BarrierConsumed.HasValue)
                {
                    throw new ScgsProtocolException("A v05 damage observation is incomplete.");
                }

                break;
            case "heal":
                if (observation.Subject is null || !observation.ActualAmount.HasValue)
                {
                    throw new ScgsProtocolException("A v05 heal observation is incomplete.");
                }

                break;
            case "evolve":
                if (observation.Subject?.Kind != "card" || hasHiddenEndpoint ||
                    observation.Before?.Evolved != false || observation.After?.Evolved != true)
                {
                    throw new ScgsProtocolException("A v05 evolve observation is incomplete.");
                }

                break;
            case "state_change":
                if (observation.Subject is null || observation.Before is null || observation.After is null)
                {
                    throw new ScgsProtocolException("A v05 state-change observation is incomplete.");
                }

                break;
            case "declaration":
                if (observation.Source is null)
                {
                    throw new ScgsProtocolException("A v05 declaration observation has no source.");
                }

                break;
        }
    }

    private static bool HasObservationIdentity(EventObservationEndpoint? endpoint) =>
        endpoint?.Card is not null || endpoint?.DesignId is not null;

    private static void ValidateObservationEndpoint(EventObservationEndpoint? endpoint)
    {
        if (endpoint is null)
        {
            return;
        }

        RequirePlayer(endpoint.Player, "observation.endpoint.player");
        switch (endpoint.Kind)
        {
            case "leader":
                if (endpoint.Hidden || HasObservationIdentity(endpoint))
                {
                    throw new ScgsProtocolException("A v05 leader endpoint contains card identity or is hidden.");
                }

                break;
            case "card":
                if (endpoint.Hidden)
                {
                    if (HasObservationIdentity(endpoint))
                    {
                        throw new ScgsProtocolException("A hidden v05 endpoint leaked a card identity.");
                    }
                }
                else if (endpoint.Card is null or 0 || string.IsNullOrWhiteSpace(endpoint.DesignId))
                {
                    throw new ScgsProtocolException("A visible v05 card endpoint has incomplete identity.");
                }

                break;
            default:
                throw new ScgsProtocolException("Unsupported v05 observation endpoint kind.");
        }
    }

    private static void ValidateObservationLocation(EventObservationLocation? location)
    {
        if (location is null)
        {
            return;
        }

        RequirePlayer(location.Player, "observation.location.player");
        RequireDefined(location.Zone, "observation.location.zone");
        bool valid = location.Zone switch
        {
            Zone.MainBoard => location.Slot is < 5,
            Zone.Tactic => location.Slot is < 3,
            _ => location.Slot is null,
        };
        if (!valid)
        {
            throw new ScgsProtocolException("A v05 observation location has a missing, invalid or private slot.");
        }
    }

    private static void ValidateObservationState(EventObservationState? state)
    {
        if (state is not null && state.Health is null && state.MaxHealth is null &&
            state.Attack is null && state.Countdown is null && state.Evolved is null && state.Keywords is null)
        {
            throw new ScgsProtocolException("An observation state must contain a known state field.");
        }
    }

    private static void RequireObservationToken(string? value, string field)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64 ||
            value.Any(character => character is not (>= 'a' and <= 'z') and not '_'))
        {
            throw new ScgsProtocolException($"The v05 observation {field} is not a protocol token.");
        }
    }
}
