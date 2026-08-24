using Godot;

namespace Scgs.GodotClient.UI;

/// <summary>
/// Shared responsive measurements for the compact battle presentation. The
/// center battlefield receives a stable safe rectangle; opening a detail or
/// log surface must not make the camera zoom in and out.
/// </summary>
public static class GlassHudTheme
{
    public const float MinimumWidth = 1280.0f;
    public const float MinimumHeight = 720.0f;
    public const float PanelCornerRadius = 10.0f;
    public const float CompactGap = 12.0f;

    public static MatchHudMetrics MetricsFor(Vector2 viewportSize)
    {
        float width = Mathf.Max(viewportSize.X, MinimumWidth);
        float height = Mathf.Max(viewportSize.Y, MinimumHeight);
        float detailWidth = width switch
        {
            < 1440.0f => 240.0f,
            < 2200.0f => 288.0f,
            _ => 320.0f,
        };
        float detailHeight = Mathf.Clamp(height - 96.0f, 560.0f, 820.0f);
        float statusWidth = width switch
        {
            < 1440.0f => 196.0f,
            < 2200.0f => 240.0f,
            _ => 264.0f,
        };
        float dockWidth = statusWidth;

        return new MatchHudMetrics(
            detailWidth,
            detailHeight,
            statusWidth,
            dockWidth,
            width < 1440.0f ? 12.0f : width >= 2200.0f ? 20.0f : 16.0f,
            width >= 2200.0f ? 20.0f : 14.0f);
    }
}

public readonly record struct MatchHudMetrics(
    float DetailWidth,
    float DetailHeight,
    float StatusWidth,
    float DockWidth,
    float EdgeInset,
    float TopInset);
