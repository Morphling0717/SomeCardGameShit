using Godot;
using Scgs.Client;
using Scgs.GodotClient.Visuals;
using Scgs.Hotseat;

namespace Scgs.GodotClient.UI;

/// <summary>
/// Applies the floating HUD geometry without reading match DTOs. MatchScreen
/// remains responsible for safe data binding and interaction state.
/// </summary>
public sealed class MatchHudPresenter
{
    private Control? _root;
    private ILeaderPortraitCatalog? _portraitCatalog;
    private MatchVisualIdentity? _identity;
    private PlayerId? _perspectiveViewer;

    public MatchHudPresenter()
    {
    }

    public MatchHudPresenter(Control root)
    {
        Bind(root);
    }

    public void Bind(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _root = root;
    }

    public void ConfigureIdentity(
        MatchVisualIdentity identity,
        ILeaderPortraitCatalog portraitCatalog)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(portraitCatalog);
        _identity = identity;
        _portraitCatalog = portraitCatalog;
        if (_perspectiveViewer.HasValue)
        {
            BindPortraits(_perspectiveViewer.Value);
        }
    }

    /// <summary>Renders only viewer-safe snapshot values.</summary>
    public void Render(MatchView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        EnsureBound();
        if (view.Players.Length != 2)
        {
            throw new ScgsProtocolException("The match HUD requires exactly two players.");
        }

        _perspectiveViewer = view.Viewer;
        PlayerId opponent = Other(view.Viewer);
        BindPortraits(view.Viewer);
        BindPlayerStatus(
            own: true,
            view.Viewer,
            view.ActivePlayer,
            view.Players[(int)view.Viewer].LeaderHealth,
            view.Players[(int)view.Viewer].MaximumLeaderHealth);
        BindPlayerStatus(
            own: false,
            opponent,
            view.ActivePlayer,
            view.Players[(int)opponent].LeaderHealth,
            view.Players[(int)opponent].MaximumLeaderHealth);
    }

    /// <summary>
    /// Keeps the last safe perspective while presenting the neutral public
    /// projection during the two-frame resolving interval.
    /// </summary>
    public void RenderPublic(HotseatPublicBoardView board)
    {
        ArgumentNullException.ThrowIfNull(board);
        EnsureBound();
        if (!_perspectiveViewer.HasValue || board.Players.Count != 2)
        {
            return;
        }

        PlayerId viewer = _perspectiveViewer.Value;
        PlayerId opponent = Other(viewer);
        BindPlayerStatus(
            own: true,
            viewer,
            board.ActivePlayer,
            board.Players[(int)viewer].LeaderHealth,
            board.Players[(int)viewer].MaximumLeaderHealth);
        BindPlayerStatus(
            own: false,
            opponent,
            board.ActivePlayer,
            board.Players[(int)opponent].LeaderHealth,
            board.Players[(int)opponent].MaximumLeaderHealth);
    }

    public MatchHudLayout ApplyLayout(
        Vector2 viewportSize,
        Control cardDetails,
        Control statusRail,
        Control phaseCapsule,
        Control interactionDock,
        Control endTurnButton)
    {
        ArgumentNullException.ThrowIfNull(cardDetails);
        ArgumentNullException.ThrowIfNull(statusRail);
        ArgumentNullException.ThrowIfNull(phaseCapsule);
        ArgumentNullException.ThrowIfNull(interactionDock);
        ArgumentNullException.ThrowIfNull(endTurnButton);

        MatchHudMetrics metrics = GlassHudTheme.MetricsFor(viewportSize);
        float width = Mathf.Max(viewportSize.X, GlassHudTheme.MinimumWidth);
        float height = Mathf.Max(viewportSize.Y, GlassHudTheme.MinimumHeight);
        float safeLeft = metrics.EdgeInset + metrics.DetailWidth + GlassHudTheme.CompactGap;
        float safeRight = width - metrics.EdgeInset - metrics.StatusWidth - GlassHudTheme.CompactGap;
        float safeCenter = (safeLeft + safeRight) * 0.5f;

        var detailsRect = new Rect2(
            metrics.EdgeInset,
            metrics.TopInset + 48.0f,
            metrics.DetailWidth,
            metrics.DetailHeight);
        if (cardDetails is CardDetailPanel detailsPanel)
        {
            detailsPanel.SetExpandedRect(detailsRect);
        }
        else
        {
            SetTopLeftRect(cardDetails, detailsRect);
        }
        SetTopLeftRect(
            statusRail,
            new Rect2(
                width - metrics.EdgeInset - metrics.StatusWidth,
                metrics.TopInset,
                metrics.StatusWidth,
                218.0f));
        SetTopLeftRect(
            phaseCapsule,
            new Rect2(safeCenter - 96.0f, metrics.TopInset, 192.0f, 38.0f));
        SetTopLeftRect(
            interactionDock,
            new Rect2(
                width - metrics.EdgeInset - metrics.DockWidth,
                metrics.TopInset + 228.0f,
                metrics.DockWidth,
                Mathf.Min(286.0f, height - 380.0f)));
        SetTopLeftRect(
            endTurnButton,
            new Rect2(
                width - metrics.EdgeInset - metrics.StatusWidth,
                height - metrics.EdgeInset - 56.0f,
                metrics.StatusWidth,
                48.0f));

        return new MatchHudLayout(
            cardDetails.GetRect(),
            statusRail.GetRect(),
            phaseCapsule.GetRect(),
            interactionDock.GetRect(),
            endTurnButton.GetRect());
    }

    private static void SetTopLeftRect(Control control, Rect2 rect)
    {
        control.AnchorLeft = 0.0f;
        control.AnchorTop = 0.0f;
        control.AnchorRight = 0.0f;
        control.AnchorBottom = 0.0f;
        control.OffsetLeft = rect.Position.X;
        control.OffsetTop = rect.Position.Y;
        control.OffsetRight = rect.End.X;
        control.OffsetBottom = rect.End.Y;
    }

    private void BindPortraits(PlayerId viewer)
    {
        if (_root is null || _identity is null || _portraitCatalog is null)
        {
            return;
        }

        PlayerId opponent = Other(viewer);
        _root.GetNode<TextureRect>("%OwnLeaderPortrait").Texture =
            _portraitCatalog.LoadPortrait(_identity.ForPlayer(viewer).DeckId);
        _root.GetNode<TextureRect>("%OpponentLeaderPortrait").Texture =
            _portraitCatalog.LoadPortrait(_identity.ForPlayer(opponent).DeckId);
    }

    private void BindPlayerStatus(
        bool own,
        PlayerId player,
        PlayerId activePlayer,
        int health,
        int maximumHealth)
    {
        Control root = EnsureBound();
        string prefix = own ? "Own" : "Opponent";
        Label seat = root.GetNode<Label>($"%{prefix}SeatLabel");
        Label active = root.GetNode<Label>($"%{prefix}ActiveIndicator");
        ProgressBar healthBar = root.GetNode<ProgressBar>($"%{prefix}HealthBar");
        Control pod = root.GetNode<Control>(own ? "%OwnStatusPod" : "%OpponentStatusPod");

        seat.Text = FormatCompactSeat(own, player, health, maximumHealth);
        bool isActive = player == activePlayer;
        active.Visible = isActive;
        pod.Modulate = isActive
            ? new Color(1.0f, 1.0f, 1.0f, 1.0f)
            : new Color(0.78f, 0.84f, 0.9f, 0.88f);
        int safeMaximum = Math.Max(maximumHealth, 1);
        healthBar.MaxValue = safeMaximum;
        healthBar.Value = Math.Clamp(health, 0, safeMaximum);
    }

    internal static string FormatCompactSeat(
        bool own,
        PlayerId player,
        int health,
        int maximumHealth) =>
        $"{(own ? "己方" : "对手")}·{PlayerLabel(player)} ♥{health}/{maximumHealth}";

    internal static string FormatCompactResources(
        int currentPp,
        int ppCapacity,
        int cracks,
        int evolutionEnergy) =>
        $"PP {currentPp}/{ppCapacity}  裂{cracks}  进{evolutionEnergy}";

    internal MatchHudMaximumStateEvidence MeasureMaximumStateForCi()
    {
        Control root = EnsureBound();
        Control rail = root.GetNode<Control>("%BattlefieldControlRail");
        Rect2 railRect = rail.GetGlobalRect();
        bool podsInsideRail = true;
        bool labelsSingleLine = true;
        bool labelsFit = true;
        bool valuesPresent = true;
        bool healthBarsMaxed = true;

        foreach (string prefix in new[] { "Opponent", "Own" })
        {
            Control pod = root.GetNode<Control>($"%{prefix}StatusPod");
            Label seat = root.GetNode<Label>($"%{prefix}SeatLabel");
            Label resources = root.GetNode<Label>($"%{prefix}ResourceLabel");
            ProgressBar health = root.GetNode<ProgressBar>($"%{prefix}HealthBar");
            Rect2 podRect = pod.GetGlobalRect();

            podsInsideRail &= railRect.Grow(1.0f).Encloses(podRect);
            labelsSingleLine &= seat.GetLineCount() == 1 &&
                                seat.GetVisibleLineCount() == 1 &&
                                resources.GetLineCount() == 1 &&
                                resources.GetVisibleLineCount() == 1;
            labelsFit &= TextFits(seat) && TextFits(resources);
            valuesPresent &= seat.Text.Contains("25/25", StringComparison.Ordinal) &&
                             resources.Text == "PP 10/10  裂99  进99";
            healthBarsMaxed &= Math.Abs(health.MaxValue - 25.0) < 0.01 &&
                               Math.Abs(health.Value - 25.0) < 0.01;
        }

        return new MatchHudMaximumStateEvidence(
            podsInsideRail,
            labelsSingleLine,
            labelsFit,
            valuesPresent,
            healthBarsMaxed);

        static bool TextFits(Label label)
        {
            Font font = label.GetThemeFont("font");
            int fontSize = label.GetThemeFontSize("font_size");
            float textWidth = font.GetStringSize(
                label.Text,
                HorizontalAlignment.Left,
                -1.0f,
                fontSize).X;
            return textWidth <= label.Size.X + 0.5f;
        }
    }

    private Control EnsureBound() => _root ??
        throw new InvalidOperationException("MatchHudPresenter must be bound before rendering.");

    private static PlayerId Other(PlayerId player) => player switch
    {
        PlayerId.Player0 => PlayerId.Player1,
        PlayerId.Player1 => PlayerId.Player0,
        _ => throw new ArgumentOutOfRangeException(nameof(player), player, "Unknown player."),
    };

    private static string PlayerLabel(PlayerId player) => player switch
    {
        PlayerId.Player0 => "P0",
        PlayerId.Player1 => "P1",
        _ => "P?",
    };
}

public readonly record struct MatchHudLayout(
    Rect2 CardDetails,
    Rect2 StatusRail,
    Rect2 PhaseCapsule,
    Rect2 InteractionDock,
    Rect2 EndTurnButton)
{
    public bool HasNoPrimaryOverlap =>
        !CardDetails.Intersects(StatusRail) &&
        !CardDetails.Intersects(InteractionDock) &&
        !PhaseCapsule.Intersects(StatusRail) &&
        !EndTurnButton.Intersects(InteractionDock);
}

internal readonly record struct MatchHudMaximumStateEvidence(
    bool PodsInsideRail,
    bool LabelsSingleLine,
    bool LabelsFit,
    bool ValuesPresent,
    bool HealthBarsMaxed)
{
    internal bool IsValid =>
        PodsInsideRail && LabelsSingleLine && LabelsFit && ValuesPresent && HealthBarsMaxed;
}
