// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;

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

    private static readonly MultiMesh CornerBracketMesh = CreateCornerBrackets();
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

    private static readonly StandardMaterial3D IdleUnitMaterial =
        CreateSurfaceMaterial(new Color("2f89a7"), 0.24f, 0.16f);
    private static readonly StandardMaterial3D IdleTacticMaterial =
        CreateSurfaceMaterial(new Color("8c72b5"), 0.22f, 0.15f);
    private static readonly StandardMaterial3D PileMaterial =
        CreateSurfaceMaterial(new Color("6f91a6"), 0.20f, 0.10f);
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

    private MeshInstance3D _fillMesh = null!;
    private MultiMeshInstance3D _brackets = null!;
    private MeshInstance3D _leaderPlatform = null!;
    private MeshInstance3D _leaderHalo = null!;
    private MeshInstance3D _leaderCore = null!;
    private Label3D _label = null!;
    private CollisionShape3D _collision = null!;
    private BattlefieldHighlightKind _highlight;
    private VisualMode _visualMode;
    private StandardMaterial3D _idleMaterial = IdleUnitMaterial;
    private StandardMaterial3D _leaderCoreMaterial = CyanCoreMaterial;
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

    public void Bind(Transform3D transform, string label, BattlefieldSurfaceRef? surface)
    {
        EnsureBuilt();
        PrepareBinding(transform, surface, VisualMode.Slot);
        _baseLabel = label;
        bool tactic = label.Contains("策略", StringComparison.Ordinal);
        _slotGlyph = tactic ? "△" : "◇";
        _idleMaterial = tactic ? IdleTacticMaterial : IdleUnitMaterial;
        _fillMesh.MaterialOverride = _idleMaterial;
        _brackets.MaterialOverride = _idleMaterial;
        _fillMesh.Visible = false;
        _brackets.Visible = true;
        _label.Text = _slotGlyph;
        _label.Visible = true;
    }

    public void BindPile(Transform3D transform, string title, ulong count)
    {
        EnsureBuilt();
        PrepareBinding(transform, surface: null, VisualMode.Pile);
        _baseLabel = title;
        _slotGlyph = "▱";
        _idleMaterial = PileMaterial;
        _fillMesh.MaterialOverride = PileMaterial;
        _brackets.MaterialOverride = PileMaterial;
        _fillMesh.Visible = false;
        _brackets.Visible = true;
        _label.Text = $"{title}  {count}";
        _label.Visible = true;
    }

    public void BindLeader(
        Transform3D transform,
        int health,
        int maximumHealth,
        bool near,
        BattlefieldSurfaceRef? surface)
    {
        EnsureBuilt();
        PrepareBinding(transform, surface, VisualMode.Leader);
        _baseLabel = $"{health}/{maximumHealth}";
        _leaderCoreMaterial = near ? CyanCoreMaterial : VioletCoreMaterial;
        _leaderCore.MaterialOverride = _leaderCoreMaterial;
        _leaderPlatform.Visible = true;
        _leaderCore.Visible = true;
        _leaderHalo.Visible = true;
        _leaderHalo.MaterialOverride = _leaderCoreMaterial;
        _label.Text = _baseLabel;
        _label.Position = new Vector3(0.0f, 0.13f, 0.73f);
        _label.FontSize = 34;
        _label.Modulate = near ? new Color("b9ffff") : new Color("ead3ff");
        _label.Visible = true;
    }

    public void SetHighlight(BattlefieldHighlightKind highlight)
    {
        EnsureBuilt();
        _highlight = highlight;
        CanActivate = Surface.HasValue && highlight != BattlefieldHighlightKind.None;
        StandardMaterial3D highlightMaterial = highlight switch
        {
            BattlefieldHighlightKind.Legal => LegalMaterial,
            BattlefieldHighlightKind.Destination => DestinationMaterial,
            BattlefieldHighlightKind.Selected => SelectedMaterial,
            _ => _idleMaterial,
        };

        if (_visualMode == VisualMode.Leader)
        {
            _leaderHalo.MaterialOverride = highlight == BattlefieldHighlightKind.None
                ? _leaderCoreMaterial
                : highlightMaterial;
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
            _fillMesh.Visible = _visualMode == VisualMode.Slot &&
                                highlight != BattlefieldHighlightKind.None;
            _brackets.MaterialOverride = highlightMaterial;
            _label.Text = highlight switch
            {
                BattlefieldHighlightKind.Legal => $"{_slotGlyph}\n●",
                BattlefieldHighlightKind.Destination => $"{_slotGlyph}\n◎",
                BattlefieldHighlightKind.Selected => $"{_slotGlyph}\n◆",
                _ => _visualMode == VisualMode.Pile ? _label.Text : _slotGlyph,
            };
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
        _fillMesh.Visible = false;
        _fillMesh.MaterialOverride = IdleUnitMaterial;
        _brackets.Visible = false;
        _brackets.MaterialOverride = IdleUnitMaterial;
        _leaderPlatform.Visible = false;
        _leaderHalo.Visible = false;
        _leaderCore.Visible = false;
        _leaderHalo.MaterialOverride = CyanCoreMaterial;
        _leaderCore.MaterialOverride = CyanCoreMaterial;
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
        _fillMesh.Visible = false;
        _brackets.Visible = false;
        _leaderPlatform.Visible = false;
        _leaderHalo.Visible = false;
        _leaderCore.Visible = false;
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
