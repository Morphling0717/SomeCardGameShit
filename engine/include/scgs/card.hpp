// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once

#include "scgs/types.hpp"

#include <stdexcept>
#include <string>
#include <unordered_map>
#include <vector>

namespace scgs {

struct CardDefinition {
    CardId id = 0;
    std::string name;
    CardKind kind = CardKind::Unit;
    int cost = 0;
    int attack = 0;
    int health = 0;
    TraitMask traits = mask(Trait::None);
    KeywordMask keywords = mask(Keyword::None);
    Imprint printed_imprint = Imprint::None;

    Ability play_ability = Ability::None;
    Ability entry_ability = Ability::None;
    Ability evolution_ability = Ability::None;
    Ability last_words_ability = Ability::None;
    Ability trap_ability = Ability::None;
    Ability countdown_expire_ability = Ability::None;

    AdvancedSummonKind advanced_kind = AdvancedSummonKind::None;
    int advanced_cost = 0;
    int min_materials = 0;
    int max_materials = 0;
    int min_material_original_cost_sum = 0;
    TraitMask required_material_traits = mask(Trait::None);
    bool can_attack_leader_on_advanced_turn = false;

    int countdown = 0;
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
    Ability ability = Ability::None;
};

struct DeckList {
    std::vector<CardId> main;
    std::vector<CardId> summon;
    LeaderSkillDefinition leader_skill;
};

namespace cards {

inline constexpr CardId kRoyalRecruit = 1001;
inline constexpr CardId kRoyalVanguard = 1002;
inline constexpr CardId kRoyalLancer = 1003;
inline constexpr CardId kRoyalTactician = 1004;
inline constexpr CardId kRoyalCrownKnight = 1005;
inline constexpr CardId kRoyalSquire = 1006;
inline constexpr CardId kRoyalShieldbearer = 1007;
inline constexpr CardId kRoyalCavalier = 1008;
inline constexpr CardId kRoyalCommander = 1009;
inline constexpr CardId kRoyalBolt = 1010;
inline constexpr CardId kRoyalCountdownRelic = 1011;
inline constexpr CardId kRoyalAmbushTrap = 1012;
inline constexpr CardId kRoyalRenewal = 1013;
inline constexpr CardId kRoyalWarBanner = 1014;
inline constexpr CardId kRoyalCountercharge = 1015;

inline constexpr CardId kMachineRushPart = 2001;
inline constexpr CardId kMachineGuardPart = 2002;
inline constexpr CardId kMachineBarrierPart = 2003;
inline constexpr CardId kMachineRepairPart = 2004;
inline constexpr CardId kMachineAssembler = 2005;
inline constexpr CardId kMachineDrone = 2006;
inline constexpr CardId kMachineHeavyFrame = 2007;
inline constexpr CardId kMachineSalvager = 2008;
inline constexpr CardId kMachineCore = 2009;
inline constexpr CardId kMachineSpark = 2010;
inline constexpr CardId kMachineFactoryRelic = 2011;
inline constexpr CardId kMachineRetaliationTrap = 2012;
inline constexpr CardId kMachineRepairSpell = 2013;
inline constexpr CardId kMachineEmergencyBarrier = 2014;
inline constexpr CardId kMachineReserveCore = 2015;

inline constexpr CardId kTrainingDummy = 9001;
inline constexpr CardId kBastionConstruct = 2901;
inline constexpr CardId kAssaultConstruct = 2902;
inline constexpr CardId kRecoveryConstruct = 2903;

} // namespace cards

[[nodiscard]] CardCatalog make_prototype_catalog();
[[nodiscard]] DeckList make_royal_prototype_deck();
[[nodiscard]] DeckList make_machine_prototype_deck();

} // namespace scgs
