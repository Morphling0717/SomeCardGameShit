// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

namespace Scgs.GodotClient.Battlefield;

public sealed partial class BattlefieldCameraRig : Camera3D
{
    private const float WheelStep = 0.07f;
    private const float HudInsetToWorldScale = 13.5f;
    private const float ReferenceSafeWidthRatio = 0.68f;
    private const float ReferenceAspectRatio = 16.0f / 9.0f;
    private const float MinimumProductAspectRatio = 16.0f / 10.0f;
    private const float ProductAspectFramingWeight = 0.5f;
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
        float safeWidth = MathF.Max(1.0f, _viewportWidth - _leftInset - _rightInset);
        float safeRatio = Mathf.Clamp(safeWidth / _viewportWidth, 0.42f, 1.0f);
        Vector2 viewportSize = GetViewport()?.GetVisibleRect().Size ??
                               new Vector2(_viewportWidth, 900.0f);
        float aspectRatio = MathF.Max(0.5f, viewportSize.X / MathF.Max(1.0f, viewportSize.Y));
        // Insets are part of framing, not player zoom. Product viewports from
        // 16:9 through 16:10 have enough horizontal room for the authored board,
        // so only half of the aspect-ratio compensation is needed. Applying the
        // full 16:9/16:10 ratio made the board unnecessarily small vertically on
        // 2560x1600. Narrow diagnostic viewports retain the conservative full
        // compensation so headless structural smoke cannot clip the side zones.
        float aspectFramingScale = ReferenceAspectRatio / aspectRatio;
        if (aspectRatio >= MinimumProductAspectRatio)
        {
            aspectFramingScale = Mathf.Lerp(
                1.0f,
                aspectFramingScale,
                ProductAspectFramingWeight);
        }
        float framingScale = MathF.Max(
            MathF.Max(1.0f, ReferenceSafeWidthRatio / safeRatio),
            aspectFramingScale);
        Position = BattlefieldPerspective.CameraPosition(_zoom, framingScale);
        // Camera HOffset moves the projected scene in the opposite screen direction.
        // A wider right HUD therefore needs a positive camera offset so the board
        // shifts left into the center of the remaining play area.
        HOffset = ((_rightInset - _leftInset) / _viewportWidth) *
                  HudInsetToWorldScale * _zoom;
        VOffset = -0.62f * _zoom;
        LookAt(Vector3.Zero, Vector3.Up);
    }
}
