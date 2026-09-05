// SPDX-License-Identifier: GPL-3.0-or-later
#include "scgs/product_runtime.hpp"

#include <algorithm>
#include <random>
#include <stdexcept>
#include <unordered_set>
#include <utility>

namespace scgs::v2 {

KeywordMask KeywordState::effective() const noexcept {
    return (printed | permanent | turn) & ~consumed;
}

bool KeywordState::has(const Keyword keyword) const noexcept {
    return contains(effective(), keyword);
}

void KeywordState::grant_permanent(const Keyword keyword) noexcept {
    permanent |= mask(keyword);
    consumed &= ~mask(keyword);
}

void KeywordState::grant_for_turn(const Keyword keyword) noexcept {
    turn |= mask(keyword);
    consumed &= ~mask(keyword);
}

bool KeywordState::consume(const Keyword keyword) noexcept {
    if (!has(keyword)) {
        return false;
    }
    consumed |= mask(keyword);
    return true;
}

void KeywordState::clear_turn() noexcept {
    const KeywordMask turn_only_consumed = consumed & turn & ~(printed | permanent);
    turn = mask(Keyword::None);
    consumed &= ~turn_only_consumed;
}

bool CardIdentity::is_constructible_for(const std::string_view profession) const noexcept {
    return neutral || profession_id == profession;
}

void CardCatalog::add(CardDefinition definition) {
    if (definition.identity.design_id.empty()) {
        throw std::invalid_argument("product card design_id cannot be empty");
    }
    if (definition.identity.neutral) {
        if (definition.identity.profession_id != "neutral" || definition.identity.series_id != "neutral") {
            throw std::invalid_argument("neutral product cards must use neutral profession and series tags");
        }
    } else if (definition.identity.profession_id.empty() || definition.identity.series_id.empty()) {
        throw std::invalid_argument("class product cards require profession and series tags");
    }
    if (definition.kind != CardKind::Follower && (definition.attack != 0 || definition.health != 0)) {
        throw std::invalid_argument("only followers may have combat stats");
    }
    if (definition.kind == CardKind::Follower && definition.health <= 0) {
        throw std::invalid_argument("followers require positive health");
    }
    if (definition.kind != CardKind::Amulet && definition.countdown != 0) {
        throw std::invalid_argument("only amulets may carry countdown");
    }
    if ((definition.availability == CardAvailability::Standby) != definition.standby.has_value()) {
        throw std::invalid_argument("standby availability and deployment metadata must agree");
    }
    if (definition.availability == CardAvailability::Standby && definition.can_advance) {
        throw std::invalid_argument("standby cards cannot use advance");
    }
    if (definition.availability == CardAvailability::Token &&
        (definition.cost != 0 || definition.can_advance)) {
        throw std::invalid_argument("tokens cannot have a hand-play cost or use advance");
    }
    const bool locked =
        definition.implementation_status == CardImplementationStatus::LockedNotImplemented;
    if (locked != !definition.effects_compiled) {
        throw std::invalid_argument(
            "product implementation status and effects_compiled must agree");
    }
    if (locked && (!definition.effects.empty() || std::any_of(
            definition.modes.begin(), definition.modes.end(), [](const ModeSpec& mode) {
                return !mode.effects.empty();
            }))) {
        throw std::invalid_argument(
            "locked-not-implemented product definitions cannot carry executable effects");
    }
    if (definition.standby.has_value()) {
        const StandbySpec& standby = *definition.standby;
        if (standby.conditions.empty()) {
            throw std::invalid_argument("standby definitions require typed deployment conditions");
        }
        const bool has_cost = standby.requires_additional_cost;
        const bool valid_cost = has_cost
            ? standby.additional_cost_target != TargetSpec::None &&
                standby.additional_cost_minimum > 0 &&
                standby.additional_cost_maximum >= standby.additional_cost_minimum
            : standby.additional_cost_target == TargetSpec::None &&
                standby.additional_cost_minimum == 0 && standby.additional_cost_maximum == 0;
        if (!valid_cost) {
            throw std::invalid_argument("standby additional-cost metadata is inconsistent");
        }
    }
    const DesignId design_id = definition.identity.design_id;
    const bool inserted = definitions_.emplace(design_id, std::move(definition)).second;
    if (!inserted) {
        throw std::invalid_argument("duplicate product design_id");
    }
}

bool CardCatalog::contains(const std::string_view design_id) const noexcept {
    return std::any_of(definitions_.begin(), definitions_.end(), [&](const auto& entry) {
        return entry.first == design_id;
    });
}

const CardDefinition& CardCatalog::at(const std::string_view design_id) const {
    return definitions_.at(std::string(design_id));
}

std::size_t CardCatalog::size() const noexcept {
    return definitions_.size();
}

std::vector<DesignId> CardCatalog::list_executable(
    const CardAvailability availability) const {
    std::vector<DesignId> result;
    for (const auto& [design_id, definition] : definitions_) {
        if (definition.availability == availability && definition.is_executable()) {
            result.push_back(design_id);
        }
    }
    std::sort(result.begin(), result.end());
    return result;
}

const std::unordered_map<DesignId, CardDefinition>& CardCatalog::definitions() const noexcept {
    return definitions_;
}

bool CardFilter::matches(const CardDefinition& definition) const noexcept {
    if (required_kind.has_value() && definition.kind != *required_kind) {
        return false;
    }
    if (excluded_kind.has_value() && definition.kind == *excluded_kind) {
        return false;
    }
    if (!profession_id.empty() && definition.identity.profession_id != profession_id) {
        return false;
    }
    if (!series_id.empty() && definition.identity.series_id != series_id) {
        return false;
    }
    return !neutral.has_value() || definition.identity.neutral == *neutral;
}

bool PermanentFilter::matches(const CardDefinition& definition) const noexcept {
    if (!allowed_kinds.empty() &&
        std::find(allowed_kinds.begin(), allowed_kinds.end(), definition.kind) == allowed_kinds.end()) {
        return false;
    }
    if (!profession_id.empty() && definition.identity.profession_id != profession_id) {
        return false;
    }
    return series_id.empty() || definition.identity.series_id == series_id;
}

PermanentFilter PermanentFilter::from_spec(const PermanentSelectorSpec& spec) {
    PermanentFilter filter;
    filter.allowed_kinds = spec.allowed_kinds;
    filter.profession_id = spec.profession_id;
    filter.series_id = spec.series_id;
    filter.include_main_board = spec.include_main_board;
    filter.include_field = spec.include_field;
    return filter;
}

namespace {

CardDefinition follower(
    const std::string_view id,
    const int attack,
    const int health,
    const KeywordMask keywords = mask(Keyword::None)) {
    CardDefinition card;
    card.identity = CardIdentity{std::string(id), "fixture", "synthetic", false};
    card.name = std::string(id);
    card.kind = CardKind::Follower;
    card.attack = attack;
    card.health = health;
    card.printed_keywords = keywords;
    card.implementation_status = CardImplementationStatus::SyntheticFixture;
    card.effects_compiled = true;
    return card;
}

CardDefinition permanent(const std::string_view id, const CardKind kind) {
    CardDefinition card;
    card.identity = CardIdentity{std::string(id), "fixture", "synthetic", false};
    card.name = std::string(id);
    card.kind = kind;
    card.implementation_status = CardImplementationStatus::SyntheticFixture;
    card.effects_compiled = true;
    return card;
}

void erase_card(std::vector<InstanceId>& cards, const InstanceId card) {
    cards.erase(std::remove(cards.begin(), cards.end(), card), cards.end());
}

void normalize(std::vector<InstanceId>& cards, std::unordered_map<InstanceId, CardInstance>& instances) {
    for (std::size_t index = 0; index < cards.size(); ++index) {
        instances.at(cards[index]).sequence = index;
    }
}

} // namespace

CardCatalog make_synthetic_product_catalog() {
    CardCatalog catalog;
    catalog.add(follower(synthetic::kFollower, 2, 2));
    CardDefinition token = follower(synthetic::kToken, 3, 3, mask(Keyword::Ward));
    token.availability = CardAvailability::Token;
    token.can_advance = false;
    catalog.add(std::move(token));
    catalog.add(follower(synthetic::kBarrierFollower, 2, 4, mask(Keyword::Barrier)));
    catalog.add(follower(synthetic::kBaneFollower, 1, 3, mask(Keyword::Bane)));
    catalog.add(follower(synthetic::kLifestealFollower, 3, 4, mask(Keyword::Lifesteal)));
    catalog.add(follower(synthetic::kRushFollower, 2, 2, mask(Keyword::Rush)));
    catalog.add(follower(synthetic::kStormFollower, 3, 3, mask(Keyword::Storm)));

    CardDefinition amulet = permanent(synthetic::kAmulet, CardKind::Amulet);
    amulet.countdown = 1;
    EffectSpec summon;
    summon.trigger = EffectTrigger::OnCountdownEnd;
    summon.kind = EffectKind::SummonToken;
    summon.amount = 1;
    summon.target = TargetSpec::Self;
    summon.parameter = std::string(synthetic::kToken);
    amulet.effects.push_back(std::move(summon));
    catalog.add(std::move(amulet));
    catalog.add(permanent(synthetic::kFieldA, CardKind::Field));
    catalog.add(permanent(synthetic::kFieldB, CardKind::Field));
    catalog.add(permanent(synthetic::kSpell, CardKind::Spell));
    catalog.add(permanent(synthetic::kTrap, CardKind::Trap));

    CardDefinition no_advance = follower(synthetic::kNoAdvanceFollower, 1, 1);
    no_advance.can_advance = false;
    catalog.add(std::move(no_advance));

    CardDefinition oath_follower = follower(synthetic::kOathFollower, 2, 3);
    oath_follower.identity.profession_id = "oathguard";
    oath_follower.identity.series_id = "luminous_oath";
    catalog.add(std::move(oath_follower));

    CardDefinition oath_spell = permanent(synthetic::kOathSpell, CardKind::Spell);
    oath_spell.identity.profession_id = "oathguard";
    oath_spell.identity.series_id = "luminous_oath";
    catalog.add(std::move(oath_spell));

    CardDefinition oath_amulet = permanent(synthetic::kOathAmulet, CardKind::Amulet);
    oath_amulet.identity.profession_id = "oathguard";
    oath_amulet.identity.series_id = "luminous_oath";
    oath_amulet.countdown = 2;
    catalog.add(std::move(oath_amulet));

    CardDefinition other_spell = permanent(synthetic::kOtherSpell, CardKind::Spell);
    other_spell.identity.profession_id = "pactmage";
    other_spell.identity.series_id = "abyssal_pact";
    catalog.add(std::move(other_spell));

    CardDefinition modal_spell = permanent(synthetic::kModalSpell, CardKind::Spell);
    modal_spell.modes = {
        ModeSpec{"repair", "repair", {}, TargetSpec::None, {}},
        ModeSpec{"empower", "empower", {}, TargetSpec::None, {}},
    };
    catalog.add(std::move(modal_spell));

    CardDefinition standby = follower(synthetic::kStandbyFollower, 4, 4);
    standby.availability = CardAvailability::Standby;
    standby.can_advance = false;
    standby.standby = StandbySpec{};
    standby.standby->pp_cost = 2;
    standby.standby->conditions.push_back(ConditionSpec{
        ConditionKind::CracksAtLeast,
        "cracks_at_least",
        4,
        0,
        {},
        {},
    });
    standby.standby->requires_additional_cost = true;
    standby.standby->additional_cost_target = TargetSpec::FriendlyPermanent;
    standby.standby->additional_cost_filter.allowed_kinds = {
        CardKind::Follower,
        CardKind::Amulet,
    };
    standby.standby->additional_cost_filter.series_id = "luminous_oath";
    standby.standby->additional_cost_filter.include_field = false;
    standby.standby->additional_cost_minimum = 1;
    standby.standby->additional_cost_maximum = 1;
    catalog.add(std::move(standby));
    return catalog;
}

ProductBoard::ProductBoard(CardCatalog catalog) : catalog_(std::move(catalog)) {}

InstanceId ProductBoard::create_instance(
    const std::string_view design_id,
    const PlayerId owner,
    const Zone initial_zone,
    const MoveReason reason) {
    if (!is_valid_player(owner)) {
        throw std::invalid_argument("invalid product-card owner");
    }
    const CardDefinition& definition = catalog_.at(design_id);
    if (initial_zone == Zone::MainBoard || initial_zone == Zone::Tactic || initial_zone == Zone::Field) {
        throw std::invalid_argument("slotted product zones require an explicit placement operation");
    }

    CardInstance card;
    card.id = next_instance_id_++;
    card.design_id = definition.identity.design_id;
    card.owner = owner;
    card.controller = owner;
    card.zone = initial_zone;
    card.current_attack = definition.attack;
    card.current_health = definition.health;
    card.maximum_health = definition.health;
    card.countdown = definition.countdown;
    card.keywords.printed = definition.printed_keywords;
    const InstanceId id = card.id;
    instances_.emplace(id, std::move(card));

    PlayerState& state = players_[to_index(owner)];
    switch (initial_zone) {
        case Zone::Deck: attach_vector(state.deck, id); break;
        case Zone::Hand: attach_vector(state.hand, id); break;
        case Zone::Graveyard: attach_vector(state.graveyard, id); break;
        case Zone::Archive: attach_vector(state.archive, id); break;
        case Zone::Standby: attach_vector(state.standby, id); break;
        case Zone::None: break;
        case Zone::MainBoard:
        case Zone::Tactic:
        case Zone::Field:
            break;
    }
    if (initial_zone != Zone::None) {
        record_move(id, Zone::None, initial_zone, reason, false);
    }
    return id;
}

Status ProductBoard::place_main(
    const PlayerId player_id,
    const InstanceId card_id,
    const std::size_t slot,
    const MoveReason reason) {
    const Status controlled = ensure_controller(player_id, card_id);
    if (!controlled) {
        return controlled;
    }
    if (slot >= kMainBoardSize) {
        return Status::error(ErrorCode::InvalidSlot, "main-board slot is out of range");
    }
    const CardDefinition& definition = catalog_.at(instances_.at(card_id).design_id);
    if (definition.kind != CardKind::Follower && definition.kind != CardKind::Amulet) {
        return Status::error(ErrorCode::InvalidKind, "only followers and amulets use the main board");
    }
    PlayerState& state = players_[to_index(player_id)];
    if (std::all_of(state.main_board.begin(), state.main_board.end(), [](const auto& occupied) {
            return occupied.has_value();
        })) {
        return Status::error(ErrorCode::MainBoardFull, "all five mixed main-board slots are occupied");
    }
    if (state.main_board[slot].has_value()) {
        return Status::error(ErrorCode::SlotOccupied, "main-board slot is occupied");
    }
    if (reservations_[to_index(player_id)][slot].has_value()) {
        return Status::error(ErrorCode::SlotReserved, "main-board slot is reserved by a resolving effect");
    }
    const Zone from = instances_.at(card_id).zone;
    detach(card_id);
    state.main_board[slot] = card_id;
    CardInstance& card = instances_.at(card_id);
    card.controller = player_id;
    card.zone = Zone::MainBoard;
    card.sequence = slot;
    card.countdown = definition.countdown;
    card.entered_this_turn = reason != MoveReason::ScenarioSetup;
    card.attacked_this_turn = false;
    record_move(card_id, from, Zone::MainBoard, reason, false);
    return Status::ok();
}

Status ProductBoard::place_tactic(
    const PlayerId player_id,
    const InstanceId card_id,
    const std::size_t slot,
    const MoveReason reason) {
    const Status controlled = ensure_controller(player_id, card_id);
    if (!controlled) {
        return controlled;
    }
    if (slot >= kStrategyZoneSize) {
        return Status::error(ErrorCode::InvalidSlot, "strategy slot is out of range");
    }
    const CardDefinition& definition = catalog_.at(instances_.at(card_id).design_id);
    if (definition.kind != CardKind::Spell && definition.kind != CardKind::Trap) {
        return Status::error(ErrorCode::InvalidKind, "only pending spells and traps use strategy slots");
    }
    PlayerState& state = players_[to_index(player_id)];
    if (state.tactics[slot].has_value()) {
        return Status::error(ErrorCode::SlotOccupied, "strategy slot is occupied");
    }
    const Zone from = instances_.at(card_id).zone;
    detach(card_id);
    state.tactics[slot] = card_id;
    CardInstance& card = instances_.at(card_id);
    card.controller = player_id;
    card.zone = Zone::Tactic;
    card.sequence = slot;
    record_move(card_id, from, Zone::Tactic, reason, false);
    return Status::ok();
}

Status ProductBoard::play_field(const PlayerId player_id, const InstanceId card_id) {
    const Status controlled = ensure_controller(player_id, card_id);
    if (!controlled) {
        return controlled;
    }
    const CardDefinition& definition = catalog_.at(instances_.at(card_id).design_id);
    if (definition.kind != CardKind::Field) {
        return Status::error(ErrorCode::InvalidKind, "only field cards use the field zone");
    }
    PlayerState& state = players_[to_index(player_id)];
    if (state.field.has_value() && *state.field != card_id) {
        const Status replaced = move_to_graveyard(*state.field, MoveReason::FieldReplaced, false);
        if (!replaced) {
            return replaced;
        }
    }
    const Zone from = instances_.at(card_id).zone;
    detach(card_id);
    state.field = card_id;
    CardInstance& card = instances_.at(card_id);
    card.controller = player_id;
    card.zone = Zone::Field;
    card.sequence = 0;
    record_move(card_id, from, Zone::Field, MoveReason::Played, false);
    return Status::ok();
}

Status ProductBoard::move_to_graveyard(
    const InstanceId card_id,
    const MoveReason reason,
    const bool destroyed) {
    const Status valid = ensure_card(card_id);
    if (!valid) {
        return valid;
    }
    if (destroyed != (reason == MoveReason::Destroyed || reason == MoveReason::CountdownExpired)) {
        return Status::error(ErrorCode::InvalidZone, "destroyed move semantic does not match its reason");
    }
    CardInstance& card = instances_.at(card_id);
    const Zone from = card.zone;
    const PlayerId controller = card.controller;
    // Standby availability is immutable provenance. A deployed standby card
    // leaves the normal card cycle even when its actual cause was destruction;
    // retain that cause independently from the final destination.
    const Zone destination = catalog_.at(card.design_id).availability == CardAvailability::Standby
        ? Zone::Archive : Zone::Graveyard;
    detach(card_id);
    card.zone = destination;
    attach_vector(destination == Zone::Archive
        ? players_[to_index(controller)].archive
        : players_[to_index(controller)].graveyard, card_id);
    record_move(card_id, from, destination, reason, destroyed);
    return Status::ok();
}

Status ProductBoard::move_to_archive(const InstanceId card_id, const MoveReason reason) {
    const Status valid = ensure_card(card_id);
    if (!valid) {
        return valid;
    }
    CardInstance& card = instances_.at(card_id);
    const Zone from = card.zone;
    const PlayerId controller = card.controller;
    detach(card_id);
    card.zone = Zone::Archive;
    attach_vector(players_[to_index(controller)].archive, card_id);
    record_move(card_id, from, Zone::Archive, reason, false);
    return Status::ok();
}

Status ProductBoard::move_hand_card_to_deck_bottom(
    const PlayerId player_id,
    const InstanceId card_id) {
    const Status controlled = ensure_controller(player_id, card_id);
    if (!controlled) {
        return controlled;
    }
    CardInstance& card = instances_.at(card_id);
    if (card.zone != Zone::Hand) {
        return Status::error(ErrorCode::InvalidZone, "deck-bottom card is not in hand");
    }
    detach(card_id);
    PlayerState& state = players_[to_index(player_id)];
    state.deck.insert(state.deck.begin(), card_id);
    card.zone = Zone::Deck;
    normalize(state.deck, instances_);
    record_move(card_id, Zone::Hand, Zone::Deck, MoveReason::ReturnedToDeckBottom, false);
    return Status::ok();
}

Status ProductBoard::discard_from_hand(const PlayerId player_id, const InstanceId card_id) {
    const Status controlled = ensure_controller(player_id, card_id);
    if (!controlled) {
        return controlled;
    }
    if (instances_.at(card_id).zone != Zone::Hand) {
        return Status::error(ErrorCode::InvalidZone, "discarded card is not in hand");
    }
    return move_to_graveyard(card_id, MoveReason::Discarded, false);
}

Status ProductBoard::put_deck_cards_on_bottom(
    const PlayerId player_id,
    const std::span<const InstanceId> cards,
    const bool randomize,
    const std::uint64_t seed) {
    if (!is_valid_player(player_id)) {
        return Status::error(ErrorCode::InvalidPlayer, "invalid deck-bottom player");
    }
    std::unordered_set<InstanceId> unique;
    std::vector<InstanceId> ordered(cards.begin(), cards.end());
    for (const InstanceId card_id : ordered) {
        const Status controlled = ensure_controller(player_id, card_id);
        if (!controlled) {
            return controlled;
        }
        if (instances_.at(card_id).zone != Zone::Deck) {
            return Status::error(ErrorCode::InvalidZone, "deck-bottom selection is not in deck");
        }
        if (!unique.insert(card_id).second) {
            return Status::error(ErrorCode::DuplicateSelection, "deck-bottom selection contains a duplicate");
        }
    }
    for (const InstanceId card_id : ordered) {
        detach(card_id);
    }
    if (randomize) {
        std::mt19937_64 generator(seed);
        std::shuffle(ordered.begin(), ordered.end(), generator);
    }
    PlayerState& state = players_[to_index(player_id)];
    state.deck.insert(state.deck.begin(), ordered.begin(), ordered.end());
    normalize(state.deck, instances_);
    for (const InstanceId card_id : ordered) {
        instances_.at(card_id).zone = Zone::Deck;
        record_move(card_id, Zone::Deck, Zone::Deck, MoveReason::ReturnedToDeckBottom, false);
    }
    return Status::ok();
}

std::vector<InstanceId> ProductBoard::reveal_top_matching(
    const PlayerId player_id,
    const std::size_t count,
    const CardFilter& filter) const {
    const PlayerState& state = player(player_id);
    std::vector<InstanceId> result;
    const std::size_t viewed = std::min(count, state.deck.size());
    result.reserve(viewed);
    for (std::size_t offset = 0; offset < viewed; ++offset) {
        const InstanceId card_id = state.deck[state.deck.size() - 1U - offset];
        if (filter.matches(catalog_.at(instances_.at(card_id).design_id))) {
            result.push_back(card_id);
        }
    }
    return result;
}

std::vector<InstanceId> ProductBoard::reveal_top(
    const PlayerId player_id,
    const std::size_t count) const {
    const PlayerState& state = player(player_id);
    const std::size_t viewed = std::min(count, state.deck.size());
    std::vector<InstanceId> result;
    result.reserve(viewed);
    for (std::size_t offset = 0; offset < viewed; ++offset) {
        result.push_back(state.deck[state.deck.size() - 1U - offset]);
    }
    return result;
}

DrawResult ProductBoard::draw_one(const PlayerId player_id) {
    PlayerState& state = player(player_id);
    if (state.deck.empty()) {
        return DrawResult{std::nullopt, false, true};
    }
    const InstanceId card_id = state.deck.back();
    detach(card_id);
    CardInstance& card = instances_.at(card_id);
    if (state.hand.size() >= 9U) {
        card.zone = Zone::Archive;
        attach_vector(state.archive, card_id);
        record_move(card_id, Zone::Deck, Zone::Archive, MoveReason::HandOverflow, false);
        return DrawResult{card_id, false, false};
    }
    card.zone = Zone::Hand;
    attach_vector(state.hand, card_id);
    record_move(card_id, Zone::Deck, Zone::Hand, MoveReason::Drawn, false);
    return DrawResult{card_id, true, false};
}

Status ProductBoard::exchange_mulligan(
    const PlayerId player_id,
    const std::span<const InstanceId> selected_cards,
    const bool shuffle,
    const std::uint64_t seed,
    std::vector<DrawResult>& replacements) {
    if (!is_valid_player(player_id)) {
        return Status::error(ErrorCode::InvalidPlayer, "invalid mulligan player");
    }
    PlayerState& state = players_[to_index(player_id)];
    if (state.deck.size() < selected_cards.size()) {
        return Status::error(ErrorCode::InvalidChoice, "not enough cards remain for mulligan replacements");
    }
    std::unordered_set<InstanceId> unique;
    for (const InstanceId card_id : selected_cards) {
        const Status controlled = ensure_controller(player_id, card_id);
        if (!controlled) {
            return controlled;
        }
        if (instances_.at(card_id).zone != Zone::Hand || !unique.insert(card_id).second) {
            return Status::error(ErrorCode::InvalidChoice, "mulligan card is invalid or duplicated");
        }
    }

    for (const InstanceId card_id : selected_cards) {
        detach(card_id);
        instances_.at(card_id).zone = Zone::None;
    }
    replacements.clear();
    replacements.reserve(selected_cards.size());
    for (std::size_t index = 0; index < selected_cards.size(); ++index) {
        replacements.push_back(draw_one(player_id));
    }

    state.deck.insert(state.deck.begin(), selected_cards.begin(), selected_cards.end());
    for (const InstanceId card_id : selected_cards) {
        CardInstance& card = instances_.at(card_id);
        card.controller = player_id;
        card.zone = Zone::Deck;
        record_move(card_id, Zone::Hand, Zone::Deck, MoveReason::ReturnedToDeckBottom, false);
    }
    if (shuffle) {
        std::mt19937_64 generator(seed);
        std::shuffle(state.deck.begin(), state.deck.end(), generator);
    }
    normalize(state.deck, instances_);
    return Status::ok();
}

DrawThenBottomResult ProductBoard::draw_then_prepare_bottom(const PlayerId player_id) {
    DrawThenBottomResult result;
    result.draw = draw_one(player_id);
    if (result.draw.entered_hand) {
        result.bottom_candidates = player(player_id).hand;
    }
    return result;
}

Status ProductBoard::move_deck_card_to_hand(
    const PlayerId player_id,
    const InstanceId card_id) {
    const Status controlled = ensure_controller(player_id, card_id);
    if (!controlled) {
        return controlled;
    }
    CardInstance& card = instances_.at(card_id);
    if (card.zone != Zone::Deck) {
        return Status::error(ErrorCode::InvalidZone, "searched card is not in deck");
    }
    detach(card_id);
    PlayerState& state = players_[to_index(player_id)];
    if (state.hand.size() >= 9U) {
        card.zone = Zone::Archive;
        attach_vector(state.archive, card_id);
        record_move(card_id, Zone::Deck, Zone::Archive, MoveReason::HandOverflow, false);
        return Status::ok();
    }
    card.zone = Zone::Hand;
    attach_vector(state.hand, card_id);
    record_move(card_id, Zone::Deck, Zone::Hand, MoveReason::Drawn, false);
    return Status::ok();
}

std::vector<InstanceId> ProductBoard::list_permanents(
    const PlayerId controller,
    const PermanentFilter& filter) const {
    const PlayerState& state = player(controller);
    std::vector<InstanceId> result;
    if (filter.include_main_board) {
        for (const auto& slot : state.main_board) {
            if (slot.has_value() && filter.matches(catalog_.at(instances_.at(*slot).design_id))) {
                result.push_back(*slot);
            }
        }
    }
    if (filter.include_field && state.field.has_value() &&
        filter.matches(catalog_.at(instances_.at(*state.field).design_id))) {
        result.push_back(*state.field);
    }
    return result;
}

Status ProductBoard::validate_permanent_target(
    const PlayerId acting_player,
    const InstanceId target,
    const bool friendly,
    const PermanentFilter& filter) const {
    if (!is_valid_player(acting_player)) {
        return Status::error(ErrorCode::InvalidPlayer, "invalid permanent-target player");
    }
    const Status valid = ensure_card(target);
    if (!valid) {
        return valid;
    }
    const CardInstance& instance = instances_.at(target);
    if ((instance.controller == acting_player) != friendly) {
        return Status::error(ErrorCode::InvalidCard, "permanent has the wrong controller relation");
    }
    const bool in_main = instance.zone == Zone::MainBoard && filter.include_main_board;
    const bool in_field = instance.zone == Zone::Field && filter.include_field;
    if ((!in_main && !in_field) || !filter.matches(catalog_.at(instance.design_id))) {
        return Status::error(ErrorCode::InvalidKind, "card is not a legal permanent target");
    }
    return Status::ok();
}

Status ProductBoard::validate_optional_enemy_follower_target(
    const PlayerId acting_player,
    const std::optional<InstanceId> target) const {
    if (!target.has_value()) {
        return Status::ok();
    }
    PermanentFilter follower;
    follower.allowed_kinds = {CardKind::Follower};
    follower.include_field = false;
    return validate_permanent_target(acting_player, *target, false, follower);
}

Status ProductBoard::destroy_permanent(const InstanceId target) {
    const Status valid = ensure_card(target);
    if (!valid) {
        return valid;
    }
    const CardInstance& instance = instances_.at(target);
    const CardKind kind = catalog_.at(instance.design_id).kind;
    if ((instance.zone != Zone::MainBoard && instance.zone != Zone::Field) ||
        (kind != CardKind::Follower && kind != CardKind::Amulet && kind != CardKind::Field)) {
        return Status::error(ErrorCode::InvalidKind, "destroy target is not a battlefield permanent");
    }
    return move_to_graveyard(target, MoveReason::Destroyed, true);
}

Status ProductBoard::pay_additional_archive_cost(
    const PlayerId player_id,
    const InstanceId target,
    const PermanentFilter& filter) {
    const Status target_status = validate_permanent_target(player_id, target, true, filter);
    if (!target_status) {
        return target_status;
    }
    return move_to_archive(target, MoveReason::AdditionalCost);
}

Status ProductBoard::validate_payable(
    const std::string_view design_id,
    const CardAvailability availability) const {
    if (!catalog_.contains(design_id)) {
        return Status::error(ErrorCode::InvalidCard, "unknown payable card definition");
    }
    const CardDefinition& definition = catalog_.at(design_id);
    if (!definition.is_executable()) {
        return Status::error(
            ErrorCode::InvalidCard,
            "card definition is locked_not_implemented and cannot be paid or enumerated");
    }
    if (definition.availability != availability || availability == CardAvailability::Token) {
        return Status::error(ErrorCode::InvalidZone, "card is not payable from the requested source");
    }
    return Status::ok();
}

std::vector<DesignId> ProductBoard::list_payable_definitions(
    const CardAvailability availability) const {
    if (availability == CardAvailability::Token) {
        return {};
    }
    return catalog_.list_executable(availability);
}

Status ProductBoard::validate_advance(const std::string_view design_id, const bool use_advance) const {
    if (!catalog_.contains(design_id)) {
        return Status::error(ErrorCode::InvalidCard, "unknown advance card definition");
    }
    const CardAvailability availability = catalog_.at(design_id).availability;
    const Status payable = validate_payable(design_id, availability);
    if (!payable) {
        return payable;
    }
    if (use_advance && !catalog_.at(design_id).can_advance) {
        return Status::error(ErrorCode::InvalidChoice, "card definition forbids advance");
    }
    return Status::ok();
}

Status ProductBoard::validate_mode(
    const std::string_view design_id,
    const std::optional<std::string_view> mode_id) const {
    if (!catalog_.contains(design_id)) {
        return Status::error(ErrorCode::InvalidCard, "unknown modal card definition");
    }
    const CardAvailability availability = catalog_.at(design_id).availability;
    const Status payable = validate_payable(design_id, availability);
    if (!payable) {
        return payable;
    }
    const auto& modes = catalog_.at(design_id).modes;
    if (modes.empty()) {
        return mode_id.has_value()
            ? Status::error(ErrorCode::InvalidChoice, "non-modal card received a mode")
            : Status::ok();
    }
    if (!mode_id.has_value()) {
        return Status::error(ErrorCode::InvalidChoice, "modal card requires a mode");
    }
    const bool found = std::any_of(modes.begin(), modes.end(), [&](const ModeSpec& mode) {
        return mode.mode_id == *mode_id;
    });
    return found ? Status::ok() : Status::error(ErrorCode::InvalidChoice, "mode is not printed on this card");
}

Status ProductBoard::validate_standby(
    const std::string_view design_id,
    const ConditionEvaluationContext& context) const {
    if (!catalog_.contains(design_id)) {
        return Status::error(ErrorCode::InvalidCard, "unknown standby card definition");
    }
    const CardDefinition& definition = catalog_.at(design_id);
    if (definition.availability != CardAvailability::Standby || !definition.standby.has_value()) {
        return Status::error(ErrorCode::InvalidKind, "card is not a standby definition");
    }
    const Status payable = validate_payable(design_id, CardAvailability::Standby);
    if (!payable) {
        return payable;
    }
    for (const ConditionSpec& condition : definition.standby->conditions) {
        if (!evaluate_condition(condition, context)) {
            return Status::error(ErrorCode::InvalidChoice, "standby condition is not satisfied");
        }
    }
    return Status::ok();
}

Status ProductBoard::grant_permanent_stats(
    const InstanceId card_id,
    const int attack,
    const int health) {
    const Status valid = ensure_card(card_id);
    if (!valid) {
        return valid;
    }
    CardInstance& card = instances_.at(card_id);
    if (catalog_.at(card.design_id).kind != CardKind::Follower || card.zone != Zone::MainBoard ||
        card.maximum_health + health <= 0) {
        return Status::error(ErrorCode::InvalidKind, "permanent stat target must be a living follower");
    }
    card.permanent_attack_bonus += attack;
    card.permanent_health_bonus += health;
    card.current_attack += attack;
    card.maximum_health += health;
    card.current_health += health;
    return Status::ok();
}

Status ProductBoard::grant_temporary_attack(const InstanceId card_id, const int attack) {
    const Status valid = ensure_card(card_id);
    if (!valid) {
        return valid;
    }
    CardInstance& card = instances_.at(card_id);
    if (catalog_.at(card.design_id).kind != CardKind::Follower || card.zone != Zone::MainBoard) {
        return Status::error(ErrorCode::InvalidKind, "temporary attack target must be a follower");
    }
    card.turn_attack_bonus += attack;
    card.current_attack += attack;
    return Status::ok();
}

Status ProductBoard::grant_permanent_keyword(const InstanceId card_id, const Keyword keyword) {
    const Status valid = ensure_card(card_id);
    if (!valid) {
        return valid;
    }
    CardInstance& card = instances_.at(card_id);
    if (catalog_.at(card.design_id).kind != CardKind::Follower || card.zone != Zone::MainBoard) {
        return Status::error(ErrorCode::InvalidKind, "permanent keyword target must be a follower");
    }
    card.keywords.grant_permanent(keyword);
    return Status::ok();
}

DamageResult ProductBoard::damage_follower(const InstanceId card_id, const int amount) {
    const Status valid = ensure_card(card_id);
    if (!valid || catalog_.at(instances_.at(card_id).design_id).kind != CardKind::Follower ||
        instances_.at(card_id).zone != Zone::MainBoard) {
        throw std::invalid_argument("effect damage target must be a battlefield follower");
    }
    DamageResult result = deal_positive_damage(instances_.at(card_id), amount);
    if (instances_.at(card_id).current_health <= 0) {
        const Status moved = move_to_graveyard(card_id, MoveReason::Destroyed, true);
        if (!moved) {
            throw std::logic_error(moved.message);
        }
    }
    return result;
}

int ProductBoard::heal_leader(const PlayerId player_id, const int amount) {
    PlayerState& state = player(player_id);
    const int actual = std::min(std::max(0, amount), state.maximum_leader_health - state.leader_health);
    state.leader_health += actual;
    return actual;
}

Status ProductBoard::change_countdown(const InstanceId card_id, const int delta) {
    const Status valid = ensure_card(card_id);
    if (!valid) {
        return valid;
    }
    CardInstance& card = instances_.at(card_id);
    if (catalog_.at(card.design_id).kind != CardKind::Amulet || card.zone != Zone::MainBoard) {
        return Status::error(ErrorCode::InvalidKind, "countdown target must be a battlefield amulet");
    }
    card.countdown = std::max(0, card.countdown + delta);
    return Status::ok();
}

Status ProductBoard::expire_amulet_and_reserve(
    const InstanceId amulet_id,
    const ResolutionFrameId frame_id) {
    const Status valid = ensure_card(amulet_id);
    if (!valid) {
        return valid;
    }
    CardInstance& amulet = instances_.at(amulet_id);
    if (catalog_.at(amulet.design_id).kind != CardKind::Amulet) {
        return Status::error(ErrorCode::InvalidKind, "countdown expiry requires an amulet");
    }
    if (amulet.zone != Zone::MainBoard || amulet.countdown > 1 || frame_id == 0) {
        return Status::error(ErrorCode::InvalidZone, "amulet is not ready for a reserved countdown expiry");
    }
    const std::optional<std::size_t> slot = main_slot_of(amulet_id);
    if (!slot.has_value()) {
        return Status::error(ErrorCode::InvalidZone, "amulet is missing from its main-board slot");
    }
    auto& reservation = reservations_[to_index(amulet.controller)][*slot];
    if (reservation.has_value()) {
        return Status::error(ErrorCode::SlotReserved, "countdown slot is already reserved");
    }
    amulet.countdown = 0;
    reservation = frame_id;
    const Status moved = move_to_graveyard(amulet_id, MoveReason::CountdownExpired, true);
    if (!moved) {
        reservation.reset();
        return moved;
    }
    // Moving a card does not clear the frame reservation.
    return Status::ok();
}

Status ProductBoard::summon_token_in_reserved_slot(
    const PlayerId player_id,
    const std::string_view token_design_id,
    const std::size_t slot,
    const ResolutionFrameId frame_id,
    InstanceId& out_token) {
    if (!is_valid_player(player_id)) {
        return Status::error(ErrorCode::InvalidPlayer, "invalid token controller");
    }
    if (slot >= kMainBoardSize) {
        return Status::error(ErrorCode::InvalidSlot, "reserved token slot is out of range");
    }
    const auto& reservation = reservations_[to_index(player_id)][slot];
    if (!reservation.has_value() || *reservation != frame_id) {
        return Status::error(ErrorCode::SlotReserved, "resolution frame does not own the token slot");
    }
    if (players_[to_index(player_id)].main_board[slot].has_value()) {
        return Status::error(ErrorCode::SlotOccupied, "reserved token slot unexpectedly became occupied");
    }
    if (!catalog_.contains(token_design_id) || catalog_.at(token_design_id).kind != CardKind::Follower) {
        return Status::error(ErrorCode::InvalidKind, "reserved countdown result must be a follower token");
    }

    const InstanceId token = create_instance(token_design_id, player_id);
    PlayerState& state = players_[to_index(player_id)];
    state.main_board[slot] = token;
    CardInstance& card = instances_.at(token);
    card.zone = Zone::MainBoard;
    card.sequence = slot;
    card.entered_this_turn = true;
    reservations_[to_index(player_id)][slot].reset();
    record_move(token, Zone::None, Zone::MainBoard, MoveReason::TokenSummoned, false);
    out_token = token;
    return Status::ok();
}

void ProductBoard::release_reservations(const ResolutionFrameId frame_id) noexcept {
    for (auto& player_reservations : reservations_) {
        for (auto& reservation : player_reservations) {
            if (reservation == frame_id) {
                reservation.reset();
            }
        }
    }
}

Status ProductBoard::validate_attack_source(const PlayerId player_id, const InstanceId card_id) const {
    const Status controlled = ensure_controller(player_id, card_id);
    if (!controlled) {
        return controlled;
    }
    const CardInstance& card = instances_.at(card_id);
    if (card.zone != Zone::MainBoard) {
        return Status::error(ErrorCode::InvalidZone, "attacker is not on the main board");
    }
    if (catalog_.at(card.design_id).kind != CardKind::Follower) {
        return Status::error(ErrorCode::InvalidKind, "amulets cannot attack");
    }
    if (card.attacked_this_turn) {
        return Status::error(ErrorCode::AlreadyAttacked, "follower has already attacked this turn");
    }
    return Status::ok();
}

Status ProductBoard::validate_attack_target(const PlayerId player_id, const InstanceId card_id) const {
    const Status valid = ensure_card(card_id);
    if (!valid) {
        return valid;
    }
    const CardInstance& card = instances_.at(card_id);
    if (card.controller == player_id || card.zone != Zone::MainBoard) {
        return Status::error(ErrorCode::InvalidZone, "attack target is not an opposing main-board card");
    }
    if (catalog_.at(card.design_id).kind != CardKind::Follower) {
        return Status::error(ErrorCode::InvalidKind, "amulets cannot be attacked");
    }
    return Status::ok();
}

Status ProductBoard::validate_attack(
    const PlayerId player_id,
    const InstanceId attacker_id,
    const std::optional<InstanceId> follower_target) const {
    const Status source = validate_attack_source(player_id, attacker_id);
    if (!source) {
        return source;
    }
    if (follower_target.has_value()) {
        const Status target = validate_attack_target(player_id, *follower_target);
        if (!target) {
            return target;
        }
    }
    const CardInstance& attacker = instances_.at(attacker_id);
    if (attacker.entered_this_turn) {
        if (!follower_target.has_value() && !attacker.keywords.has(Keyword::Storm)) {
            return Status::error(ErrorCode::InvalidChoice, "only storm can attack a leader on entry turn");
        }
        if (follower_target.has_value() &&
            !attacker.keywords.has(Keyword::Rush) && !attacker.keywords.has(Keyword::Storm)) {
            return Status::error(ErrorCode::InvalidChoice, "follower has summoning sickness");
        }
    }

    const PlayerId opponent = player_id == PlayerId::Player0 ? PlayerId::Player1 : PlayerId::Player0;
    bool ward_present = false;
    for (const auto& slot : players_[to_index(opponent)].main_board) {
        if (slot.has_value()) {
            const CardInstance& candidate = instances_.at(*slot);
            if (catalog_.at(candidate.design_id).kind == CardKind::Follower &&
                candidate.keywords.has(Keyword::Ward)) {
                ward_present = true;
                break;
            }
        }
    }
    if (ward_present &&
        (!follower_target.has_value() || !instances_.at(*follower_target).keywords.has(Keyword::Ward))) {
        return Status::error(ErrorCode::InvalidChoice, "an opposing ward must be attacked first");
    }
    return Status::ok();
}

Status ProductBoard::accept_attack_declaration(
    const PlayerId player_id,
    const InstanceId attacker_id,
    const std::optional<InstanceId> follower_target) {
    const Status validation = validate_attack(player_id, attacker_id, follower_target);
    if (!validation) {
        return validation;
    }
    // Match the frozen v0.4 ordering: the attack is spent as soon as the
    // declaration is accepted, before a response can cancel or suspend it.
    instances_.at(attacker_id).attacked_this_turn = true;
    return Status::ok();
}

Status ProductBoard::validate_evolve(const PlayerId player_id, const InstanceId card_id) const {
    const Status controlled = ensure_controller(player_id, card_id);
    if (!controlled) {
        return controlled;
    }
    const CardInstance& card = instances_.at(card_id);
    if (card.zone != Zone::MainBoard) {
        return Status::error(ErrorCode::InvalidZone, "evolution target is not on the main board");
    }
    if (catalog_.at(card.design_id).kind != CardKind::Follower) {
        return Status::error(ErrorCode::InvalidKind, "amulets cannot evolve");
    }
    return Status::ok();
}

CombatResult ProductBoard::resolve_follower_combat(
    const InstanceId attacker_id,
    const InstanceId defender_id) {
    const CardInstance attacker_before = instances_.at(attacker_id);
    if (!accept_attack_declaration(attacker_before.controller, attacker_id, defender_id)) {
        throw std::invalid_argument("invalid product follower combat");
    }
    return resolve_accepted_follower_combat(attacker_id, defender_id);
}

CombatResult ProductBoard::resolve_accepted_follower_combat(
    const InstanceId attacker_id,
    const InstanceId defender_id) {
    const CardInstance attacker_before = instances_.at(attacker_id);
    const CardInstance defender_before = instances_.at(defender_id);
    if (attacker_before.zone != Zone::MainBoard || defender_before.zone != Zone::MainBoard ||
        catalog_.at(attacker_before.design_id).kind != CardKind::Follower ||
        catalog_.at(defender_before.design_id).kind != CardKind::Follower ||
        attacker_before.controller == defender_before.controller ||
        !attacker_before.attacked_this_turn) {
        throw std::invalid_argument("invalid accepted product follower combat");
    }

    CardInstance& attacker = instances_.at(attacker_id);
    CardInstance& defender = instances_.at(defender_id);
    CombatResult result;
    result.damage_to_defender = deal_positive_damage(defender, attacker_before.current_attack);
    result.damage_to_attacker = deal_positive_damage(attacker, defender_before.current_attack);
    if (attacker_before.keywords.has(Keyword::Bane) && result.damage_to_defender.actual_damage > 0) {
        defender.current_health = 0;
    }
    if (defender_before.keywords.has(Keyword::Bane) && result.damage_to_attacker.actual_damage > 0) {
        attacker.current_health = 0;
    }
    // Product rule: only damage from the active attacker can lifesteal. A
    // defending follower's counterattack and effect damage never heal.
    if (attacker_before.keywords.has(Keyword::Lifesteal) && result.damage_to_defender.actual_damage > 0) {
        PlayerState& owner = players_[to_index(attacker_before.controller)];
        result.attacker_healed = std::min(
            result.damage_to_defender.actual_damage,
            owner.maximum_leader_health - owner.leader_health);
        owner.leader_health += result.attacker_healed;
    }
    result.attacker_destroyed = attacker.current_health <= 0;
    result.defender_destroyed = defender.current_health <= 0;
    result.attacker_killed_follower_and_survived = result.defender_destroyed && !result.attacker_destroyed;
    if (result.attacker_destroyed) {
        const Status moved = move_to_graveyard(attacker_id, MoveReason::Destroyed, true);
        if (!moved) {
            throw std::logic_error(moved.message);
        }
    }
    if (result.defender_destroyed) {
        const Status moved = move_to_graveyard(defender_id, MoveReason::Destroyed, true);
        if (!moved) {
            throw std::logic_error(moved.message);
        }
    }
    return result;
}

void ProductBoard::clear_turn_keyword_grants(const PlayerId player_id) noexcept {
    if (!is_valid_player(player_id)) {
        return;
    }
    for (const auto& slot : players_[to_index(player_id)].main_board) {
        if (slot.has_value()) {
            CardInstance& card = instances_.at(*slot);
            card.current_attack -= card.turn_attack_bonus;
            card.turn_attack_bonus = 0;
            card.keywords.clear_turn();
        }
    }
}

void ProductBoard::ready_starting_turn_permanents(const PlayerId player_id) noexcept {
    if (!is_valid_player(player_id)) {
        return;
    }
    for (const auto& slot : players_[to_index(player_id)].main_board) {
        if (slot.has_value()) {
            CardInstance& card = instances_.at(*slot);
            if (catalog_.at(card.design_id).kind == CardKind::Follower) {
                card.entered_this_turn = false;
                card.attacked_this_turn = false;
            }
        }
    }
}

const CardCatalog& ProductBoard::catalog() const noexcept { return catalog_; }

bool ProductBoard::contains_instance(const InstanceId card) const noexcept {
    return instances_.contains(card);
}

const CardInstance& ProductBoard::instance(const InstanceId card) const { return instances_.at(card); }

CardInstance& ProductBoard::instance(const InstanceId card) { return instances_.at(card); }

const PlayerState& ProductBoard::player(const PlayerId player_id) const {
    if (!is_valid_player(player_id)) {
        throw std::out_of_range("invalid product player");
    }
    return players_[to_index(player_id)];
}

PlayerState& ProductBoard::player(const PlayerId player_id) {
    if (!is_valid_player(player_id)) {
        throw std::out_of_range("invalid product player");
    }
    return players_[to_index(player_id)];
}

const std::vector<MoveRecord>& ProductBoard::moves() const noexcept { return moves_; }

std::optional<ResolutionFrameId> ProductBoard::reserved_by(
    const PlayerId player_id,
    const std::size_t slot) const {
    if (!is_valid_player(player_id) || slot >= kMainBoardSize) {
        return std::nullopt;
    }
    return reservations_[to_index(player_id)][slot];
}

std::size_t ProductBoard::main_board_count(const PlayerId player_id) const {
    const auto& board = player(player_id).main_board;
    return static_cast<std::size_t>(std::count_if(board.begin(), board.end(), [](const auto& slot) {
        return slot.has_value();
    }));
}

bool ProductBoard::field_is(const PlayerId player_id, const std::string_view design_id) const {
    const auto field = player(player_id).field;
    return field.has_value() && instances_.at(*field).design_id == design_id;
}

std::vector<std::string> ProductBoard::validate_invariants() const {
    std::vector<std::string> problems;
    std::unordered_set<InstanceId> seen;
    const auto see = [&](const InstanceId id, const Zone expected, const PlayerId controller, const std::size_t seq) {
        if (!instances_.contains(id)) {
            problems.push_back("zone contains an unknown product instance");
            return;
        }
        if (!seen.insert(id).second) {
            problems.push_back("product instance appears in more than one zone");
        }
        const CardInstance& card = instances_.at(id);
        if (card.zone != expected || card.controller != controller || card.sequence != seq) {
            problems.push_back("product instance zone/controller/sequence mismatch");
        }
    };

    for (std::size_t player_index = 0; player_index < kPlayerCount; ++player_index) {
        const PlayerId player_id = static_cast<PlayerId>(player_index);
        const PlayerState& state = players_[player_index];
        for (std::size_t slot = 0; slot < kMainBoardSize; ++slot) {
            if (state.main_board[slot].has_value()) {
                see(*state.main_board[slot], Zone::MainBoard, player_id, slot);
                const CardKind kind = catalog_.at(instances_.at(*state.main_board[slot]).design_id).kind;
                if (kind != CardKind::Follower && kind != CardKind::Amulet) {
                    problems.push_back("non-follower/non-amulet occupies the main board");
                }
                if (reservations_[player_index][slot].has_value()) {
                    problems.push_back("reserved main-board slot is occupied");
                }
            }
        }
        for (std::size_t slot = 0; slot < kStrategyZoneSize; ++slot) {
            if (state.tactics[slot].has_value()) {
                see(*state.tactics[slot], Zone::Tactic, player_id, slot);
            }
        }
        if (state.field.has_value()) {
            see(*state.field, Zone::Field, player_id, 0);
            if (catalog_.at(instances_.at(*state.field).design_id).kind != CardKind::Field) {
                problems.push_back("non-field occupies the field zone");
            }
        }
        const auto see_vector = [&](const std::vector<InstanceId>& cards, const Zone zone) {
            for (std::size_t index = 0; index < cards.size(); ++index) {
                see(cards[index], zone, player_id, index);
            }
        };
        see_vector(state.deck, Zone::Deck);
        see_vector(state.hand, Zone::Hand);
        see_vector(state.graveyard, Zone::Graveyard);
        see_vector(state.archive, Zone::Archive);
        see_vector(state.standby, Zone::Standby);
    }
    for (const auto& [id, card] : instances_) {
        if (card.zone != Zone::None && !seen.contains(id)) {
            problems.push_back("product instance names a zone but is absent from it");
        }
    }
    return problems;
}

Status ProductBoard::ensure_card(const InstanceId card) const {
    if (!instances_.contains(card)) {
        return Status::error(ErrorCode::InvalidCard, "unknown product card instance");
    }
    return Status::ok();
}

Status ProductBoard::ensure_controller(const PlayerId player_id, const InstanceId card) const {
    if (!is_valid_player(player_id)) {
        return Status::error(ErrorCode::InvalidPlayer, "invalid product player");
    }
    const Status valid = ensure_card(card);
    if (!valid) {
        return valid;
    }
    if (instances_.at(card).controller != player_id) {
        return Status::error(ErrorCode::InvalidCard, "product card is not controlled by this player");
    }
    return Status::ok();
}

std::optional<std::size_t> ProductBoard::main_slot_of(const InstanceId card) const {
    if (!instances_.contains(card)) {
        return std::nullopt;
    }
    const CardInstance& instance = instances_.at(card);
    if (!is_valid_player(instance.controller)) {
        return std::nullopt;
    }
    const auto& board = players_[to_index(instance.controller)].main_board;
    for (std::size_t slot = 0; slot < board.size(); ++slot) {
        if (board[slot] == card) {
            return slot;
        }
    }
    return std::nullopt;
}

void ProductBoard::detach(const InstanceId card_id) {
    CardInstance& card = instances_.at(card_id);
    PlayerState& state = players_[to_index(card.controller)];
    switch (card.zone) {
        case Zone::Deck: erase_card(state.deck, card_id); normalize(state.deck, instances_); break;
        case Zone::Hand: erase_card(state.hand, card_id); normalize(state.hand, instances_); break;
        case Zone::MainBoard:
            for (auto& slot : state.main_board) {
                if (slot == card_id) {
                    slot.reset();
                    break;
                }
            }
            break;
        case Zone::Tactic:
            for (auto& slot : state.tactics) {
                if (slot == card_id) {
                    slot.reset();
                    break;
                }
            }
            break;
        case Zone::Graveyard: erase_card(state.graveyard, card_id); normalize(state.graveyard, instances_); break;
        case Zone::Archive: erase_card(state.archive, card_id); normalize(state.archive, instances_); break;
        case Zone::Standby: erase_card(state.standby, card_id); normalize(state.standby, instances_); break;
        case Zone::Field:
            if (state.field == card_id) {
                state.field.reset();
            }
            break;
        case Zone::None: break;
    }
    card.zone = Zone::None;
    card.sequence = 0;
}

void ProductBoard::attach_vector(std::vector<InstanceId>& destination, const InstanceId card) {
    destination.push_back(card);
    instances_.at(card).sequence = destination.size() - 1;
}

void ProductBoard::record_move(
    const InstanceId card,
    const Zone from,
    const Zone to,
    const MoveReason reason,
    const bool destroyed) {
    moves_.push_back(MoveRecord{card, instances_.at(card).controller, from, to, reason, destroyed});
}

DamageResult ProductBoard::deal_positive_damage(CardInstance& target, const int amount) {
    if (amount <= 0) {
        return {};
    }
    if (target.keywords.consume(Keyword::Barrier)) {
        return DamageResult{0, true};
    }
    const int actual = std::min(amount, std::max(0, target.current_health));
    target.current_health -= actual;
    return DamageResult{actual, false};
}

bool evaluate_condition(
    const ConditionSpec& condition,
    const ConditionEvaluationContext& context) noexcept {
    const int cracks = condition.read_cap > 0
        ? std::min(context.cracks, condition.read_cap)
        : context.cracks;
    switch (condition.kind) {
        case ConditionKind::Always: return true;
        case ConditionKind::CracksAtLeast: return cracks >= condition.threshold;
        case ConditionKind::CracksAtMost: return cracks <= condition.threshold;
        case ConditionKind::Advanced: return context.advanced;
        case ConditionKind::OnTime: return !context.advanced;
        case ConditionKind::ActualRepairAtLeast: return context.actual_repair >= condition.threshold;
        case ConditionKind::RepairToZero: return context.repaired_to_zero;
        case ConditionKind::FutureUseAtLeast: return context.future_use_amount >= condition.threshold;
        case ConditionKind::TurnRepairAtLeast: return context.turn.actual_repaired >= condition.threshold;
        case ConditionKind::TurnFutureUseAtLeast: return context.turn.future_cracks_added >= condition.threshold;
        case ConditionKind::TurnBarrierGranted:
            if (condition.permanent_filter.allowed_kinds.empty() &&
                condition.permanent_filter.profession_id.empty() &&
                condition.permanent_filter.series_id.empty()) {
                return context.turn.barrier_granted;
            }
            return std::any_of(context.turn.barrier_sources.begin(), context.turn.barrier_sources.end(),
                [&](const ProductTurnHistory::PermanentRecord& record) {
                    return (condition.permanent_filter.allowed_kinds.empty() ||
                            std::find(condition.permanent_filter.allowed_kinds.begin(),
                                condition.permanent_filter.allowed_kinds.end(), record.kind) !=
                                condition.permanent_filter.allowed_kinds.end()) &&
                        (condition.permanent_filter.profession_id.empty() ||
                            condition.permanent_filter.profession_id == record.profession_id) &&
                        (condition.permanent_filter.series_id.empty() ||
                            condition.permanent_filter.series_id == record.series_id);
                });
        case ConditionKind::TurnCountdownExpired:
            if (condition.permanent_filter.allowed_kinds.empty() &&
                condition.permanent_filter.profession_id.empty() &&
                condition.permanent_filter.series_id.empty()) {
                return context.turn.countdown_expired >= condition.threshold;
            }
            return static_cast<int>(std::count_if(
                context.turn.countdown_sources.begin(), context.turn.countdown_sources.end(),
                [&](const ProductTurnHistory::PermanentRecord& record) {
                    return (condition.permanent_filter.allowed_kinds.empty() ||
                            std::find(condition.permanent_filter.allowed_kinds.begin(),
                                condition.permanent_filter.allowed_kinds.end(), record.kind) !=
                                condition.permanent_filter.allowed_kinds.end()) &&
                        (condition.permanent_filter.profession_id.empty() ||
                            condition.permanent_filter.profession_id == record.profession_id) &&
                        (condition.permanent_filter.series_id.empty() ||
                            condition.permanent_filter.series_id == record.series_id);
                })) >= condition.threshold;
        case ConditionKind::MatchRepairToZeroAtLeast:
            return context.match.repair_to_zero_count >= condition.threshold;
        case ConditionKind::MatchCountdownExpiredAtLeast:
            if (condition.permanent_filter.allowed_kinds.empty() &&
                condition.permanent_filter.profession_id.empty() &&
                condition.permanent_filter.series_id.empty()) {
                return context.match.countdown_expired >= condition.threshold;
            }
            return static_cast<int>(std::count_if(
                context.match.countdown_sources.begin(), context.match.countdown_sources.end(),
                [&](const ProductTurnHistory::PermanentRecord& record) {
                    return (condition.permanent_filter.allowed_kinds.empty() ||
                            std::find(condition.permanent_filter.allowed_kinds.begin(),
                                condition.permanent_filter.allowed_kinds.end(), record.kind) !=
                                condition.permanent_filter.allowed_kinds.end()) &&
                        (condition.permanent_filter.profession_id.empty() ||
                            condition.permanent_filter.profession_id == record.profession_id) &&
                        (condition.permanent_filter.series_id.empty() ||
                            condition.permanent_filter.series_id == record.series_id);
                })) >= condition.threshold;
        case ConditionKind::LeaderHealthAtMost: return context.leader_health <= condition.threshold;
        case ConditionKind::BoardCountLessThanOpponent:
            return context.own_board_count < context.enemy_board_count;
        case ConditionKind::FieldIs: return context.field_design_id == condition.parameter;
        case ConditionKind::ControlsSeriesPermanent:
            return std::find(
                context.controlled_series.begin(), context.controlled_series.end(), condition.parameter) !=
                context.controlled_series.end();
    }
    return false;
}

ProductRuleState::PlayerRules& ProductRuleState::rules(const PlayerId player_id) {
    if (!is_valid_player(player_id)) {
        throw std::out_of_range("invalid product rule player");
    }
    return players_[to_index(player_id)];
}

const ProductRuleState::PlayerRules& ProductRuleState::rules(const PlayerId player_id) const {
    if (!is_valid_player(player_id)) {
        throw std::out_of_range("invalid product rule player");
    }
    return players_[to_index(player_id)];
}

void ProductRuleState::set_cracks(const PlayerId player_id, const int cracks_value) {
    rules(player_id).cracks = std::max(0, cracks_value);
}

int ProductRuleState::cracks(const PlayerId player_id) const { return rules(player_id).cracks; }

int ProductRuleState::cracks_capped(const PlayerId player_id, const int cap) const {
    return std::min(rules(player_id).cracks, std::max(0, cap));
}

RepairResult ProductRuleState::repair(const PlayerId player_id, const int amount) {
    PlayerRules& state = rules(player_id);
    RepairResult result;
    result.before = state.cracks;
    result.actual_repaired = std::min(std::max(0, amount), state.cracks);
    state.cracks -= result.actual_repaired;
    result.after = state.cracks;
    result.repaired_to_zero = result.actual_repaired > 0 && result.after == 0;
    state.turn.actual_repaired += result.actual_repaired;
    if (result.repaired_to_zero) {
        ++state.match.repair_to_zero_count;
    }
    append_event(player_id, ProductRuleEvent::Kind::Repair, result.actual_repaired, result.repaired_to_zero);
    maybe_charge_evolution(state, result.repaired_to_zero &&
        state.evolution_policy == EvolutionChargePolicy::RepairToZero);
    return result;
}

FutureUseEvent ProductRuleState::use_future(
    const PlayerId player_id,
    const int advance_cracks,
    const int burn_cracks) {
    PlayerRules& state = rules(player_id);
    FutureUseEvent event;
    event.sequence = next_event_sequence_;
    event.player = player_id;
    event.advance_cracks = std::max(0, advance_cracks);
    event.burn_cracks = std::max(0, burn_cracks);
    state.cracks += event.total_cracks();
    state.turn.future_cracks_added += event.total_cracks();
    append_event(player_id, ProductRuleEvent::Kind::FutureUse, event.total_cracks(), false);
    maybe_charge_evolution(state, event.total_cracks() >= 2 &&
        state.evolution_policy == EvolutionChargePolicy::FutureUseAtLeastTwo);
    return event;
}

void ProductRuleState::record_barrier_granted(
    const PlayerId player_id,
    const CardDefinition* source) {
    PlayerRules& state = rules(player_id);
    state.turn.barrier_granted = true;
    if (source != nullptr) {
        state.turn.barrier_sources.push_back(ProductTurnHistory::PermanentRecord{
            source->kind,
            source->identity.profession_id,
            source->identity.series_id,
        });
    }
    append_event(player_id, ProductRuleEvent::Kind::BarrierGranted, 1, true);
}

void ProductRuleState::record_countdown_expired(
    const PlayerId player_id,
    const CardDefinition* source) {
    PlayerRules& state = rules(player_id);
    ++state.turn.countdown_expired;
    ++state.match.countdown_expired;
    if (source != nullptr) {
        const ProductTurnHistory::PermanentRecord record{
            source->kind,
            source->identity.profession_id,
            source->identity.series_id,
        };
        state.turn.countdown_sources.push_back(record);
        state.match.countdown_sources.push_back(record);
    }
    append_event(player_id, ProductRuleEvent::Kind::CountdownExpired, 1, true);
}

void ProductRuleState::begin_owner_turn(const PlayerId player_id) {
    PlayerRules& state = rules(player_id);
    state.turn = ProductTurnHistory{};
    state.evolution_charged_this_owner_turn = false;
}

bool ProductRuleState::consume_once_per_owner_turn(
    const PlayerId player_id,
    const std::string_view key) {
    if (key.empty()) {
        return false;
    }
    return rules(player_id).turn.consumed_once_keys.emplace(key).second;
}

void ProductRuleState::configure_evolution_charge(
    const PlayerId player_id,
    const EvolutionChargePolicy policy) {
    rules(player_id).evolution_policy = policy;
}

void ProductRuleState::set_evolution_unlocked(const PlayerId player_id, const bool unlocked) {
    rules(player_id).evolution_unlocked = unlocked;
}

int ProductRuleState::evolution_energy(const PlayerId player_id) const {
    return rules(player_id).evolution_energy;
}

ProductListenerToken ProductRuleState::arm_listener(
    const PlayerId player_id,
    const ProductRuleEvent::Kind kind) const {
    (void)rules(player_id);
    return ProductListenerToken{player_id, kind, next_event_sequence_ - 1U};
}

std::vector<ProductRuleEvent> ProductRuleState::events_observed_by(
    const ProductListenerToken& listener) const {
    (void)rules(listener.player);
    std::vector<ProductRuleEvent> result;
    for (const ProductRuleEvent& event : events_) {
        if (event.player == listener.player && event.kind == listener.kind &&
            event.sequence > listener.armed_after_sequence) {
            result.push_back(event);
        }
    }
    return result;
}

const ProductTurnHistory& ProductRuleState::turn_history(const PlayerId player_id) const {
    return rules(player_id).turn;
}

const ProductMatchHistory& ProductRuleState::match_history(const PlayerId player_id) const {
    return rules(player_id).match;
}

ConditionEvaluationContext ProductRuleState::make_condition_context(
    const PlayerId player_id,
    const ProductBoard& board,
    const std::optional<RepairResult> repair_result,
    const std::optional<FutureUseEvent> future_use,
    const bool advanced) const {
    const PlayerRules& state = rules(player_id);
    ConditionEvaluationContext context;
    context.cracks = state.cracks;
    context.advanced = advanced;
    context.actual_repair = repair_result.has_value() ? repair_result->actual_repaired : 0;
    context.repaired_to_zero = repair_result.has_value() && repair_result->repaired_to_zero;
    context.future_use_amount = future_use.has_value() ? future_use->total_cracks() : 0;
    context.turn = state.turn;
    context.match = state.match;
    context.leader_health = board.player(player_id).leader_health;
    context.own_board_count = board.main_board_count(player_id);
    const PlayerId opponent = player_id == PlayerId::Player0 ? PlayerId::Player1 : PlayerId::Player0;
    context.enemy_board_count = board.main_board_count(opponent);
    const auto field = board.player(player_id).field;
    if (field.has_value()) {
        context.field_design_id = board.instance(*field).design_id;
    }
    for (const InstanceId permanent : board.list_permanents(player_id)) {
        const std::string& series = board.catalog().at(board.instance(permanent).design_id).identity.series_id;
        if (std::find(context.controlled_series.begin(), context.controlled_series.end(), series) ==
            context.controlled_series.end()) {
            context.controlled_series.push_back(series);
        }
    }
    return context;
}

void ProductRuleState::maybe_charge_evolution(PlayerRules& state, const bool condition) noexcept {
    if (!condition || !state.evolution_unlocked || state.evolution_charged_this_owner_turn ||
        state.evolution_energy >= 4) {
        return;
    }
    ++state.evolution_energy;
    state.evolution_charged_this_owner_turn = true;
}

void ProductRuleState::append_event(
    const PlayerId player_id,
    const ProductRuleEvent::Kind kind,
    const int amount,
    const bool flag) {
    events_.push_back(ProductRuleEvent{next_event_sequence_++, player_id, kind, amount, flag});
}

void ResolutionQueue::enqueue(ResolutionFrame frame) {
    if (finished_) {
        throw std::logic_error("cannot enqueue a frame after match completion");
    }
    if (frame.frame_id == 0) {
        throw std::invalid_argument("resolution frame id cannot be zero");
    }
    if (std::any_of(frames_.begin(), frames_.end(), [&](const ResolutionFrame& queued) {
            return queued.frame_id == frame.frame_id;
        })) {
        throw std::invalid_argument("resolution frame id is already queued");
    }
    frames_.push_back(std::move(frame));
}

void ResolutionQueue::enqueue_response(ResolutionFrame frame) {
    if (finished_) {
        throw std::logic_error("cannot enqueue a response after match completion");
    }
    if (pending_choice_.has_value() || resume_frame_id_.has_value()) {
        throw std::logic_error("cannot insert a response while a choice is pending");
    }
    if (frame.frame_id == 0 || frame.kind != ResolutionFrameKind::ResponseEffect) {
        throw std::invalid_argument("response queue insertion requires a non-zero response frame");
    }
    if (std::any_of(frames_.begin(), frames_.end(), [&](const ResolutionFrame& queued) {
            return queued.frame_id == frame.frame_id;
        })) {
        throw std::invalid_argument("resolution frame id is already queued");
    }
    frames_.push_front(std::move(frame));
}

void ResolutionQueue::enqueue_entry_pending(
    ResolutionFrame window,
    ResolutionFrame continuation) {
    if (window.kind != ResolutionFrameKind::EntryEffectPending ||
        continuation.kind != ResolutionFrameKind::Continuation ||
        window.frame_id == 0 || continuation.frame_id == 0 ||
        window.frame_id == continuation.frame_id) {
        throw std::invalid_argument("entry window requires distinct pending and continuation frames");
    }
    const auto already_queued = [&](const ResolutionFrameId id) {
        return std::any_of(frames_.begin(), frames_.end(), [&](const ResolutionFrame& queued) {
            return queued.frame_id == id;
        });
    };
    if (finished_ || pending_choice_.has_value() || resume_frame_id_.has_value()) {
        throw std::logic_error("cannot open an entry window in the current resolution state");
    }
    if (already_queued(window.frame_id) || already_queued(continuation.frame_id)) {
        throw std::invalid_argument("entry window frame id is already queued");
    }
    frames_.push_back(std::move(window));
    frames_.push_back(std::move(continuation));
}

Status ResolutionQueue::suspend_for_choice(PendingChoice choice) {
    if (finished_) {
        return Status::error(ErrorCode::ResolutionFinished, "resolution is already finished");
    }
    if (pending_choice_.has_value() || resolved_choice_.has_value() || resume_frame_id_.has_value()) {
        return Status::error(ErrorCode::ChoicePending, "another choice is pending or awaiting frame resume");
    }
    if (!is_valid_player(choice.chooser) || choice.choice_id == 0 || choice.minimum > choice.maximum ||
        choice.maximum > choice.options.size()) {
        return Status::error(ErrorCode::InvalidChoice, "pending choice shape is invalid");
    }
    if (choice.suspended_frame_id != 0 &&
        std::none_of(frames_.begin(), frames_.end(), [&](const ResolutionFrame& frame) {
            return frame.frame_id == choice.suspended_frame_id;
        })) {
        return Status::error(ErrorCode::InvalidChoice, "pending choice references an unknown resolution frame");
    }
    std::unordered_set<std::string> option_ids;
    for (const ChoiceOption& option : choice.options) {
        if (option.option_id.empty() || !option_ids.insert(option.option_id).second) {
            return Status::error(ErrorCode::InvalidChoice, "choice option ids must be non-empty and unique");
        }
    }
    pending_choice_ = std::move(choice);
    return Status::ok();
}

bool ResolutionQueue::input_blocked() const noexcept { return pending_choice_.has_value(); }

bool ResolutionQueue::permits(const ActionKind action) const noexcept {
    return !finished_ &&
        (!input_blocked() || action == ActionKind::ResolveChoice || action == ActionKind::Surrender);
}

const std::optional<PendingChoice>& ResolutionQueue::pending_choice() const noexcept { return pending_choice_; }

Status ResolutionQueue::resolve_choice(
    const PlayerId player,
    const ChoiceId choice_id,
    const std::span<const std::string> selected_option_ids) {
    if (finished_) {
        return Status::error(ErrorCode::ResolutionFinished, "resolution is already finished");
    }
    if (!pending_choice_.has_value()) {
        return Status::error(ErrorCode::NoPendingChoice, "there is no pending choice");
    }
    const PendingChoice& choice = *pending_choice_;
    if (player != choice.chooser) {
        return Status::error(ErrorCode::NotChoiceOwner, "only the choice owner may resolve it");
    }
    if (choice_id != choice.choice_id) {
        return Status::error(ErrorCode::InvalidChoice, "choice id is stale or unknown");
    }
    if (selected_option_ids.size() < choice.minimum || selected_option_ids.size() > choice.maximum) {
        return Status::error(ErrorCode::WrongSelectionCount, "choice selection count is outside its bounds");
    }
    std::unordered_set<std::string> available;
    for (const ChoiceOption& option : choice.options) {
        available.insert(option.option_id);
    }
    std::unordered_set<std::string> selected;
    for (const std::string& option_id : selected_option_ids) {
        if (!selected.insert(option_id).second) {
            return Status::error(ErrorCode::DuplicateSelection, "choice contains a duplicate option");
        }
        if (!available.contains(option_id)) {
            return Status::error(ErrorCode::InvalidChoice, "choice contains an unavailable option");
        }
    }
    resolved_choice_ = ChoiceResolution{
        choice.choice_id,
        choice.suspended_frame_id,
        {selected_option_ids.begin(), selected_option_ids.end()},
    };
    if (choice.suspended_frame_id != 0) {
        resume_frame_id_ = choice.suspended_frame_id;
    }
    pending_choice_.reset();
    ++revision_;
    return Status::ok();
}

std::optional<ChoiceResolution> ResolutionQueue::take_resolved_choice() {
    std::optional<ChoiceResolution> result = std::move(resolved_choice_);
    resolved_choice_.reset();
    return result;
}

std::optional<ResolutionFrame> ResolutionQueue::pop_ready_frame() {
    if (finished_ || pending_choice_.has_value() || frames_.empty()) {
        return std::nullopt;
    }
    auto selected = frames_.begin();
    if (resume_frame_id_.has_value()) {
        selected = std::find_if(frames_.begin(), frames_.end(), [&](const ResolutionFrame& frame) {
            return frame.frame_id == *resume_frame_id_;
        });
        if (selected == frames_.end()) {
            throw std::logic_error("resolved choice lost its suspended resolution frame");
        }
    }
    ResolutionFrame result = std::move(*selected);
    frames_.erase(selected);
    resume_frame_id_.reset();
    return result;
}

void ResolutionQueue::finish_match() noexcept {
    if (finished_) {
        return;
    }
    finished_ = true;
    frames_.clear();
    pending_choice_.reset();
    resolved_choice_.reset();
    resume_frame_id_.reset();
    ++revision_;
}

bool ResolutionQueue::finished() const noexcept { return finished_; }

std::uint64_t ResolutionQueue::revision() const noexcept { return revision_; }

std::size_t ResolutionQueue::frame_count() const noexcept { return frames_.size(); }

TriggerOrderPlanner::TriggerOrderPlanner(
    const PlayerId active_player,
    std::vector<TriggeredAbility> triggers) {
    if (!is_valid_player(active_player)) {
        throw std::invalid_argument("invalid active player for trigger ordering");
    }
    std::unordered_set<std::string> trigger_ids;
    for (TriggeredAbility& trigger : triggers) {
        if (!is_valid_player(trigger.controller) || trigger.trigger_id.empty()) {
            throw std::invalid_argument("invalid simultaneous trigger");
        }
        if (!trigger_ids.insert(trigger.trigger_id).second) {
            throw std::invalid_argument("simultaneous trigger ids must be unique within a batch");
        }
        const std::size_t group = trigger.controller == active_player ? 0U : 1U;
        groups_[group].push_back(std::move(trigger));
    }
    const auto canonical = [](const TriggeredAbility& lhs, const TriggeredAbility& rhs) {
        if (lhs.printed_order != rhs.printed_order) {
            return lhs.printed_order < rhs.printed_order;
        }
        return lhs.trigger_id < rhs.trigger_id;
    };
    for (auto& group : groups_) {
        std::sort(group.begin(), group.end(), canonical);
    }
    advance();
}

bool TriggerOrderPlanner::complete() const noexcept {
    return group_index_ >= groups_.size() && !pending_choice_.has_value();
}

const std::optional<PendingChoice>& TriggerOrderPlanner::pending_choice() const noexcept {
    return pending_choice_;
}

Status TriggerOrderPlanner::resolve_order(
    const PlayerId player,
    const ChoiceId choice_id,
    const std::span<const std::string> ordered_trigger_ids) {
    if (!pending_choice_.has_value()) {
        return Status::error(ErrorCode::NoPendingChoice, "trigger batch has no manual order pending");
    }
    if (player != pending_choice_->chooser) {
        return Status::error(ErrorCode::NotChoiceOwner, "wrong player attempted to order triggers");
    }
    if (choice_id != pending_choice_->choice_id || ordered_trigger_ids.size() != groups_[group_index_].size()) {
        return Status::error(ErrorCode::InvalidChoice, "trigger ordering choice is stale or incomplete");
    }
    std::unordered_map<std::string, TriggeredAbility> by_id;
    for (const TriggeredAbility& trigger : groups_[group_index_]) {
        by_id.emplace(trigger.trigger_id, trigger);
    }
    std::unordered_set<std::string> selected;
    std::vector<TriggeredAbility> ordered_group;
    ordered_group.reserve(ordered_trigger_ids.size());
    for (const std::string& id : ordered_trigger_ids) {
        if (!selected.insert(id).second) {
            return Status::error(ErrorCode::DuplicateSelection, "trigger ordering contains a duplicate");
        }
        const auto found = by_id.find(id);
        if (found == by_id.end()) {
            return Status::error(ErrorCode::InvalidChoice, "trigger ordering contains an unknown trigger");
        }
        ordered_group.push_back(found->second);
    }
    ordered_.insert(ordered_.end(), ordered_group.begin(), ordered_group.end());
    pending_choice_.reset();
    ++group_index_;
    advance();
    return Status::ok();
}

const std::vector<TriggeredAbility>& TriggerOrderPlanner::ordered_triggers() const noexcept { return ordered_; }

void TriggerOrderPlanner::advance() {
    while (group_index_ < groups_.size()) {
        const auto& group = groups_[group_index_];
        if (group.empty()) {
            ++group_index_;
            continue;
        }
        const bool equivalent = group.size() == 1 ||
            (!group.front().equivalence_key.empty() &&
             std::all_of(group.begin(), group.end(), [&](const TriggeredAbility& trigger) {
                 return trigger.equivalence_key == group.front().equivalence_key;
             }));
        if (equivalent) {
            ordered_.insert(ordered_.end(), group.begin(), group.end());
            ++group_index_;
            continue;
        }

        PendingChoice choice;
        choice.choice_id = 0x5452470000000001ULL + group_index_;
        choice.chooser = group.front().controller;
        choice.kind = ChoiceKind::TriggerOrder;
        choice.minimum = group.size();
        choice.maximum = group.size();
        choice.ordered = true;
        for (const TriggeredAbility& trigger : group) {
            choice.options.push_back(ChoiceOption{trigger.trigger_id, trigger.source});
        }
        pending_choice_ = std::move(choice);
        return;
    }
}

} // namespace scgs::v2
