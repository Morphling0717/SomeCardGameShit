// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;

namespace Scgs.GodotClient.Battlefield;

public static class BattlefieldPerspective
{
    public const int UnitSlotCount = 5;
    public const int TacticSlotCount = 3;
    public const float BoardWidth = 18.4f;
    public const float BoardDepth = 13.6f;
    public const float CameraFovDegrees = 70.0f;
    public const float CameraPitchDegrees = 58.0f;
    public const float MinimumZoom = 0.82f;
    public const float MaximumZoom = 1.24f;

    private const float UnitSpacing = 2.4f;
    private const float TacticSpacing = 3.15f;

    public static bool IsNear(PlayerId player, PlayerId viewer)
    {
        ValidatePlayer(player);
        ValidatePlayer(viewer);
        return player == viewer;
    }

    public static int VisualSlotIndex(
        PlayerId player,
        PlayerId viewer,
        int slot,
        int slotCount)
    {
        ValidatePlayer(player);
        ValidatePlayer(viewer);
        ArgumentOutOfRangeException.ThrowIfNegative(slotCount);
        if (slot < 0 || slot >= slotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        return IsNear(player, viewer) ? slot : slotCount - 1 - slot;
    }

    public static Transform3D UnitTransform(
        PlayerId player,
        PlayerId viewer,
        int slot)
    {
        int visual = VisualSlotIndex(player, viewer, slot, UnitSlotCount);
        float x = (visual - ((UnitSlotCount - 1) / 2.0f)) * UnitSpacing;
        float z = IsNear(player, viewer) ? 1.45f : -1.45f;
        return CreateFlatTransform(player, viewer, new Vector3(x, 0.22f, z));
    }

    public static Transform3D TacticTransform(
        PlayerId player,
        PlayerId viewer,
        int slot)
    {
        int visual = VisualSlotIndex(player, viewer, slot, TacticSlotCount);
        float x = (visual - ((TacticSlotCount - 1) / 2.0f)) * TacticSpacing;
        float z = IsNear(player, viewer) ? 4.0f : -4.0f;
        return CreateFlatTransform(player, viewer, new Vector3(x, 0.18f, z));
    }

    public static Transform3D LeaderTransform(PlayerId player, PlayerId viewer)
    {
        float z = IsNear(player, viewer) ? 1.6f : -1.6f;
        float x = IsNear(player, viewer) ? -7.15f : 7.15f;
        return CreateFlatTransform(player, viewer, new Vector3(x, 0.26f, z));
    }

    public static Transform3D HandTransform(
        PlayerId player,
        PlayerId viewer,
        int index,
        int count)
    {
        ValidateCountAndIndex(count, index);
        bool near = IsNear(player, viewer);
        float spacing = count <= 1 ? 0.0f : MathF.Min(1.18f, 9.6f / (count - 1));
        float center = (count - 1) / 2.0f;
        float offset = index - center;
        float x = offset * spacing;
        float z = near ? 6.15f + (MathF.Abs(offset) * 0.025f) : -6.05f;
        float y = near ? 0.48f + (MathF.Abs(offset) * 0.015f) : 0.32f;
        float fanDegrees = near ? -offset * 2.5f : 0.0f;
        float facingDegrees = near ? fanDegrees : 180.0f;
        Basis basis = Basis.FromEuler(new Vector3(0.0f, Mathf.DegToRad(facingDegrees), 0.0f));
        return new Transform3D(basis, new Vector3(x, y, z));
    }

    public static Transform3D StandbyTransform(
        PlayerId player,
        PlayerId viewer,
        int index,
        int count)
    {
        ValidateCountAndIndex(count, index);
        float z = IsNear(player, viewer) ? 4.55f : -4.55f;
        float xBase = IsNear(player, viewer) ? 7.1f : -7.1f;
        float offset = (index - ((count - 1) / 2.0f)) * 0.34f;
        return CreateFlatTransform(
            player,
            viewer,
            new Vector3(xBase + offset, 0.3f + (index * 0.025f), z));
    }

    public static Transform3D StandbyPileTransform(PlayerId player, PlayerId viewer)
    {
        bool near = IsNear(player, viewer);
        float z = near ? 4.75f : -4.75f;
        float x = near ? 7.05f : -7.05f;
        return CreateFlatTransform(player, viewer, new Vector3(x, 0.3f, z));
    }

    public static Transform3D PileTransform(
        PlayerId player,
        PlayerId viewer,
        Zone zone)
    {
        bool near = IsNear(player, viewer);
        float side = near ? 1.0f : -1.0f;
        Vector3 position = zone switch
        {
            Zone.Deck => new Vector3(7.1f * side, 0.25f, 1.75f * side),
            Zone.Graveyard => new Vector3(7.1f * side, 0.25f, 0.0f),
            Zone.Archive => new Vector3(7.1f * side, 0.25f, -1.75f * side),
            _ => throw new ArgumentOutOfRangeException(nameof(zone), zone, "Unsupported pile zone."),
        };
        return CreateFlatTransform(player, viewer, position);
    }

    public static Transform3D CastZoneTransform(PlayerId viewer) =>
        CreateFlatTransform(viewer, viewer, new Vector3(0.0f, 0.13f, 0.0f));

    public static Vector3 CameraPosition(float zoom)
    {
        if (!float.IsFinite(zoom))
        {
            throw new ArgumentOutOfRangeException(nameof(zoom));
        }

        float clamped = Mathf.Clamp(zoom, MinimumZoom, MaximumZoom);
        float radians = Mathf.DegToRad(CameraPitchDegrees);
        const float baseDistance = 17.0f;
        float distance = baseDistance * clamped;
        return new Vector3(
            0.0f,
            MathF.Sin(radians) * distance,
            MathF.Cos(radians) * distance);
    }

    private static Transform3D CreateFlatTransform(
        PlayerId player,
        PlayerId viewer,
        Vector3 position)
    {
        float facingDegrees = IsNear(player, viewer) ? 0.0f : 180.0f;
        Basis basis = Basis.FromEuler(new Vector3(0.0f, Mathf.DegToRad(facingDegrees), 0.0f));
        return new Transform3D(basis, position);
    }

    private static void ValidateCountAndIndex(int count, int index)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (index < 0 || index >= count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    private static void ValidatePlayer(PlayerId player)
    {
        if (player is not (PlayerId.Player0 or PlayerId.Player1))
        {
            throw new ArgumentOutOfRangeException(nameof(player), player, "Unsupported player value.");
        }
    }
}
