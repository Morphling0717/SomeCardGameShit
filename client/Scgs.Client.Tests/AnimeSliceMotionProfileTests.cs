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

    [TestMethod]
    [DataRow("1280x720", 1280, 720)]
    [DataRow("1600x900", 1600, 900)]
    [DataRow("2560x1440", 2560, 1440)]
    [DataRow("2560x1600", 2560, 1600)]
    public void ProductScreenshotViewportsNeedNoException(string value, int width, int height)
    {
        AnimeSliceViewportSize? viewport = AnimeVisualSliceViewportPolicy.Resolve(
            [$"--ci-visual-viewport={value}"]);

        Assert.AreEqual(new AnimeSliceViewportSize(width, height), viewport);
    }

    [TestMethod]
    public void HostedMacViewportRequiresTheDedicatedException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AnimeVisualSliceViewportPolicy.Resolve(["--ci-visual-viewport=1024x684"]));

        AnimeSliceViewportSize? viewport = AnimeVisualSliceViewportPolicy.Resolve(
            ["--ci-visual-viewport=1024x684", "--ci-anime-runner-viewport"]);

        Assert.AreEqual(new AnimeSliceViewportSize(1024, 684), viewport);
    }

    [TestMethod]
    public void HostedMacExceptionCannotWidenAnyOtherViewport()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AnimeVisualSliceViewportPolicy.Resolve(
                ["--ci-visual-viewport=1280x720", "--ci-anime-runner-viewport"]));
        Assert.Throws<InvalidOperationException>(() =>
            AnimeVisualSliceViewportPolicy.Resolve(["--ci-anime-runner-viewport"]));
    }

    [TestMethod]
    public void WindowRequestCompensatesForObservedMacFramebufferInset()
    {
        AnimeSliceViewportSize corrected =
            AnimeVisualSliceViewportPolicy.CorrectWindowSizeForFramebuffer(
                new AnimeSliceViewportSize(1024, 684),
                new AnimeSliceViewportSize(1024, 681),
                AnimeVisualSliceViewportPolicy.CiRunnerViewport);

        Assert.AreEqual(new AnimeSliceViewportSize(1024, 687), corrected);
        Assert.AreEqual(
            corrected,
            AnimeVisualSliceViewportPolicy.CorrectWindowSizeForFramebuffer(
                corrected,
                AnimeVisualSliceViewportPolicy.CiRunnerViewport,
                AnimeVisualSliceViewportPolicy.CiRunnerViewport));
    }

    [TestMethod]
    public void WindowRequestCompensationRejectsInvalidDimensions()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            AnimeVisualSliceViewportPolicy.CorrectWindowSizeForFramebuffer(
                new AnimeSliceViewportSize(0, 684),
                new AnimeSliceViewportSize(1024, 681),
                AnimeVisualSliceViewportPolicy.CiRunnerViewport));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            AnimeVisualSliceViewportPolicy.CorrectWindowSizeForFramebuffer(
                new AnimeSliceViewportSize(1024, 684),
                new AnimeSliceViewportSize(1024, 0),
                AnimeVisualSliceViewportPolicy.CiRunnerViewport));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            AnimeVisualSliceViewportPolicy.CorrectWindowSizeForFramebuffer(
                new AnimeSliceViewportSize(1024, 684),
                new AnimeSliceViewportSize(1024, 681),
                new AnimeSliceViewportSize(1024, 0)));
    }
}
