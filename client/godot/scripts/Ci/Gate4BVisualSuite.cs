// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;
using Scgs.GodotClient.Battlefield;
using Scgs.GodotClient.Visuals;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scgs.GodotClient.Ci;

/// <summary>
/// Display-backed screenshot and steady-state resource/performance evidence.
/// This collector only observes the already-rendered UI; it never queries the
/// native session and therefore cannot become a second viewer or rules path.
/// </summary>
internal sealed class Gate4BVisualSuite
{
    private const string AssetManifestPath =
        "res://assets/visual/ASSET_MANIFEST.json";

    private static readonly string[] ExpectedStates =
    [
        "menu",
        "match-setup",
        "covered",
        "mulligan",
        "action",
        "source-selection",
        "slot-or-target-selection",
        "reaction",
        "resolving",
        "result",
        "error",
    ];

    private static readonly string[] SoftwareAdapterNameMarkers =
    [
        "Microsoft Basic Render Driver",
        "llvmpipe",
        "SwiftShader",
        "software renderer",
    ];

    private readonly Node root;
    private readonly string outputDirectory;
    private readonly string assetManifestSha256;
    private readonly List<Gate4BVisualCapture> captures = [];
    private readonly HashSet<string> capturedStates = new(StringComparer.Ordinal);
    private Gate4BPerformanceEvidence? performance;
    private int captureSequence;

    internal Gate4BVisualSuite(Node root, string outputDirectory)
    {
        this.root = root ?? throw new ArgumentNullException(nameof(root));
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        if (!Path.IsPathFullyQualified(outputDirectory))
        {
            throw new ArgumentException(
                "--ci-visual-suite requires an absolute output directory.",
                nameof(outputDirectory));
        }
        if (string.Equals(
                DisplayServer.GetName(),
                "headless",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "--ci-visual-suite requires a display-backed renderer.");
        }

        this.outputDirectory = Path.GetFullPath(outputDirectory);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        if (ResolveRequestedViewport(OS.GetCmdlineUserArgs()) is { } requestedViewport)
        {
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
            DisplayServer.WindowSetPosition(Vector2I.Zero);
            DisplayServer.WindowSetSize(requestedViewport);
        }
        CardVisualEntry[] cardVisuals = CardVisualCatalog.Shared.Entries.ToArray();
        if (cardVisuals.Length != 29 ||
            cardVisuals.Select(entry => entry.DefinitionId).Distinct().Count() != 29 ||
            cardVisuals.Select(entry => entry.ArtworkPath).Distinct(StringComparer.Ordinal).Count() != 29 ||
             cardVisuals.Any(entry => !ResourceLoader.Exists(entry.ArtworkPath)) ||
             !ResourceLoader.Exists("res://assets/visual/shared/card_back.png") ||
             !ResourceLoader.Exists("res://assets/visual/cards/shared/fallback_front.svg") ||
             !ResourceLoader.Exists("res://assets/visual/portraits/midrange_commander.png") ||
             !ResourceLoader.Exists("res://assets/visual/portraits/advance_technarch.png"))
        {
            throw new InvalidOperationException(
                "Gate 4B visual catalog does not cover all 29 frozen definitions and shared faces.");
        }
        VerifyLeaderPortraitContract();
        VerifyGpuPrivacySentinelDetector();
        byte[] manifestBytes = Godot.FileAccess.GetFileAsBytes(AssetManifestPath);
        if (manifestBytes.Length == 0)
        {
            throw new InvalidOperationException(
                $"Gate 4B asset manifest is missing or empty: {AssetManifestPath}");
        }
        using (JsonDocument manifest = JsonDocument.Parse(manifestBytes))
        {
            if (!manifest.RootElement.TryGetProperty("assets", out JsonElement assets) ||
                assets.ValueKind != JsonValueKind.Array || assets.GetArrayLength() != 34)
            {
                throw new InvalidOperationException(
                    "Gate 4B-R1 asset manifest must contain 29 card illustrations, " +
                    "two leader portraits, and three shared product visuals (34 total).");
            }
        }
        assetManifestSha256 = Convert.ToHexString(SHA256.HashData(manifestBytes))
            .ToLowerInvariant();
        Directory.CreateDirectory(this.outputDirectory);
    }

    private static Vector2I? ResolveRequestedViewport(IReadOnlyList<string> arguments)
    {
        const string prefix = "--ci-visual-viewport=";
        string[] values = arguments
            .Where(argument => argument.StartsWith(prefix, StringComparison.Ordinal))
            .Select(argument => argument[prefix.Length..])
            .ToArray();
        if (values.Length == 0)
        {
            return null;
        }
        if (values.Length != 1)
        {
            throw new InvalidOperationException(
                "--ci-visual-viewport may be specified only once.");
        }
        string[] dimensions = values[0].Split('x', StringSplitOptions.TrimEntries);
        if (dimensions.Length != 2 ||
            !int.TryParse(dimensions[0], out int width) ||
            !int.TryParse(dimensions[1], out int height) ||
            (width, height) is not ((1280, 720) or (1600, 900) or
                                    (2560, 1440) or (2560, 1600)))
        {
            throw new InvalidOperationException(
                "--ci-visual-viewport must be 1280x720, 1600x900, 2560x1440, or 2560x1600.");
        }
        return new Vector2I(width, height);
    }

    private static void VerifyLeaderPortraitContract()
    {
        LeaderPortraitCatalog catalog = LeaderPortraitCatalog.Shared;
        MatchVisualIdentity sameDeck = MatchVisualIdentity.FromDecks(
            LeaderPortraitCatalog.MidrangeDeckId,
            LeaderPortraitCatalog.MidrangeDeckId,
            catalog);
        bool sameDeckSeats =
            sameDeck.ForPlayer(PlayerId.Player0).Faction == CardVisualFaction.Midrange &&
            sameDeck.ForPlayer(PlayerId.Player1).Faction == CardVisualFaction.Midrange &&
            sameDeck.ForPlayer(PlayerId.Player0).DeckId == LeaderPortraitCatalog.MidrangeDeckId &&
            sameDeck.ForPlayer(PlayerId.Player1).DeckId == LeaderPortraitCatalog.MidrangeDeckId;

        MatchVisualIdentity unknownDecks = MatchVisualIdentity.FromDecks(
            "__ci_unknown_player0",
            "__ci_unknown_player1",
            catalog);
        Texture2D neutralPortrait = catalog.LoadPortrait("__ci_unknown_player0");
        Texture2D midrangePortrait = catalog.LoadPortrait(
            LeaderPortraitCatalog.MidrangeDeckId);
        Texture2D advancePortrait = catalog.LoadPortrait(
            LeaderPortraitCatalog.AdvanceDeckId);
        bool valid = sameDeckSeats &&
                     unknownDecks.Player0.Faction == CardVisualFaction.Neutral &&
                     unknownDecks.Player1.Faction == CardVisualFaction.Neutral &&
                     neutralPortrait.GetWidth() > 0 && neutralPortrait.GetHeight() > 0 &&
                     midrangePortrait.GetWidth() > 0 && advancePortrait.GetWidth() > 0 &&
                     midrangePortrait.GetInstanceId() != advancePortrait.GetInstanceId();
        if (!valid)
        {
            throw new InvalidOperationException(
                "Gate 4B-R1 leader portraits failed same-deck, seat, or neutral-fallback runtime validation.");
        }
    }

    private static void VerifyGpuPrivacySentinelDetector()
    {
        using Image sentinel = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        sentinel.SetPixel(0, 0, new Color(1.0f, 0.0f, 1.0f, 1.0f));
        if (!ContainsGpuPrivacySentinel(sentinel))
        {
            throw new InvalidOperationException(
                "Gate 4B-R1 GPU privacy sentinel detector failed its #ff00ff self-check.");
        }
    }

    internal bool HasCapture(string state) => capturedStates.Contains(state);

    internal async Task CaptureAsync(
        string state,
        PlayerId? viewer = null,
        ulong? revision = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        if (!capturedStates.Add(state))
        {
            return;
        }

        await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
        await root.ToSignal(
            RenderingServer.Singleton,
            RenderingServer.SignalName.FramePostDraw);

        Image image = root.GetViewport().GetTexture().GetImage();
        int width = image.GetWidth();
        int height = image.GetHeight();
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException(
                $"Gate 4B visual capture {state} produced an empty viewport.");
        }
        if (state == "resolving" && ContainsGpuPrivacySentinel(image))
        {
            throw new InvalidOperationException(
                "The resolving screenshot contains the private GPU sentinel (#ff00ff).");
        }
        string safeState = string.Concat(state.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character == '-'
                ? character
                : '-'));
        string filename = $"{captureSequence++:00}-{safeState}.png";
        string path = Path.Combine(outputDirectory, filename);
        Error save = image.SavePng(path);
        if (save != Error.Ok)
        {
            throw new IOException(
                $"Godot could not save Gate 4B visual state {state} ({save}).");
        }
        string screenshotHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();
        captures.Add(new Gate4BVisualCapture
        {
            State = state,
            Viewer = viewer.HasValue ? checked((int)(uint)viewer.Value) : null,
            Revision = revision,
            Width = width,
            Height = height,
            File = filename,
            Sha256 = screenshotHash,
            AssetManifestSha256 = assetManifestSha256,
            Layout = MeasureLayout(state, width, height),
        });
        GD.Print(
            $"SCGS_GODOT_CI_VISUAL_CAPTURE_OK state={state} viewer={viewer?.ToString() ?? "none"} " +
            $"revision={revision?.ToString() ?? "none"} size={width}x{height} path={path}");
    }

    internal async Task RunPerformanceSmokeAsync()
    {
        if (performance is not null)
        {
            return;
        }

        const int warmupFrames = 300;
        const int measuredFrames = 300;
        for (int frame = 0; frame < warmupFrames; frame++)
        {
            await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        ResourceCounts before = CountVisualResources(root);
        var frameTimes = new List<double>(measuredFrames);
        for (int frame = 0; frame < measuredFrames; frame++)
        {
            long started = Stopwatch.GetTimestamp();
            await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
            frameTimes.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            ResourceCounts current = CountVisualResources(root);
            if (current.Actors > before.Actors || current.Materials > before.Materials ||
                current.Textures > before.Textures)
            {
                throw new InvalidOperationException(
                    $"Gate 4B visual resources grew at measured frame {frame + 1}: " +
                    $"actors {before.Actors}->{current.Actors}, " +
                    $"materials {before.Materials}->{current.Materials}, " +
                    $"textures {before.Textures}->{current.Textures}.");
            }
        }
        ResourceCounts after = CountVisualResources(root);
        frameTimes.Sort();
        int p95Index = Math.Clamp(
            (int)Math.Ceiling(frameTimes.Count * 0.95) - 1,
            0,
            frameTimes.Count - 1);
        string adapterName = RenderingServer.GetVideoAdapterName();
        RenderingDevice.DeviceType adapterType = RenderingServer.GetVideoAdapterType();
        performance = new Gate4BPerformanceEvidence
        {
            AdapterName = adapterName,
            AdapterType = adapterType.ToString(),
            TimingBudgetApplicable = IsTimingBudgetApplicable(adapterName, adapterType),
            WarmupFrames = warmupFrames,
            MeasuredFrames = measuredFrames,
            P95FrameMilliseconds = frameTimes[p95Index],
            MaxFrameMilliseconds = frameTimes[^1],
            ActorCountBefore = before.Actors,
            ActorCountAfter = after.Actors,
            MaterialCountBefore = before.Materials,
            MaterialCountAfter = after.Materials,
            TextureCountBefore = before.Textures,
            TextureCountAfter = after.Textures,
        };
        if (before != after)
        {
            throw new InvalidOperationException(
                "Gate 4B visual resources grew after warmup: " +
                $"actors {before.Actors}->{after.Actors}, " +
                $"materials {before.Materials}->{after.Materials}, " +
                $"textures {before.Textures}->{after.Textures}.");
        }
    }

    private static bool IsTimingBudgetApplicable(
        string adapterName,
        RenderingDevice.DeviceType adapterType)
    {
        if (adapterType == RenderingDevice.DeviceType.Cpu)
        {
            return false;
        }

        return !SoftwareAdapterNameMarkers.Any(marker =>
            adapterName.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    internal void Complete()
    {
        string[] missingStates = ExpectedStates
            .Where(state => !capturedStates.Contains(state))
            .ToArray();
        if (missingStates.Length != 0)
        {
            throw new InvalidOperationException(
                $"Gate 4B visual suite is missing states: {string.Join(", ", missingStates)}.");
        }
        Gate4BPerformanceEvidence measured = performance ??
            throw new InvalidOperationException(
                "Gate 4B visual suite completed without its 600-frame performance smoke.");
        Gate4BVisualCapture firstCapture = captures.FirstOrDefault() ??
            throw new InvalidOperationException("Gate 4B visual suite has no screenshots.");
        if (captures.Any(capture =>
                capture.Width != firstCapture.Width || capture.Height != firstCapture.Height))
        {
            throw new InvalidOperationException(
                "Gate 4B visual suite changed viewport size during capture.");
        }
        var report = new Gate4BVisualSuiteReport
        {
            AssetManifestSha256 = assetManifestSha256,
            Viewport = new Gate4BViewportSize
            {
                Width = firstCapture.Width,
                Height = firstCapture.Height,
            },
            Captures = captures.OrderBy(capture => capture.File, StringComparer.Ordinal).ToArray(),
            Performance = measured,
        };
        string reportPath = Path.Combine(outputDirectory, "visual-suite.json");
        string temporaryPath = $"{reportPath}.tmp-{System.Environment.ProcessId}";
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        try
        {
            File.WriteAllText(temporaryPath, json + System.Environment.NewLine, new UTF8Encoding(false));
            File.Move(temporaryPath, reportPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        GD.Print($"SCGS_GODOT_CI_VISUAL_SUITE_OK report={reportPath}");
    }

    private static ResourceCounts CountVisualResources(Node node)
    {
        int actors = 0;
        var materials = new HashSet<ulong>();
        var textures = new HashSet<ulong>();
        Visit(node);
        return new ResourceCounts(actors, materials.Count, textures.Count);

        void Visit(Node current)
        {
            if (current.GetType().Name.Contains("CardActor", StringComparison.Ordinal))
            {
                actors++;
            }
            switch (current)
            {
                case MeshInstance3D meshInstance:
                    AddMaterial(meshInstance.MaterialOverride);
                    if (meshInstance.Mesh is { } mesh)
                    {
                        for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
                        {
                            AddMaterial(mesh.SurfaceGetMaterial(surface));
                        }
                    }
                    break;
                case Sprite2D sprite:
                    AddTexture(sprite.Texture);
                    break;
                case Sprite3D sprite:
                    AddTexture(sprite.Texture);
                    break;
                case TextureRect textureRect:
                    AddTexture(textureRect.Texture);
                    break;
            }
            foreach (Node child in current.GetChildren())
            {
                Visit(child);
            }
        }

        void AddMaterial(Material? material)
        {
            if (material is null || !materials.Add(material.GetInstanceId()))
            {
                return;
            }
            if (material is BaseMaterial3D baseMaterial)
            {
                AddTexture(baseMaterial.AlbedoTexture);
                AddTexture(baseMaterial.NormalTexture);
                AddTexture(baseMaterial.EmissionTexture);
                AddTexture(baseMaterial.OrmTexture);
            }
        }

        void AddTexture(Texture2D? texture)
        {
            if (texture is not null)
            {
                textures.Add(texture.GetInstanceId());
            }
        }
    }

    private static bool ContainsGpuPrivacySentinel(Image image)
    {
        if (image.GetFormat() != Image.Format.Rgba8)
        {
            image.Convert(Image.Format.Rgba8);
        }
        byte[] pixels = image.GetData();
        for (int index = 0; index + 3 < pixels.Length; index += 4)
        {
            if (pixels[index] >= 250 && pixels[index + 1] <= 5 &&
                pixels[index + 2] >= 250 && pixels[index + 3] >= 250)
            {
                return true;
            }
        }
        return false;
    }

    private Gate4BLayoutEvidence MeasureLayout(string state, int width, int height)
    {
        var controls = new List<Control>();
        CollectControls(root, controls);
        Control[] visible = controls
            .Where(control => control.IsVisibleInTree() &&
                              control.Size.X > 1.0f && control.Size.Y > 1.0f)
            .ToArray();
        Transform2D stretchTransform = root.GetViewport().GetStretchTransform();
        var viewportRect = new Rect2(Vector2.Zero, new Vector2(width, height));
        Rect2 generousViewport = viewportRect.Grow(2.0f);
        string[] trackedNames =
        [
            "BattlefieldCardDetails",
            "BattlefieldControlRail",
            "InteractionDock",
            "EndTurnButton",
            "PhaseCapsule",
            "OwnStatusPod",
            "OpponentStatusPod",
            "LogButton",
            "ReactionOverlay",
            "StandbyTray",
            "DirectActionPanel",
        ];
        Control[] tracked = visible
            .Where(control => trackedNames.Contains(control.Name.ToString(), StringComparer.Ordinal))
            .ToArray();
        Control[] outsideViewport = tracked
            .Where(control => !generousViewport.Encloses(
                GetScreenRect(control, stretchTransform)))
            .ToArray();
        bool controlsInsideViewport = outsideViewport.Length == 0;

        bool overlapFree = true;
        for (int first = 0; first < tracked.Length && overlapFree; first++)
        {
            for (int second = first + 1; second < tracked.Length; second++)
            {
                Control left = tracked[first];
                Control right = tracked[second];
                if (left.IsAncestorOf(right) || right.IsAncestorOf(left) ||
                    !ShouldBeDisjoint(left.Name.ToString(), right.Name.ToString()))
                {
                    continue;
                }
                Rect2 intersection = GetScreenRect(left, stretchTransform)
                    .Intersection(GetScreenRect(right, stretchTransform));
                if (intersection.Size.X * intersection.Size.Y > 16.0f)
                {
                    overlapFree = false;
                    break;
                }
            }
        }

        Control[] opaqueFullHeight = visible.Where(control =>
            IsOpaqueFullHeightPanel(
                control,
                state,
                width,
                height,
                stretchTransform)).ToArray();
        int opaqueFullHeightPanels = opaqueFullHeight.Length;
        int glassSurfaces = visible
            .OfType<PanelContainer>()
            .Count(panel => panel.ThemeTypeVariation.ToString()
                .StartsWith("Glass", StringComparison.Ordinal));
        string[] debugNames = ["ViewerLabel", "RevisionLabel", "MatchMetaLabel", "PrivacyProof"];
        int visibleDebugLabels = visible
            .OfType<Label>()
            .Count(label => debugNames.Contains(label.Name.ToString(), StringComparer.Ordinal) &&
                            !string.IsNullOrWhiteSpace(label.Text));
        Battlefield3DPresenter? battlefield = root.FindChild(
            "Battlefield3D",
            recursive: true,
            owned: false) as Battlefield3DPresenter;
        Rect2 projectedBoard = battlefield?.IsVisibleInTree() == true
            ? TransformRect(stretchTransform, battlefield.CiProjectedBoardRect)
            : new Rect2();
        if (state == "mulligan" && battlefield is not null &&
            root.FindChild("InteractionDock", recursive: true, owned: false) is Control tray &&
            tray.IsVisibleInTree())
        {
            // The batch tray is 2D while the selectable hand is projected from
            // 3D. Include their real screen-space bounds in the same overlap
            // contract so a visually translucent tray cannot silently consume
            // the cards' click targets.
            Rect2 handIntersection = GetScreenRect(tray, stretchTransform)
                .Intersection(TransformRect(
                    stretchTransform,
                    battlefield.CiOwnHandScreenRect));
            overlapFree &= handIntersection.Size.X * handIntersection.Size.Y <= 16.0f;
        }
        Rect2 visibleProjectedBoard = projectedBoard.Intersection(viewportRect);
        return new Gate4BLayoutEvidence
        {
            ControlsInsideViewport = controlsInsideViewport,
            HudRegionsOverlapFree = overlapFree,
            OpaqueFullHeightPanelCount = opaqueFullHeightPanels,
            GlassSurfaceCount = glassSurfaces,
            VisibleDebugLabelCount = visibleDebugLabels,
            BattlefieldWidthRatio = Math.Clamp(
                visibleProjectedBoard.Size.X / width,
                0.0f,
                1.0f),
            BattlefieldHeightRatio = Math.Clamp(
                visibleProjectedBoard.Size.Y / height,
                0.0f,
                1.0f),
        };
    }

    private static void CollectControls(Node node, ICollection<Control> output)
    {
        if (node is Control control)
        {
            output.Add(control);
        }
        foreach (Node child in node.GetChildren())
        {
            CollectControls(child, output);
        }
    }

    private static bool IsOpaqueFullHeightPanel(
        Control control,
        string state,
        int width,
        int height,
        Transform2D stretchTransform)
    {
        Rect2 rect = GetScreenRect(control, stretchTransform);
        if (rect.Size.Y < height * 0.88f || rect.Size.X < 100.0f ||
            rect.Size.X > width * 0.55f)
        {
            return false;
        }
        if (state == "covered" && control.Name == "OpaqueBackground")
        {
            return false;
        }
        Color? color = control switch
        {
            ColorRect colorRect => colorRect.Color,
            PanelContainer panel when panel.GetThemeStylebox("panel") is StyleBoxFlat flat =>
                flat.BgColor,
            _ => null,
        };
        return color is { A: >= 0.88f } value &&
               (value.R + value.G + value.B) / 3.0f < 0.18f;
    }

    private static Rect2 GetScreenRect(Control control, Transform2D stretchTransform) =>
        TransformRect(stretchTransform, control.GetGlobalRect());

    private static Rect2 TransformRect(Transform2D transform, Rect2 rect)
    {
        Vector2[] corners =
        [
            transform * rect.Position,
            transform * new Vector2(rect.End.X, rect.Position.Y),
            transform * rect.End,
            transform * new Vector2(rect.Position.X, rect.End.Y),
        ];
        float minX = corners.Min(point => point.X);
        float minY = corners.Min(point => point.Y);
        float maxX = corners.Max(point => point.X);
        float maxY = corners.Max(point => point.Y);
        return new Rect2(minX, minY, maxX - minX, maxY - minY);
    }

    private static bool ShouldBeDisjoint(string first, string second)
    {
        var pair = new HashSet<string>(StringComparer.Ordinal) { first, second };
        if (pair.SetEquals(["OwnStatusPod", "OpponentStatusPod"]))
        {
            return true;
        }
        if (pair.Contains("BattlefieldCardDetails"))
        {
            return pair.Overlaps(
                ["BattlefieldControlRail", "InteractionDock", "EndTurnButton",
                 "PhaseCapsule", "OwnStatusPod", "OpponentStatusPod", "LogButton"]);
        }
        if (pair.Contains("InteractionDock"))
        {
            return pair.Overlaps(["OwnStatusPod", "OpponentStatusPod", "EndTurnButton"]);
        }
        return false;
    }

    private readonly record struct ResourceCounts(int Actors, int Materials, int Textures);
}

internal sealed record Gate4BVisualSuiteReport
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 3;

    [JsonPropertyName("gate")]
    public string Gate { get; init; } = "4B-R1";

    [JsonPropertyName("scenario")]
    public string Scenario { get; init; } = "visual-suite";

    [JsonPropertyName("asset_manifest_sha256")]
    public required string AssetManifestSha256 { get; init; }

    [JsonPropertyName("viewport")]
    public required Gate4BViewportSize Viewport { get; init; }

    [JsonPropertyName("captures")]
    public required IReadOnlyList<Gate4BVisualCapture> Captures { get; init; }

    [JsonPropertyName("performance")]
    public required Gate4BPerformanceEvidence Performance { get; init; }
}

internal sealed record Gate4BViewportSize
{
    [JsonPropertyName("width")]
    public required int Width { get; init; }

    [JsonPropertyName("height")]
    public required int Height { get; init; }
}

internal sealed record Gate4BVisualCapture
{
    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("viewer")]
    public int? Viewer { get; init; }

    [JsonPropertyName("revision")]
    public ulong? Revision { get; init; }

    [JsonPropertyName("width")]
    public required int Width { get; init; }

    [JsonPropertyName("height")]
    public required int Height { get; init; }

    [JsonPropertyName("file")]
    public required string File { get; init; }

    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    [JsonPropertyName("asset_manifest_sha256")]
    public required string AssetManifestSha256 { get; init; }

    [JsonPropertyName("layout")]
    public required Gate4BLayoutEvidence Layout { get; init; }
}

internal sealed record Gate4BLayoutEvidence
{
    [JsonPropertyName("controls_inside_viewport")]
    public required bool ControlsInsideViewport { get; init; }

    [JsonPropertyName("hud_regions_overlap_free")]
    public required bool HudRegionsOverlapFree { get; init; }

    [JsonPropertyName("opaque_full_height_panel_count")]
    public required int OpaqueFullHeightPanelCount { get; init; }

    [JsonPropertyName("glass_surface_count")]
    public required int GlassSurfaceCount { get; init; }

    [JsonPropertyName("visible_debug_label_count")]
    public required int VisibleDebugLabelCount { get; init; }

    [JsonPropertyName("battlefield_width_ratio")]
    public required double BattlefieldWidthRatio { get; init; }

    [JsonPropertyName("battlefield_height_ratio")]
    public required double BattlefieldHeightRatio { get; init; }
}

internal sealed record Gate4BPerformanceEvidence
{
    [JsonPropertyName("adapter_name")]
    public required string AdapterName { get; init; }

    [JsonPropertyName("adapter_type")]
    public required string AdapterType { get; init; }

    [JsonPropertyName("timing_budget_applicable")]
    public required bool TimingBudgetApplicable { get; init; }

    [JsonPropertyName("warmup_frames")]
    public required int WarmupFrames { get; init; }

    [JsonPropertyName("measured_frames")]
    public required int MeasuredFrames { get; init; }

    [JsonPropertyName("p95_frame_ms")]
    public required double P95FrameMilliseconds { get; init; }

    [JsonPropertyName("max_frame_ms")]
    public required double MaxFrameMilliseconds { get; init; }

    [JsonPropertyName("actor_count_before")]
    public required int ActorCountBefore { get; init; }

    [JsonPropertyName("actor_count_after")]
    public required int ActorCountAfter { get; init; }

    [JsonPropertyName("material_count_before")]
    public required int MaterialCountBefore { get; init; }

    [JsonPropertyName("material_count_after")]
    public required int MaterialCountAfter { get; init; }

    [JsonPropertyName("texture_count_before")]
    public required int TextureCountBefore { get; init; }

    [JsonPropertyName("texture_count_after")]
    public required int TextureCountAfter { get; init; }
}
