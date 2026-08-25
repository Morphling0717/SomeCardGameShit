// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.GodotClient.Battlefield;

namespace Scgs.GodotClient.UI;

/// <summary>
/// A compact, board-adjacent prompt. It deliberately carries only opaque keys;
/// authoritative choices stay in MatchScreen and are cleared before resolving.
/// </summary>
public sealed partial class DirectActionPanel : PanelContainer
{
    private readonly Dictionary<Button, Action> _handlers = new();
    private Label _prompt = null!;
    private Label _payment = null!;
    private Container _chips = null!;
    private Button _back = null!;
    private MarginContainer _margin = null!;
    private VBoxContainer _layout = null!;
    private bool _tacticalCandidate;

    public event Action<string>? ChoiceRequested;

    public event Action? BackRequested;

    internal bool HasSensitiveContentForSmoke =>
        !string.IsNullOrEmpty(_prompt.Text) ||
        !string.IsNullOrEmpty(_payment.Text) ||
        _chips.GetChildren().OfType<Button>().Any(button =>
            button.Visible && !string.IsNullOrEmpty(button.Text)) ||
        _handlers.Count != 0;

    public override void _Ready()
    {
        _prompt = GetNode<Label>("%DirectPrompt");
        _payment = GetNode<Label>("%DirectPayment");
        _chips = GetNode<Container>("%DirectChips");
        _back = GetNode<Button>("%DirectBackButton");
        _margin = GetNode<MarginContainer>("Margin");
        _layout = GetNode<VBoxContainer>("Margin/Layout");
        _back.Pressed += () => BackRequested?.Invoke();
        Visible = false;
    }

    internal void ConfigureVisualProfile(BattlefieldVisualProfile profile)
    {
        _tacticalCandidate = profile == BattlefieldVisualProfile.R3Candidate;
        SetCompactMode(compact: false);
    }

    public void Present(
        string prompt,
        IReadOnlyList<(string Label, string Key)> choices,
        string? paymentText,
        bool canGoBack)
    {
        SetCompactMode(false);
        ClearButtons();
        _prompt.Text = prompt;
        _payment.Text = paymentText ?? string.Empty;
        _payment.Visible = !string.IsNullOrWhiteSpace(paymentText);
        foreach ((string label, string key) in choices)
        {
            var chip = new Button
            {
                Text = label,
                CustomMinimumSize = _tacticalCandidate
                    ? new Vector2(92, 34)
                    : new Vector2(108, 42),
                ThemeTypeVariation = "PrimaryButton",
                FocusMode = FocusModeEnum.All,
                TooltipText = label,
            };
            string captured = key;
            Action handler = () => ChoiceRequested?.Invoke(captured);
            _handlers.Add(chip, handler);
            chip.Pressed += handler;
            if (_tacticalCandidate)
            {
                TacticalHudTheme.R3Candidate.ApplyActionButton(chip);
            }
            _chips.AddChild(chip);
        }

        _back.Visible = canGoBack;
        _back.Disabled = !canGoBack;
        Visible = true;
    }

    /// <summary>
    /// Reduces the prompt to its actionable pills when a full card-adjacent
    /// panel cannot fit without covering the selected card. Labels remain on
    /// the buttons and in their tooltips, so this fallback never changes the
    /// command or choice semantics.
    /// </summary>
    public void SetCompactMode(bool compact)
    {
        CustomMinimumSize = _tacticalCandidate
            ? compact ? new Vector2(176, 50) : new Vector2(300, 66)
            : compact ? new Vector2(196, 64) : new Vector2(360, 92);
        _prompt.Visible = !compact;
        _payment.Visible = !compact && !string.IsNullOrWhiteSpace(_payment.Text);
        _prompt.AddThemeFontSizeOverride("font_size", _tacticalCandidate ? 14 : 17);
        _payment.AddThemeFontSizeOverride("font_size", _tacticalCandidate ? 12 : 14);
        _layout.AddThemeConstantOverride("separation", _tacticalCandidate ? 2 : 6);
        int marginHorizontal = _tacticalCandidate ? 10 : 14;
        int marginVertical = _tacticalCandidate ? 5 : 9;
        _margin.AddThemeConstantOverride("margin_left", marginHorizontal);
        _margin.AddThemeConstantOverride("margin_top", marginVertical);
        _margin.AddThemeConstantOverride("margin_right", marginHorizontal);
        _margin.AddThemeConstantOverride("margin_bottom", marginVertical);
        _back.CustomMinimumSize = _tacticalCandidate
            ? compact ? new Vector2(64, 30) : new Vector2(72, 32)
            : compact ? new Vector2(72, 36) : new Vector2(88, 42);
        foreach (Button button in _chips.GetChildren().OfType<Button>())
        {
            button.CustomMinimumSize = _tacticalCandidate
                ? compact ? new Vector2(78, 30) : new Vector2(92, 34)
                : compact ? new Vector2(88, 36) : new Vector2(108, 42);
        }
    }

    public void SetBusy(bool busy)
    {
        foreach (Node child in _chips.GetChildren())
        {
            if (child is Button button)
            {
                button.Disabled = busy;
            }
        }
        _back.Disabled = busy || !_back.Visible;
    }

    public void ClearSensitive()
    {
        SetCompactMode(compact: false);
        _prompt.Text = string.Empty;
        _payment.Text = string.Empty;
        _payment.Visible = false;
        ClearButtons();
        _back.Disabled = true;
        Visible = false;
    }

    internal void PressChoiceForSmoke(string label)
    {
        Button? choice = _chips.GetChildren()
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Text, label, StringComparison.Ordinal));
        if (choice is null || choice.Disabled)
        {
            throw new InvalidOperationException($"Direct action chip '{label}' is unavailable.");
        }
        choice.EmitSignal(Button.SignalName.Pressed);
    }

    private void ClearButtons()
    {
        foreach (Node child in _chips.GetChildren())
        {
            if (child is Button button)
            {
                if (_handlers.Remove(button, out Action? handler))
                {
                    button.Pressed -= handler;
                }
                button.Disabled = true;
                button.Text = string.Empty;
                button.TooltipText = string.Empty;
                button.Visible = false;
            }
            child.QueueFree();
        }
        _handlers.Clear();
    }
}
