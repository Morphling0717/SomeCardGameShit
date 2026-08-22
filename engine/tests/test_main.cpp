// SPDX-License-Identifier: GPL-3.0-or-later
#include "scgs/game.hpp"
#include "scgs/protocol.hpp"

#include <algorithm>
#include <cstdlib>
#include <exception>
#include <functional>
#include <iostream>
#include <random>
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

GameConfig deterministic_test_config() {
    GameConfig config;
    config.random_seed = 0x5C6A2026U;
    config.first_player_mode = FirstPlayerMode::Player0;
    return config;
}

Game scenario_game(const Scenario& scenario, GameConfig config = deterministic_test_config()) {
    Game game(make_v04_catalog(), make_midrange_deck(), make_advance_deck(), config);
    const Status status = game.load_scenario(scenario);
    if (!status) {
        throw std::runtime_error("failed to load test scenario: " + status.message);
    }
    return game;
}

Game scenario_game_with_catalog(CardCatalog catalog, const Scenario& scenario) {
    Game game(std::move(catalog), make_midrange_deck(), make_advance_deck(), deterministic_test_config());
    const Status status = game.load_scenario(scenario);
    if (!status) {
        throw std::runtime_error("failed to load custom test scenario: " + status.message);
    }
    return game;
}

Scenario base_scenario(const PlayerId active = PlayerId::Player0) {
    Scenario scenario;
    scenario.active_player = active;
    for (auto& player : scenario.players) {
        player.leader_health = 25;
        player.maximum_leader_health = 25;
        player.current_pp = 5;
        player.pp_capacity = 5;
        player.evolution_points = 2;
        player.own_turn_number = 5;
        // Enough deck filler to keep turn-flow tests away from fatigue.
        for (int i = 0; i < 20; ++i) {
            player.deck.push_back(cards::midrange::kGuardSentry);
        }
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

int count_events(const std::vector<GameEvent>& events, const EventType type) {
    return static_cast<int>(std::count_if(events.begin(), events.end(), [type](const GameEvent& event) {
        return event.type == type;
    }));
}

// ---------------------------------------------------------------------------
// R1. PP capacity growth and refill (rules-v0.4 §7)
// ---------------------------------------------------------------------------
void test_pp_capacity_growth_and_refill(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].current_pp = 2; // deliberately low
    scenario.players[0].pp_capacity = 4;
    scenario.players[1].own_turn_number = 5;
    Game game = scenario_game(scenario);

    // Capacity grows by 1 on each own turn with no cap; current PP refills.
    EXPECT(context, game.end_turn(PlayerId::Player0));
    EXPECT(context, game.end_turn(PlayerId::Player1));
    const PlayerState& state = game.player(PlayerId::Player0);
    EXPECT(context, state.pp_capacity == 5);
    EXPECT(context, state.current_pp == 5);
    EXPECT(context, state.cracks == 0);

    // Unused PP does not carry over (it was refilled, not accumulated).
    for (int i = 0; i < 6; ++i) {
        EXPECT(context, game.end_turn(game.active_player()));
    }
    const PlayerState& later = game.player(PlayerId::Player0);
    EXPECT(context, later.pp_capacity == 8);
    EXPECT(context, later.current_pp == 8);

    // No fixed cap: drive capacity far beyond 10.
    for (int i = 0; i < 30; ++i) {
        EXPECT(context, game.end_turn(game.active_player()));
    }
    EXPECT(context, game.player(PlayerId::Player0).pp_capacity > 20);
    expect_valid_state(context, game);
}

void test_end_turn_cleanup_order(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].units = {cards::midrange::kEliteCommander};
    Game game = scenario_game(scenario);
    const InstanceId unit = *game.find_on_field(PlayerId::Player0, cards::midrange::kEliteCommander);
    EXPECT(context, game.evolve(PlayerId::Player0, unit));
    EXPECT(context, game.instance(unit).temporary_rush);
    (void)game.drain_events();

    EXPECT(context, game.end_turn(PlayerId::Player0));
    EXPECT(context, game.player(PlayerId::Player0).current_pp == 0);
    EXPECT(context, !game.instance(unit).temporary_rush);

    const std::vector<GameEvent> events = game.drain_events();
    std::size_t pp_cleared = events.size();
    std::size_t turn_ended = events.size();
    for (std::size_t index = 0; index < events.size(); ++index) {
        if (events[index].type == EventType::PPChanged && events[index].player == PlayerId::Player0 &&
            events[index].value == 0) {
            pp_cleared = std::min(pp_cleared, index);
        }
        if (events[index].type == EventType::TurnEnded && events[index].player == PlayerId::Player0) {
            turn_ended = std::min(turn_ended, index);
        }
    }
    EXPECT(context, pp_cleared < turn_ended);
    expect_valid_state(context, game);
}

// ---------------------------------------------------------------------------
// R2. Advance payment and limits (rules-v0.4 §9/§10)
// ---------------------------------------------------------------------------
void test_advance_payment_and_limits(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].hand = {cards::advance::kDebtLord}; // 8PP 8/6
    Game game = scenario_game(scenario);
    const InstanceId debt_lord = *game.find_in_hand(PlayerId::Player0, cards::advance::kDebtLord);

    // Without advance: refused.
    EXPECT_CODE(context, game.play_unit(PlayerId::Player0, debt_lord), ErrorCode::InsufficientPP);
    // With advance: 5 current / 5 capacity → 0 current / 2 capacity / 3 cracks.
    EXPECT(context, game.play_unit(PlayerId::Player0, debt_lord, std::nullopt, std::nullopt, true));
    const PlayerState& state = game.player(PlayerId::Player0);
    EXPECT(context, state.current_pp == 0);
    EXPECT(context, state.pp_capacity == 2);
    EXPECT(context, state.cracks == 3);
    EXPECT(context, game.find_on_field(PlayerId::Player0, cards::advance::kDebtLord).has_value());

    // Once per turn (rules-v0.4 §10.1): a second capacity payment is refused.
    Scenario scenario2 = base_scenario();
    scenario2.players[0].hand = {cards::advance::kDebtLord, cards::advance::kBurnBlast};
    Game game2 = scenario_game(scenario2);
    const InstanceId debt2 = *game2.find_in_hand(PlayerId::Player0, cards::advance::kDebtLord);
    EXPECT(context, game2.play_unit(PlayerId::Player0, debt2, std::nullopt, std::nullopt, true));
    const InstanceId blast = *game2.find_in_hand(PlayerId::Player0, cards::advance::kBurnBlast);
    // Burn counts as 动用未来 too; current PP 0 so the burn spell cannot even pay.
    EXPECT_CODE(context, game2.cast_spell(PlayerId::Player0, blast), ErrorCode::InsufficientPP);

    // Capacity cannot fall below zero (rules-v0.4 §10.5).
    Scenario scenario3 = base_scenario();
    scenario3.players[0].current_pp = 1;
    scenario3.players[0].pp_capacity = 1;
    scenario3.players[0].hand = {cards::advance::kDebtLord};
    Game game3 = scenario_game(scenario3);
    const InstanceId debt3 = *game3.find_in_hand(PlayerId::Player0, cards::advance::kDebtLord);
    EXPECT_CODE(context, game3.play_unit(PlayerId::Player0, debt3, std::nullopt, std::nullopt, true),
                ErrorCode::AdvanceWouldExceedCap);

    // Advance only applies when current PP is insufficient (rules-v0.4 §10.2):
    // requesting advance with enough PP pays normally, no cracks.
    Scenario scenario4 = base_scenario();
    scenario4.players[0].current_pp = 8;
    scenario4.players[0].pp_capacity = 8;
    scenario4.players[0].hand = {cards::advance::kDebtLord};
    Game game4 = scenario_game(scenario4);
    const InstanceId debt4 = *game4.find_in_hand(PlayerId::Player0, cards::advance::kDebtLord);
    EXPECT(context, game4.play_unit(PlayerId::Player0, debt4, std::nullopt, std::nullopt, true));
    EXPECT(context, game4.player(PlayerId::Player0).current_pp == 0);
    EXPECT(context, game4.player(PlayerId::Player0).pp_capacity == 8);
    EXPECT(context, game4.player(PlayerId::Player0).cracks == 0);

    expect_valid_state(context, game);
    expect_valid_state(context, game2);
    expect_valid_state(context, game3);
    expect_valid_state(context, game4);
}

// ---------------------------------------------------------------------------
// R3. Burn cost and combined advance+burn (rules-v0.4 §12/§13)
// ---------------------------------------------------------------------------
void test_burn_cost_and_combined_advance(TestContext& context) {
    // Pure burn with enough current PP (rules-v0.4 §12): capacity drops and
    // cracks grow even though current PP covered the cost.
    Scenario scenario = base_scenario();
    scenario.players[0].current_pp = 6;
    scenario.players[0].pp_capacity = 6;
    scenario.players[1].units = {cards::midrange::kGuardSentry};
    scenario.players[0].hand = {cards::advance::kBurnBlast}; // 1PP + burn2
    Game game = scenario_game(scenario);
    const InstanceId blast = *game.find_in_hand(PlayerId::Player0, cards::advance::kBurnBlast);
    EXPECT(context, game.cast_spell(PlayerId::Player0, blast, first_enemy_unit_target(game, PlayerId::Player0)));
    const PlayerState& state = game.player(PlayerId::Player0);
    EXPECT(context, state.current_pp == 5);
    EXPECT(context, state.pp_capacity == 4);
    EXPECT(context, state.cracks == 2);

    // Combined advance + burn (rules-v0.4 §13): 3 current / 6 capacity, a
    // 5PP + 燃耗1 card → pay 3, advance 2, burn 1: capacity 6→3, cracks 3.
    Scenario scenario2 = base_scenario();
    scenario2.players[0].current_pp = 3;
    scenario2.players[0].pp_capacity = 6;
    scenario2.players[1].units = {cards::midrange::kIronShieldBearer, cards::midrange::kIronShieldBearer}; // 2/5 ×2
    scenario2.players[0].hand = {cards::advance::kBurnBlast, cards::advance::kAdvanceStrike};
    Game game2 = scenario_game(scenario2);
    // 燃耗爆破 is 1PP+burn2; with 3 current PP it pays normally (burn only).
    const InstanceId blast2 = *game2.find_in_hand(PlayerId::Player0, cards::advance::kBurnBlast);
    EXPECT(context, game2.cast_spell(PlayerId::Player0, blast2, first_enemy_unit_target(game2, PlayerId::Player0)));
    EXPECT(context, game2.player(PlayerId::Player0).current_pp == 2);
    EXPECT(context, game2.player(PlayerId::Player0).pp_capacity == 4);
    EXPECT(context, game2.player(PlayerId::Player0).cracks == 2);
    // 超前打击 is 2PP+burn1 but 动用未来 was already used this turn → refused.
    const InstanceId strike = *game2.find_in_hand(PlayerId::Player0, cards::advance::kAdvanceStrike);
    EXPECT_CODE(context, game2.cast_spell(PlayerId::Player0, strike, first_enemy_unit_target(game2, PlayerId::Player0)),
                ErrorCode::AdvanceAlreadyUsed);

    expect_valid_state(context, game);
    expect_valid_state(context, game2);
}

// ---------------------------------------------------------------------------
// R4. Cracks persist and are readable (rules-v0.4 §14)
// ---------------------------------------------------------------------------
void test_cracks_persistence_and_read(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].hand = {cards::advance::kDebtLord, cards::advance::kCrackFeeder};
    scenario.players[0].current_pp = 5;
    scenario.players[0].pp_capacity = 5;
    scenario.players[1].units = {cards::midrange::kIronShieldBearer}; // 2/5
    Game game = scenario_game(scenario);

    const InstanceId debt = *game.find_in_hand(PlayerId::Player0, cards::advance::kDebtLord);
    EXPECT(context, game.play_unit(PlayerId::Player0, debt, std::nullopt, std::nullopt, true));
    EXPECT(context, game.player(PlayerId::Player0).cracks == 3);

    // Cracks survive the opponent's turn (natural growth does not clear them).
    EXPECT(context, game.end_turn(PlayerId::Player0));
    EXPECT(context, game.end_turn(PlayerId::Player1));
    EXPECT(context, game.player(PlayerId::Player0).cracks == 3);

    // 裂痕感知者 reads cracks: deals min(cracks,3) to the enemy unit.
    const InstanceId feeder = *game.find_in_hand(PlayerId::Player0, cards::advance::kCrackFeeder);
    const auto enemy = first_enemy_unit_target(game, PlayerId::Player0);
    EXPECT(context, game.play_unit(PlayerId::Player0, feeder, std::nullopt, enemy));
    const CardInstance& shield = game.instance(enemy->unit);
    EXPECT(context, shield.current_health == 2); // 5 - min(3,3)
    expect_valid_state(context, game);
}

// ---------------------------------------------------------------------------
// R5. Repair restores capacity (rules-v0.4 §15)
// ---------------------------------------------------------------------------
void test_repair_restores_capacity(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].hand = {cards::advance::kRepairTechnician};
    scenario.players[0].current_pp = 4;
    scenario.players[0].pp_capacity = 4;
    scenario.players[0].cracks = 5;
    Game game = scenario_game(scenario);

    const InstanceId tech = *game.find_in_hand(PlayerId::Player0, cards::advance::kRepairTechnician);
    EXPECT(context, game.play_unit(PlayerId::Player0, tech));
    // Repair 2 removes at most 2 cracks and restores 2 capacity.
    EXPECT(context, game.player(PlayerId::Player0).cracks == 3);
    EXPECT(context, game.player(PlayerId::Player0).pp_capacity == 6);
    // Repair does not touch current PP (rules-v0.4 §15).
    EXPECT(context, game.player(PlayerId::Player0).current_pp == 2);

    // No cracks → repair does nothing (rules-v0.4 §15: repair is not ramp).
    Scenario scenario2 = base_scenario();
    scenario2.players[0].hand = {cards::advance::kRepairTechnician};
    scenario2.players[0].current_pp = 4;
    scenario2.players[0].pp_capacity = 4;
    Game game2 = scenario_game(scenario2);
    const InstanceId tech2 = *game2.find_in_hand(PlayerId::Player0, cards::advance::kRepairTechnician);
    EXPECT(context, game2.play_unit(PlayerId::Player0, tech2));
    EXPECT(context, game2.player(PlayerId::Player0).pp_capacity == 4);
    expect_valid_state(context, game);
    expect_valid_state(context, game2);
}

// ---------------------------------------------------------------------------
// R6. Growth adds capacity (rules-v0.4 §16)
// ---------------------------------------------------------------------------
void test_growth_adds_capacity(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].hand = {cards::advance::kGrowthFacility}; // 2PP relic
    scenario.players[0].current_pp = 4;
    scenario.players[0].pp_capacity = 4;
    Game game = scenario_game(scenario);

    const InstanceId facility = *game.find_in_hand(PlayerId::Player0, cards::advance::kGrowthFacility);
    EXPECT(context, game.play_tactic(PlayerId::Player0, facility, 0));
    // Countdown 2: tick on each own turn start.
    EXPECT(context, game.end_turn(PlayerId::Player0));
    EXPECT(context, game.end_turn(PlayerId::Player1));
    EXPECT(context, game.end_turn(PlayerId::Player0));
    EXPECT(context, game.end_turn(PlayerId::Player1));
    // After two of player 0's turn starts the relic expired: capacity grew by 1
    // beyond the two natural increments (4 → 7: two natural +1, one growth).
    EXPECT(context, game.player(PlayerId::Player0).pp_capacity == 7);
    EXPECT(context, game.player(PlayerId::Player0).cracks == 0);
    expect_valid_state(context, game);
}

// ---------------------------------------------------------------------------
// R7. Current PP above capacity (rules-v0.4 §17)
// ---------------------------------------------------------------------------
void test_current_pp_above_capacity(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].current_pp = 6;
    scenario.players[0].pp_capacity = 6;
    scenario.players[1].units = {cards::midrange::kGuardSentry};
    scenario.players[0].hand = {cards::advance::kBurnBlast, cards::midrange::kPioneerScout};
    Game game = scenario_game(scenario);

    const InstanceId blast = *game.find_in_hand(PlayerId::Player0, cards::advance::kBurnBlast);
    EXPECT(context, game.cast_spell(PlayerId::Player0, blast, first_enemy_unit_target(game, PlayerId::Player0)));
    // 6/6 → 5 current / 4 capacity: current PP legally exceeds capacity.
    EXPECT(context, game.player(PlayerId::Player0).current_pp == 5);
    EXPECT(context, game.player(PlayerId::Player0).pp_capacity == 4);
    EXPECT(context, game.player(PlayerId::Player0).current_pp > game.player(PlayerId::Player0).pp_capacity);

    // The remaining PP is still spendable this turn.
    const InstanceId scout = *game.find_in_hand(PlayerId::Player0, cards::midrange::kPioneerScout);
    EXPECT(context, game.play_unit(PlayerId::Player0, scout));
    EXPECT(context, game.player(PlayerId::Player0).current_pp == 4);

    // Next own turn refills to the new capacity (rules-v0.4 §17/§7.2).
    EXPECT(context, game.end_turn(PlayerId::Player0));
    EXPECT(context, game.end_turn(PlayerId::Player1));
    EXPECT(context, game.player(PlayerId::Player0).pp_capacity == 5);
    EXPECT(context, game.player(PlayerId::Player0).current_pp == 5);
    expect_valid_state(context, game);
}

// ---------------------------------------------------------------------------
// R8. Advanced/on-time status (rules-v0.4 §11)
// ---------------------------------------------------------------------------
void test_advanced_on_time_status(TestContext& context) {
    // 超前先锋 (4PP, OnPlayIfAdvanced: rush) played with advance on entry turn
    // may attack units immediately.
    Scenario scenario = base_scenario();
    scenario.players[0].current_pp = 3;
    scenario.players[0].pp_capacity = 3;
    scenario.players[0].hand = {cards::advance::kAdvanceWarrior};
    scenario.players[1].units = {cards::midrange::kGuardSentry};
    Game game = scenario_game(scenario);
    const InstanceId warrior = *game.find_in_hand(PlayerId::Player0, cards::advance::kAdvanceWarrior);
    EXPECT(context, game.play_unit(PlayerId::Player0, warrior, std::nullopt, std::nullopt, true));
    const InstanceId warrior_unit = *game.find_on_field(PlayerId::Player0, cards::advance::kAdvanceWarrior);
    const auto enemy = first_enemy_unit_target(game, PlayerId::Player0);
    // Rush lets it attack the enemy unit on its entry turn, but not the leader.
    EXPECT(context, game.attack(PlayerId::Player0, warrior_unit, *enemy));
    // 4/4 vs 1/3: the sentry dies; the warrior takes 1 (3/4).
    EXPECT(context, game.instance(enemy->unit).zone != Zone::Unit);
    EXPECT(context, game.instance(warrior_unit).current_health == 3);

    // 按期精英 (3PP, OnPlayIfNotAdvanced: draw 1): played on time it draws.
    Scenario scenario2 = base_scenario();
    scenario2.players[0].hand = {cards::advance::kOnTimeElite};
    scenario2.players[0].deck = {cards::midrange::kGuardSentry};
    Game game2 = scenario_game(scenario2);
    const int hand_before = static_cast<int>(game2.player(PlayerId::Player0).hand.size());
    const InstanceId elite = *game2.find_in_hand(PlayerId::Player0, cards::advance::kOnTimeElite);
    EXPECT(context, game2.play_unit(PlayerId::Player0, elite));
    EXPECT(context, static_cast<int>(game2.player(PlayerId::Player0).hand.size()) == hand_before); // played one, drew one

    // The same card played WITH advance must not draw (OnPlayIfNotAdvanced).
    Scenario scenario3 = base_scenario();
    scenario3.players[0].current_pp = 1;
    scenario3.players[0].pp_capacity = 3;
    scenario3.players[0].hand = {cards::advance::kOnTimeElite};
    Game game3 = scenario_game(scenario3);
    const InstanceId elite3 = *game3.find_in_hand(PlayerId::Player0, cards::advance::kOnTimeElite);
    EXPECT(context, game3.play_unit(PlayerId::Player0, elite3, std::nullopt, std::nullopt, true));
    EXPECT(context, game3.player(PlayerId::Player0).hand.empty());

    expect_valid_state(context, game);
    expect_valid_state(context, game2);
    expect_valid_state(context, game3);
}

// ---------------------------------------------------------------------------
// R9a. Evolution unlock, cost and limits (rules-v0.4 §22)
// ---------------------------------------------------------------------------
void test_evolution_unlock_and_cost(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].own_turn_number = 4; // first player, not yet unlocked
    scenario.players[0].evolution_points = 0;
    scenario.players[0].units = {cards::midrange::kEliteCommander};
    Game game = scenario_game(scenario);
    const InstanceId commander = *game.find_on_field(PlayerId::Player0, cards::midrange::kEliteCommander);
    EXPECT_CODE(context, game.evolve(PlayerId::Player0, commander), ErrorCode::EvolutionLocked);

    // Unlock at own turn 5 grants 2 energy to the first player.
    EXPECT(context, game.end_turn(PlayerId::Player0));
    EXPECT(context, game.end_turn(PlayerId::Player1));
    const PlayerState& state = game.player(PlayerId::Player0);
    EXPECT(context, state.own_turn_number == 5);
    EXPECT(context, state.evolution_points == 2);

    const InstanceId ready = *game.find_on_field(PlayerId::Player0, cards::midrange::kEliteCommander);
    // Costs 2 energy, once per turn.
    EXPECT(context, game.evolve(PlayerId::Player0, ready));
    EXPECT(context, game.player(PlayerId::Player0).evolution_points == 0);
    EXPECT_CODE(context, game.evolve(PlayerId::Player0, ready), ErrorCode::AlreadyEvolved);

    // Second player unlocks on own turn 4 with 3 energy.
    Scenario scenario2 = base_scenario(PlayerId::Player0);
    scenario2.players[1].own_turn_number = 3;
    scenario2.players[1].evolution_points = 0;
    scenario2.players[1].units = {cards::advance::kOnTimeElite};
    Game game2 = scenario_game(scenario2);
    EXPECT(context, game2.end_turn(PlayerId::Player0));
    EXPECT(context, game2.player(PlayerId::Player1).own_turn_number == 4);
    EXPECT(context, game2.player(PlayerId::Player1).evolution_points == 3);
    const InstanceId unit2 = *game2.find_on_field(PlayerId::Player1, cards::advance::kOnTimeElite);
    EXPECT(context, game2.evolve(PlayerId::Player1, unit2));
    EXPECT(context, game2.player(PlayerId::Player1).evolution_points == 1);

    expect_valid_state(context, game);
    expect_valid_state(context, game2);
}

// ---------------------------------------------------------------------------
// R9b. Evolution states and the "进化时" trigger (rules-v0.4 §22)
// ---------------------------------------------------------------------------
void test_evolution_states_and_trigger(TestContext& context) {
    // 精锐统帅 (5/5, no evolved stats): default +2/+2, no trigger.
    Scenario scenario = base_scenario();
    scenario.players[0].units = {cards::midrange::kEliteCommander};
    Game game = scenario_game(scenario);
    const InstanceId plain = *game.find_on_field(PlayerId::Player0, cards::midrange::kEliteCommander);
    EXPECT(context, game.evolve(PlayerId::Player0, plain));
    EXPECT(context, game.instance(plain).current_attack == 7);
    EXPECT(context, game.instance(plain).maximum_health == 7);
    // Evolution grants "may attack enemy units this turn" (temporary rush).
    EXPECT(context, game.instance(plain).temporary_rush);

    // 战场指挥者 (3/3 → 5/5, OnEvolution: Draw 1).
    Scenario scenario2 = base_scenario();
    scenario2.players[0].units = {cards::midrange::kFieldCommander};
    scenario2.players[0].deck = {cards::midrange::kGuardSentry};
    Game game2 = scenario_game(scenario2);
    const InstanceId commander = *game2.find_on_field(PlayerId::Player0, cards::midrange::kFieldCommander);
    const int hand_before = static_cast<int>(game2.player(PlayerId::Player0).hand.size());
    EXPECT(context, game2.evolve(PlayerId::Player0, commander));
    EXPECT(context, game2.instance(commander).current_attack == 5);
    EXPECT(context, game2.instance(commander).maximum_health == 5);
    EXPECT(context, static_cast<int>(game2.player(PlayerId::Player0).hand.size()) == hand_before + 1);

    expect_valid_state(context, game);
    expect_valid_state(context, game2);
}

// ---------------------------------------------------------------------------
// R9c. Class charge conditions (rules-v0.4 §23)
// ---------------------------------------------------------------------------
void test_charge_conditions(TestContext& context) {
    // Midrange deck (p0): the 2nd friendly death in one turn cycle grants 1 energy.
    // Both deaths happen during player 1's turn, inside one of p0's cycles.
    Scenario scenario = base_scenario();
    scenario.players[0].evolution_points = 1;
    scenario.players[0].units = {
        cards::midrange::kGuardSentry,  // 1/3 guard
        cards::midrange::kGuardSentry,  // 1/3 guard
        cards::midrange::kPioneerScout, // 1/2
    };
    scenario.players[1].units = {
        cards::midrange::kAssaultVanguard, // 3/1 attacker
        cards::midrange::kAssaultVanguard, // 3/1 attacker
    };
    Game game = scenario_game(scenario);

    EXPECT(context, game.end_turn(PlayerId::Player0));
    // Player 1 attacks both p0 guards with its 3/1 vanguards: each sentry dies.
    const InstanceId sentry_a = *game.find_on_field(PlayerId::Player0, cards::midrange::kGuardSentry);
    const auto attacker_1 = first_enemy_unit_target(game, PlayerId::Player0);
    EXPECT(context, game.attack(PlayerId::Player1, attacker_1->unit, Target::unit_target(PlayerId::Player0, sentry_a)));
    EXPECT(context, game.player(PlayerId::Player0).evolution_points == 1); // 1st death, no grant yet
    const InstanceId sentry_b = *game.find_on_field(PlayerId::Player0, cards::midrange::kGuardSentry);
    const auto attacker_2 = first_enemy_unit_target(game, PlayerId::Player0);
    EXPECT(context, game.attack(PlayerId::Player1, attacker_2->unit, Target::unit_target(PlayerId::Player0, sentry_b)));
    // 2nd friendly death in the cycle → +1 energy.
    EXPECT(context, game.player(PlayerId::Player0).evolution_points == 2);

    // Advance deck: SpellsNoUnitsThisTurn — ≥2 spells and no unit played at own
    // end of turn grants 1 energy.
    Game fresh(make_v04_catalog(), make_advance_deck(), make_midrange_deck());
    Scenario s2 = base_scenario();
    s2.players[0].evolution_points = 0;
    s2.players[1].units = {cards::midrange::kGuardSentry, cards::midrange::kGuardSentry};
    s2.players[0].hand = {cards::midrange::kPrecisionStrike, cards::midrange::kPrecisionStrike};
    const Status load = fresh.load_scenario(s2);
    EXPECT(context, load);
    const auto t1 = first_enemy_unit_target(fresh, PlayerId::Player0);
    const InstanceId s_a = *fresh.find_in_hand(PlayerId::Player0, cards::midrange::kPrecisionStrike);
    EXPECT(context, fresh.cast_spell(PlayerId::Player0, s_a, t1));
    const InstanceId s_b = *fresh.find_in_hand(PlayerId::Player0, cards::midrange::kPrecisionStrike);
    const auto t2 = first_enemy_unit_target(fresh, PlayerId::Player0);
    EXPECT(context, fresh.cast_spell(PlayerId::Player0, s_b, t2));
    EXPECT(context, fresh.player(PlayerId::Player0).evolution_points == 0); // before end of turn
    EXPECT(context, fresh.end_turn(PlayerId::Player0));
    EXPECT(context, fresh.player(PlayerId::Player0).evolution_points == 1);
    expect_valid_state(context, fresh);

    expect_valid_state(context, game);
}

void test_charge_requires_evolution_unlock(TestContext& context) {
    // The death threshold may be met before unlock, but cannot grant energy.
    Scenario deaths = base_scenario(PlayerId::Player1);
    deaths.players[0].own_turn_number = 2;
    deaths.players[0].evolution_points = 0;
    deaths.players[0].units = {
        cards::midrange::kGuardSentry,
        cards::midrange::kGuardSentry,
    };
    deaths.players[1].units = {
        cards::midrange::kAssaultVanguard,
        cards::midrange::kAssaultVanguard,
    };
    Game death_game = scenario_game(deaths);
    for (int i = 0; i < 2; ++i) {
        const InstanceId guard = *death_game.find_on_field(PlayerId::Player0, cards::midrange::kGuardSentry);
        const InstanceId attacker = *death_game.find_on_field(PlayerId::Player1, cards::midrange::kAssaultVanguard);
        EXPECT(context, death_game.attack(
            PlayerId::Player1, attacker, Target::unit_target(PlayerId::Player0, guard)));
    }
    EXPECT(context, death_game.player(PlayerId::Player0).evolution_points == 0);

    // The spell/no-unit threshold is likewise inert before unlock.
    Scenario spells = base_scenario();
    spells.players[0].own_turn_number = 2;
    spells.players[0].evolution_points = 0;
    spells.players[0].hand = {
        cards::midrange::kPrecisionStrike,
        cards::midrange::kPrecisionStrike,
    };
    spells.players[1].units = {
        cards::midrange::kGuardSentry,
        cards::midrange::kGuardSentry,
    };
    Game spell_game(make_v04_catalog(), make_advance_deck(), make_midrange_deck(), deterministic_test_config());
    EXPECT(context, spell_game.load_scenario(spells));
    for (int i = 0; i < 2; ++i) {
        const InstanceId spell = *spell_game.find_in_hand(PlayerId::Player0, cards::midrange::kPrecisionStrike);
        const auto target = first_enemy_unit_target(spell_game, PlayerId::Player0);
        EXPECT(context, spell_game.cast_spell(PlayerId::Player0, spell, target));
    }
    EXPECT(context, spell_game.end_turn(PlayerId::Player0));
    EXPECT(context, spell_game.player(PlayerId::Player0).evolution_points == 0);

    // Once unlocked, charging remains capped at four.
    Scenario capped = base_scenario(PlayerId::Player1);
    capped.players[0].evolution_points = 4;
    capped.players[0].units = {
        cards::midrange::kGuardSentry,
        cards::midrange::kGuardSentry,
    };
    capped.players[1].units = {
        cards::midrange::kAssaultVanguard,
        cards::midrange::kAssaultVanguard,
    };
    Game capped_game = scenario_game(capped);
    for (int i = 0; i < 2; ++i) {
        const InstanceId guard = *capped_game.find_on_field(PlayerId::Player0, cards::midrange::kGuardSentry);
        const InstanceId attacker = *capped_game.find_on_field(PlayerId::Player1, cards::midrange::kAssaultVanguard);
        EXPECT(context, capped_game.attack(
            PlayerId::Player1, attacker, Target::unit_target(PlayerId::Player0, guard)));
    }
    EXPECT(context, capped_game.player(PlayerId::Player0).evolution_points == 4);
    expect_valid_state(context, death_game);
    expect_valid_state(context, spell_game);
    expect_valid_state(context, capped_game);
}

// ---------------------------------------------------------------------------
// R10. Standby deployment (rules-v0.4 §24/§25)
// ---------------------------------------------------------------------------
void test_deployment_flow_and_limits(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].current_pp = 5;
    scenario.players[0].pp_capacity = 5;
    scenario.players[0].standby = {cards::midrange::kSiegeTitan, cards::midrange::kGuardAce};
    scenario.players[0].units = {cards::midrange::kGuardSentry};
    Game game = scenario_game(scenario);

    const InstanceId titan = *game.find_in_standby(PlayerId::Player0, cards::midrange::kSiegeTitan);
    // Condition not met (needs ≥2 friendly units).
    EXPECT_CODE(context, game.deploy(PlayerId::Player0, titan), ErrorCode::DeployConditionNotMet);

    Scenario scenario2 = base_scenario();
    scenario2.players[0].current_pp = 5;
    scenario2.players[0].pp_capacity = 5;
    scenario2.players[0].standby = {cards::midrange::kSiegeTitan, cards::midrange::kGuardAce};
    scenario2.players[0].units = {cards::midrange::kGuardSentry, cards::midrange::kPioneerScout};
    Game game2 = scenario_game(scenario2);
    const InstanceId titan2 = *game2.find_in_standby(PlayerId::Player0, cards::midrange::kSiegeTitan);
    EXPECT(context, game2.deploy(PlayerId::Player0, titan2));
    EXPECT(context, game2.player(PlayerId::Player0).current_pp == 2);
    EXPECT(context, game2.find_on_field(PlayerId::Player0, cards::midrange::kSiegeTitan).has_value());
    // Once per turn (rules-v0.4 §25).
    const InstanceId ace = *game2.find_in_standby(PlayerId::Player0, cards::midrange::kGuardAce);
    EXPECT_CODE(context, game2.deploy(PlayerId::Player0, ace), ErrorCode::DeployAlreadyUsed);

    // A deployed unit leaving the field goes to the archive (rules-v0.4 §5).
    Scenario scenario4 = base_scenario();
    scenario4.players[0].current_pp = 5;
    scenario4.players[0].pp_capacity = 5;
    scenario4.players[0].standby = {cards::midrange::kSiegeTitan};
    scenario4.players[0].units = {cards::midrange::kPioneerScout, cards::midrange::kPioneerScout}; // no guard
    scenario4.players[1].units = {cards::advance::kDebtLord}; // 8/6 attacker
    Game game4 = scenario_game(scenario4);
    const InstanceId titan4 = *game4.find_in_standby(PlayerId::Player0, cards::midrange::kSiegeTitan);
    EXPECT(context, game4.deploy(PlayerId::Player0, titan4));
    const InstanceId titan_unit4 = *game4.find_on_field(PlayerId::Player0, cards::midrange::kSiegeTitan);
    EXPECT(context, game4.end_turn(PlayerId::Player0));
    const auto enemy_attacker = first_enemy_unit_target(game4, PlayerId::Player0); // p1's unit
    EXPECT(context, game4.attack(PlayerId::Player1, enemy_attacker->unit,
                                 Target::unit_target(PlayerId::Player0, titan_unit4)));
    // 8 damage kills the 5/5 titan → archived, not graveyard.
    EXPECT(context, game4.find_on_field(PlayerId::Player0, cards::midrange::kSiegeTitan) == std::nullopt);
    EXPECT(context, game4.player(PlayerId::Player0).graveyard.size() == 0);
    EXPECT(context, !game4.player(PlayerId::Player0).archive.empty());

    expect_valid_state(context, game);
    expect_valid_state(context, game2);
    expect_valid_state(context, game4);
}

void test_deploy_into_archived_donor_slot(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].standby = {cards::midrange::kGuardAce};
    scenario.players[0].units = {cards::midrange::kAssaultVanguard};
    Game game = scenario_game(scenario);
    const InstanceId ace = *game.find_in_standby(PlayerId::Player0, cards::midrange::kGuardAce);
    const InstanceId donor = *game.find_on_field(PlayerId::Player0, cards::midrange::kAssaultVanguard);

    EXPECT(context, game.deploy(PlayerId::Player0, ace, 0, donor));
    EXPECT(context, game.player(PlayerId::Player0).units[0] == ace);
    EXPECT(context, game.instance(donor).zone == Zone::Archive);
    EXPECT(context, game.instance(ace).sequence == 0);
    expect_valid_state(context, game);
}

// ---------------------------------------------------------------------------
// R11. Component abilities (rules-v0.4 §31)
// ---------------------------------------------------------------------------
void test_component_grant_and_no_retransfer(TestContext& context) {
    // 戍卫王机 deployment archives a friendly unit; if that unit carries a
    // printed component (突击前锋: GrantRush), the deployed unit gets it.
    Scenario scenario = base_scenario();
    scenario.players[0].current_pp = 6;
    scenario.players[0].pp_capacity = 6;
    scenario.players[0].standby = {cards::midrange::kGuardAce};
    scenario.players[0].units = {cards::midrange::kAssaultVanguard}; // has component
    scenario.players[1].units = {cards::midrange::kGuardSentry};
    Game game = scenario_game(scenario);

    const InstanceId ace = *game.find_in_standby(PlayerId::Player0, cards::midrange::kGuardAce);
    const InstanceId donor = *game.find_on_field(PlayerId::Player0, cards::midrange::kAssaultVanguard);
    EXPECT(context, game.deploy(PlayerId::Player0, ace, std::nullopt, donor));
    const InstanceId ace_unit = *game.find_on_field(PlayerId::Player0, cards::midrange::kGuardAce);
    EXPECT(context, game.instance(ace_unit).granted_component.has_component);
    EXPECT(context, game.instance(ace_unit).granted_component.granted_kind == EffectKind::GrantRush);
    // The component lets the deployed ace attack enemy units on its entry turn.
    const auto enemy = first_enemy_unit_target(game, PlayerId::Player0);
    EXPECT(context, game.attack(PlayerId::Player0, ace_unit, *enemy));
    // Donor went to the archive (deployment cost), not the graveyard.
    EXPECT(context, game.player(PlayerId::Player0).graveyard.empty());
    EXPECT(context, game.player(PlayerId::Player0).archive.size() == 1);

    // Deployed standby cards leave to the archive; a granted component never
    // survives leaving the field (rules-v0.4 §31).
    Scenario scenario3 = base_scenario();
    scenario3.players[0].current_pp = 6;
    scenario3.players[0].pp_capacity = 6;
    scenario3.players[0].standby = {cards::midrange::kGuardAce};
    scenario3.players[0].units = {cards::midrange::kAssaultVanguard};
    scenario3.players[1].units = {cards::advance::kDebtLord}; // 8/6 attacker
    Game game3 = scenario_game(scenario3);
    const InstanceId ace3 = *game3.find_in_standby(PlayerId::Player0, cards::midrange::kGuardAce);
    const InstanceId donor3 = *game3.find_on_field(PlayerId::Player0, cards::midrange::kAssaultVanguard);
    EXPECT(context, game3.deploy(PlayerId::Player0, ace3, std::nullopt, donor3));
    const InstanceId ace_unit3 = *game3.find_on_field(PlayerId::Player0, cards::midrange::kGuardAce);
    EXPECT(context, game3.end_turn(PlayerId::Player0));
    const auto enemy3 = first_enemy_unit_target(game3, PlayerId::Player0); // p1's 8/6 attacker
    EXPECT(context, game3.attack(PlayerId::Player1, enemy3->unit,
                                 Target::unit_target(PlayerId::Player0, ace_unit3)));
    // Deployed ace died → archive; the granted component must not linger.
    EXPECT(context, game3.player(PlayerId::Player0).graveyard.empty());
    expect_valid_state(context, game);
    expect_valid_state(context, game3);
}

// ---------------------------------------------------------------------------
// R12a. Response stack: trap cancels attack (rules-v0.4 §26)
// ---------------------------------------------------------------------------
void test_response_stack_lifo(TestContext& context) {
    // Player 0 declares an attack; player 1 responds with a cancel trap; the
    // chain resolves: attack cancelled, no combat damage, attacker still
    // considered to have attacked (rules-v0.4 §21/§26).
    Scenario scenario = base_scenario();
    scenario.players[0].current_pp = 6;
    scenario.players[0].pp_capacity = 6;
    scenario.players[0].units = {cards::midrange::kEliteCommander}; // 5/5
    scenario.players[1].units = {cards::midrange::kGuardSentry};    // 1/3
    scenario.players[1].tactics = {cards::midrange::kInterceptTrap};
    Game game = scenario_game(scenario);

    const InstanceId attacker = *game.find_on_field(PlayerId::Player0, cards::midrange::kEliteCommander);
    const auto target = first_enemy_unit_target(game, PlayerId::Player0);
    EXPECT(context, game.attack(PlayerId::Player0, attacker, *target));
    // A response window is open for player 1.
    EXPECT(context, game.phase() == Phase::Reaction);
    EXPECT(context, game.reaction_window() == ReactionWindow::AttackDeclared);
    EXPECT(context, !game.eligible_traps().empty());

    // Player 1 activates the cancel trap → no counter available → chain resolves.
    const InstanceId trap = game.eligible_traps().front();
    EXPECT(context, game.activate_trap(PlayerId::Player1, trap));
    // Attack cancelled: no combat damage, attacker still marked as attacked.
    EXPECT(context, game.instance(target->unit).current_health == 3);
    EXPECT(context, game.instance(attacker).attacked_this_turn);
    EXPECT(context, game.phase() == Phase::Action);
    // Trap resolved into the graveyard (rules-v0.4 §20).
    EXPECT(context, game.player(PlayerId::Player1).graveyard.size() == 1);

    // A spell use also opens a window; no matching trap → resolves immediately.
    Scenario scenario2 = base_scenario();
    scenario2.players[0].hand = {cards::midrange::kPrecisionStrike};
    scenario2.players[1].units = {cards::midrange::kGuardSentry};
    scenario2.players[1].tactics = {cards::midrange::kInterceptTrap}; // not matching
    Game game2 = scenario_game(scenario2);
    const InstanceId strike = *game2.find_in_hand(PlayerId::Player0, cards::midrange::kPrecisionStrike);
    const auto enemy2 = first_enemy_unit_target(game2, PlayerId::Player0);
    EXPECT(context, game2.cast_spell(PlayerId::Player0, strike, enemy2));
    // The 3 damage killed the 1/3 sentry: it left the field.
    EXPECT(context, game2.instance(enemy2->unit).zone != Zone::Unit);

    expect_valid_state(context, game);
    expect_valid_state(context, game2);
}

void test_spell_response_three_level_lifo_and_counter_pass(TestContext& context) {
    constexpr CardId kTestSpell = 9001;
    constexpr CardId kTestSpellTrap = 9002;
    CardCatalog catalog = make_v04_catalog();

    CardDefinition spell;
    spell.id = kTestSpell;
    spell.name = "test spell";
    spell.kind = CardKind::Spell;
    spell.effects.push_back(
        EffectRecord{EffectTrigger::OnPlay, EffectKind::DealDamageToLeader, 2, TargetSpec::None});
    catalog.add(spell);

    CardDefinition trap;
    trap.id = kTestSpellTrap;
    trap.name = "spell response trap";
    trap.kind = CardKind::Trap;
    trap.effects.push_back(
        EffectRecord{EffectTrigger::OnSpellDeclared, EffectKind::DealDamageToLeader, 1, TargetSpec::None});
    catalog.add(trap);

    Scenario scenario = base_scenario();
    scenario.players[0].hand = {kTestSpell};
    scenario.players[0].tactics = {kTestSpellTrap};
    scenario.players[1].tactics = {kTestSpellTrap};
    Game game = scenario_game_with_catalog(catalog, scenario);
    const InstanceId spell_id = *game.find_in_hand(PlayerId::Player0, kTestSpell);
    const InstanceId counter_trap = *game.player(PlayerId::Player0).tactics[0];
    const InstanceId response_trap = *game.player(PlayerId::Player1).tactics[0];
    (void)game.drain_events();

    EXPECT(context, game.cast_spell(PlayerId::Player0, spell_id));
    EXPECT(context, game.reaction_window() == ReactionWindow::SpellDeclared);
    EXPECT(context, game.response_depth() == 1);
    EXPECT(context, game.activate_trap(PlayerId::Player1, response_trap));
    EXPECT(context, game.response_depth() == 2);
    EXPECT(context, game.activate_trap(PlayerId::Player0, counter_trap));
    EXPECT(context, game.phase() == Phase::Action);
    EXPECT(context, game.player(PlayerId::Player0).leader_health == 24);
    EXPECT(context, game.player(PlayerId::Player1).leader_health == 22);

    const std::vector<GameEvent> events = game.drain_events();
    std::vector<InstanceId> trap_order;
    std::vector<PlayerId> damaged_order;
    std::vector<int> damage_amounts;
    for (const GameEvent& event : events) {
        if (event.type == EventType::TrapActivated) {
            trap_order.push_back(event.card);
        } else if (event.type == EventType::LeaderDamaged) {
            damaged_order.push_back(event.player);
            damage_amounts.push_back(event.value);
        }
    }
    const std::vector<InstanceId> expected_traps = {counter_trap, response_trap};
    const std::vector<PlayerId> expected_players = {PlayerId::Player1, PlayerId::Player0, PlayerId::Player1};
    const std::vector<int> expected_damage = {1, 1, 2};
    EXPECT(context, trap_order == expected_traps);
    EXPECT(context, damaged_order == expected_players);
    EXPECT(context, damage_amounts == expected_damage);

    // Passing the counter layer must still resolve the first response and then
    // the original spell; it must not discard the base layer.
    Scenario pass_scenario = base_scenario();
    pass_scenario.players[0].hand = {kTestSpell};
    pass_scenario.players[0].tactics = {kTestSpellTrap};
    pass_scenario.players[1].tactics = {kTestSpellTrap};
    Game pass_game = scenario_game_with_catalog(std::move(catalog), pass_scenario);
    const InstanceId pass_spell = *pass_game.find_in_hand(PlayerId::Player0, kTestSpell);
    const InstanceId pass_response = *pass_game.player(PlayerId::Player1).tactics[0];
    EXPECT(context, pass_game.cast_spell(PlayerId::Player0, pass_spell));
    EXPECT(context, pass_game.activate_trap(PlayerId::Player1, pass_response));
    EXPECT(context, pass_game.response_depth() == 2);
    EXPECT(context, pass_game.pass_reaction(PlayerId::Player0));
    EXPECT(context, pass_game.phase() == Phase::Action);
    EXPECT(context, pass_game.player(PlayerId::Player0).leader_health == 24);
    EXPECT(context, pass_game.player(PlayerId::Player1).leader_health == 23);
    EXPECT(context, pass_game.instance(pass_response).zone == Zone::Graveyard);
    expect_valid_state(context, game);
    expect_valid_state(context, pass_game);
}

void test_response_target_invalidation_continues_effects(TestContext& context) {
    constexpr CardId kMultiEffectSpell = 9011;
    constexpr CardId kInvalidateTrap = 9012;
    constexpr CardId kConditionalSpell = 9013;
    CardCatalog catalog = make_v04_catalog();

    CardDefinition spell;
    spell.id = kMultiEffectSpell;
    spell.name = "target then heal";
    spell.kind = CardKind::Spell;
    spell.cost = 2;
    spell.effects.push_back(
        EffectRecord{EffectTrigger::OnPlay, EffectKind::DealDamageToEnemyUnit, 3, TargetSpec::EnemyUnit});
    spell.effects.push_back(
        EffectRecord{EffectTrigger::OnPlay, EffectKind::HealLeader, 4, TargetSpec::None});
    catalog.add(spell);

    CardDefinition trap;
    trap.id = kInvalidateTrap;
    trap.name = "invalidate spell target";
    trap.kind = CardKind::Trap;
    trap.effects.push_back(
        EffectRecord{EffectTrigger::OnSpellDeclared, EffectKind::DamageEnteredUnit, 3, TargetSpec::None});
    catalog.add(trap);

    CardDefinition conditional;
    conditional.id = kConditionalSpell;
    conditional.name = "conditional targeted spell";
    conditional.kind = CardKind::Spell;
    conditional.cost = 2;
    conditional.effects.push_back(EffectRecord{
        EffectTrigger::OnPlayIfNotAdvanced, EffectKind::DealDamageToEnemyUnit, 1, TargetSpec::EnemyUnit});
    catalog.add(conditional);

    Scenario invalid_declaration = base_scenario();
    invalid_declaration.players[0].hand = {kConditionalSpell};
    Game validation_game = scenario_game_with_catalog(catalog, invalid_declaration);
    const InstanceId conditional_id = *validation_game.find_in_hand(PlayerId::Player0, kConditionalSpell);
    const int pp_before = validation_game.player(PlayerId::Player0).current_pp;
    EXPECT_CODE(context, validation_game.cast_spell(PlayerId::Player0, conditional_id), ErrorCode::InvalidTarget);
    EXPECT(context, validation_game.player(PlayerId::Player0).current_pp == pp_before);
    EXPECT(context, validation_game.instance(conditional_id).zone == Zone::Hand);

    Scenario scenario = base_scenario();
    scenario.players[0].leader_health = 10;
    scenario.players[0].hand = {kMultiEffectSpell};
    scenario.players[1].units = {cards::midrange::kGuardSentry};
    scenario.players[1].tactics = {kInvalidateTrap};
    Game game = scenario_game_with_catalog(std::move(catalog), scenario);
    const InstanceId spell_id = *game.find_in_hand(PlayerId::Player0, kMultiEffectSpell);
    const InstanceId target_id = *game.find_on_field(PlayerId::Player1, cards::midrange::kGuardSentry);
    const InstanceId trap_id = *game.player(PlayerId::Player1).tactics[0];
    const int paid_from = game.player(PlayerId::Player0).current_pp;
    EXPECT(context, game.cast_spell(
        PlayerId::Player0, spell_id, Target::unit_target(PlayerId::Player1, target_id)));
    EXPECT(context, game.activate_trap(PlayerId::Player1, trap_id));
    EXPECT(context, game.instance(target_id).zone == Zone::Graveyard);
    EXPECT(context, game.instance(spell_id).zone == Zone::Graveyard);
    EXPECT(context, game.player(PlayerId::Player0).current_pp == paid_from - 2);
    EXPECT(context, game.player(PlayerId::Player0).leader_health == 14);
    EXPECT(context, game.phase() == Phase::Action);
    expect_valid_state(context, validation_game);
    expect_valid_state(context, game);
}

// ---------------------------------------------------------------------------
// R12b. Trap on entry-effect pending (rules-v0.4 §26)
// ---------------------------------------------------------------------------
void test_trap_entry_pending_damage(TestContext& context) {
    // p0 plays 先驱侦察兵 (OnEntry draw) while p1 has 反制伏策 → the window
    // opens before the entry effect; the trap damages the entering unit.
    Scenario scenario = base_scenario();
    scenario.players[0].hand = {cards::midrange::kPioneerScout};
    scenario.players[0].deck = {cards::midrange::kGuardSentry};
    scenario.players[1].tactics = {cards::midrange::kCounterTrap};
    Game game = scenario_game(scenario);

    const InstanceId scout = *game.find_in_hand(PlayerId::Player0, cards::midrange::kPioneerScout);
    EXPECT(context, game.play_unit(PlayerId::Player0, scout));
    EXPECT(context, game.phase() == Phase::Reaction);
    EXPECT(context, game.reaction_window() == ReactionWindow::EntryEffectPending);
    const InstanceId trap = game.eligible_traps().front();
    EXPECT(context, game.activate_trap(PlayerId::Player1, trap));
    // The 1/2 scout took 2 damage and died; its draw still resolves (LIFO).
    EXPECT(context, game.find_on_field(PlayerId::Player0, cards::midrange::kPioneerScout) == std::nullopt);
    EXPECT(context, game.player(PlayerId::Player0).graveyard.size() == 1);
    EXPECT(context, game.player(PlayerId::Player0).hand.size() == 1);
    expect_valid_state(context, game);
}

// ---------------------------------------------------------------------------
// R13. Tactic zone never auto-replaces (rules-v0.4 §5)
// ---------------------------------------------------------------------------
void test_tactic_zone_no_replacement(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].hand = {
        cards::advance::kGrowthFacility,
        cards::midrange::kCommandOrder,
        cards::midrange::kInterceptTrap,
    };
    scenario.players[0].tactics = {cards::midrange::kCommandOrder}; // slot 0 occupied
    Game game = scenario_game(scenario);

    const InstanceId facility = *game.find_in_hand(PlayerId::Player0, cards::advance::kGrowthFacility);
    // Slot 0 occupied → rejected; no free replacement (rules-v0.4 §5).
    EXPECT_CODE(context, game.play_tactic(PlayerId::Player0, facility, 0), ErrorCode::TacticZoneFull);
    // Slot 1 is free → accepted.
    EXPECT(context, game.play_tactic(PlayerId::Player0, facility, 1));
    // Slot 2 is free → accepted (v0.4: 3 tactic slots per player).
    const InstanceId trap = *game.find_in_hand(PlayerId::Player0, cards::midrange::kInterceptTrap);
    EXPECT(context, game.play_tactic(PlayerId::Player0, trap, 2));
    // All three slots now full: nothing can be placed anywhere.
    const InstanceId order = *game.find_in_hand(PlayerId::Player0, cards::midrange::kCommandOrder);
    EXPECT_CODE(context, game.play_tactic(PlayerId::Player0, order, 0), ErrorCode::TacticZoneFull);
    EXPECT_CODE(context, game.play_tactic(PlayerId::Player0, order, 1), ErrorCode::TacticZoneFull);
    EXPECT_CODE(context, game.play_tactic(PlayerId::Player0, order, 2), ErrorCode::TacticZoneFull);
    expect_valid_state(context, game);
}

// ---------------------------------------------------------------------------
// R14. Hand overflow archive and fatigue (rules-v0.4 §5/§33)
// ---------------------------------------------------------------------------
void test_hand_overflow_and_fatigue(TestContext& context) {
    Scenario scenario = base_scenario();
    for (int i = 0; i < 9; ++i) {
        scenario.players[0].hand.push_back(cards::midrange::kGuardSentry);
    }
    scenario.players[0].deck = {cards::midrange::kPioneerScout};
    Game game = scenario_game(scenario);
    EXPECT(context, game.player(PlayerId::Player0).hand.size() == 9);
    // Drawing with a full hand archives the card publicly (rules-v0.4 §5).
    EXPECT(context, game.end_turn(PlayerId::Player0));
    EXPECT(context, game.end_turn(PlayerId::Player1));
    EXPECT(context, game.player(PlayerId::Player0).hand.size() == 9);
    EXPECT(context, !game.player(PlayerId::Player0).archive.empty());

    // Fatigue: empty deck, each draw deals escalating damage (rules-v0.4 §33).
    Scenario scenario2 = base_scenario();
    scenario2.players[0].deck = {};
    Game game2 = scenario_game(scenario2);
    const int health_before = game2.player(PlayerId::Player0).leader_health;
    EXPECT(context, game2.end_turn(PlayerId::Player0));
    EXPECT(context, game2.end_turn(PlayerId::Player1));
    EXPECT(context, game2.player(PlayerId::Player0).leader_health == health_before - 1);
    EXPECT(context, game2.end_turn(PlayerId::Player0));
    EXPECT(context, game2.end_turn(PlayerId::Player1));
    EXPECT(context, game2.player(PlayerId::Player0).leader_health == health_before - 3);

    expect_valid_state(context, game);
    expect_valid_state(context, game2);
}

// ---------------------------------------------------------------------------
// R15. Combat: simultaneous damage, persistence, guard, sickness (rules-v0.4 §21)
// ---------------------------------------------------------------------------
void test_combat_and_attack_rules(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].units = {cards::midrange::kEliteCommander}; // 5/5, on field since load
    scenario.players[1].units = {cards::midrange::kFortressGuard, cards::midrange::kAssaultVanguard}; // 3/6 guard+barrier, 3/1
    Game game = scenario_game(scenario);

    const InstanceId attacker = *game.find_on_field(PlayerId::Player0, cards::midrange::kEliteCommander);
    // Guard blocks attacking the non-guard unit.
    const auto non_guard = *game.find_on_field(PlayerId::Player1, cards::midrange::kAssaultVanguard);
    EXPECT_CODE(context, game.attack(PlayerId::Player0, attacker, Target::unit_target(PlayerId::Player1, non_guard)),
                ErrorCode::GuardBlocksTarget);
    // Attacking the guard: barrier absorbs the first hit, simultaneous damage.
    const auto guard = *game.find_on_field(PlayerId::Player1, cards::midrange::kFortressGuard);
    EXPECT(context, game.attack(PlayerId::Player0, attacker, Target::unit_target(PlayerId::Player1, guard)));
    // Barrier absorbed the 5 damage: guard stays 3/6 (barrier gone), attacker takes 3.
    EXPECT(context, game.instance(guard).current_health == 6);
    EXPECT(context, game.instance(attacker).current_health == 2);
    EXPECT(context, !has_keyword(game.instance(guard).keywords, Keyword::Barrier));
    // The attacker already attacked this turn: a second attack is refused.
    EXPECT_CODE(context, game.attack(PlayerId::Player0, attacker, Target::unit_target(PlayerId::Player1, guard)),
                ErrorCode::AlreadyAttacked);

    // Persistent damage: attacker keeps 2 health at end of turn.
    EXPECT(context, game.end_turn(PlayerId::Player0));
    EXPECT(context, game.end_turn(PlayerId::Player1));
    EXPECT(context, game.instance(attacker).current_health == 2);

    // Summoning sickness: a fresh unit cannot attack; rush units can attack
    // units but not the leader (rules-v0.4 §21).
    Scenario scenario2 = base_scenario();
    scenario2.players[0].hand = {cards::midrange::kAssaultVanguard, cards::midrange::kPioneerScout};
    scenario2.players[1].units = {cards::midrange::kGuardSentry};
    Game game2 = scenario_game(scenario2);
    const InstanceId fresh = *game2.find_in_hand(PlayerId::Player0, cards::midrange::kPioneerScout);
    EXPECT(context, game2.play_unit(PlayerId::Player0, fresh));
    const auto enemy2 = first_enemy_unit_target(game2, PlayerId::Player0);
    EXPECT_CODE(context, game2.attack(PlayerId::Player0, fresh, *enemy2), ErrorCode::SummoningSickness);
    const InstanceId rush = *game2.find_in_hand(PlayerId::Player0, cards::midrange::kAssaultVanguard);
    EXPECT(context, game2.play_unit(PlayerId::Player0, rush));
    const auto enemy3 = first_enemy_unit_target(game2, PlayerId::Player0);
    EXPECT(context, game2.attack(PlayerId::Player0, rush, *enemy3)); // rush → unit OK
    // 3/1 vs 1/3: both die simultaneously (rush takes the sentry's 1 damage).
    EXPECT(context, game2.find_on_field(PlayerId::Player0, cards::midrange::kAssaultVanguard) == std::nullopt);
    EXPECT(context, game2.instance(enemy3->unit).zone != Zone::Unit);

    expect_valid_state(context, game);
    expect_valid_state(context, game2);
}

// ---------------------------------------------------------------------------
// R16. Simultaneous death batch (rules-v0.4 §28)
// ---------------------------------------------------------------------------
void test_simultaneous_death_batch(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].units = {cards::midrange::kAssaultVanguard}; // 3/1
    scenario.players[1].units = {cards::midrange::kAssaultVanguard}; // 3/1
    Game game = scenario_game(scenario);
    const InstanceId attacker = *game.find_on_field(PlayerId::Player0, cards::midrange::kAssaultVanguard);
    const auto defender = *game.find_on_field(PlayerId::Player1, cards::midrange::kAssaultVanguard);
    EXPECT(context, game.attack(PlayerId::Player0, attacker, Target::unit_target(PlayerId::Player1, defender)));
    // Both die simultaneously; both enter the graveyard.
    EXPECT(context, game.find_on_field(PlayerId::Player0, cards::midrange::kAssaultVanguard) == std::nullopt);
    EXPECT(context, game.find_on_field(PlayerId::Player1, cards::midrange::kAssaultVanguard) == std::nullopt);
    EXPECT(context, game.player(PlayerId::Player0).graveyard.size() == 1);
    EXPECT(context, game.player(PlayerId::Player1).graveyard.size() == 1);
    expect_valid_state(context, game);
}

// ---------------------------------------------------------------------------
// R17. Trap set limits (rules-v0.4 §20)
// ---------------------------------------------------------------------------
void test_trap_set_once_per_turn(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].hand = {cards::midrange::kInterceptTrap, cards::midrange::kCounterTrap};
    Game game = scenario_game(scenario);
    const InstanceId trap_a = *game.find_in_hand(PlayerId::Player0, cards::midrange::kInterceptTrap);
    EXPECT(context, game.play_tactic(PlayerId::Player0, trap_a, 0));
    const InstanceId trap_b = *game.find_in_hand(PlayerId::Player0, cards::midrange::kCounterTrap);
    EXPECT_CODE(context, game.play_tactic(PlayerId::Player0, trap_b, 1), ErrorCode::TrapAlreadySetThisTurn);
    // The set trap is face-down.
    EXPECT(context, game.instance(trap_a).face_down);
    expect_valid_state(context, game);
}

// ---------------------------------------------------------------------------
// R18. Documented golden walkthrough (rules-v0.4 §9/§13/§15/§17)
// ---------------------------------------------------------------------------
void test_documented_overdraw_walkthrough(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].current_pp = 5;
    scenario.players[0].pp_capacity = 5;
    scenario.players[0].evolution_points = 2;
    scenario.players[0].own_turn_number = 5;
    scenario.players[0].hand = {cards::advance::kDebtLord, cards::advance::kBurnBlast, cards::advance::kRepairTechnician};
    scenario.players[0].deck = {cards::midrange::kGuardSentry, cards::midrange::kGuardSentry};
    scenario.players[1].own_turn_number = 1;
    scenario.players[1].units = {cards::midrange::kGuardSentry};
    scenario.players[1].deck = {cards::advance::kOnTimeElite, cards::advance::kOnTimeElite};
    Game game = scenario_game(scenario);

    const InstanceId debt_lord = *game.find_in_hand(PlayerId::Player0, cards::advance::kDebtLord);
    const InstanceId enemy = *game.find_on_field(PlayerId::Player1, cards::midrange::kGuardSentry);
    EXPECT(context, game.play_unit(PlayerId::Player0, debt_lord, std::nullopt, std::nullopt, true));
    EXPECT(context, game.player(PlayerId::Player0).current_pp == 0);
    EXPECT(context, game.player(PlayerId::Player0).pp_capacity == 2);
    EXPECT(context, game.player(PlayerId::Player0).cracks == 3);

    EXPECT(context, game.end_turn(PlayerId::Player0));
    EXPECT(context, game.end_turn(PlayerId::Player1));
    EXPECT(context, game.player(PlayerId::Player0).pp_capacity == 3);
    EXPECT(context, game.player(PlayerId::Player0).current_pp == 3);

    const InstanceId blast = *game.find_in_hand(PlayerId::Player0, cards::advance::kBurnBlast);
    EXPECT(context, game.cast_spell(PlayerId::Player0, blast, Target::unit_target(PlayerId::Player1, enemy)));
    EXPECT(context, game.player(PlayerId::Player0).current_pp == 2);
    EXPECT(context, game.player(PlayerId::Player0).pp_capacity == 1);
    EXPECT(context, game.player(PlayerId::Player0).cracks == 5);
    EXPECT(context, game.player(PlayerId::Player0).current_pp > game.player(PlayerId::Player0).pp_capacity);

    const InstanceId tech = *game.find_in_hand(PlayerId::Player0, cards::advance::kRepairTechnician);
    EXPECT(context, game.play_unit(PlayerId::Player0, tech));
    EXPECT(context, game.player(PlayerId::Player0).current_pp == 0);
    EXPECT(context, game.player(PlayerId::Player0).pp_capacity == 3);
    EXPECT(context, game.player(PlayerId::Player0).cracks == 3);

    expect_valid_state(context, game);
}

void test_terminal_state_is_idempotent(TestContext& context) {
    // Lethal combat emits one terminal event and rejects later commands.
    Scenario lethal = base_scenario();
    lethal.players[0].units = {cards::midrange::kEliteCommander};
    lethal.players[1].leader_health = 4;
    Game attack_game = scenario_game(lethal);
    const InstanceId attacker = *attack_game.find_on_field(PlayerId::Player0, cards::midrange::kEliteCommander);
    (void)attack_game.drain_events();
    EXPECT(context, attack_game.attack(PlayerId::Player0, attacker, Target::leader(PlayerId::Player1)));
    EXPECT(context, attack_game.result() == GameResult::Player0Won);
    std::vector<GameEvent> events = attack_game.drain_events();
    EXPECT(context, count_events(events, EventType::MatchEnded) == 1);
    EXPECT_CODE(context, attack_game.end_turn(PlayerId::Player0), ErrorCode::GameOver);
    EXPECT_CODE(context, attack_game.surrender(PlayerId::Player1), ErrorCode::GameOver);
    EXPECT(context, attack_game.drain_events().empty());

    // A fatal fatigue draw stops before countdowns and TurnStarted.
    Scenario fatigue = base_scenario();
    fatigue.players[1].leader_health = 1;
    fatigue.players[1].deck.clear();
    fatigue.players[1].tactics = {cards::midrange::kCommandOrder};
    Game fatigue_game = scenario_game(fatigue);
    const InstanceId relic = *fatigue_game.player(PlayerId::Player1).tactics[0];
    EXPECT(context, fatigue_game.instance(relic).countdown == 2);
    (void)fatigue_game.drain_events();
    EXPECT(context, fatigue_game.end_turn(PlayerId::Player0));
    EXPECT(context, fatigue_game.result() == GameResult::Player0Won);
    EXPECT(context, fatigue_game.instance(relic).countdown == 2);
    events = fatigue_game.drain_events();
    EXPECT(context, count_events(events, EventType::MatchEnded) == 1);
    EXPECT(context, std::none_of(events.begin(), events.end(), [](const GameEvent& event) {
        return event.type == EventType::TurnStarted && event.player == PlayerId::Player1;
    }));
    const int fatigue_count = fatigue_game.player(PlayerId::Player1).fatigue_count;
    EXPECT_CODE(context, fatigue_game.end_turn(PlayerId::Player1), ErrorCode::GameOver);
    EXPECT(context, fatigue_game.player(PlayerId::Player1).fatigue_count == fatigue_count);
    EXPECT(context, fatigue_game.drain_events().empty());

    // Surrender is also idempotent.
    Game surrender_game = scenario_game(base_scenario());
    (void)surrender_game.drain_events();
    EXPECT(context, surrender_game.surrender(PlayerId::Player0));
    events = surrender_game.drain_events();
    EXPECT(context, count_events(events, EventType::PlayerSurrendered) == 1);
    EXPECT(context, count_events(events, EventType::MatchEnded) == 1);
    EXPECT_CODE(context, surrender_game.surrender(PlayerId::Player0), ErrorCode::GameOver);
    EXPECT(context, surrender_game.drain_events().empty());

    // If an early effect in a multi-effect trap ends the match, later trap
    // effects must not emit events after MatchEnded.
    constexpr CardId kLethalTrap = 9021;
    CardCatalog catalog = make_v04_catalog();
    CardDefinition lethal_trap;
    lethal_trap.id = kLethalTrap;
    lethal_trap.name = "lethal response trap";
    lethal_trap.kind = CardKind::Trap;
    lethal_trap.effects = {
        EffectRecord{EffectTrigger::OnAttackDeclared, EffectKind::DealDamageToLeader, 1, TargetSpec::None},
        EffectRecord{EffectTrigger::OnAttackDeclared, EffectKind::CancelAttack, 0, TargetSpec::None},
    };
    catalog.add(std::move(lethal_trap));
    Scenario trap_lethal = base_scenario();
    trap_lethal.players[0].leader_health = 1;
    trap_lethal.players[0].units = {cards::midrange::kEliteCommander};
    trap_lethal.players[1].tactics = {kLethalTrap};
    Game trap_game = scenario_game_with_catalog(std::move(catalog), trap_lethal);
    const InstanceId trap_attacker = *trap_game.find_on_field(PlayerId::Player0, cards::midrange::kEliteCommander);
    (void)trap_game.drain_events();
    EXPECT(context, trap_game.attack(PlayerId::Player0, trap_attacker, Target::leader(PlayerId::Player1)));
    const InstanceId lethal_trap_id = trap_game.eligible_traps().front();
    EXPECT(context, trap_game.activate_trap(PlayerId::Player1, lethal_trap_id));
    events = trap_game.drain_events();
    EXPECT(context, trap_game.result() == GameResult::Player1Won);
    EXPECT(context, count_events(events, EventType::MatchEnded) == 1);
    EXPECT(context, std::none_of(events.begin(), events.end(), [](const GameEvent& event) {
        return event.type == EventType::AttackCancelled;
    }));
    EXPECT(context, !events.empty() && events.back().type == EventType::MatchEnded);

    // Even pathological setup draws keep MatchEnded terminal and unique.
    GameConfig fatal_start_config = deterministic_test_config();
    fatal_start_config.starting_hand_size = 1;
    fatal_start_config.leader_health = 1;
    fatal_start_config.shuffle_decks = false;
    DeckList empty_deck;
    Game fatal_start(make_v04_catalog(), empty_deck, empty_deck, fatal_start_config);
    EXPECT(context, fatal_start.start());
    events = fatal_start.drain_events();
    EXPECT(context, fatal_start.phase() == Phase::Finished);
    EXPECT(context, fatal_start.result() == GameResult::Player1Won);
    EXPECT(context, count_events(events, EventType::MatchStarted) == 1);
    EXPECT(context, count_events(events, EventType::MatchEnded) == 1);
    EXPECT(context, !events.empty() && events.back().type == EventType::MatchEnded);
    EXPECT_CODE(context, fatal_start.surrender(PlayerId::Player0), ErrorCode::GameOver);
    EXPECT(context, fatal_start.drain_events().empty());

    // A mulligan that cannot be fully replaced is rejected before moving cards,
    // so it cannot enter a half-mutated fatigue state.
    DeckList four_cards;
    four_cards.main.assign(4, cards::midrange::kGuardSentry);
    GameConfig short_deck_config = deterministic_test_config();
    short_deck_config.shuffle_decks = false;
    Game short_deck_game(make_v04_catalog(), four_cards, four_cards, short_deck_config);
    EXPECT(context, short_deck_game.start());
    const InstanceId selected = short_deck_game.player(PlayerId::Player0).hand.front();
    (void)short_deck_game.drain_events();
    EXPECT_CODE(context, short_deck_game.mulligan(PlayerId::Player0, {selected}), ErrorCode::InvalidCard);
    EXPECT(context, short_deck_game.player(PlayerId::Player0).hand.size() == 4U);
    EXPECT(context, short_deck_game.instance(selected).zone == Zone::Hand);
    EXPECT(context, short_deck_game.drain_events().empty());
    expect_valid_state(context, attack_game);
    expect_valid_state(context, fatigue_game);
    expect_valid_state(context, surrender_game);
    expect_valid_state(context, trap_game);
    expect_valid_state(context, fatal_start);
    expect_valid_state(context, short_deck_game);
}

void test_first_player_modes_and_match_metadata(TestContext& context) {
    GameConfig forced;
    forced.random_seed = 0xDEADBEEFU;
    forced.first_player_mode = FirstPlayerMode::Player1;
    Game forced_game(make_v04_catalog(), make_midrange_deck(), make_advance_deck(), forced);
    EXPECT(context, forced_game.first_player() == PlayerId::Player1);
    EXPECT(context, forced_game.random_seed() == 0xDEADBEEFU);
    EXPECT(context, forced_game.start());
    const std::vector<GameEvent> events = forced_game.drain_events();
    const auto started = std::find_if(events.begin(), events.end(), [](const GameEvent& event) {
        return event.type == EventType::MatchStarted;
    });
    EXPECT(context, started != events.end());
    if (started != events.end()) {
        EXPECT(context, started->player == PlayerId::Player1);
        EXPECT(context, started->random_seed == 0xDEADBEEFU);
    }
    EXPECT(context, forced_game.mulligan(PlayerId::Player0, {}));
    EXPECT(context, forced_game.mulligan(PlayerId::Player1, {}));
    const std::vector<GameEvent> mulligan_events = forced_game.drain_events();
    EXPECT(context, count_events(mulligan_events, EventType::MulliganCompleted) == 2);
    EXPECT(context, std::all_of(mulligan_events.begin(), mulligan_events.end(), [](const GameEvent& event) {
        return event.type != EventType::MulliganCompleted || event.card == 0;
    }));

    GameConfig seeded_random;
    seeded_random.random_seed = 123456U;
    seeded_random.first_player_mode = FirstPlayerMode::Random;
    Game random_a(make_v04_catalog(), make_midrange_deck(), make_advance_deck(), seeded_random);
    Game random_b(make_v04_catalog(), make_midrange_deck(), make_advance_deck(), seeded_random);
    EXPECT(context, random_a.random_seed() == 123456U);
    EXPECT(context, random_a.first_player() == random_b.first_player());
    EXPECT(context, is_valid_player(random_a.first_player()));
    EXPECT(context, random_a.start());
    EXPECT(context, random_b.start());
    for (const PlayerId player : {PlayerId::Player0, PlayerId::Player1}) {
        const auto definition_order = [player](const Game& game) {
            std::vector<CardId> result;
            for (const InstanceId id : game.player(player).hand) {
                result.push_back(game.definition(id).id);
            }
            for (const InstanceId id : game.player(player).deck) {
                result.push_back(game.definition(id).id);
            }
            return result;
        };
        EXPECT(context, definition_order(random_a) == definition_order(random_b));
    }
    const auto random_events = random_a.drain_events();
    const auto random_started = std::find_if(random_events.begin(), random_events.end(), [](const GameEvent& event) {
        return event.type == EventType::MatchStarted;
    });
    EXPECT(context, random_started != random_events.end());
    if (random_started != random_events.end()) {
        EXPECT(context, random_started->player == random_a.first_player());
        EXPECT(context, random_started->random_seed == 123456U);
    }
}

void test_invalid_player_commands(TestContext& context) {
    const PlayerId invalid = static_cast<PlayerId>(2);
    GameConfig config = deterministic_test_config();
    Game mulligan_game(make_v04_catalog(), make_midrange_deck(), make_advance_deck(), config);
    EXPECT(context, mulligan_game.start());
    EXPECT_CODE(context, mulligan_game.mulligan(invalid, {}), ErrorCode::InvalidPlayer);

    Scenario scenario = base_scenario();
    scenario.players[0].hand = {
        cards::midrange::kEliteCommander,
        cards::midrange::kPrecisionStrike,
        cards::midrange::kInterceptTrap,
    };
    scenario.players[0].units = {cards::midrange::kGuardSentry};
    scenario.players[0].standby = {cards::midrange::kGuardAce};
    Game game = scenario_game(scenario);
    EXPECT_CODE(context, game.end_turn(invalid), ErrorCode::InvalidPlayer);
    EXPECT_CODE(context, game.surrender(invalid), ErrorCode::InvalidPlayer);
    EXPECT_CODE(context, game.play_unit(invalid, 0), ErrorCode::InvalidPlayer);
    EXPECT_CODE(context, game.cast_spell(invalid, 0), ErrorCode::InvalidPlayer);
    EXPECT_CODE(context, game.play_tactic(invalid, 0, 0), ErrorCode::InvalidPlayer);
    EXPECT_CODE(context, game.attack(invalid, 0, Target::leader(PlayerId::Player1)), ErrorCode::InvalidPlayer);
    EXPECT_CODE(context, game.validate_attack(invalid, 0, Target::leader(PlayerId::Player1)), ErrorCode::InvalidPlayer);
    EXPECT_CODE(context, game.evolve(invalid, 0), ErrorCode::InvalidPlayer);
    EXPECT_CODE(context, game.deploy(invalid, 0), ErrorCode::InvalidPlayer);
    EXPECT_CODE(context, game.use_leader_skill(invalid), ErrorCode::InvalidPlayer);
    EXPECT_CODE(context, game.activate_trap(invalid, 0), ErrorCode::InvalidPlayer);
    EXPECT_CODE(context, game.pass_reaction(invalid), ErrorCode::InvalidPlayer);

    const InstanceId attacker = *game.find_on_field(PlayerId::Player0, cards::midrange::kGuardSentry);
    Target malformed = Target::leader(PlayerId::Player1);
    malformed.kind = static_cast<Target::Kind>(99);
    const int defender_health = game.player(PlayerId::Player1).leader_health;
    (void)game.drain_events();
    EXPECT_CODE(context, game.validate_attack(PlayerId::Player0, attacker, malformed), ErrorCode::InvalidTarget);
    EXPECT_CODE(context, game.attack(PlayerId::Player0, attacker, malformed), ErrorCode::InvalidTarget);
    EXPECT(context, game.player(PlayerId::Player1).leader_health == defender_health);
    EXPECT(context, !game.instance(attacker).attacked_this_turn);
    EXPECT(context, game.drain_events().empty());

    Scenario invalid_scenario = base_scenario();
    invalid_scenario.active_player = invalid;
    Game loader(make_v04_catalog(), make_midrange_deck(), make_advance_deck(), config);
    EXPECT_CODE(context, loader.load_scenario(invalid_scenario), ErrorCode::InvalidPlayer);
    EXPECT(context, loader.phase() == Phase::NotStarted);
}

// ---------------------------------------------------------------------------
// R19. Deterministic smoke matches with invariants (both first players)
// ---------------------------------------------------------------------------
void take_smoke_action(Game& game, int& call_counter) {
    ++call_counter;
    const PlayerId active = game.active_player();

    // Response windows: the layer responder may activate a trap or pass.
    if (game.phase() == Phase::Reaction) {
        const std::vector<InstanceId> traps = game.eligible_traps();
        const PlayerId responder = traps.empty() ? opponent(active) : game.instance(traps.front()).controller;
        const bool use_trap = !traps.empty() && ((call_counter % 3) != 0);
        if (use_trap) {
            (void)game.activate_trap(responder, traps.front());
        } else {
            (void)game.pass_reaction(responder);
        }
        return;
    }
    if (game.phase() == Phase::Mulligan) {
        (void)game.mulligan(PlayerId::Player0, {});
        (void)game.mulligan(PlayerId::Player1, {});
        return;
    }
    if (game.result() != GameResult::Ongoing) {
        return;
    }

    const PlayerState& state = game.player(active);
    const PlayerState& enemy = game.player(opponent(active));

    // 1. Attack with the first legal attacker.
    for (const auto& slot : state.units) {
        if (!slot.has_value()) {
            continue;
        }
        const CardInstance& unit = game.instance(*slot);
        if (unit.attacked_this_turn || unit.current_attack <= 0) {
            continue;
        }
        std::optional<Target> candidate;
        for (const auto& enemy_slot : enemy.units) {
            if (!enemy_slot.has_value()) {
                continue;
            }
            if (game.validate_attack(active, *slot, Target::unit_target(opponent(active), *enemy_slot))) {
                candidate = Target::unit_target(opponent(active), *enemy_slot);
                break;
            }
        }
        if (!candidate.has_value() &&
            game.validate_attack(active, *slot, Target::leader(opponent(active)))) {
            candidate = Target::leader(opponent(active));
        }
        if (candidate.has_value()) {
            (void)game.attack(active, *slot, *candidate);
            return;
        }
    }

    // 2. Deploy a standby card when the condition holds.
    for (const InstanceId id : state.standby) {
        if (game.deploy(active, id)) {
            return;
        }
    }

    // 3. Play a unit or spell, with advance when needed.
    const bool can_advance = !state.advance_used_this_turn;
    for (const InstanceId id : state.hand) {
        const CardDefinition& def = game.definition(id);
        if (def.kind == CardKind::Unit) {
            const auto target = first_enemy_unit_target(game, active);
            if (game.play_unit(active, id, std::nullopt, target, can_advance)) {
                return;
            }
        }
    }
    for (const InstanceId id : state.hand) {
        const CardDefinition& def = game.definition(id);
        if (def.kind == CardKind::Spell) {
            const auto target = first_enemy_unit_target(game, active);
            if (game.cast_spell(active, id, target, can_advance)) {
                return;
            }
        }
    }
    // 4. Set a relic or trap into the first free tactic slot.
    for (const InstanceId id : state.hand) {
        const CardDefinition& def = game.definition(id);
        if (def.kind == CardKind::Relic || def.kind == CardKind::Trap) {
            for (std::size_t slot = 0; slot < kTacticZoneSize; ++slot) {
                if (game.play_tactic(active, id, slot)) {
                    return;
                }
            }
        }
    }
    // 5. Evolve the first evolvable unit.
    for (const auto& slot : state.units) {
        if (slot.has_value() && game.evolve(active, *slot)) {
            return;
        }
    }
    // 6. Use the leader skill when affordable.
    if (game.use_leader_skill(active)) {
        return;
    }
    // 7. Otherwise end the turn.
    (void)game.end_turn(active);
}

void test_invariants_and_smoke_matches(TestContext& context) {
    const char* seed_env = std::getenv("SCGS_SMOKE_SEEDS");
    const int seeds = seed_env != nullptr ? std::atoi(seed_env) : 32;
    for (int seed = 0; seed < seeds; ++seed) {
        for (int first = 0; first < 2; ++first) {
            GameConfig config;
            config.random_seed = static_cast<std::uint32_t>(seed * 2 + first);
            config.first_player_mode = first == 0 ? FirstPlayerMode::Player0 : FirstPlayerMode::Player1;
            Game game(make_v04_catalog(), make_midrange_deck(), make_advance_deck(), config);
            EXPECT(context, game.start());
            int call_counter = 0;
            int iterations = 0;
            while (game.result() == GameResult::Ongoing && iterations < 1000) {
                take_smoke_action(game, call_counter);
                const std::vector<std::string> problems = game.validate_invariants();
                if (!problems.empty()) {
                    std::cerr << "seed " << seed << " first " << first << " iteration " << iterations
                              << ": invariant violations:\n";
                    for (const std::string& problem : problems) {
                        std::cerr << "  - " << problem << '\n';
                    }
                    EXPECT(context, false);
                    return;
                }
                ++iterations;
            }
            EXPECT(context, game.result() != GameResult::Ongoing);
        }
    }
}

// ---------------------------------------------------------------------------
// Test harness
// ---------------------------------------------------------------------------
struct TestCase {
    std::string_view name;
    void (*function)(TestContext&);
};

} // namespace

int main() {
    const std::vector<TestCase> tests = {
        {"pp_capacity_growth_and_refill", test_pp_capacity_growth_and_refill},
        {"end_turn_cleanup_order", test_end_turn_cleanup_order},
        {"advance_payment_and_limits", test_advance_payment_and_limits},
        {"burn_cost_and_combined_advance", test_burn_cost_and_combined_advance},
        {"cracks_persistence_and_read", test_cracks_persistence_and_read},
        {"repair_restores_capacity", test_repair_restores_capacity},
        {"growth_adds_capacity", test_growth_adds_capacity},
        {"current_pp_above_capacity", test_current_pp_above_capacity},
        {"advanced_on_time_status", test_advanced_on_time_status},
        {"evolution_unlock_and_cost", test_evolution_unlock_and_cost},
        {"evolution_states_and_trigger", test_evolution_states_and_trigger},
        {"charge_conditions", test_charge_conditions},
        {"charge_requires_evolution_unlock", test_charge_requires_evolution_unlock},
        {"deployment_flow_and_limits", test_deployment_flow_and_limits},
        {"deploy_into_archived_donor_slot", test_deploy_into_archived_donor_slot},
        {"component_grant_and_no_retransfer", test_component_grant_and_no_retransfer},
        {"response_stack_lifo", test_response_stack_lifo},
        {"spell_response_three_level_lifo_and_counter_pass", test_spell_response_three_level_lifo_and_counter_pass},
        {"response_target_invalidation_continues_effects", test_response_target_invalidation_continues_effects},
        {"trap_entry_pending_damage", test_trap_entry_pending_damage},
        {"tactic_zone_no_replacement", test_tactic_zone_no_replacement},
        {"hand_overflow_and_fatigue", test_hand_overflow_and_fatigue},
        {"combat_and_attack_rules", test_combat_and_attack_rules},
        {"simultaneous_death_batch", test_simultaneous_death_batch},
        {"trap_set_once_per_turn", test_trap_set_once_per_turn},
        {"documented_overdraw_walkthrough", test_documented_overdraw_walkthrough},
        {"terminal_state_is_idempotent", test_terminal_state_is_idempotent},
        {"first_player_modes_and_match_metadata", test_first_player_modes_and_match_metadata},
        {"invalid_player_commands", test_invalid_player_commands},
        {"invariants_and_smoke_matches", test_invariants_and_smoke_matches},
    };

    TestContext context;
    for (const TestCase& test : tests) {
        try {
            test.function(context);
        } catch (const std::exception& exception) {
            ++context.failures;
            std::cerr << "test threw: " << test.name << ": " << exception.what() << '\n';
        }
    }

    std::cout << tests.size() << " test cases\n"
              << context.assertions << " assertions\n"
              << context.failures << " failures\n";
    return context.failures == 0 ? EXIT_SUCCESS : EXIT_FAILURE;
}
