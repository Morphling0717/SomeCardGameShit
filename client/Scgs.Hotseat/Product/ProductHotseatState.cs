// SPDX-License-Identifier: GPL-3.0-or-later
using Scgs.Client;
using V05 = Scgs.Client.V05;

namespace Scgs.Hotseat.Product;

public enum ProductHotseatUiMode
{
    Covered,
    MulliganSelecting,
    MulliganReview,
    Action,
    Reaction,
    Choice,
    Resolving,
    Finished,
    Faulted,
    Disposed,
    Presenting,
}

public enum ProductHotseatSelectionStep
{
    None,
    ChooseSource,
    ChooseAction,
    ChooseMode,
    ChooseAdditionalCost,
    ChooseSlot,
    ChooseTarget,
    ChooseAdvance,
    ChooseChoiceOptions,
    Ready,
}

public enum ProductHotseatCoverReason
{
    InitialReveal,
    PassingDevice,
    FailedCommand,
}

public readonly record struct ProductHotseatEventCursors(ulong Player0, ulong Player1)
{
    public ulong For(V05.PlayerId player) => player switch
    {
        V05.PlayerId.Player0 => Player0,
        V05.PlayerId.Player1 => Player1,
        _ => throw new ArgumentOutOfRangeException(nameof(player), player, "Unsupported player."),
    };

    internal ProductHotseatEventCursors With(V05.PlayerId player, ulong value) => player switch
    {
        V05.PlayerId.Player0 => this with { Player0 = value },
        V05.PlayerId.Player1 => this with { Player1 = value },
        _ => throw new ArgumentOutOfRangeException(nameof(player), player, "Unsupported player."),
    };
}

public sealed record ProductHotseatActionSelection
{
    public V05.ActionKind? Action { get; init; }

    public bool HasSource { get; init; }

    public ulong Source { get; init; }

    public bool HasMode { get; init; }

    public string? ModeId { get; init; }

    public bool HasAdditionalCost { get; init; }

    public IReadOnlyList<ulong> AdditionalCostCards { get; init; } = Array.Empty<ulong>();

    public bool HasSlot { get; init; }

    public ulong? Slot { get; init; }

    public bool HasTarget { get; init; }

    public V05.Target? Target { get; init; }

    public bool HasAdvanceChoice { get; init; }

    public bool UseAdvance { get; init; }

    public static ProductHotseatActionSelection Empty { get; } = new();

    internal ProductHotseatActionSelection Freeze() => this with
    {
        AdditionalCostCards = Array.AsReadOnly(AdditionalCostCards.ToArray()),
    };
}

public sealed record ProductHotseatCandidateOptions
{
    internal ProductHotseatCandidateOptions(IEnumerable<V05.LegalAction> actions)
    {
        Actions = Freeze(actions);
        ActionKinds = Freeze(Actions.Select(item => item.Command.Action).Distinct());
        Sources = Freeze(Actions.Select(item => item.Command.Source).Distinct());
        ModeIds = FreezeDistinct(Actions.Select(item => item.Command.ModeId));
        AdditionalCostSets = FreezeDistinctLists(
            Actions.Select(item => item.Command.AdditionalCostCards));
        Slots = Freeze(Actions.Select(item => item.Command.Slot).Distinct());
        Targets = FreezeDistinct(Actions.Select(item => item.Command.Target));
        AdvanceChoices = Freeze(Actions.Select(item => item.Command.UseAdvance).Distinct());
    }

    public IReadOnlyList<V05.LegalAction> Actions { get; }

    public IReadOnlyList<V05.ActionKind> ActionKinds { get; }

    public IReadOnlyList<ulong> Sources { get; }

    public IReadOnlyList<string?> ModeIds { get; }

    public IReadOnlyList<IReadOnlyList<ulong>> AdditionalCostSets { get; }

    public IReadOnlyList<ulong?> Slots { get; }

    public IReadOnlyList<V05.Target?> Targets { get; }

    public IReadOnlyList<bool> AdvanceChoices { get; }

    internal static ProductHotseatCandidateOptions Empty { get; } = new([]);

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) =>
        Array.AsReadOnly(values.ToArray());

    private static IReadOnlyList<T?> FreezeDistinct<T>(IEnumerable<T?> values)
    {
        var distinct = new List<T?>();
        foreach (T? value in values)
        {
            if (!distinct.Any(existing => Equals(existing, value)))
            {
                distinct.Add(value);
            }
        }

        return Array.AsReadOnly(distinct.ToArray());
    }

    private static IReadOnlyList<IReadOnlyList<ulong>> FreezeDistinctLists(
        IEnumerable<IReadOnlyList<ulong>> values)
    {
        var result = new List<IReadOnlyList<ulong>>();
        foreach (IReadOnlyList<ulong> value in values)
        {
            if (!result.Any(existing => existing.SequenceEqual(value)))
            {
                result.Add(Array.AsReadOnly(value.ToArray()));
            }
        }

        return Array.AsReadOnly(result.ToArray());
    }
}

public sealed record ProductPendingChoiceState
{
    internal ProductPendingChoiceState(
        bool pending,
        bool waitingForOpponent,
        V05.PlayerId? chooser,
        string? choiceId,
        V05.PendingChoiceKind? kind,
        ulong minimumSelections,
        ulong maximumSelections,
        bool ordered,
        IEnumerable<V05.PendingChoiceOptionView> options,
        IEnumerable<string> selectedOptionIds,
        ulong revision)
    {
        Pending = pending;
        WaitingForOpponent = waitingForOpponent;
        Chooser = chooser;
        ChoiceId = choiceId;
        Kind = kind;
        MinimumSelections = minimumSelections;
        MaximumSelections = maximumSelections;
        Ordered = ordered;
        Options = Array.AsReadOnly(options.ToArray());
        SelectedOptionIds = Array.AsReadOnly(selectedOptionIds.ToArray());
        Revision = revision;
    }

    public bool Pending { get; }

    public bool WaitingForOpponent { get; }

    public V05.PlayerId? Chooser { get; }

    public string? ChoiceId { get; }

    public V05.PendingChoiceKind? Kind { get; }

    public ulong MinimumSelections { get; }

    public ulong MaximumSelections { get; }

    public bool Ordered { get; }

    public IReadOnlyList<V05.PendingChoiceOptionView> Options { get; }

    public IReadOnlyList<string> SelectedOptionIds { get; }

    public ulong Revision { get; }

    public bool RequiresInput => Pending && !WaitingForOpponent;

    public bool SatisfiesBounds =>
        (ulong)SelectedOptionIds.Count >= MinimumSelections &&
        (ulong)SelectedOptionIds.Count <= MaximumSelections;

    internal ProductPendingChoiceState WithSelection(IEnumerable<string> optionIds) => new(
        Pending,
        WaitingForOpponent,
        Chooser,
        ChoiceId,
        Kind,
        MinimumSelections,
        MaximumSelections,
        Ordered,
        Options,
        optionIds,
        Revision);

    internal static ProductPendingChoiceState None(ulong revision) => new(
        false,
        false,
        null,
        null,
        null,
        0,
        0,
        false,
        [],
        [],
        revision);
}

public sealed record ProductHotseatInteractionContext
{
    internal ProductHotseatInteractionContext(
        ulong revision,
        ProductHotseatSelectionStep step,
        ProductHotseatActionSelection selection,
        IEnumerable<V05.LegalAction> candidates,
        V05.LegalAction? canonicalAction,
        bool canStepBack)
    {
        Revision = revision;
        Step = step;
        Selection = selection.Freeze();
        Options = new ProductHotseatCandidateOptions(candidates);
        CanonicalAction = canonicalAction;
        Payment = canonicalAction?.Payment ?? InvariantPayment(Options.Actions);
        CanStepBack = canStepBack;
    }

    public ulong Revision { get; }

    public ProductHotseatSelectionStep Step { get; }

    public ProductHotseatActionSelection Selection { get; }

    public ProductHotseatCandidateOptions Options { get; }

    public V05.LegalAction? CanonicalAction { get; }

    public V05.PaymentPreview? Payment { get; }

    public bool CanStepBack { get; }

    private static V05.PaymentPreview? InvariantPayment(IReadOnlyList<V05.LegalAction> actions)
    {
        if (actions.Count == 0)
        {
            return null;
        }

        V05.PaymentPreview first = actions[0].Payment;
        return actions.All(action => ProductHotseatCommandComparer.PaymentsEqual(
            first,
            action.Payment))
            ? first
            : null;
    }
}

public sealed record ProductHotseatPublicCardView
{
    internal ProductHotseatPublicCardView(V05.CardView card, bool hideIdentity)
    {
        InstanceId = hideIdentity ? null : card.InstanceId;
        DesignId = hideIdentity ? null : card.DesignId;
        ProfessionId = hideIdentity ? null : card.ProfessionId;
        SeriesId = hideIdentity ? null : card.SeriesId;
        Neutral = hideIdentity ? null : card.Neutral;
        Kind = hideIdentity ? null : card.Kind;
        Name = hideIdentity ? string.Empty : card.Name;
        Owner = card.Owner;
        Controller = card.Controller;
        Zone = card.Zone;
        Sequence = hideIdentity ? 0 : card.Sequence;
        Cost = hideIdentity ? 0 : card.Cost;
        CurrentAttack = hideIdentity ? 0 : card.CurrentAttack;
        CurrentHealth = hideIdentity ? 0 : card.CurrentHealth;
        MaximumHealth = hideIdentity ? 0 : card.MaximumHealth;
        Keywords = hideIdentity ? V05.Keyword.None : card.Keywords;
        Evolved = !hideIdentity && card.Evolved;
        AttackedThisTurn = !hideIdentity && card.AttackedThisTurn;
        EnteredThisTurn = !hideIdentity && card.EnteredThisTurn;
        FaceDown = card.FaceDown;
        Countdown = hideIdentity ? 0 : card.Countdown;
    }

    public ulong? InstanceId { get; }
    public string? DesignId { get; }
    public string? ProfessionId { get; }
    public string? SeriesId { get; }
    public bool? Neutral { get; }
    public V05.CardKind? Kind { get; }
    public string Name { get; }
    public V05.PlayerId Owner { get; }
    public V05.PlayerId Controller { get; }
    public V05.Zone Zone { get; }
    public ulong Sequence { get; }
    public int Cost { get; }
    public int CurrentAttack { get; }
    public int CurrentHealth { get; }
    public int MaximumHealth { get; }
    public V05.Keyword Keywords { get; }
    public bool Evolved { get; }
    public bool AttackedThisTurn { get; }
    public bool EnteredThisTurn { get; }
    public bool FaceDown { get; }
    public int Countdown { get; }

    public bool HasKnownIdentity => InstanceId.HasValue && DesignId is not null;
}

public sealed record ProductHotseatPublicPlayerView
{
    internal ProductHotseatPublicPlayerView(V05.PlayerView player)
    {
        Player = player.Player;
        ProfessionId = player.ProfessionId;
        LeaderHealth = player.LeaderHealth;
        MaximumLeaderHealth = player.MaximumLeaderHealth;
        CurrentPp = player.CurrentPp;
        PpCapacity = player.PpCapacity;
        Cracks = player.Cracks;
        EvolutionEnergy = player.EvolutionEnergy;
        OwnTurnNumber = player.OwnTurnNumber;
        FatigueCount = player.FatigueCount;
        DeckCount = player.DeckCount;
        HandCount = player.HandCount;
        MainBoard = ProjectSlots(player.MainBoard);
        Tactics = ProjectSlots(player.Tactics);
        Field = player.Field is null
            ? null
            : new ProductHotseatPublicCardView(player.Field, player.Field.FaceDown);
        Graveyard = ProjectCards(player.Graveyard);
        Archive = ProjectCards(player.Archive);
        Standby = ProjectCards(player.Standby);
    }

    public V05.PlayerId Player { get; }
    public string ProfessionId { get; }
    public int LeaderHealth { get; }
    public int MaximumLeaderHealth { get; }
    public int CurrentPp { get; }
    public int PpCapacity { get; }
    public int Cracks { get; }
    public int EvolutionEnergy { get; }
    public int OwnTurnNumber { get; }
    public int FatigueCount { get; }
    public ulong DeckCount { get; }
    public ulong HandCount { get; }
    public IReadOnlyList<ProductHotseatPublicCardView?> MainBoard { get; }
    public IReadOnlyList<ProductHotseatPublicCardView?> Tactics { get; }
    public ProductHotseatPublicCardView? Field { get; }
    public IReadOnlyList<ProductHotseatPublicCardView> Graveyard { get; }
    public IReadOnlyList<ProductHotseatPublicCardView> Archive { get; }
    public IReadOnlyList<ProductHotseatPublicCardView> Standby { get; }

    private static IReadOnlyList<ProductHotseatPublicCardView?> ProjectSlots(
        IEnumerable<V05.CardView?> cards) => Array.AsReadOnly(cards
        .Select(card => card is null
            ? null
            : new ProductHotseatPublicCardView(card, card.FaceDown))
        .ToArray());

    private static IReadOnlyList<ProductHotseatPublicCardView> ProjectCards(
        IEnumerable<V05.CardView> cards) => Array.AsReadOnly(cards
        .Select(card => new ProductHotseatPublicCardView(card, card.FaceDown))
        .ToArray());
}

public sealed record ProductHotseatPublicBoardView
{
    internal ProductHotseatPublicBoardView(V05.MatchView view)
    {
        if (view.Players.Length != 2)
        {
            throw new ScgsProtocolException("A product hot-seat board requires two players.");
        }

        ActivePlayer = view.ActivePlayer;
        FirstPlayer = view.FirstPlayer;
        Phase = view.Phase;
        Result = view.Result;
        Revision = view.Revision;
        Players = Array.AsReadOnly(view.Players
            .Select(player => new ProductHotseatPublicPlayerView(player))
            .ToArray());
        ReactionPending = view.Reaction.Pending;
        ReactionWindow = view.Reaction.Window;
        ReactionResponder = view.Reaction.Responder;
        ReactionDepth = view.Reaction.Depth;
        ChoicePending = view.PendingChoice.Pending;
        ChoiceChooser = view.PendingChoice.Chooser;
    }

    public V05.PlayerId ActivePlayer { get; }
    public V05.PlayerId FirstPlayer { get; }
    public V05.MatchPhase Phase { get; }
    public V05.GameResult Result { get; }
    public ulong Revision { get; }
    public IReadOnlyList<ProductHotseatPublicPlayerView> Players { get; }
    public bool ReactionPending { get; }
    public V05.ReactionWindow ReactionWindow { get; }
    public V05.PlayerId ReactionResponder { get; }
    public ulong ReactionDepth { get; }
    public bool ChoicePending { get; }
    public V05.PlayerId? ChoiceChooser { get; }
}

public sealed record ProductHotseatUiState
{
    internal ProductHotseatUiState(
        ProductHotseatUiMode mode,
        ProductHotseatCoverReason? coverReason,
        V05.PlayerId? viewer,
        V05.PlayerId? awaitingPlayer,
        V05.MatchView? snapshot,
        IEnumerable<V05.LegalAction> legalActions,
        IEnumerable<V05.LegalAction> candidates,
        ProductHotseatActionSelection selection,
        IEnumerable<ulong> mulliganCards,
        V05.LegalAction? selectedAction,
        ProductPendingChoiceState pendingChoice,
        IEnumerable<V05.GameEventView> events,
        IEnumerable<V05.GameEventView> pendingEvents,
        ulong? pendingEventLastSequence,
        ProductHotseatEventCursors eventCursors,
        uint? lastEngineCode,
        string? failureText,
        bool commandPrepared,
        ProductHotseatPublicBoardView? publicBoard,
        ProductHotseatSelectionStep step,
        bool canStepBack,
        ProductPresentationBatch? presentation = null)
    {
        Mode = mode;
        CoverReason = coverReason;
        Viewer = viewer;
        AwaitingPlayer = awaitingPlayer;
        Snapshot = snapshot;
        LegalActions = Array.AsReadOnly(legalActions.ToArray());
        Selection = selection.Freeze();
        MulliganCards = Array.AsReadOnly(mulliganCards.ToArray());
        SelectedAction = selectedAction;
        PendingChoice = pendingChoice;
        Events = Array.AsReadOnly(events.ToArray());
        PendingEvents = Array.AsReadOnly(pendingEvents.ToArray());
        PendingEventLastSequence = pendingEventLastSequence;
        EventCursors = eventCursors;
        LastEngineCode = lastEngineCode;
        FailureText = failureText;
        CommandPrepared = commandPrepared;
        PublicBoard = publicBoard;
        Presentation = presentation;
        Interaction = new ProductHotseatInteractionContext(
            snapshot?.Revision ?? presentation?.Revision ?? publicBoard?.Revision ?? pendingChoice.Revision,
            step,
            Selection,
            candidates,
            selectedAction,
            canStepBack);
    }

    public ProductHotseatUiMode Mode { get; }
    public ProductHotseatCoverReason? CoverReason { get; }
    public V05.PlayerId? Viewer { get; }
    public V05.PlayerId? AwaitingPlayer { get; }
    public V05.MatchView? Snapshot { get; }
    public IReadOnlyList<V05.LegalAction> LegalActions { get; }
    public ProductHotseatActionSelection Selection { get; }
    public IReadOnlyList<ulong> MulliganCards { get; }
    public V05.LegalAction? SelectedAction { get; }
    public ProductPendingChoiceState PendingChoice { get; }
    public IReadOnlyList<V05.GameEventView> Events { get; }
    public IReadOnlyList<V05.GameEventView> PendingEvents { get; }
    public ulong? PendingEventLastSequence { get; }
    public ProductHotseatEventCursors EventCursors { get; }
    public uint? LastEngineCode { get; }
    public string? FailureText { get; }
    public bool CommandPrepared { get; }
    public ProductHotseatPublicBoardView? PublicBoard { get; }
    public ProductPresentationBatch? Presentation { get; }
    public ProductHotseatInteractionContext Interaction { get; }

    public bool IsCovered => Mode == ProductHotseatUiMode.Covered;
    public bool CanPrepare => SelectedAction is not null &&
                              Interaction.Step == ProductHotseatSelectionStep.Ready &&
                              !CommandPrepared;
    public bool HasUnacknowledgedEvents => PendingEventLastSequence.HasValue;
}

public sealed class ProductHotseatStateChangedEventArgs : EventArgs
{
    public ProductHotseatStateChangedEventArgs(ProductHotseatUiState state) => State = state;

    public ProductHotseatUiState State { get; }
}

internal static class ProductHotseatCommandComparer
{
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

    internal static bool PaymentsEqual(V05.PaymentPreview left, V05.PaymentPreview right) =>
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

    internal static V05.GameCommandRequest Clone(V05.GameCommandRequest command) => command with
    {
        MulliganCards = Array.AsReadOnly(command.MulliganCards.ToArray()),
        SelectedOptionIds = Array.AsReadOnly(command.SelectedOptionIds.ToArray()),
        AdditionalCostCards = Array.AsReadOnly(command.AdditionalCostCards.ToArray()),
    };
}
