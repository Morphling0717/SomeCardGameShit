// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Scgs.GodotClient.Battlefield;
using Scgs.GodotClient.CardFaces;
using Scgs.GodotClient.PresentationV2;
using Scgs.Hotseat.ProductReview;

namespace Scgs.GodotClient.Match;

/// <summary>
/// One bounded, independent layout viewport. Its only input is the labelled
/// synthetic fixture catalogue: no session, CardView, event cursor or command.
/// This is not evidence that a fixed-deck match produced the supplied numbers.
/// </summary>
internal sealed partial class CardFrameSyntheticReviewHost : CanvasLayer
{
    private SubViewport viewport = null!;
    private Camera3D camera = null!;
    private CardActor3D actor = null!;
    private Control root = null!;
    private TextureRect display = null!;
    private Label sampleLabel = null!;
    private CardFrameSyntheticSample? sample;
    private Func<bool>? stillAllowed;
    private Action? closeRequested;
    private bool closed, captureBusy;
    private ulong sampleGeneration, capturedGeneration;
    private string captureResult = "{\"available\":false,\"reason\":\"not_captured\",\"synthetic\":true}";

    internal void Configure(Func<bool> allowed, Action close)
    {
        stillAllowed = allowed;
        closeRequested = close;
    }

    public override void _Ready()
    {
        Layer = 1000;
        root = new Control { Name = "SyntheticReviewShield", MouseFilter = Control.MouseFilterEnum.Stop };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        var background = new ColorRect { Color = new Color("151924"), MouseFilter = Control.MouseFilterEnum.Stop };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(background);
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "top", "right", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 12);
        root.AddChild(margin);
        var column = new VBoxContainer();
        margin.AddChild(column);
        var toolbar = new HBoxContainer();
        column.AddChild(toolbar);
        foreach (CardFrameSyntheticSample item in CardFrameSyntheticSamples.All)
        {
            CardFrameSyntheticSample fixedItem = item;
            var button = new Button { Text = item.Key, TooltipText = "仅合成排版，不改变对局" };
            button.Pressed += () => { if (!captureBusy) Bind(fixedItem); };
            toolbar.AddChild(button);
        }
        var close = new Button { Text = "关闭样本（Esc）", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        close.Pressed += () => closeRequested?.Invoke();
        toolbar.AddChild(close);
        display = new TextureRect {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        column.AddChild(display);

        viewport = new SubViewport {
            Name = "SyntheticLayoutViewport", Size = new(720, 960), OwnWorld3D = true,
            TransparentBg = false, Msaa3D = Viewport.Msaa.Msaa4X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(viewport);
        camera = new Camera3D {
            Position = new(0, 3, 0), RotationDegrees = new(-90, 0, 0),
            Projection = Camera3D.ProjectionType.Orthogonal, Size = 2.65f,
            Current = true, Near = .1f, Far = 6,
        };
        viewport.AddChild(camera);
        viewport.AddChild(new DirectionalLight3D {
            RotationDegrees = new(-55, -28, 0), LightColor = new("fff1d7"), LightEnergy = 1.10f,
        });
        var environment = new Godot.Environment {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new("242c3d"),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new("b9c9df"), AmbientLightEnergy = .5f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        CardFrameLighting.Apply(environment);
        viewport.AddChild(new WorldEnvironment { Environment = environment });
        actor = new CardActor3D { Name = "ExplicitSyntheticCard" };
        viewport.AddChild(actor);
        actor.ClearSensitive();

        // These labels belong to the captured viewport, not just the outer UI:
        // a detached screenshot therefore cannot masquerade as native gameplay.
        Font font = ResourceLoader.Load<Font>("res://assets/fonts/NotoSansCJKsc-Regular.otf");
        var heading = new Label {
            Text = "合成排版样本 · 非真实对局状态", Position = new(8, 14), Size = new(704, 40),
            HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        heading.AddThemeFontOverride("font", font);
        heading.AddThemeFontSizeOverride("font_size", 24);
        heading.AddThemeColorOverride("font_color", new("ffd792"));
        viewport.AddChild(heading);
        sampleLabel = new Label {
            Position = new(12, 845), Size = new(696, 104), HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        sampleLabel.AddThemeFontOverride("font", font);
        sampleLabel.AddThemeFontSizeOverride("font_size", 19);
        viewport.AddChild(sampleLabel);
        display.Texture = viewport.GetTexture();
    }

    internal bool Bind(CardFrameSyntheticSample value)
    {
        if (closed || captureBusy || stillAllowed?.Invoke() != true) return false;
        ++sampleGeneration;
        sample = value;
        ProductCardVisualEntry entry = ProductCardVisualCatalog.Shared.Resolve(value.ReferenceDesignId);
        CardFaceComposition composition = CardFaceComposer.Compose(new CardFaceViewModel {
            DesignId = value.ReferenceDesignId, DisplayName = value.FullName,
            Kind = entry.Kind, Faction = entry.Faction, Rarity = entry.Rarity,
            Cost = value.Cost, Attack = value.Attack, Health = value.Health,
        }, CardFaceContext.Detail, ProductCardVisualCatalog.Shared, CardFrameStyleCatalog.Shared);
        actor.BindProductFace(composition, Transform3D.Identity, BattlefieldCardLayout.Field);
        // BindProductFace does not attach a surface intent or an instance ID.
        actor.SetPickEnabled(false);
        sampleLabel.Text = value.Key + "\n费用 " + value.Cost +
            (value.Attack is null ? " · 无身材法术" : $" · 攻击 {value.Attack} · 当前生命 {value.Health} / 合成最大生命 {value.MaximumHealth}") +
            "\n仅验证排版；不是实战效果、不是最小尺寸可读性通过证明";
        captureResult = "{\"available\":false,\"reason\":\"sample_changed\",\"synthetic\":true}";
        return true;
    }

    internal string Describe() => JsonSerializer.Serialize(new {
        available = !closed, synthetic = true, sample_bound = !closed && sample is not null,
        rendered = !closed && capturedGeneration != 0 && capturedGeneration == sampleGeneration,
        suite = "card-frame-r1-synthetic-layout-viewport", sample_key = sample?.Key,
        required_visible_label = "合成排版样本 · 非真实对局状态",
        viewport_size = new[] { 720, 960 }, native_session_accessed = false,
        commands_submitted = 0, event_acknowledgements = 0,
    });

    internal string StartCapture()
    {
        if (closed || captureBusy || sample is null || stillAllowed?.Invoke() != true)
            return "{\"accepted\":false,\"synthetic\":true,\"reason\":\"idle_synthetic_viewport_required\"}";
        if (DisplayServer.GetName().Equals("headless", StringComparison.OrdinalIgnoreCase))
            return "{\"accepted\":false,\"synthetic\":true,\"reason\":\"display_backed_gpu_required\"}";
        string id = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8];
        captureBusy = true;
        captureResult = JsonSerializer.Serialize(new { accepted = true, status = "capturing", synthetic = true, capture_id = id });
        _ = CaptureAsync(id, sample, sampleGeneration);
        return captureResult;
    }

    internal string CaptureResult() => closed || stillAllowed?.Invoke() != true
        ? "{\"available\":false,\"synthetic\":true,\"reason\":\"synthetic_viewport_closed\"}" : captureResult;

    private async Task CaptureAsync(string id, CardFrameSyntheticSample capturedSample, ulong generation)
    {
        try
        {
            for (int count = 0; count < 2; ++count)
            {
                Task draw = Draw();
                if (await Task.WhenAny(draw, Task.Delay(5000)) != draw)
                    throw new TimeoutException("synthetic_gpu_frame_timeout");
                await draw;
                if (closed || !IsInsideTree() || generation != sampleGeneration || stillAllowed?.Invoke() != true)
                    throw new InvalidOperationException("synthetic_capture_cancelled");
            }
            using Image image = viewport.GetTexture().GetImage();
            if (image.IsEmpty()) throw new InvalidOperationException("empty_synthetic_gpu_image");
            string directory = ProjectSettings.GlobalizePath("user://screenshots/card-frame-r1/synthetic/" + id);
            Directory.CreateDirectory(directory);
            string imagePath = Path.Combine(directory, capturedSample.Key + ".png");
            if (image.SavePng(imagePath) != Error.Ok) throw new IOException("synthetic_gpu_image_save_failed");
            capturedGeneration = generation;
            captureResult = JsonSerializer.Serialize(new {
                available = true, status = "captured", synthetic = true, rendered = true, schema_version = 1,
                suite = "card-frame-r1-synthetic-layout-viewport", capture_id = id,
                fixture = capturedSample, image = imagePath,
                image_sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(imagePath))).ToLowerInvariant(),
                captured_image_size = new[] { image.GetWidth(), image.GetHeight() },
                completed_frame_post_draws = 2, actor_count = 1, independent_subviewport_count = 1,
                adapter = RenderingServer.GetVideoAdapterName(), engine_version = Engine.GetVersionInfo()["string"].AsString(),
                native_session_accessed = false, commands_submitted = 0, event_acknowledgements = 0,
                boundary = "Explicit synthetic fixture rendered by the real card actor. No native match, damage or gameplay result is claimed; this large detail viewport does not certify 16px field readability.",
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(directory, "manifest.json"), captureResult);
        }
        catch (Exception failure)
        {
            captureResult = JsonSerializer.Serialize(new { available = false, status = "aborted", synthetic = true, reason = failure.Message });
        }
        finally { captureBusy = false; }
        async Task Draw()
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
    }

    public override void _Process(double delta)
    {
        if (!closed && stillAllowed?.Invoke() != true) closeRequested?.Invoke();
    }

    public override void _Input(InputEvent @event)
    {
        if (closed || @event is not InputEventKey key) return;
        // Keep every gameplay keyboard shortcut outside this synthetic modal.
        if (key.Pressed && !key.Echo && key.Keycode == Key.Escape) closeRequested?.Invoke();
        GetViewport().SetInputAsHandled();
    }

    internal void Close()
    {
        if (closed) return;
        closed = true; ++sampleGeneration;
        root?.Hide();
        actor?.ClearSensitive();
        if (display is not null) display.Texture = null;
        if (viewport is not null) viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        sample = null; stillAllowed = null; closeRequested = null;
        SetProcess(false); SetProcessInput(false);
        QueueFree();
    }

    public override void _ExitTree()
    {
        closed = true; ++sampleGeneration;
        actor?.ClearSensitive();
        if (display is not null) display.Texture = null;
        sample = null; stillAllowed = null; closeRequested = null;
    }
}
