using Godot;

namespace Scgs.GodotClient.UI;

/// <summary>
/// Shared responsive measurements for the product HUD.  These values describe
/// safe, floating surfaces; they deliberately never reserve a full-height rail.
/// </summary>
public static class GlassHudTheme
{
    public const float MinimumWidth = 1280.0f;
    public const float MinimumHeight = 720.0f;
    public const float PanelCornerRadius = 18.0f;
    public const float CompactGap = 12.0f;

    public static MatchHudMetrics MetricsFor(Vector2 viewportSize)
    {
        float width = Mathf.Max(viewportSize.X, MinimumWidth);
        float height = Mathf.Max(viewportSize.Y, MinimumHeight);
        float detailWidth = width switch
        {
            < 1440.0f => 248.0f,
            < 2200.0f => 288.0f,
            _ => 320.0f,
        };
        float detailHeight = Mathf.Clamp(height - 152.0f, 520.0f, 720.0f);
        float statusWidth = width < 1440.0f ? 292.0f : 316.0f;
        float dockWidth = width < 1440.0f ? 248.0f : 270.0f;

        return new MatchHudMetrics(
            detailWidth,
            detailHeight,
            statusWidth,
            dockWidth,
            width < 1440.0f ? 14.0f : 18.0f,
            width >= 2200.0f ? 24.0f : 18.0f);
    }
}

public readonly record struct MatchHudMetrics(
    float DetailWidth,
    float DetailHeight,
    float StatusWidth,
    float DockWidth,
    float EdgeInset,
    float TopInset);
