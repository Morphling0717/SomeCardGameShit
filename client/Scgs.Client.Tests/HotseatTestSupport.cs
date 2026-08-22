// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Scgs.Client.Tests;

internal sealed class FakeGameSession : IScgsGameSession
{
    internal Func<PlayerId, MatchView> ViewHandler { get; set; } =
        _ => throw new NotSupportedException();

    internal Func<ActionQueryRequest, LegalActionsResult> ActionsHandler { get; set; } =
        _ => throw new NotSupportedException();

    internal Func<GameCommandRequest, PaymentResult> PaymentHandler { get; set; } =
        _ => throw new NotSupportedException();

    internal Func<GameCommandRequest, EngineStatus> SubmitHandler { get; set; } =
        _ => throw new NotSupportedException();

    internal Func<PlayerId, ulong, EventBatch> EventsHandler { get; set; } =
        (_, _) => throw new NotSupportedException();

    internal List<string> Calls { get; } = [];

    internal List<ActionQueryRequest> Queries { get; } = [];

    internal List<GameCommandRequest> SubmittedCommands { get; } = [];

    internal int DisposeCalls { get; private set; }

    public MatchView GetView(PlayerId viewer)
    {
        Calls.Add($"view:{viewer}");
        return ViewHandler(viewer);
    }

    public LegalActionsResult ListLegalActions(ActionQueryRequest query)
    {
        Calls.Add($"actions:{query.Player}:{query.ExpectedRevision}");
        Queries.Add(query);
        return ActionsHandler(query);
    }

    public PaymentResult PreviewPayment(GameCommandRequest command)
    {
        Calls.Add($"payment:{command.Player}:{command.ExpectedRevision}");
        return PaymentHandler(command);
    }

    public EngineStatus SubmitCommand(GameCommandRequest command)
    {
        Calls.Add($"submit:{command.Player}:{command.Action}:{command.ExpectedRevision}");
        SubmittedCommands.Add(command);
        return SubmitHandler(command);
    }

    public EventBatch ReadEvents(PlayerId viewer, ulong afterSequence)
    {
        Calls.Add($"events:{viewer}:{afterSequence}");
        return EventsHandler(viewer, afterSequence);
    }

    public void Dispose()
    {
        ++DisposeCalls;
        Calls.Add("dispose");
    }

    public EngineStatus Start() => throw new NotSupportedException();

    public ValidTargetsResult ListValidTargets(ActionQueryRequest query) =>
        throw new NotSupportedException();

    public ValidSlotsResult ListValidSlots(ActionQueryRequest query) =>
        throw new NotSupportedException();

    public ValidDonorsResult ListValidDonors(ActionQueryRequest query) =>
        throw new NotSupportedException();

    public ReactionContext GetReactionContext(PlayerId viewer) =>
        throw new NotSupportedException();

    public EventBatch ReadNewEvents(PlayerId viewer) =>
        throw new AssertFailedException("The hot-seat controller must own explicit cursors.");

    public ulong GetEventCursor(PlayerId viewer) =>
        throw new AssertFailedException("The hot-seat controller must own explicit cursors.");
}

internal static class HotseatTestModel
{
    internal static MatchView View(
        PlayerId viewer,
        ulong revision,
        MatchPhase phase,
        PlayerId activePlayer,
        bool player0MulliganDone,
        bool player1MulliganDone,
        IReadOnlyList<ulong>? ownHand = null,
        PlayerId firstPlayer = PlayerId.Player0,
        PlayerId responder = PlayerId.Player0,
        GameResult result = GameResult.Ongoing,
        ReactionOrigin? origin = null,
        IReadOnlyList<CardView?>? player0Units = null,
        IReadOnlyList<CardView?>? player1Units = null,
        IReadOnlyList<CardView?>? player0Tactics = null,
        IReadOnlyList<CardView?>? player1Tactics = null)
    {
        ownHand ??= [];
        PlayerView MakePlayer(PlayerId player) => new()
        {
            Player = player,
            LeaderHealth = 20,
            MaximumLeaderHealth = 20,
            CurrentPp = 4,
            PpCapacity = 4,
            Cracks = 0,
            EvolutionEnergy = 0,
            OwnTurnNumber = 1,
            FatigueCount = 0,
            MulliganDone = player == PlayerId.Player0
                ? player0MulliganDone
                : player1MulliganDone,
            EvolutionUsedThisTurn = false,
            AdvanceUsedThisTurn = false,
            DeployUsedThisTurn = false,
            TrapSetThisTurn = false,
            LeaderSkillUsed = false,
            ChargeGrantedThisCycle = false,
            FriendlyDeathsThisCycle = 0,
            SpellsUsedThisTurn = 0,
            UnitsPlayedThisTurn = 0,
            LeaderSkill = new LeaderSkillDefinition
            {
                Name = "测试主战技",
                Cost = 0,
                Effects = [],
            },
            DeckCount = 37,
            HandCount = (ulong)ownHand.Count,
            Hand = player == viewer
                ? ownHand.Select(id => Card(id, player, Zone.Hand)).ToArray()
                : [],
            Units = (player == PlayerId.Player0 ? player0Units : player1Units)?.ToArray() ??
                new CardView?[5],
            Tactics = (player == PlayerId.Player0 ? player0Tactics : player1Tactics)?.ToArray() ??
                new CardView?[3],
            Graveyard = [],
            Archive = [],
            Standby = [],
        };

        bool pending = phase == MatchPhase.Reaction;
        var reaction = new ReactionContext
        {
            Pending = pending,
            Window = pending ? ReactionWindow.SpellDeclared : ReactionWindow.None,
            Responder = responder,
            Subject = pending ? 500UL : 0UL,
            Origin = pending
                ? origin ?? new ReactionOrigin
                {
                    Action = ActionKind.CastSpell,
                    Player = activePlayer,
                    Source = 500,
                    Target = Target.Leader(
                        activePlayer == PlayerId.Player0
                            ? PlayerId.Player1
                            : PlayerId.Player0),
                }
                : null,
            Depth = pending ? 1UL : 0UL,
            EligibleCount = 0,
            EligibleTraps = [],
            Revision = revision,
        };

        return new MatchView
        {
            Viewer = viewer,
            ActivePlayer = activePlayer,
            FirstPlayer = firstPlayer,
            RandomSeed = 123,
            Phase = phase,
            Result = result,
            Revision = revision,
            Players = [MakePlayer(PlayerId.Player0), MakePlayer(PlayerId.Player1)],
            Reaction = reaction,
        };
    }

    internal static LegalAction Action(
        PlayerId player,
        ActionKind action,
        ulong revision,
        ulong source = 0,
        Target? target = null,
        ulong? slot = null,
        ulong? donor = null,
        bool useAdvance = false,
        IReadOnlyList<ulong>? mulliganCards = null,
        PaymentPreview? payment = null) => new()
        {
            Command = new GameCommandRequest(player, action, revision)
            {
                Source = source,
                Target = target,
                Slot = slot,
                ComponentDonor = donor,
                UseAdvance = useAdvance,
                MulliganCards = Array.AsReadOnly((mulliganCards ?? []).ToArray()),
            },
            Payment = payment ?? Payment(),
        };

    internal static PaymentPreview Payment(int baseCost = 0, bool usedAdvance = false) => new()
    {
        Status = Status(EngineCode.Ok),
        CurrentPpBefore = 4,
        CurrentPpAfter = 4 - baseCost,
        PpCapacityBefore = 4,
        PpCapacityAfter = 4,
        CracksBefore = 0,
        CracksAfter = 0,
        EvolutionEnergyBefore = 0,
        EvolutionEnergyAfter = 0,
        BaseCost = baseCost,
        BurnCost = 0,
        AdvanceCost = usedAdvance ? 1 : 0,
        UsedAdvance = usedAdvance,
    };

    internal static EngineStatus Status(EngineCode code) => new()
    {
        RawCode = (uint)code,
        Message = code.ToString(),
    };

    internal static EventBatch Events(
        ulong revision,
        ulong afterSequence,
        ulong lastSequence,
        PlayerId player)
    {
        GameEventView[] events = Enumerable.Range(
                checked((int)afterSequence + 1),
                checked((int)(lastSequence - afterSequence)))
            .Select(sequence => new GameEventView
            {
                Sequence = (ulong)sequence,
                Type = EventType.TurnStarted,
                Player = player,
                Value = 0,
                SecondaryValue = 0,
                HiddenCard = false,
                Text = $"安全事件 {sequence}",
            }).ToArray();
        return new EventBatch(revision, lastSequence, Array.AsReadOnly(events));
    }

    internal static LegalActionsResult Filter(
        ActionQueryRequest query,
        ulong revision,
        IEnumerable<LegalAction> actions)
    {
        LegalAction[] filtered = actions.Where(action =>
            action.Command.Player == query.Player &&
            (!query.Action.HasValue || action.Command.Action == query.Action.Value) &&
            (!query.Source.HasValue || action.Command.Source == query.Source.Value) &&
            (query.Target is null || Equals(action.Command.Target, query.Target)) &&
            (!query.Slot.HasValue || action.Command.Slot == query.Slot) &&
            (!query.ComponentDonor.HasValue ||
                action.Command.ComponentDonor == query.ComponentDonor) &&
            (!query.UseAdvance.HasValue ||
                action.Command.UseAdvance == query.UseAdvance.Value) &&
            (query.MulliganCards is null || query.MulliganCards.Count == 0 ||
                action.Command.MulliganCards.SequenceEqual(query.MulliganCards))).ToArray();
        return new LegalActionsResult(revision, Array.AsReadOnly(filtered));
    }

    internal static CardView Card(
        ulong instanceId,
        PlayerId player,
        Zone zone,
        string name = "测试牌",
        bool faceDown = false)
    {
        var definition = new CardDefinition
        {
            Id = 1,
            Name = name,
            Kind = CardKind.Unit,
            Cost = 1,
            Attack = 1,
            Health = 1,
            Countdown = 0,
            PrintedGuard = false,
            PrintedRush = false,
            PrintedStorm = false,
            PrintedBarrier = false,
            PrintedLifesteal = false,
            PrintedBane = false,
            EvolvedAttack = 0,
            EvolvedHealth = 0,
            AdditionalCost = new AdditionalCost { BurnPpCapacity = 0 },
            Component = new ComponentSpec
            {
                HasComponent = false,
                GrantedKind = EffectKind.DrawCards,
                GrantedAmount = 0,
            },
            Effects = [],
        };
        return new CardView
        {
            InstanceId = instanceId,
            DefinitionId = definition.Id,
            Definition = definition,
            Kind = definition.Kind,
            Name = definition.Name,
            Owner = player,
            Controller = player,
            Zone = zone,
            Sequence = instanceId,
            Cost = definition.Cost,
            CurrentAttack = definition.Attack,
            CurrentHealth = definition.Health,
            MaximumHealth = definition.Health,
            Keywords = Keyword.None,
            Evolved = false,
            AttackedThisTurn = false,
            EnteredThisTurn = false,
            TemporaryRush = false,
            DeployedFromStandby = false,
            FaceDown = faceDown,
            Countdown = 0,
            GrantedComponent = definition.Component,
        };
    }
}
