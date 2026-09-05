// SPDX-License-Identifier: GPL-3.0-or-later
using Scgs.GodotClient.CardFaces;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class CardFaceContractsTests
{
    private static readonly IReadOnlyDictionary<string, CardVisualRarity> ExpectedRarities =
        new Dictionary<string, CardVisualRarity>(StringComparer.Ordinal)
        {
            ["LO-01"] = CardVisualRarity.Common,
            ["LO-02"] = CardVisualRarity.Common,
            ["LO-03"] = CardVisualRarity.Rare,
            ["LO-04"] = CardVisualRarity.Common,
            ["LO-05"] = CardVisualRarity.Rare,
            ["LO-06"] = CardVisualRarity.Rare,
            ["LO-07"] = CardVisualRarity.Rare,
            ["LO-08"] = CardVisualRarity.Epic,
            ["LO-09"] = CardVisualRarity.Epic,
            ["LO-10"] = CardVisualRarity.Epic,
            ["LO-11"] = CardVisualRarity.Legendary,
            ["LO-S01"] = CardVisualRarity.Rare,
            ["LO-S02"] = CardVisualRarity.Epic,
            ["LO-S03"] = CardVisualRarity.Epic,
            ["LO-S04"] = CardVisualRarity.Legendary,
            ["AP-01"] = CardVisualRarity.Common,
            ["AP-02"] = CardVisualRarity.Common,
            ["AP-03"] = CardVisualRarity.Rare,
            ["AP-04"] = CardVisualRarity.Rare,
            ["AP-05"] = CardVisualRarity.Epic,
            ["AP-06"] = CardVisualRarity.Rare,
            ["AP-07"] = CardVisualRarity.Epic,
            ["AP-08"] = CardVisualRarity.Rare,
            ["AP-09"] = CardVisualRarity.Epic,
            ["AP-10"] = CardVisualRarity.Epic,
            ["AP-11"] = CardVisualRarity.Legendary,
            ["AP-S01"] = CardVisualRarity.Rare,
            ["AP-S02"] = CardVisualRarity.Epic,
            ["AP-S03"] = CardVisualRarity.Epic,
            ["AP-S04"] = CardVisualRarity.Legendary,
            ["NT-01"] = CardVisualRarity.Common,
            ["NT-02"] = CardVisualRarity.Common,
            ["NT-03"] = CardVisualRarity.Rare,
            ["NT-04"] = CardVisualRarity.Epic,
        };

    [TestMethod]
    public void VisualEnumsAreFrozenForTheApprovalSlice()
    {
        CollectionAssert.AreEqual(
            new[] { 0, 1, 2, 3 },
            Enum.GetValues<CardVisualRarity>().Select(value => (int)value).ToArray());
        CollectionAssert.AreEqual(
            new[] { 0, 1, 2 },
            Enum.GetValues<CardFaceContext>().Select(value => (int)value).ToArray());
        CollectionAssert.AreEqual(
            new[] { 0, 1, 2 },
            Enum.GetValues<CardFrameVariant>().Select(value => (int)value).ToArray());
    }

    [TestMethod]
    public void ProductCatalogCoversTheExactLockedPoolAndRarityMapping()
    {
        ProductCardVisualCatalog catalog = ProductCardVisualCatalog.Shared;
        Assert.HasCount(35, catalog.Entries);
        Assert.AreEqual(35, catalog.Entries.Select(entry => entry.DesignId).Distinct().Count());

        foreach ((string designId, CardVisualRarity rarity) in ExpectedRarities)
        {
            ProductCardVisualEntry? entry = catalog.Find(designId);
            Assert.IsNotNull(entry, designId);
            Assert.AreEqual(rarity, entry.Rarity, designId);
        }

        ProductCardVisualEntry token = catalog.Resolve("LO-T01");
        Assert.AreEqual(ProductCardFaction.Oathguard, token.Faction);
        Assert.AreEqual(ProductCardKind.Follower, token.Kind);
        Assert.AreEqual(
            ProductCardVisualCatalog.ProductArtRoot + "/LO-T01.png",
            token.BaseArtPath);
    }

    [TestMethod]
    public void EveryLockedProductIdentityUsesUniqueExistingRealBaseArt()
    {
        string repo = FindRepositoryRoot();
        ProductCardVisualEntry[] entries = ProductCardVisualCatalog.Shared.Entries
            .OrderBy(entry => entry.DesignId, StringComparer.Ordinal)
            .ToArray();
        string[] baseArtPaths = entries.Select(entry => entry.BaseArtPath).ToArray();

        Assert.HasCount(35, entries);
        Assert.AreEqual(35, baseArtPaths.Distinct(StringComparer.Ordinal).Count());
        Assert.IsFalse(baseArtPaths.Contains(
            ProductCardVisualCatalog.FallbackArt,
            StringComparer.Ordinal));

        foreach (string resourcePath in baseArtPaths)
        {
            Assert.IsTrue(resourcePath.EndsWith(".png", StringComparison.Ordinal));
            string relative = resourcePath.Replace(
                "res://",
                "client/godot/",
                StringComparison.Ordinal);
            Assert.IsTrue(File.Exists(Path.Combine(repo, relative)), resourcePath);
        }
    }

    [TestMethod]
    public void ProductCatalogUsesFiveKindsAndThreeFactions()
    {
        ProductCardVisualCatalog catalog = ProductCardVisualCatalog.Shared;
        CollectionAssert.AreEquivalent(
            Enum.GetValues<ProductCardKind>(),
            catalog.Entries.Select(entry => entry.Kind).Distinct().ToArray());
        CollectionAssert.AreEquivalent(
            Enum.GetValues<ProductCardFaction>(),
            catalog.Entries.Select(entry => entry.Faction).Distinct().ToArray());

        Assert.AreEqual(ProductCardKind.Trap, catalog.Resolve("LO-07").Kind);
        Assert.AreEqual(ProductCardKind.Field, catalog.Resolve("AP-05").Kind);
        Assert.AreEqual(ProductCardKind.Amulet, catalog.Resolve("NT-03").Kind);
    }

    [TestMethod]
    public void OnlyTheTwoLockedAcesHaveDistinctEvolvedArtwork()
    {
        ProductCardVisualCatalog catalog = ProductCardVisualCatalog.Shared;
        string[] evolved = catalog.Entries
            .Where(entry => entry.EvolvedArtPath is not null)
            .Select(entry => entry.DesignId)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "AP-11", "LO-11" }, evolved);
        Assert.EndsWith(
            "/LO-11-evolved.png",
            catalog.ResolveArtPath(catalog.Resolve("LO-11"), CardFrameVariant.Evolved));
        Assert.AreEqual(
            ProductCardVisualCatalog.ProductArtRoot + "/LO-01.png",
            catalog.ResolveArtPath(catalog.Resolve("LO-01"), CardFrameVariant.Evolved));
    }

    [TestMethod]
    public void FrameCatalogProvidesEveryKindFactionRarityAndVariantCombination()
    {
        CardFrameStyleCatalog catalog = CardFrameStyleCatalog.Shared;
        Assert.HasCount(180, catalog.Styles);
        Assert.AreEqual(180, catalog.Styles.Select(style => style.Key).Distinct().Count());

        foreach (ProductCardFaction faction in Enum.GetValues<ProductCardFaction>())
        foreach (ProductCardKind kind in Enum.GetValues<ProductCardKind>())
        foreach (CardVisualRarity rarity in Enum.GetValues<CardVisualRarity>())
        foreach (CardFrameVariant variant in Enum.GetValues<CardFrameVariant>())
        {
            CardFrameStyle style = catalog.Resolve(new CardFrameStyleKey(faction, kind, rarity, variant));
            Assert.EndsWith($"/{kind.ToString().ToLowerInvariant()}.svg", style.SilhouettePath);
            Assert.IsTrue(style.CrestPath.Contains(faction.ToString().ToLowerInvariant(), StringComparison.Ordinal));
            Assert.IsTrue(style.NamePlatePath.Contains(faction.ToString().ToLowerInvariant(), StringComparison.Ordinal));
            if (variant == CardFrameVariant.Token)
            {
                Assert.IsNull(style.RarityOverlayPath);
            }
            else
            {
                Assert.IsNotNull(style.RarityOverlayPath);
            }
            Assert.AreEqual(
                rarity == CardVisualRarity.Legendary,
                style.FoilTexturePath is not null);
        }
    }

    [TestMethod]
    public void NormalizedLayoutsStayInsideAThreeByFourCard()
    {
        foreach (CardFaceContext context in Enum.GetValues<CardFaceContext>())
        foreach (ProductCardKind kind in Enum.GetValues<ProductCardKind>())
        {
            CardFaceLayout layout = CardFaceLayout.For(context, kind);
            layout.Validate();
            Assert.IsTrue(layout.ArtWindow.IsInsideUnitSquare);
            Assert.AreEqual(new CardFaceRect(0.0f, 0.0f, 1.0f, 1.0f), layout.ArtWindow);
            Assert.IsTrue(layout.NamePlate.IsInsideUnitSquare);
            Assert.IsTrue(layout.NameText.IsInsideUnitSquare);
            Assert.IsGreaterThanOrEqualTo(0.094f, layout.NameText.X - layout.NamePlate.X);
            Assert.IsGreaterThanOrEqualTo(0.094f, layout.NamePlate.Right - layout.NameText.Right);
            Assert.IsGreaterThanOrEqualTo(0.011f, layout.NameText.Y - layout.NamePlate.Y);
            Assert.IsGreaterThanOrEqualTo(0.011f, layout.NamePlate.Bottom - layout.NameText.Bottom);
            Assert.IsGreaterThanOrEqualTo(0.58f, layout.NameText.Width);
            Assert.IsTrue(layout.CostGem.IsInsideUnitSquare);
            Assert.IsTrue(layout.CostText.IsInsideUnitSquare);
            Assert.IsTrue(layout.TypeCrest.IsInsideUnitSquare);
            Assert.AreEqual(kind == ProductCardKind.Follower, layout.AttackGem.HasValue);
            Assert.AreEqual(kind == ProductCardKind.Follower, layout.AttackText.HasValue);
            Assert.AreEqual(kind == ProductCardKind.Follower, layout.HealthGem.HasValue);
            Assert.AreEqual(kind == ProductCardKind.Follower, layout.HealthText.HasValue);
            Assert.AreEqual(kind is ProductCardKind.Amulet or ProductCardKind.Trap, layout.CountdownGem.HasValue);
            Assert.AreEqual(kind is ProductCardKind.Amulet or ProductCardKind.Trap, layout.CountdownText.HasValue);
            if (kind == ProductCardKind.Follower)
            {
                Assert.IsGreaterThanOrEqualTo(0.27f, layout.AttackGem!.Value.Width);
                Assert.IsGreaterThanOrEqualTo(0.27f, layout.HealthGem!.Value.Width);
                Assert.IsGreaterThanOrEqualTo(0.20f, layout.AttackText!.Value.Width);
                Assert.IsGreaterThanOrEqualTo(0.20f, layout.HealthText!.Value.Width);
            }
            if (kind is ProductCardKind.Amulet or ProductCardKind.Trap)
            {
                Assert.IsGreaterThanOrEqualTo(0.30f, layout.CountdownGem!.Value.Width);
                Assert.IsGreaterThanOrEqualTo(0.21f, layout.CountdownText!.Value.Width);
            }
        }
    }

    [TestMethod]
    public void TwoByThreeArtworkUsesCenteredCoverCropWithoutStretching()
    {
        CardFaceLayout layout = CardFaceLayout.For(CardFaceContext.Hand, ProductCardKind.Follower);
        CardArtCrop crop = CardArtCrop.Cover(1024, 1536, layout.ArtWindowAspectRatio);
        float croppedAspect = (1024.0f * crop.Width) / (1536.0f * crop.Height);

        Assert.AreEqual(layout.ArtWindowAspectRatio, croppedAspect, 0.0001f);
        Assert.AreEqual(1.0f, crop.Width);
        Assert.IsLessThan(1.0f, crop.Height);
        Assert.AreEqual((1.0f - crop.Height) * 0.5f, crop.V, 0.0001f);
    }

    [TestMethod]
    public void ArtworkFocusIsClampedWhileMaintainingTheCoverRatio()
    {
        CardArtCrop left = CardArtCrop.Cover(2048, 1024, 0.75f, focusX: 0.0f);
        CardArtCrop right = CardArtCrop.Cover(2048, 1024, 0.75f, focusX: 1.0f);

        Assert.AreEqual(0.0f, left.U);
        Assert.AreEqual(1.0f - right.Width, right.U, 0.0001f);
        Assert.AreEqual(left.Width, right.Width);
    }

    [TestMethod]
    public void CompositionKeepsAllGameplayNumbersInIntegratedSlots()
    {
        var model = new CardFaceViewModel
        {
            DesignId = "LO-11",
            DisplayName = "曜誓大团长·蕾奥妮",
            Kind = ProductCardKind.Follower,
            Faction = ProductCardFaction.Oathguard,
            Rarity = CardVisualRarity.Legendary,
            Cost = 10,
            Attack = 12,
            Health = 10,
            Variant = CardFrameVariant.Evolved,
        };

        CardFaceComposition composition = CardFaceComposer.Compose(
            model,
            CardFaceContext.Hand,
            ProductCardVisualCatalog.Shared,
            CardFrameStyleCatalog.Shared);

        Assert.AreEqual(10, composition.ViewModel.Cost);
        Assert.AreEqual(12, composition.ViewModel.Attack);
        Assert.AreEqual(10, composition.ViewModel.Health);
        Assert.IsNotNull(composition.Layout.AttackGem);
        Assert.IsNotNull(composition.Layout.HealthGem);
        Assert.EndsWith("LO-11-evolved.png", composition.ArtPath);
        Assert.AreEqual(CardFrameVariant.Evolved, composition.FrameStyle.Key.Variant);
        Assert.IsNotNull(composition.FrameStyle.FoilTexturePath);
    }

    [TestMethod]
    public void LongNamesRemainCompleteInTheAuthoritativeComposition()
    {
        var model = new CardFaceViewModel
        {
            DesignId = "AP-05",
            DisplayName = "渊契魔导院·零时讲堂超长验收名称",
            Kind = ProductCardKind.Field,
            Faction = ProductCardFaction.Pactmage,
            Rarity = CardVisualRarity.Epic,
            Cost = 3,
        };

        CardFaceComposition composition = CardFaceComposer.Compose(
            model,
            CardFaceContext.Field,
            ProductCardVisualCatalog.Shared,
            CardFrameStyleCatalog.Shared);

        Assert.AreEqual(model.DisplayName, composition.ViewModel.DisplayName);
        Assert.DoesNotContain("…", composition.ViewModel.DisplayName);
    }

    [TestMethod]
    public void InvalidStatShapesAreRejectedBeforeComposition()
    {
        var spellWithStats = new CardFaceViewModel
        {
            DesignId = "AP-03",
            DisplayName = "契式·违约穿刺",
            Kind = ProductCardKind.Spell,
            Faction = ProductCardFaction.Pactmage,
            Rarity = CardVisualRarity.Rare,
            Cost = 2,
            Attack = 3,
            Health = 3,
        };
        Assert.Throws<ArgumentException>(spellWithStats.Validate);

        var wrongToken = spellWithStats with
        {
            Attack = null,
            Health = null,
            Variant = CardFrameVariant.Token,
        };
        Assert.Throws<ArgumentException>(wrongToken.Validate);
    }

    [TestMethod]
    public void KnownProductIdentityRejectsFactionKindAndRarityMismatch()
    {
        var canonical = new CardFaceViewModel
        {
            DesignId = "LO-03",
            DisplayName = "晨钟誓碑",
            Kind = ProductCardKind.Amulet,
            Faction = ProductCardFaction.Oathguard,
            Rarity = CardVisualRarity.Rare,
            Cost = 2,
            Countdown = 3,
        };

        _ = CardFaceComposer.Compose(
            canonical,
            CardFaceContext.Hand,
            ProductCardVisualCatalog.Shared,
            CardFrameStyleCatalog.Shared);
        Assert.Throws<ArgumentException>(() => CardFaceComposer.Compose(
            canonical with { Faction = ProductCardFaction.Neutral },
            CardFaceContext.Hand,
            ProductCardVisualCatalog.Shared,
            CardFrameStyleCatalog.Shared));
        Assert.Throws<ArgumentException>(() => CardFaceComposer.Compose(
            canonical with { Kind = ProductCardKind.Trap },
            CardFaceContext.Hand,
            ProductCardVisualCatalog.Shared,
            CardFrameStyleCatalog.Shared));
        Assert.Throws<ArgumentException>(() => CardFaceComposer.Compose(
            canonical with { Rarity = CardVisualRarity.Legendary },
            CardFaceContext.Hand,
            ProductCardVisualCatalog.Shared,
            CardFrameStyleCatalog.Shared));
    }

    [TestMethod]
    public void StylePreviewRequiresAnExplicitMatchingVirtualIdentity()
    {
        var model = new CardFaceViewModel
        {
            DesignId = "STYLE-PREVIEW:Neutral:Field:Legendary",
            DisplayName = "中立·场地",
            Kind = ProductCardKind.Field,
            Faction = ProductCardFaction.Neutral,
            Rarity = CardVisualRarity.Legendary,
            Cost = 4,
        };
        var visual = new ProductCardVisualEntry(
            model.DesignId,
            model.Faction,
            model.Kind,
            model.Rarity,
            ProductCardVisualCatalog.FallbackArt);

        CardFaceComposition preview = CardFaceComposer.ComposeStylePreview(
            model,
            CardFaceContext.Field,
            visual,
            ProductCardVisualCatalog.Shared,
            CardFrameStyleCatalog.Shared);
        Assert.AreEqual(model.DesignId, preview.Visual.DesignId);

        Assert.Throws<ArgumentException>(() => CardFaceComposer.ComposeStylePreview(
            model,
            CardFaceContext.Field,
            visual with { Kind = ProductCardKind.Follower },
            ProductCardVisualCatalog.Shared,
            CardFrameStyleCatalog.Shared));
        Assert.Throws<ArgumentException>(() => CardFaceComposer.ComposeStylePreview(
            model with { DesignId = "LO-11" },
            CardFaceContext.Field,
            visual with { DesignId = "LO-11" },
            ProductCardVisualCatalog.Shared,
            CardFrameStyleCatalog.Shared));
    }

    [TestMethod]
    public void AllDeterministicFrameAssetsExistInTheCheckout()
    {
        string repo = FindRepositoryRoot();
        CardFrameStyleCatalog catalog = CardFrameStyleCatalog.Shared;
        string[] resourcePaths = catalog.Styles
            .SelectMany(style => new[]
            {
                style.SilhouettePath,
                style.CrestPath,
                style.NamePlatePath,
                style.RarityOverlayPath,
                style.VariantOverlayPath,
                style.MaterialTexturePath,
                style.FoilTexturePath,
                style.CostGemPath,
                style.AttackGemPath,
                style.HealthGemPath,
                style.CountdownGemPath,
            })
            .Where(path => path is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(23, resourcePaths);
        foreach (string resourcePath in resourcePaths)
        {
            string relative = resourcePath.Replace("res://", "client/godot/", StringComparison.Ordinal);
            Assert.IsTrue(File.Exists(Path.Combine(repo, relative)), resourcePath);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
