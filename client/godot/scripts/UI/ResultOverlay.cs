// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;
using V05 = Scgs.Client.V05;

namespace Scgs.GodotClient.UI;

public sealed partial class ResultOverlay : Control
{
    private Label _title = null!;
    private Button _restartButton = null!;

    public event Action? RestartRequested;

    public event Action? MenuRequested;

    public override void _Ready()
    {
        _title = GetNode<Label>("%ResultTitle");
        _restartButton = GetNode<Button>("%RestartMatchButton");
        _restartButton.Pressed += () => RestartRequested?.Invoke();
        GetNode<Button>("%ResultMenuButton").Pressed += () => MenuRequested?.Invoke();
    }

    public void Present(GameResult result, PlayerId viewer)
    {
        _title.Text = result switch
        {
            GameResult.Player0Won when viewer == PlayerId.Player0 => "你获胜了",
            GameResult.Player1Won when viewer == PlayerId.Player1 => "你获胜了",
            GameResult.Player0Won or GameResult.Player1Won => "对手获胜",
            GameResult.Draw => "平局",
            _ => "比赛尚未结束",
        };
        Visible = true;
        MouseFilter = MouseFilterEnum.Stop;
        _restartButton.GrabFocus();
    }

    public void Present(V05.GameResult result, V05.PlayerId viewer)
    {
        _title.Text = result switch
        {
            V05.GameResult.Player0Won when viewer == V05.PlayerId.Player0 => "你获胜了",
            V05.GameResult.Player1Won when viewer == V05.PlayerId.Player1 => "你获胜了",
            V05.GameResult.Player0Won or V05.GameResult.Player1Won => "对手获胜",
            V05.GameResult.Draw => "平局",
            _ => "比赛尚未结束",
        };
        Visible = true;
        MouseFilter = MouseFilterEnum.Stop;
        _restartButton.GrabFocus();
    }

    public void Dismiss()
    {
        _title.Text = string.Empty;
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    internal void RequestRestartForSmoke()
    {
        if (!Visible || _restartButton.Disabled)
        {
            throw new InvalidOperationException("The result restart button is unavailable.");
        }
        _restartButton.EmitSignal(Button.SignalName.Pressed);
    }
}
