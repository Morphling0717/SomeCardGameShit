// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

namespace Scgs.GodotClient.Visuals;

public enum CardVisualFaction
{
    Neutral = 0,
    Midrange = 1,
    Advance = 2,
}

public sealed record CardVisualEntry(
    uint DefinitionId,
    CardVisualFaction Faction,
    string ArtworkPath);

public interface ICardVisualCatalog
{
    IReadOnlyCollection<CardVisualEntry> Entries { get; }

    CardVisualEntry? Find(uint definitionId);

    Texture2D LoadArtwork(uint definitionId);

    Texture2D CardBack { get; }

    Texture2D FallbackFront { get; }
}

/// <summary>
/// Strict, presentation-only mapping for the frozen alpha card catalog. The
/// engine remains the only source of rules and identity; this catalog only
/// selects an artwork after a viewer-safe DTO has disclosed a definition id.
/// </summary>
public sealed class CardVisualCatalog : ICardVisualCatalog
{
    private const string ArtworkRoot = "res://assets/visual/cards/art";
    private const string CardFallbackRoot = "res://assets/visual/cards/shared";

    private static readonly uint[] MidrangeIds =
    [
        1001, 1002, 1003, 1004, 1005, 1006, 1007, 1009,
        1010, 1011, 1012, 1013, 1014, 3001, 3002,
    ];

    private static readonly uint[] AdvanceIds =
    [
        2001, 2002, 2003, 2004, 2005, 2006, 2007,
        2008, 2009, 2010, 2011, 2012, 3011, 3012,
    ];

    private readonly Dictionary<uint, CardVisualEntry> _entries;
    private readonly Dictionary<uint, Texture2D> _artworkCache = [];
    private readonly Texture2D _fallbackFront;
    private readonly Texture2D _cardBack;

    public static CardVisualCatalog Shared { get; } = new();

    public CardVisualCatalog()
    {
        _entries = MidrangeIds
            .Select(id => new CardVisualEntry(
                id,
                CardVisualFaction.Midrange,
                $"{ArtworkRoot}/{id}.png"))
            .Concat(AdvanceIds.Select(id => new CardVisualEntry(
                id,
                CardVisualFaction.Advance,
                $"{ArtworkRoot}/{id}.png")))
            .ToDictionary(entry => entry.DefinitionId);

        _fallbackFront = LoadTextureOrGeneratedFallback(
            $"{CardFallbackRoot}/fallback_front.svg",
            new Color("18374a"),
            new Color("54d8cf"));
        _cardBack = LoadTextureOrGeneratedFallback(
            "res://assets/visual/shared/card_back.png",
            new Color("081526"),
            new Color("41d4d0"));
    }

    public IReadOnlyCollection<CardVisualEntry> Entries => _entries.Values;

    public Texture2D CardBack => _cardBack;

    public Texture2D FallbackFront => _fallbackFront;

    public CardVisualEntry? Find(uint definitionId) =>
        _entries.GetValueOrDefault(definitionId);

    public Texture2D LoadArtwork(uint definitionId)
    {
        if (_artworkCache.TryGetValue(definitionId, out Texture2D? cached))
        {
            return cached;
        }

        CardVisualEntry? entry = Find(definitionId);
        Texture2D texture = entry is not null && ResourceLoader.Exists(entry.ArtworkPath)
            ? GD.Load<Texture2D>(entry.ArtworkPath)
            : _fallbackFront;
        _artworkCache[definitionId] = texture;
        return texture;
    }

    public static CardVisualFaction FactionFor(uint? definitionId)
    {
        if (!definitionId.HasValue)
        {
            return CardVisualFaction.Neutral;
        }

        uint value = definitionId.Value;
        if (MidrangeIds.Contains(value))
        {
            return CardVisualFaction.Midrange;
        }

        return AdvanceIds.Contains(value)
            ? CardVisualFaction.Advance
            : CardVisualFaction.Neutral;
    }

    private static Texture2D LoadTextureOrGeneratedFallback(
        string path,
        Color background,
        Color accent)
    {
        if (ResourceLoader.Exists(path))
        {
            return GD.Load<Texture2D>(path);
        }

        var image = Image.CreateEmpty(32, 48, false, Image.Format.Rgba8);
        image.Fill(background);
        for (int y = 4; y < 44; ++y)
        {
            for (int x = 3; x < 29; ++x)
            {
                bool border = x is 3 or 28 || y is 4 or 43;
                bool circuit = (x + y) % 13 == 0 && x is > 7 and < 25;
                if (border || circuit)
                {
                    image.SetPixel(x, y, accent);
                }
            }
        }

        return ImageTexture.CreateFromImage(image);
    }
}
