// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;

namespace Scgs.GodotClient.Battlefield;

/// <summary>
/// Identity-free placement feedback. The ghost intentionally never receives a card DTO,
/// instance id, definition id, label, metadata, collision shape, or input callback.
/// </summary>
public sealed partial class BattlefieldPlacementGhost3D : Node3D
{
    private MeshInstance3D? _mesh;

    public override void _Ready()
    {
        EnsureBuilt();
        Stop();
    }

    public void ShowAt(Vector3 boardPosition, PlayerId viewer)
    {
        if (viewer is not (PlayerId.Player0 or PlayerId.Player1))
        {
            throw new ArgumentOutOfRangeException(nameof(viewer), viewer, "Unsupported viewer value.");
        }

        EnsureBuilt();
        float facingDegrees = viewer == PlayerId.Player0 ? 0.0f : 180.0f;
        Basis basis = Basis.FromEuler(
            new Vector3(0.0f, Mathf.DegToRad(facingDegrees), 0.0f));
        Transform = new Transform3D(
            basis,
            new Vector3(boardPosition.X, 0.32f, boardPosition.Z));
        Visible = true;
    }

    public void Stop()
    {
        Visible = false;
        Transform = Transform3D.Identity;
    }

    private void EnsureBuilt()
    {
        if (_mesh is not null)
        {
            return;
        }

        _mesh = new MeshInstance3D
        {
            Name = "IdentityFreeGhost",
            Mesh = new BoxMesh { Size = new Vector3(1.68f, 0.045f, 2.28f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.25f, 0.94f, 0.78f, 0.34f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                EmissionEnabled = true,
                Emission = new Color(0.18f, 0.72f, 0.61f) * 0.45f,
                NoDepthTest = true,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_mesh);
    }
}
