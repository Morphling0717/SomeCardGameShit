// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once

#include "scgs/card.hpp"

#include <utility>

// Frozen-v04 rule tests use this deliberately synthetic catalog instead of
// either product deck. The identifiers, names and deck lists are test data;
// legacy wire goldens construct their protocol records directly.
namespace scgs::test_fixture {

inline constexpr CardId kEntryDrawUnit = 0x7001;
inline constexpr CardId kSmallWardUnit = 0x7002;
inline constexpr CardId kRushComponentUnit = 0x7003;
inline constexpr CardId kMediumWardUnit = 0x7004;
inline constexpr CardId kEvolutionDrawUnit = 0x7005;
inline constexpr CardId kLargeWardUnit = 0x7006;
inline constexpr CardId kBarrierWardUnit = 0x7007;
inline constexpr CardId kVanillaFiveUnit = 0x7008;
inline constexpr CardId kDamageThreeSpell = 0x7009;
inline constexpr CardId kHealThreeSpell = 0x700A;
inline constexpr CardId kDrawCountdownRelic = 0x700B;
inline constexpr CardId kCancelAttackTrap = 0x700C;
inline constexpr CardId kEntryDamageTrap = 0x700D;
inline constexpr CardId kUnitCountStandby = 0x700E;
inline constexpr CardId kArchiveCostStandby = 0x700F;

inline constexpr CardId kAdvancedRushUnit = 0x7101;
inline constexpr CardId kOnTimeDrawUnit = 0x7102;
inline constexpr CardId kBurnTwoUnit = 0x7103;
inline constexpr CardId kRepairTwoUnit = 0x7104;
inline constexpr CardId kCrackDamageUnit = 0x7105;
inline constexpr CardId kVanillaSevenUnit = 0x7106;
inline constexpr CardId kBurnOneDamageFourSpell = 0x7107;
inline constexpr CardId kRepairHealSpell = 0x7108;
inline constexpr CardId kBurnTwoDamageFiveSpell = 0x7109;
inline constexpr CardId kCapacityCountdownRelic = 0x710A;
inline constexpr CardId kVanillaEightUnit = 0x710B;
inline constexpr CardId kSecondEntryDamageTrap = 0x710C;
inline constexpr CardId kSpellCountStandby = 0x710D;
inline constexpr CardId kSecondArchiveCostStandby = 0x710E;

namespace detail {

inline CardDefinition unit(
    const CardId id,
    const int cost,
    const int attack,
    const int health) {
    CardDefinition card;
    card.id = id;
    card.name = "synthetic unit " + std::to_string(id);
    card.kind = CardKind::Unit;
    card.cost = cost;
    card.attack = attack;
    card.health = health;
    return card;
}

inline CardDefinition spell(const CardId id, const int cost) {
    CardDefinition card;
    card.id = id;
    card.name = "synthetic spell " + std::to_string(id);
    card.kind = CardKind::Spell;
    card.cost = cost;
    return card;
}

inline CardDefinition relic(const CardId id, const int cost, const int countdown) {
    CardDefinition card;
    card.id = id;
    card.name = "synthetic relic " + std::to_string(id);
    card.kind = CardKind::Relic;
    card.cost = cost;
    card.countdown = countdown;
    return card;
}

inline CardDefinition trap(const CardId id, const int cost) {
    CardDefinition card;
    card.id = id;
    card.name = "synthetic trap " + std::to_string(id);
    card.kind = CardKind::Trap;
    card.cost = cost;
    return card;
}

} // namespace detail

inline CardCatalog make_catalog() {
    CardCatalog catalog;
    {
        auto card = detail::unit(kEntryDrawUnit, 1, 1, 2);
        card.effects.push_back({EffectTrigger::OnEntry, EffectKind::DrawCards, 1, TargetSpec::None});
        catalog.add(std::move(card));
    }
    {
        auto card = detail::unit(kSmallWardUnit, 1, 1, 3);
        card.printed_guard = true;
        catalog.add(std::move(card));
    }
    {
        auto card = detail::unit(kRushComponentUnit, 2, 3, 1);
        card.printed_rush = true;
        card.component = ComponentSpec{true, EffectKind::GrantRush, 0};
        catalog.add(std::move(card));
    }
    {
        auto card = detail::unit(kMediumWardUnit, 2, 2, 3);
        card.printed_guard = true;
        catalog.add(std::move(card));
    }
    {
        auto card = detail::unit(kEvolutionDrawUnit, 3, 3, 3);
        card.evolved_attack = 5;
        card.evolved_health = 5;
        card.effects.push_back({EffectTrigger::OnEvolution, EffectKind::DrawCards, 1, TargetSpec::None});
        catalog.add(std::move(card));
    }
    {
        auto card = detail::unit(kLargeWardUnit, 3, 2, 5);
        card.printed_guard = true;
        catalog.add(std::move(card));
    }
    {
        auto card = detail::unit(kBarrierWardUnit, 4, 3, 6);
        card.printed_guard = true;
        card.printed_barrier = true;
        catalog.add(std::move(card));
    }
    catalog.add(detail::unit(kVanillaFiveUnit, 5, 5, 5));
    {
        auto card = detail::spell(kDamageThreeSpell, 2);
        card.effects.push_back({EffectTrigger::OnPlay, EffectKind::DealDamageToEnemyUnit, 3, TargetSpec::EnemyUnit});
        catalog.add(std::move(card));
    }
    {
        auto card = detail::spell(kHealThreeSpell, 2);
        card.effects.push_back({EffectTrigger::OnPlay, EffectKind::HealLeader, 3, TargetSpec::None});
        catalog.add(std::move(card));
    }
    {
        auto card = detail::relic(kDrawCountdownRelic, 2, 2);
        card.effects.push_back({EffectTrigger::OnCountdownExpire, EffectKind::DrawCards, 1, TargetSpec::None});
        catalog.add(std::move(card));
    }
    {
        auto card = detail::trap(kCancelAttackTrap, 1);
        card.effects.push_back({EffectTrigger::OnAttackDeclared, EffectKind::CancelAttack, 0, TargetSpec::None});
        catalog.add(std::move(card));
    }
    {
        auto card = detail::trap(kEntryDamageTrap, 1);
        card.effects.push_back({EffectTrigger::OnEntryEffectPending, EffectKind::DamageEnteredUnit, 2, TargetSpec::None});
        catalog.add(std::move(card));
    }
    {
        auto card = detail::unit(kUnitCountStandby, 0, 5, 5);
        card.deployment = DeploymentSpec{DeploymentCondition::FriendlyUnitsMin, 2, 3, false};
        catalog.add(std::move(card));
    }
    {
        auto card = detail::unit(kArchiveCostStandby, 0, 4, 6);
        card.printed_guard = true;
        card.deployment = DeploymentSpec{DeploymentCondition::None, 0, 4, true};
        catalog.add(std::move(card));
    }
    {
        auto card = detail::unit(kAdvancedRushUnit, 4, 4, 4);
        card.effects.push_back({EffectTrigger::OnPlayIfAdvanced, EffectKind::GrantRush, 0, TargetSpec::None});
        catalog.add(std::move(card));
    }
    {
        auto card = detail::unit(kOnTimeDrawUnit, 3, 3, 3);
        card.effects.push_back({EffectTrigger::OnPlayIfNotAdvanced, EffectKind::DrawCards, 1, TargetSpec::None});
        catalog.add(std::move(card));
    }
    {
        auto card = detail::unit(kBurnTwoUnit, 1, 3, 3);
        card.additional_cost.burn_pp_capacity = 2;
        catalog.add(std::move(card));
    }
    {
        auto card = detail::unit(kRepairTwoUnit, 2, 2, 2);
        card.effects.push_back({EffectTrigger::OnEntry, EffectKind::RepairCracks, 2, TargetSpec::None});
        catalog.add(std::move(card));
    }
    {
        auto card = detail::unit(kCrackDamageUnit, 3, 2, 4);
        card.effects.push_back({EffectTrigger::OnEntry, EffectKind::DealDamageToEnemyUnit, -3, TargetSpec::EnemyUnit});
        card.component = ComponentSpec{true, EffectKind::GrantRush, 0};
        catalog.add(std::move(card));
    }
    catalog.add(detail::unit(kVanillaSevenUnit, 7, 7, 7));
    {
        auto card = detail::spell(kBurnOneDamageFourSpell, 2);
        card.additional_cost.burn_pp_capacity = 1;
        card.effects.push_back({EffectTrigger::OnPlay, EffectKind::DealDamageToEnemyUnit, 4, TargetSpec::EnemyUnit});
        catalog.add(std::move(card));
    }
    {
        auto card = detail::spell(kRepairHealSpell, 2);
        card.effects.push_back({EffectTrigger::OnPlay, EffectKind::RepairCracks, 2, TargetSpec::None});
        card.effects.push_back({EffectTrigger::OnPlay, EffectKind::HealLeader, 2, TargetSpec::None});
        catalog.add(std::move(card));
    }
    {
        auto card = detail::spell(kBurnTwoDamageFiveSpell, 1);
        card.additional_cost.burn_pp_capacity = 2;
        card.effects.push_back({EffectTrigger::OnPlay, EffectKind::DealDamageToEnemyUnit, 5, TargetSpec::EnemyUnit});
        catalog.add(std::move(card));
    }
    {
        auto card = detail::relic(kCapacityCountdownRelic, 2, 2);
        card.effects.push_back({EffectTrigger::OnCountdownExpire, EffectKind::GainPPCapacity, 1, TargetSpec::None});
        catalog.add(std::move(card));
    }
    {
        auto card = detail::unit(kVanillaEightUnit, 8, 8, 6);
        card.evolved_attack = 10;
        card.evolved_health = 8;
        catalog.add(std::move(card));
    }
    {
        auto card = detail::trap(kSecondEntryDamageTrap, 1);
        card.effects.push_back({EffectTrigger::OnEntryEffectPending, EffectKind::DamageEnteredUnit, 2, TargetSpec::None});
        catalog.add(std::move(card));
    }
    {
        auto card = detail::unit(kSpellCountStandby, 0, 7, 7);
        card.deployment = DeploymentSpec{DeploymentCondition::SpellsThisTurnMin, 2, 2, false};
        catalog.add(std::move(card));
    }
    {
        auto card = detail::unit(kSecondArchiveCostStandby, 0, 6, 6);
        card.deployment = DeploymentSpec{DeploymentCondition::None, 0, 3, true};
        catalog.add(std::move(card));
    }
    return catalog;
}

inline DeckList make_alpha_deck() {
    DeckList deck;
    for (int copy = 0; copy < 3; ++copy) {
        deck.main.insert(deck.main.end(), {
            kEntryDrawUnit,
            kSmallWardUnit,
            kRushComponentUnit,
            kEvolutionDrawUnit,
            kDamageThreeSpell,
            kHealThreeSpell,
        });
    }
    for (int copy = 0; copy < 2; ++copy) {
        deck.main.insert(deck.main.end(), {
            kMediumWardUnit,
            kLargeWardUnit,
            kBarrierWardUnit,
            kVanillaFiveUnit,
            kDrawCountdownRelic,
            kCancelAttackTrap,
        });
    }
    deck.standby = {kUnitCountStandby, kArchiveCostStandby};
    deck.leader_skill = {
        "synthetic buff skill",
        2,
        {{EffectTrigger::OnPlay, EffectKind::BuffFriendlyUnit, 1, TargetSpec::FriendlyUnit}},
    };
    deck.charge_condition = ChargeCondition::FriendlyDeathsPerCycle;
    deck.charge_amount = 2;
    return deck;
}

inline DeckList make_beta_deck() {
    DeckList deck;
    for (int copy = 0; copy < 3; ++copy) {
        deck.main.insert(deck.main.end(), {
            kAdvancedRushUnit,
            kOnTimeDrawUnit,
            kBurnTwoUnit,
            kRepairTwoUnit,
            kCrackDamageUnit,
            kBurnOneDamageFourSpell,
            kRepairHealSpell,
            kBurnTwoDamageFiveSpell,
        });
    }
    for (int copy = 0; copy < 2; ++copy) {
        deck.main.push_back(kVanillaSevenUnit);
        deck.main.push_back(kCapacityCountdownRelic);
    }
    deck.main.push_back(kVanillaEightUnit);
    deck.main.push_back(kSecondEntryDamageTrap);
    deck.standby = {kSpellCountStandby, kSecondArchiveCostStandby};
    deck.leader_skill = {
        "synthetic repair skill",
        2,
        {{EffectTrigger::OnPlay, EffectKind::RepairCracks, 1, TargetSpec::None}},
    };
    deck.charge_condition = ChargeCondition::SpellsNoUnitsThisTurn;
    deck.charge_amount = 2;
    return deck;
}

} // namespace scgs::test_fixture
