// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using Godot;
using Scgs.GodotClient.Battlefield;
using Scgs.GodotClient.CardFaces;
using Scgs.GodotClient.PresentationV2;
using V04 = Scgs.Client;

namespace Scgs.GodotClient.Match;

public sealed partial class ProductMatchScreen
{
    private const int FramePoolActors = 24, FramePoolWarmup = 6, FramePoolMeasured = 24;
    private bool cardFramePoolProbeBusy;
    private CardFramePoolRun? cardFramePoolRun;
    private string cardFramePoolResult = "{\"available\":false,\"synthetic\":true,\"reason\":\"not_run\"}";

    /// <summary>
    /// Isolated synthetic stress of the actual 24-actor pool, not a native match
    /// or maximum game-board benchmark. Does not read session/commands/events.
    /// </summary>
    public string ReviewStartCardFramePoolProbe()
    {
        if (!FrameReviewIsRevealed() || cardFrameCaptureBusy || cardFramePerformanceBusy ||
            cardFrameSyntheticHost is not null || cardFramePoolProbeBusy)
            return "{\"accepted\":false,\"synthetic\":true,\"reason\":\"revealed_idle_r1_without_other_probes_required\"}";
        if (DisplayServer.GetName().Equals("headless", StringComparison.OrdinalIgnoreCase))
            return "{\"accepted\":false,\"synthetic\":true,\"reason\":\"display_backed_gpu_required\"}";
        string id = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8];
        var run = new CardFramePoolRun {
            Id = id, Generation = sessionGeneration, Revision = controller!.State.Snapshot!.Revision,
            Viewer = controller.State.Viewer!.Value, Environment = FrameEnvironment(),
        };
        cardFramePoolRun = run;
        cardFramePoolProbeBusy = true;
        cardFramePoolResult = JsonSerializer.Serialize(new { accepted = true, available = true,
            synthetic = true, status = "running", capture_id = id, actors = FramePoolActors,
            warmup_cycles = FramePoolWarmup, measured_cycles = FramePoolMeasured });
        _ = RunCardFramePoolProbeAsync(run);
        return cardFramePoolResult;
    }

    public string ReviewCardFramePoolProbeResult() => cardFramePoolRun is { } run
        ? JsonSerializer.Serialize(new { available = true, synthetic = true, status = "running", capture_id = run.Id,
            cycles_completed = run.Cycles, completed_frame_post_draws = run.Draws,
            static_heavy_completed_frame_post_draws = run.StaticHeavyDraws, cancellation_requested = run.Cancelled })
        : cardFramePoolResult;

    public string ReviewCancelCardFramePoolProbe()
    {
        if (cardFramePoolRun is not { } run)
            return "{\"cancelled\":false,\"synthetic\":true,\"reason\":\"not_running\"}";
        run.Cancelled = true;
        return "{\"cancelled\":true,\"synthetic\":true,\"commands_submitted\":0}";
    }

    private async Task RunCardFramePoolProbeAsync(CardFramePoolRun run)
    {
        SubViewport? view = null;
        var actors = new List<CardActor3D>(FramePoolActors);
        var records = new List<object>(FramePoolMeasured * 3);
        var phaseBaselines = new Dictionary<string, PoolResources>(StringComparer.Ordinal);
        var failures = new HashSet<string>(StringComparer.Ordinal);
        string directory = ProjectSettings.GlobalizePath("user://review-evidence/card-frame-r1-pool/" + run.Id);
        string status = "aborted", reason = "setup_failed";
        object? cleanEvidence = null, hiddenEvidence = null, beforeRelease = null, staticHeavy = null;
        string? publicImage = null, hiddenImage = null, clearedImage = null;
        bool viewportReleased = false, resourceStable = true;
        int injectedActors = 0;
        try
        {
            RequireCurrent();
            Directory.CreateDirectory(directory);
            view = CreateFramePoolViewport();
            AddChild(view);
            for (int index = 0; index < FramePoolActors; ++index)
            {
                var actor = new CardActor3D { Name = "SyntheticPoolCard" + index };
                view.AddChild(actor);
                actor.ClearSensitive();
                actors.Add(actor);
            }
            CardFaceComposition[,] faces = PoolCompositions();
            // Warm every actor through all three factions and both real GLB LODs.
            for (int cycle = 0; cycle < FramePoolWarmup + FramePoolMeasured; ++cycle)
            {
                RequireCurrent();
                for (int index = 0; index < actors.Count; ++index)
                {
                    actors[index].BindProductFace(faces[cycle % 2, (index + cycle) % 3], PoolPose(index),
                        BattlefieldCardLayout.Field);
                    actors[index].SetPickEnabled(false);
                }
                await DrawTwice();
                if (actors.Any(actor => !actor.CiUsesIntegratedProductFace || actor.CiProductFace is null))
                    throw new InvalidOperationException("synthetic_public_frame_not_bound");
                Observe("public_" + (cycle % 2 == 0 ? "detail" : "field"), cycle);
                if (cycle == FramePoolWarmup)
                {
                    publicImage = SaveImage("synthetic-public-24.png");
                    staticHeavy = await MeasureStaticHeavyAsync();
                }

                // One malicious pass additionally exercises old text/metadata,
                // hover callback, pick collision and the *real R1 artwork* shader.
                // Sentinels are synthetic, local to these actors, and then cleared.
                if (cycle == FramePoolWarmup - 1)
                {
                    using Image sentinelPixels = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
                    sentinelPixels.Fill(new Color(1, 0, 1));
                    using ImageTexture sentinel = ImageTexture.CreateFromImage(sentinelPixels);
                    sentinel.ResourceName = "SCGS_R1_POOL_SYNTHETIC_SENTINEL";
                    foreach (CardActor3D actor in actors)
                    {
                        actor.CiArmPrivacySentinel("SCGS_R1_POOL_SYNTHETIC_SENTINEL");
                        foreach (Node node in PoolNodes(actor)) node.SetMeta("r1_pool_synthetic", "SCGS_R1_POOL_SYNTHETIC_SENTINEL");
                        MeshInstance3D art = actor.GetNode<MeshInstance3D>("SculptedBody/RefinedMaster/ArtworkWindow");
                        ((ShaderMaterial)art.MaterialOverride).SetShaderParameter("artwork", sentinel);
                        actor.CollisionLayer = CardActor3D.PickCollisionLayer;
                        foreach (CollisionShape3D shape in PoolNodes(actor).OfType<CollisionShape3D>()) shape.Disabled = false;
                        if (!ReferenceEquals(((ShaderMaterial)art.MaterialOverride).GetShaderParameter("artwork").AsGodotObject(), sentinel) ||
                            !actor.CiHasPrivacyResources || !actor.CiHasActiveHoverTween || actor.CollisionLayer == 0 ||
                            actor.CountForbiddenToken("SCGS_R1_POOL_SYNTHETIC_SENTINEL") == 0)
                            throw new InvalidOperationException("synthetic_privacy_sentinel_was_not_armed");
                        ++injectedActors;
                    }
                    // Clear before disposing the shared sentinel texture. No
                    // stale freed GPU texture is allowed to mask a retention bug.
                    foreach (CardActor3D actor in actors) actor.ClearSensitive();
                    cleanEvidence = InspectPoolClean(actors, hidden: false, failures);
                    await DrawTwice();
                    clearedImage = SaveImage("synthetic-cleared-after-sentinel.png");
                    CheckMagenta("cleared_after_sentinel");
                }
                foreach (CardActor3D actor in actors) actor.ClearSensitive();
                InspectPoolClean(actors, hidden: false, failures);
                for (int index = 0; index < actors.Count; ++index)
                    actors[index].BindHidden(V04.PlayerId.Player0, V04.Zone.Hand, PoolPose(index), BattlefieldCardLayout.FarHand);
                await DrawTwice();
                hiddenEvidence = InspectPoolClean(actors, hidden: true, failures);
                Observe("anonymous_back", cycle);
                if (cycle == FramePoolWarmup) {
                    hiddenImage = SaveImage("synthetic-anonymous-24.png");
                    CheckMagenta("anonymous_after_sentinel");
                }
                foreach (CardActor3D actor in actors) actor.ClearSensitive();
                await DrawTwice();
                cleanEvidence = InspectPoolClean(actors, hidden: false, failures);
                Observe("pooled_clear", cycle);
                ++run.Cycles;
            }
            beforeRelease = ReadPoolResources(view);
            status = failures.Count == 0 && resourceStable ? "completed" : "failed";
            reason = failures.Count == 0 && resourceStable ? "completed_requested_cycles" : "privacy_or_pool_resource_check_failed";

            void Observe(string phase, int cycle)
            {
                PoolResources resources = ReadPoolResources(view);
                if (cycle < FramePoolWarmup) return;
                if (!phaseBaselines.TryGetValue(phase, out PoolResources baseline)) phaseBaselines[phase] = baseline = resources;
                bool stable = resources.BindingFingerprint == baseline.BindingFingerprint;
                resourceStable &= stable;
                records.Add(new { cycle = cycle - FramePoolWarmup + 1, phase, resources, same_resource_ids_as_phase_baseline = stable });
            }
            string SaveImage(string file)
            {
                using Image image = view.GetTexture().GetImage();
                if (image.IsEmpty()) throw new InvalidOperationException("empty_synthetic_pool_image");
                string path = Path.Combine(directory, file);
                if (image.SavePng(path) != Error.Ok) throw new IOException("synthetic_pool_image_write_failed");
                return path;
            }
            void CheckMagenta(string phase)
            {
                using Image image = view.GetTexture().GetImage();
                image.Convert(Image.Format.Rgba8);
                byte[] pixels = image.GetData();
                int exactMagenta = 0;
                for (int at = 0; at < pixels.Length; at += 4)
                    if (pixels[at] >= 253 && pixels[at + 1] <= 2 && pixels[at + 2] >= 253) ++exactMagenta;
                if (exactMagenta > 0) failures.Add(phase + "_magenta_pixels");
            }
            async Task<object> MeasureStaticHeavyAsync()
            {
                // This workload is explicitly synthetic: all 24 independent
                // public card bodies stay still. It never edits the native board.
                // The surrounding six rebind cycles have already loaded all
                // three representative artworks and both authored GLB LODs.
                DisplayServer.VSyncMode vsync = DisplayServer.WindowGetVsyncMode();
                int fpsLimit = Engine.MaxFps;
                Vector2I requestedSize = view.Size;
                int[] imageSizeBefore;
                using (Image image = view.GetTexture().GetImage())
                    imageSizeBefore = [image.GetWidth(), image.GetHeight()];
                var intervals = new double[300];
                var globalResources = new long[300];
                PoolResources before = default;
                long previousTime = 0;
                int previousDraw = 0;
                for (int frame = 1; frame <= 600; ++frame)
                {
                    RequireCurrent();
                    await Draw().WaitAsync(TimeSpan.FromSeconds(5));
                    long now = Stopwatch.GetTimestamp();
                    ++run.Draws;
                    run.StaticHeavyDraws = frame;
                    RequireCurrent();
                    if (view.Size != requestedSize || DisplayServer.WindowGetVsyncMode() != vsync || Engine.MaxFps != fpsLimit)
                        throw new InvalidOperationException("synthetic_heavy_settings_or_size_changed");
                    int currentDraw = Engine.GetFramesDrawn();
                    if (frame > 1 && currentDraw != previousDraw + 1)
                        throw new InvalidOperationException("synthetic_heavy_nonconsecutive_draws");
                    if (frame > 300)
                    {
                        intervals[frame - 301] = Stopwatch.GetElapsedTime(previousTime, now).TotalMilliseconds;
                        globalResources[frame - 301] = (long)Godot.Performance.GetMonitor(Godot.Performance.Monitor.ObjectResourceCount);
                    }
                    previousTime = now;
                    previousDraw = currentDraw;
                    // Walk the geometry before the final warm-up draw, so the
                    // expensive reference scan is not charged to sample 301.
                    // Font-atlas/global-cache work is separately observable.
                    if (frame == 299) before = ReadPoolResources(view);
                }
                PoolResources after = ReadPoolResources(view);
                int[] imageSizeAfter;
                using (Image image = view.GetTexture().GetImage())
                    imageSizeAfter = [image.GetWidth(), image.GetHeight()];
                bool sizeStable = imageSizeBefore.SequenceEqual(imageSizeAfter);
                if (!sizeStable) failures.Add("synthetic_heavy_real_framebuffer_size_changed");
                if (intervals.Any(value => !double.IsFinite(value) || value <= 0))
                    throw new InvalidOperationException("synthetic_heavy_invalid_frame_interval");
                double[] sorted = intervals.OrderBy(value => value).ToArray();
                double p95 = sorted[(int)Math.Ceiling(sorted.Length * .95) - 1], maximum = sorted[^1];
                bool referencesStable = before.BindingFingerprint == after.BindingFingerprint;
                if (!referencesStable) failures.Add("synthetic_heavy_geometry_resource_ids_changed");
                return new {
                    suite = "card-frame-r1-synthetic-static-heavy-24", synthetic = true, synthetic_heavy = true,
                    native_maximum_board = false, actor_count = FramePoolActors,
                    stable_workload = "24 public synthetic representative cards using high-detail real GLBs, fixed for all 600 draws",
                    warmup_frame_post_draws = 300, measured_frame_post_draws = 300, total_frame_post_draws = 600,
                    timing_source = "consecutive_FramePostDraw_Stopwatch", p95_ms = p95, max_ms = maximum,
                    frame_intervals_ms = intervals, vsync = vsync.ToString(), fps_limit = fpsLimit,
                    changes_vsync_or_fps_limit = false, requested_subviewport_size = new[] { requestedSize.X, requestedSize.Y },
                    captured_image_size_before = imageSizeBefore, captured_image_size_after = imageSizeAfter,
                    actual_framebuffer_size_stable = sizeStable, gpu_readbacks_during_measured_frames = 0,
                    resource_reference_bookend_warmup_frame = 299, reference_bookends_only = true,
                    before, after, geometry_bound_resource_ids_stable = referencesStable,
                    global_resource_counts = globalResources, global_resource_count_min = globalResources.Min(),
                    global_resource_count_max = globalResources.Max(), global_resource_count_delta = globalResources[^1] - globalResources[0],
                    reference_budget = new { p95_ms = 33.3, max_ms_exclusive = 100 },
                    current_capped_synthetic_workload_frame_budget_met = p95 <= 33.3 && maximum < 100,
                    timing_scope = "Whole real game frame including the additional independent 1440x1200 synthetic viewport. Not isolated GPU draw time, native maximum board, animation coverage, or uncapped performance unless the recorded settings actually disable VSync/FPS limits.",
                    resource_scope = "Geometry/node/mesh/material/texture reference IDs at warmup frame 299 and after frame 600; global ObjectResourceCount recorded each measured frame. No claim that font atlases, driver allocations, or managed heap were individually traced.",
                    session_calls = 0, commands_submitted = 0, event_acknowledgements = 0,
                    visual_approval = false,
                };
            }
        }
        catch (Exception failure)
        {
            status = run.Cancelled ? "cancelled" : "aborted";
            reason = failure is InvalidOperationException or TimeoutException ? failure.Message : "synthetic_probe_setup_or_io_failed";
        }
        finally
        {
            foreach (CardActor3D actor in actors)
                if (GodotObject.IsInstanceValid(actor)) actor.ClearSensitive();
            actors.Clear();
            if (view is not null && GodotObject.IsInstanceValid(view))
            {
                view.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
                view.QueueFree();
                if (GodotObject.IsInstanceValid(this) && IsInsideTree())
                {
                    Task release = WaitForRelease();
                    await Task.WhenAny(release, Task.Delay(2000));
                }
                viewportReleased = !GodotObject.IsInstanceValid(view);
            }
            var report = new {
                schema_version = 1, suite = "card-frame-r1-synthetic-24-actor-pool", synthetic = true,
                status, reason, capture_id = run.Id, environment = run.Environment,
                actor_pool_size = FramePoolActors, warmup_cycles = Math.Min(run.Cycles, FramePoolWarmup),
                measured_cycles = Math.Max(0, run.Cycles - FramePoolWarmup), completed_frame_post_draws = run.Draws,
                static_heavy_completed_frame_post_draws = run.StaticHeavyDraws,
                injected_sentinel_actor_count = injectedActors, failures = failures.OrderBy(value => value).ToArray(),
                all_same_phase_bound_resource_ids_stable = resourceStable && run.Cycles == FramePoolWarmup + FramePoolMeasured,
                final_clean_evidence = cleanEvidence, final_hidden_evidence = hiddenEvidence,
                pooled_before_viewport_release = beforeRelease, temporary_viewport_released = viewportReleased,
                records, static_heavy = staticHeavy, public_image = publicImage, anonymous_image = hiddenImage, cleared_image = clearedImage,
                native_session_accessed = false, commands_submitted = 0, event_acknowledgements = 0, gate_reveals = 0,
                boundary = "Synthetic layout compositions only; 24 real actors use actual high/low GLBs and shared anonymous backs. The separate static_heavy report records 600 real draws of this synthetic workload only. This is not a native maximum board, full animation performance suite, 35-card coverage, managed heap proof, or visual approval.",
                resource_scope = "All probe descendant nodes plus geometry mesh/material/texture object IDs, including inactive GLB LODs and shader texture uniforms. Excludes font atlases and Environment/Sky. Global ObjectResourceCount is recorded separately and may vary due to editor/engine caches.",
                sentinel_scope = "Synthetic metadata/text/callback/pick plus actual R1 artwork texture, cleared before hidden bind. Two captured GPU states additionally reject near-exact magenta; not exhaustive pixel information-flow proof.",
            };
            cardFramePoolResult = JsonSerializer.Serialize(report, FrameEvidenceJson);
            try { Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "pool.json"), cardFramePoolResult); }
            catch (IOException) { /* The in-memory controlled report remains readable. */ }
            cardFramePoolRun = null;
            cardFramePoolProbeBusy = false;
        }

        async Task WaitForRelease()
        {
            SceneTree tree = GetTree();
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        void RequireCurrent()
        {
            if (run.Cancelled) throw new InvalidOperationException("explicit_synthetic_pool_cancel");
            if (!FrameReviewIsRevealed() || sessionGeneration != run.Generation || controller!.State.Snapshot!.Revision != run.Revision ||
                controller.State.Viewer != run.Viewer || cardFrameCaptureBusy || cardFramePerformanceBusy || cardFrameSyntheticHost is not null)
                throw new InvalidOperationException("review_context_changed");
            if (DateTime.UtcNow - run.Started > TimeSpan.FromSeconds(120)) throw new TimeoutException("synthetic_pool_120_second_deadline");
        }
        async Task DrawTwice()
        {
            for (int count = 0; count < 2; ++count)
            {
                RequireCurrent();
                Task draw = Draw();
                if (await Task.WhenAny(draw, Task.Delay(5000)) != draw) throw new TimeoutException("synthetic_pool_frame_timeout");
                await draw; ++run.Draws; RequireCurrent();
            }
        }
        async Task Draw()
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
    }

    private static SubViewport CreateFramePoolViewport()
    {
        var view = new SubViewport { Name = "ExplicitSynthetic24CardPoolViewport", Size = new(1440, 1200),
            OwnWorld3D = true, TransparentBg = false, Msaa3D = Viewport.Msaa.Msaa4X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        view.AddChild(new Camera3D { Position = new(0, 20, 0), RotationDegrees = new(-90, 0, 0),
            Projection = Camera3D.ProjectionType.Orthogonal, Size = 14, Current = true, Near = .1f, Far = 40 });
        view.AddChild(new DirectionalLight3D { RotationDegrees = new(-55, -28, 0), LightColor = new("fff1d7"), LightEnergy = 1.1f });
        var environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new("242c3d"),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new("b9c9df"), AmbientLightEnergy = .5f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic };
        CardFrameLighting.Apply(environment);
        view.AddChild(new WorldEnvironment { Environment = environment });
        var heading = new Label { Text = "合成 24 卡回池测试 · 非真实对局 / 非性能通过证明", Position = new(15, 18), Size = new(1410, 60),
            HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        heading.AddThemeFontOverride("font", GD.Load<Font>("res://assets/fonts/NotoSansCJKsc-Regular.otf"));
        heading.AddThemeFontSizeOverride("font_size", 30);
        heading.AddThemeColorOverride("font_color", new Color("ffd792"));
        view.AddChild(heading);
        return view;
    }

    private static Transform3D PoolPose(int index) => new(Basis.Identity, new Vector3((index % 6 - 2.5f) * 1.8f, 0, (index / 6 - 1.5f) * 2.3f));
    private static CardFaceComposition[,] PoolCompositions()
    {
        string[] ids = ["LO-11", "AP-11", "NT-04"];
        var result = new CardFaceComposition[2, 3];
        for (int lod = 0; lod < 2; ++lod) for (int card = 0; card < 3; ++card)
        {
            ProductCardVisualEntry entry = ProductCardVisualCatalog.Shared.Resolve(ids[card]);
            result[lod, card] = CardFaceComposer.Compose(new CardFaceViewModel {
                DesignId = ids[card], DisplayName = ids[card] + " 合成回池样本", Kind = entry.Kind,
                Faction = entry.Faction, Rarity = entry.Rarity, Cost = card == 0 ? 10 : card == 1 ? 8 : 4,
                Attack = entry.Kind == ProductCardKind.Follower ? 8 : null,
                Health = entry.Kind == ProductCardKind.Follower ? 8 : null,
            }, lod == 0 ? CardFaceContext.Detail : CardFaceContext.Field, ProductCardVisualCatalog.Shared, CardFrameStyleCatalog.Shared);
        }
        return result;
    }

    private static object InspectPoolClean(IReadOnlyList<CardActor3D> actors, bool hidden, HashSet<string> failures)
    {
        int identities = 0, texts = 0, metadata = 0, collisions = 0, tweens = 0, overrides = 0, artwork = 0, backMismatch = 0, refinedMeshVisibility = 0;
        foreach (CardActor3D actor in actors)
        {
            if (actor.CiProductFace is not null || actor.CardPresentation?.InstanceId is not null ||
                actor.CardPresentation?.DefinitionId is not null || actor.CiAnonymousFaceHasIdentity ||
                actor.CountForbiddenToken("SCGS_R1_POOL_SYNTHETIC_SENTINEL") != 0 || actor.CiHasPrivacyResources) ++identities;
            if (actor.CollisionLayer != 0 || actor.Surface is not null || actor.CanActivate) ++collisions;
            if (actor.CiHasActiveHoverTween) ++tweens;
            if (hidden ? !actor.CiUsesSharedCardBack : actor.Visible || actor.CardPresentation is not null) ++backMismatch;
            foreach (Node node in PoolNodes(actor))
            {
                metadata += node.GetMetaList().Count;
                if (node is Label3D label && (!string.IsNullOrEmpty(label.Text) || label.Visible)) ++texts;
                if (node is CollisionShape3D shape && !shape.Disabled) ++collisions;
                if (node is RefinedCardBody refined && (refined.HasIdentity || refined.Visible)) ++identities;
                if (node is MeshInstance3D mesh && mesh.GetPath().ToString().Contains("/RefinedMaster/", StringComparison.Ordinal))
                {
                    if (mesh.Visible) ++refinedMeshVisibility;
                    for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); ++surface)
                        if (mesh.GetSurfaceOverrideMaterial(surface) is not null) ++overrides;
                    if (mesh.Name == "ArtworkWindow" && mesh.MaterialOverride is ShaderMaterial art &&
                        art.GetShaderParameter("artwork").AsGodotObject() is not null) ++artwork;
                }
            }
        }
        if (identities + texts + metadata + collisions + tweens + overrides + artwork + backMismatch + refinedMeshVisibility != 0)
            failures.Add(hidden ? "anonymous_state_not_clean" : "pooled_state_not_clean");
        return new { hidden, actors = actors.Count, identities, texts, metadata, collisions, tweens,
            refined_surface_overrides = overrides, refined_artwork_bindings = artwork,
            refined_visible_child_meshes = refinedMeshVisibility, expected_visibility_or_back_mismatches = backMismatch };
    }

    private static IEnumerable<Node> PoolNodes(Node node)
    {
        yield return node;
        foreach (Node child in node.GetChildren()) foreach (Node item in PoolNodes(child)) yield return item;
    }

    private static PoolResources ReadPoolResources(Node root)
    {
        var nodes = new HashSet<ulong>(); var meshes = new HashSet<ulong>(); var materials = new HashSet<ulong>(); var textures = new HashSet<ulong>();
        foreach (Node node in PoolNodes(root))
        {
            nodes.Add(node.GetInstanceId());
            if (node is GeometryInstance3D geometry) { AddMaterial(geometry.MaterialOverride); AddMaterial(geometry.MaterialOverlay); }
            if (node is MeshInstance3D mesh && mesh.Mesh is { } source)
            {
                meshes.Add(source.GetInstanceId());
                for (int i = 0; i < source.GetSurfaceCount(); ++i) { AddMaterial(source.SurfaceGetMaterial(i)); AddMaterial(mesh.GetSurfaceOverrideMaterial(i)); }
            }
        }
        string ids = string.Join("/", new[] { nodes, meshes, materials, textures }.Select(set => string.Join(",", set.OrderBy(id => id))));
        return new(nodes.Count, meshes.Count, materials.Count, textures.Count,
            (long)Godot.Performance.GetMonitor(Godot.Performance.Monitor.ObjectResourceCount),
            Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ids))).ToLowerInvariant());
        void AddMaterial(Material? material)
        {
            if (material is null || !materials.Add(material.GetInstanceId())) return;
            AddMaterial(material.NextPass);
            if (material is BaseMaterial3D standard)
                foreach (BaseMaterial3D.TextureParam channel in CardFrameTextureChannels) AddTexture(standard.GetTexture(channel));
            if (material is ShaderMaterial shader && shader.Shader is { } source)
                foreach (Godot.Collections.Dictionary uniform in source.GetShaderUniformList())
                {
                    Variant value = shader.GetShaderParameter(uniform["name"].AsStringName());
                    if (value.VariantType == Variant.Type.Object && value.AsGodotObject() is Texture2D texture) AddTexture(texture);
                }
        }
        void AddTexture(Texture2D? texture) { if (texture is not null) textures.Add(texture.GetInstanceId()); }
    }

    private readonly record struct PoolResources(int Nodes, int Meshes, int Materials, int Textures, long GlobalResources, string BindingFingerprint);
    private sealed class CardFramePoolRun
    {
        internal required string Id;
        internal required object Environment;
        internal ulong Generation, Revision;
        internal Scgs.Client.V05.PlayerId Viewer;
        internal bool Cancelled;
        internal int Cycles, Draws, StaticHeavyDraws;
        internal readonly DateTime Started = DateTime.UtcNow;
    }
}
