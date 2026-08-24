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
    private MeshInstance3D _statsBadge = null!;
    private Label3D _faceLabel = null!;
    private Label3D _pileLabel = null!;
    private Label3D _costLabel = null!;
    private Label3D _kindLabel = null!;
    private Label3D _statsLabel = null!;
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

    public BattlefieldSurfaceRef? Surface { get; private set; }

    public BattlefieldCardPresentation? CardPresentation { get; private set; }

    public Vector3 WorldAnchor => GlobalPosition + new Vector3(0.0f, 0.22f, 0.0f);

    public bool CanActivate { get; private set; }

    public bool CollisionEnabled => CollisionLayer != 0 && !_collision.Disabled;

    public string DisplayText => _displayText;

    public string StateText => _stateLabel?.Text ?? string.Empty;

    public bool OutlineVisible => _outlineMesh?.Visible == true;

    internal BattlefieldCardLayout CiLayout => _layout;

    internal float CiFaceLabelWorldWidth =>
        _faceLabel.Width * _faceLabel.PixelSize * GlobalTransform.Basis.X.Length();

    internal int CiFaceLineCount => string.IsNullOrEmpty(DisplayText)
        ? 0
        : DisplayText.Count(character => character == '\n') + 1;

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
        _statsBadge.Visible = false;
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
                     _statsLabel.Text, CardPresentation?.Name,
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
                layout is BattlefieldCardLayout.NearHand or BattlefieldCardLayout.FarHand ? 5 : 7)
            : string.Empty;
        _faceLabel.Visible = known && layout is BattlefieldCardLayout.NearHand;
        _pileLabel.Text = string.Empty;
        _pileLabel.Visible = false;
        _stackUnderlayA.Visible = false;
        _stackUnderlayB.Visible = false;
        _costLabel.Text = known ? presentation.Cost.ToString(CultureInfo.InvariantCulture) : string.Empty;
        _kindLabel.Text = known ? KindLabel(presentation.Kind) : string.Empty;
        _statsLabel.Text = known ? StatsLabel(presentation) : string.Empty;
        _costLabel.Visible = known;
        _kindLabel.Visible = known;
        _statsLabel.Visible = known && !string.IsNullOrEmpty(_statsLabel.Text);
        _costBadge.Visible = known;
        _kindBadge.Visible = known;
        _statsBadge.Visible = known && !string.IsNullOrEmpty(_statsLabel.Text);
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

    private static string StatsLabel(BattlefieldCardPresentation card) => card.Kind switch
    {
        CardKind.Unit => $"{card.Attack} / {card.Health}",
        CardKind.Relic or CardKind.Trap when card.Countdown > 0 => $"倒计时 {card.Countdown}",
        _ => string.Empty,
    };

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
                BattlefieldCardLayout.NearHand => (27, 0.0088f, 5, 124.0f),
                BattlefieldCardLayout.FarHand => (25, 0.0085f, 4, 120.0f),
                BattlefieldCardLayout.Pile => (28, 0.0092f, 5, 126.0f),
                _ => (28, 0.0091f, 5, 128.0f),
            };
        _faceLabel.Position = new Vector3(0.0f, 0.073f, 0.63f);

        int badgeSize = layout == BattlefieldCardLayout.NearHand ? 24 : 26;
        _costLabel.FontSize = badgeSize;
        _statsLabel.FontSize = badgeSize;
        _kindLabel.FontSize = Math.Max(18, badgeSize - 5);
        _stateLabel.FontSize = badgeSize;
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
                     _faceLabel, _pileLabel, _costLabel, _kindLabel, _statsLabel, _stateLabel,
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

        _costBadge = CreateBadge("CostBadgePlate", RoundBadgeMesh, new Vector3(-0.57f, 0.067f, -0.84f));
        AddChild(_costBadge);
        _kindBadge = CreateBadge("KindBadgePlate", PillBadgeMesh, new Vector3(0.45f, 0.067f, -0.84f));
        AddChild(_kindBadge);
        _statsBadge = CreateBadge("StatsBadgePlate", PillBadgeMesh, new Vector3(0.43f, 0.067f, 0.84f));
        AddChild(_statsBadge);

        _collision = new CollisionShape3D
        {
            Name = "PickShape",
            Shape = new BoxShape3D { Size = new Vector3(1.66f, 0.28f, 2.26f) },
            Position = new Vector3(0.0f, 0.1f, 0.0f),
        };
        AddChild(_collision);

        _faceLabel = CreateTopLabel("FaceLabel", 28, new Vector3(0.0f, 0.073f, 0.63f));
        _faceLabel.Width = 128.0f;
        AddChild(_faceLabel);

        _pileLabel = CreateTopLabel("PileBadge", 23, new Vector3(0.0f, 0.091f, 0.66f));
        _pileLabel.Width = 132.0f;
        _pileLabel.Modulate = new Color("efffff");
        AddChild(_pileLabel);

        _costLabel = CreateTopLabel("CostBadge", 26, new Vector3(-0.57f, 0.079f, -0.84f));
        _costLabel.Width = 36.0f;
        _costLabel.Modulate = new Color("ddfff9");
        AddChild(_costLabel);

        _kindLabel = CreateTopLabel("KindBadge", 20, new Vector3(0.45f, 0.079f, -0.84f));
        _kindLabel.Width = 56.0f;
        _kindLabel.Modulate = new Color("d7e4ed");
        AddChild(_kindLabel);

        _statsLabel = CreateTopLabel("StatsBadge", 25, new Vector3(0.43f, 0.079f, 0.84f));
        _statsLabel.Width = 64.0f;
        _statsLabel.Modulate = new Color("fff0b2");
        AddChild(_statsLabel);

        _stateLabel = CreateTopLabel("StateLabel", 26, new Vector3(0.59f, 0.095f, -0.70f));
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
