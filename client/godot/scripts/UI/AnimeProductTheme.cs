// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.GodotClient.Preview;

namespace Scgs.GodotClient.UI;

/// <summary>Shared product skin; no game state, identity inference or rules.</summary>
internal static class AnimeProductTheme
{
    private static readonly Lazy<Theme> Shared = new(CreateTheme);

    internal static void Apply(Control root)
    {
        root.Theme = Shared.Value;
        foreach (Node node in Descendants(root))
        {
            if (node is Label label)
            {
                label.AddThemeColorOverride("font_color", AnimeVisualTheme.MoonWhite);
                label.AddThemeColorOverride("font_shadow_color", new Color("201632"));
            }
            if (node is RichTextLabel rules)
                rules.AddThemeColorOverride("default_color", AnimeVisualTheme.MoonWhite);
            if (node is PanelContainer panel)
                panel.AddThemeStyleboxOverride("panel", Shared.Value.GetStylebox("panel", "PanelContainer"));
            if (node is Button button)
            {
                AnimeVisualTheme.ApplyButton(button, AnimeFaction.Neutral,
                    button.ThemeTypeVariation == "PrimaryButton");
                // Preserve inherited layout/font sizes (card names must remain complete).
                button.RemoveThemeFontSizeOverride("font_size");
            }
            if (node is ColorRect { Material: ShaderMaterial source } glass && glass.Name == "GlassSurface")
            {
                var material = (ShaderMaterial)source.Duplicate();
                material.SetShaderParameter("tint_top", new Color(0.22f, 0.16f, 0.33f, 0.60f));
                material.SetShaderParameter("tint_bottom", new Color(0.10f, 0.07f, 0.20f, 0.52f));
                material.SetShaderParameter("edge_color", new Color(0.85f, 0.73f, 0.48f, 0.64f));
                material.SetShaderParameter("corner_radius_px", 18.0f);
                glass.Material = material;
            }
        }
    }

    private static Theme CreateTheme()
    {
        var theme = (Theme)GD.Load<Theme>("res://assets/themes/default_theme.tres").Duplicate(true);
        theme.SetStylebox("panel", "PanelContainer", AnimeVisualTheme.Panel(AnimeVisualTheme.DeepIndigo, 0.64f));
        theme.SetStylebox("panel", "PopupMenu", AnimeVisualTheme.Panel(AnimeVisualTheme.DeepIndigo, 0.96f));
        theme.SetColor("font_color", "Label", AnimeVisualTheme.MoonWhite);
        theme.SetColor("default_color", "RichTextLabel", AnimeVisualTheme.MoonWhite);
        theme.SetColor("font_color", "PopupMenu", AnimeVisualTheme.MoonWhite);
        foreach (string type in new[] { "Button", "PrimaryButton", "OptionButton", "MenuButton", "CheckButton" })
        {
            theme.SetStylebox("normal", type, AnimeVisualTheme.Panel(new Color("30223f"), 0.82f, 14));
            theme.SetStylebox("hover", type, AnimeVisualTheme.Panel(new Color("513960"), 0.92f, 14));
            theme.SetStylebox("pressed", type, AnimeVisualTheme.Panel(new Color("21162e"), 0.92f, 14));
            theme.SetColor("font_color", type, AnimeVisualTheme.MoonWhite);
            theme.SetColor("font_focus_color", type, AnimeVisualTheme.PaleGold);
        }
        return theme;
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        yield return root;
        foreach (Node child in root.GetChildren())
            foreach (Node node in Descendants(child)) yield return node;
    }
}
