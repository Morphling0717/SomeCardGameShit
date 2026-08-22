// SPDX-License-Identifier: GPL-3.0-or-later
using Scgs.Client;

namespace Scgs.Hotseat;

public enum HotseatUiMode
{
    Covered,
    MulliganSelecting,
    MulliganReview,
    Action,
    Reaction,
    Finished,
    Faulted,
    Disposed,
    Resolving,
}

public enum HotseatSelectionStep
{
    None,
    ChooseAction,
    ChooseDonor,
    ChooseSlot,
    ChooseTarget,
    ChooseAdvance,
    Ready,
}

public enum HotseatCoverReason
{
    InitialReveal,
    PassingDevice,
    ResolvingCommand,
}

public readonly record struct HotseatEventCursors(ulong Player0, ulong Player1)
{
    public ulong For(PlayerId player) => player switch
    {
        PlayerId.Player0 => Player0,
        PlayerId.Player1 => Player1,
        _ => throw new ArgumentOutOfRangeException(
            nameof(player),
            player,
            "Unsupported player value."),
    };

    internal HotseatEventCursors With(PlayerId player, ulong value) => player switch
    {
        PlayerId.Player0 => this with { Player0 = value },
        PlayerId.Player1 => this with { Player1 = value },
        _ => throw new ArgumentOutOfRangeException(
            nameof(player),
            player,
            "Unsupported player value."),
    };
}

public sealed record HotseatActionSelection
{
    public ActionKind? Action { get; init; }

    public ulong? Source { get; init; }

    public bool HasTarget { get; init; }

    public Target? Target { get; init; }

    public bool HasSlot { get; init; }

    public ulong? Slot { get; init; }

    public bool HasDonor { get; init; }

    public ulong? Donor { get; init; }

    public bool HasAdvanceChoice { get; init; }

    public bool UseAdvance { get; init; }

    public static HotseatActionSelection Empty { get; } = new();
}

public sealed record HotseatCandidateOptions
{
    internal HotseatCandidateOptions(IReadOnlyList<LegalAction> actions)
    {
        Actions = Freeze(actions);
        ActionKinds = Freeze(actions.Select(item => item.Command.Action).Distinct());
        Sources = Freeze(actions.Select(item => item.Command.Source).Distinct());
        Targets = FreezeDistinct(actions.Select(item => item.Command.Target));
        Slots = Freeze(actions.Select(item => item.Command.Slot).Distinct());
        Donors = Freeze(actions.Select(item => item.Command.ComponentDonor).Distinct());
        AdvanceChoices = Freeze(actions.Select(item => item.Command.UseAdvance).Distinct());
    }

    public IReadOnlyList<LegalAction> Actions { get; }

    public IReadOnlyList<ActionKind> ActionKinds { get; }

    public IReadOnlyList<ulong> Sources { get; }

    public IReadOnlyList<Target?> Targets { get; }

    public IReadOnlyList<ulong?> Slots { get; }

    public IReadOnlyList<ulong?> Donors { get; }

    public IReadOnlyList<bool> AdvanceChoices { get; }

    internal static HotseatCandidateOptions Empty { get; } = new(Array.Empty<LegalAction>());

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) =>
        Array.AsReadOnly(values.ToArray());

    private static IReadOnlyList<Target?> FreezeDistinct(IEnumerable<Target?> values)
    {
        var distinct = new List<Target?>();
        foreach (Target? value in values)
        {
            if (!distinct.Any(existing => Equals(existing, value)))
            {
                distinct.Add(value);
            }
        }

        return Array.AsReadOnly(distinct.ToArray());
    }
}

public sealed record HotseatInteractionContext
{
    internal HotseatInteractionContext(
        ulong revision,
        HotseatSelectionStep step,
        HotseatActionSelection selection,
        HotseatCandidateOptions options,
        PaymentPreview? payment,
        LegalAction? canonicalAction,
        bool canStepBack)
    {
        Revision = revision;
        Step = step;
        Selection = selection;
        Options = options;
        Payment = payment ?? InvariantPayment(options.Actions);
        CanonicalAction = canonicalAction;
        CanStepBack = canStepBack;
    }

    public ulong Revision { get; }

    public HotseatSelectionStep Step { get; }

    public HotseatActionSelection Selection { get; }

    public HotseatCandidateOptions Options { get; }

    public ActionKind? Action => Selection.Action;

    public ulong? Source => Selection.Source;

    public IReadOnlyList<ActionKind> Actions => Options.ActionKinds;

    public IReadOnlyList<Target?> Targets => Options.Targets;

    public IReadOnlyList<ulong?> Slots => Options.Slots;

    public IReadOnlyList<ulong?> Donors => Options.Donors;

    public IReadOnlyList<bool> AdvanceChoices => Options.AdvanceChoices;

    public PaymentPreview? Payment { get; }

    public LegalAction? CanonicalAction { get; }

    public bool CanStepBack { get; }

    private static PaymentPreview? InvariantPayment(IReadOnlyList<LegalAction> actions)
    {
        if (actions.Count == 0)
        {
            return null;
        }

        PaymentPreview first = actions[0].Payment;
        return actions.All(action => PaymentsEqual(first, action.Payment)) ? first : null;
    }

    private static bool PaymentsEqual(PaymentPreview left, PaymentPreview right) =>
        left.Status.RawCode == right.Status.RawCode &&
        string.Equals(left.Status.Message, right.Status.Message, StringComparison.Ordinal) &&
        left.CurrentPpBefore == right.CurrentPpBefore &&
        left.CurrentPpAfter == right.CurrentPpAfter &&
        left.PpCapacityBefore == right.PpCapacityBefore &&
        left.PpCapacityAfter == right.PpCapacityAfter &&
        left.CracksBefore == right.CracksBefore &&
        left.CracksAfter == right.CracksAfter &&
        left.EvolutionEnergyBefore == right.EvolutionEnergyBefore &&
        left.EvolutionEnergyAfter == right.EvolutionEnergyAfter &&
        left.BaseCost == right.BaseCost &&
        left.BurnCost == right.BurnCost &&
        left.AdvanceCost == right.AdvanceCost &&
        left.UsedAdvance == right.UsedAdvance;
}

public sealed record HotseatPublicCardView
{
    internal HotseatPublicCardView(CardView card, bool hideIdentity)
    {
        InstanceId = hideIdentity ? null : card.InstanceId;
        DefinitionId = hideIdentity ? null : card.DefinitionId;
        Kind = hideIdentity ? null : card.Kind;
        Name = hideIdentity ? string.Empty : card.Name;
        Owner = card.Owner;
        Controller = card.Controller;
        Zone = card.Zone;
        Cost = hideIdentity ? 0 : card.Cost;
        CurrentAttack = hideIdentity ? 0 : card.CurrentAttack;
        CurrentHealth = hideIdentity ? 0 : card.CurrentHealth;
        MaximumHealth = hideIdentity ? 0 : card.MaximumHealth;
        Keywords = hideIdentity ? Keyword.None : card.Keywords;
        Evolved = !hideIdentity && card.Evolved;
        AttackedThisTurn = !hideIdentity && card.AttackedThisTurn;
        EnteredThisTurn = !hideIdentity && card.EnteredThisTurn;
        TemporaryRush = !hideIdentity && card.TemporaryRush;
        DeployedFromStandby = !hideIdentity && card.DeployedFromStandby;
        FaceDown = card.FaceDown;
        Countdown = hideIdentity ? 0 : card.Countdown;
    }

    public ulong? InstanceId { get; }

    public uint? DefinitionId { get; }

    public CardKind? Kind { get; }

    public string Name { get; }

    public PlayerId Owner { get; }

    public PlayerId Controller { get; }

    public Zone Zone { get; }

    public int Cost { get; }

    public int CurrentAttack { get; }

    public int CurrentHealth { get; }

    public int MaximumHealth { get; }

    public Keyword Keywords { get; }

    public bool Evolved { get; }

    public bool AttackedThisTurn { get; }

    public bool EnteredThisTurn { get; }

    public bool TemporaryRush { get; }

    public bool DeployedFromStandby { get; }

    public bool FaceDown { get; }

    public int Countdown { get; }

    public bool HasKnownIdentity => InstanceId.HasValue && DefinitionId.HasValue;
}

public sealed record HotseatPublicPlayerView
{
    internal HotseatPublicPlayerView(PlayerView player)
    {
        Player = player.Player;
        LeaderHealth = player.LeaderHealth;
        MaximumLeaderHealth = player.MaximumLeaderHealth;
        CurrentPp = player.CurrentPp;
        PpCapacity = player.PpCapacity;
        Cracks = player.Cracks;
        EvolutionEnergy = player.EvolutionEnergy;
        OwnTurnNumber = player.OwnTurnNumber;
        FatigueCount = player.FatigueCount;
        MulliganDone = player.MulliganDone;
        EvolutionUsedThisTurn = player.EvolutionUsedThisTurn;
        AdvanceUsedThisTurn = player.AdvanceUsedThisTurn;
        DeployUsedThisTurn = player.DeployUsedThisTurn;
        TrapSetThisTurn = player.TrapSetThisTurn;
        DeckCount = player.DeckCount;
        HandCount = player.HandCount;
        Units = ProjectSlots(player.Units);
        Tactics = ProjectSlots(player.Tactics);
        Graveyard = ProjectCards(player.Graveyard);
        Archive = ProjectCards(player.Archive);
        Standby = ProjectCards(player.Standby);
    }

    public PlayerId Player { get; }

    public int LeaderHealth { get; }

    public int MaximumLeaderHealth { get; }

    public int CurrentPp { get; }

    public int PpCapacity { get; }

    public int Cracks { get; }

    public int EvolutionEnergy { get; }

    public int OwnTurnNumber { get; }

    public int FatigueCount { get; }

    public bool MulliganDone { get; }

    public bool EvolutionUsedThisTurn { get; }

    public bool AdvanceUsedThisTurn { get; }

    public bool DeployUsedThisTurn { get; }

    public bool TrapSetThisTurn { get; }

    public ulong DeckCount { get; }

    public ulong HandCount { get; }

    public IReadOnlyList<HotseatPublicCardView?> Units { get; }

    public IReadOnlyList<HotseatPublicCardView?> Tactics { get; }

    public IReadOnlyList<HotseatPublicCardView> Graveyard { get; }

    public IReadOnlyList<HotseatPublicCardView> Archive { get; }

    public IReadOnlyList<HotseatPublicCardView> Standby { get; }

    private static IReadOnlyList<HotseatPublicCardView?> ProjectSlots(
        IEnumerable<CardView?> cards) => Array.AsReadOnly(cards
            .Select(card => card is null
                ? null
                : new HotseatPublicCardView(card, card.FaceDown))
            .ToArray());

    private static IReadOnlyList<HotseatPublicCardView> ProjectCards(
        IEnumerable<CardView> cards) => Array.AsReadOnly(cards
            .Select(card => new HotseatPublicCardView(card, card.FaceDown))
            .ToArray());
}

public sealed record HotseatPublicReactionView
{
    internal HotseatPublicReactionView(ReactionContext reaction)
    {
        Pending = reaction.Pending;
        Window = reaction.Window;
        Responder = reaction.Responder;
        Depth = reaction.Depth;
    }

    public bool Pending { get; }

    public ReactionWindow Window { get; }

    public PlayerId Responder { get; }

    public ulong Depth { get; }
}

public sealed record HotseatPublicBoardView
{
    internal HotseatPublicBoardView(MatchView view)
    {
        if (view.Players.Length != 2)
        {
            throw new ScgsProtocolException(
                "A hot-seat public board requires exactly two players.");
        }

        ActivePlayer = view.ActivePlayer;
        FirstPlayer = view.FirstPlayer;
        RandomSeed = view.RandomSeed;
        Phase = view.Phase;
        Result = view.Result;
        Revision = view.Revision;
        Players = Array.AsReadOnly(view.Players
            .Select(player => new HotseatPublicPlayerView(player))
            .ToArray());
        Reaction = new HotseatPublicReactionView(view.Reaction);
    }

    public PlayerId ActivePlayer { get; }

    public PlayerId FirstPlayer { get; }

    public uint RandomSeed { get; }

    public MatchPhase Phase { get; }

    public GameResult Result { get; }

    public ulong Revision { get; }

    public IReadOnlyList<HotseatPublicPlayerView> Players { get; }

    public HotseatPublicReactionView Reaction { get; }
}

public sealed record HotseatUiState
{
    internal HotseatUiState(
        HotseatUiMode mode,
        HotseatCoverReason? coverReason,
        PlayerId? viewer,
        PlayerId? awaitingPlayer,
        MatchView? snapshot,
        IReadOnlyList<LegalAction> legalActions,
        IReadOnlyList<LegalAction> candidateActions,
        HotseatActionSelection selection,
        IReadOnlyList<ulong> mulliganCards,
        LegalAction? selectedAction,
        IReadOnlyList<GameEventView> events,
        IReadOnlyList<GameEventView> pendingEvents,
        ulong? pendingEventLastSequence,
        HotseatEventCursors eventCursors,
        uint? lastEngineCode,
        string? failureText,
        bool commandPrepared,
        HotseatPublicBoardView? publicBoard = null,
        HotseatSelectionStep selectionStep = HotseatSelectionStep.None,
        bool canStepBack = false)
    {
        Mode = mode;
        CoverReason = coverReason;
        Viewer = viewer;
        AwaitingPlayer = awaitingPlayer;
        Snapshot = snapshot;
        LegalActions = Freeze(legalActions);
        Selection = selection;
        MulliganCards = Freeze(mulliganCards);
        SelectedAction = selectedAction;
        Events = Freeze(events);
        PendingEvents = Freeze(pendingEvents);
        PendingEventLastSequence = pendingEventLastSequence;
        EventCursors = eventCursors;
        LastEngineCode = lastEngineCode;
        FailureText = failureText;
        CommandPrepared = commandPrepared;
        CandidateOptions = new HotseatCandidateOptions(candidateActions);
        PublicBoard = publicBoard;
        Interaction = new HotseatInteractionContext(
            snapshot?.Revision ?? publicBoard?.Revision ?? 0,
            selectionStep,
            selection,
            CandidateOptions,
            selectedAction?.Payment,
            selectedAction,
            canStepBack);
    }

    public HotseatUiMode Mode { get; }

    public HotseatCoverReason? CoverReason { get; }

    public PlayerId? Viewer { get; }

    public PlayerId? AwaitingPlayer { get; }

    public MatchView? Snapshot { get; }

    public IReadOnlyList<LegalAction> LegalActions { get; }

    public HotseatCandidateOptions CandidateOptions { get; }

    public HotseatActionSelection Selection { get; }

    public IReadOnlyList<ulong> MulliganCards { get; }

    public LegalAction? SelectedAction { get; }

    public IReadOnlyList<GameEventView> Events { get; }

    public IReadOnlyList<GameEventView> PendingEvents { get; }

    public ulong? PendingEventLastSequence { get; }

    public HotseatEventCursors EventCursors { get; }

    public uint? LastEngineCode { get; }

    public string? FailureText { get; }

    public bool CommandPrepared { get; }

    public HotseatPublicBoardView? PublicBoard { get; }

    public HotseatInteractionContext Interaction { get; }

    public bool IsCovered => Mode == HotseatUiMode.Covered;

    public bool CanConfirm => SelectedAction is not null &&
                              Interaction.Step == HotseatSelectionStep.Ready &&
                              !CommandPrepared;

    public bool HasUnacknowledgedEvents => PendingEventLastSequence.HasValue;

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) =>
        Array.AsReadOnly(values.ToArray());
}

public sealed class HotseatStateChangedEventArgs : EventArgs
{
    public HotseatStateChangedEventArgs(HotseatUiState state) => State = state;

    public HotseatUiState State { get; }
}
