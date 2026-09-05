// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Text.Json;
using Godot;
using Scgs.GodotClient.Battlefield;
using Scgs.Hotseat.Product;
using V05 = Scgs.Client.V05;

namespace Scgs.GodotClient.Match;

public sealed partial class ProductMatchScreen
{
    private const int CardFramePerformanceWarmupFrames = 300;
    private const int CardFramePerformanceMeasuredFrames = 300;
    private const int CardFramePerformanceTimeoutSeconds = 120;
    private CardFramePerformanceRun? cardFramePerformanceRun;
    private string cardFramePerformanceResult = "{\"available\":false,\"reason\":\"not_sampled\"}";
    private ulong cardFramePerformanceGeneration, cardFramePerformanceRevision;
    private V05.PlayerId? cardFramePerformanceViewer;
    private readonly HashSet<ulong> cardFramePerformanceMaterials = [];
    private readonly HashSet<ulong> cardFramePerformanceTextures = [];
    private static readonly BaseMaterial3D.TextureParam[] CardFrameTextureChannels =
        Enum.GetValues<BaseMaterial3D.TextureParam>().Where(channel => channel != BaseMaterial3D.TextureParam.Max).ToArray();
    private static readonly JsonSerializerOptions CardFramePerformanceJson = new() {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true,
    };

    /// <summary>
    /// Observe the real, already revealed R1 Action scene. No native call, submit,
    /// artificial board setup, input, VSync change, or viewer reveal is performed.
    /// Poll ReviewCardFramePerformanceResult after approximately 600 real draws.
    /// </summary>
    public string ReviewStartCardFramePerformanceCapture()
    {
        if (!FrameReviewIsRevealed() || cardFramePerformanceBusy || cardFrameCaptureBusy || cardFramePoolProbeBusy ||
            cardFrameSyntheticHost is not null)
            return "{\"accepted\":false,\"reason\":\"revealed_idle_r1_action_without_other_probes_required\"}";
        if (DisplayServer.GetName().Equals("headless", StringComparison.OrdinalIgnoreCase))
            return "{\"accepted\":false,\"reason\":\"display_backed_gpu_required\"}";
        Camera3D? camera = GetViewport().GetCamera3D();
        if (camera is null) return "{\"accepted\":false,\"reason\":\"camera_required\"}";
        Vector2I actualImageSize;
        try { actualImageSize = ReadCardFrameActualGpuImageSize(); }
        catch (Exception) { return "{\"accepted\":false,\"reason\":\"actual_gpu_image_unavailable\"}"; }

        var state = controller!.State;
        string id = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8];
        cardFramePerformanceGeneration = sessionGeneration;
        cardFramePerformanceRevision = state.Snapshot!.Revision;
        cardFramePerformanceViewer = state.Viewer;
        cardFramePerformanceRun = new CardFramePerformanceRun {
            Id = id, Controller = controller, Camera = camera, CameraTransform = camera.GlobalTransform,
            WindowSize = GetWindow().Size, ImageSize = actualImageSize,
            TextureApiSize = CardFramePerformanceTextureApiSize(),
            LogicalViewportSize = GetViewport().GetVisibleRect().Size,
            Vsync = DisplayServer.WindowGetVsyncMode(), FpsLimit = Engine.MaxFps,
            Environment = FrameEnvironment(),
            MainBoardCounts = state.Snapshot.Players.Select(player => player.MainBoard.Count(card => card is not null)).ToArray(),
            Initial = ReadCardFramePerformanceResources(), Started = Stopwatch.GetTimestamp(),
        };
        cardFramePerformanceBusy = true;
        cardFramePerformanceResult = JsonSerializer.Serialize(new { available = true, accepted = true,
            status = "sampling", capture_id = id, warmup_frames = 300, measured_frames = 300,
            session_calls = 0, changes_vsync_or_fps_limit = false });
        RenderingServer.FramePostDraw += ObserveCardFramePerformanceDraw;
        TreeExiting += CardFramePerformanceOwnerExiting;
        _ = CardFramePerformanceDeadlineAsync(id, cardFramePerformanceRun.Deadline.Token);
        return cardFramePerformanceResult;
    }

    public string ReviewCardFramePerformanceResult()
    {
        if (!FrameReviewIsRevealed() || sessionGeneration != cardFramePerformanceGeneration ||
            controller!.State.Snapshot!.Revision != cardFramePerformanceRevision ||
            controller.State.Viewer != cardFramePerformanceViewer)
            return "{\"available\":false,\"reason\":\"performance_viewer_or_revision_not_current\"}";
        if (cardFramePerformanceRun is { } run)
            return JsonSerializer.Serialize(new { available = true, status = "sampling", capture_id = run.Id,
                completed_frame_post_draws = run.Draws, measured_frames = run.Measured });
        return cardFramePerformanceResult;
    }

    public string ReviewCancelCardFramePerformanceCapture()
    {
        if (!cardFramePerformanceBusy) return "{\"cancelled\":false,\"reason\":\"not_sampling\"}";
        CompleteCardFramePerformance("cancelled", "explicit_probe_cancel");
        return "{\"cancelled\":true,\"commands_submitted\":0}";
    }

    private async Task CardFramePerformanceDeadlineAsync(string id, CancellationToken cancellation)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(CardFramePerformanceTimeoutSeconds), cancellation); }
        catch (OperationCanceledException) { return; }
        // The Godot synchronization context resumes this callback on its main
        // thread. Never touch the node after it exited/freed or a newer capture.
        if (GodotObject.IsInstanceValid(this) && cardFramePerformanceRun?.Id == id)
            CompleteCardFramePerformance("aborted", "120_second_real_draw_deadline");
    }

    private void CardFramePerformanceOwnerExiting() =>
        CompleteCardFramePerformance("aborted", "owner_left_tree", save: false);

    private void ObserveCardFramePerformanceDraw()
    {
        if (cardFramePerformanceRun is not { } run) return;
        long now = Stopwatch.GetTimestamp(); // Scan overhead belongs to the next actual interval.
        try
        {
            if (!CardFramePerformanceContextCurrent(run))
            {
                CompleteCardFramePerformance("aborted", "viewer_revision_scene_camera_or_probe_changed");
                return;
            }
            int frame = Engine.GetFramesDrawn();
            if (run.Draws > 0 && frame != run.PreviousDraw + 1)
            {
                CompleteCardFramePerformance("aborted", "nonconsecutive_frame_post_draw");
                return;
            }
            CardFrameSceneResourceCounts counts = ReadCardFramePerformanceResources();
            run.AllPeak = MaxCounts(run.AllPeak, counts);
            ++run.Draws;
            if (run.Draws == CardFramePerformanceWarmupFrames)
            {
                run.Before = counts;
                run.MeasurementPeak = counts;
            }
            else if (run.Draws > CardFramePerformanceWarmupFrames)
            {
                double interval = Stopwatch.GetElapsedTime(run.PreviousTimestamp, now).TotalMilliseconds;
                if (!double.IsFinite(interval) || interval <= 0)
                {
                    CompleteCardFramePerformance("aborted", "invalid_monotonic_frame_interval");
                    return;
                }
                run.Intervals[run.Measured++] = interval;
                run.After = counts;
                run.MeasurementPeak = MaxCounts(run.MeasurementPeak, counts);
            }
            run.PreviousDraw = frame;
            run.PreviousTimestamp = now;
            if (run.Measured == CardFramePerformanceMeasuredFrames)
                CompleteCardFramePerformance("measured", null);
        }
        catch (Exception)
        {
            // Reports contain controlled failure codes, not arbitrary private
            // DTO/string/exception content from the scene being inspected.
            CompleteCardFramePerformance("aborted", "resource_observer_failed");
        }
    }

    private bool CardFramePerformanceContextCurrent(CardFramePerformanceRun run) =>
        FrameReviewIsRevealed() && !cardFrameCaptureBusy && !cardFramePoolProbeBusy && cardFrameSyntheticHost is null &&
        ReferenceEquals(controller, run.Controller) && sessionGeneration == cardFramePerformanceGeneration &&
        controller!.State.Snapshot!.Revision == cardFramePerformanceRevision && controller.State.Viewer == cardFramePerformanceViewer &&
        GetWindow().Size == run.WindowSize && CardFramePerformanceTextureApiSize() == run.TextureApiSize &&
        GetViewport().GetVisibleRect().Size == run.LogicalViewportSize &&
        GodotObject.IsInstanceValid(run.Camera) && run.Camera.IsInsideTree() &&
        run.Camera.GlobalTransform.IsEqualApprox(run.CameraTransform) &&
        DisplayServer.WindowGetVsyncMode() == run.Vsync && Engine.MaxFps == run.FpsLimit;

    private Vector2I CardFramePerformanceTextureApiSize() =>
        new(GetViewport().GetTexture().GetWidth(), GetViewport().GetTexture().GetHeight());

    private Vector2I ReadCardFrameActualGpuImageSize()
    {
        // ViewportTexture.GetWidth/GetHeight may disagree with GetImage under
        // canvas_items stretch. Read back only before warmup and after timing,
        // never multiply screen scale or stall every measured frame for pixels.
        using Image image = GetViewport().GetTexture().GetImage();
        if (image.IsEmpty()) throw new InvalidOperationException("actual_gpu_image_empty");
        return image.GetSize();
    }

    private void CompleteCardFramePerformance(string status, string? reason, bool save = true)
    {
        if (cardFramePerformanceRun is not { } run) return;
        RenderingServer.FramePostDraw -= ObserveCardFramePerformanceDraw;
        TreeExiting -= CardFramePerformanceOwnerExiting;
        cardFramePerformanceRun = null;
        cardFramePerformanceBusy = false;
        run.Deadline.Cancel();
        run.Deadline.Dispose();
        try
        {
            Vector2I? finalImageSize = null;
            if (status == "measured")
            {
                try
                {
                    finalImageSize = ReadCardFrameActualGpuImageSize();
                    if (finalImageSize.Value != run.ImageSize)
                    {
                        status = "aborted";
                        reason = "actual_gpu_image_dimensions_changed";
                    }
                }
                catch (Exception) { status = "aborted"; reason = "actual_gpu_image_unavailable_after_measurement"; }
            }
            bool complete = status == "measured" && run.Draws == 600 && run.Measured == 300;
            double[] samples = run.Intervals.Take(run.Measured).ToArray();
            double[] sorted = samples.OrderBy(value => value).ToArray();
            double? p95 = sorted.Length == 0 ? null : sorted[(int)Math.Ceiling(sorted.Length * .95) - 1];
            double? maximum = sorted.Length == 0 ? null : sorted[^1];
            bool sceneNoGrowth = complete && NoSceneGrowth(run.MeasurementPeak, run.Before);
            string? path = null;
            if (save && GodotObject.IsInstanceValid(this) && IsInsideTree())
            {
                string directory = ProjectSettings.GlobalizePath("user://review-evidence/card-frame-r1-performance/" + run.Id);
                Directory.CreateDirectory(directory);
                path = Path.Combine(directory, "performance.json");
            }
            var report = new {
                schema_version = 1, measurement_revision = 2,
                suite = "card-frame-r1-current-real-scene-performance", available = true,
                status, reason, capture_id = run.Id, synthetic = false, completed = complete,
                scene_scope = "current_revealed_action_revision_not_a_maximum_board_claim",
                maximum_board_or_full_animation_suite_verified = false,
                synthetic_rebind_pool_probe_performed = false,
                main_board_counts = run.MainBoardCounts, environment = run.Environment,
                vsync = run.Vsync.ToString(), fps_limit = run.FpsLimit,
                os_driver_package_version = (string?)null,
                driver_note = "Renderer driver/API version is in environment; OS driver package version is not inferred.",
                window_size = new[] { run.WindowSize.X, run.WindowSize.Y },
                rendered_image_size = new[] { run.ImageSize.X, run.ImageSize.Y },
                rendered_image_size_after = finalImageSize is { } finalSize ? new[] { finalSize.X, finalSize.Y } : null,
                rendered_image_size_source = "ViewportTexture.GetImage().GetSize()_before_warmup_and_after_measurement",
                actual_gpu_image_dimensions_verified = complete && finalImageSize == run.ImageSize,
                raw_texture_api_size = new[] { run.TextureApiSize.X, run.TextureApiSize.Y },
                gpu_readbacks_inside_timed_frames = 0,
                logical_viewport_size = new[] { run.LogicalViewportSize.X, run.LogicalViewportSize.Y },
                revision = cardFramePerformanceRevision, viewer = (int?)cardFramePerformanceViewer,
                timing_source = "consecutive_FramePostDraw_Stopwatch_monotonic_clock",
                warmup_frames = Math.Min(run.Draws, 300), measured_frames = run.Measured,
                completed_frame_post_draws = run.Draws, requested_frame_post_draws = 600,
                total_elapsed_ms = Stopwatch.GetElapsedTime(run.Started).TotalMilliseconds,
                p95_ms = p95, max_ms = maximum, frame_intervals_ms = samples,
                reference_budget = new { p95_ms = 33.3, max_ms_exclusive = 100 },
                current_capped_scene_frame_budget_met = complete && p95 <= 33.3 && maximum < 100,
                initial = run.Initial, before = run.Before, after = run.After,
                measurement_peak = run.MeasurementPeak, warmup_and_measurement_peak = run.AllPeak,
                scene_bound_reference_no_growth = sceneNoGrowth,
                global_resource_no_growth = complete && run.MeasurementPeak.ObjectResources <= run.Before.ObjectResources,
                global_resource_delta = run.After.ObjectResources - run.Before.ObjectResources,
                resource_scope = "unique_scene_bound_material_and_texture_references_plus_all_CardActor3D_and_SlotActor3D_nodes",
                texture_scope = "Texture2D references on geometry materials/all_BaseMaterial3D_channels/shader_uniforms/sprites/TextureRects; not GPU allocations, editor caches or Environment/Sky resources",
                global_resource_note = "Godot ObjectResourceCount includes unrelated editor/global resources; growth or collection is reported, not hidden or treated as a scene leak proof.",
                resource_scan_overhead_included = true, overall_performance_or_visual_approval = false,
                setting_changes = 0, session_calls = 0, commands_submitted = 0,
                event_acknowledgements = 0, gate_reveals = 0, saved_report = path,
            };
            cardFramePerformanceResult = JsonSerializer.Serialize(report, CardFramePerformanceJson);
            if (path is not null) File.WriteAllText(path, cardFramePerformanceResult);
        }
        catch (Exception)
        {
            cardFramePerformanceResult = "{\"available\":true,\"status\":\"aborted\",\"reason\":\"report_write_failed\"}";
        }
        finally
        {
            cardFramePerformanceMaterials.Clear();
            cardFramePerformanceTextures.Clear();
            Array.Clear(run.Intervals);
        }
    }

    private CardFrameSceneResourceCounts ReadCardFramePerformanceResources()
    {
        cardFramePerformanceMaterials.Clear(); cardFramePerformanceTextures.Clear();
        int cards = 0, slots = 0, visible = 0;
        Visit(this);
        return new(cards, slots, visible, cardFramePerformanceMaterials.Count, cardFramePerformanceTextures.Count,
            (long)Godot.Performance.GetMonitor(Godot.Performance.Monitor.ObjectResourceCount));

        void Visit(Node node)
        {
            if (node is CardActor3D card) { ++cards; if (card.IsVisibleInTree()) ++visible; }
            if (node is SlotActor3D) ++slots;
            if (node is GeometryInstance3D geometry) { Material(geometry.MaterialOverride); Material(geometry.MaterialOverlay); }
            if (node is MeshInstance3D mesh && mesh.Mesh is { } source)
                for (int surface = 0; surface < source.GetSurfaceCount(); ++surface)
                { Material(source.SurfaceGetMaterial(surface)); Material(mesh.GetSurfaceOverrideMaterial(surface)); }
            if (node is CanvasItem canvas) Material(canvas.Material);
            if (node is TextureRect rect) Texture(rect.Texture);
            if (node is Sprite2D sprite) Texture(sprite.Texture);
            if (node is Sprite3D sprite3D) Texture(sprite3D.Texture);
            foreach (Node child in node.GetChildren()) Visit(child);
        }
        void Material(Material? material)
        {
            if (material is null || !cardFramePerformanceMaterials.Add(material.GetInstanceId())) return;
            Material(material.NextPass);
            if (material is BaseMaterial3D standard)
                foreach (BaseMaterial3D.TextureParam channel in CardFrameTextureChannels) Texture(standard.GetTexture(channel));
            if (material is ShaderMaterial shader && shader.Shader is { } source)
                foreach (Godot.Collections.Dictionary uniform in source.GetShaderUniformList())
                {
                    Variant value = shader.GetShaderParameter(uniform["name"].AsStringName());
                    if (value.VariantType == Variant.Type.Object && value.AsGodotObject() is Texture2D texture) Texture(texture);
                }
        }
        void Texture(Texture2D? texture)
        { if (texture is not null) cardFramePerformanceTextures.Add(texture.GetInstanceId()); }
    }

    private static CardFrameSceneResourceCounts MaxCounts(CardFrameSceneResourceCounts a, CardFrameSceneResourceCounts b) =>
        new(Math.Max(a.Cards,b.Cards), Math.Max(a.Slots,b.Slots), Math.Max(a.VisibleCards,b.VisibleCards),
            Math.Max(a.Materials,b.Materials), Math.Max(a.Textures,b.Textures), Math.Max(a.ObjectResources,b.ObjectResources));
    private static bool NoSceneGrowth(CardFrameSceneResourceCounts a, CardFrameSceneResourceCounts b) =>
        a.Cards <= b.Cards && a.Slots <= b.Slots && a.Materials <= b.Materials && a.Textures <= b.Textures;
    private readonly record struct CardFrameSceneResourceCounts(int Cards, int Slots, int VisibleCards,
        int Materials, int Textures, long ObjectResources);

    private sealed class CardFramePerformanceRun
    {
        internal required string Id;
        internal required ProductHotseatMatchController Controller;
        internal required Camera3D Camera;
        internal Transform3D CameraTransform;
        internal Vector2I WindowSize, ImageSize, TextureApiSize;
        internal Vector2 LogicalViewportSize;
        internal DisplayServer.VSyncMode Vsync;
        internal int FpsLimit;
        internal required object Environment;
        internal required int[] MainBoardCounts;
        internal long Started, PreviousTimestamp;
        internal int Draws, PreviousDraw, Measured;
        internal readonly double[] Intervals = new double[CardFramePerformanceMeasuredFrames];
        internal readonly CancellationTokenSource Deadline = new();
        internal CardFrameSceneResourceCounts Initial, Before, After, AllPeak, MeasurementPeak;
    }
}
