// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;

namespace Scgs.GodotClient.Battlefield;

/// <summary>
/// Computes a stable screen-space hand and projects it back into the main 3D
/// viewport. Cards therefore remain ordinary pooled/raycastable 3D actors while
/// reading like a deliberate 2.5D hand instead of objects lying on the table.
/// </summary>
public sealed class BattlefieldHandRig
{
    public const float ReferenceNearPixelHeight = 184.0f;
    public const float ReferenceFarPixelHeight = 76.0f;
    private const float NearCameraDepth = 7.2f;
    private const float FarCameraDepth = 9.2f;
    private const float HoverForward = 0.52f;
    private const float SelectedForward = 0.28f;
    private const float CardReliefDepthStep = 0.135f;

    private readonly Camera3D _camera;
    private BattlefieldViewportLayout _layout;
    private ArenaVisualProfile _visualProfile;

    public BattlefieldHandRig(
        Camera3D camera,
        BattlefieldViewportLayout layout,
        BattlefieldVisualProfile visualProfile = BattlefieldVisualProfile.AnimeV1)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _layout = layout;
        _visualProfile = ArenaVisualProfile.Resolve(visualProfile);
    }

    public BattlefieldViewportLayout Layout => _layout;

    public void SetViewportLayout(BattlefieldViewportLayout layout) => _layout = layout;

    public void SetVisualProfile(BattlefieldVisualProfile profile) =>
        _visualProfile = ArenaVisualProfile.Resolve(profile);

    public HandCardPose CreatePose(
        PlayerId player,
        PlayerId viewer,
        int index,
        int count,
        int? hoveredIndex = null,
        int? selectedIndex = null)
    {
        ValidatePlayer(player);
        ValidatePlayer(viewer);
        if (count is < 1 or > BattlefieldPerspective.MaximumHandCards ||
            index < 0 || index >= count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        if (hoveredIndex is < 0 || hoveredIndex >= count)
        {
            throw new ArgumentOutOfRangeException(nameof(hoveredIndex));
        }
        if (selectedIndex is < 0 || selectedIndex >= count)
        {
            throw new ArgumentOutOfRangeException(nameof(selectedIndex));
        }

        bool near = BattlefieldPerspective.IsNear(player, viewer);
        bool hovered = near && hoveredIndex == index;
        bool selected = near && selectedIndex == index;
        int? focusIndex = hoveredIndex ?? selectedIndex;
        HandVisualProfile hand = _visualProfile.Hand;
        float basePixelHeight = near
            ? PixelHeightFor(
                _layout.ViewportSize.Y,
                hand.NearHeightRatio,
                hand.NearMinimumHeight,
                hand.NearMaximumHeight)
            : PixelHeightFor(
                _layout.ViewportSize.Y,
                hand.FarHeightRatio,
                hand.FarMinimumHeight,
                hand.FarMaximumHeight);
        float focusScale = hovered ? hand.HoverScale : selected ? hand.SelectedScale : 1.0f;
        float pixelHeight = basePixelHeight * focusScale;
        float pixelWidth = pixelHeight * BattlefieldPerspective.CardWidth /
                           BattlefieldPerspective.CardDepth;
        float center = (count - 1) / 2.0f;
        float offset = index - center;
        float normalized = count == 1 ? 0.0f : offset / MathF.Max(1.0f, center);

        Rect2 safe = _layout.SafeRect;
        float maximumSpan = safe.Size.X *
                            (near ? hand.NearMaximumSpanRatio : hand.FarMaximumSpanRatio);
        float nominalSpacing = near ? hand.NearNominalSpacing : hand.FarNominalSpacing;
        float spacing = count == 1
            ? 0.0f
            : MathF.Min(
                nominalSpacing,
                MathF.Max(32.0f, (maximumSpan - basePixelHeight *
                    BattlefieldPerspective.CardWidth / BattlefieldPerspective.CardDepth) /
                    (count - 1)));
        float screenX = safe.GetCenter().X + (offset * spacing);
        if (near && focusIndex.HasValue && focusIndex.Value != index)
        {
            int direction = Math.Sign(index - focusIndex.Value);
            float distanceWeight = 1.0f / MathF.Max(1.0f, Math.Abs(index - focusIndex.Value));
            screenX += direction * hand.FocusNeighborSpread * distanceWeight;
        }

        float edgeArc = MathF.Pow(MathF.Abs(normalized), 1.55f) * (near ? 17.0f : 7.0f);
        float baseY = near
            ? _layout.ViewportSize.Y - _layout.BottomReservedPixels -
              (basePixelHeight * 0.5f) - 4.0f
            : _layout.TopReservedPixels + (basePixelHeight * 0.5f) + 4.0f;
        float screenY = near ? baseY - edgeArc : baseY + edgeArc;
        if (hovered)
        {
            screenY -= hand.HoverLiftPixels;
        }
        else if (selected)
        {
            screenY -= hand.SelectedLiftPixels;
        }

        float rollDegrees = normalized *
                            (near ? hand.NearMaximumRoll : -hand.FarMaximumRoll);
        float depthStep = CardReliefDepthStep;
        // A candidate card's labels sit roughly 0.10 world units above its
        // face. Each following card must therefore advance beyond that
        // relief, and a focused card must advance beyond the entire fan.
        bool candidateFocused = hovered || selected;
        float focusForward = candidateFocused
            ? BattlefieldPerspective.MaximumHandCards * depthStep +
              (hovered ? 0.22f : 0.12f)
            : hovered ? HoverForward : selected ? SelectedForward : 0.0f;
        float cameraDepth = (near ? NearCameraDepth : FarCameraDepth) -
                            (index * depthStep) -
                            focusForward;
        Vector2 screenCenter = new(screenX, screenY);
        Rect2 screenBounds = RotatedBounds(screenCenter, pixelWidth, pixelHeight, rollDegrees);
        float topLimit = near ? 0.0f : _layout.TopReservedPixels;
        float bottomLimit = near
            ? _layout.ViewportSize.Y - _layout.BottomReservedPixels
            : _layout.ViewportSize.Y;
        if (screenBounds.Position.Y < topLimit)
        {
            screenCenter.Y += topLimit - screenBounds.Position.Y;
            screenBounds = RotatedBounds(screenCenter, pixelWidth, pixelHeight, rollDegrees);
        }
        if (screenBounds.End.Y > bottomLimit)
        {
            screenCenter.Y -= screenBounds.End.Y - bottomLimit;
            screenBounds = RotatedBounds(screenCenter, pixelWidth, pixelHeight, rollDegrees);
        }
        Transform3D transform = CreateCameraFacingTransform(
            screenCenter,
            cameraDepth,
            pixelHeight,
            rollDegrees);

        return new HandCardPose(
            player,
            index,
            count,
            near,
            hovered,
            selected,
            screenCenter,
            screenBounds,
            pixelHeight,
            rollDegrees,
            cameraDepth,
            transform);
    }

    public static float NearPixelHeightFor(float viewportHeight) =>
        Mathf.Clamp(viewportHeight * 0.205f, 148.0f, 238.0f);

    public static float FarPixelHeightFor(float viewportHeight) =>
        Mathf.Clamp(viewportHeight * 0.085f, 60.0f, 104.0f);

    private static float PixelHeightFor(
        float viewportHeight,
        float ratio,
        float minimum,
        float maximum) => Mathf.Clamp(viewportHeight * ratio, minimum, maximum);

    private Transform3D CreateCameraFacingTransform(
        Vector2 screenCenter,
        float cameraDepth,
        float pixelHeight,
        float rollDegrees)
    {
        Vector3 origin = _camera.ProjectPosition(screenCenter, cameraDepth);
        Basis cameraBasis = _camera.GlobalTransform.Basis.Orthonormalized();
        Vector3 right = cameraBasis.X.Normalized();
        Vector3 normal = cameraBasis.Z.Normalized();
        Vector3 down = -cameraBasis.Y.Normalized();
        float radians = Mathf.DegToRad(rollDegrees);
        float cosine = MathF.Cos(radians);
        float sine = MathF.Sin(radians);
        Vector3 rolledRight = (right * cosine) + (down * sine);
        Vector3 rolledDown = (-right * sine) + (down * cosine);

        float viewportHeight = MathF.Max(1.0f, _layout.ViewportSize.Y);
        float worldHeight = 2.0f * cameraDepth *
                            MathF.Tan(Mathf.DegToRad(_camera.Fov) * 0.5f);
        if (_camera.Projection == Camera3D.ProjectionType.Orthogonal)
        {
            // Project through the real lens: the fixed screen-space hand must
            // not shrink with a table-lens change or depend on camera depth.
            worldHeight = _camera.ProjectPosition(new Vector2(0, viewportHeight), cameraDepth)
                .DistanceTo(_camera.ProjectPosition(Vector2.Zero, cameraDepth));
        }
        float scale = pixelHeight * worldHeight /
                      (viewportHeight * BattlefieldPerspective.CardDepth);
        Basis basis = new(
            rolledRight * scale,
            normal * scale,
            rolledDown * scale);
        return new Transform3D(basis, origin);
    }

    private static Rect2 RotatedBounds(
        Vector2 center,
        float width,
        float height,
        float rollDegrees)
    {
        float radians = Mathf.DegToRad(rollDegrees);
        float cosine = MathF.Abs(MathF.Cos(radians));
        float sine = MathF.Abs(MathF.Sin(radians));
        Vector2 size = new(
            (width * cosine) + (height * sine),
            (width * sine) + (height * cosine));
        return new Rect2(center - (size * 0.5f), size);
    }

    private static void ValidatePlayer(PlayerId player)
    {
        if (player is not (PlayerId.Player0 or PlayerId.Player1))
        {
            throw new ArgumentOutOfRangeException(nameof(player), player, "Unsupported player value.");
        }
    }
}
