// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;
using Scgs.GodotClient.Presentation;

namespace Scgs.GodotClient.UI;

public sealed partial class ActionPromptPanel : PanelContainer
{
    private static readonly PackedScene SlotScene =
        GD.Load<PackedScene>("res://scenes/cards/SnapshotSlot.tscn");

    private Label _prompt = null!;
    private Container _buttons = null!;
    private Button _cancelButton = null!;

    public event Action<ActionKind>? ActionRequested;

    public event Action<ulong>? CardRequested;

    public event Action<string>? ChoiceRequested;

    public event Action? CancelRequested;

    public override void _Ready()
    {
        _prompt = GetNode<Label>("%ActionPrompt");
        _buttons = GetNode<Container>("%ActionButtons");
        _cancelButton = GetNode<Button>("%ActionCancelButton");
        _cancelButton.Pressed += () => CancelRequested?.Invoke();
    }

    public void Present(
        string prompt,
        IEnumerable<ActionKind> actions,
        bool canCancel,
        string cancelLabel = "取消选择")
    {
        Visible = true;
        _prompt.Text = prompt;
        FreeChildren(_buttons);
        foreach (ActionKind action in actions.Distinct().OrderBy(value => (uint)value))
        {
            var button = new Button
            {
                Text = ActionPresentation.FormatAction(action),
                CustomMinimumSize = new Vector2(0, 42),
                FocusMode = FocusModeEnum.All,
            };
            button.Pressed += () => ActionRequested?.Invoke(action);
            _buttons.AddChild(button);
        }

        _cancelButton.Visible = canCancel;
        _cancelButton.Disabled = false;
        _cancelButton.Text = cancelLabel;
    }

    public void PresentCards(
        string prompt,
        IReadOnlyList<CardView> cards,
        string zoneName,
        bool canCancel,
        string cancelLabel = "取消选择")
    {
        Visible = true;
        _prompt.Text = prompt;
        FreeChildren(_buttons);
        for (int index = 0; index < cards.Count; index++)
        {
            SnapshotSlot slot = SlotScene.Instantiate<SnapshotSlot>();
            _buttons.AddChild(slot);
            slot.ShowCard(cards[index], zoneName, index, selectable: true);
            ulong? knownId = slot.KnownInstanceId;
            if (knownId.HasValue)
            {
                ulong capturedId = knownId.Value;
                slot.Activated += _ => CardRequested?.Invoke(capturedId);
            }
            else
            {
                slot.SetSelectable(false);
            }
        }

        if (cards.Count == 0)
        {
            _buttons.AddChild(new Label { Text = "没有可选择的公开卡牌。" });
        }
        _cancelButton.Visible = canCancel;
        _cancelButton.Disabled = false;
        _cancelButton.Text = cancelLabel;
    }

    public void PresentChoices(
        string prompt,
        IReadOnlyList<(string Label, string Key)> choices,
        bool canCancel,
        string cancelLabel = "取消选择")
    {
        Visible = true;
        _prompt.Text = prompt;
        FreeChildren(_buttons);
        foreach ((string label, string key) in choices)
        {
            var button = new Button
            {
                Text = label,
                CustomMinimumSize = new Vector2(0, 42),
                FocusMode = FocusModeEnum.All,
            };
            string capturedKey = key;
            button.Pressed += () => ChoiceRequested?.Invoke(capturedKey);
            _buttons.AddChild(button);
        }

        if (choices.Count == 0)
        {
            _buttons.AddChild(new Label { Text = "没有可选择的合法选项。" });
        }

        _cancelButton.Visible = canCancel;
        _cancelButton.Disabled = false;
        _cancelButton.Text = cancelLabel;
    }

    public void SetBusy(bool busy)
    {
        foreach (Node child in _buttons.GetChildren())
        {
            if (child is SnapshotSlot slot)
            {
                slot.SetSelectable(!busy && slot.HasKnownIdentity);
            }
            else if (child is Button button)
            {
                button.Disabled = busy;
            }
        }
        _cancelButton.Disabled = busy;
    }

    public void ClearSensitive()
    {
        _prompt.Text = string.Empty;
        FreeChildren(_buttons);
        _cancelButton.Disabled = true;
        _cancelButton.Text = "取消选择";
        Visible = false;
    }

    private static void FreeChildren(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            child.Free();
        }
    }
}
