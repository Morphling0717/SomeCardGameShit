// SPDX-License-Identifier: GPL-3.0-or-later
using Scgs.GodotClient.Preview;
using Scgs.GodotClient.Ci;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class AnimeCardBodySliceLaunchTests
{
    [TestMethod]
    public void StandaloneApprovalModeDefaultsToRepresentativesWithoutNative()
    {
        AnimeCardBodySliceLaunch launch = AnimeCardBodySliceLaunch.Parse(
            [AnimeCardBodySliceLaunch.Option]);

        Assert.IsTrue(launch.Requested);
        Assert.IsNull(launch.OutputDirectory);
        Assert.IsFalse(launch.ExitWhenComplete);
        Assert.AreEqual(AnimeCardBodySliceLaunch.StateRepresentatives, launch.InitialState);
    }

    [TestMethod]
    public void CaptureRequiresAbsoluteDirectoryAndKnownState()
    {
        string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "scgs-card-body"));
        AnimeCardBodySliceLaunch launch = AnimeCardBodySliceLaunch.Parse(
        [
            $"{AnimeCardBodySliceLaunch.OutputPrefix}{directory}",
            AnimeCardBodySliceLaunch.ExitOption,
            $"{AnimeCardBodySliceLaunch.StatePrefix}{AnimeCardBodySliceLaunch.StateContact}",
        ]);

        Assert.AreEqual(directory, launch.OutputDirectory);
        Assert.IsTrue(launch.ExitWhenComplete);
        Assert.AreEqual(AnimeCardBodySliceLaunch.StateContact, launch.InitialState);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            AnimeCardBodySliceLaunch.Parse(
            [
                AnimeCardBodySliceLaunch.Option,
                $"{AnimeCardBodySliceLaunch.StatePrefix}unknown",
            ]));
    }

    [TestMethod]
    public void ApprovalModeRejectsProductAndOlderVisualModes()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            AnimeCardBodySliceLaunch.Parse(
            [
                AnimeCardBodySliceLaunch.Option,
                "--anime-style-slice",
            ]));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            AnimeCardBodySliceLaunch.Parse(
            [
                AnimeCardBodySliceLaunch.Option,
                "--ci-smoke",
            ]));
    }

    [TestMethod]
    public void CaptureAcceptsTheSharedExactViewportPolicy()
    {
        string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "scgs-card-body-viewport"));
        string[] arguments =
        [
            $"{AnimeCardBodySliceLaunch.OutputPrefix}{directory}",
            AnimeCardBodySliceLaunch.ExitOption,
            "--ci-visual-viewport=2560x1600",
        ];

        AnimeCardBodySliceLaunch launch = AnimeCardBodySliceLaunch.Parse(arguments);
        AnimeSliceViewportSize? viewport = AnimeVisualSliceViewportPolicy.Resolve(arguments);

        Assert.IsTrue(launch.Requested);
        Assert.IsNotNull(viewport);
        Assert.AreEqual(2560, viewport.Value.Width);
        Assert.AreEqual(1600, viewport.Value.Height);
    }

    [TestMethod]
    public void FrameFingerprintRequiresExactConsecutivePixelIdentity()
    {
        byte[] pixels = [0, 1, 2, 3, 4, 5, 6, 7];
        AnimeCardBodyFrameSample first = AnimeCardBodyFrameSample.Create(
            2, 1, "Rgba8", pixels);
        AnimeCardBodyFrameSample same = AnimeCardBodyFrameSample.Create(
            2, 1, "Rgba8", pixels.ToArray());

        Assert.IsTrue(first.HasIdenticalPixels(same));
        Assert.AreEqual(first.Fingerprint.PixelSha256, same.Fingerprint.PixelSha256);

        byte[] changedPixels = pixels.ToArray();
        changedPixels[^1] ^= 1;
        AnimeCardBodyFrameSample changed = AnimeCardBodyFrameSample.Create(
            2, 1, "Rgba8", changedPixels);
        Assert.IsFalse(first.HasIdenticalPixels(changed));
        StringAssert.Contains(
            first.Fingerprint.DescribeDifference(changed.Fingerprint),
            "pixel SHA-256 changed");

        var forgedCollision = new AnimeCardBodyFrameSample(first.Fingerprint, changedPixels);
        Assert.IsFalse(first.HasIdenticalPixels(forgedCollision));
    }

    [TestMethod]
    public void FrameFingerprintRejectsShapeFormatAndEmptyPixels()
    {
        byte[] pixels = [0, 1, 2, 3];
        AnimeCardBodyFrameFingerprint baseline = AnimeCardBodyFrameFingerprint.Create(
            1, 1, "Rgba8", pixels);
        AnimeCardBodyFrameFingerprint resized = AnimeCardBodyFrameFingerprint.Create(
            2, 1, "Rgba8", pixels);
        AnimeCardBodyFrameFingerprint reformatted = AnimeCardBodyFrameFingerprint.Create(
            1, 1, "Rgb8", pixels);

        Assert.IsFalse(baseline.HasIdenticalPixels(resized));
        Assert.IsFalse(baseline.HasIdenticalPixels(reformatted));
        StringAssert.Contains(baseline.DescribeDifference(resized), "viewport changed");
        StringAssert.Contains(baseline.DescribeDifference(reformatted), "pixel format changed");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            AnimeCardBodyFrameFingerprint.Create(0, 1, "Rgba8", pixels));
        Assert.ThrowsExactly<ArgumentException>(() =>
            AnimeCardBodyFrameFingerprint.Create(1, 1, "Rgba8", []));
    }

    [TestMethod]
    public void GpuReadabilityRequiresBoundedFinalFrameGlyphDifference()
    {
        Assert.IsTrue(AnimeCardBodyGpuReadabilityPolicy.RequiresEvidence("hand-ten"));
        Assert.IsTrue(AnimeCardBodyGpuReadabilityPolicy.RequiresEvidence("values"));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.RequiresEvidence("contact-sheet"));
        Assert.AreEqual(14, AnimeCardBodyGpuReadabilityPolicy.MinimumBadgePixelHeight(720));
        Assert.AreEqual(17, AnimeCardBodyGpuReadabilityPolicy.MinimumBadgePixelHeight(900));
        Assert.AreEqual(7, AnimeCardBodyGpuReadabilityPolicy.MinimumGlyphPixelHeight(17));
        Assert.AreEqual(3, AnimeCardBodyGpuReadabilityPolicy.MinimumGlyphPixelWidth("0"));
        Assert.AreEqual(6, AnimeCardBodyGpuReadabilityPolicy.MinimumGlyphPixelWidth("10"));
        Assert.AreEqual(8, AnimeCardBodyGpuReadabilityPolicy.MinimumNameGlyphPixelHeight(900));
        Assert.AreEqual(12, AnimeCardBodyGpuReadabilityPolicy.MinimumNameGlyphPixelWidth("测试卡牌"));
        Assert.AreEqual(27, AnimeCardBodyGpuReadabilityPolicy.MinimumNameGlyphPixelWidth("曜誓大团长·蕾奥妮"));
        Assert.AreEqual(2.0f, AnimeCardBodyGpuReadabilityPolicy.MaximumNameCenterDeltaPixels(1280, 720));
        Assert.AreEqual(2.5f, AnimeCardBodyGpuReadabilityPolicy.MaximumNameCenterDeltaPixels(1600, 900));
        Assert.AreEqual(4.0f, AnimeCardBodyGpuReadabilityPolicy.MinimumNamePlateHorizontalInsetPixels(1280, 720));
        Assert.AreEqual(5.0f, AnimeCardBodyGpuReadabilityPolicy.MinimumNamePlateHorizontalInsetPixels(1600, 900));

        AnimeCardBodyBadgeGpuEvidence readable = ReadableBadge();
        Assert.IsTrue(AnimeCardBodyGpuReadabilityPolicy.IsBadgeReadable(readable, 17));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsBadgeReadable(
            readable with { GlyphDifferencePixelCount = 0 }, 17));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsBadgeReadable(
            readable with { BrightGlyphDifferencePixelCount = 0 }, 17));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsBadgeReadable(
            readable with { GlyphPixelWidth = 1 }, 17));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsBadgeReadable(
            readable with { GlyphPixelWidth = 5 }, 17));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsBadgeReadable(
            readable with { GlyphPixelHeight = 6 }, 17));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsBadgeReadable(
            readable with { HighContrastGlyphDifferencePixelCount = 2 }, 17));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsBadgeReadable(
            readable with { MaximumGlyphContrast = 0.17f }, 17));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsBadgeReadable(
            readable with { FullyInsideViewport = false }, 17));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsBadgeReadable(
            readable with { PixelHeight = 16 }, 17));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsBadgeReadable(
            readable with { GlyphSocketInsetRight = 0.5f }, 17));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsBadgeReadable(
            readable with { GlyphInsideSocket = false }, 17));

        AnimeCardBodyNameGpuEvidence readableName = ReadableName();
        Assert.IsTrue(AnimeCardBodyGpuReadabilityPolicy.IsNameReadable(readableName, 1600, 900));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsNameReadable(
            readableName with { GlyphSocketInsetLeft = 0.5f }, 1600, 900));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsNameReadable(
            readableName with { TextSocketNamePlateInsetBottom = 0.5f }, 1600, 900));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsNameReadable(
            readableName with { TextSocketNamePlateInsetLeft = 4.99f }, 1600, 900));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsNameReadable(
            readableName with { GlyphDifferencePixelCount = 10 }, 1600, 900));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsNameReadable(
            readableName with { FontSize = 13 }, 1600, 900));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsNameReadable(
            readableName with
            {
                Text = "曜誓大团长…",
                SourceText = "曜誓大团长·蕾奥妮",
                FullNameMatchesSource = false,
            },
            1600,
            900));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsNameReadable(
            readableName with { GlyphSocketCenterDeltaX = 2.51f }, 1600, 900));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsNameReadable(
            readableName with { MaximumGlyphSocketCenterDeltaPixels = 2.6f }, 1600, 900));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsNameReadable(
            readableName with { GlyphCenteredInTextSocket = false }, 1600, 900));

        var actor = new AnimeCardBodyActorGpuEvidence
        {
            ActorName = "actor",
            DesignId = "LO-11",
            ProductKind = "Follower",
            LocalCompositionReadable = true,
            RequiredBadgeCount = 1,
            AllRequiredBadgesReadable = true,
            NameReadable = true,
            Badges = [readable],
            Name = readableName,
        };
        var capture = new AnimeCardBodyGpuReadabilityEvidence
        {
            State = "values",
            Required = true,
            MinimumBadgePixelHeight = 17,
            ViewportWidth = 1600,
            ViewportHeight = 900,
            ActorCount = 1,
            RequiredBadgeCount = 1,
            RequiredNameCount = 1,
            CompleteNameCount = 1,
            AllRequiredBadgesReadable = true,
            AllRequiredNamesReadable = true,
            Actors = [actor],
        };
        Assert.IsTrue(AnimeCardBodyGpuReadabilityPolicy.IsCaptureReadable(capture));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsCaptureReadable(
            capture with { RequiredBadgeCount = 2 }));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsCaptureReadable(
            capture with
            {
                Actors =
                [
                    actor with
                    {
                        Badges = [readable with { ReferenceActorName = "some-other-actor" }],
                    },
                ],
            }));
        Assert.IsTrue(AnimeCardBodyGpuReadabilityPolicy.IsCaptureReadable(
            capture with { State = "hand-five" }));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsCaptureReadable(
            capture with { CompleteNameCount = 0 }));
        Assert.IsFalse(AnimeCardBodyGpuReadabilityPolicy.IsCaptureReadable(
            capture with
            {
                Actors =
                [
                    actor with
                    {
                        Name = readableName with { ReferenceActorName = "some-other-actor" },
                    },
                ],
            }));
    }

    [TestMethod]
    public void SilhouetteEvidenceRequiresHiddenBaseAndFourPassingCorners()
    {
        AnimeCardBodySilhouetteProbeEvidence[] probes =
        [
            Probe("upper-left-edge"), Probe("upper-right-edge"),
            Probe("lower-left-edge"), Probe("lower-right-edge"),
        ];
        var evidence = new AnimeCardBodySilhouetteEvidence
        {
            State = "representatives",
            Required = true,
            ActorCount = 1,
            ProbeCount = 4,
            InteriorProbeCount = 1,
            AllRectangularBasesHidden = true,
            AllCornerProbesMatchBackground = true,
            AllInteriorProbesShowProductFace = true,
            Probes = probes,
            InteriorProbes =
            [
                new AnimeCardBodyInteriorProbeEvidence
                {
                    ActorName = "actor",
                    ScreenX = 20.0f,
                    ScreenY = 20.0f,
                    FullyInsideViewport = true,
                    RoiX = 16,
                    RoiY = 16,
                    RoiWidth = 9,
                    RoiHeight = 9,
                    ProductLayerDifferencePixelCount = 20,
                    Passed = true,
                },
            ],
        };

        Assert.IsTrue(AnimeCardBodySilhouettePolicy.IsCaptureIsolated(evidence));
        Assert.IsFalse(AnimeCardBodySilhouettePolicy.IsCaptureIsolated(
            evidence with { AllRectangularBasesHidden = false }));
        Assert.IsFalse(AnimeCardBodySilhouettePolicy.IsCaptureIsolated(
            evidence with
            {
                Probes = probes.Select((probe, index) => index == 0
                    ? probe with { CornerBackgroundColorDelta = 0.11f }
                    : probe).ToArray(),
            }));
    }

    private static AnimeCardBodyBadgeGpuEvidence ReadableBadge() => new()
    {
        Role = "cost",
        Text = "10",
        ReferenceActorName = "actor",
        Expected = true,
        ScreenX = 10.0f,
        ScreenY = 20.0f,
        ScreenWidth = 24.0f,
        ScreenHeight = 20.0f,
        PixelHeight = 20,
        FullyInsideViewport = true,
        RoiX = 10,
        RoiY = 20,
        RoiWidth = 24,
        RoiHeight = 20,
        BrightPixelCount = 8,
        ColorBucketCount = 4,
        GlyphDifferencePixelCount = 24,
        BrightGlyphDifferencePixelCount = 10,
        SocketScreenX = 8.0f,
        SocketScreenY = 18.0f,
        SocketScreenWidth = 30.0f,
        SocketScreenHeight = 26.0f,
        SocketFullyInsideViewport = true,
        RequiredSocketInsetPixels = 1,
        GlyphSocketInsetLeft = 7.0f,
        GlyphSocketInsetTop = 7.0f,
        GlyphSocketInsetRight = 15.0f,
        GlyphSocketInsetBottom = 9.0f,
        GlyphInsideSocket = true,
        GlyphRoiX = 15,
        GlyphRoiY = 25,
        GlyphPixelWidth = 8,
        GlyphPixelHeight = 10,
        HighContrastGlyphDifferencePixelCount = 8,
        MaximumGlyphContrast = 0.72f,
        Readable = true,
    };

    private static AnimeCardBodyNameGpuEvidence ReadableName() => new()
    {
        Text = "曜誓大团长",
        SourceText = "曜誓大团长",
        FullNameMatchesSource = true,
        ReferenceActorName = "actor",
        Expected = true,
        FontSize = 22,
        ScreenX = 30.0f,
        ScreenY = 40.0f,
        ScreenWidth = 90.0f,
        ScreenHeight = 20.0f,
        ScreenFullyInsideViewport = true,
        TextSocketScreenX = 29.0f,
        TextSocketScreenY = 38.0f,
        TextSocketScreenWidth = 92.0f,
        TextSocketScreenHeight = 24.0f,
        TextSocketFullyInsideViewport = true,
        NamePlateScreenX = 24.0f,
        NamePlateScreenY = 34.0f,
        NamePlateScreenWidth = 102.0f,
        NamePlateScreenHeight = 32.0f,
        NamePlateFullyInsideViewport = true,
        RequiredSocketInsetPixels = 1,
        RequiredNamePlateHorizontalInsetPixels = 5.0f,
        TextSocketNamePlateInsetLeft = 5.0f,
        TextSocketNamePlateInsetTop = 4.0f,
        TextSocketNamePlateInsetRight = 5.0f,
        TextSocketNamePlateInsetBottom = 4.0f,
        TextSocketInsideNamePlate = true,
        RoiX = 30,
        RoiY = 40,
        RoiWidth = 90,
        RoiHeight = 20,
        GlyphDifferencePixelCount = 80,
        BrightGlyphDifferencePixelCount = 30,
        GlyphRoiX = 40,
        GlyphRoiY = 44,
        GlyphPixelWidth = 70,
        GlyphPixelHeight = 12,
        HighContrastGlyphDifferencePixelCount = 36,
        MaximumGlyphContrast = 0.70f,
        GlyphSocketInsetLeft = 11.0f,
        GlyphSocketInsetTop = 6.0f,
        GlyphSocketInsetRight = 11.0f,
        GlyphSocketInsetBottom = 6.0f,
        GlyphInsideTextSocket = true,
        MaximumGlyphSocketCenterDeltaPixels = 2.5f,
        GlyphSocketCenterDeltaX = 0.0f,
        GlyphSocketCenterDeltaY = 0.0f,
        GlyphCenteredInTextSocket = true,
        Readable = true,
    };

    private static AnimeCardBodySilhouetteProbeEvidence Probe(string corner) => new()
    {
        ActorName = "actor",
        Corner = corner,
        ScreenX = 10.0f,
        ScreenY = 10.0f,
        ReferenceX = 5.0f,
        ReferenceY = 5.0f,
        FullyInsideViewport = true,
        CornerBackgroundColorDelta = 0.005f,
        Passed = true,
    };
}
