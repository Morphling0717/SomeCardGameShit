// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

namespace Scgs.GodotClient.Preview;

internal enum AnimeFaction
{
    Neutral = 0,
    Oathguard = 1,
    Pactmage = 2,
}

internal enum AnimeCardKind
{
    Follower = 0,
    Spell = 1,
    Amulet = 2,
    Trap = 3,
    Field = 4,
}

internal static class AnimeVisualTheme
{
    internal static readonly Color Ink = new("100d23");
    internal static readonly Color DeepIndigo = new("181333");
    internal static readonly Color MoonWhite = new("f2e9d5");
    internal static readonly Color OldGold = new("d5ad61");
    internal static readonly Color PaleGold = new("f4d890");
    internal static readonly Color OathBlue = new("5da9ea");
    internal static readonly Color OathIvory = new("f6f0dc");
    internal static readonly Color PactViolet = new("9b5ad8");
    internal static readonly Color PactCrimson = new("d74978");
    internal static readonly Color Positive = new("65d6bc");
    internal static readonly Color Shadow = new(0.02f, 0.015f, 0.06f, 0.62f);

    internal static Color FactionColor(AnimeFaction faction) => faction switch
    {
        AnimeFaction.Oathguard => OathBlue,
        AnimeFaction.Pactmage => PactViolet,
        _ => OldGold,
    };

    internal static StyleBoxFlat Panel(
        Color tint,
        float alpha = 0.74f,
        int radius = 18,
        int borderWidth = 1)
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(tint, alpha),
            BorderColor = new Color(OldGold, MathF.Min(0.78f, alpha + 0.08f)),
            BorderWidthLeft = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
            ShadowColor = Shadow,
            ShadowSize = 9,
            ShadowOffset = new Vector2(0.0f, 4.0f),
            ContentMarginLeft = 18.0f,
            ContentMarginTop = 14.0f,
            ContentMarginRight = 18.0f,
            ContentMarginBottom = 14.0f,
        };
        return style;
    }

    internal static void ApplyButton(Button button, AnimeFaction faction, bool primary = false)
    {
        Color accent = FactionColor(faction);
        Color fill = primary ? accent.Darkened(0.42f) : DeepIndigo.Lightened(0.08f);
        StyleBoxFlat normal = Panel(fill, primary ? 0.92f : 0.78f, 14, primary ? 2 : 1);
        normal.BorderColor = new Color(primary ? PaleGold : accent, 0.82f);
        StyleBoxFlat hover = (StyleBoxFlat)normal.Duplicate(true);
        hover.BgColor = fill.Lightened(0.13f);
        hover.BorderColor = new Color(PaleGold, 0.98f);
        StyleBoxFlat pressed = (StyleBoxFlat)hover.Duplicate(true);
        pressed.BgColor = fill.Darkened(0.08f);
        foreach ((string key, StyleBoxFlat style) in new[]
                 {
                     ("normal", normal), ("hover", hover), ("focus", hover),
                     ("pressed", pressed),
                 })
        {
            button.AddThemeStyleboxOverride(key, style);
        }
        button.AddThemeColorOverride("font_color", MoonWhite);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_focus_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", MoonWhite);
        button.AddThemeFontSizeOverride("font_size", primary ? 20 : 17);
    }

    internal static Font DisplayFont =>
        GD.Load<Font>("res://assets/fonts/NotoSansCJKsc-Regular.otf");
}
