// SPDX-License-Identifier: GPL-3.0-or-later
#include "scgs/client_api.hpp"

#include "scgs/game.hpp"

#include <algorithm>
#include <utility>

namespace scgs {

namespace {
namespace client_api_detail {

[[nodiscard]] ReactionContext get_reaction_context(
    const Game& game,
    PlayerId viewer,
    std::uint64_t revision);

namespace {

bool target_equal(const Target& lhs, const Target& rhs) noexcept {
    return lhs.kind == rhs.kind && lhs.player == rhs.player && lhs.unit == rhs.unit;
}

bool contains_id(const std::vector<InstanceId>& values, const InstanceId id) {
    return std::find(values.begin(), values.end(), id) != values.end();
}

bool instance_is_in_state(const Game& game, const InstanceId id) {
    if (id == 0) {
        return false;
    }
    for (std::size_t player_index = 0; player_index < kPlayerCount; ++player_index) {
        const PlayerState& state = game.player(static_cast<PlayerId>(player_index));
        if (contains_id(state.deck, id) || contains_id(state.hand, id) ||
            contains_id(state.graveyard, id) || contains_id(state.archive, id) ||
            contains_id(state.standby, id)) {
            return true;
        }
        for (const auto& slot : state.units) {
            if (slot.has_value() && *slot == id) {
                return true;
            }
        }
        for (const auto& slot : state.tactics) {
            if (slot.has_value() && *slot == id) {
                return true;
            }
        }
    }
    return false;
}

CardView card_view(const Game& game, const InstanceId id, const bool hide_identity) {
    const CardInstance& card = game.instance(id);
    CardView view;
    view.owner = card.owner;
    view.controller = card.controller;
    view.zone = card.zone;
    view.sequence = card.sequence;
    view.current_attack = card.current_attack;
    view.current_health = card.current_health;
    view.maximum_health = card.maximum_health;
    view.keywords = card.keywords;
    view.evolved = card.evolved;
    view.attacked_this_turn = card.attacked_this_turn;
    view.entered_this_turn = card.entered_this_turn;
    view.temporary_rush = card.temporary_rush;
    view.deployed_from_standby = card.deployed_from_standby;
    view.face_down = card.face_down;
    view.countdown = card.countdown;
    view.granted_component = card.granted_component;
    if (hide_identity) {
        return view;
    }

    const CardDefinition& definition = game.definition(id);
    view.instance_id = card.id;
    view.definition_id = definition.id;
    view.definition = definition;
    view.kind = definition.kind;
    view.name = definition.name;
    view.cost = definition.cost;
    return view;
}

PlayerView player_view(const Game& game, const PlayerId subject, const PlayerId viewer) {
    const PlayerState& state = game.player(subject);
    PlayerView view;
    view.player = subject;
    view.leader_health = state.leader_health;
    view.maximum_leader_health = state.maximum_leader_health;
    view.current_pp = state.current_pp;
    view.pp_capacity = state.pp_capacity;
    view.cracks = state.cracks;
    view.evolution_energy = state.evolution_points;
    view.own_turn_number = state.own_turn_number;
    view.fatigue_count = state.fatigue_count;
    view.mulligan_done = state.mulligan_done;
    view.evolution_used_this_turn = state.evolution_used_this_turn;
    view.advance_used_this_turn = state.advance_used_this_turn;
    view.deploy_used_this_turn = state.deploy_used_this_turn;
    view.trap_set_this_turn = state.trap_set_this_turn;
    view.leader_skill_used = state.leader_skill_used;
    view.charge_granted_this_cycle = state.charge_granted_this_cycle;
    view.friendly_deaths_this_cycle = state.friendly_deaths_this_cycle;
    view.spells_used_this_turn = state.spells_used_this_turn;
    view.units_played_this_turn = state.units_played_this_turn;
    view.leader_skill = state.leader_skill;
    view.deck_count = state.deck.size();
    view.hand_count = state.hand.size();

    if (subject == viewer) {
        view.hand.reserve(state.hand.size());
        for (const InstanceId id : state.hand) {
            view.hand.push_back(card_view(game, id, false));
        }
    }
    for (std::size_t index = 0; index < state.units.size(); ++index) {
        if (state.units[index].has_value()) {
            view.units[index] = card_view(game, *state.units[index], false);
        }
    }
    for (std::size_t index = 0; index < state.tactics.size(); ++index) {
        if (!state.tactics[index].has_value()) {
            continue;
        }
        const InstanceId id = *state.tactics[index];
        const bool hide_identity = subject != viewer && game.instance(id).face_down;
        view.tactics[index] = card_view(game, id, hide_identity);
    }

    const auto append_public_zone = [&](const std::vector<InstanceId>& source, std::vector<CardView>& destination) {
        destination.reserve(source.size());
        for (const InstanceId id : source) {
            destination.push_back(card_view(game, id, false));
        }
    };
    append_public_zone(state.graveyard, view.graveyard);
    append_public_zone(state.archive, view.archive);
    append_public_zone(state.standby, view.standby);
    return view;
}

bool command_matches_query(const GameCommand& command, const ActionQuery& query) {
    if (command.player != query.player) {
        return false;
    }
    if (query.action.has_value() && command.action != *query.action) {
        return false;
    }
    if (query.source.has_value() && command.source != *query.source) {
        return false;
    }
    if (query.target.has_value() &&
        (!command.target.has_value() || !target_equal(*command.target, *query.target))) {
        return false;
    }
    if (query.slot.has_value() && command.slot != query.slot) {
        return false;
    }
    if (query.component_donor.has_value() && command.component_donor != query.component_donor) {
        return false;
    }
    if (query.use_advance.has_value() && command.use_advance != *query.use_advance) {
        return false;
    }
    if (!query.mulligan_cards.empty() && command.mulligan_cards != query.mulligan_cards) {
        return false;
    }
    return true;
}

std::vector<std::optional<Target>> unit_target_options(
    const Game& game,
    const PlayerId actor,
    const CardDefinition& definition,
    const ActionKind action) {
    bool needs_enemy = false;
    bool needs_friendly = false;
    const auto include_trigger = [action](const EffectTrigger trigger) {
        switch (action) {
            case ActionKind::PlayUnit:
                return trigger == EffectTrigger::OnPlay || trigger == EffectTrigger::OnPlayIfAdvanced ||
                       trigger == EffectTrigger::OnPlayIfNotAdvanced || trigger == EffectTrigger::OnEntry;
            case ActionKind::CastSpell:
                return trigger == EffectTrigger::OnPlay || trigger == EffectTrigger::OnPlayIfAdvanced ||
                       trigger == EffectTrigger::OnPlayIfNotAdvanced;
            case ActionKind::Evolve:
                return trigger == EffectTrigger::OnEvolution;
            case ActionKind::Deploy:
                return trigger == EffectTrigger::OnEntry;
            default:
                return false;
        }
    };
    for (const EffectRecord& effect : definition.effects) {
        if (!include_trigger(effect.trigger)) {
            continue;
        }
        needs_enemy = needs_enemy || effect.target_spec == TargetSpec::EnemyUnit;
        needs_friendly = needs_friendly || effect.target_spec == TargetSpec::FriendlyUnit;
    }

    std::vector<std::optional<Target>> result;
    if (!needs_enemy && !needs_friendly) {
        result.push_back(std::nullopt);
        return result;
    }
    if (needs_enemy) {
        for (const auto& slot : game.player(opponent(actor)).units) {
            if (slot.has_value()) {
                result.emplace_back(Target::unit_target(opponent(actor), *slot));
            }
        }
    }
    if (needs_friendly) {
        for (const auto& slot : game.player(actor).units) {
            if (slot.has_value()) {
                result.emplace_back(Target::unit_target(actor, *slot));
            }
        }
    }
    return result;
}

template <typename Consumer>
void for_each_candidate(
    const Game& game,
    const ActionQuery& query,
    const std::uint64_t current_revision,
    Consumer&& consume) {
    if (!scgs::is_valid_player(query.player) || query.expected_revision != current_revision ||
        game.result() != GameResult::Ongoing) {
        return;
    }

    const PlayerId player = query.player;
    const PlayerState& own = game.player(player);
    const auto wants = [&query](const ActionKind action) {
        return !query.action.has_value() || *query.action == action;
    };
    const auto consider = [&](GameCommand command) {
        command.expected_revision = current_revision;
        if (command_matches_query(command, query)) {
            consume(std::move(command));
        }
    };

    if (game.phase() == Phase::Mulligan && wants(ActionKind::Mulligan)) {
        if (!query.mulligan_cards.empty()) {
            GameCommand command;
            command.player = player;
            command.action = ActionKind::Mulligan;
            command.mulligan_cards = query.mulligan_cards;
            consider(std::move(command));
        } else if (own.hand.size() <= 12U) {
            const std::uint64_t subset_count = std::uint64_t{1} << own.hand.size();
            for (std::uint64_t subset = 0; subset < subset_count; ++subset) {
                GameCommand command;
                command.player = player;
                command.action = ActionKind::Mulligan;
                for (std::size_t index = 0; index < own.hand.size(); ++index) {
                    if ((subset & (std::uint64_t{1} << index)) != 0U) {
                        command.mulligan_cards.push_back(own.hand[index]);
                    }
                }
                consider(std::move(command));
            }
        } else {
            GameCommand pass;
            pass.player = player;
            pass.action = ActionKind::Mulligan;
            consider(std::move(pass));
            for (const InstanceId id : own.hand) {
                GameCommand single;
                single.player = player;
                single.action = ActionKind::Mulligan;
                single.mulligan_cards.push_back(id);
                consider(std::move(single));
            }
        }
    }

    if (game.phase() == Phase::Reaction) {
        if (wants(ActionKind::ActivateTrap)) {
            for (const InstanceId id : game.eligible_traps()) {
                GameCommand command;
                command.player = player;
                command.action = ActionKind::ActivateTrap;
                command.source = id;
                consider(std::move(command));
            }
        }
        if (wants(ActionKind::PassReaction)) {
            GameCommand command;
            command.player = player;
            command.action = ActionKind::PassReaction;
            consider(std::move(command));
        }
    }

    if (game.phase() == Phase::Action) {
        for (const InstanceId id : own.hand) {
            const CardDefinition& definition = game.definition(id);
            if (definition.kind == CardKind::Unit && wants(ActionKind::PlayUnit)) {
                const auto targets = unit_target_options(game, player, definition, ActionKind::PlayUnit);
                for (std::size_t slot = 0; slot < kUnitZoneSize; ++slot) {
                    for (const auto& target : targets) {
                        for (const bool advance : {false, true}) {
                            if (advance && definition.cost <= own.current_pp) {
                                continue;
                            }
                            GameCommand command;
                            command.player = player;
                            command.action = ActionKind::PlayUnit;
                            command.source = id;
                            command.target = target;
                            command.slot = slot;
                            command.use_advance = advance;
                            consider(std::move(command));
                        }
                    }
                }
            } else if (definition.kind == CardKind::Spell && wants(ActionKind::CastSpell)) {
                const auto targets = unit_target_options(game, player, definition, ActionKind::CastSpell);
                for (std::size_t slot = 0; slot < kTacticZoneSize; ++slot) {
                    if (own.tactics[slot].has_value()) {
                        continue;
                    }
                    for (const auto& target : targets) {
                        for (const bool advance : {false, true}) {
                            if (advance && definition.cost <= own.current_pp) {
                                continue;
                            }
                            GameCommand command;
                            command.player = player;
                            command.action = ActionKind::CastSpell;
                            command.source = id;
                            command.target = target;
                            command.slot = slot;
                            command.use_advance = advance;
                            consider(std::move(command));
                        }
                    }
                }
            } else if ((definition.kind == CardKind::Relic || definition.kind == CardKind::Trap) &&
                       wants(ActionKind::PlayTactic)) {
                for (std::size_t slot = 0; slot < kTacticZoneSize; ++slot) {
                    for (const bool advance : {false, true}) {
                        if (advance && definition.cost <= own.current_pp) {
                            continue;
                        }
                        GameCommand command;
                        command.player = player;
                        command.action = ActionKind::PlayTactic;
                        command.source = id;
                        command.slot = slot;
                        command.use_advance = advance;
                        consider(std::move(command));
                    }
                }
            }
        }

        if (wants(ActionKind::Attack)) {
            for (const auto& attacker_slot : own.units) {
                if (!attacker_slot.has_value()) {
                    continue;
                }
                GameCommand leader_attack;
                leader_attack.player = player;
                leader_attack.action = ActionKind::Attack;
                leader_attack.source = *attacker_slot;
                leader_attack.target = Target::leader(opponent(player));
                consider(std::move(leader_attack));
                for (const auto& target_slot : game.player(opponent(player)).units) {
                    if (!target_slot.has_value()) {
                        continue;
                    }
                    GameCommand unit_attack;
                    unit_attack.player = player;
                    unit_attack.action = ActionKind::Attack;
                    unit_attack.source = *attacker_slot;
                    unit_attack.target = Target::unit_target(opponent(player), *target_slot);
                    consider(std::move(unit_attack));
                }
            }
        }

        if (wants(ActionKind::Evolve)) {
            for (const auto& slot : own.units) {
                if (!slot.has_value()) {
                    continue;
                }
                const InstanceId id = *slot;
                const auto targets = unit_target_options(game, player, game.definition(id), ActionKind::Evolve);
                for (const auto& target : targets) {
                    GameCommand command;
                    command.player = player;
                    command.action = ActionKind::Evolve;
                    command.source = id;
                    command.target = target;
                    consider(std::move(command));
                }
            }
        }

        if (wants(ActionKind::Deploy)) {
            std::vector<std::optional<InstanceId>> donors{std::nullopt};
            for (const auto& slot : own.units) {
                if (slot.has_value()) {
                    donors.emplace_back(*slot);
                }
            }
            for (const InstanceId id : own.standby) {
                const auto targets = unit_target_options(game, player, game.definition(id), ActionKind::Deploy);
                for (std::size_t slot = 0; slot < kUnitZoneSize; ++slot) {
                    for (const auto& donor : donors) {
                        for (const auto& target : targets) {
                            GameCommand command;
                            command.player = player;
                            command.action = ActionKind::Deploy;
                            command.source = id;
                            command.target = target;
                            command.slot = slot;
                            command.component_donor = donor;
                            consider(std::move(command));
                        }
                    }
                }
            }
        }

        if (wants(ActionKind::EndTurn)) {
            GameCommand command;
            command.player = player;
            command.action = ActionKind::EndTurn;
            consider(std::move(command));
        }
    }

    // Surrender is intentionally offered in mulligan, action and reaction, but
    // never before start or after the terminal result.
    if (game.phase() != Phase::NotStarted && game.phase() != Phase::Finished && wants(ActionKind::Surrender)) {
        GameCommand command;
        command.player = player;
        command.action = ActionKind::Surrender;
        consider(std::move(command));
    }
}

} // namespace

MatchView make_view(
    const Game& game,
    const PlayerId viewer,
    const std::uint64_t revision,
    const std::uint32_t random_seed,
    const PlayerId first_player) {
    MatchView view;
    view.viewer = viewer;
    view.active_player = game.active_player();
    view.first_player = first_player;
    view.random_seed = random_seed;
    view.phase = game.phase();
    view.result = game.result();
    view.revision = revision;
    if (!scgs::is_valid_player(viewer)) {
        return view;
    }
    for (std::size_t index = 0; index < kPlayerCount; ++index) {
        view.players[index] = player_view(game, static_cast<PlayerId>(index), viewer);
    }
    view.reaction = get_reaction_context(game, viewer, revision);
    return view;
}

Status dispatch_command(Game& game, const GameCommand& command) {
    if (!scgs::is_valid_player(command.player)) {
        return Status::error(ErrorCode::InvalidPlayer, "player id is outside the supported range");
    }
    switch (command.action) {
        case ActionKind::Mulligan:
            return game.mulligan(command.player, command.mulligan_cards);
        case ActionKind::PlayUnit:
            return game.play_unit(
                command.player, command.source, command.slot, command.target, command.use_advance);
        case ActionKind::CastSpell:
            if (!command.slot.has_value()) {
                return Status::error(ErrorCode::InvalidSlot, "casting a spell requires a tactic slot");
            }
            return game.cast_spell(
                command.player, command.source, *command.slot, command.target, command.use_advance);
        case ActionKind::PlayTactic:
            if (!command.slot.has_value()) {
                return Status::error(ErrorCode::InvalidSlot, "playing a tactic requires a slot");
            }
            return game.play_tactic(command.player, command.source, *command.slot, command.use_advance);
        case ActionKind::Attack:
            if (!command.target.has_value()) {
                return Status::error(ErrorCode::InvalidTarget, "attacking requires a target");
            }
            return game.attack(command.player, command.source, *command.target);
        case ActionKind::Evolve:
            return game.evolve(command.player, command.source, command.target);
        case ActionKind::Deploy:
            return game.deploy(
                command.player, command.source, command.slot, command.component_donor, command.target);
        case ActionKind::ActivateTrap:
            return game.activate_trap(command.player, command.source, command.target);
        case ActionKind::PassReaction:
            return game.pass_reaction(command.player);
        case ActionKind::EndTurn:
            return game.end_turn(command.player);
        case ActionKind::Surrender:
            return game.surrender(command.player);
    }
    return Status::error(ErrorCode::InvalidCard, "unknown action kind");
}

Status validate_command(
    const Game& game,
    const GameCommand& command,
    const std::uint64_t current_revision) {
    if (!scgs::is_valid_player(command.player)) {
        return Status::error(ErrorCode::InvalidPlayer, "player id is outside the supported range");
    }
    if (command.expected_revision != current_revision) {
        return Status::error(ErrorCode::StaleRevision, "command was created for an older game revision");
    }
    Game candidate = game;
    return dispatch_command(candidate, command);
}

std::vector<LegalAction> list_legal_actions(
    const Game& game,
    const ActionQuery& query,
    const std::uint64_t current_revision) {
    std::vector<LegalAction> actions;
    for_each_candidate(game, query, current_revision, [&](GameCommand command) {
        const Status status = validate_command(game, command, current_revision);
        if (!status) {
            return;
        }
        LegalAction action;
        action.payment = game.preview_payment(command);
        action.command = std::move(command);
        actions.push_back(std::move(action));
    });
    return actions;
}

std::vector<Target> list_valid_targets(
    const Game& game,
    const ActionQuery& query,
    const std::uint64_t current_revision) {
    std::vector<Target> targets;
    for (const LegalAction& action : list_legal_actions(game, query, current_revision)) {
        if (!action.command.target.has_value()) {
            continue;
        }
        const Target target = *action.command.target;
        const bool duplicate = std::any_of(targets.begin(), targets.end(), [&](const Target& existing) {
            return target_equal(existing, target);
        });
        if (!duplicate) {
            targets.push_back(target);
        }
    }
    return targets;
}

std::vector<std::size_t> list_valid_slots(
    const Game& game,
    const ActionQuery& query,
    const std::uint64_t current_revision) {
    std::vector<std::size_t> slots;
    for (const LegalAction& action : list_legal_actions(game, query, current_revision)) {
        if (action.command.slot.has_value() &&
            std::find(slots.begin(), slots.end(), *action.command.slot) == slots.end()) {
            slots.push_back(*action.command.slot);
        }
    }
    return slots;
}

std::vector<InstanceId> list_valid_donors(
    const Game& game,
    const ActionQuery& query,
    const std::uint64_t current_revision) {
    std::vector<InstanceId> donors;
    for (const LegalAction& action : list_legal_actions(game, query, current_revision)) {
        if (action.command.component_donor.has_value() &&
            std::find(donors.begin(), donors.end(), *action.command.component_donor) == donors.end()) {
            donors.push_back(*action.command.component_donor);
        }
    }
    return donors;
}

ReactionContext get_reaction_context(
    const Game& game,
    const PlayerId viewer,
    const std::uint64_t revision) {
    ReactionContext context;
    context.window = game.reaction_window();
    context.depth = game.response_depth();
    context.pending = game.phase() == Phase::Reaction && context.depth > 0U;
    context.revision = revision;
    if (!context.pending || !scgs::is_valid_player(viewer)) {
        return context;
    }
    const std::vector<InstanceId>& eligible = game.eligible_traps();
    context.eligible_count = eligible.size();
    if (!eligible.empty()) {
        context.responder = game.instance(eligible.front()).controller;
    }
    if (viewer == context.responder) {
        context.eligible_traps.reserve(eligible.size());
        for (const InstanceId id : eligible) {
            context.eligible_traps.push_back(card_view(game, id, false));
        }
    }
    return context;
}

GameEventView redact_event(
    const Game& game,
    const PlayerId viewer,
    const std::uint64_t sequence,
    const GameEvent& event,
    const std::uint32_t random_seed,
    const PlayerId first_player) {
    GameEventView view;
    view.sequence = sequence;
    view.type = event.type;
    view.player = event.player;
    view.value = event.value;
    view.secondary_value = event.secondary_value;
    view.text = event.text;
    if (event.type == EventType::MatchStarted) {
        view.random_seed = random_seed;
        view.first_player = first_player;
    }

    if (event.type == EventType::MulliganCompleted && viewer != event.player) {
        view.value = 0;
        view.secondary_value = 0;
        view.hidden_card = true;
        view.text = "opponent completed mulligan";
        return view;
    }

    bool hidden_card = false;
    if (event.card != 0 && viewer != event.player) {
        if (event.type == EventType::CardDrawn) {
            hidden_card = true;
        } else if (event.type == EventType::CardMoved &&
                   event.value == static_cast<int>(Zone::Tactic) &&
                   instance_is_in_state(game, event.card) &&
                   game.definition(event.card).kind == CardKind::Trap) {
            // Classify the event by its original semantic destination, not by
            // whether the trap has subsequently been revealed or moved.
            hidden_card = true;
        }
    }

    view.hidden_card = hidden_card;
    if (hidden_card) {
        view.text = event.type == EventType::CardDrawn ? "opponent drew a card" : "opponent set a trap";
        return view;
    }
    if (event.card != 0 && instance_is_in_state(game, event.card)) {
        view.card = event.card;
        view.definition_id = game.definition(event.card).id;
    }
    return view;
}

} // namespace client_api_detail
} // namespace

std::uint64_t Game::revision() const noexcept {
    return revision_;
}

MatchView Game::make_view(const PlayerId viewer) const {
    MatchView view = client_api_detail::make_view(
        *this, viewer, revision_, actual_seed_, first_player_);
    view.reaction = get_reaction_context(viewer);
    return view;
}

std::vector<LegalAction> Game::list_legal_actions(const ActionQuery& query) const {
    return client_api_detail::list_legal_actions(*this, query, revision_);
}

std::vector<Target> Game::list_valid_targets(const ActionQuery& query) const {
    return client_api_detail::list_valid_targets(*this, query, revision_);
}

std::vector<std::size_t> Game::list_valid_slots(const ActionQuery& query) const {
    return client_api_detail::list_valid_slots(*this, query, revision_);
}

std::vector<InstanceId> Game::list_valid_donors(const ActionQuery& query) const {
    return client_api_detail::list_valid_donors(*this, query, revision_);
}

PaymentPreview Game::preview_payment(const GameCommand& command) const {
    PaymentPreview preview;
    if (!is_valid_player(command.player)) {
        preview.status = Status::error(
            ErrorCode::InvalidPlayer, "player id is outside the supported range");
        return preview;
    }

    const PlayerState& before = player(command.player);
    preview.current_pp_before = before.current_pp;
    preview.current_pp_after = before.current_pp;
    preview.pp_capacity_before = before.pp_capacity;
    preview.pp_capacity_after = before.pp_capacity;
    preview.cracks_before = before.cracks;
    preview.cracks_after = before.cracks;
    preview.evolution_energy_before = before.evolution_points;
    preview.evolution_energy_after = before.evolution_points;

    preview.status = client_api_detail::validate_command(*this, command, revision_);
    if (!preview.status) {
        return preview;
    }

    PaymentProjection projection;
    switch (command.action) {
        case ActionKind::PlayUnit:
        case ActionKind::CastSpell:
        case ActionKind::PlayTactic: {
            const CardDefinition& card = definition(command.source);
            projection = project_payment(
                command.player,
                card.cost,
                card.additional_cost.burn_pp_capacity,
                /*allow_advance=*/true,
                command.use_advance);
            break;
        }
        case ActionKind::Deploy: {
            const CardDefinition& card = definition(command.source);
            projection = project_payment(
                command.player,
                card.deployment->pp_cost,
                /*burn_cost=*/0,
                /*allow_advance=*/false,
                /*use_advance=*/false);
            break;
        }
        case ActionKind::Evolve:
            projection = project_payment(
                command.player,
                /*base_cost=*/0,
                /*burn_cost=*/0,
                /*allow_advance=*/false,
                /*use_advance=*/false,
                /*evolution_energy_cost=*/2);
            break;
        case ActionKind::Mulligan:
        case ActionKind::Attack:
        case ActionKind::ActivateTrap:
        case ActionKind::PassReaction:
        case ActionKind::EndTurn:
        case ActionKind::Surrender:
            projection = project_payment(
                command.player,
                /*base_cost=*/0,
                /*burn_cost=*/0,
                /*allow_advance=*/false,
                /*use_advance=*/false);
            break;
    }

    if (!projection.status) {
        preview.status = projection.status;
        return preview;
    }
    preview.current_pp_after = projection.current_pp_after;
    preview.pp_capacity_after = projection.pp_capacity_after;
    preview.cracks_after = projection.cracks_after;
    preview.evolution_energy_after = projection.evolution_energy_after;
    preview.base_cost = projection.base_cost;
    preview.burn_cost = projection.burn_cost;
    preview.advance_cost = projection.advance_cost;
    preview.used_advance = projection.used_advance;
    return preview;
}

ReactionContext Game::get_reaction_context(const PlayerId viewer) const {
    if (!is_valid_player(viewer)) {
        return {};
    }
    ReactionContext context = client_api_detail::get_reaction_context(*this, viewer, revision_);
    if (!response_stack_.empty()) {
        context.responder = response_stack_.back().responder;
        context.subject = response_stack_.back().subject;

        const SuspendedAction& suspended = response_stack_.front().suspended;
        ReactionOrigin origin;
        origin.player = suspended.player;
        origin.source = suspended.card;
        origin.target = suspended.target;
        switch (suspended.kind) {
            case SuspendedAction::Kind::Spell:
                origin.action = ActionKind::CastSpell;
                context.origin = std::move(origin);
                break;
            case SuspendedAction::Kind::EntryEffect:
                origin.action = instances_.at(suspended.card).deployed_from_standby
                                    ? ActionKind::Deploy
                                    : ActionKind::PlayUnit;
                context.origin = std::move(origin);
                break;
            case SuspendedAction::Kind::Attack:
                origin.action = ActionKind::Attack;
                origin.source = suspended.attack.attacker;
                origin.target = suspended.attack.target;
                context.origin = std::move(origin);
                break;
            case SuspendedAction::Kind::None:
                break;
        }
    }
    return context;
}

Status Game::submit_command(const GameCommand& command) {
    if (!is_valid_player(command.player)) {
        return Status::error(ErrorCode::InvalidPlayer, "player id is outside the supported range");
    }
    if (command.expected_revision != revision_) {
        return Status::error(ErrorCode::StaleRevision, "command was created for an older game revision");
    }

    Game candidate = *this;
    const Status status = client_api_detail::dispatch_command(candidate, command);
    if (!status) {
        return status;
    }
    candidate.revision_ = revision_ + 1U;
    *this = std::move(candidate);
    return Status::ok();
}

std::vector<GameEventView> Game::read_events(
    const PlayerId viewer,
    const std::uint64_t after_sequence) const {
    std::vector<GameEventView> result;
    if (!is_valid_player(viewer)) {
        return result;
    }
    for (const GameEvent& event : event_history_) {
        if (event.sequence <= after_sequence) {
            continue;
        }
        result.push_back(client_api_detail::redact_event(
            *this, viewer, event.sequence, event, actual_seed_, first_player_));
    }
    return result;
}

} // namespace scgs
