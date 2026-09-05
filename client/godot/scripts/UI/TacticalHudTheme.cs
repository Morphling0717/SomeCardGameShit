// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.GodotClient.Battlefield;

namespace Scgs.GodotClient.UI;

/// <summary>
/// Runtime-only tactical HUD skin used by the R3 vertical slice. It deliberately
/// keeps the stage dominant: neutral translucent metal, warm functional accents
/// and no cyan full-height panel language.
/// </summary>
internal sealed record TacticalHudTheme(
    Color PanelFill,
    Color PanelBorder,
    Color ChipFill,
    Color ChipBorder,
    Color PrimaryText,
    Color SecondaryText,
    Color FunctionalAccent,
    Color HealthFill,
    Color HealthTrack,
    Color ButtonFill,
    Color ButtonHover,
    Color ButtonPressed,
    Color ButtonBorder)
{
    private const string DefaultGlassMaterialPath =
        "res://assets/themes/glass_panel_material.tres";

    internal static TacticalHudTheme AnimeV1 { get; } = new(
        new Color(0.10f, 0.07f, 0.19f, 0.68f), new Color(0.82f, 0.69f, 0.43f, 0.62f),
        new Color(0.18f, 0.12f, 0.28f, 0.74f), new Color(0.85f, 0.73f, 0.48f, 0.68f),
        new Color("f2e9d5"), new Color("c9bdd8"), new Color("e4c787"),
        new Color("ad759d"), new Color(0.15f, 0.10f, 0.23f, 0.8f),
        new Color(0.29f, 0.22f, 0.38f, 0.92f), new Color(0.40f, 0.30f, 0.48f, 0.94f),
        new Color(0.21f, 0.15f, 0.29f, 0.94f), new Color(0.90f, 0.77f, 0.49f, 0.9f));

    internal static TacticalHudTheme R3Candidate { get; } = new(
        new Color(0.055f, 0.063f, 0.068f, 0.72f),
        new Color(0.48f, 0.48f, 0.44f, 0.48f),
        new Color(0.075f, 0.082f, 0.087f, 0.78f),
        new Color(0.58f, 0.55f, 0.47f, 0.55f),
        new Color("eee8dc"),
        new Color("b9b8b1"),
        new Color("d3b36e"),
        new Color("c08a55"),
        new Color(0.045f, 0.05f, 0.052f, 0.92f),
        new Color(0.12f, 0.125f, 0.124f, 0.92f),
        new Color(0.19f, 0.185f, 0.17f, 0.96f),
        new Color(0.095f, 0.098f, 0.095f, 0.98f),
        new Color(0.65f, 0.57f, 0.39f, 0.88f));

    internal static MatchHudMetrics MetricsFor(
        BattlefieldVisualProfile profile,
        Vector2 viewportSize)
    {
        if (profile == BattlefieldVisualProfile.Gate4BR2)
        {
            return GlassHudTheme.MetricsFor(viewportSize);
        }

        float width = Mathf.Max(viewportSize.X, GlassHudTheme.MinimumWidth);
        float height = Mathf.Max(viewportSize.Y, GlassHudTheme.MinimumHeight);
        float detailWidth = width switch
        {
            < 1440.0f => 228.0f,
            < 2200.0f => 264.0f,
            _ => 304.0f,
        };
        float statusWidth = width switch
        {
            < 1440.0f => 216.0f,
            < 2200.0f => 248.0f,
            _ => 268.0f,
        };

        return new MatchHudMetrics(
            detailWidth,
            Mathf.Clamp(height - 110.0f, 550.0f, 810.0f),
            statusWidth,
            statusWidth,
            width < 1440.0f ? 10.0f : width >= 2200.0f ? 18.0f : 14.0f,
            width >= 2200.0f ? 18.0f : 12.0f);
    }

    internal void Apply(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        StyleBoxFlat panel = CreateStyle(PanelFill, PanelBorder, 10, 1, 5);
        StyleBoxFlat chip = CreateStyle(ChipFill, ChipBorder, 7, 1, 3);
        StyleBoxFlat healthTrack = CreateStyle(HealthTrack, Colors.Transparent, 3, 0, 0);
        StyleBoxFlat healthFill = CreateStyle(HealthFill, Colors.Transparent, 3, 0, 0);
        StyleBoxFlat button = CreateStyle(ButtonFill, ButtonBorder, 8, 1, 4);
        StyleBoxFlat buttonHover = CreateStyle(ButtonHover, FunctionalAccent, 8, 1, 6);
        StyleBoxFlat buttonPressed = CreateStyle(ButtonPressed, FunctionalAccent, 8, 1, 2);

        foreach (string prefix in new[] { "Opponent", "Own" })
        {
            PanelContainer pod = root.GetNode<PanelContainer>($"%{prefix}StatusPod");
            pod.AddThemeStyleboxOverride("panel", panel);
            pod.GetNode<PanelContainer>($"{prefix}StatusRow/{prefix}PortraitFrame")
                .AddThemeStyleboxOverride("panel", chip);
            root.GetNode<Label>($"%{prefix}SeatLabel")
                .AddThemeColorOverride("font_color", PrimaryText);
            root.GetNode<Label>($"%{prefix}SeatLabel")
                .AddThemeFontSizeOverride("font_size", 14);
            root.GetNode<Label>($"%{prefix}ResourceLabel")
                .AddThemeColorOverride("font_color", SecondaryText);
            root.GetNode<Label>($"%{prefix}ResourceLabel")
                .AddThemeFontSizeOverride("font_size", 13);
            root.GetNode<Label>($"%{prefix}ActiveIndicator")
                .AddThemeColorOverride("font_color", FunctionalAccent);
            ProgressBar health = root.GetNode<ProgressBar>($"%{prefix}HealthBar");
            health.AddThemeStyleboxOverride("background", healthTrack);
            health.AddThemeStyleboxOverride("fill", healthFill);
        }

        root.GetNode<PanelContainer>("%PhaseCapsule")
            .AddThemeStyleboxOverride("panel", chip);
        root.GetNode<Label>("%PhaseLabel")
            .AddThemeColorOverride("font_color", PrimaryText);
        Button endTurn = root.GetNode<Button>("%EndTurnButton");
        endTurn.AddThemeStyleboxOverride("normal", button);
        endTurn.AddThemeStyleboxOverride("hover", buttonHover);
        endTurn.AddThemeStyleboxOverride("pressed", buttonPressed);
        endTurn.AddThemeColorOverride("font_color", PrimaryText);
        endTurn.AddThemeColorOverride("font_hover_color", Colors.White);

        PanelContainer directActions = root.GetNode<PanelContainer>("%DirectActionPanel");
        directActions.AddThemeStyleboxOverride(
            "panel",
            CreateStyle(
                new Color(PanelFill, 0.04f),
                Colors.Transparent,
                12,
                0,
                0));
        directActions.GetNode<Label>("%DirectPrompt")
            .AddThemeColorOverride("font_color", PrimaryText);
        directActions.GetNode<Label>("%DirectPayment")
            .AddThemeColorOverride("font_color", FunctionalAccent.Darkened(0.04f));
        ApplyActionButton(directActions.GetNode<Button>("%DirectBackButton"));

        if (root.GetNodeOrNull<PanelContainer>("%BattlefieldCardDetails") is { } details)
        {
            details.AddThemeStyleboxOverride("panel", panel);
            details.GetNode<PanelContainer>("Margin/Layout/CardDetailBody/ArtworkFrame")
                .AddThemeStyleboxOverride("panel", chip);
            details.GetNode<Label>("%CardDetailTitle")
                .AddThemeColorOverride("font_color", FunctionalAccent.Lightened(0.18f));
            details.GetNode<RichTextLabel>("%CardDetailRules")
                .AddThemeColorOverride("default_color", PrimaryText.Darkened(0.08f));
        }

        ApplyGlassMaterial(root, "%OpponentStatusPod", PanelFill, PanelBorder);
        ApplyGlassMaterial(root, "%OwnStatusPod", PanelFill, PanelBorder);
        ApplyGlassMaterial(root, "%BattlefieldCardDetails", PanelFill, PanelBorder);
        ApplyGlassMaterial(
            root,
            "%DirectActionPanel",
            ChipFill.Lightened(0.06f),
            ButtonBorder,
            topAlpha: 0.28f,
            bottomAlpha: 0.19f,
            edgeAlpha: 0.32f,
            blurLod: 1.15f,
            radius: 12.0f,
            highlight: 0.08f);
    }

    internal static void RestoreGate4BR2(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        foreach (string prefix in new[] { "Opponent", "Own" })
        {
            PanelContainer pod = root.GetNode<PanelContainer>($"%{prefix}StatusPod");
            pod.RemoveThemeStyleboxOverride("panel");
            pod.GetNode<PanelContainer>($"{prefix}StatusRow/{prefix}PortraitFrame")
                .RemoveThemeStyleboxOverride("panel");
            root.GetNode<Label>($"%{prefix}SeatLabel")
                .RemoveThemeColorOverride("font_color");
            root.GetNode<Label>($"%{prefix}SeatLabel")
                .RemoveThemeFontSizeOverride("font_size");
            root.GetNode<Label>($"%{prefix}ResourceLabel")
                .RemoveThemeColorOverride("font_color");
            root.GetNode<Label>($"%{prefix}ResourceLabel")
                .RemoveThemeFontSizeOverride("font_size");
            root.GetNode<Label>($"%{prefix}ActiveIndicator")
                .RemoveThemeColorOverride("font_color");
            ProgressBar health = root.GetNode<ProgressBar>($"%{prefix}HealthBar");
            health.RemoveThemeStyleboxOverride("background");
            health.RemoveThemeStyleboxOverride("fill");
        }

        root.GetNode<PanelContainer>("%PhaseCapsule").RemoveThemeStyleboxOverride("panel");
        root.GetNode<Label>("%PhaseLabel").RemoveThemeColorOverride("font_color");
        Button endTurn = root.GetNode<Button>("%EndTurnButton");
        endTurn.RemoveThemeStyleboxOverride("normal");
        endTurn.RemoveThemeStyleboxOverride("hover");
        endTurn.RemoveThemeStyleboxOverride("pressed");
        endTurn.RemoveThemeColorOverride("font_color");
        endTurn.RemoveThemeColorOverride("font_hover_color");
        PanelContainer directActions = root.GetNode<PanelContainer>("%DirectActionPanel");
        directActions.RemoveThemeStyleboxOverride("panel");
        directActions.GetNode<Label>("%DirectPrompt")
            .RemoveThemeColorOverride("font_color");
        directActions.GetNode<Label>("%DirectPayment")
            .RemoveThemeColorOverride("font_color");
        RestoreActionButton(directActions.GetNode<Button>("%DirectBackButton"));
        if (directActions.GetNodeOrNull<Container>("%DirectChips") is { } directChips)
        {
            foreach (Button button in directChips.GetChildren().OfType<Button>())
            {
                RestoreActionButton(button);
            }
        }

        if (root.GetNodeOrNull<PanelContainer>("%BattlefieldCardDetails") is { } details)
        {
            details.RemoveThemeStyleboxOverride("panel");
            details.GetNode<PanelContainer>("Margin/Layout/CardDetailBody/ArtworkFrame")
                .RemoveThemeStyleboxOverride("panel");
            details.GetNode<Label>("%CardDetailTitle").RemoveThemeColorOverride("font_color");
            details.GetNode<RichTextLabel>("%CardDetailRules")
                .RemoveThemeColorOverride("default_color");
        }

        RestoreGlassMaterial(root, "%OpponentStatusPod");
        RestoreGlassMaterial(root, "%OwnStatusPod");
        RestoreGlassMaterial(root, "%BattlefieldCardDetails");
        RestoreGlassMaterial(root, "%DirectActionPanel");
    }

    internal void ApplyActionButton(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        StyleBoxFlat normal = CreateStyle(
            new Color(ButtonFill, 0.80f),
            new Color(ButtonBorder, 0.52f),
            7,
            1,
            1);
        StyleBoxFlat hover = CreateStyle(
            new Color(ButtonHover, 0.94f),
            new Color(FunctionalAccent, 0.86f),
            7,
            1,
            3);
        StyleBoxFlat pressed = CreateStyle(
            new Color(ButtonPressed, 0.98f),
            new Color(FunctionalAccent, 0.96f),
            7,
            1,
            1);
        StyleBoxFlat disabled = CreateStyle(
            new Color(ButtonFill, 0.38f),
            new Color(ButtonBorder, 0.22f),
            7,
            1,
            0);
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", pressed);
        button.AddThemeStyleboxOverride("focus", hover);
        button.AddThemeStyleboxOverride("disabled", disabled);
        button.AddThemeColorOverride("font_color", PrimaryText);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", PrimaryText);
        button.AddThemeColorOverride("font_focus_color", Colors.White);
        button.AddThemeColorOverride("font_disabled_color", SecondaryText.Darkened(0.22f));
        button.AddThemeFontSizeOverride("font_size", 13);
    }

    private static void RestoreActionButton(Button button)
    {
        foreach (string style in new[] { "normal", "hover", "pressed", "focus", "disabled" })
        {
            button.RemoveThemeStyleboxOverride(style);
        }
        foreach (string color in new[]
                 {
                     "font_color",
                     "font_hover_color",
                     "font_pressed_color",
                     "font_focus_color",
                     "font_disabled_color",
                 })
        {
            button.RemoveThemeColorOverride(color);
        }
        button.RemoveThemeFontSizeOverride("font_size");
    }

    private static StyleBoxFlat CreateStyle(
        Color fill,
        Color border,
        int radius,
        int borderWidth,
        int shadowSize) => new()
        {
            BgColor = fill,
            BorderColor = border,
            BorderWidthLeft = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomRight = radius,
            CornerRadiusBottomLeft = radius,
            ShadowColor = new Color(0.0f, 0.0f, 0.0f, 0.26f),
            ShadowSize = shadowSize,
        };

    private static void ApplyGlassMaterial(
        Control root,
        string panelPath,
        Color tint,
        Color edge,
        float topAlpha = 0.58f,
        float bottomAlpha = 0.48f,
        float edgeAlpha = 0.52f,
        float blurLod = 1.65f,
        float radius = 10.0f,
        float highlight = 0.16f)
    {
        if (root.GetNodeOrNull<Control>(panelPath)?.GetNodeOrNull<ColorRect>("GlassSurface") is not
            { Material: ShaderMaterial source } glass)
        {
            return;
        }

        var material = (ShaderMaterial)source.Duplicate(true);
        material.SetShaderParameter("tint_top", new Color(tint.Lightened(0.10f), topAlpha));
        material.SetShaderParameter("tint_bottom", new Color(tint.Darkened(0.10f), bottomAlpha));
        material.SetShaderParameter("edge_color", new Color(edge, edgeAlpha));
        material.SetShaderParameter("blur_lod", blurLod);
        material.SetShaderParameter("corner_radius_px", radius);
        material.SetShaderParameter("highlight_strength", highlight);
        glass.Material = material;
    }

    private static void RestoreGlassMaterial(Control root, string panelPath)
    {
        if (root.GetNodeOrNull<Control>(panelPath)?.GetNodeOrNull<ColorRect>("GlassSurface") is
            { } glass)
        {
            glass.Material = GD.Load<Material>(DefaultGlassMaterialPath);
        }
    }
}
