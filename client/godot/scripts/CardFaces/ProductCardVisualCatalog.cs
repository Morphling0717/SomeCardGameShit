// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.GodotClient.CardFaces;

internal sealed class ProductCardVisualCatalog : IProductCardVisualCatalog
{
    internal const string SliceArtRoot = "res://assets/visual/anime_v1/slice/cards";
    internal const string ProductArtRoot = "res://assets/visual/anime_v1/cards";
    internal const string FallbackArt = "res://assets/visual/anime_v1/shared/fallback_front.svg";
    internal const string SharedCardBack = "res://assets/visual/anime_v1/slice/shared/card-back.png";
    internal const int MaxResidentIdentityTextures = 24;

    private static readonly IReadOnlyDictionary<string, string> ProductArt =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["LO-01"] = ProductArtRoot + "/LO-01.png",
            ["LO-02"] = ProductArtRoot + "/LO-02.png",
            ["LO-03"] = SliceArtRoot + "/LO-03.png",
            ["LO-04"] = ProductArtRoot + "/LO-04.png",
            ["LO-05"] = ProductArtRoot + "/LO-05.png",
            ["LO-06"] = ProductArtRoot + "/LO-06.png",
            ["LO-07"] = SliceArtRoot + "/LO-07.png",
            ["LO-08"] = ProductArtRoot + "/LO-08.png",
            ["LO-09"] = ProductArtRoot + "/LO-09.png",
            ["LO-10"] = ProductArtRoot + "/LO-10.png",
            ["LO-11"] = SliceArtRoot + "/LO-11.png",
            ["LO-11-EVOLVED"] = SliceArtRoot + "/LO-11-evolved.png",
            ["LO-S01"] = ProductArtRoot + "/LO-S01.png",
            ["LO-S02"] = ProductArtRoot + "/LO-S02.png",
            ["LO-S03"] = ProductArtRoot + "/LO-S03.png",
            ["LO-S04"] = ProductArtRoot + "/LO-S04.png",
            ["LO-T01"] = ProductArtRoot + "/LO-T01.png",
            ["AP-01"] = ProductArtRoot + "/AP-01.png",
            ["AP-02"] = ProductArtRoot + "/AP-02.png",
            ["AP-03"] = SliceArtRoot + "/AP-03.png",
            ["AP-04"] = ProductArtRoot + "/AP-04.png",
            ["AP-05"] = SliceArtRoot + "/AP-05.png",
            ["AP-06"] = ProductArtRoot + "/AP-06.png",
            ["AP-07"] = ProductArtRoot + "/AP-07.png",
            ["AP-08"] = ProductArtRoot + "/AP-08.png",
            ["AP-09"] = ProductArtRoot + "/AP-09.png",
            ["AP-10"] = ProductArtRoot + "/AP-10.png",
            ["AP-11"] = SliceArtRoot + "/AP-11.png",
            ["AP-11-EVOLVED"] = SliceArtRoot + "/AP-11-evolved.png",
            ["AP-S01"] = ProductArtRoot + "/AP-S01.png",
            ["AP-S02"] = ProductArtRoot + "/AP-S02.png",
            ["AP-S03"] = ProductArtRoot + "/AP-S03.png",
            ["AP-S04"] = ProductArtRoot + "/AP-S04.png",
            ["NT-01"] = ProductArtRoot + "/NT-01.png",
            ["NT-02"] = ProductArtRoot + "/NT-02.png",
            ["NT-03"] = ProductArtRoot + "/NT-03.png",
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
            Entry("LO-T01", ProductCardFaction.Oathguard, ProductCardKind.Follower, CardVisualRarity.Common),
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
        string baseArt = ProductArt.GetValueOrDefault(designId, FallbackArt);
        string? evolvedArt = evolved
            ? ProductArt.GetValueOrDefault($"{designId}-EVOLVED", baseArt)
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
