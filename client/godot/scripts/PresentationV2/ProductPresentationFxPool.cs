// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

namespace Scgs.GodotClient.PresentationV2;

/// <summary>
/// Fixed-capacity, identity-free geometry shared by all presentation cues. No
/// per-frame mesh/material allocation and no per-card viewport or shader.
/// </summary>
internal sealed partial class ProductPresentationFxPool : Node3D
{
    private static readonly Font Font = GD.Load<Font>("res://assets/fonts/NotoSansCJKsc-Regular.otf");
    private readonly MeshInstance3D[] _rings = new MeshInstance3D[2];
    private readonly MeshInstance3D[] _sparks = new MeshInstance3D[12];
    private readonly StandardMaterial3D[] _materials = new StandardMaterial3D[4];
    private MeshInstance3D _beam = null!;
    private MeshInstance3D _orb = null!;
    private Label3D _number = null!;
    private bool _built;

    internal void Build()
    {
        if (_built) return;
        _built = true;
        for (int index = 0; index < _materials.Length; ++index)
        {
            _materials[index] = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                NoDepthTest = false,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                AlbedoColor = Colors.White,
                EmissionEnabled = true,
                Emission = Colors.White,
                EmissionEnergyMultiplier = 1.2f,
            };
        }

        var ring = new TorusMesh
        {
            InnerRadius = 0.47f,
            OuterRadius = 0.495f,
            Rings = 48,
            RingSegments = 8,
        };
        for (int index = 0; index < _rings.Length; ++index)
        {
            _rings[index] = MakeMesh($"EnergyRing{index}", ring, _materials[index]);
        }

        var spark = new SphereMesh { Radius = 0.033f, Height = 0.066f, RadialSegments = 8, Rings = 4 };
        for (int index = 0; index < _sparks.Length; ++index)
        {
            _sparks[index] = MakeMesh($"Spark{index}", spark, _materials[2]);
        }

        _beam = MakeMesh("EnergyThread", new CylinderMesh
        {
            TopRadius = 0.027f,
            BottomRadius = 0.027f,
            Height = 1.0f,
            RadialSegments = 8,
        }, _materials[2]);
        _orb = MakeMesh("TravellingCore", new SphereMesh
        {
            Radius = 0.13f,
            Height = 0.26f,
            RadialSegments = 16,
            Rings = 8,
        }, _materials[3]);
        _number = new Label3D
        {
            Name = "AuthoritativeAmount",
            Font = Font,
            FontSize = 72,
            OutlineSize = 9,
            PixelSize = 0.011f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = false,
            OutlineModulate = new Color("242038"),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_number);
        Reset();
    }

    internal void Tint(Color color, float alpha = 1.0f)
    {
        Build();
        for (int index = 0; index < _materials.Length; ++index)
        {
            Color tint = index == 1 ? color.Lerp(new Color("f8f0da"), 0.55f) : color;
            _materials[index].AlbedoColor = tint with { A = alpha };
            _materials[index].Emission = tint;
        }
    }

    internal void Rings(Vector3 center, float progress, float strength = 1.0f)
    {
        for (int index = 0; index < _rings.Length; ++index)
        {
            float radius = 0.48f + progress * (index == 0 ? 2.0f : 1.35f);
            _rings[index].GlobalPosition = center + Vector3.Up * (0.11f + index * 0.018f);
            _rings[index].Scale = new Vector3(radius, 0.36f, radius);
            _rings[index].Visible = strength > 0.001f;
            _materials[index].AlbedoColor = _materials[index].AlbedoColor with
            {
                A = Math.Clamp((1.0f - progress) * strength, 0.0f, 1.0f),
            };
        }
    }

    internal void Sparks(Vector3 center, float progress, float radius = 1.1f, bool converge = false)
    {
        float distance = (converge ? 1.0f - progress : progress) * radius;
        for (int index = 0; index < _sparks.Length; ++index)
        {
            float angle = index * Mathf.Tau / _sparks.Length;
            Vector3 direction = new(MathF.Cos(angle), 0.24f + 0.17f * (index % 3), MathF.Sin(angle));
            _sparks[index].GlobalPosition = center + direction * distance;
            _sparks[index].Scale = Vector3.One * (0.45f + 0.9f * (1.0f - progress));
            _sparks[index].Visible = progress < 0.96f;
        }
        _materials[2].AlbedoColor = _materials[2].AlbedoColor with { A = 1.0f - progress * 0.7f };
    }

    internal void Beam(Vector3 start, Vector3 end, float reveal, float alpha)
    {
        Vector3 visibleEnd = start.Lerp(end, Math.Clamp(reveal, 0.0f, 1.0f));
        Vector3 delta = visibleEnd - start;
        float length = delta.Length();
        _beam.Visible = length > 0.03f && alpha > 0.001f;
        if (!_beam.Visible) return;
        Basis basis = new(new Quaternion(Vector3.Up, delta / length));
        _beam.GlobalTransform = new Transform3D(basis.Scaled(new Vector3(1, length, 1)), (start + visibleEnd) * 0.5f);
        _materials[2].AlbedoColor = _materials[2].AlbedoColor with { A = alpha };
    }

    internal void Orb(Vector3 position, float scale, bool visible = true)
    {
        _orb.Visible = visible;
        _orb.GlobalPosition = position;
        _orb.Scale = Vector3.One * scale;
    }

    internal void Amount(Vector3 center, string amount, Color color, float progress)
    {
        _number.Visible = progress < 0.99f;
        _number.Text = amount;
        _number.GlobalPosition = center + Vector3.Up * (0.65f + 0.85f * progress);
        _number.Modulate = color with { A = 1.0f - MathF.Max(0, (progress - 0.55f) / 0.45f) };
        _number.Scale = Vector3.One * (1.0f + 0.16f * MathF.Sin(MathF.PI * MathF.Min(1, progress * 3)));
    }

    internal void Reset()
    {
        if (!_built) return;
        foreach (MeshInstance3D ring in _rings) ring.Visible = false;
        foreach (MeshInstance3D spark in _sparks) spark.Visible = false;
        _beam.Visible = false;
        _orb.Visible = false;
        _number.Visible = false;
        _number.Text = string.Empty;
        _number.Modulate = Colors.White;
        foreach (StandardMaterial3D material in _materials)
        {
            material.AlbedoColor = Colors.White;
            material.Emission = Colors.White;
        }
    }

    private MeshInstance3D MakeMesh(string name, Mesh mesh, Material material)
    {
        var node = new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(node);
        return node;
    }
}
