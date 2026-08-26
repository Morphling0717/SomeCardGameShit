// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using Godot;
using Scgs.Client;
using Scgs.GodotClient.CardFaces;
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
    private const float ProductFaceDepth = BattlefieldPerspective.CardWidth /
                                           CardFaceLayout.CardAspectRatio;
    private const float ProductArtworkY = 0.058f;
    private const float ProductMaterialY = 0.061f;
    private const float ProductFrameY = 0.064f;
    private const float ProductRarityY = 0.067f;
    private const float ProductFoilY = 0.0685f;
    private const float ProductVariantY = 0.070f;
    private const float ProductNamePlateY = 0.073f;
    private const float ProductSocketY = 0.078f;
    // Keep glyphs safely above the 0.078 socket layer while limiting the
    // perspective parallax between text and its authored nameplate/gem.
    private const float ProductLabelY = 0.094f;

    private static readonly Color LegalColor = new("55ead0");
    private static readonly Color DestinationColor = new("ffc35d");
    private static readonly Color SelectedColor = new("ff765f");
    private static readonly Color MidrangeFrameColor = new("285d7c");
    private static readonly Color AdvanceFrameColor = new("675084");
    private static readonly Color NeutralFrameColor = new("2b4052");
    private static readonly Color HiddenFrameColor = new("101d31");
    private static readonly CardFaceRect FullProductRect = new(0.0f, 0.0f, 1.0f, 1.0f);
    private static readonly ArenaVisualProfile R3Profile =
        ArenaVisualProfile.R3Candidate;

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
    private static readonly QuadMesh ProductLayerMesh = new()
    {
        Size = Vector2.One,
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
    private static readonly StandardMaterial3D R3MidrangeFrame =
        CreateCandidateFrameMaterial(R3Profile.MidrangeMetal);
    private static readonly StandardMaterial3D R3AdvanceFrame =
        CreateCandidateFrameMaterial(R3Profile.AdvanceMetal);
    private static readonly StandardMaterial3D R3NeutralFrame =
        CreateCandidateFrameMaterial(R3Profile.NeutralMetal);
    private static readonly StandardMaterial3D R3HiddenFrame =
        CreateCandidateFrameMaterial(R3Profile.HiddenMetal);
    private static readonly StandardMaterial3D R3LegalOutline =
        CreateCandidateOutlineMaterial(R3Profile.FunctionalAccent);
    private static readonly StandardMaterial3D R3DestinationOutline =
        CreateCandidateOutlineMaterial(R3Profile.DestinationAccent);
    private static readonly StandardMaterial3D R3SelectedOutline =
        CreateCandidateOutlineMaterial(R3Profile.SelectedAccent);
    private static readonly StandardMaterial3D R3BadgeMaterial = CreateCandidateBadgeMaterial();
    private static readonly StandardMaterial3D ProductNeutralBase =
        CreateProductBaseMaterial(new Color("241c35"));
    private static readonly StandardMaterial3D ProductOathguardBase =
        CreateProductBaseMaterial(new Color("282334"));
    private static readonly StandardMaterial3D ProductPactmageBase =
        CreateProductBaseMaterial(new Color("24142f"));
    private static readonly Dictionary<(ulong TextureId, BattlefieldVisualProfile Profile),
        StandardMaterial3D> ArtworkMaterials = [];
    private static readonly Dictionary<string, StandardMaterial3D> ProductLayerMaterials =
        new(StringComparer.Ordinal);
    private static readonly Shader ProductArtworkMaskShader = new()
    {
        ResourceName = "AnimeV1 integrated artwork mask",
        Code = """
            shader_type spatial;
            render_mode unshaded, cull_disabled, depth_prepass_alpha;

            uniform sampler2D art_texture : source_color, filter_linear_mipmap_anisotropic;
            uniform sampler2D frame_mask : source_color, filter_linear_mipmap_anisotropic;
            uniform vec4 art_crop = vec4(0.0, 0.0, 1.0, 1.0);
            uniform vec4 frame_region = vec4(0.0, 0.0, 1.0, 1.0);
            uniform float layer_opacity : hint_range(0.0, 1.0) = 1.0;

            void fragment() {
                vec2 source_uv = art_crop.xy + (UV * art_crop.zw);
                vec2 mask_uv = frame_region.xy + (UV * frame_region.zw);
                vec4 art = texture(art_texture, source_uv);
                float silhouette = texture(frame_mask, mask_uv).a;
                ALBEDO = art.rgb;
                ALPHA = art.a * layer_opacity * smoothstep(0.04, 0.10, silhouette);
            }
            """,
    };
    private static readonly Font LegacyCardFont =
        GD.Load<Font>("res://assets/fonts/NotoSansCJKsc-Regular.otf");
    private static readonly Font ProductCardFont = LoadProductCardFont();

    private ICardVisualCatalog _visualCatalog = CardVisualCatalog.Shared;
    private ArenaVisualProfile _visualProfile = ArenaVisualProfile.Gate4BR2;
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
    private MeshInstance3D _productArtwork = null!;
    private MeshInstance3D _productMaterial = null!;
    private MeshInstance3D _productFrame = null!;
    private MeshInstance3D _productRarity = null!;
    private MeshInstance3D _productFoil = null!;
    private MeshInstance3D _productVariant = null!;
    private MeshInstance3D _productCrest = null!;
    private MeshInstance3D _productNamePlate = null!;
    private MeshInstance3D _productCostGem = null!;
    private MeshInstance3D _productAttackGem = null!;
    private MeshInstance3D _productHealthGem = null!;
    private MeshInstance3D _productCountdownGem = null!;
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
    private CardFaceComposition? _productFace;
    private ShaderMaterial? _productArtworkMaterial;
    private ShaderMaterial? _productSurfaceMaterial;
    private ShaderMaterial? _productFoilMaterial;

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

    internal CardFaceComposition? CiProductFace => _productFace;

    internal bool CiUsesIntegratedProductFace
    {
        get
        {
            EnsureBuilt();
            if (_productFace is not { } face)
            {
                return false;
            }

            CardFrameStyle style = face.FrameStyle;
            return _showsIdentity &&
                   ProductLayerIsBound(_productArtwork) &&
                   ProductLayerMatches(_productMaterial, style.MaterialTexturePath) &&
                   ProductLayerMatches(_productFrame, style.SilhouettePath) &&
                   ProductLayerMatches(_productRarity, style.RarityOverlayPath) &&
                   ProductLayerMatches(_productFoil, style.FoilTexturePath) &&
                   ProductLayerMatches(_productVariant, style.VariantOverlayPath) &&
                   ProductLayerMatches(_productCrest, style.CrestPath) &&
                   ProductLayerMatches(_productNamePlate, style.NamePlatePath) &&
                   ProductLayerMatches(_productCostGem, style.CostGemPath) &&
                   ProductOptionalLayerMatches(
                       _productAttackGem,
                       face.Layout.AttackGem,
                       face.ViewModel.Attack,
                       style.AttackGemPath) &&
                   ProductOptionalLayerMatches(
                       _productHealthGem,
                       face.Layout.HealthGem,
                       face.ViewModel.Health,
                       style.HealthGemPath) &&
                   ProductOptionalLayerMatches(
                       _productCountdownGem,
                       face.Layout.CountdownGem,
                       face.ViewModel.Countdown,
                       style.CountdownGemPath) &&
                   !_kindBadge.Visible && !_kindLabel.Visible &&
                   !_costBadge.Visible && !_attackBadge.Visible && !_healthBadge.Visible &&
                   !_countdownBadge.Visible &&
                   CiReadabilityEvidence.MatchesExpectedComposition;
        }
    }

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
            CardFaceViewModel? product = _productFace?.ViewModel;
            bool usesProductFace = product is not null;
            return new CardReadabilityEvidence(
                _showsIdentity,
                usesProductFace,
                _layout,
                CardPresentation?.Kind,
                product?.Kind,
                product?.Cost ?? CardPresentation?.Cost ?? 0,
                product?.Attack ?? CardPresentation?.Attack ?? 0,
                product?.Health ?? CardPresentation?.Health ?? 0,
                product?.Countdown ?? CardPresentation?.Countdown ?? 0,
                usesProductFace && product!.Attack.HasValue,
                usesProductFace && product!.Health.HasValue,
                usesProductFace && product!.Countdown.HasValue,
                _faceLabel.Text,
                _faceLabel.Visible,
                usesProductFace && ProductLayerIsBound(_productCrest),
                usesProductFace
                    ? CreateProductBadgeEvidence(_costLabel, _productCostGem)
                    : CreateBadgeEvidence(_costLabel, _costBadge),
                CreateBadgeEvidence(_kindLabel, _kindBadge),
                usesProductFace
                    ? CreateProductBadgeEvidence(_attackLabel, _productAttackGem)
                    : CreateBadgeEvidence(_attackLabel, _attackBadge),
                usesProductFace
                    ? CreateProductBadgeEvidence(_healthLabel, _productHealthGem)
                    : CreateBadgeEvidence(_healthLabel, _healthBadge),
                usesProductFace
                    ? CreateProductBadgeEvidence(_countdownLabel, _productCountdownGem)
                    : CreateBadgeEvidence(_countdownLabel, _countdownBadge),
                MinimumBadgeDepthClearance);
        }
    }

    internal bool CiHasReadableComposition =>
        CiReadabilityEvidence.MatchesExpectedComposition;

    internal CardGpuReadabilityEvidence CiGpuReadabilityEvidence(Camera3D camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        CardReadabilityEvidence local = CiReadabilityEvidence;
        CardFaceLayout? productLayout = _productFace?.Layout;
        return new CardGpuReadabilityEvidence(
            local,
            CreateGpuBadgeEvidence(
                camera,
                _costLabel,
                local.CostBadge,
                productLayout?.CostGem),
            CreateGpuBadgeEvidence(camera, _kindLabel, local.KindBadge),
            CreateGpuBadgeEvidence(
                camera,
                _attackLabel,
                local.AttackBadge,
                productLayout?.AttackGem),
            CreateGpuBadgeEvidence(
                camera,
                _healthLabel,
                local.HealthBadge,
                productLayout?.HealthGem),
            CreateGpuBadgeEvidence(
                camera,
                _countdownLabel,
                local.CountdownBadge,
                productLayout?.CountdownGem));
    }

    internal CardNameGpuEvidence CiProductNameGpuEvidence(Camera3D camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (_productFace is not { } product || !_faceLabel.Visible)
        {
            return new CardNameGpuEvidence(string.Empty, 0, new Rect2(), new Rect2(), new Rect2());
        }
        return new CardNameGpuEvidence(
            _faceLabel.Text,
            _faceLabel.FontSize,
            ProjectLabelScreenRect(camera, _faceLabel),
            ProjectProductRect(camera, product.Layout.NameText),
            ProjectProductRect(camera, product.Layout.NamePlate));
    }

    internal bool CiProductRectangularBaseHidden =>
        _productFace is not null && !_baseMesh.Visible;

    internal IReadOnlyList<CardSilhouetteGpuProbe> CiProductSilhouetteGpuProbes(
        Camera3D camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (_productFace is null)
        {
            return [];
        }

        // These side probes sit outside every authored silhouette (all five
        // frame paths begin at least 54/900 from the side) and avoid the cost,
        // crest and stat sockets that intentionally protrude at the corners.
        (string Name, float U, float V)[] probes =
        [
            ("upper-left-edge", 0.014f, 0.38f),
            ("upper-right-edge", 0.986f, 0.38f),
            ("lower-left-edge", 0.014f, 0.62f),
            ("lower-right-edge", 0.986f, 0.62f),
        ];
        return probes.Select(probe => new CardSilhouetteGpuProbe(
            probe.Name,
            ProjectProductPoint(camera, probe.U, probe.V),
            ProjectProductPoint(camera, probe.U, probe.V))).ToArray();
    }

    internal Vector2 CiProductInteriorGpuPosition(Camera3D camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        return _productFace is null
            ? new Vector2(float.NaN, float.NaN)
            : ProjectProductPoint(camera, 0.5f, 0.43f);
    }

    internal void CiSetProductLayersVisible(bool visible)
    {
        foreach (MeshInstance3D layer in ProductLayers)
        {
            layer.Visible = visible && layer.MaterialOverride is not null;
        }
    }

    internal void CiSetProductMaskedSurfaceLayersVisible(bool visible)
    {
        foreach (MeshInstance3D layer in new[]
                 {
                     _productArtwork, _productMaterial, _productFoil,
                 })
        {
            layer.Visible = visible && layer.MaterialOverride is not null;
        }
    }

    internal void CiSetProductValueLabelsVisible(bool visible)
    {
        CardFaceViewModel? view = _productFace?.ViewModel;
        _costLabel.Visible = visible && view is not null;
        _attackLabel.Visible = visible && view?.Attack.HasValue == true;
        _healthLabel.Visible = visible && view?.Health.HasValue == true;
        _countdownLabel.Visible = visible && view?.Countdown.HasValue == true;
    }

    internal void CiSetProductNameLabelVisible(bool visible)
    {
        _faceLabel.Visible = visible && _productFace is not null && _showsIdentity &&
                             !string.IsNullOrWhiteSpace(_faceLabel.Text);
    }

    internal bool CiHasPrivacyTextureSentinel(string token) =>
        _ciPrivacyMaterial is { } material &&
        _ciPrivacyTexture is { } texture &&
        ReferenceEquals(_artworkSurface?.MaterialOverride, material) &&
        material.ResourceName == token && texture.ResourceName == token;

    internal bool CiUsesSharedCardBack
    {
        get
        {
            EnsureBuilt();
            return Visible && !_showsIdentity &&
                   ReferenceEquals(
                       _artworkSurface.MaterialOverride,
                       SharedArtworkMaterial(_visualCatalog.CardBack, _visualProfile));
        }
    }

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

    internal void ConfigureVisualProfile(BattlefieldVisualProfile profile)
    {
        if (Visible && CardPresentation is not null)
        {
            throw new InvalidOperationException("Visual profiles can only change while an actor is pooled.");
        }

        _visualProfile = ArenaVisualProfile.Resolve(profile);
        if (_baseMesh is not null)
        {
            ApplyVisualProfileMaterials();
        }
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
            _artworkSurface.MaterialOverride = SharedArtworkMaterial(
                _visualCatalog.FallbackFront,
                _visualProfile);
            _baseMesh.MaterialOverride = NeutralFrameMaterial;
        }

        _faceLabel.Text = string.Empty;
        _faceLabel.Visible = false;
        _pileLabel.Text = $"{title}  {count}";
        _pileLabel.Visible = true;
        _stackUnderlayA.MaterialOverride = hidden ? HiddenFrameMaterial : NeutralFrameMaterial;
        _stackUnderlayB.MaterialOverride = hidden ? HiddenFrameMaterial : NeutralFrameMaterial;
        _stackUnderlayA.Visible = count >= 2;
        _stackUnderlayB.Visible = count >= 5;
        if (_visualProfile.UsesOpenArena)
        {
            _pileLabel.FontSize = 20;
            _pileLabel.Modulate = new Color("d9cfbd");
            _pileLabel.Position = new Vector3(0.0f, 0.106f, 0.72f);
        }
    }

    /// <summary>
    /// Binds the AnimeV1 product face as layered 3D quads.  Artwork, frame,
    /// crest, rarity, variant and gem sockets are composed directly in the
    /// battlefield viewport; no per-card SubViewport is created.
    /// </summary>
    internal void BindProductFace(
        CardFaceComposition composition,
        Transform3D transform,
        BattlefieldCardLayout layout,
        BattlefieldSurfaceRef? surface = null)
    {
        ArgumentNullException.ThrowIfNull(composition);
        EnsureBuilt();
        ValidateProductContext(composition.Layout.Context, layout);
        CancelHoverTween();
        DisposeCiPrivacyResources();
        ClearProductFace();
        ApplyShadowPolicy(layout);

        _layout = layout;
        Surface = surface;
        _boundSurface = surface;
        CardPresentation = null;
        _restTransform = transform;
        Transform = transform;
        _pointerHovered = false;
        _highlight = BattlefieldHighlightKind.None;
        CanActivate = false;
        _showsIdentity = true;
        _productFace = composition;
        _displayText = FormatProductCard(composition);

        // The legacy BoxMesh is rectangular and cannot follow all five ornate
        // silhouettes. Keep it out of the product render entirely; collision
        // remains the stable full-card target. Legacy and hidden binds restore
        // the mesh through ClearProductFace.
        _baseMesh.Scale = Vector3.One;
        _baseMesh.MaterialOverride = ProductBaseMaterial(composition.ViewModel.Faction);
        _baseMesh.Visible = false;
        _artworkSurface.Visible = false;
        _pileLabel.Visible = false;
        _stackUnderlayA.Visible = false;
        _stackUnderlayB.Visible = false;

        BindProductArtwork(
            _productArtwork,
            composition.Layout.ArtWindow,
            composition.ArtPath,
            composition.FrameStyle.SilhouettePath,
            ProductArtworkY,
            composition.ArtCrop);
        BindProductMaskedLayer(
            _productMaterial,
            FullProductRect,
            composition.FrameStyle.MaterialTexturePath,
            composition.FrameStyle.SilhouettePath,
            ProductMaterialY,
            ref _productSurfaceMaterial,
            "material",
            opacity: 0.10f);
        BindProductLayer(
            _productFrame,
            FullProductRect,
            composition.FrameStyle.SilhouettePath,
            ProductFrameY);
        BindProductLayer(
            _productRarity,
            FullProductRect,
            composition.FrameStyle.RarityOverlayPath,
            ProductRarityY);
        BindProductMaskedLayer(
            _productFoil,
            FullProductRect,
            composition.FrameStyle.FoilTexturePath,
            composition.FrameStyle.SilhouettePath,
            ProductFoilY,
            ref _productFoilMaterial,
            "foil",
            opacity: 0.16f);
        BindProductLayer(
            _productVariant,
            FullProductRect,
            composition.FrameStyle.VariantOverlayPath,
            ProductVariantY);
        BindProductLayer(
            _productCrest,
            composition.Layout.TypeCrest,
            composition.FrameStyle.CrestPath,
            ProductSocketY);
        BindProductLayer(
            _productNamePlate,
            composition.Layout.NamePlate,
            composition.FrameStyle.NamePlatePath,
            ProductNamePlateY,
            transparent: true);
        BindProductLayer(
            _productCostGem,
            composition.Layout.CostGem,
            composition.FrameStyle.CostGemPath,
            ProductSocketY);
        BindOptionalProductLayer(
            _productAttackGem,
            composition.Layout.AttackGem,
            composition.ViewModel.Attack,
            composition.FrameStyle.AttackGemPath);
        BindOptionalProductLayer(
            _productHealthGem,
            composition.Layout.HealthGem,
            composition.ViewModel.Health,
            composition.FrameStyle.HealthGemPath);
        BindOptionalProductLayer(
            _productCountdownGem,
            composition.Layout.CountdownGem,
            composition.ViewModel.Countdown,
            composition.FrameStyle.CountdownGemPath);

        ApplyProductLabels(composition);
        foreach (MeshInstance3D oldPlate in LegacyBadgePlates)
        {
            oldPlate.Visible = false;
        }
        _kindLabel.Text = string.Empty;
        _kindLabel.Visible = false;
        _stateLabel.Text = string.Empty;
        _stateLabel.Visible = false;
        _outlineMesh.Visible = false;
        _collision.Disabled = !surface.HasValue;
        CollisionLayer = surface.HasValue ? PickCollisionLayer : 0;
        Visible = true;
        UpdateHighlight();
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
        ApplyVisualProfileMaterials();
        CancelHoverTween();
        ClearProductFace();
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
        _baseMesh.Scale = Vector3.One;
        ClearLabels();
        _baseMesh.MaterialOverride = NeutralFrameMaterial;
        _artworkSurface.MaterialOverride = null;
        DisposeCiPrivacyResources();
        _artworkSurface.Visible = false;
        _stackUnderlayA.MaterialOverride = NeutralFrameMaterial;
        _stackUnderlayB.MaterialOverride = NeutralFrameMaterial;
        _stackUnderlayA.Visible = false;
        _stackUnderlayB.Visible = false;
        _costBadge.Visible = false;
        _kindBadge.Visible = false;
        _attackBadge.Visible = false;
        _healthBadge.Visible = false;
        _countdownBadge.Visible = false;
        _outlineMesh.Visible = false;
        _outlineMesh.MaterialOverride = LegalOutlineMaterial;
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
                     _productFace?.ViewModel.DesignId,
                     _productFace?.ViewModel.DisplayName,
                     _productFace?.ArtPath,
                     _baseMesh.MaterialOverride?.ResourceName,
                     _artworkSurface.MaterialOverride?.ResourceName,
                     _outlineMesh.MaterialOverride?.ResourceName,
                 })
        {
            count += ContainsToken(value, token) ? 1 : 0;
        }

        foreach (MeshInstance3D layer in ProductLayers)
        {
            count += ContainsToken(layer.MaterialOverride?.ResourceName, token) ? 1 : 0;
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
        ClearProductFace();
        _baseMesh.Scale = Vector3.One;
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
        _artworkSurface.MaterialOverride = SharedArtworkMaterial(faceTexture, _visualProfile);
        _artworkSurface.Visible = true;
        _baseMesh.MaterialOverride = known
            ? FrameMaterialFor(CardVisualCatalog.FactionFor(presentation.DefinitionId))
            : HiddenFrameMaterial;

        _displayText = known ? FormatKnownCard(presentation, layout) : string.Empty;
        _faceLabel.Text = known ? NormalizeCardName(presentation.Name) : string.Empty;
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
            string handName = NormalizeCardName(card.Name);
            return card.Kind switch
            {
                CardKind.Unit => $"{card.Cost}费 {card.Attack}/{card.Health}\n{handName}",
                CardKind.Relic when card.Countdown > 0 =>
                    $"{card.Cost}费 倒{card.Countdown}\n{handName}",
                _ => $"{card.Cost}费\n{handName}",
            };
        }

        string name = NormalizeCardName(card.Name);
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

    private static string NormalizeCardName(string name) =>
        string.IsNullOrWhiteSpace(name) ? "—" : name.Trim();

    private void ApplyLabelLayout(BattlefieldCardLayout layout)
    {
        foreach (Label3D label in FaceLabels)
        {
            label.Font = LegacyCardFont;
        }
        bool candidateNearHand = _visualProfile.Id == BattlefieldVisualProfile.R3Candidate &&
                                 layout == BattlefieldCardLayout.NearHand;
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
            BattlefieldCardLayout.NearHand => candidateNearHand ? 25 : 28,
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
        float fieldPlateScale = layout == BattlefieldCardLayout.Field
            ? 1.22f
            : candidateNearHand ? 0.86f : 1.0f;
        Vector3 roundScale = new(fieldPlateScale, 1.0f, fieldPlateScale);
        _costBadge.Scale = roundScale;
        _attackBadge.Scale = roundScale;
        _healthBadge.Scale = roundScale;
        _kindBadge.Scale = candidateNearHand
            ? new Vector3(0.9f, 1.0f, 0.9f)
            : Vector3.One;
        _countdownBadge.Scale = layout == BattlefieldCardLayout.Field
            ? new Vector3(1.18f, 1.0f, 1.18f)
            : candidateNearHand ? new Vector3(0.88f, 1.0f, 0.88f) : Vector3.One;
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
            BattlefieldHighlightKind.Selected => SelectedOutlineMaterial,
            BattlefieldHighlightKind.Destination => DestinationOutlineMaterial,
            _ => LegalOutlineMaterial,
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
            MaterialOverride = LegalOutlineMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_outlineMesh);

        _baseMesh = new MeshInstance3D
        {
            Name = "CardBase",
            Mesh = CardMesh,
            MaterialOverride = NeutralFrameMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        AddChild(_baseMesh);

        _stackUnderlayA = CreateStackUnderlay(
            "StackUnderlayA",
            new Vector3(0.075f, -0.105f, 0.075f),
            NeutralFrameMaterial);
        AddChild(_stackUnderlayA);
        _stackUnderlayB = CreateStackUnderlay(
            "StackUnderlayB",
            new Vector3(0.14f, -0.17f, 0.14f),
            NeutralFrameMaterial);
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

        _productArtwork = CreateProductLayer("ProductArtwork");
        _productMaterial = CreateProductLayer("ProductMaterial");
        _productFrame = CreateProductLayer("ProductFrame");
        _productRarity = CreateProductLayer("ProductRarity");
        _productFoil = CreateProductLayer("ProductFoil");
        _productVariant = CreateProductLayer("ProductVariant");
        _productCrest = CreateProductLayer("ProductCrest");
        _productNamePlate = CreateProductLayer("ProductNamePlate");
        _productCostGem = CreateProductLayer("ProductCostGem");
        _productAttackGem = CreateProductLayer("ProductAttackGem");
        _productHealthGem = CreateProductLayer("ProductHealthGem");
        _productCountdownGem = CreateProductLayer("ProductCountdownGem");
        foreach (MeshInstance3D layer in ProductLayers)
        {
            AddChild(layer);
        }

        _costBadge = CreateBadge(
            "CostBadgePlate",
            RoundBadgeMesh,
            new Vector3(-0.57f, BadgePlateCenterY, -0.84f),
            BadgePlateMaterial);
        AddChild(_costBadge);
        _kindBadge = CreateBadge(
            "KindBadgePlate",
            PillBadgeMesh,
            new Vector3(0.45f, BadgePlateCenterY, -0.84f),
            BadgePlateMaterial);
        AddChild(_kindBadge);
        _attackBadge = CreateBadge(
            "AttackBadgePlate",
            RoundBadgeMesh,
            new Vector3(-0.57f, BadgePlateCenterY, 0.84f),
            BadgePlateMaterial);
        AddChild(_attackBadge);
        _healthBadge = CreateBadge(
            "HealthBadgePlate",
            RoundBadgeMesh,
            new Vector3(0.57f, BadgePlateCenterY, 0.84f),
            BadgePlateMaterial);
        AddChild(_healthBadge);
        _countdownBadge = CreateBadge(
            "CountdownBadgePlate",
            PillBadgeMesh,
            new Vector3(0.43f, BadgePlateCenterY, 0.84f),
            BadgePlateMaterial);
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

    private StandardMaterial3D FrameMaterialFor(CardVisualFaction faction) =>
        (_visualProfile.Id, faction) switch
        {
            (BattlefieldVisualProfile.R3Candidate, CardVisualFaction.Midrange) => R3MidrangeFrame,
            (BattlefieldVisualProfile.R3Candidate, CardVisualFaction.Advance) => R3AdvanceFrame,
            (BattlefieldVisualProfile.R3Candidate, _) => R3NeutralFrame,
            (_, CardVisualFaction.Midrange) => MidrangeFrame,
            (_, CardVisualFaction.Advance) => AdvanceFrame,
            _ => NeutralFrame,
        };

    private StandardMaterial3D NeutralFrameMaterial =>
        _visualProfile.Id == BattlefieldVisualProfile.R3Candidate
            ? R3NeutralFrame
            : NeutralFrame;

    private StandardMaterial3D HiddenFrameMaterial =>
        _visualProfile.Id == BattlefieldVisualProfile.R3Candidate
            ? R3HiddenFrame
            : HiddenFrame;

    private StandardMaterial3D BadgePlateMaterial =>
        _visualProfile.Id == BattlefieldVisualProfile.R3Candidate
            ? R3BadgeMaterial
            : BadgeMaterial;

    private StandardMaterial3D LegalOutlineMaterial =>
        _visualProfile.Id == BattlefieldVisualProfile.R3Candidate
            ? R3LegalOutline
            : LegalOutline;

    private StandardMaterial3D DestinationOutlineMaterial =>
        _visualProfile.Id == BattlefieldVisualProfile.R3Candidate
            ? R3DestinationOutline
            : DestinationOutline;

    private StandardMaterial3D SelectedOutlineMaterial =>
        _visualProfile.Id == BattlefieldVisualProfile.R3Candidate
            ? R3SelectedOutline
            : SelectedOutline;

    private static MeshInstance3D CreateStackUnderlay(
        string name,
        Vector3 position,
        Material material) =>
        new()
        {
            Name = name,
            Mesh = CardMesh,
            Position = position,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            Visible = false,
        };

    private static MeshInstance3D CreateBadge(
        string name,
        Mesh mesh,
        Vector3 position,
        Material material) =>
        new()
        {
            Name = name,
            Mesh = mesh,
            Position = position,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };

    private static MeshInstance3D CreateProductLayer(string name) => new()
    {
        Name = name,
        Mesh = ProductLayerMesh,
        RotationDegrees = new Vector3(-90.0f, 0.0f, 0.0f),
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        Visible = false,
    };

    private MeshInstance3D[] ProductLayers =>
    [
        _productArtwork,
        _productMaterial,
        _productFrame,
        _productRarity,
        _productFoil,
        _productVariant,
        _productCrest,
        _productNamePlate,
        _productCostGem,
        _productAttackGem,
        _productHealthGem,
        _productCountdownGem,
    ];

    private MeshInstance3D[] LegacyBadgePlates =>
    [
        _costBadge,
        _kindBadge,
        _attackBadge,
        _healthBadge,
        _countdownBadge,
    ];

    private Label3D[] FaceLabels =>
    [
        _faceLabel,
        _costLabel,
        _kindLabel,
        _attackLabel,
        _healthLabel,
        _countdownLabel,
    ];

    private static bool ProductLayerIsBound(MeshInstance3D layer) =>
        layer.Visible && layer.Mesh is not null && layer.MaterialOverride is not null;

    private static bool ProductLayerMatches(MeshInstance3D layer, string? texturePath) =>
        string.IsNullOrWhiteSpace(texturePath)
            ? !layer.Visible && layer.MaterialOverride is null
            : ProductLayerIsBound(layer) &&
              ContainsToken(layer.MaterialOverride?.ResourceName, texturePath);

    private static bool ProductOptionalLayerMatches(
        MeshInstance3D layer,
        CardFaceRect? socket,
        int? value,
        string texturePath) =>
        socket.HasValue && value.HasValue
            ? ProductLayerMatches(layer, texturePath)
            : !layer.Visible && layer.MaterialOverride is null;

    private void BindOptionalProductLayer(
        MeshInstance3D layer,
        CardFaceRect? socket,
        int? value,
        string texturePath)
    {
        if (socket is not { } rect || !value.HasValue)
        {
            layer.Visible = false;
            layer.MaterialOverride = null;
            return;
        }
        BindProductLayer(layer, rect, texturePath, ProductSocketY);
    }

    private void BindProductArtwork(
        MeshInstance3D layer,
        CardFaceRect rect,
        string texturePath,
        string maskPath,
        float height,
        CardArtCrop crop)
    {
        if (!ResourceLoader.Exists(texturePath, "Texture2D") ||
            !ResourceLoader.Exists(maskPath, "Texture2D"))
        {
            layer.Visible = false;
            layer.MaterialOverride = null;
            return;
        }

        PositionProductLayer(layer, rect, height);
        layer.MaterialOverride = ConfigureProductMaskedMaterial(
            ref _productArtworkMaterial,
            "artwork",
            texturePath,
            maskPath,
            crop,
            rect,
            1.0f);
        layer.Visible = true;
    }

    private void BindProductMaskedLayer(
        MeshInstance3D layer,
        CardFaceRect rect,
        string? texturePath,
        string maskPath,
        float height,
        ref ShaderMaterial? actorMaterial,
        string layerName,
        float opacity)
    {
        if (string.IsNullOrWhiteSpace(texturePath) ||
            !ResourceLoader.Exists(texturePath, "Texture2D") ||
            !ResourceLoader.Exists(maskPath, "Texture2D"))
        {
            layer.Visible = false;
            layer.MaterialOverride = null;
            ClearProductMaskedMaterial(ref actorMaterial, layerName);
            return;
        }

        PositionProductLayer(layer, rect, height);
        layer.MaterialOverride = ConfigureProductMaskedMaterial(
            ref actorMaterial,
            layerName,
            texturePath,
            maskPath,
            new CardArtCrop(0.0f, 0.0f, 1.0f, 1.0f),
            rect,
            opacity);
        layer.Visible = true;
    }

    private static void BindProductLayer(
        MeshInstance3D layer,
        CardFaceRect rect,
        string? texturePath,
        float height,
        CardArtCrop? crop = null,
        bool transparent = true,
        float opacity = 1.0f)
    {
        if (string.IsNullOrWhiteSpace(texturePath) ||
            !ResourceLoader.Exists(texturePath, "Texture2D"))
        {
            layer.Visible = false;
            layer.MaterialOverride = null;
            return;
        }

        PositionProductLayer(layer, rect, height);
        layer.MaterialOverride = ProductLayerMaterial(texturePath, crop, transparent, opacity);
        layer.Visible = true;
    }

    private static void PositionProductLayer(
        MeshInstance3D layer,
        CardFaceRect rect,
        float height)
    {
        float width = rect.Width * BattlefieldPerspective.CardWidth;
        float depth = rect.Height * ProductFaceDepth;
        float centerX = (-BattlefieldPerspective.CardWidth * 0.5f) +
                        ((rect.X + (rect.Width * 0.5f)) * BattlefieldPerspective.CardWidth);
        float centerZ = (-ProductFaceDepth * 0.5f) +
                        ((rect.Y + (rect.Height * 0.5f)) * ProductFaceDepth);
        layer.Position = new Vector3(centerX, height, centerZ);
        layer.Scale = new Vector3(width, depth, 1.0f);
    }

    private static ShaderMaterial ConfigureProductMaskedMaterial(
        ref ShaderMaterial? actorMaterial,
        string layerName,
        string texturePath,
        string maskPath,
        CardArtCrop crop,
        CardFaceRect frameRegion,
        float opacity)
    {
        ShaderMaterial material = actorMaterial ??= new ShaderMaterial
        {
            Shader = ProductArtworkMaskShader,
        };
        material.ResourceName = $"AnimeV1 actor-local masked {layerName}:{texturePath}";
        material.SetShaderParameter("art_texture", GD.Load<Texture2D>(texturePath));
        material.SetShaderParameter("frame_mask", GD.Load<Texture2D>(maskPath));
        material.SetShaderParameter(
            "art_crop",
            new Vector4(crop.U, crop.V, crop.Width, crop.Height));
        material.SetShaderParameter(
            "frame_region",
            new Vector4(frameRegion.X, frameRegion.Y, frameRegion.Width, frameRegion.Height));
        material.SetShaderParameter("layer_opacity", opacity);
        return material;
    }

    private static void ClearProductMaskedMaterial(
        ref ShaderMaterial? actorMaterial,
        string layerName)
    {
        if (actorMaterial is null || !GodotObject.IsInstanceValid(actorMaterial))
        {
            actorMaterial = null;
            return;
        }

        // The materials themselves are actor-local and reused, keeping counts
        // bounded. Every texture uniform (including the kind/faction mask) is
        // detached before the actor returns to the pool so a hidden card can
        // never retain the previous identity in a shader parameter.
        actorMaterial.ResourceName = $"AnimeV1 actor-local masked {layerName}:cleared";
        actorMaterial.SetShaderParameter("art_texture", default(Variant));
        actorMaterial.SetShaderParameter("frame_mask", default(Variant));
        actorMaterial.SetShaderParameter("art_crop", new Vector4(0.0f, 0.0f, 1.0f, 1.0f));
        actorMaterial.SetShaderParameter("frame_region", new Vector4(0.0f, 0.0f, 1.0f, 1.0f));
        actorMaterial.SetShaderParameter("layer_opacity", 0.0f);
    }

    private static StandardMaterial3D ProductLayerMaterial(
        string texturePath,
        CardArtCrop? crop,
        bool transparent,
        float opacity)
    {
        string cropKey = crop is { } present
            ? FormattableString.Invariant(
                $"{present.U:F6},{present.V:F6},{present.Width:F6},{present.Height:F6}")
            : "full";
        string key = FormattableString.Invariant(
            $"{texturePath}|{cropKey}|{transparent}|{opacity:F3}");
        if (ProductLayerMaterials.TryGetValue(key, out StandardMaterial3D? cached))
        {
            return cached;
        }

        Texture2D texture = GD.Load<Texture2D>(texturePath);
        var material = new StandardMaterial3D
        {
            ResourceName = $"AnimeV1:{texturePath}:{cropKey}",
            AlbedoTexture = texture,
            AlbedoColor = new Color(1.0f, 1.0f, 1.0f, opacity),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            Transparency = transparent
                ? BaseMaterial3D.TransparencyEnum.AlphaScissor
                : BaseMaterial3D.TransparencyEnum.Disabled,
            AlphaScissorThreshold = 0.50f,
        };
        if (crop is { } uv)
        {
            material.Uv1Scale = new Vector3(uv.Width, uv.Height, 1.0f);
            material.Uv1Offset = new Vector3(uv.U, uv.V, 0.0f);
        }
        ProductLayerMaterials[key] = material;
        return material;
    }

    private void ApplyProductLabels(CardFaceComposition composition)
    {
        CardFaceViewModel view = composition.ViewModel;
        ConfigureProductLabel(
            _faceLabel,
            composition.Layout.NameText,
            composition.ViewModel.DisplayName,
            maximumFontSize: 24,
            minimumFontSize: 14,
            color: new Color("fff8e7"),
            noDepthTest: _layout == BattlefieldCardLayout.NearHand,
            // The authored NameText socket already clears both ornaments by
            // about 12% of the plaque. Two font pixels keep the rasterized
            // glyphs inside that socket without sacrificing the full name.
            horizontalFitPaddingPixels: 2,
            verticalFitPaddingPixels: 1);
        ConfigureProductLabel(
            _costLabel,
            composition.Layout.CostText,
            view.Cost.ToString(CultureInfo.InvariantCulture),
            maximumFontSize: 38,
            minimumFontSize: 16,
            color: Colors.White,
            noDepthTest: _layout == BattlefieldCardLayout.NearHand,
            horizontalFitPaddingPixels: 1,
            verticalFitPaddingPixels: 1);
        ConfigureOptionalProductLabel(
            _attackLabel,
            composition.Layout.AttackText,
            view.Attack,
            new Color("eef8ff"),
            _layout == BattlefieldCardLayout.NearHand);
        ConfigureOptionalProductLabel(
            _healthLabel,
            composition.Layout.HealthText,
            view.Health,
            new Color("fff1f3"),
            _layout == BattlefieldCardLayout.NearHand);
        ConfigureOptionalProductLabel(
            _countdownLabel,
            composition.Layout.CountdownText,
            view.Countdown,
            new Color("fff4cf"),
            _layout == BattlefieldCardLayout.NearHand);
        _kindLabel.Text = string.Empty;
        _kindLabel.Visible = false;
    }

    private static void ConfigureOptionalProductLabel(
        Label3D label,
        CardFaceRect? socket,
        int? value,
        Color color,
        bool noDepthTest)
    {
        if (socket is not { } rect || !value.HasValue)
        {
            label.Text = string.Empty;
            label.Visible = false;
            return;
        }
        ConfigureProductLabel(
            label,
            rect,
            value.Value.ToString(CultureInfo.InvariantCulture),
            maximumFontSize: 38,
            minimumFontSize: 16,
            color: color,
            noDepthTest: noDepthTest,
            horizontalFitPaddingPixels: 1,
            verticalFitPaddingPixels: 1);
    }

    private static void ConfigureProductLabel(
        Label3D label,
        CardFaceRect rect,
        string text,
        int maximumFontSize,
        int minimumFontSize,
        Color color,
        bool noDepthTest,
        int horizontalFitPaddingPixels,
        int verticalFitPaddingPixels)
    {
        const float pixelSize = 0.0066f;
        float width = rect.Width * BattlefieldPerspective.CardWidth;
        float height = rect.Height * ProductFaceDepth;
        float centerX = (-BattlefieldPerspective.CardWidth * 0.5f) +
                         ((rect.X + (rect.Width * 0.5f)) * BattlefieldPerspective.CardWidth);
        float centerZ = (-ProductFaceDepth * 0.5f) +
                         ((rect.Y + (rect.Height * 0.5f)) * ProductFaceDepth);
        ProductTextFit fit = FitProductText(
            text,
            width / pixelSize,
            height / pixelSize,
            maximumFontSize,
            minimumFontSize,
            horizontalFitPaddingPixels,
            verticalFitPaddingPixels);
        label.Font = ProductCardFont;
        label.FontSize = fit.FontSize;
        label.PixelSize = pixelSize;
        label.OutlineSize = fit.OutlineSize;
        label.OutlineModulate = new Color("2b2236");
        label.Width = MathF.Max(1.0f, width / pixelSize);
        // Label3D exposes a bounded width but no independent height in Godot
        // 4.7.  The normalized text rectangle still owns both dimensions: its
        // exact center positions the intrinsic single-line ascent/descent box,
        // while Width constrains horizontal alignment without borrowing the
        // asymmetric decorative gem rectangle.
        label.Position = new Vector3(centerX, ProductLabelY, centerZ);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.NoDepthTest = noDepthTest;
        label.RenderPriority = noDepthTest ? 10 : 4;
        label.Text = fit.Text;
        label.Modulate = color;
        label.Visible = true;

        // Center the generated glyph geometry, not merely Label3D's nominal
        // layout origin. This accounts for the selected font's real
        // ascent/descent and its outline while keeping the layer depth fixed.
        Aabb glyphBounds = label.GetAabb();
        Vector3 geometryCenterOffset = label.Transform.Basis * glyphBounds.GetCenter();
        label.Position = new Vector3(
            centerX - geometryCenterOffset.X,
            ProductLabelY,
            centerZ - geometryCenterOffset.Z);
    }

    private static ProductTextFit FitProductText(
        string text,
        float availableWidth,
        float availableHeight,
        int maximumFontSize,
        int minimumFontSize,
        int horizontalFitPaddingPixels,
        int verticalFitPaddingPixels)
    {
        string source = string.IsNullOrWhiteSpace(text) ? "—" : text.Trim();
        int maximum = Math.Max(1, maximumFontSize);
        int minimum = Math.Clamp(minimumFontSize, 1, maximum);
        for (int size = maximum; size >= minimum; --size)
        {
            ProductTextFit fit = MeasureProductText(source, size);
            if (ProductTextFits(
                    fit,
                    availableWidth,
                    availableHeight,
                    horizontalFitPaddingPixels,
                    verticalFitPaddingPixels))
            {
                return fit;
            }
        }

        // Preserve every codepoint. The widened authored name socket keeps all
        // locked product names above their readability floor; shrinking below
        // that floor remains only a safe fallback for unexpected future data.
        for (int size = minimum - 1; size >= 1; --size)
        {
            ProductTextFit fit = MeasureProductText(source, size);
            if (ProductTextFits(
                    fit,
                    availableWidth,
                    availableHeight,
                    horizontalFitPaddingPixels,
                    verticalFitPaddingPixels))
            {
                return fit;
            }
        }
        return MeasureProductText(source, 1);
    }

    private static ProductTextFit MeasureProductText(string text, int fontSize)
    {
        int outlineSize = Math.Max(1, fontSize / 12);
        Vector2 measured = new(
            ProductCardFont.GetStringSize(
                text,
                HorizontalAlignment.Left,
                -1.0f,
                fontSize).X,
            ProductCardFont.GetAscent(fontSize) + ProductCardFont.GetDescent(fontSize));
        return new ProductTextFit(text, fontSize, outlineSize, measured);
    }

    private static bool ProductTextFits(
        ProductTextFit fit,
        float availableWidth,
        float availableHeight,
        int horizontalFitPaddingPixels,
        int verticalFitPaddingPixels) =>
        fit.MeasuredSize.X + (fit.OutlineSize * 2.0f) +
            (horizontalFitPaddingPixels * 2.0f) <=
            availableWidth + 0.01f &&
        fit.MeasuredSize.Y + (fit.OutlineSize * 2.0f) +
            (verticalFitPaddingPixels * 2.0f) <=
            availableHeight + 0.01f;

    private void ClearProductFace()
    {
        _productFace = null;
        _baseMesh.Visible = true;
        _baseMesh.Scale = Vector3.One;
        ClearProductMaskedMaterial(ref _productArtworkMaterial, "artwork");
        ClearProductMaskedMaterial(ref _productSurfaceMaterial, "material");
        ClearProductMaskedMaterial(ref _productFoilMaterial, "foil");
        foreach (MeshInstance3D layer in ProductLayers)
        {
            layer.Visible = false;
            layer.MaterialOverride = null;
            ScrubMetadata(layer);
        }
        foreach (Label3D label in FaceLabels)
        {
            label.NoDepthTest = false;
        }
    }

    private static void ValidateProductContext(
        CardFaceContext context,
        BattlefieldCardLayout layout)
    {
        bool compatible = (context, layout) switch
        {
            (CardFaceContext.Hand, BattlefieldCardLayout.NearHand or BattlefieldCardLayout.FarHand) => true,
            (CardFaceContext.Field or CardFaceContext.Detail, BattlefieldCardLayout.Field) => true,
            _ => false,
        };
        if (!compatible)
        {
            throw new ArgumentException(
                $"Card-face context {context} is incompatible with battlefield layout {layout}.");
        }
    }

    private static string FormatProductCard(CardFaceComposition composition)
    {
        CardFaceViewModel view = composition.ViewModel;
        return view.Kind switch
        {
            ProductCardKind.Follower =>
                $"{view.Cost}费 {view.Attack}/{view.Health}\n{view.DisplayName}",
            ProductCardKind.Amulet or ProductCardKind.Trap
                when view.Countdown.HasValue =>
                $"{view.Cost}费 倒{view.Countdown}\n{view.DisplayName}",
            _ => $"{view.Cost}费\n{view.DisplayName}",
        };
    }

    private static Material ProductBaseMaterial(ProductCardFaction faction) => faction switch
    {
        ProductCardFaction.Oathguard => ProductOathguardBase,
        ProductCardFaction.Pactmage => ProductPactmageBase,
        _ => ProductNeutralBase,
    };

    private static StandardMaterial3D SharedArtworkMaterial(
        Texture2D texture,
        ArenaVisualProfile profile)
    {
        var key = (texture.GetInstanceId(), profile.Id);
        if (ArtworkMaterials.TryGetValue(key, out StandardMaterial3D? material))
        {
            return material;
        }

        material = new StandardMaterial3D
        {
            AlbedoTexture = texture,
            AlbedoColor = Colors.White,
            Roughness = profile.UsesShadedCardArtwork ? 0.46f : 0.58f,
            Metallic = profile.UsesShadedCardArtwork ? 0.015f : 0.04f,
            ShadingMode = profile.UsesShadedCardArtwork
                ? BaseMaterial3D.ShadingModeEnum.PerPixel
                : BaseMaterial3D.ShadingModeEnum.Unshaded,
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

    private void ApplyVisualProfileMaterials()
    {
        _baseMesh.MaterialOverride = NeutralFrameMaterial;
        _outlineMesh.MaterialOverride = LegalOutlineMaterial;
        _stackUnderlayA.MaterialOverride = NeutralFrameMaterial;
        _stackUnderlayB.MaterialOverride = NeutralFrameMaterial;
        foreach (MeshInstance3D badge in new[]
                 {
                     _costBadge, _kindBadge, _attackBadge, _healthBadge, _countdownBadge,
                 })
        {
            badge.MaterialOverride = BadgePlateMaterial;
        }

        if (_visualProfile.Id == BattlefieldVisualProfile.R3Candidate)
        {
            _faceLabel.Modulate = new Color("f0e9dd");
            _pileLabel.Modulate = new Color("d9cfbd");
            _costLabel.Modulate = new Color("f1e4bd");
            _kindLabel.Modulate = new Color("c7c7c0");
            _attackLabel.Modulate = new Color("e6bd75");
            _healthLabel.Modulate = new Color("c4d5c3");
            _countdownLabel.Modulate = new Color("d8c89a");
            _stateLabel.Modulate = new Color("ead19a");
            return;
        }

        _faceLabel.Modulate = Colors.White;
        _pileLabel.Modulate = new Color("efffff");
        _costLabel.Modulate = new Color("ddfff9");
        _kindLabel.Modulate = new Color("d7e4ed");
        _attackLabel.Modulate = new Color("ffe0a5");
        _healthLabel.Modulate = new Color("baffcf");
        _countdownLabel.Modulate = new Color("fff0b2");
        _stateLabel.Modulate = new Color(1.0f, 0.94f, 0.63f, 1.0f);
    }

    private static StandardMaterial3D CreateFrameMaterial(Color color) => new()
    {
        AlbedoColor = color,
        Metallic = 0.56f,
        Roughness = 0.35f,
    };

    private static StandardMaterial3D CreateProductBaseMaterial(Color color) => new()
    {
        AlbedoColor = color,
        Metallic = 0.42f,
        Roughness = 0.38f,
    };

    private static Font LoadProductCardFont()
    {
        const string path = "res://assets/fonts/NotoSerifCJKsc-SemiBold.otf";
        return ResourceLoader.Exists(path, "Font") ? GD.Load<Font>(path) : LegacyCardFont;
    }

    private static StandardMaterial3D CreateCandidateFrameMaterial(Color color) => new()
    {
        AlbedoColor = color,
        Metallic = 0.72f,
        Roughness = 0.39f,
    };

    private static StandardMaterial3D CreateBadgeMaterial() => new()
    {
        AlbedoColor = new Color(0.025f, 0.055f, 0.09f, 0.94f),
        Metallic = 0.62f,
        Roughness = 0.32f,
        EmissionEnabled = true,
        Emission = new Color(0.02f, 0.13f, 0.16f, 1.0f),
    };

    private static StandardMaterial3D CreateCandidateBadgeMaterial() => new()
    {
        AlbedoColor = new Color(0.045f, 0.052f, 0.055f, 0.98f),
        Metallic = 0.76f,
        Roughness = 0.36f,
        EmissionEnabled = true,
        Emission = new Color(0.035f, 0.03f, 0.02f, 1.0f),
    };

    private static StandardMaterial3D CreateOutlineMaterial(Color color) => new()
    {
        AlbedoColor = color,
        Roughness = 0.42f,
        EmissionEnabled = true,
        Emission = color * 1.4f,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    private static StandardMaterial3D CreateCandidateOutlineMaterial(Color color) => new()
    {
        AlbedoColor = color,
        Metallic = 0.48f,
        Roughness = 0.30f,
        EmissionEnabled = true,
        Emission = color * 0.34f,
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
            plate.Visible && plate.Mesh is not null && plate.MaterialOverride is not null,
            label.Position.Y,
            plateTop);
    }

    private static CardBadgeReadabilityEvidence CreateProductBadgeEvidence(
        Label3D label,
        MeshInstance3D gem)
    {
        // Product gems are planar, rotated card-face layers. Their rendered
        // top is the layer Y rather than the unrotated QuadMesh AABB top used
        // by legacy 3D badge plates.
        return new CardBadgeReadabilityEvidence(
            label.Text,
            label.Visible,
            ProductLayerIsBound(gem),
            label.Position.Y,
            gem.Position.Y);
    }

    private CardBadgeGpuEvidence CreateGpuBadgeEvidence(
        Camera3D camera,
        Label3D label,
        CardBadgeReadabilityEvidence local,
        CardFaceRect? productSocket = null)
    {
        Rect2 socketScreenRect = productSocket is { } socket
            ? ProjectProductRect(camera, socket)
            : new Rect2();
        if (!label.Visible || string.IsNullOrEmpty(label.Text) ||
            camera.IsPositionBehind(label.GlobalPosition))
        {
            return new CardBadgeGpuEvidence(local, new Rect2(), socketScreenRect);
        }
        return new CardBadgeGpuEvidence(
            local,
            ProjectLabelScreenRect(camera, label),
            socketScreenRect);
    }

    private static Rect2 ProjectLabelScreenRect(Camera3D camera, Label3D label)
    {
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
                return new Rect2();
            }
            Vector2 projected = camera.UnprojectPosition(worldPoint);
            minimum = new Vector2(
                MathF.Min(minimum.X, projected.X),
                MathF.Min(minimum.Y, projected.Y));
            maximum = new Vector2(
                MathF.Max(maximum.X, projected.X),
                MathF.Max(maximum.Y, projected.Y));
        }
        return new Rect2(minimum, maximum - minimum);
    }

    private Rect2 ProjectProductRect(Camera3D camera, CardFaceRect rect) =>
        ProjectProductRect(camera, this, rect);

    private static Rect2 ProjectProductRect(
        Camera3D camera,
        CardActor3D actor,
        CardFaceRect rect)
    {
        Vector2 minimum = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 maximum = new(float.NegativeInfinity, float.NegativeInfinity);
        (float U, float V)[] corners =
        [
            (rect.X, rect.Y),
            (rect.Right, rect.Y),
            (rect.X, rect.Bottom),
            (rect.Right, rect.Bottom),
        ];
        foreach ((float u, float v) in corners)
        {
            Vector3 localPoint = new(
                (u - 0.5f) * BattlefieldPerspective.CardWidth,
                ProductFrameY,
                (v - 0.5f) * ProductFaceDepth);
            Vector3 worldPoint = actor.GlobalTransform * localPoint;
            if (camera.IsPositionBehind(worldPoint))
            {
                return new Rect2();
            }
            Vector2 projected = camera.UnprojectPosition(worldPoint);
            minimum = new Vector2(
                MathF.Min(minimum.X, projected.X),
                MathF.Min(minimum.Y, projected.Y));
            maximum = new Vector2(
                MathF.Max(maximum.X, projected.X),
                MathF.Max(maximum.Y, projected.Y));
        }
        return new Rect2(minimum, maximum - minimum);
    }

    private Vector2 ProjectProductPoint(Camera3D camera, float u, float v)
    {
        Vector3 localPoint = new(
            (u - 0.5f) * BattlefieldPerspective.CardWidth,
            ProductFrameY,
            (v - 0.5f) * ProductFaceDepth);
        Vector3 worldPoint = GlobalTransform * localPoint;
        return camera.IsPositionBehind(worldPoint)
            ? new Vector2(float.NaN, float.NaN)
            : camera.UnprojectPosition(worldPoint);
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
    bool UsesIntegratedProductFace,
    BattlefieldCardLayout Layout,
    CardKind? Kind,
    ProductCardKind? ProductKind,
    int Cost,
    int Attack,
    int Health,
    int Countdown,
    bool AttackExpected,
    bool HealthExpected,
    bool CountdownExpected,
    string NameText,
    bool NameVisible,
    bool TypeCrestVisible,
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
                       !TypeCrestVisible &&
                       CostBadge.IsCleared && KindBadge.IsCleared &&
                       AttackBadge.IsCleared && HealthBadge.IsCleared &&
                       CountdownBadge.IsCleared;
            }

            if (UsesIntegratedProductFace)
            {
                bool commonProduct = ProductKind.HasValue && TypeCrestVisible &&
                                     NameVisible && !string.IsNullOrWhiteSpace(NameText) &&
                                     CostBadge.Text == Cost.ToString(CultureInfo.InvariantCulture) &&
                                     CostBadge.IsReadable(MinimumDepthClearance) &&
                                     KindBadge.IsCleared;
                if (!commonProduct)
                {
                    return false;
                }

                return ProductKind switch
                {
                    ProductCardKind.Follower =>
                        AttackExpected && HealthExpected &&
                        AttackBadge.Text == Attack.ToString(CultureInfo.InvariantCulture) &&
                        HealthBadge.Text == Health.ToString(CultureInfo.InvariantCulture) &&
                        AttackBadge.IsReadable(MinimumDepthClearance) &&
                        HealthBadge.IsReadable(MinimumDepthClearance) &&
                        CountdownBadge.IsCleared,
                    ProductCardKind.Amulet or ProductCardKind.Trap when CountdownExpected =>
                        AttackBadge.IsCleared && HealthBadge.IsCleared &&
                        CountdownBadge.Text == Countdown.ToString(CultureInfo.InvariantCulture) &&
                        CountdownBadge.IsReadable(MinimumDepthClearance),
                    _ => AttackBadge.IsCleared && HealthBadge.IsCleared &&
                         CountdownBadge.IsCleared,
                };
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
    Rect2 ScreenRect,
    Rect2 SocketScreenRect)
{
    internal bool IsReadable(float minimumPixelHeight) =>
        Local.IsReadable(0.012f) &&
        ScreenRect.Size.X > 0.0f &&
        ScreenRect.Size.Y >= minimumPixelHeight;
}

internal readonly record struct CardNameGpuEvidence(
    string Text,
    int FontSize,
    Rect2 ScreenRect,
    Rect2 TextSocketScreenRect,
    Rect2 NamePlateScreenRect);

internal readonly record struct ProductTextFit(
    string Text,
    int FontSize,
    int OutlineSize,
    Vector2 MeasuredSize);

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
        if (Local.UsesIntegratedProductFace)
        {
            return Local.ProductKind switch
            {
                ProductCardKind.Follower =>
                    AttackBadge.IsReadable(minimumPixelHeight) &&
                    HealthBadge.IsReadable(minimumPixelHeight),
                ProductCardKind.Amulet or ProductCardKind.Trap
                    when Local.CountdownExpected =>
                    CountdownBadge.IsReadable(minimumPixelHeight),
                _ => true,
            };
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

internal readonly record struct CardSilhouetteGpuProbe(
    string Corner,
    Vector2 ScreenPosition,
    Vector2 BackgroundReferencePosition);
