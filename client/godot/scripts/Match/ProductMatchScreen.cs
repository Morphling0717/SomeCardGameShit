// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.GodotClient.Battlefield;
using Scgs.GodotClient.Presentation;
using Scgs.GodotClient.PresentationV2;
using Scgs.GodotClient.Visual;
using Scgs.GodotClient.UI;
using Scgs.GodotClient.Visuals;
using Scgs.Hotseat.Product;
using V04 = Scgs.Client;
using V05 = Scgs.Client.V05;

namespace Scgs.GodotClient.Match;

/// <summary>
/// Product-v05 hot-seat client. It consumes only viewer-safe schema-2 DTOs,
/// keeps the two-frame public resolution gate, and delegates every legality
/// decision to ProductHotseatMatchController/native legal actions.
/// </summary>
public sealed partial class ProductMatchScreen : Control
{
    private Battlefield3DPresenter battlefield = null!;
    private PassDeviceOverlay privacy = null!;
    private MatchInteractionDock dock = null!;
    private CardDetailPanel details = null!;
    private DirectActionPanel direct = null!;
    private ResultOverlay result = null!;
    private ErrorOverlay error = null!;
    private Control resolving = null!;
    private Control standbyTray = null!;
    private Control statusRail = null!;
    private MatchHudPresenter hud = null!;
    private ProductPresentationDirector? presentationDirector;
    private ulong? presentingBatch;
    private ProductHotseatMatchController? controller;
    private MatchVisualIdentity visualIdentity = MatchVisualIdentity.FromDecks(
        LeaderPortraitCatalog.OathguardDeckId,
        LeaderPortraitCatalog.PactmageDeckId);
    private bool submitting;
    private bool preparing;
    private bool pinnedDetails;
    private ulong? detailRevision;
    private V05.PlayerId? detailViewer;
    private bool logOpen;
    private ulong frameToken;
    private ConfirmationPurpose confirmationPurpose;
    private ulong sessionGeneration;
    private bool leavingScene;
    private bool readyCompleted;
    private ulong? confirmationRevision;
    private readonly Dictionary<Button, Action> standbyHandlers = new();

    public event Action? ExitRequested;
    public event Action? RestartRequested;

    public override void _Ready()
    {
        battlefield = GetNode<Battlefield3DPresenter>("%Battlefield3D");
        privacy = GetNode<PassDeviceOverlay>("%PassDeviceOverlay");
        dock = GetNode<MatchInteractionDock>("%InteractionDock");
        details = GetNode<CardDetailPanel>("%BattlefieldCardDetails");
        direct = GetNode<DirectActionPanel>("%DirectActionPanel");
        result = GetNode<ResultOverlay>("%ResultOverlay");
        error = GetNode<ErrorOverlay>("%ErrorOverlay");
        resolving = GetNode<Control>("%ResolvingShield");
        standbyTray = GetNode<Control>("%StandbyTray");
        statusRail = GetNode<Control>("%BattlefieldControlRail");
        hud = new MatchHudPresenter(this);
        hud.ConfigureVisualProfile(BattlefieldVisualProfile.AnimeV1);
        hud.ConfigureIdentity(visualIdentity, LeaderPortraitCatalog.Shared);

        battlefield.ConfigureProductPresentation();
        battlefield.ConfigureVisualIdentity(visualIdentity);
        battlefield.SurfaceGestureRequested += OnSurfaceGesture;
        battlefield.SurfaceHovered += OnSurfaceHovered;
        battlefield.SurfaceSecondaryRequested += OnSurfaceSecondary;
        battlefield.SetGuiBlocker(IsGuiBlockingBattlefield);
        battlefield.SetViewportObstructions(details, statusRail);

        privacy.RevealRequested += Reveal;
        privacy.ExitRequested += RequestExit;
        dock.Mulligan.ConfirmRequested += ConfirmMulligan;
        dock.Mulligan.ReviewAcknowledged += CompleteMulliganReview;
        dock.Confirmation.ConfirmRequested += ConfirmModal;
        dock.Confirmation.CancelRequested += CancelModal;
        dock.CollapsedChanged += _ => ApplyHudLayout();
        direct.ChoiceRequested += OnDirectChoice;
        direct.BackRequested += StepBack;
        result.RestartRequested += () => RestartRequested?.Invoke();
        result.MenuRequested += RequestExit;
        error.RetryRequested += () => RestartRequested?.Invoke();
        error.MenuRequested += RequestExit;
        GetNode<Button>("%CloseStandbyButton").Pressed += CloseStandby;
        GetNode<Button>("%EndTurnButton").Pressed += EndTurn;
        GetNode<Button>("%LogButton").Pressed += ToggleLog;
        GetNode<Button>("%PauseButton").Pressed += OpenPauseMenu;
        GetNode<Button>("%ReturnButton").Pressed += RequestExit;
        GetNode<Button>("%OwnStandbyButton").Pressed += () => OpenStandby(own: true);
        GetNode<Button>("%OpponentStandbyButton").Pressed += () => OpenStandby(own: false);

        PopupMenu pause = GetNode<PopupMenu>("%PauseMenu");
        pause.Clear();
        pause.AddItem("继续对局", 0);
        pause.AddSeparator();
        pause.AddItem("投降", 1);
        pause.AddItem("返回主菜单", 2);
        pause.IdPressed += OnPauseMenuItemPressed;

        AnimeProductTheme.Apply(this);
        TacticalHudTheme.AnimeV1.Apply(this);
        direct.ConfigureVisualProfile(BattlefieldVisualProfile.AnimeV1);
        GetNode<ColorRect>("Background").Color = Colors.Transparent;
        SetLegacyPanelsVisible(false);
        GetNode<Button>("%ReturnButton").Visible = false;
        GetNode<Button>("%SurrenderButton").Visible = false;
        GetNode<Button>("%PauseButton").Visible = true;
        GetNode<Label>("%Title").Visible = false;
        GetNode<Label>("%ViewerLabel").Visible = false;
        GetNode<Label>("%RevisionLabel").Visible = false;
        details.Visible = true;
        dock.Visible = false;
        direct.Visible = false;
        standbyTray.Visible = false;
        resolving.Visible = false;
        result.Dismiss();
        error.Dismiss();
        Resized += ApplyHudLayout;
        ApplyHudLayout();
        readyCompleted = true;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (controller?.State.Mode == ProductHotseatUiMode.Presenting &&
            @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space })
        {
            presentationDirector?.Skip();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!@event.IsActionPressed("ui_cancel") || controller is null || leavingScene)
        {
            return;
        }
        GetViewport().SetInputAsHandled();
        if (confirmationPurpose != ConfirmationPurpose.None)
        {
            CancelModal();
            return;
        }
        ProductHotseatUiState state = controller.State;
        if (state.Mode is ProductHotseatUiMode.Action or ProductHotseatUiMode.Reaction or
            ProductHotseatUiMode.Choice)
        {
            if (state.Interaction.CanStepBack)
            {
                StepBack();
            }
            else if (state.Selection != ProductHotseatActionSelection.Empty)
            {
                controller.CancelSelection();
            }
            else
            {
                OpenPauseMenu();
            }
        }
    }

    public override void _ExitTree()
    {
        PrepareForSceneExit();
        if (controller is not null)
        {
            controller.StateChanged -= OnStateChanged;
            controller.Dispose();
            controller = null;
        }
    }

    internal void PrepareForSceneExit()
    {
        if (leavingScene) return;
        leavingScene = true;
        presentationDirector?.Cancel();
        ++sessionGeneration;
        confirmationPurpose = ConfirmationPurpose.None;
        confirmationRevision = null;
        battlefield?.SetInputEnabled(false);
        battlefield?.ClearSensitive();
        details?.ClearSensitive();
        dock?.ClearSensitive();
        direct?.ClearSensitive();
        CloseStandby();
        pinnedDetails = false;
        GetNodeOrNull<PopupMenu>("%PauseMenu")?.Hide();
    }

    public void Begin(
        V05.IScgsV05GameSession session,
        MatchVisualIdentity identity,
        bool enablePresentation = false)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(identity);
        if (!readyCompleted || leavingScene)
        {
            throw new InvalidOperationException("Product scene initialization did not complete.");
        }
        if (controller is not null)
        {
            throw new InvalidOperationException("The product match screen is already bound.");
        }
        visualIdentity = identity;
        hud.ConfigureIdentity(identity, LeaderPortraitCatalog.Shared);
        battlefield.ConfigureVisualIdentity(identity);
        if (enablePresentation)
        {
            presentationDirector = new ProductPresentationDirector { Name = "ProductPresentationDirector" };
            AddChild(presentationDirector);
            Control capsule = resolving.GetNode<Control>("ResolvingCapsule");
            capsule.AnchorTop = .095f;
            capsule.AnchorBottom = .095f;
        }
        controller = new ProductHotseatMatchController(session, enablePresentation);
        ++sessionGeneration;
        controller.StateChanged += OnStateChanged;
        RenderState(controller.State);
    }

    private void OnStateChanged(object? sender, ProductHotseatStateChangedEventArgs args) =>
        RenderState(args.State);

    private void RenderState(ProductHotseatUiState state)
    {
        if (leavingScene) return;
        try
        {
            HideTransient();
            switch (state.Mode)
            {
                case ProductHotseatUiMode.Covered:
                    RenderCovered(state);
                    break;
                case ProductHotseatUiMode.MulliganSelecting:
                case ProductHotseatUiMode.MulliganReview:
                case ProductHotseatUiMode.Action:
                case ProductHotseatUiMode.Reaction:
                case ProductHotseatUiMode.Choice:
                    RenderPrivate(state);
                    break;
                case ProductHotseatUiMode.Resolving:
                    RenderResolving(state);
                    break;
                case ProductHotseatUiMode.Presenting:
                    RenderPresenting(state);
                    break;
                case ProductHotseatUiMode.Finished:
                    RenderFinished(state);
                    break;
                case ProductHotseatUiMode.Faulted:
                    RenderFaulted(state.FailureText ?? "产品对局无法继续。", canRetry: true);
                    break;
                case ProductHotseatUiMode.Disposed:
                    battlefield.ClearSensitive();
                    break;
                default:
                    throw new InvalidOperationException($"Unknown product UI mode {state.Mode}.");
            }
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            RenderFaulted(exception.Message, canRetry: false);
        }
    }

    private void RenderCovered(ProductHotseatUiState state)
    {
        presentationDirector?.Cancel();
        pinnedDetails = false;
        detailRevision = null;
        detailViewer = null;
        CiRevokeViewerAccess();
        confirmationPurpose = ConfirmationPurpose.None;
        confirmationRevision = null;
        battlefield.ClearSensitive();
        battlefield.SetInputEnabled(false);
        details.ClearSensitive();
        dock.ClearSensitive();
        direct.ClearSensitive();
        standbyTray.Visible = false;
        V05.PlayerId player = state.AwaitingPlayer ?? V05.PlayerId.Player0;
        privacy.Cover(PlayerLabel(player));
    }

    private void RenderPrivate(ProductHotseatUiState state)
    {
        V05.MatchView view = state.Snapshot ??
            throw new InvalidOperationException("The revealed product state has no snapshot.");
        privacy.CompleteReveal();
        dock.Visible = logOpen;
        dock.CardDetails.Visible = false;
        dock.EventLog.Visible = logOpen;
        if (!pinnedDetails && (detailRevision != view.Revision || detailViewer != view.Viewer))
            details.ShowPlaceholder();
        detailRevision = view.Revision;
        detailViewer = view.Viewer;
        battlefield.RenderProductPrivate(view, state.Interaction);
        hud.RenderProduct(view);
        BindPhase(view);
        BindProductInteractions(state);
        dock.EventLog.ReplaceProduct(view.Viewer, state.Events);
        if (state.Mode == ProductHotseatUiMode.MulliganSelecting)
        {
            dock.Visible = true;
            dock.ShowMulligan();
            V05.PlayerView own = Player(view, view.Viewer);
            dock.Mulligan.PresentSelection(
                state.MulliganCards.Count,
                own.Hand.Length,
                state.CanPrepare);
        }
        else if (state.Mode == ProductHotseatUiMode.MulliganReview)
        {
            dock.Visible = true;
            dock.ShowMulligan();
            dock.Mulligan.PresentProductReview(Player(view, view.Viewer).Hand);
        }
        else
        {
            PresentProgressivePrompt(state);
        }

        GetNode<Button>("%EndTurnButton").Disabled =
            state.Mode != ProductHotseatUiMode.Action ||
            !state.LegalActions.Any(action => action.Command.Action == V05.ActionKind.EndTurn);
        GetNode<Button>("%PauseButton").Disabled = false;
        ScheduleEventAcknowledge(state);
    }

    private void RenderResolving(ProductHotseatUiState state)
    {
        presentationDirector?.Cancel();
        pinnedDetails = false;
        detailRevision = null;
        detailViewer = null;
        CiRevokeViewerAccess();
        ProductHotseatPublicBoardView board = state.PublicBoard ??
            throw new InvalidOperationException("The resolving product state has no public board.");
        V05.PlayerId perspective = LastPerspective(board);
        privacy.CompleteReveal();
        battlefield.RenderProductPublic(board, perspective);
        hud.RenderProductPublic(board);
        battlefield.SetInputEnabled(false);
        details.ClearSensitive();
        dock.ClearSensitive();
        direct.ClearSensitive();
        resolving.Visible = true;
        dock.Visible = false;
        GetNode<Label>("%ResolvingLabel").Text = "◆  正在结算公共战场";
        GetNode<Button>("%EndTurnButton").Disabled = true;
        SubmitPreparedAfterPublicFrames();
    }

    private async void RenderPresenting(ProductHotseatUiState state)
    {
        ProductPresentationBatch batch = state.Presentation ??
            throw new InvalidOperationException("Presenting requires a public event batch.");
        if (presentingBatch == batch.Id) return;
        ProductHotseatMatchController? active = controller;
        ulong generation = sessionGeneration;
        presentingBatch = batch.Id;
        try
        {
            if (presentationDirector is null) throw new InvalidOperationException("No presentation director is attached.");
            CiRevokeViewerAccess();
            details.ClearSensitive(); dock.ClearSensitive(); direct.ClearSensitive();
            battlefield.SetInputEnabled(false);
            GetNode<Button>("%EndTurnButton").Disabled = true;
            GetNode<Button>("%PauseButton").Disabled = true;
            resolving.Visible = true;
            GetNode<Label>("%ResolvingLabel").Text = "◆  演出中 · 空格跳过";
            await presentationDirector.PlayAsync(batch, battlefield, ClientVisualSettingsRuntime.Current.ReduceMotion);
            if (!leavingScene && generation == sessionGeneration && ReferenceEquals(active, controller) &&
                !presentationDirector.LastPlaybackCancelled && active?.State.Mode == ProductHotseatUiMode.Presenting &&
                active.State.Presentation?.Id == batch.Id)
                active?.CompletePresentation(batch.Id, batch.Revision);
        }
        catch (Exception exception)
        {
            if (!leavingScene && generation == sessionGeneration)
            {
                GD.PushError(exception.ToString());
                RenderFaulted(exception.Message, canRetry: false);
            }
        }
        finally
        {
            if (presentingBatch == batch.Id) presentingBatch = null;
        }
    }

    private void RenderFinished(ProductHotseatUiState state)
    {
        V05.MatchView view = state.Snapshot ??
            throw new InvalidOperationException("The finished product state has no snapshot.");
        privacy.CompleteReveal();
        battlefield.RenderProductPrivate(view, state.Interaction);
        hud.RenderProduct(view);
        BindPhase(view);
        battlefield.SetInputEnabled(false);
        direct.ClearSensitive();
        result.Present(view.Result, view.Viewer);
    }

    private void RenderFaulted(string message, bool canRetry)
    {
        battlefield.ClearSensitive();
        battlefield.SetInputEnabled(false);
        details.ClearSensitive();
        dock.ClearSensitive();
        direct.ClearSensitive();
        resolving.Visible = false;
        error.Present(message, canRetry);
    }

    private void HideTransient()
    {
        resolving.Visible = false;
        result.Dismiss();
        error.Dismiss();
        dock.HideTransientPanels();
        direct.ClearSensitive();
        CloseStandby();
    }

    private void BindPhase(V05.MatchView view)
    {
        GetNode<Label>("%PhaseLabel").Text = view.Phase switch
        {
            V05.MatchPhase.Mulligan => "调度",
            V05.MatchPhase.Action => "行动",
            V05.MatchPhase.Reaction => "响应",
            V05.MatchPhase.Finished => "对局结束",
            _ => "准备中",
        };
        GetNode<Label>("%OwnResourceLabel").Text = Resources(Player(view, view.Viewer));
        GetNode<Label>("%OpponentResourceLabel").Text = Resources(Player(view, Other(view.Viewer)));
    }

    private void BindProductInteractions(ProductHotseatUiState state)
    {
        V05.MatchView view = state.Snapshot!;
        var surfaces = new List<BattlefieldInteractionSurface>();
        BattlefieldSurfaceRef? selected = null;
        BattlefieldSurfaceRef? targetSource = null;
        if (state.Selection.HasSource && state.Selection.Source != 0 &&
            TryFindSurface(view, state.Selection.Source, out BattlefieldSurfaceRef source))
        {
            selected = source;
            targetSource = source;
        }

        if (state.Mode == ProductHotseatUiMode.MulliganSelecting)
        {
            foreach (V05.CardView card in Player(view, view.Viewer).Hand)
            {
                if (card.InstanceId is { } id && TryFindSurface(view, id, out BattlefieldSurfaceRef hand))
                {
                    surfaces.Add(new BattlefieldInteractionSurface(
                        hand,
                        state.MulliganCards.Contains(id)
                            ? BattlefieldHighlightKind.Selected
                            : BattlefieldHighlightKind.Legal));
                }
            }
        }
        else if (state.Mode is ProductHotseatUiMode.Action or ProductHotseatUiMode.Reaction)
        {
            IEnumerable<V05.LegalAction> candidates = state.Interaction.Options.Actions.Count == 0
                ? state.LegalActions
                : state.Interaction.Options.Actions;
            switch (state.Interaction.Step)
            {
                case ProductHotseatSelectionStep.None:
                case ProductHotseatSelectionStep.ChooseSource:
                case ProductHotseatSelectionStep.ChooseAction:
                    foreach (ulong id in candidates.Select(item => item.Command.Source)
                                 .Where(id => id != 0).Distinct())
                    {
                        AddCardSurface(view, surfaces, id, BattlefieldHighlightKind.Legal);
                    }
                    break;
                case ProductHotseatSelectionStep.ChooseAdditionalCost:
                    foreach (ulong id in candidates.SelectMany(item => item.Command.AdditionalCostCards).Distinct())
                    {
                        AddCardSurface(view, surfaces, id, BattlefieldHighlightKind.Destination);
                    }
                    break;
                case ProductHotseatSelectionStep.ChooseSlot:
                    foreach (ulong slot in candidates.Select(item => item.Command.Slot)
                                 .Where(slot => slot.HasValue).Select(slot => slot!.Value).Distinct())
                    {
                        BattlefieldSurfaceKind kind = SlotKind(state.Selection.Action);
                        surfaces.Add(new BattlefieldInteractionSurface(
                            new BattlefieldSurfaceRef(
                                kind,
                                Battlefield3DPresenter.LegacyPlayer(view.Viewer),
                                checked((int)slot)),
                            BattlefieldHighlightKind.Destination));
                    }
                    break;
                case ProductHotseatSelectionStep.ChooseTarget:
                    foreach (V05.Target target in candidates.Select(item => item.Command.Target)
                                 .Where(target => target is not null).Cast<V05.Target>().Distinct())
                    {
                        if (TryTargetSurface(view, target, out BattlefieldSurfaceRef surface))
                        {
                            surfaces.Add(new BattlefieldInteractionSurface(
                                surface,
                                BattlefieldHighlightKind.Destination));
                        }
                    }
                    break;
            }
        }

        battlefield.TryConfigureInteraction(
            view.Revision,
            surfaces,
            selected,
            state.Interaction.Step == ProductHotseatSelectionStep.ChooseTarget
                ? targetSource
                : null);
        battlefield.SetInputEnabled(
            state.Mode is ProductHotseatUiMode.MulliganSelecting or
                ProductHotseatUiMode.Action or ProductHotseatUiMode.Reaction);
    }

    private void PresentProgressivePrompt(ProductHotseatUiState state)
    {
        if (state.Mode == ProductHotseatUiMode.Choice)
        {
            PresentPendingChoice(state);
            return;
        }

        if (state.Mode == ProductHotseatUiMode.Reaction &&
            state.Interaction.Step is ProductHotseatSelectionStep.None or
                ProductHotseatSelectionStep.ChooseSource)
        {
            var choices = new List<(string Label, string Key)>();
            if (state.LegalActions.Any(action => action.Command.Action == V05.ActionKind.PassReaction))
            {
                choices.Add(("不过", "pass"));
            }
            foreach (V05.LegalAction trap in state.LegalActions
                         .Where(action => action.Command.Action == V05.ActionKind.ActivateTrap)
                         .GroupBy(action => action.Command.Source)
                         .Select(group => group.First()))
            {
                choices.Add((CardName(state.Snapshot!, trap.Command.Source) ?? "发动伏策",
                    $"source:{trap.Command.Source}"));
            }
            ShowDirect("选择响应", choices, state.Interaction.CanStepBack);
            return;
        }

        ProductHotseatInteractionContext interaction = state.Interaction;
        switch (interaction.Step)
        {
            case ProductHotseatSelectionStep.None:
            case ProductHotseatSelectionStep.ChooseSource:
                direct.ClearSensitive();
                break;
            case ProductHotseatSelectionStep.ChooseAction:
                ShowDirect(
                    ProductActionPresentation.FormatStep(interaction.Step),
                    interaction.Options.ActionKinds
                        .Select(action => (ProductActionPresentation.Format(action), $"action:{(uint)action}"))
                        .ToArray(),
                    interaction.CanStepBack);
                break;
            case ProductHotseatSelectionStep.ChooseMode:
                ShowDirect(
                    ProductActionPresentation.FormatStep(interaction.Step),
                    interaction.Options.ModeIds.Where(mode => mode is not null)
                        .Select(mode => (ProductActionPresentation.FormatMode(mode!), $"mode:{mode}"))
                        .ToArray(),
                    interaction.CanStepBack);
                break;
            case ProductHotseatSelectionStep.ChooseAdvance:
                ShowDirect(
                    ProductActionPresentation.FormatStep(interaction.Step),
                    interaction.Options.AdvanceChoices
                        .Select(value => (value ? "动用未来" : "按期支付", $"advance:{(value ? 1 : 0)}"))
                        .ToArray(),
                    interaction.CanStepBack);
                break;
            case ProductHotseatSelectionStep.ChooseAdditionalCost:
            case ProductHotseatSelectionStep.ChooseSlot:
                ShowDirect(
                    ProductActionPresentation.FormatStep(interaction.Step),
                    Array.Empty<(string, string)>(),
                    interaction.CanStepBack);
                break;
            case ProductHotseatSelectionStep.ChooseTarget:
                ShowDirect(
                    ProductActionPresentation.FormatStep(interaction.Step),
                    interaction.Options.Targets.Any(target => target is null)
                        ? [("不选择目标", "skip-target")]
                        : Array.Empty<(string, string)>(),
                    interaction.CanStepBack);
                break;
            case ProductHotseatSelectionStep.Ready:
                V05.ActionKind action = state.SelectedAction?.Command.Action ??
                    interaction.Selection.Action ?? throw new InvalidOperationException("Ready action missing.");
                ShowDirect(
                    "再次点击明确的动作按钮后执行",
                    [(ProductActionPresentation.Format(action), "commit")],
                    interaction.CanStepBack);
                break;
        }
    }

    private void PresentPendingChoice(ProductHotseatUiState state)
    {
        ProductPendingChoiceState choice = state.PendingChoice;
        if (!choice.RequiresInput)
        {
            direct.ClearSensitive();
            return;
        }
        var buttons = choice.Options.Select(option =>
        {
            bool selected = choice.SelectedOptionIds.Contains(option.OptionId);
            string label = option.Card?.Name ?? option.Label ?? "选项";
            return ($"{(selected ? "◆ " : string.Empty)}{label}", $"choice:{option.OptionId}");
        }).ToList();
        if (choice.MinimumSelections == 0)
        {
            buttons.Add(("不选择 / 跳过", "skip-choice"));
        }
        if (state.CanPrepare)
        {
            buttons.Add(("完成选择", "commit-choice"));
        }
        ShowDirect(
            $"选择 {choice.MinimumSelections}～{choice.MaximumSelections} 项" +
            (choice.Ordered ? "（按点击顺序结算）" : string.Empty),
            buttons,
            state.Interaction.CanStepBack || choice.SelectedOptionIds.Count != 0);
    }

    private void ShowDirect(
        string prompt,
        IReadOnlyList<(string Label, string Key)> choices,
        bool canBack)
    {
        V05.PaymentPreview? payment = controller?.State.Interaction.Payment;
        string? paymentText = payment is null ? null :
            $"PP {payment.CurrentPpBefore} → {payment.CurrentPpAfter}" +
            (payment.CracksAfter != payment.CracksBefore ? $" · 裂痕 {payment.CracksBefore} → {payment.CracksAfter}" : "") +
            (payment.BurnCost > 0 ? $" · 燃耗 {payment.BurnCost}" : "") +
            (payment.UsedAdvance ? " · 动用未来" : "");
        direct.Present(prompt, choices, paymentText, canBack);
        Vector2 viewport = GetViewportRect().Size;
        float width = Math.Min(560.0f, viewport.X * 0.44f);
        direct.SetAnchorsPreset(LayoutPreset.TopLeft);
        direct.Position = new Vector2((viewport.X - width) * 0.5f, 82.0f);
        direct.Size = new Vector2(width, 112.0f);
        if ((BattlePresentationReviewRuntime.Enabled || CardFrameReviewRuntime.Enabled) && controller?.State is { Snapshot: { } view } state &&
            TryFindSurface(view, state.Selection.Source, out BattlefieldSurfaceRef source) &&
            // A hand card sits below the board. Anchoring its full prompt above
            // it would cover legal placement slots with an invisible GUI hitbox.
            !(CardFrameReviewRuntime.Enabled && source.Kind == BattlefieldSurfaceKind.HandCard) &&
            battlefield.TryGetScreenBounds(source, out Rect2 bounds))
        {
            float left = Math.Clamp(bounds.GetCenter().X - width * .5f, 300f, viewport.X - width - 260f);
            direct.Position = new Vector2(left, Math.Max(110f, bounds.Position.Y - 124f));
        }
    }

    private void OnSurfaceGesture(object? sender, BattlefieldSurfaceGestureEventArgs args)
    {
        if (leavingScene || confirmationPurpose != ConfirmationPurpose.None ||
            controller?.State is not { } state || state.Snapshot is null ||
            args.Revision != state.Snapshot.Revision)
        {
            return;
        }
        try
        {
            if (args.Source.Kind == BattlefieldSurfaceKind.StandbyPile)
            {
                if (args.Gesture != BattlefieldSurfaceGesture.Click) return;
                OpenStandby(args.Source.Player == Battlefield3DPresenter.LegacyPlayer(state.Snapshot.Viewer));
                return;
            }
            if (state.Mode == ProductHotseatUiMode.MulliganSelecting)
            {
                if (args.Gesture != BattlefieldSurfaceGesture.Click) return;
                if (args.Source.Kind == BattlefieldSurfaceKind.HandCard &&
                    args.Source.InstanceId is { } mulligan)
                {
                    controller.ToggleMulliganCard(mulligan);
                }
                return;
            }
            if (state.Mode is not ProductHotseatUiMode.Action and not ProductHotseatUiMode.Reaction)
            {
                return;
            }

            if (args.Gesture == BattlefieldSurfaceGesture.Drag && args.Destination is { } destination)
            {
                // Reject ambiguous/wrong-owner/wrong-zone drops BEFORE any
                // source selection (which would issue a native legal query).
                V05.ActionKind[] actions = state.LegalActions
                    .Where(item => item.Command.Source == args.Source.InstanceId &&
                        item.Command.Player == state.Snapshot.Viewer &&
                        IsFirstDropDestination(item.Command, destination))
                    .Select(item => item.Command.Action).Distinct().ToArray();
                if (actions.Length != 1 || args.Source.Player !=
                    Battlefield3DPresenter.LegacyPlayer(state.Snapshot.Viewer)) return;
                BeginSource(args.Source);
                if (controller.State.Interaction.Step == ProductHotseatSelectionStep.ChooseAction)
                    controller.ChooseAction(actions[0]);
                ApplyDestination(destination, autoPrepare: true);
                return;
            }
            if (ApplyDestination(args.Source, autoPrepare: true))
            {
                return;
            }
            BeginSource(args.Source);
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Product surface intent rejected without native mutation: {exception.Message}");
        }
    }

    private void BeginSource(BattlefieldSurfaceRef surface)
    {
        if (controller is null || surface.InstanceId is not { } source)
        {
            return;
        }
        if (!controller.State.LegalActions.Any(item => item.Command.Source == source &&
            surface.Player == Battlefield3DPresenter.LegacyPlayer(item.Command.Player))) return;
        controller.BeginSourceSelection(source);
        // A source whose complete action has no destination still requires the
        // explicit action chip rendered by the Ready state; first click never submits.
    }

    private bool ApplyDestination(BattlefieldSurfaceRef surface, bool autoPrepare)
    {
        if (controller is null)
        {
            return false;
        }
        ProductHotseatSelectionStep step = controller.State.Interaction.Step;
        IReadOnlyList<V05.LegalAction> candidates = controller.State.Interaction.Options.Actions;
        switch (step)
        {
            case ProductHotseatSelectionStep.ChooseSlot when
                candidates.Any(item => MatchesSlotSurface(item.Command, surface)):
                controller.SelectSlot(checked((ulong)surface.Index!.Value));
                break;
            case ProductHotseatSelectionStep.ChooseTarget when
                TrySurfaceTarget(surface, out V05.Target? target) &&
                candidates.Any(item => item.Command.Target == target):
                controller.SelectTarget(target!);
                break;
            case ProductHotseatSelectionStep.ChooseAdditionalCost when surface.InstanceId is { } cost &&
                candidates.Any(item => item.Command.AdditionalCostCards.Count == 1 &&
                    item.Command.AdditionalCostCards[0] == cost):
                controller.SelectAdditionalCostCards([cost]);
                break;
            default:
                return false;
        }
        if (autoPrepare && controller.State.CanPrepare)
        {
            PrepareCurrent();
        }
        return true;
    }

    private static bool IsFirstDropDestination(V05.GameCommandRequest command, BattlefieldSurfaceRef destination)
    {
        // A hand permanent/spell must be placed in its exact zone first; a
        // target icon is never a substitute for selecting the back-row slot.
        if (command.Slot.HasValue) return command.AdditionalCostCards.Count == 0 && MatchesSlotSurface(command, destination);
        return command.Action == V05.ActionKind.Attack && TrySurfaceTarget(destination, out V05.Target? target) &&
               command.Target == target;
    }

    private static bool MatchesSlotSurface(V05.GameCommandRequest command, BattlefieldSurfaceRef surface)
    {
        if (surface.Player is not { } owner || surface.Index is not { } index) return false;
        ProductSlotSurfaceKind? kind = surface.Kind switch
        {
            BattlefieldSurfaceKind.UnitSlot => ProductSlotSurfaceKind.MainBoard,
            BattlefieldSurfaceKind.TacticSlot => ProductSlotSurfaceKind.Tactic,
            BattlefieldSurfaceKind.FieldSlot => ProductSlotSurfaceKind.Field,
            _ => null,
        };
        return kind is { } value && ProductSurfaceIntentPolicy.MatchesSlot(command, (V05.PlayerId)(uint)owner, value, index);
    }

    private void OnDirectChoice(string key)
    {
        if (controller is null || leavingScene || confirmationPurpose != ConfirmationPurpose.None)
        {
            return;
        }
        try
        {
            if (key == "skip-target")
            {
                controller.SkipOptionalTarget();
                if (controller.State.CanPrepare) PrepareCurrent();
                return;
            }
            if (key == "skip-choice")
            {
                controller.SkipPendingChoice();
                if (controller.State.CanPrepare) PrepareCurrent();
                return;
            }
            if (key == "pass")
            {
                controller.BeginActionSelection(V05.ActionKind.PassReaction);
                PrepareCurrent();
                return;
            }
            if (key == "commit")
            {
                if (controller.State.SelectedAction?.Command.Action == V05.ActionKind.Surrender)
                {
                    ShowSurrenderConfirmation();
                }
                else
                {
                    PrepareCurrent();
                }
                return;
            }
            if (key == "commit-choice")
            {
                PrepareCurrent();
                return;
            }
            if (key.StartsWith("source:", StringComparison.Ordinal) &&
                ulong.TryParse(key[7..], out ulong source))
            {
                controller.BeginSourceSelection(source);
            }
            else if (key.StartsWith("action:", StringComparison.Ordinal) &&
                     uint.TryParse(key[7..], out uint rawAction))
            {
                controller.ChooseAction((V05.ActionKind)rawAction);
            }
            else if (key.StartsWith("mode:", StringComparison.Ordinal))
            {
                controller.SelectMode(key[5..]);
            }
            else if (key.StartsWith("advance:", StringComparison.Ordinal))
            {
                controller.SelectAdvance(key.EndsWith('1'));
            }
            else if (key.StartsWith("choice:", StringComparison.Ordinal))
            {
                controller.TogglePendingChoiceOption(key[7..]);
                ProductPendingChoiceState pending = controller.State.PendingChoice;
                if (controller.State.CanPrepare && pending.MaximumSelections == 1)
                {
                    PrepareCurrent();
                }
                return;
            }

            if (controller.State.CanPrepare)
            {
                PrepareCurrent();
            }
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Product action choice rejected: {exception.Message}");
        }
    }

    private void ConfirmMulligan()
    {
        if (controller?.State is { CanPrepare: true })
        {
            PrepareCurrent();
        }
    }

    private void CompleteMulliganReview() => controller?.CompleteMulliganReview();

    private async void PrepareCurrent()
    {
        if (preparing || submitting || leavingScene || controller is not { } active || !active.State.CanPrepare) return;
        preparing = true;
        ulong generation = sessionGeneration;
        ulong? revision = active.State.Snapshot?.Revision;
        ProductHotseatUiMode mode = active.State.Mode;
        try
        {
            await CiArmProductPrivacyBeforePrepareAsync();
            if (leavingScene || !GodotObject.IsInstanceValid(this) || !IsInsideTree() ||
                sessionGeneration != generation || !ReferenceEquals(controller, active) ||
                active.State.Mode != mode || active.State.Snapshot?.Revision != revision) return;
            if (!active.PrepareSelectedCommand())
                GD.PushWarning("The selected product command was not ready to prepare.");
        }
        catch (Exception exception)
        {
            if (!leavingScene && sessionGeneration == generation && IsInsideTree())
            {
                GD.PushError(exception.ToString());
                RenderFaulted(exception.Message, canRetry: false);
            }
        }
        finally { preparing = false; }
    }

    private async void SubmitPreparedAfterPublicFrames()
    {
        if (submitting || controller is null || leavingScene)
        {
            return;
        }
        submitting = true;
        ProductHotseatMatchController active = controller;
        ulong generation = sessionGeneration;
        ulong? revision = active.State.PublicBoard?.Revision;
        try
        {
            for (int frame = 0; frame < ProductHotseatMatchController.RequiredPublicFramesBeforeSubmit; ++frame)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!string.Equals(DisplayServer.GetName(), "headless", StringComparison.OrdinalIgnoreCase))
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }
                if (leavingScene || !IsInsideTree() || sessionGeneration != generation ||
                    !ReferenceEquals(controller, active) ||
                    active.State.Mode != ProductHotseatUiMode.Resolving ||
                    active.State.PublicBoard?.Revision != revision)
                {
                    return;
                }
                active.NotifyPublicFrameDrawn(++frameToken);
            }
            await CiObserveProductPrivacyAsync();
            await CiCaptureResolvingIfRequestedAsync();
            if (leavingScene || !GodotObject.IsInstanceValid(this) || !IsInsideTree() ||
                sessionGeneration != generation || !ReferenceEquals(controller, active) ||
                active.State.Mode != ProductHotseatUiMode.Resolving ||
                active.State.PublicBoard?.Revision != revision)
            {
                return;
            }
            CiBeforeSubmit();
            active.SubmitPreparedCommand();
        }
        catch (Exception exception)
        {
            if (!leavingScene && sessionGeneration == generation && IsInsideTree())
            {
                GD.PushError(exception.ToString());
                RenderFaulted(exception.Message, canRetry: true);
            }
        }
        finally
        {
            submitting = false;
        }
    }

    private void Reveal()
    {
        try
        {
            if (leavingScene) return;
            CiAuthorizeReveal();
            controller?.Reveal();
        }
        catch (Exception exception)
        {
            privacy.KeepCoveredAfterFailure(exception.Message);
        }
    }

    private void EndTurn()
    {
        if (controller?.State.Mode != ProductHotseatUiMode.Action)
        {
            return;
        }
        controller.BeginActionSelection(V05.ActionKind.EndTurn);
        PrepareCurrent();
    }

    private void ShowSurrenderConfirmation()
    {
        if (leavingScene || controller?.State is not { } state ||
            !state.LegalActions.Any(action => action.Command.Action == V05.ActionKind.Surrender)) return;
        confirmationPurpose = ConfirmationPurpose.Surrender;
        confirmationRevision = state.Snapshot?.Revision;
        battlefield.SetInputEnabled(false);
        direct.Visible = false;
        dock.Visible = true;
        dock.ShowConfirmation();
        dock.Confirmation.PresentProductConfirmation(
            "确认投降",
            "投降会立即结束本局比赛。",
            "这是唯一需要二次确认的常规行动。",
            canConfirm: true);
    }

    private void ConfirmModal()
    {
        if (confirmationPurpose != ConfirmationPurpose.Surrender || controller is null ||
            leavingScene || confirmationRevision != controller.State.Snapshot?.Revision)
        {
            return;
        }
        confirmationPurpose = ConfirmationPurpose.None;
        confirmationRevision = null;
        controller.PrepareSurrender();
    }

    private void CancelModal()
    {
        confirmationPurpose = ConfirmationPurpose.None;
        confirmationRevision = null;
        dock.HideTransientPanels();
        if (controller is not null && !leavingScene)
        {
            RenderState(controller.State);
        }
    }

    private void StepBack()
    {
        if (controller is null)
        {
            return;
        }
        if (!controller.StepBackSelection())
        {
            controller.CancelSelection();
        }
    }

    private void OpenStandby(bool own)
    {
        if (controller?.State.Snapshot is not { } view || controller.State.Mode != ProductHotseatUiMode.Action)
        {
            return;
        }
        V05.PlayerId owner = own ? view.Viewer : Other(view.Viewer);
        V05.PlayerView player = Player(view, owner);
        Container container = GetNode<Container>("%StandbyCards");
        FreeChildren(container);
        GetNode<Label>("%StandbyTrayTitle").Text = own ? "己方战备" : "对方公开战备";
        foreach (V05.CardView card in player.Standby)
        {
            ulong? id = card.InstanceId;
            var button = new Button
            {
                Text = $"{card.Name}\n{ProductCardPresentation.FormatKind(card.Kind)}",
                CustomMinimumSize = new Vector2(176, 72),
                ThemeTypeVariation = "PrimaryButton",
                Disabled = !own || !id.HasValue ||
                    !controller.State.LegalActions.Any(action => action.Command.Source == id.Value),
                TooltipText = ProductCardPresentation.FormatRules(card),
            };
            if (!button.Disabled)
            {
                ulong captured = id!.Value;
                ProductHotseatMatchController active = controller;
                ulong generation = sessionGeneration;
                ulong revision = view.Revision;
                Action handler = () =>
                {
                    if (leavingScene || sessionGeneration != generation ||
                        !ReferenceEquals(active, controller) ||
                        active.State.Snapshot?.Revision != revision) return;
                    CloseStandby();
                    active.BeginSourceSelection(captured);
                };
                standbyHandlers.Add(button, handler);
                button.Pressed += handler;
            }
            container.AddChild(button);
        }
        standbyTray.Visible = true;
    }

    private void CloseStandby()
    {
        if (standbyTray is null)
        {
            return;
        }
        standbyTray.Visible = false;
        foreach ((Button button, Action handler) in standbyHandlers)
        {
            button.Pressed -= handler;
            button.Text = string.Empty;
            button.TooltipText = string.Empty;
            button.Icon = null;
            button.Disabled = true;
            button.Hide();
        }
        standbyHandlers.Clear();
        Container? cards = GetNodeOrNull<Container>("%StandbyCards");
        if (cards is not null)
        {
            FreeChildren(cards);
        }
    }

    private void ToggleLog()
    {
        if (controller?.State.Mode is not ProductHotseatUiMode.Action and
            not ProductHotseatUiMode.Reaction and not ProductHotseatUiMode.Choice)
        {
            return;
        }
        logOpen = !logOpen;
        dock.Visible = logOpen;
        if (dock.Visible)
        {
            dock.HideTransientPanels();
            dock.SetCollapsed(false);
            dock.CardDetails.Visible = false;
            dock.EventLog.Visible = true;
        }
        ApplyHudLayout();
    }

    private void OpenPauseMenu() =>
        GetNode<PopupMenu>("%PauseMenu").PopupCentered(new Vector2I(320, 210));

    private void OnPauseMenuItemPressed(long id)
    {
        switch (id)
        {
            case 1:
                if (controller?.State.Mode is ProductHotseatUiMode.Action or
                    ProductHotseatUiMode.Reaction or ProductHotseatUiMode.Choice)
                {
                    ShowSurrenderConfirmation();
                }
                break;
            case 2:
                RequestExit();
                break;
        }
    }

    private void OnSurfaceHovered(object? sender, BattlefieldSurfaceHoverEventArgs args)
    {
        if (pinnedDetails || args.Surface?.InstanceId is not { } id ||
            controller?.State.Snapshot is not { } view)
        {
            if (!pinnedDetails && args.Surface is null)
            {
                details.ShowPlaceholder();
            }
            return;
        }
        V05.CardView? card = FindCard(view, id);
        if (card is not null)
        {
            details.ShowProductCard(card);
        }
    }

    private void OnSurfaceSecondary(object? sender, BattlefieldSurfaceHoverEventArgs args)
    {
        if (args.Surface?.InstanceId is not { } id || controller?.State.Snapshot is not { } view)
        {
            pinnedDetails = false;
            details.ShowPlaceholder();
            return;
        }
        V05.CardView? card = FindCard(view, id);
        if (card is not null)
        {
            pinnedDetails = true;
            details.ShowProductCard(card, "已固定卡牌");
        }
    }

    private void ScheduleEventAcknowledge(ProductHotseatUiState rendered)
    {
        if (!rendered.HasUnacknowledgedEvents)
        {
            return;
        }
        ulong revision = rendered.Snapshot?.Revision ?? 0;
        ulong generation = sessionGeneration;
        ProductHotseatMatchController? active = controller;
        V05.PlayerId? viewer = rendered.Snapshot?.Viewer;
        Callable.From(() =>
        {
            if (!leavingScene && confirmationPurpose == ConfirmationPurpose.None &&
                IsInsideTree() && generation == sessionGeneration &&
                ReferenceEquals(active, controller) &&
                controller?.State.Snapshot?.Viewer == viewer &&
                controller?.State.Snapshot?.Revision == revision &&
                controller.State.HasUnacknowledgedEvents &&
                controller.State.Mode is not ProductHotseatUiMode.Covered and
                    not ProductHotseatUiMode.Resolving)
            {
                controller.AcknowledgeEvents();
            }
        }).CallDeferred();
    }

    private void ApplyHudLayout()
    {
        if (!IsNodeReady())
        {
            return;
        }
        hud.ApplyLayout(
            GetViewportRect().Size,
            details,
            statusRail,
            GetNode<Control>("%PhaseCapsule"),
            dock,
            GetNode<Control>("%EndTurnButton"));
        battlefield.SetViewportObstructions(details, statusRail);
    }

    private bool IsGuiBlockingBattlefield(Vector2 screenPosition)
    {
        Control? hovered = GetViewport().GuiGetHoveredControl();
        // Full-screen layout ancestors are not HUD surfaces. Treating the
        // ScreenHost as a modal silently disabled every real board click.
        return hovered is not null && hovered != this && !hovered.IsAncestorOf(this) &&
               hovered != GetNode<Control>("Background") &&
               hovered.MouseFilter != MouseFilterEnum.Ignore;
    }

    private void SetLegacyPanelsVisible(bool visible)
    {
        GetNode<Control>("SafeMargin").Visible = visible;
        foreach (string path in new[]
                 {
                     "SafeMargin/Layout/OpponentPanel",
                     "SafeMargin/Layout/Board",
                     "SafeMargin/Layout/OwnPanel",
                     "SafeMargin/Layout/HandPanel",
                 })
        {
            GetNode<Control>(path).Visible = visible;
        }
    }

    private static void AddCardSurface(
        V05.MatchView view,
        ICollection<BattlefieldInteractionSurface> output,
        ulong instanceId,
        BattlefieldHighlightKind highlight)
    {
        if (TryFindSurface(view, instanceId, out BattlefieldSurfaceRef surface))
        {
            output.Add(new BattlefieldInteractionSurface(surface, highlight));
        }
    }

    private static bool TryFindSurface(
        V05.MatchView view,
        ulong instanceId,
        out BattlefieldSurfaceRef surface)
    {
        foreach (V05.PlayerView player in view.Players)
        {
            V04.PlayerId owner = Battlefield3DPresenter.LegacyPlayer(player.Player);
            for (int index = 0; index < player.Hand.Length; ++index)
            {
                if (player.Hand[index].InstanceId == instanceId)
                {
                    surface = new BattlefieldSurfaceRef(
                        BattlefieldSurfaceKind.HandCard,
                        owner,
                        index,
                        instanceId);
                    return true;
                }
            }
            for (int index = 0; index < player.MainBoard.Length; ++index)
            {
                if (player.MainBoard[index]?.InstanceId == instanceId)
                {
                    surface = new BattlefieldSurfaceRef(
                        BattlefieldSurfaceKind.Unit,
                        owner,
                        index,
                        instanceId);
                    return true;
                }
            }
            for (int index = 0; index < player.Tactics.Length; ++index)
            {
                if (player.Tactics[index]?.InstanceId == instanceId)
                {
                    surface = new BattlefieldSurfaceRef(
                        BattlefieldSurfaceKind.Tactic,
                        owner,
                        index,
                        instanceId);
                    return true;
                }
            }
            if (player.Field?.InstanceId == instanceId)
            {
                surface = new BattlefieldSurfaceRef(
                    BattlefieldSurfaceKind.FieldCard,
                    owner,
                    InstanceId: instanceId);
                return true;
            }
        }
        surface = default;
        return false;
    }

    private static bool TryTargetSurface(
        V05.MatchView view,
        V05.Target target,
        out BattlefieldSurfaceRef surface)
    {
        V04.PlayerId player = Battlefield3DPresenter.LegacyPlayer(target.Player);
        if (target.Kind == V05.TargetKind.Leader)
        {
            surface = new BattlefieldSurfaceRef(BattlefieldSurfaceKind.Leader, player);
            return true;
        }
        if (target.Permanent.HasValue)
        {
            return TryFindSurface(view, target.Permanent.Value, out surface);
        }
        surface = default;
        return false;
    }

    private static bool TrySurfaceTarget(BattlefieldSurfaceRef surface, out V05.Target? target)
    {
        target = null;
        if (!surface.Player.HasValue)
        {
            return false;
        }
        V05.PlayerId player = ProductPlayer(surface.Player.Value);
        if (surface.Kind == BattlefieldSurfaceKind.Leader)
        {
            target = V05.Target.Leader(player);
            return true;
        }
        if ((surface.Kind is BattlefieldSurfaceKind.Unit or BattlefieldSurfaceKind.Tactic or
            BattlefieldSurfaceKind.FieldCard) && surface.InstanceId.HasValue)
        {
            target = V05.Target.PermanentTarget(player, surface.InstanceId.Value);
            return true;
        }
        return false;
    }

    private static BattlefieldSurfaceKind SlotKind(V05.ActionKind? action) => action switch
    {
        V05.ActionKind.CastSpell or V05.ActionKind.PlayTrap => BattlefieldSurfaceKind.TacticSlot,
        _ => BattlefieldSurfaceKind.UnitSlot,
    };

    private static V05.CardView? FindCard(V05.MatchView view, ulong instanceId) =>
        view.Players.SelectMany(AllCards).FirstOrDefault(card => card.InstanceId == instanceId);

    private static IEnumerable<V05.CardView> AllCards(V05.PlayerView player) =>
        player.Hand
            .Concat(player.MainBoard.Where(card => card is not null).Cast<V05.CardView>())
            .Concat(player.Tactics.Where(card => card is not null).Cast<V05.CardView>())
            .Concat(player.Field is null ? [] : [player.Field])
            .Concat(player.Graveyard)
            .Concat(player.Archive)
            .Concat(player.Standby);

    private static string? CardName(V05.MatchView view, ulong instanceId) =>
        FindCard(view, instanceId)?.Name;

    private V05.PlayerId LastPerspective(ProductHotseatPublicBoardView board)
    {
        // During resolving, retain the operator's existing orientation. The
        // controller clears Viewer before exposing the public projection.
        return battlefield.PerspectiveViewer switch
        {
            V04.PlayerId.Player0 => V05.PlayerId.Player0,
            V04.PlayerId.Player1 => V05.PlayerId.Player1,
            _ => board.ActivePlayer,
        };
    }

    private static V05.PlayerView Player(V05.MatchView view, V05.PlayerId player) =>
        view.Players.Single(candidate => candidate.Player == player);

    private static V05.PlayerId Other(V05.PlayerId player) => player switch
    {
        V05.PlayerId.Player0 => V05.PlayerId.Player1,
        V05.PlayerId.Player1 => V05.PlayerId.Player0,
        _ => throw new ArgumentOutOfRangeException(nameof(player)),
    };

    private static V05.PlayerId ProductPlayer(V04.PlayerId player) => player switch
    {
        V04.PlayerId.Player0 => V05.PlayerId.Player0,
        V04.PlayerId.Player1 => V05.PlayerId.Player1,
        _ => throw new ArgumentOutOfRangeException(nameof(player)),
    };

    private static string PlayerLabel(V05.PlayerId player) =>
        player == V05.PlayerId.Player0 ? "玩家 0" : "玩家 1";

    private static string Resources(V05.PlayerView player) =>
        $"PP {player.CurrentPp}/{player.PpCapacity}  裂{player.Cracks}  进{player.EvolutionEnergy}";

    private static void FreeChildren(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void RequestExit() => ExitRequested?.Invoke();

    private enum ConfirmationPurpose
    {
        None,
        Surrender,
    }
}
