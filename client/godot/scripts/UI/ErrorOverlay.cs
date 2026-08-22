// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

namespace Scgs.GodotClient.UI;

public sealed partial class ErrorOverlay : Control
{
    private Label _message = null!;
    private Button _retryButton = null!;

    public event Action? RetryRequested;

    public event Action? MenuRequested;

    public override void _Ready()
    {
        _message = GetNode<Label>("%ErrorMessage");
        _retryButton = GetNode<Button>("%RetryButton");
        _retryButton.Pressed += () => RetryRequested?.Invoke();
        GetNode<Button>("%ErrorMenuButton").Pressed += () => MenuRequested?.Invoke();
    }

    public void Present(string safeMessage, bool canRetry)
    {
        _message.Text = safeMessage;
        _retryButton.Visible = canRetry;
        Visible = true;
        MouseFilter = MouseFilterEnum.Stop;
        (canRetry ? _retryButton : GetNode<Button>("%ErrorMenuButton")).GrabFocus();
    }

    public void Dismiss()
    {
        _message.Text = string.Empty;
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
    }
}
