// SPDX-License-Identifier: GPL-3.0-or-later
using Scgs.GodotClient.Ci;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class ProductSmokeCompletionPolicyTests
{
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
