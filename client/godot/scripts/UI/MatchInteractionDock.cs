// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

namespace Scgs.GodotClient.UI;

public sealed partial class MatchInteractionDock : PanelContainer
{
    private Button _collapseButton = null!;

    public event Action<bool>? CollapsedChanged;

    public bool IsCollapsed { get; private set; }
    public MulliganPanel Mulligan { get; private set; } = null!;

    public ActionPromptPanel Actions { get; private set; } = null!;

    public CardDetailPanel CardDetails { get; private set; } = null!;

    public ConfirmationPanel Confirmation { get; private set; } = null!;

    public ReactionPanel Reaction { get; private set; } = null!;

    public EventLogPanel EventLog { get; private set; } = null!;

    public override void _Ready()
    {
        Mulligan = GetNode<MulliganPanel>("%MulliganPanel");
        Actions = GetNode<ActionPromptPanel>("%ActionPromptPanel");
        CardDetails = GetNode<CardDetailPanel>("%CardDetailPanel");
        Confirmation = GetNode<ConfirmationPanel>("%ConfirmationPanel");
        Reaction = GetNode<ReactionPanel>("%ReactionPanel");
        EventLog = GetNode<EventLogPanel>("%EventLogPanel");
        _collapseButton = GetNode<Button>("%DockCollapseButton");
        _collapseButton.Pressed += ToggleCollapsed;
    }

    public void ShowMulligan()
    {
        HideTransientPanels();
        Mulligan.Visible = true;
    }

    public void ShowActions()
    {
        HideTransientPanels();
        Actions.Visible = true;
    }

    public void ShowConfirmation()
    {
        HideTransientPanels();
        Confirmation.Visible = true;
    }

    public void ShowReaction()
    {
        HideTransientPanels();
        Reaction.Visible = true;
    }

    public void HideTransientPanels()
    {
        Mulligan.Visible = false;
        Actions.Visible = false;
        Confirmation.Visible = false;
        Reaction.Visible = false;
    }

    public void ClearSensitive()
    {
        Mulligan.ClearSensitive();
        Actions.ClearSensitive();
        CardDetails.ClearSensitive();
        Confirmation.ClearSensitive();
        Reaction.ClearSensitive();
        EventLog.ClearSensitive();
    }

    private void ToggleCollapsed()
    {
        IsCollapsed = !IsCollapsed;
        CardDetails.Visible = !IsCollapsed;
        EventLog.Visible = !IsCollapsed;
        _collapseButton.Text = IsCollapsed ? "展开" : "收起";
        CustomMinimumSize = new Vector2(IsCollapsed ? 72 : 374, 0);
        CollapsedChanged?.Invoke(IsCollapsed);
    }

    internal void ToggleForSmoke() =>
        _collapseButton.EmitSignal(Button.SignalName.Pressed);
}
