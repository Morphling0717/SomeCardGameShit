// SPDX-License-Identifier: GPL-3.0-or-later
using Scgs.Client;

namespace Scgs.GodotClient.Visuals;

public enum BattlefieldFxKind
{
    Damage = 0,
    Healing = 1,
    Phase = 2,
    Reaction = 3,
    Graveyard = 4,
}

/// <summary>
/// A deliberately identity-free visual cue. It can be produced from a
/// viewer-safe event stream without carrying a card id, definition, name,
/// tooltip or arbitrary text into resolving/covered presentation state.
/// </summary>
public readonly record struct BattlefieldFxCue(
    ulong Sequence,
    BattlefieldFxKind Kind,
    PlayerId? Player = null,
    int Value = 0);
