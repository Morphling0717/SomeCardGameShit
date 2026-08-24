// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

namespace Scgs.GodotClient.Battlefield;

/// <summary>
/// Stable screen-space lanes reserved around the authored battlefield.  The
/// product layout reserves the expanded detail/status lanes even while their
/// controls are collapsed, so opening a drawer never moves the board or a live
/// hit target underneath the pointer.
/// </summary>
public readonly record struct BattlefieldViewportLayout
{
    private const float MinimumSafeWidth = 320.0f;
    private const float MinimumSafeHeight = 360.0f;

    public BattlefieldViewportLayout(
        Vector2 viewportSize,
        float leftReservedPixels,
        float rightReservedPixels,
        float topReservedPixels = 72.0f,
        float bottomReservedPixels = 12.0f)
    {
        ValidateViewport(viewportSize);
        ValidateInset(leftReservedPixels, nameof(leftReservedPixels));
        ValidateInset(rightReservedPixels, nameof(rightReservedPixels));
        ValidateInset(topReservedPixels, nameof(topReservedPixels));
        ValidateInset(bottomReservedPixels, nameof(bottomReservedPixels));

        float horizontalScale = MathF.Min(
            1.0f,
            MathF.Max(0.0f, viewportSize.X - MinimumSafeWidth) /
            MathF.Max(1.0f, leftReservedPixels + rightReservedPixels));
        float verticalScale = MathF.Min(
            1.0f,
            MathF.Max(0.0f, viewportSize.Y - MinimumSafeHeight) /
            MathF.Max(1.0f, topReservedPixels + bottomReservedPixels));

        ViewportSize = viewportSize;
        LeftReservedPixels = leftReservedPixels * horizontalScale;
        RightReservedPixels = rightReservedPixels * horizontalScale;
        TopReservedPixels = topReservedPixels * verticalScale;
        BottomReservedPixels = bottomReservedPixels * verticalScale;
    }

    public Vector2 ViewportSize { get; }

    public float LeftReservedPixels { get; }

    public float RightReservedPixels { get; }

    public float TopReservedPixels { get; }

    public float BottomReservedPixels { get; }

    public Rect2 SafeRect => new(
        new Vector2(LeftReservedPixels, TopReservedPixels),
        new Vector2(
            MathF.Max(1.0f, ViewportSize.X - LeftReservedPixels - RightReservedPixels),
            MathF.Max(1.0f, ViewportSize.Y - TopReservedPixels - BottomReservedPixels)));

    public static BattlefieldViewportLayout Product(
        Vector2 viewportSize,
        float paddingPixels = 12.0f)
    {
        ValidateViewport(viewportSize);
        ValidateInset(paddingPixels, nameof(paddingPixels));

        float edge = viewportSize.X < 1440.0f
            ? 12.0f
            : viewportSize.X >= 2200.0f
                ? 20.0f
                : 16.0f;
        float detailWidth = viewportSize.X switch
        {
            < 1440.0f => 240.0f,
            < 2200.0f => 288.0f,
            _ => 320.0f,
        };
        float statusWidth = viewportSize.X switch
        {
            < 1440.0f => 196.0f,
            < 2200.0f => 240.0f,
            _ => 264.0f,
        };

        return new BattlefieldViewportLayout(
            viewportSize,
            edge + detailWidth + paddingPixels,
            edge + statusWidth + paddingPixels,
            topReservedPixels: viewportSize.Y < 800.0f ? 66.0f : 76.0f,
            bottomReservedPixels: edge);
    }

    public static BattlefieldViewportLayout FromInsets(
        Vector2 viewportSize,
        float leftReservedPixels,
        float rightReservedPixels) => new(
            viewportSize,
            leftReservedPixels,
            rightReservedPixels);

    public BattlefieldViewportLayout WithViewportSize(Vector2 viewportSize) => new(
        viewportSize,
        LeftReservedPixels,
        RightReservedPixels,
        TopReservedPixels,
        BottomReservedPixels);

    public BattlefieldViewportLayout MaxHorizontalReservations(
        float leftReservedPixels,
        float rightReservedPixels) => new(
            ViewportSize,
            MathF.Max(LeftReservedPixels, leftReservedPixels),
            MathF.Max(RightReservedPixels, rightReservedPixels),
            TopReservedPixels,
            BottomReservedPixels);

    private static void ValidateViewport(Vector2 viewportSize)
    {
        if (!float.IsFinite(viewportSize.X) || !float.IsFinite(viewportSize.Y) ||
            viewportSize.X <= 0.0f || viewportSize.Y <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportSize));
        }
    }

    private static void ValidateInset(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0.0f)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}
