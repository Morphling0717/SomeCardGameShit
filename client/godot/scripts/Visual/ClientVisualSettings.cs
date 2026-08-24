// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.GodotClient.Visual;

public enum ClientWindowMode : byte
{
    Windowed = 0,
    BorderlessFullscreen = 1,
}

public readonly record struct ClientResolution(int Width, int Height)
{
    public override string ToString() => $"{Width} × {Height}";
}

public sealed record ClientVisualSettings(
    ClientWindowMode WindowMode,
    ClientResolution Resolution,
    int UiScalePercent,
    bool VSync,
    bool ReduceMotion)
{
    public static IReadOnlyList<ClientResolution> SupportedResolutions { get; } =
        Array.AsReadOnly<ClientResolution>(
        [
        new(1280, 720),
        new(1600, 900),
        new(1920, 1080),
        new(2560, 1440),
        ]);

    public static IReadOnlyList<int> SupportedUiScales { get; } =
        Array.AsReadOnly([90, 100, 110, 125]);

    public static ClientVisualSettings Defaults { get; } = new(
        ClientWindowMode.Windowed,
        new ClientResolution(1600, 900),
        100,
        VSync: true,
        ReduceMotion: false);

    public ClientVisualSettings Normalize()
    {
        ClientWindowMode windowMode = Enum.IsDefined(WindowMode)
            ? WindowMode
            : Defaults.WindowMode;
        ClientResolution resolution = SupportedResolutions.Contains(Resolution)
            ? Resolution
            : Defaults.Resolution;
        int uiScale = SupportedUiScales.Contains(UiScalePercent)
            ? UiScalePercent
            : Defaults.UiScalePercent;

        return this with
        {
            WindowMode = windowMode,
            Resolution = resolution,
            UiScalePercent = uiScale,
        };
    }
}

public interface IVisualSettingsStore
{
    ClientVisualSettings Load();

    void Save(ClientVisualSettings settings);
}

public static class ClientVisualSettingsRuntime
{
    public static ClientVisualSettings Current { get; private set; } =
        ClientVisualSettings.Defaults;

    public static void SetCurrent(ClientVisualSettings settings)
    {
        Current = settings.Normalize();
    }

    public static float Duration(float normalSeconds)
    {
        if (normalSeconds < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(normalSeconds));
        }

        return Current.ReduceMotion ? Math.Min(normalSeconds, 0.05f) : normalSeconds;
    }
}
