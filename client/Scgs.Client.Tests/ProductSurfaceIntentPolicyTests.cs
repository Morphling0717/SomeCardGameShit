using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scgs.Hotseat.Product;
using V05 = Scgs.Client.V05;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class ProductSurfaceIntentPolicyTests
{
    [TestMethod]
    public void SlotSurfacePreservesOwnerZoneAndExactIndex()
    {
        foreach (V05.ActionKind action in new[] { V05.ActionKind.PlayUnit, V05.ActionKind.PlayAmulet,
            V05.ActionKind.Deploy, V05.ActionKind.CastSpell, V05.ActionKind.PlayTrap, V05.ActionKind.PlayField })
        {
            ProductSlotSurfaceKind kind = action switch
            {
                V05.ActionKind.CastSpell or V05.ActionKind.PlayTrap => ProductSlotSurfaceKind.Tactic,
                V05.ActionKind.PlayField => ProductSlotSurfaceKind.Field,
                _ => ProductSlotSurfaceKind.MainBoard,
            };
            var command = new V05.GameCommandRequest(V05.PlayerId.Player0, action, 7) { Source = 41, Slot = 1 };
            Assert.IsTrue(ProductSurfaceIntentPolicy.MatchesSlot(command, V05.PlayerId.Player0, kind, 1));
            Assert.IsFalse(ProductSurfaceIntentPolicy.MatchesSlot(command, V05.PlayerId.Player1, kind, 1));
            Assert.IsFalse(ProductSurfaceIntentPolicy.MatchesSlot(command, V05.PlayerId.Player0, kind, 0));
            Assert.IsFalse(ProductSurfaceIntentPolicy.MatchesSlot(command, V05.PlayerId.Player0, kind, -1));
            foreach (ProductSlotSurfaceKind other in Enum.GetValues<ProductSlotSurfaceKind>().Where(value => value != kind))
                Assert.IsFalse(ProductSurfaceIntentPolicy.MatchesSlot(command, V05.PlayerId.Player0, other, 1));
        }
    }
}
