// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scgs.Hotseat;
using V05 = Scgs.Client.V05;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class V05HotseatChoiceTests
{
    [TestMethod]
    public void ProductChoiceKindsMapToDedicatedHotseatStates()
    {
        (V05.PendingChoiceKind Kind, HotseatSelectionStep Step, bool Ordered)[] cases =
        [
            (V05.PendingChoiceKind.Mode, HotseatSelectionStep.ChooseMode, false),
            (V05.PendingChoiceKind.Cards, HotseatSelectionStep.ChooseCards, false),
            (V05.PendingChoiceKind.TriggerOrder, HotseatSelectionStep.OrderTriggers, true),
            (V05.PendingChoiceKind.AdditionalCost, HotseatSelectionStep.ChooseAdditionalCost, false),
        ];

        foreach ((V05.PendingChoiceKind kind, HotseatSelectionStep expected, bool ordered) in cases)
        {
            ProductHotseatChoiceState state = ProductHotseatChoiceState.From(
                Choice(kind, ordered),
                V05.PlayerId.Player0);
            Assert.AreEqual(expected, state.Step);
            Assert.IsTrue(state.RequiresInput);
            Assert.IsFalse(state.WaitingForOpponent);
            Assert.AreEqual("choice-7", state.ChoiceId);
            Assert.HasCount(2, state.Options);
        }
    }

    [TestMethod]
    public void OpponentChoiceExposesOnlyWaitingState()
    {
        var redacted = new V05.PendingChoiceView
        {
            Pending = true,
            Chooser = V05.PlayerId.Player0,
            Revision = 8,
        };
        ProductHotseatChoiceState state = ProductHotseatChoiceState.From(
            redacted,
            V05.PlayerId.Player1);
        Assert.IsTrue(state.WaitingForOpponent);
        Assert.IsFalse(state.RequiresInput);
        Assert.AreEqual(HotseatSelectionStep.None, state.Step);
        Assert.IsNull(state.ChoiceId);
        Assert.IsEmpty(state.Options);
    }

    [TestMethod]
    public void ChoiceProjectionRejectsPrivateLeaksAndInvalidBounds()
    {
        V05.PendingChoiceView leaked = Choice(V05.PendingChoiceKind.Cards, false);
        Assert.ThrowsExactly<ArgumentException>(() =>
            ProductHotseatChoiceState.From(leaked, V05.PlayerId.Player1));

        V05.PendingChoiceView invalid = Choice(
            V05.PendingChoiceKind.Cards,
            false,
            minimumSelections: 3);
        Assert.ThrowsExactly<ArgumentException>(() =>
            ProductHotseatChoiceState.From(invalid, V05.PlayerId.Player0));
    }

    private static V05.PendingChoiceView Choice(
        V05.PendingChoiceKind kind,
        bool ordered,
        ulong minimumSelections = 1) => new()
    {
        Pending = true,
        Chooser = V05.PlayerId.Player0,
        ChoiceId = "choice-7",
        Kind = kind,
        MinimumSelections = minimumSelections,
        MaximumSelections = 2,
        Ordered = ordered,
        Options =
        [
            new V05.PendingChoiceOptionView { OptionId = "option-a", Label = "A" },
            new V05.PendingChoiceOptionView { OptionId = "option-b", Label = "B" },
        ],
        Revision = 7,
    };
}
