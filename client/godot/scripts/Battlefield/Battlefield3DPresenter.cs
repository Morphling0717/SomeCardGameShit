// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;
using Scgs.GodotClient.Presentation;
using Scgs.GodotClient.Visual;
using Scgs.GodotClient.Visuals;
using Scgs.Hotseat;

namespace Scgs.GodotClient.Battlefield;

public sealed partial class Battlefield3DPresenter : Node3D, IBattlefieldPresenter
{
    private const int MaximumRenderedHandCards = BattlefieldPerspective.MaximumHandCards;
    private static readonly uint[] CiFixtureDefinitionIds =
    [
        1001, 1002, 1003, 1004, 1005, 1006, 1007, 1009, 1010, 1011,
    ];
    private readonly List<CardActor3D> _cardPool = [];
    private readonly List<SlotActor3D> _slotPool = [];
    private readonly List<HandActorBinding> _handBindings = [];
    private readonly List<HandCardPose> _nearHandPoses = [];
    private readonly List<HandCardPose> _farHandPoses = [];
    private readonly Dictionary<BattlefieldSurfaceRef, IBattlefieldPickTarget> _surfaceActors = [];
    private readonly List<BattlefieldSurfaceRef> _keyboardSurfaces = [];
    private readonly Dictionary<ulong, Godot.Environment?> _arenaEnvironments = [];
    private ICardVisualCatalog _visualCatalog = CardVisualCatalog.Shared;
    private BattlefieldVisualProfile _visualProfile = BattlefieldVisualProfile.AnimeV1;
    private ArenaVisualProfile _arenaVisualProfile = ArenaVisualProfile.AnimeV1;
    private MatchVisualIdentity _visualIdentity = MatchVisualIdentity.FromDecks(
        LeaderPortraitCatalog.MidrangeDeckId,
        LeaderPortraitCatalog.AdvanceDeckId);
    private Node3D? _animeArena;
    private BattlefieldCameraRig _camera = null!;
    private BattlefieldHandRig _handRig = null!;
    private BattlefieldRaycastInput _raycastInput = null!;
    private BattlefieldTargetArrow3D _targetArrow = null!;
    private BattlefieldPlacementGhost3D _placementGhost = null!;
    private Label3D _fxLabel = null!;
    private Tween? _fxTween;
    private ulong _lastFxSequence;
    private int _cardCursor;
    private int _slotCursor;
    private BattlefieldSurfaceRef? _targetingSource;
    private bool _built;
    private bool _privateRender;
    private BattlefieldViewportLayout _viewportLayout =
        BattlefieldViewportLayout.Product(new Vector2(1600.0f, 900.0f));
    private bool _usesProductViewportLayout = true;
    private Viewport? _subscribedViewport;
    private Control? _leftObstruction;
    private Control? _rightObstruction;
    private float _obstructionPadding;
    private int _keyboardSurfaceIndex = -1;
    private BattlefieldSurfaceRef? _dragVisualSource;
    private BattlefieldSurfaceRef? _selectedHandSurface;
    private CardActor3D? _hoveredHandActor;
    private MatchView? _lastPrivateView;
    private HotseatInteractionContext? _lastPrivateInteraction;
    private BattlefieldInteractionSurface[] _lastInteractionSurfaces = [];
    private BattlefieldSurfaceRef? _lastInteractionSelected;
    private BattlefieldSurfaceRef? _lastInteractionTargetingSource;
    private bool _lastRequestedInputEnabled;
    private bool _ciHandFixtureActive;

    public event EventHandler<BattlefieldSurfaceGestureEventArgs>? SurfaceGestureRequested;

    public event EventHandler<BattlefieldSurfaceHoverEventArgs>? SurfaceHovered;

    public event EventHandler<BattlefieldSurfaceHoverEventArgs>? SurfaceSecondaryRequested;

    public event EventHandler? ProjectionChanged;

    [Export]
    public BattlefieldVisualProfile VisualProfile
    {
        get => _visualProfile;
        set
        {
            ArenaVisualProfile.Resolve(value);
            if (_visualProfile == value)
            {
                return;
            }

            if (_built)
            {
                ConfigureVisualProfile(value);
                return;
            }

            _visualProfile = value;
            _arenaVisualProfile = ArenaVisualProfile.Resolve(value);
        }
    }

    public bool InputEnabled => _raycastInput?.InputEnabled == true;

    public ulong Revision { get; private set; }

    public PlayerId PerspectiveViewer { get; private set; }

    internal BattlefieldVisualProfile CiVisualProfile => _visualProfile;

    public int CiCardPoolSize => _cardPool.Count;

    public int CiActiveCardCount => _cardPool.Count(actor => actor.Visible);

    public int CiSlotPoolSize => _slotPool.Count;

    public int CiActiveSlotCount => _slotPool.Count(actor => actor.Visible);

    public Rect2 CiProjectedBoardRect
    {
        get
        {
            if (_camera is null || !GodotObject.IsInstanceValid(_camera))
            {
                return new Rect2();
            }

            float halfWidth = BattlefieldPerspective.BoardWidth / 2.0f;
            float halfDepth = BattlefieldPerspective.BoardDepth / 2.0f;
            Vector2[] projected =
            [
                _camera.UnprojectPosition(new Vector3(-halfWidth, 0.0f, -halfDepth)),
                _camera.UnprojectPosition(new Vector3(halfWidth, 0.0f, -halfDepth)),
                _camera.UnprojectPosition(new Vector3(-halfWidth, 0.0f, halfDepth)),
                _camera.UnprojectPosition(new Vector3(halfWidth, 0.0f, halfDepth)),
            ];
            float minX = projected.Min(point => point.X);
            float minY = projected.Min(point => point.Y);
            float maxX = projected.Max(point => point.X);
            float maxY = projected.Max(point => point.Y);
            return new Rect2(
                new Vector2(minX, minY),
                new Vector2(maxX - minX, maxY - minY));
        }
    }

    /// <summary>
    /// Screen bounds of the actual gameplay topology. R2 retains its historical
    /// framed-board contract; the open R3 arena measures slots, leaders and
    /// piles instead of inventing a perimeter which is no longer rendered.
    /// </summary>
    public Rect2 CiProjectedGameplayRect =>
        _visualProfile == BattlefieldVisualProfile.Gate4BR2
            ? CiProjectedBoardRect
            : ProjectGameplayTopologyRect();

    public Rect2 CiOwnHandScreenRect
    {
        get
        {
            if (_nearHandPoses.Count == 0)
            {
                return new Rect2();
            }

            float minX = _nearHandPoses.Min(pose => pose.ScreenBounds.Position.X);
            float minY = _nearHandPoses.Min(pose => pose.ScreenBounds.Position.Y);
            float maxX = _nearHandPoses.Max(pose => pose.ScreenBounds.End.X);
            float maxY = _nearHandPoses.Max(pose => pose.ScreenBounds.End.Y);
            // This intentionally remains unclamped: visual contracts must see
            // an off-screen hand instead of receiving a viewport-trimmed lie.
            return new Rect2(
                new Vector2(minX, minY),
                new Vector2(maxX - minX, maxY - minY));
        }
    }

    internal IReadOnlyList<HandCardPose> CiNearHandPoses => _nearHandPoses.ToArray();

    internal IReadOnlyList<HandCardPose> CiFarHandPoses => _farHandPoses.ToArray();

    internal string CiArenaProfile => _visualProfile switch
    {
        BattlefieldVisualProfile.Gate4BR2 => "gate4b-r2",
        BattlefieldVisualProfile.R3Candidate => "r3-candidate",
        BattlefieldVisualProfile.AnimeV1 => "anime-v1",
        _ => throw new InvalidOperationException("The battlefield visual profile is unknown."),
    };

    internal int CiHiddenHandCardCount =>
        _handBindings.Count(binding => !binding.Near && binding.Actor.Visible);

    internal bool CiHiddenHandUsesSharedBack =>
        CiHiddenHandCardCount > 0 &&
        _handBindings
            .Where(binding => !binding.Near && binding.Actor.Visible)
            .All(binding => binding.Actor.CiUsesSharedCardBack);

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

    internal void ConfigureVisualProfile(BattlefieldVisualProfile profile)
    {
        EnsureBuilt();
        if (_privateRender || _handBindings.Count != 0 ||
            _cardPool.Any(actor => actor.Visible) ||
            _slotPool.Any(actor => actor.Visible))
        {
            throw new InvalidOperationException(
                "The battlefield visual profile can only change while every actor is pooled.");
        }

        ArenaVisualProfile resolved = ArenaVisualProfile.Resolve(profile);
        _visualProfile = profile;
        _arenaVisualProfile = resolved;
        ActivateArenaProfile(resolved);
        _handRig.SetVisualProfile(profile);
        foreach (CardActor3D actor in _cardPool)
        {
            actor.ConfigureVisualProfile(profile);
        }
        foreach (SlotActor3D actor in _slotPool)
        {
            actor.ConfigureVisualProfile(profile);
        }
    }

    internal void ConfigureVisualIdentity(MatchVisualIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        EnsureBuilt();
        if (_privateRender || _handBindings.Count != 0)
        {
            throw new InvalidOperationException(
                "The battlefield visual identity must be selected before a private snapshot is rendered.");
        }

        _visualIdentity = identity;
    }

    internal bool CiSetNearHandHover(int index, bool hovered)
    {
        EnsureBuilt();
        HandActorBinding? target = _handBindings.SingleOrDefault(binding =>
            binding.Near && binding.Index == index);
        if (!_privateRender || target is null)
        {
            return false;
        }

        if (hovered && _hoveredHandActor is { } previous &&
            !ReferenceEquals(previous, target.Actor))
        {
            previous.SetPointerHovered(false);
        }
        target.Actor.SetPointerHovered(hovered);
        return _nearHandPoses.Any(pose =>
            pose.Index == index && pose.Hovered == hovered);
    }

    public void SetVisualCatalog(ICardVisualCatalog visualCatalog)
    {
        ArgumentNullException.ThrowIfNull(visualCatalog);
        EnsureBuilt();
        ClearSensitive();
        _visualCatalog = visualCatalog;
        foreach (CardActor3D actor in _cardPool)
        {
            actor.ConfigureVisualCatalog(visualCatalog);
        }
    }

    public void PresentFx(BattlefieldFxCue cue)
    {
        EnsureBuilt();
        if (!_privateRender || cue.Sequence <= _lastFxSequence)
        {
            return;
        }

        if (cue.Player.HasValue)
        {
            ValidatePlayer(cue.Player.Value);
        }

        _lastFxSequence = cue.Sequence;
        CancelFxTween();
        bool centeredBanner = cue.Kind is BattlefieldFxKind.Phase or
            BattlefieldFxKind.Reaction;
        _fxLabel.FontSize = centeredBanner ? 38 : 54;
        _fxLabel.Text = cue.Kind switch
        {
            BattlefieldFxKind.Damage => $"-{Math.Abs(cue.Value)}",
            BattlefieldFxKind.Healing => $"+{Math.Abs(cue.Value)}",
            BattlefieldFxKind.Phase => "回合开始",
            BattlefieldFxKind.Reaction => "响应",
            BattlefieldFxKind.Graveyard => "送入墓地",
            _ => string.Empty,
        };
        _fxLabel.Modulate = cue.Kind switch
        {
            BattlefieldFxKind.Damage => new Color("ff806d"),
            BattlefieldFxKind.Healing => new Color("69e0a8"),
            BattlefieldFxKind.Reaction => new Color("ffd06b"),
            _ => new Color("d9fffa"),
        };
        Vector3 origin = cue.Player.HasValue && !centeredBanner
            ? BattlefieldPerspective.LeaderTransform(
                cue.Player.Value,
                PerspectiveViewer).Origin + (Vector3.Up * 1.15f)
            : new Vector3(0.0f, 2.35f, -0.65f);
        _fxLabel.Position = origin;
        _fxLabel.Visible = true;
        float duration = ClientVisualSettingsRuntime.Duration(
            cue.Kind is BattlefieldFxKind.Phase or BattlefieldFxKind.Reaction
                ? 0.35f
                : 0.28f);
        if (duration <= 0.0f || !IsInsideTree())
        {
            _fxLabel.Visible = false;
            return;
        }

        _fxTween = CreateTween().SetParallel().SetEase(Tween.EaseType.Out);
        _fxTween.TweenProperty(
            _fxLabel,
            "position",
            origin + (Vector3.Up * (centeredBanner ? 0.24f : 0.65f)),
            duration);
        _fxTween.TweenProperty(_fxLabel, "modulate:a", 0.0f, duration)
            .SetDelay(duration * 0.45f);
        _fxTween.Chain().TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(_fxLabel))
            {
                _fxLabel.Visible = false;
                _fxLabel.Text = string.Empty;
            }
        }));
    }

    public void ClearFx()
    {
        CancelFxTween();
        _lastFxSequence = 0;
        if (_fxLabel is not null && GodotObject.IsInstanceValid(_fxLabel))
        {
            _fxLabel.Text = string.Empty;
            _fxLabel.Modulate = Colors.Transparent;
            _fxLabel.Visible = false;
            foreach (StringName key in _fxLabel.GetMetaList())
            {
                _fxLabel.RemoveMeta(key);
            }
        }
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
        _lastPrivateView = view;
        _lastPrivateInteraction = interaction;
        _lastInteractionSurfaces = [];
        _lastInteractionSelected = null;
        _lastInteractionTargetingSource = null;
        RenderPrivatePlayer(FindPlayer(view, PlayerId.Player0), view.Viewer);
        RenderPrivatePlayer(FindPlayer(view, PlayerId.Player1), view.Viewer);
        FinishRender();
        RelayoutHands(animate: false);
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
        FinishRender();
        RelayoutHands(animate: false);
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

        _selectedHandSurface = selected;
        RelayoutHands(animate: true);
        if (!_ciHandFixtureActive)
        {
            _lastInteractionSurfaces = configured;
            _lastInteractionSelected = selected;
            _lastInteractionTargetingSource = targetingSource;
        }

        return true;
    }

    public void SetInputEnabled(bool enabled)
    {
        EnsureBuilt();
        if (!_ciHandFixtureActive)
        {
            _lastRequestedInputEnabled = enabled;
        }
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

    public void SetViewportLayout(BattlefieldViewportLayout layout)
    {
        EnsureBuilt();
        ClearObstructionSubscriptions();
        _usesProductViewportLayout = false;
        ApplyViewportLayout(layout);
    }

    public void SetViewportInsets(float leftPixels, float rightPixels)
    {
        EnsureBuilt();
        ClearObstructionSubscriptions();
        _usesProductViewportLayout = false;
        Vector2 viewportSize = GetViewport()?.GetVisibleRect().Size ??
                               new Vector2(1600.0f, 900.0f);
        ApplyViewportLayout(BattlefieldViewportLayout.FromInsets(
            viewportSize,
            leftPixels,
            rightPixels));
    }

    public void SetViewportObstructions(
        Control? leftControl,
        Control? rightControl,
        float paddingPixels = 12.0f)
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
        _usesProductViewportLayout = true;
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
        ClearPresentationActors();
        presentationOriginals.Clear();
        presentationStates.Clear();
        ClearFx();
        SetInputEnabled(false);
        _raycastInput.CancelTransient();
        _targetArrow.Stop();
        _placementGhost.Stop();
        _targetingSource = null;
        _dragVisualSource = null;
        ClearKeyboardFocus();
        _keyboardSurfaces.Clear();
        _surfaceActors.Clear();
        _handBindings.Clear();
        _nearHandPoses.Clear();
        _farHandPoses.Clear();
        _hoveredHandActor = null;
        _selectedHandSurface = null;
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
        _lastPrivateView = null;
        _lastPrivateInteraction = null;
        _lastInteractionSurfaces = [];
        _lastInteractionSelected = null;
        _lastInteractionTargetingSource = null;
        _lastRequestedInputEnabled = false;
        _ciHandFixtureActive = false;
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

        return actor.CiHasPrivacyTextureSentinel(token);
    }

    /// <summary>
    /// CI-only visual fixture. It renders an exact private/anonymous hand pair
    /// without a session or native call, while retaining enough viewer-safe
    /// presenter state for <see cref="CiRestoreHandFixture"/>.
    /// </summary>
    internal void CiPresentHandFixture(
        int ownCount,
        int opponentCount = 5,
        int? hoveredIndex = null,
        bool includeFieldReadabilityCards = false)
    {
        if (ownCount is < 1 or > MaximumRenderedHandCards ||
            opponentCount is < 0 or > MaximumRenderedHandCards)
        {
            throw new ArgumentOutOfRangeException(nameof(ownCount));
        }
        if (hoveredIndex is < 0 || hoveredIndex >= ownCount)
        {
            throw new ArgumentOutOfRangeException(nameof(hoveredIndex));
        }
        EnsureBuilt();
        if (_lastPrivateView is null || _lastPrivateInteraction is null || !_privateRender)
        {
            throw new InvalidOperationException(
                "A hand fixture requires an already presented private snapshot.");
        }

        _ciHandFixtureActive = true;
        SetInputEnabled(false);
        ResetForRender();
        Revision = _lastPrivateView.Revision;
        PerspectiveViewer = _lastPrivateView.Viewer;
        _privateRender = true;
        RenderCiFixtureBoard(includeFieldReadabilityCards);
        for (int index = 0; index < ownCount; ++index)
        {
            CardActor3D actor = RentCard();
            HandCardPose pose = _handRig.CreatePose(
                PerspectiveViewer,
                PerspectiveViewer,
                index,
                ownCount);
            CardView card = CreateCiFixtureCard(PerspectiveViewer, index);
            BattlefieldSurfaceRef surface = new(
                BattlefieldSurfaceKind.HandCard,
                PerspectiveViewer,
                index,
                card.InstanceId);
            actor.BindPrivate(card, surface, pose.Transform, BattlefieldCardLayout.NearHand);
            Register(surface, actor);
            _handBindings.Add(new HandActorBinding(
                actor,
                PerspectiveViewer,
                index,
                ownCount,
                Near: true));
        }

        PlayerId opponent = PerspectiveViewer == PlayerId.Player0
            ? PlayerId.Player1
            : PlayerId.Player0;
        for (int index = 0; index < opponentCount; ++index)
        {
            CardActor3D actor = RentCard();
            HandCardPose pose = _handRig.CreatePose(
                opponent,
                PerspectiveViewer,
                index,
                opponentCount);
            actor.BindHidden(
                opponent,
                Zone.Hand,
                pose.Transform,
                BattlefieldCardLayout.FarHand);
            _handBindings.Add(new HandActorBinding(
                actor,
                opponent,
                index,
                opponentCount,
                Near: false));
        }

        FinishRender();
        _hoveredHandActor = hoveredIndex.HasValue
            ? _handBindings.Single(binding =>
                binding.Near && binding.Index == hoveredIndex.Value).Actor
            : null;
        RelayoutHands(animate: false);
    }

    private void RenderCiFixtureBoard(bool includeReadabilityCards)
    {
        PlayerId opponent = PerspectiveViewer == PlayerId.Player0
            ? PlayerId.Player1
            : PlayerId.Player0;
        foreach (PlayerId player in new[] { PerspectiveViewer, opponent })
        {
            for (int slot = 0; slot < BattlefieldPerspective.UnitSlotCount; ++slot)
            {
                Transform3D transform = BattlefieldPerspective.UnitTransform(
                    player,
                    PerspectiveViewer,
                    slot);
                var slotSurface = new BattlefieldSurfaceRef(
                    BattlefieldSurfaceKind.UnitSlot,
                    player,
                    slot);
                SlotActor3D slotActor = RentSlot();
                slotActor.Bind(transform, "单位位", slotSurface);
                Register(slotSurface, slotActor);

                if (includeReadabilityCards && player == PerspectiveViewer && slot == 1)
                {
                    CardView card = CreateCiFixtureCard(
                        player,
                        index: 9,
                        Zone.Unit,
                        CardKind.Unit);
                    var cardSurface = new BattlefieldSurfaceRef(
                        BattlefieldSurfaceKind.Unit,
                        player,
                        slot,
                        card.InstanceId);
                    CardActor3D cardActor = RentCard();
                    cardActor.BindPrivate(
                        card,
                        cardSurface,
                        transform,
                        BattlefieldCardLayout.Field);
                    Register(cardSurface, cardActor);
                }
            }

            for (int slot = 0; slot < BattlefieldPerspective.TacticSlotCount; ++slot)
            {
                Transform3D transform = BattlefieldPerspective.TacticTransform(
                    player,
                    PerspectiveViewer,
                    slot);
                var slotSurface = new BattlefieldSurfaceRef(
                    BattlefieldSurfaceKind.TacticSlot,
                    player,
                    slot);
                SlotActor3D slotActor = RentSlot();
                slotActor.Bind(transform, "策略位", slotSurface);
                Register(slotSurface, slotActor);

                if (includeReadabilityCards && player == PerspectiveViewer && slot == 1)
                {
                    CardView card = CreateCiFixtureCard(
                        player,
                        index: 1,
                        Zone.Tactic,
                        CardKind.Relic);
                    var cardSurface = new BattlefieldSurfaceRef(
                        BattlefieldSurfaceKind.Tactic,
                        player,
                        slot,
                        card.InstanceId);
                    CardActor3D cardActor = RentCard();
                    cardActor.BindPrivate(
                        card,
                        cardSurface,
                        transform,
                        BattlefieldCardLayout.Field);
                    Register(cardSurface, cardActor);
                }
            }

            RenderLeader(
                player,
                PerspectiveViewer,
                health: 25,
                maximumHealth: 25,
                interactive: true);
            RenderPile(player, PerspectiveViewer, Zone.Deck, "牌组", 26, hidden: true);
            RenderEmptyPile(player, PerspectiveViewer, Zone.Graveyard, "墓地");
            RenderEmptyPile(player, PerspectiveViewer, Zone.Archive, "封存");
            RentCard().BindPile(
                player,
                Zone.Standby,
                "战备",
                2,
                BattlefieldPerspective.StandbyPileTransform(player, PerspectiveViewer),
                hidden: true);
        }
    }

    internal void CiRestoreHandFixture()
    {
        if (!_ciHandFixtureActive)
        {
            return;
        }

        MatchView view = _lastPrivateView ??
            throw new InvalidOperationException("The hand fixture lost its private snapshot.");
        HotseatInteractionContext interaction = _lastPrivateInteraction ??
            throw new InvalidOperationException("The hand fixture lost its interaction context.");
        BattlefieldInteractionSurface[] surfaces = _lastInteractionSurfaces;
        BattlefieldSurfaceRef? selected = _lastInteractionSelected;
        BattlefieldSurfaceRef? targetingSource = _lastInteractionTargetingSource;
        bool inputEnabled = _lastRequestedInputEnabled;

        _ciHandFixtureActive = false;
        RenderPrivate(view, interaction);
        if (surfaces.Length > 0 || selected.HasValue || targetingSource.HasValue)
        {
            TryConfigureInteraction(view.Revision, surfaces, selected, targetingSource);
        }
        SetInputEnabled(inputEnabled);
    }

    public bool CiTryGetScreenAnchor(BattlefieldSurfaceRef surface, out Vector2 screenAnchor)
    {
        if (TryGetWorldAnchor(surface, out Vector3 worldAnchor) &&
            !_camera.IsPositionBehind(worldAnchor))
        {
            screenAnchor = _camera.UnprojectPosition(worldAnchor);
            if (_raycastInput is null ||
                !GodotObject.IsInstanceValid(_raycastInput) ||
                _raycastInput.CiTryPick(screenAnchor, out BattlefieldSurfaceRef picked) &&
                picked == surface)
            {
                return true;
            }

            // A fanned hand deliberately overlaps.  Its geometric center can
            // therefore be covered by the next card even though a generous
            // visible strip remains selectable.  Return a physically pickable
            // nearby point so world-space action pills and CI gestures address
            // the same surface a player can actually click.
            ReadOnlySpan<float> offsets =
            [16.0f, 32.0f, 48.0f, 64.0f, 80.0f];
            foreach (float offset in offsets)
            {
                ReadOnlySpan<Vector2> candidates =
                [
                    new Vector2(-offset, 0.0f),
                    new Vector2(offset, 0.0f),
                    new Vector2(0.0f, -offset),
                    new Vector2(0.0f, offset),
                    new Vector2(-offset, -offset * 0.5f),
                    new Vector2(offset, -offset * 0.5f),
                    new Vector2(-offset, offset * 0.5f),
                    new Vector2(offset, offset * 0.5f),
                ];
                foreach (Vector2 candidateOffset in candidates)
                {
                    Vector2 candidate = screenAnchor + candidateOffset;
                    if (_raycastInput.CiTryPick(candidate, out picked) && picked == surface)
                    {
                        screenAnchor = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        screenAnchor = default;
        return false;
    }

    /// <summary>
    /// Resolves the real screen-space footprint of a visible card surface.
    /// Hand cards use the hand rig's roll/scale-aware bounds; field and pile
    /// cards project their actual mesh AABB through the active camera.
    /// </summary>
    public bool TryGetScreenBounds(BattlefieldSurfaceRef surface, out Rect2 screenBounds)
    {
        if (!TryResolveSurfaceActor(surface, out IBattlefieldPickTarget? target) ||
            target is not CardActor3D card)
        {
            screenBounds = default;
            return false;
        }

        HandActorBinding? hand = _handBindings.FirstOrDefault(binding =>
            ReferenceEquals(binding.Actor, card));
        if (hand is not null)
        {
            IReadOnlyList<HandCardPose> poses = hand.Near ? _nearHandPoses : _farHandPoses;
            foreach (HandCardPose pose in poses)
            {
                if (pose.Player == hand.Player && pose.Index == hand.Index)
                {
                    screenBounds = pose.ScreenBounds;
                    if (TryProjectBounds(card, card.VisualBounds, out Rect2 currentBounds))
                    {
                        // Selection and hover poses tween for a few frames. The
                        // action panel must avoid both the current rendered card
                        // and its destination pose throughout that movement.
                        screenBounds = screenBounds.Merge(currentBounds);
                    }
                    return screenBounds.Size.X > 0.0f && screenBounds.Size.Y > 0.0f;
                }
            }
        }

        return TryProjectBounds(card, card.VisualBounds, out screenBounds);
    }

    private bool TryProjectBounds(Node3D actor, Aabb localBounds, out Rect2 screenBounds)
    {
        Vector3 minimum = localBounds.Position;
        Vector3 maximum = localBounds.End;
        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;

        for (int corner = 0; corner < 8; ++corner)
        {
            var local = new Vector3(
                (corner & 1) == 0 ? minimum.X : maximum.X,
                (corner & 2) == 0 ? minimum.Y : maximum.Y,
                (corner & 4) == 0 ? minimum.Z : maximum.Z);
            Vector3 world = actor.GlobalTransform * local;
            if (_camera.IsPositionBehind(world))
            {
                screenBounds = default;
                return false;
            }

            Vector2 projected = _camera.UnprojectPosition(world);
            if (!float.IsFinite(projected.X) || !float.IsFinite(projected.Y))
            {
                screenBounds = default;
                return false;
            }

            minX = Math.Min(minX, projected.X);
            minY = Math.Min(minY, projected.Y);
            maxX = Math.Max(maxX, projected.X);
            maxY = Math.Max(maxY, projected.Y);
        }

        screenBounds = new Rect2(
            new Vector2(minX, minY),
            new Vector2(maxX - minX, maxY - minY));
        return screenBounds.Size.X > 0.0f && screenBounds.Size.Y > 0.0f;
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
        CardActor3D[] ownHand = _handBindings
            .Where(binding => binding.Near)
            .OrderBy(binding => binding.Index)
            .Select(binding => binding.Actor)
            .ToArray();
        CardActor3D[] opposingHand = _handBindings
            .Where(binding => !binding.Near)
            .OrderBy(binding => binding.Index)
            .Select(binding => binding.Actor)
            .ToArray();

        if (ownHand.Length != Math.Min(own.Hand.Length, MaximumRenderedHandCards) ||
            opposingHand.Length != Math.Min(CheckedDisplayCount(opponent.HandCount), MaximumRenderedHandCards) ||
            ownHand.Any(actor => actor.CiLayout != BattlefieldCardLayout.NearHand ||
                                 actor.CiFaceLineCount is < 1 or > 2 ||
                                 actor.StateText.Contains("可选", StringComparison.Ordinal) ||
                                 actor.StateText.Contains("目标", StringComparison.Ordinal) ||
                                 actor.StateText.Contains("已选", StringComparison.Ordinal)) ||
            opposingHand.Any(actor => actor.CiLayout != BattlefieldCardLayout.FarHand ||
                                       actor.DisplayText.Length != 0) ||
            _nearHandPoses.Count != ownHand.Length ||
            _farHandPoses.Count != opposingHand.Length ||
            _nearHandPoses.Any(pose =>
                MathF.Abs(
                    pose.PixelHeight -
                    BattlefieldHandRig.NearPixelHeightFor(_viewportLayout.ViewportSize.Y)) > 0.5f ||
                pose.ScreenBounds.Position.X < -1.0f ||
                pose.ScreenBounds.End.X > _viewportLayout.ViewportSize.X + 1.0f ||
                pose.ScreenBounds.Position.Y < -1.0f ||
                pose.ScreenBounds.End.Y > _viewportLayout.ViewportSize.Y + 1.0f))
        {
            return false;
        }

        for (int index = 1; index < _nearHandPoses.Count; ++index)
        {
            HandCardPose left = _nearHandPoses[index - 1];
            HandCardPose right = _nearHandPoses[index];
            if (right.ScreenCenter.X - left.ScreenCenter.X < 32.0f)
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

    internal bool CiValidateHandRigContract()
    {
        EnsureBuilt();
        Vector2[] productSizes =
        [
            new Vector2(1280.0f, 720.0f),
            new Vector2(1600.0f, 900.0f),
            new Vector2(2560.0f, 1440.0f),
            new Vector2(2560.0f, 1600.0f),
        ];
        foreach (Vector2 size in productSizes)
        {
            BattlefieldViewportLayout layout = BattlefieldViewportLayout.Product(size);
            var rig = new BattlefieldHandRig(_camera, layout);
            foreach (int count in new[] { 1, 5, 10 })
            {
                HandCardPose[] poses = Enumerable.Range(0, count)
                    .Select(index => rig.CreatePose(
                        PlayerId.Player0,
                        PlayerId.Player0,
                        index,
                        count))
                    .ToArray();
                float minimumHeight = size.Y <= 720.0f ? 142.0f :
                    size.Y <= 900.0f ? 170.0f : 170.0f;
                if (poses.Any(pose =>
                        !pose.Near || pose.PixelHeight < minimumHeight ||
                        MathF.Abs(pose.RollDegrees) > 8.001f ||
                        pose.ScreenBounds.Position.X < -1.0f ||
                        pose.ScreenBounds.Position.Y < -1.0f ||
                        pose.ScreenBounds.End.X > size.X + 1.0f ||
                        pose.ScreenBounds.End.Y > size.Y + 1.0f))
                {
                    return false;
                }
                for (int index = 1; index < poses.Length; ++index)
                {
                    if (poses[index].ScreenCenter.X <= poses[index - 1].ScreenCenter.X ||
                        poses[index].ScreenCenter.X - poses[index - 1].ScreenCenter.X < 32.0f)
                    {
                        return false;
                    }
                }
            }

            const int focusIndex = 5;
            HandCardPose[] normal = Enumerable.Range(0, 10)
                .Select(index => rig.CreatePose(
                    PlayerId.Player0,
                    PlayerId.Player0,
                    index,
                    10))
                .ToArray();
            HandCardPose[] hovered = Enumerable.Range(0, 10)
                .Select(index => rig.CreatePose(
                    PlayerId.Player0,
                    PlayerId.Player0,
                    index,
                    10,
                    hoveredIndex: focusIndex))
                .ToArray();
            HandCardPose[] selected = Enumerable.Range(0, 10)
                .Select(index => rig.CreatePose(
                    PlayerId.Player0,
                    PlayerId.Player0,
                    index,
                    10,
                    selectedIndex: focusIndex))
                .ToArray();
            Rect2 viewport = new(Vector2.Zero, size);
            Rect2 selectedVisible = selected[focusIndex].ScreenBounds.Intersection(viewport);
            float selectedArea = selected[focusIndex].ScreenBounds.Size.X *
                                 selected[focusIndex].ScreenBounds.Size.Y;
            float visibleArea = selectedVisible.Size.X * selectedVisible.Size.Y;
            if (!hovered[focusIndex].Hovered ||
                hovered[focusIndex].PixelHeight < normal[focusIndex].PixelHeight * 1.119f ||
                hovered[focusIndex].ScreenCenter.Y >= normal[focusIndex].ScreenCenter.Y ||
                hovered[focusIndex - 1].ScreenCenter.X >= normal[focusIndex - 1].ScreenCenter.X ||
                hovered[focusIndex + 1].ScreenCenter.X <= normal[focusIndex + 1].ScreenCenter.X ||
                !selected[focusIndex].Selected || selectedArea <= 0.0f ||
                visibleArea / selectedArea < 0.9f ||
                selected.Where((_, index) => index != focusIndex)
                    .Any(pose => pose.CameraDepth <= selected[focusIndex].CameraDepth))
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
            HandCardPose pose = _handRig.CreatePose(
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
                    pose.Transform,
                    BattlefieldCardLayout.NearHand);
                Register(surface, actor);
            }
            else
            {
                actor.BindHidden(
                    player.Player,
                    Zone.Hand,
                    pose.Transform,
                    BattlefieldCardLayout.FarHand);
            }
            _handBindings.Add(new HandActorBinding(
                actor,
                player.Player,
                index,
                handCount,
                pose.Near));
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
            CardActor3D actor = RentCard();
            HandCardPose pose = _handRig.CreatePose(
                player.Player,
                viewer,
                index,
                handCount);
            actor.BindHidden(
                player.Player,
                Zone.Hand,
                pose.Transform,
                pose.Near
                    ? BattlefieldCardLayout.NearHand
                    : BattlefieldCardLayout.FarHand);
            _handBindings.Add(new HandActorBinding(
                actor,
                player.Player,
                index,
                handCount,
                pose.Near));
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
        LeaderPortraitEntry leaderIdentity = _visualIdentity.ForPlayer(player);
        SlotActor3D actor = RentSlot();
        actor.BindLeader(
            BattlefieldPerspective.LeaderTransform(player, viewer),
            health,
            maximumHealth,
            BattlefieldPerspective.IsNear(player, viewer),
            surface,
            leaderIdentity.Faction,
            LeaderPortraitCatalog.Shared.LoadPortrait(leaderIdentity.DeckId));
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
        RentSlot().BindPile(
            BattlefieldPerspective.PileTransform(player, viewer, zone),
            title,
            0);
    }

    private void RelayoutHands(bool animate)
    {
        if (_handRig is null || _handBindings.Count == 0)
        {
            _nearHandPoses.Clear();
            _farHandPoses.Clear();
            return;
        }

        HandActorBinding? hoveredBinding = _hoveredHandActor is null
            ? null
            : _handBindings.FirstOrDefault(binding =>
                ReferenceEquals(binding.Actor, _hoveredHandActor));
        HandActorBinding? selectedBinding = _selectedHandSurface is not { } selected
            ? null
            : _handBindings.FirstOrDefault(binding =>
                SurfaceMatches(binding.Actor.Surface, selected));
        _nearHandPoses.Clear();
        _farHandPoses.Clear();
        foreach (HandActorBinding binding in _handBindings)
        {
            int? hoveredIndex = hoveredBinding is { Near: true } hovered &&
                                hovered.Player == binding.Player
                ? hovered.Index
                : null;
            int? selectedIndex = selectedBinding is { Near: true } selectedHand &&
                                 selectedHand.Player == binding.Player
                ? selectedHand.Index
                : null;
            HandCardPose pose = _handRig.CreatePose(
                binding.Player,
                PerspectiveViewer,
                binding.Index,
                binding.Count,
                hoveredIndex,
                selectedIndex);
            binding.Actor.ApplyPresentationPose(pose.Transform, animate);
            (pose.Near ? _nearHandPoses : _farHandPoses).Add(pose);
        }

        _nearHandPoses.Sort(static (left, right) => left.Index.CompareTo(right.Index));
        _farHandPoses.Sort(static (left, right) => left.Index.CompareTo(right.Index));
    }

    private void OnHandActorPointerHoverChanged(CardActor3D actor, bool hovered)
    {
        if (!_privateRender || !_handBindings.Any(binding => ReferenceEquals(binding.Actor, actor)))
        {
            return;
        }

        if (hovered)
        {
            _hoveredHandActor = actor;
        }
        else if (ReferenceEquals(_hoveredHandActor, actor))
        {
            _hoveredHandActor = null;
        }

        RelayoutHands(animate: true);
    }

    private static bool SurfaceMatches(
        BattlefieldSurfaceRef? candidate,
        BattlefieldSurfaceRef expected) =>
        candidate == expected ||
        candidate is { } value &&
        value.Kind == expected.Kind &&
        value.InstanceId.HasValue &&
        value.InstanceId == expected.InstanceId;

    private CardActor3D RentCard()
    {
        if (_cardCursor == _cardPool.Count)
        {
            var actor = new CardActor3D { Name = $"CardActor{_cardPool.Count}" };
            actor.ConfigureVisualCatalog(_visualCatalog);
            actor.ConfigureVisualProfile(_visualProfile);
            actor.PointerHoverChanged += OnHandActorPointerHoverChanged;
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
            actor.ConfigureVisualProfile(_visualProfile);
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
        ClearPresentationActors();
        presentationOriginals.Clear();
        presentationStates.Clear();
        SetInputEnabled(false);
        _raycastInput.CancelTransient();
        _targetArrow.Stop();
        _placementGhost.Stop();
        _targetingSource = null;
        _dragVisualSource = null;
        ClearKeyboardFocus();
        _keyboardSurfaces.Clear();
        _surfaceActors.Clear();
        _handBindings.Clear();
        _nearHandPoses.Clear();
        _farHandPoses.Clear();
        _hoveredHandActor = null;
        _selectedHandSurface = null;
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
        _selectedHandSurface = null;
        _targetArrow.Stop();
        _placementGhost.Stop();
        RelayoutHands(animate: true);
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

    private void ActivateArenaProfile(ArenaVisualProfile profile)
    {
        if (profile.Id != BattlefieldVisualProfile.AnimeV1)
            throw new InvalidOperationException("Retired battlefield presentation.");
        if (_animeArena is null)
        {
            _animeArena = GD.Load<PackedScene>(profile.AuthoredArenaScenePath!).Instantiate<Node3D>();
            _animeArena.Name = "AnimeV1Arena";
            AddChild(_animeArena);
            MoveChild(_animeArena, 0);
        }
        SetArenaActive(_animeArena, true);
    }

    private void SetArenaActive(Node3D arena, bool active)
    {
        arena.Visible = active;
        foreach (CanvasLayer layer in EnumerateDescendants(arena).OfType<CanvasLayer>())
        {
            layer.Visible = active;
        }
        foreach (WorldEnvironment world in EnumerateDescendants(arena).OfType<WorldEnvironment>())
        {
            ulong id = world.GetInstanceId();
            if (!_arenaEnvironments.TryGetValue(id, out Godot.Environment? environment))
            {
                environment = world.Environment;
                _arenaEnvironments[id] = environment;
            }
            world.Environment = active ? environment : null;
        }
    }

    private static IEnumerable<Node> EnumerateDescendants(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            yield return child;
            foreach (Node descendant in EnumerateDescendants(child))
            {
                yield return descendant;
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
        // The single product arena is authored; no procedural legacy table fallback.
        _camera = new BattlefieldCameraRig { Name = "BattlefieldCamera" };
        AddChild(_camera);
        Vector2 viewportSize = GetViewport()?.GetVisibleRect().Size ??
                               new Vector2(1600.0f, 900.0f);
        _viewportLayout = BattlefieldViewportLayout.Product(viewportSize);
        _handRig = new BattlefieldHandRig(_camera, _viewportLayout, _visualProfile);
        _camera.SetViewportLayout(_viewportLayout);
        _camera.ProjectionChanged += OnCameraProjectionChanged;

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

        _fxLabel = new Label3D
        {
            Name = "SafeFxLabel",
            Font = GD.Load<Font>("res://assets/fonts/NotoSansCJKsc-Regular.otf"),
            FontSize = 58,
            PixelSize = 0.012f,
            OutlineSize = 10,
            OutlineModulate = new Color(0.01f, 0.015f, 0.025f, 0.95f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Visible = false,
        };
        AddChild(_fxLabel);
        ActivateArenaProfile(_arenaVisualProfile);
    }

    private void CancelFxTween()
    {
        if (_fxTween is not null && GodotObject.IsInstanceValid(_fxTween))
        {
            _fxTween.Kill();
        }

        _fxTween = null;
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

    private void OnCameraProjectionChanged()
    {
        RelayoutHands(animate: false);
        ProjectionChanged?.Invoke(this, EventArgs.Empty);
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
            Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
            BattlefieldViewportLayout layout = _usesProductViewportLayout
                ? BattlefieldViewportLayout.Product(viewportSize, _obstructionPadding)
                : _viewportLayout.WithViewportSize(viewportSize);
            ApplyViewportLayout(layout);
        }
    }

    private void ApplyObstructionInsets()
    {
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        float viewportWidth = viewportSize.X;
        float leftCandidate = ObstructionInset(
            _leftObstruction,
            viewportWidth,
            fromLeft: true,
            _obstructionPadding);
        float rightCandidate = ObstructionInset(
            _rightObstruction,
            viewportWidth,
            fromLeft: false,
            _obstructionPadding);
        BattlefieldViewportLayout layout = BattlefieldViewportLayout
            .Product(viewportSize, _obstructionPadding)
            .MaxHorizontalReservations(leftCandidate, rightCandidate);
        ApplyViewportLayout(layout);
    }

    private void ApplyViewportLayout(BattlefieldViewportLayout layout)
    {
        _viewportLayout = layout;
        _handRig.SetViewportLayout(layout);
        _camera.SetViewportLayout(layout);
        // SetViewportLayout intentionally suppresses identical camera updates;
        // the rig still needs an explicit refresh if only its layout object was
        // renewed with equivalent camera framing values.
        RelayoutHands(animate: false);
    }

    private void SubscribeObstruction(Control? control)
    {
        if (control is null)
        {
            return;
        }

        control.ItemRectChanged += OnObstructionRectChanged;
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
    }

    private void OnObstructionRectChanged() => ApplyObstructionInsets();

    private static float ObstructionInset(
        Control? control,
        float viewportWidth,
        bool fromLeft,
        float padding)
    {
        if (control is null || !GodotObject.IsInstanceValid(control))
        {
            return 0.0f;
        }

        Rect2 rect = control.GetGlobalRect();
        if (rect.Size.X <= 0.0f)
        {
            return 0.0f;
        }

        // Visibility changes while selecting/clearing a card must not move the
        // physical battlefield under the pointer. Responsive layout may still
        // resize the reserved drawer, but a temporary hide keeps its safe lane.
        return fromLeft
            ? Mathf.Clamp(rect.End.X + padding, 0.0f, viewportWidth)
            : Mathf.Clamp(viewportWidth - rect.Position.X + padding, 0.0f, viewportWidth);
    }

    private static PlayerView FindPlayer(MatchView view, PlayerId player) =>
        view.Players.Single(item => item.Player == player);

    private static HotseatPublicPlayerView FindPlayer(
        HotseatPublicBoardView view,
        PlayerId player) => view.Players.Single(item => item.Player == player);

    private Rect2 ProjectGameplayTopologyRect()
    {
        if (_camera is null || !GodotObject.IsInstanceValid(_camera))
        {
            return new Rect2();
        }

        var points = new List<Vector2>(96);
        foreach (PlayerId player in Enum.GetValues<PlayerId>())
        {
            for (int slot = 0; slot < BattlefieldPerspective.UnitSlotCount; ++slot)
            {
                AddProjectedFootprint(
                    points,
                    BattlefieldPerspective.UnitTransform(player, PerspectiveViewer, slot),
                    BattlefieldPerspective.SlotWidth,
                    BattlefieldPerspective.SlotDepth);
            }

            for (int slot = 0; slot < BattlefieldPerspective.TacticSlotCount; ++slot)
            {
                AddProjectedFootprint(
                    points,
                    BattlefieldPerspective.TacticTransform(player, PerspectiveViewer, slot),
                    BattlefieldPerspective.SlotWidth,
                    BattlefieldPerspective.SlotDepth);
            }

            AddProjectedFootprint(
                points,
                BattlefieldPerspective.LeaderTransform(player, PerspectiveViewer),
                1.92f,
                2.52f);
            AddProjectedFootprint(
                points,
                BattlefieldPerspective.StandbyPileTransform(player, PerspectiveViewer),
                BattlefieldPerspective.SlotWidth,
                BattlefieldPerspective.SlotDepth);
            foreach (Zone zone in new[] { Zone.Deck, Zone.Graveyard, Zone.Archive })
            {
                AddProjectedFootprint(
                    points,
                    BattlefieldPerspective.PileTransform(player, PerspectiveViewer, zone),
                    BattlefieldPerspective.SlotWidth,
                    BattlefieldPerspective.SlotDepth);
            }
        }

        float minimumX = points.Min(static point => point.X);
        float minimumY = points.Min(static point => point.Y);
        float maximumX = points.Max(static point => point.X);
        float maximumY = points.Max(static point => point.Y);
        return new Rect2(
            new Vector2(minimumX, minimumY),
            new Vector2(maximumX - minimumX, maximumY - minimumY));
    }

    private void AddProjectedFootprint(
        ICollection<Vector2> destination,
        Transform3D transform,
        float width,
        float depth)
    {
        float halfWidth = width * 0.5f;
        float halfDepth = depth * 0.5f;
        foreach (float x in new[] { -halfWidth, halfWidth })
        {
            foreach (float z in new[] { -halfDepth, halfDepth })
            {
                destination.Add(_camera.UnprojectPosition(
                    transform * new Vector3(x, 0.0f, z)));
            }
        }
    }

    private static int CheckedDisplayCount(ulong value) =>
        value > int.MaxValue ? int.MaxValue : (int)value;

    private static CardView CreateCiFixtureCard(
        PlayerId player,
        int index,
        Zone zone = Zone.Hand,
        CardKind? forcedKind = null)
    {
        uint definitionId = CiFixtureDefinitionIds[index % CiFixtureDefinitionIds.Length];
        CardKind kind = forcedKind ?? index switch
        {
            1 => CardKind.Relic,
            2 => CardKind.Trap,
            3 => CardKind.Spell,
            _ => CardKind.Unit,
        };
        int cost = index switch
        {
            0 => 0,
            9 => 12,
            _ => 1 + (index % 6),
        };
        int countdown = kind is CardKind.Relic or CardKind.Trap
            ? index == 1 ? 12 : 4
            : 0;
        int attack = kind == CardKind.Unit
            ? index switch
            {
                0 => 0,
                9 => 11,
                _ => 2 + (index % 5),
            }
            : 0;
        int printedHealth = kind == CardKind.Unit
            ? index switch
            {
                0 => 0,
                8 => 9,
                9 => 12,
                _ => 3 + (index % 5),
            }
            : 0;
        int currentHealth = kind == CardKind.Unit && index == 8 ? 2 : printedHealth;
        var component = new ComponentSpec
        {
            HasComponent = false,
            GrantedKind = EffectKind.DrawCards,
            GrantedAmount = 0,
        };
        var definition = new CardDefinition
        {
            Id = definitionId,
            Name = kind switch
            {
                CardKind.Unit => $"测试单位 {index + 1}",
                CardKind.Relic => "测试倒计时设施",
                CardKind.Trap => "测试伏策装置",
                _ => "测试战术法术",
            },
            Kind = kind,
            Cost = cost,
            Attack = attack,
            Health = printedHealth,
            Countdown = countdown,
            PrintedGuard = false,
            PrintedRush = false,
            PrintedStorm = false,
            PrintedBarrier = false,
            PrintedLifesteal = false,
            PrintedBane = false,
            EvolvedAttack = attack + 2,
            EvolvedHealth = printedHealth + 2,
            AdditionalCost = new AdditionalCost { BurnPpCapacity = 0 },
            Component = component,
            Effects = [],
        };
        ulong instanceId = 0xf000_0000_0000_0000UL + (ulong)index + 1UL;
        return new CardView
        {
            InstanceId = instanceId,
            DefinitionId = definitionId,
            Definition = definition,
            Kind = kind,
            Name = definition.Name,
            Owner = player,
            Controller = player,
            Zone = zone,
            Sequence = instanceId,
            Cost = cost,
            CurrentAttack = attack,
            CurrentHealth = currentHealth,
            MaximumHealth = printedHealth,
            Keywords = Keyword.None,
            Evolved = false,
            AttackedThisTurn = false,
            EnteredThisTurn = false,
            TemporaryRush = false,
            DeployedFromStandby = false,
            FaceDown = false,
            Countdown = countdown,
            GrantedComponent = component,
        };
    }

    private sealed record HandActorBinding(
        CardActor3D Actor,
        PlayerId Player,
        int Index,
        int Count,
        bool Near);

    private static void ValidatePlayer(PlayerId player)
    {
        if (player is not (PlayerId.Player0 or PlayerId.Player1))
        {
            throw new ArgumentOutOfRangeException(nameof(player), player, "Unsupported player value.");
        }
    }
}
