// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.VisualStudio.TestTools.UnitTesting;
using V05 = Scgs.Client.V05;

namespace Scgs.Client.Tests;

internal sealed class FakeProductGameSession : V05.IScgsV05GameSession
{
    internal Func<V05.PlayerId, V05.MatchView> ViewHandler { get; set; } =
        _ => throw new NotSupportedException();

    internal Func<V05.ActionQueryRequest, V05.LegalActionsResult> ActionsHandler { get; set; } =
        _ => throw new NotSupportedException();

    internal Func<V05.GameCommandRequest, V05.PaymentResult> PaymentHandler { get; set; } =
        _ => throw new NotSupportedException();

    internal Func<V05.GameCommandRequest, V05.EngineStatus> SubmitHandler { get; set; } =
        _ => throw new NotSupportedException();

    internal Func<V05.PlayerId, ulong, V05.EventBatch> EventsHandler { get; set; } =
        (_, _) => throw new NotSupportedException();

    internal List<string> Calls { get; } = [];
    internal List<V05.ActionQueryRequest> Queries { get; } = [];
    internal List<V05.GameCommandRequest> SubmittedCommands { get; } = [];
    internal int DisposeCalls { get; private set; }

    public V05.MatchView GetView(V05.PlayerId viewer)
    {
        Calls.Add($"view:{viewer}");
        return ViewHandler(viewer);
    }

    public V05.LegalActionsResult ListLegalActions(V05.ActionQueryRequest query)
    {
        Calls.Add($"actions:{query.Player}:{query.ExpectedRevision}");
        Queries.Add(query);
        return ActionsHandler(query);
    }

    public V05.PaymentResult PreviewPayment(V05.GameCommandRequest command)
    {
        Calls.Add($"payment:{command.Player}:{command.ExpectedRevision}");
        return PaymentHandler(command);
    }

    public V05.EngineStatus SubmitCommand(V05.GameCommandRequest command)
    {
        Calls.Add($"submit:{command.Player}:{command.Action}:{command.ExpectedRevision}");
        SubmittedCommands.Add(command);
        return SubmitHandler(command);
    }

    public V05.EventBatch ReadEvents(V05.PlayerId viewer, ulong afterSequence)
    {
        Calls.Add($"events:{viewer}:{afterSequence}");
        return EventsHandler(viewer, afterSequence);
    }

    public void Dispose()
    {
        ++DisposeCalls;
        Calls.Add("dispose");
    }

    public V05.EngineStatus Start() => throw new NotSupportedException();
    public V05.ValidTargetsResult ListValidTargets(V05.ActionQueryRequest query) =>
        throw new NotSupportedException();
    public V05.ValidSlotsResult ListValidSlots(V05.ActionQueryRequest query) =>
        throw new NotSupportedException();
    public V05.ValidDonorsResult ListValidDonors(V05.ActionQueryRequest query) =>
        throw new NotSupportedException();
    public V05.ReactionAndChoiceResult GetReactionContext(V05.PlayerId viewer) =>
        throw new NotSupportedException();
    public V05.EventBatch ReadNewEvents(V05.PlayerId viewer) =>
        throw new AssertFailedException("The product controller must own viewer cursors.");
    public ulong GetEventCursor(V05.PlayerId viewer) =>
        throw new AssertFailedException("The product controller must own viewer cursors.");
}

internal static class ProductHotseatTestModel
{
    internal static V05.MatchView View(
        V05.PlayerId viewer,
        ulong revision,
        V05.MatchPhase phase = V05.MatchPhase.Action,
        V05.PlayerId activePlayer = V05.PlayerId.Player0,
        bool player0MulliganDone = true,
        bool player1MulliganDone = true,
        IReadOnlyList<ulong>? ownHand = null,
        V05.PlayerId firstPlayer = V05.PlayerId.Player0,
        V05.GameResult result = V05.GameResult.Ongoing,
        V05.ReactionContext? reaction = null,
        V05.PendingChoiceView? choice = null,
        IReadOnlyList<V05.CardView?>? player0MainBoard = null,
        IReadOnlyList<V05.CardView?>? player1MainBoard = null,
        IReadOnlyList<V05.CardView?>? player0Tactics = null,
        IReadOnlyList<V05.CardView?>? player1Tactics = null,
        V05.CardView? player0Field = null,
        V05.CardView? player1Field = null)
    {
        ownHand ??= [];
        V05.PlayerView MakePlayer(V05.PlayerId player)
        {
            bool ownsHand = player == viewer;
            return new V05.PlayerView
            {
                Player = player,
                ProfessionId = player == V05.PlayerId.Player0 ? "oathguard" : "pactmage",
                LeaderHealth = 25,
                MaximumLeaderHealth = 25,
                CurrentPp = 6,
                PpCapacity = 6,
                Cracks = player == V05.PlayerId.Player0 ? 0 : 3,
                EvolutionEnergy = 2,
                OwnTurnNumber = 4,
                FatigueCount = 0,
                MulliganDone = player == V05.PlayerId.Player0
                    ? player0MulliganDone
                    : player1MulliganDone,
                EvolutionUsedThisTurn = false,
                AdvanceUsedThisTurn = false,
                DeployUsedThisTurn = false,
                TrapSetThisTurn = false,
                DeckCount = 26,
                HandCount = ownsHand ? (ulong)ownHand.Count : 4,
                Hand = ownsHand
                    ? ownHand.Select(id => Card(id, player, V05.Zone.Hand)).ToArray()
                    : [],
                MainBoard = (player == V05.PlayerId.Player0
                    ? player0MainBoard
                    : player1MainBoard)?.ToArray() ?? new V05.CardView?[5],
                Tactics = (player == V05.PlayerId.Player0
                    ? player0Tactics
                    : player1Tactics)?.ToArray() ?? new V05.CardView?[3],
                Field = player == V05.PlayerId.Player0 ? player0Field : player1Field,
                Graveyard = [],
                Archive = [],
                Standby = [],
            };
        }

        reaction ??= Reaction(revision);
        choice ??= NoChoice(revision);
        return new V05.MatchView
        {
            Viewer = viewer,
            ActivePlayer = activePlayer,
            FirstPlayer = firstPlayer,
            Phase = phase,
            Result = result,
            Revision = revision,
            Players = [MakePlayer(V05.PlayerId.Player0), MakePlayer(V05.PlayerId.Player1)],
            Reaction = reaction,
            PendingChoice = choice,
        };
    }

    internal static V05.ReactionContext Reaction(
        ulong revision,
        bool pending = false,
        V05.PlayerId responder = V05.PlayerId.Player0,
        V05.ReactionWindow window = V05.ReactionWindow.SpellDeclared) => new()
    {
        Pending = pending,
        Window = pending ? window : V05.ReactionWindow.None,
        Responder = responder,
        Subject = pending ? 900UL : 0UL,
        Origin = pending
            ? new V05.ReactionOrigin
            {
                Action = V05.ActionKind.CastSpell,
                Player = responder == V05.PlayerId.Player0
                    ? V05.PlayerId.Player1
                    : V05.PlayerId.Player0,
                Source = 900,
                Target = V05.Target.Leader(responder),
            }
            : null,
        Depth = pending ? 1UL : 0UL,
        EligibleCount = 0,
        EligibleTraps = [],
        Revision = revision,
    };

    internal static V05.PendingChoiceView NoChoice(ulong revision) => new()
    {
        Pending = false,
        Revision = revision,
    };

    internal static V05.PendingChoiceView Choice(
        ulong revision,
        V05.PlayerId chooser,
        V05.PendingChoiceKind kind,
        bool ordered,
        ulong minimum,
        ulong maximum,
        IReadOnlyList<string> optionIds,
        bool redacted = false) => redacted
        ? new V05.PendingChoiceView
        {
            Pending = true,
            Chooser = chooser,
            Revision = revision,
        }
        : new V05.PendingChoiceView
        {
            Pending = true,
            Chooser = chooser,
            ChoiceId = "choice-current",
            Kind = kind,
            MinimumSelections = minimum,
            MaximumSelections = maximum,
            Ordered = ordered,
            Options = optionIds.Select(id => new V05.PendingChoiceOptionView
            {
                OptionId = id,
                Label = $"选项 {id}",
            }).ToArray(),
            Revision = revision,
        };

    internal static V05.CardView Card(
        ulong instanceId,
        V05.PlayerId player,
        V05.Zone zone,
        string name = "产品测试牌",
        bool faceDown = false,
        V05.CardKind kind = V05.CardKind.Follower) => new()
    {
        InstanceId = instanceId,
        DesignId = instanceId % 2 == 0 ? "LO-TEST" : "AP-TEST",
        ProfessionId = player == V05.PlayerId.Player0 ? "oathguard" : "pactmage",
        SeriesId = player == V05.PlayerId.Player0 ? "luminous-oath" : "abyss-pact",
        Neutral = false,
        Kind = kind,
        Name = name,
        Owner = player,
        Controller = player,
        Zone = zone,
        Sequence = instanceId,
        Cost = 2,
        CurrentAttack = 2,
        CurrentHealth = 3,
        MaximumHealth = 3,
        PrintedKeywords = V05.Keyword.Guard,
        PermanentKeywords = V05.Keyword.None,
        TurnKeywords = V05.Keyword.None,
        Keywords = V05.Keyword.Guard,
        Evolved = false,
        AttackedThisTurn = false,
        EnteredThisTurn = false,
        FaceDown = faceDown,
        Countdown = kind is V05.CardKind.Amulet or V05.CardKind.Trap ? 2 : 0,
    };

    internal static V05.LegalAction Action(
        V05.PlayerId player,
        V05.ActionKind action,
        ulong revision,
        ulong source = 0,
        V05.Target? target = null,
        ulong? slot = null,
        string? modeId = null,
        bool useAdvance = false,
        IReadOnlyList<ulong>? mulliganCards = null,
        IReadOnlyList<string>? selectedOptionIds = null,
        IReadOnlyList<ulong>? additionalCostCards = null,
        string? choiceId = null,
        V05.PaymentPreview? payment = null) => new()
    {
        Command = new V05.GameCommandRequest(player, action, revision)
        {
            Source = source,
            Target = target,
            Slot = slot,
            ModeId = modeId,
            ChoiceId = choiceId,
            UseAdvance = useAdvance,
            MulliganCards = Array.AsReadOnly((mulliganCards ?? []).ToArray()),
            SelectedOptionIds = Array.AsReadOnly((selectedOptionIds ?? []).ToArray()),
            AdditionalCostCards = Array.AsReadOnly((additionalCostCards ?? []).ToArray()),
        },
        Payment = payment ?? Payment(),
    };

    internal static V05.PaymentPreview Payment(int baseCost = 0, bool usedAdvance = false) => new()
    {
        Status = Status(V05.EngineCode.Ok),
        CurrentPpBefore = 6,
        CurrentPpAfter = 6 - baseCost,
        PpCapacityBefore = 6,
        PpCapacityAfter = 6,
        CracksBefore = 0,
        CracksAfter = usedAdvance ? 1 : 0,
        EvolutionEnergyBefore = 2,
        EvolutionEnergyAfter = 2,
        BaseCost = baseCost,
        BurnCost = 0,
        AdvanceCost = usedAdvance ? 1 : 0,
        UsedAdvance = usedAdvance,
    };

    internal static V05.EngineStatus Status(V05.EngineCode code) => new()
    {
        RawCode = (uint)code,
        Message = code.ToString(),
    };

    internal static V05.EventBatch Events(
        ulong revision,
        ulong afterSequence,
        ulong lastSequence,
        V05.PlayerId player)
    {
        var events = new List<V05.GameEventView>();
        for (ulong sequence = afterSequence + 1; sequence <= lastSequence; ++sequence)
        {
            events.Add(new V05.GameEventView
            {
                Sequence = sequence,
                Type = V05.EventType.TurnStarted,
                Player = player,
                Value = 0,
                SecondaryValue = 0,
                HiddenCard = false,
                Text = $"安全事件 {sequence}",
            });
        }

        return new V05.EventBatch(revision, lastSequence, Array.AsReadOnly(events.ToArray()));
    }

    internal static V05.LegalActionsResult Filter(
        V05.ActionQueryRequest query,
        ulong revision,
        IEnumerable<V05.LegalAction> actions)
    {
        V05.LegalAction[] filtered = actions.Where(action =>
            action.Command.Player == query.Player &&
            (!query.Action.HasValue || action.Command.Action == query.Action.Value) &&
            (!query.Source.HasValue || action.Command.Source == query.Source.Value) &&
            (query.Target is null || Equals(action.Command.Target, query.Target)) &&
            (!query.Slot.HasValue || action.Command.Slot == query.Slot) &&
            (query.ModeId is null || string.Equals(
                action.Command.ModeId, query.ModeId, StringComparison.Ordinal)) &&
            (query.ChoiceId is null || string.Equals(
                action.Command.ChoiceId, query.ChoiceId, StringComparison.Ordinal)) &&
            (!query.UseAdvance.HasValue || action.Command.UseAdvance == query.UseAdvance.Value) &&
            (query.MulliganCards is null ||
                action.Command.MulliganCards.SequenceEqual(query.MulliganCards)) &&
            (query.SelectedOptionIds is null ||
                action.Command.SelectedOptionIds.SequenceEqual(query.SelectedOptionIds)) &&
            (query.AdditionalCostCards is null ||
                action.Command.AdditionalCostCards.SequenceEqual(query.AdditionalCostCards))).ToArray();
        return new V05.LegalActionsResult(revision, Array.AsReadOnly(filtered));
    }

    internal static FakeProductGameSession ActionSession(
        ulong revision,
        IReadOnlyList<V05.LegalAction> actions,
        V05.MatchPhase phase = V05.MatchPhase.Action,
        V05.PlayerId active = V05.PlayerId.Player0,
        V05.ReactionContext? reaction = null,
        V05.PendingChoiceView? choice = null,
        IReadOnlyList<ulong>? hand = null) => new()
    {
        ViewHandler = viewer => View(
            viewer,
            revision,
            phase,
            active,
            ownHand: hand ?? [10],
            reaction: reaction,
            choice: choice),
        ActionsHandler = query => Filter(query, revision, actions),
        PaymentHandler = command => new V05.PaymentResult(
            revision,
            actions.Single(action => CommandsEqual(action.Command, command)).Payment),
        SubmitHandler = _ => Status(V05.EngineCode.Ok),
        EventsHandler = (viewer, after) => Events(revision, after, after, viewer),
    };

    internal static bool CommandsEqual(V05.GameCommandRequest left, V05.GameCommandRequest right) =>
        left.Player == right.Player &&
        left.Action == right.Action &&
        left.ExpectedRevision == right.ExpectedRevision &&
        left.Source == right.Source &&
        Equals(left.Target, right.Target) &&
        left.Slot == right.Slot &&
        string.Equals(left.ModeId, right.ModeId, StringComparison.Ordinal) &&
        string.Equals(left.ChoiceId, right.ChoiceId, StringComparison.Ordinal) &&
        left.UseAdvance == right.UseAdvance &&
        left.MulliganCards.SequenceEqual(right.MulliganCards) &&
        left.SelectedOptionIds.SequenceEqual(right.SelectedOptionIds) &&
        left.AdditionalCostCards.SequenceEqual(right.AdditionalCostCards);
}
