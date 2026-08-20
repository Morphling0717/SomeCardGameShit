// SPDX-License-Identifier: GPL-3.0-or-later
using System;

namespace SomeCardGame.Protocol
{
    [Flags]
    public enum ScgsPlayerFlags : byte
    {
        None = 0,
        EvolutionUsedThisTurn = 1 << 0,
        AdvancedSummonUsedThisTurn = 1 << 1,
        TrapSetThisTurn = 1 << 2,
        LeaderSkillUsed = 1 << 3,
    }

    [Flags]
    public enum ScgsUnitFlags : byte
    {
        None = 0,
        Evolved = 1 << 0,
        AttackedThisTurn = 1 << 1,
        EnteredThisTurn = 1 << 2,
        AdvancedSummonedThisTurn = 1 << 3,
        FaceDown = 1 << 4,
    }

    public sealed class ScgsPlayerState
    {
        public byte Player;
        public short LeaderHealth;
        public short MaximumLeaderHealth;
        public byte CurrentPP;
        public byte MaximumPP;
        public byte EvolutionPoints;
        public byte OwnTurnNumber;
        public ScgsPlayerFlags Flags;
    }

    public sealed class ScgsUnitState
    {
        public byte Controller;
        public byte Sequence;
        public ulong InstanceId;
        public short Attack;
        public short Health;
        public short MaximumHealth;
        public uint Keywords;
        public byte InheritedImprint;
        public ScgsUnitFlags Flags;
    }
}
