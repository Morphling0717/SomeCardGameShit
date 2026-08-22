// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scgs.Client;

internal static class ScgsJson
{
    private static readonly HashSet<string> SafeHiddenEventTexts =
    [
        "opponent drew a card",
        "opponent set a trap",
        "opponent completed mulligan",
    ];

    internal static readonly JsonSerializerOptions Options = new()
    {
        AllowTrailingCommas = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NumberHandling = JsonNumberHandling.Strict,
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    internal static string SerializeConfig(GameConfigRequest config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.Player0Deck) ||
            string.IsNullOrWhiteSpace(config.Player1Deck))
        {
            throw new ArgumentException("Both fixed deck names are required.", nameof(config));
        }

        RequireDefined(config.FirstPlayerMode, nameof(config.FirstPlayerMode));
        return Serialize(new ConfigPayload
        {
            SchemaVersion = ScgsV04Contract.SchemaVersion,
            Player0Deck = config.Player0Deck,
            Player1Deck = config.Player1Deck,
            RandomSeed = config.RandomSeed,
            FirstPlayerMode = config.FirstPlayerMode,
            ShuffleDecks = config.ShuffleDecks,
        });
    }

    internal static string SerializeCommand(GameCommandRequest command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommand(command, allowUnknownAction: false);
        return Serialize(new CommandPayload
        {
            SchemaVersion = ScgsV04Contract.SchemaVersion,
            Player = command.Player,
            Action = command.Action,
            Source = command.Source,
            Target = command.Target,
            Slot = command.Slot,
            ComponentDonor = command.ComponentDonor,
            UseAdvance = command.UseAdvance,
            MulliganCards = command.MulliganCards,
            ExpectedRevision = command.ExpectedRevision,
        });
    }

    internal static string SerializeQuery(ActionQueryRequest query)
    {
        ArgumentNullException.ThrowIfNull(query);
        RequirePlayer(query.Player, nameof(query.Player));
        if (query.Action.HasValue)
        {
            RequireDefined(query.Action.Value, nameof(query.Action));
        }

        if (query.Target is not null)
        {
            ValidateTarget(query.Target, inbound: false);
        }

        return Serialize(new QueryPayload
        {
            SchemaVersion = ScgsV04Contract.SchemaVersion,
            Player = query.Player,
            ExpectedRevision = query.ExpectedRevision,
            Action = query.Action,
            Source = query.Source,
            Target = query.Target,
            Slot = query.Slot,
            ComponentDonor = query.ComponentDonor,
            UseAdvance = query.UseAdvance,
            MulliganCards = query.MulliganCards,
        });
    }

    internal static ViewEnvelope DeserializeView(string json, PlayerId requestedViewer)
    {
        RequirePlayer(requestedViewer, nameof(requestedViewer));
        var envelope = Deserialize<ViewEnvelope>(json);
        ValidateEnvelope(envelope);
        if (envelope.View is null)
        {
            throw new ScgsProtocolException("The view payload is null.");
        }

        ValidateMatchView(envelope.View, requestedViewer);
        if (envelope.Revision != envelope.View.Revision)
        {
            throw new ScgsProtocolException("The view revision does not match its envelope.");
        }

        return envelope;
    }

    internal static ActionsEnvelope DeserializeActions(string json)
    {
        var envelope = Deserialize<ActionsEnvelope>(json);
        ValidateEnvelope(envelope);
        RequireArray(envelope.Actions, "actions");
        foreach (LegalAction action in envelope.Actions)
        {
            if (action?.Command is null || action.Payment is null)
            {
                throw new ScgsProtocolException("A legal action is incomplete.");
            }

            ValidateCommand(action.Command, allowUnknownAction: true);
            ValidatePayment(action.Payment);
            if (action.Command.ExpectedRevision != envelope.Revision)
            {
                throw new ScgsProtocolException(
                    "A legal action revision does not match its envelope.");
            }
        }

        return envelope;
    }

    internal static TargetsEnvelope DeserializeTargets(string json)
    {
        var envelope = Deserialize<TargetsEnvelope>(json);
        ValidateEnvelope(envelope);
        RequireArray(envelope.Targets, "targets");
        foreach (Target target in envelope.Targets)
        {
            ValidateTarget(
                target ?? throw new ScgsProtocolException("A target is null."),
                inbound: true);
        }

        return envelope;
    }

    internal static SlotsEnvelope DeserializeSlots(string json)
    {
        var envelope = Deserialize<SlotsEnvelope>(json);
        ValidateEnvelope(envelope);
        RequireArray(envelope.Slots, "slots");
        return envelope;
    }

    internal static DonorsEnvelope DeserializeDonors(string json)
    {
        var envelope = Deserialize<DonorsEnvelope>(json);
        ValidateEnvelope(envelope);
        RequireArray(envelope.Donors, "donors");
        return envelope;
    }

    internal static PaymentEnvelope DeserializePayment(string json)
    {
        var envelope = Deserialize<PaymentEnvelope>(json);
        ValidateEnvelope(envelope);
        if (envelope.Payment is null)
        {
            throw new ScgsProtocolException("The payment payload is null.");
        }

        ValidatePayment(envelope.Payment);
        return envelope;
    }

    internal static ReactionEnvelope DeserializeReaction(string json, PlayerId requestedViewer)
    {
        RequirePlayer(requestedViewer, nameof(requestedViewer));
        var envelope = Deserialize<ReactionEnvelope>(json);
        ValidateEnvelope(envelope);
        if (envelope.Reaction is null)
        {
            throw new ScgsProtocolException("The reaction payload is null.");
        }

        ValidateReaction(envelope.Reaction, requestedViewer);
        if (envelope.Revision != envelope.Reaction.Revision)
        {
            throw new ScgsProtocolException("The reaction revision does not match its envelope.");
        }

        return envelope;
    }

    internal static EventsEnvelope DeserializeEvents(
        string json,
        PlayerId requestedViewer,
        ulong afterSequence)
    {
        RequirePlayer(requestedViewer, nameof(requestedViewer));
        var envelope = Deserialize<EventsEnvelope>(json);
        ValidateEnvelope(envelope);
        RequireArray(envelope.Events, "events");

        ulong previous = afterSequence;
        foreach (GameEventView gameEvent in envelope.Events)
        {
            if (gameEvent is null)
            {
                throw new ScgsProtocolException("An event is null.");
            }

            if (gameEvent.Sequence <= previous)
            {
                throw new ScgsProtocolException("Event sequences are not strictly increasing.");
            }

            previous = gameEvent.Sequence;
            RequireInboundPlayer(gameEvent.Player, "event.player");
            if (gameEvent.FirstPlayer.HasValue)
            {
                RequireInboundPlayer(gameEvent.FirstPlayer.Value, "event.first_player");
            }

            if (gameEvent.Text is null)
            {
                throw new ScgsProtocolException("An event text is null.");
            }

            if (gameEvent.HiddenCard &&
                (gameEvent.Card.HasValue || gameEvent.DefinitionId.HasValue))
            {
                throw new ScgsProtocolException("A hidden event leaked a card identifier.");
            }

            if (gameEvent.HiddenCard && !SafeHiddenEventTexts.Contains(gameEvent.Text))
            {
                throw new ScgsProtocolException("A hidden event contained unsafe text.");
            }
        }

        ulong expectedLast = envelope.Events.Length == 0 ? afterSequence : previous;
        if (envelope.LastSequence != expectedLast)
        {
            throw new ScgsProtocolException("The event cursor does not match the returned batch.");
        }

        return envelope;
    }

    private static string Serialize<T>(T value)
    {
        try
        {
            return JsonSerializer.Serialize(value, Options);
        }
        catch (JsonException exception)
        {
            throw new ScgsProtocolException("A managed request could not be serialized.", exception);
        }
    }

    private static T Deserialize<T>(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });
            RejectUnexpectedNulls(document.RootElement, "$", parentProperty: null);
            return JsonSerializer.Deserialize<T>(json, Options) ??
                throw new ScgsProtocolException("The native JSON root is null.");
        }
        catch (ScgsProtocolException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new ScgsProtocolException("The native JSON does not match schema 1.", exception);
        }
    }

    private static void RejectUnexpectedNulls(
        JsonElement element,
        string path,
        string? parentProperty)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string propertyPath = $"{path}.{property.Name}";
                    if (property.Value.ValueKind == JsonValueKind.Null)
                    {
                        throw new ScgsProtocolException(
                            $"The native JSON contains a forbidden null at {propertyPath}.");
                    }

                    RejectUnexpectedNulls(property.Value, propertyPath, property.Name);
                }

                break;

            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    string itemPath = $"{path}[{index}]";
                    bool isEmptyBoardSlot =
                        parentProperty is "units" or "tactics" &&
                        item.ValueKind == JsonValueKind.Null;
                    if (item.ValueKind == JsonValueKind.Null && !isEmptyBoardSlot)
                    {
                        throw new ScgsProtocolException(
                            $"The native JSON contains a forbidden null at {itemPath}.");
                    }

                    if (!isEmptyBoardSlot)
                    {
                        RejectUnexpectedNulls(item, itemPath, parentProperty: null);
                    }

                    ++index;
                }

                break;

            case JsonValueKind.Null:
                throw new ScgsProtocolException(
                    $"The native JSON contains a forbidden null at {path}.");
        }
    }

    private static void ValidateEnvelope(IScgsEnvelope envelope)
    {
        if (envelope.SchemaVersion != ScgsV04Contract.SchemaVersion)
        {
            throw new ScgsProtocolException(
                $"Unsupported native JSON schema {envelope.SchemaVersion}.");
        }
    }

    private static void ValidateMatchView(MatchView view, PlayerId requestedViewer)
    {
        RequireInboundPlayer(view.Viewer, "view.viewer");
        RequireInboundPlayer(view.ActivePlayer, "view.active_player");
        RequireInboundPlayer(view.FirstPlayer, "view.first_player");
        RequireInboundDefined(view.Phase, "view.phase");
        RequireInboundDefined(view.Result, "view.result");
        if (view.Viewer != requestedViewer)
        {
            throw new ScgsProtocolException("The native view belongs to a different viewer.");
        }

        RequireArray(view.Players, "view.players");
        if (view.Players.Length != 2 ||
            view.Players[0]?.Player != PlayerId.Player0 ||
            view.Players[1]?.Player != PlayerId.Player1)
        {
            throw new ScgsProtocolException("The player view array is not [Player0, Player1].");
        }

        foreach (PlayerView player in view.Players)
        {
            ValidatePlayerView(player, requestedViewer);
        }

        ValidateReaction(
            view.Reaction ??
                throw new ScgsProtocolException("The embedded reaction context is null."),
            requestedViewer);
        if (view.Reaction.Revision != view.Revision)
        {
            throw new ScgsProtocolException("The embedded reaction revision is stale.");
        }
    }

    private static void ValidatePlayerView(PlayerView player, PlayerId requestedViewer)
    {
        RequireInboundPlayer(player.Player, "player.player");
        ValidateLeaderSkill(
            player.LeaderSkill ??
                throw new ScgsProtocolException("The player leader skill is null."));
        RequireArray(player.Hand, "player.hand");
        RequireArray(player.Units, "player.units");
        RequireArray(player.Tactics, "player.tactics");
        RequireArray(player.Graveyard, "player.graveyard");
        RequireArray(player.Archive, "player.archive");
        RequireArray(player.Standby, "player.standby");
        if (player.Units.Length != 5 || player.Tactics.Length != 3)
        {
            throw new ScgsProtocolException("Unit/tactic slot arrays have the wrong length.");
        }

        if (player.Player != requestedViewer && player.Hand.Length != 0)
        {
            throw new ScgsProtocolException("The opponent hand leaked card data.");
        }

        foreach (CardView card in player.Hand.Concat(player.Graveyard)
                     .Concat(player.Archive).Concat(player.Standby))
        {
            ValidateCard(
                card ?? throw new ScgsProtocolException("A public card is null."),
                allowHiddenIdentity: false);
        }

        foreach (CardView? card in player.Units)
        {
            if (card is not null)
            {
                ValidateCard(card, allowHiddenIdentity: false);
            }
        }

        foreach (CardView? card in player.Tactics)
        {
            if (card is null)
            {
                continue;
            }

            bool allowHiddenIdentity = player.Player != requestedViewer && card.FaceDown;
            ValidateCard(card, allowHiddenIdentity);
        }
    }

    private static void ValidateCard(CardView card, bool allowHiddenIdentity)
    {
        if (card.Name is null || card.GrantedComponent is null)
        {
            throw new ScgsProtocolException("A card view is incomplete.");
        }

        RequireInboundPlayer(card.Owner, "card.owner");
        RequireInboundPlayer(card.Controller, "card.controller");
        RequireInboundDefined(card.Zone, "card.zone");
        if (card.Kind.HasValue)
        {
            RequireInboundDefined(card.Kind.Value, "card.kind");
        }

        ValidateComponent(card.GrantedComponent, "card.granted_component");
        if (allowHiddenIdentity)
        {
            if (card.Zone != Zone.Tactic || !card.FaceDown || card.Name.Length != 0 ||
                card.InstanceId.HasValue || card.DefinitionId.HasValue ||
                card.Definition is not null || card.Kind.HasValue)
            {
                throw new ScgsProtocolException(
                    "An opponent face-down trap has an invalid hidden identity shape.");
            }

            return;
        }

        if (!card.InstanceId.HasValue || !card.DefinitionId.HasValue ||
            card.Definition is null || !card.Kind.HasValue ||
            string.IsNullOrEmpty(card.Name))
        {
            throw new ScgsProtocolException("A visible card is missing identity data.");
        }

        ValidateCardDefinition(card.Definition);
        if (card.DefinitionId.Value != card.Definition.Id ||
            card.Kind.Value != card.Definition.Kind ||
            !string.Equals(card.Name, card.Definition.Name, StringComparison.Ordinal))
        {
            throw new ScgsProtocolException(
                "A visible card identity does not match its embedded definition.");
        }
    }

    private static void ValidateCardDefinition(CardDefinition definition)
    {
        if (string.IsNullOrEmpty(definition.Name) || definition.AdditionalCost is null ||
            definition.Component is null)
        {
            throw new ScgsProtocolException("A card definition is incomplete.");
        }

        RequireInboundDefined(definition.Kind, "definition.kind");
        ValidateComponent(definition.Component, "definition.component");
        if (definition.Deployment is not null)
        {
            RequireInboundDefined(definition.Deployment.Condition, "definition.deployment.condition");
        }

        RequireArray(definition.Effects, "definition.effects");
        foreach (EffectRecord effect in definition.Effects)
        {
            ValidateEffect(
                effect ?? throw new ScgsProtocolException("A card effect is null."),
                "definition.effect");
        }
    }

    private static void ValidateLeaderSkill(LeaderSkillDefinition skill)
    {
        if (string.IsNullOrEmpty(skill.Name))
        {
            throw new ScgsProtocolException("The leader skill name is empty.");
        }

        RequireArray(skill.Effects, "leader_skill.effects");
        foreach (EffectRecord effect in skill.Effects)
        {
            ValidateEffect(
                effect ?? throw new ScgsProtocolException("A leader skill effect is null."),
                "leader_skill.effect");
        }
    }

    private static void ValidateEffect(EffectRecord effect, string fieldName)
    {
        RequireInboundDefined(effect.Trigger, $"{fieldName}.trigger");
        RequireInboundDefined(effect.Kind, $"{fieldName}.kind");
        RequireInboundDefined(effect.TargetSpec, $"{fieldName}.target_spec");
    }

    private static void ValidateComponent(ComponentSpec component, string fieldName) =>
        RequireInboundDefined(component.GrantedKind, $"{fieldName}.granted_kind");

    private static void ValidateReaction(ReactionContext reaction, PlayerId requestedViewer)
    {
        RequireInboundDefined(reaction.Window, "reaction.window");
        RequireInboundPlayer(reaction.Responder, "reaction.responder");
        if (reaction.Pending)
        {
            ValidateReactionOrigin(
                reaction.Origin ??
                    throw new ScgsProtocolException(
                        "A pending reaction is missing its origin."));
        }
        else if (reaction.Origin is not null)
        {
            throw new ScgsProtocolException(
                "A non-pending reaction unexpectedly contains an origin.");
        }

        RequireArray(reaction.EligibleTraps, "reaction.eligible_traps");
        foreach (CardView trap in reaction.EligibleTraps)
        {
            ValidateCard(
                trap ?? throw new ScgsProtocolException("An eligible trap is null."),
                allowHiddenIdentity: false);
        }

        if (requestedViewer != reaction.Responder && reaction.EligibleTraps.Length != 0)
        {
            throw new ScgsProtocolException(
                "A non-responder reaction context leaked eligible trap identities.");
        }

        if (requestedViewer == reaction.Responder &&
            (ulong)reaction.EligibleTraps.Length != reaction.EligibleCount)
        {
            throw new ScgsProtocolException(
                "The responder reaction context omitted eligible trap identities.");
        }
    }

    private static void ValidateReactionOrigin(ReactionOrigin origin)
    {
        // ActionKind is deliberately forward-compatible on native output. A future
        // origin can still be shown generically even when this client cannot submit it.
        RequireInboundPlayer(origin.Player, "reaction.origin.player");
        if (origin.Target is not null)
        {
            ValidateTarget(origin.Target, inbound: true);
        }
    }

    private static void ValidatePayment(PaymentPreview payment)
    {
        if (payment.Status is null || payment.Status.Message is null)
        {
            throw new ScgsProtocolException("A payment status is incomplete.");
        }
    }

    private static void ValidateCommand(GameCommandRequest command, bool allowUnknownAction)
    {
        if (allowUnknownAction)
        {
            RequireInboundPlayer(command.Player, nameof(command.Player));
        }
        else
        {
            RequirePlayer(command.Player, nameof(command.Player));
        }
        if (!allowUnknownAction)
        {
            RequireDefined(command.Action, nameof(command.Action));
        }
        if (command.Target is not null)
        {
            ValidateTarget(command.Target, inbound: allowUnknownAction);
        }

        if (command.MulliganCards is null)
        {
            if (allowUnknownAction)
            {
                throw new ScgsProtocolException("A legal action has a null mulligan selection.");
            }

            throw new ArgumentException("MulliganCards cannot be null.", nameof(command));
        }
    }

    private static void ValidateTarget(Target target, bool inbound)
    {
        if (inbound)
        {
            RequireInboundDefined(target.Kind, nameof(target.Kind));
            RequireInboundPlayer(target.Player, nameof(target.Player));
        }
        else
        {
            RequireDefined(target.Kind, nameof(target.Kind));
            RequirePlayer(target.Player, nameof(target.Player));
        }

        if (target.Kind == TargetKind.Unit && !target.Unit.HasValue)
        {
            if (inbound)
            {
                throw new ScgsProtocolException("A unit target is missing its instance ID.");
            }

            throw new ArgumentException("A unit target requires an instance ID.", nameof(target));
        }

        if (target.Kind == TargetKind.Leader && target.Unit.HasValue)
        {
            if (inbound)
            {
                throw new ScgsProtocolException("A leader target contains a unit instance ID.");
            }

            throw new ArgumentException("A leader target cannot contain a unit instance ID.", nameof(target));
        }
    }

    private static void RequirePlayer(PlayerId player, string fieldName)
    {
        if (player is not PlayerId.Player0 and not PlayerId.Player1)
        {
            throw new ArgumentOutOfRangeException(fieldName, player, "Unsupported player value.");
        }
    }

    private static void RequireDefined<T>(T value, string fieldName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(fieldName, value, "Unsupported enum value.");
        }
    }

    private static void RequireInboundPlayer(PlayerId player, string fieldName)
    {
        if (player is not PlayerId.Player0 and not PlayerId.Player1)
        {
            throw new ScgsProtocolException(
                $"The native JSON contains an unsupported {fieldName} value.");
        }
    }

    private static void RequireInboundDefined<T>(T value, string fieldName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ScgsProtocolException(
                $"The native JSON contains an unsupported {fieldName} value.");
        }
    }

    private static void RequireArray<T>(T[]? value, string fieldName)
    {
        if (value is null)
        {
            throw new ScgsProtocolException($"The {fieldName} array is null.");
        }
    }

    private sealed class ConfigPayload
    {
        public required uint SchemaVersion { get; init; }

        public required string Player0Deck { get; init; }

        public required string Player1Deck { get; init; }

        public uint? RandomSeed { get; init; }

        public required FirstPlayerMode FirstPlayerMode { get; init; }

        public required bool ShuffleDecks { get; init; }
    }

    private sealed class CommandPayload
    {
        public required uint SchemaVersion { get; init; }

        public required PlayerId Player { get; init; }

        public required ActionKind Action { get; init; }

        public required ulong Source { get; init; }

        public Target? Target { get; init; }

        public ulong? Slot { get; init; }

        public ulong? ComponentDonor { get; init; }

        public required bool UseAdvance { get; init; }

        public required IReadOnlyList<ulong> MulliganCards { get; init; }

        public required ulong ExpectedRevision { get; init; }
    }

    private sealed class QueryPayload
    {
        public required uint SchemaVersion { get; init; }

        public required PlayerId Player { get; init; }

        public required ulong ExpectedRevision { get; init; }

        public ActionKind? Action { get; init; }

        public ulong? Source { get; init; }

        public Target? Target { get; init; }

        public ulong? Slot { get; init; }

        public ulong? ComponentDonor { get; init; }

        public bool? UseAdvance { get; init; }

        public IReadOnlyList<ulong>? MulliganCards { get; init; }
    }
}
