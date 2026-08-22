// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;
using Scgs.GodotClient.Presentation;

namespace Scgs.GodotClient.UI;

public sealed partial class ReactionPanel : PanelContainer
{
    private static readonly PackedScene SlotScene =
        GD.Load<PackedScene>("res://scenes/cards/SnapshotSlot.tscn");

    private Label _summary = null!;
    private Container _traps = null!;
    private Button _passButton = null!;
    private readonly Dictionary<SnapshotSlot, Action<SnapshotSlot>> _trapHandlers = new();

    public event Action<ulong>? TrapRequested;

    public event Action? PassRequested;

    internal bool HasSensitiveContentForSmoke =>
        !string.IsNullOrEmpty(_summary.Text) ||
        _traps.GetChildren().OfType<SnapshotSlot>().Any(slot => slot.Visible) ||
        _trapHandlers.Count != 0 || !_passButton.Disabled;

    public override void _Ready()
    {
        _summary = GetNode<Label>("%ReactionSummary");
        _traps = GetNode<Container>("%ReactionTraps");
        _passButton = GetNode<Button>("%ReactionPassButton");
        _passButton.Pressed += () => PassRequested?.Invoke();
    }

    public void Present(ReactionContext context, PlayerId viewer, string? sourceName)
    {
        Visible = true;
        _summary.Text = $"响应窗口：{FormatWindow(context.Window)} · 第 {context.Depth} 层\n" +
                        ActionPresentation.FormatReactionOrigin(context, viewer, sourceName);
        FreeChildren(_traps);
        for (int index = 0; index < context.EligibleTraps.Length; index++)
        {
            CardView trap = context.EligibleTraps[index];
            SnapshotSlot slot = SlotScene.Instantiate<SnapshotSlot>();
            _traps.AddChild(slot);
            slot.ShowCard(trap, "可响应伏策", index, selectable: true);
            ulong? knownId = slot.KnownInstanceId;
            if (knownId.HasValue)
            {
                ulong capturedId = knownId.Value;
                Action<SnapshotSlot> handler = _ => TrapRequested?.Invoke(capturedId);
                _trapHandlers.Add(slot, handler);
                slot.Activated += handler;
            }
            else
            {
                slot.SetSelectable(false);
            }
        }

        _passButton.Disabled = false;
        _passButton.GrabFocus();
    }

    public void SetBusy(bool busy)
    {
        foreach (Node child in _traps.GetChildren())
        {
            if (child is SnapshotSlot slot)
            {
                slot.SetSelectable(!busy && slot.HasKnownIdentity);
            }
        }
        _passButton.Disabled = busy;
    }

    public void ClearSensitive()
    {
        _summary.Text = string.Empty;
        FreeChildren(_traps);
        _passButton.Disabled = true;
        Visible = false;
    }

    internal void RequestPassForSmoke()
    {
        if (_passButton.Disabled)
        {
            throw new InvalidOperationException("The reaction pass button is unavailable.");
        }
        _passButton.EmitSignal(Button.SignalName.Pressed);
    }

    internal void RequestTrapForSmoke(ulong instanceId)
    {
        SnapshotSlot? trap = _traps.GetChildren()
            .OfType<SnapshotSlot>()
            .FirstOrDefault(slot => slot.Visible && slot.KnownInstanceId == instanceId);
        if (trap is null || trap.Disabled)
        {
            throw new InvalidOperationException(
                $"Reaction trap {instanceId} is unavailable in the centered overlay.");
        }
        trap.EmitSignal(Button.SignalName.Pressed);
    }

    private static string FormatWindow(ReactionWindow window) => window switch
    {
        ReactionWindow.SpellDeclared => "法术宣言",
        ReactionWindow.EntryEffectPending => "登场效果待结算",
        ReactionWindow.AttackDeclared => "攻击宣言",
        ReactionWindow.None => "无",
        _ => $"未知（{(uint)window}）",
    };

    private void FreeChildren(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            if (child is SnapshotSlot slot)
            {
                if (_trapHandlers.Remove(slot, out Action<SnapshotSlot>? handler))
                {
                    slot.Activated -= handler;
                }
                slot.ClearSensitive();
                slot.Visible = false;
            }
            child.QueueFree();
        }
        _trapHandlers.Clear();
    }
}
