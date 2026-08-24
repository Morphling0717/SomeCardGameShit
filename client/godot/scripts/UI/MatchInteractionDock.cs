// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

namespace Scgs.GodotClient.UI;

public sealed partial class MatchInteractionDock : PanelContainer
{
    private Button _collapseButton = null!;
    private Control _glassSurface = null!;
    private MarginContainer _margin = null!;
    private Control _titleRow = null!;
    private float _expandedWidth = 374.0f;
    private bool _mulliganTrayActive;
    private DockLayout _dockedLayout;

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
        _glassSurface = GetNode<Control>("GlassSurface");
        _margin = GetNode<MarginContainer>("Margin");
        _titleRow = GetNode<Control>("Margin/Layout/TitleRow");
        _collapseButton = GetNode<Button>("%DockCollapseButton");
        _collapseButton.Pressed += ToggleCollapsed;
    }

    public void ShowMulligan()
    {
        HideTransientPanels();
        SetMulliganTray(true);
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
        SetMulliganTray(false);
    }

    public void ClearSensitive()
    {
        Mulligan.ClearSensitive();
        Actions.ClearSensitive();
        CardDetails.ClearSensitive();
        Confirmation.ClearSensitive();
        Reaction.ClearSensitive();
        EventLog.ClearSensitive();
        SetMulliganTray(false);
    }

    /// <summary>
    /// Mulligan is the one batch interaction in the hot-seat flow.  Present it
    /// as a bottom-centred tray instead of allowing its wide content to spill
    /// out of the compact right-side information dock.
    /// </summary>
    public void SetMulliganTray(bool enabled)
    {
        if (enabled)
        {
            if (!_mulliganTrayActive)
            {
                _dockedLayout = DockLayout.Capture(this);
                _mulliganTrayActive = true;
            }
            ApplyMulliganTrayLayout();
            _titleRow.Visible = false;
            CardDetails.Visible = false;
            EventLog.Visible = false;
            // MulliganPanel is itself the glass tray. Strip the dormant log
            // drawer's glass and padding so the two panels do not stack into a
            // dark double frame.
            ThemeTypeVariation = "HudCluster";
            _glassSurface.Visible = false;
            SetOuterMargin(0);
            return;
        }

        if (!_mulliganTrayActive)
        {
            return;
        }

        _mulliganTrayActive = false;
        _dockedLayout.Restore(this);
        _glassSurface.Visible = true;
        SetOuterMargin(8);
        _titleRow.Visible = true;
        CardDetails.Visible = !IsCollapsed;
        EventLog.Visible = !IsCollapsed;
    }

    private void ApplyMulliganTrayLayout()
    {
        Vector2 viewport = GetViewportRect().Size;
        float width = Mathf.Clamp(viewport.X * 0.52f, 680.0f, 820.0f);
        float height = viewport.Y <= 720.0f ? 148.0f : 154.0f;
        // Keep the tray immediately above the world-space hand fan.  A tray
        // docked to the bottom both obscures the cards and consumes their GUI
        // click area even though the visual remains visible behind the glass.
        float bottomInset = Mathf.Clamp(viewport.Y * 0.34f, 244.0f, 340.0f);
        AnchorLeft = 0.5f;
        AnchorTop = 1.0f;
        AnchorRight = 0.5f;
        AnchorBottom = 1.0f;
        OffsetLeft = -width * 0.5f;
        OffsetTop = -height - bottomInset;
        OffsetRight = width * 0.5f;
        OffsetBottom = -bottomInset;
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Begin;
        CustomMinimumSize = new Vector2(width, 0.0f);
    }

    private void SetOuterMargin(int pixels)
    {
        _margin.AddThemeConstantOverride("margin_left", pixels);
        _margin.AddThemeConstantOverride("margin_top", pixels);
        _margin.AddThemeConstantOverride("margin_right", pixels);
        _margin.AddThemeConstantOverride("margin_bottom", pixels);
    }

    private void ToggleCollapsed()
    {
        SetCollapsed(!IsCollapsed);
    }

    public void SetExpandedWidth(float width)
    {
        if (!float.IsFinite(width) || width < 220.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        _expandedWidth = width;
        CustomMinimumSize = new Vector2(IsCollapsed ? 72.0f : _expandedWidth, 0.0f);
    }

    public void SetCollapsed(bool collapsed)
    {
        if (IsCollapsed == collapsed)
        {
            CustomMinimumSize = new Vector2(IsCollapsed ? 72.0f : _expandedWidth, 0.0f);
            return;
        }

        IsCollapsed = collapsed;
        if (_mulliganTrayActive)
        {
            _collapseButton.Text = IsCollapsed ? "展开" : "收起";
            return;
        }
        CardDetails.Visible = !IsCollapsed;
        EventLog.Visible = !IsCollapsed;
        _collapseButton.Text = IsCollapsed ? "展开" : "收起";
        CustomMinimumSize = new Vector2(IsCollapsed ? 72.0f : _expandedWidth, 0.0f);
        CollapsedChanged?.Invoke(IsCollapsed);
    }

    internal void ToggleForSmoke() =>
        _collapseButton.EmitSignal(Button.SignalName.Pressed);

    private readonly record struct DockLayout(
        float AnchorLeft,
        float AnchorTop,
        float AnchorRight,
        float AnchorBottom,
        float OffsetLeft,
        float OffsetTop,
        float OffsetRight,
        float OffsetBottom,
        GrowDirection GrowHorizontal,
        GrowDirection GrowVertical,
        Vector2 CustomMinimumSize,
        StringName ThemeTypeVariation)
    {
        internal static DockLayout Capture(Control control) => new(
            control.AnchorLeft,
            control.AnchorTop,
            control.AnchorRight,
            control.AnchorBottom,
            control.OffsetLeft,
            control.OffsetTop,
            control.OffsetRight,
            control.OffsetBottom,
            control.GrowHorizontal,
            control.GrowVertical,
            control.CustomMinimumSize,
            control.ThemeTypeVariation);

        internal void Restore(Control control)
        {
            // Lower the tray-sized minimum before restoring anchors/offsets.
            // Otherwise Godot clamps the dock to the old 680–820 px minimum
            // while each offset is assigned and leaves an invisible blocker
            // stretched across the battlefield.
            control.CustomMinimumSize = CustomMinimumSize;
            control.ThemeTypeVariation = ThemeTypeVariation;
            control.AnchorLeft = AnchorLeft;
            control.AnchorTop = AnchorTop;
            control.AnchorRight = AnchorRight;
            control.AnchorBottom = AnchorBottom;
            control.OffsetLeft = OffsetLeft;
            control.OffsetTop = OffsetTop;
            control.OffsetRight = OffsetRight;
            control.OffsetBottom = OffsetBottom;
            control.GrowHorizontal = GrowHorizontal;
            control.GrowVertical = GrowVertical;
        }
    }
}
