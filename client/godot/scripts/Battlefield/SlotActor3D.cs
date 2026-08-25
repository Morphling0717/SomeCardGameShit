// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;
using Scgs.GodotClient.Visuals;

namespace Scgs.GodotClient.Battlefield;

/// <summary>
/// Pooled surface actor for an empty field slot, an empty pile marker or a
/// leader core. The neutral state intentionally avoids opaque rectangles: only
/// low-contrast corner brackets and a type glyph remain.
/// </summary>
public sealed partial class SlotActor3D : Area3D, IBattlefieldPickTarget
{
    private enum VisualMode
    {
        Slot,
        Pile,
        Leader,
    }

    private static readonly BoxMesh SlotFillMesh = new()
    {
        Size = new Vector3(
            BattlefieldPerspective.SlotWidth,
            0.018f,
            BattlefieldPerspective.SlotDepth),
    };
    private static readonly BoxMesh SlotTrayMesh = new()
    {
        Size = new Vector3(
            BattlefieldPerspective.SlotWidth + 0.12f,
            0.05f,
            BattlefieldPerspective.SlotDepth + 0.12f),
    };

    private static readonly MultiMesh CornerBracketMesh = CreateCornerBrackets();
    private static readonly BoxMesh PilePlinthMesh = new()
    {
        Size = new Vector3(1.76f, 0.09f, 2.34f),
    };
    private static readonly BoxMesh PileLipMesh = new()
    {
        Size = new Vector3(1.72f, 0.07f, 0.13f),
    };
    private static readonly CylinderMesh LeaderPlatformMesh = new()
    {
        TopRadius = 0.88f,
        BottomRadius = 0.98f,
        Height = 0.16f,
        RadialSegments = 40,
    };
    private static readonly CylinderMesh LeaderHaloMesh = new()
    {
        TopRadius = 0.72f,
        BottomRadius = 0.72f,
        Height = 0.025f,
        RadialSegments = 40,
    };
    private static readonly SphereMesh LeaderCoreMesh = new()
    {
        Radius = 0.34f,
        Height = 0.68f,
        RadialSegments = 32,
        Rings = 16,
    };
    private static readonly CylinderMesh LeaderOuterRingMesh = new()
    {
        TopRadius = 0.83f,
        BottomRadius = 0.83f,
        Height = 0.035f,
        RadialSegments = 40,
    };
    private static readonly CylinderMesh LeaderInnerRingMesh = new()
    {
        TopRadius = 0.50f,
        BottomRadius = 0.50f,
        Height = 0.045f,
        RadialSegments = 40,
    };
    private static readonly BoxMesh LeaderTerminalFrameMesh = new()
    {
        Size = new Vector3(0.88f, 0.80f, 0.07f),
    };
    private static readonly QuadMesh LeaderPortraitMesh = new()
    {
        Size = new Vector2(0.70f, 0.70f),
    };

    private static readonly StandardMaterial3D IdleUnitMaterial =
        CreateSurfaceMaterial(new Color("2f89a7"), 0.24f, 0.16f);
    private static readonly StandardMaterial3D IdleTacticMaterial =
        CreateSurfaceMaterial(new Color("8c72b5"), 0.22f, 0.15f);
    private static readonly StandardMaterial3D PileMaterial =
        CreateSurfaceMaterial(new Color("6f91a6"), 0.20f, 0.10f);
    private static readonly StandardMaterial3D IdleUnitTrayMaterial =
        CreateTrayMaterial(new Color("1d6075"));
    private static readonly StandardMaterial3D IdleTacticTrayMaterial =
        CreateTrayMaterial(new Color("5a4778"));
    private static readonly StandardMaterial3D PileTrayMaterial =
        CreateTrayMaterial(new Color("405b6b"));
    private static readonly StandardMaterial3D LegalMaterial =
        CreateSurfaceMaterial(new Color("4de0c4"), 0.48f, 0.82f);
    private static readonly StandardMaterial3D DestinationMaterial =
        CreateSurfaceMaterial(new Color("ffc45e"), 0.48f, 0.9f);
    private static readonly StandardMaterial3D SelectedMaterial =
        CreateSurfaceMaterial(new Color("ff7862"), 0.52f, 0.92f);
    private static readonly StandardMaterial3D CyanCoreMaterial =
        CreateCoreMaterial(new Color("43d9e2"));
    private static readonly StandardMaterial3D VioletCoreMaterial =
        CreateCoreMaterial(new Color("b276ed"));
    private static readonly StandardMaterial3D LeaderPlatformMaterial = new()
    {
        AlbedoColor = new Color("173044"),
        Metallic = 0.78f,
        Roughness = 0.26f,
    };
    private static readonly ArenaVisualProfile R3Profile = ArenaVisualProfile.R3Candidate;
    private static readonly StandardMaterial3D R3UnitMaterial =
        CreateCandidateInlayMaterial(R3Profile.UnitInlay);
    private static readonly StandardMaterial3D R3TacticMaterial =
        CreateCandidateInlayMaterial(R3Profile.TacticInlay);
    private static readonly StandardMaterial3D R3PileMaterial =
        CreateCandidateInlayMaterial(R3Profile.PileInlay);
    private static readonly StandardMaterial3D R3NeutralMetalMaterial =
        CreateCandidateMetalMaterial(R3Profile.NeutralMetal);
    private static readonly StandardMaterial3D R3LegalMaterial =
        CreateCandidateAffordanceMaterial(R3Profile.FunctionalAccent);
    private static readonly StandardMaterial3D R3DestinationMaterial =
        CreateCandidateAffordanceMaterial(R3Profile.DestinationAccent);
    private static readonly StandardMaterial3D R3SelectedMaterial =
        CreateCandidateAffordanceMaterial(R3Profile.SelectedAccent);
    private static readonly StandardMaterial3D R3MidrangeCoreMaterial =
        CreateCandidateCoreMaterial(new Color("66818b"));
    private static readonly StandardMaterial3D R3AdvanceCoreMaterial =
        CreateCandidateCoreMaterial(new Color("806a75"));
    private static readonly StandardMaterial3D R3NeutralCoreMaterial =
        CreateCandidateCoreMaterial(new Color("988568"));
    private static readonly Dictionary<ulong, StandardMaterial3D> LeaderPortraitMaterials = [];

    private MeshInstance3D _trayMesh = null!;
    private MeshInstance3D _fillMesh = null!;
    private MultiMeshInstance3D _brackets = null!;
    private MeshInstance3D _leaderPlatform = null!;
    private MeshInstance3D _leaderHalo = null!;
    private MeshInstance3D _leaderCore = null!;
    private MeshInstance3D _pilePlinth = null!;
    private MeshInstance3D _pileFrontLip = null!;
    private MeshInstance3D _leaderOuterRing = null!;
    private MeshInstance3D _leaderInnerRing = null!;
    private Node3D _leaderTerminal = null!;
    private MeshInstance3D _leaderTerminalFrame = null!;
    private MeshInstance3D _leaderPortrait = null!;
    private Label3D _label = null!;
    private CollisionShape3D _collision = null!;
    private BattlefieldHighlightKind _highlight;
    private VisualMode _visualMode;
    private StandardMaterial3D _idleMaterial = IdleUnitMaterial;
    private StandardMaterial3D _leaderCoreMaterial = CyanCoreMaterial;
    private ArenaVisualProfile _visualProfile = ArenaVisualProfile.Gate4BR2;
    private string _baseLabel = string.Empty;
    private string _slotGlyph = string.Empty;

    public BattlefieldSurfaceRef? Surface { get; private set; }

    public BattlefieldCardPresentation? CardPresentation => null;

    public Vector3 WorldAnchor => GlobalPosition + new Vector3(
        0.0f,
        _visualMode == VisualMode.Leader ? 0.62f : 0.08f,
        0.0f);

    public bool CanActivate { get; private set; }

    public bool CollisionEnabled => CollisionLayer != 0 && !_collision.Disabled;

    public string DisplayText => _label?.Text ?? string.Empty;

    // Neutral corner rails and the leader's identity-safe core are scenery,
    // not interaction outlines. Privacy probes intentionally count only a
    // live affordance produced by the current revision.
    public bool OutlineVisible => _highlight != BattlefieldHighlightKind.None &&
        (_visualMode == VisualMode.Leader
            ? _leaderHalo?.Visible == true
            : _brackets?.Visible == true);

    public bool HasTripleAffordance =>
        Visible && CanActivate && _highlight != BattlefieldHighlightKind.None &&
        OutlineVisible && DisplayText.Contains('\n');

    public override void _Ready()
    {
        EnsureBuilt();
        ClearSensitive();
    }

    internal void ConfigureVisualProfile(BattlefieldVisualProfile profile)
    {
        if (Visible && Surface.HasValue)
        {
            throw new InvalidOperationException("Visual profiles can only change while a slot actor is pooled.");
        }

        _visualProfile = ArenaVisualProfile.Resolve(profile);
        if (_fillMesh is not null)
        {
            ApplyPooledVisualDefaults();
        }
    }

    public void Bind(Transform3D transform, string label, BattlefieldSurfaceRef? surface)
    {
        EnsureBuilt();
        PrepareBinding(transform, surface, VisualMode.Slot);
        _baseLabel = label;
        bool tactic = label.Contains("策略", StringComparison.Ordinal);
        _slotGlyph = tactic ? "△" : "◇";
        _idleMaterial = _visualProfile.UsesOpenArena
            ? tactic ? R3TacticMaterial : R3UnitMaterial
            : tactic ? IdleTacticMaterial : IdleUnitMaterial;
        _trayMesh.MaterialOverride = _visualProfile.UsesOpenArena
            ? R3NeutralMetalMaterial
            : tactic ? IdleTacticTrayMaterial : IdleUnitTrayMaterial;
        _trayMesh.Visible = !_visualProfile.UsesOpenArena;
        _fillMesh.MaterialOverride = _idleMaterial;
        _brackets.MaterialOverride = _idleMaterial;
        _fillMesh.Visible = false;
        _brackets.Visible = true;
        _label.Text = _slotGlyph;
        _label.Visible = !_visualProfile.UsesOpenArena;
    }

    public void BindPile(Transform3D transform, string title, ulong count)
    {
        EnsureBuilt();
        PrepareBinding(transform, surface: null, VisualMode.Pile);
        _baseLabel = title;
        _slotGlyph = "▱";
        _idleMaterial = _visualProfile.UsesOpenArena ? R3PileMaterial : PileMaterial;
        _trayMesh.MaterialOverride = _visualProfile.UsesOpenArena
            ? R3NeutralMetalMaterial
            : PileTrayMaterial;
        _trayMesh.Visible = !_visualProfile.UsesOpenArena;
        _fillMesh.MaterialOverride = _idleMaterial;
        _brackets.MaterialOverride = _idleMaterial;
        _fillMesh.Visible = false;
        _brackets.Visible = true;
        _pilePlinth.MaterialOverride = R3NeutralMetalMaterial;
        _pileFrontLip.MaterialOverride = R3PileMaterial;
        _pilePlinth.Visible = _visualProfile.UsesOpenArena;
        _pileFrontLip.Visible = _visualProfile.UsesOpenArena;
        _label.Text = $"{title}  {count}";
        _label.Visible = true;
    }

    public void BindLeader(
        Transform3D transform,
        int health,
        int maximumHealth,
        bool near,
        BattlefieldSurfaceRef? surface,
        CardVisualFaction faction = CardVisualFaction.Neutral,
        Texture2D? portrait = null)
    {
        EnsureBuilt();
        PrepareBinding(transform, surface, VisualMode.Leader);
        _baseLabel = $"{health}/{maximumHealth}";
        _leaderCoreMaterial = _visualProfile.UsesOpenArena
            ? CandidateLeaderCoreMaterial(faction)
            : near ? CyanCoreMaterial : VioletCoreMaterial;
        _trayMesh.Visible = false;
        _leaderPlatform.MaterialOverride = _visualProfile.UsesOpenArena
            ? R3NeutralMetalMaterial
            : LeaderPlatformMaterial;
        _leaderCore.MaterialOverride = _leaderCoreMaterial;
        _leaderPlatform.Visible = true;
        _leaderCore.Visible = true;
        _leaderHalo.Visible = true;
        _leaderHalo.MaterialOverride = _leaderCoreMaterial;
        _leaderOuterRing.MaterialOverride = _visualProfile.UsesOpenArena
            ? R3NeutralMetalMaterial
            : LeaderPlatformMaterial;
        _leaderInnerRing.MaterialOverride = _leaderCoreMaterial;
        _leaderOuterRing.Visible = _visualProfile.UsesOpenArena;
        _leaderInnerRing.Visible = _visualProfile.UsesOpenArena;
        _leaderCore.Position = _visualProfile.UsesOpenArena
            ? new Vector3(0.0f, 0.27f, near ? -0.30f : 0.30f)
            : new Vector3(0.0f, 0.38f, 0.0f);
        _leaderCore.Scale = _visualProfile.UsesOpenArena
            ? Vector3.One * 0.62f
            : Vector3.One;
        _leaderTerminal.Visible = _visualProfile.UsesOpenArena;
        _leaderTerminal.Position = new Vector3(0.0f, 0.52f, near ? 0.27f : -0.27f);
        _leaderTerminal.RotationDegrees = new Vector3(-18.0f, near ? 0.0f : 180.0f, 0.0f);
        _leaderTerminalFrame.MaterialOverride = R3NeutralMetalMaterial;
        Texture2D safePortrait = portrait ??
            LeaderPortraitCatalog.Shared.LoadPortrait("unknown");
        _leaderPortrait.MaterialOverride = LeaderPortraitMaterial(safePortrait);
        _leaderPortrait.Visible = _visualProfile.UsesOpenArena;
        _label.Text = _baseLabel;
        _label.Position = _visualProfile.UsesOpenArena
            ? new Vector3(0.0f, 0.15f, near ? 0.98f : -0.98f)
            : new Vector3(0.0f, 0.13f, 0.73f);
        _label.FontSize = _visualProfile.UsesOpenArena ? 30 : 34;
        _label.Modulate = _visualProfile.UsesOpenArena
            ? new Color("eee5d5")
            : near ? new Color("b9ffff") : new Color("ead3ff");
        _label.Visible = true;
    }

    public void SetHighlight(BattlefieldHighlightKind highlight)
    {
        EnsureBuilt();
        _highlight = highlight;
        CanActivate = Surface.HasValue && highlight != BattlefieldHighlightKind.None;
        StandardMaterial3D highlightMaterial = HighlightMaterial(highlight);

        if (_visualMode == VisualMode.Leader)
        {
            _leaderHalo.MaterialOverride = highlight == BattlefieldHighlightKind.None
                ? _leaderCoreMaterial
                : highlightMaterial;
            if (_visualProfile.UsesOpenArena)
            {
                _leaderInnerRing.MaterialOverride = highlight == BattlefieldHighlightKind.None
                    ? _leaderCoreMaterial
                    : highlightMaterial;
            }
            _label.Text = highlight switch
            {
                BattlefieldHighlightKind.Legal => $"{_baseLabel}\n●",
                BattlefieldHighlightKind.Destination => $"{_baseLabel}\n◎",
                BattlefieldHighlightKind.Selected => $"{_baseLabel}\n◆",
                _ => _baseLabel,
            };
        }
        else
        {
            _fillMesh.MaterialOverride = highlightMaterial;
            // Keep the arena visible during decisions: affordances live on the
            // edge rails and glyph, never as an opaque rectangle over the slot.
            _fillMesh.Visible = false;
            _brackets.MaterialOverride = highlightMaterial;
            _label.Text = highlight switch
            {
                BattlefieldHighlightKind.Legal => $"{_slotGlyph}\n●",
                BattlefieldHighlightKind.Destination => $"{_slotGlyph}\n◎",
                BattlefieldHighlightKind.Selected => $"{_slotGlyph}\n◆",
                _ => _visualMode == VisualMode.Pile ? _label.Text : _slotGlyph,
            };
            _label.Visible = !_visualProfile.UsesOpenArena ||
                             _visualMode == VisualMode.Pile ||
                             highlight != BattlefieldHighlightKind.None;
        }

        if (Surface.HasValue)
        {
            CollisionLayer = CardActor3D.PickCollisionLayer;
        }
    }

    public void SetPointerHovered(bool hovered)
    {
        if (!Visible || !CanActivate)
        {
            return;
        }

        Scale = hovered ? new Vector3(1.055f, 1.0f, 1.055f) : Vector3.One;
    }

    public void ClearSensitive()
    {
        EnsureBuilt();
        Surface = null;
        CanActivate = false;
        _highlight = BattlefieldHighlightKind.None;
        _visualMode = VisualMode.Slot;
        _baseLabel = string.Empty;
        _slotGlyph = string.Empty;
        _label.Text = string.Empty;
        _label.Visible = false;
        _label.Position = new Vector3(0.0f, 0.045f, 0.0f);
        _label.FontSize = 30;
        _label.Modulate = new Color(0.65f, 0.78f, 0.84f, 0.76f);
        _trayMesh.Visible = false;
        _trayMesh.MaterialOverride = _visualProfile.UsesOpenArena
            ? R3NeutralMetalMaterial
            : IdleUnitTrayMaterial;
        _fillMesh.Visible = false;
        _fillMesh.MaterialOverride = _visualProfile.UsesOpenArena
            ? R3UnitMaterial
            : IdleUnitMaterial;
        _brackets.Visible = false;
        _brackets.MaterialOverride = _visualProfile.UsesOpenArena
            ? R3UnitMaterial
            : IdleUnitMaterial;
        _leaderPlatform.Visible = false;
        _leaderHalo.Visible = false;
        _leaderCore.Visible = false;
        _pilePlinth.Visible = false;
        _pileFrontLip.Visible = false;
        _leaderOuterRing.Visible = false;
        _leaderInnerRing.Visible = false;
        _leaderTerminal.Visible = false;
        _leaderPortrait.Visible = false;
        _leaderPortrait.MaterialOverride = null;
        _leaderTerminal.Position = Vector3.Zero;
        _leaderTerminal.Rotation = Vector3.Zero;
        _leaderCore.Position = new Vector3(0.0f, 0.38f, 0.0f);
        _leaderCore.Scale = Vector3.One;
        _leaderHalo.MaterialOverride = _visualProfile.UsesOpenArena
            ? R3NeutralCoreMaterial
            : CyanCoreMaterial;
        _leaderCore.MaterialOverride = _leaderHalo.MaterialOverride;
        Transform = Transform3D.Identity;
        Scale = Vector3.One;
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

        int count = DisplayText.Contains(token, StringComparison.Ordinal) ? 1 : 0;
        return count + CountMetadataToken(this, token);
    }

    private void PrepareBinding(
        Transform3D transform,
        BattlefieldSurfaceRef? surface,
        VisualMode visualMode)
    {
        Transform = transform;
        Scale = Vector3.One;
        Surface = surface;
        _visualMode = visualMode;
        _highlight = BattlefieldHighlightKind.None;
        CanActivate = false;
        _trayMesh.Visible = false;
        _fillMesh.Visible = false;
        _brackets.Visible = false;
        _leaderPlatform.Visible = false;
        _leaderHalo.Visible = false;
        _leaderCore.Visible = false;
        _pilePlinth.Visible = false;
        _pileFrontLip.Visible = false;
        _leaderOuterRing.Visible = false;
        _leaderInnerRing.Visible = false;
        _leaderTerminal.Visible = false;
        _leaderTerminal.Position = Vector3.Zero;
        _leaderTerminal.Rotation = Vector3.Zero;
        _leaderPortrait.Visible = false;
        _leaderPortrait.MaterialOverride = null;
        _leaderCore.Position = new Vector3(0.0f, 0.38f, 0.0f);
        _leaderCore.Scale = Vector3.One;
        _label.Position = new Vector3(0.0f, 0.045f, 0.0f);
        _label.FontSize = 30;
        _label.Modulate = new Color(0.65f, 0.78f, 0.84f, 0.76f);
        if (_collision.Shape is BoxShape3D pickShape)
        {
            bool leader = visualMode == VisualMode.Leader;
            pickShape.Size = leader
                ? new Vector3(1.92f, 0.90f, 2.52f)
                : new Vector3(1.92f, 0.12f, 2.52f);
            _collision.Position = new Vector3(
                0.0f,
                leader ? 0.35f : 0.06f,
                0.0f);
        }
        _collision.Disabled = !surface.HasValue;
        CollisionLayer = surface.HasValue ? CardActor3D.PickCollisionLayer : 0;
        Visible = true;
    }

    private void EnsureBuilt()
    {
        if (_fillMesh is not null)
        {
            return;
        }

        _trayMesh = new MeshInstance3D
        {
            Name = "SlotTray",
            Mesh = SlotTrayMesh,
            Position = new Vector3(0.0f, -0.018f, 0.0f),
            MaterialOverride = IdleUnitTrayMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_trayMesh);

        _fillMesh = new MeshInstance3D
        {
            Name = "SlotTint",
            Mesh = SlotFillMesh,
            MaterialOverride = IdleUnitMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_fillMesh);

        _brackets = new MultiMeshInstance3D
        {
            Name = "CornerAffordance",
            Multimesh = CornerBracketMesh,
            MaterialOverride = IdleUnitMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_brackets);

        _pilePlinth = new MeshInstance3D
        {
            Name = "PilePlinth",
            Mesh = PilePlinthMesh,
            Position = new Vector3(0.0f, -0.015f, 0.0f),
            MaterialOverride = R3NeutralMetalMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            Visible = false,
        };
        AddChild(_pilePlinth);

        _pileFrontLip = new MeshInstance3D
        {
            Name = "PileFrontLip",
            Mesh = PileLipMesh,
            Position = new Vector3(0.0f, 0.045f, 1.08f),
            MaterialOverride = R3PileMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_pileFrontLip);

        _leaderPlatform = new MeshInstance3D
        {
            Name = "LeaderPlatform",
            Mesh = LeaderPlatformMesh,
            MaterialOverride = LeaderPlatformMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            Visible = false,
        };
        AddChild(_leaderPlatform);

        _leaderHalo = new MeshInstance3D
        {
            Name = "LeaderHalo",
            Mesh = LeaderHaloMesh,
            Position = new Vector3(0.0f, 0.095f, 0.0f),
            MaterialOverride = CyanCoreMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_leaderHalo);

        _leaderCore = new MeshInstance3D
        {
            Name = "LeaderCore",
            Mesh = LeaderCoreMesh,
            Position = new Vector3(0.0f, 0.38f, 0.0f),
            MaterialOverride = CyanCoreMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_leaderCore);

        _leaderOuterRing = new MeshInstance3D
        {
            Name = "LeaderOuterRing",
            Mesh = LeaderOuterRingMesh,
            Position = new Vector3(0.0f, 0.105f, 0.0f),
            MaterialOverride = R3NeutralMetalMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            Visible = false,
        };
        AddChild(_leaderOuterRing);

        _leaderInnerRing = new MeshInstance3D
        {
            Name = "LeaderInnerRing",
            Mesh = LeaderInnerRingMesh,
            Position = new Vector3(0.0f, 0.13f, 0.0f),
            MaterialOverride = R3NeutralCoreMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_leaderInnerRing);

        _leaderTerminal = new Node3D
        {
            Name = "LeaderCommandTerminal",
            Visible = false,
        };
        AddChild(_leaderTerminal);

        _leaderTerminalFrame = new MeshInstance3D
        {
            Name = "TerminalFrame",
            Mesh = LeaderTerminalFrameMesh,
            MaterialOverride = R3NeutralMetalMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        _leaderTerminal.AddChild(_leaderTerminalFrame);

        _leaderPortrait = new MeshInstance3D
        {
            Name = "PublicLeaderPortrait",
            Mesh = LeaderPortraitMesh,
            Position = new Vector3(0.0f, 0.0f, 0.041f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        _leaderTerminal.AddChild(_leaderPortrait);

        _collision = new CollisionShape3D
        {
            Name = "PickShape",
            Shape = new BoxShape3D { Size = new Vector3(1.92f, 0.12f, 2.52f) },
            Position = new Vector3(0.0f, 0.06f, 0.0f),
        };
        AddChild(_collision);

        _label = new Label3D
        {
            Name = "SurfaceGlyph",
            Font = GD.Load<Font>("res://assets/fonts/NotoSansCJKsc-Regular.otf"),
            FontSize = 30,
            PixelSize = 0.0092f,
            OutlineSize = 6,
            OutlineModulate = new Color(0.01f, 0.025f, 0.04f, 0.95f),
            Width = 150.0f,
            Position = new Vector3(0.0f, 0.045f, 0.0f),
            RotationDegrees = new Vector3(-90.0f, 0.0f, 0.0f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            DoubleSided = true,
            Modulate = new Color(0.65f, 0.78f, 0.84f, 0.76f),
            Visible = false,
        };
        AddChild(_label);

        CollisionMask = 0;
        InputRayPickable = true;
        Monitoring = false;
        Monitorable = false;
    }

    private StandardMaterial3D HighlightMaterial(BattlefieldHighlightKind highlight) =>
        (_visualProfile.Id, highlight) switch
        {
            (BattlefieldVisualProfile.R3Candidate, BattlefieldHighlightKind.Legal) =>
                R3LegalMaterial,
            (BattlefieldVisualProfile.R3Candidate, BattlefieldHighlightKind.Destination) =>
                R3DestinationMaterial,
            (BattlefieldVisualProfile.R3Candidate, BattlefieldHighlightKind.Selected) =>
                R3SelectedMaterial,
            (_, BattlefieldHighlightKind.Legal) => LegalMaterial,
            (_, BattlefieldHighlightKind.Destination) => DestinationMaterial,
            (_, BattlefieldHighlightKind.Selected) => SelectedMaterial,
            _ => _idleMaterial,
        };

    private static StandardMaterial3D CandidateLeaderCoreMaterial(
        CardVisualFaction faction) => faction switch
        {
            CardVisualFaction.Midrange => R3MidrangeCoreMaterial,
            CardVisualFaction.Advance => R3AdvanceCoreMaterial,
            _ => R3NeutralCoreMaterial,
        };

    private static StandardMaterial3D LeaderPortraitMaterial(Texture2D texture)
    {
        ulong textureId = texture.GetInstanceId();
        if (LeaderPortraitMaterials.TryGetValue(
                textureId,
                out StandardMaterial3D? material))
        {
            return material;
        }

        material = new StandardMaterial3D
        {
            AlbedoTexture = texture,
            AlbedoColor = Colors.White,
            Metallic = 0.0f,
            Roughness = 0.42f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        LeaderPortraitMaterials[textureId] = material;
        return material;
    }

    private void ApplyPooledVisualDefaults()
    {
        _trayMesh.MaterialOverride = _visualProfile.UsesOpenArena
            ? R3NeutralMetalMaterial
            : IdleUnitTrayMaterial;
        _fillMesh.MaterialOverride = _visualProfile.UsesOpenArena
            ? R3UnitMaterial
            : IdleUnitMaterial;
        _brackets.MaterialOverride = _visualProfile.UsesOpenArena
            ? R3UnitMaterial
            : IdleUnitMaterial;
        _leaderPlatform.MaterialOverride = _visualProfile.UsesOpenArena
            ? R3NeutralMetalMaterial
            : LeaderPlatformMaterial;
        _leaderHalo.MaterialOverride = _visualProfile.UsesOpenArena
            ? R3NeutralCoreMaterial
            : CyanCoreMaterial;
        _leaderCore.MaterialOverride = _leaderHalo.MaterialOverride;
        _pilePlinth.MaterialOverride = R3NeutralMetalMaterial;
        _pileFrontLip.MaterialOverride = R3PileMaterial;
        _leaderOuterRing.MaterialOverride = R3NeutralMetalMaterial;
        _leaderInnerRing.MaterialOverride = R3NeutralCoreMaterial;
        _leaderTerminalFrame.MaterialOverride = R3NeutralMetalMaterial;
        _leaderTerminal.Visible = false;
        _leaderPortrait.Visible = false;
        _leaderPortrait.MaterialOverride = null;
    }

    private static MultiMesh CreateCornerBrackets()
    {
        const float segmentLength = 0.54f;
        const float inset = 0.055f;
        var segment = new BoxMesh
        {
            Size = new Vector3(segmentLength, 0.022f, 0.055f),
        };
        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = segment,
            InstanceCount = 8,
        };

        float x = (BattlefieldPerspective.SlotWidth / 2.0f) -
                  (segmentLength / 2.0f) - inset;
        float z = (BattlefieldPerspective.SlotDepth / 2.0f) - inset;
        int cursor = 0;
        foreach (float xSign in new[] { -1.0f, 1.0f })
        {
            foreach (float zSign in new[] { -1.0f, 1.0f })
            {
                multiMesh.SetInstanceTransform(
                    cursor++,
                    new Transform3D(Basis.Identity, new Vector3(xSign * x, 0.015f, zSign * z)));
                Basis verticalBasis = Basis.FromEuler(
                    new Vector3(0.0f, Mathf.Pi / 2.0f, 0.0f));
                multiMesh.SetInstanceTransform(
                    cursor++,
                    new Transform3D(
                        verticalBasis,
                        new Vector3(
                            xSign * ((BattlefieldPerspective.SlotWidth / 2.0f) - inset),
                            0.015f,
                            zSign * ((BattlefieldPerspective.SlotDepth / 2.0f) -
                                     (segmentLength / 2.0f) - inset))));
            }
        }

        return multiMesh;
    }

    private static StandardMaterial3D CreateSurfaceMaterial(
        Color color,
        float alpha,
        float emissionStrength) =>
        new()
        {
            AlbedoColor = new Color(color, alpha),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.66f,
            EmissionEnabled = true,
            Emission = color * emissionStrength,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };

    private static StandardMaterial3D CreateCoreMaterial(Color color) =>
        new()
        {
            AlbedoColor = new Color(color, 0.88f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Metallic = 0.18f,
            Roughness = 0.18f,
            EmissionEnabled = true,
            Emission = color * 1.1f,
        };

    private static StandardMaterial3D CreateTrayMaterial(Color color) =>
        new()
        {
            AlbedoColor = new Color(color, 0.34f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Metallic = 0.68f,
            Roughness = 0.34f,
            EmissionEnabled = true,
            Emission = color * 0.08f,
        };

    private static StandardMaterial3D CreateCandidateInlayMaterial(Color color) =>
        new()
        {
            AlbedoColor = new Color(color, 0.42f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Metallic = 0.46f,
            Roughness = 0.46f,
            EmissionEnabled = true,
            Emission = color * 0.035f,
        };

    private static StandardMaterial3D CreateCandidateMetalMaterial(Color color) =>
        new()
        {
            AlbedoColor = color,
            Metallic = 0.82f,
            Roughness = 0.39f,
        };

    private static StandardMaterial3D CreateCandidateAffordanceMaterial(Color color) =>
        new()
        {
            AlbedoColor = new Color(color, 0.94f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Metallic = 0.36f,
            Roughness = 0.28f,
            EmissionEnabled = true,
            Emission = color * 0.42f,
        };

    private static StandardMaterial3D CreateCandidateCoreMaterial(Color color) =>
        new()
        {
            AlbedoColor = new Color(color, 0.94f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Metallic = 0.64f,
            Roughness = 0.27f,
            EmissionEnabled = true,
            Emission = color * 0.24f,
        };

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
            count += key.ToString().Contains(token, StringComparison.Ordinal) ? 1 : 0;
            count += node.GetMeta(key).ToString().Contains(token, StringComparison.Ordinal) ? 1 : 0;
        }

        foreach (Node child in node.GetChildren())
        {
            count += CountMetadataToken(child, token);
        }

        return count;
    }
}
