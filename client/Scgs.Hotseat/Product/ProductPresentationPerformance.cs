// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.Hotseat.Product;

/// <summary>Identity-free counts observed from actual scene/resource objects.</summary>
public sealed record ProductPresentationResourceCounts(
    int CardActors,
    int MotionPoolCards,
    int MotionIdentityBindings,
    int MotionVisible,
    int MotionCollisions,
    int Materials,
    int Textures,
    long GodotResources,
    bool CutinTextureBound,
    bool CutinVisible)
{
    public bool MotionClean => MotionIdentityBindings == 0 && MotionVisible == 0 &&
        MotionCollisions == 0 && !CutinTextureBound && !CutinVisible;

    internal static ProductPresentationResourceCounts Peak(
        ProductPresentationResourceCounts first,
        ProductPresentationResourceCounts next) => new(
        Math.Max(first.CardActors, next.CardActors),
        Math.Max(first.MotionPoolCards, next.MotionPoolCards),
        Math.Max(first.MotionIdentityBindings, next.MotionIdentityBindings),
        Math.Max(first.MotionVisible, next.MotionVisible),
        Math.Max(first.MotionCollisions, next.MotionCollisions),
        Math.Max(first.Materials, next.Materials),
        Math.Max(first.Textures, next.Textures),
        Math.Max(first.GodotResources, next.GodotResources),
        first.CutinTextureBound || next.CutinTextureBound,
        first.CutinVisible || next.CutinVisible);
}

public sealed record ProductPresentationPerformanceReport(
    ulong RecordingId,
    string Workload,
    bool FirstUseWorkload,
    int StaticWarmupFrames,
    string Completion,
    int AnimatedFrames,
    int MeasuredIntervals,
    int ExcludedNonAnimationFrames,
    int DroppedAnimatedIntervals,
    bool SampleCapacityReached,
    double? SetupToFirstAnimatedDrawMilliseconds,
    double? P95Milliseconds,
    double? MaximumMilliseconds,
    bool? NumericFrameBudgetMet,
    ProductPresentationResourceCounts Before,
    ProductPresentationResourceCounts Peak,
    ProductPresentationResourceCounts? AfterSynchronousCleanup,
    ProductPresentationResourceCounts? AfterTwoDrawnFrames,
    string DeferredCleanupStatus);

/// <summary>
/// Bounded aggregation of genuine render timestamps. It never pads a short
/// animation to a fixed frame count and never merges idle time into samples.
/// </summary>
public sealed class ProductPresentationPerformanceWindow
{
    public const int MaximumIntervals = 4096;
    private readonly double[] intervals = new double[MaximumIntervals];
    private readonly ProductPresentationResourceCounts before;
    private ProductPresentationResourceCounts peak;
    private readonly ulong recordingId;
    private readonly string workload;
    private readonly bool firstUseWorkload;
    private readonly double beganMilliseconds;
    private ulong? lastObservedToken;
    private ulong? lastAnimatedToken;
    private double lastAnimatedMilliseconds;
    private ulong? cleanupToken;
    private int drawnCleanupFrames;
    private int animatedFrames;
    private int measuredIntervals;
    private int excludedFrames;
    private int droppedIntervals;
    private double? firstDrawMilliseconds;
    private string completion = "playing";
    private string cleanupStatus = "not_completed";
    private ProductPresentationResourceCounts? afterCleanup;
    private ProductPresentationResourceCounts? afterTwoFrames;

    public ProductPresentationPerformanceWindow(
        ulong recordingId,
        string workload,
        bool firstUseWorkload,
        double beganMilliseconds,
        ProductPresentationResourceCounts before)
    {
        if (!double.IsFinite(beganMilliseconds) || beganMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(beganMilliseconds));
        ArgumentException.ThrowIfNullOrWhiteSpace(workload);
        this.recordingId = recordingId;
        this.workload = workload;
        this.firstUseWorkload = firstUseWorkload;
        this.beganMilliseconds = beganMilliseconds;
        this.before = before ?? throw new ArgumentNullException(nameof(before));
        peak = before;
    }

    public bool IsComplete => completion != "playing";
    public bool AwaitingDeferredCleanup => cleanupStatus == "awaiting_two_drawn_frames";

    public void ObserveFrame(
        ulong frameToken,
        double milliseconds,
        bool animationActive,
        ProductPresentationResourceCounts resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (!double.IsFinite(milliseconds) || milliseconds < beganMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(milliseconds));
        if (lastObservedToken.HasValue && frameToken <= lastObservedToken.Value) return;
        lastObservedToken = frameToken;

        if (IsComplete)
        {
            if (!AwaitingDeferredCleanup || cleanupToken.HasValue && frameToken <= cleanupToken.Value) return;
            if (++drawnCleanupFrames == 2)
            {
                afterTwoFrames = resources;
                cleanupStatus = "observed";
            }
            return;
        }

        if (!animationActive)
        {
            ++excludedFrames;
            lastAnimatedToken = null;
            return;
        }

        ++animatedFrames;
        firstDrawMilliseconds ??= milliseconds - beganMilliseconds;
        peak = ProductPresentationResourceCounts.Peak(peak, resources);
        if (lastAnimatedToken.HasValue && frameToken == lastAnimatedToken.Value + 1)
        {
            double duration = milliseconds - lastAnimatedMilliseconds;
            if (duration < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
            if (measuredIntervals < MaximumIntervals)
                intervals[measuredIntervals++] = duration;
            else
                ++droppedIntervals;
        }
        lastAnimatedToken = frameToken;
        lastAnimatedMilliseconds = milliseconds;
    }

    public void Complete(string reason, ulong lastDrawnFrame, ProductPresentationResourceCounts resources)
    {
        if (reason is not ("completed" or "cancelled" or "skipped" or "overflow_fast_forwarded" or "faulted"))
            throw new ArgumentOutOfRangeException(nameof(reason));
        ArgumentNullException.ThrowIfNull(resources);
        if (IsComplete) return;
        completion = reason;
        afterCleanup = resources;
        cleanupToken = lastDrawnFrame;
        cleanupStatus = "awaiting_two_drawn_frames";
    }

    public void AbandonDeferredCleanup(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (AwaitingDeferredCleanup) cleanupStatus = reason;
    }

    public ProductPresentationPerformanceReport Report()
    {
        double? p95 = null;
        double? maximum = null;
        bool? numericBudget = null;
        if (measuredIntervals > 0)
        {
            double[] sorted = intervals.AsSpan(0, measuredIntervals).ToArray();
            Array.Sort(sorted);
            p95 = sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * 0.95) - 1)];
            maximum = sorted[^1];
            numericBudget = droppedIntervals == 0 && p95 <= 33.3 && maximum < 100;
        }

        return new ProductPresentationPerformanceReport(
            recordingId, workload, firstUseWorkload,
            StaticWarmupFrames: 0,
            completion, animatedFrames, measuredIntervals, excludedFrames, droppedIntervals,
            SampleCapacityReached: droppedIntervals != 0,
            firstDrawMilliseconds, p95, maximum, numericBudget,
            before, peak, afterCleanup, afterTwoFrames, cleanupStatus);
    }
}
