// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

namespace Scgs.GodotClient.Visual;

public sealed class GodotVisualSettingsStore(string path = "user://settings.cfg")
    : IVisualSettingsStore
{
    private const string Section = "visual";

    public ClientVisualSettings Load()
    {
        try
        {
            var config = new ConfigFile();
            if (config.Load(path) != Error.Ok)
            {
                return ClientVisualSettings.Defaults;
            }

            var loaded = new ClientVisualSettings(
                ParseWindowMode(config.GetValue(Section, "window_mode", "windowed").AsString()),
                new ClientResolution(
                    ReadInt(config, "width", ClientVisualSettings.Defaults.Resolution.Width),
                    ReadInt(config, "height", ClientVisualSettings.Defaults.Resolution.Height)),
                ReadInt(config, "ui_scale_percent", ClientVisualSettings.Defaults.UiScalePercent),
                ReadBool(config, "vsync", ClientVisualSettings.Defaults.VSync),
                ReadBool(config, "reduce_motion", ClientVisualSettings.Defaults.ReduceMotion));
            return loaded.Normalize();
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Ignoring invalid visual settings at {path}: {exception.Message}");
            return ClientVisualSettings.Defaults;
        }
    }

    public void Save(ClientVisualSettings settings)
    {
        ClientVisualSettings normalized = settings.Normalize();
        var config = new ConfigFile();
        config.SetValue(Section, "window_mode", normalized.WindowMode switch
        {
            ClientWindowMode.Windowed => "windowed",
            ClientWindowMode.BorderlessFullscreen => "borderless_fullscreen",
            _ => throw new InvalidOperationException("The normalized window mode is invalid."),
        });
        config.SetValue(Section, "width", normalized.Resolution.Width);
        config.SetValue(Section, "height", normalized.Resolution.Height);
        config.SetValue(Section, "ui_scale_percent", normalized.UiScalePercent);
        config.SetValue(Section, "vsync", normalized.VSync);
        config.SetValue(Section, "reduce_motion", normalized.ReduceMotion);

        Error result = config.Save(path);
        if (result != Error.Ok)
        {
            throw new IOException($"Godot could not save visual settings ({result}).");
        }
    }

    private static ClientWindowMode ParseWindowMode(string value) => value switch
    {
        "windowed" => ClientWindowMode.Windowed,
        "borderless_fullscreen" => ClientWindowMode.BorderlessFullscreen,
        _ => (ClientWindowMode)byte.MaxValue,
    };

    private static int ReadInt(ConfigFile config, string key, int fallback)
    {
        Variant value = config.GetValue(Section, key, fallback);
        return value.VariantType == Variant.Type.Int
            ? checked((int)value.AsInt64())
            : fallback;
    }

    private static bool ReadBool(ConfigFile config, string key, bool fallback)
    {
        Variant value = config.GetValue(Section, key, fallback);
        return value.VariantType == Variant.Type.Bool ? value.AsBool() : fallback;
    }
}

public static class ClientVisualSettingsApplier
{
    public static void Apply(Window window, ClientVisualSettings settings)
    {
        ArgumentNullException.ThrowIfNull(window);
        ClientVisualSettings normalized = settings.Normalize();
        ClientVisualSettingsRuntime.SetCurrent(normalized);

        window.ContentScaleFactor = normalized.UiScalePercent / 100.0f;
        DisplayServer.WindowSetVsyncMode(normalized.VSync
            ? DisplayServer.VSyncMode.Enabled
            : DisplayServer.VSyncMode.Disabled);

        if (normalized.WindowMode == ClientWindowMode.BorderlessFullscreen)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
            return;
        }

        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
        DisplayServer.WindowSetSize(new Vector2I(
            normalized.Resolution.Width,
            normalized.Resolution.Height));
    }
}
