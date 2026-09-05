// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scgs.GodotClient.Presentation;
using V05 = Scgs.Client.V05;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class ProductCardTextCatalogTests
{
    [TestMethod]
    public void LockedProductTextCatalogCoversThirtyFourCardsAndOneToken()
    {
        ProductCardTextEntry[] entries = ProductCardTextCatalog.Entries
            .OrderBy(entry => entry.DesignId, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(35, entries);
        Assert.AreEqual(35, entries.Select(entry => entry.DesignId).Distinct().Count());
        Assert.AreEqual(34, entries.Count(entry => entry.Availability != "token"));
        Assert.AreEqual("誓光守卫", ProductCardTextCatalog.Find("LO-T01")?.Name);
        Assert.AreEqual("token", ProductCardTextCatalog.Find("LO-T01")?.Availability);
        Assert.IsTrue(entries.All(entry =>
            !string.IsNullOrWhiteSpace(entry.Name) &&
            !string.IsNullOrWhiteSpace(entry.CanonicalRulesText)));
        Assert.IsNull(ProductCardTextCatalog.Find("UNKNOWN-01"));
    }

    [TestMethod]
    public void ProductRulesFormatterUsesCanonicalTextAndAllProductKeywords()
    {
        var card = new V05.CardView
        {
            InstanceId = 42,
            DesignId = "AP-07",
            ProfessionId = "pactmage",
            SeriesId = "abyssal_pact",
            Neutral = false,
            Kind = V05.CardKind.Follower,
            Name = "黑蔷薇校医·维奥拉",
            Owner = V05.PlayerId.Player0,
            Controller = V05.PlayerId.Player0,
            Zone = V05.Zone.MainBoard,
            Sequence = 0,
            Cost = 4,
            CurrentAttack = 3,
            CurrentHealth = 5,
            MaximumHealth = 5,
            PrintedKeywords = V05.Keyword.Lifesteal,
            PermanentKeywords = V05.Keyword.None,
            TurnKeywords = V05.Keyword.Rush,
            Keywords = V05.Keyword.Lifesteal | V05.Keyword.Rush,
            Evolved = false,
            AttackedThisTurn = false,
            EnteredThisTurn = true,
            FaceDown = false,
            Countdown = 0,
        };

        string text = ProductCardPresentation.FormatRules(card);
        StringAssert.Contains(text, "黑蔷薇校医·维奥拉");
        StringAssert.Contains(text, "吸血");
        StringAssert.Contains(text, "突进");
        StringAssert.Contains(text, "超前：本回合获得突进");
    }
}
