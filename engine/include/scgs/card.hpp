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

    // v0.4 evolution state. evolved_attack/evolved_health are the stats after
    // evolution; when both are 0 the default +2/+2 applies (rules-v0.4 §22).
    int evolved_attack = 0;
    int evolved_health = 0;

    // 燃耗X: additional cost that permanently reduces PP capacity.
    AdditionalCost additional_cost;

    // 战备部署 (standby deployment) specification; only meaningful for cards
    // that live in the standby zone (rules-v0.4 §24/§25).
    std::optional<DeploymentSpec> deployment;

    // 组件能力 (component ability): granted to the deployed unit when this card
    // pays a deployment cost (rules-v0.4 §31).
    ComponentSpec component;

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
    ChargeCondition charge_condition = ChargeCondition::None; // v0.4 职业进化充能条件
    int charge_amount = 0; // parameter for the charge archetype (e.g. Nth death / N spells)
};


} // namespace scgs
