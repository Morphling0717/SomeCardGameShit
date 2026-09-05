// SPDX-License-Identifier: GPL-3.0-or-later
using Scgs.GodotClient.Ci;
using V05 = Scgs.Client.V05;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class ProductSmokeCompletionPolicyTests
{
    [TestMethod]
    public void BoardAccumulationRequiresCompleteUiEvidenceAndAnOutstandingPerformanceRequest()
    {
        int[] covered = Enumerable.Repeat(1, 14).ToArray();
        Assert.IsTrue(ProductSmokeCompletionPolicy.ShouldAccumulateBoard(true, true, false, covered, true, true));
        foreach ((bool fullUi, bool requested, bool completed, bool reaction, bool choice) in new[]
        {
            (false, true, false, true, true), (true, false, false, true, true),
            (true, true, true, true, true), (true, true, false, false, true),
            (true, true, false, true, false),
        })
            Assert.IsFalse(ProductSmokeCompletionPolicy.ShouldAccumulateBoard(
                fullUi, requested, completed, covered, reaction, choice));
        for (int missing = 0; missing < covered.Length; ++missing)
        {
            int[] incomplete = (int[])covered.Clone();
            incomplete[missing] = 0;
            Assert.IsFalse(ProductSmokeCompletionPolicy.ShouldAccumulateBoard(true, true, false, incomplete, true, true));
        }
    }

    [TestMethod]
    public void BoardAccumulationOnlyPrioritizesPermanentGrowthTurnEndAndReactionPass()
    {
        V05.ActionKind[] allowed = [V05.ActionKind.PlayUnit, V05.ActionKind.PlayAmulet,
            V05.ActionKind.Deploy, V05.ActionKind.EndTurn, V05.ActionKind.PassReaction];
        foreach (V05.ActionKind action in Enum.GetValues<V05.ActionKind>())
        {
            int? priority = ProductSmokeCompletionPolicy.AccumulationPriority(Command(action));
            Assert.AreEqual(allowed.Contains(action), priority.HasValue);
        }
        Assert.AreEqual(0, ProductSmokeCompletionPolicy.AccumulationPriority(Command(V05.ActionKind.PlayUnit)));
        Assert.AreEqual(1, ProductSmokeCompletionPolicy.AccumulationPriority(Command(V05.ActionKind.PlayAmulet)));
        Assert.AreEqual(2, ProductSmokeCompletionPolicy.AccumulationPriority(Command(V05.ActionKind.Deploy)));
        Assert.AreEqual(3, ProductSmokeCompletionPolicy.AccumulationPriority(Command(V05.ActionKind.EndTurn)));
    }

    [TestMethod]
    public void BoardAccumulationRejectsArchiveCostsAndEnemyTargetsWithoutCardIdentityRules()
    {
        Assert.IsNull(ProductSmokeCompletionPolicy.AccumulationPriority(Command(V05.ActionKind.Deploy) with
        {
            AdditionalCostCards = [77],
        }));
        foreach (V05.ActionKind action in new[] { V05.ActionKind.PlayUnit, V05.ActionKind.PlayAmulet, V05.ActionKind.Deploy })
        {
            V05.GameCommandRequest command = Command(action) with
            {
                Target = V05.Target.PermanentTarget(V05.PlayerId.Player1, 99),
            };
            Assert.IsNull(ProductSmokeCompletionPolicy.AccumulationPriority(command));
            Assert.IsNotNull(ProductSmokeCompletionPolicy.AccumulationPriority(command with
            {
                Target = V05.Target.PermanentTarget(V05.PlayerId.Player0, 77),
            }));
            Assert.IsNotNull(ProductSmokeCompletionPolicy.AccumulationPriority(command with { Target = null }));
        }
    }

    private static V05.GameCommandRequest Command(V05.ActionKind action) => new(V05.PlayerId.Player0, action, 1);

    [TestMethod]
    public void CoveredActionsAndSurrendersCannotReplaceRequiredHeavyBoardEvidence()
    {
        ProductSmokeCompletionDecision pending = ProductSmokeCompletionPolicy.Evaluate(
            true, true, true, true, true, false, 4);
        Assert.IsFalse(pending.CanComplete);
        Assert.IsTrue(pending.CanRestart);
        Assert.IsFalse(pending.SeekSurrender);

        ProductSmokeCompletionDecision measured = ProductSmokeCompletionPolicy.Evaluate(
            true, true, true, true, true, true, 5);
        Assert.IsTrue(measured.CanComplete);
        Assert.IsFalse(measured.CanRestart);
        Assert.IsFalse(measured.SeekSurrender);
    }

    [TestMethod]
    public void HeavyBoardDoesNotReplaceMissingActionOrEitherSurrenderPath()
    {
        foreach ((bool natural, bool reaction, bool choice) in new[]
        {
            (false, true, true), (true, false, true), (true, true, false), (true, false, false),
        })
        {
            ProductSmokeCompletionDecision decision = ProductSmokeCompletionPolicy.Evaluate(
                true, natural, reaction, choice, true, true, 4);
            Assert.IsFalse(decision.CanComplete);
            Assert.IsTrue(decision.CanRestart);
            Assert.AreEqual(natural && (!reaction || !choice), decision.SeekSurrender);
        }
    }

    [TestMethod]
    public void PendingPerformanceContinuesFixedSeedMatchesButNeverPastTwelve()
    {
        for (int completed = 4; completed <= ProductSmokeCompletionPolicy.MaximumMatches; ++completed)
        {
            ProductSmokeCompletionDecision decision = ProductSmokeCompletionPolicy.Evaluate(
                true, true, true, true, true, false, completed);
            Assert.IsFalse(decision.CanComplete);
            Assert.IsFalse(decision.SeekSurrender);
            Assert.AreEqual(completed < 12, decision.CanRestart);
        }
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ProductSmokeCompletionPolicy.Evaluate(
            true, true, true, true, true, true, 13));
    }

    [TestMethod]
    public void OrdinaryAndExportRunsDoNotAcquireANewPerformanceRequirement()
    {
        Assert.IsTrue(ProductSmokeCompletionPolicy.Evaluate(
            true, true, true, true, false, false, 4).CanComplete);
        Assert.IsTrue(ProductSmokeCompletionPolicy.Evaluate(
            false, false, false, false, false, false, 1).CanComplete);
        Assert.IsFalse(ProductSmokeCompletionPolicy.Evaluate(
            false, false, false, false, true, false, 1).CanComplete);
        Assert.IsTrue(ProductSmokeCompletionPolicy.Evaluate(
            false, false, false, false, true, true, 1).CanComplete);
    }
}
