// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.GodotClient.Battlefield;
using Scgs.GodotClient.Ci;
using Scgs.GodotClient.Presentation;
using Scgs.Hotseat.Product;
using V05 = Scgs.Client.V05;

namespace Scgs.GodotClient.Match;

internal sealed record ProductSmokeUiStamp(ulong Revision, ProductHotseatEventCursors Cursors, int SubmitAttempts, int NativeCalls);
internal sealed record ProductSmokeDragProbe(ProductSmokeInput Source, ProductSmokeInput Destination, ProductSmokeUiStamp Stamp);

/// <summary>
/// Read-only UI planning for the product smoke runner. Never calls a controller
/// selection/submit method or emits a button/surface signal. The runner sends
/// real viewport input to the returned live control or raycast-verified point.
/// </summary>
public sealed partial class ProductMatchScreen
{
    private ProductSmokeSession? ciSession;
    private V05.GameCommandRequest? ciDesired;
    private bool ciAccumulatingBoard;

    internal ProductSmokeSession CiAudit => ciSession ??
        throw new InvalidOperationException("Product smoke session was not attached.");
    internal ProductHotseatUiMode CiProductMode => controller?.State.Mode ?? ProductHotseatUiMode.Disposed;
    internal string CiProductVisualProfile => battlefield.CiArenaProfile;
    internal bool CiHasSelection => controller?.State.Selection.HasSource == true;
    internal bool CiHasActiveDrag => battlefield.CiHasActiveDrag;
    internal bool CiCanProbeStepBack => controller?.State is { Mode: ProductHotseatUiMode.Action } state &&
        state.Selection.HasSource && state.Selection.HasSlot && state.Selection.Slot is not null && state.Interaction.CanStepBack &&
        state.Interaction.Step == ProductHotseatSelectionStep.ChooseTarget;
    internal ulong CiSelectedSource => controller?.State.Selection.Source ?? 0;
    internal ProductSmokeUiStamp CiCurrentStamp => controller?.State is { Snapshot: { } view } state
        ? new(view.Revision, state.EventCursors, CiAudit.SubmitAttempts, CiAudit.NativeCallCount)
        : throw new InvalidOperationException("No revealed product state for a UI probe.");

    internal ProductSmokeDragProbe? CiPlanInvalidDrag(bool wrongOwner)
    {
        if (controller?.State is not { Mode: ProductHotseatUiMode.Action, Snapshot: { } view } state ||
            state.Selection.HasSource) return null;
        foreach (V05.GameCommandRequest command in state.LegalActions.Select(item => item.Command))
        {
            if (command.Slot is not { } slot || slot > 2 ||
                (wrongOwner ? command.Action is not (V05.ActionKind.PlayUnit or V05.ActionKind.CastSpell or V05.ActionKind.PlayAmulet or V05.ActionKind.PlayTrap)
                    : command.Action != V05.ActionKind.CastSpell) ||
                !TryFindSurface(view, command.Source, out var source) || source.Kind != BattlefieldSurfaceKind.HandCard)
                continue;
            V05.PlayerId destinationPlayer = wrongOwner
                ? view.Viewer == V05.PlayerId.Player0 ? V05.PlayerId.Player1 : V05.PlayerId.Player0
                : view.Viewer;
            BattlefieldSurfaceKind kind = wrongOwner ? SlotKind(command.Action) : BattlefieldSurfaceKind.UnitSlot;
            V05.PlayerView destinationView = Player(view, destinationPlayer);
            V05.CardView? occupant = kind == BattlefieldSurfaceKind.UnitSlot
                ? destinationView.MainBoard[(int)slot] : destinationView.Tactics[(int)slot];
            if (occupant is not null) continue;
            var destination = new BattlefieldSurfaceRef(kind, Battlefield3DPresenter.LegacyPlayer(destinationPlayer), (int)slot);
            if (!battlefield.CiTryGetScreenAnchor(source, out Vector2 sourcePoint) ||
                !battlefield.CiTryGetScreenAnchor(destination, out Vector2 destinationPoint)) continue;
            return new(ProductSmokeInput.Pointer(sourcePoint, source), ProductSmokeInput.Pointer(destinationPoint, destination),
                new(view.Revision, state.EventCursors, CiAudit.SubmitAttempts, CiAudit.NativeCallCount));
        }
        return null;
    }

    internal void CiAssertNoSubmission(ProductSmokeUiStamp before, bool forbidNativeReads = false)
    {
        if (controller?.State is not { Mode: ProductHotseatUiMode.Action, Snapshot: { } view } state ||
            view.Revision != before.Revision || state.EventCursors != before.Cursors ||
            CiAudit.SubmitAttempts != before.SubmitAttempts ||
            (forbidNativeReads && CiAudit.NativeCallCount != before.NativeCalls))
            throw new InvalidOperationException("Rejected UI input changed native submit attempts, revision or event cursors.");
    }

    internal void CiAssertTargetStepBack(ProductSmokeUiStamp before, ulong source)
    {
        CiAssertNoSubmission(before);
        if (controller?.State is not { } state || state.Selection.Source != source ||
            !state.Selection.HasSource || state.Selection.HasSlot ||
            state.Interaction.Step != ProductHotseatSelectionStep.ChooseSlot)
            throw new InvalidOperationException($"Esc slot-back mismatch: step={controller?.State.Interaction.Step}, has_source={controller?.State.Selection.HasSource}, has_slot={controller?.State.Selection.HasSlot}, source_preserved={controller?.State.Selection.Source == source}.");
    }

    internal void CiAttach(ProductSmokeSession audit)
    {
        if (ciSession is not null) throw new InvalidOperationException("Smoke attached twice.");
        ciSession = audit;
    }

    // Called ONLY inside the actual reveal-button callback, before Reveal().
    private void CiAuthorizeReveal()
    {
        if (controller?.State.AwaitingPlayer is { } viewer) ciSession?.AuthorizeReveal(viewer);
    }

    // Called on entry to Covered/Resolving, before any sensitive UI is cleared.
    private void CiRevokeViewerAccess()
    {
        ciSession?.RevokeViewerAccess();
        ciDesired = null;
    }

    // Called immediately before the real UI submits its prepared command.
    private void CiBeforeSubmit() => ciSession?.BeforeSubmit(controller?.PublicFramesDrawn ?? 0);

    internal void CiObserveSafeFrame()
    {
        if (controller is null || ciSession is null) return;
        ProductHotseatUiState state = controller.State;
        bool covered = state.Mode == ProductHotseatUiMode.Covered;
        bool resolvingMode = state.Mode == ProductHotseatUiMode.Resolving;
        if (!covered && !resolvingMode) return;
        if (covered) ++ciSession.CoveredSamples;
        else ++ciSession.ResolvingSamples;
        if (state.Viewer is not null || state.Snapshot is not null ||
            state.LegalActions.Count != 0 || state.Events.Count != 0 ||
            details.HasSensitiveContentForSmoke || direct.HasSensitiveContentForSmoke ||
            dock.EventLog.HasSensitiveContentForSmoke || battlefield.CiHasActiveDrag ||
            battlefield.CiCollisionEnabledCount != 0 ||
            (covered && !privacy.IsCovering))
            throw new InvalidOperationException("Product smoke observed private or interactive covered state.");
    }

    internal ProductSmokeInput? CiNextUiInput(IReadOnlyList<int> coverage, bool surrender, int matchIndex = 0,
        bool surrenderInChoice = false, bool accumulateBoard = false)
    {
        if (ciAccumulatingBoard != accumulateBoard)
        {
            ciAccumulatingBoard = accumulateBoard;
            ciDesired = null;
        }
        ProductHotseatUiState state = controller?.State ??
            throw new InvalidOperationException("Product UI is not bound.");
        switch (state.Mode)
        {
            case ProductHotseatUiMode.Covered:
                return CiButton(privacy.GetNode<Button>("%RevealButton"));
            case ProductHotseatUiMode.Resolving:
                return null;
            case ProductHotseatUiMode.Faulted:
            case ProductHotseatUiMode.Disposed:
                throw new InvalidOperationException("Product UI cannot continue smoke.");
            case ProductHotseatUiMode.Finished:
                return null;
            case ProductHotseatUiMode.MulliganSelecting:
                if (matchIndex > 0 && state.Snapshot is { } mulliganView)
                {
                    V05.CardView? replace = Player(mulliganView, mulliganView.Viewer).Hand.FirstOrDefault(card =>
                        card.Cost > 3 && card.InstanceId is { } id && !state.MulliganCards.Contains(id));
                    if (replace?.InstanceId is { } replaceId && TryFindSurface(mulliganView, replaceId, out var hand))
                        return CiSurface(hand);
                }
                return CiButton(dock.Mulligan.GetNode<Button>("%MulliganConfirmButton"));
            case ProductHotseatUiMode.MulliganReview:
                return CiButton(dock.Mulligan.GetNode<Button>("%MulliganAcknowledgeButton"));
        }

        if (surrender && (surrenderInChoice ? state.Mode == ProductHotseatUiMode.Choice : state.Mode == ProductHotseatUiMode.Reaction))
        {
            if (dock.Confirmation.IsVisibleInTree())
                return CiButton(dock.Confirmation.GetNode<Button>("%ActionConfirmButton"));
            PopupMenu pause = GetNode<PopupMenu>("%PauseMenu");
            if (pause.Visible)
            {
                // Keyboard focus is a UI operation, not a selection in the game.
                pause.SetFocusedItem(pause.GetItemIndex(1));
                return ProductSmokeInput.Keyboard(Key.Enter, checked((int)pause.GetWindowId()));
            }
            return CiButton(GetNode<Button>("%PauseButton"));
        }

        if (state.Mode == ProductHotseatUiMode.Choice)
        {
            ProductPendingChoiceState choice = state.PendingChoice;
            if (choice.MinimumSelections == 0 && choice.SelectedOptionIds.Count == 0)
                return CiChoice("不选择 / 跳过");
            if (state.CanPrepare) return CiChoice("完成选择");
            var option = choice.Options.FirstOrDefault(item =>
                !choice.SelectedOptionIds.Contains(item.OptionId));
            if (option is null) throw new InvalidOperationException("Product choice has no visible continuation.");
            return CiChoice(option.Card?.Name ?? option.Label ?? "选项");
        }

        V05.MatchView view = state.Snapshot ??
            throw new InvalidOperationException("Smoke must not plan before viewer reveal.");
        if (ciDesired is null || ciDesired.ExpectedRevision != view.Revision)
        {
            ciDesired = state.LegalActions
                .Where(action => action.Command.Action != V05.ActionKind.Surrender)
                .Where(action => !accumulateBoard ||
                    ProductSmokeCompletionPolicy.AccumulationPriority(action.Command).HasValue)
                .OrderBy(action => accumulateBoard
                    ? ProductSmokeCompletionPolicy.AccumulationPriority(action.Command)!.Value
                    : CiActionPriority(action.Command, view, coverage, matchIndex, surrender))
                .ThenBy(action => accumulateBoard && action.Command.Target is not null)
                .ThenBy(action => matchIndex > 0 && coverage[(int)V05.ActionKind.Deploy] == 0
                    ? !action.Command.UseAdvance : action.Command.UseAdvance)
                .Select(action => action.Command).FirstOrDefault() ??
                throw new InvalidOperationException("No non-surrender product UI action is available.");
        }

        V05.GameCommandRequest command = ciDesired;
        switch (state.Interaction.Step)
        {
            case ProductHotseatSelectionStep.None:
            case ProductHotseatSelectionStep.ChooseSource:
                if (command.Action == V05.ActionKind.EndTurn)
                    return CiButton(GetNode<Button>("%EndTurnButton"));
                if (command.Action == V05.ActionKind.PassReaction) return CiChoice("不过");
                if (state.Mode == ProductHotseatUiMode.Reaction)
                    return CiChoice(CardName(view, command.Source) ?? "发动伏策");
                if (command.Action == V05.ActionKind.Deploy)
                {
                    if (!standbyTray.IsVisibleInTree())
                        return CiSurface(new BattlefieldSurfaceRef(BattlefieldSurfaceKind.StandbyPile,
                            Battlefield3DPresenter.LegacyPlayer(view.Viewer)));
                    string name = CardName(view, command.Source) ?? "";
                    return CiButton(CiButtons(standbyTray).First(button =>
                        button.Text.StartsWith(name + "\n", StringComparison.Ordinal)));
                }
                if (TryFindSurface(view, command.Source, out BattlefieldSurfaceRef source))
                    return CiSurface(source);
                break;
            case ProductHotseatSelectionStep.ChooseAction:
                return CiChoice(ProductActionPresentation.Format(command.Action));
            case ProductHotseatSelectionStep.ChooseMode:
                return CiChoice(ProductActionPresentation.FormatMode(command.ModeId!));
            case ProductHotseatSelectionStep.ChooseAdvance:
                return CiChoice(command.UseAdvance ? "动用未来" : "按期支付");
            case ProductHotseatSelectionStep.ChooseAdditionalCost:
                if (command.AdditionalCostCards.Count == 1 &&
                    TryFindSurface(view, command.AdditionalCostCards[0], out BattlefieldSurfaceRef cost))
                    return CiSurface(cost);
                break;
            case ProductHotseatSelectionStep.ChooseSlot:
                if (command.Slot is { } slot)
                    return CiSurface(new BattlefieldSurfaceRef(SlotKind(command.Action),
                        Battlefield3DPresenter.LegacyPlayer(view.Viewer), checked((int)slot)));
                break;
            case ProductHotseatSelectionStep.ChooseTarget:
                if (command.Target is null) return CiChoice("不选择目标");
                if (command.Target is { } target && TryTargetSurface(view, target, out BattlefieldSurfaceRef destination))
                    return CiSurface(destination);
                break;
            case ProductHotseatSelectionStep.Ready:
                return CiChoice(ProductActionPresentation.Format(command.Action));
        }
        throw new InvalidOperationException("The real product UI cannot express its enumerated legal command.");
    }

    internal ProductSmokeInput CiRestartInput() => CiButton(result.GetNode<Button>("%RestartMatchButton"));
    internal ProductSmokeInput CiReturnInput() => CiButton(result.GetNode<Button>("%ResultMenuButton"));

    private ProductSmokeInput CiSurface(BattlefieldSurfaceRef surface)
    {
        if (!battlefield.CiTryGetScreenAnchor(surface, out Vector2 point))
            throw new InvalidOperationException("A required product surface is not physically clickable.");
        return ProductSmokeInput.Pointer(point, surface);
    }

    internal void CiVerifyPointerTarget(Vector2 point, BattlefieldSurfaceRef expected)
    {
        // This runs AFTER real mouse motion and a frame, not while planning a
        // different coordinate with stale GuiGetHoveredControl state.
        if (IsGuiBlockingBattlefield(point) ||
            !battlefield.TryGetSurfaceAtScreen(point, out BattlefieldSurfaceRef actual) || actual != expected)
            throw new InvalidOperationException("Real pointer motion did not reach the intended unblocked product surface.");
    }

    private ProductSmokeInput CiChoice(string label) => CiButton(CiButtons(direct).First(button =>
        string.Equals(button.Text, label, StringComparison.Ordinal)));

    private static ProductSmokeInput CiButton(Button button)
    {
        if (!button.IsVisibleInTree() || button.Disabled || button.GetGlobalRect().Size.X < 1)
            throw new InvalidOperationException("A required product button is not visible and enabled.");
        return ProductSmokeInput.ForButton(button);
    }

    private static IEnumerable<Button> CiButtons(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Button button && button.IsVisibleInTree() && !button.Disabled) yield return button;
            foreach (Button descendant in CiButtons(child)) yield return descendant;
        }
    }

    private static int CiActionPriority(V05.GameCommandRequest command, V05.MatchView view,
        IReadOnlyList<int> coverage, int matchIndex, bool waitingForReactionSurrender)
    {
        V05.ActionKind action = command.Action;
        if (action == V05.ActionKind.EndTurn) return 1000;
        if (action == V05.ActionKind.PassReaction && coverage[(int)action] == 0 &&
            coverage[(int)V05.ActionKind.ActivateTrap] > 0) return -200;
        if (matchIndex > 0 && action == V05.ActionKind.Attack && command.Target?.Kind == V05.TargetKind.Leader &&
            Player(view, view.Viewer).OwnTurnNumber < 12 &&
            (waitingForReactionSurrender || new[] { 3, 6, 7, 8, 13 }.Any(kind => coverage[kind] == 0)))
            return 1200; // Leave time to draw and exercise reactive/deployment lines.
        int priority = action switch
        {
            V05.ActionKind.ActivateTrap => 0,
            V05.ActionKind.Deploy => 1,
            V05.ActionKind.Evolve => 2,
            V05.ActionKind.PlayTrap => 3,
            V05.ActionKind.PlayField => 4,
            V05.ActionKind.PlayAmulet => 5,
            V05.ActionKind.CastSpell => 6,
            V05.ActionKind.PlayUnit => 7,
            V05.ActionKind.Attack => 8,
            V05.ActionKind.PassReaction => 90,
            V05.ActionKind.EndTurn => 100,
            _ => 50,
        };
        return priority + (coverage[(int)action] == 0 ? 0 : 100);
    }
}
