// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using Godot;
using Scgs.GodotClient.Battlefield;
using Scgs.Hotseat.Product;
using V05 = Scgs.Client.V05;

namespace Scgs.GodotClient.PresentationV2;

/// <summary>
/// Cosmetic playback of occurrence-time native facts. This component has no
/// session, command, event ACK, private snapshot, or rule evaluator.
/// </summary>
internal sealed partial class ProductPresentationDirector : Node
{
    internal const int MaximumAnimatedEvents = 64;
    private static readonly Color OathLight = new("ead79c");
    private static readonly Color PactLight = new("d5a1e1");
    private static readonly Color PublicLight = new("cadcf5");
    private static readonly Color DamageLight = new("f2a0a7");
    private static readonly Color HealLight = new("bce1bc");
    private static readonly Font Font = GD.Load<Font>("res://assets/fonts/NotoSansCJKsc-Regular.otf");
    // The generated character source has a documented green-screen backing,
    // not a misleading claim of native PNG alpha. Keying happens only here.
    private static readonly Shader CutinChromaShader = new()
    {
        Code = """
            shader_type canvas_item;
            varying vec4 instance_tint;
            void vertex() { instance_tint = COLOR; }
            void fragment() {
                vec4 sample_color = texture(TEXTURE, UV);
                float other = max(sample_color.r, sample_color.b);
                float dominance = sample_color.g - other;
                float matte = 1.0 - smoothstep(0.06, 0.28, dominance);
                float spill = smoothstep(0.015, 0.10, dominance);
                sample_color.g = mix(sample_color.g, min(sample_color.g, other), spill);
                COLOR = vec4(sample_color.rgb, sample_color.a * matte) * instance_tint;
            }
            """,
    };
    private readonly Dictionary<string, Texture2D> _cutinTextures = new(StringComparer.Ordinal);
    private ProductPresentationFxPool? _fx;
    private Battlefield3DPresenter? _presenter;
    private CanvasLayer? _cutinLayer;
    private Control? _cutinRoot;
    private ColorRect? _cutinVeil;
    private TextureRect? _cutinPortrait;
    private Label? _cutinTitle;
    private TaskCompletionSource<bool>? _wake;
    private ulong _generation;
    private bool _skip;

    internal bool IsPlaying { get; private set; }
    internal bool LastPlaybackCancelled { get; private set; }
    internal bool LastPlaybackFastForwarded { get; private set; }
    internal int LastAnimatedEventCount { get; private set; }
    internal string CurrentCueKind { get; private set; } = string.Empty;
    internal ulong CurrentCueSequence { get; private set; }
    internal float CurrentCueProgress { get; private set; }

    internal async Task PlayAsync(
        ProductPresentationBatch batch,
        Battlefield3DPresenter presenter,
        bool reduceMotion)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(presenter);
        Cancel();
        ulong generation = _generation;
        _presenter = presenter;
        _skip = false;
        _wake = new TaskCompletionSource<bool>();
        LastPlaybackCancelled = false;
        LastPlaybackFastForwarded = batch.Observations.Count > MaximumAnimatedEvents;
        LastAnimatedEventCount = 0;
        IsPlaying = true;
        BeginPerformanceRecording(batch, reduceMotion);
        bool playbackCompleted = false;
        try
        {
            EnsureBuilt(presenter);
            // Overflow fast-forwards the whole cosmetic batch. The bound never
            // discards a rule effect: After remains the authoritative end board.
            if (!LastPlaybackFastForwarded)
            {
                foreach (ProductPresentationObservation item in batch.Observations)
                {
                    if (!IsCurrent(generation)) return;
                    V05.ProductEventObservation fact = item.Observation;
                    if (!fact.PublicToAll || fact.Version != 1 || !fact.IsKnownKind) continue;
                    CurrentCueKind = fact.Kind;
                    CurrentCueSequence = item.Sequence;
                    CurrentCueProgress = 0;
                    bool alreadyApplied = false;
                    if (!_skip)
                    {
                        switch (fact.Kind)
                        {
                            case "move":
                                await MoveAsync(batch, fact, reduceMotion, generation);
                                break;
                            case "damage":
                            case "heal":
                                alreadyApplied = await ImpactAsync(batch, fact, reduceMotion, generation);
                                break;
                            case "evolve":
                                alreadyApplied = await EvolveAsync(batch, fact, reduceMotion, generation);
                                break;
                            case "declaration":
                                await DeclareAsync(batch, fact, reduceMotion, generation);
                                break;
                        }
                        ++LastAnimatedEventCount;
                    }

                    if (!IsCurrent(generation)) return;
                    if (!alreadyApplied) presenter.ApplyPresentationObservation(batch, fact);
                    presenter.ClearPresentationActors();
                    _fx!.Reset();
                    ClearCutin();
                    ClearCueEvidence();
                }
            }

            if (!IsCurrent(generation)) return;
            presenter.ClearPresentationActors();
            presenter.RenderProductPublic(batch.After, batch.PerspectivePlayer);
            playbackCompleted = true;
        }
        finally
        {
            if (generation == _generation)
            {
                _fx?.Reset();
                ClearCutin();
                if (GodotObject.IsInstanceValid(presenter)) presenter.ClearPresentationActors();
                EndPerformanceRecording(!playbackCompleted ? "faulted" : _skip ? "skipped" :
                    LastPlaybackFastForwarded ? "overflow_fast_forwarded" : "completed");
                _presenter = null;
                _wake = null;
                IsPlaying = false;
                ClearCueEvidence();
            }
        }
    }

    internal void Cancel()
    {
        ++_generation;
        if (IsPlaying) LastPlaybackCancelled = true;
        _wake?.TrySetResult(true);
        _wake = null;
        _skip = false;
        IsPlaying = false;
        ClearCueEvidence();
        if (_fx is not null && GodotObject.IsInstanceValid(_fx)) _fx.Reset();
        ClearCutin();
        if (_presenter is not null && GodotObject.IsInstanceValid(_presenter))
        {
            _presenter.ClearPresentationActors();
        }
        EndPerformanceRecording("cancelled");
        _presenter = null;
    }

    internal void Skip()
    {
        if (!IsPlaying) return;
        _skip = true;
        LastPlaybackFastForwarded = true;
        _wake?.TrySetResult(true);
    }

    public override void _ExitTree()
    {
        Cancel();
        DisconnectPerformanceObserver();
        _cutinTextures.Clear();
    }

    private async Task MoveAsync(
        ProductPresentationBatch batch,
        V05.ProductEventObservation fact,
        bool reduceMotion,
        ulong generation)
    {
        if (fact.Subject is not { Hidden: false, Kind: "card", Card: { } instance } subject ||
            fact.From is null || fact.To is null ||
            !_presenter!.TryPresentationTransform(batch, subject, fact.From, out Transform3D start) ||
            !_presenter.TryPresentationTransform(batch, subject, fact.To, out Transform3D end)) return;
        CardActor3D? actor = _presenter.RentPresentationCard(batch, subject, fact.After ?? fact.Before);
        if (actor is null) return;
        _presenter.SetPresentationOriginalVisible(instance, false);
        actor.GlobalTransform = start;
        bool arriving = fact.To.Zone is V05.Zone.MainBoard or V05.Zone.Tactic or V05.Zone.Field;
        float duration = reduceMotion ? 0.04f : arriving ? 0.48f : 0.28f;
        Color color = Accent(subject);
        _fx!.Tint(color);
        await Animate(duration, generation, progress =>
        {
            float eased = Smooth(progress);
            Transform3D pose = reduceMotion ? end : start.InterpolateWith(end, eased);
            if (!reduceMotion)
                pose.Origin += Vector3.Up * MathF.Sin(progress * MathF.PI) * (arriving ? 0.9f : 0.6f);
            actor.GlobalTransform = pose;
            if (arriving && progress > 0.6f)
            {
                _fx.Rings(end.Origin, (progress - 0.6f) / 0.4f, 0.8f);
            }
        });
        if (IsCurrent(generation) && GodotObject.IsInstanceValid(actor)) actor.Visible = false;
    }

    private async Task<bool> ImpactAsync(
        ProductPresentationBatch batch,
        V05.ProductEventObservation fact,
        bool reduceMotion,
        ulong generation)
    {
        if (fact.Subject is null || fact.ActualAmount is not { } amount ||
            !_presenter!.TryPresentationTransform(batch, fact.Subject, null, out Transform3D targetPose)) return false;
        Vector3 target = targetPose.Origin + Vector3.Up * 0.22f;
        bool heal = fact.Kind == "heal";
        bool shield = !heal && fact.BarrierConsumed == true;
        if (amount == 0 && !shield) return false;
        Color color = heal ? HealLight : shield ? PublicLight : DamageLight;
        bool travel = !reduceMotion && !heal && fact.Source is not null &&
            _presenter.TryPresentationTransform(batch, fact.Source, null, out _);
        if (travel && _presenter.TryPresentationTransform(batch, fact.Source, null, out Transform3D sourcePose))
        {
            Vector3 source = sourcePose.Origin + Vector3.Up * 0.4f;
            _fx!.Tint(Accent(fact.Source));
            await Animate(0.24f, generation, progress =>
            {
                Vector3 point = source.Lerp(target, progress);
                point += Vector3.Up * (0.35f * MathF.Sin(progress * MathF.PI));
                _fx.Orb(point, 0.7f + 0.4f * MathF.Sin(progress * MathF.PI));
                _fx.Beam(source, point, 1.0f, (1.0f - progress) * 0.65f);
            });
            if (!IsCurrent(generation)) return false;
            _fx.Reset();
        }

        _presenter.ApplyPresentationObservation(batch, fact);
        _fx!.Tint(color);
        string text = shield ? "屏障" : (heal ? "+" : "−") + amount.ToString(CultureInfo.InvariantCulture);
        await Animate(reduceMotion ? 0.05f : 0.34f, generation, progress =>
        {
            if (!reduceMotion)
            {
                _fx.Rings(target, progress, shield ? 1.0f : 0.65f);
                _fx.Sparks(target, progress, shield ? 1.15f : 0.8f);
            }
            _fx.Amount(target, text, color, progress);
        });
        return true;
    }

    private async Task DeclareAsync(
        ProductPresentationBatch batch,
        V05.ProductEventObservation fact,
        bool reduceMotion,
        ulong generation)
    {
        // Declaration is wind-up only. In particular a response window must
        // never display an impact before a later native damage observation.
        if (fact.Source is null || fact.DeclarationKind != "attack" ||
            !_presenter!.TryPresentationTransform(batch, fact.Source, null, out Transform3D source)) return;
        _fx!.Tint(Accent(fact.Source));
        bool hasTarget = _presenter.TryPresentationTransform(batch, fact.Target, null, out Transform3D target);
        await Animate(reduceMotion ? 0.04f : 0.26f, generation, progress =>
        {
            if (hasTarget && !reduceMotion)
            {
                _fx.Beam(source.Origin + Vector3.Up * 0.3f, target.Origin + Vector3.Up * 0.3f,
                    MathF.Min(1, progress * 2), MathF.Sin(progress * MathF.PI) * 0.55f);
            }
            _fx.Rings(source.Origin, progress, 0.5f);
        });
    }

    private async Task<bool> EvolveAsync(
        ProductPresentationBatch batch,
        V05.ProductEventObservation fact,
        bool reduceMotion,
        ulong generation)
    {
        if (fact.Subject is not { Hidden: false, Kind: "card", Card: { } instance } subject ||
            !_presenter!.TryPresentationTransform(batch, subject, null, out Transform3D resting)) return false;
        CardActor3D? actor = _presenter.RentPresentationCard(batch, subject, fact.Before);
        if (actor is null) return false;
        _presenter.SetPresentationOriginalVisible(instance, false);
        bool ace = subject.DesignId is "LO-11" or "AP-11";
        if (ace && !reduceMotion) PrepareCutin(subject.DesignId!);
        float duration = reduceMotion ? 0.05f : ace ? 2.2f : 1.2f;
        _fx!.Tint(Accent(subject));
        bool applied = false;
        await Animate(duration, generation, progress =>
        {
            if (progress >= 0.5f && !applied)
            {
                actor.Visible = false;
                _presenter.ApplyPresentationObservation(batch, fact);
                applied = true;
                actor = _presenter.RentPresentationCard(batch, subject, fact.After) ?? actor;
                _presenter.SetPresentationOriginalVisible(instance, false);
            }

            Transform3D floating = resting;
            float lift = MathF.Sin(progress * MathF.PI) * (reduceMotion ? 0.0f : 1.1f);
            floating.Origin += Vector3.Up * lift;
            float scale = 1.0f + (reduceMotion ? 0.0f : 0.13f * MathF.Sin(progress * MathF.PI));
            floating.Basis = floating.Basis.Scaled(Vector3.One * scale);
            actor.GlobalTransform = floating;
            actor.SetPresentationEnergy(reduceMotion ? 0 : MathF.Sin(progress * MathF.PI) * 0.72f);
            if (!reduceMotion)
            {
                float phase = progress < 0.5f ? progress * 2.0f : (progress - 0.5f) * 2.0f;
                _fx.Sparks(floating.Origin + Vector3.Up * 0.2f, phase, 1.35f, converge: progress < 0.5f);
                _fx.Rings(resting.Origin, phase, progress < 0.5f ? 0.6f : 1.0f);
                if (ace) AnimateCutin(progress);
            }
        });
        if (IsCurrent(generation) && GodotObject.IsInstanceValid(actor)) actor.Visible = false;
        return applied;
    }

    private async Task Animate(float seconds, ulong generation, Action<float> update)
    {
        if (!IsCurrent(generation) || _skip) return;
        _insideTimedAnimation = true;
        try
        {
            update(0);
            CurrentCueProgress = 0;
            double started = Time.GetTicksUsec();
            while (IsCurrent(generation) && !_skip)
            {
                Task frame = AwaitProcessFrame();
                Task wake = _wake?.Task ?? Task.CompletedTask;
                if (await Task.WhenAny(frame, wake) == wake) return;
                if (!IsCurrent(generation) || _skip) return;
                float progress = Math.Clamp((float)((Time.GetTicksUsec() - started) / (seconds * 1_000_000.0)), 0, 1);
                CurrentCueProgress = progress;
                update(progress);
                if (progress >= 1) return;
            }
        }
        finally
        {
            if (generation == _generation) _insideTimedAnimation = false;
        }
    }

    private async Task AwaitProcessFrame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private void ClearCueEvidence()
    {
        CurrentCueKind = string.Empty;
        CurrentCueSequence = 0;
        CurrentCueProgress = 0;
    }

    private bool IsCurrent(ulong generation) => generation == _generation && IsInsideTree() &&
        _presenter is not null && GodotObject.IsInstanceValid(_presenter);

    private static float Smooth(float progress) => progress * progress * (3.0f - 2.0f * progress);

    private static Color Accent(V05.EventObservationEndpoint? source) => source?.DesignId switch
    {
        string id when id.StartsWith("LO-", StringComparison.Ordinal) => OathLight,
        string id when id.StartsWith("AP-", StringComparison.Ordinal) => PactLight,
        _ => PublicLight,
    };

    private void EnsureBuilt(Battlefield3DPresenter presenter)
    {
        if (_fx is null || !GodotObject.IsInstanceValid(_fx))
        {
            _fx = new ProductPresentationFxPool { Name = "PublicPresentationFx" };
            presenter.AddChild(_fx);
            _fx.Build();
        }
        if (_cutinLayer is not null) return;
        _cutinLayer = new CanvasLayer { Name = "PublicEvolutionCutin", Layer = 65 };
        AddChild(_cutinLayer);
        _cutinRoot = new Control { Name = "CutinRoot", MouseFilter = Control.MouseFilterEnum.Ignore };
        _cutinRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _cutinLayer.AddChild(_cutinRoot);
        _cutinVeil = new ColorRect
        {
            Name = "SoftLuminance",
            Color = new Color(0.91f, 0.9f, 0.97f, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _cutinVeil.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _cutinRoot.AddChild(_cutinVeil);
        _cutinPortrait = new TextureRect
        {
            Name = "PublicAcePortrait",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = new ShaderMaterial { Shader = CutinChromaShader },
        };
        _cutinRoot.AddChild(_cutinPortrait);
        _cutinTitle = new Label
        {
            Name = "EvolutionTitle",
            Text = "进 化",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _cutinTitle.AddThemeFontOverride("font", Font);
        _cutinTitle.AddThemeFontSizeOverride("font_size", 46);
        _cutinTitle.AddThemeColorOverride("font_color", new Color("f5e3b3"));
        _cutinTitle.AddThemeColorOverride("font_outline_color", new Color("352944"));
        _cutinTitle.AddThemeConstantOverride("outline_size", 8);
        _cutinRoot.AddChild(_cutinTitle);
        ClearCutin();
    }

    private void PrepareCutin(string designId)
    {
        if (!_cutinTextures.TryGetValue(designId, out Texture2D? texture))
        {
            string path = $"res://assets/visual/anime_v1/presentation_v2/{designId}-cutin.png";
            if (!ResourceLoader.Exists(path))
            {
                throw new InvalidOperationException($"The public evolution cut-in is missing: {designId}.");
            }
            texture = GD.Load<Texture2D>(path);
            _cutinTextures.Add(designId, texture);
        }
        _cutinPortrait!.Texture = texture;
        _cutinRoot!.Visible = true;
    }

    private void AnimateCutin(float progress)
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        float fade = Math.Clamp(progress / 0.16f, 0, 1) * Math.Clamp((0.88f - progress) / 0.18f, 0, 1);
        _cutinPortrait!.Size = new Vector2(viewport.X * 0.50f, viewport.Y * 0.92f);
        _cutinPortrait.Position = new Vector2(viewport.X * (0.49f - 0.055f * Smooth(MathF.Min(1, progress * 2))), viewport.Y * 0.05f);
        _cutinPortrait.Modulate = Colors.White with { A = fade };
        _cutinVeil!.Color = new Color(0.91f, 0.90f, 0.97f, fade * 0.045f);
        _cutinTitle!.Size = new Vector2(viewport.X * 0.32f, viewport.Y * 0.09f);
        _cutinTitle.Position = new Vector2(viewport.X * 0.31f, viewport.Y * 0.14f);
        _cutinTitle.Modulate = Colors.White with { A = fade };
    }

    private void ClearCutin()
    {
        if (_cutinRoot is null || !GodotObject.IsInstanceValid(_cutinRoot)) return;
        _cutinRoot.Visible = false;
        _cutinPortrait!.Texture = null;
        _cutinPortrait.Modulate = Colors.White;
        _cutinVeil!.Color = Colors.Transparent;
        _cutinTitle!.Modulate = Colors.White;
    }
}
