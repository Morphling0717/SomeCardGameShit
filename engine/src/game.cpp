// SPDX-License-Identifier: GPL-3.0-or-later
#include "scgs/game.hpp"

#include <algorithm>
#include <limits>
#include <set>
#include <sstream>
#include <stdexcept>
#include <utility>

namespace scgs {

namespace {

std::uint32_t resolve_random_seed(const std::optional<std::uint32_t> configured_seed) {
    if (configured_seed.has_value()) {
        return *configured_seed;
    }
    std::random_device entropy;
    const std::uint32_t first = static_cast<std::uint32_t>(entropy());
    const std::uint32_t second = static_cast<std::uint32_t>(entropy());
    return first ^ (second << 1U) ^ 0x5C6A2026U;
}

// Map CardDefinition boolean flags to KeywordMask at unit creation time so the
// wire layer can serialise them without knowing the card-definition fields.
KeywordMask flags_to_keywords(const CardDefinition& def) {
    KeywordMask kw = mask(Keyword::None);
    if (def.printed_guard) { kw |= mask(Keyword::Guard); }
    if (def.printed_rush) { kw |= mask(Keyword::Rush); }
    if (def.printed_storm) { kw |= mask(Keyword::Storm); }
    if (def.printed_barrier) { kw |= mask(Keyword::Barrier); }
    if (def.printed_lifesteal) { kw |= mask(Keyword::Lifesteal); }
    if (def.printed_bane) { kw |= mask(Keyword::Bane); }
    return kw;
}

} // namespace

Game::Game(CardCatalog catalog, DeckList player0_deck, DeckList player1_deck, GameConfig config)
    : catalog_(std::move(catalog)),
      deck_lists_{std::move(player0_deck), std::move(player1_deck)},
      config_(config),
      actual_seed_(resolve_random_seed(config.random_seed)),
      rng_(actual_seed_) {
    switch (config_.first_player_mode) {
        case FirstPlayerMode::Player0:
            first_player_ = PlayerId::Player0;
            break;
        case FirstPlayerMode::Player1:
            first_player_ = PlayerId::Player1;
            break;
        case FirstPlayerMode::Random:
            first_player_ = std::uniform_int_distribution<int>(0, 1)(rng_) == 0
                ? PlayerId::Player0
                : PlayerId::Player1;
            break;
    }
    active_player_ = first_player_;
}

Status Game::start() {
    if (phase_ != Phase::NotStarted) {
        return Status::error(ErrorCode::MatchAlreadyStarted, "match has already started");
    }
    initialize_decks();
    phase_ = Phase::Mulligan;
    emit(EventType::MatchStarted, first_player_, 0, config_.leader_health, config_.starting_hand_size);
    events_.back().random_seed = actual_seed_;
    event_history_.back().random_seed = actual_seed_;
    for (std::size_t index = 0; index < kPlayerCount; ++index) {
        if (result_ != GameResult::Ongoing) {
            break;
        }
        draw_cards(static_cast<PlayerId>(index), config_.starting_hand_size);
    }
    return Status::ok();
}

Status Game::mulligan(const PlayerId player_id, const std::vector<InstanceId>& selected_cards) {
    if (!is_valid_player(player_id)) {
        return Status::error(ErrorCode::InvalidPlayer, "player id is outside the supported range");
    }
    const Status unfinished = ensure_not_finished();
    if (!unfinished) {
        return unfinished;
    }
    if (phase_ != Phase::Mulligan) {
        return Status::error(ErrorCode::InvalidPhase, "mulligan is only available before the first turn");
    }
    PlayerState& state = players_[to_index(player_id)];
    if (state.mulligan_done) {
        return Status::error(ErrorCode::MulliganAlreadyDone, "this player already completed the mulligan");
    }

    std::set<InstanceId> unique;
    for (const InstanceId id : selected_cards) {
        if (!unique.insert(id).second) {
            return Status::error(ErrorCode::DuplicateSelection, "the same card was selected twice");
        }
        if (!vector_contains(state.hand, id)) {
            return Status::error(ErrorCode::InvalidCard, "mulligan selection is not in the player's hand");
        }
    }
    if (state.deck.size() < selected_cards.size()) {
        return Status::error(ErrorCode::InvalidCard, "not enough cards remain to replace the mulligan selection");
    }

    std::vector<InstanceId> set_aside;
    set_aside.reserve(selected_cards.size());
    for (const InstanceId id : selected_cards) {
        move_from_current_zone(id);
        instances_.at(id).zone = Zone::None;
        set_aside.push_back(id);
    }

    // The post-command snapshot supplies the replacement identities. Avoid
    // per-card events here so an opponent cannot infer the selected count from
    // either event cardinality or gaps in the global sequence.
    draw_cards(player_id, static_cast<int>(set_aside.size()), false);

    for (const InstanceId id : set_aside) {
        CardInstance& card = instances_.at(id);
        card.zone = Zone::Deck;
        card.controller = player_id;
        card.sequence = state.deck.size();
        state.deck.push_back(id);
    }
    if (config_.shuffle_decks) {
        std::shuffle(state.deck.begin(), state.deck.end(), rng_);
        normalize_sequences(player_id, Zone::Deck);
    }

    state.mulligan_done = true;
    emit(EventType::MulliganCompleted, player_id, 0, static_cast<int>(selected_cards.size()), 0,
         "mulligan completed");
    if (players_[0].mulligan_done && players_[1].mulligan_done) {
        begin_turn(first_player_);
    }
    return Status::ok();
}

Status Game::end_turn(const PlayerId player_id) {
    const Status allowed = ensure_action_player(player_id);
    if (!allowed) {
        return allowed;
    }
    PlayerState& state = players_[to_index(player_id)];

    // v0.4 §23: class charge condition evaluated at the end of the player's own
    // turn (SpellsNoUnitsThisTurn archetype), at most once per turn cycle.
    const DeckList& deck_list = deck_lists_[to_index(player_id)];
    if (evolution_is_unlocked(player_id) &&
        !state.charge_granted_this_cycle &&
        deck_list.charge_condition == ChargeCondition::SpellsNoUnitsThisTurn &&
        state.spells_used_this_turn >= deck_list.charge_amount &&
        state.units_played_this_turn == 0) {
        grant_evolution_energy(player_id, 1);
        state.charge_granted_this_cycle = true;
    }

    // End-of-turn cleanup is observable and happens before TurnEnded.
    clear_end_of_turn_state(player_id);
    state.current_pp = 0;
    emit(EventType::PPChanged, player_id, 0, state.current_pp, state.pp_capacity);
    emit(EventType::TurnEnded, player_id, 0, state.own_turn_number);
    if (result_ != GameResult::Ongoing) {
        return Status::ok();
    }
    begin_turn(opponent(player_id));
    return Status::ok();
}

Status Game::surrender(const PlayerId player_id) {
    if (!is_valid_player(player_id)) {
        return Status::error(ErrorCode::InvalidPlayer, "player id is outside the supported range");
    }
    const Status allowed = ensure_not_finished();
    if (!allowed) {
        return allowed;
    }
    result_ = player_id == PlayerId::Player0 ? GameResult::Player1Won : GameResult::Player0Won;
    emit(EventType::PlayerSurrendered, player_id);
    finalize_match_end();
    return Status::ok();
}

Game::PaymentProjection Game::project_payment(
    const PlayerId player_id,
    const int base_cost,
    const int burn_cost,
    const bool allow_advance,
    const bool use_advance,
    const int evolution_energy_cost) const {
    PaymentProjection projection;
    if (!is_valid_player(player_id)) {
        projection.status = Status::error(
            ErrorCode::InvalidPlayer, "player id is outside the supported range");
        return projection;
    }

    const PlayerState& state = players_[to_index(player_id)];
    projection.current_pp_after = state.current_pp;
    projection.pp_capacity_after = state.pp_capacity;
    projection.cracks_after = state.cracks;
    projection.evolution_energy_after = state.evolution_points;
    projection.base_cost = base_cost;
    projection.burn_cost = burn_cost;

    if (evolution_energy_cost > state.evolution_points) {
        projection.status = Status::error(
            ErrorCode::NoEvolutionPoints, "not enough evolution energy");
        return projection;
    }

    const int advance_needed = std::max(0, base_cost - state.current_pp);
    if (advance_needed > 0 && (!allow_advance || !use_advance)) {
        projection.status = Status::error(
            ErrorCode::InsufficientPP,
            allow_advance ? "not enough PP; use advance to pay the rest"
                          : "not enough PP for this payment");
        return projection;
    }

    const bool will_advance = allow_advance && use_advance && advance_needed > 0;
    if (will_advance && state.advance_used_this_turn) {
        projection.status = Status::error(
            ErrorCode::AdvanceAlreadyUsed, "advance (动用未来) already used this turn");
        return projection;
    }
    if (burn_cost > 0 && !will_advance && state.advance_used_this_turn) {
        projection.status = Status::error(
            ErrorCode::AdvanceAlreadyUsed, "burn counts as advance; already used this turn");
        return projection;
    }

    const int total_capacity_loss = (will_advance ? advance_needed : 0) + burn_cost;
    if (state.pp_capacity - total_capacity_loss < 0) {
        projection.status = Status::error(
            ErrorCode::AdvanceWouldExceedCap, "PP capacity would fall below zero");
        return projection;
    }

    projection.current_pp_after = std::max(0, state.current_pp - base_cost);
    projection.pp_capacity_after = state.pp_capacity - total_capacity_loss;
    projection.cracks_after = state.cracks + total_capacity_loss;
    projection.evolution_energy_after = state.evolution_points - evolution_energy_cost;
    projection.advance_cost = will_advance ? advance_needed : 0;
    projection.used_advance = will_advance;
    projection.consumes_advance = total_capacity_loss > 0;
    projection.status = Status::ok();
    return projection;
}

// Pay the total cost of a card (base cost + burn), applying advance if requested.
// v0.4 §9/§10/§12: advance reduces pp_capacity by the deficit; burn always
// reduces capacity; both generate cracks and share the once-per-turn 动用未来.
Status Game::pay_card_cost(
    const PlayerId player_id,
    const CardDefinition& def,
    const bool use_advance,
    bool& out_advanced) {
    const PaymentProjection projection = project_payment(
        player_id,
        def.cost,
        def.additional_cost.burn_pp_capacity,
        /*allow_advance=*/true,
        use_advance);
    if (!projection.status) {
        return projection.status;
    }

    PlayerState& state = players_[to_index(player_id)];
    state.current_pp = projection.current_pp_after;
    if (projection.consumes_advance) {
        state.pp_capacity = projection.pp_capacity_after;
        state.cracks = projection.cracks_after;
        state.advance_used_this_turn = true;
        emit(EventType::CracksChanged, player_id, 0, state.cracks, state.pp_capacity);
    }

    out_advanced = projection.used_advance;
    emit(EventType::PPChanged, player_id, 0, state.current_pp, state.pp_capacity);
    return Status::ok();
}

Status Game::play_unit(
    const PlayerId player_id,
    const InstanceId card_id,
    const std::optional<std::size_t> preferred_slot,
    const std::optional<Target> ability_target,
    const bool use_advance) {
    const Status allowed = ensure_action_player(player_id);
    if (!allowed) {
        return allowed;
    }
    const auto iterator = instances_.find(card_id);
    if (iterator == instances_.end()) {
        return Status::error(ErrorCode::InvalidCard, "unknown card instance");
    }
    const CardInstance& card = iterator->second;
    const CardDefinition& def = catalog_.at(card.definition_id);
    if (card.controller != player_id || card.zone != Zone::Hand || def.kind != CardKind::Unit) {
        return Status::error(ErrorCode::InvalidZone, "only a unit in the active player's hand can be played");
    }

    PlayerState& state = players_[to_index(player_id)];
    const int advance_needed = std::max(0, def.cost - state.current_pp);
    if (advance_needed > 0 && !use_advance) {
        return Status::error(ErrorCode::InsufficientPP, "not enough PP");
    }

    std::optional<std::size_t> slot = preferred_slot;
    if (slot.has_value()) {
        if (*slot >= kUnitZoneSize || state.units[*slot].has_value()) {
            return Status::error(ErrorCode::InvalidSlot, "requested unit slot is unavailable");
        }
    } else {
        slot = first_free_unit_slot(player_id);
    }
    if (!slot.has_value()) {
        return Status::error(ErrorCode::UnitZoneFull, "unit zone is full");
    }

    // Validate every trigger that will resolve before committing payment.
    const bool will_advance = use_advance && advance_needed > 0;
    const Status target_status = validate_effect_targets(
        def.effects,
        {EffectTrigger::OnEntry, EffectTrigger::OnPlay,
         will_advance ? EffectTrigger::OnPlayIfAdvanced : EffectTrigger::OnPlayIfNotAdvanced},
        player_id,
        ability_target);
    if (!target_status) {
        return target_status;
    }

    bool advanced = false;
    const Status payment = pay_card_cost(player_id, def, use_advance, advanced);
    if (!payment) {
        return payment;
    }

    ++state.units_played_this_turn;
    put_in_unit_slot(player_id, card_id, *slot);
    emit(EventType::UnitEntered, player_id, card_id, static_cast<int>(*slot));

    // Play-time conditional triggers (超前/按期) resolve immediately.
    const Status play_status = resolve_effects(
        def.effects,
        advanced ? EffectTrigger::OnPlayIfAdvanced : EffectTrigger::OnPlayIfNotAdvanced,
        player_id,
        card_id,
        ability_target,
        advanced);
    if (!play_status) {
        return play_status;
    }
    const Status always_play = resolve_effects(def.effects, EffectTrigger::OnPlay, player_id, card_id, ability_target, advanced);
    if (!always_play) {
        return always_play;
    }

    // v0.4 §26: the entry effect is a responseable event (登场效果准备结算).
    const bool has_entry_effects = [&] {
        for (const auto& rec : def.effects) {
            if (rec.trigger == EffectTrigger::OnEntry) {
                return true;
            }
        }
        return false;
    }();
    if (has_entry_effects && result_ == GameResult::Ongoing) {
        SuspendedAction suspended;
        suspended.kind = SuspendedAction::Kind::EntryEffect;
        suspended.player = player_id;
        suspended.card = card_id;
        suspended.target = ability_target;
        suspended.advanced = advanced;
        open_response_window(ReactionWindow::EntryEffectPending, opponent(player_id), card_id, suspended);
    }
    resolve_deaths();
    evaluate_result();
    return Status::ok();
}

Status Game::cast_spell(
    const PlayerId player_id,
    const InstanceId card_id,
    const std::size_t slot,
    const std::optional<Target> ability_target,
    const bool use_advance) {
    const Status allowed = ensure_action_player(player_id);
    if (!allowed) {
        return allowed;
    }
    const auto iterator = instances_.find(card_id);
    if (iterator == instances_.end()) {
        return Status::error(ErrorCode::InvalidCard, "unknown card instance");
    }
    const CardInstance& card = iterator->second;
    const CardDefinition& def = catalog_.at(card.definition_id);
    if (card.controller != player_id || card.zone != Zone::Hand || def.kind != CardKind::Spell) {
        return Status::error(ErrorCode::InvalidZone, "only a spell in the active player's hand can be cast");
    }

    PlayerState& state = players_[to_index(player_id)];
    const Status slot_status = validate_empty_tactic_slot(player_id, slot);
    if (!slot_status) {
        return slot_status;
    }
    const int advance_needed = std::max(0, def.cost - state.current_pp);
    if (advance_needed > 0 && !use_advance) {
        return Status::error(ErrorCode::InsufficientPP, "not enough PP");
    }

    const bool will_advance = use_advance && advance_needed > 0;
    const Status target_status = validate_effect_targets(
        def.effects,
        {EffectTrigger::OnPlay,
         will_advance ? EffectTrigger::OnPlayIfAdvanced : EffectTrigger::OnPlayIfNotAdvanced},
        player_id,
        ability_target);
    if (!target_status) {
        return target_status;
    }

    bool advanced = false;
    const Status payment = pay_card_cost(player_id, def, use_advance, advanced);
    if (!payment) {
        return payment;
    }

    ++state.spells_used_this_turn;
    put_in_tactic_slot(player_id, card_id, slot);

    // v0.4 §26: spell use opens a response window; the spell's own effects
    // resolve after the response chain (后加入者先结算). The face-up spell
    // continues to occupy its declared tactic slot until its own chain link
    // resolves or terminal cleanup aborts the chain.
    SuspendedAction suspended;
    suspended.kind = SuspendedAction::Kind::Spell;
    suspended.player = player_id;
    suspended.card = card_id;
    suspended.tactic_slot = slot;
    suspended.target = ability_target;
    suspended.advanced = advanced;
    open_response_window(ReactionWindow::SpellDeclared, opponent(player_id), card_id, suspended);
    resolve_deaths();
    evaluate_result();
    return Status::ok();
}

Status Game::play_tactic(
    const PlayerId player_id,
    const InstanceId card_id,
    const std::size_t slot,
    const bool use_advance) {
    const Status allowed = ensure_action_player(player_id);
    if (!allowed) {
        return allowed;
    }
    const auto iterator = instances_.find(card_id);
    if (iterator == instances_.end()) {
        return Status::error(ErrorCode::InvalidCard, "unknown card instance");
    }
    const CardInstance& card = iterator->second;
    const CardDefinition& def = catalog_.at(card.definition_id);
    if (card.controller != player_id || card.zone != Zone::Hand ||
        (def.kind != CardKind::Relic && def.kind != CardKind::Trap)) {
        return Status::error(ErrorCode::InvalidZone, "only a relic or trap in hand can enter the tactic zone");
    }

    PlayerState& state = players_[to_index(player_id)];
    if (def.kind == CardKind::Trap && state.trap_set_this_turn) {
        return Status::error(ErrorCode::TrapAlreadySetThisTurn, "only one trap may be set per turn");
    }
    const Status slot_status = validate_empty_tactic_slot(player_id, slot);
    if (!slot_status) {
        return slot_status;
    }

    const int advance_needed = std::max(0, def.cost - state.current_pp);
    if (advance_needed > 0 && !use_advance) {
        return Status::error(ErrorCode::InsufficientPP, "not enough PP");
    }

    bool advanced = false;
    const Status payment = pay_card_cost(player_id, def, use_advance, advanced);
    if (!payment) {
        return payment;
    }

    put_in_tactic_slot(player_id, card_id, slot);
    if (def.kind == CardKind::Trap) {
        state.trap_set_this_turn = true;
    }
    return Status::ok();
}

Status Game::deploy(
    const PlayerId player_id,
    const InstanceId standby_card_id,
    const std::optional<std::size_t> preferred_slot,
    const std::optional<InstanceId> component_donor,
    const std::optional<Target> ability_target) {
    const Status allowed = ensure_action_player(player_id);
    if (!allowed) {
        return allowed;
    }
    PlayerState& state = players_[to_index(player_id)];
    if (state.deploy_used_this_turn) {
        return Status::error(ErrorCode::DeployAlreadyUsed, "deployment was already used this turn");
    }
    const auto iterator = instances_.find(standby_card_id);
    if (iterator == instances_.end()) {
        return Status::error(ErrorCode::InvalidCard, "unknown standby card instance");
    }
    const CardInstance& card = iterator->second;
    const CardDefinition& def = catalog_.at(card.definition_id);
    if (card.controller != player_id || card.zone != Zone::Standby || def.kind != CardKind::Unit) {
        return Status::error(ErrorCode::InvalidZone, "only a unit in the player's standby zone can be deployed");
    }
    if (!def.deployment.has_value()) {
        return Status::error(ErrorCode::InvalidDeployment, "standby card does not carry a deployment specification");
    }
    const DeploymentSpec& spec = *def.deployment;

    if (!deployment_condition_met(player_id, spec)) {
        return Status::error(ErrorCode::DeployConditionNotMet, "deployment condition is not satisfied");
    }
    // v0.4 §10.8/§25: deployment never uses advance.
    const PaymentProjection payment = project_payment(
        player_id,
        spec.pp_cost,
        /*burn_cost=*/0,
        /*allow_advance=*/false,
        /*use_advance=*/false);
    if (!payment.status) {
        // Preserve the deployment-specific public error text while sharing the
        // actual affordability projection with previews and execution.
        return Status::error(ErrorCode::InsufficientPP, "not enough PP for the deployment");
    }

    std::optional<std::size_t> slot = preferred_slot;
    if (slot.has_value()) {
        if (*slot >= kUnitZoneSize) {
            return Status::error(ErrorCode::InvalidSlot, "requested unit slot is out of range");
        }
        const bool occupied_by_donor = spec.archive_one_friendly_unit && component_donor.has_value() &&
            state.units[*slot].has_value() && *state.units[*slot] == *component_donor;
        if (state.units[*slot].has_value() && !occupied_by_donor) {
            return Status::error(ErrorCode::InvalidSlot, "requested unit slot is unavailable");
        }
    } else {
        slot = first_free_unit_slot(player_id);
    }
    // v0.4 §5: a unit archived as deployment cost may free the required slot.
    if (!slot.has_value()) {
        if (!(spec.archive_one_friendly_unit && component_donor.has_value() &&
              is_controlled_unit(player_id, *component_donor))) {
            return Status::error(ErrorCode::UnitZoneFull, "unit zone is full");
        }
        for (std::size_t index = 0; index < kUnitZoneSize; ++index) {
            if (state.units[index].has_value() && *state.units[index] == *component_donor) {
                slot = index;
                break;
            }
        }
        if (!slot.has_value()) {
            return Status::error(ErrorCode::UnitZoneFull, "unit zone is full");
        }
    }

    // Validate the component donor and entry targets before paying.
    if (spec.archive_one_friendly_unit) {
        if (!component_donor.has_value() || !is_controlled_unit(player_id, *component_donor)) {
            return Status::error(ErrorCode::InvalidDeployment, "deployment requires a friendly unit to archive");
        }
    } else if (component_donor.has_value()) {
        return Status::error(ErrorCode::InvalidDeployment, "this deployment does not archive a unit");
    }
    const Status target_status = validate_effect_targets(
        def.effects, {EffectTrigger::OnEntry}, player_id, ability_target);
    if (!target_status) {
        return target_status;
    }

    // Commit: pay PP, archive the donor (its printed component is granted).
    state.current_pp = payment.current_pp_after;
    emit(EventType::PPChanged, player_id, 0, state.current_pp, state.pp_capacity);

    ComponentSpec granted;
    if (component_donor.has_value()) {
        const CardDefinition& donor_def = definition(*component_donor);
        if (donor_def.component.has_component) {
            granted = donor_def.component;
        }
        put_in_archive(player_id, *component_donor);
    }

    state.deploy_used_this_turn = true;
    put_in_unit_slot(player_id, standby_card_id, *slot, /*deployed_from_standby=*/true);
    if (granted.has_component) {
        // v0.4 §31: at most one component per deployment; printed-only (no re-transfer).
        CardInstance& deployed = instances_.at(standby_card_id);
        deployed.granted_component = granted;
        // Apply the component's bounded vocabulary effect.
        if (granted.granted_kind == EffectKind::GrantRush) {
            deployed.temporary_rush = true; // may attack enemy units this turn
        }
    }
    emit(EventType::UnitDeployed, player_id, standby_card_id, static_cast<int>(*slot));
    emit(EventType::UnitEntered, player_id, standby_card_id, static_cast<int>(*slot));

    // Entry effects are responseable, exactly like hand-played units.
    const bool has_entry_effects = [&] {
        for (const auto& rec : def.effects) {
            if (rec.trigger == EffectTrigger::OnEntry) {
                return true;
            }
        }
        return false;
    }();
    if (has_entry_effects && result_ == GameResult::Ongoing) {
        SuspendedAction suspended;
        suspended.kind = SuspendedAction::Kind::EntryEffect;
        suspended.player = player_id;
        suspended.card = standby_card_id;
        suspended.target = ability_target;
        suspended.advanced = false; // deployment can never be advanced
        open_response_window(ReactionWindow::EntryEffectPending, opponent(player_id), standby_card_id, suspended);
    }
    resolve_deaths();
    evaluate_result();
    return Status::ok();
}

Status Game::attack(const PlayerId player_id, const InstanceId attacker_id, const Target target) {
    const Status validation = validate_attack(player_id, attacker_id, target);
    if (!validation) {
        return validation;
    }

    CardInstance& attacker = instances_.at(attacker_id);
    attacker.attacked_this_turn = true;
    attacker.keywords &= ~mask(Keyword::Ambush);
    emit(EventType::AttackDeclared, player_id, attacker_id, static_cast<int>(target.kind), static_cast<int>(target.player));

    PendingAttack pending_attack{player_id, attacker_id, target};
    const InstanceId subject = target.kind == Target::Kind::Unit ? target.unit : 0;

    SuspendedAction suspended;
    suspended.kind = SuspendedAction::Kind::Attack;
    suspended.player = player_id;
    suspended.card = attacker_id;
    suspended.attack = pending_attack;
    open_response_window(ReactionWindow::AttackDeclared, opponent(player_id), subject, suspended);
    return Status::ok();
}

Status Game::evolve(
    const PlayerId player_id,
    const InstanceId unit_id,
    const std::optional<Target> ability_target) {
    const Status allowed = ensure_action_player(player_id);
    if (!allowed) {
        return allowed;
    }
    if (!is_controlled_unit(player_id, unit_id)) {
        return Status::error(ErrorCode::InvalidCard, "selected unit is not controlled by the active player");
    }

    PlayerState& state = players_[to_index(player_id)];
    const bool is_first_player = player_id == first_player_;
    const int unlock_turn = is_first_player ? 5 : 4;
    if (state.own_turn_number < unlock_turn) {
        return Status::error(ErrorCode::EvolutionLocked, "evolution is not unlocked yet");
    }

    CardInstance& unit = instances_.at(unit_id);
    const CardDefinition& def = catalog_.at(unit.definition_id);
    if (unit.evolved) {
        return Status::error(ErrorCode::AlreadyEvolved, "unit has already evolved");
    }
    if (state.evolution_used_this_turn) {
        return Status::error(ErrorCode::EvolutionAlreadyUsed, "an evolution was already used this turn");
    }
    // v0.4 §22: active evolution costs 2 evolution energy.
    const PaymentProjection payment = project_payment(
        player_id,
        /*base_cost=*/0,
        /*burn_cost=*/0,
        /*allow_advance=*/false,
        /*use_advance=*/false,
        /*evolution_energy_cost=*/2);
    if (!payment.status) {
        // Preserve the existing evolution-specific public error text.
        return Status::error(ErrorCode::NoEvolutionPoints, "need at least 2 evolution points");
    }
    const Status target_status = validate_effect_targets(
        def.effects, {EffectTrigger::OnEvolution}, player_id, ability_target);
    if (!target_status) {
        return target_status;
    }

    // v0.4 §22: change to the evolution state. Cards without printed evolved
    // stats use the default +2/+2.
    if (def.evolved_attack > 0 && def.evolved_health > 0) {
        unit.current_attack = def.evolved_attack;
        unit.current_health = def.evolved_health;
        unit.maximum_health = def.evolved_health;
    } else {
        unit.current_attack += 2;
        unit.current_health += 2;
        unit.maximum_health += 2;
    }
    unit.evolved = true;
    unit.temporary_rush = true; // v0.4 §22: may attack enemy units this turn
    state.evolution_points = payment.evolution_energy_after;
    state.evolution_used_this_turn = true;
    emit(EventType::UnitEvolved, player_id, unit_id, static_cast<int>(unit.evolved), state.evolution_points);

    const Status evo_status = resolve_effects(def.effects, EffectTrigger::OnEvolution, player_id, unit_id, ability_target);
    if (!evo_status) {
        return evo_status;
    }
    resolve_deaths();
    evaluate_result();
    return Status::ok();
}

Status Game::use_leader_skill(const PlayerId player_id, const std::optional<Target> target) {
    const Status allowed = ensure_action_player(player_id);
    if (!allowed) {
        return allowed;
    }
    PlayerState& state = players_[to_index(player_id)];
    if (state.own_turn_number < 5) {
        return Status::error(ErrorCode::LeaderSkillLocked, "leader skill unlocks on the player's fifth turn");
    }
    if (state.leader_skill_used) {
        return Status::error(ErrorCode::LeaderSkillAlreadyUsed, "leader skill has already been used");
    }
    if (state.current_pp < state.leader_skill.cost) {
        return Status::error(ErrorCode::InsufficientPP, "not enough PP for the leader skill");
    }
    const Status target_status = validate_effect_targets(
        state.leader_skill.effects, {EffectTrigger::OnPlay}, player_id, target);
    if (!target_status) {
        return target_status;
    }

    state.current_pp -= state.leader_skill.cost;
    state.leader_skill_used = true;
    emit(EventType::PPChanged, player_id, 0, state.current_pp, state.pp_capacity);
    emit(EventType::LeaderSkillUsed, player_id, 0, state.leader_skill.cost, 0, state.leader_skill.name);
    return resolve_effects(state.leader_skill.effects, EffectTrigger::OnPlay, player_id, 0, target);
}

// v0.4 §26 response stack -------------------------------------------------------

void Game::open_response_window(
    const ReactionWindow window,
    const PlayerId responder,
    const InstanceId subject,
    const SuspendedAction suspended) {
    const std::vector<InstanceId> eligible = matching_traps(responder, window);
    if (eligible.empty()) {
        // Nobody can respond: the original action resolves immediately.
        resolve_suspended_action(suspended);
        return;
    }
    ResponseLayer layer;
    layer.window = window;
    layer.responder = responder;
    layer.subject = subject;
    layer.eligible_traps = eligible;
    layer.suspended = suspended;
    response_stack_.push_back(std::move(layer));
    if (result_ == GameResult::Ongoing) {
        phase_ = Phase::Reaction;
    }
    emit(EventType::TrapWindowOpened, responder, subject, static_cast<int>(window), static_cast<int>(eligible.size()));
}

Status Game::activate_trap(
    const PlayerId player_id,
    const InstanceId trap_id,
    const std::optional<Target> target) {
    if (!is_valid_player(player_id)) {
        return Status::error(ErrorCode::InvalidPlayer, "player id is outside the supported range");
    }
    if (phase_ != Phase::Reaction || response_stack_.empty()) {
        return Status::error(ErrorCode::NoPendingReaction, "there is no trap window");
    }
    const ResponseLayer& top = response_stack_.back();
    if (top.responder != player_id) {
        return Status::error(ErrorCode::InvalidPlayer, "the other player owns this response layer");
    }
    if (!vector_contains(top.eligible_traps, trap_id)) {
        return Status::error(ErrorCode::TrapNotEligible, "selected trap is not eligible for this event");
    }
    if (instances_.at(trap_id).zone != Zone::Tactic || !instances_.at(trap_id).face_down) {
        return Status::error(ErrorCode::InvalidZone, "trap is no longer set in the tactic zone");
    }

    EffectTrigger trap_trigger = EffectTrigger::OnAttackDeclared;
    switch (top.window) {
        case ReactionWindow::SpellDeclared:
            trap_trigger = EffectTrigger::OnSpellDeclared;
            break;
        case ReactionWindow::EntryEffectPending:
            trap_trigger = EffectTrigger::OnEntryEffectPending;
            break;
        case ReactionWindow::AttackDeclared:
            trap_trigger = EffectTrigger::OnAttackDeclared;
            break;
        case ReactionWindow::None:
            return Status::error(ErrorCode::TrapNotEligible, "trap window has no matching trigger");
    }
    const Status target_status = validate_effect_targets(
        definition(trap_id).effects, {trap_trigger}, player_id, target);
    if (!target_status) {
        return target_status;
    }

    // Copy all base-layer data before push_back can reallocate the vector.
    const std::size_t depth = response_stack_.size();
    const ReactionWindow window = top.window;
    const InstanceId subject = top.subject;
    const PlayerId original_actor = response_stack_.front().suspended.player;
    response_stack_.back().activated_trap = trap_id;
    response_stack_.back().activated_target = target;

    if (depth == 1U) {
        // v0.4 §26: the original actor gets one counter-response.
        const std::vector<InstanceId> counter_eligible = matching_traps(original_actor, window);
        if (counter_eligible.empty()) {
            resolve_response_chain();
            return Status::ok();
        }
        ResponseLayer counter;
        counter.window = window;
        counter.responder = original_actor;
        counter.subject = subject;
        counter.eligible_traps = counter_eligible;
        response_stack_.push_back(std::move(counter));
        emit(EventType::TrapWindowOpened, original_actor, subject, static_cast<int>(window),
             static_cast<int>(counter_eligible.size()));
        return Status::ok();
    }

    // Depth 2 (counter layer): no further additions; resolve LIFO.
    resolve_response_chain();
    return Status::ok();
}

Status Game::pass_reaction(const PlayerId player_id) {
    if (!is_valid_player(player_id)) {
        return Status::error(ErrorCode::InvalidPlayer, "player id is outside the supported range");
    }
    if (phase_ != Phase::Reaction || response_stack_.empty()) {
        return Status::error(ErrorCode::NoPendingReaction, "there is no reaction to pass");
    }
    const ResponseLayer& top = response_stack_.back();
    if (top.responder != player_id) {
        return Status::error(ErrorCode::InvalidPlayer, "the other player owns this response layer");
    }
    if (response_stack_.size() > 1U) {
        // Passing the counter layer leaves the declared base response intact.
        response_stack_.pop_back();
        resolve_response_chain();
        return Status::ok();
    }
    // Base layer passed without a trap: the original action resolves.
    const SuspendedAction suspended = top.suspended;
    response_stack_.clear();
    resolve_suspended_action(suspended);
    if (result_ == GameResult::Ongoing) {
        phase_ = Phase::Action;
    }
    return Status::ok();
}

void Game::resolve_response_chain() {
    if (response_stack_.empty()) {
        return;
    }
    begin_terminal_cleanup_defer();
    const SuspendedAction original = response_stack_.front().suspended;
    bool attack_cancelled = false;
    while (!response_stack_.empty() && result_ == GameResult::Ongoing) {
        ResponseLayer layer = response_stack_.back();
        response_stack_.pop_back();

        if (layer.activated_trap.has_value()) {
            const InstanceId trap_id = *layer.activated_trap;
            const CardDefinition& trap_def = definition(trap_id);
            // v0.4 §20: the trap resolves and then goes to the graveyard.
            put_in_graveyard(layer.responder, trap_id);
            emit(EventType::TrapActivated, layer.responder, trap_id, static_cast<int>(layer.window));
            for (const auto& rec : trap_def.effects) {
                if (result_ != GameResult::Ongoing) {
                    break;
                }
                const bool window_match =
                    (rec.trigger == EffectTrigger::OnSpellDeclared && layer.window == ReactionWindow::SpellDeclared) ||
                    (rec.trigger == EffectTrigger::OnAttackDeclared && layer.window == ReactionWindow::AttackDeclared) ||
                    (rec.trigger == EffectTrigger::OnEntryEffectPending && layer.window == ReactionWindow::EntryEffectPending);
                if (!window_match) {
                    continue;
                }
                if (rec.kind == EffectKind::CancelAttack) {
                    if (original.kind == SuspendedAction::Kind::Attack && !attack_cancelled) {
                        attack_cancelled = true;
                        emit(EventType::AttackCancelled, original.player, original.card);
                    }
                    continue;
                }
                if (rec.kind == EffectKind::DamageEnteredUnit) {
                    InstanceId affected = 0;
                    if (original.kind == SuspendedAction::Kind::EntryEffect) {
                        affected = original.card;
                    } else if (original.kind == SuspendedAction::Kind::Spell && original.target.has_value() &&
                               original.target->kind == Target::Kind::Unit) {
                        affected = original.target->unit;
                    }
                    if (affected != 0 && instances_.contains(affected) &&
                        instances_.at(affected).zone == Zone::Unit) {
                        damage_unit(affected, rec.amount);
                        resolve_deaths();
                        evaluate_result();
                    }
                    continue;
                }
                const Status status = resolve_effects(
                    {rec}, rec.trigger, layer.responder, trap_id, layer.activated_target);
                if (!status) {
                    // Targets were validated at declaration. If one became stale
                    // while the chain resolved, only that dependent effect skips.
                    continue;
                }
                resolve_deaths();
                evaluate_result();
            }
        }
    }
    // A lethal higher layer abandons every lower declared response. Those
    // already-activated traps still pay the zone-transition part of their
    // declaration, in LIFO order, without resolving or emitting TrapActivated.
    finalize_declared_response_cards();
    response_stack_.clear();
    if (result_ == GameResult::Ongoing &&
        !(original.kind == SuspendedAction::Kind::Attack && attack_cancelled)) {
        resolve_suspended_action(original);
    }
    // A lethal response aborts the original link, but the already-declared
    // spell must still leave its reserved tactic slot before MatchEnded.
    finalize_suspended_spell(original);
    if (result_ == GameResult::Ongoing) {
        phase_ = Phase::Action;
    }
    end_terminal_cleanup_defer();
}

void Game::finalize_declared_response_cards() {
    for (auto layer = response_stack_.rbegin(); layer != response_stack_.rend(); ++layer) {
        if (!layer->activated_trap.has_value()) {
            continue;
        }
        const InstanceId trap_id = *layer->activated_trap;
        const auto iterator = instances_.find(trap_id);
        if (iterator == instances_.end()) {
            continue;
        }
        const CardInstance& trap = iterator->second;
        if (trap.controller == layer->responder && trap.zone == Zone::Tactic) {
            put_in_graveyard(layer->responder, trap_id);
        }
    }
}

void Game::finalize_suspended_spell(const SuspendedAction& suspended) {
    if (suspended.kind != SuspendedAction::Kind::Spell || !suspended.tactic_slot.has_value()) {
        return;
    }
    const auto iterator = instances_.find(suspended.card);
    if (iterator == instances_.end()) {
        return;
    }
    const CardInstance& card = iterator->second;
    const std::size_t slot = *suspended.tactic_slot;
    if (slot >= kTacticZoneSize || card.controller != suspended.player ||
        card.zone != Zone::Tactic || card.sequence != slot) {
        return;
    }
    const PlayerState& state = players_[to_index(suspended.player)];
    if (!state.tactics[slot].has_value() || *state.tactics[slot] != suspended.card) {
        return;
    }
    put_in_graveyard(suspended.player, suspended.card);
}

void Game::begin_terminal_cleanup_defer() {
    ++terminal_cleanup_defer_depth_;
}

void Game::end_terminal_cleanup_defer() {
    if (terminal_cleanup_defer_depth_ == 0U) {
        throw std::logic_error("terminal cleanup defer depth underflow");
    }
    --terminal_cleanup_defer_depth_;
    if (terminal_cleanup_defer_depth_ == 0U && result_ != GameResult::Ongoing) {
        finalize_match_end();
    }
}

void Game::resolve_suspended_action(const SuspendedAction& suspended) {
    switch (suspended.kind) {
        case SuspendedAction::Kind::None:
            return;
        case SuspendedAction::Kind::Spell: {
            begin_terminal_cleanup_defer();
            const CardDefinition& def = definition(suspended.card);
            (void)resolve_effects(
                def.effects,
                suspended.advanced ? EffectTrigger::OnPlayIfAdvanced : EffectTrigger::OnPlayIfNotAdvanced,
                suspended.player, suspended.card, suspended.target, suspended.advanced);
            (void)resolve_effects(
                def.effects, EffectTrigger::OnPlay, suspended.player, suspended.card, suspended.target, suspended.advanced);
            resolve_deaths();
            evaluate_result();
            // The spell stays face-up in the selected tactic slot throughout
            // every higher response layer and leaves after its own link ends.
            finalize_suspended_spell(suspended);
            end_terminal_cleanup_defer();
            return;
        }
        case SuspendedAction::Kind::EntryEffect: {
            const CardDefinition& def = definition(suspended.card);
            (void)resolve_effects(
                def.effects, EffectTrigger::OnEntry, suspended.player, suspended.card, suspended.target, suspended.advanced);
            resolve_deaths();
            evaluate_result();
            return;
        }
        case SuspendedAction::Kind::Attack: {
            const PendingAttack& attack = suspended.attack;
            if (!instances_.contains(attack.attacker) || instances_.at(attack.attacker).zone != Zone::Unit) {
                return;
            }
            if (attack.target.kind == Target::Kind::Unit) {
                if (!instances_.contains(attack.target.unit) || instances_.at(attack.target.unit).zone != Zone::Unit) {
                    return; // v0.4 §21: target left the field; attack ends
                }
                resolve_unit_combat(attack.attacker, attack.target.unit);
            } else {
                resolve_leader_attack(attack.attacker, attack.target.player);
            }
            evaluate_result();
            return;
        }
    }
}

std::vector<InstanceId> Game::matching_traps(
    const PlayerId responder,
    const ReactionWindow window) const {
    std::vector<InstanceId> matches;
    for (const auto& slot : players_[to_index(responder)].tactics) {
        if (!slot.has_value()) {
            continue;
        }
        const CardInstance& card = instances_.at(*slot);
        const CardDefinition& card_definition = catalog_.at(card.definition_id);
        if (card.zone == Zone::Tactic && card.face_down && card_definition.kind == CardKind::Trap &&
            trap_matches_window(card_definition, window)) {
            matches.push_back(*slot);
        }
    }
    return matches;
}

bool Game::trap_matches_window(
    const CardDefinition& trap,
    const ReactionWindow window) const {
    for (const auto& rec : trap.effects) {
        if (window == ReactionWindow::SpellDeclared && rec.trigger == EffectTrigger::OnSpellDeclared) {
            return true;
        }
        if (window == ReactionWindow::AttackDeclared && rec.trigger == EffectTrigger::OnAttackDeclared) {
            return true;
        }
        if (window == ReactionWindow::EntryEffectPending && rec.trigger == EffectTrigger::OnEntryEffectPending) {
            return true;
        }
    }
    return false;
}

void Game::close_reaction_window() {
    if (!response_stack_.empty()) {
        finalize_declared_response_cards();
        finalize_suspended_spell(response_stack_.front().suspended);
    }
    response_stack_.clear();
    if (result_ == GameResult::Ongoing) {
        phase_ = Phase::Action;
    }
}

Status Game::load_scenario(const Scenario& scenario) {
    if (phase_ != Phase::NotStarted) {
        return Status::error(ErrorCode::MatchAlreadyStarted, "scenario must be loaded before starting a match");
    }
    if (!is_valid_player(scenario.active_player)) {
        return Status::error(ErrorCode::InvalidPlayer, "scenario active player is outside the supported range");
    }
    players_ = {};
    instances_.clear();
    next_instance_id_ = 1;
    result_ = GameResult::Ongoing;
    response_stack_.clear();
    terminal_cleanup_defer_depth_ = 0;
    events_.clear();
    revision_ = 0;
    next_event_sequence_ = 1;
    event_history_.clear();

    for (std::size_t player_index = 0; player_index < kPlayerCount; ++player_index) {
        const PlayerId player_id = static_cast<PlayerId>(player_index);
        const ScenarioPlayer& source = scenario.players[player_index];
        PlayerState& destination = players_[player_index];
        destination.leader_health = source.leader_health;
        destination.maximum_leader_health = source.maximum_leader_health;
        destination.current_pp = source.current_pp;
        destination.pp_capacity = source.pp_capacity;
        destination.cracks = source.cracks;
        destination.evolution_points = source.evolution_points;
        destination.own_turn_number = source.own_turn_number;
        destination.mulligan_done = true;
        destination.leader_skill = source.leader_skill;

        for (const CardId id : source.deck) {
            create_instance(id, player_id, Zone::Deck);
        }
        for (const CardId id : source.hand) {
            const InstanceId instance_id = create_instance(id, player_id, Zone::None);
            put_in_hand(player_id, instance_id);
        }
        for (const CardId id : source.units) {
            const std::optional<std::size_t> slot = first_free_unit_slot(player_id);
            if (!slot.has_value()) {
                return Status::error(ErrorCode::UnitZoneFull, "scenario has more than five units");
            }
            const InstanceId instance_id = create_instance(id, player_id, Zone::None);
            put_in_unit_slot(player_id, instance_id, *slot);
            instances_.at(instance_id).entered_this_turn = false;
        }
        for (const CardId id : source.tactics) {
            std::optional<std::size_t> slot;
            for (std::size_t i = 0; i < kTacticZoneSize; ++i) {
                if (!destination.tactics[i].has_value()) {
                    slot = i;
                    break;
                }
            }
            if (!slot.has_value()) {
                return Status::error(ErrorCode::TacticZoneFull, "scenario has more than three tactics");
            }
            const InstanceId instance_id = create_instance(id, player_id, Zone::None);
            put_in_tactic_slot(player_id, instance_id, *slot);
        }
        for (const CardId id : source.graveyard) {
            const InstanceId instance_id = create_instance(id, player_id, Zone::None);
            put_in_graveyard(player_id, instance_id);
        }
        for (const CardId id : source.archive) {
            const InstanceId instance_id = create_instance(id, player_id, Zone::None);
            put_in_archive(player_id, instance_id);
        }
        for (const CardId id : source.standby) {
            create_instance(id, player_id, Zone::Standby);
        }
    }

    active_player_ = scenario.active_player;
    phase_ = Phase::Action;
    evaluate_result();
    return Status::ok();
}

const PlayerState& Game::player(const PlayerId player_id) const {
    return players_.at(to_index(player_id));
}

const CardInstance& Game::instance(const InstanceId id) const {
    return instances_.at(id);
}

const CardDefinition& Game::definition(const InstanceId id) const {
    return catalog_.at(instance(id).definition_id);
}

const CardCatalog& Game::catalog() const noexcept {
    return catalog_;
}

PlayerId Game::active_player() const noexcept {
    return active_player_;
}

PlayerId Game::first_player() const noexcept {
    return first_player_;
}

std::uint32_t Game::random_seed() const noexcept {
    return actual_seed_;
}

Phase Game::phase() const noexcept {
    return phase_;
}

GameResult Game::result() const noexcept {
    return result_;
}

ReactionWindow Game::reaction_window() const noexcept {
    return response_stack_.empty() ? ReactionWindow::None : response_stack_.back().window;
}

std::size_t Game::response_depth() const noexcept {
    return response_stack_.size();
}

const std::vector<InstanceId>& Game::eligible_traps() const noexcept {
    static const std::vector<InstanceId> empty;
    return response_stack_.empty() ? empty : response_stack_.back().eligible_traps;
}

std::vector<GameEvent> Game::drain_events() {
    std::vector<GameEvent> drained;
    drained.swap(events_);
    return drained;
}

std::vector<std::string> Game::validate_invariants() const {
    std::vector<std::string> problems;
    std::unordered_map<InstanceId, int> occurrences;

    const auto describe = [](const PlayerId player_id, const Zone zone, const std::size_t sequence) {
        std::ostringstream stream;
        stream << "player " << to_index(player_id)
               << ", zone " << static_cast<int>(zone)
               << ", sequence " << sequence;
        return stream.str();
    };

    const auto record = [&](
                            const PlayerId player_id,
                            const Zone zone,
                            const std::size_t sequence,
                            const InstanceId id) {
        ++occurrences[id];
        const auto iterator = instances_.find(id);
        if (iterator == instances_.end()) {
            problems.push_back("zone references unknown instance " + std::to_string(id) +
                               " at " + describe(player_id, zone, sequence));
            return;
        }
        const CardInstance& card = iterator->second;
        if (card.id != id) {
            problems.push_back("instance map key does not match stored id " + std::to_string(id));
        }
        if (card.controller != player_id) {
            problems.push_back("instance " + std::to_string(id) +
                               " has the wrong controller for " + describe(player_id, zone, sequence));
        }
        if (card.zone != zone) {
            problems.push_back("instance " + std::to_string(id) +
                               " reports the wrong zone at " + describe(player_id, zone, sequence));
        }
        if (card.sequence != sequence) {
            problems.push_back("instance " + std::to_string(id) +
                               " reports the wrong sequence at " + describe(player_id, zone, sequence));
        }
    };

    const auto is_pending_spell = [&](
                                      const PlayerId player_id,
                                      const std::size_t slot,
                                      const InstanceId id) {
        if (phase_ != Phase::Reaction || response_stack_.empty()) {
            return false;
        }
        const SuspendedAction& suspended = response_stack_.front().suspended;
        return suspended.kind == SuspendedAction::Kind::Spell &&
            suspended.player == player_id && suspended.card == id &&
            suspended.tactic_slot.has_value() && *suspended.tactic_slot == slot;
    };

    for (std::size_t player_index = 0; player_index < kPlayerCount; ++player_index) {
        const PlayerId player_id = static_cast<PlayerId>(player_index);
        const PlayerState& state = players_[player_index];

        if (state.maximum_leader_health <= 0) {
            problems.push_back("player " + std::to_string(player_index) + " has a non-positive leader-health limit");
        }
        if (state.leader_health < 0 || state.leader_health > state.maximum_leader_health) {
            problems.push_back("player " + std::to_string(player_index) + " has leader health outside its limits");
        }
        // v0.4: pp_capacity has no upper bound; current_pp may exceed capacity (§17).
        if (state.pp_capacity < 0 || state.current_pp < 0 || state.cracks < 0) {
            problems.push_back("player " + std::to_string(player_index) + " has a negative PP value or cracks");
        }
        // v0.4 §22: evolution energy is capped at 4.
        if (state.evolution_points < 0 || state.evolution_points > 4) {
            problems.push_back("player " + std::to_string(player_index) + " has evolution energy outside [0,4]");
        }
        if (state.own_turn_number < 0 || state.fatigue_count < 0) {
            problems.push_back("player " + std::to_string(player_index) + " has a negative counter");
        }
        if (state.hand.size() > static_cast<std::size_t>(config_.hand_limit)) {
            problems.push_back("player " + std::to_string(player_index) + " exceeds the hand limit");
        }
        // v0.4 §2: standby zone holds at most 6 public cards.
        if (state.standby.size() > 6) {
            problems.push_back("player " + std::to_string(player_index) + " exceeds six standby cards");
        }

        const auto record_vector = [&](const std::vector<InstanceId>& cards, const Zone zone) {
            for (std::size_t sequence = 0; sequence < cards.size(); ++sequence) {
                record(player_id, zone, sequence, cards[sequence]);
            }
        };
        record_vector(state.deck, Zone::Deck);
        record_vector(state.hand, Zone::Hand);
        record_vector(state.graveyard, Zone::Graveyard);
        record_vector(state.archive, Zone::Archive);
        record_vector(state.standby, Zone::Standby);

        for (std::size_t sequence = 0; sequence < state.units.size(); ++sequence) {
            if (!state.units[sequence].has_value()) {
                continue;
            }
            const InstanceId id = *state.units[sequence];
            record(player_id, Zone::Unit, sequence, id);
            const auto iterator = instances_.find(id);
            if (iterator == instances_.end()) {
                continue;
            }
            const CardInstance& unit = iterator->second;
            const CardDefinition& card_definition = catalog_.at(unit.definition_id);
            if (card_definition.kind != CardKind::Unit) {
                problems.push_back("non-unit instance " + std::to_string(id) + " occupies a unit slot");
            }
            if (unit.maximum_health <= 0 || unit.current_health <= 0 ||
                unit.current_health > unit.maximum_health || unit.current_attack < 0) {
                problems.push_back("unit instance " + std::to_string(id) + " has invalid combat statistics");
            }
            if (unit.face_down) {
                problems.push_back("unit instance " + std::to_string(id) + " is unexpectedly face down");
            }
        }

        for (std::size_t sequence = 0; sequence < state.tactics.size(); ++sequence) {
            if (!state.tactics[sequence].has_value()) {
                continue;
            }
            const InstanceId id = *state.tactics[sequence];
            record(player_id, Zone::Tactic, sequence, id);
            const auto iterator = instances_.find(id);
            if (iterator == instances_.end()) {
                continue;
            }
            const CardInstance& tactic = iterator->second;
            const CardDefinition& card_definition = catalog_.at(tactic.definition_id);
            if (card_definition.kind == CardKind::Spell) {
                if (!is_pending_spell(player_id, sequence, id)) {
                    problems.push_back(
                        "non-pending spell instance " + std::to_string(id) +
                        " occupies a tactic slot");
                }
                if (tactic.face_down || tactic.countdown != 0) {
                    problems.push_back(
                        "pending spell instance " + std::to_string(id) +
                        " has invalid tactic presentation state");
                }
            } else if (card_definition.kind != CardKind::Relic &&
                       card_definition.kind != CardKind::Trap) {
                problems.push_back("non-tactic instance " + std::to_string(id) + " occupies a tactic slot");
            }
            if (card_definition.kind == CardKind::Trap && !tactic.face_down) {
                problems.push_back("set trap instance " + std::to_string(id) + " is not face down");
            }
            if (card_definition.kind == CardKind::Relic && tactic.face_down) {
                problems.push_back("relic instance " + std::to_string(id) + " is face down");
            }
            if (tactic.countdown < 0) {
                problems.push_back("tactic instance " + std::to_string(id) + " has a negative countdown");
            }
        }
    }

    InstanceId maximum_id = 0;
    for (const auto& [id, card] : instances_) {
        maximum_id = std::max(maximum_id, id);
        const int count = occurrences[id];
        if (card.zone == Zone::None) {
            if (count != 0) {
                problems.push_back("zone-less instance " + std::to_string(id) + " appears in a zone container");
            }
            problems.push_back("instance " + std::to_string(id) + " is detached from every zone");
        } else if (count != 1) {
            problems.push_back("instance " + std::to_string(id) + " occurs in " +
                               std::to_string(count) + " zone containers");
        }
        if (!catalog_.contains(card.definition_id)) {
            problems.push_back("instance " + std::to_string(id) + " references an unknown card definition");
            continue;
        }
        if (card.zone != Zone::Unit && card.inherited_imprint != Imprint::None) {
            problems.push_back("off-field instance " + std::to_string(id) + " retains an inherited imprint");
        }
        if (card.zone != Zone::Unit && card.granted_component.has_component) {
            problems.push_back("off-field instance " + std::to_string(id) + " retains a granted component");
        }
    }
    for (const auto& [id, count] : occurrences) {
        if (!instances_.contains(id) && count > 1) {
            problems.push_back("unknown instance " + std::to_string(id) + " is referenced multiple times");
        }
    }
    if (!instances_.empty() && next_instance_id_ <= maximum_id) {
        problems.push_back("next instance id is not greater than every allocated id");
    }

    if (phase_ == Phase::Reaction) {
        if (response_stack_.empty()) {
            problems.push_back("reaction phase has no response layer");
        } else if (response_stack_.size() > 2) {
            problems.push_back("response stack exceeds the v0.4 three-layer limit");
        } else {
            const SuspendedAction& base = response_stack_.front().suspended;
            if (base.kind == SuspendedAction::Kind::Spell) {
                if (!base.tactic_slot.has_value() || *base.tactic_slot >= kTacticZoneSize ||
                    !instances_.contains(base.card)) {
                    problems.push_back("pending spell response has no valid tactic slot");
                } else {
                    const PlayerState& caster = players_[to_index(base.player)];
                    const std::size_t slot = *base.tactic_slot;
                    if (!caster.tactics[slot].has_value() ||
                        *caster.tactics[slot] != base.card ||
                        instances_.at(base.card).zone != Zone::Tactic) {
                        problems.push_back("pending spell response is not occupying its tactic slot");
                    }
                }
            } else if (base.tactic_slot.has_value()) {
                problems.push_back("non-spell response unexpectedly carries a tactic slot");
            }
            const ResponseLayer& top = response_stack_.back();
            if (top.window == ReactionWindow::None) {
                problems.push_back("response layer has no reaction-window type");
            }
            for (const InstanceId id : top.eligible_traps) {
                if (!instances_.contains(id) || instances_.at(id).zone != Zone::Tactic ||
                    instances_.at(id).controller != top.responder) {
                    problems.push_back("response layer contains an invalid eligible trap");
                }
            }
            if (top.activated_trap.has_value() &&
                (!instances_.contains(*top.activated_trap) ||
                 instances_.at(*top.activated_trap).controller != top.responder)) {
                problems.push_back("response layer contains an invalid activated trap");
            }
        }
    } else if (!response_stack_.empty()) {
        problems.push_back("response layers exist outside the reaction phase");
    }

    if (result_ == GameResult::Ongoing && phase_ == Phase::Finished) {
        problems.push_back("ongoing match is marked as finished");
    }
    if (result_ != GameResult::Ongoing && phase_ != Phase::Finished) {
        problems.push_back("finished result is not in the finished phase");
    }
    if (result_ == GameResult::Ongoing && phase_ != Phase::NotStarted &&
        (players_[0].leader_health <= 0 || players_[1].leader_health <= 0)) {
        problems.push_back("ongoing match has a defeated leader");
    }
    if (terminal_cleanup_defer_depth_ != 0U) {
        problems.push_back("terminal cleanup deferral escaped a command resolution");
    }

    return problems;
}

std::optional<InstanceId> Game::find_in_hand(const PlayerId player_id, const CardId card_id) const {
    for (const InstanceId id : players_[to_index(player_id)].hand) {
        if (instances_.at(id).definition_id == card_id) {
            return id;
        }
    }
    return std::nullopt;
}

std::optional<InstanceId> Game::find_on_field(const PlayerId player_id, const CardId card_id) const {
    for (const auto& slot : players_[to_index(player_id)].units) {
        if (slot.has_value() && instances_.at(*slot).definition_id == card_id) {
            return *slot;
        }
    }
    return std::nullopt;
}

std::optional<InstanceId> Game::find_in_standby(const PlayerId player_id, const CardId card_id) const {
    for (const InstanceId id : players_[to_index(player_id)].standby) {
        if (instances_.at(id).definition_id == card_id) {
            return id;
        }
    }
    return std::nullopt;
}

Status Game::ensure_action_player(const PlayerId player_id) const {
    if (!is_valid_player(player_id)) {
        return Status::error(ErrorCode::InvalidPlayer, "player id is outside the supported range");
    }
    if (result_ != GameResult::Ongoing || phase_ == Phase::Finished) {
        return Status::error(ErrorCode::GameOver, "the match has already finished");
    }
    if (phase_ == Phase::NotStarted || phase_ == Phase::Mulligan) {
        return Status::error(ErrorCode::InvalidPhase, "the match has not reached the action phase");
    }
    if (phase_ == Phase::Reaction) {
        return Status::error(ErrorCode::InvalidPhase, "a response window is open");
    }
    if (player_id != active_player_) {
        return Status::error(ErrorCode::NotActivePlayer, "only the active player may act");
    }
    return Status::ok();
}

Status Game::ensure_not_finished() const {
    if (result_ != GameResult::Ongoing || phase_ == Phase::Finished) {
        return Status::error(ErrorCode::GameOver, "the match has already finished");
    }
    return Status::ok();
}

Status Game::validate_empty_tactic_slot(
    const PlayerId player_id,
    const std::size_t slot) const {
    if (slot >= kTacticZoneSize) {
        return Status::error(ErrorCode::InvalidSlot, "tactic slot is out of range");
    }
    if (players_[to_index(player_id)].tactics[slot].has_value()) {
        return Status::error(
            ErrorCode::TacticZoneFull,
            "tactic slot is occupied; no replacement without an effect");
    }
    return Status::ok();
}

bool Game::evolution_is_unlocked(const PlayerId player_id) const {
    if (!is_valid_player(player_id)) {
        return false;
    }
    const bool is_first = player_id == first_player_;
    return players_[to_index(player_id)].own_turn_number >= (is_first ? 5 : 4);
}

Status Game::validate_effect_targets(
    const std::vector<EffectRecord>& effects,
    const std::vector<EffectTrigger>& triggers,
    const PlayerId actor,
    const std::optional<Target> target) const {
    for (const EffectRecord& effect : effects) {
        if (std::find(triggers.begin(), triggers.end(), effect.trigger) == triggers.end()) {
            continue;
        }
        if (effect.target_spec == TargetSpec::EnemyUnit) {
            if (!target.has_value() || !is_valid_target_for_ability(actor, *target, true)) {
                return Status::error(ErrorCode::InvalidTarget, "effect requires a legal enemy unit target");
            }
        } else if (effect.target_spec == TargetSpec::FriendlyUnit) {
            if (!target.has_value() || target->kind != Target::Kind::Unit || target->player != actor ||
                !is_controlled_unit(actor, target->unit)) {
                return Status::error(ErrorCode::InvalidTarget, "effect requires a legal friendly unit target");
            }
        }
    }
    return Status::ok();
}

InstanceId Game::create_instance(const CardId card_id, const PlayerId owner, const Zone zone) {
    const InstanceId id = next_instance_id_++;
    CardInstance instance;
    instance.id = id;
    instance.definition_id = card_id;
    instance.owner = owner;
    instance.controller = owner;
    instance.zone = zone;
    if (zone != Zone::None) {
        PlayerState& state = players_[to_index(owner)];
        switch (zone) {
            case Zone::Deck:
                instance.sequence = state.deck.size();
                state.deck.push_back(id);
                break;
            case Zone::Standby:
                instance.sequence = state.standby.size();
                state.standby.push_back(id);
                break;
            default:
                break;
        }
    }
    instances_.emplace(id, instance);
    return id;
}

void Game::initialize_decks() {
    players_ = {};
    instances_.clear();
    next_instance_id_ = 1;
    result_ = GameResult::Ongoing;
    response_stack_.clear();
    terminal_cleanup_defer_depth_ = 0;
    events_.clear();
    revision_ = 0;
    next_event_sequence_ = 1;
    event_history_.clear();
    active_player_ = first_player_;

    for (std::size_t player_index = 0; player_index < kPlayerCount; ++player_index) {
        const PlayerId player_id = static_cast<PlayerId>(player_index);
        PlayerState& state = players_[player_index];
        state.leader_health = config_.leader_health;
        state.maximum_leader_health = config_.leader_health;
        state.evolution_points = 0; // granted at the v0.4 unlock turn
        state.leader_skill = deck_lists_[player_index].leader_skill;

        for (const CardId card_id : deck_lists_[player_index].main) {
            create_instance(card_id, player_id, Zone::Deck);
        }
        for (const CardId card_id : deck_lists_[player_index].standby) {
            create_instance(card_id, player_id, Zone::Standby);
        }
        if (config_.shuffle_decks) {
            std::shuffle(state.deck.begin(), state.deck.end(), rng_);
            normalize_sequences(player_id, Zone::Deck);
        }
    }
}

void Game::begin_turn(const PlayerId player_id) {
    if (result_ != GameResult::Ongoing) {
        return;
    }
    active_player_ = player_id;
    phase_ = Phase::Action;
    PlayerState& state = players_[to_index(player_id)];
    ++state.own_turn_number;

    // v0.4 §19: per-turn resets and the turn-cycle charge window.
    state.evolution_used_this_turn = false;
    state.advance_used_this_turn = false;
    state.deploy_used_this_turn = false;
    state.trap_set_this_turn = false;
    state.charge_granted_this_cycle = false;
    state.friendly_deaths_this_cycle = 0;
    state.spells_used_this_turn = 0;
    state.units_played_this_turn = 0;

    ready_units(player_id);

    // v0.4 §7.1: PP capacity grows by 1 each turn with no cap.
    state.pp_capacity += 1;
    // v0.4 §7.2: current PP is refilled to pp_capacity.
    state.current_pp = state.pp_capacity;
    emit(EventType::PPChanged, player_id, 0, state.current_pp, state.pp_capacity);

    // v0.4 §22 evolution unlock: first player on turn 5, second on turn 4.
    const bool is_first = player_id == first_player_;
    const int unlock_turn = is_first ? 5 : 4;
    if (state.own_turn_number == unlock_turn) {
        state.evolution_points = is_first ? 2 : 3;
        emit(EventType::EvolutionEnergyChanged, player_id, 0, state.evolution_points);
    }

    const bool skip_draw = player_id == first_player_ && state.own_turn_number == 1;
    if (!skip_draw) {
        draw_one(player_id);
    }
    if (result_ != GameResult::Ongoing) {
        return;
    }
    process_relic_countdowns(player_id);
    if (result_ != GameResult::Ongoing) {
        return;
    }
    emit(EventType::TurnStarted, player_id, 0, state.own_turn_number);
    evaluate_result();
}

void Game::ready_units(const PlayerId player_id) {
    for (const auto& slot : players_[to_index(player_id)].units) {
        if (!slot.has_value()) {
            continue;
        }
        CardInstance& unit = instances_.at(*slot);
        unit.attacked_this_turn = false;
        unit.entered_this_turn = false;
        unit.temporary_rush = false;
    }
}

void Game::process_relic_countdowns(const PlayerId player_id) {
    std::vector<InstanceId> relics;
    for (const auto& slot : players_[to_index(player_id)].tactics) {
        if (slot.has_value() && definition(*slot).kind == CardKind::Relic) {
            relics.push_back(*slot);
        }
    }
    for (const InstanceId id : relics) {
        if (result_ != GameResult::Ongoing) {
            return;
        }
        CardInstance& relic = instances_.at(id);
        if (relic.zone != Zone::Tactic || relic.countdown <= 0) {
            continue;
        }
        --relic.countdown;
        if (relic.countdown == 0) {
            const CardDefinition& relic_def = definition(id);
            put_in_graveyard(player_id, id);
            const Status status = resolve_effects(
                relic_def.effects,
                EffectTrigger::OnCountdownExpire,
                player_id,
                id,
                std::nullopt);
            if (!status) {
                throw std::logic_error("countdown effect failed: " + status.message);
            }
        }
    }
}

void Game::clear_end_of_turn_state(const PlayerId player_id) {
    for (const auto& slot : players_[to_index(player_id)].units) {
        if (slot.has_value()) {
            instances_.at(*slot).temporary_rush = false;
        }
    }
}

void Game::draw_cards(const PlayerId player_id, const int count, const bool emit_draw_event) {
    for (int i = 0; i < count && result_ == GameResult::Ongoing; ++i) {
        draw_one(player_id, emit_draw_event);
    }
}

void Game::draw_one(const PlayerId player_id, const bool emit_draw_event) {
    if (result_ != GameResult::Ongoing) {
        return;
    }
    PlayerState& state = players_[to_index(player_id)];
    if (state.deck.empty()) {
        ++state.fatigue_count;
        emit(EventType::FatigueDamage, player_id, 0, state.fatigue_count);
        damage_leader(player_id, state.fatigue_count);
        return;
    }
    const InstanceId card = state.deck.front();
    put_in_hand(player_id, card);
    if (instances_.at(card).zone == Zone::Hand && emit_draw_event) {
        emit(EventType::CardDrawn, player_id, card, static_cast<int>(state.hand.size()));
    }
}

void Game::damage_leader(const PlayerId player_id, const int amount) {
    if (amount <= 0 || result_ != GameResult::Ongoing) {
        return;
    }
    PlayerState& state = players_[to_index(player_id)];
    state.leader_health = std::max(0, state.leader_health - amount);
    emit(EventType::LeaderDamaged, player_id, 0, amount, state.leader_health);
    evaluate_result();
}

void Game::heal_leader(const PlayerId player_id, const int amount) {
    if (amount <= 0 || result_ != GameResult::Ongoing) {
        return;
    }
    PlayerState& state = players_[to_index(player_id)];
    const int before = state.leader_health;
    state.leader_health = std::min(state.maximum_leader_health, state.leader_health + amount);
    emit(EventType::LeaderHealed, player_id, 0, state.leader_health - before, state.leader_health);
}

void Game::repair_cracks(const PlayerId player_id, const int amount) {
    if (amount <= 0 || result_ != GameResult::Ongoing) {
        return;
    }
    PlayerState& state = players_[to_index(player_id)];
    const int actual = std::min(amount, state.cracks);
    if (actual <= 0) {
        return;
    }
    // v0.4 §15: repair removes cracks and restores the same capacity.
    state.cracks -= actual;
    state.pp_capacity += actual;
    emit(EventType::CracksChanged, player_id, 0, state.cracks, state.pp_capacity);
    emit(EventType::PPChanged, player_id, 0, state.current_pp, state.pp_capacity);
}

void Game::gain_pp_capacity(const PlayerId player_id, const int amount) {
    if (amount <= 0 || result_ != GameResult::Ongoing) {
        return;
    }
    // v0.4 §16: growth raises capacity directly; no cracks involved.
    PlayerState& state = players_[to_index(player_id)];
    state.pp_capacity += amount;
    emit(EventType::PPChanged, player_id, 0, state.current_pp, state.pp_capacity);
}

void Game::grant_evolution_energy(const PlayerId player_id, const int amount) {
    if (amount <= 0 || result_ != GameResult::Ongoing || !evolution_is_unlocked(player_id)) {
        return;
    }
    PlayerState& state = players_[to_index(player_id)];
    // v0.4 §22: evolution energy caps at 4.
    const int before = state.evolution_points;
    state.evolution_points = std::min(4, state.evolution_points + amount);
    if (state.evolution_points != before) {
        emit(EventType::EvolutionEnergyChanged, player_id, 0, state.evolution_points);
    }
}

int Game::damage_unit(const InstanceId unit_id, const int amount) {
    if (amount <= 0 || result_ != GameResult::Ongoing || !instances_.contains(unit_id)) {
        return 0;
    }
    CardInstance& unit = instances_.at(unit_id);
    if (unit.zone != Zone::Unit) {
        return 0;
    }
    if (has_keyword(unit.keywords, Keyword::Barrier)) {
        unit.keywords &= ~mask(Keyword::Barrier);
        emit(EventType::UnitDamaged, unit.controller, unit_id, 0, unit.current_health, "barrier");
        return 0;
    }
    const int actual = std::min(amount, std::max(0, unit.current_health));
    unit.current_health -= amount;
    emit(EventType::UnitDamaged, unit.controller, unit_id, actual, unit.current_health);
    return actual;
}

void Game::resolve_deaths() {
    if (result_ != GameResult::Ongoing) {
        return;
    }
    struct DeathTrigger {
        PlayerId controller = PlayerId::Player0;
        InstanceId unit = 0;
        std::vector<EffectRecord> last_words_effects;
    };

    while (true) {
        std::vector<InstanceId> destroyed;
        const std::array<PlayerId, kPlayerCount> resolution_order = {
            active_player_,
            opponent(active_player_),
        };
        for (const PlayerId player_id : resolution_order) {
            for (const auto& slot : players_[to_index(player_id)].units) {
                if (slot.has_value() && instances_.at(*slot).current_health <= 0) {
                    destroyed.push_back(*slot);
                }
            }
        }
        if (destroyed.empty()) {
            return;
        }

        std::vector<DeathTrigger> triggers;
        triggers.reserve(destroyed.size());
        for (const InstanceId unit_id : destroyed) {
            if (!instances_.contains(unit_id)) {
                continue;
            }
            CardInstance& unit = instances_.at(unit_id);
            if (unit.zone != Zone::Unit || unit.current_health > 0) {
                continue;
            }
            const PlayerId controller = unit.controller;
            const CardDefinition& unit_def = catalog_.at(unit.definition_id);
            triggers.push_back(DeathTrigger{controller, unit_id, unit_def.effects});

            // v0.4 §23: count friendly deaths in this turn cycle for the
            // class charge condition.
            ++players_[to_index(controller)].friendly_deaths_this_cycle;

            // v0.4 §5: deployed standby units leave to the archive, not the
            // graveyard.
            if (unit.deployed_from_standby) {
                put_in_archive(controller, unit_id);
            } else {
                put_in_graveyard(controller, unit_id);
            }
            emit(EventType::UnitDestroyed, controller, unit_id);
        }

        // All units have left the field before any last-words effect resolves.
        for (const DeathTrigger& trigger : triggers) {
            if (result_ != GameResult::Ongoing) {
                return;
            }
            const Status status = resolve_effects(
                trigger.last_words_effects,
                EffectTrigger::OnLastWords,
                trigger.controller,
                trigger.unit,
                std::nullopt);
            if (!status) {
                throw std::logic_error("last words effect failed: " + status.message);
            }
        }

        // v0.4 §23: FriendlyDeathsPerCycle charge — at most once per cycle.
        for (std::size_t index = 0; index < kPlayerCount; ++index) {
            PlayerState& state = players_[index];
            const DeckList& deck_list = deck_lists_[index];
            if (evolution_is_unlocked(static_cast<PlayerId>(index)) &&
                !state.charge_granted_this_cycle &&
                deck_list.charge_condition == ChargeCondition::FriendlyDeathsPerCycle &&
                state.friendly_deaths_this_cycle >= deck_list.charge_amount) {
                grant_evolution_energy(static_cast<PlayerId>(index), 1);
                state.charge_granted_this_cycle = true;
            }
        }
    }
}

Status Game::resolve_effects(
    const std::vector<EffectRecord>& effects,
    const EffectTrigger trigger,
    const PlayerId actor,
    const InstanceId source,
    const std::optional<Target> target,
    const bool advanced) {
    for (const EffectRecord& rec : effects) {
        if (result_ != GameResult::Ongoing) {
            break;
        }
        if (rec.trigger != trigger) {
            continue;
        }
        // Conditional on-play triggers.
        if (rec.trigger == EffectTrigger::OnPlayIfAdvanced && !advanced) {
            continue;
        }
        if (rec.trigger == EffectTrigger::OnPlayIfNotAdvanced && advanced) {
            continue;
        }

        switch (rec.kind) {
            case EffectKind::DrawCards:
                draw_cards(actor, rec.amount);
                break;

            case EffectKind::DealDamageToEnemyUnit: {
                if (!target.has_value() || !is_valid_target_for_ability(actor, *target, true)) {
                    // A legal declaration target may leave while responses resolve.
                    // Skip only the dependent effect; later independent effects continue.
                    continue;
                }
                int dmg = rec.amount;
                if (dmg < 0) {
                    // Negative amount encodes "use cracks (capped at -amount)".
                    dmg = std::min(-dmg, players_[to_index(actor)].cracks);
                }
                if (dmg > 0) {
                    damage_unit(target->unit, dmg);
                }
                break;
            }

            case EffectKind::DealDamageToLeader:
                damage_leader(opponent(actor), rec.amount);
                break;

            case EffectKind::HealLeader:
                heal_leader(actor, rec.amount);
                break;

            case EffectKind::RepairCracks:
                repair_cracks(actor, rec.amount);
                break;

            case EffectKind::GainPPCapacity:
                gain_pp_capacity(actor, rec.amount);
                break;

            case EffectKind::GrantRush:
                // v0.4: the affected unit may attack enemy units this turn.
                if (source != 0 && instances_.contains(source) && instances_.at(source).zone == Zone::Unit) {
                    instances_.at(source).temporary_rush = true;
                }
                break;

            case EffectKind::BuffFriendlyUnit: {
                if (!target.has_value() || target->kind != Target::Kind::Unit ||
                    !is_controlled_unit(actor, target->unit)) {
                    continue;
                }
                CardInstance& unit = instances_.at(target->unit);
                unit.current_attack += rec.amount;
                unit.current_health += rec.amount;
                unit.maximum_health += rec.amount;
                break;
            }

            case EffectKind::CancelAttack:
            case EffectKind::DamageEnteredUnit:
                // These are handled directly inside resolve_response_chain().
                break;
        }
    }
    return Status::ok();
}

std::optional<std::size_t> Game::first_free_unit_slot(const PlayerId player_id) const {
    const auto& slots = players_[to_index(player_id)].units;
    for (std::size_t index = 0; index < slots.size(); ++index) {
        if (!slots[index].has_value()) {
            return index;
        }
    }
    return std::nullopt;
}

bool Game::contains_guard(const PlayerId player_id) const {
    for (const auto& slot : players_[to_index(player_id)].units) {
        if (slot.has_value() && has_keyword(instances_.at(*slot).keywords, Keyword::Guard)) {
            return true;
        }
    }
    return false;
}

bool Game::target_is_guard(const Target& target) const {
    return target.kind == Target::Kind::Unit && instances_.contains(target.unit) &&
           instances_.at(target.unit).zone == Zone::Unit &&
           has_keyword(instances_.at(target.unit).keywords, Keyword::Guard);
}

bool Game::can_attack_now(const CardInstance& attacker, const Target& target) const {
    if (!attacker.entered_this_turn) {
        return true;
    }
    const bool has_rush  = has_keyword(attacker.keywords, Keyword::Rush) || attacker.temporary_rush;
    const bool has_storm = has_keyword(attacker.keywords, Keyword::Storm);
    if (target.kind == Target::Kind::Unit) {
        return has_rush || has_storm;
    }
    return has_storm;
}

Status Game::validate_attack(
    const PlayerId player_id,
    const InstanceId attacker_id,
    const Target& target) const {
    const Status allowed = ensure_action_player(player_id);
    if (!allowed) {
        return allowed;
    }
    if (!is_controlled_unit(player_id, attacker_id)) {
        return Status::error(ErrorCode::InvalidCard, "attacker is not a controlled unit");
    }
    const CardInstance& attacker = instances_.at(attacker_id);
    if (attacker.current_attack <= 0) {
        return Status::error(ErrorCode::InvalidCard, "a unit with zero attack cannot attack");
    }
    if (attacker.attacked_this_turn) {
        return Status::error(ErrorCode::AlreadyAttacked, "unit has already attacked this turn");
    }
    if (target.kind != Target::Kind::Leader && target.kind != Target::Kind::Unit) {
        return Status::error(ErrorCode::InvalidTarget, "attack target kind is outside the supported range");
    }
    if (target.player != opponent(player_id)) {
        return Status::error(ErrorCode::InvalidTarget, "attack target must belong to the opponent");
    }
    if (target.kind == Target::Kind::Leader && target.unit != 0) {
        return Status::error(ErrorCode::InvalidTarget, "leader targets cannot carry a unit instance");
    }
    if (target.kind == Target::Kind::Unit) {
        if (!is_enemy_unit(player_id, target.unit)) {
            return Status::error(ErrorCode::InvalidTarget, "target unit is not on the enemy field");
        }
        if (has_keyword(instances_.at(target.unit).keywords, Keyword::Ambush)) {
            return Status::error(ErrorCode::InvalidTarget, "ambush unit cannot be selected as an attack target");
        }
    }
    if (contains_guard(opponent(player_id)) && !target_is_guard(target)) {
        return Status::error(ErrorCode::GuardBlocksTarget, "an enemy guard unit must be attacked first");
    }
    if (!can_attack_now(attacker, target)) {
        return Status::error(ErrorCode::SummoningSickness, "unit cannot attack this target on its entry turn");
    }
    return Status::ok();
}

void Game::resolve_unit_combat(const InstanceId attacker_id, const InstanceId defender_id) {
    const CardInstance attacker_before = instances_.at(attacker_id);
    const CardInstance defender_before = instances_.at(defender_id);

    const int damage_to_defender = damage_unit(defender_id, attacker_before.current_attack);
    const int damage_to_attacker = damage_unit(attacker_id, defender_before.current_attack);

    if (has_keyword(attacker_before.keywords, Keyword::Bane) && damage_to_defender > 0) {
        instances_.at(defender_id).current_health = 0;
    }
    if (has_keyword(defender_before.keywords, Keyword::Bane) && damage_to_attacker > 0) {
        instances_.at(attacker_id).current_health = 0;
    }
    if (has_keyword(attacker_before.keywords, Keyword::Lifesteal) && damage_to_defender > 0) {
        heal_leader(attacker_before.controller, damage_to_defender);
    }
    if (has_keyword(defender_before.keywords, Keyword::Lifesteal) && damage_to_attacker > 0) {
        heal_leader(defender_before.controller, damage_to_attacker);
    }
    resolve_deaths();
}

void Game::resolve_leader_attack(const InstanceId attacker_id, const PlayerId defender) {
    const CardInstance attacker = instances_.at(attacker_id);
    const int actual = std::min(attacker.current_attack, players_[to_index(defender)].leader_health);
    damage_leader(defender, attacker.current_attack);
    if (has_keyword(attacker.keywords, Keyword::Lifesteal) && actual > 0) {
        heal_leader(attacker.controller, actual);
    }
}

void Game::move_from_current_zone(const InstanceId card_id) {
    CardInstance& card = instances_.at(card_id);
    PlayerState& state = players_[to_index(card.controller)];

    auto erase_from = [&](std::vector<InstanceId>& values) {
        values.erase(std::remove(values.begin(), values.end(), card_id), values.end());
    };

    switch (card.zone) {
        case Zone::Deck:
            erase_from(state.deck);
            normalize_sequences(card.controller, Zone::Deck);
            break;
        case Zone::Hand:
            erase_from(state.hand);
            normalize_sequences(card.controller, Zone::Hand);
            break;
        case Zone::Unit:
            for (auto& slot : state.units) {
                if (slot.has_value() && *slot == card_id) {
                    slot.reset();
                    break;
                }
            }
            break;
        case Zone::Tactic:
            for (auto& slot : state.tactics) {
                if (slot.has_value() && *slot == card_id) {
                    slot.reset();
                    break;
                }
            }
            break;
        case Zone::Graveyard:
            erase_from(state.graveyard);
            normalize_sequences(card.controller, Zone::Graveyard);
            break;
        case Zone::Archive:
            erase_from(state.archive);
            normalize_sequences(card.controller, Zone::Archive);
            break;
        case Zone::Standby:
            erase_from(state.standby);
            normalize_sequences(card.controller, Zone::Standby);
            break;
        case Zone::None:
            break;
    }
    if (card.zone == Zone::Unit) {
        // v0.4 §31: granted components never survive leaving the field.
        card.granted_component = ComponentSpec{};
    }
    card.zone = Zone::None;
    card.sequence = 0;
}

void Game::put_in_hand(const PlayerId player_id, const InstanceId card_id) {
    move_from_current_zone(card_id);
    PlayerState& state = players_[to_index(player_id)];
    CardInstance& card = instances_.at(card_id);
    card.controller = player_id;
    if (state.hand.size() >= static_cast<std::size_t>(config_.hand_limit)) {
        card.zone = Zone::Archive;
        card.sequence = state.archive.size();
        state.archive.push_back(card_id);
        emit(EventType::HandOverflowArchived, player_id, card_id, static_cast<int>(state.hand.size()));
        return;
    }
    card.zone = Zone::Hand;
    card.sequence = state.hand.size();
    state.hand.push_back(card_id);
}

void Game::put_in_unit_slot(
    const PlayerId player_id,
    const InstanceId card_id,
    const std::size_t slot,
    const bool deployed_from_standby) {
    move_from_current_zone(card_id);
    PlayerState& state = players_[to_index(player_id)];
    CardInstance& card = instances_.at(card_id);
    const CardDefinition& def = catalog_.at(card.definition_id);
    card.controller = player_id;
    card.zone = Zone::Unit;
    card.sequence = slot;
    card.current_attack = def.attack;
    card.current_health = def.health;
    card.maximum_health = def.health;
    card.keywords = flags_to_keywords(def);
    card.inherited_imprint = Imprint::None;
    card.granted_component = ComponentSpec{};
    card.evolved = false;
    card.attacked_this_turn = false;
    card.entered_this_turn = true;
    card.temporary_rush = false;
    card.deployed_from_standby = deployed_from_standby;
    card.face_down = false;
    card.countdown = 0;
    state.units[slot] = card_id;
    emit(EventType::CardMoved, player_id, card_id, static_cast<int>(Zone::Unit), static_cast<int>(slot));
}

void Game::put_in_tactic_slot(const PlayerId player_id, const InstanceId card_id, const std::size_t slot) {
    move_from_current_zone(card_id);
    PlayerState& state = players_[to_index(player_id)];
    CardInstance& card = instances_.at(card_id);
    const CardDefinition& def = catalog_.at(card.definition_id);
    card.controller = player_id;
    card.zone = Zone::Tactic;
    card.sequence = slot;
    card.face_down = def.kind == CardKind::Trap;
    card.countdown = def.countdown;
    state.tactics[slot] = card_id;
    emit(EventType::CardMoved, player_id, card_id, static_cast<int>(Zone::Tactic), static_cast<int>(slot));
}

void Game::put_in_graveyard(const PlayerId player_id, const InstanceId card_id) {
    move_from_current_zone(card_id);
    PlayerState& state = players_[to_index(player_id)];
    CardInstance& card = instances_.at(card_id);
    card.controller = player_id;
    card.zone = Zone::Graveyard;
    card.sequence = state.graveyard.size();
    card.face_down = false;
    card.inherited_imprint = Imprint::None;
    state.graveyard.push_back(card_id);
    emit(EventType::CardMoved, player_id, card_id, static_cast<int>(Zone::Graveyard));
}

void Game::put_in_archive(const PlayerId player_id, const InstanceId card_id) {
    move_from_current_zone(card_id);
    PlayerState& state = players_[to_index(player_id)];
    CardInstance& card = instances_.at(card_id);
    card.controller = player_id;
    card.zone = Zone::Archive;
    card.sequence = state.archive.size();
    card.face_down = false;
    card.inherited_imprint = Imprint::None;
    state.archive.push_back(card_id);
    emit(EventType::CardMoved, player_id, card_id, static_cast<int>(Zone::Archive));
}

void Game::normalize_sequences(const PlayerId player_id, const Zone zone) {
    std::vector<InstanceId>* values = nullptr;
    PlayerState& state = players_[to_index(player_id)];
    switch (zone) {
        case Zone::Deck:
            values = &state.deck;
            break;
        case Zone::Hand:
            values = &state.hand;
            break;
        case Zone::Graveyard:
            values = &state.graveyard;
            break;
        case Zone::Archive:
            values = &state.archive;
            break;
        case Zone::Standby:
            values = &state.standby;
            break;
        default:
            return;
    }
    for (std::size_t index = 0; index < values->size(); ++index) {
        instances_.at((*values)[index]).sequence = index;
    }
}

bool Game::vector_contains(const std::vector<InstanceId>& values, const InstanceId id) const {
    return std::find(values.begin(), values.end(), id) != values.end();
}

bool Game::is_controlled_unit(const PlayerId player_id, const InstanceId id) const {
    return instances_.contains(id) && instances_.at(id).zone == Zone::Unit &&
           instances_.at(id).controller == player_id;
}

bool Game::is_enemy_unit(const PlayerId player_id, const InstanceId id) const {
    return instances_.contains(id) && instances_.at(id).zone == Zone::Unit &&
           instances_.at(id).controller == opponent(player_id);
}

bool Game::is_valid_target_for_ability(
    const PlayerId actor,
    const Target& target,
    const bool require_enemy_unit) const {
    if (require_enemy_unit) {
        if (target.kind != Target::Kind::Unit || !is_valid_player(target.player) ||
            target.player != opponent(actor)) {
            return false;
        }
        if (!is_enemy_unit(actor, target.unit)) {
            return false;
        }
        if (has_keyword(instances_.at(target.unit).keywords, Keyword::Ambush)) {
            return false;
        }
        return true;
    }
    return true;
}

bool Game::deployment_condition_met(const PlayerId player_id, const DeploymentSpec& spec) const {
    const PlayerState& state = players_[to_index(player_id)];
    switch (spec.condition) {
        case DeploymentCondition::None:
            return true;
        case DeploymentCondition::FriendlyUnitsMin: {
            int count = 0;
            for (const auto& slot : state.units) {
                count += slot.has_value() ? 1 : 0;
            }
            return count >= spec.condition_amount;
        }
        case DeploymentCondition::SpellsThisTurnMin:
            return state.spells_used_this_turn >= spec.condition_amount;
    }
    return false;
}

void Game::evaluate_result() {
    if (result_ != GameResult::Ongoing) {
        if (terminal_cleanup_defer_depth_ == 0U) {
            finalize_match_end();
        }
        return;
    }
    const bool p0_dead = players_[0].leader_health <= 0;
    const bool p1_dead = players_[1].leader_health <= 0;
    if (p0_dead && p1_dead) {
        result_ = GameResult::Draw;
    } else if (p0_dead) {
        result_ = GameResult::Player1Won;
    } else if (p1_dead) {
        result_ = GameResult::Player0Won;
    } else {
        return;
    }

    if (terminal_cleanup_defer_depth_ == 0U) {
        finalize_match_end();
    }
}

void Game::finalize_match_end() {
    if (result_ == GameResult::Ongoing || phase_ == Phase::Finished) {
        return;
    }
    if (!response_stack_.empty()) {
        finalize_declared_response_cards();
        finalize_suspended_spell(response_stack_.front().suspended);
    }
    response_stack_.clear();
    phase_ = Phase::Finished;
    emit(EventType::MatchEnded, active_player_, 0, static_cast<int>(result_));
}

void Game::emit(
    const EventType type,
    const PlayerId player,
    const InstanceId card,
    const int value,
    const int secondary_value,
    std::string text) {
    GameEvent event;
    event.type = type;
    event.player = player;
    event.card = card;
    event.value = value;
    event.secondary_value = secondary_value;
    event.text = std::move(text);
    event.sequence = next_event_sequence_++;
    events_.push_back(event);
    event_history_.push_back(std::move(event));
}

} // namespace scgs
