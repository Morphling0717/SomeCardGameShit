// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

namespace Scgs.GodotClient.Battlefield;

/// <summary>Identity-free painted vista behind the real, depth-tested board actors.</summary>
public sealed partial class AnimeArenaBackdrop : MeshInstance3D
{
    private Transform3D lastCamera;
    private Vector2 lastSize;
    private float lastFov;
    private float lastHOffset;
    private float lastVOffset;

    public override void _Process(double delta)
    {
        Camera3D? camera = GetViewport().GetCamera3D();
        if (camera is null) return;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        if (lastCamera == camera.GlobalTransform && lastSize == size &&
            lastFov == camera.Fov && lastHOffset == camera.HOffset && lastVOffset == camera.VOffset) return;
        lastCamera = camera.GlobalTransform;
        lastSize = size;
        lastFov = camera.Fov;
        lastHOffset = camera.HOffset;
        lastVOffset = camera.VOffset;
        const float distance = 60.0f;
        float halfHeight = Mathf.Tan(Mathf.DegToRad(camera.Fov * 0.5f)) * distance;
        // A single full-bleed 16:9 vista crops on 16:10; it never stretches the painting.
        float aspect = MathF.Max(size.X / MathF.Max(size.Y, 1), 16.0f / 9.0f);
        Basis basis = camera.GlobalBasis;
        Vector3 origin = camera.GlobalPosition - basis.Z * distance +
            basis.X * camera.HOffset + basis.Y * camera.VOffset;
        GlobalTransform = new Transform3D(basis * Basis.FromScale(new Vector3(halfHeight * aspect, halfHeight, 1)), origin);
    }
}
