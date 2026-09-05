// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

namespace Scgs.GodotClient.Battlefield;

public sealed partial class BattlefieldTargetArrow3D : Node3D
{
    private MeshInstance3D _shaft = null!;
    private MeshInstance3D _head = null!;

    public override void _Ready()
    {
        EnsureBuilt();
        Visible = false;
    }

    public void ShowBetween(Vector3 start, Vector3 end)
    {
        EnsureBuilt();
        Vector3 delta = end - start;
        float length = delta.Length();
        // The head itself is half a world unit long. A shorter preview
        // degenerates into a red slab covering the selected card; its outline
        // already identifies the source until the pointer moves away.
        if (length < 0.55f)
        {
            Visible = false;
            return;
        }

        Position = (start + end) * 0.5f + (Vector3.Up * 0.34f);
        LookAt(end + (Vector3.Up * 0.34f), Vector3.Up);
        _shaft.Scale = new Vector3(1.0f, 1.0f, MathF.Max(0.05f, length - 0.48f));
        _head.Position = new Vector3(0.0f, 0.0f, -(length * 0.5f));
        Visible = true;
    }

    public void Stop()
    {
        Visible = false;
        Transform = Transform3D.Identity;
    }

    private void EnsureBuilt()
    {
        if (_shaft is not null)
        {
            return;
        }

        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color("ff675b"),
            EmissionEnabled = true,
            Emission = new Color("ff3f32") * 0.7f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };

        _shaft = new MeshInstance3D
        {
            Name = "Shaft",
            Mesh = new BoxMesh { Size = new Vector3(0.11f, 0.07f, 1.0f) },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_shaft);

        _head = new MeshInstance3D
        {
            Name = "Head",
            Mesh = new PrismMesh
            {
                Size = new Vector3(0.48f, 0.12f, 0.5f),
                LeftToRight = 0.5f,
            },
            MaterialOverride = material,
            RotationDegrees = new Vector3(0.0f, 90.0f, 0.0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_head);
    }
}
