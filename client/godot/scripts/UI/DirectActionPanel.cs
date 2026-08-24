// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

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
        _back.Pressed += () => BackRequested?.Invoke();
        Visible = false;
    }

    public void Present(
        string prompt,
        IReadOnlyList<(string Label, string Key)> choices,
        string? paymentText,
        bool canGoBack)
    {
        ClearButtons();
        _prompt.Text = prompt;
        _payment.Text = paymentText ?? string.Empty;
        _payment.Visible = !string.IsNullOrWhiteSpace(paymentText);
        foreach ((string label, string key) in choices)
        {
            var chip = new Button
            {
                Text = label,
                CustomMinimumSize = new Vector2(108, 42),
                ThemeTypeVariation = "PrimaryButton",
                FocusMode = FocusModeEnum.All,
                TooltipText = label,
            };
            string captured = key;
            Action handler = () => ChoiceRequested?.Invoke(captured);
            _handlers.Add(chip, handler);
            chip.Pressed += handler;
            _chips.AddChild(chip);
        }

        _back.Visible = canGoBack;
        _back.Disabled = !canGoBack;
        Visible = true;
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
