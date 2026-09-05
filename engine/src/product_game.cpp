// SPDX-License-Identifier: GPL-3.0-or-later
#include "scgs/product_game.hpp"

#include <algorithm>
#include <functional>
#include <limits>
#include <random>
#include <stdexcept>
#include <utility>

namespace scgs::v2 {
namespace {

[[nodiscard]] bool is_main_play(const ActionKind action) noexcept {
    return action == ActionKind::PlayFollower || action == ActionKind::CastSpell ||
        action == ActionKind::PlayTrap || action == ActionKind::PlayAmulet ||
        action == ActionKind::PlayField || action == ActionKind::Attack ||
        action == ActionKind::Evolve || action == ActionKind::Deploy ||
        action == ActionKind::EndTurn;
}

[[nodiscard]] bool has_kind(const std::vector<CardKind>& kinds, const CardKind kind) {
    return kinds.empty() || std::find(kinds.begin(), kinds.end(), kind) != kinds.end();
}

[[nodiscard]] bool card_filter_matches(
    const CardDefinition& definition,
    const CardSelectorSpec& filter) {
    if (!has_kind(filter.allowed_kinds, definition.kind) ||
        std::find(filter.excluded_kinds.begin(), filter.excluded_kinds.end(), definition.kind) !=
            filter.excluded_kinds.end()) {
        return false;
    }
    if (!filter.profession_id.empty() && definition.identity.profession_id != filter.profession_id) {
        return false;
    }
    if (!filter.series_id.empty() && definition.identity.series_id != filter.series_id) {
        return false;
    }
    return !filter.neutral.has_value() || definition.identity.neutral == *filter.neutral;
}

[[nodiscard]] bool is_direct_play_effect(const EffectSpec& effect) noexcept {
    return effect.trigger == EffectTrigger::OnPlay || effect.trigger == EffectTrigger::OnEntry;
}

struct EffectActionTarget final {
    TargetSpec target = TargetSpec::None;
    PermanentSelectorSpec filter;
    bool optional = false;
};

[[nodiscard]] bool trigger_matches(
    const EffectTrigger trigger,
    const std::span<const EffectTrigger> allowed) noexcept {
    return std::find(allowed.begin(), allowed.end(), trigger) != allowed.end();
}

[[nodiscard]] bool mode_applies_to_triggers(
    const ModeSpec& mode,
    const std::span<const EffectTrigger> triggers) {
    return std::any_of(mode.effects.begin(), mode.effects.end(), [&](const EffectSpec& effect) {
        return trigger_matches(effect.trigger, triggers);
    });
}

[[nodiscard]] std::vector<std::string> modes_for_triggers(
    const CardDefinition& definition,
    const std::span<const EffectTrigger> triggers) {
    std::vector<std::string> result;
    for (const ModeSpec& mode : definition.modes) {
        if (mode_applies_to_triggers(mode, triggers)) {
            result.push_back(mode.mode_id);
        }
    }
    if (result.empty()) {
        result.emplace_back();
    }
    return result;
}

[[nodiscard]] ProductGameStatus validate_mode_for_triggers(
    const CardDefinition& definition,
    const std::string_view mode_id,
    const std::span<const EffectTrigger> triggers) {
    const std::vector<std::string> modes = modes_for_triggers(definition, triggers);
    const bool has_modes = !(modes.size() == 1U && modes.front().empty());
    if (!has_modes) {
        return mode_id.empty()
            ? ProductGameStatus::ok()
            : ProductGameStatus::error(
                ProductGameError::InvalidMode,
                "this action does not accept a mode");
    }
    if (mode_id.empty() || std::find(modes.begin(), modes.end(), mode_id) == modes.end()) {
        return ProductGameStatus::error(
            ProductGameError::InvalidMode,
            "the selected mode is not valid for this action timing");
    }
    return ProductGameStatus::ok();
}

[[nodiscard]] EffectActionTarget target_for_triggers(
    const CardDefinition& definition,
    const std::string_view mode_id,
    const std::span<const EffectTrigger> triggers) {
    const ModeSpec* selected_mode = nullptr;
    if (!mode_id.empty()) {
        const auto found = std::find_if(
            definition.modes.begin(), definition.modes.end(), [&](const ModeSpec& mode) {
                return mode.mode_id == mode_id && mode_applies_to_triggers(mode, triggers);
            });
        if (found != definition.modes.end()) {
            selected_mode = std::addressof(*found);
            if (found->target != TargetSpec::None) {
                return EffectActionTarget{found->target, found->target_filter, false};
            }
        }
    }

    const auto inspect = [&](const bool optional) -> std::optional<EffectActionTarget> {
        const auto scan = [&](const std::vector<EffectSpec>& effects)
            -> std::optional<EffectActionTarget> {
            const auto found = std::find_if(effects.begin(), effects.end(), [&](const EffectSpec& effect) {
                return trigger_matches(effect.trigger, triggers) && effect.optional == optional &&
                    effect.target != TargetSpec::None && effect.target != TargetSpec::Self &&
                    effect.target_from_effect_id.empty();
            });
            if (found == effects.end()) {
                return std::nullopt;
            }
            return EffectActionTarget{found->target, found->target_filter, optional};
        };
        if (const auto base = scan(definition.effects)) {
            return base;
        }
        return selected_mode == nullptr ? std::nullopt : scan(selected_mode->effects);
    };

    if (const auto required = inspect(false)) {
        return *required;
    }
    if (const auto optional = inspect(true)) {
        return *optional;
    }
    return {};
}

} // namespace

ProductGame::ProductGame(CardCatalog catalog, ProductGameConfig config)
    : catalog_(std::move(catalog)), board_(catalog_), config_(std::move(config)) {}

ProductGameStatus ProductGame::validate_configuration() const {
    for (std::size_t index = 0; index < kPlayerCount; ++index) {
        const PlayerId player = static_cast<PlayerId>(index);
        const auto& main = config_.main_decks[index];
        const auto& standby = config_.standby_decks[index];
        if (main.size() != config_.required_main_deck_size ||
            standby.size() != config_.required_standby_size ||
            config_.starting_hand_size > main.size() || config_.starting_hand_size > 9U) {
            return ProductGameStatus::error(
                ProductGameError::InvalidConfiguration,
                "product deck sizes do not match the configured format");
        }
        if (config_.professions[index].empty()) {
            return ProductGameStatus::error(
                ProductGameError::InvalidConfiguration,
                "each product player requires a profession");
        }
        std::unordered_map<DesignId, std::size_t> copies;
        for (const DesignId& design_id : main) {
            if (!catalog_.contains(design_id)) {
                return ProductGameStatus::error(ProductGameError::InvalidConfiguration, "unknown main-deck card");
            }
            const CardDefinition& definition = catalog_.at(design_id);
            if (definition.availability != CardAvailability::MainDeck || !definition.is_executable() ||
                !definition.identity.is_constructible_for(config_.professions[index])) {
                return ProductGameStatus::error(
                    ProductGameError::InvalidConfiguration,
                    "main-deck card is locked, unavailable, or belongs to another profession");
            }
            if (++copies[design_id] > 3U) {
                return ProductGameStatus::error(
                    ProductGameError::InvalidConfiguration,
                    "main-deck card exceeds the three-copy product limit");
            }
        }
        std::unordered_set<DesignId> unique_standby;
        for (const DesignId& design_id : standby) {
            if (!catalog_.contains(design_id)) {
                return ProductGameStatus::error(ProductGameError::InvalidConfiguration, "unknown standby card");
            }
            const CardDefinition& definition = catalog_.at(design_id);
            if (definition.availability != CardAvailability::Standby || !definition.is_executable() ||
                !definition.identity.is_constructible_for(config_.professions[index]) ||
                !unique_standby.insert(design_id).second) {
                return ProductGameStatus::error(
                    ProductGameError::InvalidConfiguration,
                    "standby card is locked, unavailable, or belongs to another profession");
            }
        }
        (void)player;
    }
    return ProductGameStatus::ok();
}

ProductGameStatus ProductGame::start() {
    if (phase_ != ProductGamePhase::NotStarted) {
        return ProductGameStatus::error(ProductGameError::AlreadyStarted, "product match already started");
    }
    const ProductGameStatus configuration = validate_configuration();
    if (!configuration) {
        return configuration;
    }

    effective_seed_ = config_.seed;
    if (effective_seed_ == 0U) {
        std::random_device entropy;
        effective_seed_ = (static_cast<std::uint64_t>(entropy()) << 32U) ^ entropy();
    }
    if (config_.first_player_mode == FirstPlayerMode::Player0) {
        first_player_ = PlayerId::Player0;
    } else if (config_.first_player_mode == FirstPlayerMode::Player1) {
        first_player_ = PlayerId::Player1;
    } else {
        std::mt19937_64 generator(effective_seed_);
        first_player_ = (generator() & 1U) == 0U ? PlayerId::Player0 : PlayerId::Player1;
    }
    active_player_ = first_player_;

    for (std::size_t index = 0; index < kPlayerCount; ++index) {
        const PlayerId player = static_cast<PlayerId>(index);
        for (const DesignId& design_id : config_.main_decks[index]) {
            (void)board_.create_instance(design_id, player, Zone::Deck);
        }
        for (const DesignId& design_id : config_.standby_decks[index]) {
            (void)board_.create_instance(design_id, player, Zone::Standby);
        }
        if (config_.shuffle) {
            const std::vector<InstanceId> deck = board_.player(player).deck;
            const Status shuffled = board_.put_deck_cards_on_bottom(
                player, deck, true, effective_seed_ ^ (0x9E3779B97F4A7C15ULL * (index + 1U)));
            if (!shuffled) {
                throw std::logic_error(shuffled.message);
            }
        }
        rules_.configure_evolution_charge(player, config_.evolution_charge_policies[index]);
        for (std::size_t draw = 0; draw < config_.starting_hand_size; ++draw) {
            const DrawResult result = board_.draw_one(player);
            if (result.card.has_value()) {
                emit(result.entered_hand ? ProductEventKind::CardDrawn : ProductEventKind::CardArchived,
                    player, result.card);
            }
        }
    }
    phase_ = ProductGamePhase::Mulligan;
    emit(ProductEventKind::MatchStarted, first_player_, std::nullopt, std::nullopt, 0, 0, "match started");
    revision_ = 1;
    return ProductGameStatus::ok();
}

ProductGameStatus ProductGame::validate_common(const ProductGameCommand& command) const {
    if (!is_valid_player(command.player)) {
        return ProductGameStatus::error(ProductGameError::InvalidPlayer, "invalid product player");
    }
    if (phase_ == ProductGamePhase::NotStarted) {
        return ProductGameStatus::error(ProductGameError::NotStarted, "product match has not started");
    }
    if (phase_ == ProductGamePhase::Finished) {
        return ProductGameStatus::error(ProductGameError::MatchFinished, "product match is finished");
    }
    if (command.expected_revision != revision_) {
        return ProductGameStatus::error(ProductGameError::StaleRevision, "product command revision is stale");
    }
    if (phase_ == ProductGamePhase::Choice &&
        command.action != ActionKind::ResolveChoice && command.action != ActionKind::Surrender) {
        return ProductGameStatus::error(ProductGameError::ChoicePending, "a paid resolution choice is pending");
    }
    return ProductGameStatus::ok();
}

ProductGameStatus ProductGame::validate_card_in_zone(
    const PlayerId player,
    const InstanceId card,
    const Zone zone) const {
    if (!board_.contains_instance(card)) {
        return ProductGameStatus::error(ProductGameError::InvalidCard, "unknown product card instance");
    }
    const CardInstance& instance = board_.instance(card);
    if (instance.controller != player) {
        return ProductGameStatus::error(ProductGameError::InvalidCard, "card is not controlled by player");
    }
    if (instance.zone != zone) {
        return ProductGameStatus::error(ProductGameError::InvalidZone, "card is in the wrong zone");
    }
    return ProductGameStatus::ok();
}

ProductPaymentPreview ProductGame::project_payment(
    const PlayerId player,
    const int base_cost,
    const int burn_cost,
    const bool allow_advance,
    const bool use_advance,
    const int evolution_energy_cost,
    ProductGameStatus& status) const {
    const ProductPlayerResources& state = resources_[to_index(player)];
    ProductPaymentPreview preview;
    preview.base_cost = base_cost;
    preview.burn_cost = burn_cost;
    preview.current_pp_after = state.current_pp;
    preview.pp_capacity_after = state.pp_capacity;
    preview.cracks_after = state.cracks;
    preview.evolution_energy_after = state.evolution_energy;
    if (state.evolution_energy < evolution_energy_cost) {
        status = ProductGameStatus::error(ProductGameError::EvolutionUnavailable, "not enough evolution energy");
        return preview;
    }
    const int advance_needed = std::max(0, base_cost - state.current_pp);
    if (advance_needed > 0 && (!allow_advance || !use_advance)) {
        status = ProductGameStatus::error(ProductGameError::InsufficientPP, "not enough current PP");
        return preview;
    }
    const bool advanced = allow_advance && use_advance && advance_needed > 0;
    const int capacity_loss = (advanced ? advance_needed : 0) + std::max(0, burn_cost);
    if (capacity_loss > 0 && state.future_used_this_turn) {
        status = ProductGameStatus::error(ProductGameError::FutureAlreadyUsed, "future was already used this turn");
        return preview;
    }
    if (state.pp_capacity - capacity_loss < 0) {
        status = ProductGameStatus::error(ProductGameError::AdvanceUnavailable, "payment would reduce PP capacity below zero");
        return preview;
    }
    preview.current_pp_after = std::max(0, state.current_pp - base_cost);
    preview.pp_capacity_after = state.pp_capacity - capacity_loss;
    // Burn removes future capacity, not already available current PP. Current
    // PP can legitimately exceed capacity until the next owner-turn refill.
    preview.cracks_after = state.cracks + capacity_loss;
    preview.evolution_energy_after = state.evolution_energy - evolution_energy_cost;
    preview.advance_cost = advanced ? advance_needed : 0;
    preview.advanced = advanced;
    preview.future_used = capacity_loss > 0;
    status = ProductGameStatus::ok();
    return preview;
}

TargetSpec ProductGame::required_target(
    const CardDefinition& definition,
    const std::string_view mode) const {
    if (!mode.empty()) {
        const auto found = std::find_if(definition.modes.begin(), definition.modes.end(), [&](const ModeSpec& item) {
            return item.mode_id == mode;
        });
        if (found != definition.modes.end() && found->target != TargetSpec::None) {
            return found->target;
        }
    }
    for (const EffectSpec& effect : definition.effects) {
        if (is_direct_play_effect(effect) && effect.target != TargetSpec::None &&
            effect.target != TargetSpec::Self && !effect.optional) {
            return effect.target;
        }
    }
    return TargetSpec::None;
}

PermanentSelectorSpec ProductGame::target_filter(
    const CardDefinition& definition,
    const std::string_view mode) const {
    if (!mode.empty()) {
        const auto found = std::find_if(definition.modes.begin(), definition.modes.end(), [&](const ModeSpec& item) {
            return item.mode_id == mode;
        });
        if (found != definition.modes.end()) {
            return found->target_filter;
        }
    }
    for (const EffectSpec& effect : definition.effects) {
        if (is_direct_play_effect(effect) && effect.target != TargetSpec::None &&
            effect.target != TargetSpec::Self) {
            return effect.target_filter;
        }
    }
    return {};
}

ProductGameStatus ProductGame::validate_target(
    const PlayerId actor,
    const InstanceId source,
    const std::optional<InstanceId> target,
    const TargetSpec target_spec,
    const PermanentSelectorSpec& selector) const {
    if (target_spec == TargetSpec::None || target_spec == TargetSpec::Self) {
        if (target.has_value() && target_spec == TargetSpec::None) {
            return ProductGameStatus::error(ProductGameError::InvalidTarget, "action does not accept a target");
        }
        return ProductGameStatus::ok();
    }
    if (!target.has_value() || !board_.contains_instance(*target)) {
        return ProductGameStatus::error(ProductGameError::InvalidTarget, "action requires a valid target");
    }
    const CardInstance& instance = board_.instance(*target);
    const bool friendly = target_spec == TargetSpec::FriendlyFollower ||
        target_spec == TargetSpec::FriendlyPermanent;
    if ((instance.controller == actor) != friendly) {
        return ProductGameStatus::error(ProductGameError::InvalidTarget, "target controller relation is invalid");
    }
    const CardDefinition& target_definition = catalog_.at(instance.design_id);
    const bool follower_only = target_spec == TargetSpec::FriendlyFollower ||
        target_spec == TargetSpec::EnemyFollower;
    if (follower_only && (instance.zone != Zone::MainBoard || target_definition.kind != CardKind::Follower)) {
        return ProductGameStatus::error(ProductGameError::InvalidTarget, "target must be a battlefield follower");
    }
    // A follower target is a restricted permanent, not an exemption from the
    // selector's profession/series/kind/zone constraints.
    const PermanentFilter filter = PermanentFilter::from_spec(selector);
    const Status result = board_.validate_permanent_target(actor, *target, friendly, filter);
    if (!result) {
        return ProductGameStatus::error(ProductGameError::InvalidTarget, result.message);
    }
    if (selector.exclude_source && *target == source) {
        return ProductGameStatus::error(ProductGameError::InvalidTarget, "source is excluded from this target selector");
    }
    return ProductGameStatus::ok();
}

ProductGameStatus ProductGame::validate_main_action(const ProductGameCommand& command) const {
    if (!is_main_play(command.action) || phase_ != ProductGamePhase::Main) {
        return ProductGameStatus::error(ProductGameError::WrongPhase, "command is not legal in the main phase");
    }
    if (command.player != active_player_) {
        return ProductGameStatus::error(ProductGameError::NotActivePlayer, "only the active player may act");
    }
    return ProductGameStatus::ok();
}

ProductActionPlan ProductGame::plan_command(const ProductGameCommand& command) const {
    ProductActionPlan plan;
    plan.command = command;
    plan.status = validate_common(command);
    if (!plan.status) {
        return plan;
    }
    if (command.action == ActionKind::Surrender) {
        plan.operation = ProductPlanOperation::Surrender;
        return plan;
    }
    if (phase_ == ProductGamePhase::Mulligan) {
        if (command.action != ActionKind::Mulligan) {
            plan.status = ProductGameStatus::error(ProductGameError::WrongPhase, "only an unfinished mulligan is legal");
            return plan;
        }
        if (mulligan_done_[to_index(command.player)]) {
            plan.status = ProductGameStatus::error(
                ProductGameError::MulliganAlreadyDone,
                "this player already completed the mulligan");
            return plan;
        }
        std::unordered_set<InstanceId> selected;
        for (const InstanceId card : command.selected_cards) {
            const ProductGameStatus card_status = validate_card_in_zone(command.player, card, Zone::Hand);
            if (!card_status || !selected.insert(card).second) {
                plan.status = ProductGameStatus::error(ProductGameError::InvalidSelection, "invalid mulligan card selection");
                return plan;
            }
        }
        if (board_.player(command.player).deck.size() < command.selected_cards.size()) {
            plan.status = ProductGameStatus::error(
                ProductGameError::InvalidSelection,
                "not enough cards remain for mulligan replacements");
            return plan;
        }
        plan.operation = ProductPlanOperation::Mulligan;
        return plan;
    }
    if (phase_ == ProductGamePhase::Choice) {
        if (command.action != ActionKind::ResolveChoice || !resolution_.pending_choice().has_value()) {
            plan.status = ProductGameStatus::error(ProductGameError::NoPendingChoice, "no matching choice is pending");
            return plan;
        }
        const PendingChoice& choice = *resolution_.pending_choice();
        if (choice.chooser != command.player) {
            plan.status = ProductGameStatus::error(
                ProductGameError::ChoiceNotOwned,
                "choice is owned by the other player");
            return plan;
        }
        if (choice.choice_id != command.choice_id) {
            plan.status = ProductGameStatus::error(
                ProductGameError::InvalidSelection,
                "choice token is invalid or expired");
            return plan;
        }
        std::unordered_set<std::string> available;
        for (const ChoiceOption& option : choice.options) {
            available.insert(option.option_id);
        }
        std::unordered_set<std::string> selected;
        if (command.selected_option_ids.size() < choice.minimum ||
            command.selected_option_ids.size() > choice.maximum) {
            plan.status = ProductGameStatus::error(ProductGameError::InvalidSelection, "wrong choice selection count");
            return plan;
        }
        for (const std::string& id : command.selected_option_ids) {
            if (!available.contains(id) || !selected.insert(id).second) {
                plan.status = ProductGameStatus::error(ProductGameError::InvalidSelection, "choice option is invalid or duplicated");
                return plan;
            }
        }
        plan.operation = ProductPlanOperation::ResolveChoice;
        return plan;
    }
    if (phase_ == ProductGamePhase::Reaction) {
        if (!suspended_origin_.has_value() || command.player != suspended_origin_->priority) {
            plan.status = ProductGameStatus::error(ProductGameError::ReactionUnavailable, "player does not hold reaction priority");
            return plan;
        }
        if (command.action == ActionKind::PassReaction) {
            plan.operation = ProductPlanOperation::PassReaction;
            return plan;
        }
        if (command.action != ActionKind::ActivateTrap || !command.source.has_value()) {
            plan.status = ProductGameStatus::error(ProductGameError::WrongPhase, "only a trap or pass is legal in reaction");
            return plan;
        }
        const ProductGameStatus card = validate_card_in_zone(command.player, *command.source, Zone::Tactic);
        const std::vector<InstanceId> eligible_traps = available_traps(command.player);
        if (!card || catalog_.at(board_.instance(*command.source).design_id).kind != CardKind::Trap ||
            suspended_origin_->declared_traps.contains(*command.source) ||
            std::find(eligible_traps.begin(), eligible_traps.end(), *command.source) ==
                eligible_traps.end()) {
            plan.status = ProductGameStatus::error(ProductGameError::ReactionUnavailable, "trap cannot be activated now");
            return plan;
        }
        const CardDefinition& definition = catalog_.at(board_.instance(*command.source).design_id);
        const std::array<EffectTrigger, 1U> triggers{
            suspended_origin_->plan.command.action == ActionKind::Attack
                ? EffectTrigger::OnAttackDeclared
                : EffectTrigger::OnEntry};
        plan.status = validate_mode_for_triggers(definition, command.mode_id, triggers);
        if (!plan.status) {
            return plan;
        }
        const PlayerId origin_player = suspended_origin_->plan.command.player;
        const auto effect_is_eligible = [&](const EffectSpec& effect) {
            const bool relation = effect.trigger_player_relation == TriggerPlayerRelation::Any ||
                (effect.trigger_player_relation == TriggerPlayerRelation::SourceController &&
                    command.player == origin_player) ||
                (effect.trigger_player_relation == TriggerPlayerRelation::OpponentOfSourceController &&
                    command.player != origin_player);
            return effect.trigger == triggers.front() && relation;
        };
        bool trigger_is_eligible = std::any_of(
            definition.effects.begin(), definition.effects.end(), effect_is_eligible);
        if (!command.mode_id.empty()) {
            const auto selected_mode = std::find_if(
                definition.modes.begin(), definition.modes.end(), [&](const ModeSpec& mode) {
                    return mode.mode_id == command.mode_id;
                });
            trigger_is_eligible = trigger_is_eligible ||
                (selected_mode != definition.modes.end() && std::any_of(
                    selected_mode->effects.begin(), selected_mode->effects.end(), effect_is_eligible));
        }
        if (!trigger_is_eligible) {
            plan.status = ProductGameStatus::error(
                ProductGameError::ReactionUnavailable,
                "trap does not respond to this origin player or timing");
            return plan;
        }
        const EffectActionTarget action_target = target_for_triggers(definition, command.mode_id, triggers);
        if (!(action_target.optional && !command.target.has_value())) {
            plan.status = validate_target(
                command.player,
                *command.source,
                command.target,
                action_target.target,
                action_target.filter);
            if (!plan.status) {
                return plan;
            }
        }
        plan.operation = ProductPlanOperation::ActivateTrap;
        plan.source_kind = CardKind::Trap;
        return plan;
    }

    plan.status = validate_main_action(command);
    if (!plan.status) {
        return plan;
    }
    if (command.action == ActionKind::EndTurn) {
        plan.operation = ProductPlanOperation::EndTurn;
        return plan;
    }
    if (!command.source.has_value()) {
        plan.status = ProductGameStatus::error(ProductGameError::InvalidCard, "action requires a source card");
        return plan;
    }

    if (command.action == ActionKind::Attack) {
        const Status attack = board_.validate_attack(command.player, *command.source, command.target);
        if (!attack) {
            plan.status = ProductGameStatus::error(ProductGameError::InvalidTarget, attack.message);
            return plan;
        }
        plan.operation = command.target.has_value() ? ProductPlanOperation::AttackFollower :
            ProductPlanOperation::AttackLeader;
        plan.opens_response = true;
        return plan;
    }
    if (command.action == ActionKind::Evolve) {
        const Status evolve = board_.validate_evolve(command.player, *command.source);
        if (!evolve || board_.instance(*command.source).evolved || resources_[to_index(command.player)].evolved_this_turn ||
            !resources_[to_index(command.player)].evolution_unlocked) {
            plan.status = ProductGameStatus::error(ProductGameError::EvolutionUnavailable, "follower cannot evolve now");
            return plan;
        }
        const CardDefinition& definition = catalog_.at(board_.instance(*command.source).design_id);
        static constexpr std::array<EffectTrigger, 1U> kEvolveTriggers{EffectTrigger::OnEvolve};
        plan.status = validate_mode_for_triggers(definition, command.mode_id, kEvolveTriggers);
        if (!plan.status) {
            return plan;
        }
        const EffectActionTarget action_target = target_for_triggers(
            definition,
            command.mode_id,
            kEvolveTriggers);
        if (!(action_target.optional && !command.target.has_value())) {
            plan.status = validate_target(
                command.player,
                *command.source,
                command.target,
                action_target.target,
                action_target.filter);
            if (!plan.status) {
                return plan;
            }
        }
        ProductGameStatus payment;
        plan.payment = project_payment(command.player, 0, 0, false, false, 2, payment);
        plan.status = payment;
        plan.operation = ProductPlanOperation::Evolve;
        return plan;
    }

    const Zone expected_zone = command.action == ActionKind::Deploy ? Zone::Standby : Zone::Hand;
    plan.status = validate_card_in_zone(command.player, *command.source, expected_zone);
    if (!plan.status) {
        return plan;
    }
    const CardDefinition& definition = catalog_.at(board_.instance(*command.source).design_id);
    plan.source_kind = definition.kind;
    if (!definition.is_executable()) {
        plan.status = ProductGameStatus::error(ProductGameError::InvalidCard, "card has no executable product program");
        return plan;
    }
    const auto validate_slot = [&](const std::size_t limit, const auto& slots, const ProductGameError full) {
        if (!command.slot.has_value() || *command.slot >= limit) {
            return ProductGameStatus::error(ProductGameError::InvalidSlot, "action requires an in-range slot");
        }
        if (slots[*command.slot].has_value()) {
            const bool vacated_by_additional_cost = command.action == ActionKind::Deploy &&
                std::find(
                    command.additional_cost_cards.begin(),
                    command.additional_cost_cards.end(),
                    *slots[*command.slot]) != command.additional_cost_cards.end();
            if (vacated_by_additional_cost) {
                return ProductGameStatus::ok();
            }
            return ProductGameStatus::error(full, "selected slot is occupied");
        }
        return ProductGameStatus::ok();
    };

    int base_cost = definition.cost;
    int burn_cost = definition.burn_pp_capacity;
    bool allow_advance = definition.can_advance;
    if (command.action == ActionKind::Deploy) {
        if (resources_[to_index(command.player)].deploy_used_this_turn) {
            plan.status = ProductGameStatus::error(ProductGameError::DeploymentUnavailable,
                "only one standby deployment is allowed per owner turn");
            return plan;
        }
        if (definition.availability != CardAvailability::Standby || definition.kind != CardKind::Follower ||
            !definition.standby.has_value()) {
            plan.status = ProductGameStatus::error(ProductGameError::DeploymentUnavailable, "source is not a deployable standby follower");
            return plan;
        }
        const ConditionEvaluationContext context = rules_.make_condition_context(command.player, board_);
        const Status condition = board_.validate_standby(definition.identity.design_id, context);
        if (!condition) {
            plan.status = ProductGameStatus::error(ProductGameError::DeploymentUnavailable, condition.message);
            return plan;
        }
        const StandbySpec& standby = *definition.standby;
        if (command.additional_cost_cards.size() < standby.additional_cost_minimum ||
            command.additional_cost_cards.size() > standby.additional_cost_maximum) {
            plan.status = ProductGameStatus::error(ProductGameError::InvalidSelection, "deployment additional cost count is invalid");
            return plan;
        }
        std::unordered_set<InstanceId> unique;
        for (const InstanceId cost : command.additional_cost_cards) {
            if (!unique.insert(cost).second || !board_.validate_permanent_target(
                    command.player, cost, true, PermanentFilter::from_spec(standby.additional_cost_filter))) {
                plan.status = ProductGameStatus::error(ProductGameError::InvalidSelection, "deployment additional cost is invalid");
                return plan;
            }
        }
        base_cost = standby.pp_cost;
        burn_cost = 0;
        allow_advance = false;
        plan.operation = ProductPlanOperation::Deploy;
        plan.status = validate_slot(kMainBoardSize, board_.player(command.player).main_board, ProductGameError::MainBoardFull);
    } else if (command.action == ActionKind::PlayFollower && definition.kind == CardKind::Follower) {
        plan.operation = ProductPlanOperation::PlayMainPermanent;
        plan.status = validate_slot(kMainBoardSize, board_.player(command.player).main_board, ProductGameError::MainBoardFull);
    } else if (command.action == ActionKind::PlayAmulet && definition.kind == CardKind::Amulet) {
        plan.operation = ProductPlanOperation::PlayMainPermanent;
        plan.status = validate_slot(kMainBoardSize, board_.player(command.player).main_board, ProductGameError::MainBoardFull);
    } else if (command.action == ActionKind::CastSpell && definition.kind == CardKind::Spell) {
        plan.operation = ProductPlanOperation::CastSpell;
        plan.status = validate_slot(kStrategyZoneSize, board_.player(command.player).tactics, ProductGameError::TacticZoneFull);
    } else if (command.action == ActionKind::PlayTrap && definition.kind == CardKind::Trap) {
        plan.operation = ProductPlanOperation::SetTrap;
        plan.status = validate_slot(kStrategyZoneSize, board_.player(command.player).tactics, ProductGameError::TacticZoneFull);
    } else if (command.action == ActionKind::PlayField && definition.kind == CardKind::Field) {
        plan.operation = ProductPlanOperation::PlayField;
        if (command.slot.has_value()) {
            plan.status = ProductGameStatus::error(ProductGameError::InvalidSlot, "field cards use the independent field zone");
        }
    } else {
        plan.status = ProductGameStatus::error(ProductGameError::InvalidCardKind, "action kind does not match source card kind");
    }
    if (!plan.status) {
        return plan;
    }
    const Status mode = board_.validate_mode(definition.identity.design_id,
        command.mode_id.empty() ? std::nullopt : std::optional<std::string_view>(command.mode_id));
    if (!mode) {
        plan.status = ProductGameStatus::error(ProductGameError::InvalidMode, mode.message);
        return plan;
    }
    TargetSpec target_spec = required_target(definition, command.mode_id);
    PermanentSelectorSpec selector = target_filter(definition, command.mode_id);
    if (target_spec == TargetSpec::None && command.target.has_value()) {
        const auto find_optional = [&](const std::vector<EffectSpec>& effects) {
            return std::find_if(effects.begin(), effects.end(), [](const EffectSpec& effect) {
                return is_direct_play_effect(effect) && effect.optional &&
                    effect.target != TargetSpec::None && effect.target != TargetSpec::Self;
            });
        };
        auto optional = find_optional(definition.effects);
        if (optional != definition.effects.end()) {
            target_spec = optional->target;
            selector = optional->target_filter;
        } else if (!command.mode_id.empty()) {
            const auto mode_definition = std::find_if(definition.modes.begin(), definition.modes.end(), [&](const ModeSpec& mode) {
                return mode.mode_id == command.mode_id;
            });
            if (mode_definition != definition.modes.end()) {
                const auto mode_optional = find_optional(mode_definition->effects);
                if (mode_optional != mode_definition->effects.end()) {
                    target_spec = mode_optional->target;
                    selector = mode_optional->target_filter;
                }
            }
        }
    }
    plan.status = validate_target(command.player, *command.source, command.target,
        target_spec, selector);
    if (!plan.status) {
        return plan;
    }
    ProductGameStatus payment_status;
    plan.payment = project_payment(command.player, base_cost, burn_cost, allow_advance,
        command.use_advance, 0, payment_status);
    plan.status = payment_status;
    plan.opens_response = plan.operation != ProductPlanOperation::SetTrap;
    return plan;
}

std::vector<ProductLegalAction> ProductGame::list_legal_actions(const PlayerId player) const {
    std::vector<ProductLegalAction> result;
    if (!is_valid_player(player) || phase_ == ProductGamePhase::NotStarted ||
        phase_ == ProductGamePhase::Finished) {
        return result;
    }
    const auto add = [&](ProductGameCommand command) {
        command.player = player;
        command.expected_revision = revision_;
        const ProductActionPlan plan = plan_command(command);
        if (plan) {
            result.push_back(ProductLegalAction{plan.command, plan.payment});
        }
    };
    if (phase_ == ProductGamePhase::Mulligan) {
        if (!mulligan_done_[to_index(player)]) {
            const std::vector<InstanceId>& hand = board_.player(player).hand;
            const std::uint64_t subset_count = std::uint64_t{1} << hand.size();
            for (std::uint64_t subset = 0; subset < subset_count; ++subset) {
                ProductGameCommand mulligan;
                mulligan.action = ActionKind::Mulligan;
                for (std::size_t index = 0; index < hand.size(); ++index) {
                    if ((subset & (std::uint64_t{1} << index)) != 0U) {
                        mulligan.selected_cards.push_back(hand[index]);
                    }
                }
                add(std::move(mulligan));
            }
        }
        ProductGameCommand surrender;
        surrender.action = ActionKind::Surrender;
        add(std::move(surrender));
        return result;
    }
    if (phase_ == ProductGamePhase::Choice) {
        const PendingChoice& choice = *resolution_.pending_choice();
        if (choice.chooser == player) {
            const std::size_t maximum = std::min(choice.maximum, choice.options.size());
            std::vector<std::string> selected;
            std::vector<bool> used(choice.options.size(), false);
            const auto emit_choice = [&]() {
                ProductGameCommand command;
                command.action = ActionKind::ResolveChoice;
                command.choice_id = choice.choice_id;
                command.selected_option_ids = selected;
                add(std::move(command));
            };
            if (choice.ordered) {
                std::function<void(std::size_t)> enumerate_ordered = [&](const std::size_t length) {
                    if (selected.size() == length) {
                        emit_choice();
                        return;
                    }
                    for (std::size_t index = 0; index < choice.options.size(); ++index) {
                        if (used[index]) {
                            continue;
                        }
                        used[index] = true;
                        selected.push_back(choice.options[index].option_id);
                        enumerate_ordered(length);
                        selected.pop_back();
                        used[index] = false;
                    }
                };
                for (std::size_t length = choice.minimum; length <= maximum; ++length) {
                    enumerate_ordered(length);
                }
            } else {
                std::function<void(std::size_t, std::size_t)> enumerate_unordered =
                    [&](const std::size_t next, const std::size_t length) {
                        if (selected.size() == length) {
                            emit_choice();
                            return;
                        }
                        const std::size_t remaining = length - selected.size();
                        for (std::size_t index = next;
                             index + remaining <= choice.options.size(); ++index) {
                            selected.push_back(choice.options[index].option_id);
                            enumerate_unordered(index + 1U, length);
                            selected.pop_back();
                        }
                    };
                for (std::size_t length = choice.minimum; length <= maximum; ++length) {
                    enumerate_unordered(0U, length);
                }
            }
        }
        ProductGameCommand surrender;
        surrender.action = ActionKind::Surrender;
        add(std::move(surrender));
        return result;
    }
    if (phase_ == ProductGamePhase::Reaction) {
        if (suspended_origin_->priority == player) {
            for (const InstanceId trap : available_traps(player)) {
                const CardDefinition& definition = catalog_.at(board_.instance(trap).design_id);
                const std::array<EffectTrigger, 1U> triggers{
                    suspended_origin_->plan.command.action == ActionKind::Attack
                        ? EffectTrigger::OnAttackDeclared
                        : EffectTrigger::OnEntry};
                for (const std::string& mode : modes_for_triggers(definition, triggers)) {
                    const EffectActionTarget action_target = target_for_triggers(definition, mode, triggers);
                    std::vector<std::optional<InstanceId>> targets;
                    if (action_target.target == TargetSpec::None ||
                        action_target.target == TargetSpec::Self || action_target.optional) {
                        targets.push_back(std::nullopt);
                    }
                    if (action_target.target != TargetSpec::None && action_target.target != TargetSpec::Self) {
                        const bool friendly = action_target.target == TargetSpec::FriendlyFollower ||
                            action_target.target == TargetSpec::FriendlyPermanent;
                        const PlayerId controller = friendly ? player : opponent(player);
                        for (const InstanceId target : board_.list_permanents(controller)) {
                            targets.push_back(target);
                        }
                    }
                    for (const std::optional<InstanceId> target : targets) {
                        ProductGameCommand command;
                        command.action = ActionKind::ActivateTrap;
                        command.source = trap;
                        command.target = target;
                        command.mode_id = mode;
                        add(std::move(command));
                    }
                }
            }
            ProductGameCommand pass;
            pass.action = ActionKind::PassReaction;
            add(std::move(pass));
        }
        ProductGameCommand surrender;
        surrender.action = ActionKind::Surrender;
        add(std::move(surrender));
        return result;
    }
    if (player != active_player_) {
        ProductGameCommand surrender;
        surrender.action = ActionKind::Surrender;
        add(std::move(surrender));
        return result;
    }

    const auto target_variants = [&](const CardDefinition& definition, const std::string_view mode) {
        TargetSpec target_spec = required_target(definition, mode);
        bool optional = false;
        if (target_spec == TargetSpec::None) {
            const auto find_optional = [](const std::vector<EffectSpec>& effects) {
                return std::find_if(effects.begin(), effects.end(), [](const EffectSpec& effect) {
                    return is_direct_play_effect(effect) && effect.optional && effect.target != TargetSpec::None &&
                        effect.target != TargetSpec::Self;
                });
            };
            const auto direct = find_optional(definition.effects);
            if (direct != definition.effects.end()) {
                target_spec = direct->target;
                optional = true;
            } else if (!mode.empty()) {
                const auto selected_mode = std::find_if(
                    definition.modes.begin(), definition.modes.end(), [&](const ModeSpec& candidate) {
                        return candidate.mode_id == mode;
                    });
                if (selected_mode != definition.modes.end()) {
                    const auto mode_effect = find_optional(selected_mode->effects);
                    if (mode_effect != selected_mode->effects.end()) {
                        target_spec = mode_effect->target;
                        optional = true;
                    }
                }
            }
        }

        std::vector<std::optional<InstanceId>> targets;
        if (target_spec == TargetSpec::None || target_spec == TargetSpec::Self || optional) {
            targets.push_back(std::nullopt);
        }
        if (target_spec != TargetSpec::None && target_spec != TargetSpec::Self) {
            const bool friendly = target_spec == TargetSpec::FriendlyFollower ||
                target_spec == TargetSpec::FriendlyPermanent;
            const PlayerId controller = friendly ? player : opponent(player);
            for (const InstanceId candidate : board_.list_permanents(controller)) {
                targets.push_back(candidate);
            }
        }
        return targets;
    };

    for (const InstanceId card : board_.player(player).hand) {
        const CardDefinition& definition = catalog_.at(board_.instance(card).design_id);
        const ActionKind action = definition.kind == CardKind::Follower ? ActionKind::PlayFollower :
            definition.kind == CardKind::Spell ? ActionKind::CastSpell :
            definition.kind == CardKind::Amulet ? ActionKind::PlayAmulet :
            definition.kind == CardKind::Trap ? ActionKind::PlayTrap : ActionKind::PlayField;
        const std::size_t slot_count = (definition.kind == CardKind::Follower || definition.kind == CardKind::Amulet)
            ? kMainBoardSize : (definition.kind == CardKind::Spell || definition.kind == CardKind::Trap)
                ? kStrategyZoneSize : 1U;
        std::vector<std::string> modes;
        if (definition.modes.empty()) {
            modes.emplace_back();
        } else {
            for (const ModeSpec& mode : definition.modes) {
                modes.push_back(mode.mode_id);
            }
        }
        for (const std::string& mode : modes) {
            const std::vector<std::optional<InstanceId>> targets = target_variants(definition, mode);
            for (std::size_t slot = 0; slot < slot_count; ++slot) {
                for (const auto target : targets) {
                    std::array<bool, 2> advance_candidates{false, false};
                    std::size_t advance_candidate_count = 1U;
                    if (definition.can_advance &&
                        definition.cost > resources_[to_index(player)].current_pp) {
                        advance_candidates[advance_candidate_count++] = true;
                    }
                    for (std::size_t advance_index = 0;
                         advance_index < advance_candidate_count; ++advance_index) {
                        ProductGameCommand command;
                        command.action = action;
                        command.source = card;
                        command.target = target;
                        command.mode_id = mode;
                        command.use_advance = advance_candidates[advance_index];
                        if (definition.kind != CardKind::Field) {
                            command.slot = slot;
                        }
                        add(std::move(command));
                    }
                }
            }
        }
    }
    for (const auto& slot : board_.player(player).main_board) {
        if (!slot.has_value() || catalog_.at(board_.instance(*slot).design_id).kind != CardKind::Follower) {
            continue;
        }
        ProductGameCommand leader;
        leader.action = ActionKind::Attack;
        leader.source = *slot;
        add(leader);
        for (const auto& enemy : board_.player(opponent(player)).main_board) {
            if (enemy.has_value()) {
                ProductGameCommand attack = leader;
                attack.target = enemy;
                add(std::move(attack));
            }
        }
        const CardDefinition& definition = catalog_.at(board_.instance(*slot).design_id);
        static constexpr std::array<EffectTrigger, 1U> kEvolveTriggers{EffectTrigger::OnEvolve};
        for (const std::string& mode : modes_for_triggers(definition, kEvolveTriggers)) {
            const EffectActionTarget action_target = target_for_triggers(definition, mode, kEvolveTriggers);
            std::vector<std::optional<InstanceId>> targets;
            if (action_target.target == TargetSpec::None || action_target.target == TargetSpec::Self ||
                action_target.optional) {
                targets.push_back(std::nullopt);
            }
            if (action_target.target != TargetSpec::None && action_target.target != TargetSpec::Self) {
                const bool friendly = action_target.target == TargetSpec::FriendlyFollower ||
                    action_target.target == TargetSpec::FriendlyPermanent;
                const PlayerId controller = friendly ? player : opponent(player);
                for (const InstanceId target : board_.list_permanents(controller)) {
                    targets.push_back(target);
                }
            }
            for (const std::optional<InstanceId> target : targets) {
                ProductGameCommand evolve;
                evolve.action = ActionKind::Evolve;
                evolve.source = *slot;
                evolve.target = target;
                evolve.mode_id = mode;
                add(std::move(evolve));
            }
        }
    }
    for (const InstanceId standby : board_.player(player).standby) {
        const CardDefinition& definition = catalog_.at(board_.instance(standby).design_id);
        std::vector<std::string> modes;
        if (definition.modes.empty()) {
            modes.emplace_back();
        } else {
            for (const ModeSpec& mode : definition.modes) {
                modes.push_back(mode.mode_id);
            }
        }
        std::vector<std::vector<InstanceId>> additional_costs;
        if (definition.standby.has_value() && definition.standby->requires_additional_cost) {
            if (definition.standby->additional_cost_minimum == 0U) {
                additional_costs.emplace_back();
            }
            if (definition.standby->additional_cost_maximum >= 1U) {
                for (const InstanceId permanent : board_.list_permanents(
                         player, PermanentFilter::from_spec(definition.standby->additional_cost_filter))) {
                    additional_costs.push_back({permanent});
                }
            }
        } else {
            additional_costs.emplace_back();
        }
        for (const std::string& mode : modes) {
            const std::vector<std::optional<InstanceId>> targets = target_variants(definition, mode);
            for (std::size_t slot = 0; slot < kMainBoardSize; ++slot) {
                for (const auto target : targets) {
                    for (const std::vector<InstanceId>& cost : additional_costs) {
                        ProductGameCommand deploy;
                        deploy.action = ActionKind::Deploy;
                        deploy.source = standby;
                        deploy.slot = slot;
                        deploy.target = target;
                        deploy.mode_id = mode;
                        deploy.additional_cost_cards = cost;
                        add(std::move(deploy));
                    }
                }
            }
        }
    }
    ProductGameCommand end;
    end.action = ActionKind::EndTurn;
    add(std::move(end));
    ProductGameCommand surrender;
    surrender.action = ActionKind::Surrender;
    add(std::move(surrender));
    return result;
}

void ProductGame::emit(
    const ProductEventKind kind,
    const PlayerId player,
    const std::optional<InstanceId> source,
    const std::optional<InstanceId> target,
    const int value,
    const int secondary_value,
    std::string text) {
    events_.push_back(ProductGameEvent{
        next_event_sequence_++, revision_ + 1U, kind, player, source, target,
        value, secondary_value, std::move(text)});
}

void ProductGame::execute_mulligan(const ProductActionPlan& plan) {
    const PlayerId player = plan.command.player;
    std::vector<DrawResult> replacements;
    const Status exchanged = board_.exchange_mulligan(
        player,
        plan.command.selected_cards,
        config_.shuffle,
        effective_seed_ ^ (revision_ << 17U) ^ to_index(player),
        replacements);
    if (!exchanged) {
        throw std::logic_error(exchanged.message);
    }
    for (const DrawResult& draw : replacements) {
        if (draw.card.has_value()) {
            emit(draw.entered_hand ? ProductEventKind::CardDrawn : ProductEventKind::CardArchived,
                player, draw.card);
        }
    }
    mulligan_done_[to_index(player)] = true;
    emit(ProductEventKind::MulliganSubmitted, player, std::nullopt, std::nullopt,
        static_cast<int>(plan.command.selected_cards.size()));
    if (mulligan_done_[0] && mulligan_done_[1]) {
        begin_turn(first_player_);
    }
}

void ProductGame::pay(const ProductPaymentPreview& payment, const PlayerId player) {
    ProductPlayerResources& state = resources_[to_index(player)];
    state.current_pp = payment.current_pp_after;
    state.pp_capacity = payment.pp_capacity_after;
    state.cracks = payment.cracks_after;
    state.evolution_energy = payment.evolution_energy_after;
    if (payment.future_used) {
        state.future_used_this_turn = true;
        const FutureUseEvent event = rules_.use_future(player, payment.advance_cost, payment.burn_cost);
        if (config_.evolution_charge_policies[to_index(player)] == EvolutionChargePolicy::FutureUseAtLeastTwo &&
            state.evolution_unlocked && !state.profession_charge_used_this_turn &&
            event.total_cracks() >= 2 && state.evolution_energy < 4) {
            ++state.evolution_energy;
            state.profession_charge_used_this_turn = true;
        }
    }
    emit(ProductEventKind::CostPaid, player, std::nullopt, std::nullopt,
        payment.base_cost, payment.advance_cost + payment.burn_cost);
}

void ProductGame::begin_turn(const PlayerId player) {
    active_player_ = player;
    phase_ = ProductGamePhase::Main;
    // A global turn changes for either player; owner-turn history deliberately
    // continues across the opponent's turn for profession/cycle abilities.
    turn_once_keys_.clear();
    ProductPlayerResources& state = resources_[to_index(player)];
    ++state.own_turn_number;
    ++state.pp_capacity;
    state.current_pp = state.pp_capacity;
    state.future_used_this_turn = false;
    state.evolved_this_turn = false;
    state.deploy_used_this_turn = false;
    state.profession_charge_used_this_turn = false;
    board_.ready_starting_turn_permanents(player);
    rules_.begin_owner_turn(player);
    const int unlock_turn = player == first_player_ ? 5 : 4;
    if (state.own_turn_number == unlock_turn) {
        state.evolution_unlocked = true;
        state.evolution_energy = player == first_player_ ? 2 : 3;
        rules_.set_evolution_unlocked(player, true);
    }
    const bool skip_draw = player == first_player_ && state.own_turn_number == 1;
    if (!skip_draw) {
        const DrawResult draw = board_.draw_one(player);
        if (draw.deck_empty) {
            handle_fatigue(player);
        } else if (draw.card.has_value()) {
            emit(draw.entered_hand ? ProductEventKind::CardDrawn : ProductEventKind::CardArchived,
                player, draw.card);
        }
    }
    if (phase_ != ProductGamePhase::Finished) {
        process_countdowns(player);
    }
    if (phase_ != ProductGamePhase::Finished) {
        emit(ProductEventKind::TurnStarted, player, std::nullopt, std::nullopt, state.own_turn_number);
    }
}

void ProductGame::end_turn() {
    ProductPlayerResources& state = resources_[to_index(active_player_)];
    state.current_pp = 0;
    board_.clear_turn_keyword_grants(active_player_);
    emit(ProductEventKind::TurnEnded, active_player_, std::nullopt, std::nullopt, state.own_turn_number);
    begin_turn(opponent(active_player_));
}

bool ProductGame::has_available_trap(const PlayerId player) const {
    return !available_traps(player).empty();
}

std::vector<InstanceId> ProductGame::available_traps(const PlayerId player) const {
    std::vector<InstanceId> result;
    if (!suspended_origin_.has_value()) {
        return result;
    }
    for (const auto& slot : board_.player(player).tactics) {
        if (!slot.has_value() || suspended_origin_->declared_traps.contains(*slot)) {
            continue;
        }
        const CardDefinition& definition = catalog_.at(board_.instance(*slot).design_id);
        if (definition.kind != CardKind::Trap) {
            continue;
        }
        const EffectTrigger required = suspended_origin_->plan.command.action == ActionKind::Attack
            ? EffectTrigger::OnAttackDeclared : EffectTrigger::OnEntry;
        const PlayerId event_player = suspended_origin_->plan.command.player;
        const auto eligible_effect = [&](const EffectSpec& effect) {
                const bool relation = effect.trigger_player_relation == TriggerPlayerRelation::Any ||
                    (effect.trigger_player_relation == TriggerPlayerRelation::SourceController && player == event_player) ||
                    (effect.trigger_player_relation == TriggerPlayerRelation::OpponentOfSourceController &&
                        player != event_player);
                return effect.trigger == required && relation;
            };
        const bool eligible = std::any_of(
            definition.effects.begin(), definition.effects.end(), eligible_effect) ||
            std::any_of(definition.modes.begin(), definition.modes.end(), [&](const ModeSpec& mode) {
                return std::any_of(mode.effects.begin(), mode.effects.end(), eligible_effect);
            });
        if (eligible) {
            result.push_back(*slot);
        }
    }
    return result;
}

void ProductGame::open_or_resolve_origin(
    ProductActionPlan plan,
    std::vector<EffectTask> post_direct_triggers) {
    SuspendedOrigin origin;
    origin.priority = opponent(plan.command.player);
    origin.plan = std::move(plan);
    origin.post_direct_triggers = std::move(post_direct_triggers);
    suspended_origin_ = std::move(origin);
    if (has_available_trap(suspended_origin_->priority)) {
        phase_ = ProductGamePhase::Reaction;
    } else {
        resolve_suspended_origin();
    }
}

std::size_t ProductGame::enqueue_effects(
    const CardDefinition& definition,
    const EffectTrigger trigger,
    const EffectContext& context,
    const bool front) {
    std::vector<EffectTask> tasks;
    const auto append = [&](const std::vector<EffectSpec>& effects) {
        for (const EffectSpec& effect : effects) {
            if (effect.trigger == trigger) {
                tasks.push_back(EffectTask{effect, context, std::nullopt, false});
            }
        }
    };
    append(definition.effects);
    if (!context.mode_id.empty()) {
        const std::string& mode_id = context.mode_id;
        const auto mode = std::find_if(definition.modes.begin(), definition.modes.end(), [&](const ModeSpec& item) {
            return item.mode_id == mode_id;
        });
        if (mode != definition.modes.end()) {
            append(mode->effects);
        }
    }
    if (front) {
        for (auto iterator = tasks.rbegin(); iterator != tasks.rend(); ++iterator) {
            effect_tasks_.push_front(std::move(*iterator));
        }
    } else {
        for (EffectTask& task : tasks) {
            effect_tasks_.push_back(std::move(task));
        }
    }
    return tasks.size();
}

std::vector<ProductGame::EffectTask> ProductGame::collect_global_effects(
    const EffectTrigger trigger,
    const EffectContext& event_context) const {
    std::vector<EffectTask> result;
    const std::array<PlayerId, kPlayerCount> order = {
        active_player_, opponent(active_player_),
    };
    for (const PlayerId controller : order) {
        for (const InstanceId source : board_.list_permanents(controller)) {
            const CardDefinition& definition = catalog_.at(board_.instance(source).design_id);
            for (const EffectSpec& effect : definition.effects) {
                if (effect.trigger != trigger) {
                    continue;
                }
                if (effect.trigger_owner_turn_only && controller != active_player_) {
                    continue;
                }
                if ((effect.trigger_player_relation == TriggerPlayerRelation::SourceController &&
                        controller != event_context.actor) ||
                    (effect.trigger_player_relation == TriggerPlayerRelation::OpponentOfSourceController &&
                        controller == event_context.actor)) {
                    continue;
                }
                // An independently triggered ability has its own target and
                // mode selection. Only the triggering event payload is shared;
                // inheriting a completed direct-play selection would silently
                // skip a target that must instead pause resolution.
                EffectContext context;
                context.actor = controller;
                context.source = source;
                context.last_repair = event_context.last_repair;
                context.future_use = event_context.future_use;
                result.push_back(EffectTask{effect, std::move(context), std::nullopt, false});
            }
        }
    }
    return result;
}

void ProductGame::queue_global_effects(std::vector<EffectTask> tasks) {
    if (tasks.empty()) {
        return;
    }
    std::vector<TriggerAbilityTask> abilities;
    for (EffectTask& task : tasks) {
        auto found = std::find_if(abilities.begin(), abilities.end(), [&](const TriggerAbilityTask& ability) {
            return ability.source == task.context.source;
        });
        if (found == abilities.end()) {
            TriggerAbilityTask ability;
            ability.controller = task.context.actor;
            ability.source = task.context.source;
            ability.trigger_id = "trigger-" + std::to_string(revision_ + 1U) + '-' +
                std::to_string(next_event_sequence_) + '-' + std::to_string(ability.source);
            ability.equivalence_key = board_.instance(ability.source).design_id;
            abilities.push_back(std::move(ability));
            found = std::prev(abilities.end());
        }
        found->equivalence_key += ':' + task.effect.effect_id;
        found->effects.push_back(std::move(task));
    }
    for (const PlayerId controller : {active_player_, opponent(active_player_)}) {
        std::vector<TriggerAbilityTask> group;
        for (TriggerAbilityTask& ability : abilities) {
            if (ability.controller == controller) {
                group.push_back(std::move(ability));
            }
        }
        if (!group.empty()) {
            trigger_batches_.push_back(std::move(group));
        }
    }
}

bool ProductGame::begin_next_trigger_batch() {
    if (trigger_batches_.empty()) {
        return false;
    }
    std::vector<TriggerAbilityTask> batch = std::move(trigger_batches_.front());
    trigger_batches_.pop_front();
    const bool equivalent = batch.size() == 1U ||
        std::all_of(batch.begin(), batch.end(), [&](const TriggerAbilityTask& ability) {
            return ability.equivalence_key == batch.front().equivalence_key;
        });
    if (equivalent) {
        std::sort(batch.begin(), batch.end(), [](const TriggerAbilityTask& lhs, const TriggerAbilityTask& rhs) {
            return lhs.source < rhs.source;
        });
        for (TriggerAbilityTask& ability : batch) {
            for (EffectTask& effect : ability.effects) {
                effect_tasks_.push_back(std::move(effect));
            }
        }
        return true;
    }

    PendingChoice choice;
    choice.choice_id = next_choice_id_++;
    choice.chooser = batch.front().controller;
    choice.kind = ChoiceKind::TriggerOrder;
    choice.minimum = batch.size();
    choice.maximum = batch.size();
    choice.ordered = true;
    PendingEffectChoice pending;
    pending.continuation = ChoiceContinuation::OrderTriggers;
    for (std::size_t index = 0; index < batch.size(); ++index) {
        const std::string option = "trigger-option-" + std::to_string(choice.choice_id) + '-' +
            std::to_string(index + 1U);
        choice.options.push_back(ChoiceOption{option, batch[index].source});
        pending.trigger_options.emplace(option, std::move(batch[index]));
    }
    const Status suspended = resolution_.suspend_for_choice(std::move(choice));
    if (!suspended) {
        throw std::logic_error(suspended.message);
    }
    pending_effect_choice_ = std::move(pending);
    phase_ = ProductGamePhase::Choice;
    emit(ProductEventKind::ChoiceRequested, pending_effect_choice_->trigger_options.begin()->second.controller);
    return false;
}

void ProductGame::resolve_suspended_origin() {
    if (!suspended_origin_.has_value()) {
        return;
    }
    phase_ = ProductGamePhase::Main;
    effect_tasks_.clear();
    trigger_batches_.clear();
    effect_outcomes_.clear();
    last_effect_keys_.clear();
    latest_repairs_.clear();
    SuspendedOrigin& origin = *suspended_origin_;
    for (auto iterator = origin.response_chain.rbegin(); iterator != origin.response_chain.rend(); ++iterator) {
        const ProductGameCommand& response = *iterator;
        if (!response.source.has_value()) {
            throw std::logic_error("validated response lost its source card");
        }
        const InstanceId trap = *response.source;
        const CardDefinition& definition = catalog_.at(board_.instance(trap).design_id);
        EffectContext context{board_.instance(trap).controller, trap, response.target,
            false, std::nullopt, std::nullopt, true, response.mode_id};
        const EffectTrigger trigger = origin.plan.command.action == ActionKind::Attack
            ? EffectTrigger::OnAttackDeclared : EffectTrigger::OnEntry;
        const std::size_t before = effect_tasks_.size();
        (void)enqueue_effects(definition, trigger, context);
        if (effect_tasks_.size() > before) {
            effect_tasks_.back().cleanup_after = trap;
        } else {
            (void)board_.move_to_graveyard(trap, MoveReason::Resolved, false);
            emit(ProductEventKind::CardMoved, context.actor, trap);
        }
    }

    const ProductActionPlan& plan = origin.plan;
    if (plan.operation == ProductPlanOperation::PlayMainPermanent ||
        plan.operation == ProductPlanOperation::PlayField ||
        plan.operation == ProductPlanOperation::Deploy) {
        const InstanceId source = *plan.command.source;
        const CardDefinition& definition = catalog_.at(board_.instance(source).design_id);
        const FutureUseEvent future{0, plan.command.player, plan.payment.advance_cost, plan.payment.burn_cost};
        EffectContext context{plan.command.player, source, plan.command.target, plan.payment.advanced,
            std::nullopt, plan.payment.future_used ? std::optional<FutureUseEvent>(future) : std::nullopt,
            true, plan.command.mode_id};
        (void)enqueue_effects(definition, EffectTrigger::OnPlay, context);
        (void)enqueue_effects(definition, EffectTrigger::OnEntry, context);
    } else if (plan.operation == ProductPlanOperation::CastSpell) {
        const InstanceId source = *plan.command.source;
        const CardDefinition& definition = catalog_.at(board_.instance(source).design_id);
        const FutureUseEvent future{0, plan.command.player, plan.payment.advance_cost, plan.payment.burn_cost};
        EffectContext context{plan.command.player, source, plan.command.target, plan.payment.advanced,
            std::nullopt, plan.payment.future_used ? std::optional<FutureUseEvent>(future) : std::nullopt,
            true, plan.command.mode_id};
        const std::size_t before = effect_tasks_.size();
        (void)enqueue_effects(definition, EffectTrigger::OnPlay, context);
        if (effect_tasks_.size() > before) {
            effect_tasks_.back().cleanup_after = source;
        } else {
            (void)board_.move_to_graveyard(source, MoveReason::Resolved, false);
            emit(ProductEventKind::CardMoved, plan.command.player, source);
        }
    }
    queue_global_effects(std::move(origin.post_direct_triggers));
    continue_effect_resolution();
}

bool ProductGame::effect_conditions_pass(const EffectSpec& effect, const EffectContext& context) const {
    std::optional<RepairResult> repair = context.last_repair;
    if (!repair.has_value()) {
        const auto latest = latest_repairs_.find(context.source);
        if (latest != latest_repairs_.end()) {
            repair = latest->second;
        }
    }
    const ConditionEvaluationContext evaluation = rules_.make_condition_context(
        context.actor, board_, repair, context.future_use, context.advanced);
    if (effect.condition.has_value() && !evaluate_condition(*effect.condition, evaluation)) {
        return false;
    }
    if (!std::all_of(effect.conditions.all.begin(), effect.conditions.all.end(), [&](const ConditionSpec& condition) {
            return evaluate_condition(condition, evaluation);
        })) {
        return false;
    }
    return effect.conditions.any.empty() ||
        std::any_of(effect.conditions.any.begin(), effect.conditions.any.end(), [&](const ConditionSpec& condition) {
            return evaluate_condition(condition, evaluation);
        });
}

int ProductGame::effect_value(const EffectSpec& effect, const EffectContext& context) const {
    if (!effect.uses_value_spec) {
        return effect.amount;
    }
    int value = effect.value.fixed;
    if (effect.value.source == AmountSource::ActualRepair) {
        const auto latest = latest_repairs_.find(context.source);
        value = context.last_repair.has_value() ? context.last_repair->actual_repaired :
            latest != latest_repairs_.end() ? latest->second.actual_repaired : 0;
    } else if (effect.value.source == AmountSource::Cracks) {
        value = resources_[to_index(context.actor)].cracks;
    }
    value *= effect.value.multiplier;
    if (effect.value.cap > 0) {
        value = std::min(value, effect.value.cap);
    }
    return value;
}

std::optional<InstanceId> ProductGame::effect_target(const EffectTask& task) const {
    if (task.effect.target == TargetSpec::Self) {
        if (!board_.contains_instance(task.context.source)) {
            return std::nullopt;
        }
        const Zone zone = board_.instance(task.context.source).zone;
        return zone == Zone::MainBoard || zone == Zone::Field
            ? std::optional<InstanceId>(task.context.source) : std::nullopt;
    }
    if (task.effect.target == TargetSpec::None || !task.context.target.has_value()) {
        return std::nullopt;
    }
    return validate_target(
               task.context.actor,
               task.context.source,
               task.context.target,
               task.effect.target,
               task.effect.target_filter)
        ? task.context.target : std::nullopt;
}

void ProductGame::open_card_choice(
    const EffectTask& task,
    const ChoiceContinuation continuation,
    const std::span<const InstanceId> cards) {
    PendingChoice choice;
    choice.choice_id = next_choice_id_++;
    choice.chooser = task.context.actor;
    choice.kind = ChoiceKind::Cards;
    choice.minimum = task.effect.selection_minimum;
    choice.maximum = task.effect.selection_maximum == 0U ? choice.minimum : task.effect.selection_maximum;
    choice.maximum = std::min(choice.maximum, cards.size());
    choice.minimum = std::min(choice.minimum, choice.maximum);
    PendingEffectChoice pending;
    pending.continuation = continuation;
    pending.task = task;
    for (std::size_t index = 0; index < cards.size(); ++index) {
        const std::string option = "choice-" + std::to_string(choice.choice_id) + '-' + std::to_string(index + 1U);
        choice.options.push_back(ChoiceOption{option, cards[index]});
        pending.option_cards.emplace(option, cards[index]);
    }
    const Status suspended = resolution_.suspend_for_choice(std::move(choice));
    if (!suspended) {
        throw std::logic_error(suspended.message);
    }
    pending_effect_choice_ = std::move(pending);
    phase_ = ProductGamePhase::Choice;
    emit(ProductEventKind::ChoiceRequested, task.context.actor, task.context.source);
}

bool ProductGame::execute_effect(EffectTask& task, EffectOutcome& outcome) {
    if (task.effect.trigger_owner_turn_only && task.context.actor != active_player_) {
        return true;
    }
    if (task.effect.dependency != EffectDependency::None) {
        std::string dependency_key;
        if (!task.effect.depends_on_effect_id.empty()) {
            dependency_key = effect_key(task.context.source, task.effect.depends_on_effect_id);
        } else if (last_effect_keys_.contains(task.context.source)) {
            dependency_key = last_effect_keys_.at(task.context.source);
        }
        const auto found = effect_outcomes_.find(dependency_key);
        if (found == effect_outcomes_.end() ||
            (task.effect.dependency == EffectDependency::PreviousEffectSucceeded && !found->second.succeeded) ||
            (task.effect.dependency == EffectDependency::PreviousDrawEnteredHand && !found->second.draw_entered_hand)) {
            return true;
        }
    }
    if (!effect_conditions_pass(task.effect, task.context)) {
        return true;
    }
    const auto consume_once = [&]() {
        if (task.effect.once_scope == OnceScope::None || task.effect.once_key.empty()) {
            return true;
        }
        if (task.effect.once_scope == OnceScope::OwnerTurn) {
            return rules_.consume_once_per_owner_turn(task.context.actor, task.effect.once_key);
        }
        if (task.effect.once_scope == OnceScope::SourceOwnerTurn) {
            return rules_.consume_once_per_owner_turn(task.context.actor,
                std::to_string(task.context.source) + ':' + task.effect.once_key);
        }
        if (task.effect.once_scope == OnceScope::SourceTurn) {
            return turn_once_keys_.insert(
                std::to_string(task.context.source) + ':' + task.effect.once_key).second;
        }
        return match_once_keys_.insert(
            std::to_string(to_index(task.context.actor)) + ':' + task.effect.once_key).second;
    };
    if (task.effect.once_consumption == OnceConsumption::OnTrigger && !task.trigger_once_consumed) {
        if (!consume_once()) {
            return true;
        }
        task.trigger_once_consumed = true;
    }
    if (!task.effect.target_from_effect_id.empty()) {
        const auto inherited = effect_outcomes_.find(
            effect_key(task.context.source, task.effect.target_from_effect_id));
        if (inherited != effect_outcomes_.end()) {
            task.context.target = inherited->second.selected_target;
        }
    }
    if (!task.context.target.has_value() && task.effect.target != TargetSpec::None &&
        task.effect.target != TargetSpec::Self) {
        if (task.context.target_selection_complete) {
            outcome.succeeded = task.effect.optional;
            return true;
        }
        const bool friendly = task.effect.target == TargetSpec::FriendlyFollower ||
            task.effect.target == TargetSpec::FriendlyPermanent;
        const PlayerId controller = friendly ? task.context.actor : opponent(task.context.actor);
        std::vector<InstanceId> candidates;
        for (const InstanceId candidate : board_.list_permanents(controller)) {
            if (validate_target(task.context.actor, task.context.source, candidate,
                    task.effect.target, task.effect.target_filter)) {
                candidates.push_back(candidate);
            }
        }
        if (!candidates.empty()) {
            open_card_choice(task, ChoiceContinuation::ResolvePermanentTarget, candidates);
            return false;
        }
        outcome.succeeded = task.effect.optional || task.effect.selection_minimum == 0U;
        return true;
    }
    if (task.effect.once_consumption == OnceConsumption::OnResolution && !consume_once()) {
        return true;
    }
    const int amount = std::max(0, effect_value(task.effect, task.context));
    const std::optional<InstanceId> target = effect_target(task);
    switch (task.effect.kind) {
        case EffectKind::Draw:
            for (int index = 0; index < amount && phase_ != ProductGamePhase::Finished; ++index) {
                const DrawResult draw = board_.draw_one(task.context.actor);
                if (draw.deck_empty) {
                    handle_fatigue(task.context.actor);
                } else if (draw.card.has_value()) {
                    outcome.draw_entered_hand = outcome.draw_entered_hand || draw.entered_hand;
                    emit(draw.entered_hand ? ProductEventKind::CardDrawn : ProductEventKind::CardArchived,
                        task.context.actor, draw.card);
                }
            }
            outcome.succeeded = amount == 0 || outcome.draw_entered_hand;
            return true;
        case EffectKind::HealLeader: {
            const int healed = board_.heal_leader(task.context.actor, amount);
            emit(ProductEventKind::Healing, task.context.actor, task.context.source, std::nullopt, healed);
            outcome.succeeded = healed > 0;
            return true;
        }
        case EffectKind::DamageFollower:
            if (target.has_value() && board_.contains_instance(*target) &&
                board_.instance(*target).zone == Zone::MainBoard) {
                const CardDefinition& target_definition = catalog_.at(board_.instance(*target).design_id);
                const PlayerId target_controller = board_.instance(*target).controller;
                const DamageResult damage = board_.damage_follower(*target, amount);
                emit(ProductEventKind::Damage, task.context.actor, task.context.source, target, damage.actual_damage);
                if (board_.instance(*target).zone == Zone::Graveyard &&
                    board_.instance(*target).current_health <= 0) {
                    EffectContext last_words{target_controller, *target, std::nullopt,
                        false, std::nullopt, std::nullopt, false, {}};
                    (void)enqueue_effects(target_definition, EffectTrigger::OnLastWords, last_words);
                }
                outcome.succeeded = damage.actual_damage > 0;
                outcome.selected_target = target;
            }
            return true;
        case EffectKind::RepairCracks: {
            const RepairResult repair = rules_.repair(task.context.actor, amount);
            ProductPlayerResources& state = resources_[to_index(task.context.actor)];
            state.cracks = repair.after;
            state.pp_capacity += repair.actual_repaired;
            task.context.last_repair = repair;
            latest_repairs_[task.context.source] = repair;
            if (repair.repaired_to_zero && state.evolution_unlocked &&
                config_.evolution_charge_policies[to_index(task.context.actor)] == EvolutionChargePolicy::RepairToZero &&
                !state.profession_charge_used_this_turn && state.evolution_energy < 4) {
                ++state.evolution_energy;
                state.profession_charge_used_this_turn = true;
            }
            if (repair.actual_repaired > 0) {
                queue_global_effects(collect_global_effects(
                    EffectTrigger::OnActualRepair, task.context));
            }
            if (repair.repaired_to_zero) {
                queue_global_effects(collect_global_effects(
                    EffectTrigger::OnRepairToZero, task.context));
            }
            outcome.succeeded = repair.actual_repaired > 0;
            return true;
        }
        case EffectKind::ModifyStats:
            if (target.has_value() && board_.contains_instance(*target)) {
                if (task.effect.duration == EffectDuration::OwnerTurn) {
                    (void)board_.grant_temporary_attack(*target, amount);
                } else {
                    (void)board_.grant_permanent_stats(*target, amount,
                        task.effect.uses_secondary_amount ? task.effect.secondary_amount : amount);
                }
                outcome.succeeded = true;
                outcome.selected_target = target;
            }
            return true;
        case EffectKind::GrantKeyword:
            if (target.has_value() && board_.contains_instance(*target)) {
                if (task.effect.duration == EffectDuration::OwnerTurn) {
                    board_.instance(*target).keywords.grant_for_turn(task.effect.granted_keyword);
                } else {
                    (void)board_.grant_permanent_keyword(*target, task.effect.granted_keyword);
                }
                if (task.effect.granted_keyword == Keyword::Barrier) {
                    rules_.record_barrier_granted(task.context.actor,
                        &catalog_.at(board_.instance(*target).design_id));
                }
                outcome.succeeded = true;
                outcome.selected_target = target;
            }
            return true;
        case EffectKind::ChangeCountdown:
            if (target.has_value()) {
                outcome.succeeded = static_cast<bool>(board_.change_countdown(*target, -amount));
                outcome.selected_target = target;
                if (outcome.succeeded && board_.instance(*target).countdown == 0) {
                    resolve_countdown_expiry(board_.instance(*target).controller, *target);
                }
            }
            return true;
        case EffectKind::SummonToken: {
            const auto& main = board_.player(task.context.actor).main_board;
            const auto empty = std::find(main.begin(), main.end(), std::nullopt);
            if (empty != main.end() && catalog_.contains(task.effect.parameter)) {
                const std::size_t slot = static_cast<std::size_t>(std::distance(main.begin(), empty));
                const InstanceId token = board_.create_instance(task.effect.parameter, task.context.actor);
                (void)board_.place_main(task.context.actor, token, slot, MoveReason::TokenSummoned);
                emit(ProductEventKind::CardPlayed, task.context.actor, token);
                outcome.succeeded = true;
            }
            return true;
        }
        case EffectKind::SearchTop: {
            const std::size_t reveal_count = task.effect.reveal_count == 0U
                ? static_cast<std::size_t>(amount) : task.effect.reveal_count;
            const std::vector<InstanceId> revealed = board_.reveal_top(task.context.actor, reveal_count);
            std::vector<InstanceId> cards;
            std::copy_if(revealed.begin(), revealed.end(), std::back_inserter(cards), [&](const InstanceId card) {
                return card_filter_matches(
                    catalog_.at(board_.instance(card).design_id), task.effect.card_filter);
            });
            if (!cards.empty()) {
                open_card_choice(task, ChoiceContinuation::SearchToHand, cards);
                pending_effect_choice_->revealed_cards = revealed;
                pending_effect_choice_->randomize_remainder = task.effect.randomize_remainder;
                return false;
            }
            if (!revealed.empty()) {
                const Status bottomed = board_.put_deck_cards_on_bottom(
                    task.context.actor, revealed, task.effect.randomize_remainder,
                    effective_seed_ ^ revision_ ^ task.context.source);
                if (!bottomed) {
                    throw std::logic_error(bottomed.message);
                }
            }
            outcome.succeeded = task.effect.selection_minimum == 0U;
            return true;
        }
        case EffectKind::PutOnDeckBottom: {
            std::vector<InstanceId> cards;
            for (const InstanceId card : board_.player(task.context.actor).hand) {
                if (card_filter_matches(catalog_.at(board_.instance(card).design_id), task.effect.card_filter)) {
                    cards.push_back(card);
                }
            }
            if (!cards.empty()) {
                open_card_choice(task, ChoiceContinuation::PutHandOnBottom, cards);
                return false;
            }
            outcome.succeeded = task.effect.selection_minimum == 0U;
            return true;
        }
        case EffectKind::Discard: {
            std::vector<InstanceId> cards;
            for (const InstanceId card : board_.player(task.context.actor).hand) {
                if (card_filter_matches(catalog_.at(board_.instance(card).design_id), task.effect.card_filter)) {
                    cards.push_back(card);
                }
            }
            if (!cards.empty()) {
                open_card_choice(task, ChoiceContinuation::DiscardFromHand, cards);
                return false;
            }
            outcome.succeeded = task.effect.selection_minimum == 0U;
            return true;
        }
        case EffectKind::DestroyPermanent:
            if (target.has_value() && board_.contains_instance(*target)) {
                const CardDefinition& target_definition = catalog_.at(board_.instance(*target).design_id);
                const PlayerId target_controller = board_.instance(*target).controller;
                outcome.succeeded = static_cast<bool>(board_.destroy_permanent(*target));
                outcome.selected_target = target;
                if (outcome.succeeded && board_.instance(*target).zone == Zone::Graveyard) {
                    EffectContext last_words{target_controller, *target, std::nullopt,
                        false, std::nullopt, std::nullopt, false, {}};
                    (void)enqueue_effects(target_definition, EffectTrigger::OnLastWords, last_words);
                }
            }
            return true;
        case EffectKind::CancelAttack:
            if (suspended_origin_.has_value() && suspended_origin_->plan.command.action == ActionKind::Attack) {
                suspended_origin_->cancelled = true;
                outcome.succeeded = true;
            }
            return true;
    }
    return true;
}

void ProductGame::continue_effect_resolution() {
    while (!effect_tasks_.empty() && phase_ != ProductGamePhase::Finished) {
        EffectTask task = std::move(effect_tasks_.front());
        effect_tasks_.pop_front();
        EffectOutcome outcome;
        if (!execute_effect(task, outcome)) {
            pending_effect_choice_->task.cleanup_after = task.cleanup_after;
            return;
        }
        record_effect_outcome(task, outcome);
        if (task.cleanup_after.has_value() && board_.contains_instance(*task.cleanup_after) &&
            board_.instance(*task.cleanup_after).zone == Zone::Tactic) {
            const PlayerId owner = board_.instance(*task.cleanup_after).controller;
            (void)board_.move_to_graveyard(*task.cleanup_after, MoveReason::Resolved, false);
            emit(ProductEventKind::CardMoved, owner, *task.cleanup_after);
        }
    }
    if (phase_ != ProductGamePhase::Finished && !pending_effect_choice_.has_value()) {
        if (begin_next_trigger_batch()) {
            continue_effect_resolution();
        } else if (phase_ != ProductGamePhase::Choice) {
            finish_resolution();
        }
    }
}

void ProductGame::apply_effect_choice(const std::span<const std::string> selected_option_ids) {
    if (!pending_effect_choice_.has_value()) {
        throw std::logic_error("resolved choice has no product continuation");
    }
    PendingEffectChoice pending = std::move(*pending_effect_choice_);
    pending_effect_choice_.reset();
    if (pending.continuation == ChoiceContinuation::OrderTriggers) {
        for (const std::string& option : selected_option_ids) {
            TriggerAbilityTask ability = std::move(pending.trigger_options.at(option));
            for (EffectTask& effect : ability.effects) {
                effect_tasks_.push_back(std::move(effect));
            }
        }
        return;
    }
    if (pending.continuation == ChoiceContinuation::ResolvePermanentTarget) {
        EffectOutcome outcome;
        if (!selected_option_ids.empty()) {
            pending.task.context.target = pending.option_cards.at(selected_option_ids.front());
            (void)execute_effect(pending.task, outcome);
            outcome.selected_target = pending.task.context.target;
        } else {
            outcome.succeeded = pending.task.effect.optional ||
                pending.task.effect.selection_minimum == 0U;
        }
        record_effect_outcome(pending.task, outcome);
        if (pending.task.cleanup_after.has_value() &&
            board_.contains_instance(*pending.task.cleanup_after) &&
            board_.instance(*pending.task.cleanup_after).zone == Zone::Tactic) {
            const PlayerId owner = board_.instance(*pending.task.cleanup_after).controller;
            (void)board_.move_to_graveyard(*pending.task.cleanup_after, MoveReason::Resolved, false);
            emit(ProductEventKind::CardMoved, owner, *pending.task.cleanup_after);
        }
        return;
    }
    for (const std::string& option : selected_option_ids) {
        const InstanceId card = pending.option_cards.at(option);
        Status status;
        if (pending.continuation == ChoiceContinuation::SearchToHand) {
            status = board_.move_deck_card_to_hand(pending.task.context.actor, card);
        } else if (pending.continuation == ChoiceContinuation::PutHandOnBottom) {
            status = board_.move_hand_card_to_deck_bottom(pending.task.context.actor, card);
        } else {
            status = board_.discard_from_hand(pending.task.context.actor, card);
        }
        if (!status) {
            throw std::logic_error(status.message);
        }
    }
    if (pending.continuation == ChoiceContinuation::SearchToHand &&
        !pending.revealed_cards.empty()) {
        std::unordered_set<InstanceId> selected_cards;
        for (const std::string& option : selected_option_ids) {
            selected_cards.insert(pending.option_cards.at(option));
        }
        std::vector<InstanceId> remainder;
        std::copy_if(pending.revealed_cards.begin(), pending.revealed_cards.end(),
            std::back_inserter(remainder), [&](const InstanceId card) {
                return !selected_cards.contains(card) && board_.instance(card).zone == Zone::Deck;
            });
        const Status bottomed = board_.put_deck_cards_on_bottom(
            pending.task.context.actor, remainder, pending.randomize_remainder,
            effective_seed_ ^ revision_ ^ pending.task.context.source);
        if (!bottomed) {
            throw std::logic_error(bottomed.message);
        }
    }
    EffectOutcome outcome;
    outcome.succeeded = selected_option_ids.size() >= pending.task.effect.selection_minimum;
    outcome.draw_entered_hand = pending.continuation == ChoiceContinuation::SearchToHand &&
        !selected_option_ids.empty();
    if (!selected_option_ids.empty()) {
        outcome.selected_target = pending.option_cards.at(selected_option_ids.front());
    }
    record_effect_outcome(pending.task, outcome);
    if (pending.task.cleanup_after.has_value() &&
        board_.contains_instance(*pending.task.cleanup_after) &&
        board_.instance(*pending.task.cleanup_after).zone == Zone::Tactic) {
        const PlayerId owner = board_.instance(*pending.task.cleanup_after).controller;
        (void)board_.move_to_graveyard(*pending.task.cleanup_after, MoveReason::Resolved, false);
        emit(ProductEventKind::CardMoved, owner, *pending.task.cleanup_after);
    }
}

void ProductGame::finish_resolution() {
    if (!suspended_origin_.has_value()) {
        phase_ = ProductGamePhase::Main;
        return;
    }
    SuspendedOrigin origin = std::move(*suspended_origin_);
    suspended_origin_.reset();
    const ProductActionPlan& plan = origin.plan;
    if (plan.operation == ProductPlanOperation::AttackFollower ||
        plan.operation == ProductPlanOperation::AttackLeader) {
        if (origin.cancelled) {
            emit(ProductEventKind::AttackCancelled, plan.command.player, plan.command.source, plan.command.target);
        } else if (plan.command.source.has_value() && board_.contains_instance(*plan.command.source) &&
            board_.instance(*plan.command.source).zone == Zone::MainBoard) {
            const InstanceId attacker = *plan.command.source;
            if (plan.command.target.has_value() && board_.contains_instance(*plan.command.target) &&
                board_.instance(*plan.command.target).zone == Zone::MainBoard) {
                const CombatResult combat = board_.resolve_accepted_follower_combat(attacker, *plan.command.target);
                emit(ProductEventKind::Damage, plan.command.player, attacker, plan.command.target,
                    combat.damage_to_defender.actual_damage, combat.damage_to_attacker.actual_damage);
                if (combat.attacker_healed > 0) {
                    emit(ProductEventKind::Healing, plan.command.player, attacker,
                        std::nullopt, combat.attacker_healed);
                }
                EffectContext attacker_context{plan.command.player, attacker, plan.command.target,
                    false, std::nullopt, std::nullopt, false, {}};
                if (combat.attacker_killed_follower_and_survived) {
                    (void)enqueue_effects(catalog_.at(board_.instance(attacker).design_id),
                        EffectTrigger::OnCombatKillSurvived, attacker_context);
                }
                if (combat.attacker_destroyed && !combat.defender_destroyed) {
                    // A surviving defender also destroyed a follower through
                    // combat. This is separate from active-attack-only Lifesteal.
                    EffectContext defender_context{opponent(plan.command.player), *plan.command.target,
                        attacker, false, std::nullopt, std::nullopt, false, {}};
                    (void)enqueue_effects(catalog_.at(board_.instance(*plan.command.target).design_id),
                        EffectTrigger::OnCombatKillSurvived, defender_context);
                }
                if (combat.defender_destroyed &&
                    board_.instance(*plan.command.target).zone == Zone::Graveyard) {
                    EffectContext defender_context{opponent(plan.command.player), *plan.command.target,
                        std::nullopt, false, std::nullopt, std::nullopt, false, {}};
                    (void)enqueue_effects(catalog_.at(board_.instance(*plan.command.target).design_id),
                        EffectTrigger::OnLastWords, defender_context);
                }
                if (combat.attacker_destroyed && board_.instance(attacker).zone == Zone::Graveyard) {
                    (void)enqueue_effects(catalog_.at(board_.instance(attacker).design_id),
                        EffectTrigger::OnLastWords, attacker_context);
                }
            } else if (!plan.command.target.has_value()) {
                PlayerState& defender = board_.player(opponent(plan.command.player));
                const int damage = std::min(std::max(0, board_.instance(attacker).current_attack),
                    std::max(0, defender.leader_health));
                defender.leader_health = std::max(0, defender.leader_health - damage);
                emit(ProductEventKind::Damage, plan.command.player, attacker, std::nullopt, damage);
                if (board_.instance(attacker).keywords.has(Keyword::Lifesteal) && damage > 0) {
                    const int healed = board_.heal_leader(plan.command.player, damage);
                    emit(ProductEventKind::Healing, plan.command.player, attacker, std::nullopt, healed);
                }
                if (defender.leader_health <= 0) {
                    finish_match(plan.command.player, "leader damage");
                    return;
                }
            }
        }
    }
    phase_ = ProductGamePhase::Main;
    if (!effect_tasks_.empty()) {
        continue_effect_resolution();
    }
}

void ProductGame::process_countdowns(const PlayerId player) {
    std::vector<InstanceId> expiring;
    for (const auto& slot : board_.player(player).main_board) {
        if (!slot.has_value()) {
            continue;
        }
        const CardDefinition& definition = catalog_.at(board_.instance(*slot).design_id);
        if (definition.kind != CardKind::Amulet) {
            continue;
        }
        CardInstance& amulet = board_.instance(*slot);
        if (amulet.countdown <= 1) {
            expiring.push_back(*slot);
        } else {
            --amulet.countdown;
        }
    }
    for (const InstanceId amulet : expiring) {
        if (phase_ == ProductGamePhase::Finished) {
            break;
        }
        if (!board_.contains_instance(amulet) || board_.instance(amulet).zone != Zone::MainBoard) {
            continue;
        }
        resolve_countdown_expiry(player, amulet);
    }
}

void ProductGame::resolve_countdown_expiry(
    const PlayerId player,
    const InstanceId amulet) {
    if (!board_.contains_instance(amulet) || board_.instance(amulet).zone != Zone::MainBoard) {
        return;
    }
    const CardDefinition& definition = catalog_.at(board_.instance(amulet).design_id);
    const std::size_t slot = board_.instance(amulet).sequence;
    const ResolutionFrameId frame = (revision_ + 1U) * 1000U +
        next_event_sequence_ * 10U + slot + 1U;
    const Status expired = board_.expire_amulet_and_reserve(amulet, frame);
    if (!expired) {
        throw std::logic_error(expired.message);
    }
    rules_.record_countdown_expired(player, &definition);
    EffectContext context{player, amulet, std::nullopt, false, std::nullopt, std::nullopt, false, {}};
    for (const EffectSpec& effect : definition.effects) {
        if (effect.trigger != EffectTrigger::OnCountdownEnd) {
            continue;
        }
        if (effect.kind == EffectKind::SummonToken && effect.preserve_source_slot &&
            catalog_.contains(effect.parameter)) {
            InstanceId token = 0;
            const Status summoned = board_.summon_token_in_reserved_slot(
                player, effect.parameter, slot, frame, token);
            if (!summoned) {
                throw std::logic_error(summoned.message);
            }
            emit(ProductEventKind::CardPlayed, player, token);
        } else {
            EffectTask task{effect, context, std::nullopt, false};
            EffectOutcome outcome;
            if (!execute_effect(task, outcome)) {
                throw std::logic_error("countdown effect unexpectedly requires a paid choice");
            }
            if (phase_ == ProductGamePhase::Finished) {
                break;
            }
            record_effect_outcome(task, outcome);
        }
    }
    board_.release_reservations(frame);
}

void ProductGame::handle_fatigue(const PlayerId player) {
    ProductPlayerResources& state = resources_[to_index(player)];
    ++state.fatigue_count;
    PlayerState& board_state = board_.player(player);
    board_state.leader_health = std::max(0, board_state.leader_health - state.fatigue_count);
    emit(ProductEventKind::Damage, opponent(player), std::nullopt, std::nullopt, state.fatigue_count);
    if (board_state.leader_health <= 0) {
        finish_match(opponent(player), "fatigue");
    }
}

void ProductGame::finish_match(const PlayerId winner, const std::string_view reason) {
    if (phase_ == ProductGamePhase::Finished) {
        return;
    }
    if (suspended_origin_.has_value()) {
        for (auto iterator = suspended_origin_->response_chain.rbegin();
             iterator != suspended_origin_->response_chain.rend(); ++iterator) {
            const std::optional<InstanceId> source = iterator->source;
            if (source.has_value() && board_.contains_instance(*source) &&
                board_.instance(*source).zone == Zone::Tactic) {
                const PlayerId controller = board_.instance(*source).controller;
                (void)board_.move_to_graveyard(*source, MoveReason::TerminalCleanup, false);
                emit(ProductEventKind::CardMoved, controller, source);
            }
        }
        const auto source = suspended_origin_->plan.command.source;
        if (source.has_value() && board_.contains_instance(*source) &&
            board_.instance(*source).zone == Zone::Tactic &&
            catalog_.at(board_.instance(*source).design_id).kind == CardKind::Spell) {
            const PlayerId owner = board_.instance(*source).controller;
            (void)board_.move_to_graveyard(*source, MoveReason::TerminalCleanup, false);
            emit(ProductEventKind::CardMoved, owner, source);
        }
    }
    suspended_origin_.reset();
    pending_effect_choice_.reset();
    effect_tasks_.clear();
    trigger_batches_.clear();
    resolution_.finish_match();
    result_ = winner == PlayerId::Player0 ? ProductMatchResult::Player0Won : ProductMatchResult::Player1Won;
    phase_ = ProductGamePhase::Finished;
    emit(ProductEventKind::MatchEnded, winner, std::nullopt, std::nullopt, 0, 0, std::string(reason));
}

void ProductGame::execute_plan(const ProductActionPlan& plan) {
    const ProductGameCommand& command = plan.command;
    switch (plan.operation) {
        case ProductPlanOperation::Mulligan:
            execute_mulligan(plan);
            return;
        case ProductPlanOperation::Surrender:
            emit(ProductEventKind::PlayerSurrendered, command.player);
            finish_match(opponent(command.player), "surrender");
            return;
        case ProductPlanOperation::EndTurn:
            end_turn();
            return;
        case ProductPlanOperation::ResolveChoice: {
            const Status resolved = resolution_.resolve_choice(
                command.player, command.choice_id, command.selected_option_ids);
            if (!resolved) {
                throw std::logic_error(resolved.message);
            }
            (void)resolution_.take_resolved_choice();
            apply_effect_choice(command.selected_option_ids);
            emit(ProductEventKind::ChoiceResolved, command.player);
            phase_ = ProductGamePhase::Main;
            continue_effect_resolution();
            return;
        }
        case ProductPlanOperation::ActivateTrap:
            suspended_origin_->response_chain.push_back(command);
            suspended_origin_->declared_traps.insert(*command.source);
            suspended_origin_->consecutive_passes = 0;
            suspended_origin_->priority = opponent(command.player);
            emit(ProductEventKind::TrapActivated, command.player, command.source);
            return;
        case ProductPlanOperation::PassReaction:
            ++suspended_origin_->consecutive_passes;
            emit(ProductEventKind::ReactionPassed, command.player);
            if (suspended_origin_->consecutive_passes >= 2) {
                resolve_suspended_origin();
            } else {
                suspended_origin_->priority = opponent(command.player);
            }
            return;
        case ProductPlanOperation::Evolve: {
            pay(plan.payment, command.player);
            CardInstance& follower = board_.instance(*command.source);
            follower.evolved = true;
            follower.current_attack += 2;
            follower.current_health += 2;
            follower.maximum_health += 2;
            resources_[to_index(command.player)].evolved_this_turn = true;
            emit(ProductEventKind::Evolved, command.player, command.source);
            EffectContext context{command.player, *command.source, command.target,
                false, std::nullopt, std::nullopt, true, command.mode_id};
            (void)enqueue_effects(catalog_.at(follower.design_id), EffectTrigger::OnEvolve, context);
            continue_effect_resolution();
            return;
        }
        case ProductPlanOperation::AttackFollower:
        case ProductPlanOperation::AttackLeader: {
            const Status accepted = board_.accept_attack_declaration(command.player, *command.source, command.target);
            if (!accepted) {
                throw std::logic_error(accepted.message);
            }
            emit(ProductEventKind::AttackDeclared, command.player, command.source, command.target);
            open_or_resolve_origin(plan);
            return;
        }
        case ProductPlanOperation::PlayMainPermanent:
        case ProductPlanOperation::CastSpell:
        case ProductPlanOperation::SetTrap:
        case ProductPlanOperation::PlayField:
        case ProductPlanOperation::Deploy:
            break;
    }

    pay(plan.payment, command.player);
    std::vector<EffectTask> future_triggers;
    if (plan.payment.future_used) {
        const FutureUseEvent future{0, command.player, plan.payment.advance_cost, plan.payment.burn_cost};
        EffectContext event_context{command.player, *command.source, command.target,
            plan.payment.advanced, std::nullopt, future, true, command.mode_id};
        future_triggers = collect_global_effects(EffectTrigger::OnFutureUsed, event_context);
    }
    if (plan.operation == ProductPlanOperation::Deploy) {
        const CardDefinition& definition = catalog_.at(board_.instance(*command.source).design_id);
        for (const InstanceId cost : command.additional_cost_cards) {
            const Status paid = board_.pay_additional_archive_cost(
                command.player, cost, PermanentFilter::from_spec(definition.standby->additional_cost_filter));
            if (!paid) {
                throw std::logic_error(paid.message);
            }
        }
    }
    Status moved;
    if (plan.operation == ProductPlanOperation::PlayMainPermanent ||
        plan.operation == ProductPlanOperation::Deploy) {
        moved = board_.place_main(command.player, *command.source, *command.slot, MoveReason::Played);
    } else if (plan.operation == ProductPlanOperation::CastSpell ||
        plan.operation == ProductPlanOperation::SetTrap) {
        moved = board_.place_tactic(command.player, *command.source, *command.slot, MoveReason::Played);
    } else {
        moved = board_.play_field(command.player, *command.source);
    }
    if (!moved) {
        throw std::logic_error(moved.message);
    }
    if (plan.operation == ProductPlanOperation::Deploy) {
        resources_[to_index(command.player)].deploy_used_this_turn = true;
    }
    emit(ProductEventKind::CardPlayed, command.player, command.source);
    if (plan.operation == ProductPlanOperation::SetTrap) {
        queue_global_effects(std::move(future_triggers));
        continue_effect_resolution();
        return;
    }
    open_or_resolve_origin(plan, std::move(future_triggers));
}

ProductGameStatus ProductGame::submit_command(const ProductGameCommand& command) {
    const ProductActionPlan plan = plan_command(command);
    if (!plan) {
        return plan.status;
    }
    execute_plan(plan);
    ++revision_;
    return ProductGameStatus::ok();
}

ProductGamePhase ProductGame::phase() const noexcept { return phase_; }
ProductMatchResult ProductGame::result() const noexcept { return result_; }
PlayerId ProductGame::active_player() const noexcept { return active_player_; }
PlayerId ProductGame::first_player() const noexcept { return first_player_; }
std::uint64_t ProductGame::revision() const noexcept { return revision_; }
const ProductBoard& ProductGame::board() const noexcept { return board_; }

const ProductPlayerResources& ProductGame::resources(const PlayerId player) const {
    if (!is_valid_player(player)) {
        throw std::out_of_range("invalid product resource player");
    }
    return resources_[to_index(player)];
}

bool ProductGame::mulligan_complete(const PlayerId player) const {
    if (!is_valid_player(player)) {
        throw std::out_of_range("invalid product mulligan player");
    }
    return mulligan_done_[to_index(player)];
}

const std::optional<PendingChoice>& ProductGame::pending_choice() const noexcept {
    return resolution_.pending_choice();
}

ProductReactionContext ProductGame::reaction_context() const noexcept {
    ProductReactionContext context;
    if (!suspended_origin_.has_value() || phase_ != ProductGamePhase::Reaction) {
        return context;
    }
    context.pending = true;
    context.priority = suspended_origin_->priority;
    context.origin_player = suspended_origin_->plan.command.player;
    context.origin_action = suspended_origin_->plan.command.action;
    context.origin_source = suspended_origin_->plan.command.source;
    context.origin_target = suspended_origin_->plan.command.target;
    context.chain_size = suspended_origin_->response_chain.size();
    return context;
}

std::vector<ProductGameEvent> ProductGame::read_events(const std::uint64_t after_sequence) const {
    std::vector<ProductGameEvent> result;
    std::copy_if(events_.begin(), events_.end(), std::back_inserter(result), [&](const ProductGameEvent& event) {
        return event.sequence > after_sequence;
    });
    return result;
}

const std::vector<ProductGameEvent>& ProductGame::events() const noexcept { return events_; }

std::vector<std::string> ProductGame::validate_invariants() const {
    std::vector<std::string> problems = board_.validate_invariants();
    for (std::size_t index = 0; index < kPlayerCount; ++index) {
        const ProductPlayerResources& state = resources_[index];
        if (state.current_pp < 0 || state.pp_capacity < 0 ||
            state.cracks < 0 || state.evolution_energy < 0 || state.evolution_energy > 4) {
            problems.push_back("product player resource invariant failed");
        }
    }
    if (phase_ == ProductGamePhase::Choice && !resolution_.pending_choice().has_value()) {
        problems.push_back("choice phase has no pending choice");
    }
    if (phase_ == ProductGamePhase::Reaction && !suspended_origin_.has_value()) {
        problems.push_back("reaction phase has no suspended origin");
    }
    if (phase_ == ProductGamePhase::Finished) {
        const std::size_t ended = static_cast<std::size_t>(std::count_if(events_.begin(), events_.end(), [](const auto& event) {
            return event.kind == ProductEventKind::MatchEnded;
        }));
        if (ended != 1U || events_.empty() || events_.back().kind != ProductEventKind::MatchEnded) {
            problems.push_back("finished product match must have one final MatchEnded event");
        }
    }
    return problems;
}

ProductGameError ProductGame::translate(const ErrorCode code) noexcept {
    switch (code) {
        case ErrorCode::Ok: return ProductGameError::Ok;
        case ErrorCode::InvalidPlayer: return ProductGameError::InvalidPlayer;
        case ErrorCode::InvalidCard: return ProductGameError::InvalidCard;
        case ErrorCode::InvalidKind: return ProductGameError::InvalidCardKind;
        case ErrorCode::InvalidZone: return ProductGameError::InvalidZone;
        case ErrorCode::InvalidSlot: return ProductGameError::InvalidSlot;
        case ErrorCode::SlotOccupied: return ProductGameError::SlotOccupied;
        case ErrorCode::MainBoardFull: return ProductGameError::MainBoardFull;
        case ErrorCode::NoPendingChoice: return ProductGameError::NoPendingChoice;
        case ErrorCode::ChoicePending: return ProductGameError::ChoicePending;
        case ErrorCode::NotChoiceOwner:
        case ErrorCode::InvalidChoice:
        case ErrorCode::DuplicateSelection:
        case ErrorCode::WrongSelectionCount: return ProductGameError::InvalidSelection;
        default: return ProductGameError::InvalidCommand;
    }
}

std::string ProductGame::effect_key(
    const InstanceId source,
    const std::string_view effect_id) {
    return std::to_string(source) + ':' + std::string(effect_id);
}

void ProductGame::record_effect_outcome(
    const EffectTask& task,
    const EffectOutcome& outcome) {
    const std::string id = task.effect.effect_id.empty()
        ? "step-" + std::to_string(effect_outcomes_.size() + 1U)
        : task.effect.effect_id;
    const std::string key = effect_key(task.context.source, id);
    effect_outcomes_[key] = outcome;
    last_effect_keys_[task.context.source] = key;
}

} // namespace scgs::v2
