// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;
using Scgs.GodotClient.Presentation;

namespace Scgs.GodotClient.UI;

public sealed partial class ConfirmationPanel : PanelContainer
{
    private Label _title = null!;
    private Label _summary = null!;
    private Label _payment = null!;
    private Button _confirmButton = null!;
    private Button _cancelButton = null!;

    public event Action? ConfirmRequested;

    public event Action? CancelRequested;

    internal bool HasSensitiveContentForSmoke =>
        !string.IsNullOrEmpty(_summary.Text) || !string.IsNullOrEmpty(_payment.Text);

    public override void _Ready()
    {
        _title = GetNode<Label>("%ConfirmationTitle");
        _summary = GetNode<Label>("%ConfirmationSummary");
        _payment = GetNode<Label>("%PaymentSummary");
        _confirmButton = GetNode<Button>("%ActionConfirmButton");
        _cancelButton = GetNode<Button>("%ConfirmationCancelButton");
        _confirmButton.Pressed += () => ConfirmRequested?.Invoke();
        _cancelButton.Pressed += () => CancelRequested?.Invoke();
    }

    public void Present(
        GameCommandRequest command,
        PaymentPreview payment,
        string? sourceName,
        string? targetDescription,
        string? warning = null)
    {
        Visible = true;
        _title.Text = "确认行动";
        _summary.Text = ActionPresentation.FormatConfirmation(
            command,
            payment,
            sourceName,
            targetDescription);
        _payment.Text = string.IsNullOrWhiteSpace(warning)
            ? "支付预览仅包含费用变化，不包含卡牌效果与响应结果。"
            : $"{warning}\n支付预览仅包含费用变化，不包含卡牌效果与响应结果。";
        _confirmButton.Disabled = !payment.Status.IsSuccess;
        _cancelButton.Disabled = false;
        _cancelButton.GrabFocus();
    }

    public void PresentProductConfirmation(
        string title,
        string summary,
        string detail,
        bool canConfirm = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        Visible = true;
        _title.Text = title;
        _summary.Text = summary;
        _payment.Text = detail;
        _confirmButton.Disabled = !canConfirm;
        _cancelButton.Disabled = false;
        _cancelButton.GrabFocus();
    }

    public void SetBusy(bool busy)
    {
        _confirmButton.Disabled = busy;
        _cancelButton.Disabled = busy;
    }

    public void ClearSensitive()
    {
        _summary.Text = string.Empty;
        _payment.Text = string.Empty;
        _confirmButton.Disabled = true;
        _cancelButton.Disabled = true;
        Visible = false;
    }

    internal void RequestConfirmForSmoke()
    {
        if (!Visible || _confirmButton.Disabled)
        {
            throw new InvalidOperationException("The confirmation button is unavailable.");
        }
        _confirmButton.EmitSignal(Button.SignalName.Pressed);
    }
}
