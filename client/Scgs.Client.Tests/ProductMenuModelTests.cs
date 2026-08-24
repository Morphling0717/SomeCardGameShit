// SPDX-License-Identifier: GPL-3.0-or-later
using Scgs.GodotClient.UI;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class ProductMenuModelTests
{
    [TestMethod]
    public void ProductShellPublishesEveryLockedGate4BEntryExactlyOnce()
    {
        Assert.AreEqual(8, ProductMenuCatalog.Entries.Count);
        CollectionAssert.AreEquivalent(
            Enum.GetValues<ProductMenuFeature>(),
            ProductMenuCatalog.Entries.Select(entry => entry.Feature).ToArray());
    }

    [TestMethod]
    public void OnlyLocalHotseatRequiresTheNativeSession()
    {
        ProductMenuEntry[] nativeEntries = ProductMenuCatalog.Entries
            .Where(entry => entry.RequiresNativeSession)
            .ToArray();

        Assert.HasCount(1, nativeEntries);
        Assert.AreEqual(ProductMenuFeature.LocalHotseat, nativeEntries[0].Feature);
        Assert.AreEqual(ProductMenuFeatureStatus.Available, nativeEntries[0].Status);
    }

    [TestMethod]
    public void DevelopmentEntriesCannotReachNativeSessionCreation()
    {
        ProductMenuEntry[] developmentEntries = ProductMenuCatalog.Entries
            .Where(entry => entry.Status == ProductMenuFeatureStatus.InDevelopment)
            .ToArray();

        Assert.HasCount(5, developmentEntries);
        Assert.IsTrue(developmentEntries.All(entry => !entry.RequiresNativeSession));
    }

    [TestMethod]
    public void SettingsAndExitRemainAvailableWithoutNativeEngine()
    {
        foreach (ProductMenuFeature feature in
                 new[] { ProductMenuFeature.Settings, ProductMenuFeature.Exit })
        {
            ProductMenuEntry entry = ProductMenuCatalog.Get(feature);
            Assert.AreEqual(ProductMenuFeatureStatus.Available, entry.Status);
            Assert.IsFalse(entry.RequiresNativeSession);
        }
    }
}
