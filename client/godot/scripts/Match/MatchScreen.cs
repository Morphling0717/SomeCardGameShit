// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;
using Scgs.GodotClient.Battlefield;
using Scgs.GodotClient.Presentation;
using Scgs.GodotClient.UI;
using Scgs.Hotseat;

namespace Scgs.GodotClient.Match;

public sealed partial class MatchScreen : Control
{
    private const string PrivacySentinel = "SCGS_CI_PRIVATE_SENTINEL_9D2D7B15";

    private static readonly PackedScene SlotScene =
        GD.Load<PackedScene>("res://scenes/cards/SnapshotSlot.tscn");

    private readonly Dictionary<string, Action> _directCallbacks = new(StringComparer.Ordinal);
    private readonly Dictionary<Container, List<SnapshotSlot>> _slotPools = new();
    private readonly Dictionary<SnapshotSlot, SlotBinding> _slotBindings = new();
    private HotseatMatchController? _controller;
    private HotseatSurfaceInteractionCoordinator? _surfaceCoordinator;
    private Battlefield3DPresenter _battlefield3d = null!;
    private CountingSession? _countingSession;
    private PassDeviceOverlay _privacyOverlay = null!;
    private MatchInteractionDock _dock = null!;
    private CardDetailPanel _battlefieldDetails = null!;
    private Control _battlefieldControlRail = null!;
    private DirectActionPanel _directActions = null!;
    private ReactionPanel _reactionOverlay = null!;
    private DirectDropButton _ownLeader = null!;
    private DirectDropButton _opponentLeader = null!;
    private DirectDropButton _castZone = null!;
    private TargetingLine _targetingLine = null!;
    private Control _resolvingShield = null!;
    private Control _standbyTray = null!;
    private ResultOverlay _resultOverlay = null!;
    private ErrorOverlay _errorOverlay = null!;
    private bool _detailsPinned;
    private ulong? _pinnedCardId;
    private SnapshotSlot? _dragSourceSlot;
    private ulong? _dragStartRevision;
    private PlayerId? _lastVisibleViewer;
    private bool _renderScheduled;
    private bool _eventAcknowledgeScheduled;
    private bool _submitting;
    private bool _firstSnapshotRaised;
    private ulong _directChoiceGeneration;
    private ActionKind? _preparedActionKind;
    private readonly HashSet<ActionKind> _successfulActionKinds = [];
    private int _coverPresentationCount;
    private int _revealRequestCount;
    private int _passingDeviceCoverCount;
    private int _eventAcknowledgeCount;
    private int _submissionCount;
    private int _successfulSubmissionCount;
    private int _disposedSessionCount;
    private bool _sawReaction;
    private EngineStatus? _lastSubmissionStatus;
    private int _currentResolvingFrames;
    private int _minimumResolvingFrames = int.MaxValue;
    private int _resolvingPrivateLeakCount;
    private bool _ciSuppressAutoPrepare;
    private bool _ciClickDragParityVerified;
    private bool _ciSelectionCommitObserved;
    private bool _ciSelectionConfirmationViolation;
    private bool _ciSawSourceDestinationSignal;
    private bool _ciSawFixedSignal;
    private bool _ciSawReactionSignal;
    private bool _ciCancelledDragNoSideEffects;
    private bool _ciSourceAdjacentPanelVerified;
    private bool _ciPrivacySentinelArmed;
    private bool _ciPrivacySentinelVerified;
    private bool _ciPrivacySentinelFrameAuditPending;
    private string? _ciResolvingScreenshotPath;
    private bool _ciResolvingScreenshotCaptured;
    private bool _ciSurfaceIntentE2e;
    private bool _legacy2dBoard;
    private PlayerId? _renderedPerspectiveViewer;
    private int _ciPerspectiveRebuilds;
    private int _ciActorPoolReuses;
    private int _ciHudRaycastBlocks;
    private int _ciBlockedSpatialInputs;
    private int _ciSpatialPrivateLeaks;
    private bool _hasRendered3d;
    private bool _ciRaycastE2e;
    private bool _ciPhysicalDragSubmitted;
    private bool _ciKeyboardE2e;
    private bool _ciExternalStandbyDrag;

    private CardDetailPanel ActiveCardDetails =>
        _legacy2dBoard ? _dock.CardDetails : _battlefieldDetails;

    public event Action? ExitRequested;

    public event Action? RestartRequested;

    public event Action<MatchView>? FirstSnapshotPresented;

    public bool HasPresentedSnapshot { get; private set; }

    public bool IsPrivacyCoverVisible => _privacyOverlay.IsCovering;

    public int SnapshotRequestCount => _countingSession?.GetViewCallCount ?? 0;

    public int OpponentHandBackCount =>
        GetNodeOrNull<Container>("%OpponentHandBacks")?.GetChildren()
            .OfType<CanvasItem>()
            .Count(item => item.Visible) ?? 0;

    internal HotseatUiState CiState => _controller?.State ??
        throw new InvalidOperationException("The CI match controller is unavailable.");

    internal bool CiConfirmationVisible => _dock.Confirmation.Visible;

    internal bool CiMulliganVisible => _dock.Mulligan.Visible;

    internal bool CiResultVisible => _resultOverlay.Visible;

    internal int CiCoverPresentationCount => _coverPresentationCount;

    internal int CiRevealRequestCount => _revealRequestCount;

    internal int CiPassingDeviceCoverCount => _passingDeviceCoverCount;

    internal int CiEventAcknowledgeCount => _eventAcknowledgeCount;

    internal int CiSubmissionCount => _submissionCount;

    internal int CiSuccessfulSubmissionCount => _successfulSubmissionCount;

    internal int CiDisposedSessionCount => _disposedSessionCount;

    internal int CiMinimumResolvingFrames =>
        _minimumResolvingFrames == int.MaxValue ? 0 : _minimumResolvingFrames;

    internal int CiResolvingPrivateLeakCount => _resolvingPrivateLeakCount;

    internal bool CiClickDragCanonicalParity => _ciClickDragParityVerified;

    internal bool CiSelectionCommitWithoutConfirmation =>
        _ciSelectionCommitObserved && !_ciSelectionConfirmationViolation;

    internal bool CiSignalE2e =>
        _ciSawSourceDestinationSignal && _ciSawFixedSignal && _ciSawReactionSignal;

    internal bool CiCancelledDragNoSideEffects => _ciCancelledDragNoSideEffects;

    internal bool CiSourceAdjacentPanelVerified => _ciSourceAdjacentPanelVerified;

    internal bool CiPrivacySentinelVerified => _ciPrivacySentinelVerified;

    internal bool CiResolvingScreenshotCaptured => _ciResolvingScreenshotCaptured;

    internal bool CiSurfaceIntentE2e => _ciSurfaceIntentE2e;

    internal string CiPresentationMode => _legacy2dBoard ? "legacy-2d" : "3d";

    internal bool CiRaycastE2e => !_legacy2dBoard && _ciRaycastE2e;

    internal bool CiKeyboardE2e => _legacy2dBoard || _ciKeyboardE2e;

    internal bool CiExternalStandbyDrag => _legacy2dBoard || _ciExternalStandbyDrag;

    internal bool CiPhysicalDragSubmitted => _legacy2dBoard || _ciPhysicalDragSubmitted;

    internal bool CiHasTransientDragData =>
        _dragSourceSlot is not null || _dragStartRevision.HasValue;

    internal int CiHudRaycastBlocks => _legacy2dBoard ? 0 : _ciHudRaycastBlocks;

    internal int CiDragThresholdPixels => _legacy2dBoard
        ? 0
        : (int)BattlefieldRaycastInput.DragThresholdPixels;

    internal int CiCameraFovDegrees => _legacy2dBoard
        ? 0
        : (int)BattlefieldPerspective.CameraFovDegrees;

    internal int CiCameraPitchDegrees => _legacy2dBoard
        ? 0
        : (int)BattlefieldPerspective.CameraPitchDegrees;

    internal int CiPerspectiveRebuilds => _legacy2dBoard ? 0 : _ciPerspectiveRebuilds;

    internal int CiActorPoolReuses => _legacy2dBoard ? 0 : _ciActorPoolReuses;

    internal int CiBlockedSpatialInputs => _legacy2dBoard ? 0 : _ciBlockedSpatialInputs;

    internal int CiSpatialPrivateLeaks => _ciSpatialPrivateLeaks;

    internal int CiPrematureViewerCallCount => _countingSession?.PrematureViewerCallCount ?? 0;

    internal bool CiSawReaction => _sawReaction;

    internal EngineStatus? CiLastSubmissionStatus => _lastSubmissionStatus;

    internal IReadOnlyCollection<ActionKind> CiSuccessfulActionKinds => _successfulActionKinds;

    public override void _Ready()
    {
        _legacy2dBoard = OS.GetCmdlineUserArgs().Contains(
            "--legacy-2d-board",
            StringComparer.Ordinal);
        _battlefield3d = GetNode<Battlefield3DPresenter>("%Battlefield3D");
        _privacyOverlay = GetNode<PassDeviceOverlay>("%PassDeviceOverlay");
        _dock = GetNode<MatchInteractionDock>("%InteractionDock");
        _battlefieldDetails = GetNode<CardDetailPanel>("%BattlefieldCardDetails");
        _battlefieldControlRail = GetNode<Control>("%BattlefieldControlRail");
        _directActions = GetNode<DirectActionPanel>("%DirectActionPanel");
        _reactionOverlay = GetNode<ReactionPanel>("%ReactionOverlay");
        _ownLeader = GetNode<DirectDropButton>("%OwnLeaderButton");
        _opponentLeader = GetNode<DirectDropButton>("%OpponentLeaderButton");
        _castZone = GetNode<DirectDropButton>("%CastZone");
        _targetingLine = GetNode<TargetingLine>("%TargetingLine");
        _resolvingShield = GetNode<Control>("%ResolvingShield");
        _standbyTray = GetNode<Control>("%StandbyTray");
        _resultOverlay = GetNode<ResultOverlay>("%ResultOverlay");
        _errorOverlay = GetNode<ErrorOverlay>("%ErrorOverlay");

        _battlefield3d.Visible = !_legacy2dBoard;
        _battlefieldDetails.Visible = !_legacy2dBoard;
        _dock.CardDetails.Visible = _legacy2dBoard;
        _battlefield3d.SurfaceGestureRequested += OnBattlefieldSurfaceGestureRequested;
        _battlefield3d.SurfaceHovered += OnBattlefieldSurfaceHovered;
        _battlefield3d.SurfaceSecondaryRequested += OnBattlefieldSurfaceSecondaryRequested;
        _battlefield3d.ProjectionChanged += OnBattlefieldProjectionChanged;
        _battlefield3d.SetGuiBlocker(IsGuiBlockingBattlefield);
        _battlefield3d.SetViewportObstructions(
            _legacy2dBoard ? null : _battlefieldDetails,
            _legacy2dBoard ? null : _battlefieldControlRail);
        ConfigurePresentationLayout();

        _privacyOverlay.RevealRequested += OnRevealRequested;
        _privacyOverlay.ExitRequested += RequestExit;
        GetNode<Button>("%ReturnButton").Pressed += RequestExit;

        _dock.Mulligan.ConfirmRequested += OnMulliganConfirmRequested;
        _dock.Mulligan.ReviewAcknowledged += OnMulliganReviewAcknowledged;
        _dock.Confirmation.ConfirmRequested += OnConfirmationAccepted;
        _dock.Confirmation.CancelRequested += OnCancelRequested;
        _dock.Reaction.TrapRequested += OnReactionTrapRequested;
        _dock.Reaction.PassRequested += OnReactionPassRequested;
        _dock.CollapsedChanged += OnDockCollapsedChanged;
        _directActions.ChoiceRequested += OnDirectChoiceRequested;
        _directActions.BackRequested += OnStepBackRequested;
        _reactionOverlay.TrapRequested += OnReactionTrapRequested;
        _reactionOverlay.PassRequested += OnReactionPassRequested;

        _resultOverlay.RestartRequested += () =>
            Callable.From(() => RestartRequested?.Invoke()).CallDeferred();
        _resultOverlay.MenuRequested += RequestExit;
        _errorOverlay.RetryRequested += () => RestartRequested?.Invoke();
        _errorOverlay.MenuRequested += RequestExit;

        _ownLeader.Pressed += () => OnLeaderRequested(own: true);
        _opponentLeader.Pressed += () => OnLeaderRequested(own: false);
        _ownLeader.DropReceived += () => OnLeaderDropped(own: true);
        _opponentLeader.DropReceived += () => OnLeaderDropped(own: false);
        _castZone.DropReceived += OnCastZoneDropped;
        GetNode<Button>("%OwnStandbyButton").Pressed += () => OpenStandby(own: true);
        GetNode<Button>("%OpponentStandbyButton").Pressed += () => OpenStandby(own: false);
        GetNode<Button>("%CloseStandbyButton").Pressed += CloseStandby;
        GetNode<Button>("%EndTurnButton").Pressed += OnEndTurnRequested;
        GetNode<Button>("%SurrenderButton").Pressed += OnSurrenderRequested;
    }

    private void ConfigurePresentationLayout()
    {
        ColorRect background = GetNode<ColorRect>("Background");
        background.Color = _legacy2dBoard
            ? new Color(0.028f, 0.045f, 0.075f, 1.0f)
            : new Color(0.028f, 0.045f, 0.075f, 0.16f);
        SetLegacyBoardPanelsVisible(_legacy2dBoard, showMulliganHand: false);
    }

    private void OnBattlefieldProjectionChanged(object? sender, EventArgs eventArgs)
    {
        if (_legacy2dBoard || !_directActions.Visible || !IsInsideTree())
        {
            return;
        }

        Callable.From(RepositionDirectPanelAfterProjectionChange).CallDeferred();
    }

    private void RepositionDirectPanelAfterProjectionChange()
    {
        if (!_legacy2dBoard && _directActions.Visible && _controller?.State is { } state &&
            state.Mode is HotseatUiMode.Action or HotseatUiMode.Reaction)
        {
            PositionDirectPanel(state);
        }
    }

    private void SetLegacyBoardPanelsVisible(bool showLegacyBoard, bool showMulliganHand)
    {
        GetNode<Control>("SafeMargin/Layout/OpponentPanel").Visible = showLegacyBoard;
        GetNode<Control>("SafeMargin/Layout/Board").Visible = showLegacyBoard;
        GetNode<Control>("SafeMargin/Layout/OwnPanel").Visible = showLegacyBoard;
        GetNode<Control>("SafeMargin/Layout/HandPanel").Visible =
            showLegacyBoard || showMulliganHand;
    }

    private void OnBattlefieldSurfaceGestureRequested(
        object? sender,
        BattlefieldSurfaceGestureEventArgs eventArgs)
    {
        if (_legacy2dBoard || _controller?.State is not { } state ||
            state.Mode is not HotseatUiMode.Action and not HotseatUiMode.Reaction)
        {
            _ciBlockedSpatialInputs++;
            return;
        }

        if (eventArgs.Source.Kind == BattlefieldSurfaceKind.StandbyPile)
        {
            if (eventArgs.Gesture == BattlefieldSurfaceGesture.Click &&
                eventArgs.Destination is null && eventArgs.Source.Player is { } pileOwner &&
                state.Snapshot is { } pileView && eventArgs.Revision == pileView.Revision)
            {
                OpenStandby(own: pileOwner == pileView.Viewer);
                _ciRaycastE2e = true;
            }
            else
            {
                _ciBlockedSpatialInputs++;
            }
            return;
        }

        if (eventArgs.Destination is { Kind: BattlefieldSurfaceKind.StandbyPile })
        {
            _ciBlockedSpatialInputs++;
            return;
        }

        RunUiAction(() =>
        {
            var intent = new HotseatSurfaceIntent(
                eventArgs.Revision,
                (HotseatSurfaceGesture)(uint)eventArgs.Gesture,
                eventArgs.Source.ToHotseat())
            {
                Destination = eventArgs.Destination?.ToHotseat(),
            };
            HotseatSurfaceIntentResult result = ApplySurfaceIntent(intent);
            if (result.Accepted)
            {
                _ciRaycastE2e = true;
            }
        });
    }

    private void OnBattlefieldSurfaceHovered(
        object? sender,
        BattlefieldSurfaceHoverEventArgs eventArgs)
    {
        if (_legacy2dBoard || _detailsPinned ||
            _controller?.State is not
            {
                Mode: HotseatUiMode.Action or HotseatUiMode.Reaction,
                Snapshot: { } view,
            })
        {
            return;
        }

        ulong? instanceId = eventArgs.Card?.InstanceId ?? eventArgs.Surface?.InstanceId;
        if (instanceId is not { } knownId)
        {
            ActiveCardDetails.ShowPlaceholder();
            return;
        }

        CardView? card = FindCard(view, knownId);
        if (card is not null && !CardPresentation.IsIdentityHidden(card))
        {
            ActiveCardDetails.ShowCard(card, "卡牌详情（右键固定）");
            return;
        }

        ActiveCardDetails.ShowPlaceholder();
    }

    private void OnBattlefieldSurfaceSecondaryRequested(
        object? sender,
        BattlefieldSurfaceHoverEventArgs eventArgs)
    {
        ulong? instanceId = eventArgs.Card?.InstanceId ?? eventArgs.Surface?.InstanceId;
        if (_legacy2dBoard || instanceId is not { } knownId ||
            _controller?.State.Snapshot is not { } view)
        {
            OnStepBackRequested();
            return;
        }

        CardView? card = FindCard(view, knownId);
        if (card is null || CardPresentation.IsIdentityHidden(card))
        {
            return;
        }

        if (_detailsPinned && _pinnedCardId == knownId)
        {
            _detailsPinned = false;
            _pinnedCardId = null;
            ActiveCardDetails.ShowPlaceholder();
            return;
        }

        _detailsPinned = true;
        _pinnedCardId = knownId;
        ActiveCardDetails.ShowCard(card, "已固定卡牌详情（再次右键取消）");
    }

    private bool IsGuiBlockingBattlefield(Vector2 position)
    {
        if (_legacy2dBoard || _controller?.State.Mode is HotseatUiMode.Covered or
                HotseatUiMode.Resolving or HotseatUiMode.Finished or
                HotseatUiMode.Faulted or HotseatUiMode.Disposed)
        {
            return true;
        }

        foreach (Control control in new Control[]
                 {
                     _privacyOverlay,
                     _resolvingShield,
                     _resultOverlay,
                     _errorOverlay,
                     _reactionOverlay,
                     _directActions,
                     _standbyTray,
                     _dock,
                     _battlefieldDetails,
                     GetNode<Control>("%BattlefieldControlRail"),
                     GetNode<Control>("SafeMargin/Layout/HandPanel"),
                 })
        {
            if (control.IsVisibleInTree() && control.GetGlobalRect().HasPoint(position) &&
                control.MouseFilter != MouseFilterEnum.Ignore)
            {
                _ciHudRaycastBlocks++;
                return true;
            }
        }

        for (Control? hovered = GetViewport().GuiGetHoveredControl();
             hovered is not null && hovered != this;
             hovered = hovered.GetParentOrNull<Control>())
        {
            if (hovered == GetNode<Control>("Background") ||
                hovered == GetNode<Control>("SafeMargin") ||
                hovered == GetNode<Control>("SafeMargin/Layout"))
            {
                continue;
            }
            if (hovered.IsVisibleInTree() && hovered.MouseFilter != MouseFilterEnum.Ignore)
            {
                _ciHudRaycastBlocks++;
                return true;
            }
        }

        return false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_cancel") ||
            _controller?.State.Interaction.CanStepBack != true)
        {
            return;
        }

        GetViewport().SetInputAsHandled();
        OnStepBackRequested();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventMouseButton
            {
                ButtonIndex: MouseButton.Right,
                Pressed: true,
            } || _controller?.State.Interaction.CanStepBack != true)
        {
            return;
        }

        if (!_legacy2dBoard && @event is InputEventMouseButton mouse &&
            _battlefield3d.InputEnabled &&
            _battlefield3d.TryGetSurfaceAtScreen(
                mouse.Position,
                out BattlefieldSurfaceRef _))
        {
            // Let BattlefieldRaycastInput deliver the physical card's
            // secondary-click detail event. Only true 3D blank space steps back.
            return;
        }

        for (Node? hovered = GetViewport().GuiGetHoveredControl();
             hovered is not null && hovered != this;
             hovered = hovered.GetParent())
        {
            if (hovered is BaseButton)
            {
                return;
            }
        }

        GetViewport().SetInputAsHandled();
        OnStepBackRequested();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationDragEnd)
        {
            if (!_legacy2dBoard && _dragSourceSlot is not null)
            {
                _ = TryCompleteExternalBattlefieldDrag(GetViewport().GetMousePosition());
            }
            _dragSourceSlot = null;
            _dragStartRevision = null;
            if (_controller?.State is { } state)
            {
                UpdateTargetingLine(state);
            }
            else
            {
                _targetingLine?.Stop();
            }
        }
    }

    private bool TryCompleteExternalBattlefieldDrag(Vector2 screenPosition)
    {
        SnapshotSlot? sourceSlot = _dragSourceSlot;
        ulong? revision = _dragStartRevision;
        if (sourceSlot is null || !revision.HasValue ||
            !_slotBindings.TryGetValue(sourceSlot, out SlotBinding? binding) ||
            !TryCreateSurfaceRef(binding, out HotseatSurfaceRef sourceSurface) ||
            sourceSurface.Kind != HotseatSurfaceKind.StandbyCard ||
            _controller?.State is not { Snapshot: { } view } state ||
            state.Mode is not HotseatUiMode.Action and not HotseatUiMode.Reaction ||
            view.Revision != revision.Value || !_battlefield3d.InputEnabled ||
            IsGuiBlockingBattlefield(screenPosition) ||
            !_battlefield3d.TryGetSurfaceAtScreen(
                screenPosition,
                out BattlefieldSurfaceRef battlefieldDestination) ||
            battlefieldDestination.Kind == BattlefieldSurfaceKind.StandbyPile)
        {
            return false;
        }

        HotseatSurfaceRef destination;
        try
        {
            destination = battlefieldDestination.ToHotseat();
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        _dragSourceSlot = null;
        _dragStartRevision = null;
        HotseatSurfaceIntentResult result = ApplySurfaceIntent(new HotseatSurfaceIntent(
            revision.Value,
            HotseatSurfaceGesture.Drag,
            sourceSurface)
        {
            Destination = destination,
        });
        return result.Accepted;
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
        ResetCiMetrics();
        _countingSession = new CountingSession(session);
        _controller = new HotseatMatchController(_countingSession);
        _surfaceCoordinator = new HotseatSurfaceInteractionCoordinator(_controller);
        _surfaceCoordinator.CommandPreparing += OnSurfaceCommandPreparing;
        _controller.StateChanged += OnControllerStateChanged;
        _countingSession.RequireReveal(PlayerId.Player0);
        _coverPresentationCount = 1;
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

    internal void SubmitLegalActionThroughSignalsForCi(LegalAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_controller?.State is not { } state || state.Snapshot is null ||
            state.IsCovered || _submitting)
        {
            throw new InvalidOperationException("Direct CI input requires a revealed, idle match state.");
        }
        if (state.Interaction.Step != HotseatSelectionStep.None)
        {
            _controller.CancelSelection();
            state = _controller.State;
            RenderState(state);
        }
        if (!state.LegalActions.Any(candidate => CommandsEquivalent(candidate.Command, action.Command)))
        {
            throw new ArgumentException("The CI signal action is not canonical for the current revision.", nameof(action));
        }
        MatchView snapshot = state.Snapshot ??
            throw new InvalidOperationException("Direct CI input lost its safe viewer snapshot.");

        if (action.Command.Action == ActionKind.EndTurn)
        {
            _ciSawFixedSignal = true;
            GetNode<Button>("%EndTurnButton").EmitSignal(Button.SignalName.Pressed);
            return;
        }
        if (action.Command.Action == ActionKind.PassReaction)
        {
            _ciSawReactionSignal = true;
            _reactionOverlay.RequestPassForSmoke();
            return;
        }

        if (action.Command.Action == ActionKind.ActivateTrap)
        {
            _reactionOverlay.RequestTrapForSmoke(action.Command.Source);
            _ciSawReactionSignal = true;
            RenderState(_controller!.State);
            if (_reactionOverlay.Visible)
            {
                throw new InvalidOperationException(
                    "The centered reaction layer still intercepts battlefield target input.");
            }
            if (_legacy2dBoard)
            {
                DriveCanonicalSelectionWithSignals(action.Command);
            }
            else
            {
                DriveCanonicalSelectionWith3dSignals(action.Command);
            }
            if (action.Command.Target is not null)
            {
                _ciSawSourceDestinationSignal = true;
            }
            return;
        }

        if (!_legacy2dBoard)
        {
            bool beganWithExternalStandbyDrag = !_ciExternalStandbyDrag &&
                action.Command.Action == ActionKind.Deploy &&
                TryExternalStandbyDragForCi(action.Command);
            bool beganWithDrag = !beganWithExternalStandbyDrag && !_ciPhysicalDragSubmitted &&
                                  TryDragBattlefieldCommandForCi(action.Command);
            if (!beganWithExternalStandbyDrag && !beganWithDrag &&
                !TryClickBattlefieldSourceForCi(action.Command.Source))
            {
                throw new InvalidOperationException(
                    $"No pickable 3D source exists for {action.Command.Action} source {action.Command.Source}.");
            }
            DriveCanonicalSelectionWith3dSignals(action.Command);
            if (action.Command.Target is not null || action.Command.Slot.HasValue)
            {
                _ciSawSourceDestinationSignal = true;
            }
            return;
        }

        SnapshotSlot? source = FindVisibleSlotForCard(action.Command.Source);
        if (source is null && snapshot.Players[(int)snapshot.Viewer].Standby.Any(
                card => card.InstanceId == action.Command.Source))
        {
            GetNode<Button>("%OwnStandbyButton").EmitSignal(Button.SignalName.Pressed);
            source = FindVisibleSlotForCard(action.Command.Source);
        }
        if (source is null)
        {
            throw new InvalidOperationException(
                $"No visible source slot exists for {action.Command.Action} source {action.Command.Source}.");
        }
        _slotBindings.TryGetValue(source, out SlotBinding? sourceBindingBeforePress);
        source.EmitSignal(Button.SignalName.Pressed);
        if (_controller.State.Interaction.Source != action.Command.Source)
        {
            throw new InvalidOperationException(
                "The legacy source signal selected a different card: " +
                $"expected={action.Command.Source}, actual={_controller.State.Interaction.Source}, " +
                $"binding={sourceBindingBeforePress}, disabled={source.Disabled}, " +
                $"visible={source.IsVisibleInTree()}.");
        }
        DriveCanonicalSelectionWithSignals(action.Command);
        if (action.Command.Target is not null || action.Command.Slot.HasValue)
        {
            _ciSawSourceDestinationSignal = true;
        }
    }

    private bool TryClickBattlefieldSourceForCi(ulong source)
    {
        if (_controller?.State.Snapshot is not { } view ||
            !TryFindSurfaceRef(view, source, out HotseatSurfaceRef surface))
        {
            return false;
        }

        BattlefieldSurfaceRef battlefieldSurface = BattlefieldSurfaceRef.FromHotseat(surface);
        if (!_battlefield3d.CiTryGetScreenAnchor(battlefieldSurface, out Vector2 screen))
        {
            bool ownStandbySource = surface.Kind == HotseatSurfaceKind.StandbyCard &&
                surface.Player == view.Viewer &&
                view.Players[(int)view.Viewer].Standby.Any(card => card.InstanceId == source);
            BattlefieldSurfaceRef standbyPile = BattlefieldSurfaceRef.StandbyPile(view.Viewer);
            if (!ownStandbySource ||
                !_battlefield3d.CiTryGetScreenAnchor(standbyPile, out Vector2 pileScreen) ||
                !_battlefield3d.CiRaycastInput.CiClickAt(pileScreen))
            {
                return false;
            }

            SnapshotSlot traySource = FindVisibleSlotForCard(source) ??
                throw new InvalidOperationException(
                    $"The 3D standby pile did not expose source {source} in its public tray.");
            traySource.EmitSignal(Button.SignalName.Pressed);
            _ciRaycastE2e = true;
            return _controller.State.Selection.Source == source;
        }

        return _battlefield3d.CiRaycastInput.CiClickAt(screen);
    }

    private bool TryExternalStandbyDragForCi(GameCommandRequest command)
    {
        HotseatUiState state = _controller?.State ??
            throw new InvalidOperationException("The external standby drag has no controller.");
        MatchView view = state.Snapshot ??
            throw new InvalidOperationException("The external standby drag requires a snapshot.");
        if (command.Slot is not { } slot || slot > int.MaxValue ||
            !view.Players[(int)view.Viewer].Standby.Any(card => card.InstanceId == command.Source))
        {
            return false;
        }

        BattlefieldSurfaceRef pile = BattlefieldSurfaceRef.StandbyPile(view.Viewer);
        if (!_battlefield3d.CiTryGetScreenAnchor(pile, out Vector2 pileScreen) ||
            !_battlefield3d.CiRaycastInput.CiClickAt(pileScreen))
        {
            return false;
        }

        SnapshotSlot traySource = FindVisibleSlotForCard(command.Source) ??
            throw new InvalidOperationException(
                $"The standby tray did not expose deploy source {command.Source}.");
        Variant payload = traySource.BeginDragForSmoke();
        if (payload.VariantType != Variant.Type.Dictionary)
        {
            return false;
        }

        BattlefieldSurfaceRef destination = new(
            BattlefieldSurfaceKind.UnitSlot,
            command.Player,
            (int)slot);
        if (!_battlefield3d.CiTryGetScreenAnchor(destination, out Vector2 destinationScreen) ||
            !TryCompleteExternalBattlefieldDrag(destinationScreen))
        {
            _dragSourceSlot = null;
            _dragStartRevision = null;
            return false;
        }

        _ciExternalStandbyDrag = true;
        _ciPhysicalDragSubmitted = true;
        _ciRaycastE2e = true;
        return true;
    }

    private bool TryDragBattlefieldCommandForCi(GameCommandRequest command)
    {
        HotseatUiState before = _controller?.State ??
            throw new InvalidOperationException("The 3D drag smoke has no controller.");
        MatchView view = before.Snapshot ??
            throw new InvalidOperationException("The 3D drag smoke requires a snapshot.");
        if (!TryFindSurfaceRef(view, command.Source, out HotseatSurfaceRef source) ||
            !TryCreateCommandDestinationSurface(view, command, out HotseatSurfaceRef destination) ||
            !_battlefield3d.CiTryGetScreenAnchor(
                BattlefieldSurfaceRef.FromHotseat(source),
                out Vector2 sourceScreen) ||
            !_battlefield3d.CiTryGetScreenAnchor(
                BattlefieldSurfaceRef.FromHotseat(destination),
                out Vector2 destinationScreen) ||
            sourceScreen.DistanceTo(destinationScreen) <
                BattlefieldRaycastInput.DragThresholdPixels)
        {
            return false;
        }

        if (!_battlefield3d.CiRaycastInput.CiDragAt(sourceScreen, destinationScreen))
        {
            return false;
        }

        HotseatUiState after = _controller.State;
        bool accepted = after.Mode == HotseatUiMode.Resolving ||
                        after.Selection.Source == command.Source;
        if (accepted)
        {
            _ciPhysicalDragSubmitted = true;
            _ciRaycastE2e = true;
        }
        return accepted;
    }

    private void DriveCanonicalSelectionWith3dSignals(GameCommandRequest command)
    {
        for (int step = 0; step < 10; step++)
        {
            HotseatUiState state = _controller?.State ??
                throw new InvalidOperationException("The 3D CI selection controller is unavailable.");
            if (state.Mode == HotseatUiMode.Resolving)
            {
                return;
            }
            RenderState(state);

            switch (state.Interaction.Step)
            {
                case HotseatSelectionStep.ChooseAction:
                    if (_ciSuppressAutoPrepare)
                    {
                        _controller.ChooseAction(command.Action);
                    }
                    else
                    {
                        _directActions.PressChoiceForSmoke(ActionPresentation.FormatAction(command.Action));
                    }
                    break;
                case HotseatSelectionStep.ChooseDonor:
                    if (_ciSuppressAutoPrepare)
                    {
                        _controller.SelectDonor(command.ComponentDonor);
                        break;
                    }
                    if (command.ComponentDonor is { } donor)
                    {
                        ClickBattlefieldSurfaceForCi(
                            SurfaceForCardForCi(state.Snapshot!, donor),
                            "deployment donor");
                    }
                    else
                    {
                        _directActions.PressChoiceForSmoke("不使用组件");
                    }
                    AssertExplicitSelectionAutoPrepared(HotseatSelectionStep.ChooseDonor);
                    break;
                case HotseatSelectionStep.ChooseSlot:
                    if (command.Slot is not { } slot || slot > int.MaxValue)
                    {
                        throw new InvalidOperationException("The canonical 3D placement has no slot.");
                    }
                    HotseatSurfaceRef slotSurface = command.Action switch
                    {
                        ActionKind.PlayUnit or ActionKind.Deploy =>
                            HotseatSurfaceRef.UnitSlot(command.Player, (int)slot),
                        ActionKind.PlayTactic =>
                            HotseatSurfaceRef.TacticSlot(command.Player, (int)slot),
                        _ => throw new InvalidOperationException(
                            $"{command.Action} cannot use a 3D placement slot."),
                    };
                    if (_ciSuppressAutoPrepare)
                    {
                        _controller.SelectSlot(slot);
                    }
                    else
                    {
                        ClickBattlefieldSurfaceForCi(slotSurface, "placement slot");
                    }
                    AssertExplicitSelectionAutoPrepared(HotseatSelectionStep.ChooseSlot);
                    break;
                case HotseatSelectionStep.ChooseTarget:
                    if (_ciSuppressAutoPrepare)
                    {
                        _controller.SelectTarget(command.Target);
                        break;
                    }
                    if (command.Target is null)
                    {
                        ClickBattlefieldSurfaceForCi(
                            HotseatSurfaceRef.CastZone(),
                            "no-target spell cast zone");
                    }
                    else
                    {
                        if (!TryCreateTargetSurface(
                                state.Snapshot!,
                                command.Target,
                                out HotseatSurfaceRef targetSurface))
                        {
                            throw new InvalidOperationException("The canonical 3D target is unavailable.");
                        }
                        ClickBattlefieldSurfaceForCi(targetSurface, "effect or attack target");
                    }
                    AssertExplicitSelectionAutoPrepared(HotseatSelectionStep.ChooseTarget);
                    break;
                case HotseatSelectionStep.ChooseAdvance:
                    if (_ciSuppressAutoPrepare)
                    {
                        _controller.SelectAdvance(command.UseAdvance);
                    }
                    else
                    {
                        _directActions.PressChoiceForSmoke(command.UseAdvance ? "使用预支" : "正常支付");
                    }
                    AssertExplicitSelectionAutoPrepared(HotseatSelectionStep.ChooseAdvance);
                    break;
                case HotseatSelectionStep.Ready:
                    if (_ciSuppressAutoPrepare)
                    {
                        return;
                    }
                    _directActions.PressChoiceForSmoke(ActionPresentation.FormatAction(command.Action));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"The 3D signal path stopped at unexpected step {state.Interaction.Step}.");
            }
        }

        throw new InvalidOperationException("The canonical 3D signal path exceeded its selection-step limit.");
    }

    private HotseatSurfaceRef SurfaceForCardForCi(MatchView view, ulong instanceId) =>
        TryFindSurfaceRef(view, instanceId, out HotseatSurfaceRef surface)
            ? surface
            : throw new InvalidOperationException($"Card {instanceId} has no visible 3D surface.");

    private void ClickBattlefieldSurfaceForCi(HotseatSurfaceRef surface, string role)
    {
        BattlefieldSurfaceRef battlefieldSurface = BattlefieldSurfaceRef.FromHotseat(surface);
        if (!_battlefield3d.CiTryGetScreenAnchor(battlefieldSurface, out Vector2 screen))
        {
            throw new InvalidOperationException($"The 3D {role} has no screen anchor.");
        }
        bool blocked = IsGuiBlockingBattlefield(screen);
        bool pickedOk = _battlefield3d.CiRaycastInput.CiTryPick(
            screen,
            out BattlefieldSurfaceRef picked) && picked == battlefieldSurface;
        if (blocked || !pickedOk || !_battlefield3d.CiRaycastInput.CiClickAt(screen))
        {
            Control? hovered = GetViewport().GuiGetHoveredControl();
            throw new InvalidOperationException(
                $"The 3D raycast could not activate the {role}; blocked={blocked}, " +
                $"picked={pickedOk}:{picked}, expected={battlefieldSurface}, " +
                $"screen={screen}, hovered={hovered?.Name}, " +
                $"panel={_directActions.GetGlobalRect()}.");
        }
        _ciRaycastE2e = true;
    }

    internal bool VerifyClickDragParityForCi(LegalAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Command.Source == 0 ||
            (action.Command.Target is null && !action.Command.Slot.HasValue) ||
            action.Command.ComponentDonor.HasValue)
        {
            return false;
        }

        if (!_legacy2dBoard)
        {
            if (action.Command.Target is null || !action.Command.Slot.HasValue)
            {
                return false;
            }

            HotseatUiState state = _controller?.State ??
                throw new InvalidOperationException("The 3D parity probe has no controller.");
            MatchView view = state.Snapshot ??
                throw new InvalidOperationException("The 3D parity probe requires a snapshot.");
            if (!TryFindSurfaceRef(view, action.Command.Source, out HotseatSurfaceRef source) ||
                !TryCreateCommandDestinationSurface(view, action.Command, out HotseatSurfaceRef destination))
            {
                return false;
            }

            BattlefieldSurfaceRef source3d = BattlefieldSurfaceRef.FromHotseat(source);
            BattlefieldSurfaceRef destination3d = BattlefieldSurfaceRef.FromHotseat(destination);
            if (!_battlefield3d.CiTryGetScreenAnchor(source3d, out Vector2 sourceScreen) ||
                !_battlefield3d.CiTryGetScreenAnchor(destination3d, out Vector2 destinationScreen) ||
                sourceScreen.DistanceTo(destinationScreen) < BattlefieldRaycastInput.DragThresholdPixels)
            {
                return false;
            }

            _ciSuppressAutoPrepare = true;
            try
            {
                if (!_battlefield3d.CiRaycastInput.CiClickAt(sourceScreen))
                {
                    return false;
                }
                DriveCanonicalSelectionWith3dSignals(action.Command);
                GameCommandRequest clickCommand = _controller.State.Interaction.CanonicalAction?.Command ??
                    throw new InvalidOperationException(
                        "The physical 3D click path did not converge to a canonical command.");
                _controller.CancelSelection();
                RenderState(_controller.State);

                if (!_battlefield3d.CiRaycastInput.CiDragAt(sourceScreen, destinationScreen) ||
                    _controller.State.Mode == HotseatUiMode.Resolving)
                {
                    return false;
                }
                DriveCanonicalSelectionWith3dSignals(action.Command);
                GameCommandRequest dragCommand = _controller.State.Interaction.CanonicalAction?.Command ??
                    throw new InvalidOperationException(
                        "The physical 3D drag path did not converge to a canonical command.");
                _controller.CancelSelection();
                RenderState(_controller.State);

                _ciClickDragParityVerified = CommandsEquivalent(clickCommand, dragCommand) &&
                                             CommandsEquivalent(clickCommand, action.Command);
                _ciRaycastE2e |= _ciClickDragParityVerified;
                return _ciClickDragParityVerified;
            }
            finally
            {
                _ciSuppressAutoPrepare = false;
                if (_controller?.State is
                    {
                        Mode: HotseatUiMode.Action or HotseatUiMode.Reaction,
                    } cleanup && cleanup.Interaction.Step != HotseatSelectionStep.None)
                {
                    _controller.CancelSelection();
                    RenderState(_controller.State);
                }
            }
        }

        _ciSuppressAutoPrepare = true;
        try
        {
            SnapshotSlot clickSource = FindVisibleSlotForCard(action.Command.Source) ??
                throw new InvalidOperationException("The parity click source is not visible.");
            clickSource.EmitSignal(Button.SignalName.Pressed);
            DriveCanonicalSelectionWithSignals(action.Command);
            GameCommandRequest clickCommand = _controller!.State.Interaction.CanonicalAction?.Command ??
                throw new InvalidOperationException("The click path did not converge to a canonical command.");
            _controller.CancelSelection();
            RenderState(_controller.State);

            SnapshotSlot dragSource = FindVisibleSlotForCard(action.Command.Source) ??
                throw new InvalidOperationException("The parity drag source is not visible.");
            SnapshotSlot? dragDestination = FindDestinationSlot(action.Command);
            if (dragDestination is null)
            {
                _controller.CancelSelection();
                RenderState(_controller.State);
                return false;
            }
            Variant payload = dragSource.BeginDragForSmoke();
            dragDestination.DropForSmoke(payload);
            DriveCanonicalSelectionWithSignals(action.Command);
            GameCommandRequest dragCommand = _controller.State.Interaction.CanonicalAction?.Command ??
                throw new InvalidOperationException("The drag path did not converge to a canonical command.");
            _controller.CancelSelection();
            RenderState(_controller.State);

            _ciClickDragParityVerified = CommandsEquivalent(clickCommand, dragCommand) &&
                                         CommandsEquivalent(clickCommand, action.Command);
            return _ciClickDragParityVerified;
        }
        finally
        {
            _ciSuppressAutoPrepare = false;
            if (_controller?.State is { Mode: HotseatUiMode.Action or HotseatUiMode.Reaction } cleanup &&
                cleanup.Interaction.Step != HotseatSelectionStep.None)
            {
                _controller.CancelSelection();
                RenderState(_controller.State);
            }
        }
    }

    internal async Task<bool> VerifyKeyboardNavigationForCiAsync()
    {
        if (_legacy2dBoard)
        {
            return true;
        }

        HotseatUiState before = _controller?.State ??
            throw new InvalidOperationException("The keyboard probe has no controller.");
        if (before.Mode != HotseatUiMode.Action || before.Snapshot is null ||
            before.Interaction.Step != HotseatSelectionStep.None || !_battlefield3d.InputEnabled)
        {
            return false;
        }

        int submissionsBefore = _submissionCount;
        GetViewport().GuiReleaseFocus();
        ParseKeyForCi(Key.Tab);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        BattlefieldSurfaceRef? focused = _battlefield3d.CiKeyboardFocusedSurface;
        if (focused is not { InstanceId: { } source })
        {
            return false;
        }

        ParseKeyForCi(Key.Enter);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        HotseatUiState selected = _controller.State;
        bool selectedThroughKeyboard =
            selected.Mode == before.Mode &&
            selected.Snapshot?.Revision == before.Snapshot.Revision &&
            selected.Selection.Source == source &&
            selected.Interaction.Step != HotseatSelectionStep.None &&
            selected.EventCursors == before.EventCursors &&
            _submissionCount == submissionsBefore;
        if (selected.Mode is HotseatUiMode.Action or HotseatUiMode.Reaction &&
            selected.Interaction.Step != HotseatSelectionStep.None)
        {
            _controller.CancelSelection();
            RenderState(_controller.State);
        }

        _ciKeyboardE2e |= selectedThroughKeyboard;
        return selectedThroughKeyboard;
    }

    private static void ParseKeyForCi(Key keycode)
    {
        Input.ParseInputEvent(new InputEventKey
        {
            Keycode = keycode,
            Pressed = true,
            Echo = false,
        });
        Input.ParseInputEvent(new InputEventKey
        {
            Keycode = keycode,
            Pressed = false,
            Echo = false,
        });
    }

    internal bool VerifyCancelledDragNoSideEffectsForCi(LegalAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        HotseatUiState before = _controller?.State ??
            throw new InvalidOperationException("The cancelled-drag probe has no controller.");
        MatchView view = before.Snapshot ??
            throw new InvalidOperationException("The cancelled-drag probe requires a visible snapshot.");
        if (!_legacy2dBoard)
        {
            if (!TryFindSurfaceRef(view, action.Command.Source, out HotseatSurfaceRef surface) ||
                !_battlefield3d.CiTryGetScreenAnchor(
                    BattlefieldSurfaceRef.FromHotseat(surface),
                    out Vector2 sourceScreen))
            {
                return false;
            }

            BattlefieldRaycastInput input = _battlefield3d.CiRaycastInput;
            input._Input(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = sourceScreen,
            });
            input._Input(new InputEventMouseMotion
            {
                Position = sourceScreen + new Vector2(
                    BattlefieldRaycastInput.DragThresholdPixels - 1.0f,
                    0.0f),
            });
            bool belowThreshold = !input.IsDragging;
            input._Input(new InputEventMouseMotion
            {
                Position = sourceScreen + new Vector2(
                    BattlefieldRaycastInput.DragThresholdPixels,
                    0.0f),
            });
            bool atThreshold = input.IsDragging;
            bool expectedDragVisual = surface.Kind switch
            {
                HotseatSurfaceKind.Unit => _battlefield3d.CiTargetArrowVisible,
                HotseatSurfaceKind.HandCard or HotseatSurfaceKind.StandbyCard =>
                    _battlefield3d.CiPlacementGhostVisible,
                _ => true,
            };
            input._Input(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = false,
                Position = new Vector2(-500.0f, -500.0f),
            });

            HotseatUiState after3d = _controller.State;
            bool pileDropSafe = true;
            BattlefieldSurfaceRef pile = BattlefieldSurfaceRef.StandbyPile(view.Viewer);
            if (_battlefield3d.CiTryGetScreenAnchor(pile, out Vector2 pileScreen))
            {
                HotseatUiState beforePileDrop = _controller.State;
                bool gestureDelivered = input.CiDragAt(sourceScreen, pileScreen);
                HotseatUiState afterPileDrop = _controller.State;
                pileDropSafe = gestureDelivered && ReferenceEquals(beforePileDrop, afterPileDrop) &&
                               afterPileDrop.Snapshot?.Revision == view.Revision &&
                               afterPileDrop.Selection == beforePileDrop.Selection &&
                               afterPileDrop.EventCursors == beforePileDrop.EventCursors;
            }
            _ciCancelledDragNoSideEffects = belowThreshold && atThreshold &&
                expectedDragVisual && !_battlefield3d.CiTargetArrowVisible &&
                !_battlefield3d.CiPlacementGhostVisible &&
                pileDropSafe && !input.HasActiveDrag && after3d.Mode == before.Mode &&
                after3d.Snapshot?.Revision == view.Revision &&
                Equals(after3d.Selection, before.Selection) &&
                after3d.EventCursors == before.EventCursors;
            _ciRaycastE2e |= belowThreshold && atThreshold;
            return _ciCancelledDragNoSideEffects;
        }

        SnapshotSlot source = FindVisibleSlotForCard(action.Command.Source) ??
            throw new InvalidOperationException("The cancelled-drag source is not visible.");

        Variant payload = source.BeginDragForSmoke();
        if (payload.VariantType != Variant.Type.Dictionary)
        {
            throw new InvalidOperationException("The legal source did not begin a direct drag.");
        }
        _Notification((int)NotificationDragEnd);

        HotseatUiState after = _controller.State;
        _ciCancelledDragNoSideEffects =
            after.Mode == before.Mode &&
            after.Snapshot?.Revision == view.Revision &&
            Equals(after.Selection, before.Selection) &&
            after.EventCursors == before.EventCursors &&
            _dragSourceSlot is null && !_dragStartRevision.HasValue;
        return _ciCancelledDragNoSideEffects;
    }

    private static bool TryCreateCommandDestinationSurface(
        MatchView view,
        GameCommandRequest command,
        out HotseatSurfaceRef surface)
    {
        if (TryCreateTargetSurface(view, command.Target, out surface))
        {
            return true;
        }
        if (command.Slot is { } slot && slot <= int.MaxValue)
        {
            surface = command.Action switch
            {
                ActionKind.PlayUnit or ActionKind.Deploy =>
                    HotseatSurfaceRef.UnitSlot(command.Player, (int)slot),
                ActionKind.PlayTactic =>
                    HotseatSurfaceRef.TacticSlot(command.Player, (int)slot),
                _ => default,
            };
            return surface.Player.HasValue;
        }
        if (command.Action == ActionKind.CastSpell && command.Target is null)
        {
            surface = HotseatSurfaceRef.CastZone();
            return true;
        }

        surface = default;
        return false;
    }

    internal bool VerifyDockCollapseForCi()
    {
        MarginContainer safe = GetNode<MarginContainer>("SafeMargin");
        _dock.ToggleForSmoke();
        bool collapsed = _dock.IsCollapsed && !_dock.EventLog.Visible &&
                         safe.GetThemeConstant("margin_right") == 96;
        _dock.ToggleForSmoke();
        bool detailsCorrect = _legacy2dBoard
            ? _dock.CardDetails.Visible
            : !_dock.CardDetails.Visible && _battlefieldDetails.Visible;
        return collapsed && !_dock.IsCollapsed && detailsCorrect &&
               _dock.EventLog.Visible && safe.GetThemeConstant("margin_right") == 420;
    }

    internal bool VerifyGate4PresentationForCi()
    {
        if (_legacy2dBoard)
        {
            return !_battlefield3d.Visible &&
                   GetNode<Control>("SafeMargin/Layout/Board").Visible;
        }

        HotseatUiState state = _controller?.State ??
            throw new InvalidOperationException("The Gate 4A presentation probe has no controller.");
        MatchView view = state.Snapshot ??
            throw new InvalidOperationException("The Gate 4A presentation probe requires a snapshot.");
        int reuseBefore = _ciActorPoolReuses;
        RenderBattlefieldPrivate(view, state);

        bool hudBlocked = IsGuiBlockingBattlefield(_dock.GetGlobalRect().GetCenter());
        bool minimumApplied = _battlefield3d.CiSetCameraZoom(-100.0f) &&
                              Mathf.IsEqualApprox(
                                  _battlefield3d.CiCameraZoom,
                                  BattlefieldPerspective.MinimumZoom);
        bool maximumApplied = _battlefield3d.CiSetCameraZoom(100.0f) &&
                              Mathf.IsEqualApprox(
                                  _battlefield3d.CiCameraZoom,
                                  BattlefieldPerspective.MaximumZoom);
        _battlefield3d.CiSetCameraZoom(1.0f);

        bool perspective = _battlefield3d.CiPerspectiveViewer == view.Viewer &&
                           BattlefieldPerspective.IsNear(view.Viewer, view.Viewer) &&
                           !BattlefieldPerspective.IsNear(Other(view.Viewer), view.Viewer) &&
                           BattlefieldPerspective.VisualSlotIndex(
                               Other(view.Viewer),
                               view.Viewer,
                               0,
                               BattlefieldPerspective.UnitSlotCount) ==
                           BattlefieldPerspective.UnitSlotCount - 1;
        bool actorPool = _battlefield3d.CiActiveCardCount > 0 &&
                         _battlefield3d.CiActiveSlotCount > 0 &&
                         _ciActorPoolReuses > reuseBefore;
        bool tripleAffordance = _battlefield3d.CiSawTripleAffordance &&
                                _battlefield3d.CiTripleAffordanceSurfaceCount > 0 &&
                                _battlefield3d.CiOutlineVisibleCount > 0;
        bool readableLayout = _battlefield3d.CiValidateReadableLayout(view);
        return _battlefield3d.Visible && _battlefield3d.InputEnabled && hudBlocked &&
               minimumApplied && maximumApplied && perspective && actorPool &&
               tripleAffordance && readableLayout &&
               Mathf.IsEqualApprox(
                   _battlefield3d.CiCameraPitch,
                   BattlefieldPerspective.CameraPitchDegrees);
    }

    internal bool VerifySpatialInputLockedForCi()
    {
        if (_legacy2dBoard)
        {
            return true;
        }

        HotseatUiState before = _controller?.State ??
            throw new InvalidOperationException("The spatial-lock probe has no controller.");
        if (before.Mode != HotseatUiMode.Resolving || before.PublicBoard is null ||
            _battlefield3d.InputEnabled)
        {
            return false;
        }

        int submissions = _submissionCount;
        Vector2 center = GetViewportRect().Size / 2.0f;
        BattlefieldRaycastInput input = _battlefield3d.CiRaycastInput;
        input._Input(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = center,
        });
        input._Input(new InputEventMouseMotion
        {
            Position = center + new Vector2(32.0f, 0.0f),
        });
        input._Input(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = false,
            Position = center + new Vector2(32.0f, 0.0f),
        });

        HotseatUiState after = _controller.State;
        bool locked = ReferenceEquals(before, after) &&
                      after.Mode == HotseatUiMode.Resolving &&
                      after.PublicBoard?.Revision == before.PublicBoard.Revision &&
                      after.EventCursors == before.EventCursors &&
                      _submissionCount == submissions && !input.HasActiveDrag &&
                      !_battlefield3d.InputEnabled;
        if (locked)
        {
            _ciBlockedSpatialInputs++;
        }
        return locked;
    }

    internal bool VerifyReactionChoiceModalBlocksSpatialInputForCi()
    {
        if (_legacy2dBoard)
        {
            return true;
        }

        HotseatUiState before = _controller?.State ??
            throw new InvalidOperationException("The reaction modal probe has no controller.");
        MatchView view = before.Snapshot ??
            throw new InvalidOperationException("The reaction modal probe requires a snapshot.");
        if (before.Mode != HotseatUiMode.Reaction ||
            before.Interaction.Step != HotseatSelectionStep.None ||
            _battlefield3d.InputEnabled)
        {
            return false;
        }

        LegalAction? trap = before.LegalActions.FirstOrDefault(action =>
            action.Command.Action == ActionKind.ActivateTrap && action.Command.Source != 0);
        if (trap is null ||
            !TryFindSurfaceRef(view, trap.Command.Source, out HotseatSurfaceRef trapSurface) ||
            !_battlefield3d.CiTryGetScreenAnchor(
                BattlefieldSurfaceRef.FromHotseat(trapSurface),
                out Vector2 trapScreen))
        {
            return false;
        }

        bool trapRejected = !_battlefield3d.CiRaycastInput.CiClickAt(trapScreen);
        BattlefieldSurfaceRef pile = BattlefieldSurfaceRef.StandbyPile(view.Viewer);
        bool pileRejected = !_battlefield3d.CiTryGetScreenAnchor(pile, out Vector2 pileScreen) ||
                            !_battlefield3d.CiRaycastInput.CiClickAt(pileScreen);
        HotseatUiState after = _controller.State;
        bool locked = trapRejected && pileRejected && ReferenceEquals(before, after) &&
                      after.EventCursors == before.EventCursors &&
                      after.Selection == before.Selection;
        if (locked)
        {
            _ciBlockedSpatialInputs += 2;
        }
        return locked;
    }

    internal static bool ValidateReferenceLayoutForCi(int width, int height)
    {
        if (width < 1 || height < 1)
        {
            return false;
        }
        const float leftDetails = 278.0f;
        const float rightControls = 400.0f;
        const float safeGutters = 48.0f;
        float availableBoardWidth = width - leftDetails - rightControls - safeGutters;
        float availableBoardHeight = height - 120.0f;
        float directPanelWidth = Math.Min(420.0f, availableBoardWidth - 24.0f);
        return availableBoardWidth >= 520.0f &&
               availableBoardHeight >= 600.0f && directPanelWidth >= 360.0f;
    }

    internal bool ValidateRenderedLayoutForCi()
    {
        if (_legacy2dBoard)
        {
            return GetNode<Control>("SafeMargin/Layout/Board").IsVisibleInTree();
        }

        Vector2 viewport = GetViewportRect().Size;
        Rect2 left = _battlefieldDetails.GetGlobalRect();
        Rect2 right = _battlefieldControlRail.GetGlobalRect();
        if (!_battlefieldDetails.IsVisibleInTree() || !_battlefieldControlRail.IsVisibleInTree() ||
            left.Position.X < 0.0f || left.End.X >= right.Position.X || right.End.X > viewport.X + 1.0f)
        {
            return false;
        }

        float safeLeft = left.End.X + 8.0f;
        float safeRight = right.Position.X - 8.0f;
        if (safeRight - safeLeft < 500.0f)
        {
            return false;
        }

        var surfaces = new List<BattlefieldSurfaceRef>();
        foreach (PlayerId player in Enum.GetValues<PlayerId>())
        {
            surfaces.Add(new BattlefieldSurfaceRef(BattlefieldSurfaceKind.Leader, player));
            surfaces.Add(BattlefieldSurfaceRef.StandbyPile(player));
            for (int slot = 0; slot < BattlefieldPerspective.UnitSlotCount; slot++)
            {
                surfaces.Add(new BattlefieldSurfaceRef(BattlefieldSurfaceKind.UnitSlot, player, slot));
            }
            for (int slot = 0; slot < BattlefieldPerspective.TacticSlotCount; slot++)
            {
                surfaces.Add(new BattlefieldSurfaceRef(BattlefieldSurfaceKind.TacticSlot, player, slot));
            }
        }
        surfaces.Add(new BattlefieldSurfaceRef(BattlefieldSurfaceKind.CastZone));

        int visibleAnchors = 0;
        foreach (BattlefieldSurfaceRef surface in surfaces)
        {
            if (!_battlefield3d.CiTryGetScreenAnchor(surface, out Vector2 anchor))
            {
                continue;
            }
            visibleAnchors++;
            if (anchor.X < safeLeft || anchor.X > safeRight ||
                anchor.Y < 36.0f || anchor.Y > viewport.Y - 36.0f)
            {
                return false;
            }
        }

        return visibleAnchors >= 19;
    }

    internal void ConfirmCurrentSelectionForCi()
    {
        if (!HasPresentedSnapshot || IsPrivacyCoverVisible)
        {
            throw new InvalidOperationException(
                "CI may confirm only after the viewer snapshot is visibly presented.");
        }

        PrepareAndSubmitSelection();
    }

    internal void CompleteMulliganReviewForCi()
    {
        if (!HasPresentedSnapshot || IsPrivacyCoverVisible ||
            CiState.Mode != HotseatUiMode.MulliganReview)
        {
            throw new InvalidOperationException(
                "CI may complete mulligan review only while its visible review panel is active.");
        }

        OnMulliganReviewAcknowledged();
    }

    internal void ConfirmMulliganThroughSignalForCi() =>
        _dock.Mulligan.RequestConfirmForSmoke();

    internal void CompleteMulliganReviewThroughSignalForCi() =>
        _dock.Mulligan.RequestReviewAcknowledgeForSmoke();

    internal void SubmitSurrenderThroughSignalsForCi()
    {
        if (_controller?.State.Mode != HotseatUiMode.Action)
        {
            throw new InvalidOperationException("Surrender smoke requires a visible action phase.");
        }
        GetNode<Button>("%SurrenderButton").EmitSignal(Button.SignalName.Pressed);
        RenderState(_controller.State);
        if (!_dock.Confirmation.Visible)
        {
            throw new InvalidOperationException("Surrender did not present its required confirmation.");
        }
        _dock.Confirmation.RequestConfirmForSmoke();
    }

    internal void RestartThroughResultSignalForCi() =>
        _resultOverlay.RequestRestartForSmoke();

    internal void ArmResolvingPrivacySentinelForCi(string? screenshotPath = null)
    {
        if (_controller?.State is not { Snapshot: { } view } state || state.IsCovered ||
            view.Players[(int)view.Viewer].Hand.Length == 0)
        {
            throw new InvalidOperationException(
                "The privacy sentinel requires a revealed viewer with a private hand card.");
        }
        if (screenshotPath is not null && !Path.IsPathFullyQualified(screenshotPath))
        {
            throw new ArgumentException(
                "The resolving privacy screenshot path must be absolute.",
                nameof(screenshotPath));
        }
        _ciPrivacySentinelArmed = true;
        _ciResolvingScreenshotPath = screenshotPath;
    }

    private void DriveCanonicalSelectionWithSignals(GameCommandRequest command)
    {
        for (int step = 0; step < 10; step++)
        {
            HotseatUiState state = _controller?.State ??
                throw new InvalidOperationException("The CI direct-selection controller is unavailable.");
            if (state.Mode == HotseatUiMode.Resolving)
            {
                return;
            }
            RenderState(state);

            switch (state.Interaction.Step)
            {
                case HotseatSelectionStep.ChooseAction:
                    _directActions.PressChoiceForSmoke(ActionPresentation.FormatAction(command.Action));
                    break;
                case HotseatSelectionStep.ChooseDonor:
                    if (command.ComponentDonor is { } donor)
                    {
                        (FindVisibleSlotForCard(donor) ??
                            throw new InvalidOperationException("The canonical donor is not visible."))
                            .EmitSignal(Button.SignalName.Pressed);
                    }
                    else
                    {
                        _directActions.PressChoiceForSmoke("不使用组件");
                    }
                    AssertExplicitSelectionAutoPrepared(HotseatSelectionStep.ChooseDonor);
                    break;
                case HotseatSelectionStep.ChooseSlot:
                    (FindPlacementSlot(command) ??
                        throw new InvalidOperationException("The canonical destination slot is not visible."))
                        .EmitSignal(Button.SignalName.Pressed);
                    AssertExplicitSelectionAutoPrepared(HotseatSelectionStep.ChooseSlot);
                    break;
                case HotseatSelectionStep.ChooseTarget:
                    if (command.Target is null)
                    {
                        if (!state.Interaction.Targets.Any(target => target is null))
                        {
                            throw new InvalidOperationException(
                                "The canonical no-target action was filtered from the current candidates: " +
                                $"action={command.Action}, source={command.Source}, " +
                                $"selection={state.Selection}, candidates={string.Join(';', state.CandidateOptions.Actions.Select(candidate => candidate.Command))}.");
                        }
                        _directActions.PressChoiceForSmoke("不指定目标 / 直接发动");
                    }
                    else if (command.Target is { Kind: TargetKind.Leader } leader)
                    {
                        DirectDropButton button = leader.Player == state.Snapshot!.Viewer
                            ? _ownLeader
                            : _opponentLeader;
                        button.EmitSignal(Button.SignalName.Pressed);
                    }
                    else
                    {
                        (FindTargetSlot(command) ??
                            throw new InvalidOperationException(
                                "The canonical unit target is not visible: " +
                                $"target={command.Target}, viewer={state.Snapshot?.Viewer}, " +
                                $"bindings={string.Join(';', _slotBindings.Values.Select(binding =>
                                    $"{binding.Player}/{binding.Zone}/{binding.Index}/" +
                                    $"{binding.Card?.InstanceId?.ToString() ?? "empty"}"))}."))
                            .EmitSignal(Button.SignalName.Pressed);
                    }
                    AssertExplicitSelectionAutoPrepared(HotseatSelectionStep.ChooseTarget);
                    break;
                case HotseatSelectionStep.ChooseAdvance:
                    _directActions.PressChoiceForSmoke(command.UseAdvance ? "使用预支" : "正常支付");
                    AssertExplicitSelectionAutoPrepared(HotseatSelectionStep.ChooseAdvance);
                    break;
                case HotseatSelectionStep.Ready:
                    if (_ciSuppressAutoPrepare)
                    {
                        return;
                    }
                    _directActions.PressChoiceForSmoke(ActionPresentation.FormatAction(command.Action));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"The direct signal path stopped at unexpected step {state.Interaction.Step}.");
            }
        }

        throw new InvalidOperationException("The canonical signal path exceeded its selection-step limit.");
    }

    private void AssertExplicitSelectionAutoPrepared(HotseatSelectionStep completedStep)
    {
        if (_ciSuppressAutoPrepare)
        {
            return;
        }

        HotseatUiState state = _controller?.State ??
            throw new InvalidOperationException("The CI direct-selection controller is unavailable.");
        if (state.Mode == HotseatUiMode.Resolving)
        {
            _ciSelectionCommitObserved = true;
            _ciSelectionConfirmationViolation |= _dock.Confirmation.Visible;
            return;
        }

        if (state.Interaction.Step == HotseatSelectionStep.Ready)
        {
            _ciSelectionConfirmationViolation = true;
            throw new InvalidOperationException(
                $"Completing {completedStep} stopped at Ready instead of preparing immediately.");
        }
    }

    private SnapshotSlot? FindVisibleSlotForCard(ulong instanceId) =>
        _slotBindings.FirstOrDefault(pair =>
            pair.Key.IsVisibleInTree() && pair.Value.Card?.InstanceId == instanceId).Key;

    private SnapshotSlot? FindDestinationSlot(GameCommandRequest command)
    {
        return command.Target is { Kind: TargetKind.Unit }
            ? FindTargetSlot(command)
            : command.Slot.HasValue
                ? FindPlacementSlot(command)
                : null;
    }

    private SnapshotSlot? FindTargetSlot(GameCommandRequest command)
    {
        if (command.Target is { Kind: TargetKind.Unit, Unit: { } unit })
        {
            return _slotBindings.FirstOrDefault(pair =>
                pair.Key.IsVisibleInTree() && pair.Value.Player == command.Target.Player &&
                pair.Value.Card?.InstanceId == unit).Key;
        }
        return null;
    }

    private SnapshotSlot? FindPlacementSlot(GameCommandRequest command)
    {
        if (command.Slot is { } index)
        {
            Zone zone = command.Action is ActionKind.PlayUnit or ActionKind.Deploy
                ? Zone.Unit
                : Zone.Tactic;
            return _slotBindings.FirstOrDefault(pair =>
                pair.Key.IsVisibleInTree() && pair.Value.Surface == SlotSurface.Board &&
                pair.Value.Player == command.Player && pair.Value.Zone == zone &&
                pair.Value.Index == (int)index).Key;
        }
        return null;
    }

    private static bool CommandsEquivalent(GameCommandRequest left, GameCommandRequest right) =>
        left.Player == right.Player &&
        left.Action == right.Action &&
        left.ExpectedRevision == right.ExpectedRevision &&
        left.Source == right.Source &&
        Equals(left.Target, right.Target) &&
        left.Slot == right.Slot &&
        left.ComponentDonor == right.ComponentDonor &&
        left.UseAdvance == right.UseAdvance &&
        left.MulliganCards.SequenceEqual(right.MulliganCards);

    internal void DisposeForCiSmoke() => DisposeController();

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
        if (_dragStartRevision.HasValue &&
            (eventArgs.State.Interaction.Revision != _dragStartRevision.Value ||
             eventArgs.State.Mode is HotseatUiMode.Covered or HotseatUiMode.Resolving or
                 HotseatUiMode.Finished or HotseatUiMode.Faulted or HotseatUiMode.Disposed))
        {
            _dragSourceSlot = null;
            _dragStartRevision = null;
        }

        if (eventArgs.State.Mode == HotseatUiMode.Covered)
        {
            _coverPresentationCount++;
            if (eventArgs.State.CoverReason == HotseatCoverReason.PassingDevice)
            {
                _passingDeviceCoverCount++;
            }

            if (eventArgs.State.AwaitingPlayer is { } awaitingPlayer)
            {
                _countingSession?.RequireReveal(awaitingPlayer);
            }

            RenderCoveredState(eventArgs.State);
        }
        else if (eventArgs.State.Mode == HotseatUiMode.Resolving)
        {
            _currentResolvingFrames = 0;
            RenderResolvingState(eventArgs.State);
        }
        else if (eventArgs.State.Mode == HotseatUiMode.Reaction)
        {
            _sawReaction = true;
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
        _directCallbacks.Clear();

        if (state.Mode == HotseatUiMode.Covered)
        {
            RenderCoveredState(state);
            return;
        }

        if (state.Mode == HotseatUiMode.Resolving)
        {
            RenderResolvingState(state);
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
        SetLegacyBoardPanelsVisible(
            _legacy2dBoard,
            !_legacy2dBoard && state.Mode is HotseatUiMode.MulliganSelecting or
                HotseatUiMode.MulliganReview);
        _resolvingShield.Visible = false;
        _lastVisibleViewer = view.Viewer;
        _resultOverlay.Dismiss();
        _errorOverlay.Dismiss();

        if (state.Mode == HotseatUiMode.Finished)
        {
            ClearSensitiveVisuals();
            // Keep the final event batch behind the fully opaque result layer
            // long enough for the normal deferred acknowledgement path to run.
            _dock.EventLog.Replace(view.Viewer, state.Events);
            ScheduleEventAcknowledge(state);
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
                RenderDirectActionState(state);
                break;
            case HotseatUiMode.Reaction:
                RenderDirectReactionState(state);
                break;
            default:
                throw new InvalidOperationException($"Unsupported visible hot-seat mode {state.Mode}.");
        }

        // Keep the hand-off cover opaque until the new viewer's private 2D/3D
        // projection has been rebuilt in the same call stack.
        _privacyOverlay.CompleteReveal();
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
        SetLegacyBoardPanelsVisible(_legacy2dBoard, showMulliganHand: false);
        ClearSensitiveVisuals();
        _resolvingShield.Visible = false;
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

    private void RenderResolvingState(HotseatUiState state)
    {
        HasPresentedSnapshot = false;
        SetLegacyBoardPanelsVisible(_legacy2dBoard, showMulliganHand: false);
        _privacyOverlay.CompleteReveal();
        _resultOverlay.Dismiss();
        _errorOverlay.Dismiss();
        _directCallbacks.Clear();
        _dragSourceSlot = null;
        _dragStartRevision = null;
        _detailsPinned = false;
        _pinnedCardId = null;
        _targetingLine.Stop();
        _directActions.ClearSensitive();
        _reactionOverlay.ClearSensitive();
        _standbyTray.Visible = false;
        _dock.ClearSensitive();
        _battlefieldDetails.ClearSensitive();
        ClearPrivateSlotState();
        if (!_legacy2dBoard)
        {
            _battlefield3d.ClearSensitive();
            _battlefield3d.SetInputEnabled(false);
        }

        HotseatPublicBoardView board = state.PublicBoard ??
            throw new InvalidOperationException("Resolving state is missing its public board projection.");
        RenderPublicBoard(board);
        _resolvingShield.Visible = true;
        int currentLeaks = CountResolvingPrivateLeaks(state);
        _resolvingPrivateLeakCount += currentLeaks;
        if (_ciPrivacySentinelArmed)
        {
            _ciPrivacySentinelVerified = currentLeaks == 0 &&
                                         CountPrivacySentinelLeaks(this) == 0;
            _ciPrivacySentinelFrameAuditPending = true;
            _ciPrivacySentinelArmed = false;
        }
    }

    private void RenderDirectActionState(HotseatUiState state)
    {
        _dock.HideTransientPanels();
        _reactionOverlay.ClearSensitive();
        ConfigureFixedActionButtons(state);
        PresentDirectInteraction(state, reaction: false);
    }

    private void RenderDirectReactionState(HotseatUiState state)
    {
        _sawReaction = true;
        _dock.HideTransientPanels();
        ConfigureFixedActionButtons(state);

        MatchView view = state.Snapshot!;
        if (state.Interaction.Step == HotseatSelectionStep.None)
        {
            string? sourceName = FindCard(view, view.Reaction.Origin?.Source)?.Name;
            _reactionOverlay.Present(view.Reaction, view.Viewer, sourceName);
        }
        else
        {
            // Once a trap has been chosen, the battlefield becomes the target
            // picker. Keeping the centered response layer open would intercept
            // clicks on central unit slots.
            _reactionOverlay.ClearSensitive();
        }
        PresentDirectInteraction(state, reaction: true);
    }

    private void PresentDirectInteraction(HotseatUiState state, bool reaction)
    {
        HotseatInteractionContext context = state.Interaction;
        _directCallbacks.Clear();
        unchecked
        {
            _directChoiceGeneration++;
        }
        _directActions.ClearSensitive();
        UpdateTargetingLine(state);

        string? payment = context.Payment is null
            ? null
            : ActionPresentation.FormatPayment(context.Payment);
        switch (context.Step)
        {
            case HotseatSelectionStep.None:
                return;
            case HotseatSelectionStep.ChooseAction:
                PresentDirectChoices(
                    "选择这张牌要执行的行动：",
                    context.Actions,
                    ActionPresentation.FormatAction,
                    ChooseSurfaceAction,
                    context.Revision,
                    payment,
                    context.CanStepBack);
                break;
            case HotseatSelectionStep.ChooseDonor:
                PresentDirectChoices(
                    "选择部署组件；将被献祭的单位以橙色标出：",
                    context.Donors,
                    donor => donor.HasValue
                        ? $"使用 {FormatSourceChoice(state.Snapshot!, donor.Value)}"
                        : "不使用组件",
                    donor => SelectAndMaybePrepare(
                        () => _controller!.SelectDonor(donor),
                        autoPrepareWhenReady: true),
                    context.Revision,
                    payment,
                    context.CanStepBack);
                break;
            case HotseatSelectionStep.ChooseSlot:
                string? noSlotKey = context.Slots.Any(slot => !slot.HasValue)
                    ? DirectChoiceKey(context.Revision, "no-slot")
                    : null;
                if (noSlotKey is not null)
                {
                    _directCallbacks[noSlotKey] = () => SelectAndMaybePrepare(
                        () => _controller!.SelectSlot(null),
                        autoPrepareWhenReady: true);
                }
                _directActions.Present(
                    "点击亮起的具体格位，或把牌拖到该格位。",
                    noSlotKey is null ? [] : [("不指定格位 / 直接发动", noSlotKey)],
                    payment,
                    context.CanStepBack);
                break;
            case HotseatSelectionStep.ChooseTarget:
                string? noTargetKey = context.Targets.Any(target => target is null)
                    ? DirectChoiceKey(context.Revision, "no-target")
                    : null;
                if (noTargetKey is not null)
                {
                    _directCallbacks[noTargetKey] = () => SelectAndMaybePrepare(
                        () => _controller!.SelectTarget(null),
                        autoPrepareWhenReady: true);
                }
                _directActions.Present(
                    context.Action == ActionKind.Attack
                        ? "选择攻击目标：点击目标，或拖线到目标。"
                        : "选择效果目标；选中即提交。",
                    noTargetKey is null ? [] : [("不指定目标 / 直接发动", noTargetKey)],
                    payment,
                    context.CanStepBack);
                break;
            case HotseatSelectionStep.ChooseAdvance:
                PresentDirectChoices(
                    "选择支付方式；费用变化会立即显示：",
                    context.AdvanceChoices,
                    useAdvance => FormatAdvanceChoice(context, useAdvance),
                    useAdvance => SelectAndMaybePrepare(
                        () => _controller!.SelectAdvance(useAdvance),
                        autoPrepareWhenReady: true),
                    context.Revision,
                    payment,
                    context.CanStepBack);
                break;
            case HotseatSelectionStep.Ready:
                if (context.CanonicalAction?.Command.Action == ActionKind.Surrender)
                {
                    PresentConfirmation(state);
                    return;
                }

                string commitKey = DirectChoiceKey(context.Revision, "commit");
                _directCallbacks[commitKey] = PrepareAndSubmitSelection;
                _directActions.Present(
                    "再次点击明确的动作按钮后执行：",
                    [(ActionPresentation.FormatAction(context.Action!.Value), commitKey)],
                    payment,
                    context.CanStepBack);
                break;
            default:
                throw new InvalidOperationException($"Unsupported selection step {context.Step}.");
        }

        PositionDirectPanel(state);
    }

    private void PositionDirectPanel(HotseatUiState state)
    {
        Vector2 viewport = GetViewportRect().Size;
        float reservedRight = _legacy2dBoard
            ? (_dock.IsCollapsed ? 96.0f : 418.0f)
            : Math.Max(18.0f, viewport.X - _battlefieldControlRail.GetGlobalRect().Position.X + 16.0f);
        float reservedLeft = _legacy2dBoard
            ? 18.0f
            : Math.Max(18.0f, _battlefieldDetails.GetGlobalRect().End.X + 16.0f);
        float availableWidth = Math.Max(360.0f, viewport.X - reservedRight - reservedLeft - 18.0f);
        Vector2 panelSize = new(
            Math.Min(420.0f, availableWidth),
            Mathf.Clamp(_directActions.GetCombinedMinimumSize().Y, 100.0f, 220.0f));
        float maxX = Math.Max(reservedLeft, viewport.X - reservedRight - panelSize.X);
        float maxY = Math.Max(70.0f, viewport.Y - panelSize.Y - 112.0f);
        Vector2 position;

        SnapshotSlot? source = null;
        Vector2? sourceAnchor = null;
        if (state.Interaction.Source is { } sourceId)
        {
            if (!_legacy2dBoard && state.Snapshot is { } view &&
                TryFindSurfaceRef(view, sourceId, out HotseatSurfaceRef surface) &&
                _battlefield3d.CiTryGetScreenAnchor(
                    BattlefieldSurfaceRef.FromHotseat(surface),
                    out Vector2 screenAnchor))
            {
                sourceAnchor = screenAnchor;
            }
            else
            {
                source = FindVisibleSlotForCard(sourceId);
            }
        }

        if (source is null && !sourceAnchor.HasValue)
        {
            position = new Vector2(
                Mathf.Clamp((viewport.X - reservedRight - panelSize.X) / 2.0f, reservedLeft, maxX),
                maxY);
        }
        else if (sourceAnchor is { } anchor)
        {
            Vector2[] candidates =
            [
                new(anchor.X - panelSize.X / 2.0f, anchor.Y - panelSize.Y - 18.0f),
                new(anchor.X - panelSize.X / 2.0f, anchor.Y + 24.0f),
                new(anchor.X - panelSize.X - 24.0f, anchor.Y - panelSize.Y / 2.0f),
                new(anchor.X + 24.0f, anchor.Y - panelSize.Y / 2.0f),
            ];
            Vector2[] destinationAnchors = CurrentBattlefieldDestinationAnchors(state).ToArray();
            int BestScore(Vector2 candidate)
            {
                Vector2 clamped = new(
                    Mathf.Clamp(candidate.X, reservedLeft, maxX),
                    Mathf.Clamp(candidate.Y, 70.0f, maxY));
                var rect = new Rect2(clamped - new Vector2(10.0f, 10.0f),
                    panelSize + new Vector2(20.0f, 20.0f));
                return destinationAnchors.Count(rect.HasPoint);
            }
            Vector2 chosen = candidates.OrderBy(BestScore).First();
            position = new Vector2(
                Mathf.Clamp(chosen.X, reservedLeft, maxX),
                Mathf.Clamp(chosen.Y, 70.0f, maxY));
        }
        else
        {
            Rect2 sourceRect = source!.GetGlobalRect();
            float y = sourceRect.Position.Y - panelSize.Y - 12.0f;
            if (y < 70.0f)
            {
                y = sourceRect.End.Y + 12.0f;
            }
            position = new Vector2(
                Mathf.Clamp(sourceRect.GetCenter().X - panelSize.X / 2.0f, 18.0f, maxX),
                Mathf.Clamp(y, 70.0f, maxY));
        }

        _directActions.SetAnchorsPreset(LayoutPreset.TopLeft);
        _directActions.Position = position;
        _directActions.Size = panelSize;

        if (source is not null)
        {
            Rect2 sourceRect = source.GetGlobalRect();
            Rect2 panelRect = _directActions.GetGlobalRect();
            bool horizontalOverlap = panelRect.End.X >= sourceRect.Position.X &&
                                     sourceRect.End.X >= panelRect.Position.X;
            float verticalGap = Math.Min(
                Math.Abs(panelRect.End.Y - sourceRect.Position.Y),
                Math.Abs(sourceRect.End.Y - panelRect.Position.Y));
            bool insideSafeArea = panelRect.Position.X >= 17.0f &&
                                  panelRect.Position.Y >= 69.0f &&
                                  panelRect.End.X <= viewport.X - reservedRight + 1.0f &&
                                  panelRect.End.Y <= viewport.Y - 111.0f;
            _ciSourceAdjacentPanelVerified |=
                horizontalOverlap && verticalGap <= 14.0f && insideSafeArea;
        }
        else if (sourceAnchor is { } anchor)
        {
            Rect2 panelRect = _directActions.GetGlobalRect();
            bool horizontallyAdjacent = anchor.X >= panelRect.Position.X - 1.0f &&
                                        anchor.X <= panelRect.End.X + 1.0f;
            float verticalGap = Math.Min(
                Math.Abs(panelRect.End.Y - anchor.Y),
                Math.Abs(anchor.Y - panelRect.Position.Y));
            _ciSourceAdjacentPanelVerified |= horizontallyAdjacent && verticalGap <= 30.0f;
        }
    }

    private IEnumerable<Vector2> CurrentBattlefieldDestinationAnchors(HotseatUiState state)
    {
        if (_legacy2dBoard || state.Snapshot is not { } view)
        {
            yield break;
        }

        var surfaces = new HashSet<HotseatSurfaceRef>();
        switch (state.Interaction.Step)
        {
            case HotseatSelectionStep.ChooseDonor:
                foreach (ulong donor in state.Interaction.Donors.OfType<ulong>())
                {
                    if (TryFindSurfaceRef(view, donor, out HotseatSurfaceRef surface))
                    {
                        surfaces.Add(surface);
                    }
                }
                break;
            case HotseatSelectionStep.ChooseSlot when state.Interaction.Action.HasValue:
                foreach (ulong slot in state.Interaction.Slots.OfType<ulong>())
                {
                    if (slot > int.MaxValue)
                    {
                        continue;
                    }
                    HotseatSurfaceRef surface = state.Interaction.Action.Value switch
                    {
                        ActionKind.PlayUnit or ActionKind.Deploy =>
                            HotseatSurfaceRef.UnitSlot(view.Viewer, (int)slot),
                        ActionKind.PlayTactic =>
                            HotseatSurfaceRef.TacticSlot(view.Viewer, (int)slot),
                        _ => default,
                    };
                    if (surface.Player.HasValue)
                    {
                        surfaces.Add(surface);
                    }
                }
                break;
            case HotseatSelectionStep.ChooseTarget:
                foreach (Target target in state.Interaction.Targets.OfType<Target>())
                {
                    if (TryCreateTargetSurface(view, target, out HotseatSurfaceRef surface))
                    {
                        surfaces.Add(surface);
                    }
                }
                break;
        }

        foreach (HotseatSurfaceRef surface in surfaces)
        {
            if (_battlefield3d.CiTryGetScreenAnchor(
                    BattlefieldSurfaceRef.FromHotseat(surface),
                    out Vector2 anchor))
            {
                yield return anchor;
            }
        }
    }

    private void OnDockCollapsedChanged(bool collapsed)
    {
        if (!_legacy2dBoard)
        {
            _dock.CardDetails.Visible = false;
            _battlefieldDetails.Visible = true;
        }
        _dock.OffsetLeft = collapsed ? -78.0f : -400.0f;
        GetNode<MarginContainer>("SafeMargin")
            .AddThemeConstantOverride("margin_right", collapsed ? 96 : 420);
        if (_controller?.State is { } state && _directActions.Visible)
        {
            PositionDirectPanel(state);
        }
    }

    private void PresentDirectChoices<T>(
        string prompt,
        IReadOnlyList<T> values,
        Func<T, string> label,
        Action<T> choose,
        ulong revision,
        string? payment,
        bool canGoBack)
    {
        var choices = new List<(string Label, string Key)>(values.Count);
        for (int index = 0; index < values.Count; index++)
        {
            T value = values[index];
            string key = DirectChoiceKey(revision, $"choice:{index}");
            choices.Add((label(value), key));
            _directCallbacks[key] = () => choose(value);
        }
        _directActions.Present(prompt, choices, payment, canGoBack);
    }

    private string DirectChoiceKey(ulong revision, string suffix) =>
        $"r{revision}:g{_directChoiceGeneration}:{suffix}";

    private static string FormatAdvanceChoice(
        HotseatInteractionContext context,
        bool useAdvance)
    {
        string label = useAdvance ? "使用预支" : "正常支付";
        PaymentPreview? payment = context.Options.Actions
            .FirstOrDefault(action => action.Command.UseAdvance == useAdvance)
            ?.Payment;
        return payment is null
            ? label
            : $"{label} · {ActionPresentation.FormatPayment(payment)}";
    }

    private void ConfigureFixedActionButtons(HotseatUiState state)
    {
        bool idle = state.Interaction.Step == HotseatSelectionStep.None;
        Button endTurn = GetNode<Button>("%EndTurnButton");
        Button surrender = GetNode<Button>("%SurrenderButton");
        endTurn.Disabled = !idle || !state.LegalActions.Any(
            action => action.Command.Action == ActionKind.EndTurn);
        surrender.Disabled = !idle || !state.LegalActions.Any(
            action => action.Command.Action == ActionKind.Surrender);
        endTurn.FocusMode = endTurn.Disabled ? FocusModeEnum.None : FocusModeEnum.All;
        surrender.FocusMode = surrender.Disabled ? FocusModeEnum.None : FocusModeEnum.All;
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

    private void OnRevealRequested()
    {
        try
        {
            PlayerId viewer = _controller?.State.AwaitingPlayer ??
                throw new InvalidOperationException("No covered viewer is awaiting reveal.");
            _countingSession?.AuthorizeReveal(viewer);
            _revealRequestCount++;
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

    private void OnDirectChoiceRequested(string key)
    {
        if (_controller?.State is not { } state ||
            state.Mode is HotseatUiMode.Covered or HotseatUiMode.Resolving or
                HotseatUiMode.Finished or HotseatUiMode.Faulted or HotseatUiMode.Disposed)
        {
            return;
        }

        string currentPrefix =
            $"r{state.Interaction.Revision}:g{_directChoiceGeneration}:";
        if (key.StartsWith(currentPrefix, StringComparison.Ordinal) &&
            _directCallbacks.TryGetValue(key, out Action? callback))
        {
            RunUiAction(callback);
        }
    }

    private void OnStepBackRequested()
    {
        if (_controller?.State.Interaction.CanStepBack == true)
        {
            RunUiAction(() => _controller.StepBackSelection());
        }
    }

    private void OnCancelRequested()
    {
        if (_controller?.State.Interaction.CanStepBack == true)
        {
            OnStepBackRequested();
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
        if (_controller?.State is not { Snapshot: { } view } state ||
            !TryFindSurfaceRef(view, instanceId, out HotseatSurfaceRef surface))
        {
            return;
        }

        RunUiAction(() => ApplySurfaceIntent(new HotseatSurfaceIntent(
            state.Interaction.Revision,
            HotseatSurfaceGesture.Click,
            surface)));
    }

    private void OnReactionPassRequested()
    {
        SelectExactFixedAction(ActionKind.PassReaction, requireConfirmation: false);
    }

    private void OnEndTurnRequested() =>
        SelectExactFixedAction(ActionKind.EndTurn, requireConfirmation: false);

    private void OnSurrenderRequested() =>
        SelectExactFixedAction(ActionKind.Surrender, requireConfirmation: true);

    private void SelectExactFixedAction(ActionKind kind, bool requireConfirmation)
    {
        if (_controller?.State is not { } state || _submitting)
        {
            return;
        }

        LegalAction? action = state.LegalActions.FirstOrDefault(
            candidate => candidate.Command.Action == kind);
        if (action is null)
        {
            return;
        }

        RunUiAction(() =>
        {
            _controller.SelectLegalAction(action);
            if (!requireConfirmation)
            {
                PrepareAndSubmitSelection();
            }
        });
    }

    private void SelectAndMaybePrepare(Action selection, bool autoPrepareWhenReady)
    {
        RunUiAction(() =>
        {
            selection();
            if (!_ciSuppressAutoPrepare && autoPrepareWhenReady &&
                _controller?.State.Interaction.Step == HotseatSelectionStep.Ready)
            {
                PrepareAndSubmitSelection();
            }
        });
    }

    private void ChooseSurfaceAction(ActionKind action)
    {
        if (_ciSuppressAutoPrepare)
        {
            SelectAndMaybePrepare(
                () => _controller!.ChooseAction(action),
                autoPrepareWhenReady: true);
            return;
        }

        if (_controller?.State is not { Snapshot: { } view } state ||
            state.Interaction.Source is not { } source ||
            !TryFindSurfaceRef(view, source, out HotseatSurfaceRef surface))
        {
            return;
        }

        RunUiAction(() => ApplySurfaceIntent(new HotseatSurfaceIntent(
            state.Interaction.Revision,
            HotseatSurfaceGesture.Click,
            surface)
        {
            Action = action,
        }));
    }

    private static bool TryFindSurfaceRef(
        MatchView view,
        ulong instanceId,
        out HotseatSurfaceRef surface)
    {
        foreach (PlayerView player in view.Players)
        {
            for (int index = 0; index < player.Hand.Length; index++)
            {
                if (player.Hand[index].InstanceId == instanceId)
                {
                    surface = HotseatSurfaceRef.HandCard(player.Player, index, instanceId);
                    return true;
                }
            }
            for (int index = 0; index < player.Units.Length; index++)
            {
                if (player.Units[index]?.InstanceId == instanceId)
                {
                    surface = HotseatSurfaceRef.Unit(player.Player, index, instanceId);
                    return true;
                }
            }
            for (int index = 0; index < player.Tactics.Length; index++)
            {
                if (player.Tactics[index]?.InstanceId == instanceId)
                {
                    surface = HotseatSurfaceRef.Tactic(player.Player, index, instanceId);
                    return true;
                }
            }
            for (int index = 0; index < player.Standby.Length; index++)
            {
                if (player.Standby[index].InstanceId == instanceId)
                {
                    surface = HotseatSurfaceRef.StandbyCard(player.Player, index, instanceId);
                    return true;
                }
            }
        }

        surface = default;
        return false;
    }

    private HotseatSurfaceIntentResult ApplySurfaceIntent(HotseatSurfaceIntent intent)
    {
        if (_submitting || _surfaceCoordinator is null || _controller is null)
        {
            throw new InvalidOperationException("The surface coordinator is unavailable or busy.");
        }

        bool confirmationVisible = _dock.Confirmation.Visible;
        HotseatSurfaceIntentResult result = _surfaceCoordinator.ApplyIntent(intent);
        if (!result.Accepted)
        {
            return result;
        }

        _ciSurfaceIntentE2e = true;
        if (result.CommandPrepared)
        {
            ActionKind action = result.CanonicalCommand?.Action ??
                throw new InvalidOperationException(
                    "A prepared surface intent lost its canonical action.");
            BeginPreparedSubmission(action, confirmationVisible);
        }

        return result;
    }

    private void OnSurfaceCommandPreparing()
    {
        if (_ciPrivacySentinelArmed)
        {
            InjectMaliciousPrivateDtoForCi();
        }
    }

    private void PrepareAndSubmitSelection()
    {
        if (_submitting || _controller is null)
        {
            return;
        }

        try
        {
            ActionKind? action = _controller.State.SelectedAction?.Command.Action;
            bool confirmationVisible = _dock.Confirmation.Visible;
            if (_ciPrivacySentinelArmed)
            {
                InjectMaliciousPrivateDtoForCi();
            }
            if (!_controller.PrepareSelectedCommand())
            {
                return;
            }

            BeginPreparedSubmission(
                action ?? throw new InvalidOperationException(
                    "A prepared command lost its action kind."),
                confirmationVisible);
        }
        catch (Exception exception)
        {
            HandleFatal(exception);
        }
    }

    private void BeginPreparedSubmission(ActionKind action, bool confirmationVisible)
    {
        if (_controller?.State.Mode != HotseatUiMode.Resolving ||
            _controller.State.PublicBoard is null)
        {
            throw new InvalidOperationException(
                "A prepared command did not enter the public resolving state.");
        }

        _preparedActionKind = action;
        if (action is not ActionKind.Mulligan and not ActionKind.Surrender)
        {
            _ciSelectionConfirmationViolation |= confirmationVisible;
        }

        RenderResolvingState(_controller.State);
        SubmitPreparedAfterPublicFrames();
    }

    private async void SubmitPreparedAfterPublicFrames()
    {
        if (_submitting)
        {
            return;
        }

        _submitting = true;
        try
        {
            // The public-only projection must survive two complete frames. This
            // gives players a stable resolution transition without retaining a
            // viewer hand, face-down identity, callbacks, or event text.
            await AwaitCompleteResolvingFrame();
            CaptureResolvingPrivacyScreenshotIfRequested();
            await AwaitCompleteResolvingFrame();
            _ciPrivacySentinelFrameAuditPending = false;
            if (_controller is null || !IsInsideTree())
            {
                return;
            }

            EngineStatus status = _controller.SubmitPreparedCommand();
            _minimumResolvingFrames = Math.Min(
                _minimumResolvingFrames,
                _currentResolvingFrames);
            _submissionCount++;
            _lastSubmissionStatus = status;
            if (status.IsSuccess)
            {
                _successfulSubmissionCount++;
                if (_preparedActionKind is { } action)
                {
                    _successfulActionKinds.Add(action);
                }
            }

            _preparedActionKind = null;
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

    private void AuditRenderedResolvingFrame()
    {
        HotseatUiState state = _controller?.State ??
            throw new InvalidOperationException("The resolving frame lost its controller state.");
        if (state.Mode != HotseatUiMode.Resolving || state.PublicBoard is null)
        {
            throw new InvalidOperationException(
                "The public resolving projection disappeared before command submission.");
        }

        int leaks = CountResolvingPrivateLeaks(state);
        _resolvingPrivateLeakCount += leaks;
        if (_ciPrivacySentinelFrameAuditPending)
        {
            _ciPrivacySentinelVerified &= leaks == 0 && CountPrivacySentinelLeaks(this) == 0;
        }
    }

    private async Task AwaitCompleteResolvingFrame()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!string.Equals(
                DisplayServer.GetName(),
                "headless",
                StringComparison.OrdinalIgnoreCase))
        {
            await ToSignal(
                RenderingServer.Singleton,
                RenderingServer.SignalName.FramePostDraw);
        }

        _currentResolvingFrames++;
        AuditRenderedResolvingFrame();
    }

    private void CaptureResolvingPrivacyScreenshotIfRequested()
    {
        if (_ciResolvingScreenshotPath is not { } screenshotPath)
        {
            return;
        }
        if (string.Equals(DisplayServer.GetName(), "headless", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "--ci-screenshot requires a display-backed renderer; the headless texture is unavailable.");
        }

        SaveCiScreenshot(screenshotPath, "Resolving");
        _ciResolvingScreenshotCaptured = true;
        _ciResolvingScreenshotPath = null;
    }

    internal async Task CaptureActionLayoutScreenshotForCiAsync(string screenshotPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenshotPath);
        if (!Path.IsPathFullyQualified(screenshotPath) ||
            _legacy2dBoard || !HasPresentedSnapshot || IsPrivacyCoverVisible ||
            _controller?.State is not { Mode: HotseatUiMode.Action or HotseatUiMode.Reaction })
        {
            throw new InvalidOperationException(
                "An action layout screenshot requires a revealed default-3D command state and absolute path.");
        }

        if (string.Equals(DisplayServer.GetName(), "headless", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "--ci-action-screenshot requires a display-backed renderer.");
        }

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(
            RenderingServer.Singleton,
            RenderingServer.SignalName.FramePostDraw);
        SaveCiScreenshot(screenshotPath, "Action");
    }

    private void SaveCiScreenshot(string screenshotPath, string stateName)
    {
        string? directory = Path.GetDirectoryName(screenshotPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(screenshotPath);
        if (result != Error.Ok)
        {
            throw new IOException($"Godot could not save the {stateName} screenshot ({result}).");
        }

        GD.Print(
            $"SCGS_GODOT_CI_SCREENSHOT_OK path={screenshotPath} " +
            $"size={image.GetWidth()}x{image.GetHeight()} state={stateName}");
    }

    private void ScheduleEventAcknowledge(HotseatUiState state)
    {
        if (_eventAcknowledgeScheduled || !state.HasUnacknowledgedEvents ||
            !state.Viewer.HasValue || !state.PendingEventLastSequence.HasValue)
        {
            return;
        }

        _eventAcknowledgeScheduled = true;
        AcknowledgeRenderedEvents(
            state.Viewer.Value,
            state.PendingEventLastSequence.Value);
    }

    private async void AcknowledgeRenderedEvents(PlayerId viewer, ulong lastSequence)
    {
        try
        {
            // Do not advance the native event cursor until the matching batch has
            // survived a complete rendered frame. Two process-frame boundaries
            // also work under the headless renderer used by CI.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_controller is null || !IsInsideTree())
            {
                return;
            }

            HotseatUiState current = _controller.State;
            if (!current.IsCovered && current.Viewer == viewer &&
                current.PendingEventLastSequence == lastSequence)
            {
                if (_controller.AcknowledgeEvents())
                {
                    _eventAcknowledgeCount++;
                }
            }
        }
        catch (Exception exception)
        {
            HandleFatal(exception);
        }
        finally
        {
            _eventAcknowledgeScheduled = false;
            if (_controller is { State: { } current } && !current.IsCovered &&
                current.HasUnacknowledgedEvents)
            {
                ScheduleEventAcknowledge(current);
            }
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
        GetNode<Label>("%OpponentResourceLabel").Text = FormatRailResources(opponent, "对手");
        GetNode<Label>("%OwnResourceLabel").Text = FormatRailResources(own, "己方");

        if (_legacy2dBoard)
        {
            PopulateSlots(GetNode<Container>("%OpponentTactics"), opponent.Tactics, "策略", opponent.Player, Zone.Tactic, state);
            PopulateSlots(GetNode<Container>("%OpponentUnits"), opponent.Units, "单位", opponent.Player, Zone.Unit, state);
            PopulateSlots(GetNode<Container>("%OwnUnits"), own.Units, "单位", own.Player, Zone.Unit, state);
            PopulateSlots(GetNode<Container>("%OwnTactics"), own.Tactics, "策略", own.Player, Zone.Tactic, state);
        }
        else
        {
            ClearLegacyBoardPools(keepHand: state.Mode is HotseatUiMode.MulliganSelecting or
                HotseatUiMode.MulliganReview);
            RenderBattlefieldPrivate(view, state);
        }
        PopulateOpponentHandBacks(GetNode<Container>("%OpponentHandBacks"), opponent.HandCount);
        if (_legacy2dBoard || state.Mode is HotseatUiMode.MulliganSelecting or
                HotseatUiMode.MulliganReview)
        {
            PopulateHand(GetNode<Container>("%HandCards"), own.Hand, state);
        }
        ConfigureLeaderAndStandbyButtons(view, state);

        GetNode<Label>("%PrivacyProof").Text =
            $"隐私校验：对手手牌仅显示数量 {opponent.HandCount}；安全快照中的对手 hand 数组为 {opponent.Hand.Length}。";
    }

    private void RenderBattlefieldPrivate(MatchView view, HotseatUiState state)
    {
        int cardsBefore = _battlefield3d.CiCardPoolSize;
        int slotsBefore = _battlefield3d.CiSlotPoolSize;
        if (_renderedPerspectiveViewer.HasValue && _renderedPerspectiveViewer.Value != view.Viewer)
        {
            _ciPerspectiveRebuilds++;
        }

        _renderedPerspectiveViewer = view.Viewer;
        _battlefield3d.RenderPrivate(view, state.Interaction);
        if (_hasRendered3d && cardsBefore > 0 && slotsBefore > 0 &&
            _battlefield3d.CiCardPoolSize == cardsBefore &&
            _battlefield3d.CiSlotPoolSize == slotsBefore)
        {
            _ciActorPoolReuses++;
        }
        _hasRendered3d = true;

        bool responseChoiceModal = state.Mode == HotseatUiMode.Reaction &&
                                   state.Interaction.Step == HotseatSelectionStep.None;
        if (responseChoiceModal)
        {
            _battlefield3d.TryConfigureInteraction(
                view.Revision,
                Array.Empty<BattlefieldInteractionSurface>());
        }
        else
        {
            ConfigureBattlefieldInteraction(view, state);
        }
        bool inputEnabled = state.Mode == HotseatUiMode.Action ||
                            state.Mode == HotseatUiMode.Reaction && !responseChoiceModal;
        _battlefield3d.SetInputEnabled(inputEnabled);
    }

    private void ConfigureBattlefieldInteraction(MatchView view, HotseatUiState state)
    {
        var surfaces = new Dictionary<HotseatSurfaceRef, BattlefieldHighlightKind>();
        IReadOnlyList<LegalAction> actions = state.Interaction.Step == HotseatSelectionStep.None
            ? state.LegalActions
            : state.CandidateOptions.Actions;

        foreach (LegalAction legal in actions)
        {
            GameCommandRequest command = legal.Command;
            if (command.Source != 0 &&
                TryFindSurfaceRef(view, command.Source, out HotseatSurfaceRef source))
            {
                AddSurfaceHighlight(surfaces, source, BattlefieldHighlightKind.Legal);
            }

            if (state.Interaction.Step == HotseatSelectionStep.ChooseDonor &&
                command.ComponentDonor is { } donor &&
                TryFindSurfaceRef(view, donor, out HotseatSurfaceRef donorSurface))
            {
                AddSurfaceHighlight(surfaces, donorSurface, BattlefieldHighlightKind.Destination);
            }
            if (state.Interaction.Step == HotseatSelectionStep.ChooseSlot &&
                command.Slot is { } slot && slot <= int.MaxValue)
            {
                HotseatSurfaceRef slotSurface = command.Action switch
                {
                    ActionKind.PlayUnit or ActionKind.Deploy =>
                        HotseatSurfaceRef.UnitSlot(command.Player, (int)slot),
                    ActionKind.PlayTactic =>
                        HotseatSurfaceRef.TacticSlot(command.Player, (int)slot),
                    _ => default,
                };
                if (slotSurface.Player.HasValue)
                {
                    AddSurfaceHighlight(surfaces, slotSurface, BattlefieldHighlightKind.Destination);
                }
            }
            if (state.Interaction.Step == HotseatSelectionStep.ChooseTarget &&
                TryCreateTargetSurface(view, command.Target, out HotseatSurfaceRef targetSurface))
            {
                AddSurfaceHighlight(surfaces, targetSurface, BattlefieldHighlightKind.Destination);
            }
            if (state.Interaction.Step == HotseatSelectionStep.ChooseTarget &&
                command.Action == ActionKind.CastSpell && command.Target is null &&
                !command.Slot.HasValue)
            {
                AddSurfaceHighlight(
                    surfaces,
                    HotseatSurfaceRef.CastZone(),
                    BattlefieldHighlightKind.Destination);
            }
        }

        BattlefieldSurfaceRef? selected = null;
        if (state.Selection.Source is { } selectedId &&
            TryFindSurfaceRef(view, selectedId, out HotseatSurfaceRef selectedSurface))
        {
            selected = BattlefieldSurfaceRef.FromHotseat(selectedSurface);
        }

        BattlefieldSurfaceRef? targetingSource =
            state.Interaction.Action == ActionKind.Attack && selected.HasValue
                ? selected
                : null;
        _battlefield3d.TryConfigureInteraction(
            state.Interaction.Revision,
            surfaces.Select(pair => new BattlefieldInteractionSurface(
                BattlefieldSurfaceRef.FromHotseat(pair.Key),
                pair.Value)),
            selected,
            targetingSource);
    }

    private static void AddSurfaceHighlight(
        IDictionary<HotseatSurfaceRef, BattlefieldHighlightKind> surfaces,
        HotseatSurfaceRef surface,
        BattlefieldHighlightKind highlight)
    {
        if (!surfaces.TryGetValue(surface, out BattlefieldHighlightKind existing) ||
            (uint)highlight > (uint)existing)
        {
            surfaces[surface] = highlight;
        }
    }

    private static bool TryCreateTargetSurface(
        MatchView view,
        Target? target,
        out HotseatSurfaceRef surface)
    {
        if (target is { Kind: TargetKind.Leader })
        {
            surface = HotseatSurfaceRef.Leader(target.Player);
            return true;
        }
        if (target is { Kind: TargetKind.Unit, Unit: { } unit } &&
            TryFindSurfaceRef(view, unit, out surface))
        {
            return true;
        }

        surface = default;
        return false;
    }

    private void ClearLegacyBoardPools(bool keepHand)
    {
        foreach (string path in new[]
                 {
                     "%OpponentTactics", "%OpponentUnits", "%OwnUnits", "%OwnTactics",
                 })
        {
            ClearPool(GetNode<Container>(path));
        }
        if (!keepHand)
        {
            ClearPool(GetNode<Container>("%HandCards"));
        }
    }

    private void PopulateSlots(
        Container container,
        IReadOnlyList<CardView?> cards,
        string zoneName,
        PlayerId player,
        Zone zone,
        HotseatUiState state)
    {
        IReadOnlyList<SnapshotSlot> slots = EnsureSlots(container, cards.Count);
        for (int index = 0; index < cards.Count; index++)
        {
            SnapshotSlot slot = slots[index];
            CardView? card = cards[index];
            if (card is not null)
            {
                slot.ShowCard(card, zoneName, index);
            }
            else
            {
                slot.ShowEmpty(zoneName, index);
            }

            _slotBindings[slot] = new SlotBinding(
                SlotSurface.Board,
                player,
                zone,
                index,
                card);
            bool actionable = IsBoardSlotActionable(state, player, zone, index, card);
            bool source = card?.InstanceId is { } id &&
                          state.LegalActions.Any(action => action.Command.Source == id);
            slot.SetSelectable(actionable || source, actionable ? "点击选择" : "点击行动");
            slot.SetDirectInteraction(
                draggable: source,
                dropTarget: actionable || IsPotentialDropDestination(state, player, zone, index, card));
            bool selected = IsBoardSlotSelected(state, player, zone, index, card);
            slot.SetSelected(selected);
            slot.SetAffordance(selected
                ? SnapshotAffordance.Selected
                : GetBoardAffordance(state, player, zone, index, card, source));
        }
    }

    private void PopulateHand(
        Container container,
        IReadOnlyList<CardView> cards,
        HotseatUiState state)
    {
        IReadOnlyList<SnapshotSlot> slots = EnsureSlots(container, cards.Count);
        for (int index = 0; index < cards.Count; index++)
        {
            CardView card = cards[index];
            SnapshotSlot slot = slots[index];
            slot.CustomMinimumSize = new Vector2(190, 76);
            ulong? id = card.InstanceId;
            bool source = id.HasValue &&
                          state.LegalActions.Any(action => action.Command.Source == id.Value);
            bool mulligan = state.Mode == HotseatUiMode.MulliganSelecting;
            slot.ShowCard(card, "手牌", index, selectable: source || mulligan);
            _slotBindings[slot] = new SlotBinding(
                SlotSurface.Hand,
                state.Snapshot!.Viewer,
                Zone.Hand,
                index,
                card);
            slot.SetDirectInteraction(draggable: source, dropTarget: false);
            bool selected = id.HasValue &&
                            (state.MulliganCards.Contains(id.Value) ||
                             state.Selection.Source == id.Value);
            slot.SetSelected(selected);
            slot.SetAffordance(selected
                ? SnapshotAffordance.Selected
                : source ? SnapshotAffordance.Source : SnapshotAffordance.None);
        }
    }

    private static void PopulateOpponentHandBacks(Container container, ulong handCount)
    {
        while ((ulong)container.GetChildCount() < handCount)
        {
            container.AddChild(new ColorRect
            {
                Color = new Color(0.12f, 0.31f, 0.39f, 1.0f),
                CustomMinimumSize = new Vector2(24, 32),
                MouseFilter = MouseFilterEnum.Ignore,
            });
        }
        for (int index = 0; index < container.GetChildCount(); index++)
        {
            ((CanvasItem)container.GetChild(index)).Visible = (ulong)index < handCount;
        }
    }

    private IReadOnlyList<SnapshotSlot> EnsureSlots(Container container, int count)
    {
        if (!_slotPools.TryGetValue(container, out List<SnapshotSlot>? slots))
        {
            slots = [];
            _slotPools.Add(container, slots);
        }

        while (slots.Count < count)
        {
            SnapshotSlot slot = SlotScene.Instantiate<SnapshotSlot>();
            container.AddChild(slot);
            HookSlot(slot);
            slots.Add(slot);
        }

        for (int index = 0; index < slots.Count; index++)
        {
            SnapshotSlot slot = slots[index];
            slot.Visible = index < count;
            if (index >= count)
            {
                slot.ClearSensitive();
                _slotBindings.Remove(slot);
            }
        }
        return slots;
    }

    private void HookSlot(SnapshotSlot slot)
    {
        slot.Activated += OnBoundSlotActivated;
        slot.Hovered += OnBoundSlotHovered;
        slot.SecondaryActivated += OnBoundSlotSecondaryActivated;
        slot.DragStarted += OnBoundSlotDragStarted;
        slot.DropReceived += OnBoundSlotDropped;
    }

    private void OnBoundSlotActivated(SnapshotSlot slot)
    {
        if (!_slotBindings.TryGetValue(slot, out SlotBinding? binding))
        {
            return;
        }

        if (binding.Surface == SlotSurface.Hand &&
            _controller?.State.Mode == HotseatUiMode.MulliganSelecting)
        {
            OnHandCardRequested(slot, binding.Card!);
            return;
        }

        if (_ciSuppressAutoPrepare)
        {
            switch (binding.Surface)
            {
                case SlotSurface.Hand when binding.Card is not null:
                    OnHandCardRequested(slot, binding.Card);
                    return;
                case SlotSurface.Board:
                    OnBoardSlotRequested(
                        slot,
                        binding.Player,
                        binding.Zone,
                        binding.Index,
                        binding.Card);
                    return;
                case SlotSurface.Standby when binding.Card?.InstanceId is { } source:
                    TrySelectSourceLegacyForCi(source);
                    return;
            }
        }

        if (_controller?.State is not { } state ||
            state.Mode is not HotseatUiMode.Action and not HotseatUiMode.Reaction ||
            !TryCreateInteractionSurfaceRef(state, binding, out HotseatSurfaceRef surface))
        {
            slot.SetSelected(false);
            return;
        }

        RunUiAction(() => ApplySurfaceIntent(new HotseatSurfaceIntent(
            state.Interaction.Revision,
            HotseatSurfaceGesture.Click,
            surface)));
    }

    private static bool TryCreateInteractionSurfaceRef(
        HotseatUiState state,
        SlotBinding binding,
        out HotseatSurfaceRef surface)
    {
        if (state.Interaction.Step == HotseatSelectionStep.ChooseSlot &&
            state.Interaction.Action.HasValue &&
            binding.Player == state.Viewer &&
            state.Interaction.Slots.Contains((ulong)binding.Index) &&
            IsSlotZoneForAction(state.Interaction.Action.Value, binding.Zone))
        {
            surface = binding.Zone == Zone.Unit
                ? HotseatSurfaceRef.UnitSlot(binding.Player, binding.Index)
                : HotseatSurfaceRef.TacticSlot(binding.Player, binding.Index);
            return true;
        }

        return TryCreateSurfaceRef(binding, out surface);
    }

    private void OnBoundSlotHovered(SnapshotSlot slot)
    {
        if (_detailsPinned || !_slotBindings.TryGetValue(slot, out SlotBinding? binding) ||
            binding.Card is not { } card || !slot.HasKnownIdentity)
        {
            return;
        }

        ActiveCardDetails.ShowCard(card, "卡牌详情（右键固定）");
    }

    private void OnBoundSlotSecondaryActivated(SnapshotSlot slot)
    {
        if (!_slotBindings.TryGetValue(slot, out SlotBinding? binding) ||
            binding.Card is not { } card || !slot.HasKnownIdentity)
        {
            OnStepBackRequested();
            return;
        }

        ulong? id = card.InstanceId;
        if (_detailsPinned && _pinnedCardId == id)
        {
            _detailsPinned = false;
            _pinnedCardId = null;
            ActiveCardDetails.ShowPlaceholder();
            return;
        }

        _detailsPinned = true;
        _pinnedCardId = id;
        ActiveCardDetails.ShowCard(card, "已固定卡牌详情（再次右键取消）");
    }

    private void OnBoundSlotDragStarted(SnapshotSlot slot)
    {
        if (!_slotBindings.TryGetValue(slot, out SlotBinding? binding) ||
            binding.Card?.InstanceId is not { } source)
        {
            return;
        }

        _dragSourceSlot = slot;
        _dragStartRevision = _controller?.State.Snapshot?.Revision;
        if (_controller?.State.LegalActions.Any(action =>
                action.Command.Source == source && action.Command.Action == ActionKind.Attack) == true)
        {
            _targetingLine.BeginAtGlobal(slot.GetGlobalRect().GetCenter());
        }
    }

    private void OnBoundSlotDropped(SnapshotSlot destination)
    {
        if (_dragSourceSlot is null ||
            !_slotBindings.TryGetValue(_dragSourceSlot, out SlotBinding? source) ||
            source.Card?.InstanceId is null ||
            !_slotBindings.TryGetValue(destination, out SlotBinding? target))
        {
            return;
        }

        if (_controller?.State is { } state &&
            TryCreateSurfaceRef(source, out HotseatSurfaceRef sourceSurface) &&
            TryCreateSurfaceRef(target, out HotseatSurfaceRef destinationSurface))
        {
            if (_ciSuppressAutoPrepare && source.Card?.InstanceId is { } sourceId)
            {
                ResolveDragAction(sourceId, target);
            }
            else
            {
                RunUiAction(() => ApplySurfaceIntent(new HotseatSurfaceIntent(
                    state.Interaction.Revision,
                    HotseatSurfaceGesture.Drag,
                    sourceSurface)
                {
                    Destination = destinationSurface,
                }));
            }
        }
        _dragSourceSlot = null;
        _dragStartRevision = null;
    }

    private static bool TryCreateSurfaceRef(
        SlotBinding binding,
        out HotseatSurfaceRef surface)
    {
        ulong? instanceId = binding.Card?.InstanceId;
        switch (binding.Surface, binding.Zone, instanceId)
        {
            case (SlotSurface.Hand, Zone.Hand, { } id):
                surface = HotseatSurfaceRef.HandCard(binding.Player, binding.Index, id);
                return true;
            case (SlotSurface.Board, Zone.Unit, { } id):
                surface = HotseatSurfaceRef.Unit(binding.Player, binding.Index, id);
                return true;
            case (SlotSurface.Board, Zone.Unit, null):
                surface = HotseatSurfaceRef.UnitSlot(binding.Player, binding.Index);
                return true;
            case (SlotSurface.Board, Zone.Tactic, { } id):
                surface = HotseatSurfaceRef.Tactic(binding.Player, binding.Index, id);
                return true;
            case (SlotSurface.Board, Zone.Tactic, null):
                surface = HotseatSurfaceRef.TacticSlot(binding.Player, binding.Index);
                return true;
            case (SlotSurface.Standby, Zone.Standby, { } id):
                surface = HotseatSurfaceRef.StandbyCard(binding.Player, binding.Index, id);
                return true;
            default:
                surface = default;
                return false;
        }
    }

    private void ResolveDragAction(ulong source, SlotBinding destination)
    {
        if (_controller?.State is not { } state)
        {
            return;
        }

        if (_dragStartRevision != state.Snapshot?.Revision)
        {
            return;
        }

        (LegalAction Action, DragDestinationRole Role)[] matching = state.LegalActions
            .Where(action => action.Command.Source == source)
            .Select(action => (action, DragRoleForCommand(action.Command, destination)))
            .Where(candidate => candidate.Item2 != DragDestinationRole.None)
            .ToArray();
        ActionKind[] actions = matching
            .Select(candidate => candidate.Action.Command.Action)
            .Distinct()
            .ToArray();
        DragDestinationRole roles = matching.Aggregate(
            DragDestinationRole.None,
            (combined, candidate) => combined | candidate.Role);
        if (actions.Length != 1 || !HasSingleFlag(roles))
        {
            return;
        }

        RunUiAction(() =>
        {
            _controller.BeginSourceSelection(source);
            if (_controller.State.Interaction.Step == HotseatSelectionStep.ChooseAction)
            {
                _controller.ChooseAction(actions[0]);
            }

            ApplyDragDestination(roles, destination);

            if (!_ciSuppressAutoPrepare &&
                _controller.State.Interaction.Step == HotseatSelectionStep.Ready)
            {
                PrepareAndSubmitSelection();
            }
        });
    }

    private void ApplyDragDestination(
        DragDestinationRole role,
        SlotBinding destination)
    {
        HotseatInteractionContext context = _controller!.State.Interaction;
        switch (role)
        {
            case DragDestinationRole.Donor
                when destination.Card?.InstanceId is { } donor &&
                     context.Donors.Contains(donor):
                _controller.SelectDonor(donor);
                break;
            case DragDestinationRole.Slot
                when context.Slots.Contains((ulong)destination.Index):
                _controller.SelectSlot((ulong)destination.Index);
                break;
            case DragDestinationRole.Target
                when destination.Card?.InstanceId is { } unit:
                Target target = Target.UnitTarget(destination.Player, unit);
                if (context.Targets.Any(option => Equals(option, target)))
                {
                    _controller.SelectTarget(target);
                }
                break;
        }
    }

    private static DragDestinationRole DragRoleForCommand(
        GameCommandRequest command,
        SlotBinding destination)
    {
        DragDestinationRole role = DragDestinationRole.None;
        if (destination.Card?.InstanceId is { } cardId &&
            command.Target is { Kind: TargetKind.Unit, Unit: { } targetId } &&
            command.Target.Player == destination.Player && targetId == cardId)
        {
            role |= DragDestinationRole.Target;
        }
        if (destination.Card?.InstanceId is { } donor &&
            command.ComponentDonor == donor)
        {
            role |= DragDestinationRole.Donor;
        }
        if (command.Slot == (ulong)destination.Index &&
            IsSlotZoneForAction(command.Action, destination.Zone) &&
            destination.Player == command.Player)
        {
            role |= DragDestinationRole.Slot;
        }
        return role;
    }

    private static bool HasSingleFlag(DragDestinationRole value) =>
        value != DragDestinationRole.None &&
        ((int)value & ((int)value - 1)) == 0;

    private void ConfigureLeaderAndStandbyButtons(MatchView view, HotseatUiState state)
    {
        PlayerId viewer = view.Viewer;
        ConfigureLeaderButton(_ownLeader, state, viewer);
        ConfigureLeaderButton(_opponentLeader, state, Other(viewer));

        PlayerView own = view.Players[(int)viewer];
        PlayerView opponent = view.Players[(int)Other(viewer)];
        ConfigureStandbyButton(GetNode<Button>("%OwnStandbyButton"), own.Standby, "己方");
        ConfigureStandbyButton(GetNode<Button>("%OpponentStandbyButton"), opponent.Standby, "对方");

        bool canCastDrop = state.LegalActions.Any(action =>
            action.Command.Source != 0 &&
            action.Command.Target is null &&
            !action.Command.Slot.HasValue &&
            !action.Command.ComponentDonor.HasValue &&
            action.Command.Action is ActionKind.CastSpell or ActionKind.Evolve or
                ActionKind.ActivateTrap);
        _castZone.Visible = canCastDrop;
        _castZone.SetDirectInteraction(clickable: false, droppable: canCastDrop);
    }

    private static void ConfigureLeaderButton(
        DirectDropButton button,
        HotseatUiState state,
        PlayerId player)
    {
        bool selectable = state.Interaction.Step == HotseatSelectionStep.ChooseTarget &&
                          state.Interaction.Targets.Any(target =>
                              target is { Kind: TargetKind.Leader } && target.Player == player);
        bool dropTarget = selectable || state.LegalActions.Any(action =>
            action.Command.Target is { Kind: TargetKind.Leader } target && target.Player == player);
        button.SetDirectInteraction(selectable, dropTarget);
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
            if (_ciSuppressAutoPrepare)
            {
                TrySelectSourceLegacyForCi(id.Value);
            }
            else
            {
                TrySelectSource(id.Value);
            }
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
        if (_controller is null || _controller.State.Mode is not HotseatUiMode.Action and not HotseatUiMode.Reaction)
        {
            slot.SetSelected(false);
            return;
        }

        HotseatUiState state = _controller.State;
        ulong? cardId = card?.InstanceId;
        if (state.Interaction.Step == HotseatSelectionStep.ChooseDonor && cardId.HasValue &&
            state.Interaction.Donors.Contains(cardId.Value))
        {
            SelectAndMaybePrepare(
                () => _controller.SelectDonor(cardId.Value),
                autoPrepareWhenReady: true);
            return;
        }

        if (state.Interaction.Step == HotseatSelectionStep.ChooseSlot &&
            player == state.Viewer &&
            state.Interaction.Action.HasValue &&
            IsSlotZoneForAction(state.Interaction.Action.Value, zone) &&
            state.Interaction.Slots.Contains((ulong)index))
        {
            SelectAndMaybePrepare(
                () => _controller.SelectSlot((ulong)index),
                autoPrepareWhenReady: true);
            return;
        }

        if (state.Interaction.Step == HotseatSelectionStep.ChooseTarget && cardId.HasValue)
        {
            Target target = Target.UnitTarget(player, cardId.Value);
            if (state.Interaction.Targets.Any(option => Equals(option, target)))
            {
                SelectAndMaybePrepare(
                    () => _controller.SelectTarget(target),
                    autoPrepareWhenReady: true);
                return;
            }
        }

        if (cardId.HasValue)
        {
            if (_ciSuppressAutoPrepare)
            {
                TrySelectSourceLegacyForCi(cardId.Value);
            }
            else
            {
                TrySelectSource(cardId.Value);
            }
        }
        else
        {
            slot.SetSelected(false);
        }
    }

    private void OnLeaderRequested(bool own)
    {
        if (_controller?.State is not { Snapshot: { } view } state ||
            state.Mode is not HotseatUiMode.Action and not HotseatUiMode.Reaction)
        {
            return;
        }

        PlayerId targetPlayer = own ? view.Viewer : Other(view.Viewer);
        RunUiAction(() => ApplySurfaceIntent(new HotseatSurfaceIntent(
            state.Interaction.Revision,
            HotseatSurfaceGesture.Click,
            HotseatSurfaceRef.Leader(targetPlayer))));
    }

    private void OnLeaderDropped(bool own)
    {
        if (_controller?.State is not { Snapshot: { } view } state ||
            _dragSourceSlot is null ||
            !_slotBindings.TryGetValue(_dragSourceSlot, out SlotBinding? binding) ||
            binding.Card?.InstanceId is null)
        {
            return;
        }

        if (_dragStartRevision != state.Snapshot.Revision)
        {
            _dragSourceSlot = null;
            _dragStartRevision = null;
            return;
        }

        if (TryCreateSurfaceRef(binding, out HotseatSurfaceRef sourceSurface))
        {
            PlayerId targetPlayer = own ? view.Viewer : Other(view.Viewer);
            RunUiAction(() => ApplySurfaceIntent(new HotseatSurfaceIntent(
                state.Interaction.Revision,
                HotseatSurfaceGesture.Drag,
                sourceSurface)
            {
                Destination = HotseatSurfaceRef.Leader(targetPlayer),
            }));
        }
        _dragSourceSlot = null;
        _dragStartRevision = null;
    }

    private void OnCastZoneDropped()
    {
        if (_controller?.State is not { } state || _dragSourceSlot is null ||
            !_slotBindings.TryGetValue(_dragSourceSlot, out SlotBinding? binding) ||
            binding.Card?.InstanceId is null)
        {
            return;
        }

        if (_dragStartRevision != state.Snapshot?.Revision)
        {
            _dragSourceSlot = null;
            _dragStartRevision = null;
            return;
        }

        if (!TryCreateSurfaceRef(binding, out HotseatSurfaceRef sourceSurface))
        {
            return;
        }

        RunUiAction(() => ApplySurfaceIntent(new HotseatSurfaceIntent(
            state.Interaction.Revision,
            HotseatSurfaceGesture.Drag,
            sourceSurface)
        {
            Destination = HotseatSurfaceRef.CastZone(),
            Action = ActionKind.CastSpell,
        }));
        _dragSourceSlot = null;
        _dragStartRevision = null;
    }

    private void ResolveDragTarget(ulong source, Target target)
    {
        if (_controller?.State is not { } state)
        {
            return;
        }

        if (_dragStartRevision != state.Snapshot?.Revision)
        {
            return;
        }

        ActionKind[] actions = state.LegalActions
            .Where(action => action.Command.Source == source && Equals(action.Command.Target, target))
            .Select(action => action.Command.Action)
            .Distinct()
            .ToArray();
        if (actions.Length != 1)
        {
            return;
        }

        RunUiAction(() =>
        {
            _controller.BeginSourceSelection(source);
            if (_controller.State.Interaction.Step == HotseatSelectionStep.ChooseAction)
            {
                _controller.ChooseAction(actions[0]);
            }
            if (_controller.State.Interaction.Step == HotseatSelectionStep.ChooseTarget &&
                _controller.State.Interaction.Targets.Any(option => Equals(option, target)))
            {
                _controller.SelectTarget(target);
            }
            if (!_ciSuppressAutoPrepare &&
                _controller.State.Interaction.Step == HotseatSelectionStep.Ready)
            {
                PrepareAndSubmitSelection();
            }
        });
    }

    private void OpenStandby(bool own)
    {
        if (_controller?.State is not { Snapshot: { } view } state || state.IsCovered)
        {
            return;
        }

        PlayerId player = own ? view.Viewer : Other(view.Viewer);
        IReadOnlyList<CardView> cards = view.Players[(int)player].Standby;
        _standbyTray.Visible = true;
        GetNode<Label>("%StandbyTrayTitle").Text =
            $"{(own ? "己方" : "对方")}战备区（公开）";
        Container container = GetNode<Container>("%StandbyCards");
        IReadOnlyList<SnapshotSlot> slots = EnsureSlots(container, cards.Count);
        for (int index = 0; index < cards.Count; index++)
        {
            CardView card = cards[index];
            SnapshotSlot slot = slots[index];
            ulong? id = card.InstanceId;
            bool source = own && id.HasValue &&
                          state.LegalActions.Any(action => action.Command.Source == id.Value);
            slot.ShowCard(card, "战备", index, selectable: source);
            _slotBindings[slot] = new SlotBinding(
                SlotSurface.Standby,
                player,
                Zone.Standby,
                index,
                card);
            slot.SetDirectInteraction(source, dropTarget: false);
            bool selected = id.HasValue && state.Interaction.Source == id.Value;
            slot.SetSelected(selected);
            slot.SetAffordance(selected
                ? SnapshotAffordance.Selected
                : source ? SnapshotAffordance.Source : SnapshotAffordance.None);
        }
    }

    private void CloseStandby()
    {
        ClearPool(GetNode<Container>("%StandbyCards"));
        _standbyTray.Visible = false;
    }

    private void TrySelectSource(ulong source)
    {
        if (_controller?.State is not { Snapshot: { } view } state ||
            state.Mode is not HotseatUiMode.Action and not HotseatUiMode.Reaction)
        {
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

        if (TryFindSurfaceRef(view, source, out HotseatSurfaceRef surface))
        {
            RunUiAction(() => ApplySurfaceIntent(new HotseatSurfaceIntent(
                state.Interaction.Revision,
                HotseatSurfaceGesture.Click,
                surface)));
        }
    }

    private void TrySelectSourceLegacyForCi(ulong source)
    {
        if (_controller?.State is not { } state ||
            state.Mode is not HotseatUiMode.Action and not HotseatUiMode.Reaction ||
            !state.LegalActions.Any(action => action.Command.Source == source))
        {
            return;
        }

        RunUiAction(() => _controller.BeginSourceSelection(source));
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
        if (state.Interaction.Step == HotseatSelectionStep.ChooseTarget && id.HasValue &&
            state.Interaction.Targets.Any(target =>
                target is { Kind: TargetKind.Unit, Unit: { } unit } &&
                target.Player == player && unit == id.Value))
        {
            return true;
        }

        if (state.Interaction.Step == HotseatSelectionStep.ChooseDonor && id.HasValue &&
            state.Interaction.Donors.Contains(id.Value))
        {
            return true;
        }

        if (player == state.Viewer &&
            state.Interaction.Step == HotseatSelectionStep.ChooseSlot &&
            state.Interaction.Action.HasValue &&
            IsSlotZoneForAction(state.Interaction.Action.Value, zone) &&
            state.Interaction.Slots.Contains((ulong)index))
        {
            return true;
        }

        return id.HasValue && state.LegalActions.Any(action => action.Command.Source == id.Value);
    }

    private static SnapshotAffordance GetBoardAffordance(
        HotseatUiState state,
        PlayerId player,
        Zone zone,
        int index,
        CardView? card,
        bool source)
    {
        ulong? id = card?.InstanceId;
        if (state.Interaction.Step == HotseatSelectionStep.ChooseDonor && id.HasValue &&
            state.Interaction.Donors.Contains(id.Value))
        {
            return SnapshotAffordance.Donor;
        }
        if (state.Interaction.Step == HotseatSelectionStep.ChooseSlot &&
            player == state.Viewer && state.Interaction.Action.HasValue &&
            IsSlotZoneForAction(state.Interaction.Action.Value, zone) &&
            state.Interaction.Slots.Contains((ulong)index))
        {
            return SnapshotAffordance.Slot;
        }
        if (state.Interaction.Step == HotseatSelectionStep.ChooseTarget && id.HasValue &&
            state.Interaction.Targets.Any(target =>
                target is { Kind: TargetKind.Unit, Unit: { } unit } &&
                target.Player == player && unit == id.Value))
        {
            return SnapshotAffordance.Target;
        }
        return source ? SnapshotAffordance.Source : SnapshotAffordance.None;
    }

    private static bool IsPotentialDropDestination(
        HotseatUiState state,
        PlayerId player,
        Zone zone,
        int index,
        CardView? card)
    {
        ulong? id = card?.InstanceId;
        return state.LegalActions.Any(action =>
            (id.HasValue && action.Command.Target is
            { Kind: TargetKind.Unit, Unit: { } unit } &&
             action.Command.Target.Player == player && unit == id.Value) ||
            (id.HasValue && action.Command.ComponentDonor == id.Value) ||
            (action.Command.Slot == (ulong)index &&
             action.Command.Player == player &&
             IsSlotZoneForAction(action.Command.Action, zone)));
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

        return player == state.Viewer && state.Selection.HasSlot &&
               state.Selection.Slot == (ulong)index &&
               state.Selection.Action.HasValue &&
               IsSlotZoneForAction(state.Selection.Action.Value, zone);
    }

    private static bool IsSlotZoneForAction(ActionKind action, Zone zone) => action switch
    {
        ActionKind.PlayUnit or ActionKind.Deploy => zone == Zone.Unit,
        ActionKind.PlayTactic => zone == Zone.Tactic,
        _ => false,
    };

    private void UpdateTargetingLine(HotseatUiState state)
    {
        if (!_legacy2dBoard)
        {
            _targetingLine.Stop();
            return;
        }

        if (state.Interaction.Action != ActionKind.Attack ||
            state.Interaction.Step != HotseatSelectionStep.ChooseTarget ||
            state.Interaction.Source is not { } source)
        {
            _targetingLine.Stop();
            return;
        }

        SnapshotSlot? sourceSlot = _slotBindings.FirstOrDefault(pair =>
            pair.Value.Card?.InstanceId == source).Key;
        if (sourceSlot is null || !sourceSlot.IsVisibleInTree())
        {
            _targetingLine.Stop();
            return;
        }

        Rect2 rect = sourceSlot.GetGlobalRect();
        _targetingLine.BeginAtGlobal(rect.Position + rect.Size / 2.0f);
    }

    private void RenderPublicBoard(HotseatPublicBoardView board)
    {
        if (board.Players.Count != 2)
        {
            throw new InvalidOperationException("A public board must contain exactly two players.");
        }

        PlayerId ownPlayer = _lastVisibleViewer ?? board.ActivePlayer;
        PlayerId opponentPlayer = Other(ownPlayer);
        HotseatPublicPlayerView own = board.Players[(int)ownPlayer];
        HotseatPublicPlayerView opponent = board.Players[(int)opponentPlayer];

        GetNode<Label>("%ViewerLabel").Text = "公开结算视图";
        GetNode<Label>("%PhaseLabel").Text = $"阶段：{PhaseLabel(board.Phase)}";
        GetNode<Label>("%RevisionLabel").Text = $"Revision {board.Revision}";
        GetNode<Label>("%MatchMetaLabel").Text =
            $"先手：{PlayerLabel(board.FirstPlayer)}  ·  当前行动：{PlayerLabel(board.ActivePlayer)}  ·  Seed：{board.RandomSeed}";
        GetNode<Label>("%OpponentSummary").Text = FormatPublicPlayerSummary(opponent, "对手");
        GetNode<Label>("%OpponentZones").Text = FormatPublicZoneSummary(opponent);
        GetNode<Label>("%OwnSummary").Text = FormatPublicPlayerSummary(own, "己方");
        GetNode<Label>("%OwnZones").Text = FormatPublicZoneSummary(own);
        GetNode<Label>("%OpponentResourceLabel").Text = FormatPublicRailResources(opponent, "对手");
        GetNode<Label>("%OwnResourceLabel").Text = FormatPublicRailResources(own, "己方");
        GetNode<Label>("%PrivacyProof").Text =
            $"结算隐私：玩家 0 手牌 {board.Players[0].HandCount}，玩家 1 手牌 {board.Players[1].HandCount}；身份均未保留。";

        if (_legacy2dBoard)
        {
            PopulatePublicSlots(GetNode<Container>("%OpponentTactics"), opponent.Tactics, "策略");
            PopulatePublicSlots(GetNode<Container>("%OpponentUnits"), opponent.Units, "单位");
            PopulatePublicSlots(GetNode<Container>("%OwnUnits"), own.Units, "单位");
            PopulatePublicSlots(GetNode<Container>("%OwnTactics"), own.Tactics, "策略");
        }
        else
        {
            ClearLegacyBoardPools(keepHand: false);
            int cardPoolSize = _battlefield3d.CiCardPoolSize;
            int slotPoolSize = _battlefield3d.CiSlotPoolSize;
            _battlefield3d.RenderPublic(board, ownPlayer);
            if (cardPoolSize > 0 && slotPoolSize > 0 &&
                _battlefield3d.CiCardPoolSize == cardPoolSize &&
                _battlefield3d.CiSlotPoolSize == slotPoolSize)
            {
                _ciActorPoolReuses++;
            }
            _battlefield3d.SetInputEnabled(false);
        }
        PopulateOpponentHandBacks(GetNode<Container>("%OpponentHandBacks"), opponent.HandCount);
        ClearPool(GetNode<Container>("%HandCards"));

        _ownLeader.SetDirectInteraction(clickable: false, droppable: false);
        _opponentLeader.SetDirectInteraction(clickable: false, droppable: false);
        _castZone.Visible = false;
        _ownLeader.TooltipText = string.Empty;
        _opponentLeader.TooltipText = string.Empty;
        _castZone.TooltipText = string.Empty;
        foreach (string path in new[] { "%OwnStandbyButton", "%OpponentStandbyButton", "%EndTurnButton", "%SurrenderButton" })
        {
            Button button = GetNode<Button>(path);
            button.Disabled = true;
            button.TooltipText = string.Empty;
        }
    }

    private void PopulatePublicSlots(
        Container container,
        IReadOnlyList<HotseatPublicCardView?> cards,
        string zoneName)
    {
        IReadOnlyList<SnapshotSlot> slots = EnsureSlots(container, cards.Count);
        for (int index = 0; index < cards.Count; index++)
        {
            SnapshotSlot slot = slots[index];
            HotseatPublicCardView? card = cards[index];
            if (card is null)
            {
                slot.ShowEmpty(zoneName, index);
            }
            else
            {
                slot.ShowPublicCard(card, zoneName, index);
            }
            slot.SetDirectInteraction(draggable: false, dropTarget: false);
            slot.SetAffordance(SnapshotAffordance.None);
            _slotBindings.Remove(slot);
        }
    }

    private static string FormatPublicPlayerSummary(
        HotseatPublicPlayerView player,
        string relation) =>
        $"{relation} · {PlayerLabel(player.Player)}    " +
        $"生命 {player.LeaderHealth}/{player.MaximumLeaderHealth}    " +
        $"当前 PP {player.CurrentPp} / 容量 {player.PpCapacity}    " +
        $"裂痕 {player.Cracks}    进化能量 {player.EvolutionEnergy}";

    private static string FormatRailResources(PlayerView player, string relation) =>
        $"{relation} · 生命 {player.LeaderHealth}/{player.MaximumLeaderHealth} · " +
        $"PP {player.CurrentPp}/{player.PpCapacity} · 裂痕 {player.Cracks} · 进化 {player.EvolutionEnergy}";

    private static string FormatPublicRailResources(
        HotseatPublicPlayerView player,
        string relation) =>
        $"{relation} · 生命 {player.LeaderHealth}/{player.MaximumLeaderHealth} · " +
        $"PP {player.CurrentPp}/{player.PpCapacity} · 裂痕 {player.Cracks} · 进化 {player.EvolutionEnergy}";

    private static string FormatPublicZoneSummary(HotseatPublicPlayerView player) =>
        $"手牌 {player.HandCount} · 牌组 {player.DeckCount} · " +
        $"战备 {player.Standby.Count} · 墓地 {player.Graveyard.Count} · 封存 {player.Archive.Count}";

    private void ClearPrivateSlotState()
    {
        _slotBindings.Clear();
        foreach (List<SnapshotSlot> slots in _slotPools.Values)
        {
            foreach (SnapshotSlot slot in slots)
            {
                slot.ClearSensitive();
            }
        }
    }

    private int CountResolvingPrivateLeaks(HotseatUiState state)
    {
        int leaks = 0;
        int spatialLeaks = CountSpatialPrivateLeaks(requireEmpty: false);
        _ciSpatialPrivateLeaks += spatialLeaks;
        leaks += spatialLeaks;
        if (state.Snapshot is not null || state.Viewer.HasValue || state.AwaitingPlayer.HasValue ||
            state.LegalActions.Count != 0 || state.Events.Count != 0 || state.PendingEvents.Count != 0)
        {
            leaks++;
        }
        if (_directActions.Visible || _reactionOverlay.Visible || _standbyTray.Visible ||
            _dragSourceSlot is not null || _dragStartRevision.HasValue || _slotBindings.Count != 0 ||
            _directCallbacks.Count != 0)
        {
            leaks++;
        }

        if (_directActions.HasSensitiveContentForSmoke ||
            _reactionOverlay.HasSensitiveContentForSmoke ||
            _dock.CardDetails.HasSensitiveContentForSmoke ||
            _battlefieldDetails.HasSensitiveContentForSmoke ||
            _dock.EventLog.HasSensitiveContentForSmoke ||
            _dock.Actions.HasSensitiveContentForSmoke ||
            _dock.Confirmation.HasSensitiveContentForSmoke ||
            _dock.Reaction.HasSensitiveContentForSmoke ||
            _dock.Mulligan.HasSensitiveContentForSmoke)
        {
            leaks++;
        }

        foreach (Control control in new Control[]
                 {
                     _ownLeader, _opponentLeader, _castZone,
                     GetNode<Button>("%OwnStandbyButton"),
                     GetNode<Button>("%OpponentStandbyButton"),
                     GetNode<Button>("%EndTurnButton"),
                     GetNode<Button>("%SurrenderButton"),
                 })
        {
            if (!string.IsNullOrEmpty(control.TooltipText))
            {
                leaks++;
            }
        }

        foreach ((Container container, List<SnapshotSlot> slots) in _slotPools)
        {
            bool hand = container == GetNode<Container>("%HandCards") ||
                        container == GetNode<Container>("%StandbyCards");
            foreach (SnapshotSlot slot in slots)
            {
                if (slot.HasMetadataForSmoke || slot.HasTooltipForSmoke ||
                    !slot.IsInteractionDisabledForSmoke ||
                    (hand && slot.Visible && slot.HasKnownIdentity))
                {
                    leaks++;
                }
            }
        }

        HotseatPublicBoardView? board = state.PublicBoard;
        if (board is not null)
        {
            PlayerId ownPlayer = _lastVisibleViewer ?? board.ActivePlayer;
            ValidateFaceDownPublicSlots(
                board.Players[(int)ownPlayer].Tactics,
                GetNode<Container>("%OwnTactics"),
                ref leaks);
            ValidateFaceDownPublicSlots(
                board.Players[(int)Other(ownPlayer)].Tactics,
                GetNode<Container>("%OpponentTactics"),
                ref leaks);
        }
        return leaks;
    }

    private void InjectMaliciousPrivateDtoForCi()
    {
        MatchView view = _controller?.State.Snapshot ??
            throw new InvalidOperationException("The private sentinel lost its viewer snapshot.");
        CardView template = view.Players[(int)view.Viewer].Hand.FirstOrDefault() ??
            throw new InvalidOperationException("The private sentinel requires one private hand card.");
        var malicious = new CardView
        {
            InstanceId = template.InstanceId,
            DefinitionId = template.DefinitionId,
            Definition = template.Definition,
            Kind = template.Kind,
            Name = PrivacySentinel,
            Owner = template.Owner,
            Controller = template.Controller,
            Zone = template.Zone,
            Sequence = template.Sequence,
            Cost = template.Cost,
            CurrentAttack = template.CurrentAttack,
            CurrentHealth = template.CurrentHealth,
            MaximumHealth = template.MaximumHealth,
            Keywords = template.Keywords,
            Evolved = template.Evolved,
            AttackedThisTurn = template.AttackedThisTurn,
            EnteredThisTurn = template.EnteredThisTurn,
            TemporaryRush = template.TemporaryRush,
            DeployedFromStandby = template.DeployedFromStandby,
            FaceDown = false,
            Countdown = template.Countdown,
            GrantedComponent = template.GrantedComponent,
        };

        ActiveCardDetails.ShowCard(malicious, PrivacySentinel);
        _dock.Actions.Present(PrivacySentinel, [ActionKind.Surrender], canCancel: true);
        _directActions.Present(
            PrivacySentinel,
            [(PrivacySentinel, PrivacySentinel)],
            PrivacySentinel,
            canGoBack: true);
        _directCallbacks[PrivacySentinel] = () =>
            throw new InvalidOperationException("A private sentinel callback survived resolving.");

        Container handContainer = GetNode<Container>("%HandCards");
        SnapshotSlot? handSlot = _slotPools.TryGetValue(
            handContainer,
            out List<SnapshotSlot>? handSlots)
            ? handSlots.FirstOrDefault(slot => slot.Visible)
            : null;
        if (handSlot is not null)
        {
            handSlot.ShowCard(malicious, PrivacySentinel, 0, selectable: true);
            handSlot.ArmPrivacySentinelForSmoke(PrivacySentinel);
        }
        if (!_legacy2dBoard)
        {
            _battlefield3d.CiArmPrivacySentinel(PrivacySentinel);
        }
    }

    private static int CountPrivacySentinelLeaks(Node root)
    {
        int leaks = 0;
        foreach (Node node in EnumerateSubtree(root))
        {
            string? text = node switch
            {
                RichTextLabel richText => richText.Text,
                Label label => label.Text,
                Button button => button.Text,
                _ => null,
            };
            if (text?.Contains(PrivacySentinel, StringComparison.Ordinal) == true)
            {
                leaks++;
            }
            if (node is Control control &&
                control.TooltipText.Contains(PrivacySentinel, StringComparison.Ordinal))
            {
                leaks++;
            }
            foreach (StringName key in node.GetMetaList())
            {
                Variant value = node.GetMeta(key);
                if (key.ToString().Contains(PrivacySentinel, StringComparison.Ordinal) ||
                    value.VariantType == Variant.Type.String &&
                    value.AsString().Contains(PrivacySentinel, StringComparison.Ordinal))
                {
                    leaks++;
                }
            }
        }
        return leaks;
    }

    private static IEnumerable<Node> EnumerateSubtree(Node root)
    {
        yield return root;
        foreach (Node child in root.GetChildren())
        {
            foreach (Node descendant in EnumerateSubtree(child))
            {
                yield return descendant;
            }
        }
    }

    private static void ValidateFaceDownPublicSlots(
        IReadOnlyList<HotseatPublicCardView?> cards,
        Container container,
        ref int leaks)
    {
        for (int index = 0; index < cards.Count && index < container.GetChildCount(); index++)
        {
            if (cards[index] is not { FaceDown: true } ||
                container.GetChild(index) is not SnapshotSlot slot)
            {
                continue;
            }
            if (slot.HasKnownIdentity || slot.HasTooltipForSmoke ||
                !slot.IsInteractionDisabledForSmoke)
            {
                leaks++;
            }
        }
    }

    private void ClearPool(Container container)
    {
        if (!_slotPools.TryGetValue(container, out List<SnapshotSlot>? slots))
        {
            return;
        }
        foreach (SnapshotSlot slot in slots)
        {
            slot.ClearSensitive();
            slot.Visible = false;
            _slotBindings.Remove(slot);
        }
    }

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
        _directCallbacks.Clear();
        _dragSourceSlot = null;
        _dragStartRevision = null;
        _detailsPinned = false;
        _pinnedCardId = null;
        _targetingLine.Stop();
        _resolvingShield.Visible = false;
        _standbyTray.Visible = false;
        _directActions.ClearSensitive();
        _reactionOverlay.ClearSensitive();
        if (!_legacy2dBoard && _battlefield3d is not null &&
            GodotObject.IsInstanceValid(_battlefield3d))
        {
            _battlefield3d.ClearSensitive();
            _battlefield3d.SetInputEnabled(false);
            _ciSpatialPrivateLeaks += CountSpatialPrivateLeaks(requireEmpty: true);
        }

        foreach (string path in new[]
                 {
                     "%OpponentTactics", "%OpponentUnits", "%OwnUnits",
                     "%OwnTactics", "%HandCards", "%StandbyCards",
                 })
        {
            ClearPool(GetNode<Container>(path));
        }
        foreach (CanvasItem child in GetNode<Container>("%OpponentHandBacks").GetChildren())
        {
            child.Visible = false;
        }

        foreach (string path in new[]
                 {
                     "%OpponentSummary", "%OpponentZones", "%OwnSummary", "%OwnZones", "%PrivacyProof",
                     "%OpponentResourceLabel", "%OwnResourceLabel",
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
        _battlefieldDetails.ClearSensitive();
    }

    private int CountSpatialPrivateLeaks(bool requireEmpty)
    {
        if (_legacy2dBoard || _battlefield3d is null ||
            !GodotObject.IsInstanceValid(_battlefield3d))
        {
            return 0;
        }

        int leaks = 0;
        leaks += _battlefield3d.InputEnabled ? 1 : 0;
        leaks += _battlefield3d.CiHasActiveDrag ? 1 : 0;
        leaks += _battlefield3d.CiStableSurfaceLookupCount;
        leaks += _battlefield3d.CiCollisionEnabledCount;
        leaks += _battlefield3d.CiCountForbiddenToken(PrivacySentinel);
        leaks += _battlefield3d.CiTargetArrowVisible ? 1 : 0;
        leaks += _battlefield3d.CiPlacementGhostVisible ? 1 : 0;
        leaks += _battlefield3d.CiOutlineVisibleCount;
        if (requireEmpty)
        {
            leaks += _battlefield3d.Revision != 0 ? 1 : 0;
            leaks += _battlefield3d.CiActiveCardCount;
            leaks += _battlefield3d.CiActiveSlotCount;
        }
        return leaks;
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
        GD.PushError($"Gate 4A match flow failed: {exception}");
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
            _surfaceCoordinator = null;
            _disposedSessionCount++;
        }

        _countingSession = null;
    }

    private void ResetCiMetrics()
    {
        _preparedActionKind = null;
        _lastVisibleViewer = null;
        _dragSourceSlot = null;
        _dragStartRevision = null;
        _minimumResolvingFrames = int.MaxValue;
        _currentResolvingFrames = 0;
        _resolvingPrivateLeakCount = 0;
        _ciSuppressAutoPrepare = false;
        _ciClickDragParityVerified = false;
        _ciSelectionCommitObserved = false;
        _ciSelectionConfirmationViolation = false;
        _ciSawSourceDestinationSignal = false;
        _ciSawFixedSignal = false;
        _ciSawReactionSignal = false;
        _ciCancelledDragNoSideEffects = false;
        _ciSourceAdjacentPanelVerified = false;
        _ciPrivacySentinelArmed = false;
        _ciPrivacySentinelVerified = false;
        _ciPrivacySentinelFrameAuditPending = false;
        _ciResolvingScreenshotPath = null;
        _ciResolvingScreenshotCaptured = false;
        _ciSurfaceIntentE2e = false;
        _renderedPerspectiveViewer = null;
        _ciPerspectiveRebuilds = 0;
        _ciActorPoolReuses = 0;
        _ciHudRaycastBlocks = 0;
        _ciBlockedSpatialInputs = 0;
        _ciSpatialPrivateLeaks = 0;
        _hasRendered3d = false;
        _ciRaycastE2e = false;
        _ciPhysicalDragSubmitted = false;
        _ciKeyboardE2e = false;
        _ciExternalStandbyDrag = false;
        _successfulActionKinds.Clear();
        _coverPresentationCount = 0;
        _revealRequestCount = 0;
        _passingDeviceCoverCount = 0;
        _eventAcknowledgeCount = 0;
        _submissionCount = 0;
        _successfulSubmissionCount = 0;
        _disposedSessionCount = 0;
        _sawReaction = false;
        _lastSubmissionStatus = null;
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

    private enum SlotSurface
    {
        Hand,
        Board,
        Standby,
    }

    [Flags]
    private enum DragDestinationRole
    {
        None = 0,
        Target = 1,
        Donor = 2,
        Slot = 4,
    }

    private sealed record SlotBinding(
        SlotSurface Surface,
        PlayerId Player,
        Zone Zone,
        int Index,
        CardView? Card);

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
        private PlayerId? _authorizedViewer;
        private PlayerId? _awaitingReveal;
        private bool _disposed;

        internal CountingSession(IScgsGameSession inner) =>
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        internal int GetViewCallCount { get; private set; }

        internal int PrematureViewerCallCount { get; private set; }

        internal void RequireReveal(PlayerId viewer)
        {
            _awaitingReveal = viewer;
            _authorizedViewer = null;
        }

        internal void AuthorizeReveal(PlayerId viewer)
        {
            if (_awaitingReveal != viewer)
            {
                throw new InvalidOperationException(
                    $"Viewer {viewer} was not the player protected by the current privacy cover.");
            }

            _authorizedViewer = viewer;
            _awaitingReveal = null;
        }

        public EngineStatus Start() => _inner.Start();

        public MatchView GetView(PlayerId viewer)
        {
            VerifyViewerAccess(viewer);
            GetViewCallCount++;
            return _inner.GetView(viewer);
        }

        public LegalActionsResult ListLegalActions(ActionQueryRequest query)
        {
            VerifyViewerAccess(query.Player);
            return _inner.ListLegalActions(query);
        }

        public ValidTargetsResult ListValidTargets(ActionQueryRequest query)
        {
            VerifyViewerAccess(query.Player);
            return _inner.ListValidTargets(query);
        }

        public ValidSlotsResult ListValidSlots(ActionQueryRequest query)
        {
            VerifyViewerAccess(query.Player);
            return _inner.ListValidSlots(query);
        }

        public ValidDonorsResult ListValidDonors(ActionQueryRequest query)
        {
            VerifyViewerAccess(query.Player);
            return _inner.ListValidDonors(query);
        }

        public PaymentResult PreviewPayment(GameCommandRequest command)
        {
            VerifyViewerAccess(command.Player);
            return _inner.PreviewPayment(command);
        }

        public ReactionContext GetReactionContext(PlayerId viewer)
        {
            VerifyViewerAccess(viewer);
            return _inner.GetReactionContext(viewer);
        }

        public EngineStatus SubmitCommand(GameCommandRequest command)
        {
            VerifyViewerAccess(command.Player);
            return _inner.SubmitCommand(command);
        }

        public EventBatch ReadEvents(PlayerId viewer, ulong afterSequence)
        {
            VerifyViewerAccess(viewer);
            return _inner.ReadEvents(viewer, afterSequence);
        }

        public EventBatch ReadNewEvents(PlayerId viewer)
        {
            VerifyViewerAccess(viewer);
            return _inner.ReadNewEvents(viewer);
        }

        public ulong GetEventCursor(PlayerId viewer)
        {
            VerifyViewerAccess(viewer);
            return _inner.GetEventCursor(viewer);
        }

        private void VerifyViewerAccess(PlayerId viewer)
        {
            if (_authorizedViewer == viewer)
            {
                return;
            }

            PrematureViewerCallCount++;
            throw new InvalidOperationException(
                $"Viewer-scoped native call for {viewer} occurred before an explicit reveal.");
        }

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
