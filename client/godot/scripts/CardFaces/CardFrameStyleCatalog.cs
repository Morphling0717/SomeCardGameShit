// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.GodotClient.CardFaces;

internal sealed class CardFrameStyleCatalog : ICardFrameStyleCatalog
{
    internal const string Root = "res://assets/visual/anime_v1/card_body";
    internal const string CostGem = Root + "/gems/cost.svg";
    internal const string AttackGem = Root + "/gems/attack.svg";
    internal const string HealthGem = Root + "/gems/health.svg";
    internal const string CountdownGem = Root + "/gems/countdown.svg";

    private static readonly IReadOnlyDictionary<ProductCardKind, string> Silhouettes =
        new Dictionary<ProductCardKind, string>
        {
            [ProductCardKind.Follower] = Root + "/frames/follower.svg",
            [ProductCardKind.Spell] = Root + "/frames/spell.svg",
            [ProductCardKind.Amulet] = Root + "/frames/amulet.svg",
            [ProductCardKind.Trap] = Root + "/frames/trap.svg",
            [ProductCardKind.Field] = Root + "/frames/field.svg",
        };

    private static readonly IReadOnlyDictionary<ProductCardFaction, string> Crests =
        new Dictionary<ProductCardFaction, string>
        {
            [ProductCardFaction.Neutral] = Root + "/crests/neutral.svg",
            [ProductCardFaction.Oathguard] = Root + "/crests/oathguard.svg",
            [ProductCardFaction.Pactmage] = Root + "/crests/pactmage.svg",
        };

    private static readonly IReadOnlyDictionary<ProductCardFaction, string> NamePlates =
        new Dictionary<ProductCardFaction, string>
        {
            [ProductCardFaction.Neutral] = Root + "/nameplates/neutral.svg",
            [ProductCardFaction.Oathguard] = Root + "/nameplates/oathguard.svg",
            [ProductCardFaction.Pactmage] = Root + "/nameplates/pactmage.svg",
        };

    private static readonly IReadOnlyDictionary<CardVisualRarity, string> RarityOverlays =
        new Dictionary<CardVisualRarity, string>
        {
            [CardVisualRarity.Common] = Root + "/rarity/common.svg",
            [CardVisualRarity.Rare] = Root + "/rarity/rare.svg",
            [CardVisualRarity.Epic] = Root + "/rarity/epic.svg",
            [CardVisualRarity.Legendary] = Root + "/rarity/legendary.svg",
        };

    private static readonly IReadOnlyDictionary<CardFrameVariant, string?> VariantOverlays =
        new Dictionary<CardFrameVariant, string?>
        {
            [CardFrameVariant.Normal] = null,
            [CardFrameVariant.Evolved] = Root + "/variants/evolved.svg",
            [CardFrameVariant.Token] = Root + "/variants/token.svg",
        };

    internal const string EngravedMetal = Root + "/materials/engraved-metal-v1.png";
    internal const string LegendaryFoil = Root + "/materials/legendary-foil-v1.png";

    private readonly IReadOnlyDictionary<CardFrameStyleKey, CardFrameStyle> _styles;

    internal static CardFrameStyleCatalog Shared { get; } = new();

    internal CardFrameStyleCatalog()
    {
        _styles = Enum.GetValues<ProductCardFaction>()
            .SelectMany(faction => Enum.GetValues<ProductCardKind>()
                .SelectMany(kind => Enum.GetValues<CardVisualRarity>()
                    .SelectMany(rarity => Enum.GetValues<CardFrameVariant>()
                        .Select(variant => Create(new CardFrameStyleKey(
                            faction,
                            kind,
                            rarity,
                            variant))))))
            .ToDictionary(style => style.Key);
    }

    public IReadOnlyCollection<CardFrameStyle> Styles => _styles.Values.ToArray();

    public CardFrameStyle Resolve(CardFrameStyleKey key) =>
        _styles.TryGetValue(key, out CardFrameStyle? style)
            ? style
            : throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown AnimeV1 frame style key.");

    private static CardFrameStyle Create(CardFrameStyleKey key) => new(
        key,
        Silhouettes[key.Kind],
        Crests[key.Faction],
        NamePlates[key.Faction],
        key.Variant == CardFrameVariant.Token ? null : RarityOverlays[key.Rarity],
        VariantOverlays[key.Variant],
        EngravedMetal,
        key.Rarity == CardVisualRarity.Legendary ? LegendaryFoil : null,
        CostGem,
        AttackGem,
        HealthGem,
        CountdownGem);
}
