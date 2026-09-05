// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Scgs.GodotClient.Battlefield;
using Scgs.GodotClient.CardFaces;
using Scgs.GodotClient.PresentationV2;

namespace Scgs.GodotClient.Match;

/// <summary>
/// Public catalogue design display. Captures real GPU FramePostDraw samples with
/// wall-clock timestamps, not a fabricated gameplay/evolution outcome or fixed fps.
/// One actor, one viewport, at most 180 PNGs; no native session or hidden DTO input.
/// </summary>
internal sealed partial class CardFrameTurntableHost : CanvasLayer
{
    private const double DurationSeconds = 6, TargetInterval = 1.0 / 30;
    private const int MaximumFrames = 180;
    private readonly List<object> frames = new(MaximumFrames);
    private SubViewport viewport = null!;
    private Camera3D camera = null!;
    private CardActor3D actor = null!;
    private DirectionalLight3D key = null!;
    private Control shield = null!;
    private TextureRect display = null!;
    private Label progress = null!;
    private SceneTreeTimer? deadline;
    private Func<bool>? stillAllowed;
    private Action<string>? completed;
    private Action? closeRequested;
    private string designId = "", id = "", directory = "", artPath = "";
    private long started;
    private int warmDraws;
    private double nextCapture, poseSeconds, lastSampleSeconds;
    private bool closed, finishing;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    internal static bool Supports(string value) => value is "LO-11" or "AP-11" or "NT-04";

    internal void Configure(string value, Func<bool> allowed, Action<string> done, Action cancel)
    {
        if (!Supports(value)) throw new ArgumentException("representative_design_required", nameof(value));
        designId = value; stillAllowed = allowed; completed = done; closeRequested = cancel;
    }

    public override void _Ready()
    {
        Layer = 1000;
        id = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8];
        directory = ProjectSettings.GlobalizePath("user://screenshots/card-frame-r1/turntable/" + id);
        Directory.CreateDirectory(directory);
        shield = new Control { Name = "DesignDisplayShield", MouseFilter = Control.MouseFilterEnum.Stop };
        shield.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); AddChild(shield);
        var background = new ColorRect { Color = new("151924"), MouseFilter = Control.MouseFilterEnum.Stop };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); shield.AddChild(background);
        display = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Stop };
        display.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); shield.AddChild(display);
        var cancel = new Button { Text = "结束设计展示（Esc）", Position = new(16, 16), Size = new(240, 48) };
        cancel.Pressed += () => closeRequested?.Invoke(); shield.AddChild(cancel);
        viewport = new SubViewport { Name = "LabelledDesignTurntableViewport", Size = new(720, 960),
            OwnWorld3D = true, TransparentBg = false, Msaa3D = Viewport.Msaa.Msaa4X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(viewport);
        camera = new Camera3D { Position = new(0, 3, 0), RotationDegrees = new(-90, 0, 0),
            Projection = Camera3D.ProjectionType.Orthogonal, Size = 2.8f, Current = true, Near = .1f, Far = 6 };
        viewport.AddChild(camera);
        key = new DirectionalLight3D { RotationDegrees = new(-55, -28, 0), LightColor = new("fff1d7"), LightEnergy = 1.10f };
        viewport.AddChild(key);
        var environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new("242c3d"), AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new("b9c9df"), AmbientLightEnergy = .5f, TonemapMode = Godot.Environment.ToneMapper.Filmic };
        CardFrameLighting.Apply(environment); viewport.AddChild(new WorldEnvironment { Environment = environment });
        actor = new CardActor3D { Name = "PublicCatalogueDesignCard" }; viewport.AddChild(actor); actor.ClearSensitive();
        BindPublicDesign();
        Font font = GD.Load<Font>("res://assets/fonts/NotoSansCJKsc-Regular.otf");
        AddLabel("卡框设计展示 · 非对局状态", new(8, 14), new(704, 44), 26, new("ffd792"));
        AddLabel("仅观察卡体、材质与斜视；不是出牌或进化演出", new(8, 876), new(704, 32), 18, new("d6dfef"));
        progress = AddLabel(designId + " · GPU 实际帧取证", new(8, 913), new(704, 32), 18, new("d6dfef"));
        display.Texture = viewport.GetTexture();
        RenderingServer.FramePostDraw += CaptureDraw;
        deadline = GetTree().CreateTimer(15, processAlways: true, ignoreTimeScale: true);
        deadline.Timeout += CaptureDeadline;

        Label AddLabel(string text, Vector2 position, Vector2 size, int fontSize, Color color)
        {
            var label = new Label { Text = text, Position = position, Size = size,
                HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            label.AddThemeFontOverride("font", font); label.AddThemeFontSizeOverride("font_size", fontSize);
            label.AddThemeColorOverride("font_color", color); viewport.AddChild(label); return label;
        }
    }

    private void BindPublicDesign()
    {
        // Printed public design metadata, not a mutated snapshot or a native setup.
        (string name, int cost, int? attack, int? health) = designId switch {
            "LO-11" => ("曜誓大团长·蕾奥妮", 10, (int?)8, (int?)8),
            "AP-11" => ("禁忌毕业生·诺克缇娅", 8, (int?)6, (int?)6),
            _ => ("界域裁定", 4, (int?)null, (int?)null),
        };
        ProductCardVisualEntry entry = ProductCardVisualCatalog.Shared.Resolve(designId);
        CardFaceComposition face = CardFaceComposer.Compose(new CardFaceViewModel {
            DesignId = designId, DisplayName = name, Kind = entry.Kind, Faction = entry.Faction,
            Rarity = entry.Rarity, Cost = cost, Attack = attack, Health = health,
        }, CardFaceContext.Detail, ProductCardVisualCatalog.Shared, CardFrameStyleCatalog.Shared);
        artPath = face.ArtPath;
        actor.BindProductFace(face, Transform3D.Identity, BattlefieldCardLayout.Field);
        actor.SetPickEnabled(false);
    }

    internal string Describe() => JsonSerializer.Serialize(new { available = !closed, status = "recording",
        capture_id = id, design_id = designId, design_display = true, gameplay_recording = false,
        frames_captured = frames.Count, elapsed_seconds = started == 0 ? 0 : Stopwatch.GetElapsedTime(started).TotalSeconds,
        target_seconds = DurationSeconds, maximum_frames = MaximumFrames, session_calls = 0 });

    public override void _Process(double delta)
    {
        if (closed || finishing) return;
        if (stillAllowed?.Invoke() != true) { closeRequested?.Invoke(); return; }
        if (started == 0) return;
        poseSeconds = Math.Min(DurationSeconds, Stopwatch.GetElapsedTime(started).TotalSeconds);
        float phase = (float)(poseSeconds / DurationSeconds * Math.Tau);
        // One slow, bounded light/card inspection. No shader pulse or VFX queue.
        actor.RotationDegrees = new(12 * MathF.Sin(phase), 0, 12 * MathF.Sin(phase * 2) * .65f);
        camera.RotationDegrees = new(-90, 0, 3 * MathF.Sin(phase));
        key.RotationDegrees = new(-55 + 8 * MathF.Sin(phase), -28 + 12 * MathF.Sin(phase), 0);
        progress.Text = $"{designId} · {poseSeconds:F2} s · 真实 GPU 帧（非固定帧率）";
    }

    private void CaptureDraw()
    {
        if (closed || finishing) return;
        try
        {
            if (stillAllowed?.Invoke() != true) { closeRequested?.Invoke(); return; }
            if (++warmDraws <= 2) return;
            if (started == 0) started = Stopwatch.GetTimestamp();
            long timestamp = Stopwatch.GetTimestamp();
            double seconds = Stopwatch.GetElapsedTime(started, timestamp).TotalSeconds;
            if (frames.Count > 0 && seconds >= DurationSeconds) { Finish("recorded", null); return; }
            if (seconds < nextCapture) return;
            using Image image = viewport.GetTexture().GetImage();
            if (image.IsEmpty()) throw new InvalidOperationException("empty_gpu_image");
            string path = Path.Combine(directory, $"frame-{frames.Count:D4}.png");
            if (image.SavePng(path) != Error.Ok) throw new IOException("frame_png_save_failed");
            frames.Add(new { index = frames.Count, image = path, timestamp_ticks = timestamp,
                time_seconds = seconds, displayed_pose_seconds = poseSeconds, frame_post_draw = Engine.GetFramesDrawn(),
                width = image.GetWidth(), height = image.GetHeight(),
                sha256 = Hash(File.ReadAllBytes(path)) });
            lastSampleSeconds = seconds;
            // Skip deadlines missed during actual rendering/readback. No duplicated
            // frames and no resampling of pose to pretend capture held 30/60 fps.
            nextCapture = (Math.Floor(seconds / TargetInterval) + 1) * TargetInterval;
            if (frames.Count >= MaximumFrames) Finish("recorded", "maximum_frame_bound");
        }
        catch (Exception) { Finish("aborted", "gpu_capture_or_file_write_failed"); }
    }

    private void Finish(string status, string? reason)
    {
        if (closed || finishing) return;
        finishing = true;
        RenderingServer.FramePostDraw -= CaptureDraw;
        string manifest = Path.Combine(directory, "manifest.json");
        string report;
        try
        {
            double elapsed = started == 0 ? 0 : Stopwatch.GetElapsedTime(started).TotalSeconds;
            report = JsonSerializer.Serialize(new {
                available = true, status, reason, schema_version = 1, suite = "card-frame-r1-public-design-turntable",
                capture_id = id, design_id = designId, design_display = true, gameplay_recording = false,
                required_pixel_label = "卡框设计展示 · 非对局状态", captured_image_size = new[] { 720, 960 },
                target_seconds = DurationSeconds, actual_elapsed_seconds = elapsed, last_frame_seconds = lastSampleSeconds,
                capture_target_max_fps = 30, actual_frame_count = frames.Count,
                observed_sample_fps = lastSampleSeconds > 0 ? (frames.Count - 1) / lastSampleSeconds : 0,
                timing_source = "FramePostDraw_Stopwatch_monotonic_clock", timestamp_frequency = Stopwatch.Frequency,
                not_fixed_fps = true, maximum_frames = MaximumFrames, initial_stable_frame_post_draws = 2,
                adapter = RenderingServer.GetVideoAdapterName(), vendor = RenderingServer.GetVideoAdapterVendor(),
                adapter_api = RenderingServer.GetVideoAdapterApiVersion(), engine = Engine.GetVersionInfo()["string"].AsString(),
                render_method = RenderingServer.GetCurrentRenderingMethod(),
                model_sha256 = SourceAssetHash(CardFrameMaster.RootPath + "frame-master.glb"),
                artwork_path = artPath, artwork_sha256 = SourceAssetHash(artPath),
                source_hash_note = "Raw source hashes are nullable when an exported PCK only contains imported resources; package asset manifest remains authoritative.",
                commands_submitted = 0, session_calls = 0, event_acknowledgements = 0, gate_reveals = 0,
                animation_upgrade = false, independent_actor_count = 1, independent_subviewport_count = 1,
                boundary = "Public printed metadata and actual R1 model/material/typography only. Does not prove native gameplay, field readability, user approval or fixed-frame-rate recording.",
                saved_report = manifest, frames,
            }, Json);
            File.WriteAllText(manifest, report);
        }
        catch (Exception)
        {
            report = JsonSerializer.Serialize(new { available = false, status = "aborted", reason = "manifest_write_failed",
                design_display = true, gameplay_recording = false, frames_captured = frames.Count });
        }
        completed?.Invoke(report);
    }

    private void CaptureDeadline()
    {
        if (!closed && !finishing) Finish("aborted", "15_second_real_draw_deadline");
    }

    private void ReleaseDeadline()
    {
        if (deadline is not null && GodotObject.IsInstanceValid(deadline)) deadline.Timeout -= CaptureDeadline;
        deadline = null;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string? SourceAssetHash(string path)
    {
        // Exporters may retain only the imported .ctex/.scn and a remap. Avoid
        // logging a Godot file error or hashing empty bytes as source evidence.
        if (!Godot.FileAccess.FileExists(path)) return null;
        byte[] bytes = Godot.FileAccess.GetFileAsBytes(path);
        return bytes.Length == 0 ? null : Hash(bytes);
    }

    public override void _Input(InputEvent @event)
    {
        if (closed || @event is not InputEventKey keyEvent) return;
        if (keyEvent.Pressed && !keyEvent.Echo && keyEvent.Keycode == Key.Escape) closeRequested?.Invoke();
        GetViewport().SetInputAsHandled();
    }

    internal void Close()
    {
        if (closed) return;
        closed = true; RenderingServer.FramePostDraw -= CaptureDraw;
        ReleaseDeadline();
        shield?.Hide(); actor?.ClearSensitive();
        if (display is not null) display.Texture = null;
        if (viewport is not null) viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        stillAllowed = null; completed = null; closeRequested = null; frames.Clear();
        SetProcess(false); SetProcessInput(false); QueueFree();
    }

    public override void _ExitTree()
    {
        RenderingServer.FramePostDraw -= CaptureDraw;
        ReleaseDeadline();
        closed = true; actor?.ClearSensitive();
        if (display is not null) display.Texture = null;
        stillAllowed = null; completed = null; closeRequested = null; frames.Clear();
    }
}
