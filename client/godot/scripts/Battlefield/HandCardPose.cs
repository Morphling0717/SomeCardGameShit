// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;

namespace Scgs.GodotClient.Battlefield;

/// <summary>A camera-locked 2.5D card pose and its real screen-space bounds.</summary>
public readonly record struct HandCardPose(
    PlayerId Player,
    int Index,
    int Count,
    bool Near,
    bool Hovered,
    bool Selected,
    Vector2 ScreenCenter,
    Rect2 ScreenBounds,
    float PixelHeight,
    float RollDegrees,
    float CameraDepth,
    Transform3D Transform);
