// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.GodotClient.Ci;
using Scgs.Hotseat.Product;
using V05 = Scgs.Client.V05;

namespace Scgs.GodotClient.Match;

public sealed partial class ProductMatchScreen
{
    private ProductVisualCapture? ciVisualCapture;

    internal void CiAttachProductVisual(ProductVisualCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (ciVisualCapture is not null && !ReferenceEquals(ciVisualCapture, capture))
            throw new InvalidOperationException("A product match cannot change its visual evidence owner.");
        if (!CiAudit.IsRealNativeSession) throw new InvalidOperationException("Product capture requires a real v05 session.");
        ciVisualCapture = capture;
    }

    internal Task CiCaptureProductVisualAsync() =>
        ciVisualCapture?.CaptureAsync(CiVisualStamp) ?? Task.CompletedTask;

    // Awaited by the normal prepared-command pipeline after its two public
    // frames, before SubmitPreparedCommand. It never changes/reads the viewer.
    internal Task CiCaptureResolvingIfRequestedAsync() =>
        CiProductMode == ProductHotseatUiMode.Resolving
            ? CiCaptureProductVisualAsync() : Task.CompletedTask;

    internal async Task CiCaptureProductPerformanceAsync()
    {
        ProductVisualCapture? capture = ciVisualCapture;
        if (capture is null || !capture.RequiresPerformance || capture.PerformanceCompleted || !CiHeavyBoard()) return;
        ProductVisualStamp? stamp = CiVisualStamp();
        if (stamp?.State != "action") return;
        // Retain only counts and the identity-free stamp across await.
        int player0 = controller!.State.Snapshot!.Players[0].MainBoard.Count(card => card is not null);
        int player1 = controller.State.Snapshot.Players[1].MainBoard.Count(card => card is not null);
        await capture.MeasureHeavyBoardAsync(this, stamp, player0, player1, CiVisualStamp, CiHeavyBoard);
    }

    private bool CiHeavyBoard()
    {
        if (leavingScene || controller?.State is not { Mode: ProductHotseatUiMode.Action, Snapshot: { } snapshot } ||
            snapshot.Result != V05.GameResult.Ongoing || snapshot.Players.Length != 2 ||
            controller.State.Interaction.Step is not (ProductHotseatSelectionStep.None or ProductHotseatSelectionStep.ChooseSource) ||
            !IsInsideTree() || !IsVisibleInTree() || battlefield.CiArenaProfile != "anime-v1") return false;
        int first = snapshot.Players[0].MainBoard.Count(card => card is not null);
        int second = snapshot.Players[1].MainBoard.Count(card => card is not null);
        return first >= ProductVisualCapture.MinimumHeavyBoardCardsPerPlayer &&
               second >= ProductVisualCapture.MinimumHeavyBoardCardsPerPlayer &&
               first + second >= ProductVisualCapture.MinimumHeavyBoardCards &&
               battlefield.CiActiveCardCount >= first + second;
    }

    private ProductVisualStamp? CiVisualStamp()
    {
        if (leavingScene || !GodotObject.IsInstanceValid(this) || !IsInsideTree() || !IsVisibleInTree() ||
            controller is null || ciSession is null || !ciSession.IsRealNativeSession) return null;
        ProductHotseatUiState state = controller.State;
        CiObserveSafeFrame();
        string? name = state.Mode switch
        {
            ProductHotseatUiMode.Covered when privacy.IsCovering => "covered",
            ProductHotseatUiMode.MulliganSelecting when dock.Mulligan.IsVisibleInTree() => "mulligan",
            ProductHotseatUiMode.MulliganReview when dock.Mulligan.IsVisibleInTree() => "mulligan-review",
            ProductHotseatUiMode.Action => state.Interaction.Step switch
            {
                ProductHotseatSelectionStep.None or ProductHotseatSelectionStep.ChooseSource => "action",
                ProductHotseatSelectionStep.ChooseAction => "source-selection",
                ProductHotseatSelectionStep.ChooseMode => "mode-selection",
                ProductHotseatSelectionStep.ChooseAdditionalCost => "additional-cost",
                ProductHotseatSelectionStep.ChooseSlot => "slot-selection",
                ProductHotseatSelectionStep.ChooseTarget => "target-selection",
                ProductHotseatSelectionStep.ChooseAdvance => "advance-selection",
                _ => null,
            },
            ProductHotseatUiMode.Choice when direct.IsVisibleInTree() && state.PendingChoice.RequiresInput => "choice",
            ProductHotseatUiMode.Reaction when state.Interaction.Step == ProductHotseatSelectionStep.ChooseTarget => "reaction-target",
            ProductHotseatUiMode.Reaction when direct.IsVisibleInTree() => "reaction",
            ProductHotseatUiMode.Resolving when resolving.IsVisibleInTree() => "resolving",
            ProductHotseatUiMode.Finished when result.IsVisibleInTree() => "finished",
            ProductHotseatUiMode.Faulted when error.IsVisibleInTree() => "error",
            _ => null,
        };
        if (name is null) return null;
        int? viewer = state.Viewer is { } visibleViewer ? (int)visibleViewer : null;
        // The initial cover deliberately has no viewer read from which to know
        // a revision. Do not fetch a snapshot merely to fill report metadata.
        ulong? revision = state.Snapshot?.Revision ?? state.PublicBoard?.Revision ??
            (ciSession.Revision == 0 ? null : ciSession.Revision);
        Vector2I pixels = GetWindow().Size;
        return new ProductVisualStamp(name, viewer, revision, pixels.X, pixels.Y);
    }
}
