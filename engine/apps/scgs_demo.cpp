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

int main(const int argc, char** argv) {
    const bool verify = argc > 1 && std::string_view(argv[1]) == "--verify";

    Game game(make_prototype_catalog(), {}, {});
    Scenario scenario;
    scenario.active_player = PlayerId::Player0;
    scenario.players[0].leader_health = 25;
    scenario.players[0].maximum_leader_health = 25;
    scenario.players[0].current_pp = 5;
    scenario.players[0].maximum_pp = 5;
    scenario.players[0].evolution_points = 1;
    scenario.players[0].own_turn_number = 5;
    scenario.players[0].units = {
        cards::kMachineRushPart,
        cards::kMachineGuardPart,
    };
    scenario.players[0].summon_deck = {cards::kBastionConstruct};

    scenario.players[1].leader_health = 25;
    scenario.players[1].maximum_leader_health = 25;
    scenario.players[1].units = {cards::kTrainingDummy};

    if (!require(game.load_scenario(scenario), "load documented scenario")) {
        return EXIT_FAILURE;
    }

    const InstanceId rush_part = *game.find_on_field(PlayerId::Player0, cards::kMachineRushPart);
    const InstanceId guard_part = *game.find_on_field(PlayerId::Player0, cards::kMachineGuardPart);
    const InstanceId dummy = *game.find_on_field(PlayerId::Player1, cards::kTrainingDummy);
    const InstanceId construct = *game.find_in_summon_deck(PlayerId::Player0, cards::kBastionConstruct);

    AdvancedSummonRequest summon;
    summon.player = PlayerId::Player0;
    summon.card = construct;
    summon.materials = {rush_part, guard_part};
    summon.inherited_imprint = Imprint::Guard;
    summon.ability_target = Target::unit_target(PlayerId::Player1, dummy);

    if (!require(game.advanced_summon(summon), "construct summon")) {
        return EXIT_FAILURE;
    }
    if (!require(
            game.evolve(PlayerId::Player0, construct, EvolutionMode::Combat),
            "combat evolution")) {
        return EXIT_FAILURE;
    }

    const Status illegal_leader_attack = game.attack(
        PlayerId::Player0,
        construct,
        Target::leader(PlayerId::Player1));
    if (illegal_leader_attack.code != ErrorCode::SummoningSickness) {
        std::cerr << "advanced summon unexpectedly attacked the leader on its entry turn\n";
        return EXIT_FAILURE;
    }

    if (!require(
            game.attack(
                PlayerId::Player0,
                construct,
                Target::unit_target(PlayerId::Player1, dummy)),
            "attack enemy unit")) {
        return EXIT_FAILURE;
    }

    const CardInstance& final_construct = game.instance(construct);
    const PlayerState& player = game.player(PlayerId::Player0);

    const bool correct =
        player.current_pp == 3 &&
        player.evolution_points == 0 &&
        player.archive.size() == 2U &&
        final_construct.current_attack == 7 &&
        final_construct.current_health == 5 &&
        final_construct.maximum_health == 8 &&
        final_construct.inherited_imprint == Imprint::Guard &&
        has_keyword(final_construct.keywords, Keyword::Guard) &&
        final_construct.evolved &&
        final_construct.attacked_this_turn &&
        game.player(PlayerId::Player1).graveyard.size() == 1U;

    std::cout
        << "{\n"
        << "  \"scenario\": \"documented_construct_guard_then_combat_evolve\",\n"
        << "  \"verified\": " << (correct ? "true" : "false") << ",\n"
        << "  \"player_pp\": " << player.current_pp << ",\n"
        << "  \"evolution_points\": " << player.evolution_points << ",\n"
        << "  \"materials_archived\": " << player.archive.size() << ",\n"
        << "  \"construct_attack\": " << final_construct.current_attack << ",\n"
        << "  \"construct_health\": " << final_construct.current_health << ",\n"
        << "  \"construct_max_health\": " << final_construct.maximum_health << ",\n"
        << "  \"guard_inherited\": "
        << (has_keyword(final_construct.keywords, Keyword::Guard) ? "true" : "false") << ",\n"
        << "  \"enemy_unit_destroyed\": "
        << (game.player(PlayerId::Player1).graveyard.size() == 1U ? "true" : "false") << "\n"
        << "}\n";

    if (verify && !correct) {
        return EXIT_FAILURE;
    }
    return correct ? EXIT_SUCCESS : EXIT_FAILURE;
}
