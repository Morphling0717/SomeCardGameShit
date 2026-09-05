// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scgs.Hotseat.Product;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class ProductPresentationPerformanceTests
{
    [TestMethod]
    public void ShortAnimationKeepsItsActualFramesWithoutPaddingOrInventedWarmup()
    {
        var window = Window();
        for (ulong frame = 1; frame <= 5; ++frame)
            window.ObserveFrame(frame, 100 + frame * 16, true, Counts());
        window.Complete("completed", 5, Counts());
        ProductPresentationPerformanceReport report = window.Report();
        Assert.AreEqual(5, report.AnimatedFrames);
        Assert.AreEqual(4, report.MeasuredIntervals);
        Assert.AreEqual(0, report.StaticWarmupFrames);
        Assert.AreEqual(16.0, report.SetupToFirstAnimatedDrawMilliseconds);
        Assert.AreEqual(16.0, report.P95Milliseconds);
        Assert.AreEqual(16.0, report.MaximumMilliseconds);
        Assert.AreEqual(true, report.NumericFrameBudgetMet);
        Assert.IsTrue(report.FirstUseWorkload);
        Assert.AreEqual("completed", report.Completion);
    }

    [TestMethod]
    public void IdleAndNonConsecutiveFramesNeverBecomeAnimationTimingSamples()
    {
        var window = Window();
        window.ObserveFrame(1, 116, true, Counts());
        window.ObserveFrame(2, 132, true, Counts());
        window.ObserveFrame(3, 900, false, Counts());
        window.ObserveFrame(4, 10_000, false, Counts());
        window.ObserveFrame(5, 11_000, true, Counts());
        window.ObserveFrame(6, 11_020, true, Counts());
        window.ObserveFrame(8, 20_000, true, Counts());
        window.ObserveFrame(9, 20_018, true, Counts());
        ProductPresentationPerformanceReport report = window.Report();
        Assert.AreEqual(6, report.AnimatedFrames);
        Assert.AreEqual(3, report.MeasuredIntervals);
        Assert.AreEqual(2, report.ExcludedNonAnimationFrames);
        Assert.AreEqual(20.0, report.MaximumMilliseconds);
        Assert.AreEqual(20.0, report.P95Milliseconds);
    }

    [TestMethod]
    public void RealLongFrameFailsBudgetInsteadOfBeingDiscardedAsAnOutlier()
    {
        var window = Window();
        double time = 116;
        window.ObserveFrame(1, time, true, Counts());
        for (ulong frame = 2; frame <= 21; ++frame)
        {
            time += frame == 21 ? 100 : 16;
            window.ObserveFrame(frame, time, true, Counts());
        }
        ProductPresentationPerformanceReport report = window.Report();
        Assert.AreEqual(20, report.MeasuredIntervals);
        Assert.AreEqual(16.0, report.P95Milliseconds);
        Assert.AreEqual(100.0, report.MaximumMilliseconds);
        Assert.AreEqual(false, report.NumericFrameBudgetMet);
    }

    [TestMethod]
    public void ResourcePeakAndRealCleanupStaySeparateAndRequireTwoDistinctDraws()
    {
        var window = Window();
        ProductPresentationResourceCounts active = Counts() with
        {
            MotionPoolCards = 2, MotionIdentityBindings = 2, MotionVisible = 1,
            Materials = 60, Textures = 12, GodotResources = 250,
            CutinTextureBound = true, CutinVisible = true,
        };
        window.ObserveFrame(1, 116, true, active);
        ProductPresentationResourceCounts cleared = active with
        {
            MotionIdentityBindings = 0, MotionVisible = 0,
            CutinTextureBound = false, CutinVisible = false,
        };
        window.Complete("completed", 1, cleared);
        Assert.IsTrue(window.Report().AfterSynchronousCleanup!.MotionClean);
        Assert.IsFalse(window.Report().Peak.MotionClean);
        Assert.IsNull(window.Report().AfterTwoDrawnFrames);
        window.ObserveFrame(1, 117, false, active);
        window.ObserveFrame(2, 132, false, cleared);
        window.ObserveFrame(2, 133, false, active);
        Assert.IsNull(window.Report().AfterTwoDrawnFrames);
        window.ObserveFrame(3, 148, false, cleared with { GodotResources = 190 });
        ProductPresentationPerformanceReport report = window.Report();
        Assert.AreEqual("observed", report.DeferredCleanupStatus);
        Assert.IsTrue(report.AfterTwoDrawnFrames!.MotionClean);
        Assert.AreEqual(190L, report.AfterTwoDrawnFrames.GodotResources);
        Assert.AreEqual(250L, report.Peak.GodotResources);
        Assert.AreEqual(1, report.AnimatedFrames);
        Assert.AreEqual(0, report.MeasuredIntervals);
        Assert.IsNull(report.NumericFrameBudgetMet);
    }

    [TestMethod]
    public void CancellationSkipOverflowAndNoFramesKeepTheirExplicitNonPassStatus()
    {
        foreach (string reason in new[] { "cancelled", "skipped", "overflow_fast_forwarded", "faulted" })
        {
            var window = Window();
            window.Complete(reason, 9, Counts());
            ProductPresentationPerformanceReport report = window.Report();
            Assert.AreEqual(reason, report.Completion);
            Assert.AreEqual(0, report.AnimatedFrames);
            Assert.AreEqual(0, report.MeasuredIntervals);
            Assert.IsNull(report.P95Milliseconds);
            Assert.IsNull(report.NumericFrameBudgetMet);
            window.AbandonDeferredCleanup("owner_left_tree_before_two_drawn_frames");
            Assert.AreEqual("owner_left_tree_before_two_drawn_frames", window.Report().DeferredCleanupStatus);
            Assert.IsNull(window.Report().AfterTwoDrawnFrames);
        }
    }

    [TestMethod]
    public void CapacityOverflowIsBoundedAndCannotReportTimingAcceptance()
    {
        var window = Window();
        int expectedFrameCount = ProductPresentationPerformanceWindow.MaximumIntervals + 12;
        for (ulong frame = 1; frame <= (ulong)expectedFrameCount; ++frame)
            window.ObserveFrame(frame, 100 + frame * 16, true, Counts());
        ProductPresentationPerformanceReport report = window.Report();
        Assert.AreEqual(expectedFrameCount, report.AnimatedFrames);
        Assert.AreEqual(ProductPresentationPerformanceWindow.MaximumIntervals, report.MeasuredIntervals);
        Assert.AreEqual(11, report.DroppedAnimatedIntervals);
        Assert.IsTrue(report.SampleCapacityReached);
        Assert.AreEqual(false, report.NumericFrameBudgetMet);
    }

    [TestMethod]
    public void DuplicateDrawOrRepeatedCompletionCannotRewriteEvidence()
    {
        var window = Window();
        window.ObserveFrame(8, 116, true, Counts());
        window.ObserveFrame(8, 117, true, Counts() with { Materials = 9999 });
        window.ObserveFrame(7, 118, true, Counts() with { Materials = 9999 });
        window.ObserveFrame(9, 132, true, Counts());
        window.Complete("completed", 9, Counts());
        window.Complete("cancelled", 10, Counts() with { MotionIdentityBindings = 9 });
        ProductPresentationPerformanceReport first = window.Report();
        ProductPresentationPerformanceReport second = window.Report();
        Assert.AreEqual(first, second);
        Assert.AreEqual(2, first.AnimatedFrames);
        Assert.AreEqual(1, first.MeasuredIntervals);
        Assert.AreEqual(20, first.Peak.Materials);
        Assert.AreEqual("completed", first.Completion);
        Assert.IsTrue(first.AfterSynchronousCleanup!.MotionClean);
    }

    [TestMethod]
    public void InvalidTimestampsAreRejectedAndRepeatWorkloadIsOnlyALabel()
    {
        var window = new ProductPresentationPerformanceWindow(2, "known-workload", false, 100, Counts());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => window.ObserveFrame(1, double.NaN, true, Counts()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => window.ObserveFrame(1, 90, true, Counts()));
        window.ObserveFrame(1, 116, true, Counts());
        window.ObserveFrame(2, 316, true, Counts());
        ProductPresentationPerformanceReport report = window.Report();
        Assert.IsFalse(report.FirstUseWorkload);
        Assert.AreEqual(0, report.StaticWarmupFrames);
        Assert.AreEqual(false, report.NumericFrameBudgetMet);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => window.Complete("passed", 2, Counts()));
    }

    private static ProductPresentationPerformanceWindow Window() => new(1, "workload", true, 100, Counts());

    private static ProductPresentationResourceCounts Counts() => new(18, 0, 0, 0, 0, 20, 8, 120, false, false);
}
