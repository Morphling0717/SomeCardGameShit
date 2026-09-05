// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using Scgs.GodotClient.Battlefield;

namespace Scgs.GodotClient.Ci;

/// <summary>Identity-free description of an already rendered product UI. Never holds a DTO.</summary>
internal sealed record ProductVisualStamp(string State, int? Viewer, ulong? Revision, int Width, int Height);

/// <summary>
/// Captures only the current, explicitly revealed viewer. It does not create a
/// match, query native, choose a viewer, populate a board or inject private data.
/// One instance belongs to the whole smoke run, including real menu/restarts.
/// </summary>
internal sealed class ProductVisualCapture
{
    internal static readonly string[] RequiredStates =
        ["menu", "setup", "covered", "mulligan", "action", "choice", "reaction", "resolving", "finished"];
    internal const int MinimumHeavyBoardCards = 8;
    internal const int MinimumHeavyBoardCardsPerPlayer = 3;
    private const int MaximumStableAttempts = 120;
    private readonly Node host;
    private readonly string directory;
    private readonly bool requirePerformance;
    private readonly Dictionary<string, ProductVisualImage> captures = new(StringComparer.Ordinal);
    private Task? pendingCapture;
    private ProductPerformanceEvidence? performance;
    private bool performanceRunning;
    private bool completed;

    internal ProductVisualCapture(Node host, string absoluteDirectory, bool requirePerformance)
    {
        if (!Path.IsPathFullyQualified(absoluteDirectory))
            throw new ArgumentException("Product captures require an explicit absolute output directory.");
        if (DisplayServer.GetName() == "headless")
            throw new InvalidOperationException("Product visual evidence requires a display-backed viewport.");
        this.host = host;
        directory = Path.GetFullPath(absoluteDirectory);
        this.requirePerformance = requirePerformance;
        Directory.CreateDirectory(directory);
        WriteManifest(success: false);
    }

    internal bool PerformanceCompleted => performance?.Success == true;
    internal bool RequiresPerformance => requirePerformance;
    internal bool PerformanceAcceptanceSatisfied => !requirePerformance || PerformanceCompleted;

    internal Task CaptureShellAsync(string state, Func<bool> stillCurrent)
    {
        if (state is not ("menu" or "setup")) throw new ArgumentException("Not a product shell state.");
        return CaptureAsync(() =>
        {
            if (!stillCurrent()) return null;
            // In canvas_items stretch, ViewportTexture's dimension accessors
            // can be canvas-scaled (1280 window -> 1024 texture API), while
            // GetImage returns the real 1280 GPU framebuffer. Never resample it.
            Vector2I pixels = host.GetWindow().Size;
            return new ProductVisualStamp(state, null, null, pixels.X, pixels.Y);
        });
    }

    internal async Task CaptureAsync(Func<ProductVisualStamp?> observe)
    {
        if (completed) return;
        if (pendingCapture is not null) await pendingCapture;
        ProductVisualStamp? expected = observe();
        if (expected is null || captures.ContainsKey(expected.State)) return;
        Task work = CaptureCoreAsync(expected, observe);
        pendingCapture = work;
        try { await work; }
        finally { if (ReferenceEquals(pendingCapture, work)) pendingCapture = null; }
    }

    private async Task CaptureCoreAsync(ProductVisualStamp expected, Func<ProductVisualStamp?> observe)
    {
        Image? previous = null;
        int previousFrame = 0;
        string lastDimensions = "No GPU frame was received.";
        try
        {
            for (int attempt = 0; attempt < MaximumStableAttempts; ++attempt)
            {
                await DrawFrame();
                ProductVisualStamp? observed = observe();
                if (observed is null || observed.State != expected.State ||
                    observed.Viewer != expected.Viewer || observed.Revision != expected.Revision)
                    return; // A legitimate transition is never relabelled.
                int frame = Engine.GetFramesDrawn();
                Image current = host.GetViewport().GetTexture().GetImage();
                Vector2I window = host.GetWindow().Size;
                lastDimensions = $"state={expected.State}, expected={expected.Width}x{expected.Height}, " +
                    $"image={current.GetWidth()}x{current.GetHeight()}, empty={current.IsEmpty()}, " +
                    $"window={window}, texture-api={host.GetViewport().GetTexture().GetSize()}, " +
                    $"visible={host.GetViewport().GetVisibleRect()}, frame={frame}, attempt={attempt + 1}";
                if (attempt == 0) GD.Print($"SCGS_PRODUCT_CAPTURE_DIMENSIONS {lastDimensions}");
                if (current.IsEmpty() || current.GetWidth() != expected.Width || current.GetHeight() != expected.Height ||
                    window.X != expected.Width || window.Y != expected.Height ||
                    observed.Width != expected.Width || observed.Height != expected.Height)
                {
                    // A resize can reach Window and RenderingServer on adjacent
                    // frames. Drop that frame and restart the stable pair; only
                    // the original requested physical dimensions may succeed.
                    current.Dispose();
                    previous?.Dispose();
                    previous = null;
                    previousFrame = 0;
                    continue;
                }
                bool stable = previous is not null && frame == previousFrame + 1 &&
                              StableContent(previous, current);
                previous?.Dispose();
                previous = current;
                previousFrame = frame;
                if (!stable) continue;
                if (observe() != expected) return;
                ValidateVisibleContent(current);
                current.Convert(Image.Format.Rgba8);
                byte[] png = current.SavePngToBuffer();
                if (png.Length == 0) throw new InvalidOperationException("Product screenshot encoding failed.");
                try
                {
                    string sha256 = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();
                    File.WriteAllBytes(Path.Combine(directory, $"{expected.State}.png"), png);
                    captures.Add(expected.State, new ProductVisualImage
                    {
                        State = expected.State, Viewer = expected.Viewer, Revision = expected.Revision,
                        Sha256 = sha256, Width = expected.Width, Height = expected.Height,
                    });
                }
                finally { Array.Clear(png); }
                WriteManifest(success: false);
                return;
            }
            throw new InvalidOperationException($"Product {expected.State} did not produce two stable consecutive rendered frames " +
                $"at its exact physical dimensions within {MaximumStableAttempts} attempts: {lastDimensions}.");
        }
        finally { previous?.Dispose(); }
    }

    // Continuous atmosphere motion may differ by subpixel values. Geometry and
    // UI must stabilize; accept <=0.2% normalized RGB difference after a fixed
    // thumbnail reduction, never a string-only or ProcessFrame-only substitute.
    private static bool StableContent(Image previous, Image current)
    {
        using Image a = (Image)previous.Duplicate();
        using Image b = (Image)current.Duplicate();
        a.Resize(160, 90, Image.Interpolation.Bilinear);
        b.Resize(160, 90, Image.Interpolation.Bilinear);
        a.Convert(Image.Format.Rgb8);
        b.Convert(Image.Format.Rgb8);
        byte[] first = a.GetData();
        byte[] second = b.GetData();
        try
        {
            if (first.Length != second.Length || first.Length == 0) return false;
            long difference = 0;
            for (int index = 0; index < first.Length; ++index)
                difference += Math.Abs(first[index] - second[index]);
            return difference / (255.0 * first.Length) <= 0.002;
        }
        finally { Array.Clear(first); Array.Clear(second); }
    }

    private static void ValidateVisibleContent(Image source)
    {
        using Image thumbnail = (Image)source.Duplicate();
        thumbnail.Resize(160, 90, Image.Interpolation.Bilinear);
        thumbnail.Convert(Image.Format.Rgb8);
        byte[] pixels = thumbnail.GetData();
        try
        {
            int low = 255;
            int high = 0;
            int lit = 0;
            for (int index = 0; index < pixels.Length; index += 3)
            {
                int value = Math.Max(pixels[index], Math.Max(pixels[index + 1], pixels[index + 2]));
                low = Math.Min(low, value);
                high = Math.Max(high, value);
                if (value > 24) ++lit;
            }
            if (high - low < 24 || lit < pixels.Length / 150)
                throw new InvalidOperationException("The product GPU capture is blank or lacks visible UI content.");
        }
        finally { Array.Clear(pixels); }
    }

    internal async Task MeasureHeavyBoardAsync(Node match, ProductVisualStamp expected,
        int player0Cards, int player1Cards, Func<ProductVisualStamp?> observe,
        Func<bool> stillHeavy)
    {
        if (!requirePerformance || performance is not null || performanceRunning || completed) return;
        if (expected.State != "action" || expected.Viewer is null || expected.Revision is null ||
            player0Cards < MinimumHeavyBoardCardsPerPlayer || player1Cards < MinimumHeavyBoardCardsPerPlayer ||
            player0Cards + player1Cards < MinimumHeavyBoardCards || !stillHeavy()) return;
        performanceRunning = true;
        var samples = new double[300];
        ProductResourceCounts before = default;
        ProductResourceCounts after = default;
        bool noGrowth = true;
        int measured = 0;
        DisplayServer.VSyncMode vsync = DisplayServer.WindowGetVsyncMode();
        int maxFps = Engine.MaxFps;
        try
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            Engine.MaxFps = 0;
            for (int frame = 0; frame < 300; ++frame)
            {
                await DrawFrame();
                EnsureSameHeavyBoard();
            }
            before = CountResources(match);
            long previousTimestamp = Stopwatch.GetTimestamp();
            for (int frame = 0; frame < samples.Length; ++frame)
            {
                await DrawFrame();
                long now = Stopwatch.GetTimestamp();
                samples[frame] = Stopwatch.GetElapsedTime(previousTimestamp, now).TotalMilliseconds;
                previousTimestamp = now;
                EnsureSameHeavyBoard();
                after = CountResources(match);
                noGrowth &= after.Actors <= before.Actors && after.Materials <= before.Materials &&
                            after.Textures <= before.Textures && after.Resources <= before.Resources;
                ++measured;
            }
            Array.Sort(samples);
            double p95 = samples[(int)Math.Ceiling(samples.Length * 0.95) - 1];
            double maximum = samples[^1];
            // Resource cleanup/GC may legitimately reduce these counts. The
            // contract forbids growth on every measured frame, not collection.
            bool success = noGrowth && p95 <= 33.3 && maximum < 100;
            performance = new ProductPerformanceEvidence
            {
                State = expected.State, Viewer = expected.Viewer, Revision = expected.Revision,
                Width = expected.Width, Height = expected.Height,
                Player0MainBoard = player0Cards, Player1MainBoard = player1Cards,
                WarmupFrames = 300, MeasuredFrames = measured, Before = before, After = after,
                ZeroGrowth = noGrowth, P95Milliseconds = p95,
                MaximumMilliseconds = maximum, Status = success ? "passed" : "budget_failed", Success = success,
            };
            WritePerformance();
            if (!success) throw new InvalidOperationException("Real heavy-board product performance exceeded its frame/resource budget.");
        }
        catch
        {
            if (performance is null)
            {
                performance = new ProductPerformanceEvidence
                {
                    State = expected.State, Viewer = expected.Viewer, Revision = expected.Revision,
                    Width = expected.Width, Height = expected.Height,
                    Player0MainBoard = player0Cards, Player1MainBoard = player1Cards,
                    MeasuredFrames = measured, Before = before, After = after,
                    Status = "interrupted", Success = false,
                };
                WritePerformance();
            }
            throw;
        }
        finally
        {
            Engine.MaxFps = maxFps;
            DisplayServer.WindowSetVsyncMode(vsync);
            performanceRunning = false;
        }

        void EnsureSameHeavyBoard()
        {
            if (!GodotObject.IsInstanceValid(match) || !match.IsInsideTree() || observe() != expected || !stillHeavy())
                throw new InvalidOperationException("Product heavy-board measurement changed state, viewer or revision.");
        }
    }

    internal void Complete()
    {
        string[] missing = RequiredStates.Where(state => !captures.ContainsKey(state)).ToArray();
        RecordIncompletePerformance();
        bool success = missing.Length == 0 && PerformanceAcceptanceSatisfied;
        WriteManifest(success);
        completed = true;
        if (!success)
            throw new InvalidOperationException($"Product visual evidence incomplete: missing states [{string.Join(", ", missing)}]; heavy-board performance={performance?.Status ?? "not_requested"}.");
    }

    internal void RecordIncompletePerformance()
    {
        if (!requirePerformance || performance is not null) return;
        performance = new ProductPerformanceEvidence { Status = "heavy_board_not_observed", Success = false };
        WritePerformance();
    }

    private async Task DrawFrame()
    {
        if (!GodotObject.IsInstanceValid(host) || !host.IsInsideTree())
            throw new InvalidOperationException("Product visual host exited before capture completed.");
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    private void WriteManifest(bool success) => File.WriteAllText(Path.Combine(directory, "product-visual.json"),
        JsonSerializer.Serialize(new
        {
            schema_version = 1, suite = "product-v05-visual", success,
            missing_states = RequiredStates.Where(state => !captures.ContainsKey(state)).ToArray(),
            captures = captures.Values.OrderBy(capture => capture.State, StringComparer.Ordinal).ToArray(),
        }, new JsonSerializerOptions { WriteIndented = true }));

    private void WritePerformance() => File.WriteAllText(Path.Combine(directory, "product-performance.json"),
        JsonSerializer.Serialize(performance, new JsonSerializerOptions { WriteIndented = true }));

    private static ProductResourceCounts CountResources(Node root)
    {
        int actors = 0;
        var materials = new HashSet<ulong>();
        var textures = new HashSet<ulong>();
        Visit(root);
        return new ProductResourceCounts(actors, materials.Count, textures.Count,
            (long)Godot.Performance.GetMonitor(Godot.Performance.Monitor.ObjectResourceCount));

        void Visit(Node node)
        {
            if (node is CardActor3D or SlotActor3D) ++actors;
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
            if (material is null || !materials.Add(material.GetInstanceId())) return;
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
            if (texture is not null) textures.Add(texture.GetInstanceId());
        }
    }
}

internal sealed record ProductVisualImage
{
    [JsonPropertyName("state")] public required string State { get; init; }
    [JsonPropertyName("viewer")] public int? Viewer { get; init; }
    [JsonPropertyName("revision")] public ulong? Revision { get; init; }
    [JsonPropertyName("sha256")] public required string Sha256 { get; init; }
    [JsonPropertyName("width")] public int Width { get; init; }
    [JsonPropertyName("height")] public int Height { get; init; }
}

internal readonly record struct ProductResourceCounts(
    [property: JsonPropertyName("actors")] int Actors,
    [property: JsonPropertyName("materials")] int Materials,
    [property: JsonPropertyName("textures")] int Textures,
    [property: JsonPropertyName("resources")] long Resources);

internal sealed record ProductPerformanceEvidence
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; init; } = 1;
    [JsonPropertyName("suite")] public string Suite { get; init; } = "product-v05-heavy-board";
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("success")] public bool Success { get; init; }
    [JsonPropertyName("state")] public string? State { get; init; }
    [JsonPropertyName("viewer")] public int? Viewer { get; init; }
    [JsonPropertyName("revision")] public ulong? Revision { get; init; }
    [JsonPropertyName("width")] public int Width { get; init; }
    [JsonPropertyName("height")] public int Height { get; init; }
    [JsonPropertyName("player0_main_board")] public int Player0MainBoard { get; init; }
    [JsonPropertyName("player1_main_board")] public int Player1MainBoard { get; init; }
    [JsonPropertyName("warmup_frames")] public int WarmupFrames { get; init; }
    [JsonPropertyName("measured_frames")] public int MeasuredFrames { get; init; }
    [JsonPropertyName("before")] public ProductResourceCounts Before { get; init; }
    [JsonPropertyName("after")] public ProductResourceCounts After { get; init; }
    [JsonPropertyName("zero_growth")] public bool ZeroGrowth { get; init; }
    [JsonPropertyName("p95_ms")] public double P95Milliseconds { get; init; }
    [JsonPropertyName("max_ms")] public double MaximumMilliseconds { get; init; }
}
