// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using Godot;
using Scgs.Hotseat.Product;
using V05 = Scgs.Client.V05;

namespace Scgs.GodotClient.Match;

public sealed partial class ProductMatchScreen
{
    private CardFrameTurntableHost? cardFrameTurntable;
    private ProductHotseatMatchController? cardFrameTurntableController;
    private EventHandler<ProductHotseatStateChangedEventArgs>? cardFrameTurntableStateHandler;
    private ulong cardFrameTurntableGeneration, cardFrameTurntableRevision;
    private V05.PlayerId? cardFrameTurntableViewer;
    private Vector2I cardFrameTurntableWindowSize;
    private Window.ModeEnum cardFrameTurntableWindowMode;
    private bool cardFrameTurntablePreviousInput;
    private string cardFrameTurntableResult = "{\"available\":false,\"reason\":\"not_recorded\"}";

    /// <summary>
    /// Explicit public-design turntable, not a match recording or an animation
    /// upgrade. Requires an already revealed idle review, never opens that gate.
    /// </summary>
    public string ReviewStartCardFrameTurntable(string designId)
    {
        if (!FrameReviewIsRevealed() || cardFrameCaptureBusy || cardFramePerformanceBusy ||
            cardFramePoolProbeBusy || cardFrameSyntheticHost is not null || cardFrameTurntable is not null)
            return "{\"accepted\":false,\"reason\":\"revealed_idle_r1_without_other_probes_required\"}";
        if (!CardFrameTurntableHost.Supports(designId))
            return "{\"accepted\":false,\"reason\":\"representative_design_required\"}";
        if (DisplayServer.GetName().Equals("headless", StringComparison.OrdinalIgnoreCase))
            return "{\"accepted\":false,\"reason\":\"display_backed_gpu_required\"}";
        cardFrameTurntableGeneration = sessionGeneration;
        cardFrameTurntableRevision = controller!.State.Snapshot!.Revision;
        cardFrameTurntableViewer = controller.State.Viewer;
        cardFrameTurntableController = controller;
        cardFrameTurntableWindowSize = GetWindow().Size;
        cardFrameTurntableWindowMode = GetWindow().Mode;
        cardFrameTurntablePreviousInput = battlefield.InputEnabled;
        // Every existing capture/performance/synthetic entry checks this lock.
        // We own it only after checking no capture is in flight.
        cardFrameCaptureBusy = true;
        battlefield.SetInputEnabled(false);
        try
        {
            cardFrameTurntable = new CardFrameTurntableHost { Name = "ExplicitCardFrameDesignTurntable" };
            cardFrameTurntable.Configure(designId, CardFrameTurntableIsCurrent,
                result => { cardFrameTurntableResult = result; CloseCardFrameTurntable("completed"); },
                () => CloseCardFrameTurntable("explicit_cancel"));
            cardFrameTurntableStateHandler = (_, _) => CloseCardFrameTurntable("review_state_changed");
            controller.StateChanged += cardFrameTurntableStateHandler;
            TreeExiting += CardFrameTurntableOwnerExiting;
            cardFrameTurntableResult = JsonSerializer.Serialize(new {
                available = true, accepted = true, status = "recording", design_id = designId,
                design_display = true, gameplay_recording = false, target_seconds = 6, maximum_frames = 180,
                session_calls = 0, commands_submitted = 0, gate_reveals = 0,
            });
            AddChild(cardFrameTurntable);
            return cardFrameTurntableResult;
        }
        catch (Exception)
        {
            CloseCardFrameTurntable("setup_failed");
            return cardFrameTurntableResult;
        }
    }

    public string ReviewCardFrameTurntableResult() => CardFrameTurntableIsCurrent()
        ? cardFrameTurntable?.Describe() ?? cardFrameTurntableResult
        : "{\"available\":false,\"reason\":\"original_revealed_review_context_required\"}";

    public string ReviewCancelCardFrameTurntable()
    {
        bool existed = cardFrameTurntable is not null;
        CloseCardFrameTurntable("explicit_cancel");
        return JsonSerializer.Serialize(new { cancelled = existed, commands_submitted = 0 });
    }

    private bool CardFrameTurntableIsCurrent() => FrameReviewIsRevealed() &&
        sessionGeneration == cardFrameTurntableGeneration &&
        controller!.State.Snapshot!.Revision == cardFrameTurntableRevision &&
        controller.State.Viewer == cardFrameTurntableViewer &&
        GetWindow().Size == cardFrameTurntableWindowSize && GetWindow().Mode == cardFrameTurntableWindowMode;

    private void CardFrameTurntableOwnerExiting() => CloseCardFrameTurntable("owner_left_tree");

    private void CloseCardFrameTurntable(string reason)
    {
        if (cardFrameTurntable is null) return;
        // A resize cancels capture, but must not strand the same revealed game
        // with disabled input. A new viewer/revision/mode still owns its lock.
        bool restore = FrameReviewIsRevealed() && sessionGeneration == cardFrameTurntableGeneration &&
            controller!.State.Snapshot!.Revision == cardFrameTurntableRevision &&
            controller.State.Viewer == cardFrameTurntableViewer && cardFrameTurntablePreviousInput;
        if (cardFrameTurntableController is not null && cardFrameTurntableStateHandler is not null)
            cardFrameTurntableController.StateChanged -= cardFrameTurntableStateHandler;
        TreeExiting -= CardFrameTurntableOwnerExiting;
        cardFrameTurntableStateHandler = null;
        cardFrameTurntableController = null;
        CardFrameTurntableHost host = cardFrameTurntable;
        cardFrameTurntable = null;
        host.Close();
        cardFrameCaptureBusy = false;
        if (reason != "completed") cardFrameTurntableResult = JsonSerializer.Serialize(new {
            available = true, status = "aborted", reason, design_display = true, gameplay_recording = false,
        });
        if (restore) battlefield.SetInputEnabled(true);
    }
}
