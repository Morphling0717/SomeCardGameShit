// SPDX-License-Identifier: GPL-3.0-or-later
#include "scgs/game.hpp"

#include <cstdlib>
#include <iostream>
#include <optional>
#include <string_view>

namespace {

using namespace scgs;

bool require(const Status& status, const std::string_view step) {
    if (status) {
        return true;
    }
    std::cerr << "step failed: " << step << ": " << status.message << '\n';
    return false;
}

} // namespace

// v0.4 golden scenario: the documented overdraw walkthrough
// (rules-v0.4 §9/§13/§14/§15/§17) executed step by step.
//
//   Turn 5, player 0: 5 current PP / 5 capacity.
//   1. Play an 8-cost unit with advance  (§9):  5/5 → 0/2, cracks 3.
//   2. Next own turn (turn 6): capacity 2→3, current PP refills to 3 (§7).
//   3. Cast a 1PP + 燃耗2 spell (§12/§13/§17): 3→2 current, 3→1 capacity,
//      cracks 3→5, and current PP (2) is legally above capacity (1).
//   4. Play 修复技师 (2PP, OnEntry 修复2) (§15): current 0, cracks 5→3,
//      capacity 1→3.
int main(const int argc, char** argv) {
    const bool verify = argc > 1 && std::string_view(argv[1]) == "--verify";

    Game game(make_v04_catalog(), make_midrange_deck(), make_advance_deck());
    Scenario scenario;
    scenario.active_player = PlayerId::Player0;
    scenario.players[0].leader_health = 25;
    scenario.players[0].maximum_leader_health = 25;
    scenario.players[0].current_pp = 5;
    scenario.players[0].pp_capacity = 5;
    scenario.players[0].evolution_points = 2;
    scenario.players[0].own_turn_number = 5;
    scenario.players[0].hand = {
        cards::advance::kDebtLord,
        cards::advance::kBurnBlast,
        cards::advance::kRepairTechnician,
    };
    scenario.players[0].deck = {cards::midrange::kGuardSentry, cards::midrange::kGuardSentry};

    scenario.players[1].leader_health = 25;
    scenario.players[1].maximum_leader_health = 25;
    scenario.players[1].own_turn_number = 1;
    scenario.players[1].units = {cards::midrange::kGuardSentry};
    scenario.players[1].deck = {cards::advance::kOnTimeElite, cards::advance::kOnTimeElite};

    if (!require(game.load_scenario(scenario), "load documented scenario")) {
        return EXIT_FAILURE;
    }

    const InstanceId debt_lord = *game.find_in_hand(PlayerId::Player0, cards::advance::kDebtLord);
    const InstanceId enemy_sentry = *game.find_on_field(PlayerId::Player1, cards::midrange::kGuardSentry);

    // Step 1: advance-play the 8-cost unit from 5 PP (rules-v0.4 §9).
    if (!require(game.play_unit(PlayerId::Player0, debt_lord, std::nullopt, std::nullopt, /*use_advance=*/true),
                 "advance play 8-cost unit")) {
        return EXIT_FAILURE;
    }
    const int s1_pp = game.player(PlayerId::Player0).current_pp;
    const int s1_cap = game.player(PlayerId::Player0).pp_capacity;
    const int s1_cracks = game.player(PlayerId::Player0).cracks;
    const bool step1 = s1_pp == 0 && s1_cap == 2 && s1_cracks == 3;

    // Step 2: pass through player 1's turn; player 0's turn 6 refills (rules-v0.4 §7.2).
    if (!require(game.end_turn(PlayerId::Player0), "end turn 5") ||
        !require(game.end_turn(PlayerId::Player1), "end turn 2")) {
        return EXIT_FAILURE;
    }
    const int s2_pp = game.player(PlayerId::Player0).current_pp;
    const int s2_cap = game.player(PlayerId::Player0).pp_capacity;
    const bool step2 = s2_cap == 3 && s2_pp == 3;

    // Step 3: cast 1PP + 燃耗2 spell (rules-v0.4 §12/§13/§17).
    const InstanceId burn_blast = *game.find_in_hand(PlayerId::Player0, cards::advance::kBurnBlast);
    if (!require(game.cast_spell(PlayerId::Player0, burn_blast, 0,
                                 Target::unit_target(PlayerId::Player1, enemy_sentry)),
                 "cast burn spell")) {
        return EXIT_FAILURE;
    }
    const int s3_pp = game.player(PlayerId::Player0).current_pp;
    const int s3_cap = game.player(PlayerId::Player0).pp_capacity;
    const int s3_cracks = game.player(PlayerId::Player0).cracks;
    const bool step3 = s3_pp == 2 && s3_cap == 1 && s3_cracks == 5 && s3_pp > s3_cap;

    // Step 4: play 修复技师 (2PP, OnEntry 修复2) (rules-v0.4 §15).
    const InstanceId technician = *game.find_in_hand(PlayerId::Player0, cards::advance::kRepairTechnician);
    if (!require(game.play_unit(PlayerId::Player0, technician), "play repair technician")) {
        return EXIT_FAILURE;
    }
    const int s4_pp = game.player(PlayerId::Player0).current_pp;
    const int s4_cap = game.player(PlayerId::Player0).pp_capacity;
    const int s4_cracks = game.player(PlayerId::Player0).cracks;
    const bool step4 = s4_pp == 0 && s4_cap == 3 && s4_cracks == 3;

    const std::vector<std::string> problems = game.validate_invariants();
    const bool invariants_hold = problems.empty();
    if (!invariants_hold) {
        for (const std::string& problem : problems) {
            std::cerr << "invariant violation: " << problem << '\n';
        }
    }

    if (verify) {
        std::cout << "{\n"
                  << "  \"scenario\": \"documented_overdraw_then_burn_then_repair\",\n"
                  << "  \"verified\": " << (step1 && step2 && step3 && step4 && invariants_hold ? "true" : "false") << ",\n"
                  << "  \"step1_advance\": { \"current_pp\": " << s1_pp
                  << ", \"pp_capacity\": " << s1_cap
                  << ", \"cracks\": " << s1_cracks << " },\n"
                  << "  \"step2_refill\": { \"current_pp\": " << s2_pp
                  << ", \"pp_capacity\": " << s2_cap << " },\n"
                  << "  \"step3_burn\": { \"current_pp\": " << s3_pp
                  << ", \"pp_capacity\": " << s3_cap
                  << ", \"cracks\": " << s3_cracks
                  << ", \"pp_above_capacity\": " << (s3_pp > s3_cap ? "true" : "false") << " },\n"
                  << "  \"step4_repair\": { \"current_pp\": " << s4_pp
                  << ", \"pp_capacity\": " << s4_cap
                  << ", \"cracks\": " << s4_cracks << " },\n"
                  << "  \"invariants_hold\": " << (invariants_hold ? "true" : "false") << "\n"
                  << "}\n";
        return (step1 && step2 && step3 && step4 && invariants_hold) ? EXIT_SUCCESS : EXIT_FAILURE;
    }

    return EXIT_SUCCESS;
}
