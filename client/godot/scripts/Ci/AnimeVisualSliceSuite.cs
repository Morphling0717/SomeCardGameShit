// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.GodotClient.Preview;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scgs.GodotClient.Ci;

internal sealed class AnimeVisualSliceSuite
{
    private static readonly HashSet<Vector2I> AllowedViewports =
    [
        new Vector2I(1280, 720),
        new Vector2I(1600, 900),
        new Vector2I(2560, 1440),
        new Vector2I(2560, 1600),
    ];

    private readonly AnimeStyleSliceScreen _screen;
    private readonly string _outputDirectory;
    private readonly DisplayServer.VSyncMode _previousVsync;

    internal AnimeVisualSliceSuite(AnimeStyleSliceScreen screen, string outputDirectory)
    {
        _screen = screen ?? throw new ArgumentNullException(nameof(screen));
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        if (!Path.IsPathFullyQualified(outputDirectory))
        {
            throw new ArgumentException("Anime visual-slice output must be absolute.", nameof(outputDirectory));
        }
        if (string.Equals(DisplayServer.GetName(), "headless", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "--anime-style-slice=<directory> requires a display-backed Compatibility renderer.");
        }

        _outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(_outputDirectory);
        _previousVsync = DisplayServer.WindowGetVsyncMode();
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        if (ResolveRequestedViewport(OS.GetCmdlineUserArgs()) is { } viewport)
        {
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
            DisplayServer.WindowSetPosition(Vector2I.Zero);
            DisplayServer.WindowSetSize(viewport);
        }
    }

    internal async Task<string> RunAsync()
    {
        try
        {
            await ReadCompletedFrameAsync();
            var captures = new List<AnimeSliceCapture>();
            int sequence = 0;
            foreach (string state in AnimeStyleSliceScreen.States)
            {
                _screen.SetPreviewState(state);
                using Image first = await ReadCompletedFrameAsync();
                using Image second = await ReadCompletedFrameAsync();
                if (first.GetWidth() != second.GetWidth() || first.GetHeight() != second.GetHeight())
                {
                    throw new InvalidOperationException(
                        $"Anime visual-slice state {state} changed viewport between complete frames.");
                }

                AnimeSliceLayoutEvidence layout = _screen.MeasureLayout();
                ValidateRuntimeEvidence(layout, second.GetWidth(), second.GetHeight());
                string filename = $"{sequence++:00}-{state}.png";
                string path = Path.Combine(_outputDirectory, filename);
                Error save = second.SavePng(path);
                if (save != Error.Ok)
                {
                    throw new IOException($"Godot could not save AnimeV1 state {state} ({save}).");
                }
                AnimeSliceReadabilityEvidence readability = BuildReadabilityEvidence(
                    state,
                    layout,
                    second);
                string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
                    .ToLowerInvariant();
                captures.Add(new AnimeSliceCapture
                {
                    State = state,
                    File = filename,
                    Sha256 = hash,
                    Width = second.GetWidth(),
                    Height = second.GetHeight(),
                    CompleteFramePostDraws = 2,
                    Layout = layout,
                    ReadabilityEvidence = readability,
                });
                GD.Print($"SCGS_ANIME_VISUAL_CAPTURE_OK state={state} size={second.GetWidth()}x{second.GetHeight()} path={path}");
            }

            string[] loaded = AnimeVisualAssetCatalog.LoadedPaths().ToArray();
            string[] missing = AnimeVisualAssetCatalog.RequiredPaths
                .Except(loaded, StringComparer.Ordinal)
                .ToArray();
            var report = new AnimeVisualSliceReport
            {
                Viewport = new AnimeSliceViewport
                {
                    Width = captures[0].Width,
                    Height = captures[0].Height,
                },
                AssetContract = new AnimeSliceAssetContract
                {
                    RequiredPaths = AnimeVisualAssetCatalog.RequiredPaths,
                    LoadedPaths = loaded,
                    MissingPaths = missing,
                    Complete = missing.Length == 0,
                },
                Captures = captures,
            };
            string reportPath = Path.Combine(_outputDirectory, "anime-visual-slice.json");
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
            return reportPath;
        }
        finally
        {
            DisplayServer.WindowSetVsyncMode(_previousVsync);
        }
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
            throw new InvalidOperationException("--ci-visual-viewport may be specified only once.");
        }
        string[] parts = values[0].Split('x', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out int width) ||
            !int.TryParse(parts[1], out int height) ||
            !AllowedViewports.Contains(new Vector2I(width, height)))
        {
            throw new InvalidOperationException(
                "AnimeV1 screenshots support 1280x720, 1600x900, 2560x1440, or 2560x1600.");
        }
        return new Vector2I(width, height);
    }

    private static void ValidateRuntimeEvidence(AnimeSliceLayoutEvidence layout, int width, int height)
    {
        if (layout.Viewport.Width < 1.0f || layout.Viewport.Height < 1.0f ||
            width < 1 || height < 1 || layout.UsesNativeSession || layout.HasOuterTableFrame ||
            layout.HiddenCardsWithIdentity != 0)
        {
            throw new InvalidOperationException(
                $"AnimeV1 layout contract failed for {layout.State}.");
        }
        bool battle = layout.State is AnimeStyleSliceScreen.StateAction or
            AnimeStyleSliceScreen.StateHandHover or
            AnimeStyleSliceScreen.StateMixed or
            AnimeStyleSliceScreen.StateReaction;
        if (battle &&
            (layout.Board.Width < layout.Viewport.Width * 0.45f ||
             layout.Board.Height < layout.Viewport.Height * 0.78f ||
             layout.MainBoardSlotCount != 10 || layout.TacticSlotCount != 6 ||
             layout.FieldSlotCount != 2 || layout.HiddenCardCount != 5))
        {
            throw new InvalidOperationException(
                $"AnimeV1 battle composition is incomplete for {layout.State}.");
        }
        if (layout.State == AnimeStyleSliceScreen.StateMixed)
        {
            string[] requiredKinds = ["Amulet", "Field", "Follower", "Spell", "Trap"];
            if (!requiredKinds.All(layout.VisibleCardKinds.Contains))
            {
                throw new InvalidOperationException(
                    "The mixed-permanents sample does not expose all five product card silhouettes.");
            }
        }
        if (layout.State == AnimeStyleSliceScreen.StateCovered &&
            (!layout.CoveredOpaque || layout.VisibleCardCount != 0 || layout.HiddenCardCount != 0))
        {
            throw new InvalidOperationException(
                "The hot-seat cover is not opaque and identity-free.");
        }
    }

    private AnimeSliceReadabilityEvidence BuildReadabilityEvidence(
        string state,
        AnimeSliceLayoutEvidence layout,
        Image screenshot)
    {
        Rect2 logicalViewport = ToRect(layout.Viewport);
        Rect2 safeArea = new(
            logicalViewport.Position + new Vector2(4.0f, 4.0f),
            logicalViewport.Size - new Vector2(8.0f, 8.0f));
        AnimeCardPreview[] cards = EnumerateCards(_screen)
            .Where(card => !card.IsHidden)
            .ToArray();
        var handCards = new List<AnimeSliceHandCardEvidence>();
        bool battle = state is AnimeStyleSliceScreen.StateAction or
            AnimeStyleSliceScreen.StateHandHover or
            AnimeStyleSliceScreen.StateMixed or
            AnimeStyleSliceScreen.StateReaction;
        if (battle)
        {
            AnimeCardPreview[] hand = cards
                .Where(card => card.Name.ToString().StartsWith("NearHand", StringComparison.Ordinal))
                .OrderBy(card => card.Name.ToString(), StringComparer.Ordinal)
                .ToArray();
            if (hand.Length != 5 || hand.Where((card, index) => card.Name != $"NearHand{index}").Any())
            {
                throw new InvalidOperationException(
                    $"AnimeV1 {state} must expose the five canonical real hand nodes.");
            }
            foreach (AnimeCardPreview card in hand)
            {
                Rect2 cardRect = card.VisualScreenRect;
                bool cardInside = Contains(safeArea, cardRect);
                if (!cardInside || card.BadgeFontPixelSize < 16)
                {
                    throw new InvalidOperationException(
                        $"AnimeV1 hand readability failed for {card.Name} in {state}.");
                }
                handCards.Add(new AnimeSliceHandCardEvidence
                {
                    NodeName = card.Name,
                    DesignId = card.DesignId,
                    Kind = card.Kind.ToString(),
                    CardRect = AnimeSliceRect.From(cardRect),
                    CardInsideSafeArea = cardInside,
                    BadgeFontPixelSize = card.BadgeFontPixelSize,
                    Badges =
                    [
                        BuildBadgeEvidence("cost", card.CostBadgeScreenRect, cardRect, safeArea, logicalViewport, screenshot),
                        BuildBadgeEvidence("attack", card.AttackBadgeScreenRect, cardRect, safeArea, logicalViewport, screenshot),
                        BuildBadgeEvidence("health", card.HealthBadgeScreenRect, cardRect, safeArea, logicalViewport, screenshot),
                        BuildBadgeEvidence("countdown", card.CountdownBadgeScreenRect, cardRect, safeArea, logicalViewport, screenshot),
                    ],
                });
            }
        }

        var typeMarkers = new List<AnimeSliceTypeMarkerEvidence>();
        if (state == AnimeStyleSliceScreen.StateMixed)
        {
            foreach (AnimeCardKind kind in Enum.GetValues<AnimeCardKind>())
            {
                AnimeCardPreview card = cards
                    .Where(candidate => candidate.Kind == kind)
                    .OrderByDescending(candidate =>
                        candidate.VisualScreenRect.Size.X * candidate.VisualScreenRect.Size.Y)
                    .ThenBy(candidate => candidate.Name.ToString(), StringComparer.Ordinal)
                    .FirstOrDefault()
                    ?? throw new InvalidOperationException(
                        $"AnimeV1 mixed state has no visible {kind} card for marker evidence.");
                Rect2 cardRect = card.VisualScreenRect;
                Rect2 roi = card.TypeMarkerScreenRect;
                bool inside = Contains(safeArea, roi) && Contains(cardRect, roi);
                if (!inside || roi.Size.X < 24.0f || roi.Size.Y < 24.0f)
                {
                    throw new InvalidOperationException(
                        $"AnimeV1 {kind} marker is not a large safe-area marker.");
                }
                AnimeSlicePixelEvidence pixels = BuildPixelEvidence(
                    screenshot,
                    logicalViewport,
                    roi);
                ValidatePixelContrast(pixels, $"{kind} type marker");
                typeMarkers.Add(new AnimeSliceTypeMarkerEvidence
                {
                    Kind = kind.ToString(),
                    NodeName = card.Name,
                    DesignId = card.DesignId,
                    Glyph = card.TypeMarkerGlyph,
                    Shape = card.TypeMarkerShape,
                    CardRect = AnimeSliceRect.From(cardRect),
                    Roi = AnimeSliceRect.From(roi),
                    InsideSafeArea = inside,
                    Pixels = pixels,
                });
            }
        }
        return new AnimeSliceReadabilityEvidence
        {
            SafeArea = AnimeSliceRect.From(safeArea),
            HandCards = handCards,
            TypeMarkers = typeMarkers,
        };
    }

    private static AnimeSliceBadgeEvidence BuildBadgeEvidence(
        string role,
        Rect2? roi,
        Rect2 cardRect,
        Rect2 safeArea,
        Rect2 logicalViewport,
        Image screenshot)
    {
        if (roi is not { } presentRoi)
        {
            return new AnimeSliceBadgeEvidence
            {
                Role = role,
                Present = false,
                Roi = null,
                InsideSafeArea = false,
                Pixels = null,
            };
        }
        bool inside = Contains(safeArea, presentRoi) && Contains(cardRect, presentRoi);
        if (!inside)
        {
            throw new InvalidOperationException($"AnimeV1 {role} badge escapes its card or safe area.");
        }
        AnimeSlicePixelEvidence pixels = BuildPixelEvidence(screenshot, logicalViewport, presentRoi);
        ValidatePixelContrast(pixels, $"{role} badge");
        return new AnimeSliceBadgeEvidence
        {
            Role = role,
            Present = true,
            Roi = AnimeSliceRect.From(presentRoi),
            InsideSafeArea = true,
            Pixels = pixels,
        };
    }

    private static AnimeSlicePixelEvidence BuildPixelEvidence(
        Image screenshot,
        Rect2 logicalViewport,
        Rect2 roi)
    {
        float scaleX = screenshot.GetWidth() / logicalViewport.Size.X;
        float scaleY = screenshot.GetHeight() / logicalViewport.Size.Y;
        int x = (int)MathF.Floor((roi.Position.X - logicalViewport.Position.X) * scaleX);
        int y = (int)MathF.Floor((roi.Position.Y - logicalViewport.Position.Y) * scaleY);
        int endX = (int)MathF.Ceiling((roi.End.X - logicalViewport.Position.X) * scaleX);
        int endY = (int)MathF.Ceiling((roi.End.Y - logicalViewport.Position.Y) * scaleY);
        int width = endX - x;
        int height = endY - y;
        if (x < 0 || y < 0 || width < 1 || height < 1 ||
            endX > screenshot.GetWidth() || endY > screenshot.GetHeight())
        {
            throw new InvalidOperationException("AnimeV1 evidence ROI escapes the physical screenshot.");
        }

        byte[] rgba = new byte[checked(width * height * 4)];
        int[] luminance = new int[checked(width * height)];
        var quantized = new HashSet<int>();
        int pixelIndex = 0;
        for (int row = y; row < endY; row++)
        {
            for (int column = x; column < endX; column++)
            {
                Color color = screenshot.GetPixel(column, row);
                byte red = ToByte(color.R);
                byte green = ToByte(color.G);
                byte blue = ToByte(color.B);
                byte alpha = ToByte(color.A);
                int byteIndex = pixelIndex * 4;
                rgba[byteIndex] = red;
                rgba[byteIndex + 1] = green;
                rgba[byteIndex + 2] = blue;
                rgba[byteIndex + 3] = alpha;
                luminance[pixelIndex] = ((54 * red) + (183 * green) + (19 * blue) + 128) >> 8;
                quantized.Add(
                    ((red >> 4) << 12) |
                    ((green >> 4) << 8) |
                    ((blue >> 4) << 4) |
                    (alpha >> 4));
                pixelIndex++;
            }
        }
        int edgeCount = 0;
        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                int index = (row * width) + column;
                if (column + 1 < width && Math.Abs(luminance[index] - luminance[index + 1]) >= 24)
                {
                    edgeCount++;
                }
                if (row + 1 < height && Math.Abs(luminance[index] - luminance[index + width]) >= 24)
                {
                    edgeCount++;
                }
            }
        }
        int minimum = luminance.Min();
        int maximum = luminance.Max();
        return new AnimeSlicePixelEvidence
        {
            PhysicalX = x,
            PhysicalY = y,
            PhysicalWidth = width,
            PhysicalHeight = height,
            SampleCount = width * height,
            QuantizedColorCount = quantized.Count,
            LuminanceMin8 = minimum,
            LuminanceMax8 = maximum,
            LuminanceRange8 = maximum - minimum,
            GrayscaleEdgeCount = edgeCount,
            PixelSha256 = Convert.ToHexString(SHA256.HashData(rgba)).ToLowerInvariant(),
        };
    }

    private static void ValidatePixelContrast(AnimeSlicePixelEvidence pixels, string label)
    {
        if (pixels.QuantizedColorCount < 4 || pixels.LuminanceRange8 < 28 ||
            pixels.GrayscaleEdgeCount < Math.Max(8, pixels.SampleCount / 80))
        {
            throw new InvalidOperationException($"AnimeV1 {label} lacks non-color pixel evidence.");
        }
    }

    private static byte ToByte(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255.0f), 0, 255);

    private static bool Contains(Rect2 container, Rect2 child)
    {
        const float tolerance = 0.25f;
        return child.Size.X > 0.0f && child.Size.Y > 0.0f &&
            child.Position.X >= container.Position.X - tolerance &&
            child.Position.Y >= container.Position.Y - tolerance &&
            child.End.X <= container.End.X + tolerance &&
            child.End.Y <= container.End.Y + tolerance;
    }

    private static Rect2 ToRect(AnimeSliceRect rect) =>
        new(rect.X, rect.Y, rect.Width, rect.Height);

    private static IEnumerable<AnimeCardPreview> EnumerateCards(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is AnimeCardPreview card)
            {
                yield return card;
            }
            foreach (AnimeCardPreview descendant in EnumerateCards(child))
            {
                yield return descendant;
            }
        }
    }

    private async Task<Image> ReadCompletedFrameAsync()
    {
        await _screen.ToSignal(_screen.GetTree(), SceneTree.SignalName.ProcessFrame);
        await _screen.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        return _screen.GetViewport().GetTexture().GetImage();
    }
}

internal sealed record AnimeVisualSliceReport
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 2;

    [JsonPropertyName("gate")]
    public string Gate { get; init; } = "6A";

    [JsonPropertyName("scenario")]
    public string Scenario { get; init; } = "anime-style-slice";

    [JsonPropertyName("visual_profile")]
    public string VisualProfile { get; init; } = "anime-v1-proposal";

    [JsonPropertyName("approval_status")]
    public string ApprovalStatus { get; init; } = "pending_user_approval";

    [JsonPropertyName("uses_native_session")]
    public bool UsesNativeSession { get; init; }

    [JsonPropertyName("default_product_path_unchanged")]
    public bool DefaultProductPathUnchanged { get; init; } = true;

    [JsonPropertyName("viewport")]
    public required AnimeSliceViewport Viewport { get; init; }

    [JsonPropertyName("asset_contract")]
    public required AnimeSliceAssetContract AssetContract { get; init; }

    [JsonPropertyName("captures")]
    public required IReadOnlyList<AnimeSliceCapture> Captures { get; init; }
}

internal sealed record AnimeSliceViewport
{
    [JsonPropertyName("width")]
    public required int Width { get; init; }

    [JsonPropertyName("height")]
    public required int Height { get; init; }
}

internal sealed record AnimeSliceAssetContract
{
    [JsonPropertyName("required_paths")]
    public required IReadOnlyList<string> RequiredPaths { get; init; }

    [JsonPropertyName("loaded_paths")]
    public required IReadOnlyList<string> LoadedPaths { get; init; }

    [JsonPropertyName("missing_paths")]
    public required IReadOnlyList<string> MissingPaths { get; init; }

    [JsonPropertyName("complete")]
    public required bool Complete { get; init; }
}

internal sealed record AnimeSliceCapture
{
    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("file")]
    public required string File { get; init; }

    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    [JsonPropertyName("width")]
    public required int Width { get; init; }

    [JsonPropertyName("height")]
    public required int Height { get; init; }

    [JsonPropertyName("complete_frame_post_draws")]
    public required int CompleteFramePostDraws { get; init; }

    [JsonPropertyName("layout")]
    public required AnimeSliceLayoutEvidence Layout { get; init; }

    [JsonPropertyName("readability_evidence")]
    public required AnimeSliceReadabilityEvidence ReadabilityEvidence { get; init; }
}

internal sealed record AnimeSliceReadabilityEvidence
{
    [JsonPropertyName("safe_area")]
    public required AnimeSliceRect SafeArea { get; init; }

    [JsonPropertyName("hand_cards")]
    public required IReadOnlyList<AnimeSliceHandCardEvidence> HandCards { get; init; }

    [JsonPropertyName("type_markers")]
    public required IReadOnlyList<AnimeSliceTypeMarkerEvidence> TypeMarkers { get; init; }
}

internal sealed record AnimeSliceHandCardEvidence
{
    [JsonPropertyName("node_name")]
    public required string NodeName { get; init; }

    [JsonPropertyName("design_id")]
    public required string DesignId { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("card_rect")]
    public required AnimeSliceRect CardRect { get; init; }

    [JsonPropertyName("card_inside_safe_area")]
    public required bool CardInsideSafeArea { get; init; }

    [JsonPropertyName("badge_font_pixel_size")]
    public required int BadgeFontPixelSize { get; init; }

    [JsonPropertyName("badges")]
    public required IReadOnlyList<AnimeSliceBadgeEvidence> Badges { get; init; }
}

internal sealed record AnimeSliceBadgeEvidence
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("present")]
    public required bool Present { get; init; }

    [JsonPropertyName("roi")]
    public AnimeSliceRect? Roi { get; init; }

    [JsonPropertyName("inside_safe_area")]
    public required bool InsideSafeArea { get; init; }

    [JsonPropertyName("pixels")]
    public AnimeSlicePixelEvidence? Pixels { get; init; }
}

internal sealed record AnimeSliceTypeMarkerEvidence
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("node_name")]
    public required string NodeName { get; init; }

    [JsonPropertyName("design_id")]
    public required string DesignId { get; init; }

    [JsonPropertyName("glyph")]
    public required string Glyph { get; init; }

    [JsonPropertyName("shape")]
    public required string Shape { get; init; }

    [JsonPropertyName("card_rect")]
    public required AnimeSliceRect CardRect { get; init; }

    [JsonPropertyName("roi")]
    public required AnimeSliceRect Roi { get; init; }

    [JsonPropertyName("inside_safe_area")]
    public required bool InsideSafeArea { get; init; }

    [JsonPropertyName("pixels")]
    public required AnimeSlicePixelEvidence Pixels { get; init; }
}

internal sealed record AnimeSlicePixelEvidence
{
    [JsonPropertyName("physical_x")]
    public required int PhysicalX { get; init; }

    [JsonPropertyName("physical_y")]
    public required int PhysicalY { get; init; }

    [JsonPropertyName("physical_width")]
    public required int PhysicalWidth { get; init; }

    [JsonPropertyName("physical_height")]
    public required int PhysicalHeight { get; init; }

    [JsonPropertyName("sample_count")]
    public required int SampleCount { get; init; }

    [JsonPropertyName("quantized_color_count")]
    public required int QuantizedColorCount { get; init; }

    [JsonPropertyName("luminance_min_8")]
    public required int LuminanceMin8 { get; init; }

    [JsonPropertyName("luminance_max_8")]
    public required int LuminanceMax8 { get; init; }

    [JsonPropertyName("luminance_range_8")]
    public required int LuminanceRange8 { get; init; }

    [JsonPropertyName("grayscale_edge_count")]
    public required int GrayscaleEdgeCount { get; init; }

    [JsonPropertyName("pixel_sha256")]
    public required string PixelSha256 { get; init; }
}

internal sealed record AnimeSliceLayoutEvidence
{
    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("viewport")]
    public required AnimeSliceRect Viewport { get; init; }

    [JsonPropertyName("board")]
    public required AnimeSliceRect Board { get; init; }

    [JsonPropertyName("left_panel")]
    public required AnimeSliceRect LeftPanel { get; init; }

    [JsonPropertyName("right_panel")]
    public required AnimeSliceRect RightPanel { get; init; }

    [JsonPropertyName("has_outer_table_frame")]
    public required bool HasOuterTableFrame { get; init; }

    [JsonPropertyName("uses_native_session")]
    public required bool UsesNativeSession { get; init; }

    [JsonPropertyName("main_board_slot_count")]
    public required int MainBoardSlotCount { get; init; }

    [JsonPropertyName("tactic_slot_count")]
    public required int TacticSlotCount { get; init; }

    [JsonPropertyName("field_slot_count")]
    public required int FieldSlotCount { get; init; }

    [JsonPropertyName("visible_card_count")]
    public required int VisibleCardCount { get; init; }

    [JsonPropertyName("hidden_card_count")]
    public required int HiddenCardCount { get; init; }

    [JsonPropertyName("hidden_cards_with_identity")]
    public required int HiddenCardsWithIdentity { get; init; }

    [JsonPropertyName("visible_card_kinds")]
    public required IReadOnlyList<string> VisibleCardKinds { get; init; }

    [JsonPropertyName("covered_opaque")]
    public required bool CoveredOpaque { get; init; }

    [JsonPropertyName("loaded_asset_count")]
    public required int LoadedAssetCount { get; init; }

    [JsonPropertyName("required_asset_count")]
    public required int RequiredAssetCount { get; init; }
}

internal sealed record AnimeSliceRect
{
    [JsonPropertyName("x")]
    public required float X { get; init; }

    [JsonPropertyName("y")]
    public required float Y { get; init; }

    [JsonPropertyName("width")]
    public required float Width { get; init; }

    [JsonPropertyName("height")]
    public required float Height { get; init; }

    internal static AnimeSliceRect From(Rect2 rect) => new()
    {
        X = rect.Position.X,
        Y = rect.Position.Y,
        Width = rect.Size.X,
        Height = rect.Size.Y,
    };
}
