// SPDX-License-Identifier: GPL-3.0-or-later
namespace SomeCardGame.Protocol
{
    public enum ScgsGameMessage : byte
    {
        GameMode = 210,
        PlayerState = 211,
        UnitState = 212,
        EvolutionState = 213,
        AdvancedSummonState = 214,
        RequestEvolutionMode = 215,
        RequestMaterials = 216,
        RequestImprint = 217,
        TacticWindow = 218,
        MatchStatistics = 219,
    }
}
