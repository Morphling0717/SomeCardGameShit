// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;
using Scgs.Hotseat;

namespace Scgs.GodotClient.Battlefield;

/// <summary>
/// Values 0-7 intentionally mirror Scgs.Hotseat.HotseatSurfaceKind. StandbyPile is a
/// Godot-only navigation surface and must be handled before converting to a rules intent.
/// </summary>
public enum BattlefieldSurfaceKind : uint
{
    HandCard = 0,
    Unit = 1,
    Tactic = 2,
    UnitSlot = 3,
    TacticSlot = 4,
    StandbyCard = 5,
    Leader = 6,
    CastZone = 7,
    StandbyPile = 8,
}

public enum BattlefieldSurfaceGesture : uint
{
    Click = 0,
    Drag = 1,
}

public enum BattlefieldHighlightKind : uint
{
    None = 0,
    Legal = 1,
    Selected = 2,
    Destination = 3,
}

public enum BattlefieldCardLayout
{
    Field,
    NearHand,
    FarHand,
    Pile,
}

public readonly record struct BattlefieldSurfaceRef(
    BattlefieldSurfaceKind Kind,
    PlayerId? Player = null,
    int? Index = null,
    ulong? InstanceId = null)
{
    public bool HasStableIdentity => InstanceId.HasValue;

    public static BattlefieldSurfaceRef StandbyPile(PlayerId player) =>
        new(BattlefieldSurfaceKind.StandbyPile, player);

    public HotseatSurfaceRef ToHotseat() => new(
        Kind switch
        {
            BattlefieldSurfaceKind.HandCard => HotseatSurfaceKind.HandCard,
            BattlefieldSurfaceKind.Unit => HotseatSurfaceKind.Unit,
            BattlefieldSurfaceKind.Tactic => HotseatSurfaceKind.Tactic,
            BattlefieldSurfaceKind.UnitSlot => HotseatSurfaceKind.UnitSlot,
            BattlefieldSurfaceKind.TacticSlot => HotseatSurfaceKind.TacticSlot,
            BattlefieldSurfaceKind.StandbyCard => HotseatSurfaceKind.StandbyCard,
            BattlefieldSurfaceKind.Leader => HotseatSurfaceKind.Leader,
            BattlefieldSurfaceKind.CastZone => HotseatSurfaceKind.CastZone,
            BattlefieldSurfaceKind.StandbyPile => throw new InvalidOperationException(
                "The Godot-only standby pile cannot be converted to a game intent."),
            _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unsupported surface kind."),
        },
        Player,
        Index,
        InstanceId);

    public static BattlefieldSurfaceRef FromHotseat(HotseatSurfaceRef surface) => new(
        surface.Kind switch
        {
            HotseatSurfaceKind.HandCard => BattlefieldSurfaceKind.HandCard,
            HotseatSurfaceKind.Unit => BattlefieldSurfaceKind.Unit,
            HotseatSurfaceKind.Tactic => BattlefieldSurfaceKind.Tactic,
            HotseatSurfaceKind.UnitSlot => BattlefieldSurfaceKind.UnitSlot,
            HotseatSurfaceKind.TacticSlot => BattlefieldSurfaceKind.TacticSlot,
            HotseatSurfaceKind.StandbyCard => BattlefieldSurfaceKind.StandbyCard,
            HotseatSurfaceKind.Leader => BattlefieldSurfaceKind.Leader,
            HotseatSurfaceKind.CastZone => BattlefieldSurfaceKind.CastZone,
            _ => throw new ArgumentOutOfRangeException(
                nameof(surface),
                surface.Kind,
                "Unsupported surface kind."),
        },
        surface.Player,
        surface.Index,
        surface.InstanceId);
}

public readonly record struct BattlefieldInteractionSurface(
    BattlefieldSurfaceRef Surface,
    BattlefieldHighlightKind Highlight = BattlefieldHighlightKind.Legal);

public sealed record BattlefieldCardPresentation(
    ulong? InstanceId,
    uint? DefinitionId,
    string Name,
    CardKind? Kind,
    PlayerId Controller,
    Zone Zone,
    int Cost,
    int Attack,
    int Health,
    int MaximumHealth,
    int Countdown,
    bool FaceDown,
    bool KnownIdentity);

public sealed class BattlefieldSurfaceGestureEventArgs : EventArgs
{
    public BattlefieldSurfaceGestureEventArgs(
        ulong revision,
        BattlefieldSurfaceGesture gesture,
        BattlefieldSurfaceRef source,
        BattlefieldSurfaceRef? destination)
    {
        Revision = revision;
        Gesture = gesture;
        Source = source;
        Destination = destination;
    }

    public ulong Revision { get; }

    public BattlefieldSurfaceGesture Gesture { get; }

    public BattlefieldSurfaceRef Source { get; }

    public BattlefieldSurfaceRef? Destination { get; }

    public HotseatSurfaceIntent ToHotseatIntent(ActionKind? action = null) => new(
        Revision,
        Gesture switch
        {
            BattlefieldSurfaceGesture.Click => HotseatSurfaceGesture.Click,
            BattlefieldSurfaceGesture.Drag => HotseatSurfaceGesture.Drag,
            _ => throw new ArgumentOutOfRangeException(
                nameof(Gesture),
                Gesture,
                "Unsupported surface gesture."),
        },
        Source.ToHotseat())
    {
        Destination = Destination?.ToHotseat(),
        Action = action,
    };
}

public sealed class BattlefieldSurfaceHoverEventArgs : EventArgs
{
    public BattlefieldSurfaceHoverEventArgs(
        BattlefieldSurfaceRef? surface,
        BattlefieldCardPresentation? card,
        Vector3 worldAnchor)
    {
        Surface = surface;
        Card = card;
        WorldAnchor = worldAnchor;
    }

    public BattlefieldSurfaceRef? Surface { get; }

    public BattlefieldCardPresentation? Card { get; }

    public Vector3 WorldAnchor { get; }
}

internal interface IBattlefieldPickTarget
{
    BattlefieldSurfaceRef? Surface { get; }

    BattlefieldCardPresentation? CardPresentation { get; }

    Vector3 WorldAnchor { get; }

    bool CanActivate { get; }

    void SetPointerHovered(bool hovered);
}
