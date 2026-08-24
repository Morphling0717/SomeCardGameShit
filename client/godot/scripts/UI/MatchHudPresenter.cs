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

        var detailsRect = new Rect2(
            metrics.EdgeInset,
            metrics.TopInset + 54.0f,
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
                248.0f));
        SetTopLeftRect(
            phaseCapsule,
            new Rect2(width * 0.5f - 112.0f, metrics.TopInset, 224.0f, 44.0f));
        SetTopLeftRect(
            interactionDock,
            new Rect2(
                width - metrics.EdgeInset - metrics.DockWidth,
                284.0f,
                metrics.DockWidth,
                Mathf.Min(300.0f, height - 410.0f)));
        SetTopLeftRect(
            endTurnButton,
            new Rect2(
                width - metrics.EdgeInset - 196.0f,
                height - metrics.EdgeInset - 64.0f,
                196.0f,
                52.0f));

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

        seat.Text = $"{(own ? "己方" : "对手")} · {PlayerLabel(player)}";
        bool isActive = player == activePlayer;
        active.Visible = isActive;
        pod.Modulate = isActive
            ? new Color(1.0f, 1.0f, 1.0f, 1.0f)
            : new Color(0.78f, 0.84f, 0.9f, 0.88f);
        int safeMaximum = Math.Max(maximumHealth, 1);
        healthBar.MaxValue = safeMaximum;
        healthBar.Value = Math.Clamp(health, 0, safeMaximum);
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
        PlayerId.Player0 => "玩家 0",
        PlayerId.Player1 => "玩家 1",
        _ => "玩家 ?",
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
