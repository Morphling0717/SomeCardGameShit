// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using Godot;
using Scgs.GodotClient.CardFaces;

namespace Scgs.GodotClient.Preview;

/// <summary>
/// Lightweight approval renderer for the AnimeV1 face composition. It consumes
/// the same catalog, layout anchors and art crop as the real 3D card actor; it
/// deliberately never receives a composition for a hidden card.
/// </summary>
internal sealed partial class AnimeCardPreview : Control
{
    private static readonly Font CardDisplayFont = LoadCardDisplayFont();
    private static readonly Shader ArtworkMaskShader = new()
    {
        ResourceName = "AnimeV1 preview silhouette mask",
        Code = """
            shader_type canvas_item;
            render_mode unshaded;

            uniform sampler2D source_texture : source_color, filter_linear_mipmap_anisotropic;
            uniform sampler2D frame_mask : source_color, filter_linear_mipmap_anisotropic;
            uniform vec4 source_crop = vec4(0.0, 0.0, 1.0, 1.0);
            uniform vec4 frame_region = vec4(0.0, 0.0, 1.0, 1.0);

            void fragment() {
                vec4 tint = COLOR;
                vec2 source_uv = source_crop.xy + (UV * source_crop.zw);
                vec2 mask_uv = frame_region.xy + (UV * frame_region.zw);
                vec4 source = texture(source_texture, source_uv);
                float silhouette = texture(frame_mask, mask_uv).a;
                COLOR = vec4(
                    source.rgb * tint.rgb,
                    source.a * tint.a * smoothstep(0.04, 0.10, silhouette));
            }
            """,
    };
    private static readonly CardFaceRect FullFace = new(0.0f, 0.0f, 1.0f, 1.0f);

    private string _designId = string.Empty;
    private AnimeCardKind? _kind;
    private bool _hidden;
    private CardFaceComposition? _composition;
    private Texture2D? _art;
    private Texture2D? _material;
    private Texture2D? _foil;
    private Texture2D? _silhouette;
    private Texture2D? _crest;
    private Texture2D? _namePlate;
    private Texture2D? _rarity;
    private Texture2D? _variant;
    private Texture2D? _costGem;
    private Texture2D? _attackGem;
    private Texture2D? _healthGem;
    private Texture2D? _countdownGem;
    private TextureRect? _maskedArtwork;
    private TextureRect? _maskedMaterial;
    private TextureRect? _maskedFoil;
    private Control? _faceOverlay;

    internal string DesignId => _designId;
    internal bool ShowsIdentity =>
        _designId.Length != 0 || _kind.HasValue || _composition is not null;
    internal bool UsesExpectedRaster => _art is not null;
    internal bool UsesFoil => !_hidden && _foil is not null;
    internal bool IsHidden => _hidden;
    internal AnimeCardKind Kind => _kind ?? throw new InvalidOperationException(
        "Hidden cards do not expose a card-kind identity.");
    internal bool IsOwnHandCard => Name.ToString().StartsWith("NearHand", StringComparison.Ordinal);
    internal CardFaceComposition? Composition => _composition;
    internal CardFrameStyleKey? StyleKey => _composition?.FrameStyle.Key;
    internal string TypeMarkerGlyph => _kind is { } kind
        ? kind switch
        {
            AnimeCardKind.Follower => "随",
            AnimeCardKind.Spell => "法",
            AnimeCardKind.Amulet => "护",
            AnimeCardKind.Trap => "伏",
            AnimeCardKind.Field => "场",
            _ => "?",
        }
        : string.Empty;
    internal string TypeMarkerShape => _kind is { } kind
        ? kind switch
        {
            AnimeCardKind.Follower => "shield",
            AnimeCardKind.Spell => "star",
            AnimeCardKind.Amulet => "ring",
            AnimeCardKind.Trap => "inverted_triangle",
            AnimeCardKind.Field => "gate",
            _ => "unknown",
        }
        : string.Empty;
    internal int BadgeFontPixelSize => _composition is { } composition
        ? FitText(
            composition.ViewModel.Cost.ToString(CultureInfo.InvariantCulture),
            SocketRect(composition.Layout.CostText),
            MaximumBadgeFontSize,
            minimumFontSize: 7,
            outlineSize: BadgeOutlineSize).FontSize
        : 0;
    internal Rect2 VisualScreenRect => TransformLocalRect(new Rect2(Vector2.Zero, Size));
    internal Rect2 NamePlateScreenRect =>
        TransformLocalRect(SocketRect(_composition?.Layout.NamePlate));
    internal Rect2 NameTextScreenRect =>
        TransformLocalRect(SocketRect(_composition?.Layout.NameText));
    internal Rect2 CostSocketScreenRect =>
        TransformLocalRect(SocketRect(_composition?.Layout.CostGem));
    internal Rect2 CostBadgeScreenRect => TransformLocalRect(SocketRect(_composition?.Layout.CostText));
    internal Rect2? AttackSocketScreenRect =>
        _composition?.ViewModel.Attack.HasValue == true
            ? SocketScreenRect(_composition.Layout.AttackGem)
            : null;
    internal Rect2? AttackBadgeScreenRect =>
        _composition?.ViewModel.Attack.HasValue == true
            ? SocketScreenRect(_composition.Layout.AttackText)
            : null;
    internal Rect2? HealthBadgeScreenRect =>
        _composition?.ViewModel.Health.HasValue == true
            ? SocketScreenRect(_composition.Layout.HealthText)
            : null;
    internal Rect2? HealthSocketScreenRect =>
        _composition?.ViewModel.Health.HasValue == true
            ? SocketScreenRect(_composition.Layout.HealthGem)
            : null;
    internal Rect2? CountdownBadgeScreenRect =>
        _composition?.ViewModel.Countdown.HasValue == true
            ? SocketScreenRect(_composition.Layout.CountdownText)
            : null;
    internal Rect2? CountdownSocketScreenRect =>
        _composition?.ViewModel.Countdown.HasValue == true
            ? SocketScreenRect(_composition.Layout.CountdownGem)
            : null;
    internal Rect2 TypeMarkerScreenRect
    {
        get
        {
            Rect2 marker = TransformLocalRect(SocketRect(_composition?.Layout.TypeCrest));
            Rect2 card = VisualScreenRect;
            Vector2 size = new(
                MathF.Min(card.Size.X, MathF.Max(24.0f, marker.Size.X)),
                MathF.Min(card.Size.Y, MathF.Max(24.0f, marker.Size.Y)));
            Vector2 position = marker.GetCenter() - (size * 0.5f);
            position = new Vector2(
                Math.Clamp(position.X, card.Position.X, card.End.X - size.X),
                Math.Clamp(position.Y, card.Position.Y, card.End.Y - size.Y));
            return new Rect2(position, size);
        }
    }

    private float CardScale => MathF.Min(Size.X / 120.0f, Size.Y / 160.0f);
    private int MaximumBadgeFontSize => Math.Max(10, (int)MathF.Round(20.0f * CardScale));
    private int BadgeOutlineSize => Math.Max(1, (int)MathF.Round(CardScale));

    internal void Configure(
        string designId,
        string displayName,
        AnimeCardKind kind,
        AnimeFaction faction,
        int cost,
        int? attack = null,
        int? health = null,
        int? countdown = null,
        bool hidden = false,
        bool evolved = false)
    {
        _designId = hidden ? string.Empty : designId;
        _kind = hidden ? null : kind;
        _hidden = hidden;
        EnsureFaceLayers();
        ClearFaceResources();
        MouseFilter = MouseFilterEnum.Ignore;

        if (hidden)
        {
            // Privacy invariant: hidden cards never touch the product face catalog.
            _art = AnimeVisualAssetCatalog.TryLoad(AnimeVisualAssetCatalog.CardBack);
            RefreshFaceLayers();
            return;
        }

        ProductCardVisualEntry entry = ProductCardVisualCatalog.Shared.Resolve(designId);
        CardFrameVariant variant = evolved
            ? CardFrameVariant.Evolved
            : designId == "LO-T01" ? CardFrameVariant.Token : CardFrameVariant.Normal;
        string artPath = ProductCardVisualCatalog.Shared.ResolveArtPath(entry, variant);
        Texture2D? candidateArt = LoadTexture(artPath);
        var viewModel = new CardFaceViewModel
        {
            DesignId = designId,
            DisplayName = displayName,
            Kind = ToProductKind(kind),
            Faction = ToProductFaction(faction),
            Rarity = entry.Rarity,
            Cost = cost,
            Attack = attack,
            Health = health,
            Countdown = countdown,
            Variant = variant,
            ArtPixelWidth = Math.Max(1, candidateArt?.GetWidth() ?? 1024),
            ArtPixelHeight = Math.Max(1, candidateArt?.GetHeight() ?? 1536),
            ArtFocusX = entry.ArtFocusX,
            ArtFocusY = entry.ArtFocusY,
        };
        _composition = CardFaceComposer.Compose(
            viewModel,
            ResolveContext(),
            ProductCardVisualCatalog.Shared,
            CardFrameStyleCatalog.Shared);
        _art = candidateArt ?? LoadTexture(_composition.ArtPath);
        CardFrameStyle style = _composition.FrameStyle;
        _material = LoadTexture(style.MaterialTexturePath);
        _foil = LoadTexture(style.FoilTexturePath);
        _silhouette = LoadTexture(style.SilhouettePath);
        _crest = LoadTexture(style.CrestPath);
        _namePlate = LoadTexture(style.NamePlatePath);
        _rarity = LoadTexture(style.RarityOverlayPath);
        _variant = LoadTexture(style.VariantOverlayPath);
        _costGem = LoadTexture(style.CostGemPath);
        _attackGem = LoadTexture(style.AttackGemPath);
        _healthGem = LoadTexture(style.HealthGemPath);
        _countdownGem = LoadTexture(style.CountdownGemPath);
        RefreshFaceLayers();
    }

    public override void _Ready()
    {
        EnsureFaceLayers();
        RefreshFaceLayers();
        Resized += RefreshFaceLayers;
    }

    public override void _Draw()
    {
        Rect2 bounds = new(Vector2.Zero, Size);
        if (bounds.Size.X < 8.0f || bounds.Size.Y < 12.0f)
        {
            return;
        }

        if (_hidden)
        {
            DrawCardShadow(bounds);
            DrawHiddenFace(bounds);
            return;
        }
        if (_composition is null)
        {
            DrawRect(bounds.Grow(-2.0f), AnimeVisualTheme.DeepIndigo);
            return;
        }

        // Artwork, material and foil are child CanvasItems so each can use the
        // same silhouette-alpha mask as CardActor3D.  The parent intentionally
        // does not draw a rectangular face slab behind those layers.
    }

    private void DrawFaceOverlay(Control canvas)
    {
        if (_hidden || _composition is null)
        {
            return;
        }

        Rect2 bounds = new(Vector2.Zero, Size);
        DrawLayer(canvas, _silhouette, bounds);
        DrawLayer(canvas, _rarity, bounds);
        DrawLayer(canvas, _variant, bounds);
        DrawLayer(canvas, _crest, SocketRect(_composition.Layout.TypeCrest));
        DrawLayer(canvas, _namePlate, SocketRect(_composition.Layout.NamePlate));
        DrawName(canvas, _composition);
        DrawSocket(
            canvas,
            _costGem,
            _composition.Layout.CostGem,
            _composition.Layout.CostText,
            _composition.ViewModel.Cost.ToString());
        DrawOptionalSocket(
            canvas,
            _attackGem,
            _composition.Layout.AttackGem,
            _composition.Layout.AttackText,
            _composition.ViewModel.Attack);
        DrawOptionalSocket(
            canvas,
            _healthGem,
            _composition.Layout.HealthGem,
            _composition.Layout.HealthText,
            _composition.ViewModel.Health);
        DrawOptionalSocket(
            canvas,
            _countdownGem,
            _composition.Layout.CountdownGem,
            _composition.Layout.CountdownText,
            _composition.ViewModel.Countdown);
    }

    private void DrawHiddenFace(Rect2 bounds)
    {
        DrawRect(bounds.Grow(-1.0f), AnimeVisualTheme.DeepIndigo.Darkened(0.22f));
        if (_art is not null)
        {
            DrawTextureRect(_art, bounds.Grow(-3.0f), tile: false);
        }
        DrawArc(
            bounds.GetCenter(),
            MathF.Min(bounds.Size.X, bounds.Size.Y) * 0.46f,
            0.0f,
            MathF.Tau,
            64,
            new Color(AnimeVisualTheme.OldGold, 0.76f),
            MathF.Max(1.0f, CardScale),
            true);
    }

    private void DrawCardShadow(Rect2 bounds)
    {
        StyleBoxFlat shadow = AnimeVisualTheme.Panel(AnimeVisualTheme.Ink, 0.54f, (int)MathF.Max(5.0f, 8.0f * CardScale), 0);
        shadow.ShadowSize = (int)MathF.Max(3.0f, 8.0f * CardScale);
        DrawStyleBox(shadow, bounds.Grow(-1.0f));
    }

    private void DrawName(Control canvas, CardFaceComposition composition)
    {
        Rect2 rect = SocketRect(composition.Layout.NameText);
        int maximumFontSize = Math.Max(9, (int)MathF.Round(13.5f * CardScale));
        int minimumFontSize = Math.Max(8, (int)MathF.Floor(maximumFontSize * 0.55f));
        DrawFittedText(
            canvas,
            rect,
            composition.ViewModel.DisplayName,
            maximumFontSize,
            minimumFontSize,
            AnimeVisualTheme.MoonWhite,
            outlineSize: BadgeOutlineSize);
    }

    private void DrawSocket(
        Control canvas,
        Texture2D? texture,
        CardFaceRect socket,
        CardFaceRect textSocket,
        string value)
    {
        Rect2 rect = SocketRect(socket);
        DrawLayer(canvas, texture, rect);
        DrawFittedText(
            canvas,
            SocketRect(textSocket),
            value,
            MaximumBadgeFontSize,
            minimumFontSize: 7,
            Colors.White,
            outlineSize: BadgeOutlineSize);
    }

    private void DrawOptionalSocket(
        Control canvas,
        Texture2D? texture,
        CardFaceRect? socket,
        CardFaceRect? textSocket,
        int? value)
    {
        if (socket is { } rect && textSocket is { } textRect && value.HasValue)
        {
            DrawSocket(
                canvas,
                texture,
                rect,
                textRect,
                value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static void DrawFittedText(
        Control canvas,
        Rect2 rect,
        string text,
        int maximumFontSize,
        int minimumFontSize,
        Color color,
        int outlineSize)
    {
        FittedCardText fit = FitText(
            text,
            rect,
            maximumFontSize,
            minimumFontSize,
            outlineSize);
        float ascent = CardDisplayFont.GetAscent(fit.FontSize);
        float descent = CardDisplayFont.GetDescent(fit.FontSize);
        float baseline = rect.Position.Y +
                          ((rect.Size.Y - (ascent + descent)) * 0.5f) +
                          ascent;
        float startX = rect.GetCenter().X - (fit.MeasuredSize.X * 0.5f);
        if (outlineSize > 0)
        {
            canvas.DrawStringOutline(
                CardDisplayFont,
                new Vector2(startX, baseline),
                fit.Text,
                HorizontalAlignment.Left,
                -1.0f,
                fit.FontSize,
                outlineSize,
                new Color("352943"));
        }
        canvas.DrawString(
            CardDisplayFont,
            new Vector2(startX, baseline),
            fit.Text,
            HorizontalAlignment.Left,
            -1.0f,
            fit.FontSize,
            color);
    }

    private static FittedCardText FitText(
        string text,
        Rect2 rect,
        int maximumFontSize,
        int minimumFontSize,
        int outlineSize)
    {
        string source = string.IsNullOrWhiteSpace(text) ? "—" : text.Trim();
        int maximum = Math.Max(1, maximumFontSize);
        int minimum = Math.Clamp(minimumFontSize, 1, maximum);
        for (int size = maximum; size >= minimum; --size)
        {
            Vector2 measured = MeasureText(source, size);
            if (Fits(measured, rect, outlineSize))
            {
                return new FittedCardText(source, size, measured);
            }
        }

        // Product text is never truncated. Continue shrinking below the
        // preferred readability floor only as a future-content safety net;
        // the authored product names fit above their locked floor because the
        // name socket uses almost all of the opaque nameplate.
        for (int size = minimum - 1; size >= 1; --size)
        {
            Vector2 measured = MeasureText(source, size);
            if (Fits(measured, rect, outlineSize))
            {
                return new FittedCardText(source, size, measured);
            }
        }

        return new FittedCardText(source, 1, MeasureText(source, 1));
    }

    private static Vector2 MeasureText(string text, int fontSize) => new(
        CardDisplayFont.GetStringSize(
            text,
            HorizontalAlignment.Left,
            -1.0f,
            fontSize).X,
        CardDisplayFont.GetAscent(fontSize) + CardDisplayFont.GetDescent(fontSize));

    private static bool Fits(Vector2 measured, Rect2 rect, int outlineSize) =>
        measured.X + (outlineSize * 2.0f) <= rect.Size.X + 0.01f &&
        measured.Y + (outlineSize * 2.0f) <= rect.Size.Y + 0.01f;

    private readonly record struct FittedCardText(string Text, int FontSize, Vector2 MeasuredSize);

    private static void DrawLayer(
        Control canvas,
        Texture2D? texture,
        Rect2 rect,
        Color? modulate = null)
    {
        if (texture is not null)
        {
            canvas.DrawTextureRect(texture, rect, tile: false, modulate ?? Colors.White);
        }
    }

    private void EnsureFaceLayers()
    {
        if (_maskedArtwork is not null)
        {
            return;
        }

        _maskedArtwork = CreateMaskedLayer("MaskedArtwork", zIndex: 0);
        _maskedMaterial = CreateMaskedLayer("MaskedMaterial", zIndex: 1);
        _maskedFoil = CreateMaskedLayer("MaskedFoil", zIndex: 2);
        var overlay = new Control
        {
            Name = "FaceOverlay",
            MouseFilter = MouseFilterEnum.Ignore,
            ZIndex = 3,
        };
        overlay.Draw += () => DrawFaceOverlay(overlay);
        _faceOverlay = overlay;
        AddChild(_maskedArtwork);
        AddChild(_maskedMaterial);
        AddChild(_maskedFoil);
        AddChild(_faceOverlay);
    }

    private static TextureRect CreateMaskedLayer(string name, int zIndex) => new()
    {
        Name = name,
        MouseFilter = MouseFilterEnum.Ignore,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.Scale,
        ZIndex = zIndex,
        Visible = false,
    };

    private void RefreshFaceLayers()
    {
        EnsureFaceLayers();
        if (_maskedArtwork is null || _maskedMaterial is null ||
            _maskedFoil is null || _faceOverlay is null)
        {
            return;
        }

        _faceOverlay.Position = Vector2.Zero;
        _faceOverlay.Size = Size;
        _faceOverlay.Visible = !_hidden && _composition is not null;
        _faceOverlay.QueueRedraw();

        if (_hidden || _composition is null || _silhouette is null)
        {
            ResetMaskedLayer(_maskedArtwork);
            ResetMaskedLayer(_maskedMaterial);
            ResetMaskedLayer(_maskedFoil);
            QueueRedraw();
            return;
        }

        CardFaceLayout layout = _composition.Layout;
        ConfigureMaskedLayer(
            _maskedArtwork,
            _art,
            _silhouette,
            SocketRect(layout.ArtWindow),
            _composition.ArtCrop,
            layout.ArtWindow,
            1.0f);
        ConfigureMaskedLayer(
            _maskedMaterial,
            _material,
            _silhouette,
            new Rect2(Vector2.Zero, Size),
            new CardArtCrop(0.0f, 0.0f, 1.0f, 1.0f),
            FullFace,
            0.10f);
        ConfigureMaskedLayer(
            _maskedFoil,
            _foil,
            _silhouette,
            new Rect2(Vector2.Zero, Size),
            new CardArtCrop(0.0f, 0.0f, 1.0f, 1.0f),
            FullFace,
            0.14f);
        QueueRedraw();
    }

    private static void ConfigureMaskedLayer(
        TextureRect layer,
        Texture2D? source,
        Texture2D mask,
        Rect2 destination,
        CardArtCrop sourceCrop,
        CardFaceRect frameRegion,
        float opacity)
    {
        if (source is null)
        {
            ResetMaskedLayer(layer);
            return;
        }

        var material = new ShaderMaterial
        {
            ResourceName = $"AnimeV1 masked preview:{source.ResourcePath}",
            Shader = ArtworkMaskShader,
        };
        material.SetShaderParameter("source_texture", source);
        material.SetShaderParameter("frame_mask", mask);
        material.SetShaderParameter(
            "source_crop",
            new Vector4(sourceCrop.U, sourceCrop.V, sourceCrop.Width, sourceCrop.Height));
        material.SetShaderParameter(
            "frame_region",
            new Vector4(frameRegion.X, frameRegion.Y, frameRegion.Width, frameRegion.Height));
        layer.Position = destination.Position;
        layer.Size = destination.Size;
        layer.Texture = source;
        layer.Material = material;
        layer.SelfModulate = new Color(1.0f, 1.0f, 1.0f, opacity);
        layer.Visible = true;
    }

    private static void ResetMaskedLayer(TextureRect layer)
    {
        layer.Visible = false;
        layer.Texture = null;
        layer.Material = null;
        layer.SelfModulate = Colors.White;
    }

    private Rect2 SocketRect(CardFaceRect? normalized)
    {
        if (normalized is not { } rect)
        {
            return new Rect2();
        }
        return new Rect2(
            rect.X * Size.X,
            rect.Y * Size.Y,
            rect.Width * Size.X,
            rect.Height * Size.Y);
    }

    private Rect2? SocketScreenRect(CardFaceRect? normalized) =>
        normalized is { } rect ? TransformLocalRect(SocketRect(rect)) : null;

    private CardFaceContext ResolveContext()
    {
        // Existing preview callers name nodes after Configure().  Size is the
        // stable semantic signal here: detail cards are large, hand cards are
        // at least the locked 142 px height, and board cards remain compact.
        if (Size.X >= 160.0f)
        {
            return CardFaceContext.Detail;
        }
        if (Size.Y >= 136.0f && Size.X >= 84.0f)
        {
            return CardFaceContext.Hand;
        }
        return CardFaceContext.Field;
    }

    private static Texture2D? LoadTexture(string? path) =>
        !string.IsNullOrWhiteSpace(path) && ResourceLoader.Exists(path, "Texture2D")
            ? GD.Load<Texture2D>(path)
            : null;

    private static Font LoadCardDisplayFont()
    {
        const string path = "res://assets/fonts/NotoSerifCJKsc-SemiBold.otf";
        return ResourceLoader.Exists(path, "Font")
            ? GD.Load<Font>(path)
            : AnimeVisualTheme.DisplayFont;
    }

    private void ClearFaceResources()
    {
        _composition = null;
        _art = null;
        _material = null;
        _foil = null;
        _silhouette = null;
        _crest = null;
        _namePlate = null;
        _rarity = null;
        _variant = null;
        _costGem = null;
        _attackGem = null;
        _healthGem = null;
        _countdownGem = null;
    }

    private static ProductCardKind ToProductKind(AnimeCardKind kind) => kind switch
    {
        AnimeCardKind.Follower => ProductCardKind.Follower,
        AnimeCardKind.Spell => ProductCardKind.Spell,
        AnimeCardKind.Amulet => ProductCardKind.Amulet,
        AnimeCardKind.Trap => ProductCardKind.Trap,
        AnimeCardKind.Field => ProductCardKind.Field,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static ProductCardFaction ToProductFaction(AnimeFaction faction) => faction switch
    {
        AnimeFaction.Oathguard => ProductCardFaction.Oathguard,
        AnimeFaction.Pactmage => ProductCardFaction.Pactmage,
        AnimeFaction.Neutral => ProductCardFaction.Neutral,
        _ => throw new ArgumentOutOfRangeException(nameof(faction), faction, null),
    };

    private Rect2 TransformLocalRect(Rect2 localRect)
    {
        Transform2D transform = GetGlobalTransformWithCanvas();
        Vector2 topLeft = transform * localRect.Position;
        Vector2 topRight = transform * new Vector2(localRect.End.X, localRect.Position.Y);
        Vector2 bottomRight = transform * localRect.End;
        Vector2 bottomLeft = transform * new Vector2(localRect.Position.X, localRect.End.Y);
        float minX = MathF.Min(MathF.Min(topLeft.X, topRight.X), MathF.Min(bottomRight.X, bottomLeft.X));
        float minY = MathF.Min(MathF.Min(topLeft.Y, topRight.Y), MathF.Min(bottomRight.Y, bottomLeft.Y));
        float maxX = MathF.Max(MathF.Max(topLeft.X, topRight.X), MathF.Max(bottomRight.X, bottomLeft.X));
        float maxY = MathF.Max(MathF.Max(topLeft.Y, topRight.Y), MathF.Max(bottomRight.Y, bottomLeft.Y));
        return new Rect2(minX, minY, maxX - minX, maxY - minY);
    }
}

internal sealed partial class AnimeRuneSlot : Control
{
    private AnimeFaction _faction;
    private AnimeCardKind _kind;
    private bool _active;

    internal void Configure(AnimeFaction faction, AnimeCardKind kind, bool active = false)
    {
        _faction = faction;
        _kind = kind;
        _active = active;
        MouseFilter = MouseFilterEnum.Ignore;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 center = Size * 0.5f;
        Color accent = AnimeVisualTheme.FactionColor(_faction);
        float radius = MathF.Min(Size.X, Size.Y) * 0.42f;
        Color fill = new(accent.Darkened(0.45f), _active ? 0.24f : 0.10f);
        DrawCircle(center, radius, fill);
        DrawArc(center, radius, 0.0f, MathF.Tau, 56, new Color(accent, _active ? 0.88f : 0.30f), _active ? 2.3f : 1.2f, true);
        DrawArc(center, radius * 0.72f, 0.0f, MathF.Tau, 48, new Color(AnimeVisualTheme.OldGold, _active ? 0.68f : 0.20f), 1.0f, true);
        Vector2[] diamond =
        [
            center + new Vector2(0.0f, -radius * 0.45f),
            center + new Vector2(radius * 0.34f, 0.0f),
            center + new Vector2(0.0f, radius * 0.45f),
            center + new Vector2(-radius * 0.34f, 0.0f),
        ];
        DrawPolyline(diamond.Append(diamond[0]).ToArray(), new Color(AnimeVisualTheme.MoonWhite, _active ? 0.84f : 0.28f), 1.4f, true);
        if (_active)
        {
            string glyph = _kind switch
            {
                AnimeCardKind.Field => "场",
                AnimeCardKind.Trap => "策",
                _ => "主",
            };
            DrawString(AnimeVisualTheme.DisplayFont, center + new Vector2(-14.0f, 6.0f), glyph, HorizontalAlignment.Center, 28.0f, 15, AnimeVisualTheme.MoonWhite);
        }
    }
}
