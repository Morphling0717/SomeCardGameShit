// SPDX-License-Identifier: GPL-3.0-or-later
using Scgs.Client;
using V05 = Scgs.Client.V05;

namespace Scgs.Hotseat.Product;

public sealed class ProductHotseatMatchController : IDisposable
{
    private const int RefreshRetryLimit = 2;

    public const int RequiredPublicFramesBeforeSubmit = 2;

    private readonly V05.IScgsV05GameSession session;
    private readonly V05.EventBatch?[] pendingEventBatches = new V05.EventBatch?[2];
    private readonly List<ProductHotseatActionSelection> selectionHistory = [];
    private readonly List<IReadOnlyList<string>> choiceSelectionHistory = [];
    private ProductHotseatEventCursors eventCursors;
    private V05.GameCommandRequest? preparedCommand;
    private V05.PlayerId preparedViewer;
    private ulong? lastPublicFrameToken;
    private int publicFramesDrawn;
    private bool submissionInProgress;
    private bool disposed;

    public ProductHotseatMatchController(V05.IScgsV05GameSession session)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        State = CoveredState(
            V05.PlayerId.Player0,
            ProductHotseatCoverReason.InitialReveal,
            lastEngineCode: null);
    }

    public event EventHandler<ProductHotseatStateChangedEventArgs>? StateChanged;

    public ProductHotseatUiState State { get; private set; }

    public int PublicFramesDrawn => publicFramesDrawn;

    public bool CanSubmitPreparedCommand =>
        State.Mode == ProductHotseatUiMode.Resolving &&
        State.CommandPrepared &&
        preparedCommand is not null &&
        publicFramesDrawn >= RequiredPublicFramesBeforeSubmit;

    public void Reveal()
    {
        ThrowIfDisposed();
        if (submissionInProgress || preparedCommand is not null ||
            State.Mode != ProductHotseatUiMode.Covered || !State.AwaitingPlayer.HasValue)
        {
            throw new InvalidOperationException("No product player is awaiting reveal.");
        }

        V05.PlayerId viewer = State.AwaitingPlayer.Value;
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
        if (!State.Viewer.HasValue ||
            State.Mode is ProductHotseatUiMode.Covered or ProductHotseatUiMode.Resolving)
        {
            throw new InvalidOperationException("Only the revealed viewer can acknowledge events.");
        }

        V05.PlayerId viewer = State.Viewer.Value;
        int index = PlayerIndex(viewer);
        V05.EventBatch? pending = pendingEventBatches[index];
        if (pending is null)
        {
            return false;
        }

        eventCursors = eventCursors.With(viewer, pending.LastSequence);
        pendingEventBatches[index] = null;
        SetState(CopyState(
            State,
            pendingEvents: [],
            pendingEventLastSequence: null,
            eventCursors));
        return true;
    }

    public void ToggleMulliganCard(ulong instanceId)
    {
        EnsureMode(ProductHotseatUiMode.MulliganSelecting);
        SetMulliganCardSelected(instanceId, !State.MulliganCards.Contains(instanceId));
    }

    public void SetMulliganCardSelected(ulong instanceId, bool selected)
    {
        EnsureMode(ProductHotseatUiMode.MulliganSelecting);
        var values = State.MulliganCards.ToList();
        if (selected && !values.Contains(instanceId))
        {
            values.Add(instanceId);
        }
        else if (!selected)
        {
            values.Remove(instanceId);
        }

        ApplyMulliganSelection(values);
    }

    public void SelectMulliganCards(IEnumerable<ulong> instanceIds)
    {
        EnsureMode(ProductHotseatUiMode.MulliganSelecting);
        ArgumentNullException.ThrowIfNull(instanceIds);
        ulong[] values = instanceIds.ToArray();
        if (values.Length != values.Distinct().Count())
        {
            throw new ArgumentException("Mulligan cards must be unique.", nameof(instanceIds));
        }

        ApplyMulliganSelection(values);
    }

    public void BeginSourceSelection(ulong source)
    {
        EnsureNormalSelectionMode();
        if (source == 0 || !State.LegalActions.Any(action => action.Command.Source == source))
        {
            throw new ArgumentException("The source is not currently legal.", nameof(source));
        }

        selectionHistory.Clear();
        choiceSelectionHistory.Clear();
        ApplyActionSelection(new ProductHotseatActionSelection
        {
            HasSource = true,
            Source = source,
        }, ProductHotseatActionSelection.Empty);
    }

    public void BeginActionSelection(V05.ActionKind action, ulong? source = null)
    {
        EnsureNormalSelectionMode();
        RequireAction(action);
        selectionHistory.Clear();
        choiceSelectionHistory.Clear();
        ApplyActionSelection(new ProductHotseatActionSelection
        {
            Action = action,
            HasSource = source.HasValue || State.LegalActions
                .Where(candidate => candidate.Command.Action == action)
                .All(candidate => candidate.Command.Source == 0),
            Source = source ?? 0,
        }, ProductHotseatActionSelection.Empty);
    }

    public void ChooseAction(V05.ActionKind action)
    {
        EnsureNormalSelectionMode();
        RequireAction(action);
        if (!State.Selection.HasSource)
        {
            throw new InvalidOperationException("A source must be selected first.");
        }

        ApplyActionSelection(State.Selection with { Action = action }, State.Selection);
    }

    public void SelectMode(string modeId)
    {
        EnsureProgressiveSelectionStarted();
        ArgumentException.ThrowIfNullOrWhiteSpace(modeId);
        ApplyActionSelection(State.Selection with
        {
            HasMode = true,
            ModeId = modeId,
        }, State.Selection);
    }

    public void SelectAdditionalCostCards(IEnumerable<ulong> instanceIds)
    {
        EnsureProgressiveSelectionStarted();
        ArgumentNullException.ThrowIfNull(instanceIds);
        ulong[] values = instanceIds.ToArray();
        if (values.Any(value => value == 0) || values.Length != values.Distinct().Count())
        {
            throw new ArgumentException(
                "Additional-cost card IDs must be non-zero and unique.",
                nameof(instanceIds));
        }

        ProductHotseatActionSelection withoutAdditionalCost = State.Selection with
        {
            HasAdditionalCost = false,
            AdditionalCostCards = Array.Empty<ulong>(),
        };
        IReadOnlyList<ulong> canonicalOrder = State.LegalActions
            .Where(action => MatchesSelection(action.Command, withoutAdditionalCost))
            .Select(action => action.Command.AdditionalCostCards)
            .FirstOrDefault(candidate => SameCardSet(candidate, values)) ?? values;

        ApplyActionSelection(State.Selection with
        {
            HasAdditionalCost = true,
            AdditionalCostCards = Array.AsReadOnly(canonicalOrder.ToArray()),
        }, State.Selection);
    }

    public void ToggleAdditionalCostCard(ulong instanceId)
    {
        EnsureProgressiveSelectionStarted();
        if (instanceId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(instanceId));
        }

        var values = State.Selection.AdditionalCostCards.ToList();
        if (!values.Remove(instanceId))
        {
            values.Add(instanceId);
        }

        SelectAdditionalCostCards(values);
    }

    public void SelectSlot(ulong slot)
    {
        EnsureProgressiveSelectionStarted();
        ApplyActionSelection(State.Selection with
        {
            HasSlot = true,
            Slot = slot,
        }, State.Selection);
    }

    public void SelectTarget(V05.Target target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ApplyTargetSelection(target);
    }

    /// <summary>
    /// Explicitly chooses the target-less legal variant. This is a selection,
    /// never an automatic command submission or cancellation of a paid effect.
    /// </summary>
    public void SkipOptionalTarget() => ApplyTargetSelection(null);

    private void ApplyTargetSelection(V05.Target? target)
    {
        EnsureProgressiveSelectionStarted();
        ApplyActionSelection(State.Selection with
        {
            HasTarget = true,
            Target = target,
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

    public void SelectPendingChoiceOptions(IEnumerable<string> optionIds)
    {
        EnsureMode(ProductHotseatUiMode.Choice);
        ArgumentNullException.ThrowIfNull(optionIds);
        ProductPendingChoiceState choice = State.PendingChoice;
        if (!choice.RequiresInput || choice.ChoiceId is null)
        {
            throw new InvalidOperationException("No private product choice is selectable.");
        }

        string[] requested = optionIds.ToArray();
        if (requested.Any(string.IsNullOrWhiteSpace) ||
            requested.Length != requested.Distinct(StringComparer.Ordinal).Count())
        {
            throw new ArgumentException("Choice option IDs must be non-empty and unique.", nameof(optionIds));
        }

        HashSet<string> available = choice.Options
            .Select(option => option.OptionId)
            .ToHashSet(StringComparer.Ordinal);
        if (requested.Any(option => !available.Contains(option)) ||
            (ulong)requested.Length > choice.MaximumSelections)
        {
            throw new ArgumentException("The choice selection contains an unavailable option.", nameof(optionIds));
        }

        if (!choice.Ordered)
        {
            HashSet<string> selected = requested.ToHashSet(StringComparer.Ordinal);
            requested = choice.Options
                .Where(option => selected.Contains(option.OptionId))
                .Select(option => option.OptionId)
                .ToArray();
        }

        IReadOnlyList<string> previous = choice.SelectedOptionIds;
        ProductPendingChoiceState updated = choice.WithSelection(requested);
        V05.LegalAction? canonical = null;
        V05.LegalAction[] candidates = ResolveChoiceActions(choice.ChoiceId);
        ProductHotseatSelectionStep step = ProductHotseatSelectionStep.ChooseChoiceOptions;
        if (updated.SatisfiesBounds)
        {
            V05.LegalAction[] matches = candidates.Where(action =>
                action.Command.SelectedOptionIds.SequenceEqual(requested)).ToArray();
            if (matches.Length != 1)
            {
                throw new ArgumentException(
                    "The selected option sequence is not one exact current legal choice.",
                    nameof(optionIds));
            }

            canonical = matches[0];
            step = ProductHotseatSelectionStep.Ready;
        }

        if (!previous.SequenceEqual(requested))
        {
            choiceSelectionHistory.Add(Array.AsReadOnly(previous.ToArray()));
        }

        ProductHotseatActionSelection selection = ChoiceSelection(canonical?.Command);
        ReplaceVisibleSelection(
            selection,
            State.MulliganCards,
            candidates,
            canonical,
            updated,
            step);
    }

    public void ChooseMode(string optionId)
    {
        RequirePendingChoiceKind(V05.PendingChoiceKind.Mode);
        ArgumentException.ThrowIfNullOrWhiteSpace(optionId);
        SelectPendingChoiceOptions([optionId]);
    }

    public void ChooseCards(IEnumerable<string> optionIds)
    {
        RequirePendingChoiceKind(V05.PendingChoiceKind.Cards);
        SelectPendingChoiceOptions(optionIds);
    }

    /// <summary>
    /// Selects the explicit empty resolution of an optional pending choice.
    /// Entering or revealing a choice never invokes this method implicitly.
    /// </summary>
    public void SkipPendingChoice()
    {
        EnsureMode(ProductHotseatUiMode.Choice);
        if (!State.PendingChoice.RequiresInput || State.PendingChoice.MinimumSelections != 0)
        {
            throw new InvalidOperationException("The pending product choice cannot be skipped.");
        }

        SelectPendingChoiceOptions([]);
    }

    public void OrderTriggers(IEnumerable<string> optionIds)
    {
        RequirePendingChoiceKind(V05.PendingChoiceKind.TriggerOrder);
        if (!State.PendingChoice.Ordered)
        {
            throw new ScgsProtocolException("A trigger-order choice is not marked as ordered.");
        }

        SelectPendingChoiceOptions(optionIds);
    }

    public void ChooseAdditionalCost(IEnumerable<string> optionIds)
    {
        RequirePendingChoiceKind(V05.PendingChoiceKind.AdditionalCost);
        SelectPendingChoiceOptions(optionIds);
    }

    public void TogglePendingChoiceOption(string optionId)
    {
        EnsureMode(ProductHotseatUiMode.Choice);
        ArgumentException.ThrowIfNullOrWhiteSpace(optionId);
        var selected = State.PendingChoice.SelectedOptionIds.ToList();
        if (!selected.Remove(optionId))
        {
            selected.Add(optionId);
        }

        SelectPendingChoiceOptions(selected);
    }

    public void MovePendingChoiceOption(int fromIndex, int toIndex)
    {
        EnsureMode(ProductHotseatUiMode.Choice);
        if (!State.PendingChoice.Ordered)
        {
            throw new InvalidOperationException("The pending choice is not ordered.");
        }

        var selected = State.PendingChoice.SelectedOptionIds.ToList();
        if (fromIndex < 0 || fromIndex >= selected.Count ||
            toIndex < 0 || toIndex >= selected.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(fromIndex));
        }

        string option = selected[fromIndex];
        selected.RemoveAt(fromIndex);
        selected.Insert(toIndex, option);
        SelectPendingChoiceOptions(selected);
    }

    public void SelectLegalAction(V05.LegalAction action)
    {
        EnsureNormalSelectionMode(allowChoice: true);
        ArgumentNullException.ThrowIfNull(action);
        V05.LegalAction canonical = FindCanonicalAction(action.Command);
        RequireAction(canonical.Command.Action);
        selectionHistory.Clear();
        choiceSelectionHistory.Clear();
        ProductPendingChoiceState pendingChoice = State.PendingChoice;
        if (canonical.Command.Action == V05.ActionKind.ResolveChoice)
        {
            pendingChoice = pendingChoice.WithSelection(canonical.Command.SelectedOptionIds);
        }

        ReplaceVisibleSelection(
            SelectionFrom(canonical.Command),
            State.MulliganCards,
            [canonical],
            canonical,
            pendingChoice,
            ProductHotseatSelectionStep.Ready);
    }

    public V05.PaymentPreview? PreviewSelectedPayment()
    {
        EnsureNormalSelectionMode(allowChoice: true);
        V05.LegalAction selected = State.SelectedAction ??
            throw new InvalidOperationException("An exact legal action is required.");
        V05.PaymentResult result = session.PreviewPayment(selected.Command);
        if (result.Revision != State.Interaction.Revision)
        {
            throw new ScgsProtocolException("The product payment preview has a stale revision.");
        }

        if (!ProductHotseatCommandComparer.PaymentsEqual(result.Payment, selected.Payment))
        {
            throw new ScgsProtocolException("The product payment preview changed after enumeration.");
        }

        return result.Payment;
    }

    public bool StepBackSelection()
    {
        ThrowIfDisposed();
        EnsureNotPrepared();
        if (State.Mode == ProductHotseatUiMode.Choice && choiceSelectionHistory.Count != 0)
        {
            int index = choiceSelectionHistory.Count - 1;
            IReadOnlyList<string> previous = choiceSelectionHistory[index];
            choiceSelectionHistory.RemoveAt(index);
            SelectPendingChoiceOptionsWithoutHistory(previous);
            return true;
        }

        EnsureNormalSelectionMode();
        if (selectionHistory.Count == 0)
        {
            return false;
        }

        int last = selectionHistory.Count - 1;
        ProductHotseatActionSelection selection = selectionHistory[last];
        selectionHistory.RemoveAt(last);
        ApplyActionSelection(selection, historyEntry: null);
        return true;
    }

    public void CancelSelection()
    {
        ThrowIfDisposed();
        EnsureNotPrepared();
        selectionHistory.Clear();
        choiceSelectionHistory.Clear();
        switch (State.Mode)
        {
            case ProductHotseatUiMode.MulliganSelecting:
                ApplyMulliganSelection([]);
                break;
            case ProductHotseatUiMode.Action:
            case ProductHotseatUiMode.Reaction:
                ReplaceVisibleSelection(
                    ProductHotseatActionSelection.Empty,
                    [],
                    State.LegalActions,
                    null,
                    State.PendingChoice,
                    ProductHotseatSelectionStep.None);
                break;
            case ProductHotseatUiMode.Choice:
                SelectPendingChoiceOptionsWithoutHistory([]);
                break;
            default:
                throw new InvalidOperationException("There is no visible selection to cancel.");
        }
    }

    /// <summary>
    /// Called only after the user confirms surrender. Merely opening or
    /// cancelling that confirmation must leave the current paid choice intact.
    /// </summary>
    public bool PrepareSurrender()
    {
        ThrowIfDisposed();
        EnsureNotPrepared();
        if (State.Mode is not ProductHotseatUiMode.Action and
            not ProductHotseatUiMode.Reaction and not ProductHotseatUiMode.Choice)
        {
            return false;
        }

        V05.LegalAction? surrender = State.LegalActions.SingleOrDefault(action =>
            action.Command.Action == V05.ActionKind.Surrender);
        if (surrender is null)
        {
            return false;
        }

        SelectLegalAction(surrender);
        return PrepareSelectedCommand();
    }

    public bool PrepareSelectedCommand()
    {
        ThrowIfDisposed();
        if (submissionInProgress || preparedCommand is not null || State.SelectedAction is null ||
            State.Viewer is null || State.Interaction.Step != ProductHotseatSelectionStep.Ready ||
            State.Mode is not ProductHotseatUiMode.MulliganSelecting and
                not ProductHotseatUiMode.Action and
                not ProductHotseatUiMode.Reaction and
                not ProductHotseatUiMode.Choice)
        {
            return false;
        }

        V05.LegalAction canonical = FindCanonicalAction(State.SelectedAction.Command);
        V05.MatchView snapshot = State.Snapshot ??
            throw new InvalidOperationException("The product snapshot is unavailable.");
        preparedCommand = ProductHotseatCommandComparer.Clone(canonical.Command);
        preparedViewer = State.Viewer.Value;
        ResetPublicFrameGate();
        selectionHistory.Clear();
        choiceSelectionHistory.Clear();
        SetState(CreateState(
            ProductHotseatUiMode.Resolving,
            null,
            null,
            null,
            null,
            [],
            [],
            ProductHotseatActionSelection.Empty,
            [],
            null,
            ProductPendingChoiceState.None(snapshot.Revision),
            [],
            [],
            null,
            lastEngineCode: null,
            failureText: null,
            commandPrepared: true,
            publicBoard: new ProductHotseatPublicBoardView(snapshot),
            ProductHotseatSelectionStep.None));
        return true;
    }

    /// <summary>
    /// Records one fully presented public-board frame. Repeated or older frame
    /// tokens do not advance the gate, so a caller cannot accidentally submit
    /// from two callbacks belonging to the same rendered frame.
    /// </summary>
    public bool NotifyPublicFrameDrawn(ulong frameToken)
    {
        ThrowIfDisposed();
        if (State.Mode != ProductHotseatUiMode.Resolving ||
            !State.CommandPrepared || preparedCommand is null || State.PublicBoard is null)
        {
            throw new InvalidOperationException("No resolving public board is awaiting presentation.");
        }

        if (!lastPublicFrameToken.HasValue || frameToken > lastPublicFrameToken.Value)
        {
            lastPublicFrameToken = frameToken;
            publicFramesDrawn = Math.Min(
                RequiredPublicFramesBeforeSubmit,
                publicFramesDrawn + 1);
        }

        return CanSubmitPreparedCommand;
    }

    public V05.EngineStatus SubmitPreparedCommand()
    {
        ThrowIfDisposed();
        if (submissionInProgress || preparedCommand is null ||
            State.Mode != ProductHotseatUiMode.Resolving || !State.CommandPrepared)
        {
            throw new InvalidOperationException("No prepared product command is ready.");
        }

        if (!CanSubmitPreparedCommand)
        {
            throw new InvalidOperationException(
                "The safe public board must be presented for two complete frames before submission.");
        }

        V05.GameCommandRequest command = preparedCommand;
        V05.PlayerId previousViewer = preparedViewer;
        preparedCommand = null;
        ResetPublicFrameGate();
        submissionInProgress = true;
        V05.EngineStatus status;
        try
        {
            status = session.SubmitCommand(command) ??
                throw new ScgsProtocolException("The product engine returned a null status.");
        }
        catch
        {
            submissionInProgress = false;
            SetFaulted();
            throw;
        }

        submissionInProgress = false;
        if (!status.IsSuccess)
        {
            // A rejected revision-bound command must not cause any viewer read,
            // event acknowledgement or cursor movement. The same player can
            // explicitly reveal again to obtain a fresh authoritative state.
            SetState(CoveredState(
                previousViewer,
                ProductHotseatCoverReason.FailedCommand,
                status.RawCode));
            return status;
        }

        try
        {
            if (command.Action == V05.ActionKind.Mulligan)
            {
                ShowMulliganReview(previousViewer);
            }
            else
            {
                RefreshForViewer(previousViewer, lastEngineCode: null);
            }
        }
        catch
        {
            SetFaulted();
            throw;
        }

        return status;
    }

    public void CompleteMulliganReview()
    {
        EnsureMode(ProductHotseatUiMode.MulliganReview);
        V05.MatchView view = State.Snapshot ??
            throw new InvalidOperationException("The mulligan review has no snapshot.");
        V05.PlayerId viewer = State.Viewer!.Value;
        V05.PlayerId? actor = DetermineActor(view);
        if (actor.HasValue && actor != viewer)
        {
            SetState(CoveredState(
                actor.Value,
                ProductHotseatCoverReason.PassingDevice,
                lastEngineCode: null));
            return;
        }

        RefreshForViewer(viewer, lastEngineCode: null);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        preparedCommand = null;
        ResetPublicFrameGate();
        submissionInProgress = false;
        selectionHistory.Clear();
        choiceSelectionHistory.Clear();
        Array.Clear(pendingEventBatches);
        session.Dispose();
        SetState(CreateState(
            ProductHotseatUiMode.Disposed,
            null,
            null,
            null,
            null,
            [],
            [],
            ProductHotseatActionSelection.Empty,
            [],
            null,
            ProductPendingChoiceState.None(0),
            [],
            [],
            null,
            null,
            null,
            false,
            null,
            ProductHotseatSelectionStep.None));
    }

    public static V05.PlayerId? DetermineActor(V05.MatchView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.PendingChoice.Pending)
        {
            return view.PendingChoice.Chooser ??
                throw new ScgsProtocolException("A pending choice has no chooser.");
        }

        if (view.Reaction.Pending)
        {
            return view.Reaction.Responder;
        }

        return view.Phase switch
        {
            V05.MatchPhase.Mulligan when !view.Players[(int)V05.PlayerId.Player0].MulliganDone =>
                V05.PlayerId.Player0,
            V05.MatchPhase.Mulligan when !view.Players[(int)V05.PlayerId.Player1].MulliganDone =>
                V05.PlayerId.Player1,
            V05.MatchPhase.Mulligan or V05.MatchPhase.Action => view.ActivePlayer,
            V05.MatchPhase.Reaction => throw new ScgsProtocolException(
                "Reaction phase has neither a pending choice nor a reaction."),
            V05.MatchPhase.Finished => null,
            _ => throw new ScgsProtocolException($"Phase {view.Phase} has no product actor."),
        };
    }

    private void RefreshForViewer(
        V05.PlayerId viewer,
        uint? lastEngineCode,
        int remainingRetries = RefreshRetryLimit)
    {
        selectionHistory.Clear();
        choiceSelectionHistory.Clear();
        V05.MatchView view = session.GetView(viewer);
        ValidateView(view, viewer);
        V05.PlayerId? actor = DetermineActor(view);
        if (actor.HasValue && actor != viewer)
        {
            SetState(CoveredState(
                actor.Value,
                ProductHotseatCoverReason.PassingDevice,
                lastEngineCode));
            return;
        }

        ProductHotseatUiMode mode = ModeFor(view);
        V05.LegalAction[] legalActions = [];
        if (mode != ProductHotseatUiMode.Finished)
        {
            V05.LegalActionsResult legal = session.ListLegalActions(
                new V05.ActionQueryRequest(viewer, view.Revision));
            if (legal.Revision != view.Revision)
            {
                RetryRefreshOrThrow(viewer, lastEngineCode, remainingRetries);
                return;
            }

            legalActions = legal.Actions.ToArray();
            ValidateLegalActions(legalActions, viewer, view);
        }

        V05.EventBatch eventBatch = session.ReadEvents(viewer, eventCursors.For(viewer));
        if (eventBatch.Revision != view.Revision)
        {
            RetryRefreshOrThrow(viewer, lastEngineCode, remainingRetries);
            return;
        }

        ValidateEventBatch(eventBatch, eventCursors.For(viewer));

        SetPendingBatch(viewer, eventBatch);
        ProductPendingChoiceState choice = ProjectChoice(view.PendingChoice, viewer);
        IReadOnlyList<V05.LegalAction> candidates = legalActions;
        ProductHotseatActionSelection selection = ProductHotseatActionSelection.Empty;
        V05.LegalAction? selectedAction = null;
        ProductHotseatSelectionStep step = ProductHotseatSelectionStep.None;
        IReadOnlyList<ulong> mulliganCards = [];

        if (mode == ProductHotseatUiMode.MulliganSelecting)
        {
            V05.LegalAction[] empty = legalActions.Where(action =>
                action.Command.Action == V05.ActionKind.Mulligan &&
                action.Command.MulliganCards.Count == 0).ToArray();
            if (empty.Length != 1)
            {
                throw new ScgsProtocolException("Mulligan requires one empty legal candidate.");
            }

            candidates = empty;
            selectedAction = empty[0];
            selection = SelectionFrom(empty[0].Command);
            step = ProductHotseatSelectionStep.Ready;
        }
        else if (mode == ProductHotseatUiMode.Choice)
        {
            V05.LegalAction[] resolveChoices = ResolveChoiceActions(legalActions, choice.ChoiceId!);
            if (resolveChoices.Length == 0)
            {
                throw new ScgsProtocolException("A pending choice exposes no legal resolution.");
            }

            candidates = resolveChoices;
            selection = new ProductHotseatActionSelection
            {
                Action = V05.ActionKind.ResolveChoice,
                HasSource = true,
                Source = 0,
                HasMode = true,
                HasAdditionalCost = true,
                HasSlot = true,
                HasTarget = true,
                HasAdvanceChoice = true,
            };
            step = ProductHotseatSelectionStep.ChooseChoiceOptions;
        }

        bool pendingEvents = pendingEventBatches[PlayerIndex(viewer)] is not null;
        SetState(CreateState(
            mode,
            null,
            viewer,
            null,
            view,
            legalActions,
            candidates,
            selection,
            mulliganCards,
            selectedAction,
            choice,
            eventBatch.Events,
            pendingEvents ? eventBatch.Events : [],
            pendingEvents ? eventBatch.LastSequence : null,
            lastEngineCode,
            lastEngineCode.HasValue ? $"引擎拒绝了上一条命令（{lastEngineCode.Value}）。" : null,
            false,
            null,
            step));
    }

    private static ProductHotseatUiMode ModeFor(V05.MatchView view)
    {
        if (view.PendingChoice.Pending)
        {
            return ProductHotseatUiMode.Choice;
        }

        return view.Phase switch
        {
            V05.MatchPhase.Mulligan => ProductHotseatUiMode.MulliganSelecting,
            V05.MatchPhase.Action => ProductHotseatUiMode.Action,
            V05.MatchPhase.Reaction when view.Reaction.Pending => ProductHotseatUiMode.Reaction,
            V05.MatchPhase.Finished => ProductHotseatUiMode.Finished,
            _ => throw new ScgsProtocolException($"Phase {view.Phase} cannot be displayed."),
        };
    }

    private void RetryRefreshOrThrow(
        V05.PlayerId viewer,
        uint? lastEngineCode,
        int remainingRetries)
    {
        if (remainingRetries <= 0)
        {
            throw new ScgsProtocolException("The product snapshot revision did not stabilize.");
        }

        RefreshForViewer(viewer, lastEngineCode, remainingRetries - 1);
    }

    private void ShowMulliganReview(V05.PlayerId viewer)
    {
        V05.MatchView view = session.GetView(viewer);
        ValidateView(view, viewer);
        if (!view.Players[(int)viewer].MulliganDone)
        {
            throw new ScgsProtocolException("A successful mulligan was not recorded.");
        }

        V05.EventBatch events = session.ReadEvents(viewer, eventCursors.For(viewer));
        if (events.Revision != view.Revision)
        {
            throw new ScgsProtocolException("Mulligan review events have a stale revision.");
        }

        ValidateEventBatch(events, eventCursors.For(viewer));

        SetPendingBatch(viewer, events);
        bool pending = pendingEventBatches[PlayerIndex(viewer)] is not null;
        SetState(CreateState(
            ProductHotseatUiMode.MulliganReview,
            null,
            viewer,
            null,
            view,
            [],
            [],
            ProductHotseatActionSelection.Empty,
            [],
            null,
            ProductPendingChoiceState.None(view.Revision),
            events.Events,
            pending ? events.Events : [],
            pending ? events.LastSequence : null,
            null,
            null,
            false,
            null,
            ProductHotseatSelectionStep.None));
    }

    private void ApplyMulliganSelection(IEnumerable<ulong> instanceIds)
    {
        V05.MatchView view = State.Snapshot ??
            throw new InvalidOperationException("The mulligan snapshot is unavailable.");
        V05.PlayerId viewer = State.Viewer!.Value;
        ulong[] handOrder = view.Players[(int)viewer].Hand
            .Select(card => card.InstanceId ??
                throw new ScgsProtocolException("A visible hand card has no instance ID."))
            .ToArray();
        HashSet<ulong> requested = instanceIds.ToHashSet();
        if (requested.Count != instanceIds.Count() || requested.Any(id => !handOrder.Contains(id)))
        {
            throw new ArgumentException("The mulligan selection is not a unique hand subset.", nameof(instanceIds));
        }

        ulong[] normalized = handOrder.Where(requested.Contains).ToArray();
        V05.LegalAction[] local = State.LegalActions.Where(action =>
            action.Command.Action == V05.ActionKind.Mulligan &&
            action.Command.MulliganCards.SequenceEqual(normalized)).ToArray();
        if (local.Length != 1)
        {
            throw new ArgumentException("The mulligan subset is not currently legal.", nameof(instanceIds));
        }

        V05.LegalActionsResult queried = session.ListLegalActions(new V05.ActionQueryRequest(
            viewer,
            view.Revision)
        {
            Action = V05.ActionKind.Mulligan,
            MulliganCards = normalized,
        });
        RequireRevision(queried.Revision, view.Revision);
        RequireSameCandidateSet(local, queried.Actions);
        ReplaceVisibleSelection(
            SelectionFrom(local[0].Command),
            normalized,
            local,
            local[0],
            State.PendingChoice,
            ProductHotseatSelectionStep.Ready);
    }

    private void ApplyActionSelection(
        ProductHotseatActionSelection requested,
        ProductHotseatActionSelection? historyEntry)
    {
        V05.MatchView view = State.Snapshot ??
            throw new InvalidOperationException("The product snapshot is unavailable.");
        V05.PlayerId viewer = State.Viewer!.Value;
        ProductHotseatActionSelection frozen = requested.Freeze();
        V05.LegalAction[] local = State.LegalActions
            .Where(action => MatchesSelection(action.Command, frozen))
            .ToArray();
        if (local.Length == 0)
        {
            throw new ArgumentException("The partial product selection has no legal candidate.", nameof(requested));
        }

        // Schema 2 does not allow a source-only query without Action, nor
        // unrelated default fields (even false or []). Until the source's
        // action is chosen, refresh the whole legal set and refine locally.
        V05.ActionQueryRequest query = frozen.Action is null
            ? new V05.ActionQueryRequest(viewer, view.Revision)
            : new V05.ActionQueryRequest(viewer, view.Revision)
            {
                Action = frozen.Action,
                Source = frozen.HasSource && frozen.Source != 0 ? frozen.Source : null,
                Target = frozen.HasTarget ? frozen.Target : null,
                Slot = frozen.HasSlot ? frozen.Slot : null,
                ModeId = frozen.HasMode ? frozen.ModeId : null,
                UseAdvance = frozen.HasAdvanceChoice && frozen.Action is
                    V05.ActionKind.PlayUnit or V05.ActionKind.CastSpell or V05.ActionKind.PlayTrap or
                    V05.ActionKind.Deploy or V05.ActionKind.PlayAmulet or V05.ActionKind.PlayField
                        ? frozen.UseAdvance
                        : null,
                AdditionalCostCards = frozen.HasAdditionalCost && frozen.Action == V05.ActionKind.Deploy
                    ? frozen.AdditionalCostCards
                    : null,
            };
        V05.LegalActionsResult result = session.ListLegalActions(query);
        RequireRevision(result.Revision, view.Revision);
        V05.LegalAction[] queried = result.Actions.Where(action =>
            local.Any(expected => ProductHotseatCommandComparer.CommandsEqual(
                expected.Command,
                action.Command))).ToArray();
        RequireSameCandidateSet(local, queried);

        ProductHotseatActionSelection completed = CompleteInvariantDefaults(frozen, local);
        ProductHotseatSelectionStep step = DetermineSelectionStep(completed, local);
        V05.LegalAction? canonical = local.Length == 1 && step == ProductHotseatSelectionStep.Ready
            ? local[0]
            : null;
        if (historyEntry is not null && !Equals(historyEntry.Freeze(), completed))
        {
            selectionHistory.Add(historyEntry.Freeze());
        }

        ReplaceVisibleSelection(
            completed,
            State.MulliganCards,
            local,
            canonical,
            State.PendingChoice,
            step);
    }

    private void SelectPendingChoiceOptionsWithoutHistory(IEnumerable<string> optionIds)
    {
        IReadOnlyList<IReadOnlyList<string>> preserved = choiceSelectionHistory.ToArray();
        choiceSelectionHistory.Clear();
        SelectPendingChoiceOptions(optionIds);
        choiceSelectionHistory.Clear();
        foreach (IReadOnlyList<string> entry in preserved)
        {
            choiceSelectionHistory.Add(entry);
        }
    }

    private V05.LegalAction[] ResolveChoiceActions(string choiceId) =>
        ResolveChoiceActions(State.LegalActions, choiceId);

    private static V05.LegalAction[] ResolveChoiceActions(
        IEnumerable<V05.LegalAction> actions,
        string choiceId) => actions.Where(action =>
            action.Command.Action == V05.ActionKind.ResolveChoice &&
            string.Equals(action.Command.ChoiceId, choiceId, StringComparison.Ordinal)).ToArray();

    private void ReplaceVisibleSelection(
        ProductHotseatActionSelection selection,
        IEnumerable<ulong> mulliganCards,
        IEnumerable<V05.LegalAction> candidates,
        V05.LegalAction? selectedAction,
        ProductPendingChoiceState pendingChoice,
        ProductHotseatSelectionStep step)
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
            pendingChoice,
            State.Events,
            State.PendingEvents,
            State.PendingEventLastSequence,
            State.LastEngineCode,
            State.FailureText,
            false,
            State.PublicBoard,
            step));
    }

    private V05.LegalAction FindCanonicalAction(V05.GameCommandRequest command)
    {
        V05.LegalAction[] matches = State.LegalActions.Where(action =>
            ProductHotseatCommandComparer.CommandsEqual(action.Command, command)).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new ArgumentException("The product command is not currently legal.", nameof(command)),
            _ => throw new ScgsProtocolException("The product legal-action list has duplicate commands."),
        };
    }

    private static ProductHotseatActionSelection CompleteInvariantDefaults(
        ProductHotseatActionSelection selection,
        IReadOnlyList<V05.LegalAction> candidates)
    {
        ProductHotseatActionSelection result = selection;
        V05.ActionKind[] actions = candidates.Select(item => item.Command.Action).Distinct().ToArray();
        if (!result.Action.HasValue && actions.Length == 1)
        {
            result = result with { Action = actions[0] };
        }

        if (!result.HasSource && candidates.All(candidate => candidate.Command.Source == 0))
        {
            result = result with { HasSource = true, Source = 0 };
        }

        if (!result.HasMode && candidates.All(candidate => candidate.Command.ModeId is null))
        {
            result = result with { HasMode = true, ModeId = null };
        }

        if (!result.HasAdditionalCost && candidates.All(candidate =>
                candidate.Command.AdditionalCostCards.Count == 0))
        {
            result = result with
            {
                HasAdditionalCost = true,
                AdditionalCostCards = Array.Empty<ulong>(),
            };
        }

        if (!result.HasSlot && candidates.All(candidate => !candidate.Command.Slot.HasValue))
        {
            result = result with { HasSlot = true, Slot = null };
        }

        if (!result.HasTarget && candidates.All(candidate => candidate.Command.Target is null))
        {
            result = result with { HasTarget = true, Target = null };
        }

        if (!result.HasAdvanceChoice && candidates.All(candidate => !candidate.Command.UseAdvance))
        {
            result = result with { HasAdvanceChoice = true, UseAdvance = false };
        }

        return result.Freeze();
    }

    private static ProductHotseatSelectionStep DetermineSelectionStep(
        ProductHotseatActionSelection selection,
        IReadOnlyList<V05.LegalAction> candidates)
    {
        if (!selection.Action.HasValue)
        {
            return selection.HasSource
                ? ProductHotseatSelectionStep.ChooseAction
                : ProductHotseatSelectionStep.ChooseSource;
        }

        if (!selection.HasSource)
        {
            return ProductHotseatSelectionStep.ChooseSource;
        }

        if (!selection.HasMode && candidates.Any(candidate => candidate.Command.ModeId is not null))
        {
            return ProductHotseatSelectionStep.ChooseMode;
        }

        if (!selection.HasAdditionalCost && candidates.Any(candidate =>
                candidate.Command.AdditionalCostCards.Count != 0))
        {
            return ProductHotseatSelectionStep.ChooseAdditionalCost;
        }

        if (!selection.HasSlot && candidates.Any(candidate => candidate.Command.Slot.HasValue))
        {
            return ProductHotseatSelectionStep.ChooseSlot;
        }

        if (!selection.HasTarget && candidates.Any(candidate => candidate.Command.Target is not null))
        {
            return ProductHotseatSelectionStep.ChooseTarget;
        }

        if (!selection.HasAdvanceChoice && candidates.Any(candidate => candidate.Command.UseAdvance))
        {
            return ProductHotseatSelectionStep.ChooseAdvance;
        }

        return ProductHotseatSelectionStep.Ready;
    }

    private static bool MatchesSelection(
        V05.GameCommandRequest command,
        ProductHotseatActionSelection selection) =>
        (!selection.Action.HasValue || command.Action == selection.Action) &&
        (!selection.HasSource || command.Source == selection.Source) &&
        (!selection.HasMode || string.Equals(command.ModeId, selection.ModeId, StringComparison.Ordinal)) &&
        (!selection.HasAdditionalCost ||
         SameCardSet(command.AdditionalCostCards, selection.AdditionalCostCards)) &&
        (!selection.HasSlot || command.Slot == selection.Slot) &&
        (!selection.HasTarget || Equals(command.Target, selection.Target)) &&
        (!selection.HasAdvanceChoice || command.UseAdvance == selection.UseAdvance);

    private static bool SameCardSet(
        IReadOnlyList<ulong> left,
        IReadOnlyList<ulong> right) =>
        left.Count == right.Count && left.All(right.Contains);

    private static ProductHotseatActionSelection SelectionFrom(V05.GameCommandRequest command) => new()
    {
        Action = command.Action,
        HasSource = true,
        Source = command.Source,
        HasMode = true,
        ModeId = command.ModeId,
        HasAdditionalCost = true,
        AdditionalCostCards = Array.AsReadOnly(command.AdditionalCostCards.ToArray()),
        HasSlot = true,
        Slot = command.Slot,
        HasTarget = true,
        Target = command.Target,
        HasAdvanceChoice = true,
        UseAdvance = command.UseAdvance,
    };

    private static ProductHotseatActionSelection ChoiceSelection(V05.GameCommandRequest? command) =>
        command is null
            ? new ProductHotseatActionSelection
            {
                Action = V05.ActionKind.ResolveChoice,
                HasSource = true,
                HasMode = true,
                HasAdditionalCost = true,
                HasSlot = true,
                HasTarget = true,
                HasAdvanceChoice = true,
            }
            : SelectionFrom(command);

    private static ProductPendingChoiceState ProjectChoice(
        V05.PendingChoiceView choice,
        V05.PlayerId viewer)
    {
        if (!choice.Pending)
        {
            return ProductPendingChoiceState.None(choice.Revision);
        }

        V05.PlayerId chooser = choice.Chooser ??
            throw new ScgsProtocolException("A pending product choice has no chooser.");
        if (chooser != viewer)
        {
            if (choice.ChoiceId is not null || choice.Kind.HasValue ||
                choice.MinimumSelections.HasValue || choice.MaximumSelections.HasValue ||
                choice.Ordered.HasValue || choice.Options.Length != 0)
            {
                throw new ScgsProtocolException("An opponent product choice leaked private details.");
            }

            return new ProductPendingChoiceState(
                true, true, chooser, null, null, 0, 0, false, [], [], choice.Revision);
        }

        string choiceId = string.IsNullOrWhiteSpace(choice.ChoiceId)
            ? throw new ScgsProtocolException("The choice owner received no opaque choice ID.")
            : choice.ChoiceId;
        V05.PendingChoiceKind kind = choice.Kind ??
            throw new ScgsProtocolException("The choice owner received no choice kind.");
        ulong minimum = choice.MinimumSelections ??
            throw new ScgsProtocolException("The choice owner received no minimum.");
        ulong maximum = choice.MaximumSelections ??
            throw new ScgsProtocolException("The choice owner received no maximum.");
        bool ordered = choice.Ordered ??
            throw new ScgsProtocolException("The choice owner received no ordering flag.");
        if (minimum > maximum || maximum > (ulong)choice.Options.Length ||
            choice.Options.Any(option => string.IsNullOrWhiteSpace(option.OptionId)) ||
            choice.Options.Select(option => option.OptionId)
                .Distinct(StringComparer.Ordinal).Count() != choice.Options.Length ||
            (kind == V05.PendingChoiceKind.TriggerOrder && !ordered))
        {
            throw new ScgsProtocolException("The pending product choice shape is invalid.");
        }

        return new ProductPendingChoiceState(
            true,
            false,
            chooser,
            choiceId,
            kind,
            minimum,
            maximum,
            ordered,
            choice.Options,
            [],
            choice.Revision);
    }

    private static void ValidateLegalActions(
        IEnumerable<V05.LegalAction> actions,
        V05.PlayerId viewer,
        V05.MatchView view)
    {
        V05.LegalAction[] materialized = actions.ToArray();
        if (materialized.Any(action => action.Command.Player != viewer ||
                                       action.Command.ExpectedRevision != view.Revision ||
                                       !Enum.IsDefined(action.Command.Action)))
        {
            throw new ScgsProtocolException("A product legal action has the wrong actor, revision or kind.");
        }

        if (materialized.Select(action => action.Command)
            .Distinct(ProductCommandEqualityComparer.Instance).Count() != materialized.Length)
        {
            throw new ScgsProtocolException("The product legal-action list contains duplicates.");
        }

        V05.PlayerView player = view.Players[(int)viewer];
        foreach (V05.GameCommandRequest command in materialized.Select(action => action.Command))
        {
            if (command.Action is V05.ActionKind.PlayUnit or V05.ActionKind.PlayAmulet or
                    V05.ActionKind.Deploy)
            {
                if (command.Slot is not { } mainSlot || mainSlot >= (ulong)player.MainBoard.Length)
                {
                    throw new ScgsProtocolException("A main-board action must name an owned slot.");
                }

                V05.CardView? occupant = player.MainBoard[(int)mainSlot];
                bool vacatedByAdditionalCost = command.Action == V05.ActionKind.Deploy &&
                    occupant?.InstanceId is { } occupantId &&
                    command.AdditionalCostCards.Contains(occupantId);
                if (occupant is not null && !vacatedByAdditionalCost)
                {
                    throw new ScgsProtocolException(
                        "A main-board action must name an empty slot or one vacated by its additional cost.");
                }
            }

            if (command.Action is V05.ActionKind.CastSpell or V05.ActionKind.PlayTrap &&
                (command.Slot is not { } tacticSlot || tacticSlot >= (ulong)player.Tactics.Length ||
                 player.Tactics[(int)tacticSlot] is not null))
            {
                throw new ScgsProtocolException("A tactic action must name an empty owned slot.");
            }

            if (command.Action == V05.ActionKind.PlayField && command.Slot.HasValue)
            {
                throw new ScgsProtocolException("A field action must use the independent field zone.");
            }
        }
    }

    private static void ValidateView(V05.MatchView view, V05.PlayerId viewer)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.Viewer != viewer ||
            view.Players.Length != 2 ||
            view.Players[0].Player != V05.PlayerId.Player0 ||
            view.Players[1].Player != V05.PlayerId.Player1 ||
            !Enum.IsDefined(view.ActivePlayer) ||
            !Enum.IsDefined(view.FirstPlayer) ||
            !Enum.IsDefined(view.Phase) ||
            !Enum.IsDefined(view.Result) ||
            view.Reaction.Revision != view.Revision ||
            view.PendingChoice.Revision != view.Revision)
        {
            throw new ScgsProtocolException("The product snapshot envelope is inconsistent.");
        }

        foreach (V05.PlayerView player in view.Players)
        {
            bool ownsPrivateHand = player.Player == viewer;
            if (player.MainBoard.Length != 5 || player.Tactics.Length != 3 ||
                (ownsPrivateHand && player.HandCount != (ulong)player.Hand.Length) ||
                (!ownsPrivateHand && player.Hand.Length != 0))
            {
                throw new ScgsProtocolException("The product player snapshot has an unsafe zone shape.");
            }
        }

        if (view.Reaction.Pending)
        {
            if (!Enum.IsDefined(view.Reaction.Responder) ||
                view.Reaction.Window == V05.ReactionWindow.None)
            {
                throw new ScgsProtocolException("The pending product reaction is malformed.");
            }
        }
        else if (view.Phase == V05.MatchPhase.Reaction)
        {
            throw new ScgsProtocolException("Reaction phase has no pending reaction.");
        }

        if (view.PendingChoice.Pending &&
            (!view.PendingChoice.Chooser.HasValue ||
             !Enum.IsDefined(view.PendingChoice.Chooser.Value)))
        {
            throw new ScgsProtocolException("The pending product choice has no valid chooser.");
        }
    }

    private static void ValidateEventBatch(V05.EventBatch batch, ulong afterSequence)
    {
        if (batch.LastSequence < afterSequence)
        {
            throw new ScgsProtocolException("The product event cursor moved backwards.");
        }

        ulong previous = afterSequence;
        foreach (V05.GameEventView gameEvent in batch.Events)
        {
            if (gameEvent.Sequence <= previous || gameEvent.Sequence > batch.LastSequence ||
                !Enum.IsDefined(gameEvent.Type) || !Enum.IsDefined(gameEvent.Player) ||
                (gameEvent.HiddenCard &&
                 (gameEvent.Card.HasValue || gameEvent.DesignId is not null)))
            {
                throw new ScgsProtocolException("The product event batch is malformed or leaks identity.");
            }

            previous = gameEvent.Sequence;
        }

        if ((batch.Events.Count == 0 && batch.LastSequence != afterSequence) ||
            (batch.Events.Count != 0 && previous != batch.LastSequence))
        {
            throw new ScgsProtocolException("The product event batch does not cover its cursor interval.");
        }
    }

    private static void RequireSameCandidateSet(
        IReadOnlyList<V05.LegalAction> expected,
        IEnumerable<V05.LegalAction> actual)
    {
        V05.LegalAction[] materialized = actual.ToArray();
        bool same = expected.Count == materialized.Length && expected.All(candidate =>
            materialized.Any(other => ProductHotseatCommandComparer.CommandsEqual(
                candidate.Command,
                other.Command)));
        if (!same)
        {
            throw new ScgsProtocolException("A partial product query disagrees with the visible candidate set.");
        }
    }

    private static void RequireRevision(ulong actual, ulong expected)
    {
        if (actual != expected)
        {
            throw new ScgsProtocolException("A product query returned a stale revision.");
        }
    }

    private void SetPendingBatch(V05.PlayerId viewer, V05.EventBatch batch)
    {
        pendingEventBatches[PlayerIndex(viewer)] = batch.LastSequence == eventCursors.For(viewer)
            ? null
            : batch;
    }

    private ProductHotseatUiState CoveredState(
        V05.PlayerId awaitingPlayer,
        ProductHotseatCoverReason reason,
        uint? lastEngineCode) => CreateState(
        ProductHotseatUiMode.Covered,
        reason,
        null,
        awaitingPlayer,
        null,
        [],
        [],
        ProductHotseatActionSelection.Empty,
        [],
        null,
        ProductPendingChoiceState.None(0),
        [],
        [],
        null,
        lastEngineCode,
        lastEngineCode.HasValue ? $"引擎拒绝了上一条命令（{lastEngineCode.Value}）。" : null,
        false,
        null,
        ProductHotseatSelectionStep.None);

    private ProductHotseatUiState CreateState(
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
        uint? lastEngineCode,
        string? failureText,
        bool commandPrepared,
        ProductHotseatPublicBoardView? publicBoard,
        ProductHotseatSelectionStep step) => new(
        mode,
        coverReason,
        viewer,
        awaitingPlayer,
        snapshot,
        legalActions,
        candidates,
        selection,
        mulliganCards,
        selectedAction,
        pendingChoice,
        events,
        pendingEvents,
        pendingEventLastSequence,
        eventCursors,
        lastEngineCode,
        failureText,
        commandPrepared,
        publicBoard,
        step,
        selectionHistory.Count != 0 || choiceSelectionHistory.Count != 0);

    private static ProductHotseatUiState CopyState(
        ProductHotseatUiState state,
        IEnumerable<V05.GameEventView> pendingEvents,
        ulong? pendingEventLastSequence,
        ProductHotseatEventCursors cursors) => new(
        state.Mode,
        state.CoverReason,
        state.Viewer,
        state.AwaitingPlayer,
        state.Snapshot,
        state.LegalActions,
        state.Interaction.Options.Actions,
        state.Selection,
        state.MulliganCards,
        state.SelectedAction,
        state.PendingChoice,
        state.Events,
        pendingEvents,
        pendingEventLastSequence,
        cursors,
        state.LastEngineCode,
        state.FailureText,
        state.CommandPrepared,
        state.PublicBoard,
        state.Interaction.Step,
        state.Interaction.CanStepBack);

    private void SetFaulted()
    {
        ResetPublicFrameGate();
        selectionHistory.Clear();
        choiceSelectionHistory.Clear();
        SetState(CreateState(
            ProductHotseatUiMode.Faulted,
            null,
            null,
            null,
            null,
            [],
            [],
            ProductHotseatActionSelection.Empty,
            [],
            null,
            ProductPendingChoiceState.None(0),
            [],
            [],
            null,
            null,
            "客户端无法继续读取产品对局。",
            false,
            null,
            ProductHotseatSelectionStep.None));
    }

    private void EnsureNormalSelectionMode(bool allowChoice = false)
    {
        ThrowIfDisposed();
        bool valid = State.Mode is ProductHotseatUiMode.Action or ProductHotseatUiMode.Reaction ||
                     (allowChoice && State.Mode == ProductHotseatUiMode.Choice);
        if (!valid)
        {
            throw new InvalidOperationException("Product actions cannot be selected in this mode.");
        }
    }

    private void EnsureProgressiveSelectionStarted()
    {
        EnsureNormalSelectionMode();
        if (!State.Selection.Action.HasValue)
        {
            throw new InvalidOperationException("An action must be selected first.");
        }
    }

    private void EnsureMode(ProductHotseatUiMode mode)
    {
        ThrowIfDisposed();
        if (State.Mode != mode)
        {
            throw new InvalidOperationException($"The product UI mode is not {mode}.");
        }
    }

    private void RequirePendingChoiceKind(V05.PendingChoiceKind kind)
    {
        EnsureMode(ProductHotseatUiMode.Choice);
        if (!State.PendingChoice.RequiresInput || State.PendingChoice.Kind != kind)
        {
            throw new InvalidOperationException($"The pending product choice is not {kind}.");
        }
    }

    private void EnsureNotPrepared()
    {
        if (submissionInProgress || preparedCommand is not null || State.CommandPrepared)
        {
            throw new InvalidOperationException("A prepared product command cannot be changed.");
        }
    }

    private static void RequireAction(V05.ActionKind action)
    {
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported action.");
        }
    }

    private static int PlayerIndex(V05.PlayerId player) => player switch
    {
        V05.PlayerId.Player0 => 0,
        V05.PlayerId.Player1 => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(player), player, "Unsupported player."),
    };

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private void ResetPublicFrameGate()
    {
        lastPublicFrameToken = null;
        publicFramesDrawn = 0;
    }

    private void SetState(ProductHotseatUiState state)
    {
        State = state;
        StateChanged?.Invoke(this, new ProductHotseatStateChangedEventArgs(state));
    }

    private sealed class ProductCommandEqualityComparer : IEqualityComparer<V05.GameCommandRequest>
    {
        internal static ProductCommandEqualityComparer Instance { get; } = new();

        public bool Equals(V05.GameCommandRequest? x, V05.GameCommandRequest? y) =>
            ReferenceEquals(x, y) ||
            x is not null && y is not null && ProductHotseatCommandComparer.CommandsEqual(x, y);

        public int GetHashCode(V05.GameCommandRequest command)
        {
            var hash = new HashCode();
            hash.Add(command.Player);
            hash.Add(command.Action);
            hash.Add(command.ExpectedRevision);
            hash.Add(command.Source);
            hash.Add(command.Target);
            hash.Add(command.Slot);
            hash.Add(command.ModeId, StringComparer.Ordinal);
            hash.Add(command.ChoiceId, StringComparer.Ordinal);
            hash.Add(command.UseAdvance);
            foreach (ulong value in command.MulliganCards) hash.Add(value);
            foreach (string value in command.SelectedOptionIds) hash.Add(value, StringComparer.Ordinal);
            foreach (ulong value in command.AdditionalCostCards) hash.Add(value);
            return hash.ToHashCode();
        }
    }
}
