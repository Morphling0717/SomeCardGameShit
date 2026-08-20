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

bool ability_requires_enemy_unit(const Ability ability) noexcept {
    return ability == Ability::DealTwoToEnemyUnit ||
           ability == Ability::DealThreeToEnemyUnit;
}

bool ability_requires_friendly_unit(const Ability ability) noexcept {
    return ability == Ability::GiveFriendlyUnitOneOne;
}

} // namespace

Game::Game(CardCatalog catalog, DeckList player0_deck, DeckList player1_deck, GameConfig config)
    : catalog_(std::move(catalog)),
      deck_lists_{std::move(player0_deck), std::move(player1_deck)},
      config_(config),
      rng_(config.random_seed),
      active_player_(config.first_player) {}

Status Game::start() {
    if (phase_ != Phase::NotStarted) {
        return Status::error(ErrorCode::MatchAlreadyStarted, "match has already started");
    }
    initialize_decks();
    for (std::size_t index = 0; index < kPlayerCount; ++index) {
        draw_cards(static_cast<PlayerId>(index), config_.starting_hand_size);
    }
    phase_ = Phase::Mulligan;
    emit(EventType::MatchStarted, config_.first_player, 0, config_.leader_health, config_.starting_hand_size);
    return Status::ok();
}

Status Game::mulligan(const PlayerId player_id, const std::vector<InstanceId>& selected_cards) {
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

    std::vector<InstanceId> set_aside;
    set_aside.reserve(selected_cards.size());
    for (const InstanceId id : selected_cards) {
        move_from_current_zone(id);
        instances_.at(id).zone = Zone::None;
        set_aside.push_back(id);
    }

    draw_cards(player_id, static_cast<int>(set_aside.size()));

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
    if (players_[0].mulligan_done && players_[1].mulligan_done) {
        begin_turn(config_.first_player);
    }
    return Status::ok();
}

Status Game::end_turn(const PlayerId player_id) {
    const Status allowed = ensure_action_player(player_id);
    if (!allowed) {
        return allowed;
    }
    emit(EventType::TurnEnded, player_id, 0, players_[to_index(player_id)].own_turn_number);
    clear_end_of_turn_state(player_id);
    begin_turn(opponent(player_id));
    return Status::ok();
}

Status Game::surrender(const PlayerId player_id) {
    const Status allowed = ensure_not_finished();
    if (!allowed) {
        return allowed;
    }
    result_ = player_id == PlayerId::Player0 ? GameResult::Player1Won : GameResult::Player0Won;
    phase_ = Phase::Finished;
    pending_reaction_.reset();
    emit(EventType::PlayerSurrendered, player_id);
    emit(EventType::MatchEnded, active_player_, 0, static_cast<int>(result_));
    return Status::ok();
}

Status Game::play_unit(
    const PlayerId player_id,
    const InstanceId card_id,
    const std::optional<std::size_t> preferred_slot,
    const std::optional<Target> ability_target) {
    const Status allowed = ensure_action_player(player_id);
    if (!allowed) {
        return allowed;
    }
    const auto iterator = instances_.find(card_id);
    if (iterator == instances_.end()) {
        return Status::error(ErrorCode::InvalidCard, "unknown card instance");
    }
    const CardInstance& card = iterator->second;
    const CardDefinition& definition_value = catalog_.at(card.definition_id);
    if (card.controller != player_id || card.zone != Zone::Hand || definition_value.kind != CardKind::Unit) {
        return Status::error(ErrorCode::InvalidZone, "only a unit in the active player's hand can be played");
    }

    PlayerState& state = players_[to_index(player_id)];
    if (state.current_pp < definition_value.cost) {
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

    if (ability_requires_enemy_unit(definition_value.entry_ability) &&
        (!ability_target.has_value() || !is_valid_target_for_ability(player_id, *ability_target, true))) {
        return Status::error(ErrorCode::InvalidTarget, "entry ability requires a legal enemy unit target");
    }

    state.current_pp -= definition_value.cost;
    emit(EventType::PPChanged, player_id, 0, state.current_pp, state.maximum_pp);
    put_in_unit_slot(player_id, card_id, *slot, false);
    emit(EventType::UnitEntered, player_id, card_id, static_cast<int>(*slot));

    const Status ability_status = resolve_ability(definition_value.entry_ability, player_id, card_id, ability_target);
    if (!ability_status) {
        return ability_status;
    }
    resolve_deaths();
    evaluate_result();
    if (result_ == GameResult::Ongoing && instances_.at(card_id).zone == Zone::Unit) {
        open_reaction_window(ReactionWindow::AfterEnemyUnitSummoned, opponent(player_id), card_id);
    }
    return Status::ok();
}

Status Game::cast_spell(
    const PlayerId player_id,
    const InstanceId card_id,
    const std::optional<Target> ability_target) {
    const Status allowed = ensure_action_player(player_id);
    if (!allowed) {
        return allowed;
    }
    const auto iterator = instances_.find(card_id);
    if (iterator == instances_.end()) {
        return Status::error(ErrorCode::InvalidCard, "unknown card instance");
    }
    const CardInstance& card = iterator->second;
    const CardDefinition& definition_value = catalog_.at(card.definition_id);
    if (card.controller != player_id || card.zone != Zone::Hand || definition_value.kind != CardKind::Spell) {
        return Status::error(ErrorCode::InvalidZone, "only a spell in the active player's hand can be cast");
    }
    PlayerState& state = players_[to_index(player_id)];
    if (state.current_pp < definition_value.cost) {
        return Status::error(ErrorCode::InsufficientPP, "not enough PP");
    }
    if (ability_requires_enemy_unit(definition_value.play_ability) &&
        (!ability_target.has_value() || !is_valid_target_for_ability(player_id, *ability_target, true))) {
        return Status::error(ErrorCode::InvalidTarget, "spell requires a legal enemy unit target");
    }

    state.current_pp -= definition_value.cost;
    emit(EventType::PPChanged, player_id, 0, state.current_pp, state.maximum_pp);
    put_in_graveyard(player_id, card_id);
    const Status ability_status = resolve_ability(definition_value.play_ability, player_id, card_id, ability_target);
    if (!ability_status) {
        return ability_status;
    }
    resolve_deaths();
    evaluate_result();
    if (result_ == GameResult::Ongoing) {
        open_reaction_window(ReactionWindow::AfterEnemySpellResolved, opponent(player_id), card_id);
    }
    return Status::ok();
}

Status Game::play_tactic(const PlayerId player_id, const InstanceId card_id, const std::size_t slot) {
    const Status allowed = ensure_action_player(player_id);
    if (!allowed) {
        return allowed;
    }
    if (slot >= kTacticZoneSize) {
        return Status::error(ErrorCode::InvalidSlot, "tactic slot is out of range");
    }
    const auto iterator = instances_.find(card_id);
    if (iterator == instances_.end()) {
        return Status::error(ErrorCode::InvalidCard, "unknown card instance");
    }
    const CardInstance& card = iterator->second;
    const CardDefinition& definition_value = catalog_.at(card.definition_id);
    if (card.controller != player_id || card.zone != Zone::Hand ||
        (definition_value.kind != CardKind::Relic && definition_value.kind != CardKind::Trap)) {
        return Status::error(ErrorCode::InvalidZone, "only a relic or trap in hand can enter the tactic zone");
    }

    PlayerState& state = players_[to_index(player_id)];
    if (definition_value.kind == CardKind::Trap && state.trap_set_this_turn) {
        return Status::error(ErrorCode::TrapAlreadySetThisTurn, "only one trap may be set per turn");
    }
    if (state.current_pp < definition_value.cost) {
        return Status::error(ErrorCode::InsufficientPP, "not enough PP");
    }

    if (state.tactics[slot].has_value()) {
        put_in_graveyard(player_id, *state.tactics[slot]);
    }
    state.current_pp -= definition_value.cost;
    emit(EventType::PPChanged, player_id, 0, state.current_pp, state.maximum_pp);
    put_in_tactic_slot(player_id, card_id, slot);
    if (definition_value.kind == CardKind::Trap) {
        state.trap_set_this_turn = true;
    }
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
    open_reaction_window(
        ReactionWindow::BeforeAttackDamage,
        opponent(player_id),
        subject,
        pending_attack);
    return Status::ok();
}

Status Game::evolve(
    const PlayerId player_id,
    const InstanceId unit_id,
    const EvolutionMode mode,
    const std::optional<Target> ability_target,
    const bool free_evolution,
    const bool ignore_turn_limit) {
    const Status allowed = ensure_action_player(player_id);
    if (!allowed) {
        return allowed;
    }
    if (!is_controlled_unit(player_id, unit_id)) {
        return Status::error(ErrorCode::InvalidCard, "selected unit is not controlled by the active player");
    }

    PlayerState& state = players_[to_index(player_id)];
    const bool is_first_player = player_id == config_.first_player;
    const int unlock_turn = is_first_player ? 5 : 4;
    if (state.own_turn_number < unlock_turn) {
        return Status::error(ErrorCode::EvolutionLocked, "evolution is not unlocked yet");
    }
    if (!ignore_turn_limit && state.evolution_used_this_turn) {
        return Status::error(ErrorCode::EvolutionAlreadyUsed, "an evolution was already used this turn");
    }
    if (!free_evolution && state.evolution_points <= 0) {
        return Status::error(ErrorCode::NoEvolutionPoints, "no evolution points remain");
    }

    CardInstance& unit = instances_.at(unit_id);
    const CardDefinition& definition_value = catalog_.at(unit.definition_id);
    if (unit.evolved) {
        return Status::error(ErrorCode::AlreadyEvolved, "unit has already evolved");
    }
    if (mode == EvolutionMode::Ability && definition_value.evolution_ability == Ability::None) {
        return Status::error(ErrorCode::AbilityEvolutionUnavailable, "unit has no ability-evolution text");
    }
    if (ability_requires_enemy_unit(definition_value.evolution_ability) &&
        (!ability_target.has_value() || !is_valid_target_for_ability(player_id, *ability_target, true))) {
        return Status::error(ErrorCode::InvalidTarget, "ability evolution requires a legal enemy unit target");
    }

    const int bonus = mode == EvolutionMode::Combat ? 2 : 1;
    unit.current_attack += bonus;
    unit.current_health += bonus;
    unit.maximum_health += bonus;
    unit.evolved = true;
    if (mode == EvolutionMode::Combat) {
        unit.temporary_rush = true;
    }
    if (!free_evolution) {
        --state.evolution_points;
    }
    if (!ignore_turn_limit) {
        state.evolution_used_this_turn = true;
    }
    emit(
        EventType::UnitEvolved,
        player_id,
        unit_id,
        static_cast<int>(mode),
        state.evolution_points);

    if (mode == EvolutionMode::Ability) {
        const Status ability_status = resolve_ability(
            definition_value.evolution_ability,
            player_id,
            unit_id,
            ability_target);
        if (!ability_status) {
            return ability_status;
        }
        resolve_deaths();
        evaluate_result();
    }
    return Status::ok();
}

Status Game::advanced_summon(const AdvancedSummonRequest& request) {
    const Status allowed = ensure_action_player(request.player);
    if (!allowed) {
        return allowed;
    }
    PlayerState& state = players_[to_index(request.player)];
    if (state.advanced_summon_used_this_turn) {
        return Status::error(ErrorCode::AdvancedSummonAlreadyUsed, "an advanced summon was already used this turn");
    }

    const auto target_iterator = instances_.find(request.card);
    if (target_iterator == instances_.end()) {
        return Status::error(ErrorCode::InvalidCard, "unknown advanced-summon card");
    }
    const CardInstance& target_card = target_iterator->second;
    const CardDefinition& target_definition = catalog_.at(target_card.definition_id);
    if (target_definition.advanced_kind == AdvancedSummonKind::None) {
        return Status::error(ErrorCode::InvalidCard, "selected card has no advanced summon procedure");
    }
    if (target_card.controller != request.player) {
        return Status::error(ErrorCode::InvalidCard, "selected card belongs to the other player");
    }
    if (target_definition.advanced_kind == AdvancedSummonKind::Tribute && target_card.zone != Zone::Hand) {
        return Status::error(ErrorCode::InvalidZone, "tribute target must be in hand");
    }
    if (target_definition.advanced_kind == AdvancedSummonKind::Construct && target_card.zone != Zone::SummonDeck) {
        return Status::error(ErrorCode::InvalidZone, "construct target must be in the summon deck");
    }
    if (state.current_pp < target_definition.advanced_cost) {
        return Status::error(ErrorCode::InsufficientPP, "not enough PP for the advanced summon");
    }

    if (request.materials.size() < static_cast<std::size_t>(target_definition.min_materials) ||
        request.materials.size() > static_cast<std::size_t>(target_definition.max_materials) ||
        request.materials.size() > 3U) {
        return Status::error(ErrorCode::InvalidMaterials, "wrong number of materials");
    }

    std::set<InstanceId> unique_materials;
    int original_cost_sum = 0;
    for (const InstanceId material_id : request.materials) {
        if (!unique_materials.insert(material_id).second) {
            return Status::error(ErrorCode::DuplicateSelection, "the same material was selected twice");
        }
        if (!is_controlled_unit(request.player, material_id)) {
            return Status::error(ErrorCode::InvalidMaterials, "all materials must be controlled units");
        }
        const CardDefinition& material_definition = definition(material_id);
        if (!has_all_traits(material_definition.traits, target_definition.required_material_traits)) {
            return Status::error(ErrorCode::InvalidMaterials, "a material does not satisfy the required traits");
        }
        original_cost_sum += material_definition.cost;
    }
    if (original_cost_sum < target_definition.min_material_original_cost_sum) {
        return Status::error(ErrorCode::InvalidMaterials, "material original-cost total is too low");
    }
    if (!can_inherit_imprint(request.materials, request.inherited_imprint)) {
        return Status::error(ErrorCode::InvalidImprint, "chosen imprint is not printed on any selected material");
    }
    if (ability_requires_enemy_unit(target_definition.entry_ability) &&
        (!request.ability_target.has_value() ||
         !is_valid_target_for_ability(request.player, *request.ability_target, true))) {
        return Status::error(ErrorCode::InvalidTarget, "advanced summon entry ability requires a legal enemy unit");
    }

    std::size_t occupied = 0;
    for (const auto& slot : state.units) {
        occupied += slot.has_value() ? 1U : 0U;
    }
    if (occupied - request.materials.size() >= kUnitZoneSize) {
        return Status::error(ErrorCode::UnitZoneFull, "materials do not free a unit slot");
    }

    state.current_pp -= target_definition.advanced_cost;
    state.advanced_summon_used_this_turn = true;
    emit(EventType::PPChanged, request.player, 0, state.current_pp, state.maximum_pp);

    for (const InstanceId material_id : request.materials) {
        put_in_archive(request.player, material_id);
    }

    const std::optional<std::size_t> slot = first_free_unit_slot(request.player);
    if (!slot.has_value()) {
        throw std::logic_error("validated advanced summon failed to free a slot");
    }
    put_in_unit_slot(request.player, request.card, *slot, true);
    CardInstance& summoned = instances_.at(request.card);
    apply_imprint(summoned, request.inherited_imprint);
    if (request.inherited_imprint != Imprint::None) {
        emit(
            EventType::ImprintInherited,
            request.player,
            request.card,
            static_cast<int>(request.inherited_imprint));
    }
    emit(
        EventType::AdvancedSummoned,
        request.player,
        request.card,
        static_cast<int>(target_definition.advanced_kind),
        static_cast<int>(request.materials.size()));
    emit(EventType::UnitEntered, request.player, request.card, static_cast<int>(*slot));

    const Status ability_status = resolve_ability(
        target_definition.entry_ability,
        request.player,
        request.card,
        request.ability_target);
    if (!ability_status) {
        return ability_status;
    }
    resolve_deaths();
    evaluate_result();
    if (result_ == GameResult::Ongoing && summoned.zone == Zone::Unit) {
        open_reaction_window(
            ReactionWindow::AfterEnemyUnitSummoned,
            opponent(request.player),
            request.card);
    }
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
    if (ability_requires_friendly_unit(state.leader_skill.ability) &&
        (!target.has_value() || target->kind != Target::Kind::Unit || !is_controlled_unit(player_id, target->unit))) {
        return Status::error(ErrorCode::InvalidTarget, "leader skill requires a friendly unit target");
    }

    state.current_pp -= state.leader_skill.cost;
    state.leader_skill_used = true;
    emit(EventType::PPChanged, player_id, 0, state.current_pp, state.maximum_pp);
    emit(EventType::LeaderSkillUsed, player_id, 0, state.leader_skill.cost, 0, state.leader_skill.name);
    return resolve_ability(state.leader_skill.ability, player_id, 0, target);
}

Status Game::activate_trap(
    const PlayerId player_id,
    const InstanceId trap_id,
    const std::optional<Target> target) {
    (void)target;
    if (phase_ != Phase::Reaction || !pending_reaction_.has_value()) {
        return Status::error(ErrorCode::NoPendingReaction, "there is no trap window");
    }
    PendingReaction reaction = *pending_reaction_;
    if (reaction.responder != player_id) {
        return Status::error(ErrorCode::InvalidPlayer, "the other player owns this reaction window");
    }
    if (!vector_contains(reaction.eligible_traps, trap_id)) {
        return Status::error(ErrorCode::TrapNotEligible, "selected trap is not eligible for this event");
    }
    if (instances_.at(trap_id).zone != Zone::Tactic) {
        return Status::error(ErrorCode::InvalidZone, "trap is no longer in the tactic zone");
    }

    const Ability trap_ability = definition(trap_id).trap_ability;
    put_in_graveyard(player_id, trap_id);
    emit(EventType::TrapActivated, player_id, trap_id, static_cast<int>(reaction.window));

    if (trap_ability == Ability::TrapCancelAttack) {
        if (!reaction.attack.has_value()) {
            return Status::error(ErrorCode::TrapNotEligible, "attack trap was used outside an attack window");
        }
        emit(EventType::AttackCancelled, reaction.attack->player, reaction.attack->attacker);
        close_reaction_window();
        return Status::ok();
    }
    if (trap_ability == Ability::TrapDamageSummonedUnitTwo) {
        if (reaction.subject != 0 && instances_.contains(reaction.subject) &&
            instances_.at(reaction.subject).zone == Zone::Unit) {
            damage_unit(reaction.subject, 2);
            resolve_deaths();
            evaluate_result();
        }
        close_reaction_window();
        return Status::ok();
    }

    return Status::error(ErrorCode::TrapNotEligible, "trap ability is not implemented for this window");
}

Status Game::pass_reaction(const PlayerId player_id) {
    if (phase_ != Phase::Reaction || !pending_reaction_.has_value()) {
        return Status::error(ErrorCode::NoPendingReaction, "there is no reaction to pass");
    }
    if (pending_reaction_->responder != player_id) {
        return Status::error(ErrorCode::InvalidPlayer, "the other player owns this reaction window");
    }
    if (pending_reaction_->attack.has_value()) {
        resolve_pending_attack();
    } else {
        close_reaction_window();
    }
    return Status::ok();
}

Status Game::load_scenario(const Scenario& scenario) {
    if (phase_ != Phase::NotStarted) {
        return Status::error(ErrorCode::MatchAlreadyStarted, "scenario must be loaded before starting a match");
    }
    players_ = {};
    instances_.clear();
    next_instance_id_ = 1;
    result_ = GameResult::Ongoing;
    pending_reaction_.reset();
    events_.clear();

    for (std::size_t player_index = 0; player_index < kPlayerCount; ++player_index) {
        const PlayerId player_id = static_cast<PlayerId>(player_index);
        const ScenarioPlayer& source = scenario.players[player_index];
        PlayerState& destination = players_[player_index];
        destination.leader_health = source.leader_health;
        destination.maximum_leader_health = source.maximum_leader_health;
        destination.current_pp = source.current_pp;
        destination.maximum_pp = source.maximum_pp;
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
            put_in_unit_slot(player_id, instance_id, *slot, false);
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
                return Status::error(ErrorCode::TacticZoneFull, "scenario has more than two tactics");
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
        for (const CardId id : source.summon_deck) {
            create_instance(id, player_id, Zone::SummonDeck);
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

Phase Game::phase() const noexcept {
    return phase_;
}

GameResult Game::result() const noexcept {
    return result_;
}

ReactionWindow Game::reaction_window() const noexcept {
    return pending_reaction_.has_value() ? pending_reaction_->window : ReactionWindow::None;
}

const std::vector<InstanceId>& Game::eligible_traps() const noexcept {
    static const std::vector<InstanceId> empty;
    return pending_reaction_.has_value() ? pending_reaction_->eligible_traps : empty;
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

    for (std::size_t player_index = 0; player_index < kPlayerCount; ++player_index) {
        const PlayerId player_id = static_cast<PlayerId>(player_index);
        const PlayerState& state = players_[player_index];

        if (state.maximum_leader_health <= 0) {
            problems.push_back("player " + std::to_string(player_index) + " has a non-positive leader-health limit");
        }
        if (state.leader_health < 0 || state.leader_health > state.maximum_leader_health) {
            problems.push_back("player " + std::to_string(player_index) + " has leader health outside its limits");
        }
        if (state.maximum_pp < 0 || state.maximum_pp > config_.maximum_pp ||
            state.current_pp < 0 || state.current_pp > state.maximum_pp) {
            problems.push_back("player " + std::to_string(player_index) + " has an invalid PP state");
        }
        if (state.evolution_points < 0 || state.own_turn_number < 0 || state.fatigue_count < 0) {
            problems.push_back("player " + std::to_string(player_index) + " has a negative counter");
        }
        if (state.hand.size() > static_cast<std::size_t>(config_.hand_limit)) {
            problems.push_back("player " + std::to_string(player_index) + " exceeds the hand limit");
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
        record_vector(state.summon_deck, Zone::SummonDeck);

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
            if (card_definition.kind != CardKind::Unit && card_definition.kind != CardKind::SummonUnit) {
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
            if (card_definition.kind != CardKind::Relic && card_definition.kind != CardKind::Trap) {
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
            // A stable public state must not leave cards detached. During a
            // mulligan operation the set-aside cards are private and are put
            // back before control returns to the caller.
            problems.push_back("instance " + std::to_string(id) + " is detached from every zone");
        } else if (count != 1) {
            problems.push_back("instance " + std::to_string(id) + " occurs in " +
                               std::to_string(count) + " zone containers");
        }
        if (!catalog_.contains(card.definition_id)) {
            problems.push_back("instance " + std::to_string(id) + " references an unknown card definition");
            continue;
        }
        const CardDefinition& card_definition = catalog_.at(card.definition_id);
        if (card.zone == Zone::SummonDeck && card_definition.kind != CardKind::SummonUnit) {
            problems.push_back("non-summon unit instance " + std::to_string(id) + " is in the summon deck");
        }
        if (card.zone == Zone::Deck && card_definition.kind == CardKind::SummonUnit) {
            problems.push_back("summon unit instance " + std::to_string(id) + " is in a main deck");
        }
        if (card.zone != Zone::Unit && card.inherited_imprint != Imprint::None) {
            problems.push_back("off-field instance " + std::to_string(id) + " retains an inherited imprint");
        }
    }
    for (const auto& [id, count] : occurrences) {
        if (!instances_.contains(id) && count > 0) {
            // Already reported by record(), retained here to make accidental
            // duplicate unknown references visible as a separate corruption.
            if (count > 1) {
                problems.push_back("unknown instance " + std::to_string(id) + " is referenced multiple times");
            }
        }
    }
    if (!instances_.empty() && next_instance_id_ <= maximum_id) {
        problems.push_back("next instance id is not greater than every allocated id");
    }

    if (phase_ == Phase::Reaction) {
        if (!pending_reaction_.has_value()) {
            problems.push_back("reaction phase has no pending reaction");
        } else {
            if (pending_reaction_->window == ReactionWindow::None) {
                problems.push_back("pending reaction has no reaction-window type");
            }
            if (pending_reaction_->eligible_traps.empty()) {
                problems.push_back("reaction phase has no eligible trap");
            }
            for (const InstanceId id : pending_reaction_->eligible_traps) {
                if (!instances_.contains(id) || instances_.at(id).zone != Zone::Tactic ||
                    instances_.at(id).controller != pending_reaction_->responder) {
                    problems.push_back("pending reaction contains an invalid eligible trap");
                }
            }
        }
    } else if (pending_reaction_.has_value()) {
        problems.push_back("pending reaction exists outside the reaction phase");
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

    return problems;
}

std::optional<InstanceId> Game::find_in_hand(const PlayerId player_id, const CardId card_id) const {
    for (const InstanceId id : players_[to_index(player_id)].hand) {
        if (instance(id).definition_id == card_id) {
            return id;
        }
    }
    return std::nullopt;
}

std::optional<InstanceId> Game::find_on_field(const PlayerId player_id, const CardId card_id) const {
    for (const auto& slot : players_[to_index(player_id)].units) {
        if (slot.has_value() && instance(*slot).definition_id == card_id) {
            return *slot;
        }
    }
    return std::nullopt;
}

std::optional<InstanceId> Game::find_in_summon_deck(const PlayerId player_id, const CardId card_id) const {
    for (const InstanceId id : players_[to_index(player_id)].summon_deck) {
        if (instance(id).definition_id == card_id) {
            return id;
        }
    }
    return std::nullopt;
}

Status Game::ensure_action_player(const PlayerId player_id) const {
    const Status alive = ensure_not_finished();
    if (!alive) {
        return alive;
    }
    if (phase_ != Phase::Action) {
        return Status::error(ErrorCode::InvalidPhase, "an action cannot be taken in the current phase");
    }
    if (active_player_ != player_id) {
        return Status::error(ErrorCode::NotActivePlayer, "it is not this player's turn");
    }
    return Status::ok();
}

Status Game::ensure_not_finished() const {
    if (result_ != GameResult::Ongoing || phase_ == Phase::Finished) {
        return Status::error(ErrorCode::GameOver, "the match has already ended");
    }
    if (phase_ == Phase::NotStarted) {
        return Status::error(ErrorCode::MatchNotStarted, "the match has not started");
    }
    return Status::ok();
}

InstanceId Game::create_instance(const CardId card_id, const PlayerId owner, const Zone zone) {
    if (!catalog_.contains(card_id)) {
        throw std::invalid_argument("deck or scenario contains an unknown card id: " + std::to_string(card_id));
    }
    const InstanceId id = next_instance_id_++;
    CardInstance card;
    card.id = id;
    card.definition_id = card_id;
    card.owner = owner;
    card.controller = owner;
    card.zone = Zone::None;
    instances_.emplace(id, card);

    PlayerState& state = players_[to_index(owner)];
    CardInstance& stored = instances_.at(id);
    stored.zone = zone;
    switch (zone) {
        case Zone::Deck:
            stored.sequence = state.deck.size();
            state.deck.push_back(id);
            break;
        case Zone::SummonDeck:
            stored.sequence = state.summon_deck.size();
            state.summon_deck.push_back(id);
            break;
        case Zone::None:
            break;
        default:
            throw std::logic_error("create_instance only supports deck, summon deck, or no zone");
    }
    return id;
}

void Game::initialize_decks() {
    players_ = {};
    instances_.clear();
    next_instance_id_ = 1;
    result_ = GameResult::Ongoing;
    pending_reaction_.reset();
    events_.clear();
    active_player_ = config_.first_player;

    for (std::size_t player_index = 0; player_index < kPlayerCount; ++player_index) {
        const PlayerId player_id = static_cast<PlayerId>(player_index);
        PlayerState& state = players_[player_index];
        state.leader_health = config_.leader_health;
        state.maximum_leader_health = config_.leader_health;
        state.evolution_points = player_id == config_.first_player ? 2 : 3;
        state.leader_skill = deck_lists_[player_index].leader_skill;

        for (const CardId card_id : deck_lists_[player_index].main) {
            const CardDefinition& card_definition = catalog_.at(card_id);
            if (card_definition.kind == CardKind::SummonUnit) {
                throw std::invalid_argument("summon-deck card found in main deck");
            }
            create_instance(card_id, player_id, Zone::Deck);
        }
        for (const CardId card_id : deck_lists_[player_index].summon) {
            const CardDefinition& card_definition = catalog_.at(card_id);
            if (card_definition.kind != CardKind::SummonUnit) {
                throw std::invalid_argument("main-deck card found in summon deck");
            }
            create_instance(card_id, player_id, Zone::SummonDeck);
        }
        if (config_.shuffle_decks) {
            std::shuffle(state.deck.begin(), state.deck.end(), rng_);
            normalize_sequences(player_id, Zone::Deck);
        }
    }
}

void Game::begin_turn(const PlayerId player_id) {
    active_player_ = player_id;
    phase_ = Phase::Action;
    PlayerState& state = players_[to_index(player_id)];
    ++state.own_turn_number;
    state.evolution_used_this_turn = false;
    state.advanced_summon_used_this_turn = false;
    state.trap_set_this_turn = false;
    ready_units(player_id);
    state.maximum_pp = std::min(config_.maximum_pp, state.maximum_pp + 1);
    state.current_pp = state.maximum_pp;
    emit(EventType::PPChanged, player_id, 0, state.current_pp, state.maximum_pp);

    const bool skip_draw = player_id == config_.first_player && state.own_turn_number == 1;
    if (!skip_draw) {
        draw_one(player_id);
    }
    process_relic_countdowns(player_id);
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
        unit.advanced_summoned_this_turn = false;
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
        CardInstance& relic = instances_.at(id);
        if (relic.zone != Zone::Tactic || relic.countdown <= 0) {
            continue;
        }
        --relic.countdown;
        if (relic.countdown == 0) {
            const Ability ability = definition(id).countdown_expire_ability;
            put_in_graveyard(player_id, id);
            const Status status = resolve_ability(ability, player_id, id, std::nullopt);
            if (!status) {
                throw std::logic_error("countdown ability failed after prior validation: " + status.message);
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

void Game::draw_cards(const PlayerId player_id, const int count) {
    for (int i = 0; i < count && result_ == GameResult::Ongoing; ++i) {
        draw_one(player_id);
    }
}

void Game::draw_one(const PlayerId player_id) {
    PlayerState& state = players_[to_index(player_id)];
    if (state.deck.empty()) {
        ++state.fatigue_count;
        emit(EventType::FatigueDamage, player_id, 0, state.fatigue_count);
        damage_leader(player_id, state.fatigue_count);
        return;
    }
    const InstanceId card = state.deck.front();
    put_in_hand(player_id, card);
    if (instances_.at(card).zone == Zone::Hand) {
        emit(EventType::CardDrawn, player_id, card, static_cast<int>(state.hand.size()));
    }
}

void Game::damage_leader(const PlayerId player_id, const int amount) {
    if (amount <= 0) {
        return;
    }
    PlayerState& state = players_[to_index(player_id)];
    state.leader_health = std::max(0, state.leader_health - amount);
    emit(EventType::LeaderDamaged, player_id, 0, amount, state.leader_health);
    evaluate_result();
}

void Game::heal_leader(const PlayerId player_id, const int amount) {
    if (amount <= 0) {
        return;
    }
    PlayerState& state = players_[to_index(player_id)];
    const int before = state.leader_health;
    state.leader_health = std::min(state.maximum_leader_health, state.leader_health + amount);
    emit(EventType::LeaderHealed, player_id, 0, state.leader_health - before, state.leader_health);
}

int Game::damage_unit(const InstanceId unit_id, const int amount) {
    if (amount <= 0 || !instances_.contains(unit_id)) {
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
    struct DeathTrigger {
        PlayerId controller = PlayerId::Player0;
        InstanceId unit = 0;
        Ability last_words = Ability::None;
        bool imprint_draw = false;
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
            const PlayerId owner = unit.owner;
            const CardDefinition& unit_definition = catalog_.at(unit.definition_id);
            triggers.push_back(DeathTrigger{
                controller,
                unit_id,
                unit_definition.last_words_ability,
                unit.inherited_imprint == Imprint::LastWordsDrawOne,
            });
            const bool summon_unit = unit_definition.kind == CardKind::SummonUnit;
            if (summon_unit) {
                put_in_archive(owner, unit_id);
            } else {
                put_in_graveyard(owner, unit_id);
            }
            emit(EventType::UnitDestroyed, controller, unit_id, summon_unit ? 1 : 0);
        }

        // Every unit in the batch has left the field before any last-words effect
        // resolves. Triggers are already ordered active player first, then the
        // non-active player, matching the source rules.
        for (const DeathTrigger& trigger : triggers) {
            if (trigger.last_words != Ability::None) {
                const Status status = resolve_ability(
                    trigger.last_words,
                    trigger.controller,
                    trigger.unit,
                    std::nullopt);
                if (!status) {
                    throw std::logic_error("last words failed after trigger: " + status.message);
                }
            }
            if (trigger.imprint_draw) {
                draw_one(trigger.controller);
            }
        }
    }
}

Status Game::resolve_ability(
    const Ability ability,
    const PlayerId actor,
    const InstanceId source,
    const std::optional<Target> target) {
    (void)source;
    switch (ability) {
        case Ability::None:
            return Status::ok();
        case Ability::DrawOne:
            draw_one(actor);
            return Status::ok();
        case Ability::DealTwoToEnemyUnit:
        case Ability::DealThreeToEnemyUnit: {
            if (!target.has_value() || !is_valid_target_for_ability(actor, *target, true)) {
                return Status::error(ErrorCode::InvalidTarget, "damage ability requires an enemy unit");
            }
            const int amount = ability == Ability::DealTwoToEnemyUnit ? 2 : 3;
            damage_unit(target->unit, amount);
            return Status::ok();
        }
        case Ability::HealLeaderThree:
            heal_leader(actor, 3);
            return Status::ok();
        case Ability::GiveFriendlyUnitOneOne: {
            if (!target.has_value() || target->kind != Target::Kind::Unit ||
                !is_controlled_unit(actor, target->unit)) {
                return Status::error(ErrorCode::InvalidTarget, "buff ability requires a friendly unit");
            }
            CardInstance& unit = instances_.at(target->unit);
            ++unit.current_attack;
            ++unit.current_health;
            ++unit.maximum_health;
            return Status::ok();
        }
        case Ability::CreateRushPartInHand: {
            const InstanceId generated = create_instance(cards::kMachineRushPart, actor, Zone::None);
            put_in_hand(actor, generated);
            return Status::ok();
        }
        case Ability::TrapCancelAttack:
        case Ability::TrapDamageSummonedUnitTwo:
            return Status::error(ErrorCode::TrapNotEligible, "trap abilities resolve through a reaction window");
    }
    return Status::error(ErrorCode::InvalidCard, "unknown ability");
}

void Game::apply_imprint(CardInstance& unit, const Imprint imprint) {
    unit.inherited_imprint = imprint;
    switch (imprint) {
        case Imprint::None:
        case Imprint::LastWordsDrawOne:
            break;
        case Imprint::Guard:
            unit.keywords |= mask(Keyword::Guard);
            break;
        case Imprint::Rush:
            unit.keywords |= mask(Keyword::Rush);
            break;
        case Imprint::Barrier:
            unit.keywords |= mask(Keyword::Barrier);
            break;
        case Imprint::Lifesteal:
            unit.keywords |= mask(Keyword::Lifesteal);
            break;
    }
}

bool Game::can_inherit_imprint(
    const std::vector<InstanceId>& materials,
    const Imprint imprint) const {
    if (imprint == Imprint::None) {
        return true;
    }
    return std::any_of(materials.begin(), materials.end(), [&](const InstanceId id) {
        return definition(id).printed_imprint == imprint;
    });
}

std::optional<std::size_t> Game::first_free_unit_slot(const PlayerId player_id) const {
    const auto& units = players_[to_index(player_id)].units;
    for (std::size_t slot = 0; slot < units.size(); ++slot) {
        if (!units[slot].has_value()) {
            return slot;
        }
    }
    return std::nullopt;
}

bool Game::contains_guard(const PlayerId player_id) const {
    for (const auto& slot : players_[to_index(player_id)].units) {
        if (slot.has_value()) {
            const CardInstance& unit = instances_.at(*slot);
            if (unit.current_health > 0 && has_keyword(unit.keywords, Keyword::Guard)) {
                return true;
            }
        }
    }
    return false;
}

bool Game::target_is_guard(const Target& target) const {
    if (target.kind != Target::Kind::Unit || !instances_.contains(target.unit)) {
        return false;
    }
    const CardInstance& unit = instances_.at(target.unit);
    return unit.zone == Zone::Unit && has_keyword(unit.keywords, Keyword::Guard);
}

bool Game::can_attack_now(const CardInstance& attacker, const Target& target) const {
    if (!attacker.entered_this_turn) {
        return true;
    }
    const bool has_rush = has_keyword(attacker.keywords, Keyword::Rush) || attacker.temporary_rush;
    const bool has_storm = has_keyword(attacker.keywords, Keyword::Storm);
    if (target.kind == Target::Kind::Unit) {
        return has_rush || has_storm;
    }
    if (!has_storm) {
        return false;
    }
    if (attacker.advanced_summoned_this_turn &&
        !catalog_.at(attacker.definition_id).can_attack_leader_on_advanced_turn) {
        return false;
    }
    return true;
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
    if (target.player != opponent(player_id)) {
        return Status::error(ErrorCode::InvalidTarget, "attack target must belong to the opponent");
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

void Game::resolve_pending_attack() {
    if (!pending_reaction_.has_value() || !pending_reaction_->attack.has_value()) {
        return;
    }
    const PendingAttack attack = *pending_reaction_->attack;
    close_reaction_window();

    if (!instances_.contains(attack.attacker) || instances_.at(attack.attacker).zone != Zone::Unit) {
        return;
    }
    if (attack.target.kind == Target::Kind::Unit) {
        if (!instances_.contains(attack.target.unit) || instances_.at(attack.target.unit).zone != Zone::Unit) {
            return;
        }
        resolve_unit_combat(attack.attacker, attack.target.unit);
    } else {
        resolve_leader_attack(attack.attacker, attack.target.player);
    }
    evaluate_result();
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

void Game::open_reaction_window(
    const ReactionWindow window,
    const PlayerId responder,
    const InstanceId subject,
    const std::optional<PendingAttack> attack) {
    PendingReaction reaction;
    reaction.window = window;
    reaction.responder = responder;
    reaction.subject = subject;
    reaction.eligible_traps = matching_traps(responder, window);
    reaction.attack = attack;
    pending_reaction_ = reaction;

    if (reaction.eligible_traps.empty()) {
        if (attack.has_value()) {
            resolve_pending_attack();
        } else {
            pending_reaction_.reset();
        }
        return;
    }

    phase_ = Phase::Reaction;
    emit(
        EventType::TrapWindowOpened,
        responder,
        subject,
        static_cast<int>(window),
        static_cast<int>(reaction.eligible_traps.size()));
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
    if (trap.trap_ability == Ability::TrapCancelAttack) {
        return window == ReactionWindow::BeforeAttackDamage;
    }
    if (trap.trap_ability == Ability::TrapDamageSummonedUnitTwo) {
        return window == ReactionWindow::AfterEnemyUnitSummoned;
    }
    return false;
}

void Game::close_reaction_window() {
    pending_reaction_.reset();
    if (result_ == GameResult::Ongoing) {
        phase_ = Phase::Action;
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
        case Zone::SummonDeck:
            erase_from(state.summon_deck);
            normalize_sequences(card.controller, Zone::SummonDeck);
            break;
        case Zone::None:
            break;
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
    const bool advanced_summoned) {
    move_from_current_zone(card_id);
    PlayerState& state = players_[to_index(player_id)];
    CardInstance& card = instances_.at(card_id);
    const CardDefinition& card_definition = catalog_.at(card.definition_id);
    card.controller = player_id;
    card.zone = Zone::Unit;
    card.sequence = slot;
    card.current_attack = card_definition.attack;
    card.current_health = card_definition.health;
    card.maximum_health = card_definition.health;
    card.keywords = card_definition.keywords;
    card.inherited_imprint = Imprint::None;
    card.evolved = false;
    card.attacked_this_turn = false;
    card.entered_this_turn = true;
    card.advanced_summoned_this_turn = advanced_summoned;
    card.temporary_rush = false;
    card.face_down = false;
    card.countdown = 0;
    state.units[slot] = card_id;
    emit(EventType::CardMoved, player_id, card_id, static_cast<int>(Zone::Unit), static_cast<int>(slot));
}

void Game::put_in_tactic_slot(const PlayerId player_id, const InstanceId card_id, const std::size_t slot) {
    move_from_current_zone(card_id);
    PlayerState& state = players_[to_index(player_id)];
    CardInstance& card = instances_.at(card_id);
    const CardDefinition& card_definition = catalog_.at(card.definition_id);
    card.controller = player_id;
    card.zone = Zone::Tactic;
    card.sequence = slot;
    card.face_down = card_definition.kind == CardKind::Trap;
    card.countdown = card_definition.countdown;
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
        case Zone::SummonDeck:
            values = &state.summon_deck;
            break;
        default:
            return;
    }
    for (std::size_t index = 0; index < values->size(); ++index) {
        CardInstance& card = instances_.at((*values)[index]);
        card.sequence = index;
        card.zone = zone;
    }
}

bool Game::vector_contains(const std::vector<InstanceId>& values, const InstanceId id) const {
    return std::find(values.begin(), values.end(), id) != values.end();
}

bool Game::is_controlled_unit(const PlayerId player_id, const InstanceId id) const {
    if (!instances_.contains(id)) {
        return false;
    }
    const CardInstance& unit = instances_.at(id);
    return unit.controller == player_id && unit.zone == Zone::Unit;
}

bool Game::is_enemy_unit(const PlayerId player_id, const InstanceId id) const {
    return is_controlled_unit(opponent(player_id), id);
}

bool Game::is_valid_target_for_ability(
    const PlayerId actor,
    const Target& target,
    const bool require_enemy_unit) const {
    if (target.kind != Target::Kind::Unit || !instances_.contains(target.unit)) {
        return false;
    }
    const CardInstance& unit = instances_.at(target.unit);
    if (unit.zone != Zone::Unit) {
        return false;
    }
    if (require_enemy_unit) {
        return unit.controller == opponent(actor) && !has_keyword(unit.keywords, Keyword::Ambush);
    }
    return unit.controller == actor;
}

void Game::evaluate_result() {
    if (result_ != GameResult::Ongoing) {
        return;
    }
    const bool player0_dead = players_[0].leader_health <= 0;
    const bool player1_dead = players_[1].leader_health <= 0;
    if (!player0_dead && !player1_dead) {
        return;
    }
    if (player0_dead && player1_dead) {
        result_ = GameResult::Draw;
    } else if (player0_dead) {
        result_ = GameResult::Player1Won;
    } else {
        result_ = GameResult::Player0Won;
    }
    phase_ = Phase::Finished;
    pending_reaction_.reset();
    emit(EventType::MatchEnded, active_player_, 0, static_cast<int>(result_));
}

void Game::emit(
    const EventType type,
    const PlayerId player_id,
    const InstanceId card,
    const int value,
    const int secondary_value,
    std::string text) {
    events_.push_back(GameEvent{type, player_id, card, value, secondary_value, std::move(text)});
}

} // namespace scgs
