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
/// Identity-free compatibility skin for synthetic v04 fixtures. Product card
/// identity is selected only by ProductCardVisualCatalog and safe schema-2 DTOs.
/// </summary>
public sealed class CardVisualCatalog : ICardVisualCatalog
{
    private readonly Texture2D _fallbackFront;
    private readonly Texture2D _cardBack;

    public static CardVisualCatalog Shared { get; } = new();

    public CardVisualCatalog()
    {
        _fallbackFront = LoadTextureOrGeneratedFallback(
            "res://assets/visual/anime_v1/shared/fallback_front.svg",
            new Color("211832"),
            new Color("d4c3a1"));
        _cardBack = LoadTextureOrGeneratedFallback(
            "res://assets/visual/anime_v1/slice/shared/card-back.png",
            new Color("211832"),
            new Color("d4c3a1"));
    }

    public IReadOnlyCollection<CardVisualEntry> Entries => Array.Empty<CardVisualEntry>();

    public Texture2D CardBack => _cardBack;

    public Texture2D FallbackFront => _fallbackFront;

    public CardVisualEntry? Find(uint definitionId) => null;

    public Texture2D LoadArtwork(uint definitionId) => _fallbackFront;

    public static CardVisualFaction FactionFor(uint? definitionId) => CardVisualFaction.Neutral;

    private static Texture2D LoadTextureOrGeneratedFallback(
        string path,
        Color background,
        Color accent)
    {
        if (ResourceLoader.Exists(path))
        {
            return GD.Load<Texture2D>(path);
        }

        using var image = Image.CreateEmpty(32, 48, false, Image.Format.Rgba8);
        image.Fill(background);
        for (int y = 4; y < 44; ++y)
        {
            for (int x = 3; x < 29; ++x)
            {
                bool border = x is 3 or 28 || y is 4 or 43;
                bool rune = Math.Abs(x - 16) + Math.Abs(y - 24) == 9;
                if (border || rune)
                {
                    image.SetPixel(x, y, accent);
                }
            }
        }

        return ImageTexture.CreateFromImage(image);
    }
}
