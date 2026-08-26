// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace Scgs.GodotClient.CardFaces;

internal enum ProductCardFaction
{
    Neutral = 0,
    Oathguard = 1,
    Pactmage = 2,
}

/// <summary>
/// Visual card-kind identity.  This intentionally lives in the presentation
/// boundary instead of borrowing a native/schema DTO enum, so the standalone
/// AnimeV1 approval slice remains independent from Scgs.Client and native.
/// Product-session adapters must map their protocol kind explicitly.
/// </summary>
internal enum ProductCardKind
{
    Follower = 0,
    Spell = 1,
    Amulet = 2,
    Trap = 3,
    Field = 4,
}

internal enum CardVisualRarity
{
    Common = 0,
    Rare = 1,
    Epic = 2,
    Legendary = 3,
}

internal enum CardFaceContext
{
    Hand = 0,
    Field = 1,
    Detail = 2,
}

internal enum CardFrameVariant
{
    Normal = 0,
    Evolved = 1,
    Token = 2,
}

/// <summary>A rectangle normalized against the complete 3:4 card face.</summary>
internal readonly record struct CardFaceRect(float X, float Y, float Width, float Height)
{
    internal float Right => X + Width;
    internal float Bottom => Y + Height;

    internal bool IsInsideUnitSquare =>
        X >= 0.0f && Y >= 0.0f && Width > 0.0f && Height > 0.0f &&
        Right <= 1.0f && Bottom <= 1.0f;
}

/// <summary>Normalized source UV rectangle used to cover an art window without stretching.</summary>
internal readonly record struct CardArtCrop(float U, float V, float Width, float Height)
{
    internal static CardArtCrop Cover(
        int sourceWidth,
        int sourceHeight,
        float destinationAspectRatio,
        float focusX = 0.5f,
        float focusY = 0.5f)
    {
        if (sourceWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        }
        if (sourceHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceHeight));
        }
        if (!float.IsFinite(destinationAspectRatio) || destinationAspectRatio <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationAspectRatio));
        }
        if (!float.IsFinite(focusX) || focusX < 0.0f || focusX > 1.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(focusX));
        }
        if (!float.IsFinite(focusY) || focusY < 0.0f || focusY > 1.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(focusY));
        }

        float sourceAspect = sourceWidth / (float)sourceHeight;
        if (MathF.Abs(sourceAspect - destinationAspectRatio) <= 0.00001f)
        {
            return new CardArtCrop(0.0f, 0.0f, 1.0f, 1.0f);
        }

        if (sourceAspect > destinationAspectRatio)
        {
            float width = destinationAspectRatio / sourceAspect;
            float u = Math.Clamp(focusX - (width * 0.5f), 0.0f, 1.0f - width);
            return new CardArtCrop(u, 0.0f, width, 1.0f);
        }

        float height = sourceAspect / destinationAspectRatio;
        float v = Math.Clamp(focusY - (height * 0.5f), 0.0f, 1.0f - height);
        return new CardArtCrop(0.0f, v, 1.0f, height);
    }
}

internal sealed record CardFaceViewModel
{
    internal required string DesignId { get; init; }
    internal required string DisplayName { get; init; }
    internal required ProductCardKind Kind { get; init; }
    internal required ProductCardFaction Faction { get; init; }
    internal required CardVisualRarity Rarity { get; init; }
    internal required int Cost { get; init; }
    internal int? Attack { get; init; }
    internal int? Health { get; init; }
    internal int? Countdown { get; init; }
    internal CardFrameVariant Variant { get; init; } = CardFrameVariant.Normal;
    internal int ArtPixelWidth { get; init; } = 1024;
    internal int ArtPixelHeight { get; init; } = 1536;
    internal float ArtFocusX { get; init; } = 0.5f;
    internal float ArtFocusY { get; init; } = 0.5f;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(DesignId))
        {
            throw new ArgumentException("A card face requires a design ID.", nameof(DesignId));
        }
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            throw new ArgumentException("A card face requires a display name.", nameof(DisplayName));
        }
        if (Cost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Cost));
        }
        if ((Attack.HasValue || Health.HasValue) && Kind != ProductCardKind.Follower)
        {
            throw new ArgumentException("Only followers expose attack and health on the card face.");
        }
        if (Attack.HasValue != Health.HasValue)
        {
            throw new ArgumentException("Follower attack and health must be supplied together.");
        }
        if (Countdown.HasValue && Kind is not (ProductCardKind.Amulet or ProductCardKind.Trap))
        {
            throw new ArgumentException("Only amulets and traps expose countdown on the card face.");
        }
        if (Variant == CardFrameVariant.Token && DesignId != "LO-T01")
        {
            throw new ArgumentException("The locked AnimeV1 token frame is reserved for LO-T01.");
        }
    }
}

internal readonly record struct CardFrameStyleKey(
    ProductCardFaction Faction,
    ProductCardKind Kind,
    CardVisualRarity Rarity,
    CardFrameVariant Variant);

internal sealed record CardFaceLayout(
    CardFaceContext Context,
    CardFaceRect ArtWindow,
    CardFaceRect NamePlate,
    CardFaceRect NameText,
    CardFaceRect CostGem,
    CardFaceRect CostText,
    CardFaceRect TypeCrest,
    CardFaceRect? AttackGem,
    CardFaceRect? AttackText,
    CardFaceRect? HealthGem,
    CardFaceRect? HealthText,
    CardFaceRect? CountdownGem,
    CardFaceRect? CountdownText)
{
    internal const float CardAspectRatio = 3.0f / 4.0f;
    internal const float MinimumNameDecorationInset = 0.060f;

    internal float ArtWindowAspectRatio =>
        (ArtWindow.Width * CardAspectRatio) / ArtWindow.Height;

    internal static CardFaceLayout For(CardFaceContext context, ProductCardKind kind)
    {
        CardFaceRect? attack = kind == ProductCardKind.Follower
            ? new CardFaceRect(0.012f, 0.830f, 0.274f, 0.157f)
            : null;
        CardFaceRect? attackText = kind == ProductCardKind.Follower
            ? new CardFaceRect(0.047f, 0.845f, 0.204f, 0.125f)
            : null;
        CardFaceRect? health = kind == ProductCardKind.Follower
            ? new CardFaceRect(0.714f, 0.830f, 0.274f, 0.157f)
            : null;
        CardFaceRect? healthText = kind == ProductCardKind.Follower
            ? new CardFaceRect(0.749f, 0.845f, 0.204f, 0.125f)
            : null;
        CardFaceRect? countdown = kind is ProductCardKind.Amulet or ProductCardKind.Trap
            ? new CardFaceRect(0.680f, 0.824f, 0.308f, 0.163f)
            : null;
        CardFaceRect? countdownText = kind is ProductCardKind.Amulet or ProductCardKind.Trap
            ? new CardFaceRect(0.725f, 0.850f, 0.218f, 0.111f)
            : null;

        (float nameY, float nameHeight) = context switch
        {
            CardFaceContext.Field => (0.680f, 0.140f),
            CardFaceContext.Detail => (0.670f, 0.150f),
            _ => (0.675f, 0.145f),
        };

        return new CardFaceLayout(
            context,
            new CardFaceRect(0.0f, 0.0f, 1.0f, 1.0f),
            new CardFaceRect(0.105f, nameY, 0.790f, nameHeight),
            // The SVG diamonds and faction flourishes occupy roughly the
            // outer 12% of the authored plate. Keep the measured full name in
            // the undecorated center bay; font fitting may reduce its size but
            // must never borrow ornament space or truncate the source name.
            new CardFaceRect(0.200f, nameY + 0.012f, 0.600f, nameHeight - 0.024f),
            new CardFaceRect(0.006f, 0.006f, 0.250f, 0.190f),
            new CardFaceRect(0.0385f, 0.036f, 0.185f, 0.116f),
            new CardFaceRect(0.780f, 0.020f, 0.170f, 0.128f),
            attack,
            attackText,
            health,
            healthText,
            countdown,
            countdownText);
    }

    internal void Validate()
    {
        IEnumerable<CardFaceRect> rectangles = new[]
            {
                ArtWindow,
                NamePlate,
                NameText,
                CostGem,
                CostText,
                TypeCrest,
            }
            .Concat(new[]
                {
                    AttackGem, AttackText, HealthGem, HealthText,
                    CountdownGem, CountdownText,
                }
                .Where(rect => rect.HasValue)
                .Select(rect => rect!.Value));
        if (rectangles.Any(rect => !rect.IsInsideUnitSquare))
        {
            throw new InvalidOperationException("Card-face anchors must remain inside the normalized card rectangle.");
        }
        if (new[] { AttackGem, HealthGem, CountdownGem }
            .Where(rect => rect.HasValue)
            .Select(rect => rect!.Value)
            .Any(rect => NamePlate.Bottom > rect.Y))
        {
            throw new InvalidOperationException(
                "The one-line name socket must not overlap an attack, health or countdown socket.");
        }
        if (NameText.X - NamePlate.X < MinimumNameDecorationInset ||
            NamePlate.Right - NameText.Right < MinimumNameDecorationInset)
        {
            throw new InvalidOperationException(
                "The name text must remain inside the nameplate's undecorated center bay.");
        }
        ValidateTextSocket(NamePlate, NameText, "name");
        ValidateTextSocket(CostGem, CostText, "cost");
        ValidateOptionalTextSocket(AttackGem, AttackText, "attack");
        ValidateOptionalTextSocket(HealthGem, HealthText, "health");
        ValidateOptionalTextSocket(CountdownGem, CountdownText, "countdown");
    }

    private static void ValidateOptionalTextSocket(
        CardFaceRect? gem,
        CardFaceRect? text,
        string label)
    {
        if (gem.HasValue != text.HasValue)
        {
            throw new InvalidOperationException($"The {label} gem and text rectangle must appear together.");
        }
        if (gem is { } presentGem && text is { } presentText)
        {
            ValidateTextSocket(presentGem, presentText, label);
        }
    }

    private static void ValidateTextSocket(
        CardFaceRect gem,
        CardFaceRect text,
        string label)
    {
        if (text.X < gem.X || text.Y < gem.Y ||
            text.Right > gem.Right || text.Bottom > gem.Bottom)
        {
            throw new InvalidOperationException(
                $"The {label} text rectangle must remain inside its decorative gem.");
        }
    }
}

internal sealed record ProductCardVisualEntry(
    string DesignId,
    ProductCardFaction Faction,
    ProductCardKind Kind,
    CardVisualRarity Rarity,
    string BaseArtPath,
    string? EvolvedArtPath = null,
    float ArtFocusX = 0.5f,
    float ArtFocusY = 0.5f);

internal sealed record CardFrameStyle(
    CardFrameStyleKey Key,
    string SilhouettePath,
    string CrestPath,
    string NamePlatePath,
    string? RarityOverlayPath,
    string? VariantOverlayPath,
    string MaterialTexturePath,
    string? FoilTexturePath,
    string CostGemPath,
    string AttackGemPath,
    string HealthGemPath,
    string CountdownGemPath);

internal sealed record CardFaceComposition(
    CardFaceViewModel ViewModel,
    ProductCardVisualEntry Visual,
    CardFrameStyle FrameStyle,
    CardFaceLayout Layout,
    string ArtPath,
    CardArtCrop ArtCrop);

internal interface IProductCardVisualCatalog
{
    IReadOnlyCollection<ProductCardVisualEntry> Entries { get; }
    ProductCardVisualEntry? Find(string designId);
    ProductCardVisualEntry Resolve(string designId);
    string ResolveArtPath(ProductCardVisualEntry entry, CardFrameVariant variant);
}

internal interface ICardFrameStyleCatalog
{
    IReadOnlyCollection<CardFrameStyle> Styles { get; }
    CardFrameStyle Resolve(CardFrameStyleKey key);
}

internal static class CardFaceComposer
{
    internal static CardFaceComposition Compose(
        CardFaceViewModel viewModel,
        CardFaceContext context,
        IProductCardVisualCatalog visualCatalog,
        ICardFrameStyleCatalog frameCatalog)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(visualCatalog);
        ArgumentNullException.ThrowIfNull(frameCatalog);
        viewModel.Validate();

        ProductCardVisualEntry visual = visualCatalog.Resolve(viewModel.DesignId);
        ProductCardVisualEntry? knownVisual = visualCatalog.Find(viewModel.DesignId);
        if (knownVisual is not null &&
            (knownVisual.Faction != viewModel.Faction ||
             knownVisual.Kind != viewModel.Kind ||
             knownVisual.Rarity != viewModel.Rarity))
        {
            throw new ArgumentException(
                $"Known card {viewModel.DesignId} must use its catalog faction, kind and rarity.",
                nameof(viewModel));
        }
        return ComposeResolved(viewModel, context, visual, visualCatalog, frameCatalog);
    }

    /// <summary>
    /// Explicitly composes a virtual style sample.  Product identities must use
    /// <see cref="Compose"/> so a real design ID can never be paired with the
    /// wrong faction, kind or rarity merely to populate a contact sheet.
    /// </summary>
    internal static CardFaceComposition ComposeStylePreview(
        CardFaceViewModel viewModel,
        CardFaceContext context,
        ProductCardVisualEntry previewVisual,
        IProductCardVisualCatalog visualCatalog,
        ICardFrameStyleCatalog frameCatalog)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(previewVisual);
        ArgumentNullException.ThrowIfNull(visualCatalog);
        ArgumentNullException.ThrowIfNull(frameCatalog);
        viewModel.Validate();
        if (!viewModel.DesignId.StartsWith("STYLE-PREVIEW:", StringComparison.Ordinal) ||
            previewVisual.DesignId != viewModel.DesignId ||
            previewVisual.Faction != viewModel.Faction ||
            previewVisual.Kind != viewModel.Kind ||
            previewVisual.Rarity != viewModel.Rarity)
        {
            throw new ArgumentException(
                "A style preview requires a matching virtual STYLE-PREVIEW identity.",
                nameof(previewVisual));
        }
        return ComposeResolved(viewModel, context, previewVisual, visualCatalog, frameCatalog);
    }

    private static CardFaceComposition ComposeResolved(
        CardFaceViewModel viewModel,
        CardFaceContext context,
        ProductCardVisualEntry visual,
        IProductCardVisualCatalog visualCatalog,
        ICardFrameStyleCatalog frameCatalog)
    {
        var styleKey = new CardFrameStyleKey(
            viewModel.Faction,
            viewModel.Kind,
            viewModel.Rarity,
            viewModel.Variant);
        CardFrameStyle style = frameCatalog.Resolve(styleKey);
        CardFaceLayout layout = CardFaceLayout.For(context, viewModel.Kind);
        layout.Validate();
        CardArtCrop crop = CardArtCrop.Cover(
            viewModel.ArtPixelWidth,
            viewModel.ArtPixelHeight,
            layout.ArtWindowAspectRatio,
            viewModel.ArtFocusX,
            viewModel.ArtFocusY);
        return new CardFaceComposition(
            viewModel,
            visual,
            style,
            layout,
            visualCatalog.ResolveArtPath(visual, viewModel.Variant),
            crop);
    }
}
