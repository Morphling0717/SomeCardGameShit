// SPDX-License-Identifier: GPL-3.0-or-later

#include "scgs/native_api_v04.h"

#include "scgs/card.hpp"
#include "scgs/client_api.hpp"
#include "scgs/game.hpp"
#include "scgs/types.hpp"

#if defined(SCGS_V04_SYNTHETIC_FIXTURE)
#include "v04_synthetic_fixture.hpp"
#endif

#include <nlohmann/json.hpp>

#include <algorithm>
#include <cstring>
#include <limits>
#include <memory>
#include <mutex>
#include <new>
#include <stdexcept>
#include <string>
#include <string_view>
#include <type_traits>
#include <unordered_map>
#include <utility>
#include <vector>

namespace {

using Json = nlohmann::ordered_json;

constexpr std::uint64_t kMaximumInputBytes = 1024U * 1024U;
constexpr std::uint32_t kAbiMajor = 1U;
constexpr std::uint32_t kAbiMinor = 0U;

thread_local std::string g_last_error;

struct NativeFailure final {
    scgs_v04_native_code code;
    const char* message;
};

[[noreturn]] void fail(const scgs_v04_native_code code, const char* message) {
    throw NativeFailure{code, message};
}

void clear_error() {
    g_last_error.clear();
}

void set_error(const char* message) {
    try {
        g_last_error = message;
    } catch (...) {
        g_last_error.clear();
    }
}

template <typename Callback>
scgs_v04_native_code protect(Callback&& callback) noexcept {
    clear_error();
    try {
        return callback();
    } catch (const NativeFailure& failure) {
        set_error(failure.message);
        return failure.code;
    } catch (const std::bad_alloc&) {
        set_error("The native library could not allocate memory.");
        return SCGS_V04_OUT_OF_MEMORY;
    } catch (...) {
        set_error("The native library encountered an internal error.");
        return SCGS_V04_INTERNAL_ERROR;
    }
}

bool valid_utf8(const std::string_view value) noexcept {
    const auto* bytes = reinterpret_cast<const unsigned char*>(value.data());
    std::size_t index = 0;
    while (index < value.size()) {
        const unsigned char first = bytes[index];
        if (first <= 0x7FU) {
            ++index;
            continue;
        }
        if (first >= 0xC2U && first <= 0xDFU) {
            if (index + 1U >= value.size() || (bytes[index + 1U] & 0xC0U) != 0x80U) {
                return false;
            }
            index += 2U;
            continue;
        }
        if (first == 0xE0U) {
            if (index + 2U >= value.size() || bytes[index + 1U] < 0xA0U ||
                bytes[index + 1U] > 0xBFU || (bytes[index + 2U] & 0xC0U) != 0x80U) {
                return false;
            }
            index += 3U;
            continue;
        }
        if ((first >= 0xE1U && first <= 0xECU) || (first >= 0xEEU && first <= 0xEFU)) {
            if (index + 2U >= value.size() || (bytes[index + 1U] & 0xC0U) != 0x80U ||
                (bytes[index + 2U] & 0xC0U) != 0x80U) {
                return false;
            }
            index += 3U;
            continue;
        }
        if (first == 0xEDU) {
            if (index + 2U >= value.size() || bytes[index + 1U] < 0x80U ||
                bytes[index + 1U] > 0x9FU || (bytes[index + 2U] & 0xC0U) != 0x80U) {
                return false;
            }
            index += 3U;
            continue;
        }
        if (first == 0xF0U) {
            if (index + 3U >= value.size() || bytes[index + 1U] < 0x90U ||
                bytes[index + 1U] > 0xBFU || (bytes[index + 2U] & 0xC0U) != 0x80U ||
                (bytes[index + 3U] & 0xC0U) != 0x80U) {
                return false;
            }
            index += 4U;
            continue;
        }
        if (first >= 0xF1U && first <= 0xF3U) {
            if (index + 3U >= value.size() || (bytes[index + 1U] & 0xC0U) != 0x80U ||
                (bytes[index + 2U] & 0xC0U) != 0x80U ||
                (bytes[index + 3U] & 0xC0U) != 0x80U) {
                return false;
            }
            index += 4U;
            continue;
        }
        if (first == 0xF4U) {
            if (index + 3U >= value.size() || bytes[index + 1U] < 0x80U ||
                bytes[index + 1U] > 0x8FU || (bytes[index + 2U] & 0xC0U) != 0x80U ||
                (bytes[index + 3U] & 0xC0U) != 0x80U) {
                return false;
            }
            index += 4U;
            continue;
        }
        return false;
    }
    return true;
}

Json parse_payload(const char* data, const std::uint64_t byte_count) {
    if (byte_count > kMaximumInputBytes) {
        fail(SCGS_V04_PAYLOAD_TOO_LARGE, "The JSON payload exceeds the 1 MiB limit.");
    }
    if (data == nullptr || byte_count == 0U) {
        fail(SCGS_V04_INVALID_ARGUMENT, "A non-empty JSON payload is required.");
    }
    if (byte_count > static_cast<std::uint64_t>(std::numeric_limits<std::size_t>::max())) {
        fail(SCGS_V04_PAYLOAD_TOO_LARGE, "The JSON payload cannot be represented by this process.");
    }
    const auto size = static_cast<std::size_t>(byte_count);
    const std::string_view bytes(data, size);
    if (!valid_utf8(bytes)) {
        fail(SCGS_V04_INVALID_UTF8, "The JSON payload is not valid UTF-8.");
    }
    Json payload = Json::parse(bytes.begin(), bytes.end(), nullptr, false);
    if (payload.is_discarded()) {
        fail(SCGS_V04_INVALID_JSON, "The JSON payload is malformed.");
    }
    if (!payload.is_object()) {
        fail(SCGS_V04_SCHEMA_MISMATCH, "The JSON root must be an object.");
    }
    return payload;
}

const Json& require_field(const Json& object, const char* name) {
    const auto iterator = object.find(name);
    if (iterator == object.end()) {
        fail(SCGS_V04_SCHEMA_MISMATCH, "The JSON payload is missing a required field.");
    }
    return *iterator;
}

const Json* optional_field(const Json& object, const char* name) {
    const auto iterator = object.find(name);
    return iterator == object.end() ? nullptr : std::addressof(*iterator);
}

std::uint64_t require_unsigned_value(const Json& value) {
    if (value.is_number_unsigned()) {
        return value.get<std::uint64_t>();
    }
    if (value.is_number_integer()) {
        const std::int64_t result = value.get<std::int64_t>();
        if (result >= 0) {
            return static_cast<std::uint64_t>(result);
        }
    }
    fail(SCGS_V04_SCHEMA_MISMATCH, "A JSON field has the wrong integer type or range.");
}

std::uint64_t require_unsigned(const Json& object, const char* name) {
    return require_unsigned_value(require_field(object, name));
}

std::uint32_t require_u32(const Json& object, const char* name) {
    const std::uint64_t value = require_unsigned(object, name);
    if (value > std::numeric_limits<std::uint32_t>::max()) {
        fail(SCGS_V04_SCHEMA_MISMATCH, "A JSON integer is outside the uint32 range.");
    }
    return static_cast<std::uint32_t>(value);
}

bool require_boolean(const Json& object, const char* name) {
    const Json& value = require_field(object, name);
    if (!value.is_boolean()) {
        fail(SCGS_V04_SCHEMA_MISMATCH, "A JSON field must be a boolean.");
    }
    return value.get<bool>();
}

std::string require_string(const Json& object, const char* name) {
    const Json& value = require_field(object, name);
    if (!value.is_string()) {
        fail(SCGS_V04_SCHEMA_MISMATCH, "A JSON field must be a string.");
    }
    return value.get<std::string>();
}

void require_schema(const Json& object) {
    if (require_u32(object, "schema_version") != SCGS_V04_SCHEMA_VERSION) {
        fail(SCGS_V04_SCHEMA_MISMATCH, "The JSON schema version is not supported.");
    }
}

scgs::PlayerId parse_player_value(const Json& value) {
    const std::uint64_t player = require_unsigned_value(value);
    switch (player) {
        case 0U:
            return scgs::PlayerId::Player0;
        case 1U:
            return scgs::PlayerId::Player1;
        default:
            fail(SCGS_V04_SCHEMA_MISMATCH, "The JSON player value is not supported.");
    }
}

scgs::PlayerId parse_player(const Json& object, const char* name) {
    return parse_player_value(require_field(object, name));
}

scgs::PlayerId parse_viewer(const std::uint32_t viewer) {
    switch (viewer) {
        case 0U:
            return scgs::PlayerId::Player0;
        case 1U:
            return scgs::PlayerId::Player1;
        default:
            fail(SCGS_V04_INVALID_ARGUMENT, "The viewer value is outside the supported range.");
    }
}

scgs::ActionKind parse_action_value(const Json& value) {
    switch (require_unsigned_value(value)) {
        case 0U:
            return scgs::ActionKind::Mulligan;
        case 1U:
            return scgs::ActionKind::PlayUnit;
        case 2U:
            return scgs::ActionKind::CastSpell;
        case 3U:
            return scgs::ActionKind::PlayTactic;
        case 4U:
            return scgs::ActionKind::Attack;
        case 5U:
            return scgs::ActionKind::Evolve;
        case 6U:
            return scgs::ActionKind::Deploy;
        case 7U:
            return scgs::ActionKind::ActivateTrap;
        case 8U:
            return scgs::ActionKind::PassReaction;
        case 9U:
            return scgs::ActionKind::EndTurn;
        case 10U:
            return scgs::ActionKind::Surrender;
        default:
            fail(SCGS_V04_SCHEMA_MISMATCH, "The JSON action value is not supported.");
    }
}

scgs::Target parse_target(const Json& value) {
    if (!value.is_object()) {
        fail(SCGS_V04_SCHEMA_MISMATCH, "A JSON target must be an object.");
    }
    const std::uint64_t kind = require_unsigned(value, "kind");
    const scgs::PlayerId player = parse_player(value, "player");
    switch (kind) {
        case 0U:
            return scgs::Target::leader(player);
        case 1U:
            return scgs::Target::unit_target(player, require_unsigned(value, "unit"));
        default:
            fail(SCGS_V04_SCHEMA_MISMATCH, "The JSON target kind is not supported.");
    }
}

std::vector<scgs::InstanceId> parse_instance_array(const Json& value) {
    if (!value.is_array()) {
        fail(SCGS_V04_SCHEMA_MISMATCH, "A JSON card selection must be an array.");
    }
    std::vector<scgs::InstanceId> result;
    result.reserve(value.size());
    for (const Json& item : value) {
        result.push_back(require_unsigned_value(item));
    }
    return result;
}

std::size_t parse_slot_value(const Json& value) {
    const std::uint64_t slot = require_unsigned_value(value);
    if (slot > static_cast<std::uint64_t>(std::numeric_limits<std::size_t>::max())) {
        fail(SCGS_V04_SCHEMA_MISMATCH, "The JSON slot is outside the native range.");
    }
    return static_cast<std::size_t>(slot);
}

scgs::GameCommand parse_command(const Json& object) {
    require_schema(object);
    scgs::GameCommand command;
    command.player = parse_player(object, "player");
    command.action = parse_action_value(require_field(object, "action"));
    command.expected_revision = require_unsigned(object, "expected_revision");
    if (const Json* value = optional_field(object, "source")) {
        command.source = require_unsigned_value(*value);
    }
    if (const Json* value = optional_field(object, "target")) {
        if (value->is_null()) {
            fail(SCGS_V04_SCHEMA_MISMATCH, "Optional JSON fields must be omitted instead of null.");
        }
        command.target = parse_target(*value);
    }
    if (const Json* value = optional_field(object, "slot")) {
        if (value->is_null()) {
            fail(SCGS_V04_SCHEMA_MISMATCH, "Optional JSON fields must be omitted instead of null.");
        }
        command.slot = parse_slot_value(*value);
    }
    if (const Json* value = optional_field(object, "component_donor")) {
        if (value->is_null()) {
            fail(SCGS_V04_SCHEMA_MISMATCH, "Optional JSON fields must be omitted instead of null.");
        }
        command.component_donor = require_unsigned_value(*value);
    }
    if (const Json* value = optional_field(object, "use_advance")) {
        if (!value->is_boolean()) {
            fail(SCGS_V04_SCHEMA_MISMATCH, "The use_advance field must be a boolean.");
        }
        command.use_advance = value->get<bool>();
    }
    if (const Json* value = optional_field(object, "mulligan_cards")) {
        command.mulligan_cards = parse_instance_array(*value);
    }
    return command;
}

scgs::ActionQuery parse_query(const Json& object) {
    require_schema(object);
    scgs::ActionQuery query;
    query.player = parse_player(object, "player");
    query.expected_revision = require_unsigned(object, "expected_revision");
    if (const Json* value = optional_field(object, "action")) {
        if (value->is_null()) {
            fail(SCGS_V04_SCHEMA_MISMATCH, "Optional JSON fields must be omitted instead of null.");
        }
        query.action = parse_action_value(*value);
    }
    if (const Json* value = optional_field(object, "source")) {
        if (value->is_null()) {
            fail(SCGS_V04_SCHEMA_MISMATCH, "Optional JSON fields must be omitted instead of null.");
        }
        query.source = require_unsigned_value(*value);
    }
    if (const Json* value = optional_field(object, "target")) {
        if (value->is_null()) {
            fail(SCGS_V04_SCHEMA_MISMATCH, "Optional JSON fields must be omitted instead of null.");
        }
        query.target = parse_target(*value);
    }
    if (const Json* value = optional_field(object, "slot")) {
        if (value->is_null()) {
            fail(SCGS_V04_SCHEMA_MISMATCH, "Optional JSON fields must be omitted instead of null.");
        }
        query.slot = parse_slot_value(*value);
    }
    if (const Json* value = optional_field(object, "component_donor")) {
        if (value->is_null()) {
            fail(SCGS_V04_SCHEMA_MISMATCH, "Optional JSON fields must be omitted instead of null.");
        }
        query.component_donor = require_unsigned_value(*value);
    }
    if (const Json* value = optional_field(object, "use_advance")) {
        if (!value->is_boolean()) {
            fail(SCGS_V04_SCHEMA_MISMATCH, "The use_advance field must be a boolean.");
        }
        query.use_advance = value->get<bool>();
    }
    if (const Json* value = optional_field(object, "mulligan_cards")) {
        query.mulligan_cards = parse_instance_array(*value);
    }
    return query;
}

struct ParsedConfig final {
    scgs::DeckList player0_deck;
    scgs::DeckList player1_deck;
    scgs::GameConfig game_config;
};

scgs::DeckList parse_deck_name(const std::string& name) {
#if defined(SCGS_V04_SYNTHETIC_FIXTURE)
    if (name == "synthetic_alpha") {
        return scgs::test_fixture::make_alpha_deck();
    }
    if (name == "synthetic_beta") {
        return scgs::test_fixture::make_beta_deck();
    }
#else
    (void)name;
#endif
    // Retired products are never mapped to a fixture or a schema-2 deck.
    fail(SCGS_V04_SCHEMA_MISMATCH, "This v04 configuration is retired or unsupported; use the product v05 API.");
}

scgs::CardCatalog session_catalog() {
#if defined(SCGS_V04_SYNTHETIC_FIXTURE)
    return scgs::test_fixture::make_catalog();
#else
    // The compatibility artifact retains the frozen transport ABI, not a
    // hidden playable product. parse_deck_name rejects every product config.
    return {};
#endif
}

ParsedConfig parse_config(const Json& object) {
    require_schema(object);
    ParsedConfig result{
        parse_deck_name(require_string(object, "player0_deck")),
        parse_deck_name(require_string(object, "player1_deck")),
        {}};

    if (const Json* value = optional_field(object, "random_seed")) {
        if (value->is_null()) {
            fail(SCGS_V04_SCHEMA_MISMATCH, "Optional JSON fields must be omitted instead of null.");
        }
        const std::uint64_t seed = require_unsigned_value(*value);
        if (seed > std::numeric_limits<std::uint32_t>::max()) {
            fail(SCGS_V04_SCHEMA_MISMATCH, "The random seed is outside the uint32 range.");
        }
        result.game_config.random_seed = static_cast<std::uint32_t>(seed);
    }
    if (const Json* value = optional_field(object, "first_player_mode")) {
        switch (require_unsigned_value(*value)) {
            case 0U:
                result.game_config.first_player_mode = scgs::FirstPlayerMode::Random;
                break;
            case 1U:
                result.game_config.first_player_mode = scgs::FirstPlayerMode::Player0;
                break;
            case 2U:
                result.game_config.first_player_mode = scgs::FirstPlayerMode::Player1;
                break;
            default:
                fail(SCGS_V04_SCHEMA_MISMATCH, "The first-player mode is not supported.");
        }
    }
    if (optional_field(object, "shuffle_decks") != nullptr) {
        result.game_config.shuffle_decks = require_boolean(object, "shuffle_decks");
    }
    return result;
}

[[noreturn]] void invalid_internal_enum() {
    throw std::logic_error("unmapped native enum");
}

std::uint32_t map_player(const scgs::PlayerId value) {
    switch (value) {
        case scgs::PlayerId::Player0:
            return 0U;
        case scgs::PlayerId::Player1:
            return 1U;
    }
    invalid_internal_enum();
}

std::uint32_t map_action(const scgs::ActionKind value) {
    switch (value) {
        case scgs::ActionKind::Mulligan:
            return 0U;
        case scgs::ActionKind::PlayUnit:
            return 1U;
        case scgs::ActionKind::CastSpell:
            return 2U;
        case scgs::ActionKind::PlayTactic:
            return 3U;
        case scgs::ActionKind::Attack:
            return 4U;
        case scgs::ActionKind::Evolve:
            return 5U;
        case scgs::ActionKind::Deploy:
            return 6U;
        case scgs::ActionKind::ActivateTrap:
            return 7U;
        case scgs::ActionKind::PassReaction:
            return 8U;
        case scgs::ActionKind::EndTurn:
            return 9U;
        case scgs::ActionKind::Surrender:
            return 10U;
    }
    invalid_internal_enum();
}

std::uint32_t map_card_kind(const scgs::CardKind value) {
    switch (value) {
        case scgs::CardKind::Unit:
            return 0U;
        case scgs::CardKind::Spell:
            return 1U;
        case scgs::CardKind::Relic:
            return 2U;
        case scgs::CardKind::Trap:
            return 3U;
    }
    invalid_internal_enum();
}

std::uint32_t map_zone(const scgs::Zone value) {
    switch (value) {
        case scgs::Zone::None:
            return 0U;
        case scgs::Zone::Deck:
            return 1U;
        case scgs::Zone::Hand:
            return 2U;
        case scgs::Zone::Unit:
            return 3U;
        case scgs::Zone::Tactic:
            return 4U;
        case scgs::Zone::Graveyard:
            return 5U;
        case scgs::Zone::Archive:
            return 6U;
        case scgs::Zone::Standby:
            return 7U;
    }
    invalid_internal_enum();
}

std::uint32_t map_effect_trigger(const scgs::EffectTrigger value) {
    switch (value) {
        case scgs::EffectTrigger::OnPlay:
            return 0U;
        case scgs::EffectTrigger::OnPlayIfAdvanced:
            return 1U;
        case scgs::EffectTrigger::OnPlayIfNotAdvanced:
            return 2U;
        case scgs::EffectTrigger::OnEntry:
            return 3U;
        case scgs::EffectTrigger::OnEvolution:
            return 4U;
        case scgs::EffectTrigger::OnLastWords:
            return 5U;
        case scgs::EffectTrigger::OnCountdownExpire:
            return 6U;
        case scgs::EffectTrigger::OnSpellDeclared:
            return 7U;
        case scgs::EffectTrigger::OnAttackDeclared:
            return 8U;
        case scgs::EffectTrigger::OnEntryEffectPending:
            return 9U;
    }
    invalid_internal_enum();
}

std::uint32_t map_effect_kind(const scgs::EffectKind value) {
    switch (value) {
        case scgs::EffectKind::DrawCards:
            return 0U;
        case scgs::EffectKind::DealDamageToEnemyUnit:
            return 1U;
        case scgs::EffectKind::DealDamageToLeader:
            return 2U;
        case scgs::EffectKind::HealLeader:
            return 3U;
        case scgs::EffectKind::RepairCracks:
            return 4U;
        case scgs::EffectKind::GainPPCapacity:
            return 5U;
        case scgs::EffectKind::BuffFriendlyUnit:
            return 6U;
        case scgs::EffectKind::GrantRush:
            return 7U;
        case scgs::EffectKind::CancelAttack:
            return 8U;
        case scgs::EffectKind::DamageEnteredUnit:
            return 9U;
    }
    invalid_internal_enum();
}

std::uint32_t map_target_spec(const scgs::TargetSpec value) {
    switch (value) {
        case scgs::TargetSpec::None:
            return 0U;
        case scgs::TargetSpec::EnemyUnit:
            return 1U;
        case scgs::TargetSpec::FriendlyUnit:
            return 2U;
    }
    invalid_internal_enum();
}

std::uint32_t map_deployment_condition(const scgs::DeploymentCondition value) {
    switch (value) {
        case scgs::DeploymentCondition::None:
            return 0U;
        case scgs::DeploymentCondition::FriendlyUnitsMin:
            return 1U;
        case scgs::DeploymentCondition::SpellsThisTurnMin:
            return 2U;
    }
    invalid_internal_enum();
}

std::uint32_t map_phase(const scgs::Phase value) {
    switch (value) {
        case scgs::Phase::NotStarted:
            return 0U;
        case scgs::Phase::Mulligan:
            return 1U;
        case scgs::Phase::Action:
            return 2U;
        case scgs::Phase::Reaction:
            return 3U;
        case scgs::Phase::Finished:
            return 4U;
    }
    invalid_internal_enum();
}

std::uint32_t map_reaction_window(const scgs::ReactionWindow value) {
    switch (value) {
        case scgs::ReactionWindow::None:
            return 0U;
        case scgs::ReactionWindow::SpellDeclared:
            return 1U;
        case scgs::ReactionWindow::EntryEffectPending:
            return 2U;
        case scgs::ReactionWindow::AttackDeclared:
            return 3U;
    }
    invalid_internal_enum();
}

std::uint32_t map_result(const scgs::GameResult value) {
    switch (value) {
        case scgs::GameResult::Ongoing:
            return 0U;
        case scgs::GameResult::Player0Won:
            return 1U;
        case scgs::GameResult::Player1Won:
            return 2U;
        case scgs::GameResult::Draw:
            return 3U;
    }
    invalid_internal_enum();
}

std::uint32_t map_error(const scgs::ErrorCode value) {
    switch (value) {
        case scgs::ErrorCode::Ok:
            return 0U;
        case scgs::ErrorCode::InvalidPhase:
            return 1U;
        case scgs::ErrorCode::NotActivePlayer:
            return 2U;
        case scgs::ErrorCode::InvalidPlayer:
            return 3U;
        case scgs::ErrorCode::InvalidCard:
            return 4U;
        case scgs::ErrorCode::InvalidZone:
            return 5U;
        case scgs::ErrorCode::InvalidTarget:
            return 6U;
        case scgs::ErrorCode::InvalidSlot:
            return 7U;
        case scgs::ErrorCode::InsufficientPP:
            return 8U;
        case scgs::ErrorCode::HandLimit:
            return 9U;
        case scgs::ErrorCode::UnitZoneFull:
            return 10U;
        case scgs::ErrorCode::TacticZoneFull:
            return 11U;
        case scgs::ErrorCode::SummoningSickness:
            return 12U;
        case scgs::ErrorCode::AlreadyAttacked:
            return 13U;
        case scgs::ErrorCode::GuardBlocksTarget:
            return 14U;
        case scgs::ErrorCode::EvolutionLocked:
            return 15U;
        case scgs::ErrorCode::NoEvolutionPoints:
            return 16U;
        case scgs::ErrorCode::EvolutionAlreadyUsed:
            return 17U;
        case scgs::ErrorCode::AlreadyEvolved:
            return 18U;
        case scgs::ErrorCode::AdvanceAlreadyUsed:
            return 19U;
        case scgs::ErrorCode::AdvanceWouldExceedCap:
            return 20U;
        case scgs::ErrorCode::DeployAlreadyUsed:
            return 21U;
        case scgs::ErrorCode::DeployConditionNotMet:
            return 22U;
        case scgs::ErrorCode::InvalidDeployment:
            return 23U;
        case scgs::ErrorCode::ResponseDepthExceeded:
            return 24U;
        case scgs::ErrorCode::TrapAlreadySetThisTurn:
            return 25U;
        case scgs::ErrorCode::NoPendingReaction:
            return 26U;
        case scgs::ErrorCode::TrapNotEligible:
            return 27U;
        case scgs::ErrorCode::LeaderSkillLocked:
            return 28U;
        case scgs::ErrorCode::LeaderSkillAlreadyUsed:
            return 29U;
        case scgs::ErrorCode::MatchAlreadyStarted:
            return 30U;
        case scgs::ErrorCode::MatchNotStarted:
            return 31U;
        case scgs::ErrorCode::MulliganAlreadyDone:
            return 32U;
        case scgs::ErrorCode::DuplicateSelection:
            return 33U;
        case scgs::ErrorCode::GameOver:
            return 34U;
        case scgs::ErrorCode::StaleRevision:
            return 35U;
    }
    invalid_internal_enum();
}

std::uint32_t map_event_type(const scgs::EventType value) {
    switch (value) {
        case scgs::EventType::MatchStarted:
            return 0U;
        case scgs::EventType::TurnStarted:
            return 1U;
        case scgs::EventType::TurnEnded:
            return 2U;
        case scgs::EventType::CardDrawn:
            return 3U;
        case scgs::EventType::FatigueDamage:
            return 4U;
        case scgs::EventType::HandOverflowArchived:
            return 5U;
        case scgs::EventType::PPChanged:
            return 6U;
        case scgs::EventType::CracksChanged:
            return 7U;
        case scgs::EventType::CardMoved:
            return 8U;
        case scgs::EventType::UnitEntered:
            return 9U;
        case scgs::EventType::UnitDamaged:
            return 10U;
        case scgs::EventType::LeaderDamaged:
            return 11U;
        case scgs::EventType::LeaderHealed:
            return 12U;
        case scgs::EventType::UnitDestroyed:
            return 13U;
        case scgs::EventType::AttackDeclared:
            return 14U;
        case scgs::EventType::AttackCancelled:
            return 15U;
        case scgs::EventType::UnitEvolved:
            return 16U;
        case scgs::EventType::EvolutionEnergyChanged:
            return 17U;
        case scgs::EventType::UnitDeployed:
            return 18U;
        case scgs::EventType::TrapWindowOpened:
            return 19U;
        case scgs::EventType::TrapActivated:
            return 20U;
        case scgs::EventType::LeaderSkillUsed:
            return 21U;
        case scgs::EventType::PlayerSurrendered:
            return 22U;
        case scgs::EventType::MatchEnded:
            return 23U;
        case scgs::EventType::MulliganCompleted:
            return 24U;
    }
    invalid_internal_enum();
}

std::uint32_t map_target_kind(const scgs::Target::Kind value) {
    switch (value) {
        case scgs::Target::Kind::Leader:
            return 0U;
        case scgs::Target::Kind::Unit:
            return 1U;
    }
    invalid_internal_enum();
}

Json serialize_effect(const scgs::EffectRecord& value) {
    Json result = Json::object();
    result["trigger"] = map_effect_trigger(value.trigger);
    result["kind"] = map_effect_kind(value.kind);
    result["amount"] = value.amount;
    result["target_spec"] = map_target_spec(value.target_spec);
    return result;
}

Json serialize_effects(const std::vector<scgs::EffectRecord>& values) {
    Json result = Json::array();
    for (const scgs::EffectRecord& value : values) {
        result.push_back(serialize_effect(value));
    }
    return result;
}

Json serialize_component(const scgs::ComponentSpec& value) {
    Json result = Json::object();
    result["has_component"] = value.has_component;
    result["granted_kind"] = map_effect_kind(value.granted_kind);
    result["granted_amount"] = value.granted_amount;
    return result;
}

Json serialize_definition(const scgs::CardDefinition& value) {
    Json result = Json::object();
    result["id"] = value.id;
    result["name"] = value.name;
    result["kind"] = map_card_kind(value.kind);
    result["cost"] = value.cost;
    result["attack"] = value.attack;
    result["health"] = value.health;
    result["countdown"] = value.countdown;
    result["printed_guard"] = value.printed_guard;
    result["printed_rush"] = value.printed_rush;
    result["printed_storm"] = value.printed_storm;
    result["printed_barrier"] = value.printed_barrier;
    result["printed_lifesteal"] = value.printed_lifesteal;
    result["printed_bane"] = value.printed_bane;
    result["evolved_attack"] = value.evolved_attack;
    result["evolved_health"] = value.evolved_health;

    Json additional_cost = Json::object();
    additional_cost["burn_pp_capacity"] = value.additional_cost.burn_pp_capacity;
    result["additional_cost"] = std::move(additional_cost);

    if (value.deployment.has_value()) {
        Json deployment = Json::object();
        deployment["condition"] = map_deployment_condition(value.deployment->condition);
        deployment["condition_amount"] = value.deployment->condition_amount;
        deployment["pp_cost"] = value.deployment->pp_cost;
        deployment["archive_one_friendly_unit"] = value.deployment->archive_one_friendly_unit;
        result["deployment"] = std::move(deployment);
    }
    result["component"] = serialize_component(value.component);
    result["effects"] = serialize_effects(value.effects);
    return result;
}

Json serialize_card(const scgs::CardView& value) {
    Json result = Json::object();
    if (value.instance_id.has_value()) {
        result["instance_id"] = *value.instance_id;
    }
    if (value.definition_id.has_value()) {
        result["definition_id"] = *value.definition_id;
    }
    if (value.definition.has_value()) {
        result["definition"] = serialize_definition(*value.definition);
    }
    if (value.kind.has_value()) {
        result["kind"] = map_card_kind(*value.kind);
    }
    result["name"] = value.name;
    result["owner"] = map_player(value.owner);
    result["controller"] = map_player(value.controller);
    result["zone"] = map_zone(value.zone);
    result["sequence"] = static_cast<std::uint64_t>(value.sequence);
    result["cost"] = value.cost;
    result["current_attack"] = value.current_attack;
    result["current_health"] = value.current_health;
    result["maximum_health"] = value.maximum_health;
    result["keywords"] = value.keywords;
    result["evolved"] = value.evolved;
    result["attacked_this_turn"] = value.attacked_this_turn;
    result["entered_this_turn"] = value.entered_this_turn;
    result["temporary_rush"] = value.temporary_rush;
    result["deployed_from_standby"] = value.deployed_from_standby;
    result["face_down"] = value.face_down;
    result["countdown"] = value.countdown;
    result["granted_component"] = serialize_component(value.granted_component);
    return result;
}

Json serialize_card_list(const std::vector<scgs::CardView>& values) {
    Json result = Json::array();
    for (const scgs::CardView& value : values) {
        result.push_back(serialize_card(value));
    }
    return result;
}

template <std::size_t Size>
Json serialize_card_slots(const std::array<std::optional<scgs::CardView>, Size>& values) {
    Json result = Json::array();
    for (const std::optional<scgs::CardView>& value : values) {
        if (value.has_value()) {
            result.push_back(serialize_card(*value));
        } else {
            result.push_back(nullptr);
        }
    }
    return result;
}

Json serialize_leader_skill(const scgs::LeaderSkillDefinition& value) {
    Json result = Json::object();
    result["name"] = value.name;
    result["cost"] = value.cost;
    result["effects"] = serialize_effects(value.effects);
    return result;
}

Json serialize_player_view(const scgs::PlayerView& value) {
    Json result = Json::object();
    result["player"] = map_player(value.player);
    result["leader_health"] = value.leader_health;
    result["maximum_leader_health"] = value.maximum_leader_health;
    result["current_pp"] = value.current_pp;
    result["pp_capacity"] = value.pp_capacity;
    result["cracks"] = value.cracks;
    result["evolution_energy"] = value.evolution_energy;
    result["own_turn_number"] = value.own_turn_number;
    result["fatigue_count"] = value.fatigue_count;
    result["mulligan_done"] = value.mulligan_done;
    result["evolution_used_this_turn"] = value.evolution_used_this_turn;
    result["advance_used_this_turn"] = value.advance_used_this_turn;
    result["deploy_used_this_turn"] = value.deploy_used_this_turn;
    result["trap_set_this_turn"] = value.trap_set_this_turn;
    result["leader_skill_used"] = value.leader_skill_used;
    result["charge_granted_this_cycle"] = value.charge_granted_this_cycle;
    result["friendly_deaths_this_cycle"] = value.friendly_deaths_this_cycle;
    result["spells_used_this_turn"] = value.spells_used_this_turn;
    result["units_played_this_turn"] = value.units_played_this_turn;
    result["leader_skill"] = serialize_leader_skill(value.leader_skill);
    result["deck_count"] = static_cast<std::uint64_t>(value.deck_count);
    result["hand_count"] = static_cast<std::uint64_t>(value.hand_count);
    result["hand"] = serialize_card_list(value.hand);
    result["units"] = serialize_card_slots(value.units);
    result["tactics"] = serialize_card_slots(value.tactics);
    result["graveyard"] = serialize_card_list(value.graveyard);
    result["archive"] = serialize_card_list(value.archive);
    result["standby"] = serialize_card_list(value.standby);
    return result;
}

Json serialize_target(const scgs::Target& value) {
    Json result = Json::object();
    result["kind"] = map_target_kind(value.kind);
    result["player"] = map_player(value.player);
    if (value.kind == scgs::Target::Kind::Unit) {
        result["unit"] = value.unit;
    }
    return result;
}

Json serialize_status(const scgs::Status& value) {
    Json result = Json::object();
    result["engine_code"] = map_error(value.code);
    result["message"] = value.message;
    return result;
}

Json serialize_payment(const scgs::PaymentPreview& value) {
    Json result = Json::object();
    result["status"] = serialize_status(value.status);
    result["current_pp_before"] = value.current_pp_before;
    result["current_pp_after"] = value.current_pp_after;
    result["pp_capacity_before"] = value.pp_capacity_before;
    result["pp_capacity_after"] = value.pp_capacity_after;
    result["cracks_before"] = value.cracks_before;
    result["cracks_after"] = value.cracks_after;
    result["evolution_energy_before"] = value.evolution_energy_before;
    result["evolution_energy_after"] = value.evolution_energy_after;
    result["base_cost"] = value.base_cost;
    result["burn_cost"] = value.burn_cost;
    result["advance_cost"] = value.advance_cost;
    result["used_advance"] = value.used_advance;
    return result;
}

Json serialize_command(const scgs::GameCommand& value) {
    Json result = Json::object();
    result["player"] = map_player(value.player);
    result["action"] = map_action(value.action);
    result["source"] = value.source;
    if (value.target.has_value()) {
        result["target"] = serialize_target(*value.target);
    }
    if (value.slot.has_value()) {
        result["slot"] = static_cast<std::uint64_t>(*value.slot);
    }
    if (value.component_donor.has_value()) {
        result["component_donor"] = *value.component_donor;
    }
    result["use_advance"] = value.use_advance;
    result["mulligan_cards"] = value.mulligan_cards;
    result["expected_revision"] = value.expected_revision;
    return result;
}

Json serialize_legal_action(const scgs::LegalAction& value) {
    Json result = Json::object();
    result["command"] = serialize_command(value.command);
    result["payment"] = serialize_payment(value.payment);
    return result;
}

Json serialize_reaction_origin(const scgs::ReactionOrigin& value) {
    Json result = Json::object();
    result["action"] = map_action(value.action);
    result["player"] = map_player(value.player);
    result["source"] = value.source;
    if (value.target.has_value()) {
        result["target"] = serialize_target(*value.target);
    }
    return result;
}

Json serialize_reaction(const scgs::ReactionContext& value) {
    Json result = Json::object();
    result["pending"] = value.pending;
    result["window"] = map_reaction_window(value.window);
    result["responder"] = map_player(value.responder);
    result["subject"] = value.subject;
    result["depth"] = static_cast<std::uint64_t>(value.depth);
    result["eligible_count"] = static_cast<std::uint64_t>(value.eligible_count);
    result["eligible_traps"] = serialize_card_list(value.eligible_traps);
    result["revision"] = value.revision;
    if (value.origin.has_value()) {
        result["origin"] = serialize_reaction_origin(*value.origin);
    }
    return result;
}

Json serialize_match_view(const scgs::MatchView& value) {
    Json result = Json::object();
    result["viewer"] = map_player(value.viewer);
    result["active_player"] = map_player(value.active_player);
    result["first_player"] = map_player(value.first_player);
    result["random_seed"] = value.random_seed;
    result["phase"] = map_phase(value.phase);
    result["result"] = map_result(value.result);
    result["revision"] = value.revision;
    Json players = Json::array();
    for (const scgs::PlayerView& player : value.players) {
        players.push_back(serialize_player_view(player));
    }
    result["players"] = std::move(players);
    result["reaction"] = serialize_reaction(value.reaction);
    return result;
}

Json serialize_event(const scgs::GameEventView& value) {
    Json result = Json::object();
    result["sequence"] = value.sequence;
    result["type"] = map_event_type(value.type);
    result["player"] = map_player(value.player);
    if (value.card.has_value()) {
        result["card"] = *value.card;
    }
    if (value.definition_id.has_value()) {
        result["definition_id"] = *value.definition_id;
    }
    result["value"] = value.value;
    result["secondary_value"] = value.secondary_value;
    result["hidden_card"] = value.hidden_card;
    result["text"] = value.text;
    if (value.random_seed.has_value()) {
        result["random_seed"] = *value.random_seed;
    }
    if (value.first_player.has_value()) {
        result["first_player"] = map_player(*value.first_player);
    }
    return result;
}

Json make_output(const std::uint64_t revision) {
    Json result = Json::object();
    result["schema_version"] = SCGS_V04_SCHEMA_VERSION;
    result["revision"] = revision;
    return result;
}

void prepare_output(std::uint64_t* required_bytes) {
    if (required_bytes == nullptr) {
        fail(SCGS_V04_INVALID_ARGUMENT, "The required-bytes output pointer is null.");
    }
    *required_bytes = 0U;
}

scgs_v04_native_code write_bytes(
    const std::string_view payload,
    char* buffer,
    const std::uint64_t capacity,
    std::uint64_t* required_bytes) {
    prepare_output(required_bytes);
    if (payload.size() >= std::numeric_limits<std::uint64_t>::max()) {
        fail(SCGS_V04_INTERNAL_ERROR, "The native output length overflowed.");
    }
    const std::uint64_t required = static_cast<std::uint64_t>(payload.size()) + 1U;
    *required_bytes = required;
    if (capacity < required) {
        fail(SCGS_V04_BUFFER_TOO_SMALL, "The output buffer is too small.");
    }
    if (buffer == nullptr) {
        fail(SCGS_V04_INVALID_ARGUMENT, "The output buffer is null.");
    }
    std::memcpy(buffer, payload.data(), payload.size());
    buffer[payload.size()] = '\0';
    return SCGS_V04_OK;
}

scgs_v04_native_code write_json(
    const Json& value,
    char* buffer,
    const std::uint64_t capacity,
    std::uint64_t* required_bytes) {
    return write_bytes(value.dump(), buffer, capacity, required_bytes);
}

struct GameEntry final {
    GameEntry(scgs::DeckList player0, scgs::DeckList player1, const scgs::GameConfig& config)
        : game(session_catalog(), std::move(player0), std::move(player1), config) {}

    scgs::Game game;
};

static_assert(
    std::is_nothrow_move_assignable_v<scgs::Game>,
    "transactional native commits require no-throw Game move assignment");

std::mutex g_registry_mutex;
std::unordered_map<scgs_v04_handle, std::shared_ptr<GameEntry>> g_registry;
scgs_v04_handle g_next_handle = 1U;

std::shared_ptr<GameEntry> find_game(const scgs_v04_handle handle) {
    if (handle == 0U) {
        fail(SCGS_V04_INVALID_HANDLE, "The game handle is invalid.");
    }
    const std::lock_guard<std::mutex> lock(g_registry_mutex);
    const auto iterator = g_registry.find(handle);
    if (iterator == g_registry.end()) {
        fail(SCGS_V04_INVALID_HANDLE, "The game handle is invalid or was already destroyed.");
    }
    return iterator->second;
}

scgs_v04_handle add_game(std::shared_ptr<GameEntry> entry) {
    const std::lock_guard<std::mutex> lock(g_registry_mutex);
    if (g_next_handle == 0U) {
        fail(SCGS_V04_OUT_OF_MEMORY, "The native handle space has been exhausted.");
    }
    const scgs_v04_handle handle = g_next_handle;
    if (g_next_handle == std::numeric_limits<scgs_v04_handle>::max()) {
        g_next_handle = 0U;
    } else {
        ++g_next_handle;
    }
    g_registry.emplace(handle, std::move(entry));
    return handle;
}

bool abi_is_supported(const std::uint32_t requested) noexcept {
    const std::uint32_t major = requested >> 16U;
    const std::uint32_t minor = requested & 0xFFFFU;
    return major == kAbiMajor && minor <= kAbiMinor;
}

} // namespace

extern "C" {

uint32_t SCGS_V04_CALL scgs_v04_abi_version(void) {
    clear_error();
    return SCGS_V04_ABI_VERSION;
}

scgs_v04_native_code SCGS_V04_CALL scgs_v04_create(
    const std::uint32_t requested_abi,
    const char* config_json,
    const std::uint64_t config_bytes,
    scgs_v04_handle* out_handle) {
    return protect([&]() -> scgs_v04_native_code {
        if (out_handle == nullptr) {
            fail(SCGS_V04_INVALID_ARGUMENT, "The output handle pointer is null.");
        }
        *out_handle = 0U;
        if (!abi_is_supported(requested_abi)) {
            fail(SCGS_V04_ABI_MISMATCH, "The requested native ABI version is not supported.");
        }
        ParsedConfig config = parse_config(parse_payload(config_json, config_bytes));
        auto entry = std::make_shared<GameEntry>(
            std::move(config.player0_deck), std::move(config.player1_deck), config.game_config);
        *out_handle = add_game(std::move(entry));
        return SCGS_V04_OK;
    });
}

scgs_v04_native_code SCGS_V04_CALL scgs_v04_destroy(const scgs_v04_handle handle) {
    return protect([&]() -> scgs_v04_native_code {
        if (handle == 0U) {
            return SCGS_V04_OK;
        }
        const std::lock_guard<std::mutex> lock(g_registry_mutex);
        const auto iterator = g_registry.find(handle);
        if (iterator == g_registry.end()) {
            fail(SCGS_V04_INVALID_HANDLE, "The game handle is invalid or was already destroyed.");
        }
        g_registry.erase(iterator);
        return SCGS_V04_OK;
    });
}

scgs_v04_native_code SCGS_V04_CALL scgs_v04_start(
    const scgs_v04_handle handle,
    std::uint32_t* out_engine_code) {
    return protect([&]() -> scgs_v04_native_code {
        if (out_engine_code == nullptr) {
            fail(SCGS_V04_INVALID_ARGUMENT, "The engine-code output pointer is null.");
        }
        *out_engine_code = SCGS_V04_NO_ENGINE_CODE;
        const std::shared_ptr<GameEntry> entry = find_game(handle);
        // Keep native failures strongly exception-safe as well as ordinary
        // engine failures: deck/event allocation during start must not leave
        // the registered handle partially initialized.
        scgs::Game candidate = entry->game;
        const scgs::Status status = candidate.start();
        *out_engine_code = map_error(status.code);
        if (status) {
            entry->game = std::move(candidate);
        }
        return SCGS_V04_OK;
    });
}

scgs_v04_native_code SCGS_V04_CALL scgs_v04_get_view_json(
    const scgs_v04_handle handle,
    const std::uint32_t viewer,
    char* buffer,
    const std::uint64_t capacity,
    std::uint64_t* required_bytes) {
    return protect([&]() -> scgs_v04_native_code {
        prepare_output(required_bytes);
        const scgs::PlayerId parsed_viewer = parse_viewer(viewer);
        const std::shared_ptr<GameEntry> entry = find_game(handle);
        const scgs::MatchView view = entry->game.make_view(parsed_viewer);
        Json output = make_output(view.revision);
        output["view"] = serialize_match_view(view);
        return write_json(output, buffer, capacity, required_bytes);
    });
}

scgs_v04_native_code SCGS_V04_CALL scgs_v04_list_legal_actions_json(
    const scgs_v04_handle handle,
    const char* query_json,
    const std::uint64_t query_bytes,
    char* buffer,
    const std::uint64_t capacity,
    std::uint64_t* required_bytes) {
    return protect([&]() -> scgs_v04_native_code {
        prepare_output(required_bytes);
        const scgs::ActionQuery query = parse_query(parse_payload(query_json, query_bytes));
        const std::shared_ptr<GameEntry> entry = find_game(handle);
        const std::vector<scgs::LegalAction> actions = entry->game.list_legal_actions(query);
        Json values = Json::array();
        for (const scgs::LegalAction& action : actions) {
            values.push_back(serialize_legal_action(action));
        }
        Json output = make_output(entry->game.revision());
        output["actions"] = std::move(values);
        return write_json(output, buffer, capacity, required_bytes);
    });
}

scgs_v04_native_code SCGS_V04_CALL scgs_v04_list_valid_targets_json(
    const scgs_v04_handle handle,
    const char* query_json,
    const std::uint64_t query_bytes,
    char* buffer,
    const std::uint64_t capacity,
    std::uint64_t* required_bytes) {
    return protect([&]() -> scgs_v04_native_code {
        prepare_output(required_bytes);
        const scgs::ActionQuery query = parse_query(parse_payload(query_json, query_bytes));
        const std::shared_ptr<GameEntry> entry = find_game(handle);
        const std::vector<scgs::Target> targets = entry->game.list_valid_targets(query);
        Json values = Json::array();
        for (const scgs::Target& target : targets) {
            values.push_back(serialize_target(target));
        }
        Json output = make_output(entry->game.revision());
        output["targets"] = std::move(values);
        return write_json(output, buffer, capacity, required_bytes);
    });
}

scgs_v04_native_code SCGS_V04_CALL scgs_v04_list_valid_slots_json(
    const scgs_v04_handle handle,
    const char* query_json,
    const std::uint64_t query_bytes,
    char* buffer,
    const std::uint64_t capacity,
    std::uint64_t* required_bytes) {
    return protect([&]() -> scgs_v04_native_code {
        prepare_output(required_bytes);
        const scgs::ActionQuery query = parse_query(parse_payload(query_json, query_bytes));
        const std::shared_ptr<GameEntry> entry = find_game(handle);
        const std::vector<std::size_t> slots = entry->game.list_valid_slots(query);
        Json values = Json::array();
        for (const std::size_t slot : slots) {
            values.push_back(static_cast<std::uint64_t>(slot));
        }
        Json output = make_output(entry->game.revision());
        output["slots"] = std::move(values);
        return write_json(output, buffer, capacity, required_bytes);
    });
}

scgs_v04_native_code SCGS_V04_CALL scgs_v04_list_valid_donors_json(
    const scgs_v04_handle handle,
    const char* query_json,
    const std::uint64_t query_bytes,
    char* buffer,
    const std::uint64_t capacity,
    std::uint64_t* required_bytes) {
    return protect([&]() -> scgs_v04_native_code {
        prepare_output(required_bytes);
        const scgs::ActionQuery query = parse_query(parse_payload(query_json, query_bytes));
        const std::shared_ptr<GameEntry> entry = find_game(handle);
        const std::vector<scgs::InstanceId> donors = entry->game.list_valid_donors(query);
        Json output = make_output(entry->game.revision());
        output["donors"] = donors;
        return write_json(output, buffer, capacity, required_bytes);
    });
}

scgs_v04_native_code SCGS_V04_CALL scgs_v04_preview_payment_json(
    const scgs_v04_handle handle,
    const char* command_json,
    const std::uint64_t command_bytes,
    char* buffer,
    const std::uint64_t capacity,
    std::uint64_t* required_bytes) {
    return protect([&]() -> scgs_v04_native_code {
        prepare_output(required_bytes);
        const scgs::GameCommand command = parse_command(parse_payload(command_json, command_bytes));
        const std::shared_ptr<GameEntry> entry = find_game(handle);
        const scgs::PaymentPreview payment = entry->game.preview_payment(command);
        Json output = make_output(entry->game.revision());
        output["payment"] = serialize_payment(payment);
        return write_json(output, buffer, capacity, required_bytes);
    });
}

scgs_v04_native_code SCGS_V04_CALL scgs_v04_get_reaction_context_json(
    const scgs_v04_handle handle,
    const std::uint32_t viewer,
    char* buffer,
    const std::uint64_t capacity,
    std::uint64_t* required_bytes) {
    return protect([&]() -> scgs_v04_native_code {
        prepare_output(required_bytes);
        const scgs::PlayerId parsed_viewer = parse_viewer(viewer);
        const std::shared_ptr<GameEntry> entry = find_game(handle);
        const scgs::ReactionContext reaction = entry->game.get_reaction_context(parsed_viewer);
        Json output = make_output(entry->game.revision());
        output["reaction"] = serialize_reaction(reaction);
        return write_json(output, buffer, capacity, required_bytes);
    });
}

scgs_v04_native_code SCGS_V04_CALL scgs_v04_submit_command_json(
    const scgs_v04_handle handle,
    const char* command_json,
    const std::uint64_t command_bytes,
    std::uint32_t* out_engine_code) {
    return protect([&]() -> scgs_v04_native_code {
        if (out_engine_code == nullptr) {
            fail(SCGS_V04_INVALID_ARGUMENT, "The engine-code output pointer is null.");
        }
        *out_engine_code = SCGS_V04_NO_ENGINE_CODE;
        const scgs::GameCommand command = parse_command(parse_payload(command_json, command_bytes));
        const std::shared_ptr<GameEntry> entry = find_game(handle);
        *out_engine_code = map_error(entry->game.submit_command(command).code);
        return SCGS_V04_OK;
    });
}

scgs_v04_native_code SCGS_V04_CALL scgs_v04_read_events_json(
    const scgs_v04_handle handle,
    const std::uint32_t viewer,
    const std::uint64_t after_sequence,
    char* buffer,
    const std::uint64_t capacity,
    std::uint64_t* required_bytes) {
    return protect([&]() -> scgs_v04_native_code {
        prepare_output(required_bytes);
        const scgs::PlayerId parsed_viewer = parse_viewer(viewer);
        const std::shared_ptr<GameEntry> entry = find_game(handle);
        const std::vector<scgs::GameEventView> events =
            entry->game.read_events(parsed_viewer, after_sequence);
        Json values = Json::array();
        std::uint64_t last_sequence = after_sequence;
        for (const scgs::GameEventView& event : events) {
            values.push_back(serialize_event(event));
            last_sequence = std::max(last_sequence, event.sequence);
        }
        Json output = make_output(entry->game.revision());
        output["last_sequence"] = last_sequence;
        output["events"] = std::move(values);
        return write_json(output, buffer, capacity, required_bytes);
    });
}

scgs_v04_native_code SCGS_V04_CALL scgs_v04_get_last_error(
    char* buffer,
    const std::uint64_t capacity,
    std::uint64_t* required_bytes) {
    try {
        if (required_bytes == nullptr) {
            return SCGS_V04_INVALID_ARGUMENT;
        }
        if (g_last_error.size() >= std::numeric_limits<std::uint64_t>::max()) {
            return SCGS_V04_INTERNAL_ERROR;
        }
        const std::uint64_t required = static_cast<std::uint64_t>(g_last_error.size()) + 1U;
        *required_bytes = required;
        if (capacity < required) {
            return SCGS_V04_BUFFER_TOO_SMALL;
        }
        if (buffer == nullptr) {
            return SCGS_V04_INVALID_ARGUMENT;
        }
        std::memcpy(buffer, g_last_error.data(), g_last_error.size());
        buffer[g_last_error.size()] = '\0';
        return SCGS_V04_OK;
    } catch (...) {
        return SCGS_V04_INTERNAL_ERROR;
    }
}

} // extern "C"
