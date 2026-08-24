// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;

namespace Scgs.GodotClient.Battlefield;

public static class BattlefieldPerspective
{
    public const int UnitSlotCount = 5;
    public const int TacticSlotCount = 3;
    public const int MaximumHandCards = 10;
    public const float BoardWidth = 19.8f;
    public const float BoardDepth = 16.6f;
    public const float CardWidth = 1.58f;
    public const float CardDepth = 2.18f;
    public const float SlotWidth = 1.88f;
    public const float SlotDepth = 2.48f;
    public const float TerritoryBoundaryClearance = 0.12f;
    public const float CameraFovDegrees = 58.0f;
    public const float CameraPitchDegrees = 58.0f;
    public const float MinimumZoom = 0.82f;
    public const float MaximumZoom = 1.24f;

    private const float UnitSpacing = 2.4f;
    private const float TacticSpacing = 3.15f;
    private const float UnitRowDepth = 1.55f;
    private const float TacticRowDepth = 4.10f;
    private const float NearHandNominalSpacing = 1.28f;
    private const float FarHandNominalSpacing = 0.94f;
    private const float NearHandMaximumSpan = 9.7f;
    private const float FarHandMaximumSpan = 7.3f;
    private const float NearHandPreferredScale = 0.94f;
    private const float FarHandPreferredScale = 0.64f;
    private const float MinimumVisibleHandStrip = 0.46f;
    private const float SideZoneX = 7.1f;
    private const float ZonePileScale = 0.82f;
    private const float DeckDepth = 1.25f;
    private const float GraveyardDepth = 3.45f;
    private const float ArchiveDepth = 5.65f;
    private const float StandbyDepth = 1.45f;
    private const float CornerZoneDepth = 5.35f;

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
        float z = IsNear(player, viewer) ? UnitRowDepth : -UnitRowDepth;
        return CreateFlatTransform(player, viewer, new Vector3(x, 0.22f, z));
    }

    public static Transform3D TacticTransform(
        PlayerId player,
        PlayerId viewer,
        int slot)
    {
        int visual = VisualSlotIndex(player, viewer, slot, TacticSlotCount);
        float x = (visual - ((TacticSlotCount - 1) / 2.0f)) * TacticSpacing;
        float z = IsNear(player, viewer) ? TacticRowDepth : -TacticRowDepth;
        return CreateFlatTransform(player, viewer, new Vector3(x, 0.18f, z));
    }

    public static Transform3D LeaderTransform(PlayerId player, PlayerId viewer)
    {
        float z = IsNear(player, viewer) ? CornerZoneDepth : -CornerZoneDepth;
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
        float spacing = HandSpacing(near, count);
        float scale = HandScale(near, count);
        float center = (count - 1) / 2.0f;
        float offset = index - center;
        float x = offset * spacing;
        float z = near ? 6.15f + (MathF.Abs(offset) * 0.025f) : -6.05f;
        float y = near ? 0.48f + (MathF.Abs(offset) * 0.015f) : 0.32f;
        float fanDegrees = near ? -offset * 2.0f : -offset * 0.8f;
        float facingDegrees = (near ? 0.0f : 180.0f) + fanDegrees;
        Basis basis = Basis
            .FromEuler(new Vector3(0.0f, Mathf.DegToRad(facingDegrees), 0.0f))
            .Scaled(Vector3.One * scale);
        return new Transform3D(basis, new Vector3(x, y, z));
    }

    public static float HandSpacing(bool near, int count)
    {
        if (count <= 0 || count > MaximumHandCards)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (count == 1)
        {
            return 0.0f;
        }

        float nominal = near ? NearHandNominalSpacing : FarHandNominalSpacing;
        float maximumSpan = near ? NearHandMaximumSpan : FarHandMaximumSpan;
        return MathF.Min(nominal, maximumSpan / (count - 1));
    }

    public static float HandScale(bool near, int count)
    {
        if (count <= 0 || count > MaximumHandCards)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        float preferred = near ? NearHandPreferredScale : FarHandPreferredScale;
        if (count == 1)
        {
            return preferred;
        }

        // Product hands deliberately overlap like a physical fan.  Scaling is
        // stable across a revision so hover never causes the whole hand to
        // breathe, while spacing preserves a generous visible selection strip.
        return preferred;
    }

    public static Transform3D StandbyTransform(
        PlayerId player,
        PlayerId viewer,
        int index,
        int count)
    {
        ValidateCountAndIndex(count, index);
        float side = IsNear(player, viewer) ? 1.0f : -1.0f;
        float z = StandbyDepth * side;
        float xBase = -SideZoneX * side;
        float offset = (index - ((count - 1) / 2.0f)) * 0.26f * side;
        return CreateFlatTransform(
            player,
            viewer,
            new Vector3(xBase + offset, 0.3f + (index * 0.025f), z),
            ZonePileScale);
    }

    public static Transform3D StandbyPileTransform(PlayerId player, PlayerId viewer)
    {
        float side = IsNear(player, viewer) ? 1.0f : -1.0f;
        return CreateFlatTransform(
            player,
            viewer,
            new Vector3(-SideZoneX * side, 0.3f, StandbyDepth * side),
            ZonePileScale);
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
            Zone.Deck => new Vector3(SideZoneX * side, 0.25f, DeckDepth * side),
            Zone.Graveyard => new Vector3(SideZoneX * side, 0.25f, GraveyardDepth * side),
            Zone.Archive => new Vector3(SideZoneX * side, 0.25f, ArchiveDepth * side),
            _ => throw new ArgumentOutOfRangeException(nameof(zone), zone, "Unsupported pile zone."),
        };
        return CreateFlatTransform(player, viewer, position, ZonePileScale);
    }

    public static bool ValidateStaticSpacing(PlayerId viewer)
    {
        ValidatePlayer(viewer);
        PlayerId opposingViewer = viewer == PlayerId.Player0
            ? PlayerId.Player1
            : PlayerId.Player0;
        foreach (Zone zone in new[] { Zone.Deck, Zone.Graveyard, Zone.Archive })
        {
            if (!OriginsAreMirrored(
                    PileTransform(viewer, viewer, zone),
                    PileTransform(opposingViewer, viewer, zone)))
            {
                return false;
            }
        }

        if (!OriginsAreMirrored(
                LeaderTransform(viewer, viewer),
                LeaderTransform(opposingViewer, viewer)) ||
            !OriginsAreMirrored(
                StandbyPileTransform(viewer, viewer),
                StandbyPileTransform(opposingViewer, viewer)))
        {
            return false;
        }

        for (int count = 1; count <= MaximumHandCards; ++count)
        {
            for (int index = 0; index < count; ++index)
            {
                Transform3D nearStandby = StandbyTransform(viewer, viewer, index, count);
                Transform3D farStandby = StandbyTransform(
                    opposingViewer,
                    viewer,
                    index,
                    count);
                if (!OriginsAreMirrored(nearStandby, farStandby) ||
                    !IsEntirelyInPlayerTerritory(
                        viewer,
                        viewer,
                        nearStandby,
                        CardWidth,
                        CardDepth) ||
                    !IsEntirelyInPlayerTerritory(
                        opposingViewer,
                        viewer,
                        farStandby,
                        CardWidth,
                        CardDepth) ||
                    !IsEntirelyOnBoard(nearStandby, CardWidth, CardDepth) ||
                    !IsEntirelyOnBoard(farStandby, CardWidth, CardDepth))
                {
                    return false;
                }
            }
        }

        foreach (PlayerId player in Enum.GetValues<PlayerId>())
        {
            Transform3D deck = PileTransform(player, viewer, Zone.Deck);
            Transform3D graveyard = PileTransform(player, viewer, Zone.Graveyard);
            Transform3D archive = PileTransform(player, viewer, Zone.Archive);
            Transform3D standby = StandbyPileTransform(player, viewer);
            Transform3D leader = LeaderTransform(player, viewer);
            Transform3D[] piles = [deck, graveyard, archive];

            if (piles.Any(transform =>
                    !IsEntirelyInPlayerTerritory(
                        player,
                        viewer,
                        transform,
                        SlotWidth,
                        SlotDepth) ||
                    !IsEntirelyOnBoard(transform, SlotWidth, SlotDepth)))
            {
                return false;
            }

            if (!RectanglesAreSeparated(deck, SlotWidth, SlotDepth, graveyard, SlotWidth, SlotDepth) ||
                !RectanglesAreSeparated(graveyard, SlotWidth, SlotDepth, archive, SlotWidth, SlotDepth) ||
                !IsEntirelyInPlayerTerritory(player, viewer, standby, CardWidth, CardDepth) ||
                !IsEntirelyOnBoard(standby, CardWidth, CardDepth) ||
                !IsEntirelyInPlayerTerritory(player, viewer, leader, SlotWidth, SlotDepth) ||
                !IsEntirelyOnBoard(leader, SlotWidth, SlotDepth) ||
                !RectanglesAreSeparated(
                    standby,
                    CardWidth,
                    CardDepth,
                    leader,
                    SlotWidth,
                    SlotDepth))
            {
                return false;
            }

            for (int slot = 0; slot < UnitSlotCount; ++slot)
            {
                if (!IsEntirelyInPlayerTerritory(
                        player,
                        viewer,
                        UnitTransform(player, viewer, slot),
                        SlotWidth,
                        SlotDepth))
                {
                    return false;
                }
            }

            for (int slot = 0; slot < TacticSlotCount; ++slot)
            {
                if (!IsEntirelyInPlayerTerritory(
                        player,
                        viewer,
                        TacticTransform(player, viewer, slot),
                        SlotWidth,
                        SlotDepth))
                {
                    return false;
                }
            }
        }

        for (int count = 1; count <= MaximumHandCards; ++count)
        {
            foreach (bool near in new[] { false, true })
            {
                if (count > 1)
                {
                    if (HandSpacing(near, count) < MinimumVisibleHandStrip)
                    {
                        return false;
                    }
                }

                PlayerId player = near
                    ? viewer
                    : viewer == PlayerId.Player0
                        ? PlayerId.Player1
                        : PlayerId.Player0;
                for (int index = 0; index < count; ++index)
                {
                    if (!IsEntirelyInPlayerTerritory(
                            player,
                            viewer,
                            HandTransform(player, viewer, index, count),
                            CardWidth,
                            CardDepth))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    public static Vector3 CameraPosition(float zoom, float framingScale = 1.0f)
    {
        if (!float.IsFinite(zoom) || !float.IsFinite(framingScale) || framingScale <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(zoom));
        }

        float clamped = Mathf.Clamp(zoom, MinimumZoom, MaximumZoom);
        float radians = Mathf.DegToRad(CameraPitchDegrees);
        const float baseDistance = 17.0f;
        float distance = baseDistance * clamped * Mathf.Clamp(framingScale, 1.0f, 2.0f);
        return new Vector3(
            0.0f,
            MathF.Sin(radians) * distance,
            MathF.Cos(radians) * distance);
    }

    private static Transform3D CreateFlatTransform(
        PlayerId player,
        PlayerId viewer,
        Vector3 position,
        float uniformScale = 1.0f)
    {
        if (!float.IsFinite(uniformScale) || uniformScale <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(uniformScale));
        }

        float facingDegrees = IsNear(player, viewer) ? 0.0f : 180.0f;
        Basis basis = Basis
            .FromEuler(new Vector3(0.0f, Mathf.DegToRad(facingDegrees), 0.0f))
            .Scaled(Vector3.One * uniformScale);
        return new Transform3D(basis, position);
    }

    private static bool IsEntirelyInPlayerTerritory(
        PlayerId player,
        PlayerId viewer,
        Transform3D transform,
        float width,
        float depth)
    {
        float side = IsNear(player, viewer) ? 1.0f : -1.0f;
        return (transform.Origin.Z * side) - HalfDepth(transform, width, depth) >=
               TerritoryBoundaryClearance;
    }

    private static bool IsEntirelyOnBoard(
        Transform3D transform,
        float width,
        float depth)
    {
        float halfWidth = HalfWidth(transform, width, depth);
        float halfDepth = HalfDepth(transform, width, depth);
        return MathF.Abs(transform.Origin.X) + halfWidth <= (BoardWidth / 2.0f) &&
               MathF.Abs(transform.Origin.Z) + halfDepth <= (BoardDepth / 2.0f);
    }

    private static bool RectanglesAreSeparated(
        Transform3D left,
        float leftWidth,
        float leftDepth,
        Transform3D right,
        float rightWidth,
        float rightDepth)
    {
        float requiredX = HalfWidth(left, leftWidth, leftDepth) +
                          HalfWidth(right, rightWidth, rightDepth) +
                          TerritoryBoundaryClearance;
        float requiredZ = HalfDepth(left, leftWidth, leftDepth) +
                          HalfDepth(right, rightWidth, rightDepth) +
                          TerritoryBoundaryClearance;
        return MathF.Abs(left.Origin.X - right.Origin.X) >= requiredX ||
               MathF.Abs(left.Origin.Z - right.Origin.Z) >= requiredZ;
    }

    private static float HalfWidth(Transform3D transform, float width, float depth) =>
        (MathF.Abs(transform.Basis.X.X) * width / 2.0f) +
        (MathF.Abs(transform.Basis.Z.X) * depth / 2.0f);

    private static float HalfDepth(Transform3D transform, float width, float depth) =>
        (MathF.Abs(transform.Basis.X.Z) * width / 2.0f) +
        (MathF.Abs(transform.Basis.Z.Z) * depth / 2.0f);

    private static bool OriginsAreMirrored(Transform3D near, Transform3D far) =>
        MathF.Abs(near.Origin.X + far.Origin.X) <= 0.0001f &&
        MathF.Abs(near.Origin.Y - far.Origin.Y) <= 0.0001f &&
        MathF.Abs(near.Origin.Z + far.Origin.Z) <= 0.0001f;

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
