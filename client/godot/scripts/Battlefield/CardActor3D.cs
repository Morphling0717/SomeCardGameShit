// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using Godot;
using Scgs.Client;
using Scgs.GodotClient.Visual;
using Scgs.GodotClient.Visuals;
using Scgs.Hotseat;
using CardText = Scgs.GodotClient.Presentation.CardPresentation;

namespace Scgs.GodotClient.Battlefield;

public sealed partial class CardActor3D : Area3D, IBattlefieldPickTarget
{
    public const uint PickCollisionLayer = 1U << 9;

    private const float BadgePlateCenterY = 0.067f;
    private const float BadgeLabelY = 0.104f;
    private const float MinimumBadgeDepthClearance = 0.012f;

    private static readonly Color LegalColor = new("55ead0");
    private static readonly Color DestinationColor = new("ffc35d");
    private static readonly Color SelectedColor = new("ff765f");
    private static readonly Color MidrangeFrameColor = new("285d7c");
    private static readonly Color AdvanceFrameColor = new("675084");
    private static readonly Color NeutralFrameColor = new("2b4052");
    private static readonly Color HiddenFrameColor = new("101d31");

    private static readonly BoxMesh CardMesh = new()
    {
        Size = new Vector3(
            BattlefieldPerspective.CardWidth,
            0.105f,
            BattlefieldPerspective.CardDepth),
    };
    private static readonly BoxMesh OutlineMesh = new()
    {
        Size = new Vector3(
            BattlefieldPerspective.CardWidth + 0.14f,
            0.025f,
            BattlefieldPerspective.CardDepth + 0.14f),
    };
    private static readonly QuadMesh ArtworkMesh = new()
    {
        Size = new Vector2(
            BattlefieldPerspective.CardWidth - 0.16f,
            BattlefieldPerspective.CardDepth - 0.36f),
    };
    private static readonly CylinderMesh RoundBadgeMesh = new()
    {
        TopRadius = 0.24f,
        BottomRadius = 0.24f,
        Height = 0.045f,
        RadialSegments = 24,
    };
    private static readonly BoxMesh PillBadgeMesh = new()
    {
        Size = new Vector3(0.72f, 0.045f, 0.31f),
    };

    private static readonly StandardMaterial3D MidrangeFrame = CreateFrameMaterial(MidrangeFrameColor);
    private static readonly StandardMaterial3D AdvanceFrame = CreateFrameMaterial(AdvanceFrameColor);
    private static readonly StandardMaterial3D NeutralFrame = CreateFrameMaterial(NeutralFrameColor);
    private static readonly StandardMaterial3D HiddenFrame = CreateFrameMaterial(HiddenFrameColor);
    private static readonly StandardMaterial3D LegalOutline = CreateOutlineMaterial(LegalColor);
    private static readonly StandardMaterial3D DestinationOutline = CreateOutlineMaterial(DestinationColor);
    private static readonly StandardMaterial3D SelectedOutline = CreateOutlineMaterial(SelectedColor);
    private static readonly StandardMaterial3D BadgeMaterial = CreateBadgeMaterial();
    private static readonly Dictionary<ulong, StandardMaterial3D> ArtworkMaterials = [];

    private ICardVisualCatalog _visualCatalog = CardVisualCatalog.Shared;
    private MeshInstance3D _baseMesh = null!;
    private MeshInstance3D _artworkSurface = null!;
    private MeshInstance3D _outlineMesh = null!;
    private MeshInstance3D _stackUnderlayA = null!;
    private MeshInstance3D _stackUnderlayB = null!;
    private MeshInstance3D _costBadge = null!;
    private MeshInstance3D _kindBadge = null!;
    private MeshInstance3D _attackBadge = null!;
    private MeshInstance3D _healthBadge = null!;
    private MeshInstance3D _countdownBadge = null!;
    private Label3D _faceLabel = null!;
    private Label3D _pileLabel = null!;
    private Label3D _costLabel = null!;
    private Label3D _kindLabel = null!;
    private Label3D _attackLabel = null!;
    private Label3D _healthLabel = null!;
    private Label3D _countdownLabel = null!;
    private Label3D _stateLabel = null!;
    private CollisionShape3D _collision = null!;
    private Transform3D _restTransform = Transform3D.Identity;
    private BattlefieldSurfaceRef? _boundSurface;
    private BattlefieldHighlightKind _highlight;
    private BattlefieldCardLayout _layout = BattlefieldCardLayout.Field;
    private bool _pointerHovered;
    private Tween? _hoverTween;
    private StandardMaterial3D? _ciPrivacyMaterial;
    private ImageTexture? _ciPrivacyTexture;
    private string _displayText = string.Empty;

    internal event Action<CardActor3D, bool>? PointerHoverChanged;
    private bool _showsIdentity;

    public BattlefieldSurfaceRef? Surface { get; private set; }

    public BattlefieldCardPresentation? CardPresentation { get; private set; }

    public Vector3 WorldAnchor => GlobalPosition +
        (GlobalTransform.Basis.Y.Normalized() * 0.22f);

    public bool CanActivate { get; private set; }

    public bool CollisionEnabled => CollisionLayer != 0 && !_collision.Disabled;

    public string DisplayText => _displayText;

    public string StateText => _stateLabel?.Text ?? string.Empty;

    public bool OutlineVisible => _outlineMesh?.Visible == true;

    internal BattlefieldCardLayout CiLayout => _layout;

    /// <summary>
    /// Local-space bounds of the complete card body. The battlefield presenter
    /// projects these corners when a board-adjacent HUD element must avoid the
    /// card's real on-screen footprint.
    /// </summary>
    internal Aabb VisualBounds => CardMesh.GetAabb();

    internal bool CiPointerHovered => _pointerHovered;

    internal float CiFaceLabelWorldWidth =>
        _faceLabel.Width * _faceLabel.PixelSize * GlobalTransform.Basis.X.Length();

    internal int CiFaceLineCount => string.IsNullOrEmpty(DisplayText)
        ? 0
        : DisplayText.Count(character => character == '\n') + 1;

    internal CardReadabilityEvidence CiReadabilityEvidence
    {
        get
        {
            EnsureBuilt();
            return new CardReadabilityEvidence(
                _showsIdentity,
                _layout,
                CardPresentation?.Kind,
                CardPresentation?.Cost ?? 0,
                CardPresentation?.Attack ?? 0,
                CardPresentation?.Health ?? 0,
                CardPresentation?.Countdown ?? 0,
                _faceLabel.Text,
                _faceLabel.Visible,
                CreateBadgeEvidence(_costLabel, _costBadge),
                CreateBadgeEvidence(_kindLabel, _kindBadge),
                CreateBadgeEvidence(_attackLabel, _attackBadge),
                CreateBadgeEvidence(_healthLabel, _healthBadge),
                CreateBadgeEvidence(_countdownLabel, _countdownBadge),
                MinimumBadgeDepthClearance);
        }
    }

    internal bool CiHasReadableComposition =>
        CiReadabilityEvidence.MatchesExpectedComposition;

    internal CardGpuReadabilityEvidence CiGpuReadabilityEvidence(Camera3D camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        CardReadabilityEvidence local = CiReadabilityEvidence;
        return new CardGpuReadabilityEvidence(
            local,
            CreateGpuBadgeEvidence(camera, _costLabel, local.CostBadge),
            CreateGpuBadgeEvidence(camera, _kindLabel, local.KindBadge),
            CreateGpuBadgeEvidence(camera, _attackLabel, local.AttackBadge),
            CreateGpuBadgeEvidence(camera, _healthLabel, local.HealthBadge),
            CreateGpuBadgeEvidence(camera, _countdownLabel, local.CountdownBadge));
    }

    internal bool CiHasPrivacyTextureSentinel(string token) =>
        _ciPrivacyMaterial is { } material &&
        _ciPrivacyTexture is { } texture &&
        ReferenceEquals(_artworkSurface?.MaterialOverride, material) &&
        material.ResourceName == token && texture.ResourceName == token;

    public bool HasTripleAffordance =>
        Visible && CanActivate && _highlight != BattlefieldHighlightKind.None &&
        OutlineVisible && !string.IsNullOrWhiteSpace(StateText);

    public override void _Ready()
    {
        EnsureBuilt();
        ClearSensitive();
    }

    public void ConfigureVisualCatalog(ICardVisualCatalog visualCatalog)
    {
        ArgumentNullException.ThrowIfNull(visualCatalog);
        if (Visible && CardPresentation is not null)
        {
            throw new InvalidOperationException("Visual catalogs can only change while an actor is pooled.");
        }

        _visualCatalog = visualCatalog;
    }

    public void BindPrivate(
        CardView card,
        BattlefieldSurfaceRef? surface,
        Transform3D transform,
        BattlefieldCardLayout layout = BattlefieldCardLayout.Field)
    {
        ArgumentNullException.ThrowIfNull(card);
        bool known = card.InstanceId.HasValue && card.DefinitionId.HasValue && card.Definition is not null;
        (int attack, int health) = known ? CardText.GetDisplayedUnitStats(card) : (0, 0);
        int maximumHealth = known && card.Zone == Zone.Unit ? card.MaximumHealth : health;
        var presentation = new BattlefieldCardPresentation(
            card.InstanceId,
            card.DefinitionId,
            known ? card.Name : string.Empty,
            known ? card.Kind : null,
            card.Controller,
            card.Zone,
            known ? card.Cost : 0,
            attack,
            health,
            maximumHealth,
            known ? card.Countdown : 0,
            card.FaceDown,
            known);
        Bind(presentation, surface, transform, showIdentityOnBoard: known && !card.FaceDown, layout);
    }

    public void BindPublic(
        HotseatPublicCardView card,
        Transform3D transform,
        BattlefieldCardLayout layout = BattlefieldCardLayout.Field)
    {
        ArgumentNullException.ThrowIfNull(card);
        bool known = card.HasKnownIdentity && !card.FaceDown;
        var presentation = new BattlefieldCardPresentation(
            null,
            known ? card.DefinitionId : null,
            known ? card.Name : string.Empty,
            known ? card.Kind : null,
            card.Controller,
            card.Zone,
            known ? card.Cost : 0,
            known ? card.CurrentAttack : 0,
            known ? card.CurrentHealth : 0,
            known ? card.MaximumHealth : 0,
            known ? card.Countdown : 0,
            card.FaceDown,
            known);
        Bind(presentation, null, transform, known, layout);
        CollisionLayer = 0;
    }

    public void BindHidden(
        PlayerId controller,
        Zone zone,
        Transform3D transform,
        BattlefieldCardLayout layout,
        BattlefieldSurfaceRef? surface = null)
    {
        var presentation = new BattlefieldCardPresentation(
            null, null, string.Empty, null, controller, zone,
            0, 0, 0, 0, 0, true, false);
        Bind(presentation, surface, transform, showIdentityOnBoard: false, layout);
    }

    public void BindPile(
        PlayerId controller,
        Zone zone,
        string title,
        ulong count,
        Transform3D transform,
        bool hidden,
        BattlefieldSurfaceRef? surface = null)
    {
        var presentation = new BattlefieldCardPresentation(
            null, null, string.Empty, null, controller, zone,
            0, 0, 0, 0, 0, hidden, false);
        Bind(presentation, surface, transform, showIdentityOnBoard: false, BattlefieldCardLayout.Pile);
        _displayText = $"{title}\n{count}";
        if (!hidden)
        {
            _artworkSurface.MaterialOverride = SharedArtworkMaterial(_visualCatalog.FallbackFront);
            _baseMesh.MaterialOverride = NeutralFrame;
        }

        _faceLabel.Text = string.Empty;
        _faceLabel.Visible = false;
        _pileLabel.Text = $"{title}  {count}";
        _pileLabel.Visible = true;
        _stackUnderlayA.MaterialOverride = hidden ? HiddenFrame : NeutralFrame;
        _stackUnderlayB.MaterialOverride = hidden ? HiddenFrame : NeutralFrame;
        _stackUnderlayA.Visible = count >= 2;
        _stackUnderlayB.Visible = count >= 5;
    }

    public void SetHighlight(BattlefieldHighlightKind highlight)
    {
        EnsureBuilt();
        _highlight = highlight;
        CanActivate = Surface.HasValue && highlight != BattlefieldHighlightKind.None;
        _stateLabel.Text = highlight switch
        {
            BattlefieldHighlightKind.Legal => "●",
            BattlefieldHighlightKind.Selected => "◆",
            BattlefieldHighlightKind.Destination => "◎",
            _ => string.Empty,
        };
        _stateLabel.Visible = _stateLabel.Text.Length > 0;
        UpdateHighlight();
    }

    public void SetPickEnabled(bool enabled)
    {
        EnsureBuilt();
        bool pickable = enabled && Surface.HasValue;
        _collision.Disabled = !pickable;
        CollisionLayer = pickable ? PickCollisionLayer : 0;
    }

    public void SetUtilityInteractive(string label)
    {
        EnsureBuilt();
        if (!Visible || !Surface.HasValue)
        {
            throw new InvalidOperationException("A utility card must expose a bound surface.");
        }

        CanActivate = true;
        _stateLabel.Text = label;
        _stateLabel.Visible = true;
        SetPickEnabled(enabled: true);
    }

    public void OverrideInteractionSurface(BattlefieldSurfaceRef surface)
    {
        EnsureBuilt();
        if (!Visible || !_boundSurface.HasValue)
        {
            throw new InvalidOperationException("Only a bound private card can override its surface.");
        }

        Surface = surface;
        SetPickEnabled(enabled: true);
    }

    public void RestoreBoundSurface()
    {
        EnsureBuilt();
        Surface = _boundSurface;
        SetPickEnabled(enabled: _boundSurface.HasValue);
    }

    public void SetPointerHovered(bool hovered)
    {
        EnsureBuilt();
        if (_pointerHovered == hovered || !Visible)
        {
            return;
        }

        _pointerHovered = hovered;
        if (_layout is BattlefieldCardLayout.NearHand or BattlefieldCardLayout.FarHand &&
            PointerHoverChanged is not null)
        {
            PointerHoverChanged.Invoke(this, hovered);
            return;
        }

        Transform3D target = _restTransform;
        if (hovered)
        {
            target.Origin += Vector3.Up * 0.28f;
            target.Basis = target.Basis.Scaled(Vector3.One * 1.11f);
        }

        CancelHoverTween();
        if (!IsInsideTree())
        {
            Transform = target;
            return;
        }

        _hoverTween = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _hoverTween.TweenProperty(
            this,
            "transform",
            target,
            ClientVisualSettingsRuntime.Duration(0.12f));
    }

    /// <summary>
    /// Applies a camera-locked presentation pose while preserving the actor,
    /// collision surface and privacy binding. Hand hover is coordinated by the
    /// presenter so neighbouring cards can move as one composition.
    /// </summary>
    internal void ApplyPresentationPose(Transform3D pose, bool animate)
    {
        EnsureBuilt();
        _restTransform = pose;
        CancelHoverTween();
        float duration = animate ? ClientVisualSettingsRuntime.Duration(0.18f) : 0.0f;
        if (duration <= 0.0f || !IsInsideTree())
        {
            Transform = pose;
            return;
        }

        _hoverTween = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _hoverTween.TweenProperty(this, "transform", pose, duration);
    }

    public void ClearSensitive()
    {
        EnsureBuilt();
        CancelHoverTween();
        Surface = null;
        _boundSurface = null;
        CardPresentation = null;
        CanActivate = false;
        _highlight = BattlefieldHighlightKind.None;
        _layout = BattlefieldCardLayout.Field;
        _pointerHovered = false;
        _restTransform = Transform3D.Identity;
        Transform = Transform3D.Identity;
        _displayText = string.Empty;
        _showsIdentity = false;
        ClearLabels();
        _baseMesh.MaterialOverride = NeutralFrame;
        _artworkSurface.MaterialOverride = null;
        DisposeCiPrivacyResources();
        _artworkSurface.Visible = false;
        _stackUnderlayA.MaterialOverride = NeutralFrame;
        _stackUnderlayB.MaterialOverride = NeutralFrame;
        _stackUnderlayA.Visible = false;
        _stackUnderlayB.Visible = false;
        _costBadge.Visible = false;
        _kindBadge.Visible = false;
        _attackBadge.Visible = false;
        _healthBadge.Visible = false;
        _countdownBadge.Visible = false;
        _outlineMesh.Visible = false;
        _outlineMesh.MaterialOverride = LegalOutline;
        _collision.Disabled = true;
        CollisionLayer = 0;
        Visible = false;
        ScrubMetadata(this);
    }

    public int CountForbiddenToken(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return 0;
        }

        int count = 0;
        foreach (string? value in new[]
                 {
                     DisplayText, _faceLabel.Text, _pileLabel.Text, StateText, _costLabel.Text, _kindLabel.Text,
                     _attackLabel.Text, _healthLabel.Text, _countdownLabel.Text,
                     CardPresentation?.Name,
                     _baseMesh.MaterialOverride?.ResourceName,
                     _artworkSurface.MaterialOverride?.ResourceName,
                     _outlineMesh.MaterialOverride?.ResourceName,
                 })
        {
            count += ContainsToken(value, token) ? 1 : 0;
        }

        count += CountMetadataToken(this, token);
        return count;
    }

    public void CiArmPrivacySentinel(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        EnsureBuilt();
        if (!Visible || CardPresentation is null)
        {
            throw new InvalidOperationException("A visible card must be bound before arming privacy data.");
        }

        _displayText = token;
        _faceLabel.Text = token;
        CardPresentation = CardPresentation with { Name = token };
        SetMeta("ci_private_sentinel", token);
        _baseMesh.SetMeta("ci_private_sentinel", token);
        _artworkSurface.SetMeta("ci_private_sentinel", token);

        // This intentionally installs a visibly impossible private texture in
        // addition to the string/metadata sentinel.  The resolving render must
        // replace it with a viewer-safe public face before FramePostDraw; the
        // display-backed visual suite scans the resulting GPU image for this
        // exact magenta marker.  Keep this material outside the shared artwork
        // cache so a CI-only private marker can never become a product face.
        DisposeCiPrivacyResources();
        using Image image = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
        image.Fill(new Color(1.0f, 0.0f, 1.0f, 1.0f));
        _ciPrivacyTexture = ImageTexture.CreateFromImage(image);
        _ciPrivacyTexture.ResourceName = token;
        _ciPrivacyMaterial = new StandardMaterial3D
        {
            ResourceName = token,
            AlbedoTexture = _ciPrivacyTexture,
            AlbedoColor = Colors.White,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
        };
        _artworkSurface.MaterialOverride = _ciPrivacyMaterial;
        _artworkSurface.Visible = true;
    }

    private void Bind(
        BattlefieldCardPresentation presentation,
        BattlefieldSurfaceRef? surface,
        Transform3D transform,
        bool showIdentityOnBoard,
        BattlefieldCardLayout layout)
    {
        EnsureBuilt();
        CancelHoverTween();
        DisposeCiPrivacyResources();
        _layout = layout;
        ApplyShadowPolicy(layout);
        ApplyLabelLayout(layout);
        Surface = surface;
        _boundSurface = surface;
        CardPresentation = presentation;
        _restTransform = transform;
        Transform = transform;
        _pointerHovered = false;
        _highlight = BattlefieldHighlightKind.None;
        CanActivate = false;

        bool known = showIdentityOnBoard && presentation.KnownIdentity &&
                     presentation.DefinitionId.HasValue;
        _showsIdentity = known;
        Texture2D faceTexture = known
            ? _visualCatalog.LoadArtwork(presentation.DefinitionId!.Value)
            : _visualCatalog.CardBack;
        _artworkSurface.MaterialOverride = SharedArtworkMaterial(faceTexture);
        _artworkSurface.Visible = true;
        _baseMesh.MaterialOverride = known
            ? FrameMaterial(CardVisualCatalog.FactionFor(presentation.DefinitionId))
            : HiddenFrame;

        _displayText = known ? FormatKnownCard(presentation, layout) : string.Empty;
        _faceLabel.Text = known
            ? EllipsizeCardName(
                presentation.Name,
                layout is BattlefieldCardLayout.NearHand or BattlefieldCardLayout.FarHand ? 3 : 7)
            : string.Empty;
        _faceLabel.Visible = known && layout is BattlefieldCardLayout.NearHand;
        _pileLabel.Text = string.Empty;
        _pileLabel.Visible = false;
        _stackUnderlayA.Visible = false;
        _stackUnderlayB.Visible = false;
        _costLabel.Text = known ? presentation.Cost.ToString(CultureInfo.InvariantCulture) : string.Empty;
        _kindLabel.Text = known ? KindLabel(presentation.Kind) : string.Empty;
        bool unit = known && presentation.Kind == CardKind.Unit;
        bool countdown = known &&
                         presentation.Kind is CardKind.Relic or CardKind.Trap &&
                         presentation.Countdown > 0;
        _attackLabel.Text = unit
            ? presentation.Attack.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
        _healthLabel.Text = unit
            ? presentation.Health.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
        _countdownLabel.Text = countdown
            ? presentation.Countdown.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
        _costLabel.Visible = known;
        _kindLabel.Visible = known;
        _attackLabel.Visible = unit;
        _healthLabel.Visible = unit;
        _countdownLabel.Visible = countdown;
        _costBadge.Visible = known;
        _kindBadge.Visible = known;
        _attackBadge.Visible = unit;
        _healthBadge.Visible = unit;
        _countdownBadge.Visible = countdown;
        _stateLabel.Text = string.Empty;
        _stateLabel.Visible = false;
        _outlineMesh.Visible = false;
        _collision.Disabled = !surface.HasValue;
        CollisionLayer = surface.HasValue ? PickCollisionLayer : 0;
        Visible = true;
        UpdateHighlight();
    }

    private static string FormatKnownCard(
        BattlefieldCardPresentation card,
        BattlefieldCardLayout layout)
    {
        if (layout is BattlefieldCardLayout.NearHand or BattlefieldCardLayout.FarHand)
        {
            string handName = EllipsizeCardName(card.Name, 4);
            return card.Kind switch
            {
                CardKind.Unit => $"{card.Cost}费 {card.Attack}/{card.Health}\n{handName}",
                CardKind.Relic when card.Countdown > 0 =>
                    $"{card.Cost}费 倒{card.Countdown}\n{handName}",
                _ => $"{card.Cost}费\n{handName}",
            };
        }

        string name = CompactFieldCardName(card.Name);
        return card.Kind switch
        {
            CardKind.Unit => $"{card.Cost}费  {card.Attack}/{card.Health}\n{name}",
            CardKind.Relic or CardKind.Trap when card.Countdown > 0 =>
                $"{card.Cost}费  倒{card.Countdown}\n{name}",
            _ => $"{card.Cost}费\n{name}",
        };
    }

    private static string KindLabel(CardKind? kind) => kind switch
    {
        CardKind.Unit => "◆",
        CardKind.Spell => "✦",
        CardKind.Relic => "▰",
        CardKind.Trap => "◇",
        _ => string.Empty,
    };

    private static string CompactFieldCardName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "—";
        }

        int[] elements = StringInfo.ParseCombiningCharacters(name);
        if (elements.Length <= 3)
        {
            return name;
        }

        int split = elements[3];
        int keptElements = Math.Min(elements.Length, 6);
        int keptLength = keptElements < elements.Length ? elements[keptElements] : name.Length;
        string suffix = keptElements < elements.Length ? "…" : string.Empty;
        return $"{name[..split]}\n{name[split..keptLength]}{suffix}";
    }

    private static string EllipsizeCardName(string name, int maximumElements)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "—";
        }

        int[] elements = StringInfo.ParseCombiningCharacters(name);
        if (elements.Length <= maximumElements)
        {
            return name;
        }

        return $"{name[..elements[maximumElements]]}…";
    }

    private void ApplyLabelLayout(BattlefieldCardLayout layout)
    {
        (_faceLabel.FontSize, _faceLabel.PixelSize, _faceLabel.OutlineSize, _faceLabel.Width) =
            layout switch
            {
                BattlefieldCardLayout.NearHand => (23, 0.0085f, 5, 112.0f),
                BattlefieldCardLayout.FarHand => (25, 0.0085f, 4, 120.0f),
                BattlefieldCardLayout.Pile => (28, 0.0092f, 5, 126.0f),
                _ => (28, 0.0091f, 5, 128.0f),
            };
        _faceLabel.Position = new Vector3(0.0f, 0.073f, 0.53f);

        int badgeSize = layout switch
        {
            BattlefieldCardLayout.NearHand => 28,
            BattlefieldCardLayout.Field => 46,
            _ => 26,
        };
        float badgePixelSize = layout == BattlefieldCardLayout.Field ? 0.0094f : 0.009f;
        float roundBadgeWidth = layout == BattlefieldCardLayout.Field ? 58.0f : 44.0f;
        foreach (Label3D badgeLabel in new[]
                 {
                     _costLabel, _kindLabel, _attackLabel, _healthLabel,
                     _countdownLabel, _stateLabel,
                 })
        {
            badgeLabel.PixelSize = badgePixelSize;
        }
        _costLabel.Width = roundBadgeWidth;
        _attackLabel.Width = roundBadgeWidth;
        _healthLabel.Width = roundBadgeWidth;
        _countdownLabel.Width = layout == BattlefieldCardLayout.Field ? 72.0f : 64.0f;
        float fieldPlateScale = layout == BattlefieldCardLayout.Field ? 1.22f : 1.0f;
        Vector3 roundScale = new(fieldPlateScale, 1.0f, fieldPlateScale);
        _costBadge.Scale = roundScale;
        _attackBadge.Scale = roundScale;
        _healthBadge.Scale = roundScale;
        _kindBadge.Scale = Vector3.One;
        _countdownBadge.Scale = layout == BattlefieldCardLayout.Field
            ? new Vector3(1.18f, 1.0f, 1.18f)
            : Vector3.One;
        _costLabel.FontSize = badgeSize;
        _attackLabel.FontSize = badgeSize;
        _healthLabel.FontSize = badgeSize;
        _countdownLabel.FontSize = badgeSize;
        _kindLabel.FontSize = Math.Max(18, badgeSize - 8);
        _stateLabel.FontSize = Math.Max(24, badgeSize - 6);
    }

    private void UpdateHighlight()
    {
        _outlineMesh.MaterialOverride = _highlight switch
        {
            BattlefieldHighlightKind.Selected => SelectedOutline,
            BattlefieldHighlightKind.Destination => DestinationOutline,
            _ => LegalOutline,
        };
        _outlineMesh.Visible = _highlight != BattlefieldHighlightKind.None;
    }

    private void ClearLabels()
    {
        foreach (Label3D label in new[]
                 {
                     _faceLabel, _pileLabel, _costLabel, _kindLabel, _attackLabel,
                     _healthLabel, _countdownLabel, _stateLabel,
                 })
        {
            label.Text = string.Empty;
            label.Visible = false;
        }
    }

    private void CancelHoverTween()
    {
        if (_hoverTween is not null && GodotObject.IsInstanceValid(_hoverTween))
        {
            _hoverTween.Kill();
        }

        _hoverTween = null;
    }

    private void EnsureBuilt()
    {
        if (_baseMesh is not null)
        {
            return;
        }

        _outlineMesh = new MeshInstance3D
        {
            Name = "AffordanceOutline",
            Mesh = OutlineMesh,
            Position = new Vector3(0.0f, -0.062f, 0.0f),
            MaterialOverride = LegalOutline,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_outlineMesh);

        _baseMesh = new MeshInstance3D
        {
            Name = "CardBase",
            Mesh = CardMesh,
            MaterialOverride = NeutralFrame,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        AddChild(_baseMesh);

        _stackUnderlayA = CreateStackUnderlay(
            "StackUnderlayA",
            new Vector3(0.075f, -0.105f, 0.075f));
        AddChild(_stackUnderlayA);
        _stackUnderlayB = CreateStackUnderlay(
            "StackUnderlayB",
            new Vector3(0.14f, -0.17f, 0.14f));
        AddChild(_stackUnderlayB);

        _artworkSurface = new MeshInstance3D
        {
            Name = "ArtworkSurface",
            Mesh = ArtworkMesh,
            Position = new Vector3(0.0f, 0.057f, -0.08f),
            RotationDegrees = new Vector3(-90.0f, 0.0f, 0.0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_artworkSurface);

        _costBadge = CreateBadge(
            "CostBadgePlate",
            RoundBadgeMesh,
            new Vector3(-0.57f, BadgePlateCenterY, -0.84f));
        AddChild(_costBadge);
        _kindBadge = CreateBadge(
            "KindBadgePlate",
            PillBadgeMesh,
            new Vector3(0.45f, BadgePlateCenterY, -0.84f));
        AddChild(_kindBadge);
        _attackBadge = CreateBadge(
            "AttackBadgePlate",
            RoundBadgeMesh,
            new Vector3(-0.57f, BadgePlateCenterY, 0.84f));
        AddChild(_attackBadge);
        _healthBadge = CreateBadge(
            "HealthBadgePlate",
            RoundBadgeMesh,
            new Vector3(0.57f, BadgePlateCenterY, 0.84f));
        AddChild(_healthBadge);
        _countdownBadge = CreateBadge(
            "CountdownBadgePlate",
            PillBadgeMesh,
            new Vector3(0.43f, BadgePlateCenterY, 0.84f));
        AddChild(_countdownBadge);

        _collision = new CollisionShape3D
        {
            Name = "PickShape",
            Shape = new BoxShape3D { Size = new Vector3(1.66f, 0.28f, 2.26f) },
            Position = new Vector3(0.0f, 0.1f, 0.0f),
        };
        AddChild(_collision);

        _faceLabel = CreateTopLabel("FaceLabel", 28, new Vector3(0.0f, 0.073f, 0.53f));
        _faceLabel.Width = 128.0f;
        AddChild(_faceLabel);

        _pileLabel = CreateTopLabel("PileBadge", 23, new Vector3(0.0f, 0.091f, 0.66f));
        _pileLabel.Width = 132.0f;
        _pileLabel.Modulate = new Color("efffff");
        AddChild(_pileLabel);

        _costLabel = CreateTopLabel(
            "CostBadge",
            26,
            new Vector3(-0.57f, BadgeLabelY, -0.84f));
        _costLabel.Width = 36.0f;
        _costLabel.Modulate = new Color("ddfff9");
        AddChild(_costLabel);

        _kindLabel = CreateTopLabel(
            "KindBadge",
            20,
            new Vector3(0.45f, BadgeLabelY, -0.84f));
        _kindLabel.Width = 56.0f;
        _kindLabel.Modulate = new Color("d7e4ed");
        AddChild(_kindLabel);

        _attackLabel = CreateTopLabel(
            "AttackBadge",
            25,
            new Vector3(-0.57f, BadgeLabelY, 0.84f));
        _attackLabel.Width = 36.0f;
        _attackLabel.Modulate = new Color("ffe0a5");
        AddChild(_attackLabel);

        _healthLabel = CreateTopLabel(
            "HealthBadge",
            25,
            new Vector3(0.57f, BadgeLabelY, 0.84f));
        _healthLabel.Width = 36.0f;
        _healthLabel.Modulate = new Color("baffcf");
        AddChild(_healthLabel);

        _countdownLabel = CreateTopLabel(
            "CountdownBadge",
            23,
            new Vector3(0.43f, BadgeLabelY, 0.84f));
        _countdownLabel.Width = 64.0f;
        _countdownLabel.Modulate = new Color("fff0b2");
        AddChild(_countdownLabel);

        _stateLabel = CreateTopLabel("StateLabel", 26, new Vector3(0.59f, 0.112f, -0.70f));
        _stateLabel.Width = 46.0f;
        _stateLabel.Modulate = new Color(1.0f, 0.94f, 0.63f, 1.0f);
        AddChild(_stateLabel);

        CollisionMask = 0;
        InputRayPickable = true;
        Monitoring = false;
        Monitorable = false;
    }

    private static StandardMaterial3D FrameMaterial(CardVisualFaction faction) => faction switch
    {
        CardVisualFaction.Midrange => MidrangeFrame,
        CardVisualFaction.Advance => AdvanceFrame,
        _ => NeutralFrame,
    };

    private static MeshInstance3D CreateStackUnderlay(string name, Vector3 position) => new()
    {
        Name = name,
        Mesh = CardMesh,
        Position = position,
        MaterialOverride = NeutralFrame,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        Visible = false,
    };

    private static MeshInstance3D CreateBadge(string name, Mesh mesh, Vector3 position) => new()
    {
        Name = name,
        Mesh = mesh,
        Position = position,
        MaterialOverride = BadgeMaterial,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        Visible = false,
    };

    private static StandardMaterial3D SharedArtworkMaterial(Texture2D texture)
    {
        ulong key = texture.GetInstanceId();
        if (ArtworkMaterials.TryGetValue(key, out StandardMaterial3D? material))
        {
            return material;
        }

        material = new StandardMaterial3D
        {
            AlbedoTexture = texture,
            AlbedoColor = Colors.White,
            Roughness = 0.58f,
            Metallic = 0.04f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        ArtworkMaterials[key] = material;
        return material;
    }

    private static Label3D CreateTopLabel(string name, int fontSize, Vector3 position) => new()
    {
        Name = name,
        Font = GD.Load<Font>("res://assets/fonts/NotoSansCJKsc-Regular.otf"),
        FontSize = fontSize,
        PixelSize = 0.009f,
        OutlineSize = 7,
        OutlineModulate = new Color(0.01f, 0.02f, 0.035f, 0.96f),
        Modulate = Colors.White,
        Position = position,
        RotationDegrees = new Vector3(-90.0f, 0.0f, 0.0f),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        DoubleSided = true,
        NoDepthTest = false,
    };

    private static StandardMaterial3D CreateFrameMaterial(Color color) => new()
    {
        AlbedoColor = color,
        Metallic = 0.56f,
        Roughness = 0.35f,
    };

    private static StandardMaterial3D CreateBadgeMaterial() => new()
    {
        AlbedoColor = new Color(0.025f, 0.055f, 0.09f, 0.94f),
        Metallic = 0.62f,
        Roughness = 0.32f,
        EmissionEnabled = true,
        Emission = new Color(0.02f, 0.13f, 0.16f, 1.0f),
    };

    private static StandardMaterial3D CreateOutlineMaterial(Color color) => new()
    {
        AlbedoColor = color,
        Roughness = 0.42f,
        EmissionEnabled = true,
        Emission = color * 1.4f,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    private static CardBadgeReadabilityEvidence CreateBadgeEvidence(
        Label3D label,
        MeshInstance3D plate)
    {
        Aabb bounds = plate.Mesh?.GetAabb() ?? new Aabb();
        float plateTop = plate.Position.Y + bounds.End.Y;
        return new CardBadgeReadabilityEvidence(
            label.Text,
            label.Visible,
            plate.Visible,
            label.Position.Y,
            plateTop);
    }

    private static CardBadgeGpuEvidence CreateGpuBadgeEvidence(
        Camera3D camera,
        Label3D label,
        CardBadgeReadabilityEvidence local)
    {
        if (!label.Visible || string.IsNullOrEmpty(label.Text) ||
            camera.IsPositionBehind(label.GlobalPosition))
        {
            return new CardBadgeGpuEvidence(local, new Rect2());
        }

        Aabb bounds = label.GetAabb();
        Vector3 start = bounds.Position;
        Vector3 end = bounds.End;
        Vector2 minimum = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 maximum = new(float.NegativeInfinity, float.NegativeInfinity);
        for (int corner = 0; corner < 8; ++corner)
        {
            Vector3 localPoint = new(
                (corner & 1) == 0 ? start.X : end.X,
                (corner & 2) == 0 ? start.Y : end.Y,
                (corner & 4) == 0 ? start.Z : end.Z);
            Vector3 worldPoint = label.GlobalTransform * localPoint;
            if (camera.IsPositionBehind(worldPoint))
            {
                return new CardBadgeGpuEvidence(local, new Rect2());
            }
            Vector2 projected = camera.UnprojectPosition(worldPoint);
            minimum = new Vector2(
                MathF.Min(minimum.X, projected.X),
                MathF.Min(minimum.Y, projected.Y));
            maximum = new Vector2(
                MathF.Max(maximum.X, projected.X),
                MathF.Max(maximum.Y, projected.Y));
        }

        return new CardBadgeGpuEvidence(
            local,
            new Rect2(minimum, maximum - minimum));
    }

    private void ApplyShadowPolicy(BattlefieldCardLayout layout)
    {
        // Camera-locked hand actors live between the camera and the authored
        // table. Letting those oversized foreground actors cast into the world
        // produces huge black card-shaped blocks across the battlefield. Field
        // cards retain a small contact shadow; screen-space hands never cast.
        GeometryInstance3D.ShadowCastingSetting setting =
            layout is BattlefieldCardLayout.NearHand or BattlefieldCardLayout.FarHand
                ? GeometryInstance3D.ShadowCastingSetting.Off
                : GeometryInstance3D.ShadowCastingSetting.On;
        _baseMesh.CastShadow = setting;
        _stackUnderlayA.CastShadow = setting;
        _stackUnderlayB.CastShadow = setting;
        _costBadge.CastShadow = setting;
        _kindBadge.CastShadow = setting;
        _attackBadge.CastShadow = setting;
        _healthBadge.CastShadow = setting;
        _countdownBadge.CastShadow = setting;
    }

    private static bool ContainsToken(string? value, string token) =>
        value?.Contains(token, StringComparison.Ordinal) == true;

    private void DisposeCiPrivacyResources()
    {
        if (_ciPrivacyMaterial is not null &&
            ReferenceEquals(_artworkSurface?.MaterialOverride, _ciPrivacyMaterial))
        {
            _artworkSurface.MaterialOverride = null;
        }

        _ciPrivacyMaterial?.Dispose();
        _ciPrivacyTexture?.Dispose();
        _ciPrivacyMaterial = null;
        _ciPrivacyTexture = null;
    }

    private static void ScrubMetadata(Node node)
    {
        foreach (StringName key in node.GetMetaList())
        {
            node.RemoveMeta(key);
        }

        foreach (Node child in node.GetChildren())
        {
            ScrubMetadata(child);
        }
    }

    private static int CountMetadataToken(Node node, string token)
    {
        int count = 0;
        foreach (StringName key in node.GetMetaList())
        {
            string keyText = key.ToString();
            string valueText = node.GetMeta(key).ToString();
            count += ContainsToken(keyText, token) ? 1 : 0;
            count += ContainsToken(valueText, token) ? 1 : 0;
        }

        foreach (Node child in node.GetChildren())
        {
            count += CountMetadataToken(child, token);
        }

        return count;
    }
}

internal readonly record struct CardBadgeReadabilityEvidence(
    string Text,
    bool LabelVisible,
    bool PlateVisible,
    float LabelLocalY,
    float PlateTopLocalY)
{
    internal float DepthClearance => LabelLocalY - PlateTopLocalY;

    internal bool IsCleared =>
        string.IsNullOrEmpty(Text) && !LabelVisible && !PlateVisible;

    internal bool IsReadable(float minimumDepthClearance) =>
        !string.IsNullOrEmpty(Text) && LabelVisible && PlateVisible &&
        DepthClearance >= minimumDepthClearance;
}

internal sealed record CardReadabilityEvidence(
    bool KnownIdentity,
    BattlefieldCardLayout Layout,
    CardKind? Kind,
    int Cost,
    int Attack,
    int Health,
    int Countdown,
    string NameText,
    bool NameVisible,
    CardBadgeReadabilityEvidence CostBadge,
    CardBadgeReadabilityEvidence KindBadge,
    CardBadgeReadabilityEvidence AttackBadge,
    CardBadgeReadabilityEvidence HealthBadge,
    CardBadgeReadabilityEvidence CountdownBadge,
    float MinimumDepthClearance)
{
    internal bool MatchesExpectedComposition
    {
        get
        {
            if (!KnownIdentity)
            {
                return string.IsNullOrEmpty(NameText) && !NameVisible &&
                       CostBadge.IsCleared && KindBadge.IsCleared &&
                       AttackBadge.IsCleared && HealthBadge.IsCleared &&
                       CountdownBadge.IsCleared;
            }

            bool nameMatchesLayout = Layout == BattlefieldCardLayout.NearHand
                ? NameVisible && !string.IsNullOrWhiteSpace(NameText)
                : !NameVisible;
            bool common = nameMatchesLayout &&
                          CostBadge.Text == Cost.ToString(CultureInfo.InvariantCulture) &&
                          CostBadge.IsReadable(MinimumDepthClearance) &&
                          KindBadge.IsReadable(MinimumDepthClearance);
            if (!common)
            {
                return false;
            }

            return Kind switch
            {
                CardKind.Unit =>
                    AttackBadge.Text == Attack.ToString(CultureInfo.InvariantCulture) &&
                    HealthBadge.Text == Health.ToString(CultureInfo.InvariantCulture) &&
                    AttackBadge.IsReadable(MinimumDepthClearance) &&
                    HealthBadge.IsReadable(MinimumDepthClearance) &&
                    CountdownBadge.IsCleared,
                CardKind.Relic or CardKind.Trap when Countdown > 0 =>
                    AttackBadge.IsCleared && HealthBadge.IsCleared &&
                    CountdownBadge.Text == Countdown.ToString(CultureInfo.InvariantCulture) &&
                    CountdownBadge.IsReadable(MinimumDepthClearance),
                _ => AttackBadge.IsCleared && HealthBadge.IsCleared &&
                     CountdownBadge.IsCleared,
            };
        }
    }
}

internal readonly record struct CardBadgeGpuEvidence(
    CardBadgeReadabilityEvidence Local,
    Rect2 ScreenRect)
{
    internal bool IsReadable(float minimumPixelHeight) =>
        Local.IsReadable(0.012f) &&
        ScreenRect.Size.X > 0.0f &&
        ScreenRect.Size.Y >= minimumPixelHeight;
}

internal sealed record CardGpuReadabilityEvidence(
    CardReadabilityEvidence Local,
    CardBadgeGpuEvidence CostBadge,
    CardBadgeGpuEvidence KindBadge,
    CardBadgeGpuEvidence AttackBadge,
    CardBadgeGpuEvidence HealthBadge,
    CardBadgeGpuEvidence CountdownBadge)
{
    internal bool MatchesExpectedComposition(float minimumPixelHeight)
    {
        if (!Local.MatchesExpectedComposition)
        {
            return false;
        }
        if (!Local.KnownIdentity)
        {
            return true;
        }

        if (!CostBadge.IsReadable(minimumPixelHeight))
        {
            return false;
        }
        return Local.Kind switch
        {
            CardKind.Unit => AttackBadge.IsReadable(minimumPixelHeight) &&
                             HealthBadge.IsReadable(minimumPixelHeight),
            CardKind.Relic or CardKind.Trap when Local.Countdown > 0 =>
                CountdownBadge.IsReadable(minimumPixelHeight),
            _ => true,
        };
    }
}
