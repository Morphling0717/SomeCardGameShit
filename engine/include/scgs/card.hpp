// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once

#include "scgs/types.hpp"

#include <stdexcept>
#include <string>
#include <unordered_map>
#include <vector>

namespace scgs {

// v0.4 data-driven card definition.
// Effects are stored as a list of EffectRecord entries; the engine interprets
// them instead of calling per-card C++ hooks.
// Boolean flags (printed_guard, printed_rush, …) express the v0.4 keyword
// semantics that v0.4 formal keyword names have not yet been finalised for.
struct CardDefinition {
    CardId id = 0;
    std::string name;
    CardKind kind = CardKind::Unit;
    int cost = 0;
    int attack = 0;
    int health = 0;
    int countdown = 0; // relics only

    // Printed boolean capability flags (v0.4 keyword semantics without named keywords).
    bool printed_guard    = false; // must be attacked first
    bool printed_rush     = false; // can attack enemy units on entry turn
    bool printed_storm    = false; // can attack leader on entry turn
    bool printed_barrier  = false; // first hit on this unit is absorbed
    bool printed_lifesteal = false; // damage dealt heals the controller's leader
    bool printed_bane     = false; // any damage dealt destroys target

    // 燃耗X: additional cost that permanently reduces PP capacity.
    AdditionalCost additional_cost;

    // All card effects expressed as structured records.
    std::vector<EffectRecord> effects;
};

class CardCatalog {
public:
    void add(CardDefinition definition);

    [[nodiscard]] bool contains(CardId id) const noexcept;
    [[nodiscard]] const CardDefinition& at(CardId id) const;
    [[nodiscard]] std::size_t size() const noexcept;

private:
    std::unordered_map<CardId, CardDefinition> definitions_;
};

struct LeaderSkillDefinition {
    std::string name;
    int cost = 0;
    std::vector<EffectRecord> effects;
};

struct DeckList {
    std::vector<CardId> main;
    std::vector<CardId> standby; // v0.4 战备区 (0-6 public standby cards)
    LeaderSkillDefinition leader_skill;
};

// -----------------------------------------------------------------------
// v0.4 §36 Test Deck A: 标准中速 (Standard Midrange)
// Tests PP curve, unit combat, evolution, traps, reactions, advance timing.
// -----------------------------------------------------------------------
namespace cards::midrange {

inline constexpr CardId kPioneerScout      = 1001; // 1PP 1/2, OnEntry:Draw1
inline constexpr CardId kGuardSentry       = 1002; // 1PP 1/3, Guard
inline constexpr CardId kAssaultVanguard   = 1003; // 2PP 3/1, Rush
inline constexpr CardId kShieldVanguard    = 1004; // 2PP 2/3, Guard
inline constexpr CardId kFieldCommander    = 1005; // 3PP 3/3, OnEvolution:Draw1
inline constexpr CardId kIronShieldBearer  = 1006; // 3PP 2/5, Guard
inline constexpr CardId kFortressGuard     = 1007; // 4PP 3/6, Guard+Barrier
inline constexpr CardId kEliteCommander    = 1009; // 5PP 5/5
inline constexpr CardId kPrecisionStrike   = 1010; // 2PP Spell, DealDmg3 to EnemyUnit
inline constexpr CardId kCombatSupply      = 1011; // 2PP Spell, HealLeader3
inline constexpr CardId kCommandOrder      = 1012; // 2PP Relic, countdown2, OnExpire:Draw1
inline constexpr CardId kInterceptTrap     = 1013; // 1PP Trap, OnBeforeAttack:CancelAttack
inline constexpr CardId kCounterTrap       = 1014; // 1PP Trap, OnAfterEnemyUnit:DmgUnit2

} // namespace cards::midrange

// -----------------------------------------------------------------------
// v0.4 §36 Test Deck B: 预支测试 (Advance PP Test)
// Tests advance mechanic, burn costs, cracks, repair, on-time/advance effects.
// -----------------------------------------------------------------------
namespace cards::advance {

inline constexpr CardId kAdvanceWarrior    = 2001; // 4PP 4/4, OnPlayIfAdvanced:Rush
inline constexpr CardId kOnTimeElite       = 2002; // 3PP 3/3, OnPlayIfNotAdvanced:Draw1
inline constexpr CardId kBurnWarrior       = 2003; // 1PP+burn2 3/3
inline constexpr CardId kRepairTechnician  = 2004; // 2PP 2/2, OnEntry:Repair2
inline constexpr CardId kCrackFeeder       = 2005; // 3PP 2/4, OnEntry:DealDmg(cracks≤3) to EnemyUnit
inline constexpr CardId kMassiveVanguard   = 2006; // 7PP 7/7
inline constexpr CardId kAdvanceStrike     = 2007; // 2PP+burn1 Spell, DealDmg4 to EnemyUnit
inline constexpr CardId kRepairWave        = 2008; // 2PP Spell, Repair2 + HealLeader2
inline constexpr CardId kBurnBlast         = 2009; // 1PP+burn2 Spell, DealDmg5 to EnemyUnit
inline constexpr CardId kGrowthFacility    = 2010; // 2PP Relic, countdown2, OnExpire:GainPPCapacity1
inline constexpr CardId kDebtLord          = 2011; // 8PP 8/6 (high-cost advance target)
inline constexpr CardId kReactionTrap      = 2012; // 1PP Trap, OnAfterEnemyUnit:DmgUnit2

} // namespace cards::advance

[[nodiscard]] CardCatalog make_v04_catalog();
[[nodiscard]] DeckList make_midrange_deck();
[[nodiscard]] DeckList make_advance_deck();

} // namespace scgs
