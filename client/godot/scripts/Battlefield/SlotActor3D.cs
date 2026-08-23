// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

namespace Scgs.GodotClient.Battlefield;

public sealed partial class SlotActor3D : Area3D, IBattlefieldPickTarget
{
    private MeshInstance3D _mesh = null!;
    private MeshInstance3D _outlineMesh = null!;
    private Label3D _label = null!;
    private CollisionShape3D _collision = null!;
    private StandardMaterial3D _idleMaterial = null!;
    private StandardMaterial3D _legalMaterial = null!;
    private StandardMaterial3D _destinationMaterial = null!;
    private StandardMaterial3D _selectedMaterial = null!;
    private StandardMaterial3D _legalOutlineMaterial = null!;
    private StandardMaterial3D _destinationOutlineMaterial = null!;
    private StandardMaterial3D _selectedOutlineMaterial = null!;
    private BattlefieldHighlightKind _highlight;
    private string _baseLabel = string.Empty;

    public BattlefieldSurfaceRef? Surface { get; private set; }

    public BattlefieldCardPresentation? CardPresentation => null;

    public Vector3 WorldAnchor => GlobalPosition + new Vector3(0.0f, 0.08f, 0.0f);

    public bool CanActivate { get; private set; }

    public bool CollisionEnabled => CollisionLayer != 0 && !_collision.Disabled;

    public string DisplayText => _label?.Text ?? string.Empty;

    public bool OutlineVisible => _outlineMesh?.Visible == true;

    public bool HasTripleAffordance =>
        Visible && CanActivate && _highlight != BattlefieldHighlightKind.None &&
        OutlineVisible && DisplayText.Contains('\n');

    public override void _Ready()
    {
        EnsureBuilt();
        ClearSensitive();
    }

    public void Bind(
        Transform3D transform,
        string label,
        BattlefieldSurfaceRef? surface)
    {
        EnsureBuilt();
        Transform = transform;
        Surface = surface;
        _highlight = BattlefieldHighlightKind.None;
        CanActivate = false;
        _baseLabel = label;
        _label.Text = _baseLabel;
        _mesh.MaterialOverride = _idleMaterial;
        _outlineMesh.Visible = false;
        _collision.Disabled = !surface.HasValue;
        CollisionLayer = surface.HasValue ? CardActor3D.PickCollisionLayer : 0;
        Visible = true;
    }

    public void SetHighlight(BattlefieldHighlightKind highlight)
    {
        EnsureBuilt();
        _highlight = highlight;
        CanActivate = Surface.HasValue && highlight != BattlefieldHighlightKind.None;
        _mesh.MaterialOverride = highlight switch
        {
            BattlefieldHighlightKind.Legal => _legalMaterial,
            BattlefieldHighlightKind.Destination => _destinationMaterial,
            BattlefieldHighlightKind.Selected => _selectedMaterial,
            _ => _idleMaterial,
        };
        _outlineMesh.MaterialOverride = highlight switch
        {
            BattlefieldHighlightKind.Legal => _legalOutlineMaterial,
            BattlefieldHighlightKind.Destination => _destinationOutlineMaterial,
            BattlefieldHighlightKind.Selected => _selectedOutlineMaterial,
            _ => _legalOutlineMaterial,
        };
        _outlineMesh.Visible = highlight != BattlefieldHighlightKind.None;
        _label.Text = highlight switch
        {
            BattlefieldHighlightKind.Legal => $"{_baseLabel}\n● 可选",
            BattlefieldHighlightKind.Destination => $"{_baseLabel}\n◎ 目标",
            BattlefieldHighlightKind.Selected => $"{_baseLabel}\n◆ 已选",
            _ => _baseLabel,
        };

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

        Scale = hovered ? new Vector3(1.045f, 1.0f, 1.045f) : Vector3.One;
    }

    public void ClearSensitive()
    {
        EnsureBuilt();
        Surface = null;
        CanActivate = false;
        _highlight = BattlefieldHighlightKind.None;
        _baseLabel = string.Empty;
        _label.Text = string.Empty;
        _mesh.MaterialOverride = _idleMaterial;
        _outlineMesh.Visible = false;
        _outlineMesh.MaterialOverride = _legalOutlineMaterial;
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

    private void EnsureBuilt()
    {
        if (_mesh is not null)
        {
            return;
        }

        _idleMaterial = CreateMaterial(new Color("263c4d"), 0.18f);
        _legalMaterial = CreateMaterial(new Color("42bfa4"), 0.46f);
        _destinationMaterial = CreateMaterial(new Color("eab44f"), 0.52f);
        _selectedMaterial = CreateMaterial(new Color("ef6f58"), 0.56f);
        _legalOutlineMaterial = CreateOutlineMaterial(new Color("66f2d0"));
        _destinationOutlineMaterial = CreateOutlineMaterial(new Color("ffd166"));
        _selectedOutlineMaterial = CreateOutlineMaterial(new Color("ff8b74"));

        _outlineMesh = new MeshInstance3D
        {
            Name = "AffordanceOutline",
            Mesh = new BoxMesh { Size = new Vector3(2.08f, 0.02f, 2.68f) },
            Position = new Vector3(0.0f, -0.028f, 0.0f),
            MaterialOverride = _legalOutlineMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_outlineMesh);

        _mesh = new MeshInstance3D
        {
            Name = "SlotMesh",
            Mesh = new BoxMesh { Size = new Vector3(1.88f, 0.035f, 2.48f) },
            MaterialOverride = _idleMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_mesh);

        _collision = new CollisionShape3D
        {
            Name = "PickShape",
            Shape = new BoxShape3D { Size = new Vector3(1.92f, 0.18f, 2.52f) },
            Position = new Vector3(0.0f, 0.07f, 0.0f),
        };
        AddChild(_collision);

        _label = new Label3D
        {
            Name = "SlotLabel",
            Font = GD.Load<Font>("res://assets/fonts/NotoSansCJKsc-Regular.otf"),
            FontSize = 40,
            PixelSize = 0.012f,
            OutlineSize = 9,
            Position = new Vector3(0.0f, 0.035f, 0.0f),
            RotationDegrees = new Vector3(-90.0f, 0.0f, 0.0f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            DoubleSided = true,
        };
        AddChild(_label);

        CollisionMask = 0;
        InputRayPickable = true;
        Monitoring = false;
        Monitorable = false;
    }

    private static StandardMaterial3D CreateMaterial(Color color, float emissionStrength) => new()
    {
        AlbedoColor = new Color(color, 0.88f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.8f,
        EmissionEnabled = true,
        Emission = color * emissionStrength,
    };

    private static StandardMaterial3D CreateOutlineMaterial(Color color) => new()
    {
        AlbedoColor = color,
        Roughness = 0.54f,
        EmissionEnabled = true,
        Emission = color * 0.9f,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
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
