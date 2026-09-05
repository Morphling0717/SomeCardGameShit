// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using Scgs.GodotClient.Battlefield;
using Scgs.Hotseat.Product;

namespace Scgs.GodotClient.PresentationV2;

internal sealed partial class ProductPresentationDirector
{
    private const int MaximumPerformanceHistory = 32;
    private static readonly Queue<PerformanceRecording> PerformanceHistory = new();
    private static readonly HashSet<string> SeenPerformanceWorkloads = new(StringComparer.Ordinal);
    private static readonly Queue<string> SeenPerformanceWorkloadOrder = new();
    private static ulong NextPerformanceRecordingId;
    private readonly HashSet<ulong> _performanceMaterialIds = [];
    private readonly HashSet<ulong> _performanceTextureIds = [];
    private PerformanceRecording? _performanceRecording;
    private bool _performanceConnected;
    private bool _insideTimedAnimation;

    /// <summary>Read-only, bounded history of actual review animations; no native access.</summary>
    public string CiInspectPresentationPerformance()
    {
        if (!BattlePresentationReviewRuntime.Enabled && !CardFrameReviewRuntime.Enabled)
            return "{\"available\":false,\"reason\":\"review_entry_required\"}";
        return JsonSerializer.Serialize(new
        {
            schema_version = 1,
            suite = "battle-presentation-v2-real-dynamic-performance",
            available = true,
            timing_source = "consecutive-animation-active-frame-post-draw-monotonic-clock",
            resource_scope = "bound-scene-materials-and-textures-plus-global-godot-resource-count",
            resource_scan_overhead_included = true,
            cleanup_scope = "live-scene-may-have-restored-view; motion-cleanliness-is-comparable; whole-scene-counts-are-not-an-automatic-leak-test",
            first_use_is_not_static_warmup = true,
            numeric_budget_alone_does_not_validate_cancelled_skipped_or_incomplete_playback = true,
            static_warmup_frames_in_dynamic_samples = 0,
            fabricated_or_padded_frames = 0,
            maximum_intervals_per_playback = ProductPresentationPerformanceWindow.MaximumIntervals,
            maximum_history = MaximumPerformanceHistory,
            no_automatic_overall_pass = true,
            recordings = PerformanceHistory.Select(recording => new
            {
                recording.Environment,
                evidence = recording.Window.Report(),
            }).ToArray(),
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
    }

    private void BeginPerformanceRecording(ProductPresentationBatch batch, bool reduceMotion)
    {
        if (!BattlePresentationReviewRuntime.Enabled && !CardFrameReviewRuntime.Enabled) return;
        if (_performanceRecording?.Window.AwaitingDeferredCleanup == true)
            _performanceRecording.Window.AbandonDeferredCleanup("superseded_before_two_drawn_frames");
        if (!_performanceConnected)
        {
            RenderingServer.FramePostDraw += ObservePresentationDraw;
            _performanceConnected = true;
        }
        Vector2I size = GetWindow().Size;
        string signature = $"{size.X}x{size.Y}|reduce={reduceMotion}|" + string.Join(";",
            batch.Observations.Select(item => $"{item.Observation.Kind}:{item.Observation.DeclarationKind}:" +
                $"{item.Observation.Subject?.DesignId}:{item.Observation.Source?.DesignId}"));
        string workload = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature))).ToLowerInvariant();
        bool firstUse = SeenPerformanceWorkloads.Add(workload);
        if (firstUse)
        {
            SeenPerformanceWorkloadOrder.Enqueue(workload);
            if (SeenPerformanceWorkloadOrder.Count > MaximumPerformanceHistory * 2)
                SeenPerformanceWorkloads.Remove(SeenPerformanceWorkloadOrder.Dequeue());
        }
        string adapter = RenderingServer.GetVideoAdapterName();
        string lowerAdapter = adapter.ToLowerInvariant();
        var environment = new PerformanceEnvironment(
            size.X, size.Y, reduceMotion, Engine.MaxFps,
            DisplayServer.WindowGetVsyncMode().ToString(),
            adapter, RenderingServer.GetVideoAdapterVendor(), RenderingServer.GetCurrentRenderingMethod(), DisplayServer.GetName(),
            lowerAdapter.Contains("basic render", StringComparison.Ordinal) ||
                lowerAdapter.Contains("llvmpipe", StringComparison.Ordinal) ||
                lowerAdapter.Contains("swiftshader", StringComparison.Ordinal) ||
                lowerAdapter.Contains("warp", StringComparison.Ordinal),
            batch.Revision, batch.Observations.Count);
        var window = new ProductPresentationPerformanceWindow(
            ++NextPerformanceRecordingId, workload, firstUse, ClockMilliseconds(), ReadPerformanceResources());
        _performanceRecording = new PerformanceRecording(environment, window);
        PerformanceHistory.Enqueue(_performanceRecording);
        if (PerformanceHistory.Count > MaximumPerformanceHistory) PerformanceHistory.Dequeue();
    }

    private void EndPerformanceRecording(string reason)
    {
        _insideTimedAnimation = false;
        if (_performanceRecording is not { } recording || recording.Window.IsComplete) return;
        recording.Window.Complete(reason, (ulong)Engine.GetFramesDrawn(), ReadPerformanceResources());
    }

    private void ObservePresentationDraw()
    {
        if (_performanceRecording is not { } recording || !IsInsideTree() ||
            recording.Window.IsComplete && !recording.Window.AwaitingDeferredCleanup) return;
        // Timestamp is captured before this evidence scan. Scanning cost is
        // deliberately included in the next real frame's measured interval.
        double time = ClockMilliseconds();
        recording.Window.ObserveFrame((ulong)Engine.GetFramesDrawn(), time,
            IsPlaying && _insideTimedAnimation, ReadPerformanceResources());
    }

    private void DisconnectPerformanceObserver()
    {
        if (_performanceConnected)
        {
            RenderingServer.FramePostDraw -= ObservePresentationDraw;
            _performanceConnected = false;
        }
        _performanceRecording?.Window.AbandonDeferredCleanup("owner_left_tree_before_two_drawn_frames");
        _performanceRecording = null;
        _performanceMaterialIds.Clear();
        _performanceTextureIds.Clear();
    }

    private static double ClockMilliseconds() => Time.GetTicksUsec() / 1000.0;

    private ProductPresentationResourceCounts ReadPerformanceResources()
    {
        _performanceMaterialIds.Clear();
        _performanceTextureIds.Clear();
        int cards = 0, motion = 0, identities = 0, visible = 0, collisions = 0;
        Node root = GetParent() ?? this;
        Visit(root);
        bool cutinBound = _cutinPortrait is not null && GodotObject.IsInstanceValid(_cutinPortrait) && _cutinPortrait.Texture is not null;
        bool cutinVisible = _cutinRoot is not null && GodotObject.IsInstanceValid(_cutinRoot) && _cutinRoot.IsVisibleInTree();
        return new ProductPresentationResourceCounts(cards, motion, identities, visible, collisions,
            _performanceMaterialIds.Count, _performanceTextureIds.Count,
            (long)Godot.Performance.GetMonitor(Godot.Performance.Monitor.ObjectResourceCount), cutinBound, cutinVisible);

        void Visit(Node node)
        {
            if (node is CardActor3D card)
            {
                ++cards;
                if (card.Name.ToString().StartsWith("PublicMotion", StringComparison.Ordinal))
                {
                    ++motion;
                    if (card.CiProductFace is not null) ++identities;
                    if (card.IsVisibleInTree()) ++visible;
                    if (card.CollisionLayer != 0 || card.Surface is not null) ++collisions;
                }
            }
            if (node is GeometryInstance3D geometry)
            {
                AddMaterial(geometry.MaterialOverride);
                AddMaterial(geometry.MaterialOverlay);
            }
            if (node is MeshInstance3D mesh && mesh.Mesh is { } source)
                for (int surface = 0; surface < source.GetSurfaceCount(); ++surface)
                {
                    AddMaterial(source.SurfaceGetMaterial(surface));
                    AddMaterial(mesh.GetSurfaceOverrideMaterial(surface));
                }
            if (node is CanvasItem canvas) AddMaterial(canvas.Material);
            if (node is Sprite2D sprite) AddTexture(sprite.Texture);
            if (node is Sprite3D sprite3D) AddTexture(sprite3D.Texture);
            if (node is TextureRect textureRect) AddTexture(textureRect.Texture);
            foreach (Node child in node.GetChildren()) Visit(child);
        }
        void AddMaterial(Material? material)
        {
            if (material is null || !_performanceMaterialIds.Add(material.GetInstanceId())) return;
            AddMaterial(material.NextPass);
            if (material is BaseMaterial3D standard)
            {
                AddTexture(standard.AlbedoTexture);
                AddTexture(standard.NormalTexture);
                AddTexture(standard.EmissionTexture);
                AddTexture(standard.OrmTexture);
            }
            if (material is ShaderMaterial shader && shader.Shader is { } source)
                foreach (Godot.Collections.Dictionary uniform in source.GetShaderUniformList())
                {
                    Variant value = shader.GetShaderParameter(uniform["name"].AsStringName());
                    if (value.VariantType == Variant.Type.Object && value.AsGodotObject() is Texture2D texture)
                        AddTexture(texture);
                }
        }
        void AddTexture(Texture2D? texture)
        {
            if (texture is not null) _performanceTextureIds.Add(texture.GetInstanceId());
        }
    }

    private sealed record PerformanceEnvironment(
        int Width,
        int Height,
        bool ReduceMotion,
        int FpsLimit,
        string Vsync,
        string Adapter,
        string AdapterVendor,
        string RenderingMethod,
        string DisplayBackend,
        bool SoftwareAdapterNameDetected,
        ulong Revision,
        int RequestedObservations);

    private sealed record PerformanceRecording(
        PerformanceEnvironment Environment,
        ProductPresentationPerformanceWindow Window);
}
