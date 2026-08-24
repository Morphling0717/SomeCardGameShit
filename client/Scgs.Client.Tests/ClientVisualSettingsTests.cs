// SPDX-License-Identifier: GPL-3.0-or-later
using Scgs.GodotClient.Visual;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class ClientVisualSettingsTests
{
    [TestMethod]
    public void DefaultsAreAValidatedSupportedDesktopConfiguration()
    {
        ClientVisualSettings defaults = ClientVisualSettings.Defaults;

        Assert.AreEqual(ClientWindowMode.Windowed, defaults.WindowMode);
        Assert.AreEqual(new ClientResolution(1600, 900), defaults.Resolution);
        Assert.AreEqual(100, defaults.UiScalePercent);
        Assert.IsTrue(defaults.VSync);
        Assert.IsFalse(defaults.ReduceMotion);
        Assert.AreEqual(defaults, defaults.Normalize());
    }

    [TestMethod]
    public void NormalizeFallsBackOnlyInvalidStructuralValues()
    {
        var invalid = new ClientVisualSettings(
            (ClientWindowMode)byte.MaxValue,
            new ClientResolution(640, 480),
            777,
            VSync: false,
            ReduceMotion: true);

        ClientVisualSettings normalized = invalid.Normalize();

        Assert.AreEqual(ClientVisualSettings.Defaults.WindowMode, normalized.WindowMode);
        Assert.AreEqual(ClientVisualSettings.Defaults.Resolution, normalized.Resolution);
        Assert.AreEqual(ClientVisualSettings.Defaults.UiScalePercent, normalized.UiScalePercent);
        Assert.IsFalse(normalized.VSync);
        Assert.IsTrue(normalized.ReduceMotion);
    }

    [TestMethod]
    public void EveryPublishedResolutionAndScaleSurvivesNormalization()
    {
        foreach (ClientResolution resolution in ClientVisualSettings.SupportedResolutions)
        {
            foreach (int uiScale in ClientVisualSettings.SupportedUiScales)
            {
                var settings = new ClientVisualSettings(
                    ClientWindowMode.BorderlessFullscreen,
                    resolution,
                    uiScale,
                    VSync: false,
                    ReduceMotion: true);

                Assert.AreEqual(settings, settings.Normalize());
            }
        }
    }

    [TestMethod]
    public void ReducedMotionCapsPublishedAnimationDurations()
    {
        ClientVisualSettingsRuntime.SetCurrent(ClientVisualSettings.Defaults with
        {
            ReduceMotion = true,
        });

        Assert.AreEqual(0.05f, ClientVisualSettingsRuntime.Duration(0.35f));
        Assert.AreEqual(0.03f, ClientVisualSettingsRuntime.Duration(0.03f));

        ClientVisualSettingsRuntime.SetCurrent(ClientVisualSettings.Defaults);
        Assert.AreEqual(0.22f, ClientVisualSettingsRuntime.Duration(0.22f));
    }
}
