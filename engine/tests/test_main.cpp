// SPDX-License-Identifier: GPL-3.0-or-later
#include "scgs/game.hpp"
#include "scgs/protocol.hpp"

#include <algorithm>
#include <cstdlib>
#include <exception>
#include <functional>
#include <iostream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace {

using namespace scgs;

struct TestContext {
    int assertions = 0;
    int failures = 0;

    void expect(const bool condition, const char* expression, const char* file, const int line) {
        ++assertions;
        if (!condition) {
            ++failures;
            std::cerr << file << ':' << line << ": expectation failed: " << expression << '\n';
        }
    }
};

#define EXPECT(ctx, expression) (ctx).expect(static_cast<bool>(expression), #expression, __FILE__, __LINE__)
#define EXPECT_CODE(ctx, status, expected) \
    (ctx).expect((status).code == (expected), #status ".code == " #expected, __FILE__, __LINE__)

Game scenario_game(const Scenario& scenario, GameConfig config = {}) {
    Game game(make_prototype_catalog(), {}, {}, config);
    const Status status = game.load_scenario(scenario);
    if (!status) {
        throw std::runtime_error("failed to load test scenario: " + status.message);
    }
    return game;
}

Scenario base_scenario(const PlayerId active = PlayerId::Player0) {
    Scenario scenario;
    scenario.active_player = active;
    for (auto& player : scenario.players) {
        player.leader_health = 25;
        player.maximum_leader_health = 25;
    }
    return scenario;
}

void expect_valid_state(TestContext& context, const Game& game) {
    const std::vector<std::string> problems = game.validate_invariants();
    if (!problems.empty()) {
        std::cerr << "state invariant violations:\n";
        for (const std::string& problem : problems) {
            std::cerr << "  - " << problem << '\n';
        }
    }
    EXPECT(context, problems.empty());
}

std::optional<Target> first_enemy_unit_target(const Game& game, const PlayerId player_id) {
    for (const auto& slot : game.player(opponent(player_id)).units) {
        if (slot.has_value()) {
            return Target::unit_target(opponent(player_id), *slot);
        }
    }
    return std::nullopt;
}

std::optional<Target> first_friendly_unit_target(const Game& game, const PlayerId player_id) {
    for (const auto& slot : game.player(player_id).units) {
        if (slot.has_value()) {
            return Target::unit_target(player_id, *slot);
        }
    }
    return std::nullopt;
}

std::vector<InstanceId> controlled_units(const Game& game, const PlayerId player_id) {
    std::vector<InstanceId> units;
    for (const auto& slot : game.player(player_id).units) {
        if (slot.has_value()) {
            units.push_back(*slot);
        }
    }
    return units;
}

bool try_advanced_summon(Game& game, const PlayerId player_id) {
    const std::vector<InstanceId> units = controlled_units(game, player_id);
    const std::optional<Target> enemy = first_enemy_unit_target(game, player_id);

    // Main-deck tribute targets.
    const std::vector<InstanceId> hand = game.player(player_id).hand;
    for (const InstanceId card : hand) {
        const CardDefinition& card_definition = game.definition(card);
        if (card_definition.advanced_kind != AdvancedSummonKind::Tribute) {
            continue;
        }
        for (const InstanceId material : units) {
            const Imprint printed = game.definition(material).printed_imprint;
            const std::array<Imprint, 2> choices = {printed, Imprint::None};
            for (const Imprint imprint : choices) {
                AdvancedSummonRequest request;
                request.player = player_id;
                request.card = card;
                request.materials = {material};
                request.inherited_imprint = imprint;
                request.ability_target = enemy;
                if (game.advanced_summon(request)) {
                    return true;
                }
            }
        }
    }

    // Prototype construct targets all use two materials, but the loop checks
    // every pair and lets the core enforce exact conditions and costs.
    const std::vector<InstanceId> summon_deck = game.player(player_id).summon_deck;
    for (const InstanceId card : summon_deck) {
        const CardDefinition& card_definition = game.definition(card);
        if (card_definition.advanced_kind != AdvancedSummonKind::Construct) {
            continue;
        }
        for (std::size_t first = 0; first < units.size(); ++first) {
            for (std::size_t second = first + 1; second < units.size(); ++second) {
                const std::array<InstanceId, 2> pair = {units[first], units[second]};
                std::vector<Imprint> choices = {Imprint::None};
                for (const InstanceId material : pair) {
                    const Imprint printed = game.definition(material).printed_imprint;
                    if (printed != Imprint::None &&
                        std::find(choices.begin(), choices.end(), printed) == choices.end()) {
                        choices.push_back(printed);
                    }
                }
                for (const Imprint imprint : choices) {
                    AdvancedSummonRequest request;
                    request.player = player_id;
                    request.card = card;
                    request.materials = {pair[0], pair[1]};
                    request.inherited_imprint = imprint;
                    request.ability_target = enemy;
                    if (game.advanced_summon(request)) {
                        return true;
                    }
                }
            }
        }
    }
    return false;
}

bool take_smoke_action(Game& game, const int step) {
    if (game.phase() == Phase::Reaction) {
        return static_cast<bool>(game.pass_reaction(opponent(game.active_player())));
    }
    if (game.phase() != Phase::Action || game.result() != GameResult::Ongoing) {
        return false;
    }

    const PlayerId player_id = game.active_player();
    const std::optional<Target> enemy = first_enemy_unit_target(game, player_id);
    const std::optional<Target> friendly = first_friendly_unit_target(game, player_id);

    // Rotate the first choice so the smoke suite does not always play the same
    // deterministic priority order after a shuffled draw.
    const int mode = step % 5;
    if (mode == 0 && try_advanced_summon(game, player_id)) {
        return true;
    }

    const std::vector<InstanceId> hand = game.player(player_id).hand;
    for (const InstanceId card : hand) {
        const CardDefinition& card_definition = game.definition(card);
        if (card_definition.kind == CardKind::Unit) {
            if (game.play_unit(player_id, card, std::nullopt, enemy)) {
                return true;
            }
        } else if (card_definition.kind == CardKind::Spell) {
            if (game.cast_spell(player_id, card, enemy)) {
                return true;
            }
        } else if (card_definition.kind == CardKind::Relic || card_definition.kind == CardKind::Trap) {
            for (std::size_t slot = 0; slot < kTacticZoneSize; ++slot) {
                if (game.play_tactic(player_id, card, slot)) {
                    return true;
                }
            }
        }
    }

    if (mode != 0 && try_advanced_summon(game, player_id)) {
        return true;
    }

    if (friendly.has_value()) {
        if (game.use_leader_skill(player_id, friendly)) {
            return true;
        }
    } else if (game.use_leader_skill(player_id)) {
        return true;
    }

    for (const InstanceId unit : controlled_units(game, player_id)) {
        if (game.evolve(player_id, unit, EvolutionMode::Ability, enemy)) {
            return true;
        }
        if (game.evolve(player_id, unit, EvolutionMode::Combat)) {
            return true;
        }
    }

    std::optional<Target> attack_target;
    for (const auto& slot : game.player(opponent(player_id)).units) {
        if (slot.has_value() && has_keyword(game.instance(*slot).keywords, Keyword::Guard)) {
            attack_target = Target::unit_target(opponent(player_id), *slot);
            break;
        }
    }
    if (!attack_target.has_value()) {
        attack_target = enemy.has_value() ? enemy : std::optional<Target>{Target::leader(opponent(player_id))};
    }
    for (const InstanceId unit : controlled_units(game, player_id)) {
        if (game.attack(player_id, unit, *attack_target)) {
            return true;
        }
    }

    return static_cast<bool>(game.end_turn(player_id));
}

void test_catalog_and_fixed_decks(TestContext& context) {
    const CardCatalog catalog = make_prototype_catalog();
    const DeckList royal = make_royal_prototype_deck();
    const DeckList machine = make_machine_prototype_deck();

    EXPECT(context, catalog.size() >= 33U);
    EXPECT(context, royal.main.size() == 30U);
    EXPECT(context, machine.main.size() == 30U);
    EXPECT(context, royal.summon.empty());
    EXPECT(context, machine.summon.size() == 6U);
    for (const CardId id : machine.summon) {
        EXPECT(context, std::count(machine.summon.begin(), machine.summon.end(), id) <= 2);
    }
    EXPECT(context, catalog.at(cards::kBastionConstruct).advanced_kind == AdvancedSummonKind::Construct);
    EXPECT(context, catalog.at(cards::kRoyalCrownKnight).advanced_kind == AdvancedSummonKind::Tribute);
}

void test_start_mulligan_and_turn_flow(TestContext& context) {
    GameConfig config;
    config.shuffle_decks = false;
    Game game(make_prototype_catalog(), make_royal_prototype_deck(), make_machine_prototype_deck(), config);

    EXPECT(context, game.start());
    EXPECT(context, game.phase() == Phase::Mulligan);
    EXPECT(context, game.player(PlayerId::Player0).hand.size() == 4U);
    EXPECT(context, game.player(PlayerId::Player1).hand.size() == 4U);
    EXPECT(context, game.player(PlayerId::Player0).evolution_points == 2);
    EXPECT(context, game.player(PlayerId::Player1).evolution_points == 3);

    EXPECT(context, game.mulligan(PlayerId::Player0, {}));
    EXPECT(context, game.mulligan(PlayerId::Player1, {}));
    EXPECT(context, game.phase() == Phase::Action);
    EXPECT(context, game.active_player() == PlayerId::Player0);
    EXPECT(context, game.player(PlayerId::Player0).maximum_pp == 1);
    EXPECT(context, game.player(PlayerId::Player0).current_pp == 1);
    EXPECT(context, game.player(PlayerId::Player0).hand.size() == 4U);

    EXPECT(context, game.end_turn(PlayerId::Player0));
    EXPECT(context, game.active_player() == PlayerId::Player1);
    EXPECT(context, game.player(PlayerId::Player1).maximum_pp == 1);
    EXPECT(context, game.player(PlayerId::Player1).hand.size() == 5U);

    EXPECT(context, game.end_turn(PlayerId::Player1));
    EXPECT(context, game.player(PlayerId::Player0).maximum_pp == 2);
    EXPECT(context, game.player(PlayerId::Player0).current_pp == 2);
    EXPECT(context, game.player(PlayerId::Player0).hand.size() == 5U);
}

void test_mulligan_does_not_redraw_set_aside_card(TestContext& context) {
    DeckList short_deck;
    short_deck.main = {
        cards::kRoyalRecruit,
        cards::kRoyalVanguard,
        cards::kRoyalLancer,
        cards::kRoyalTactician,
        cards::kRoyalCavalier,
    };
    GameConfig config;
    config.shuffle_decks = false;
    Game game(make_prototype_catalog(), short_deck, short_deck, config);
    EXPECT(context, game.start());

    const InstanceId selected = game.player(PlayerId::Player0).hand.front();
    EXPECT(context, game.mulligan(PlayerId::Player0, {selected}));
    EXPECT(context, game.instance(selected).zone == Zone::Deck);
    EXPECT(context, game.player(PlayerId::Player0).hand.size() == 4U);
    EXPECT(context, game.find_in_hand(PlayerId::Player0, cards::kRoyalCavalier).has_value());

    Game duplicate_game(make_prototype_catalog(), short_deck, short_deck, config);
    EXPECT(context, duplicate_game.start());
    const InstanceId duplicate = duplicate_game.player(PlayerId::Player0).hand.front();
    const Status duplicate_status = duplicate_game.mulligan(PlayerId::Player0, {duplicate, duplicate});
    EXPECT_CODE(context, duplicate_status, ErrorCode::DuplicateSelection);
    EXPECT(context, duplicate_game.player(PlayerId::Player0).hand.size() == 4U);
}

void test_hand_overflow_and_fatigue(TestContext& context) {
    Scenario overflow = base_scenario(PlayerId::Player1);
    overflow.players[0].own_turn_number = 1;
    overflow.players[0].hand.assign(9U, cards::kRoyalRecruit);
    overflow.players[0].deck = {cards::kRoyalSquire};
    Game overflow_game = scenario_game(overflow);
    EXPECT(context, overflow_game.end_turn(PlayerId::Player1));
    EXPECT(context, overflow_game.player(PlayerId::Player0).hand.size() == 9U);
    EXPECT(context, overflow_game.player(PlayerId::Player0).archive.size() == 1U);
    if (!overflow_game.player(PlayerId::Player0).archive.empty()) {
        EXPECT(context, overflow_game.definition(overflow_game.player(PlayerId::Player0).archive.front()).id == cards::kRoyalSquire);
    }

    Scenario fatigue = base_scenario(PlayerId::Player1);
    fatigue.players[0].own_turn_number = 1;
    Game fatigue_game = scenario_game(fatigue);
    EXPECT(context, fatigue_game.end_turn(PlayerId::Player1));
    EXPECT(context, fatigue_game.player(PlayerId::Player0).fatigue_count == 1);
    EXPECT(context, fatigue_game.player(PlayerId::Player0).leader_health == 24);
    EXPECT(context, fatigue_game.end_turn(PlayerId::Player0));
    EXPECT(context, fatigue_game.end_turn(PlayerId::Player1));
    EXPECT(context, fatigue_game.player(PlayerId::Player0).fatigue_count == 2);
    EXPECT(context, fatigue_game.player(PlayerId::Player0).leader_health == 22);
}

void test_play_unit_and_spell_validation(TestContext& context) {
    Scenario unit_scenario = base_scenario();
    unit_scenario.players[0].current_pp = 2;
    unit_scenario.players[0].maximum_pp = 2;
    unit_scenario.players[0].hand = {cards::kRoyalVanguard, cards::kRoyalBolt};
    unit_scenario.players[1].units = {cards::kTrainingDummy};
    Game unit_game = scenario_game(unit_scenario);

    const InstanceId vanguard = *unit_game.find_in_hand(PlayerId::Player0, cards::kRoyalVanguard);
    EXPECT(context, unit_game.play_unit(PlayerId::Player0, vanguard));
    EXPECT(context, unit_game.player(PlayerId::Player0).current_pp == 0);
    EXPECT(context, unit_game.instance(vanguard).zone == Zone::Unit);
    EXPECT(context, unit_game.instance(vanguard).current_attack == 2);
    EXPECT(context, unit_game.instance(vanguard).current_health == 3);
    EXPECT(context, has_keyword(unit_game.instance(vanguard).keywords, Keyword::Guard));

    const InstanceId bolt = *unit_game.find_in_hand(PlayerId::Player0, cards::kRoyalBolt);
    const Status no_pp = unit_game.cast_spell(
        PlayerId::Player0,
        bolt,
        Target::unit_target(PlayerId::Player1, *unit_game.find_on_field(PlayerId::Player1, cards::kTrainingDummy)));
    EXPECT_CODE(context, no_pp, ErrorCode::InsufficientPP);
    EXPECT(context, unit_game.instance(bolt).zone == Zone::Hand);

    Scenario spell_scenario = base_scenario();
    spell_scenario.players[0].current_pp = 2;
    spell_scenario.players[0].maximum_pp = 2;
    spell_scenario.players[0].hand = {cards::kRoyalBolt};
    spell_scenario.players[1].units = {cards::kTrainingDummy};
    Game spell_game = scenario_game(spell_scenario);
    const InstanceId spell = *spell_game.find_in_hand(PlayerId::Player0, cards::kRoyalBolt);
    const Status invalid_target = spell_game.cast_spell(
        PlayerId::Player0,
        spell,
        Target::leader(PlayerId::Player1));
    EXPECT_CODE(context, invalid_target, ErrorCode::InvalidTarget);
    EXPECT(context, spell_game.player(PlayerId::Player0).current_pp == 2);
    EXPECT(context, spell_game.instance(spell).zone == Zone::Hand);

    const InstanceId dummy = *spell_game.find_on_field(PlayerId::Player1, cards::kTrainingDummy);
    EXPECT(context, spell_game.cast_spell(
        PlayerId::Player0,
        spell,
        Target::unit_target(PlayerId::Player1, dummy)));
    EXPECT(context, spell_game.player(PlayerId::Player0).current_pp == 0);
    EXPECT(context, spell_game.instance(spell).zone == Zone::Graveyard);
    EXPECT(context, spell_game.player(PlayerId::Player1).graveyard.size() == 1U);
}

void test_simultaneous_combat_and_persistent_damage(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].units = {cards::kRoyalRecruit};
    scenario.players[1].units = {cards::kTrainingDummy};
    Game game = scenario_game(scenario);
    const InstanceId recruit = *game.find_on_field(PlayerId::Player0, cards::kRoyalRecruit);
    const InstanceId dummy = *game.find_on_field(PlayerId::Player1, cards::kTrainingDummy);

    EXPECT(context, game.attack(
        PlayerId::Player0,
        recruit,
        Target::unit_target(PlayerId::Player1, dummy)));
    EXPECT(context, game.instance(recruit).zone == Zone::Graveyard);
    EXPECT(context, game.instance(dummy).zone == Zone::Unit);
    EXPECT(context, game.instance(dummy).current_health == 2);
    EXPECT(context, game.instance(dummy).maximum_health == 3);
}

void test_guard_and_rush_target_rules(TestContext& context) {
    Scenario guard_scenario = base_scenario();
    guard_scenario.players[0].units = {cards::kRoyalCommander};
    guard_scenario.players[1].units = {cards::kRoyalVanguard, cards::kTrainingDummy};
    Game guard_game = scenario_game(guard_scenario);
    const InstanceId attacker = *guard_game.find_on_field(PlayerId::Player0, cards::kRoyalCommander);
    const InstanceId guard = *guard_game.find_on_field(PlayerId::Player1, cards::kRoyalVanguard);
    const InstanceId dummy = *guard_game.find_on_field(PlayerId::Player1, cards::kTrainingDummy);

    EXPECT_CODE(
        context,
        guard_game.attack(PlayerId::Player0, attacker, Target::leader(PlayerId::Player1)),
        ErrorCode::GuardBlocksTarget);
    EXPECT_CODE(
        context,
        guard_game.attack(PlayerId::Player0, attacker, Target::unit_target(PlayerId::Player1, dummy)),
        ErrorCode::GuardBlocksTarget);
    EXPECT(context, guard_game.attack(
        PlayerId::Player0,
        attacker,
        Target::unit_target(PlayerId::Player1, guard)));
    EXPECT(context, guard_game.instance(guard).zone == Zone::Graveyard);

    Scenario rush_scenario = base_scenario();
    rush_scenario.players[0].current_pp = 2;
    rush_scenario.players[0].maximum_pp = 2;
    rush_scenario.players[0].hand = {cards::kRoyalLancer};
    rush_scenario.players[1].units = {cards::kTrainingDummy};
    Game rush_game = scenario_game(rush_scenario);
    const InstanceId lancer = *rush_game.find_in_hand(PlayerId::Player0, cards::kRoyalLancer);
    EXPECT(context, rush_game.play_unit(PlayerId::Player0, lancer));
    EXPECT_CODE(
        context,
        rush_game.attack(PlayerId::Player0, lancer, Target::leader(PlayerId::Player1)),
        ErrorCode::SummoningSickness);
    const InstanceId rush_dummy = *rush_game.find_on_field(PlayerId::Player1, cards::kTrainingDummy);
    EXPECT(context, rush_game.attack(
        PlayerId::Player0,
        lancer,
        Target::unit_target(PlayerId::Player1, rush_dummy)));
}

void test_barrier_and_lifesteal(TestContext& context) {
    Scenario barrier_scenario = base_scenario();
    barrier_scenario.players[0].units = {cards::kRoyalRecruit};
    barrier_scenario.players[1].units = {cards::kMachineBarrierPart};
    Game barrier_game = scenario_game(barrier_scenario);
    const InstanceId recruit = *barrier_game.find_on_field(PlayerId::Player0, cards::kRoyalRecruit);
    const InstanceId barrier = *barrier_game.find_on_field(PlayerId::Player1, cards::kMachineBarrierPart);
    EXPECT(context, barrier_game.attack(
        PlayerId::Player0,
        recruit,
        Target::unit_target(PlayerId::Player1, barrier)));
    EXPECT(context, barrier_game.instance(barrier).zone == Zone::Unit);
    EXPECT(context, barrier_game.instance(barrier).current_health == 2);
    EXPECT(context, !has_keyword(barrier_game.instance(barrier).keywords, Keyword::Barrier));
    EXPECT(context, barrier_game.instance(recruit).zone == Zone::Graveyard);

    Scenario lifesteal_scenario = base_scenario();
    lifesteal_scenario.players[0].leader_health = 20;
    lifesteal_scenario.players[0].units = {cards::kMachineRepairPart};
    lifesteal_scenario.players[1].units = {cards::kTrainingDummy};
    Game lifesteal_game = scenario_game(lifesteal_scenario);
    const InstanceId repair = *lifesteal_game.find_on_field(PlayerId::Player0, cards::kMachineRepairPart);
    const InstanceId target = *lifesteal_game.find_on_field(PlayerId::Player1, cards::kTrainingDummy);
    EXPECT(context, lifesteal_game.attack(
        PlayerId::Player0,
        repair,
        Target::unit_target(PlayerId::Player1, target)));
    EXPECT(context, lifesteal_game.player(PlayerId::Player0).leader_health == 22);
    EXPECT(context, lifesteal_game.instance(target).current_health == 1);
}

void test_combat_and_ability_evolution(TestContext& context) {
    Scenario combat = base_scenario();
    combat.players[0].current_pp = 2;
    combat.players[0].maximum_pp = 5;
    combat.players[0].evolution_points = 2;
    combat.players[0].own_turn_number = 5;
    combat.players[0].hand = {cards::kRoyalLancer};
    combat.players[0].units = {cards::kRoyalRecruit};
    combat.players[1].units = {cards::kTrainingDummy};
    Game combat_game = scenario_game(combat);
    const InstanceId lancer = *combat_game.find_in_hand(PlayerId::Player0, cards::kRoyalLancer);
    EXPECT(context, combat_game.play_unit(PlayerId::Player0, lancer));
    EXPECT(context, combat_game.evolve(PlayerId::Player0, lancer, EvolutionMode::Combat));
    EXPECT(context, combat_game.instance(lancer).current_attack == 4);
    EXPECT(context, combat_game.instance(lancer).current_health == 3);
    EXPECT(context, combat_game.instance(lancer).maximum_health == 3);
    EXPECT(context, combat_game.instance(lancer).temporary_rush);
    EXPECT(context, combat_game.player(PlayerId::Player0).evolution_points == 1);
    const InstanceId other = *combat_game.find_on_field(PlayerId::Player0, cards::kRoyalRecruit);
    EXPECT_CODE(
        context,
        combat_game.evolve(PlayerId::Player0, other, EvolutionMode::Combat),
        ErrorCode::EvolutionAlreadyUsed);

    Scenario ability = base_scenario();
    ability.players[0].current_pp = 3;
    ability.players[0].maximum_pp = 5;
    ability.players[0].evolution_points = 1;
    ability.players[0].own_turn_number = 5;
    ability.players[0].hand = {cards::kRoyalTactician};
    ability.players[0].deck = {cards::kRoyalRecruit};
    Game ability_game = scenario_game(ability);
    const InstanceId tactician = *ability_game.find_in_hand(PlayerId::Player0, cards::kRoyalTactician);
    EXPECT(context, ability_game.play_unit(PlayerId::Player0, tactician));
    EXPECT(context, ability_game.evolve(PlayerId::Player0, tactician, EvolutionMode::Ability));
    EXPECT(context, ability_game.instance(tactician).current_attack == 4);
    EXPECT(context, ability_game.instance(tactician).current_health == 4);
    EXPECT(context, !ability_game.instance(tactician).temporary_rush);
    EXPECT(context, ability_game.player(PlayerId::Player0).hand.size() == 1U);
    EXPECT_CODE(
        context,
        ability_game.attack(PlayerId::Player0, tactician, Target::leader(PlayerId::Player1)),
        ErrorCode::SummoningSickness);
}

void test_documented_construct_summon(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].current_pp = 5;
    scenario.players[0].maximum_pp = 5;
    scenario.players[0].evolution_points = 1;
    scenario.players[0].own_turn_number = 5;
    scenario.players[0].units = {cards::kMachineRushPart, cards::kMachineGuardPart};
    scenario.players[0].summon_deck = {cards::kBastionConstruct};
    scenario.players[1].units = {cards::kTrainingDummy};
    Game game = scenario_game(scenario);

    const InstanceId rush = *game.find_on_field(PlayerId::Player0, cards::kMachineRushPart);
    const InstanceId guard = *game.find_on_field(PlayerId::Player0, cards::kMachineGuardPart);
    const InstanceId construct = *game.find_in_summon_deck(PlayerId::Player0, cards::kBastionConstruct);
    const InstanceId dummy = *game.find_on_field(PlayerId::Player1, cards::kTrainingDummy);
    AdvancedSummonRequest request{
        PlayerId::Player0,
        construct,
        {rush, guard},
        Imprint::Guard,
        Target::unit_target(PlayerId::Player1, dummy),
    };
    EXPECT(context, game.advanced_summon(request));
    EXPECT(context, game.player(PlayerId::Player0).current_pp == 3);
    EXPECT(context, game.player(PlayerId::Player0).archive.size() == 2U);
    EXPECT(context, game.instance(construct).current_attack == 5);
    EXPECT(context, game.instance(construct).current_health == 6);
    EXPECT(context, game.instance(construct).inherited_imprint == Imprint::Guard);
    EXPECT(context, has_keyword(game.instance(construct).keywords, Keyword::Guard));
    EXPECT(context, game.instance(dummy).current_health == 1);
    EXPECT_CODE(
        context,
        game.attack(PlayerId::Player0, construct, Target::leader(PlayerId::Player1)),
        ErrorCode::SummoningSickness);
    EXPECT(context, game.evolve(PlayerId::Player0, construct, EvolutionMode::Combat));
    EXPECT(context, game.instance(construct).current_attack == 7);
    EXPECT(context, game.instance(construct).maximum_health == 8);
    EXPECT(context, game.attack(
        PlayerId::Player0,
        construct,
        Target::unit_target(PlayerId::Player1, dummy)));
    EXPECT(context, game.instance(construct).current_health == 5);
    EXPECT(context, game.instance(dummy).zone == Zone::Graveyard);
}

void test_advanced_summon_validation_and_once_per_turn(TestContext& context) {
    Scenario invalid = base_scenario();
    invalid.players[0].current_pp = 5;
    invalid.players[0].maximum_pp = 5;
    invalid.players[0].units = {cards::kMachineRushPart, cards::kMachineGuardPart};
    invalid.players[0].summon_deck = {cards::kBastionConstruct};
    invalid.players[1].units = {cards::kTrainingDummy};
    Game invalid_game = scenario_game(invalid);
    const InstanceId rush = *invalid_game.find_on_field(PlayerId::Player0, cards::kMachineRushPart);
    const InstanceId guard = *invalid_game.find_on_field(PlayerId::Player0, cards::kMachineGuardPart);
    const InstanceId construct = *invalid_game.find_in_summon_deck(PlayerId::Player0, cards::kBastionConstruct);
    const InstanceId dummy = *invalid_game.find_on_field(PlayerId::Player1, cards::kTrainingDummy);
    AdvancedSummonRequest invalid_request{
        PlayerId::Player0,
        construct,
        {rush, guard},
        Imprint::Barrier,
        Target::unit_target(PlayerId::Player1, dummy),
    };
    EXPECT_CODE(context, invalid_game.advanced_summon(invalid_request), ErrorCode::InvalidImprint);
    EXPECT(context, invalid_game.player(PlayerId::Player0).current_pp == 5);
    EXPECT(context, invalid_game.player(PlayerId::Player0).archive.empty());
    EXPECT(context, invalid_game.instance(construct).zone == Zone::SummonDeck);

    Scenario tribute = base_scenario();
    tribute.players[0].current_pp = 6;
    tribute.players[0].maximum_pp = 6;
    tribute.players[0].hand = {cards::kRoyalCrownKnight, cards::kRoyalCrownKnight};
    tribute.players[0].units = {cards::kRoyalShieldbearer, cards::kRoyalShieldbearer};
    Game tribute_game = scenario_game(tribute);
    const InstanceId first_crown = tribute_game.player(PlayerId::Player0).hand[0];
    const InstanceId second_crown = tribute_game.player(PlayerId::Player0).hand[1];
    const InstanceId first_material = *tribute_game.find_on_field(PlayerId::Player0, cards::kRoyalShieldbearer);
    InstanceId second_material = 0;
    for (const auto& slot : tribute_game.player(PlayerId::Player0).units) {
        if (slot.has_value() && *slot != first_material) {
            second_material = *slot;
        }
    }
    EXPECT(context, tribute_game.advanced_summon(AdvancedSummonRequest{
        PlayerId::Player0,
        first_crown,
        {first_material},
        Imprint::Guard,
        std::nullopt,
    }));
    EXPECT(context, tribute_game.instance(first_crown).zone == Zone::Unit);
    EXPECT(context, tribute_game.instance(first_crown).inherited_imprint == Imprint::Guard);
    EXPECT(context, tribute_game.player(PlayerId::Player0).current_pp == 3);
    EXPECT_CODE(
        context,
        tribute_game.advanced_summon(AdvancedSummonRequest{
            PlayerId::Player0,
            second_crown,
            {second_material},
            Imprint::Guard,
            std::nullopt,
        }),
        ErrorCode::AdvancedSummonAlreadyUsed);
}

void test_inherited_imprint_cannot_be_inherited_again(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].current_pp = 10;
    scenario.players[0].maximum_pp = 10;
    scenario.players[0].units = {
        cards::kMachineRushPart,
        cards::kMachineGuardPart,
        cards::kMachineHeavyFrame,
    };
    scenario.players[0].summon_deck = {cards::kBastionConstruct, cards::kAssaultConstruct};
    scenario.players[1].units = {cards::kTrainingDummy};
    Game game = scenario_game(scenario);

    const InstanceId rush = *game.find_on_field(PlayerId::Player0, cards::kMachineRushPart);
    const InstanceId guard = *game.find_on_field(PlayerId::Player0, cards::kMachineGuardPart);
    const InstanceId heavy = *game.find_on_field(PlayerId::Player0, cards::kMachineHeavyFrame);
    const InstanceId bastion = *game.find_in_summon_deck(PlayerId::Player0, cards::kBastionConstruct);
    const InstanceId assault = *game.find_in_summon_deck(PlayerId::Player0, cards::kAssaultConstruct);
    const InstanceId dummy = *game.find_on_field(PlayerId::Player1, cards::kTrainingDummy);

    EXPECT(context, game.advanced_summon(AdvancedSummonRequest{
        PlayerId::Player0,
        bastion,
        {rush, guard},
        Imprint::Guard,
        Target::unit_target(PlayerId::Player1, dummy),
    }));
    EXPECT(context, game.end_turn(PlayerId::Player0));
    EXPECT(context, game.end_turn(PlayerId::Player1));

    const Status second = game.advanced_summon(AdvancedSummonRequest{
        PlayerId::Player0,
        assault,
        {bastion, heavy},
        Imprint::Guard,
        std::nullopt,
    });
    EXPECT_CODE(context, second, ErrorCode::InvalidImprint);
    EXPECT(context, game.instance(assault).zone == Zone::SummonDeck);
    EXPECT(context, game.instance(bastion).zone == Zone::Unit);
}

void test_summon_deck_unit_leaves_to_archive(TestContext& context) {
    Scenario scenario = base_scenario(PlayerId::Player1);
    scenario.players[0].units = {cards::kBastionConstruct};
    scenario.players[1].units = {cards::kRoyalCrownKnight};
    Game game = scenario_game(scenario);
    const InstanceId construct = *game.find_on_field(PlayerId::Player0, cards::kBastionConstruct);
    const InstanceId crown = *game.find_on_field(PlayerId::Player1, cards::kRoyalCrownKnight);
    EXPECT(context, game.attack(
        PlayerId::Player1,
        crown,
        Target::unit_target(PlayerId::Player0, construct)));
    EXPECT(context, game.instance(construct).zone == Zone::Archive);
    EXPECT(context, game.player(PlayerId::Player0).archive.size() == 1U);
    EXPECT(context, game.player(PlayerId::Player0).graveyard.empty());
}

void test_trap_windows_and_tactic_rules(TestContext& context) {
    Scenario cancel = base_scenario();
    cancel.players[0].units = {cards::kRoyalCommander};
    cancel.players[1].tactics = {cards::kRoyalAmbushTrap};
    Game cancel_game = scenario_game(cancel);
    const InstanceId attacker = *cancel_game.find_on_field(PlayerId::Player0, cards::kRoyalCommander);
    const InstanceId trap = *cancel_game.player(PlayerId::Player1).tactics[0];
    EXPECT(context, cancel_game.attack(PlayerId::Player0, attacker, Target::leader(PlayerId::Player1)));
    EXPECT(context, cancel_game.phase() == Phase::Reaction);
    EXPECT(context, cancel_game.reaction_window() == ReactionWindow::BeforeAttackDamage);
    EXPECT(context, cancel_game.eligible_traps().size() == 1U);
    EXPECT(context, cancel_game.activate_trap(PlayerId::Player1, trap));
    EXPECT(context, cancel_game.phase() == Phase::Action);
    EXPECT(context, cancel_game.player(PlayerId::Player1).leader_health == 25);
    EXPECT(context, cancel_game.instance(trap).zone == Zone::Graveyard);
    EXPECT(context, cancel_game.instance(attacker).attacked_this_turn);

    Scenario pass = base_scenario();
    pass.players[0].units = {cards::kRoyalCommander};
    pass.players[1].tactics = {cards::kRoyalAmbushTrap};
    Game pass_game = scenario_game(pass);
    const InstanceId pass_attacker = *pass_game.find_on_field(PlayerId::Player0, cards::kRoyalCommander);
    EXPECT(context, pass_game.attack(PlayerId::Player0, pass_attacker, Target::leader(PlayerId::Player1)));
    EXPECT(context, pass_game.pass_reaction(PlayerId::Player1));
    EXPECT(context, pass_game.player(PlayerId::Player1).leader_health == 20);
    EXPECT(context, pass_game.player(PlayerId::Player1).tactics[0].has_value());

    Scenario summon_trap = base_scenario();
    summon_trap.players[0].current_pp = 1;
    summon_trap.players[0].maximum_pp = 1;
    summon_trap.players[0].hand = {cards::kRoyalRecruit};
    summon_trap.players[1].tactics = {cards::kMachineRetaliationTrap};
    Game summon_trap_game = scenario_game(summon_trap);
    const InstanceId recruit = *summon_trap_game.find_in_hand(PlayerId::Player0, cards::kRoyalRecruit);
    const InstanceId retaliation = *summon_trap_game.player(PlayerId::Player1).tactics[0];
    EXPECT(context, summon_trap_game.play_unit(PlayerId::Player0, recruit));
    EXPECT(context, summon_trap_game.phase() == Phase::Reaction);
    EXPECT(context, summon_trap_game.activate_trap(PlayerId::Player1, retaliation));
    EXPECT(context, summon_trap_game.instance(recruit).zone == Zone::Graveyard);

    Scenario set_limit = base_scenario();
    set_limit.players[0].current_pp = 4;
    set_limit.players[0].maximum_pp = 4;
    set_limit.players[0].hand = {cards::kRoyalAmbushTrap, cards::kRoyalCountercharge};
    Game set_limit_game = scenario_game(set_limit);
    const InstanceId first = set_limit_game.player(PlayerId::Player0).hand[0];
    const InstanceId second = set_limit_game.player(PlayerId::Player0).hand[1];
    EXPECT(context, set_limit_game.play_tactic(PlayerId::Player0, first, 0));
    EXPECT_CODE(
        context,
        set_limit_game.play_tactic(PlayerId::Player0, second, 1),
        ErrorCode::TrapAlreadySetThisTurn);

    Scenario replacement = base_scenario();
    replacement.players[0].current_pp = 4;
    replacement.players[0].maximum_pp = 4;
    replacement.players[0].hand = {cards::kRoyalWarBanner, cards::kRoyalAmbushTrap};
    Game replacement_game = scenario_game(replacement);
    const InstanceId relic = *replacement_game.find_in_hand(PlayerId::Player0, cards::kRoyalWarBanner);
    const InstanceId replacement_trap = *replacement_game.find_in_hand(PlayerId::Player0, cards::kRoyalAmbushTrap);
    EXPECT(context, replacement_game.play_tactic(PlayerId::Player0, relic, 0));
    EXPECT(context, replacement_game.play_tactic(PlayerId::Player0, replacement_trap, 0));
    EXPECT(context, replacement_game.instance(relic).zone == Zone::Graveyard);
    EXPECT(context, replacement_game.instance(replacement_trap).zone == Zone::Tactic);
}

void test_relic_countdown(TestContext& context) {
    Scenario scenario = base_scenario(PlayerId::Player1);
    scenario.players[0].own_turn_number = 1;
    scenario.players[0].tactics = {cards::kRoyalCountdownRelic};
    scenario.players[0].deck = {
        cards::kRoyalRecruit,
        cards::kRoyalRecruit,
        cards::kRoyalRecruit,
    };
    scenario.players[1].deck = {
        cards::kMachineDrone,
        cards::kMachineDrone,
        cards::kMachineDrone,
    };
    Game game = scenario_game(scenario);
    const InstanceId relic = *game.player(PlayerId::Player0).tactics[0];

    EXPECT(context, game.end_turn(PlayerId::Player1));
    EXPECT(context, game.instance(relic).countdown == 1);
    EXPECT(context, game.player(PlayerId::Player0).hand.size() == 1U);
    EXPECT(context, game.end_turn(PlayerId::Player0));
    EXPECT(context, game.end_turn(PlayerId::Player1));
    EXPECT(context, game.instance(relic).zone == Zone::Graveyard);
    EXPECT(context, game.player(PlayerId::Player0).hand.size() == 3U);
}

void test_leader_skill_and_generated_hand_overflow(TestContext& context) {
    Scenario royal = base_scenario();
    royal.players[0].current_pp = 2;
    royal.players[0].maximum_pp = 5;
    royal.players[0].own_turn_number = 5;
    royal.players[0].units = {cards::kRoyalRecruit};
    royal.players[0].leader_skill = {"[测试] 集结", 2, Ability::GiveFriendlyUnitOneOne};
    Game royal_game = scenario_game(royal);
    const InstanceId recruit = *royal_game.find_on_field(PlayerId::Player0, cards::kRoyalRecruit);
    EXPECT(context, royal_game.use_leader_skill(
        PlayerId::Player0,
        Target::unit_target(PlayerId::Player0, recruit)));
    EXPECT(context, royal_game.instance(recruit).current_attack == 2);
    EXPECT(context, royal_game.instance(recruit).current_health == 3);
    EXPECT(context, royal_game.player(PlayerId::Player0).current_pp == 0);
    EXPECT_CODE(
        context,
        royal_game.use_leader_skill(PlayerId::Player0, Target::unit_target(PlayerId::Player0, recruit)),
        ErrorCode::LeaderSkillAlreadyUsed);

    Scenario machine = base_scenario();
    machine.players[0].current_pp = 1;
    machine.players[0].maximum_pp = 5;
    machine.players[0].own_turn_number = 5;
    machine.players[0].hand.assign(9U, cards::kMachineDrone);
    machine.players[0].leader_skill = {"[测试] 制造零件", 1, Ability::CreateRushPartInHand};
    Game machine_game = scenario_game(machine);
    EXPECT(context, machine_game.use_leader_skill(PlayerId::Player0));
    EXPECT(context, machine_game.player(PlayerId::Player0).hand.size() == 9U);
    EXPECT(context, machine_game.player(PlayerId::Player0).archive.size() == 1U);
    if (!machine_game.player(PlayerId::Player0).archive.empty()) {
        EXPECT(context, machine_game.definition(machine_game.player(PlayerId::Player0).archive.front()).id == cards::kMachineRushPart);
    }
}

void test_pp_refreshes_instead_of_carrying(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].current_pp = 3;
    scenario.players[0].maximum_pp = 10;
    scenario.players[0].deck = {cards::kRoyalRecruit};
    scenario.players[1].current_pp = 0;
    scenario.players[1].maximum_pp = 10;
    scenario.players[1].deck = {cards::kMachineDrone};
    Game game = scenario_game(scenario);
    EXPECT(context, game.end_turn(PlayerId::Player0));
    EXPECT(context, game.player(PlayerId::Player1).current_pp == 10);
    EXPECT(context, game.end_turn(PlayerId::Player1));
    EXPECT(context, game.player(PlayerId::Player0).current_pp == 10);
    EXPECT(context, game.player(PlayerId::Player0).maximum_pp == 10);
}

void test_win_and_finished_state(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].units = {cards::kRoyalCommander};
    scenario.players[1].leader_health = 5;
    Game game = scenario_game(scenario);
    const InstanceId commander = *game.find_on_field(PlayerId::Player0, cards::kRoyalCommander);
    EXPECT(context, game.attack(PlayerId::Player0, commander, Target::leader(PlayerId::Player1)));
    EXPECT(context, game.result() == GameResult::Player0Won);
    EXPECT(context, game.phase() == Phase::Finished);
    EXPECT_CODE(context, game.end_turn(PlayerId::Player0), ErrorCode::GameOver);
}

void test_surrender_and_ambush_rules(TestContext& context) {
    Scenario surrender_scenario = base_scenario(PlayerId::Player0);
    Game surrender_game = scenario_game(surrender_scenario);
    EXPECT(context, surrender_game.surrender(PlayerId::Player1));
    EXPECT(context, surrender_game.result() == GameResult::Player0Won);
    EXPECT(context, surrender_game.phase() == Phase::Finished);
    EXPECT_CODE(context, surrender_game.surrender(PlayerId::Player1), ErrorCode::GameOver);
    expect_valid_state(context, surrender_game);

    constexpr CardId kAmbushUnit = 9810;
    CardCatalog catalog = make_prototype_catalog();
    CardDefinition ambush;
    ambush.id = kAmbushUnit;
    ambush.name = "潜伏测试单位";
    ambush.kind = CardKind::Unit;
    ambush.cost = 2;
    ambush.attack = 2;
    ambush.health = 2;
    ambush.keywords = mask(Keyword::Ambush);
    catalog.add(ambush);

    Scenario targeting = base_scenario(PlayerId::Player0);
    targeting.players[0].current_pp = 2;
    targeting.players[0].maximum_pp = 2;
    targeting.players[0].hand = {cards::kRoyalBolt};
    targeting.players[1].units = {kAmbushUnit};
    Game target_game(catalog, {}, {});
    EXPECT(context, target_game.load_scenario(targeting));
    const InstanceId spell = *target_game.find_in_hand(PlayerId::Player0, cards::kRoyalBolt);
    const InstanceId hidden = *target_game.find_on_field(PlayerId::Player1, kAmbushUnit);
    EXPECT_CODE(
        context,
        target_game.cast_spell(
            PlayerId::Player0,
            spell,
            Target::unit_target(PlayerId::Player1, hidden)),
        ErrorCode::InvalidTarget);
    EXPECT(context, target_game.player(PlayerId::Player0).current_pp == 2);
    EXPECT(context, target_game.instance(spell).zone == Zone::Hand);
    expect_valid_state(context, target_game);

    Scenario attack_scenario = base_scenario(PlayerId::Player0);
    attack_scenario.players[0].units = {kAmbushUnit};
    Game attack_game(std::move(catalog), {}, {});
    EXPECT(context, attack_game.load_scenario(attack_scenario));
    const InstanceId attacker = *attack_game.find_on_field(PlayerId::Player0, kAmbushUnit);
    EXPECT(context, has_keyword(attack_game.instance(attacker).keywords, Keyword::Ambush));
    EXPECT(context, attack_game.attack(PlayerId::Player0, attacker, Target::leader(PlayerId::Player1)));
    EXPECT(context, !has_keyword(attack_game.instance(attacker).keywords, Keyword::Ambush));
    expect_valid_state(context, attack_game);
}

void test_simultaneous_death_batch_and_trigger_order(TestContext& context) {
    constexpr CardId kActiveLastWords = 9801;
    constexpr CardId kInactiveLastWords = 9802;
    constexpr CardId kActiveDraw = 9803;
    constexpr CardId kInactiveDraw = 9804;

    CardCatalog catalog = make_prototype_catalog();
    CardDefinition active_unit;
    active_unit.id = kActiveLastWords;
    active_unit.name = "主动方遗言测试单位";
    active_unit.kind = CardKind::Unit;
    active_unit.cost = 1;
    active_unit.attack = 1;
    active_unit.health = 1;
    active_unit.last_words_ability = Ability::DrawOne;
    catalog.add(active_unit);

    CardDefinition inactive_unit = active_unit;
    inactive_unit.id = kInactiveLastWords;
    inactive_unit.name = "非主动方遗言测试单位";
    catalog.add(inactive_unit);

    CardDefinition active_draw = active_unit;
    active_draw.id = kActiveDraw;
    active_draw.name = "主动方抽牌标记";
    active_draw.last_words_ability = Ability::None;
    catalog.add(active_draw);

    CardDefinition inactive_draw = active_draw;
    inactive_draw.id = kInactiveDraw;
    inactive_draw.name = "非主动方抽牌标记";
    catalog.add(inactive_draw);

    Scenario scenario = base_scenario(PlayerId::Player0);
    scenario.players[0].units = {kActiveLastWords};
    scenario.players[0].deck = {kActiveDraw};
    scenario.players[1].units = {kInactiveLastWords};
    scenario.players[1].deck = {kInactiveDraw};

    Game game(std::move(catalog), {}, {});
    EXPECT(context, game.load_scenario(scenario));
    (void)game.drain_events();
    const InstanceId attacker = *game.find_on_field(PlayerId::Player0, kActiveLastWords);
    const InstanceId defender = *game.find_on_field(PlayerId::Player1, kInactiveLastWords);
    EXPECT(context, game.attack(
        PlayerId::Player0,
        attacker,
        Target::unit_target(PlayerId::Player1, defender)));

    const std::vector<GameEvent> events = game.drain_events();
    std::vector<EventType> significant_types;
    std::vector<PlayerId> draw_order;
    for (const GameEvent& event : events) {
        if (event.type == EventType::UnitDestroyed || event.type == EventType::CardDrawn) {
            significant_types.push_back(event.type);
        }
        if (event.type == EventType::CardDrawn) {
            draw_order.push_back(event.player);
        }
    }
    EXPECT(context, significant_types.size() == 4U);
    if (significant_types.size() == 4U) {
        EXPECT(context, significant_types[0] == EventType::UnitDestroyed);
        EXPECT(context, significant_types[1] == EventType::UnitDestroyed);
        EXPECT(context, significant_types[2] == EventType::CardDrawn);
        EXPECT(context, significant_types[3] == EventType::CardDrawn);
    }
    EXPECT(context, draw_order.size() == 2U);
    if (draw_order.size() == 2U) {
        EXPECT(context, draw_order[0] == PlayerId::Player0);
        EXPECT(context, draw_order[1] == PlayerId::Player1);
    }
    EXPECT(context, game.player(PlayerId::Player0).graveyard.size() == 1U);
    EXPECT(context, game.player(PlayerId::Player1).graveyard.size() == 1U);
    expect_valid_state(context, game);
}

void test_invariants_and_deterministic_smoke_matches(TestContext& context) {
    int seed_count = 32;
    if (const char* configured = std::getenv("SCGS_SMOKE_SEEDS")) {
        const int parsed = std::atoi(configured);
        if (parsed > 0 && parsed <= 10000) {
            seed_count = parsed;
        }
    }
    constexpr int kMaximumActions = 500;
    int completed_matches = 0;
    int total_actions = 0;

    for (int seed = 0; seed < seed_count; ++seed) {
        GameConfig config;
        config.random_seed = 0x5C6A0000U + static_cast<std::uint32_t>(seed);
        config.first_player = seed % 2 == 0 ? PlayerId::Player0 : PlayerId::Player1;
        Game game(
            make_prototype_catalog(),
            make_royal_prototype_deck(),
            make_machine_prototype_deck(),
            config);
        EXPECT(context, game.start());
        expect_valid_state(context, game);
        EXPECT(context, game.mulligan(PlayerId::Player0, {}));
        expect_valid_state(context, game);
        EXPECT(context, game.mulligan(PlayerId::Player1, {}));
        expect_valid_state(context, game);

        for (int step = 0; step < kMaximumActions && game.result() == GameResult::Ongoing; ++step) {
            const bool progressed = take_smoke_action(game, step + seed);
            EXPECT(context, progressed);
            expect_valid_state(context, game);
            ++total_actions;
            if (!progressed) {
                break;
            }
        }
        if (game.result() != GameResult::Ongoing) {
            ++completed_matches;
        }
    }

    EXPECT(context, completed_matches == seed_count);
    EXPECT(context, total_actions > seed_count * 20);
}

void test_protocol_round_trip_and_validation(TestContext& context) {
    PlayerState state;
    state.leader_health = 17;
    state.maximum_leader_health = 25;
    state.current_pp = 3;
    state.maximum_pp = 7;
    state.evolution_points = 2;
    state.own_turn_number = 6;
    state.evolution_used_this_turn = true;
    state.advanced_summon_used_this_turn = true;
    const auto player_wire = protocol::make_player_state_wire(PlayerId::Player1, state);
    const auto player_bytes = protocol::encode_player_state(player_wire);
    const auto player_payload = protocol::encode_player_state_payload(player_wire);
    const auto decoded_player = protocol::decode_player_state(player_bytes);
    const auto decoded_player_payload = protocol::decode_player_state_payload(player_payload);
    EXPECT(context, decoded_player.player == PlayerId::Player1);
    EXPECT(context, decoded_player.leader_health == 17);
    EXPECT(context, decoded_player.maximum_leader_health == 25);
    EXPECT(context, decoded_player.current_pp == 3);
    EXPECT(context, decoded_player.maximum_pp == 7);
    EXPECT(context, decoded_player.evolution_points == 2);
    EXPECT(context, decoded_player.own_turn_number == 6);
    EXPECT(context, (decoded_player.flags & 0x03U) == 0x03U);
    EXPECT(context, decoded_player_payload.player == decoded_player.player);
    EXPECT(context, decoded_player_payload.leader_health == decoded_player.leader_health);
    EXPECT(context, decoded_player_payload.flags == decoded_player.flags);
    EXPECT(context, player_payload.size() == protocol::kPlayerStatePayloadSize);
    EXPECT(context, player_bytes.size() == protocol::kPlayerStateMessageSize);
    EXPECT(context, std::equal(player_payload.begin(), player_payload.end(), player_bytes.begin() + 1));

    CardInstance unit;
    unit.id = 0x0102030405060708ULL;
    unit.controller = PlayerId::Player0;
    unit.sequence = 3;
    unit.current_attack = 7;
    unit.current_health = 5;
    unit.maximum_health = 8;
    unit.keywords = mask(Keyword::Guard) | mask(Keyword::Rush);
    unit.inherited_imprint = Imprint::Guard;
    unit.evolved = true;
    unit.advanced_summoned_this_turn = true;
    const auto unit_wire = protocol::make_unit_state_wire(unit);
    const auto unit_bytes = protocol::encode_unit_state(unit_wire);
    const auto unit_payload = protocol::encode_unit_state_payload(unit_wire);
    const auto decoded_unit = protocol::decode_unit_state(unit_bytes);
    const auto decoded_unit_payload = protocol::decode_unit_state_payload(unit_payload);
    EXPECT(context, decoded_unit.instance_id == unit.id);
    EXPECT(context, decoded_unit.sequence == 3);
    EXPECT(context, decoded_unit.attack == 7);
    EXPECT(context, decoded_unit.health == 5);
    EXPECT(context, decoded_unit.maximum_health == 8);
    EXPECT(context, decoded_unit.keywords == unit.keywords);
    EXPECT(context, decoded_unit.inherited_imprint == Imprint::Guard);
    EXPECT(context, (decoded_unit.flags & 0x09U) == 0x09U);
    EXPECT(context, decoded_unit_payload.instance_id == decoded_unit.instance_id);
    EXPECT(context, decoded_unit_payload.health == decoded_unit.health);
    EXPECT(context, decoded_unit_payload.flags == decoded_unit.flags);
    EXPECT(context, unit_payload.size() == protocol::kUnitStatePayloadSize);
    EXPECT(context, unit_bytes.size() == protocol::kUnitStateMessageSize);
    EXPECT(context, std::equal(unit_payload.begin(), unit_payload.end(), unit_bytes.begin() + 1));

    const std::vector<std::uint8_t> expected_player_bytes = {
        0xD3U, 0x01U, 0x01U, 0x11U, 0x00U, 0x19U,
        0x00U, 0x03U, 0x07U, 0x02U, 0x06U, 0x03U,
    };
    EXPECT(context, player_bytes == expected_player_bytes);
    EXPECT(context, player_payload == std::vector<std::uint8_t>(expected_player_bytes.begin() + 1, expected_player_bytes.end()));
    const std::vector<std::uint8_t> expected_unit_bytes = {
        0xD4U, 0x01U, 0x00U, 0x03U,
        0x08U, 0x07U, 0x06U, 0x05U, 0x04U, 0x03U, 0x02U, 0x01U,
        0x07U, 0x00U, 0x05U, 0x00U, 0x08U, 0x00U,
        0x03U, 0x00U, 0x00U, 0x00U, 0x01U, 0x09U,
    };
    EXPECT(context, unit_bytes == expected_unit_bytes);
    EXPECT(context, unit_payload == std::vector<std::uint8_t>(expected_unit_bytes.begin() + 1, expected_unit_bytes.end()));

    bool rejected_trailing = false;
    auto malformed = player_bytes;
    malformed.push_back(0xFFU);
    try {
        (void)protocol::decode_player_state(malformed);
    } catch (const std::invalid_argument&) {
        rejected_trailing = true;
    }
    EXPECT(context, rejected_trailing);

    bool rejected_payload_version = false;
    auto bad_payload = player_payload;
    bad_payload[0] = 99U;
    try {
        (void)protocol::decode_player_state_payload(bad_payload);
    } catch (const std::invalid_argument&) {
        rejected_payload_version = true;
    }
    EXPECT(context, rejected_payload_version);
}

using TestFunction = void (*)(TestContext&);

struct TestCase {
    const char* name;
    TestFunction function;
};

} // namespace

int main() {
    const std::vector<TestCase> tests = {
        {"catalog_and_fixed_decks", test_catalog_and_fixed_decks},
        {"start_mulligan_and_turn_flow", test_start_mulligan_and_turn_flow},
        {"mulligan_no_redraw", test_mulligan_does_not_redraw_set_aside_card},
        {"hand_overflow_and_fatigue", test_hand_overflow_and_fatigue},
        {"play_unit_and_spell_validation", test_play_unit_and_spell_validation},
        {"simultaneous_combat", test_simultaneous_combat_and_persistent_damage},
        {"guard_and_rush", test_guard_and_rush_target_rules},
        {"barrier_and_lifesteal", test_barrier_and_lifesteal},
        {"evolution", test_combat_and_ability_evolution},
        {"documented_construct", test_documented_construct_summon},
        {"advanced_summon_validation", test_advanced_summon_validation_and_once_per_turn},
        {"imprint_no_second_inheritance", test_inherited_imprint_cannot_be_inherited_again},
        {"summon_unit_archives", test_summon_deck_unit_leaves_to_archive},
        {"trap_windows_and_tactics", test_trap_windows_and_tactic_rules},
        {"relic_countdown", test_relic_countdown},
        {"leader_skill", test_leader_skill_and_generated_hand_overflow},
        {"pp_refresh", test_pp_refreshes_instead_of_carrying},
        {"win_and_finish", test_win_and_finished_state},
        {"surrender_and_ambush", test_surrender_and_ambush_rules},
        {"death_batch_order", test_simultaneous_death_batch_and_trigger_order},
        {"deterministic_smoke", test_invariants_and_deterministic_smoke_matches},
        {"protocol_round_trip", test_protocol_round_trip_and_validation},
    };

    int total_assertions = 0;
    int total_failures = 0;
    int failed_tests = 0;

    for (const TestCase& test : tests) {
        TestContext context;
        try {
            test.function(context);
        } catch (const std::exception& error) {
            ++context.failures;
            std::cerr << "uncaught exception in " << test.name << ": " << error.what() << '\n';
        } catch (...) {
            ++context.failures;
            std::cerr << "unknown exception in " << test.name << '\n';
        }
        total_assertions += context.assertions;
        total_failures += context.failures;
        if (context.failures == 0) {
            std::cout << "[PASS] " << test.name << " (" << context.assertions << " assertions)\n";
        } else {
            ++failed_tests;
            std::cout << "[FAIL] " << test.name << " (" << context.failures << " failures)\n";
        }
    }

    std::cout << "\n" << tests.size() << " test cases, " << total_assertions
              << " assertions, " << total_failures << " failures\n";
    return failed_tests == 0 ? EXIT_SUCCESS : EXIT_FAILURE;
}
