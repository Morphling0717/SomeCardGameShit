// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;
using Scgs.GodotClient.Presentation;
using Scgs.GodotClient.UI;
using Scgs.Hotseat;

namespace Scgs.GodotClient.Match;

public sealed partial class MatchScreen : Control
{
    private static readonly PackedScene SlotScene =
        GD.Load<PackedScene>("res://scenes/cards/SnapshotSlot.tscn");

    private readonly Dictionary<string, Action> _choiceCallbacks = new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, Action> _promptCardCallbacks = new();
    private HotseatMatchController? _controller;
    private CountingSession? _countingSession;
    private PassDeviceOverlay _privacyOverlay = null!;
    private MatchInteractionDock _dock = null!;
    private ResultOverlay _resultOverlay = null!;
    private ErrorOverlay _errorOverlay = null!;
    private ulong? _pendingSourceForActionChoice;
    private bool _localPromptActive;
    private bool _renderScheduled;
    private bool _eventAcknowledgeScheduled;
    private bool _submitting;
    private bool _firstSnapshotRaised;

    public event Action? ExitRequested;

    public event Action? RestartRequested;

    public event Action<MatchView>? FirstSnapshotPresented;

    public bool HasPresentedSnapshot { get; private set; }

    public bool IsPrivacyCoverVisible => _privacyOverlay.IsCovering;

    public int SnapshotRequestCount => _countingSession?.GetViewCallCount ?? 0;

    public int OpponentHandBackCount =>
        GetNodeOrNull<Container>("%OpponentHandBacks")?.GetChildCount() ?? 0;

    public override void _Ready()
    {
        _privacyOverlay = GetNode<PassDeviceOverlay>("%PassDeviceOverlay");
        _dock = GetNode<MatchInteractionDock>("%InteractionDock");
        _resultOverlay = GetNode<ResultOverlay>("%ResultOverlay");
        _errorOverlay = GetNode<ErrorOverlay>("%ErrorOverlay");

        _privacyOverlay.RevealRequested += OnRevealRequested;
        _privacyOverlay.ExitRequested += RequestExit;
        GetNode<Button>("%ReturnButton").Pressed += RequestExit;

        _dock.Mulligan.ConfirmRequested += OnMulliganConfirmRequested;
        _dock.Mulligan.ReviewAcknowledged += OnMulliganReviewAcknowledged;
        _dock.Actions.ActionRequested += OnActionRequested;
        _dock.Actions.CardRequested += OnPromptCardRequested;
        _dock.Actions.ChoiceRequested += OnPromptChoiceRequested;
        _dock.Actions.CancelRequested += OnCancelRequested;
        _dock.Confirmation.ConfirmRequested += OnConfirmationAccepted;
        _dock.Confirmation.CancelRequested += OnCancelRequested;
        _dock.Reaction.TrapRequested += OnReactionTrapRequested;
        _dock.Reaction.PassRequested += OnReactionPassRequested;

        _resultOverlay.RestartRequested += () => RestartRequested?.Invoke();
        _resultOverlay.MenuRequested += RequestExit;
        _errorOverlay.RetryRequested += () => RestartRequested?.Invoke();
        _errorOverlay.MenuRequested += RequestExit;

        GetNode<Button>("%OwnLeaderButton").Pressed += () => OnLeaderRequested(own: true);
        GetNode<Button>("%OpponentLeaderButton").Pressed += () => OnLeaderRequested(own: false);
        GetNode<Button>("%OwnStandbyButton").Pressed += () => OpenStandby(own: true);
        GetNode<Button>("%OpponentStandbyButton").Pressed += () => OpenStandby(own: false);
    }

    public override void _ExitTree()
    {
        DisposeController();
    }

    public void Begin(IScgsGameSession session, PlayerId initialViewer)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (initialViewer != PlayerId.Player0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialViewer),
                initialViewer,
                "The hot-seat controller begins with player 0's mulligan privacy gate.");
        }

        DisposeController();
        _countingSession = new CountingSession(session);
        _controller = new HotseatMatchController(_countingSession);
        _controller.StateChanged += OnControllerStateChanged;
        _firstSnapshotRaised = false;
        RenderState(_controller.State);
        if (SnapshotRequestCount != 0)
        {
            throw new InvalidOperationException("A viewer snapshot was requested before the privacy reveal.");
        }
    }

    public void RevealForCiSmoke()
    {
        if (!_privacyOverlay.IsCovering)
        {
            throw new InvalidOperationException("CI smoke must begin from the opaque privacy cover.");
        }

        _privacyOverlay.RequestRevealForSmoke();
    }

    public bool RenderedLabelsMatch(MatchView view)
    {
        if (view.Players.Length != 2)
        {
            return false;
        }

        PlayerView own = view.Players[(int)view.Viewer];
        PlayerView opponent = view.Players[(int)Other(view.Viewer)];
        return GetNode<Label>("%ViewerLabel").Text == $"观看者：{PlayerLabel(view.Viewer)}" &&
               GetNode<Label>("%PhaseLabel").Text == $"阶段：{PhaseLabel(view.Phase)}" &&
               GetNode<Label>("%RevisionLabel").Text == $"Revision {view.Revision}" &&
               GetNode<Label>("%MatchMetaLabel").Text ==
                   $"先手：{PlayerLabel(view.FirstPlayer)}  ·  当前行动：{PlayerLabel(view.ActivePlayer)}  ·  Seed：{view.RandomSeed}" &&
               GetNode<Label>("%OpponentSummary").Text == FormatPlayerSummary(opponent, "对手") &&
               GetNode<Label>("%OpponentZones").Text == FormatZoneSummary(opponent) &&
               GetNode<Label>("%OwnSummary").Text == FormatPlayerSummary(own, "己方") &&
               GetNode<Label>("%OwnZones").Text == FormatZoneSummary(own) &&
               GetNode<Label>("%PrivacyProof").Text ==
                   $"隐私校验：对手手牌仅显示数量 {opponent.HandCount}；安全快照中的对手 hand 数组为 {opponent.Hand.Length}。";
    }

    private void OnControllerStateChanged(object? sender, HotseatStateChangedEventArgs eventArgs)
    {
        if (eventArgs.State.Mode == HotseatUiMode.Covered)
        {
            RenderCoveredState(eventArgs.State);
        }

        ScheduleRender();
    }

    private void ScheduleRender()
    {
        if (_renderScheduled || !IsInsideTree())
        {
            return;
        }

        _renderScheduled = true;
        Callable.From(RenderLatestState).CallDeferred();
    }

    private void RenderLatestState()
    {
        _renderScheduled = false;
        if (_controller is null || !IsInsideTree())
        {
            return;
        }

        try
        {
            RenderState(_controller.State);
        }
        catch (Exception exception)
        {
            HandleFatal(exception);
        }
    }

    private void RenderState(HotseatUiState state)
    {
        _choiceCallbacks.Clear();
        _promptCardCallbacks.Clear();
        _pendingSourceForActionChoice = null;
        _localPromptActive = false;

        if (state.Mode == HotseatUiMode.Covered)
        {
            RenderCoveredState(state);
            return;
        }

        if (state.Mode == HotseatUiMode.Faulted)
        {
            ShowFault(state.FailureText ?? "客户端无法继续读取对局。", canRetry: true);
            return;
        }

        if (state.Mode == HotseatUiMode.Disposed)
        {
            ClearSensitiveVisuals();
            return;
        }

        MatchView view = state.Snapshot ??
            throw new InvalidOperationException("A visible hot-seat state is missing its viewer snapshot.");
        _privacyOverlay.CompleteReveal();
        _resultOverlay.Dismiss();
        _errorOverlay.Dismiss();

        if (state.Mode == HotseatUiMode.Finished)
        {
            ClearSensitiveVisuals();
            _privacyOverlay.CompleteReveal();
            _resultOverlay.Present(view.Result, view.Viewer);
            HasPresentedSnapshot = true;
            return;
        }

        RenderSnapshot(view, state);
        _dock.EventLog.Replace(view.Viewer, state.Events);
        ScheduleEventAcknowledge(state);

        switch (state.Mode)
        {
            case HotseatUiMode.MulliganSelecting:
                _dock.ShowMulligan();
                _dock.Mulligan.PresentSelection(
                    state.MulliganCards.Count,
                    view.Players[(int)view.Viewer].Hand.Length,
                    state.SelectedAction is not null);
                break;
            case HotseatUiMode.MulliganReview:
                _dock.ShowMulligan();
                _dock.Mulligan.PresentReview(view.Players[(int)view.Viewer].Hand);
                break;
            case HotseatUiMode.Action:
                RenderActionState(state);
                break;
            case HotseatUiMode.Reaction:
                RenderReactionState(state);
                break;
            default:
                throw new InvalidOperationException($"Unsupported visible hot-seat mode {state.Mode}.");
        }

        HasPresentedSnapshot = true;
        if (!_firstSnapshotRaised)
        {
            _firstSnapshotRaised = true;
            FirstSnapshotPresented?.Invoke(view);
        }
    }

    private void RenderCoveredState(HotseatUiState state)
    {
        HasPresentedSnapshot = false;
        ClearSensitiveVisuals();
        _resultOverlay.Dismiss();
        _errorOverlay.Dismiss();

        if (state.AwaitingPlayer is { } awaitingPlayer)
        {
            _privacyOverlay.Cover(PlayerLabel(awaitingPlayer));
            return;
        }

        _privacyOverlay.Cover("当前玩家（正在结算，请勿交接）");
        _privacyOverlay.GetNode<Button>("%RevealButton").Disabled = true;
    }

    private void RenderActionState(HotseatUiState state)
    {
        if (state.SelectedAction is not null)
        {
            PresentConfirmation(state);
            return;
        }

        PresentProgressiveSelection(state, "选择一个行动，或直接点击场上的牌。", includeSurrender: true);
    }

    private void RenderReactionState(HotseatUiState state)
    {
        if (state.SelectedAction is not null || state.Selection.Action.HasValue)
        {
            if (state.SelectedAction is not null)
            {
                PresentConfirmation(state);
            }
            else
            {
                PresentProgressiveSelection(state, "继续选择伏策响应所需的信息。", includeSurrender: true);
            }
            return;
        }

        MatchView view = state.Snapshot!;
        _dock.ShowReaction();
        string? sourceName = FindCard(view, view.Reaction.Origin?.Source)?.Name;
        _dock.Reaction.Present(view.Reaction, view.Viewer, sourceName);

        if (state.LegalActions.Any(action => action.Command.Action == ActionKind.Surrender))
        {
            _dock.Actions.Present(
                "也可以直接认输：",
                [ActionKind.Surrender],
                canCancel: false);
        }
    }

    private void PresentProgressiveSelection(
        HotseatUiState state,
        string initialPrompt,
        bool includeSurrender)
    {
        _dock.ShowActions();
        HotseatActionSelection selection = state.Selection;
        HotseatCandidateOptions options = state.CandidateOptions;
        string failurePrefix = string.IsNullOrWhiteSpace(state.FailureText)
            ? string.Empty
            : $"上次提交未成功：{state.FailureText}\n";

        if (!selection.Action.HasValue)
        {
            IEnumerable<ActionKind> actions = options.ActionKinds;
            if (!includeSurrender)
            {
                actions = actions.Where(action => action != ActionKind.Surrender);
            }
            _dock.Actions.Present(failurePrefix + initialPrompt, actions, canCancel: false);
            return;
        }

        if (!selection.Source.HasValue && options.Sources.Count > 1)
        {
            PresentChoices(
                failurePrefix + "选择行动来源：",
                options.Sources,
                source => FormatSourceChoice(state.Snapshot!, source),
                source => _controller!.BeginActionSelection(selection.Action.Value, source));
            return;
        }

        if (!selection.HasTarget && options.Targets.Count > 1)
        {
            PresentChoices(
                failurePrefix + "选择目标：",
                options.Targets,
                target => FormatTargetChoice(state.Snapshot!, target),
                target => _controller!.SelectTarget(target));
            return;
        }

        if (!selection.HasDonor && options.Donors.Count > 1)
        {
            PresentChoices(
                failurePrefix + "选择部署组件：",
                options.Donors,
                donor => donor.HasValue
                    ? $"使用 {FormatSourceChoice(state.Snapshot!, donor.Value)}"
                    : "不使用组件",
                donor => _controller!.SelectDonor(donor));
            return;
        }

        if (!selection.HasSlot && options.Slots.Count > 1)
        {
            PresentChoices(
                failurePrefix + "选择放置位置：",
                options.Slots,
                slot => slot.HasValue ? $"第 {slot.Value + 1} 格" : "无需位置",
                slot => _controller!.SelectSlot(slot));
            return;
        }

        if (!selection.HasAdvanceChoice && options.AdvanceChoices.Count > 1)
        {
            PresentChoices(
                failurePrefix + "选择支付方式：",
                options.AdvanceChoices,
                useAdvance => useAdvance ? "使用预支" : "正常支付",
                useAdvance => _controller!.SelectAdvance(useAdvance));
            return;
        }

        throw new ScgsProtocolException("Legal actions remain ambiguous without a displayable choice.");
    }

    private void PresentConfirmation(HotseatUiState state)
    {
        LegalAction selected = state.SelectedAction ??
            throw new InvalidOperationException("Confirmation requires one exact legal action.");
        PaymentPreview? payment = _controller!.PreviewSelectedPayment();
        if (payment is null)
        {
            return;
        }

        MatchView view = state.Snapshot!;
        string? sourceName = selected.Command.Source == 0
            ? null
            : FindCard(view, selected.Command.Source)?.Name;
        string? targetDescription = selected.Command.Target is null
            ? null
            : FormatTargetChoice(view, selected.Command.Target);
        string? warning = selected.Command.Action switch
        {
            ActionKind.EndTurn => "结束回合后可能需要把设备交给对手。",
            ActionKind.Surrender => "投降会立即结束本局比赛，无法撤销。",
            ActionKind.PassReaction => "不过会让当前响应层继续结算。",
            _ => null,
        };

        _dock.ShowConfirmation();
        _dock.Confirmation.Present(
            selected.Command,
            payment,
            sourceName,
            targetDescription,
            warning);
    }

    private void PresentChoices<T>(
        string prompt,
        IReadOnlyList<T> values,
        Func<T, string> format,
        Action<T> select)
    {
        _choiceCallbacks.Clear();
        var choices = new List<(string Label, string Key)>(values.Count);
        for (int index = 0; index < values.Count; index++)
        {
            T value = values[index];
            string key = $"choice:{index}";
            choices.Add((format(value), key));
            _choiceCallbacks.Add(key, () => select(value));
        }

        _dock.Actions.PresentChoices(prompt, choices, canCancel: true);
    }

    private void OnRevealRequested()
    {
        try
        {
            _controller?.Reveal();
        }
        catch (Exception exception)
        {
            HandleFatal(exception);
        }
    }

    private void OnMulliganConfirmRequested()
    {
        PrepareAndSubmitSelection();
    }

    private void OnMulliganReviewAcknowledged()
    {
        RunUiAction(() => _controller!.CompleteMulliganReview());
    }

    private void OnActionRequested(ActionKind action)
    {
        RunUiAction(() =>
        {
            ulong? source = _pendingSourceForActionChoice;
            _pendingSourceForActionChoice = null;
            _localPromptActive = false;
            _controller!.BeginActionSelection(action, source);
        });
    }

    private void OnPromptCardRequested(ulong instanceId)
    {
        if (_promptCardCallbacks.TryGetValue(instanceId, out Action? callback))
        {
            RunUiAction(callback);
        }
    }

    private void OnPromptChoiceRequested(string key)
    {
        if (_choiceCallbacks.TryGetValue(key, out Action? callback))
        {
            RunUiAction(callback);
        }
    }

    private void OnCancelRequested()
    {
        if (_localPromptActive)
        {
            _localPromptActive = false;
            _pendingSourceForActionChoice = null;
            ScheduleRender();
            return;
        }

        RunUiAction(() => _controller!.CancelSelection());
    }

    private void OnConfirmationAccepted()
    {
        PrepareAndSubmitSelection();
    }

    private void OnReactionTrapRequested(ulong instanceId)
    {
        RunUiAction(() => _controller!.BeginActionSelection(ActionKind.ActivateTrap, instanceId));
    }

    private void OnReactionPassRequested()
    {
        RunUiAction(() => _controller!.BeginActionSelection(ActionKind.PassReaction, source: 0));
    }

    private void PrepareAndSubmitSelection()
    {
        if (_submitting || _controller is null)
        {
            return;
        }

        try
        {
            if (!_controller.ConfirmSelection())
            {
                return;
            }

            RenderCoveredState(_controller.State);
            SubmitPreparedAfterCover();
        }
        catch (Exception exception)
        {
            HandleFatal(exception);
        }
    }

    private async void SubmitPreparedAfterCover()
    {
        if (_submitting)
        {
            return;
        }

        _submitting = true;
        try
        {
            // ProcessFrame advances in both rendered and headless runs. Waiting
            // twice lets the opaque cover enter the tree and remain observable
            // for a complete frame before the native command may mutate state.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_controller is null || !IsInsideTree())
            {
                return;
            }

            _controller.SubmitPreparedCommand();
        }
        catch (Exception exception)
        {
            HandleFatal(exception);
        }
        finally
        {
            _submitting = false;
        }
    }

    private void ScheduleEventAcknowledge(HotseatUiState state)
    {
        if (_eventAcknowledgeScheduled || !state.HasUnacknowledgedEvents)
        {
            return;
        }

        _eventAcknowledgeScheduled = true;
        Callable.From(AcknowledgeRenderedEvents).CallDeferred();
    }

    private void AcknowledgeRenderedEvents()
    {
        _eventAcknowledgeScheduled = false;
        if (_controller is null || _controller.State.IsCovered ||
            !_controller.State.HasUnacknowledgedEvents)
        {
            return;
        }

        try
        {
            _controller.AcknowledgeEvents();
        }
        catch (Exception exception)
        {
            HandleFatal(exception);
        }
    }

    private void RenderSnapshot(MatchView view, HotseatUiState state)
    {
        if (view.Players.Length != 2)
        {
            throw new InvalidOperationException("A match snapshot must contain exactly two players.");
        }

        PlayerView own = view.Players[(int)view.Viewer];
        PlayerView opponent = view.Players[(int)Other(view.Viewer)];

        GetNode<Label>("%ViewerLabel").Text = $"观看者：{PlayerLabel(view.Viewer)}";
        GetNode<Label>("%PhaseLabel").Text = $"阶段：{PhaseLabel(view.Phase)}";
        GetNode<Label>("%RevisionLabel").Text = $"Revision {view.Revision}";
        GetNode<Label>("%MatchMetaLabel").Text =
            $"先手：{PlayerLabel(view.FirstPlayer)}  ·  当前行动：{PlayerLabel(view.ActivePlayer)}  ·  Seed：{view.RandomSeed}";

        GetNode<Label>("%OpponentSummary").Text = FormatPlayerSummary(opponent, "对手");
        GetNode<Label>("%OpponentZones").Text = FormatZoneSummary(opponent);
        GetNode<Label>("%OwnSummary").Text = FormatPlayerSummary(own, "己方");
        GetNode<Label>("%OwnZones").Text = FormatZoneSummary(own);

        PopulateSlots(GetNode<Container>("%OpponentTactics"), opponent.Tactics, "策略", opponent.Player, Zone.Tactic, state);
        PopulateSlots(GetNode<Container>("%OpponentUnits"), opponent.Units, "单位", opponent.Player, Zone.Unit, state);
        PopulateSlots(GetNode<Container>("%OwnUnits"), own.Units, "单位", own.Player, Zone.Unit, state);
        PopulateSlots(GetNode<Container>("%OwnTactics"), own.Tactics, "策略", own.Player, Zone.Tactic, state);
        PopulateOpponentHandBacks(GetNode<Container>("%OpponentHandBacks"), opponent.HandCount);
        PopulateHand(GetNode<Container>("%HandCards"), own.Hand, state);
        ConfigureLeaderAndStandbyButtons(view, state);

        GetNode<Label>("%PrivacyProof").Text =
            $"隐私校验：对手手牌仅显示数量 {opponent.HandCount}；安全快照中的对手 hand 数组为 {opponent.Hand.Length}。";
    }

    private void PopulateSlots(
        Container container,
        IReadOnlyList<CardView?> cards,
        string zoneName,
        PlayerId player,
        Zone zone,
        HotseatUiState state)
    {
        FreeChildren(container);
        for (int index = 0; index < cards.Count; index++)
        {
            SnapshotSlot slot = SlotScene.Instantiate<SnapshotSlot>();
            container.AddChild(slot);
            CardView? card = cards[index];
            if (card is not null)
            {
                slot.ShowCard(card, zoneName, index);
            }
            else
            {
                slot.ShowEmpty(zoneName, index);
            }

            bool actionable = IsBoardSlotActionable(state, player, zone, index, card);
            bool inspectable = card is not null && slot.HasKnownIdentity;
            slot.SetSelectable(actionable || inspectable, actionable ? "点击选择" : "点击查看详情");
            if (actionable || inspectable)
            {
                int capturedIndex = index;
                slot.Activated += clicked =>
                    OnBoardSlotRequested(clicked, player, zone, capturedIndex, card);
            }

            slot.SetSelected(IsBoardSlotSelected(state, player, zone, index, card));
        }
    }

    private void PopulateHand(
        Container container,
        IReadOnlyList<CardView> cards,
        HotseatUiState state)
    {
        FreeChildren(container);
        for (int index = 0; index < cards.Count; index++)
        {
            CardView card = cards[index];
            SnapshotSlot slot = SlotScene.Instantiate<SnapshotSlot>();
            slot.CustomMinimumSize = new Vector2(190, 76);
            container.AddChild(slot);
            slot.ShowCard(card, "手牌", index, selectable: true);
            slot.Activated += clicked => OnHandCardRequested(clicked, card);
            slot.SetSelected(
                card.InstanceId.HasValue &&
                (state.MulliganCards.Contains(card.InstanceId.Value) ||
                 state.Selection.Source == card.InstanceId.Value));
        }

        if (cards.Count == 0)
        {
            container.AddChild(new Label { Text = "当前观看者没有可显示的手牌。" });
        }
    }

    private static void PopulateOpponentHandBacks(Container container, ulong handCount)
    {
        FreeChildren(container);
        for (ulong index = 0; index < handCount; index++)
        {
            container.AddChild(new ColorRect
            {
                Color = new Color(0.12f, 0.31f, 0.39f, 1.0f),
                CustomMinimumSize = new Vector2(24, 32),
                MouseFilter = MouseFilterEnum.Ignore,
            });
        }
    }

    private void ConfigureLeaderAndStandbyButtons(MatchView view, HotseatUiState state)
    {
        PlayerId viewer = view.Viewer;
        ConfigureLeaderButton(GetNode<Button>("%OwnLeaderButton"), state, viewer);
        ConfigureLeaderButton(GetNode<Button>("%OpponentLeaderButton"), state, Other(viewer));

        PlayerView own = view.Players[(int)viewer];
        PlayerView opponent = view.Players[(int)Other(viewer)];
        ConfigureStandbyButton(GetNode<Button>("%OwnStandbyButton"), own.Standby, "己方");
        ConfigureStandbyButton(GetNode<Button>("%OpponentStandbyButton"), opponent.Standby, "对方");
    }

    private static void ConfigureLeaderButton(
        Button button,
        HotseatUiState state,
        PlayerId player)
    {
        bool selectable = state.Selection.Action.HasValue &&
                          !state.Selection.HasTarget &&
                          state.CandidateOptions.Targets.Any(target =>
                              target is { Kind: TargetKind.Leader } && target.Player == player);
        button.Disabled = !selectable;
        button.FocusMode = selectable ? FocusModeEnum.All : FocusModeEnum.None;
        button.TooltipText = selectable ? "点击选择主战者作为目标" : string.Empty;
    }

    private static void ConfigureStandbyButton(
        Button button,
        IReadOnlyList<CardView> standby,
        string relation)
    {
        button.Text = $"查看{relation}战备（{standby.Count}）";
        button.Disabled = standby.Count == 0;
        button.FocusMode = standby.Count == 0 ? FocusModeEnum.None : FocusModeEnum.All;
        button.TooltipText = standby.Count == 0 ? string.Empty : "查看公开的战备区卡牌";
    }

    private void OnHandCardRequested(SnapshotSlot slot, CardView card)
    {
        _dock.CardDetails.ShowCard(card, "手牌详情");
        ulong? id = card.InstanceId;
        if (!id.HasValue || _controller is null)
        {
            slot.SetSelected(false);
            return;
        }

        HotseatUiState state = _controller.State;
        if (state.Mode == HotseatUiMode.MulliganSelecting)
        {
            RunUiAction(() => _controller.ToggleMulliganCard(id.Value));
            return;
        }

        if (state.Mode is HotseatUiMode.Action or HotseatUiMode.Reaction)
        {
            TrySelectSource(id.Value);
        }
        else
        {
            slot.SetSelected(false);
        }
    }

    private void OnBoardSlotRequested(
        SnapshotSlot slot,
        PlayerId player,
        Zone zone,
        int index,
        CardView? card)
    {
        if (card is not null && slot.HasKnownIdentity)
        {
            _dock.CardDetails.ShowCard(card, $"{(player == _controller?.State.Viewer ? "己方" : "对方")}{slot.ZoneName}详情");
        }

        if (_controller is null || _controller.State.Mode is not HotseatUiMode.Action and not HotseatUiMode.Reaction)
        {
            slot.SetSelected(false);
            return;
        }

        HotseatUiState state = _controller.State;
        ulong? cardId = card?.InstanceId;
        if (state.Selection.Action.HasValue && !state.Selection.HasTarget && cardId.HasValue)
        {
            Target target = Target.UnitTarget(player, cardId.Value);
            if (state.CandidateOptions.Targets.Any(option => Equals(option, target)))
            {
                RunUiAction(() => _controller.SelectTarget(target));
                return;
            }
        }

        if (state.Selection.Action.HasValue && !state.Selection.HasDonor && cardId.HasValue &&
            state.CandidateOptions.Donors.Contains(cardId.Value))
        {
            RunUiAction(() => _controller.SelectDonor(cardId.Value));
            return;
        }

        if (state.Selection.Action.HasValue && !state.Selection.HasSlot &&
            IsSlotZoneForAction(state.Selection.Action.Value, zone) &&
            state.CandidateOptions.Slots.Contains((ulong)index))
        {
            RunUiAction(() => _controller.SelectSlot((ulong)index));
            return;
        }

        if (cardId.HasValue)
        {
            TrySelectSource(cardId.Value);
        }
        else
        {
            slot.SetSelected(false);
        }
    }

    private void OnLeaderRequested(bool own)
    {
        if (_controller?.State is not { Snapshot: { } view } state ||
            state.Mode is not HotseatUiMode.Action and not HotseatUiMode.Reaction ||
            !state.Selection.Action.HasValue || state.Selection.HasTarget)
        {
            return;
        }

        Target target = Target.Leader(own ? view.Viewer : Other(view.Viewer));
        if (state.CandidateOptions.Targets.Any(option => Equals(option, target)))
        {
            RunUiAction(() => _controller.SelectTarget(target));
        }
    }

    private void OpenStandby(bool own)
    {
        if (_controller?.State is not { Snapshot: { } view } state || state.IsCovered)
        {
            return;
        }

        PlayerId player = own ? view.Viewer : Other(view.Viewer);
        IReadOnlyList<CardView> cards = view.Players[(int)player].Standby;
        _promptCardCallbacks.Clear();
        foreach (CardView card in cards)
        {
            if (!card.InstanceId.HasValue)
            {
                continue;
            }

            ulong id = card.InstanceId.Value;
            _promptCardCallbacks[id] = () =>
            {
                _dock.CardDetails.ShowCard(card, $"{(own ? "己方" : "对方")}战备详情");
                if (own && state.Mode is HotseatUiMode.Action or HotseatUiMode.Reaction)
                {
                    TrySelectSource(id);
                }
            };
        }

        _localPromptActive = true;
        _dock.ShowActions();
        _dock.Actions.PresentCards(
            $"{(own ? "己方" : "对方")}战备区（公开）：",
            cards,
            "战备",
            canCancel: true,
            cancelLabel: "返回行动选择");
    }

    private void TrySelectSource(ulong source)
    {
        if (_controller?.State is not { } state ||
            state.Mode is not HotseatUiMode.Action and not HotseatUiMode.Reaction)
        {
            return;
        }

        if (state.Selection.Action.HasValue)
        {
            if (state.CandidateOptions.Sources.Contains(source))
            {
                RunUiAction(() => _controller.BeginActionSelection(state.Selection.Action.Value, source));
            }
            return;
        }

        ActionKind[] actions = state.LegalActions
            .Where(action => action.Command.Source == source)
            .Select(action => action.Command.Action)
            .Distinct()
            .OrderBy(action => (uint)action)
            .ToArray();
        if (actions.Length == 0)
        {
            return;
        }

        if (actions.Length == 1)
        {
            RunUiAction(() => _controller.BeginActionSelection(actions[0], source));
            return;
        }

        _pendingSourceForActionChoice = source;
        _localPromptActive = true;
        _dock.ShowActions();
        _dock.Actions.Present(
            "这张牌可以执行多个行动，请选择：",
            actions,
            canCancel: true,
            cancelLabel: "返回");
    }

    private static bool IsBoardSlotActionable(
        HotseatUiState state,
        PlayerId player,
        Zone zone,
        int index,
        CardView? card)
    {
        if (state.Mode is not HotseatUiMode.Action and not HotseatUiMode.Reaction)
        {
            return false;
        }

        ulong? id = card?.InstanceId;
        if (state.Selection.Action.HasValue && !state.Selection.HasTarget && id.HasValue &&
            state.CandidateOptions.Targets.Any(target =>
                target is { Kind: TargetKind.Unit, Unit: { } unit } &&
                target.Player == player && unit == id.Value))
        {
            return true;
        }

        if (state.Selection.Action.HasValue && !state.Selection.HasDonor && id.HasValue &&
            state.CandidateOptions.Donors.Contains(id.Value))
        {
            return true;
        }

        if (state.Selection.Action.HasValue && !state.Selection.HasSlot &&
            IsSlotZoneForAction(state.Selection.Action.Value, zone) &&
            state.CandidateOptions.Slots.Contains((ulong)index))
        {
            return true;
        }

        return id.HasValue && state.LegalActions.Any(action => action.Command.Source == id.Value);
    }

    private static bool IsBoardSlotSelected(
        HotseatUiState state,
        PlayerId player,
        Zone zone,
        int index,
        CardView? card)
    {
        ulong? id = card?.InstanceId;
        if (id.HasValue &&
            (state.Selection.Source == id.Value || state.Selection.Donor == id.Value))
        {
            return true;
        }

        if (id.HasValue && state.Selection.Target is
            { Kind: TargetKind.Unit, Player: var targetPlayer, Unit: { } targetUnit } &&
            targetPlayer == player && targetUnit == id.Value)
        {
            return true;
        }

        return state.Selection.HasSlot && state.Selection.Slot == (ulong)index &&
               state.Selection.Action.HasValue &&
               IsSlotZoneForAction(state.Selection.Action.Value, zone);
    }

    private static bool IsSlotZoneForAction(ActionKind action, Zone zone) => action switch
    {
        ActionKind.PlayUnit or ActionKind.Deploy => zone == Zone.Unit,
        ActionKind.PlayTactic => zone == Zone.Tactic,
        _ => false,
    };

    private static string FormatSourceChoice(MatchView view, ulong source)
    {
        if (source == 0)
        {
            return "无需卡牌来源";
        }

        CardView? card = FindCard(view, source);
        return card is null ? "未知公开来源" : CardPresentation.FormatCompact(card);
    }

    private static string FormatTargetChoice(MatchView view, Target? target)
    {
        if (target is null)
        {
            return "无需目标";
        }

        string relation = target.Player == view.Viewer ? "己方" : "对方";
        if (target.Kind == TargetKind.Leader)
        {
            return $"{relation}主战者";
        }

        CardView? card = target.Unit.HasValue ? FindCard(view, target.Unit.Value) : null;
        return card is null ? $"{relation}单位" : $"{relation}单位「{card.Name}」";
    }

    private static CardView? FindCard(MatchView view, ulong? instanceId)
    {
        if (!instanceId.HasValue)
        {
            return null;
        }

        foreach (PlayerView player in view.Players)
        {
            foreach (CardView card in EnumerateKnownCards(player))
            {
                if (card.InstanceId == instanceId.Value)
                {
                    return card;
                }
            }
        }

        return null;
    }

    private static IEnumerable<CardView> EnumerateKnownCards(PlayerView player)
    {
        foreach (CardView card in player.Hand)
        {
            yield return card;
        }
        foreach (CardView card in player.Units.OfType<CardView>())
        {
            yield return card;
        }
        foreach (CardView card in player.Tactics.OfType<CardView>())
        {
            yield return card;
        }
        foreach (CardView card in player.Standby)
        {
            yield return card;
        }
        foreach (CardView card in player.Graveyard)
        {
            yield return card;
        }
        foreach (CardView card in player.Archive)
        {
            yield return card;
        }
    }

    private static string FormatPlayerSummary(PlayerView player, string relation) =>
        $"{relation} · {PlayerLabel(player.Player)}    " +
        $"生命 {player.LeaderHealth}/{player.MaximumLeaderHealth}    " +
        $"当前 PP {player.CurrentPp} / 容量 {player.PpCapacity}    " +
        $"裂痕 {player.Cracks}    进化能量 {player.EvolutionEnergy}";

    private static string FormatZoneSummary(PlayerView player) =>
        $"手牌 {player.HandCount} · 牌组 {player.DeckCount} · " +
        $"{FormatPublicZone("战备", player.Standby)} · " +
        $"{FormatPublicZone("墓地", player.Graveyard)} · " +
        FormatPublicZone("封存", player.Archive);

    private static string FormatPublicZone(string label, IReadOnlyList<CardView> cards) =>
        cards.Count == 0
            ? $"{label} 0"
            : $"{label} {cards.Count} [{string.Join("、", cards.Select(card => card.Name))}]";

    private void ClearSensitiveVisuals()
    {
        _choiceCallbacks.Clear();
        _promptCardCallbacks.Clear();
        _pendingSourceForActionChoice = null;
        _localPromptActive = false;

        foreach (string path in new[]
                 {
                     "%OpponentHandBacks", "%OpponentTactics", "%OpponentUnits",
                     "%OwnUnits", "%OwnTactics", "%HandCards",
                 })
        {
            FreeChildren(GetNode<Container>(path));
        }

        foreach (string path in new[]
                 {
                     "%OpponentSummary", "%OpponentZones", "%OwnSummary", "%OwnZones", "%PrivacyProof",
                 })
        {
            GetNode<Label>(path).Text = string.Empty;
        }

        GetNode<Label>("%ViewerLabel").Text = "观看者：—";
        GetNode<Label>("%PhaseLabel").Text = "阶段：—";
        GetNode<Label>("%RevisionLabel").Text = "Revision —";
        GetNode<Label>("%MatchMetaLabel").Text = "先手：—  ·  当前行动：—  ·  Seed：—";
        foreach (string path in new[]
                 {
                     "%OwnLeaderButton", "%OpponentLeaderButton", "%OwnStandbyButton", "%OpponentStandbyButton",
                 })
        {
            Button button = GetNode<Button>(path);
            button.Disabled = true;
            button.FocusMode = FocusModeEnum.None;
            button.TooltipText = string.Empty;
        }

        _dock.ClearSensitive();
    }

    private void ShowFault(string safeMessage, bool canRetry)
    {
        HasPresentedSnapshot = false;
        ClearSensitiveVisuals();
        _resultOverlay.Dismiss();
        _privacyOverlay.CompleteReveal();
        _errorOverlay.Present(safeMessage, canRetry);
    }

    private void HandleFatal(Exception exception)
    {
        GD.PushError($"Gate 3B match flow failed: {exception}");
        ShowFault(
            "客户端无法安全地继续这局比赛。已清除观看者数据；可重新开始或返回主菜单。",
            canRetry: true);
    }

    private void RunUiAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            HandleFatal(exception);
        }
    }

    private void RequestExit()
    {
        ClearSensitiveVisuals();
        ExitRequested?.Invoke();
    }

    private void DisposeController()
    {
        if (_controller is not null)
        {
            _controller.StateChanged -= OnControllerStateChanged;
            _controller.Dispose();
            _controller = null;
        }

        _countingSession = null;
    }

    private static void FreeChildren(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            if (child is SnapshotSlot slot)
            {
                slot.ClearSensitive();
            }
            child.Free();
        }
    }

    private static PlayerId Other(PlayerId player) =>
        player == PlayerId.Player0 ? PlayerId.Player1 : PlayerId.Player0;

    private static string PlayerLabel(PlayerId player) =>
        player == PlayerId.Player0 ? "玩家 0" : "玩家 1";

    private static string PhaseLabel(MatchPhase phase) => phase switch
    {
        MatchPhase.NotStarted => "未开始",
        MatchPhase.Mulligan => "调度",
        MatchPhase.Action => "行动",
        MatchPhase.Reaction => "响应",
        MatchPhase.Finished => "已结束",
        _ => $"未知（{(uint)phase}）",
    };

    private sealed class CountingSession : IScgsGameSession
    {
        private readonly IScgsGameSession _inner;
        private bool _disposed;

        internal CountingSession(IScgsGameSession inner) =>
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        internal int GetViewCallCount { get; private set; }

        public EngineStatus Start() => _inner.Start();

        public MatchView GetView(PlayerId viewer)
        {
            GetViewCallCount++;
            return _inner.GetView(viewer);
        }

        public LegalActionsResult ListLegalActions(ActionQueryRequest query) =>
            _inner.ListLegalActions(query);

        public ValidTargetsResult ListValidTargets(ActionQueryRequest query) =>
            _inner.ListValidTargets(query);

        public ValidSlotsResult ListValidSlots(ActionQueryRequest query) =>
            _inner.ListValidSlots(query);

        public ValidDonorsResult ListValidDonors(ActionQueryRequest query) =>
            _inner.ListValidDonors(query);

        public PaymentResult PreviewPayment(GameCommandRequest command) =>
            _inner.PreviewPayment(command);

        public ReactionContext GetReactionContext(PlayerId viewer) =>
            _inner.GetReactionContext(viewer);

        public EngineStatus SubmitCommand(GameCommandRequest command) =>
            _inner.SubmitCommand(command);

        public EventBatch ReadEvents(PlayerId viewer, ulong afterSequence) =>
            _inner.ReadEvents(viewer, afterSequence);

        public EventBatch ReadNewEvents(PlayerId viewer) =>
            _inner.ReadNewEvents(viewer);

        public ulong GetEventCursor(PlayerId viewer) =>
            _inner.GetEventCursor(viewer);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _inner.Dispose();
        }
    }
}
