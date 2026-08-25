// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scgs.Client.V05;

internal static class ScgsV05Json
{
    private static readonly HashSet<string> SafeHiddenEventTexts =
    [
        "opponent drew a card",
        "opponent set a trap",
        "opponent completed mulligan",
        "opponent completed a private choice",
        "opponent is choosing",
    ];

    // Unknown output members belong to a future schema-minor producer and are
    // skipped wholesale, including null-valued members. Known schema-2
    // members still enforce omission instead of null.
    private static readonly HashSet<string> KnownOutputProperties =
    [
        "schema_version", "revision", "view", "actions", "command", "payment",
        "targets", "slots", "donors", "reaction", "pending_choice", "last_sequence",
        "events", "viewer", "active_player", "first_player", "phase", "result",
        "players", "player", "profession_id", "leader_health", "maximum_leader_health",
        "current_pp", "pp_capacity", "cracks", "evolution_energy", "own_turn_number",
        "fatigue_count", "mulligan_done", "evolution_used_this_turn",
        "advance_used_this_turn", "deploy_used_this_turn", "trap_set_this_turn",
        "deck_count", "hand_count", "hand", "main_board", "tactics", "field",
        "graveyard", "archive", "standby", "instance_id", "design_id", "series_id",
        "neutral", "kind", "name", "owner", "controller", "zone", "sequence",
        "cost", "current_attack", "current_health", "maximum_health",
        "printed_keywords", "permanent_keywords", "turn_keywords", "keywords",
        "evolved", "attacked_this_turn", "entered_this_turn", "face_down", "countdown",
        "pending", "window", "responder", "subject", "origin", "depth",
        "eligible_count", "eligible_traps", "action", "source", "target", "permanent",
        "chooser", "choice_id", "minimum_selections", "maximum_selections", "ordered",
        "options", "option_id", "label", "card", "status", "engine_code", "message",
        "current_pp_before", "current_pp_after", "pp_capacity_before", "pp_capacity_after",
        "cracks_before", "cracks_after", "evolution_energy_before",
        "evolution_energy_after", "base_cost", "burn_cost", "advance_cost", "used_advance",
        "slot", "mode_id", "use_advance", "mulligan_cards", "selected_option_ids",
        "additional_cost_cards", "expected_revision", "type", "value", "secondary_value",
        "hidden_card", "text",
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
            throw new ArgumentException("Both product deck identifiers are required.", nameof(config));
        }

        RequireDefined(config.FirstPlayerMode, nameof(config.FirstPlayerMode));
        return Serialize(new ConfigPayload
        {
            SchemaVersion = ScgsV05Contract.SchemaVersion,
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
            SchemaVersion = ScgsV05Contract.SchemaVersion,
            Player = command.Player,
            Action = command.Action,
            Source = Allows(command.Action, CommandField.Source) ? command.Source : null,
            Target = Allows(command.Action, CommandField.Target) ? command.Target : null,
            Slot = Allows(command.Action, CommandField.Slot) ? command.Slot : null,
            ModeId = Allows(command.Action, CommandField.Mode) ? command.ModeId : null,
            ChoiceId = Allows(command.Action, CommandField.Choice) ? command.ChoiceId : null,
            UseAdvance = Allows(command.Action, CommandField.UseAdvance)
                ? command.UseAdvance
                : null,
            MulliganCards = Allows(command.Action, CommandField.MulliganCards)
                ? command.MulliganCards
                : null,
            SelectedOptionIds = Allows(command.Action, CommandField.SelectedOptions)
                ? command.SelectedOptionIds
                : null,
            AdditionalCostCards = Allows(command.Action, CommandField.AdditionalCosts)
                ? command.AdditionalCostCards
                : null,
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
            ValidateTarget(query.Target);
        }

        ValidateOptionalIdentifier(query.ModeId, nameof(query.ModeId));
        ValidateOptionalIdentifier(query.ChoiceId, nameof(query.ChoiceId));
        ValidateStrings(query.SelectedOptionIds, nameof(query.SelectedOptionIds));
        ValidateUnique(query.MulliganCards, nameof(query.MulliganCards));
        ValidateUnique(query.AdditionalCostCards, nameof(query.AdditionalCostCards));
        ValidateQueryFields(query);

        return Serialize(new QueryPayload
        {
            SchemaVersion = ScgsV05Contract.SchemaVersion,
            Player = query.Player,
            ExpectedRevision = query.ExpectedRevision,
            Action = query.Action,
            Source = query.Source,
            Target = query.Target,
            Slot = query.Slot,
            ModeId = query.ModeId,
            ChoiceId = query.ChoiceId,
            UseAdvance = query.UseAdvance,
            MulliganCards = query.MulliganCards,
            SelectedOptionIds = query.SelectedOptionIds,
            AdditionalCostCards = query.AdditionalCostCards,
        });
    }

    internal static MatchView DeserializeView(string json, PlayerId requestedViewer)
    {
        RequirePlayer(requestedViewer, nameof(requestedViewer));
        ViewEnvelope envelope = Deserialize<ViewEnvelope>(json);
        ValidateEnvelope(envelope);
        MatchView view = envelope.View ?? throw new ScgsProtocolException("The v05 view is null.");
        if (view.Revision != envelope.Revision)
        {
            throw new ScgsProtocolException("The v05 view revision does not match its envelope.");
        }

        ValidateInbound(() => ValidateView(view, requestedViewer));
        return view;
    }

    internal static ActionsEnvelope DeserializeActions(string json)
    {
        ActionsEnvelope envelope = Deserialize<ActionsEnvelope>(json);
        ValidateEnvelope(envelope);
        ValidateInbound(() =>
        {
            RequireArray(envelope.Actions, "actions");
            foreach (LegalAction action in envelope.Actions)
            {
                if (action?.Command is null || action.Payment is null)
                {
                    throw new ScgsProtocolException("A v05 legal action is incomplete.");
                }

                ValidateCommand(action.Command, allowUnknownAction: true);
                ValidatePayment(action.Payment);
                if (action.Command.ExpectedRevision != envelope.Revision)
                {
                    throw new ScgsProtocolException("A v05 legal action has a stale revision.");
                }
            }
        });

        return envelope;
    }

    internal static TargetsEnvelope DeserializeTargets(string json)
    {
        TargetsEnvelope envelope = Deserialize<TargetsEnvelope>(json);
        ValidateEnvelope(envelope);
        ValidateInbound(() =>
        {
            RequireArray(envelope.Targets, "targets");
            foreach (Target target in envelope.Targets)
            {
                ValidateTarget(target ?? throw new ScgsProtocolException("A v05 target is null."));
            }
        });

        return envelope;
    }

    internal static SlotsEnvelope DeserializeSlots(string json)
    {
        SlotsEnvelope envelope = Deserialize<SlotsEnvelope>(json);
        ValidateEnvelope(envelope);
        RequireArray(envelope.Slots, "slots");
        return envelope;
    }

    internal static DonorsEnvelope DeserializeDonors(string json)
    {
        DonorsEnvelope envelope = Deserialize<DonorsEnvelope>(json);
        ValidateEnvelope(envelope);
        RequireArray(envelope.Donors, "donors");
        return envelope;
    }

    internal static PaymentEnvelope DeserializePayment(string json)
    {
        PaymentEnvelope envelope = Deserialize<PaymentEnvelope>(json);
        ValidateEnvelope(envelope);
        ValidatePayment(envelope.Payment ??
            throw new ScgsProtocolException("The v05 payment preview is null."));
        return envelope;
    }

    internal static ReactionEnvelope DeserializeReaction(
        string json,
        PlayerId requestedViewer)
    {
        RequirePlayer(requestedViewer, nameof(requestedViewer));
        ReactionEnvelope envelope = Deserialize<ReactionEnvelope>(json);
        ValidateEnvelope(envelope);
        ValidateInbound(() =>
        {
            ValidateReaction(
                envelope.Reaction ?? throw new ScgsProtocolException("The v05 reaction is null."),
                envelope.Revision);
            ValidatePendingChoice(
                envelope.PendingChoice ??
                    throw new ScgsProtocolException("The v05 pending choice is null."),
                requestedViewer,
                envelope.Revision);
        });
        return envelope;
    }

    internal static EventsEnvelope DeserializeEvents(
        string json,
        PlayerId requestedViewer,
        ulong afterSequence)
    {
        RequirePlayer(requestedViewer, nameof(requestedViewer));
        EventsEnvelope envelope = Deserialize<EventsEnvelope>(json);
        ValidateEnvelope(envelope);
        ValidateInbound(() =>
        {
            RequireArray(envelope.Events, "events");
            ulong previous = afterSequence;
            foreach (GameEventView gameEvent in envelope.Events)
            {
                if (gameEvent is null)
                {
                    throw new ScgsProtocolException("A v05 event is null.");
                }

                if (gameEvent.Sequence <= previous)
                {
                    throw new ScgsProtocolException("V05 event sequences are not strictly increasing.");
                }

                previous = gameEvent.Sequence;
                RequirePlayer(gameEvent.Player, "event.player");
                if (gameEvent.FirstPlayer.HasValue)
                {
                    RequirePlayer(gameEvent.FirstPlayer.Value, "event.first_player");
                }

                if (gameEvent.Text is null)
                {
                    throw new ScgsProtocolException("A v05 event text is null.");
                }

                if (gameEvent.HiddenCard &&
                    (gameEvent.Card.HasValue || gameEvent.DesignId is not null))
                {
                    throw new ScgsProtocolException("A hidden v05 event leaked a card identifier.");
                }

                if (gameEvent.HiddenCard && !SafeHiddenEventTexts.Contains(gameEvent.Text))
                {
                    throw new ScgsProtocolException("A hidden v05 event contained unsafe text.");
                }
            }

            ulong expectedLast = envelope.Events.Length == 0 ? afterSequence : previous;
            if (envelope.LastSequence != expectedLast)
            {
                throw new ScgsProtocolException("The v05 event cursor does not match its batch.");
            }
        });

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
            throw new ScgsProtocolException("A v05 request could not be serialized.", exception);
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
            RejectUnsafeShape(document.RootElement, "$", parentProperty: null);
            return JsonSerializer.Deserialize<T>(json, Options) ??
                throw new ScgsProtocolException("The v05 native JSON root is null.");
        }
        catch (ScgsProtocolException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new ScgsProtocolException("The native JSON does not match schema 2.", exception);
        }
    }

    private static void RejectUnsafeShape(
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
                    if (property.NameEquals("random_seed"))
                    {
                        throw new ScgsProtocolException(
                            $"The v05 viewer/event payload leaked a random seed at {propertyPath}.");
                    }

                    if (!KnownOutputProperties.Contains(property.Name))
                    {
                        continue;
                    }

                    if (property.Value.ValueKind == JsonValueKind.Null)
                    {
                        throw new ScgsProtocolException(
                            $"The v05 JSON contains a forbidden null at {propertyPath}.");
                    }

                    RejectUnsafeShape(property.Value, propertyPath, property.Name);
                }

                break;

            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    string itemPath = $"{path}[{index}]";
                    bool isEmptyBoardSlot =
                        parentProperty is "main_board" or "tactics" &&
                        item.ValueKind == JsonValueKind.Null;
                    if (item.ValueKind == JsonValueKind.Null && !isEmptyBoardSlot)
                    {
                        throw new ScgsProtocolException(
                            $"The v05 JSON contains a forbidden null at {itemPath}.");
                    }

                    if (!isEmptyBoardSlot)
                    {
                        RejectUnsafeShape(item, itemPath, parentProperty: null);
                    }

                    ++index;
                }

                break;

            case JsonValueKind.Null:
                throw new ScgsProtocolException($"The v05 JSON contains a forbidden null at {path}.");
        }
    }

    private static void ValidateEnvelope(IScgsV05Envelope envelope)
    {
        if (envelope.SchemaVersion != ScgsV05Contract.SchemaVersion)
        {
            throw new ScgsProtocolException(
                $"Unsupported native JSON schema {envelope.SchemaVersion}; expected schema 2.");
        }
    }

    private static void ValidateInbound(Action validation)
    {
        try
        {
            validation();
        }
        catch (ScgsProtocolException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            throw new ScgsProtocolException(
                "The native JSON contains an invalid schema-2 structural value.",
                exception);
        }
    }

    private static void ValidateView(MatchView view, PlayerId requestedViewer)
    {
        RequirePlayer(view.Viewer, "view.viewer");
        RequirePlayer(view.ActivePlayer, "view.active_player");
        RequirePlayer(view.FirstPlayer, "view.first_player");
        RequireDefined(view.Phase, "view.phase");
        RequireDefined(view.Result, "view.result");
        if (view.Viewer != requestedViewer)
        {
            throw new ScgsProtocolException("The v05 view belongs to a different viewer.");
        }

        RequireArray(view.Players, "view.players");
        if (view.Players.Length != 2 ||
            view.Players[0]?.Player != PlayerId.Player0 ||
            view.Players[1]?.Player != PlayerId.Player1)
        {
            throw new ScgsProtocolException("The v05 player array is not [Player0, Player1].");
        }

        foreach (PlayerView player in view.Players)
        {
            ValidatePlayerView(player, requestedViewer);
        }

        ValidateReaction(
            view.Reaction ?? throw new ScgsProtocolException("The embedded v05 reaction is null."),
            view.Revision);
        ValidatePendingChoice(
            view.PendingChoice ??
                throw new ScgsProtocolException("The embedded v05 pending choice is null."),
            requestedViewer,
            view.Revision);
    }

    private static void ValidatePlayerView(PlayerView player, PlayerId requestedViewer)
    {
        RequirePlayer(player.Player, "player.player");
        if (string.IsNullOrWhiteSpace(player.ProfessionId))
        {
            throw new ScgsProtocolException("A v05 player profession is empty.");
        }

        RequireArray(player.Hand, "player.hand");
        RequireArray(player.MainBoard, "player.main_board");
        RequireArray(player.Tactics, "player.tactics");
        RequireArray(player.Graveyard, "player.graveyard");
        RequireArray(player.Archive, "player.archive");
        RequireArray(player.Standby, "player.standby");
        if (player.MainBoard.Length != 5 || player.Tactics.Length != 3)
        {
            throw new ScgsProtocolException("V05 board arrays must contain five and three slots.");
        }

        if (player.Player != requestedViewer && player.Hand.Length != 0)
        {
            throw new ScgsProtocolException("The v05 opponent hand leaked card data.");
        }

        ValidateCardArray(player.Hand, player.Player, Zone.Hand, "hand");
        ValidateCardArray(player.Graveyard, player.Player, Zone.Graveyard, "graveyard");
        ValidateCardArray(player.Archive, player.Player, Zone.Archive, "archive");
        ValidateCardArray(player.Standby, player.Player, Zone.Standby, "standby");

        foreach (CardView? card in player.MainBoard)
        {
            if (card is not null)
            {
                ValidatePlacedCard(
                    card,
                    player.Player,
                    Zone.MainBoard,
                    allowHiddenIdentity: false,
                    "main_board");
            }
        }

        foreach (CardView? card in player.Tactics)
        {
            if (card is not null)
            {
                ValidatePlacedCard(
                    card,
                    player.Player,
                    Zone.Tactic,
                    allowHiddenIdentity: player.Player != requestedViewer && card.FaceDown,
                    "tactics");
            }
        }

        if (player.Field is not null)
        {
            ValidatePlacedCard(
                player.Field,
                player.Player,
                Zone.Field,
                allowHiddenIdentity: false,
                "field");
            if (player.Field.Kind != CardKind.Field)
            {
                throw new ScgsProtocolException("A v05 field slot contains the wrong card kind or zone.");
            }
        }
    }

    private static void ValidateCardArray(
        IEnumerable<CardView> cards,
        PlayerId controller,
        Zone zone,
        string container)
    {
        foreach (CardView card in cards)
        {
            ValidatePlacedCard(
                card ?? throw new ScgsProtocolException($"A v05 {container} card is null."),
                controller,
                zone,
                allowHiddenIdentity: false,
                container);
        }
    }

    private static void ValidatePlacedCard(
        CardView card,
        PlayerId controller,
        Zone zone,
        bool allowHiddenIdentity,
        string container)
    {
        ValidateCard(card, allowHiddenIdentity);
        if (card.Controller != controller || card.Zone != zone)
        {
            throw new ScgsProtocolException(
                $"A v05 {container} card has a controller or zone inconsistent with its container.");
        }
    }

    private static void ValidateCard(CardView card, bool allowHiddenIdentity)
    {
        if (card.Name is null)
        {
            throw new ScgsProtocolException("A v05 card name is null.");
        }

        RequirePlayer(card.Owner, "card.owner");
        RequirePlayer(card.Controller, "card.controller");
        RequireDefined(card.Zone, "card.zone");
        if (allowHiddenIdentity)
        {
            if (card.Zone != Zone.Tactic || !card.FaceDown ||
                card.InstanceId.HasValue || card.DesignId is not null ||
                card.ProfessionId is not null || card.SeriesId is not null ||
                card.Neutral.HasValue || card.Kind.HasValue || card.Name.Length != 0 ||
                card.Sequence != 0 || card.Cost != 0 || card.CurrentAttack != 0 ||
                card.CurrentHealth != 0 ||
                card.MaximumHealth != 0 || card.PrintedKeywords != Keyword.None ||
                card.PermanentKeywords != Keyword.None || card.TurnKeywords != Keyword.None ||
                card.Keywords != Keyword.None || card.Evolved || card.AttackedThisTurn ||
                card.EnteredThisTurn || card.Countdown != 0)
            {
                throw new ScgsProtocolException(
                    "A face-down v05 tactic leaked identity-derived data.");
            }

            return;
        }

        if (!card.InstanceId.HasValue || string.IsNullOrWhiteSpace(card.DesignId) ||
            string.IsNullOrWhiteSpace(card.ProfessionId) || string.IsNullOrWhiteSpace(card.SeriesId) ||
            !card.Neutral.HasValue || !card.Kind.HasValue)
        {
            throw new ScgsProtocolException("A public v05 card is missing identity metadata.");
        }

        RequireDefined(card.Kind.Value, "card.kind");
        if (card.Neutral.Value)
        {
            if (card.ProfessionId != "neutral" || card.SeriesId != "neutral")
            {
                throw new ScgsProtocolException("A neutral v05 card has non-neutral construction tags.");
            }
        }
    }

    private static void ValidateReaction(ReactionContext reaction, ulong revision)
    {
        RequireDefined(reaction.Window, "reaction.window");
        RequirePlayer(reaction.Responder, "reaction.responder");
        RequireArray(reaction.EligibleTraps, "reaction.eligible_traps");
        if (reaction.Revision != revision)
        {
            throw new ScgsProtocolException("The v05 reaction revision is stale.");
        }

        if (reaction.EligibleCount != (ulong)reaction.EligibleTraps.Length)
        {
            throw new ScgsProtocolException("The v05 reaction eligible count is inconsistent.");
        }
    }

    private static void ValidatePendingChoice(
        PendingChoiceView choice,
        PlayerId requestedViewer,
        ulong revision)
    {
        RequireArray(choice.Options, "pending_choice.options");
        if (choice.Revision != revision)
        {
            throw new ScgsProtocolException("The v05 pending-choice revision is stale.");
        }

        if (!choice.Pending)
        {
            if (choice.Chooser.HasValue || choice.ChoiceId is not null || choice.Kind.HasValue ||
                choice.MinimumSelections.HasValue || choice.MaximumSelections.HasValue ||
                choice.Ordered.HasValue || choice.Options.Length != 0)
            {
                throw new ScgsProtocolException("An inactive v05 choice contains stale selection data.");
            }

            return;
        }

        if (!choice.Chooser.HasValue)
        {
            throw new ScgsProtocolException("A pending v05 choice has no chooser.");
        }

        RequirePlayer(choice.Chooser.Value, "pending_choice.chooser");
        if (choice.Chooser.Value != requestedViewer)
        {
            if (choice.ChoiceId is not null || choice.Kind.HasValue ||
                choice.MinimumSelections.HasValue || choice.MaximumSelections.HasValue ||
                choice.Ordered.HasValue || choice.Options.Length != 0)
            {
                throw new ScgsProtocolException(
                    "A private v05 choice shape leaked to the other viewer.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(choice.ChoiceId) || !choice.Kind.HasValue ||
            !choice.MinimumSelections.HasValue || !choice.MaximumSelections.HasValue ||
            !choice.Ordered.HasValue)
        {
            throw new ScgsProtocolException("The choice owner received an incomplete v05 choice.");
        }

        RequireDefined(choice.Kind.Value, "pending_choice.kind");
        if (choice.MinimumSelections > choice.MaximumSelections ||
            choice.MaximumSelections > (ulong)choice.Options.Length)
        {
            throw new ScgsProtocolException("The v05 choice selection bounds are invalid.");
        }

        var optionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (PendingChoiceOptionView option in choice.Options)
        {
            if (option is null || string.IsNullOrWhiteSpace(option.OptionId) ||
                !optionIds.Add(option.OptionId))
            {
                throw new ScgsProtocolException("V05 choice option identifiers must be unique and non-empty.");
            }

            if (option.Card is not null)
            {
                ValidateCard(option.Card, allowHiddenIdentity: false);
            }
        }
    }

    private static void ValidatePayment(PaymentPreview payment)
    {
        if (payment.Status is null || payment.Status.Message is null)
        {
            throw new ScgsProtocolException("A v05 payment status is incomplete.");
        }
    }

    private static void ValidateCommand(GameCommandRequest command, bool allowUnknownAction)
    {
        RequirePlayer(command.Player, nameof(command.Player));
        if (!allowUnknownAction)
        {
            RequireDefined(command.Action, nameof(command.Action));
        }

        if (command.Target is not null)
        {
            ValidateTarget(command.Target);
        }

        ValidateOptionalIdentifier(command.ModeId, nameof(command.ModeId));
        ValidateOptionalIdentifier(command.ChoiceId, nameof(command.ChoiceId));
        ValidateStrings(command.SelectedOptionIds, nameof(command.SelectedOptionIds));
        ValidateUnique(command.MulliganCards, nameof(command.MulliganCards));
        ValidateUnique(command.AdditionalCostCards, nameof(command.AdditionalCostCards));
        if (!allowUnknownAction || Enum.IsDefined(command.Action))
        {
            ValidateCommandFields(command);
        }
        if (command.Action == ActionKind.ResolveChoice && string.IsNullOrWhiteSpace(command.ChoiceId))
        {
            throw new ArgumentException("ResolveChoice requires a choice identifier.", nameof(command));
        }
    }

    private enum CommandField
    {
        Source,
        Target,
        Slot,
        Mode,
        Choice,
        MulliganCards,
        SelectedOptions,
        AdditionalCosts,
        UseAdvance,
    }

    private static bool Allows(ActionKind action, CommandField field) => action switch
    {
        ActionKind.Mulligan => field == CommandField.MulliganCards,
        ActionKind.PlayUnit or ActionKind.CastSpell or ActionKind.PlayAmulet =>
            field is CommandField.Source or CommandField.Target or CommandField.Slot or
                CommandField.Mode or CommandField.UseAdvance,
        ActionKind.PlayTrap =>
            field is CommandField.Source or CommandField.Slot or CommandField.Mode or
                CommandField.UseAdvance,
        ActionKind.Attack => field is CommandField.Source or CommandField.Target,
        ActionKind.Evolve =>
            field is CommandField.Source or CommandField.Target or CommandField.Mode,
        ActionKind.Deploy =>
            field is CommandField.Source or CommandField.Target or CommandField.Slot or
                CommandField.Mode or CommandField.AdditionalCosts or CommandField.UseAdvance,
        ActionKind.ActivateTrap =>
            field is CommandField.Source or CommandField.Target or CommandField.Mode,
        ActionKind.PlayField =>
            field is CommandField.Source or CommandField.Target or CommandField.Mode or
                CommandField.UseAdvance,
        ActionKind.ResolveChoice =>
            field is CommandField.Choice or CommandField.SelectedOptions,
        _ => false,
    };

    private static void ValidateCommandFields(GameCommandRequest command)
    {
        Reject(!Allows(command.Action, CommandField.Source) && command.Source != 0,
            nameof(command.Source));
        Reject(!Allows(command.Action, CommandField.Target) && command.Target is not null,
            nameof(command.Target));
        Reject(!Allows(command.Action, CommandField.Slot) && command.Slot.HasValue,
            nameof(command.Slot));
        Reject(!Allows(command.Action, CommandField.Mode) && command.ModeId is not null,
            nameof(command.ModeId));
        Reject(!Allows(command.Action, CommandField.Choice) && command.ChoiceId is not null,
            nameof(command.ChoiceId));
        Reject(!Allows(command.Action, CommandField.MulliganCards) && command.MulliganCards.Count != 0,
            nameof(command.MulliganCards));
        Reject(!Allows(command.Action, CommandField.SelectedOptions) &&
            command.SelectedOptionIds.Count != 0, nameof(command.SelectedOptionIds));
        Reject(!Allows(command.Action, CommandField.AdditionalCosts) &&
            command.AdditionalCostCards.Count != 0, nameof(command.AdditionalCostCards));
        Reject(!Allows(command.Action, CommandField.UseAdvance) && command.UseAdvance,
            nameof(command.UseAdvance));
    }

    private static void ValidateQueryFields(ActionQueryRequest query)
    {
        if (!query.Action.HasValue)
        {
            Reject(query.Source.HasValue || query.Target is not null || query.Slot.HasValue ||
                query.ModeId is not null || query.ChoiceId is not null ||
                query.UseAdvance.HasValue || query.MulliganCards is not null ||
                query.SelectedOptionIds is not null || query.AdditionalCostCards is not null,
                nameof(query.Action));
            return;
        }

        ActionKind action = query.Action.Value;
        Reject(!Allows(action, CommandField.Source) && query.Source.HasValue, nameof(query.Source));
        Reject(!Allows(action, CommandField.Target) && query.Target is not null, nameof(query.Target));
        Reject(!Allows(action, CommandField.Slot) && query.Slot.HasValue, nameof(query.Slot));
        Reject(!Allows(action, CommandField.Mode) && query.ModeId is not null, nameof(query.ModeId));
        Reject(!Allows(action, CommandField.Choice) && query.ChoiceId is not null,
            nameof(query.ChoiceId));
        Reject(!Allows(action, CommandField.MulliganCards) && query.MulliganCards is not null,
            nameof(query.MulliganCards));
        Reject(!Allows(action, CommandField.SelectedOptions) && query.SelectedOptionIds is not null,
            nameof(query.SelectedOptionIds));
        Reject(!Allows(action, CommandField.AdditionalCosts) &&
            query.AdditionalCostCards is not null, nameof(query.AdditionalCostCards));
        Reject(!Allows(action, CommandField.UseAdvance) && query.UseAdvance.HasValue,
            nameof(query.UseAdvance));
    }

    private static void Reject(bool condition, string name)
    {
        if (condition)
        {
            throw new ArgumentException(
                "The field is unrelated to the selected v05 action and must be omitted.",
                name);
        }
    }

    private static void ValidateTarget(Target target)
    {
        RequirePlayer(target.Player, nameof(target.Player));
        RequireDefined(target.Kind, nameof(target.Kind));
        if (target.Kind == TargetKind.Leader && target.Permanent.HasValue)
        {
            throw new ArgumentException("A leader target cannot name a permanent.", nameof(target));
        }

        if (target.Kind == TargetKind.Permanent && !target.Permanent.HasValue)
        {
            throw new ArgumentException("A permanent target requires an instance identifier.", nameof(target));
        }
    }

    private static void ValidateOptionalIdentifier(string? value, string name)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An optional identifier must be omitted or non-empty.", name);
        }
    }

    private static void ValidateStrings(IReadOnlyList<string>? values, string name)
    {
        if (values is null)
        {
            return;
        }

        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (string value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || !unique.Add(value))
            {
                throw new ArgumentException("Option identifiers must be unique and non-empty.", name);
            }
        }
    }

    private static void ValidateUnique(IReadOnlyList<ulong>? values, string name)
    {
        if (values is not null && values.Count != values.Distinct().Count())
        {
            throw new ArgumentException("A card selection cannot contain duplicates.", name);
        }
    }

    private static void RequirePlayer(PlayerId player, string name)
    {
        if (player is not PlayerId.Player0 and not PlayerId.Player1)
        {
            throw new ArgumentOutOfRangeException(name, player, "Unsupported v05 player value.");
        }
    }

    private static void RequireDefined<T>(T value, string name)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "Unsupported v05 enum value.");
        }
    }

    private static void RequireArray<T>(T[]? value, string name)
    {
        if (value is null)
        {
            throw new ScgsProtocolException($"The v05 {name} array is null.");
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
        public ulong? Source { get; init; }
        public Target? Target { get; init; }
        public ulong? Slot { get; init; }
        public string? ModeId { get; init; }
        public string? ChoiceId { get; init; }
        public bool? UseAdvance { get; init; }
        public IReadOnlyList<ulong>? MulliganCards { get; init; }
        public IReadOnlyList<string>? SelectedOptionIds { get; init; }
        public IReadOnlyList<ulong>? AdditionalCostCards { get; init; }
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
        public string? ModeId { get; init; }
        public string? ChoiceId { get; init; }
        public bool? UseAdvance { get; init; }
        public IReadOnlyList<ulong>? MulliganCards { get; init; }
        public IReadOnlyList<string>? SelectedOptionIds { get; init; }
        public IReadOnlyList<ulong>? AdditionalCostCards { get; init; }
    }
}
