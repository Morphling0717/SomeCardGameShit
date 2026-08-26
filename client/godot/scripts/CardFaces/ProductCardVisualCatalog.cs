// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.GodotClient.CardFaces;

internal sealed class ProductCardVisualCatalog : IProductCardVisualCatalog
{
    internal const string SliceArtRoot = "res://assets/visual/anime_v1/slice/cards";
    internal const string FallbackArt = "res://assets/visual/cards/shared/fallback_front.svg";
    internal const string SharedCardBack = "res://assets/visual/anime_v1/slice/shared/card-back.png";

    private static readonly IReadOnlyDictionary<string, string> SliceArt =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["LO-03"] = SliceArtRoot + "/LO-03.png",
            ["LO-07"] = SliceArtRoot + "/LO-07.png",
            ["LO-11"] = SliceArtRoot + "/LO-11.png",
            ["LO-11-EVOLVED"] = SliceArtRoot + "/LO-11-evolved.png",
            ["AP-03"] = SliceArtRoot + "/AP-03.png",
            ["AP-05"] = SliceArtRoot + "/AP-05.png",
            ["AP-11"] = SliceArtRoot + "/AP-11.png",
            ["AP-11-EVOLVED"] = SliceArtRoot + "/AP-11-evolved.png",
            ["NT-04"] = SliceArtRoot + "/NT-04.png",
        };

    private readonly IReadOnlyDictionary<string, ProductCardVisualEntry> _entries;

    internal static ProductCardVisualCatalog Shared { get; } = new();

    internal ProductCardVisualCatalog()
    {
        ProductCardVisualEntry[] entries =
        [
            Entry("LO-01", ProductCardFaction.Oathguard, ProductCardKind.Follower, CardVisualRarity.Common),
            Entry("LO-02", ProductCardFaction.Oathguard, ProductCardKind.Follower, CardVisualRarity.Common),
            Entry("LO-03", ProductCardFaction.Oathguard, ProductCardKind.Amulet, CardVisualRarity.Rare),
            Entry("LO-04", ProductCardFaction.Oathguard, ProductCardKind.Follower, CardVisualRarity.Common),
            Entry("LO-05", ProductCardFaction.Oathguard, ProductCardKind.Follower, CardVisualRarity.Rare),
            Entry("LO-06", ProductCardFaction.Oathguard, ProductCardKind.Spell, CardVisualRarity.Rare),
            Entry("LO-07", ProductCardFaction.Oathguard, ProductCardKind.Trap, CardVisualRarity.Rare),
            Entry("LO-08", ProductCardFaction.Oathguard, ProductCardKind.Follower, CardVisualRarity.Epic),
            Entry("LO-09", ProductCardFaction.Oathguard, ProductCardKind.Follower, CardVisualRarity.Epic),
            Entry("LO-10", ProductCardFaction.Oathguard, ProductCardKind.Field, CardVisualRarity.Epic),
            Entry("LO-11", ProductCardFaction.Oathguard, ProductCardKind.Follower, CardVisualRarity.Legendary, evolved: true),
            Entry("LO-S01", ProductCardFaction.Oathguard, ProductCardKind.Follower, CardVisualRarity.Rare),
            Entry("LO-S02", ProductCardFaction.Oathguard, ProductCardKind.Follower, CardVisualRarity.Epic),
            Entry("LO-S03", ProductCardFaction.Oathguard, ProductCardKind.Follower, CardVisualRarity.Epic),
            Entry("LO-S04", ProductCardFaction.Oathguard, ProductCardKind.Follower, CardVisualRarity.Legendary),
            Entry("AP-01", ProductCardFaction.Pactmage, ProductCardKind.Follower, CardVisualRarity.Common),
            Entry("AP-02", ProductCardFaction.Pactmage, ProductCardKind.Follower, CardVisualRarity.Common),
            Entry("AP-03", ProductCardFaction.Pactmage, ProductCardKind.Spell, CardVisualRarity.Rare),
            Entry("AP-04", ProductCardFaction.Pactmage, ProductCardKind.Amulet, CardVisualRarity.Rare),
            Entry("AP-05", ProductCardFaction.Pactmage, ProductCardKind.Field, CardVisualRarity.Epic),
            Entry("AP-06", ProductCardFaction.Pactmage, ProductCardKind.Follower, CardVisualRarity.Rare),
            Entry("AP-07", ProductCardFaction.Pactmage, ProductCardKind.Follower, CardVisualRarity.Epic),
            Entry("AP-08", ProductCardFaction.Pactmage, ProductCardKind.Spell, CardVisualRarity.Rare),
            Entry("AP-09", ProductCardFaction.Pactmage, ProductCardKind.Follower, CardVisualRarity.Epic),
            Entry("AP-10", ProductCardFaction.Pactmage, ProductCardKind.Follower, CardVisualRarity.Epic),
            Entry("AP-11", ProductCardFaction.Pactmage, ProductCardKind.Follower, CardVisualRarity.Legendary, evolved: true),
            Entry("AP-S01", ProductCardFaction.Pactmage, ProductCardKind.Follower, CardVisualRarity.Rare),
            Entry("AP-S02", ProductCardFaction.Pactmage, ProductCardKind.Follower, CardVisualRarity.Epic),
            Entry("AP-S03", ProductCardFaction.Pactmage, ProductCardKind.Follower, CardVisualRarity.Epic),
            Entry("AP-S04", ProductCardFaction.Pactmage, ProductCardKind.Follower, CardVisualRarity.Legendary),
            Entry("NT-01", ProductCardFaction.Neutral, ProductCardKind.Follower, CardVisualRarity.Common),
            Entry("NT-02", ProductCardFaction.Neutral, ProductCardKind.Follower, CardVisualRarity.Common),
            Entry("NT-03", ProductCardFaction.Neutral, ProductCardKind.Amulet, CardVisualRarity.Rare),
            Entry("NT-04", ProductCardFaction.Neutral, ProductCardKind.Spell, CardVisualRarity.Epic),
            new ProductCardVisualEntry(
                "LO-T01",
                ProductCardFaction.Oathguard,
                ProductCardKind.Follower,
                CardVisualRarity.Common,
                FallbackArt),
        ];

        _entries = entries.ToDictionary(entry => entry.DesignId, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<ProductCardVisualEntry> Entries => _entries.Values.ToArray();

    public ProductCardVisualEntry? Find(string designId) =>
        string.IsNullOrWhiteSpace(designId)
            ? null
            : _entries.GetValueOrDefault(designId);

    public ProductCardVisualEntry Resolve(string designId) =>
        Find(designId) ?? new ProductCardVisualEntry(
            designId,
            ProductCardFaction.Neutral,
            ProductCardKind.Follower,
            CardVisualRarity.Common,
            FallbackArt);

    public string ResolveArtPath(ProductCardVisualEntry entry, CardFrameVariant variant)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (variant == CardFrameVariant.Evolved && entry.EvolvedArtPath is not null)
        {
            return entry.EvolvedArtPath;
        }
        return entry.BaseArtPath;
    }

    private static ProductCardVisualEntry Entry(
        string designId,
        ProductCardFaction faction,
        ProductCardKind kind,
        CardVisualRarity rarity,
        bool evolved = false)
    {
        string baseArt = SliceArt.GetValueOrDefault(designId, FallbackArt);
        string? evolvedArt = evolved
            ? SliceArt.GetValueOrDefault($"{designId}-EVOLVED", baseArt)
            : null;
        return new ProductCardVisualEntry(
            designId,
            faction,
            kind,
            rarity,
            baseArt,
            evolvedArt);
    }
}
