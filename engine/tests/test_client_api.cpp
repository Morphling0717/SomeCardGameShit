// SPDX-License-Identifier: GPL-3.0-or-later
#include "scgs/client_api.hpp"
#include "scgs/game.hpp"

#include <algorithm>
#include <cstdint>
#include <iostream>
#include <optional>
#include <string>
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

Scenario base_scenario() {
    Scenario scenario;
    scenario.active_player = PlayerId::Player0;
    for (ScenarioPlayer& player : scenario.players) {
        player.current_pp = 8;
        player.pp_capacity = 8;
        player.evolution_points = 3;
        player.own_turn_number = 5;
        for (int index = 0; index < 12; ++index) {
            player.deck.push_back(cards::midrange::kGuardSentry);
        }
    }
    return scenario;
}

Game scenario_game(const Scenario& scenario) {
    GameConfig config;
    config.random_seed = 0x12345678U;
    config.first_player_mode = FirstPlayerMode::Player0;
    config.shuffle_decks = false;
    Game game(make_v04_catalog(), make_midrange_deck(), make_advance_deck(), config);
    const Status status = game.load_scenario(scenario);
    if (!status) {
        std::cerr << "scenario setup failed: " << status.message << '\n';
    }
    return game;
}

Game scenario_game_with_catalog(CardCatalog catalog, const Scenario& scenario) {
    GameConfig config;
    config.random_seed = 0x12345678U;
    config.first_player_mode = FirstPlayerMode::Player0;
    config.shuffle_decks = false;
    Game game(std::move(catalog), make_midrange_deck(), make_advance_deck(), config);
    const Status status = game.load_scenario(scenario);
    if (!status) {
        std::cerr << "scenario setup failed: " << status.message << '\n';
    }
    return game;
}

void test_snapshot_privacy(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].hand = {cards::midrange::kPioneerScout};
    scenario.players[1].hand = {cards::advance::kBurnBlast};
    scenario.players[0].tactics = {cards::midrange::kInterceptTrap};
    scenario.players[1].tactics = {cards::advance::kReactionTrap};
    scenario.players[1].standby = {cards::advance::kDoomEngine};
    Game game = scenario_game(scenario);

    const MatchView player0 = game.make_view(PlayerId::Player0);
    EXPECT(context, player0.revision == 0U);
    EXPECT(context, player0.random_seed == 0x12345678U);
    EXPECT(context, player0.first_player == PlayerId::Player0);
    EXPECT(context, player0.players[0].hand.size() == 1U);
    EXPECT(context, player0.players[0].hand[0].definition.has_value());
    EXPECT(context, player0.players[1].hand_count == 1U);
    EXPECT(context, player0.players[1].hand.empty());
    EXPECT(context, player0.players[0].tactics[0]->instance_id.has_value());
    EXPECT(context, player0.players[0].tactics[0]->definition.has_value());
    EXPECT(context, !player0.players[1].tactics[0]->instance_id.has_value());
    EXPECT(context, !player0.players[1].tactics[0]->definition_id.has_value());
    EXPECT(context, !player0.players[1].tactics[0]->definition.has_value());
    EXPECT(context, player0.players[1].tactics[0]->name.empty());
    EXPECT(context, player0.players[1].tactics[0]->face_down);
    EXPECT(context, player0.players[1].standby.size() == 1U);
    EXPECT(context, player0.players[1].standby[0].definition_id.has_value());

    const MatchView player1 = game.make_view(PlayerId::Player1);
    EXPECT(context, player1.players[1].hand.size() == 1U);
    EXPECT(context, player1.players[0].hand.empty());
    EXPECT(context, player1.players[1].tactics[0]->instance_id.has_value());
}

void test_pending_spell_is_public_in_its_declared_slot(TestContext& context) {
    constexpr CardId kSpellResponseTrap = 9101;
    CardCatalog catalog = make_v04_catalog();
    CardDefinition trap;
    trap.id = kSpellResponseTrap;
    trap.name = "public spell response trap";
    trap.kind = CardKind::Trap;
    trap.effects = {
        EffectRecord{
            EffectTrigger::OnSpellDeclared,
            EffectKind::DealDamageToLeader,
            1,
            TargetSpec::None},
    };
    catalog.add(std::move(trap));

    Scenario scenario = base_scenario();
    scenario.players[0].hand = {cards::midrange::kPrecisionStrike};
    scenario.players[1].units = {cards::midrange::kGuardSentry};
    scenario.players[1].tactics = {kSpellResponseTrap};
    Game game = scenario_game_with_catalog(std::move(catalog), scenario);

    const InstanceId spell = *game.find_in_hand(
        PlayerId::Player0, cards::midrange::kPrecisionStrike);
    const InstanceId target = *game.player(PlayerId::Player1).units[0];
    const auto initial_events = game.read_events(PlayerId::Player0, 0);
    const std::uint64_t cursor = initial_events.empty() ? 0U : initial_events.back().sequence;

    GameCommand cast;
    cast.player = PlayerId::Player0;
    cast.action = ActionKind::CastSpell;
    cast.source = spell;
    cast.target = Target::unit_target(PlayerId::Player1, target);
    cast.slot = 2U;
    cast.expected_revision = game.revision();
    EXPECT(context, game.submit_command(cast));
    EXPECT(context, game.phase() == Phase::Reaction);

    for (const PlayerId viewer : {PlayerId::Player0, PlayerId::Player1}) {
        const MatchView view = game.make_view(viewer);
        const auto& declared = view.players[0].tactics[2];
        EXPECT(context, declared.has_value());
        if (declared.has_value()) {
            EXPECT(context, declared->instance_id == spell);
            EXPECT(context, declared->definition_id == cards::midrange::kPrecisionStrike);
            EXPECT(context, !declared->face_down);
        }

        const auto events = game.read_events(viewer, cursor);
        const auto moved_to_slot = std::find_if(
            events.begin(), events.end(), [spell](const GameEventView& event) {
                return event.type == EventType::CardMoved && event.card == spell &&
                    event.value == static_cast<int>(Zone::Tactic) &&
                    event.secondary_value == 2;
            });
        EXPECT(context, moved_to_slot != events.end());
        if (moved_to_slot != events.end()) {
            EXPECT(context, !moved_to_slot->hidden_card);
            EXPECT(context, moved_to_slot->definition_id == cards::midrange::kPrecisionStrike);
        }
    }

    GameCommand pass;
    pass.player = PlayerId::Player1;
    pass.action = ActionKind::PassReaction;
    pass.expected_revision = game.revision();
    EXPECT(context, game.submit_command(pass));
    EXPECT(context, game.instance(spell).zone == Zone::Graveyard);
    EXPECT(context, !game.make_view(PlayerId::Player1).players[0].tactics[2].has_value());
}

void test_legal_actions_and_payment(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].current_pp = 5;
    scenario.players[0].pp_capacity = 5;
    scenario.players[0].hand = {
        cards::midrange::kPioneerScout,
        cards::midrange::kPrecisionStrike,
        cards::midrange::kCommandOrder,
    };
    scenario.players[1].units = {cards::advance::kRepairTechnician};
    Game game = scenario_game(scenario);
    const std::uint64_t revision = game.revision();

    ActionQuery query;
    query.player = PlayerId::Player0;
    query.expected_revision = revision;
    const std::vector<LegalAction> actions = game.list_legal_actions(query);
    EXPECT(context, !actions.empty());
    for (const LegalAction& legal : actions) {
        Game candidate = game;
        const Status status = candidate.submit_command(legal.command);
        EXPECT(context, status);
        EXPECT(context, legal.command.expected_revision == revision);
        EXPECT(context, legal.payment.status);
    }

    ActionQuery spell_query;
    spell_query.player = PlayerId::Player0;
    spell_query.action = ActionKind::CastSpell;
    spell_query.expected_revision = revision;
    const std::vector<LegalAction> spells = game.list_legal_actions(spell_query);
    EXPECT(context, !spells.empty());
    EXPECT(context, std::all_of(spells.begin(), spells.end(), [](const LegalAction& action) {
        return action.command.slot.has_value() && *action.command.slot < kTacticZoneSize;
    }));
    const GameCommand spell = spells.front().command;
    const PaymentPreview preview = game.preview_payment(spell);
    EXPECT(context, preview.status);
    Game candidate = game;
    EXPECT(context, candidate.submit_command(spell));
    const MatchView after = candidate.make_view(PlayerId::Player0);
    EXPECT(context, preview.current_pp_after == after.players[0].current_pp);
    EXPECT(context, preview.pp_capacity_after == after.players[0].pp_capacity);
    EXPECT(context, preview.cracks_after == after.players[0].cracks);

    GameCommand stale = spell;
    stale.expected_revision = revision + 1U;
    EXPECT_CODE(context, game.submit_command(stale), ErrorCode::StaleRevision);
    GameCommand invalid_player = spell;
    invalid_player.player = static_cast<PlayerId>(99);
    EXPECT_CODE(context, game.submit_command(invalid_player), ErrorCode::InvalidPlayer);

    ActionQuery target_query;
    target_query.player = PlayerId::Player0;
    target_query.action = ActionKind::CastSpell;
    target_query.source = spell.source;
    target_query.expected_revision = revision;
    const auto targets = game.list_valid_targets(target_query);
    EXPECT(context, targets.size() == 1U);
    EXPECT(context, targets[0].kind == Target::Kind::Unit);
    EXPECT(context, targets[0].player == PlayerId::Player1);

    ActionQuery spell_slot_query = target_query;
    const std::vector<std::size_t> spell_slots = game.list_valid_slots(spell_slot_query);
    EXPECT(context, spell_slots == std::vector<std::size_t>({0U, 1U, 2U}));

    Scenario occupied_spell_scenario = scenario;
    occupied_spell_scenario.players[0].tactics = {cards::midrange::kCommandOrder};
    Game occupied_spell_game = scenario_game(occupied_spell_scenario);
    ActionQuery occupied_spell_query;
    occupied_spell_query.player = PlayerId::Player0;
    occupied_spell_query.action = ActionKind::CastSpell;
    occupied_spell_query.source = *occupied_spell_game.find_in_hand(
        PlayerId::Player0, cards::midrange::kPrecisionStrike);
    occupied_spell_query.expected_revision = occupied_spell_game.revision();
    EXPECT(
        context,
        occupied_spell_game.list_valid_slots(occupied_spell_query) ==
            std::vector<std::size_t>({1U, 2U}));
    GameCommand occupied_spell_command;
    occupied_spell_command.player = PlayerId::Player0;
    occupied_spell_command.action = ActionKind::CastSpell;
    occupied_spell_command.source = *occupied_spell_query.source;
    occupied_spell_command.target = Target::unit_target(
        PlayerId::Player1,
        *occupied_spell_game.player(PlayerId::Player1).units[0]);
    occupied_spell_command.slot = 0U;
    occupied_spell_command.expected_revision = occupied_spell_game.revision();
    EXPECT_CODE(
        context,
        occupied_spell_game.preview_payment(occupied_spell_command).status,
        ErrorCode::TacticZoneFull);
    EXPECT_CODE(
        context,
        occupied_spell_game.submit_command(occupied_spell_command),
        ErrorCode::TacticZoneFull);
    EXPECT(context, occupied_spell_game.revision() == 0U);

    Scenario full_spell_scenario = scenario;
    full_spell_scenario.players[0].tactics = {
        cards::midrange::kCommandOrder,
        cards::midrange::kCommandOrder,
        cards::midrange::kCommandOrder,
    };
    Game full_spell_game = scenario_game(full_spell_scenario);
    ActionQuery full_spell_query;
    full_spell_query.player = PlayerId::Player0;
    full_spell_query.action = ActionKind::CastSpell;
    full_spell_query.source = *full_spell_game.find_in_hand(
        PlayerId::Player0, cards::midrange::kPrecisionStrike);
    full_spell_query.expected_revision = full_spell_game.revision();
    EXPECT(context, full_spell_game.list_legal_actions(full_spell_query).empty());
    EXPECT(context, full_spell_game.list_valid_slots(full_spell_query).empty());

    ActionQuery slot_query;
    slot_query.player = PlayerId::Player0;
    slot_query.action = ActionKind::PlayUnit;
    slot_query.expected_revision = revision;
    EXPECT(context, game.list_valid_slots(slot_query).size() == kUnitZoneSize);

    Scenario deploy_scenario = base_scenario();
    deploy_scenario.players[0].units = {
        cards::midrange::kPioneerScout,
        cards::midrange::kGuardSentry,
    };
    deploy_scenario.players[0].standby = {cards::midrange::kGuardAce};
    Game deploy_game = scenario_game(deploy_scenario);
    ActionQuery donor_query;
    donor_query.player = PlayerId::Player0;
    donor_query.action = ActionKind::Deploy;
    donor_query.source = deploy_game.find_in_standby(PlayerId::Player0, cards::midrange::kGuardAce);
    donor_query.expected_revision = deploy_game.revision();
    EXPECT(context, deploy_game.list_valid_donors(donor_query).size() == 2U);
}

void test_payment_preview_is_cost_only_and_viewer_safe(TestContext& context) {
    Scenario plain_scenario = base_scenario();
    plain_scenario.players[0].current_pp = 2;
    plain_scenario.players[0].pp_capacity = 4;
    plain_scenario.players[0].cracks = 2;
    plain_scenario.players[0].hand = {cards::advance::kRepairTechnician};

    Scenario trapped_scenario = plain_scenario;
    trapped_scenario.players[1].tactics = {cards::midrange::kCounterTrap};

    Game plain = scenario_game(plain_scenario);
    Game trapped = scenario_game(trapped_scenario);
    const auto command_for = [](const Game& game) {
        GameCommand command;
        command.player = PlayerId::Player0;
        command.action = ActionKind::PlayUnit;
        command.source = *game.find_in_hand(PlayerId::Player0, cards::advance::kRepairTechnician);
        command.slot = 0U;
        command.expected_revision = game.revision();
        return command;
    };

    const GameCommand plain_command = command_for(plain);
    const GameCommand trapped_command = command_for(trapped);
    const PaymentPreview plain_preview = plain.preview_payment(plain_command);
    const PaymentPreview trapped_preview = trapped.preview_payment(trapped_command);

    EXPECT(context, plain_preview.status);
    EXPECT(context, trapped_preview.status);
    EXPECT(context, plain_preview.current_pp_before == trapped_preview.current_pp_before);
    EXPECT(context, plain_preview.current_pp_after == trapped_preview.current_pp_after);
    EXPECT(context, plain_preview.pp_capacity_before == trapped_preview.pp_capacity_before);
    EXPECT(context, plain_preview.pp_capacity_after == trapped_preview.pp_capacity_after);
    EXPECT(context, plain_preview.cracks_before == trapped_preview.cracks_before);
    EXPECT(context, plain_preview.cracks_after == trapped_preview.cracks_after);
    EXPECT(context, plain_preview.evolution_energy_before == trapped_preview.evolution_energy_before);
    EXPECT(context, plain_preview.evolution_energy_after == trapped_preview.evolution_energy_after);
    EXPECT(context, plain_preview.base_cost == 2);
    EXPECT(context, plain_preview.current_pp_after == 0);
    EXPECT(context, plain_preview.cracks_after == 2);

    EXPECT(context, plain.submit_command(plain_command));
    EXPECT(context, trapped.submit_command(trapped_command));
    EXPECT(context, plain.phase() == Phase::Action);
    EXPECT(context, trapped.phase() == Phase::Reaction);
    EXPECT(context, plain.player(PlayerId::Player0).cracks == 0);
    EXPECT(context, trapped.player(PlayerId::Player0).cracks == 2);

    Scenario end_turn_scenario = base_scenario();
    end_turn_scenario.players[0].current_pp = 5;
    Game end_turn_game = scenario_game(end_turn_scenario);
    GameCommand end_turn;
    end_turn.player = PlayerId::Player0;
    end_turn.action = ActionKind::EndTurn;
    end_turn.expected_revision = end_turn_game.revision();
    const PaymentPreview end_turn_preview = end_turn_game.preview_payment(end_turn);
    EXPECT(context, end_turn_preview.status);
    EXPECT(context, end_turn_preview.current_pp_before == 5);
    EXPECT(context, end_turn_preview.current_pp_after == 5);
    EXPECT(context, end_turn_game.submit_command(end_turn));
    EXPECT(context, end_turn_game.player(PlayerId::Player0).current_pp == 0);
}

void test_transactional_failure_has_no_side_effects(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].hand = {cards::midrange::kPrecisionStrike};
    scenario.players[1].hand = {cards::advance::kBurnBlast};
    scenario.players[1].units = {cards::midrange::kGuardSentry};
    Game game = scenario_game(scenario);
    const MatchView before = game.make_view(PlayerId::Player0);
    const auto prior_events = game.read_events(PlayerId::Player0, 0);
    const std::uint64_t cursor = prior_events.empty() ? 0U : prior_events.back().sequence;

    GameCommand missing_slot;
    missing_slot.player = PlayerId::Player0;
    missing_slot.action = ActionKind::CastSpell;
    missing_slot.source = *before.players[0].hand[0].instance_id;
    missing_slot.target = Target::unit_target(
        PlayerId::Player1,
        *before.players[1].units[0]->instance_id);
    missing_slot.expected_revision = before.revision;
    EXPECT_CODE(context, game.preview_payment(missing_slot).status, ErrorCode::InvalidSlot);
    EXPECT_CODE(context, game.submit_command(missing_slot), ErrorCode::InvalidSlot);
    EXPECT(context, game.make_view(PlayerId::Player0).revision == before.revision);
    EXPECT(context, game.read_events(PlayerId::Player0, cursor).empty());

    GameCommand invalid;
    invalid.player = PlayerId::Player0;
    invalid.action = ActionKind::CastSpell;
    invalid.source = *before.players[0].hand[0].instance_id;
    invalid.target = Target::leader(PlayerId::Player1);
    invalid.slot = 0;
    invalid.expected_revision = before.revision;
    EXPECT_CODE(context, game.submit_command(invalid), ErrorCode::InvalidTarget);

    const MatchView after_invalid = game.make_view(PlayerId::Player0);
    EXPECT(context, after_invalid.revision == before.revision);
    EXPECT(context, after_invalid.players[0].current_pp == before.players[0].current_pp);
    EXPECT(context, after_invalid.players[0].hand_count == before.players[0].hand_count);
    EXPECT(context, game.read_events(PlayerId::Player0, cursor).empty());

    invalid.expected_revision = before.revision + 1U;
    EXPECT_CODE(context, game.submit_command(invalid), ErrorCode::StaleRevision);
    EXPECT(context, game.make_view(PlayerId::Player0).revision == before.revision);

    GameCommand hidden_probe;
    hidden_probe.player = PlayerId::Player0;
    hidden_probe.action = ActionKind::CastSpell;
    hidden_probe.source = *game.find_in_hand(PlayerId::Player1, cards::advance::kBurnBlast);
    hidden_probe.slot = 0;
    hidden_probe.expected_revision = game.revision();
    const PaymentPreview hidden_payment = game.preview_payment(hidden_probe);
    EXPECT_CODE(context, hidden_payment.status, ErrorCode::InvalidZone);
    EXPECT(context, hidden_payment.base_cost == 0);
    EXPECT(context, hidden_payment.burn_cost == 0);

    ActionQuery query;
    query.player = PlayerId::Player0;
    query.action = ActionKind::CastSpell;
    query.expected_revision = game.revision();
    const std::vector<LegalAction> spells = game.list_legal_actions(query);
    EXPECT(context, !spells.empty());
    if (!spells.empty()) {
        EXPECT(context, game.submit_command(spells.front().command));
        EXPECT(context, game.revision() == before.revision + 1U);
    }
}

void test_event_redaction_and_independent_cursors(TestContext& context) {
    Scenario scenario = base_scenario();
    scenario.players[0].hand = {cards::midrange::kPioneerScout};
    scenario.players[0].tactics = {cards::midrange::kInterceptTrap};
    scenario.players[1].tactics = {cards::advance::kReactionTrap};
    Game game = scenario_game(scenario);

    const auto p0_first = game.read_events(PlayerId::Player0, 0);
    const auto p1_first = game.read_events(PlayerId::Player1, 0);
    EXPECT(context, p0_first.size() == p1_first.size());
    EXPECT(context, !p0_first.empty());

    bool saw_hidden_for_p0 = false;
    bool saw_visible_for_p1 = false;
    for (const GameEventView& event : p0_first) {
        if (event.type == EventType::CardMoved && event.player == PlayerId::Player1) {
            saw_hidden_for_p0 = event.hidden_card && !event.card.has_value() &&
                                !event.definition_id.has_value() &&
                                event.text.find("Reaction") == std::string::npos &&
                                event.text.find("2012") == std::string::npos;
        }
    }
    for (const GameEventView& event : p1_first) {
        if (event.type == EventType::CardMoved && event.player == PlayerId::Player1) {
            saw_visible_for_p1 = !event.hidden_card && event.card.has_value() &&
                                 event.definition_id == cards::advance::kReactionTrap;
        }
    }
    EXPECT(context, saw_hidden_for_p0);
    EXPECT(context, saw_visible_for_p1);

    const std::uint64_t p0_cursor = p0_first.back().sequence;
    EXPECT(context, game.read_events(PlayerId::Player0, p0_cursor).empty());
    EXPECT(context, game.read_events(PlayerId::Player1, 0).size() == p1_first.size());

    GameCommand play_entry_unit;
    play_entry_unit.player = PlayerId::Player0;
    play_entry_unit.action = ActionKind::PlayUnit;
    play_entry_unit.source = *game.find_in_hand(PlayerId::Player0, cards::midrange::kPioneerScout);
    play_entry_unit.slot = 0U;
    play_entry_unit.expected_revision = game.revision();
    EXPECT(context, game.submit_command(play_entry_unit));
    const ReactionContext reaction = game.get_reaction_context(PlayerId::Player1);
    EXPECT(context, reaction.pending);
    EXPECT(context, reaction.eligible_traps.size() == 1U);
    EXPECT(context, reaction.origin.has_value());
    if (reaction.origin.has_value()) {
        EXPECT(context, reaction.origin->action == ActionKind::PlayUnit);
        EXPECT(context, reaction.origin->player == PlayerId::Player0);
        EXPECT(context, reaction.origin->source == play_entry_unit.source);
        EXPECT(context, !reaction.origin->target.has_value());
    }
    if (!reaction.eligible_traps.empty()) {
        GameCommand activate;
        activate.player = PlayerId::Player1;
        activate.action = ActionKind::ActivateTrap;
        activate.source = *reaction.eligible_traps.front().instance_id;
        activate.expected_revision = game.revision();
        EXPECT(context, game.submit_command(activate));
    }
    bool late_read_kept_set_event_hidden = false;
    for (const GameEventView& event : game.read_events(PlayerId::Player0, 0)) {
        if (event.type == EventType::CardMoved && event.player == PlayerId::Player1 &&
            event.value == static_cast<int>(Zone::Tactic)) {
            late_read_kept_set_event_hidden = event.hidden_card && !event.card.has_value() &&
                                              !event.definition_id.has_value() &&
                                              event.text == "opponent set a trap";
        }
    }
    EXPECT(context, late_read_kept_set_event_hidden);

    GameConfig config;
    config.random_seed = 0xA5A5U;
    config.first_player_mode = FirstPlayerMode::Player1;
    config.shuffle_decks = false;
    Game started(make_v04_catalog(), make_midrange_deck(), make_advance_deck(), config);
    EXPECT(context, started.start());
    const auto start_events = started.read_events(PlayerId::Player0, 0);
    const auto match_started = std::find_if(start_events.begin(), start_events.end(), [](const GameEventView& event) {
        return event.type == EventType::MatchStarted;
    });
    EXPECT(context, match_started != start_events.end());
    if (match_started != start_events.end()) {
        EXPECT(context, match_started->random_seed == 0xA5A5U);
        EXPECT(context, match_started->first_player == PlayerId::Player1);
    }

    bool saw_hidden_draw = false;
    for (const GameEventView& event : started.read_events(PlayerId::Player0, 0)) {
        if (event.type == EventType::CardDrawn && event.player == PlayerId::Player1) {
            saw_hidden_draw = event.hidden_card && !event.card.has_value() &&
                              !event.definition_id.has_value();
        }
    }
    EXPECT(context, saw_hidden_draw);

    const MatchView owner_hand = started.make_view(PlayerId::Player0);
    EXPECT(context, !owner_hand.players[0].hand.empty());
    const std::uint64_t before_mulligan = start_events.back().sequence;
    const InstanceId selected_for_mulligan = *owner_hand.players[0].hand[0].instance_id;
    GameCommand mulligan;
    mulligan.player = PlayerId::Player0;
    mulligan.action = ActionKind::Mulligan;
    mulligan.mulligan_cards = {selected_for_mulligan};
    mulligan.expected_revision = owner_hand.revision;
    EXPECT(context, started.submit_command(mulligan));
    bool saw_safe_mulligan = false;
    bool leaked_replacement_draw = false;
    const auto opponent_mulligan_events = started.read_events(PlayerId::Player1, before_mulligan);
    for (const GameEventView& event : opponent_mulligan_events) {
        if (event.type == EventType::MulliganCompleted && event.player == PlayerId::Player0) {
            saw_safe_mulligan = event.hidden_card && !event.card.has_value() &&
                                !event.definition_id.has_value() && event.value == 0 &&
                                event.text == "opponent completed mulligan";
        }
        if (event.type == EventType::CardDrawn && event.player == PlayerId::Player0) {
            leaked_replacement_draw = true;
        }
    }
    EXPECT(context, saw_safe_mulligan);
    EXPECT(context, !leaked_replacement_draw);
    EXPECT(context, opponent_mulligan_events.size() == 1U);
    if (!opponent_mulligan_events.empty()) {
        EXPECT(context, opponent_mulligan_events.front().sequence == before_mulligan + 1U);
    }
    const MatchView owner_after_mulligan = started.make_view(PlayerId::Player0);
    EXPECT(context, owner_after_mulligan.players[0].hand_count == owner_hand.players[0].hand_count);
    EXPECT(context, std::none_of(
        owner_after_mulligan.players[0].hand.begin(), owner_after_mulligan.players[0].hand.end(),
        [selected_for_mulligan](const CardView& card) {
            return card.instance_id == selected_for_mulligan;
        }));
}

std::optional<GameCommand> choose_agent_action(const std::vector<LegalAction>& actions) {
    const auto choose = [&actions](const ActionKind action, const bool leader_only) -> std::optional<GameCommand> {
        for (const LegalAction& legal : actions) {
            if (legal.command.action != action) {
                continue;
            }
            if (leader_only &&
                (!legal.command.target.has_value() || legal.command.target->kind != Target::Kind::Leader)) {
                continue;
            }
            return legal.command;
        }
        return std::nullopt;
    };

    for (const auto& [action, leader_only] : {
             std::pair{ActionKind::Attack, true},
             std::pair{ActionKind::Attack, false},
             std::pair{ActionKind::Evolve, false},
             std::pair{ActionKind::PlayUnit, false},
             std::pair{ActionKind::CastSpell, false},
             std::pair{ActionKind::Deploy, false},
             std::pair{ActionKind::PlayTactic, false},
             std::pair{ActionKind::ActivateTrap, false},
             std::pair{ActionKind::PassReaction, false},
             std::pair{ActionKind::EndTurn, false},
             std::pair{ActionKind::Mulligan, false},
         }) {
        if (const auto command = choose(action, leader_only); command.has_value()) {
            return command;
        }
    }
    return std::nullopt;
}

void test_headless_agent_completes_fixed_deck_match(TestContext& context) {
    GameConfig config;
    config.random_seed = 0xC0DEC0DEU;
    config.first_player_mode = FirstPlayerMode::Player0;
    config.shuffle_decks = true;
    Game match(make_v04_catalog(), make_midrange_deck(), make_advance_deck(), config);
    EXPECT(context, match.start());

    std::array<std::uint64_t, kPlayerCount> cursors{};
    bool completed = false;
    std::uint64_t last_snapshot_revision = 0;
    for (int step = 0; step < 1200; ++step) {
        const MatchView player0_view = match.make_view(PlayerId::Player0);
        const MatchView player1_view = match.make_view(PlayerId::Player1);
        last_snapshot_revision = player0_view.revision;
        EXPECT(context, player0_view.revision == player1_view.revision);
        EXPECT(context, player0_view.players[1].hand.empty());
        EXPECT(context, player1_view.players[0].hand.empty());
        if (player0_view.result != GameResult::Ongoing) {
            completed = true;
            break;
        }

        PlayerId actor = player0_view.active_player;
        if (player0_view.phase == Phase::Mulligan) {
            actor = !player0_view.players[0].mulligan_done ? PlayerId::Player0 : PlayerId::Player1;
        } else if (player0_view.phase == Phase::Reaction) {
            actor = player0_view.reaction.responder;
        }

        ActionQuery query;
        query.player = actor;
        query.expected_revision = player0_view.revision;
        const std::vector<LegalAction> actions = match.list_legal_actions(query);
        const std::optional<GameCommand> selected = choose_agent_action(actions);
        EXPECT(context, selected.has_value());
        if (!selected.has_value()) {
            break;
        }
        const PaymentPreview preview = match.preview_payment(*selected);
        EXPECT(context, preview.status);
        EXPECT(context, match.submit_command(*selected));

        // The two viewers advance independent event cursors. The agent consumes
        // only redacted views and never reaches into Game/PlayerState.
        for (std::size_t index = 0; index < kPlayerCount; ++index) {
            const PlayerId viewer = static_cast<PlayerId>(index);
            const auto events = match.read_events(viewer, cursors[index]);
            if (!events.empty()) {
                cursors[index] = events.back().sequence;
            }
        }
    }
    EXPECT(context, completed);
    EXPECT(context, last_snapshot_revision > 0U);
}

} // namespace

int main() {
    TestContext context;
    test_snapshot_privacy(context);
    test_pending_spell_is_public_in_its_declared_slot(context);
    test_legal_actions_and_payment(context);
    test_payment_preview_is_cost_only_and_viewer_safe(context);
    test_transactional_failure_has_no_side_effects(context);
    test_event_redaction_and_independent_cursors(context);
    test_headless_agent_completes_fixed_deck_match(context);

    if (context.failures != 0) {
        std::cerr << context.failures << " of " << context.assertions
                  << " client API assertions failed\n";
        return 1;
    }
    std::cout << "client API contract passed: " << context.assertions << " assertions\n";
    return 0;
}
