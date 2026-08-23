// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

namespace Scgs.GodotClient.Battlefield;

public sealed partial class BattlefieldCameraRig : Camera3D
{
    private const float WheelStep = 0.07f;
    private const float HudInsetToWorldScale = 16.0f;
    private float _zoom = 1.0f;
    private float _leftInset;
    private float _rightInset;
    private float _viewportWidth = 1600.0f;

    public event Action? ProjectionChanged;

    public float Zoom => _zoom;

    public float PitchDegrees => BattlefieldPerspective.CameraPitchDegrees;

    public override void _Ready()
    {
        Current = true;
        Fov = BattlefieldPerspective.CameraFovDegrees;
        Near = 0.1f;
        Far = 80.0f;
        ApplyPose();
    }

    public bool AdjustWheel(MouseButton button)
    {
        float delta = button switch
        {
            MouseButton.WheelUp => -WheelStep,
            MouseButton.WheelDown => WheelStep,
            _ => 0.0f,
        };

        return delta != 0.0f && SetZoom(_zoom + delta);
    }

    public bool SetZoom(float zoom)
    {
        if (!float.IsFinite(zoom))
        {
            throw new ArgumentOutOfRangeException(nameof(zoom));
        }

        float clamped = Mathf.Clamp(
            zoom,
            BattlefieldPerspective.MinimumZoom,
            BattlefieldPerspective.MaximumZoom);
        if (Mathf.IsEqualApprox(clamped, _zoom))
        {
            return false;
        }

        _zoom = clamped;
        ApplyPose();
        ProjectionChanged?.Invoke();
        return true;
    }

    public void SetViewportInsets(float leftPixels, float rightPixels, float viewportWidth)
    {
        if (!float.IsFinite(leftPixels) || !float.IsFinite(rightPixels) ||
            !float.IsFinite(viewportWidth))
        {
            throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        }

        _leftInset = MathF.Max(0.0f, leftPixels);
        _rightInset = MathF.Max(0.0f, rightPixels);
        _viewportWidth = MathF.Max(1.0f, viewportWidth);
        ApplyPose();
        ProjectionChanged?.Invoke();
    }

    private void ApplyPose()
    {
        Position = BattlefieldPerspective.CameraPosition(_zoom);
        // Camera HOffset moves the projected scene in the opposite screen direction.
        // A wider right HUD therefore needs a positive camera offset so the board
        // shifts left into the center of the remaining play area.
        HOffset = ((_rightInset - _leftInset) / _viewportWidth) *
                  HudInsetToWorldScale * _zoom;
        LookAt(Vector3.Zero, Vector3.Up);
    }
}
