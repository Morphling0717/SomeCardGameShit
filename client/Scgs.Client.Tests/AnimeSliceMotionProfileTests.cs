// SPDX-License-Identifier: GPL-3.0-or-later
using Scgs.GodotClient.Preview;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class AnimeSliceMotionProfileTests
{
    [TestMethod]
    public void CaptureOutputAlwaysSelectsACompletelyStaticProfile()
    {
        AnimeSliceMotionProfile profile = AnimeSliceMotionPolicy.Select("C:/captures/anime-v1");

        Assert.IsFalse(profile.Enabled);
        Assert.AreEqual(AnimeSliceMotionProfile.Disabled, profile);
        Assert.AreEqual(0.0f, profile.BreathScaleAmplitude);
        Assert.AreEqual(0.0f, profile.ParallaxPixels);
        Assert.AreEqual(0.0f, profile.EntryDurationSeconds);
        Assert.AreEqual(0.0f, profile.HitDurationSeconds);
    }

    [TestMethod]
    public void InteractivePreviewUsesOnlyBoundedLightweightMotion()
    {
        AnimeSliceMotionProfile profile = AnimeSliceMotionPolicy.Select(outputDirectory: null);

        Assert.AreEqual(AnimeSliceMotionProfile.Interactive, profile);
        Assert.IsTrue(profile.UsesBoundedLightweightMotion);
        Assert.IsLessThanOrEqualTo(0.02f, profile.BreathScaleAmplitude);
        Assert.IsLessThanOrEqualTo(14.0f, profile.ParallaxPixels);
        Assert.IsLessThanOrEqualTo(0.60f, profile.EntryDurationSeconds);
        Assert.IsLessThanOrEqualTo(0.35f, profile.HitDurationSeconds);
    }
}
