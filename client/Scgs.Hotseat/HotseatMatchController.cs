// SPDX-License-Identifier: GPL-3.0-or-later
using Scgs.Client;

namespace Scgs.Hotseat;

public sealed class HotseatMatchController : IDisposable
{
    private const int RefreshRetryLimit = 2;

    private readonly IScgsGameSession session;
    private readonly EventBatch?[] pendingEventBatches = new EventBatch?[2];
    private readonly List<HotseatActionSelection> selectionHistory = [];
    private HotseatEventCursors eventCursors;
    private GameCommandRequest? preparedCommand;
    private PlayerId preparedViewer;
    private bool submissionInProgress;
    private bool disposed;

    public HotseatMatchController(IScgsGameSession session)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        State = CoveredState(
            PlayerId.Player0,
            HotseatCoverReason.InitialReveal,
            commandPrepared: false,
            lastEngineCode: null);
    }

    public event EventHandler<HotseatStateChangedEventArgs>? StateChanged;

    public HotseatUiState State { get; private set; }

    public void Reveal()
    {
        ThrowIfDisposed();
        if (submissionInProgress || preparedCommand is not null ||
            State.Mode != HotseatUiMode.Covered || !State.AwaitingPlayer.HasValue)
        {
            throw new InvalidOperationException("No player is currently awaiting an explicit reveal.");
        }

        PlayerId viewer = State.AwaitingPlayer.Value;
        try
        {
            RefreshForViewer(viewer, State.LastEngineCode);
        }
        catch
        {
            SetFaulted();
            throw;
        }
    }

    public bool AcknowledgeEvents()
    {
        ThrowIfDisposed();
        if (!State.Viewer.HasValue || State.Mode is HotseatUiMode.Covered or HotseatUiMode.Resolving)
        {
            throw new InvalidOperationException(
                "Events can only be acknowledged by the currently revealed viewer.");
        }

        PlayerId viewer = State.Viewer.Value;
        int index = PlayerIndex(viewer);
        EventBatch? batch = pendingEventBatches[index];
        if (batch is null)
        {
            return false;
        }

        eventCursors = eventCursors.With(viewer, batch.LastSequence);
        pendingEventBatches[index] = null;
        SetState(CopyState(
            State,
            pendingEvents: Array.Empty<GameEventView>(),
            pendingEventLastSequence: null,
            eventCursors));
        return true;
    }

    public void ToggleMulliganCard(ulong instanceId)
    {
        EnsureVisibleMode(HotseatUiMode.MulliganSelecting);
        SetMulliganCardSelected(instanceId, !State.MulliganCards.Contains(instanceId));
    }

    public void SetMulliganCardSelected(ulong instanceId, bool selected)
    {
        EnsureVisibleMode(HotseatUiMode.MulliganSelecting);
        var values = State.MulliganCards.ToList();
        bool alreadySelected = values.Contains(instanceId);
        if (selected && !alreadySelected)
        {
            values.Add(instanceId);
        }
        else if (!selected && alreadySelected)
        {
            values.Remove(instanceId);
        }

        ApplyMulliganSelection(values);
    }

    public void SelectMulliganCards(IEnumerable<ulong> instanceIds)
    {
        EnsureVisibleMode(HotseatUiMode.MulliganSelecting);
        ArgumentNullException.ThrowIfNull(instanceIds);
        ulong[] values = instanceIds.ToArray();
        if (values.Distinct().Count() != values.Length)
        {
            throw new ArgumentException(
                "A mulligan selection cannot contain duplicate instance IDs.",
                nameof(instanceIds));
        }

        ApplyMulliganSelection(values);
    }

    public void BeginActionSelection(ActionKind action, ulong? source = null)
    {
        EnsureActionSelectionMode();
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported action value.");
        }

        selectionHistory.Clear();
        ApplyActionSelection(new HotseatActionSelection
        {
            Action = action,
            Source = source,
        }, HotseatActionSelection.Empty);
    }

    public void BeginSourceSelection(ulong source)
    {
        EnsureActionSelectionMode();
        selectionHistory.Clear();
        ApplyActionSelection(new HotseatActionSelection
        {
            Source = source,
        }, HotseatActionSelection.Empty);
    }

    public void ChooseAction(ActionKind action)
    {
        EnsureActionSelectionMode();
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported action value.");
        }

        if (!State.Selection.Source.HasValue)
        {
            throw new InvalidOperationException("BeginSourceSelection must be called first.");
        }

        ApplyActionSelection(new HotseatActionSelection
        {
            Action = action,
            Source = State.Selection.Source,
        }, State.Selection);
    }

    public void SelectTarget(Target? target)
    {
        EnsureProgressiveSelectionStarted();
        ApplyActionSelection(State.Selection with
        {
            HasTarget = true,
            Target = target,
        }, State.Selection);
    }

    public void SelectSlot(ulong? slot)
    {
        EnsureProgressiveSelectionStarted();
        ApplyActionSelection(State.Selection with
        {
            HasSlot = true,
            Slot = slot,
        }, State.Selection);
    }

    public void SelectDonor(ulong? donor)
    {
        EnsureProgressiveSelectionStarted();
        ApplyActionSelection(State.Selection with
        {
            HasDonor = true,
            Donor = donor,
        }, State.Selection);
    }

    public void SelectAdvance(bool useAdvance)
    {
        EnsureProgressiveSelectionStarted();
        ApplyActionSelection(State.Selection with
        {
            HasAdvanceChoice = true,
            UseAdvance = useAdvance,
        }, State.Selection);
    }

    public void SelectLegalAction(LegalAction action)
    {
        EnsureActionSelectionMode();
        ArgumentNullException.ThrowIfNull(action);
        LegalAction canonical = FindCanonicalAction(action.Command);
        if (!Enum.IsDefined(canonical.Command.Action))
        {
            throw new NotSupportedException(
                $"Action {Convert.ToUInt32(canonical.Command.Action)} is not supported by this client.");
        }

        selectionHistory.Clear();
        selectionHistory.Add(HotseatActionSelection.Empty);
        ReplaceVisibleSelection(
            SelectionFrom(canonical.Command),
            Array.Empty<ulong>(),
            [canonical],
            canonical,
            HotseatSelectionStep.Ready);
    }

    public PaymentPreview? PreviewSelectedPayment()
    {
        EnsureActionSelectionMode();
        LegalAction selected = State.SelectedAction ??
            throw new InvalidOperationException("An exact legal action must be selected first.");
        PlayerId viewer = State.Viewer!.Value;
        try
        {
            PaymentResult result = session.PreviewPayment(selected.Command);
            if (result.Revision != State.Snapshot!.Revision)
            {
                RefreshForViewer(viewer, (uint)EngineCode.StaleRevision);
                return null;
            }

            if (!PaymentsEqual(result.Payment, selected.Payment))
            {
                throw new ScgsProtocolException(
                    "The payment preview does not match the selected legal action.");
            }

            return result.Payment;
        }
        catch (Exception exception) when (
            exception is ScgsNativeException or ScgsProtocolException)
        {
            SetFaulted();
            throw;
        }
    }

    public void CancelSelection()
    {
        ThrowIfDisposed();
        if (submissionInProgress)
        {
            throw new InvalidOperationException("A command is currently being submitted.");
        }

        if (preparedCommand is not null || State.IsCovered)
        {
            throw new InvalidOperationException(
                "A prepared command cannot be cancelled after the privacy cover is shown.");
        }

        switch (State.Mode)
        {
            case HotseatUiMode.MulliganSelecting:
                selectionHistory.Clear();
                ApplyMulliganSelection(Array.Empty<ulong>());
                break;
            case HotseatUiMode.Action:
            case HotseatUiMode.Reaction:
                selectionHistory.Clear();
                ReplaceVisibleSelection(
                    HotseatActionSelection.Empty,
                    Array.Empty<ulong>(),
                    State.LegalActions,
                    selectedAction: null,
                    HotseatSelectionStep.None);
                break;
            default:
                throw new InvalidOperationException("There is no visible selection to cancel.");
        }
    }

    public bool StepBackSelection()
    {
        EnsureActionSelectionMode();
        if (submissionInProgress || preparedCommand is not null)
        {
            throw new InvalidOperationException(
                "A prepared command cannot be changed while it is resolving.");
        }

        if (selectionHistory.Count == 0)
        {
            return false;
        }

        int index = selectionHistory.Count - 1;
        HotseatActionSelection previous = selectionHistory[index];
        selectionHistory.RemoveAt(index);
        ApplyActionSelection(previous, historyEntry: null);
        return true;
    }

    public bool PrepareSelectedCommand()
    {
        ThrowIfDisposed();
        if (submissionInProgress || preparedCommand is not null || State.SelectedAction is null ||
            State.Viewer is null || State.Interaction.Step != HotseatSelectionStep.Ready ||
            State.Mode is not HotseatUiMode.MulliganSelecting and
                not HotseatUiMode.Action and not HotseatUiMode.Reaction)
        {
            return false;
        }

        LegalAction canonical = FindCanonicalAction(State.SelectedAction.Command);
        MatchView snapshot = State.Snapshot ??
            throw new InvalidOperationException("The visible snapshot is unavailable.");
        preparedCommand = CloneCommand(canonical.Command);
        preparedViewer = State.Viewer.Value;
        selectionHistory.Clear();
        SetState(CreateState(
            HotseatUiMode.Resolving,
            coverReason: null,
            viewer: null,
            awaitingPlayer: null,
            snapshot: null,
            legalActions: [],
            candidateActions: [],
            HotseatActionSelection.Empty,
            mulliganCards: [],
            selectedAction: null,
            events: [],
            pendingEvents: [],
            pendingEventLastSequence: null,
            lastEngineCode: null,
            failureText: null,
            commandPrepared: true,
            publicBoard: new HotseatPublicBoardView(snapshot)));
        return true;
    }

    public bool ConfirmSelection() => PrepareSelectedCommand();

    public void CompleteMulliganReview()
    {
        EnsureVisibleMode(HotseatUiMode.MulliganReview);
        MatchView view = State.Snapshot ??
            throw new InvalidOperationException("The mulligan review snapshot is unavailable.");
        PlayerId viewer = State.Viewer!.Value;
        PlayerId? nextActor = DetermineActor(view);
        if (nextActor.HasValue && nextActor.Value != viewer)
        {
            SetState(CoveredState(
                nextActor.Value,
                HotseatCoverReason.PassingDevice,
                commandPrepared: false,
                lastEngineCode: null));
            return;
        }

        try
        {
            RefreshForViewer(viewer, lastEngineCode: null);
        }
        catch
        {
            SetFaulted();
            throw;
        }
    }

    public EngineStatus SubmitPreparedCommand()
    {
        ThrowIfDisposed();
        if (submissionInProgress || preparedCommand is null ||
            State.Mode != HotseatUiMode.Resolving || !State.CommandPrepared)
        {
            throw new InvalidOperationException("No prepared command is ready for submission.");
        }

        GameCommandRequest command = preparedCommand;
        PlayerId previousViewer = preparedViewer;
        preparedCommand = null;
        submissionInProgress = true;
        EngineStatus status;
        try
        {
            status = session.SubmitCommand(command) ??
                throw new ScgsProtocolException("The engine returned a null status.");
        }
        catch
        {
            submissionInProgress = false;
            SetFaulted();
            throw;
        }

        submissionInProgress = false;
        try
        {
            if (status.IsSuccess && command.Action == ActionKind.Mulligan)
            {
                ShowMulliganReview(previousViewer);
            }
            else
            {
                RefreshForViewer(
                    previousViewer,
                    status.IsSuccess ? null : status.RawCode);
            }
        }
        catch
        {
            SetFaulted();
            throw;
        }

        return status;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        preparedCommand = null;
        submissionInProgress = false;
        selectionHistory.Clear();
        Array.Clear(pendingEventBatches);
        session.Dispose();
        SetState(CreateState(
            HotseatUiMode.Disposed,
            coverReason: null,
            viewer: null,
            awaitingPlayer: null,
            snapshot: null,
            legalActions: [],
            candidateActions: [],
            HotseatActionSelection.Empty,
            mulliganCards: [],
            selectedAction: null,
            events: [],
            pendingEvents: [],
            pendingEventLastSequence: null,
            lastEngineCode: null,
            failureText: null,
            commandPrepared: false));
    }

    private void RefreshForViewer(
        PlayerId viewer,
        uint? lastEngineCode,
        int remainingRetries = RefreshRetryLimit)
    {
        selectionHistory.Clear();
        MatchView view = session.GetView(viewer);
        PlayerId? actor = DetermineActor(view);
        if (actor.HasValue && actor.Value != viewer)
        {
            SetState(CoveredState(
                actor.Value,
                HotseatCoverReason.PassingDevice,
                commandPrepared: false,
                lastEngineCode));
            return;
        }

        HotseatUiMode mode = view.Phase switch
        {
            MatchPhase.Mulligan => HotseatUiMode.MulliganSelecting,
            MatchPhase.Action => HotseatUiMode.Action,
            MatchPhase.Reaction => HotseatUiMode.Reaction,
            MatchPhase.Finished => HotseatUiMode.Finished,
            _ => throw new ScgsProtocolException(
                $"Phase {view.Phase} cannot be displayed by a started hot-seat match."),
        };

        LegalAction[] legalActions = [];
        if (mode != HotseatUiMode.Finished)
        {
            LegalActionsResult result = session.ListLegalActions(
                new ActionQueryRequest(viewer, view.Revision));
            if (result.Revision != view.Revision)
            {
                RetryRefreshOrThrow(viewer, remainingRetries);
                return;
            }

            legalActions = result.Actions.ToArray();
            ValidateLegalActions(legalActions, viewer, view.Revision);
        }

        EventBatch eventBatch = session.ReadEvents(viewer, eventCursors.For(viewer));
        if (eventBatch.Revision != view.Revision)
        {
            RetryRefreshOrThrow(viewer, remainingRetries);
            return;
        }

        SetPendingBatch(viewer, eventBatch);
        IReadOnlyList<ulong> mulliganCards = [];
        LegalAction? selectedAction = null;
        IReadOnlyList<LegalAction> candidates = legalActions;
        HotseatActionSelection selection = HotseatActionSelection.Empty;
        if (mode == HotseatUiMode.MulliganSelecting)
        {
            LegalAction[] passCandidates = legalActions.Where(action =>
                action.Command.Action == ActionKind.Mulligan &&
                action.Command.MulliganCards.Count == 0).ToArray();
            if (passCandidates.Length != 1)
            {
                throw new ScgsProtocolException(
                    "The mulligan selection does not contain exactly one empty candidate.");
            }

            candidates = passCandidates;
            selectedAction = passCandidates[0];
            selection = SelectionFrom(selectedAction.Command);
        }

        SetState(CreateVisibleState(
            mode,
            viewer,
            view,
            legalActions,
            candidates,
            selection,
            mulliganCards,
            selectedAction,
            eventBatch,
            lastEngineCode));
    }

    private void ShowMulliganReview(PlayerId viewer)
    {
        selectionHistory.Clear();
        MatchView view = session.GetView(viewer);
        if (!view.Players[(int)viewer].MulliganDone)
        {
            throw new ScgsProtocolException(
                "A successful mulligan did not mark the submitting player complete.");
        }

        EventBatch eventBatch = session.ReadEvents(viewer, eventCursors.For(viewer));
        if (eventBatch.Revision != view.Revision)
        {
            throw new ScgsProtocolException(
                "The mulligan-review event revision does not match its snapshot.");
        }

        SetPendingBatch(viewer, eventBatch);
        SetState(CreateVisibleState(
            HotseatUiMode.MulliganReview,
            viewer,
            view,
            legalActions: [],
            candidateActions: [],
            HotseatActionSelection.Empty,
            mulliganCards: [],
            selectedAction: null,
            eventBatch,
            lastEngineCode: null));
    }

    private void RetryRefreshOrThrow(PlayerId viewer, int remainingRetries)
    {
        if (remainingRetries <= 0)
        {
            throw new ScgsProtocolException(
                "The native snapshot revision did not stabilize while refreshing.");
        }

        RefreshForViewer(
            viewer,
            (uint)EngineCode.StaleRevision,
            remainingRetries - 1);
    }

    private void ApplyMulliganSelection(IEnumerable<ulong> instanceIds)
    {
        MatchView view = State.Snapshot ??
            throw new InvalidOperationException("The mulligan snapshot is unavailable.");
        PlayerId viewer = State.Viewer ??
            throw new InvalidOperationException("The mulligan viewer is unavailable.");
        ulong[] handOrder = view.Players[(int)viewer].Hand
            .Select(card => card.InstanceId ??
                throw new ScgsProtocolException("A visible hand card is missing its instance ID."))
            .ToArray();
        ulong[] requested = instanceIds.ToArray();
        if (requested.Distinct().Count() != requested.Length)
        {
            throw new ArgumentException("A mulligan selection cannot contain duplicates.");
        }

        var selected = requested.ToHashSet();
        if (selected.Any(id => !handOrder.Contains(id)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(instanceIds),
                "A mulligan selection contains a card outside the current viewer's hand.");
        }

        ulong[] canonical = handOrder.Where(selected.Contains).ToArray();
        LegalAction[] localMatches = State.LegalActions.Where(action =>
            action.Command.Action == ActionKind.Mulligan &&
            action.Command.MulliganCards.SequenceEqual(canonical)).ToArray();
        if (localMatches.Length != 1)
        {
            throw new InvalidOperationException(
                "The selected mulligan set is not an exact legal-action candidate.");
        }

        if (canonical.Length != 0)
        {
            var query = new ActionQueryRequest(viewer, view.Revision)
            {
                Action = ActionKind.Mulligan,
                MulliganCards = Array.AsReadOnly(canonical),
            };
            LegalAction[]? queried = QueryCurrentRevision(query);
            if (queried is null)
            {
                return;
            }

            RequireSameCandidateSet(localMatches, queried);
        }

        ReplaceVisibleSelection(
            SelectionFrom(localMatches[0].Command),
            canonical,
            localMatches,
            localMatches[0],
            HotseatSelectionStep.Ready);
    }

    private void ApplyActionSelection(
        HotseatActionSelection selection,
        HotseatActionSelection? historyEntry)
    {
        LegalAction[] localMatches = State.LegalActions.Where(action =>
            MatchesSelection(action.Command, selection)).ToArray();
        if (localMatches.Length == 0)
        {
            throw new ArgumentException("The selected fields do not match a legal-action candidate.");
        }

        PlayerId viewer = State.Viewer!.Value;
        ulong revision = State.Snapshot!.Revision;
        var query = new ActionQueryRequest(viewer, revision)
        {
            Action = selection.Action,
            Source = selection.Source,
            Target = selection.HasTarget && selection.Target is not null
                ? selection.Target
                : null,
            Slot = selection.HasSlot && selection.Slot.HasValue
                ? selection.Slot
                : null,
            ComponentDonor = selection.HasDonor && selection.Donor.HasValue
                ? selection.Donor
                : null,
            UseAdvance = selection.HasAdvanceChoice
                ? selection.UseAdvance
                : null,
        };
        LegalAction[]? queried = QueryCurrentRevision(query);
        if (queried is null)
        {
            return;
        }

        LegalAction[] queriedLocalMatches = queried.Where(action =>
            localMatches.Any(local => CommandsEqual(local.Command, action.Command))).ToArray();
        RequireSameCandidateSet(localMatches, queriedLocalMatches);
        HotseatActionSelection completed = CompleteInvariantDefaults(selection, localMatches);
        HotseatSelectionStep step = DetermineSelectionStep(completed, localMatches);
        LegalAction? selectedAction = localMatches.Length == 1 &&
                                      step == HotseatSelectionStep.Ready
            ? localMatches[0]
            : null;
        if (historyEntry is not null && !Equals(historyEntry, completed))
        {
            selectionHistory.Add(historyEntry);
        }

        ReplaceVisibleSelection(
            completed,
            [],
            localMatches,
            selectedAction,
            step);
    }

    private LegalAction[]? QueryCurrentRevision(ActionQueryRequest query)
    {
        try
        {
            LegalActionsResult result = session.ListLegalActions(query);
            if (result.Revision != query.ExpectedRevision)
            {
                RefreshForViewer(query.Player, (uint)EngineCode.StaleRevision);
                return null;
            }

            LegalAction[] actions = result.Actions.ToArray();
            ValidateLegalActions(actions, query.Player, result.Revision);
            foreach (LegalAction action in actions)
            {
                _ = FindCanonicalAction(action.Command);
            }

            return actions;
        }
        catch (Exception exception) when (
            exception is ScgsNativeException or ScgsProtocolException)
        {
            SetFaulted();
            throw;
        }
    }

    private void ReplaceVisibleSelection(
        HotseatActionSelection selection,
        IReadOnlyList<ulong> mulliganCards,
        IReadOnlyList<LegalAction> candidates,
        LegalAction? selectedAction,
        HotseatSelectionStep selectionStep)
    {
        SetState(CreateState(
            State.Mode,
            State.CoverReason,
            State.Viewer,
            State.AwaitingPlayer,
            State.Snapshot,
            State.LegalActions,
            candidates,
            selection,
            mulliganCards,
            selectedAction,
            State.Events,
            State.PendingEvents,
            State.PendingEventLastSequence,
            State.LastEngineCode,
            State.FailureText,
            commandPrepared: false,
            selectionStep: selectionStep));
    }

    private LegalAction FindCanonicalAction(GameCommandRequest command)
    {
        LegalAction[] matches = State.LegalActions
            .Where(action => CommandsEqual(action.Command, command))
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new ArgumentException(
                "The command is not an exact current legal-action candidate.",
                nameof(command)),
            _ => throw new ScgsProtocolException(
                "The native legal-action list contains duplicate commands."),
        };
    }

    private static void RequireSameCandidateSet(
        IReadOnlyList<LegalAction> expected,
        IReadOnlyList<LegalAction> actual)
    {
        bool same = expected.Count == actual.Count && expected.All(item =>
            actual.Any(other => CommandsEqual(item.Command, other.Command)));
        if (!same)
        {
            throw new ScgsProtocolException(
                "A partial legal-action query disagrees with the visible candidate set.");
        }
    }

    private static void ValidateLegalActions(
        IEnumerable<LegalAction> actions,
        PlayerId viewer,
        ulong revision)
    {
        if (actions.Any(action =>
                action.Command.Player != viewer ||
                action.Command.ExpectedRevision != revision))
        {
            throw new ScgsProtocolException(
                "A legal action belongs to a different actor or revision.");
        }
    }

    private void SetPendingBatch(PlayerId viewer, EventBatch eventBatch)
    {
        int index = PlayerIndex(viewer);
        pendingEventBatches[index] = eventBatch.LastSequence == eventCursors.For(viewer)
            ? null
            : eventBatch;
    }

    private HotseatUiState CreateVisibleState(
        HotseatUiMode mode,
        PlayerId viewer,
        MatchView view,
        IReadOnlyList<LegalAction> legalActions,
        IReadOnlyList<LegalAction> candidateActions,
        HotseatActionSelection selection,
        IReadOnlyList<ulong> mulliganCards,
        LegalAction? selectedAction,
        EventBatch eventBatch,
        uint? lastEngineCode)
    {
        bool pending = pendingEventBatches[PlayerIndex(viewer)] is not null;
        HotseatSelectionStep selectionStep =
            mode == HotseatUiMode.MulliganSelecting && selectedAction is not null
                ? HotseatSelectionStep.Ready
                : HotseatSelectionStep.None;
        return CreateState(
            mode,
            coverReason: null,
            viewer,
            awaitingPlayer: null,
            view,
            legalActions,
            candidateActions,
            selection,
            mulliganCards,
            selectedAction,
            eventBatch.Events,
            pending ? eventBatch.Events : [],
            pending ? eventBatch.LastSequence : null,
            lastEngineCode,
            lastEngineCode.HasValue ? EngineCodeZhCnFormatter.Format(lastEngineCode.Value) : null,
            commandPrepared: false,
            selectionStep: selectionStep);
    }

    private HotseatUiState CoveredState(
        PlayerId? awaitingPlayer,
        HotseatCoverReason reason,
        bool commandPrepared,
        uint? lastEngineCode) => CreateState(
            HotseatUiMode.Covered,
            reason,
            viewer: null,
            awaitingPlayer,
            snapshot: null,
            legalActions: [],
            candidateActions: [],
            HotseatActionSelection.Empty,
            mulliganCards: [],
            selectedAction: null,
            events: [],
            pendingEvents: [],
            pendingEventLastSequence: null,
            lastEngineCode,
            lastEngineCode.HasValue ? EngineCodeZhCnFormatter.Format(lastEngineCode.Value) : null,
            commandPrepared);

    private HotseatUiState CreateState(
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
        uint? lastEngineCode,
        string? failureText,
        bool commandPrepared,
        HotseatPublicBoardView? publicBoard = null,
        HotseatSelectionStep selectionStep = HotseatSelectionStep.None) => new(
            mode,
            coverReason,
            viewer,
            awaitingPlayer,
            snapshot,
            legalActions,
            candidateActions,
            selection,
            mulliganCards,
            selectedAction,
            events,
            pendingEvents,
            pendingEventLastSequence,
            eventCursors,
            lastEngineCode,
            failureText,
            commandPrepared,
            publicBoard,
            selectionStep,
            selectionHistory.Count != 0);

    private static HotseatUiState CopyState(
        HotseatUiState source,
        IReadOnlyList<GameEventView> pendingEvents,
        ulong? pendingEventLastSequence,
        HotseatEventCursors eventCursors) => new(
            source.Mode,
            source.CoverReason,
            source.Viewer,
            source.AwaitingPlayer,
            source.Snapshot,
            source.LegalActions,
            source.CandidateOptions.Actions,
            source.Selection,
            source.MulliganCards,
            source.SelectedAction,
            source.Events,
            pendingEvents,
            pendingEventLastSequence,
            eventCursors,
            source.LastEngineCode,
            source.FailureText,
            source.CommandPrepared,
            source.PublicBoard,
            source.Interaction.Step,
            source.Interaction.CanStepBack);

    private void SetFaulted()
    {
        selectionHistory.Clear();
        SetState(CreateState(
            HotseatUiMode.Faulted,
            coverReason: null,
            viewer: null,
            awaitingPlayer: null,
            snapshot: null,
            legalActions: [],
            candidateActions: [],
            HotseatActionSelection.Empty,
            mulliganCards: [],
            selectedAction: null,
            events: [],
            pendingEvents: [],
            pendingEventLastSequence: null,
            lastEngineCode: null,
            failureText: "客户端无法继续读取对局。",
            commandPrepared: false));
    }

    private static PlayerId? DetermineActor(MatchView view) => view.Phase switch
    {
        MatchPhase.Mulligan when !view.Players[(int)PlayerId.Player0].MulliganDone =>
            PlayerId.Player0,
        MatchPhase.Mulligan when !view.Players[(int)PlayerId.Player1].MulliganDone =>
            PlayerId.Player1,
        MatchPhase.Mulligan => view.ActivePlayer,
        MatchPhase.Action => view.ActivePlayer,
        MatchPhase.Reaction when view.Reaction.Pending => view.Reaction.Responder,
        MatchPhase.Reaction => throw new ScgsProtocolException(
            "The match is in reaction phase without a pending reaction."),
        MatchPhase.Finished => null,
        _ => throw new ScgsProtocolException($"Phase {view.Phase} has no hot-seat actor."),
    };

    private static HotseatActionSelection CompleteInvariantDefaults(
        HotseatActionSelection selection,
        IReadOnlyList<LegalAction> candidates)
    {
        if (!selection.Source.HasValue && !selection.Action.HasValue)
        {
            return selection;
        }

        HotseatActionSelection completed = selection;
        ActionKind[] actionKinds = candidates
            .Select(candidate => candidate.Command.Action)
            .Distinct()
            .ToArray();
        if (!completed.Action.HasValue && actionKinds.Length == 1 &&
            Enum.IsDefined(actionKinds[0]))
        {
            completed = completed with { Action = actionKinds[0] };
        }

        if (!completed.HasTarget && candidates.All(candidate => candidate.Command.Target is null))
        {
            completed = completed with { HasTarget = true, Target = null };
        }

        if (!completed.HasSlot && candidates.All(candidate => !candidate.Command.Slot.HasValue))
        {
            completed = completed with { HasSlot = true, Slot = null };
        }

        if (!completed.HasDonor &&
            candidates.All(candidate => !candidate.Command.ComponentDonor.HasValue))
        {
            completed = completed with { HasDonor = true, Donor = null };
        }

        bool[] advanceChoices = candidates
            .Select(candidate => candidate.Command.UseAdvance)
            .Distinct()
            .ToArray();
        if (!completed.HasAdvanceChoice && advanceChoices.Length == 1)
        {
            completed = completed with
            {
                HasAdvanceChoice = true,
                UseAdvance = advanceChoices[0],
            };
        }

        return completed;
    }

    private static HotseatSelectionStep DetermineSelectionStep(
        HotseatActionSelection selection,
        IReadOnlyList<LegalAction> candidates)
    {
        if (!selection.Source.HasValue)
        {
            return HotseatSelectionStep.None;
        }

        if (!selection.Action.HasValue)
        {
            return HotseatSelectionStep.ChooseAction;
        }

        bool needsDonor = !selection.HasDonor &&
                          candidates.Any(candidate => candidate.Command.ComponentDonor.HasValue);
        bool needsSlot = !selection.HasSlot &&
                         candidates.Any(candidate => candidate.Command.Slot.HasValue);
        bool needsTarget = !selection.HasTarget &&
                           candidates.Any(candidate => candidate.Command.Target is not null);
        bool needsAdvance = !selection.HasAdvanceChoice &&
                            candidates.Any(candidate => candidate.Command.UseAdvance);

        return selection.Action.Value switch
        {
            ActionKind.Deploy when needsDonor => HotseatSelectionStep.ChooseDonor,
            ActionKind.Deploy when needsSlot => HotseatSelectionStep.ChooseSlot,
            ActionKind.Deploy when needsTarget => HotseatSelectionStep.ChooseTarget,
            ActionKind.Deploy when needsAdvance => HotseatSelectionStep.ChooseAdvance,

            ActionKind.PlayUnit or ActionKind.PlayTactic when needsSlot =>
                HotseatSelectionStep.ChooseSlot,
            ActionKind.PlayUnit or ActionKind.PlayTactic when needsTarget =>
                HotseatSelectionStep.ChooseTarget,
            ActionKind.PlayUnit or ActionKind.PlayTactic when needsAdvance =>
                HotseatSelectionStep.ChooseAdvance,

            ActionKind.CastSpell or ActionKind.Attack or ActionKind.Evolve or
                ActionKind.ActivateTrap when needsTarget => HotseatSelectionStep.ChooseTarget,
            ActionKind.CastSpell or ActionKind.Attack or ActionKind.Evolve or
                ActionKind.ActivateTrap when needsAdvance => HotseatSelectionStep.ChooseAdvance,

            _ when needsDonor => HotseatSelectionStep.ChooseDonor,
            _ when needsSlot => HotseatSelectionStep.ChooseSlot,
            _ when needsTarget => HotseatSelectionStep.ChooseTarget,
            _ when needsAdvance => HotseatSelectionStep.ChooseAdvance,
            _ => HotseatSelectionStep.Ready,
        };
    }

    private static bool MatchesSelection(
        GameCommandRequest command,
        HotseatActionSelection selection) =>
        (!selection.Action.HasValue || command.Action == selection.Action.Value) &&
        (!selection.Source.HasValue || command.Source == selection.Source.Value) &&
        (!selection.HasTarget || Equals(command.Target, selection.Target)) &&
        (!selection.HasSlot || command.Slot == selection.Slot) &&
        (!selection.HasDonor || command.ComponentDonor == selection.Donor) &&
        (!selection.HasAdvanceChoice || command.UseAdvance == selection.UseAdvance);

    private static bool CommandsEqual(GameCommandRequest left, GameCommandRequest right) =>
        left.Player == right.Player &&
        left.Action == right.Action &&
        left.ExpectedRevision == right.ExpectedRevision &&
        left.Source == right.Source &&
        Equals(left.Target, right.Target) &&
        left.Slot == right.Slot &&
        left.ComponentDonor == right.ComponentDonor &&
        left.UseAdvance == right.UseAdvance &&
        left.MulliganCards.SequenceEqual(right.MulliganCards);

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

    private static HotseatActionSelection SelectionFrom(GameCommandRequest command) => new()
    {
        Action = command.Action,
        Source = command.Source,
        HasTarget = true,
        Target = command.Target,
        HasSlot = true,
        Slot = command.Slot,
        HasDonor = true,
        Donor = command.ComponentDonor,
        HasAdvanceChoice = true,
        UseAdvance = command.UseAdvance,
    };

    private static GameCommandRequest CloneCommand(GameCommandRequest command) =>
        command with
        {
            MulliganCards = Array.AsReadOnly(command.MulliganCards.ToArray()),
        };

    private void EnsureActionSelectionMode()
    {
        ThrowIfDisposed();
        if (State.Mode is not HotseatUiMode.Action and not HotseatUiMode.Reaction)
        {
            throw new InvalidOperationException("Actions can only be selected while action UI is visible.");
        }
    }

    private void EnsureProgressiveSelectionStarted()
    {
        EnsureActionSelectionMode();
        if (!State.Selection.Action.HasValue)
        {
            throw new InvalidOperationException("BeginActionSelection must be called first.");
        }
    }

    private void EnsureVisibleMode(HotseatUiMode mode)
    {
        ThrowIfDisposed();
        if (State.Mode != mode)
        {
            throw new InvalidOperationException($"The current UI mode is not {mode}.");
        }
    }

    private static int PlayerIndex(PlayerId player) => player switch
    {
        PlayerId.Player0 => 0,
        PlayerId.Player1 => 1,
        _ => throw new ArgumentOutOfRangeException(
            nameof(player),
            player,
            "Unsupported player value."),
    };

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private void SetState(HotseatUiState state)
    {
        State = state;
        StateChanged?.Invoke(this, new HotseatStateChangedEventArgs(state));
    }
}
