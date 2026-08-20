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

CardDefinition unit(
    const CardId id,
    std::string name,
    const int cost,
    const int attack,
    const int health,
    const TraitMask traits,
    const KeywordMask keywords = mask(Keyword::None),
    const Imprint imprint = Imprint::None) {
    CardDefinition card;
    card.id = id;
    card.name = std::move(name);
    card.kind = CardKind::Unit;
    card.cost = cost;
    card.attack = attack;
    card.health = health;
    card.traits = traits;
    card.keywords = keywords;
    card.printed_imprint = imprint;
    return card;
}

CardDefinition spell(const CardId id, std::string name, const int cost, const Ability ability) {
    CardDefinition card;
    card.id = id;
    card.name = std::move(name);
    card.kind = CardKind::Spell;
    card.cost = cost;
    card.play_ability = ability;
    return card;
}

CardDefinition relic(
    const CardId id,
    std::string name,
    const int cost,
    const int countdown,
    const Ability expire_ability) {
    CardDefinition card;
    card.id = id;
    card.name = std::move(name);
    card.kind = CardKind::Relic;
    card.cost = cost;
    card.countdown = countdown;
    card.countdown_expire_ability = expire_ability;
    return card;
}

CardDefinition trap(const CardId id, std::string name, const int cost, const Ability ability) {
    CardDefinition card;
    card.id = id;
    card.name = std::move(name);
    card.kind = CardKind::Trap;
    card.cost = cost;
    card.trap_ability = ability;
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

} // namespace

CardCatalog make_prototype_catalog() {
    CardCatalog catalog;

    catalog.add(unit(cards::kRoyalRecruit, "王庭新兵", 1, 1, 2, mask(Trait::Soldier)));
    catalog.add(unit(
        cards::kRoyalVanguard,
        "王庭前卫",
        2,
        2,
        3,
        mask(Trait::Soldier),
        mask(Keyword::Guard),
        Imprint::Guard));
    catalog.add(unit(
        cards::kRoyalLancer,
        "疾行枪骑",
        2,
        2,
        1,
        mask(Trait::Knight),
        mask(Keyword::Rush),
        Imprint::Rush));

    CardDefinition tactician = unit(cards::kRoyalTactician, "王庭战术官", 3, 3, 3, mask(Trait::Soldier));
    tactician.evolution_ability = Ability::DrawOne;
    catalog.add(std::move(tactician));

    CardDefinition crown_knight = unit(
        cards::kRoyalCrownKnight,
        "冠冕骑士",
        7,
        6,
        7,
        mask(Trait::Knight),
        mask(Keyword::Guard));
    crown_knight.advanced_kind = AdvancedSummonKind::Tribute;
    crown_knight.advanced_cost = 3;
    crown_knight.min_materials = 1;
    crown_knight.max_materials = 1;
    crown_knight.min_material_original_cost_sum = 3;
    catalog.add(std::move(crown_knight));

    catalog.add(unit(cards::kRoyalSquire, "见习侍从", 1, 2, 1, mask(Trait::Soldier)));
    catalog.add(unit(
        cards::kRoyalShieldbearer,
        "持盾卫士",
        3,
        3,
        4,
        mask(Trait::Soldier),
        mask(Keyword::Guard),
        Imprint::Guard));
    catalog.add(unit(cards::kRoyalCavalier, "王庭骑兵", 4, 4, 4, mask(Trait::Knight)));
    catalog.add(unit(cards::kRoyalCommander, "近卫统领", 5, 5, 5, mask(Trait::Soldier) | mask(Trait::Knight)));
    catalog.add(spell(cards::kRoyalBolt, "惩戒", 2, Ability::DealThreeToEnemyUnit));
    catalog.add(relic(cards::kRoyalCountdownRelic, "战地号令", 2, 2, Ability::DrawOne));
    catalog.add(trap(cards::kRoyalAmbushTrap, "伏兵截击", 1, Ability::TrapCancelAttack));
    catalog.add(spell(cards::kRoyalRenewal, "整军", 2, Ability::HealLeaderThree));
    catalog.add(relic(cards::kRoyalWarBanner, "王庭战旗", 2, 0, Ability::None));
    catalog.add(trap(cards::kRoyalCountercharge, "反冲锋", 1, Ability::TrapDamageSummonedUnitTwo));

    catalog.add(unit(
        cards::kMachineRushPart,
        "推进零件",
        2,
        2,
        1,
        mask(Trait::Machine) | mask(Trait::Part),
        mask(Keyword::Rush),
        Imprint::Rush));
    catalog.add(unit(
        cards::kMachineGuardPart,
        "护卫零件",
        3,
        2,
        4,
        mask(Trait::Machine) | mask(Trait::Part),
        mask(Keyword::Guard),
        Imprint::Guard));
    catalog.add(unit(
        cards::kMachineBarrierPart,
        "屏障零件",
        2,
        2,
        2,
        mask(Trait::Machine) | mask(Trait::Part),
        mask(Keyword::Barrier),
        Imprint::Barrier));
    catalog.add(unit(
        cards::kMachineRepairPart,
        "回收零件",
        3,
        2,
        3,
        mask(Trait::Machine) | mask(Trait::Part),
        mask(Keyword::Lifesteal),
        Imprint::Lifesteal));

    CardDefinition assembler = unit(
        cards::kMachineAssembler,
        "装配技师",
        3,
        3,
        3,
        mask(Trait::Machine));
    assembler.entry_ability = Ability::DrawOne;
    catalog.add(std::move(assembler));

    catalog.add(unit(cards::kMachineDrone, "巡检无人机", 1, 1, 2, mask(Trait::Machine)));
    catalog.add(unit(cards::kMachineHeavyFrame, "重型框架", 4, 4, 5, mask(Trait::Machine)));
    catalog.add(unit(cards::kMachineSalvager, "废料回收者", 3, 3, 2, mask(Trait::Machine)));
    catalog.add(unit(cards::kMachineCore, "机巧核心", 5, 5, 5, mask(Trait::Machine)));
    catalog.add(spell(cards::kMachineSpark, "电弧", 2, Ability::DealThreeToEnemyUnit));
    catalog.add(relic(cards::kMachineFactoryRelic, "自动工厂", 2, 2, Ability::CreateRushPartInHand));
    catalog.add(trap(
        cards::kMachineRetaliationTrap,
        "过载反制",
        1,
        Ability::TrapDamageSummonedUnitTwo));
    catalog.add(spell(cards::kMachineRepairSpell, "紧急维修", 2, Ability::HealLeaderThree));
    catalog.add(trap(
        cards::kMachineEmergencyBarrier,
        "紧急屏障",
        1,
        Ability::TrapCancelAttack));
    catalog.add(relic(cards::kMachineReserveCore, "备用核心", 2, 0, Ability::None));

    catalog.add(unit(cards::kTrainingDummy, "训练假人", 3, 3, 3, mask(Trait::None)));

    CardDefinition bastion;
    bastion.id = cards::kBastionConstruct;
    bastion.name = "壁垒构成体";
    bastion.kind = CardKind::SummonUnit;
    bastion.attack = 5;
    bastion.health = 6;
    bastion.traits = mask(Trait::Machine) | mask(Trait::Construct);
    bastion.entry_ability = Ability::DealTwoToEnemyUnit;
    bastion.advanced_kind = AdvancedSummonKind::Construct;
    bastion.advanced_cost = 2;
    bastion.min_materials = 2;
    bastion.max_materials = 2;
    bastion.min_material_original_cost_sum = 5;
    bastion.required_material_traits = mask(Trait::Machine);
    catalog.add(std::move(bastion));

    CardDefinition assault;
    assault.id = cards::kAssaultConstruct;
    assault.name = "突击构成体";
    assault.kind = CardKind::SummonUnit;
    assault.attack = 4;
    assault.health = 4;
    assault.traits = mask(Trait::Machine) | mask(Trait::Construct);
    assault.advanced_kind = AdvancedSummonKind::Construct;
    assault.advanced_cost = 1;
    assault.min_materials = 2;
    assault.max_materials = 3;
    assault.min_material_original_cost_sum = 4;
    assault.required_material_traits = mask(Trait::Machine);
    catalog.add(std::move(assault));

    CardDefinition recovery;
    recovery.id = cards::kRecoveryConstruct;
    recovery.name = "回收构成体";
    recovery.kind = CardKind::SummonUnit;
    recovery.attack = 3;
    recovery.health = 5;
    recovery.traits = mask(Trait::Machine) | mask(Trait::Construct);
    recovery.entry_ability = Ability::DrawOne;
    recovery.advanced_kind = AdvancedSummonKind::Construct;
    recovery.advanced_cost = 2;
    recovery.min_materials = 2;
    recovery.max_materials = 2;
    recovery.min_material_original_cost_sum = 4;
    recovery.required_material_traits = mask(Trait::Machine);
    catalog.add(std::move(recovery));

    return catalog;
}

DeckList make_royal_prototype_deck() {
    DeckList deck;
    deck.main = twice({
        cards::kRoyalRecruit,
        cards::kRoyalVanguard,
        cards::kRoyalLancer,
        cards::kRoyalTactician,
        cards::kRoyalCrownKnight,
        cards::kRoyalSquire,
        cards::kRoyalShieldbearer,
        cards::kRoyalCavalier,
        cards::kRoyalCommander,
        cards::kRoyalBolt,
        cards::kRoyalCountdownRelic,
        cards::kRoyalAmbushTrap,
        cards::kRoyalRenewal,
        cards::kRoyalWarBanner,
        cards::kRoyalCountercharge,
    });
    // The source design does not yet define what Royal places in the six-card
    // summon deck. Keep it empty rather than inventing a permanent rule.
    deck.summon = {};
    deck.leader_skill = LeaderSkillDefinition{"[测试] 集结", 2, Ability::GiveFriendlyUnitOneOne};
    return deck;
}

DeckList make_machine_prototype_deck() {
    DeckList deck;
    deck.main = twice({
        cards::kMachineRushPart,
        cards::kMachineGuardPart,
        cards::kMachineBarrierPart,
        cards::kMachineRepairPart,
        cards::kMachineAssembler,
        cards::kMachineDrone,
        cards::kMachineHeavyFrame,
        cards::kMachineSalvager,
        cards::kMachineCore,
        cards::kMachineSpark,
        cards::kMachineFactoryRelic,
        cards::kMachineRetaliationTrap,
        cards::kMachineRepairSpell,
        cards::kMachineEmergencyBarrier,
        cards::kMachineReserveCore,
    });
    deck.summon = {
        cards::kBastionConstruct,
        cards::kBastionConstruct,
        cards::kAssaultConstruct,
        cards::kAssaultConstruct,
        cards::kRecoveryConstruct,
        cards::kRecoveryConstruct,
    };
    deck.leader_skill = LeaderSkillDefinition{"[测试] 制造零件", 1, Ability::CreateRushPartInHand};
    return deck;
}

} // namespace scgs
