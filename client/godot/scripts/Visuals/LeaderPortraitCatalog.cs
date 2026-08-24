// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

namespace Scgs.GodotClient.Visuals;

public sealed record LeaderPortraitEntry(
    string DeckId,
    CardVisualFaction Faction,
    string PortraitPath);

public interface ILeaderPortraitCatalog
{
    IReadOnlyCollection<LeaderPortraitEntry> Entries { get; }

    LeaderPortraitEntry Find(string deckId);

    Texture2D LoadPortrait(string deckId);
}

/// <summary>
/// Presentation-only mapping from the public deck choice made in the local
/// match setup to an original temporary leader portrait. It never consumes a
/// player snapshot or hidden card identity.
/// </summary>
public sealed class LeaderPortraitCatalog : ILeaderPortraitCatalog
{
    public const string MidrangeDeckId = "midrange";
    public const string AdvanceDeckId = "advance";

    private static readonly LeaderPortraitEntry NeutralFallback = new(
        "unknown",
        CardVisualFaction.Neutral,
        string.Empty);

    private readonly Dictionary<string, LeaderPortraitEntry> _entries =
        new(StringComparer.Ordinal)
        {
            [MidrangeDeckId] = new(
                MidrangeDeckId,
                CardVisualFaction.Midrange,
                "res://assets/visual/portraits/midrange_commander.png"),
            [AdvanceDeckId] = new(
                AdvanceDeckId,
                CardVisualFaction.Advance,
                "res://assets/visual/portraits/advance_technarch.png"),
        };

    private readonly Dictionary<string, Texture2D> _textureCache =
        new(StringComparer.Ordinal);

    private Texture2D? _fallbackTexture;

    public static LeaderPortraitCatalog Shared { get; } = new();

    public IReadOnlyCollection<LeaderPortraitEntry> Entries => _entries.Values;

    public LeaderPortraitEntry Find(string deckId)
    {
        ArgumentException.ThrowIfNullOrEmpty(deckId);
        return _entries.GetValueOrDefault(deckId, NeutralFallback);
    }

    public Texture2D LoadPortrait(string deckId)
    {
        LeaderPortraitEntry entry = Find(deckId);
        if (entry == NeutralFallback)
        {
            return _fallbackTexture ??= CreateFallbackTexture();
        }

        if (_textureCache.TryGetValue(entry.DeckId, out Texture2D? cached))
        {
            return cached;
        }

        Texture2D texture = ResourceLoader.Exists(entry.PortraitPath)
            ? GD.Load<Texture2D>(entry.PortraitPath)
            : _fallbackTexture ??= CreateFallbackTexture();
        _textureCache[entry.DeckId] = texture;
        return texture;
    }

    private static Texture2D CreateFallbackTexture()
    {
        const int size = 128;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        Color background = new("172634");
        Color accent = new("5a8194");
        image.Fill(background);

        Vector2 center = new(size / 2.0f, size / 2.0f);
        for (int y = 0; y < size; ++y)
        {
            for (int x = 0; x < size; ++x)
            {
                float radius = new Vector2(x, y).DistanceTo(center);
                bool ring = radius is > 43.0f and < 48.0f;
                bool core = radius < 17.0f;
                bool circuit = (x + y) % 29 == 0 && radius < 55.0f;
                if (ring || core || circuit)
                {
                    image.SetPixel(x, y, accent);
                }
            }
        }

        return ImageTexture.CreateFromImage(image);
    }
}

public sealed record MatchVisualIdentity(
    LeaderPortraitEntry Player0,
    LeaderPortraitEntry Player1)
{
    public static MatchVisualIdentity FromDecks(
        string player0Deck,
        string player1Deck,
        ILeaderPortraitCatalog? catalog = null)
    {
        catalog ??= LeaderPortraitCatalog.Shared;
        return new MatchVisualIdentity(
            catalog.Find(player0Deck),
            catalog.Find(player1Deck));
    }

    public LeaderPortraitEntry ForPlayer(Scgs.Client.PlayerId player) => player switch
    {
        Scgs.Client.PlayerId.Player0 => Player0,
        Scgs.Client.PlayerId.Player1 => Player1,
        _ => throw new ArgumentOutOfRangeException(nameof(player), player, "Unknown player."),
    };
}
