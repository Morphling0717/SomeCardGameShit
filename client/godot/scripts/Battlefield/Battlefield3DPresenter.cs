// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;
using Scgs.GodotClient.Presentation;
using Scgs.Hotseat;

namespace Scgs.GodotClient.Battlefield;

public sealed partial class Battlefield3DPresenter : Node3D, IBattlefieldPresenter
{
    private const int MaximumRenderedHandCards = BattlefieldPerspective.MaximumHandCards;
    private readonly List<CardActor3D> _cardPool = [];
    private readonly List<SlotActor3D> _slotPool = [];
    private readonly Dictionary<BattlefieldSurfaceRef, IBattlefieldPickTarget> _surfaceActors = [];
    private readonly List<BattlefieldSurfaceRef> _keyboardSurfaces = [];
    private BattlefieldCameraRig _camera = null!;
    private BattlefieldRaycastInput _raycastInput = null!;
    private BattlefieldTargetArrow3D _targetArrow = null!;
    private BattlefieldPlacementGhost3D _placementGhost = null!;
    private int _cardCursor;
    private int _slotCursor;
    private BattlefieldSurfaceRef? _targetingSource;
    private bool _built;
    private bool _privateRender;
    private float _leftViewportInset;
    private float _rightViewportInset;
    private Viewport? _subscribedViewport;
    private Control? _leftObstruction;
    private Control? _rightObstruction;
    private float _obstructionPadding;
    private int _keyboardSurfaceIndex = -1;
    private BattlefieldSurfaceRef? _dragVisualSource;

    public event EventHandler<BattlefieldSurfaceGestureEventArgs>? SurfaceGestureRequested;

    public event EventHandler<BattlefieldSurfaceHoverEventArgs>? SurfaceHovered;

    public event EventHandler<BattlefieldSurfaceHoverEventArgs>? SurfaceSecondaryRequested;

    public event EventHandler? ProjectionChanged;

    public bool InputEnabled => _raycastInput?.InputEnabled == true;

    public ulong Revision { get; private set; }

    public PlayerId PerspectiveViewer { get; private set; }

    public int CiCardPoolSize => _cardPool.Count;

    public int CiActiveCardCount => _cardPool.Count(actor => actor.Visible);

    public int CiSlotPoolSize => _slotPool.Count;

    public int CiActiveSlotCount => _slotPool.Count(actor => actor.Visible);

    public int CiStableSurfaceLookupCount =>
        _surfaceActors.Keys.Count(surface => surface.InstanceId.HasValue);

    public int CiCollisionEnabledCount =>
        _cardPool.Count(actor => actor.CollisionEnabled) +
        _slotPool.Count(actor => actor.CollisionEnabled);

    public bool CiHasActiveDrag => _raycastInput?.HasActiveDrag == true;

    public float CiCameraZoom => _camera?.Zoom ?? 1.0f;

    public float CiCameraPitch => _camera?.PitchDegrees ?? BattlefieldPerspective.CameraPitchDegrees;

    public float CiCameraHorizontalOffset => _camera?.HOffset ?? 0.0f;

    public PlayerId CiPerspectiveViewer => PerspectiveViewer;

    public BattlefieldRaycastInput CiRaycastInput => _raycastInput;

    public BattlefieldSurfaceRef? CiKeyboardFocusedSurface =>
        _keyboardSurfaceIndex >= 0 && _keyboardSurfaceIndex < _keyboardSurfaces.Count
            ? _keyboardSurfaces[_keyboardSurfaceIndex]
            : null;

    public bool CiTargetArrowVisible => _targetArrow?.Visible == true;

    public bool CiPlacementGhostVisible => _placementGhost?.Visible == true;

    public int CiOutlineVisibleCount =>
        _cardPool.Count(actor => actor.OutlineVisible) +
        _slotPool.Count(actor => actor.OutlineVisible);

    public int CiTripleAffordanceSurfaceCount =>
        _cardPool.Count(actor => actor.HasTripleAffordance) +
        _slotPool.Count(actor => actor.HasTripleAffordance);

    public bool CiSawTripleAffordance { get; private set; }

    public bool CiSetCameraZoom(float zoom)
    {
        EnsureBuilt();
        return _camera.SetZoom(zoom);
    }

    public override void _Ready()
    {
        EnsureBuilt();
        _subscribedViewport = GetViewport();
        _subscribedViewport.SizeChanged += OnViewportSizeChanged;
        VisibilityChanged += OnVisibilityChanged;
        OnVisibilityChanged();
        SetInputEnabled(false);
    }

    public override void _ExitTree()
    {
        ClearObstructionSubscriptions();
        if (_subscribedViewport is not null)
        {
            _subscribedViewport.SizeChanged -= OnViewportSizeChanged;
            _subscribedViewport = null;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!InputEnabled || @event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        bool handled = key.Keycode switch
        {
            Key.Tab => FocusNextSurface(key.ShiftPressed ? -1 : 1),
            Key.Left or Key.Up => FocusNextSurface(-1),
            Key.Right or Key.Down => FocusNextSurface(1),
            Key.Enter or Key.KpEnter or Key.Space => ActivateFocusedSurface(),
            _ => false,
        };
        if (handled)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    public void RenderPrivate(MatchView view, HotseatInteractionContext interaction)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(interaction);
        if (view.Players.Length != 2 || interaction.Revision != view.Revision)
        {
            throw new ArgumentException("Private battlefield input must describe one two-player revision.");
        }

        EnsureBuilt();
        ResetForRender();
        Revision = view.Revision;
        PerspectiveViewer = view.Viewer;
        _privateRender = true;
        RenderPrivatePlayer(FindPlayer(view, PlayerId.Player0), view.Viewer);
        RenderPrivatePlayer(FindPlayer(view, PlayerId.Player1), view.Viewer);
        RenderCastZone(view.Viewer, interactive: true);
        FinishRender();
    }

    public void RenderPublic(HotseatPublicBoardView board, PlayerId perspectiveViewer)
    {
        ArgumentNullException.ThrowIfNull(board);
        ValidatePlayer(perspectiveViewer);
        if (board.Players.Count != 2)
        {
            throw new ArgumentException("Public battlefield input must contain two players.", nameof(board));
        }

        EnsureBuilt();
        ClearSensitive();
        Revision = board.Revision;
        PerspectiveViewer = perspectiveViewer;
        _privateRender = false;
        RenderPublicPlayer(FindPlayer(board, PlayerId.Player0), perspectiveViewer);
        RenderPublicPlayer(FindPlayer(board, PlayerId.Player1), perspectiveViewer);
        RenderCastZone(perspectiveViewer, interactive: false);
        FinishRender();
        SetInputEnabled(false);
    }

    public bool TryConfigureInteraction(
        ulong revision,
        IEnumerable<BattlefieldInteractionSurface> surfaces,
        BattlefieldSurfaceRef? selected = null,
        BattlefieldSurfaceRef? targetingSource = null)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        EnsureBuilt();
        ClearInteractionVisuals();
        if (!_privateRender || revision != Revision)
        {
            return false;
        }

        BattlefieldInteractionSurface[] configured = surfaces
            .GroupBy(item => item.Surface)
            .Select(group => group.MaxBy(item => item.Highlight))
            .ToArray();
        foreach (BattlefieldInteractionSurface item in configured)
        {
            if (!_surfaceActors.TryGetValue(item.Surface, out IBattlefieldPickTarget? actor))
            {
                continue;
            }

            SetActorHighlight(actor, item.Highlight);
        }

        OverrideOccupiedCardsForLegalSlots();
        EnableUtilitySurfaces();
        _keyboardSurfaces.AddRange(configured
            .Select(item => item.Surface)
            .Where(surface => TryResolveSurfaceActor(surface, out IBattlefieldPickTarget? actor) &&
                              actor.CanActivate));
        _keyboardSurfaces.AddRange(_surfaceActors
            .Where(item => item.Key.Kind == BattlefieldSurfaceKind.StandbyPile &&
                           item.Value.CanActivate)
            .Select(item => item.Key));

        if (selected is { } selectedSurface &&
            TryResolveSurfaceActor(selectedSurface, out IBattlefieldPickTarget? selectedActor))
        {
            SetActorHighlight(selectedActor, BattlefieldHighlightKind.Selected);
        }

        _targetingSource = targetingSource;
        if (targetingSource is not null &&
            TryGetWorldAnchor(targetingSource.Value, out Vector3 sourceAnchor))
        {
            _targetArrow.ShowBetween(sourceAnchor, sourceAnchor + (Vector3.Forward * 0.16f));
        }

        CiSawTripleAffordance |= CiTripleAffordanceSurfaceCount > 0;

        return true;
    }

    public void SetInputEnabled(bool enabled)
    {
        EnsureBuilt();
        bool wasEnabled = InputEnabled;
        _raycastInput.SetInputEnabled(enabled && _privateRender && IsVisibleInTree());
        if (InputEnabled && !wasEnabled)
        {
            GetViewport().GuiReleaseFocus();
        }

        if (!InputEnabled)
        {
            _targetingSource = null;
            _dragVisualSource = null;
            _targetArrow.Stop();
            _placementGhost.Stop();
        }
    }

    public void SetViewportInsets(float leftPixels, float rightPixels)
    {
        EnsureBuilt();
        ClearObstructionSubscriptions();
        _leftViewportInset = leftPixels;
        _rightViewportInset = rightPixels;
        float width = GetViewport()?.GetVisibleRect().Size.X ?? 1600.0f;
        _camera.SetViewportInsets(leftPixels, rightPixels, width);
    }

    public void SetViewportObstructions(
        Control? leftControl,
        Control? rightControl,
        float paddingPixels = 16.0f)
    {
        if (!float.IsFinite(paddingPixels) || paddingPixels < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(paddingPixels));
        }

        EnsureBuilt();
        ClearObstructionSubscriptions();
        _leftObstruction = leftControl;
        _rightObstruction = rightControl;
        _obstructionPadding = paddingPixels;
        SubscribeObstruction(_leftObstruction);
        if (!ReferenceEquals(_rightObstruction, _leftObstruction))
        {
            SubscribeObstruction(_rightObstruction);
        }

        ApplyObstructionInsets();
    }

    public void SetGuiBlocker(Func<Vector2, bool>? guiBlocksPointer)
    {
        EnsureBuilt();
        _raycastInput.GuiBlocksPointer = guiBlocksPointer;
    }

    public bool TryGetWorldAnchor(BattlefieldSurfaceRef surface, out Vector3 anchor)
    {
        if (TryResolveSurfaceActor(surface, out IBattlefieldPickTarget? actor))
        {
            anchor = actor.WorldAnchor;
            return true;
        }

        anchor = default;
        return false;
    }

    public bool TryGetSurfaceAtScreen(
        Vector2 screenPosition,
        out BattlefieldSurfaceRef surface)
    {
        if (_privateRender)
        {
            return _raycastInput.TryPickSurface(screenPosition, out surface);
        }

        surface = default;
        return false;
    }

    public bool FocusNextSurface(int direction)
    {
        if (!InputEnabled || direction == 0 || _keyboardSurfaces.Count == 0)
        {
            return false;
        }

        SetKeyboardActorHovered(hovered: false);
        int normalizedDirection = direction < 0 ? -1 : 1;
        _keyboardSurfaceIndex = _keyboardSurfaceIndex < 0
            ? normalizedDirection > 0 ? 0 : _keyboardSurfaces.Count - 1
            : (_keyboardSurfaceIndex + normalizedDirection + _keyboardSurfaces.Count) %
              _keyboardSurfaces.Count;
        SetKeyboardActorHovered(hovered: true);
        return true;
    }

    public bool ActivateFocusedSurface()
    {
        BattlefieldSurfaceRef? focused = CiKeyboardFocusedSurface;
        if (!InputEnabled || focused is null)
        {
            return false;
        }

        ClearKeyboardFocus();
        OnSurfaceClicked(Revision, focused.Value);
        return true;
    }

    public void ClearSensitive()
    {
        EnsureBuilt();
        SetInputEnabled(false);
        _raycastInput.CancelTransient();
        _targetArrow.Stop();
        _placementGhost.Stop();
        _targetingSource = null;
        _dragVisualSource = null;
        ClearKeyboardFocus();
        _keyboardSurfaces.Clear();
        _surfaceActors.Clear();
        foreach (CardActor3D actor in _cardPool)
        {
            actor.ClearSensitive();
        }

        foreach (SlotActor3D actor in _slotPool)
        {
            actor.ClearSensitive();
        }

        _cardCursor = 0;
        _slotCursor = 0;
        Revision = 0;
        _privateRender = false;
    }

    public int CiCountForbiddenToken(string token) =>
        _cardPool.Sum(actor => actor.CountForbiddenToken(token)) +
        _slotPool.Sum(actor => actor.CountForbiddenToken(token));

    public bool CiArmPrivacySentinel(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        CardActor3D? actor = _cardPool.FirstOrDefault(item =>
            item.Visible && item.Surface.HasValue && item.CardPresentation is not null);
        actor ??= _cardPool.FirstOrDefault(item => item.Visible && item.CardPresentation is not null);
        if (!_privateRender || actor is null)
        {
            return false;
        }

        actor.CiArmPrivacySentinel(token);
        if (actor.Surface.HasValue)
        {
            _raycastInput.CiArmDragToken(actor, Revision);
        }

        return true;
    }

    public bool CiTryGetScreenAnchor(BattlefieldSurfaceRef surface, out Vector2 screenAnchor)
    {
        if (TryGetWorldAnchor(surface, out Vector3 worldAnchor) &&
            !_camera.IsPositionBehind(worldAnchor))
        {
            screenAnchor = _camera.UnprojectPosition(worldAnchor);
            return true;
        }

        screenAnchor = default;
        return false;
    }

    public bool CiValidateReadableLayout(MatchView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!_privateRender || view.Revision != Revision || view.Viewer != PerspectiveViewer ||
            !BattlefieldPerspective.ValidateStaticSpacing(view.Viewer))
        {
            return false;
        }

        PlayerView own = FindPlayer(view, view.Viewer);
        PlayerId opponentId = view.Viewer == PlayerId.Player0
            ? PlayerId.Player1
            : PlayerId.Player0;
        PlayerView opponent = FindPlayer(view, opponentId);
        CardActor3D[] ownHand = _cardPool
            .Where(actor => actor.Visible &&
                            actor.CardPresentation is
                            {
                                Zone: Zone.Hand,
                                KnownIdentity: true,
                            } presentation &&
                            presentation.Controller == view.Viewer)
            .OrderBy(actor => actor.GlobalPosition.X)
            .ToArray();
        CardActor3D[] opposingHand = _cardPool
            .Where(actor => actor.Visible &&
                            actor.CardPresentation is
                            {
                                Zone: Zone.Hand,
                                KnownIdentity: false,
                            } presentation &&
                            presentation.Controller == opponentId)
            .ToArray();

        if (ownHand.Length != Math.Min(own.Hand.Length, MaximumRenderedHandCards) ||
            opposingHand.Length != Math.Min(CheckedDisplayCount(opponent.HandCount), MaximumRenderedHandCards) ||
            ownHand.Any(actor => actor.CiLayout != BattlefieldCardLayout.NearHand ||
                                 actor.CiFaceLineCount is < 1 or > 2 ||
                                 actor.StateText.Contains("可选", StringComparison.Ordinal) ||
                                 actor.StateText.Contains("目标", StringComparison.Ordinal) ||
                                 actor.StateText.Contains("已选", StringComparison.Ordinal)) ||
            opposingHand.Any(actor => actor.CiLayout != BattlefieldCardLayout.FarHand ||
                                      actor.DisplayText.Length != 0))
        {
            return false;
        }

        for (int index = 1; index < ownHand.Length; ++index)
        {
            CardActor3D left = ownHand[index - 1];
            CardActor3D right = ownHand[index];
            float requiredSeparation =
                ((left.CiFaceLabelWorldWidth + right.CiFaceLabelWorldWidth) / 2.0f) + 0.04f;
            if (right.GlobalPosition.X - left.GlobalPosition.X < requiredSeparation)
            {
                return false;
            }
        }

        foreach (CardView card in own.Hand.Where(card => card.Kind == CardKind.Unit &&
                                                         card.Definition is not null))
        {
            CardActor3D? actor = ownHand.FirstOrDefault(candidate =>
                candidate.CardPresentation?.InstanceId == card.InstanceId);
            (int attack, int health) = CardPresentation.GetDisplayedUnitStats(card);
            if (actor?.CardPresentation is not { } presentation ||
                presentation.Attack != attack || presentation.Health != health ||
                !actor.DisplayText.Contains($"{attack}/{health}", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void RenderPrivatePlayer(PlayerView player, PlayerId viewer)
    {
        RenderPrivateField(player, viewer, Zone.Unit, player.Units);
        RenderPrivateField(player, viewer, Zone.Tactic, player.Tactics);
        RenderLeader(player.Player, viewer, player.LeaderHealth, player.MaximumLeaderHealth, interactive: true);

        int handCount = player.Player == viewer
            ? Math.Min(player.Hand.Length, MaximumRenderedHandCards)
            : Math.Min(CheckedDisplayCount(player.HandCount), MaximumRenderedHandCards);
        for (int index = 0; index < handCount; ++index)
        {
            CardActor3D actor = RentCard();
            Transform3D transform = BattlefieldPerspective.HandTransform(
                player.Player,
                viewer,
                index,
                handCount);
            if (player.Player == viewer && index < player.Hand.Length)
            {
                CardView card = player.Hand[index];
                BattlefieldSurfaceRef? surface = card.InstanceId is { } id
                    ? new BattlefieldSurfaceRef(BattlefieldSurfaceKind.HandCard, player.Player, index, id)
                    : null;
                actor.BindPrivate(
                    card,
                    surface,
                    transform,
                    BattlefieldCardLayout.NearHand);
                Register(surface, actor);
            }
            else
            {
                actor.BindHidden(
                    player.Player,
                    Zone.Hand,
                    transform,
                    BattlefieldCardLayout.FarHand);
            }
        }

        RenderPrivateStandby(player, viewer);
        RenderPile(player.Player, viewer, Zone.Deck, "牌组", player.DeckCount, hidden: true);
        RenderPrivateOpenPile(player, viewer, Zone.Graveyard, "墓地", player.Graveyard);
        RenderPrivateOpenPile(player, viewer, Zone.Archive, "封存", player.Archive);
    }

    private void RenderPrivateField(
        PlayerView player,
        PlayerId viewer,
        Zone zone,
        IReadOnlyList<CardView?> cards)
    {
        int slotCount = zone == Zone.Unit
            ? BattlefieldPerspective.UnitSlotCount
            : BattlefieldPerspective.TacticSlotCount;
        for (int slot = 0; slot < slotCount; ++slot)
        {
            CardView? card = slot < cards.Count ? cards[slot] : null;
            Transform3D transform = zone == Zone.Unit
                ? BattlefieldPerspective.UnitTransform(player.Player, viewer, slot)
                : BattlefieldPerspective.TacticTransform(player.Player, viewer, slot);
            BattlefieldSurfaceKind emptyKind = zone == Zone.Unit
                ? BattlefieldSurfaceKind.UnitSlot
                : BattlefieldSurfaceKind.TacticSlot;
            BattlefieldSurfaceRef slotSurface = new(emptyKind, player.Player, slot);
            SlotActor3D slotActor = RentSlot();
            slotActor.Bind(transform, zone == Zone.Unit ? "单位位" : "策略位", slotSurface);
            Register(slotSurface, slotActor);
            if (card is null)
            {
                continue;
            }

            BattlefieldSurfaceKind occupiedKind = zone == Zone.Unit
                ? BattlefieldSurfaceKind.Unit
                : BattlefieldSurfaceKind.Tactic;
            BattlefieldSurfaceRef? cardSurface = card.InstanceId is { } id
                ? new BattlefieldSurfaceRef(occupiedKind, player.Player, slot, id)
                : null;
            CardActor3D cardActor = RentCard();
            cardActor.BindPrivate(card, cardSurface, transform);
            Register(cardSurface, cardActor);
        }
    }

    private void RenderPrivateStandby(PlayerView player, PlayerId viewer)
    {
        BattlefieldSurfaceRef surface = BattlefieldSurfaceRef.StandbyPile(player.Player);
        CardActor3D actor = RentCard();
        actor.BindPile(
            player.Player,
            Zone.Standby,
            "战备",
            (ulong)player.Standby.Length,
            BattlefieldPerspective.StandbyPileTransform(player.Player, viewer),
            hidden: true,
            surface: surface);
        Register(surface, actor);
    }

    private void RenderPrivateOpenPile(
        PlayerView player,
        PlayerId viewer,
        Zone zone,
        string title,
        IReadOnlyList<CardView> cards)
    {
        if (cards.Count == 0)
        {
            RenderEmptyPile(player.Player, viewer, zone, title);
            return;
        }

        CardActor3D actor = RentCard();
        actor.BindPile(
            player.Player,
            zone,
            title,
            (ulong)cards.Count,
            BattlefieldPerspective.PileTransform(player.Player, viewer, zone),
            hidden: false);
    }

    private void RenderPublicPlayer(HotseatPublicPlayerView player, PlayerId viewer)
    {
        RenderPublicField(player, viewer, Zone.Unit, player.Units);
        RenderPublicField(player, viewer, Zone.Tactic, player.Tactics);
        RenderLeader(player.Player, viewer, player.LeaderHealth, player.MaximumLeaderHealth, interactive: false);

        int handCount = Math.Min(CheckedDisplayCount(player.HandCount), MaximumRenderedHandCards);
        for (int index = 0; index < handCount; ++index)
        {
            RentCard().BindHidden(
                player.Player,
                Zone.Hand,
                BattlefieldPerspective.HandTransform(player.Player, viewer, index, handCount),
                BattlefieldPerspective.IsNear(player.Player, viewer)
                    ? BattlefieldCardLayout.NearHand
                    : BattlefieldCardLayout.FarHand);
        }

        RentCard().BindPile(
            player.Player,
            Zone.Standby,
            "战备",
            (ulong)player.Standby.Count,
            BattlefieldPerspective.StandbyPileTransform(player.Player, viewer),
            hidden: true);

        RenderPile(player.Player, viewer, Zone.Deck, "牌组", player.DeckCount, hidden: true);
        RenderPublicOpenPile(player.Player, viewer, Zone.Graveyard, "墓地", player.Graveyard);
        RenderPublicOpenPile(player.Player, viewer, Zone.Archive, "封存", player.Archive);
    }

    private void RenderPublicField(
        HotseatPublicPlayerView player,
        PlayerId viewer,
        Zone zone,
        IReadOnlyList<HotseatPublicCardView?> cards)
    {
        int slotCount = zone == Zone.Unit
            ? BattlefieldPerspective.UnitSlotCount
            : BattlefieldPerspective.TacticSlotCount;
        for (int slot = 0; slot < slotCount; ++slot)
        {
            Transform3D transform = zone == Zone.Unit
                ? BattlefieldPerspective.UnitTransform(player.Player, viewer, slot)
                : BattlefieldPerspective.TacticTransform(player.Player, viewer, slot);
            RentSlot().Bind(transform, zone == Zone.Unit ? "单位位" : "策略位", surface: null);
            HotseatPublicCardView? card = slot < cards.Count ? cards[slot] : null;
            if (card is not null)
            {
                RentCard().BindPublic(card, transform);
            }
        }
    }

    private void RenderPublicOpenPile(
        PlayerId player,
        PlayerId viewer,
        Zone zone,
        string title,
        IReadOnlyList<HotseatPublicCardView> cards)
    {
        if (cards.Count == 0)
        {
            RenderEmptyPile(player, viewer, zone, title);
            return;
        }

        CardActor3D actor = RentCard();
        actor.BindPile(
            player,
            zone,
            title,
            (ulong)cards.Count,
            BattlefieldPerspective.PileTransform(player, viewer, zone),
            hidden: false);
    }

    private void RenderLeader(
        PlayerId player,
        PlayerId viewer,
        int health,
        int maximumHealth,
        bool interactive)
    {
        BattlefieldSurfaceRef? surface = interactive
            ? new BattlefieldSurfaceRef(BattlefieldSurfaceKind.Leader, player)
            : null;
        SlotActor3D actor = RentSlot();
        actor.Bind(
            BattlefieldPerspective.LeaderTransform(player, viewer),
            $"主战者\n{health}/{maximumHealth}",
            surface);
        Register(surface, actor);
    }

    private void RenderCastZone(PlayerId viewer, bool interactive)
    {
        BattlefieldSurfaceRef? surface = interactive
            ? new BattlefieldSurfaceRef(BattlefieldSurfaceKind.CastZone)
            : null;
        SlotActor3D actor = RentSlot();
        actor.Bind(BattlefieldPerspective.CastZoneTransform(viewer), "施放区", surface);
        Register(surface, actor);
    }

    private void RenderPile(
        PlayerId player,
        PlayerId viewer,
        Zone zone,
        string title,
        ulong count,
        bool hidden)
    {
        if (count == 0)
        {
            RenderEmptyPile(player, viewer, zone, title);
            return;
        }

        RentCard().BindPile(
            player,
            zone,
            title,
            count,
            BattlefieldPerspective.PileTransform(player, viewer, zone),
            hidden);
    }

    private void RenderEmptyPile(
        PlayerId player,
        PlayerId viewer,
        Zone zone,
        string title)
    {
        RentSlot().Bind(
            BattlefieldPerspective.PileTransform(player, viewer, zone),
            $"{title}\n0",
            surface: null);
    }

    private CardActor3D RentCard()
    {
        if (_cardCursor == _cardPool.Count)
        {
            var actor = new CardActor3D { Name = $"CardActor{_cardPool.Count}" };
            _cardPool.Add(actor);
            AddChild(actor);
        }

        return _cardPool[_cardCursor++];
    }

    private SlotActor3D RentSlot()
    {
        if (_slotCursor == _slotPool.Count)
        {
            var actor = new SlotActor3D { Name = $"SlotActor{_slotPool.Count}" };
            _slotPool.Add(actor);
            AddChild(actor);
        }

        return _slotPool[_slotCursor++];
    }

    private void Register(BattlefieldSurfaceRef? surface, IBattlefieldPickTarget actor)
    {
        if (surface is { } value)
        {
            _surfaceActors[value] = actor;
        }
    }

    private bool TryResolveSurfaceActor(
        BattlefieldSurfaceRef surface,
        out IBattlefieldPickTarget actor)
    {
        if (_surfaceActors.TryGetValue(surface, out actor!))
        {
            return true;
        }

        if (surface.InstanceId is not { } instanceId)
        {
            actor = null!;
            return false;
        }

        foreach ((BattlefieldSurfaceRef key, IBattlefieldPickTarget value) in _surfaceActors)
        {
            if (key.Kind == surface.Kind && key.InstanceId == instanceId)
            {
                actor = value;
                return true;
            }
        }

        actor = null!;
        return false;
    }

    private static void SetActorHighlight(
        IBattlefieldPickTarget actor,
        BattlefieldHighlightKind highlight)
    {
        switch (actor)
        {
            case CardActor3D card:
                card.SetHighlight(highlight);
                break;
            case SlotActor3D slot:
                slot.SetHighlight(highlight);
                break;
        }
    }

    private void ResetForRender()
    {
        SetInputEnabled(false);
        _raycastInput.CancelTransient();
        _targetArrow.Stop();
        _placementGhost.Stop();
        _targetingSource = null;
        _dragVisualSource = null;
        ClearKeyboardFocus();
        _keyboardSurfaces.Clear();
        _surfaceActors.Clear();
        _cardCursor = 0;
        _slotCursor = 0;
    }

    private void FinishRender()
    {
        for (int index = _cardCursor; index < _cardPool.Count; ++index)
        {
            _cardPool[index].ClearSensitive();
        }

        for (int index = _slotCursor; index < _slotPool.Count; ++index)
        {
            _slotPool[index].ClearSensitive();
        }
    }

    private void ClearInteractionVisuals()
    {
        ClearKeyboardFocus();
        _keyboardSurfaces.Clear();
        foreach (IBattlefieldPickTarget actor in _surfaceActors.Values.Distinct())
        {
            if (actor is CardActor3D card)
            {
                card.RestoreBoundSurface();
            }

            SetActorHighlight(actor, BattlefieldHighlightKind.None);
        }

        _targetingSource = null;
        _dragVisualSource = null;
        _targetArrow.Stop();
        _placementGhost.Stop();
    }

    private void OverrideOccupiedCardsForLegalSlots()
    {
        foreach ((BattlefieldSurfaceRef surface, IBattlefieldPickTarget actor) in _surfaceActors)
        {
            if (actor is not SlotActor3D { CanActivate: true } ||
                surface.Kind is not (BattlefieldSurfaceKind.UnitSlot or
                    BattlefieldSurfaceKind.TacticSlot) ||
                !surface.Player.HasValue || !surface.Index.HasValue)
            {
                continue;
            }

            BattlefieldSurfaceKind occupiedKind = surface.Kind == BattlefieldSurfaceKind.UnitSlot
                ? BattlefieldSurfaceKind.Unit
                : BattlefieldSurfaceKind.Tactic;
            BattlefieldSurfaceRef? occupiedSurface = _surfaceActors.Keys
                .Where(candidate =>
                    candidate.Kind == occupiedKind &&
                    candidate.Player == surface.Player &&
                    candidate.Index == surface.Index &&
                    candidate.InstanceId.HasValue)
                .Select(candidate => (BattlefieldSurfaceRef?)candidate)
                .FirstOrDefault();
            if (occupiedSurface is not { } value ||
                !_surfaceActors.TryGetValue(value, out IBattlefieldPickTarget? occupiedActor) ||
                occupiedActor is not CardActor3D card)
            {
                continue;
            }

            card.OverrideInteractionSurface(surface);
            card.SetHighlight(BattlefieldHighlightKind.Destination);
        }
    }

    private void EnableUtilitySurfaces()
    {
        foreach ((BattlefieldSurfaceRef surface, IBattlefieldPickTarget actor) in _surfaceActors)
        {
            if (surface.Kind == BattlefieldSurfaceKind.StandbyPile &&
                actor is CardActor3D card)
            {
                card.SetUtilityInteractive("▣ 查看");
            }
        }
    }

    private void EnsureBuilt()
    {
        if (_built)
        {
            return;
        }

        _built = true;
        BuildTable();

        _camera = new BattlefieldCameraRig { Name = "BattlefieldCamera" };
        AddChild(_camera);
        _camera.ProjectionChanged += () => ProjectionChanged?.Invoke(this, EventArgs.Empty);

        _raycastInput = new BattlefieldRaycastInput { Name = "RaycastInput" };
        AddChild(_raycastInput);
        _raycastInput.Configure(_camera, () => Revision);
        _raycastInput.Clicked += OnSurfaceClicked;
        _raycastInput.DragStarted += OnSurfaceDragStarted;
        _raycastInput.DragCompleted += OnSurfaceDragCompleted;
        _raycastInput.HoverChanged += args => SurfaceHovered?.Invoke(this, args);
        _raycastInput.SecondaryClicked += args => SurfaceSecondaryRequested?.Invoke(this, args);
        _raycastInput.PointerWorldChanged += OnPointerWorldChanged;
        _raycastInput.DragCancelled += () =>
        {
            _targetingSource = null;
            _dragVisualSource = null;
            _targetArrow.Stop();
            _placementGhost.Stop();
        };

        _targetArrow = new BattlefieldTargetArrow3D { Name = "TargetArrow" };
        AddChild(_targetArrow);

        _placementGhost = new BattlefieldPlacementGhost3D { Name = "PlacementGhost" };
        AddChild(_placementGhost);
    }

    private void BuildTable()
    {
        var worldEnvironment = new WorldEnvironment
        {
            Name = "BattlefieldEnvironment",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("20272d"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("6f8796"),
                AmbientLightEnergy = 0.62f,
                ReflectedLightSource = Godot.Environment.ReflectionSource.Bg,
            },
        };
        AddChild(worldEnvironment);

        var table = new MeshInstance3D
        {
            Name = "Table",
            Mesh = new BoxMesh
            {
                Size = new Vector3(
                    BattlefieldPerspective.BoardWidth,
                    0.24f,
                    BattlefieldPerspective.BoardDepth),
            },
            Position = new Vector3(0.0f, -0.18f, 0.0f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color("132c36"),
                Metallic = 0.12f,
                Roughness = 0.82f,
            },
        };
        AddChild(table);

        var centerLine = new MeshInstance3D
        {
            Name = "CenterLine",
            Mesh = new BoxMesh
            {
                Size = new Vector3(BattlefieldPerspective.BoardWidth - 0.5f, 0.015f, 0.055f),
            },
            Position = new Vector3(0.0f, -0.045f, 0.0f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color("3d7b78"),
                EmissionEnabled = true,
                Emission = new Color("235f5d") * 0.4f,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(centerLine);

        var keyLight = new DirectionalLight3D
        {
            Name = "KeyLight",
            RotationDegrees = new Vector3(-58.0f, -25.0f, 0.0f),
            LightColor = new Color("d8f1ec"),
            LightEnergy = 1.25f,
            ShadowEnabled = true,
        };
        AddChild(keyLight);

        var fillLight = new OmniLight3D
        {
            Name = "FillLight",
            Position = new Vector3(0.0f, 7.0f, 1.0f),
            OmniRange = 23.0f,
            LightColor = new Color("87a9d8"),
            LightEnergy = 3.2f,
            ShadowEnabled = false,
        };
        AddChild(fillLight);
    }

    private void OnSurfaceClicked(ulong revision, BattlefieldSurfaceRef source)
    {
        _targetingSource = null;
        _dragVisualSource = null;
        _targetArrow.Stop();
        _placementGhost.Stop();
        SurfaceGestureRequested?.Invoke(
            this,
            new BattlefieldSurfaceGestureEventArgs(
                revision,
                BattlefieldSurfaceGesture.Click,
                source,
                null));
    }

    private void OnSurfaceDragCompleted(
        ulong revision,
        BattlefieldSurfaceRef source,
        BattlefieldSurfaceRef destination)
    {
        _targetingSource = null;
        _dragVisualSource = null;
        _targetArrow.Stop();
        _placementGhost.Stop();
        SurfaceGestureRequested?.Invoke(
            this,
            new BattlefieldSurfaceGestureEventArgs(
                revision,
                BattlefieldSurfaceGesture.Drag,
                source,
                destination));
    }

    private void OnSurfaceDragStarted(ulong revision, BattlefieldSurfaceRef source)
    {
        if (revision != Revision || !TryGetWorldAnchor(source, out Vector3 sourceAnchor))
        {
            return;
        }

        _dragVisualSource = source;
        if (source.Kind == BattlefieldSurfaceKind.Unit)
        {
            _targetingSource = source;
            _targetArrow.ShowBetween(sourceAnchor, sourceAnchor + (Vector3.Forward * 0.16f));
        }
        else if (source.Kind is BattlefieldSurfaceKind.HandCard or
                 BattlefieldSurfaceKind.StandbyCard)
        {
            _placementGhost.ShowAt(sourceAnchor, PerspectiveViewer);
        }
    }

    private void OnPointerWorldChanged(Vector3 pointer)
    {
        if (_targetingSource is { } surface &&
            TryGetWorldAnchor(surface, out Vector3 source))
        {
            _targetArrow.ShowBetween(source, pointer);
        }
        else if (_dragVisualSource is
        {
            Kind: BattlefieldSurfaceKind.HandCard or
                BattlefieldSurfaceKind.StandbyCard,
        })
        {
            _placementGhost.ShowAt(pointer, PerspectiveViewer);
        }
    }

    private void OnVisibilityChanged()
    {
        if (_camera is null)
        {
            return;
        }

        _camera.Current = IsVisibleInTree();
        if (!IsVisibleInTree())
        {
            SetInputEnabled(false);
        }
    }

    private void ClearKeyboardFocus()
    {
        SetKeyboardActorHovered(hovered: false);
        _keyboardSurfaceIndex = -1;
    }

    private void SetKeyboardActorHovered(bool hovered)
    {
        BattlefieldSurfaceRef? focused = CiKeyboardFocusedSurface;
        if (focused is { } surface &&
            TryResolveSurfaceActor(surface, out IBattlefieldPickTarget? actor))
        {
            actor.SetPointerHovered(hovered);
        }
    }

    private void OnViewportSizeChanged()
    {
        if (_camera is null || !GodotObject.IsInstanceValid(_camera))
        {
            return;
        }

        if (_leftObstruction is not null || _rightObstruction is not null)
        {
            ApplyObstructionInsets();
        }
        else
        {
            _camera.SetViewportInsets(
                _leftViewportInset,
                _rightViewportInset,
                GetViewport().GetVisibleRect().Size.X);
        }
    }

    private void ApplyObstructionInsets()
    {
        float viewportWidth = GetViewport().GetVisibleRect().Size.X;
        _leftViewportInset = ObstructionInset(
            _leftObstruction,
            viewportWidth,
            fromLeft: true,
            _obstructionPadding);
        _rightViewportInset = ObstructionInset(
            _rightObstruction,
            viewportWidth,
            fromLeft: false,
            _obstructionPadding);
        _camera.SetViewportInsets(_leftViewportInset, _rightViewportInset, viewportWidth);
    }

    private void SubscribeObstruction(Control? control)
    {
        if (control is null)
        {
            return;
        }

        control.ItemRectChanged += OnObstructionRectChanged;
        control.VisibilityChanged += OnObstructionRectChanged;
    }

    private void ClearObstructionSubscriptions()
    {
        UnsubscribeObstruction(_leftObstruction);
        if (!ReferenceEquals(_rightObstruction, _leftObstruction))
        {
            UnsubscribeObstruction(_rightObstruction);
        }

        _leftObstruction = null;
        _rightObstruction = null;
        _obstructionPadding = 0.0f;
    }

    private void UnsubscribeObstruction(Control? control)
    {
        if (control is null || !GodotObject.IsInstanceValid(control))
        {
            return;
        }

        control.ItemRectChanged -= OnObstructionRectChanged;
        control.VisibilityChanged -= OnObstructionRectChanged;
    }

    private void OnObstructionRectChanged() => ApplyObstructionInsets();

    private static float ObstructionInset(
        Control? control,
        float viewportWidth,
        bool fromLeft,
        float padding)
    {
        if (control is null || !GodotObject.IsInstanceValid(control) ||
            !control.IsVisibleInTree())
        {
            return 0.0f;
        }

        Rect2 rect = control.GetGlobalRect();
        return fromLeft
            ? Mathf.Clamp(rect.End.X + padding, 0.0f, viewportWidth)
            : Mathf.Clamp(viewportWidth - rect.Position.X + padding, 0.0f, viewportWidth);
    }

    private static PlayerView FindPlayer(MatchView view, PlayerId player) =>
        view.Players.Single(item => item.Player == player);

    private static HotseatPublicPlayerView FindPlayer(
        HotseatPublicBoardView view,
        PlayerId player) => view.Players.Single(item => item.Player == player);

    private static int CheckedDisplayCount(ulong value) =>
        value > int.MaxValue ? int.MaxValue : (int)value;

    private static void ValidatePlayer(PlayerId player)
    {
        if (player is not (PlayerId.Player0 or PlayerId.Player1))
        {
            throw new ArgumentOutOfRangeException(nameof(player), player, "Unsupported player value.");
        }
    }
}
