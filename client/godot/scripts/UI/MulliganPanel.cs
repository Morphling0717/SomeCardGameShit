// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;

namespace Scgs.GodotClient.UI;

public sealed partial class MulliganPanel : PanelContainer
{
    private Label _summary = null!;
    private Label _review = null!;
    private Button _confirmButton = null!;
    private Button _acknowledgeButton = null!;

    public event Action? ConfirmRequested;

    public event Action? ReviewAcknowledged;

    public override void _Ready()
    {
        _summary = GetNode<Label>("%MulliganSummary");
        _review = GetNode<Label>("%MulliganReview");
        _confirmButton = GetNode<Button>("%MulliganConfirmButton");
        _acknowledgeButton = GetNode<Button>("%MulliganAcknowledgeButton");
        _confirmButton.Pressed += () => ConfirmRequested?.Invoke();
        _acknowledgeButton.Pressed += () => ReviewAcknowledged?.Invoke();
    }

    public void PresentSelection(int selectedCount, int handCount, bool canSubmit)
    {
        Visible = true;
        _summary.Text = $"选择要替换的手牌：已选 {selectedCount}/{handCount}。不选择任何牌也可提交。";
        _review.Text = string.Empty;
        _confirmButton.Visible = true;
        _confirmButton.Disabled = !canSubmit;
        _acknowledgeButton.Visible = false;
    }

    public void PresentReview(IReadOnlyList<CardView> replacementHand)
    {
        Visible = true;
        _summary.Text = "调度已完成。确认自己的新手牌后再交接设备。";
        _review.Text = replacementHand.Count == 0
            ? "当前没有手牌。"
            : $"当前手牌：{string.Join("、", replacementHand.Select(card => card.Name))}";
        _confirmButton.Visible = false;
        _acknowledgeButton.Visible = true;
        _acknowledgeButton.Disabled = false;
        _acknowledgeButton.GrabFocus();
    }

    public void SetBusy(bool busy)
    {
        _confirmButton.Disabled = busy;
        _acknowledgeButton.Disabled = busy;
    }

    public void ClearSensitive()
    {
        _summary.Text = string.Empty;
        _review.Text = string.Empty;
        _confirmButton.Disabled = true;
        _acknowledgeButton.Disabled = true;
        Visible = false;
    }
}
