// SPDX-License-Identifier: GPL-3.0-or-later
#include "scgs/card.hpp"

#include <algorithm>
#include <utility>

namespace scgs {

void CardCatalog::add(CardDefinition definition) {
    if (definition.id == 0) {
        throw std::invalid_argument("card id 0 is reserved");
    }
    if (definition.cost < 0 || definition.attack < 0 || definition.health < 0) {
        throw std::invalid_argument("card numbers cannot be negative");
    }
    const auto [iterator, inserted] = definitions_.emplace(definition.id, std::move(definition));
    (void)iterator;
    if (!inserted) {
        throw std::invalid_argument("duplicate card id");
    }
}

bool CardCatalog::contains(const CardId id) const noexcept {
    return definitions_.contains(id);
}

const CardDefinition& CardCatalog::at(const CardId id) const {
    const auto iterator = definitions_.find(id);
    if (iterator == definitions_.end()) {
        throw std::out_of_range("unknown card id: " + std::to_string(id));
    }
    return iterator->second;
}

std::size_t CardCatalog::size() const noexcept {
    return definitions_.size();
}

namespace {

// Helpers to build effect records concisely.

EffectRecord on_play_draw(const int n) {
    return {EffectTrigger::OnPlay, EffectKind::DrawCards, n, TargetSpec::None};
}
EffectRecord on_play_if_advanced_rush() {
    // Grants Rush (stored as DrawCards 0 but triggers Rush grant logic).
    // We encode a special EffectKind and amount=-1 for "grant rush until end of turn".
    // Instead, use CancelAttack with amount=-1 sentinel? No – better to add a
    // BuffFriendlyUnit(0) which we special-case in game.cpp as "grant rush to self".
    // We encode this as BuffFriendlyUnit with amount=0 and target_spec=None
    // meaning "give self Rush keyword".
    return {EffectTrigger::OnPlayIfAdvanced, EffectKind::BuffFriendlyUnit, 0, TargetSpec::None};
}
EffectRecord on_play_if_not_advanced_draw(const int n) {
    return {EffectTrigger::OnPlayIfNotAdvanced, EffectKind::DrawCards, n, TargetSpec::None};
}
EffectRecord on_entry_draw(const int n) {
    return {EffectTrigger::OnEntry, EffectKind::DrawCards, n, TargetSpec::None};
}
EffectRecord on_entry_repair(const int n) {
    return {EffectTrigger::OnEntry, EffectKind::RepairCracks, n, TargetSpec::None};
}
EffectRecord on_entry_deal_damage_enemy_unit(const int n) {
    return {EffectTrigger::OnEntry, EffectKind::DealDamageToEnemyUnit, n, TargetSpec::EnemyUnit};
}
// Special effect: deal damage to enemy unit equal to player's crack count (capped at amount).
EffectRecord on_entry_deal_cracks_damage(const int cap) {
    // amount = -cap encodes "use cracks, capped at cap"; negative distinguishes from fixed.
    return {EffectTrigger::OnEntry, EffectKind::DealDamageToEnemyUnit, -cap, TargetSpec::EnemyUnit};
}
EffectRecord on_evolution_draw(const int n) {
    return {EffectTrigger::OnEvolution, EffectKind::DrawCards, n, TargetSpec::None};
}
EffectRecord on_countdown_expire_draw(const int n) {
    return {EffectTrigger::OnCountdownExpire, EffectKind::DrawCards, n, TargetSpec::None};
}
EffectRecord on_countdown_expire_gain_capacity(const int n) {
    return {EffectTrigger::OnCountdownExpire, EffectKind::GainPPCapacity, n, TargetSpec::None};
}
EffectRecord on_play_deal_damage_enemy_unit(const int n) {
    return {EffectTrigger::OnPlay, EffectKind::DealDamageToEnemyUnit, n, TargetSpec::EnemyUnit};
}
EffectRecord on_play_heal_leader(const int n) {
    return {EffectTrigger::OnPlay, EffectKind::HealLeader, n, TargetSpec::None};
}
EffectRecord on_play_repair(const int n) {
    return {EffectTrigger::OnPlay, EffectKind::RepairCracks, n, TargetSpec::None};
}
EffectRecord on_play_heal_leader_and_repair(const int heal, const int repair) {
    // Two separate records; caller adds both.
    (void)heal; (void)repair;
    return {};  // not used directly; caller creates two records
}
EffectRecord on_trap_before_attack_cancel() {
    return {EffectTrigger::OnTrapBeforeAttack, EffectKind::CancelAttack, 0, TargetSpec::None};
}
EffectRecord on_trap_after_enemy_unit_damage(const int n) {
    return {EffectTrigger::OnTrapAfterEnemyUnit, EffectKind::DamageEnteredUnit, n, TargetSpec::None};
}

CardDefinition unit(
    const CardId id,
    std::string name,
    const int cost,
    const int attack,
    const int health) {
    CardDefinition card;
    card.id = id;
    card.name = std::move(name);
    card.kind = CardKind::Unit;
    card.cost = cost;
    card.attack = attack;
    card.health = health;
    return card;
}

CardDefinition spell(const CardId id, std::string name, const int cost) {
    CardDefinition card;
    card.id = id;
    card.name = std::move(name);
    card.kind = CardKind::Spell;
    card.cost = cost;
    return card;
}

CardDefinition relic(
    const CardId id,
    std::string name,
    const int cost,
    const int countdown) {
    CardDefinition card;
    card.id = id;
    card.name = std::move(name);
    card.kind = CardKind::Relic;
    card.cost = cost;
    card.countdown = countdown;
    return card;
}

CardDefinition trap(const CardId id, std::string name, const int cost) {
    CardDefinition card;
    card.id = id;
    card.name = std::move(name);
    card.kind = CardKind::Trap;
    card.cost = cost;
    return card;
}

std::vector<CardId> twice(const std::initializer_list<CardId> ids) {
    std::vector<CardId> result;
    result.reserve(ids.size() * 2U);
    for (const CardId id : ids) {
        result.push_back(id);
        result.push_back(id);
    }
    return result;
}

// Build Deck A: 标准中速 (Standard Midrange) cards.
void add_midrange_cards(CardCatalog& catalog) {
    using namespace cards::midrange;

    // 先驱侦察兵: 1PP 1/2, OnEntry: Draw 1
    {
        auto c = unit(kPioneerScout, "先驱侦察兵", 1, 1, 2);
        c.effects.push_back(on_entry_draw(1));
        catalog.add(std::move(c));
    }
    // 护卫站岗者: 1PP 1/3, Guard
    {
        auto c = unit(kGuardSentry, "护卫站岗者", 1, 1, 3);
        c.printed_guard = true;
        catalog.add(std::move(c));
    }
    // 突击前锋: 2PP 3/1, Rush (attack units on entry turn)
    {
        auto c = unit(kAssaultVanguard, "突击前锋", 2, 3, 1);
        c.printed_rush = true;
        catalog.add(std::move(c));
    }
    // 前卫盾手: 2PP 2/3, Guard
    {
        auto c = unit(kShieldVanguard, "前卫盾手", 2, 2, 3);
        c.printed_guard = true;
        catalog.add(std::move(c));
    }
    // 战场指挥者: 3PP 3/3, OnEvolution: Draw 1
    {
        auto c = unit(kFieldCommander, "战场指挥者", 3, 3, 3);
        c.effects.push_back(on_evolution_draw(1));
        catalog.add(std::move(c));
    }
    // 坚守盾卫: 3PP 2/5, Guard
    {
        auto c = unit(kIronShieldBearer, "坚守盾卫", 3, 2, 5);
        c.printed_guard = true;
        catalog.add(std::move(c));
    }
    // 铁壁屏障: 4PP 3/6, Guard + Barrier
    {
        auto c = unit(kFortressGuard, "铁壁屏障", 4, 3, 6);
        c.printed_guard = true;
        c.printed_barrier = true;
        catalog.add(std::move(c));
    }
    // 精锐统帅: 5PP 5/5
    {
        auto c = unit(kEliteCommander, "精锐统帅", 5, 5, 5);
        catalog.add(std::move(c));
    }
    // 致命一击: 2PP Spell, OnPlay: DealDamage 3 to EnemyUnit
    {
        auto c = spell(kPrecisionStrike, "致命一击", 2);
        c.effects.push_back(on_play_deal_damage_enemy_unit(3));
        catalog.add(std::move(c));
    }
    // 后方支援: 2PP Spell, OnPlay: HealLeader 3
    {
        auto c = spell(kCombatSupply, "后方支援", 2);
        c.effects.push_back(on_play_heal_leader(3));
        catalog.add(std::move(c));
    }
    // 战令设施: 2PP Relic, countdown 2, OnExpire: Draw 1
    {
        auto c = relic(kCommandOrder, "战令设施", 2, 2);
        c.effects.push_back(on_countdown_expire_draw(1));
        catalog.add(std::move(c));
    }
    // 拦截伏策: 1PP Trap, OnTrapBeforeAttack: CancelAttack
    {
        auto c = trap(kInterceptTrap, "拦截伏策", 1);
        c.effects.push_back(on_trap_before_attack_cancel());
        catalog.add(std::move(c));
    }
    // 反制伏策: 1PP Trap, OnTrapAfterEnemyUnit: DamageEnteredUnit 2
    {
        auto c = trap(kCounterTrap, "反制伏策", 1);
        c.effects.push_back(on_trap_after_enemy_unit_damage(2));
        catalog.add(std::move(c));
    }
}

// Build Deck B: 预支测试 (Advance PP Test) cards.
void add_advance_cards(CardCatalog& catalog) {
    using namespace cards::advance;

    // 超前先锋: 4PP 4/4, OnPlayIfAdvanced: grant Rush this turn
    {
        auto c = unit(kAdvanceWarrior, "超前先锋", 4, 4, 4);
        c.effects.push_back(on_play_if_advanced_rush());
        catalog.add(std::move(c));
    }
    // 按期精英: 3PP 3/3, OnPlayIfNotAdvanced: Draw 1
    {
        auto c = unit(kOnTimeElite, "按期精英", 3, 3, 3);
        c.effects.push_back(on_play_if_not_advanced_draw(1));
        catalog.add(std::move(c));
    }
    // 燃耗战士: 1PP + burn2, 3/3
    {
        auto c = unit(kBurnWarrior, "燃耗战士", 1, 3, 3);
        c.additional_cost.burn_pp_capacity = 2;
        catalog.add(std::move(c));
    }
    // 修复技师: 2PP 2/2, OnEntry: Repair 2
    {
        auto c = unit(kRepairTechnician, "修复技师", 2, 2, 2);
        c.effects.push_back(on_entry_repair(2));
        catalog.add(std::move(c));
    }
    // 裂痕感知者: 3PP 2/4, OnEntry: Deal min(cracks,3) damage to enemy unit
    {
        auto c = unit(kCrackFeeder, "裂痕感知者", 3, 2, 4);
        c.effects.push_back(on_entry_deal_cracks_damage(3));
        catalog.add(std::move(c));
    }
    // 巨型先锋: 7PP 7/7 (high-cost target for advance testing)
    {
        auto c = unit(kMassiveVanguard, "巨型先锋", 7, 7, 7);
        catalog.add(std::move(c));
    }
    // 超前打击: 2PP + burn1 Spell, OnPlay: DealDamage 4 to EnemyUnit
    {
        auto c = spell(kAdvanceStrike, "超前打击", 2);
        c.additional_cost.burn_pp_capacity = 1;
        c.effects.push_back(on_play_deal_damage_enemy_unit(4));
        catalog.add(std::move(c));
    }
    // 修复之波: 2PP Spell, OnPlay: Repair 2 AND HealLeader 2
    {
        auto c = spell(kRepairWave, "修复之波", 2);
        c.effects.push_back(on_play_repair(2));
        c.effects.push_back(on_play_heal_leader(2));
        catalog.add(std::move(c));
    }
    // 燃耗爆破: 1PP + burn2 Spell, OnPlay: DealDamage 5 to EnemyUnit
    {
        auto c = spell(kBurnBlast, "燃耗爆破", 1);
        c.additional_cost.burn_pp_capacity = 2;
        c.effects.push_back(on_play_deal_damage_enemy_unit(5));
        catalog.add(std::move(c));
    }
    // 增长设施: 2PP Relic, countdown 2, OnExpire: GainPPCapacity 1
    {
        auto c = relic(kGrowthFacility, "增长设施", 2, 2);
        c.effects.push_back(on_countdown_expire_gain_capacity(1));
        catalog.add(std::move(c));
    }
    // 债务领主: 8PP 8/6 (expensive unit to advance into)
    {
        auto c = unit(kDebtLord, "债务领主", 8, 8, 6);
        catalog.add(std::move(c));
    }
    // 截击陷阱: 1PP Trap, OnTrapAfterEnemyUnit: DamageEnteredUnit 2
    {
        auto c = trap(kReactionTrap, "截击陷阱", 1);
        c.effects.push_back(on_trap_after_enemy_unit_damage(2));
        catalog.add(std::move(c));
    }
}

} // namespace

CardCatalog make_v04_catalog() {
    CardCatalog catalog;
    add_midrange_cards(catalog);
    add_advance_cards(catalog);
    return catalog;
}

// Deck A: 标准中速 (Standard Midrange) – 30 cards, 0 standby cards.
// Distribution (using twice() helper for 2x copies):
//  3x 先驱侦察兵  3x 护卫站岗者  3x 突击前锋   2x 前卫盾手
//  3x 战场指挥者  2x 坚守盾卫   2x 铁壁屏障   2x 精锐统帅
//  3x 致命一击   3x 后方支援   2x 战令设施   2x 拦截伏策
//  = 3+3+3+2+3+2+2+2+3+3+2+2 = 30
DeckList make_midrange_deck() {
    using namespace cards::midrange;

    DeckList deck;
    // 3x
    for (int i = 0; i < 3; ++i) {
        deck.main.push_back(kPioneerScout);
        deck.main.push_back(kGuardSentry);
        deck.main.push_back(kAssaultVanguard);
        deck.main.push_back(kFieldCommander);
        deck.main.push_back(kPrecisionStrike);
        deck.main.push_back(kCombatSupply);
    }
    // 2x
    for (int i = 0; i < 2; ++i) {
        deck.main.push_back(kShieldVanguard);
        deck.main.push_back(kIronShieldBearer);
        deck.main.push_back(kFortressGuard);
        deck.main.push_back(kEliteCommander);
        deck.main.push_back(kCommandOrder);
        deck.main.push_back(kInterceptTrap);
    }
    deck.standby = {};
    deck.leader_skill = {
        "战场集结",
        2,
        {EffectRecord{EffectTrigger::OnPlay, EffectKind::BuffFriendlyUnit, 1, TargetSpec::FriendlyUnit}},
    };
    return deck;
}

// Deck B: 预支测试 (Advance PP Test) – 30 cards, 0 standby cards.
// Distribution:
//  3x 超前先锋  3x 按期精英  3x 燃耗战士  3x 修复技师
//  3x 裂痕感知者  2x 巨型先锋  3x 超前打击  3x 修复之波
//  3x 燃耗爆破  2x 增长设施  1x 债务领主  1x 截击陷阱
//  = 3+3+3+3+3+2+3+3+3+2+1+1 = 30
DeckList make_advance_deck() {
    using namespace cards::advance;

    DeckList deck;
    // 3x
    for (int i = 0; i < 3; ++i) {
        deck.main.push_back(kAdvanceWarrior);
        deck.main.push_back(kOnTimeElite);
        deck.main.push_back(kBurnWarrior);
        deck.main.push_back(kRepairTechnician);
        deck.main.push_back(kCrackFeeder);
        deck.main.push_back(kAdvanceStrike);
        deck.main.push_back(kRepairWave);
        deck.main.push_back(kBurnBlast);
    }
    // 2x
    for (int i = 0; i < 2; ++i) {
        deck.main.push_back(kMassiveVanguard);
        deck.main.push_back(kGrowthFacility);
    }
    // 1x
    deck.main.push_back(kDebtLord);
    deck.main.push_back(kReactionTrap);

    deck.standby = {};
    deck.leader_skill = {
        "裂痕汲取",
        2,
        {EffectRecord{EffectTrigger::OnPlay, EffectKind::RepairCracks, 1, TargetSpec::None}},
    };
    return deck;
}

} // namespace scgs
