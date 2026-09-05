// SPDX-License-Identifier: GPL-3.0-or-later
using V05 = Scgs.Client.V05;

namespace Scgs.Hotseat.Product;

public enum ProductSlotSurfaceKind { MainBoard, Tactic, Field }

/// <summary>Identity-preserving surface mapping, not a second rules validator.</summary>
public static class ProductSurfaceIntentPolicy
{
    public static bool MatchesSlot(V05.GameCommandRequest legal, V05.PlayerId owner,
        ProductSlotSurfaceKind kind, int index)
    {
        if (index < 0 || legal.Player != owner || legal.Slot != (ulong)index) return false;
        return legal.Action switch
        {
            V05.ActionKind.PlayUnit or V05.ActionKind.PlayAmulet or V05.ActionKind.Deploy =>
                kind == ProductSlotSurfaceKind.MainBoard,
            V05.ActionKind.CastSpell or V05.ActionKind.PlayTrap => kind == ProductSlotSurfaceKind.Tactic,
            V05.ActionKind.PlayField => kind == ProductSlotSurfaceKind.Field,
            _ => false,
        };
    }
}
