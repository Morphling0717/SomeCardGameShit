// SPDX-License-Identifier: GPL-3.0-or-later

#include "scgs/native_api_v05.h"
#include "scgs/product_game.hpp"

#include <nlohmann/json.hpp>

#include <algorithm>
#include <array>
#include <atomic>
#include <cstring>
#include <limits>
#include <memory>
#include <mutex>
#include <new>
#include <optional>
#include <random>
#include <span>
#include <stdexcept>
#include <string>
#include <string_view>
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>

namespace {

using Json = nlohmann::ordered_json;

constexpr std::uint64_t kMaximumInputBytes = 1024U * 1024U;
constexpr std::uint32_t kAbiMajor = 2U;
constexpr std::uint32_t kAbiMinor = 0U;

std::atomic<std::uint64_t> g_next_opaque_session_nonce{1U};

std::uint64_t allocate_opaque_session_nonce() {
    std::uint64_t current = g_next_opaque_session_nonce.load(std::memory_order_relaxed);
    while (current != std::numeric_limits<std::uint64_t>::max()) {
        if (g_next_opaque_session_nonce.compare_exchange_weak(
                current,
                current + 1U,
                std::memory_order_relaxed,
                std::memory_order_relaxed)) {
            return current;
        }
    }
    throw std::bad_alloc{};
}

// SplitMix64 is a bijection over uint64. Combined with the monotonic nonce it
// gives each process-local session a distinct, semantically opaque namespace
// without exposing the native handle or any card identity.
std::uint64_t mix_opaque_nonce(std::uint64_t value) noexcept {
    value += 0x9E3779B97F4A7C15ULL;
    value = (value ^ (value >> 30U)) * 0xBF58476D1CE4E5B9ULL;
    value = (value ^ (value >> 27U)) * 0x94D049BB133111EBULL;
    return value ^ (value >> 31U);
}

std::string make_opaque_session_namespace() {
    return std::to_string(mix_opaque_nonce(allocate_opaque_session_nonce()));
}

static_assert(static_cast<std::uint8_t>(scgs::v2::CardKind::Follower) == 0U);
static_assert(static_cast<std::uint8_t>(scgs::v2::CardKind::Field) == 4U);
static_assert(static_cast<std::uint8_t>(scgs::v2::Zone::MainBoard) == 3U);
static_assert(static_cast<std::uint8_t>(scgs::v2::Zone::Field) == 8U);
static_assert(static_cast<std::uint8_t>(scgs::v2::ActionKind::Mulligan) == 0U);
static_assert(static_cast<std::uint8_t>(scgs::v2::ActionKind::ResolveChoice) == 13U);

// Engine codes shared with v04. Gate 5B reserves three choice-boundary codes
// without exposing a second rules implementation in this transport adapter.
constexpr std::uint32_t kEngineOk = 0U;
constexpr std::uint32_t kEngineInvalidPhase = 1U;
constexpr std::uint32_t kEngineInvalidPlayer = 3U;
constexpr std::uint32_t kEngineInvalidCard = 4U;
constexpr std::uint32_t kEngineGameOver = 34U;
constexpr std::uint32_t kEngineStaleRevision = 35U;
constexpr std::uint32_t kEngineChoicePending = 36U;
constexpr std::uint32_t kEngineNoPendingChoice = 37U;
constexpr std::uint32_t kEngineInvalidChoice = 38U;
constexpr std::uint32_t kEngineChoiceNotOwned = 39U;
constexpr std::uint32_t kEngineInvalidMode = 40U;
constexpr std::uint32_t kEngineInvalidAdditionalCost = 41U;
constexpr std::uint32_t kEngineMatchAlreadyStarted = 30U;
constexpr std::uint32_t kEngineMatchNotStarted = 31U;
constexpr std::uint32_t kEngineMulliganAlreadyDone = 32U;

thread_local std::string g_last_error;

struct NativeFailure final {
    scgs_v05_native_code code;
    const char* message;
};

[[noreturn]] void fail(const scgs_v05_native_code code, const char* message) {
    throw NativeFailure{code, message};
}

void clear_error() { g_last_error.clear(); }

void set_error(const char* message) noexcept {
    try {
        g_last_error = message;
    } catch (...) {
        g_last_error.clear();
    }
}

template <typename Callback>
scgs_v05_native_code protect(Callback&& callback) noexcept {
    clear_error();
    try {
        return callback();
    } catch (const NativeFailure& failure) {
        set_error(failure.message);
        return failure.code;
    } catch (const std::bad_alloc&) {
        set_error("The native library could not allocate memory.");
        return SCGS_V05_OUT_OF_MEMORY;
    } catch (...) {
        set_error("The native library encountered an internal error.");
        return SCGS_V05_INTERNAL_ERROR;
    }
}

bool valid_utf8(const std::string_view value) noexcept {
    const auto* bytes = reinterpret_cast<const unsigned char*>(value.data());
    std::size_t index = 0U;
    while (index < value.size()) {
        const unsigned char first = bytes[index];
        if (first <= 0x7FU) {
            ++index;
            continue;
        }
        std::size_t remaining = 0U;
        unsigned char minimum_second = 0x80U;
        unsigned char maximum_second = 0xBFU;
        if (first >= 0xC2U && first <= 0xDFU) {
            remaining = 1U;
        } else if (first >= 0xE0U && first <= 0xEFU) {
            remaining = 2U;
            if (first == 0xE0U) {
                minimum_second = 0xA0U;
            } else if (first == 0xEDU) {
                maximum_second = 0x9FU;
            }
        } else if (first >= 0xF0U && first <= 0xF4U) {
            remaining = 3U;
            if (first == 0xF0U) {
                minimum_second = 0x90U;
            } else if (first == 0xF4U) {
                maximum_second = 0x8FU;
            }
        } else {
            return false;
        }
        if (index + remaining >= value.size() ||
            bytes[index + 1U] < minimum_second || bytes[index + 1U] > maximum_second) {
            return false;
        }
        for (std::size_t offset = 2U; offset <= remaining; ++offset) {
            if ((bytes[index + offset] & 0xC0U) != 0x80U) {
                return false;
            }
        }
        index += remaining + 1U;
    }
    return true;
}

Json parse_payload(const char* data, const std::uint64_t byte_count) {
    if (byte_count > kMaximumInputBytes) {
        fail(SCGS_V05_PAYLOAD_TOO_LARGE, "The JSON payload exceeds the 1 MiB limit.");
    }
    if (data == nullptr || byte_count == 0U) {
        fail(SCGS_V05_INVALID_ARGUMENT, "A non-empty JSON payload is required.");
    }
    if (byte_count > static_cast<std::uint64_t>(std::numeric_limits<std::size_t>::max())) {
        fail(SCGS_V05_PAYLOAD_TOO_LARGE, "The JSON payload cannot be represented by this process.");
    }
    const std::string_view bytes(data, static_cast<std::size_t>(byte_count));
    if (!valid_utf8(bytes)) {
        fail(SCGS_V05_INVALID_UTF8, "The JSON payload is not valid UTF-8.");
    }
    Json payload = Json::parse(bytes.begin(), bytes.end(), nullptr, false);
    if (payload.is_discarded()) {
        fail(SCGS_V05_INVALID_JSON, "The JSON payload is malformed.");
    }
    if (!payload.is_object()) {
        fail(SCGS_V05_SCHEMA_MISMATCH, "The JSON root must be an object.");
    }
    return payload;
}

const Json& require_field(const Json& object, const char* name) {
    const auto iterator = object.find(name);
    if (iterator == object.end()) {
        fail(SCGS_V05_SCHEMA_MISMATCH, "The JSON payload is missing a required field.");
    }
    return *iterator;
}

const Json* optional_field(const Json& object, const char* name) {
    const auto iterator = object.find(name);
    return iterator == object.end() ? nullptr : std::addressof(*iterator);
}

void require_only_fields(
    const Json& object,
    const std::span<const std::string_view> allowed) {
    for (const auto& [name, value] : object.items()) {
        (void)value;
        if (std::find(allowed.begin(), allowed.end(), name) == allowed.end()) {
            fail(SCGS_V05_SCHEMA_MISMATCH, "The JSON payload contains an unknown input field.");
        }
    }
}

std::uint64_t require_unsigned_value(const Json& value) {
    if (value.is_number_unsigned()) {
        return value.get<std::uint64_t>();
    }
    if (value.is_number_integer()) {
        const std::int64_t candidate = value.get<std::int64_t>();
        if (candidate >= 0) {
            return static_cast<std::uint64_t>(candidate);
        }
    }
    fail(SCGS_V05_SCHEMA_MISMATCH, "A JSON field has the wrong integer type or range.");
}

std::uint64_t require_unsigned(const Json& object, const char* name) {
    return require_unsigned_value(require_field(object, name));
}

std::uint32_t require_u32(const Json& object, const char* name) {
    const std::uint64_t value = require_unsigned(object, name);
    if (value > std::numeric_limits<std::uint32_t>::max()) {
        fail(SCGS_V05_SCHEMA_MISMATCH, "A JSON integer is outside the uint32 range.");
    }
    return static_cast<std::uint32_t>(value);
}

std::string require_string_value(const Json& value) {
    if (!value.is_string()) {
        fail(SCGS_V05_SCHEMA_MISMATCH, "A JSON field must be a string.");
    }
    return value.get<std::string>();
}

std::string require_string(const Json& object, const char* name) {
    return require_string_value(require_field(object, name));
}

bool require_boolean_value(const Json& value) {
    if (!value.is_boolean()) {
        fail(SCGS_V05_SCHEMA_MISMATCH, "A JSON field must be a boolean.");
    }
    return value.get<bool>();
}

void require_schema(const Json& object) {
    if (require_u32(object, "schema_version") != SCGS_V05_SCHEMA_VERSION) {
        fail(SCGS_V05_SCHEMA_MISMATCH, "The JSON schema version is not supported.");
    }
}

std::uint32_t parse_player_value(const Json& value) {
    const std::uint64_t player = require_unsigned_value(value);
    if (player > 1U) {
        fail(SCGS_V05_SCHEMA_MISMATCH, "The JSON player value is not supported.");
    }
    return static_cast<std::uint32_t>(player);
}

std::uint32_t parse_player(const Json& object, const char* name) {
    return parse_player_value(require_field(object, name));
}

std::uint32_t parse_viewer(const std::uint32_t viewer) {
    if (viewer > 1U) {
        fail(SCGS_V05_INVALID_ARGUMENT, "The viewer value is outside the supported range.");
    }
    return viewer;
}

std::uint32_t parse_action_value(const Json& value) {
    const std::uint64_t action = require_unsigned_value(value);
    if (action > 13U) {
        fail(SCGS_V05_SCHEMA_MISMATCH, "The JSON action value is not supported.");
    }
    return static_cast<std::uint32_t>(action);
}

std::vector<std::uint64_t> parse_instance_array(const Json& value) {
    if (!value.is_array()) {
        fail(SCGS_V05_SCHEMA_MISMATCH, "A JSON card selection must be an array.");
    }
    std::vector<std::uint64_t> result;
    result.reserve(value.size());
    for (const Json& item : value) {
        result.push_back(require_unsigned_value(item));
    }
    return result;
}

std::vector<std::string> parse_string_array(const Json& value) {
    if (!value.is_array()) {
        fail(SCGS_V05_SCHEMA_MISMATCH, "A JSON option selection must be an array.");
    }
    std::vector<std::string> result;
    result.reserve(value.size());
    for (const Json& item : value) {
        result.push_back(require_string_value(item));
    }
    return result;
}

struct TargetShape final {
    std::uint32_t kind = 0U;
    std::uint32_t player = 0U;
    std::optional<std::uint64_t> permanent;
};

TargetShape parse_target(const Json& value) {
    if (!value.is_object()) {
        fail(SCGS_V05_SCHEMA_MISMATCH, "A JSON target must be an object.");
    }
    static constexpr std::array<std::string_view, 3U> kTargetFields{
        "kind", "player", "permanent"};
    require_only_fields(value, kTargetFields);
    const std::uint64_t kind = require_unsigned(value, "kind");
    const std::uint32_t player = parse_player(value, "player");
    if (kind == 0U) {
        if (optional_field(value, "permanent") != nullptr) {
            fail(SCGS_V05_SCHEMA_MISMATCH, "A leader target cannot name a permanent.");
        }
        return TargetShape{0U, player, std::nullopt};
    }
    if (kind == 1U) {
        const std::uint64_t permanent = require_unsigned(value, "permanent");
        if (permanent == 0U) {
            fail(SCGS_V05_SCHEMA_MISMATCH, "A permanent target requires a non-zero instance identifier.");
        }
        return TargetShape{1U, player, permanent};
    }
    fail(SCGS_V05_SCHEMA_MISMATCH, "The JSON target kind is not supported.");
}

struct RequestShape final {
    std::uint32_t player = 0U;
    std::optional<std::uint32_t> action;
    std::optional<std::uint64_t> source;
    std::optional<TargetShape> target;
    std::optional<std::uint64_t> slot;
    std::optional<std::string> mode_id;
    std::optional<std::string> choice_id;
    std::optional<std::vector<std::uint64_t>> mulligan_cards;
    std::optional<std::vector<std::string>> selected_option_ids;
    std::optional<std::vector<std::uint64_t>> additional_cost_cards;
    std::optional<bool> use_advance;
    std::uint64_t expected_revision = 0U;
};

RequestShape parse_request(const Json& object, const bool require_action) {
    require_schema(object);
    static constexpr std::array<std::string_view, 13U> kRequestFields{
        "schema_version",
        "player",
        "action",
        "source",
        "target",
        "slot",
        "mode_id",
        "choice_id",
        "mulligan_cards",
        "selected_option_ids",
        "additional_cost_cards",
        "use_advance",
        "expected_revision",
    };
    require_only_fields(object, kRequestFields);
    RequestShape result;
    result.player = parse_player(object, "player");
    result.expected_revision = require_unsigned(object, "expected_revision");
    if (const Json* value = optional_field(object, "action")) {
        result.action = parse_action_value(*value);
    } else if (require_action) {
        fail(SCGS_V05_SCHEMA_MISMATCH, "A command requires an action.");
    }
    if (const Json* value = optional_field(object, "source")) {
        result.source = require_unsigned_value(*value);
    }
    if (const Json* value = optional_field(object, "target")) {
        if (value->is_null()) {
            fail(SCGS_V05_SCHEMA_MISMATCH, "Optional JSON fields must be omitted instead of null.");
        }
        result.target = parse_target(*value);
    }
    if (const Json* value = optional_field(object, "slot")) {
        result.slot = require_unsigned_value(*value);
    }
    if (const Json* value = optional_field(object, "mode_id")) {
        result.mode_id = require_string_value(*value);
        if (result.mode_id->empty()) {
            fail(SCGS_V05_SCHEMA_MISMATCH, "A mode identifier cannot be empty.");
        }
    }
    if (const Json* value = optional_field(object, "choice_id")) {
        result.choice_id = require_string_value(*value);
        if (result.choice_id->empty()) {
            fail(SCGS_V05_SCHEMA_MISMATCH, "A choice identifier cannot be empty.");
        }
    }
    if (const Json* value = optional_field(object, "mulligan_cards")) {
        result.mulligan_cards = parse_instance_array(*value);
    }
    if (const Json* value = optional_field(object, "selected_option_ids")) {
        result.selected_option_ids = parse_string_array(*value);
    }
    if (const Json* value = optional_field(object, "additional_cost_cards")) {
        result.additional_cost_cards = parse_instance_array(*value);
    }
    if (const Json* value = optional_field(object, "use_advance")) {
        result.use_advance = require_boolean_value(*value);
    }
    return result;
}

enum class RequestField : std::uint8_t {
    Source,
    Target,
    Slot,
    Mode,
    Choice,
    MulliganCards,
    SelectedOptions,
    AdditionalCosts,
    UseAdvance,
};

bool action_allows_field(const std::uint32_t action, const RequestField field) noexcept {
    switch (action) {
        case 0U:
            return field == RequestField::MulliganCards;
        case 1U:
        case 2U:
        case 11U:
            return field == RequestField::Source || field == RequestField::Target ||
                field == RequestField::Slot || field == RequestField::Mode ||
                field == RequestField::UseAdvance;
        case 3U:
            return field == RequestField::Source || field == RequestField::Slot ||
                field == RequestField::Mode || field == RequestField::UseAdvance;
        case 4U:
            return field == RequestField::Source || field == RequestField::Target;
        case 5U:
            return field == RequestField::Source || field == RequestField::Target ||
                field == RequestField::Mode;
        case 6U:
            return field == RequestField::Source || field == RequestField::Target ||
                field == RequestField::Slot || field == RequestField::Mode ||
                field == RequestField::AdditionalCosts || field == RequestField::UseAdvance;
        case 7U:
            return field == RequestField::Source || field == RequestField::Target ||
                field == RequestField::Mode;
        case 12U:
            return field == RequestField::Source || field == RequestField::Target ||
                field == RequestField::Mode || field == RequestField::UseAdvance;
        case 13U:
            return field == RequestField::Choice || field == RequestField::SelectedOptions;
        default:
            return false;
    }
}

std::uint32_t invalid_field_code(const RequestField field) noexcept {
    switch (field) {
        case RequestField::Target:
            return 6U;
        case RequestField::Slot:
            return 7U;
        case RequestField::Mode:
            return kEngineInvalidMode;
        case RequestField::Choice:
        case RequestField::SelectedOptions:
            return kEngineInvalidChoice;
        case RequestField::AdditionalCosts:
            return kEngineInvalidAdditionalCost;
        default:
            return kEngineInvalidCard;
    }
}

std::uint32_t validate_request_shape(const RequestShape& request) noexcept {
    if (!request.action.has_value()) {
        if (request.source.has_value() || request.target.has_value() || request.slot.has_value() ||
            request.mode_id.has_value() || request.choice_id.has_value() ||
            request.mulligan_cards.has_value() || request.selected_option_ids.has_value() ||
            request.additional_cost_cards.has_value() || request.use_advance.has_value()) {
            return kEngineInvalidCard;
        }
        return kEngineOk;
    }

    const std::uint32_t action = *request.action;
    const std::array<std::pair<RequestField, bool>, 9U> fields{{
        {RequestField::Source, request.source.has_value()},
        {RequestField::Target, request.target.has_value()},
        {RequestField::Slot, request.slot.has_value()},
        {RequestField::Mode, request.mode_id.has_value()},
        {RequestField::Choice, request.choice_id.has_value()},
        {RequestField::MulliganCards, request.mulligan_cards.has_value()},
        {RequestField::SelectedOptions, request.selected_option_ids.has_value()},
        {RequestField::AdditionalCosts, request.additional_cost_cards.has_value()},
        {RequestField::UseAdvance, request.use_advance.has_value()},
    }};
    for (const auto& [field, present] : fields) {
        if (present && !action_allows_field(action, field)) {
            return invalid_field_code(field);
        }
    }
    return kEngineOk;
}

struct DeckIdentity final {
    std::string deck_id;
    std::string profession_id;
    std::string series_id;
    std::string sample_design_id;
    std::string sample_name;
};

DeckIdentity parse_deck(const std::string& deck_id) {
    if (deck_id == "oathguard_luminous_oath_v1") {
        return {deck_id, "oathguard", "luminous_oath", "LO-01", "曜誓传令使·菲娅"};
    }
    if (deck_id == "pactmage_abyssal_pact_v1") {
        return {deck_id, "pactmage", "abyssal_pact", "AP-01", "渊契使魔·墨契"};
    }
    fail(SCGS_V05_SCHEMA_MISMATCH, "The requested product deck is not supported by schema 2.");
}

struct ParsedConfig final {
    std::array<DeckIdentity, 2U> decks;
    std::uint32_t seed;
    std::uint32_t first_player_mode;
    bool shuffle_decks;
};

ParsedConfig parse_config(const Json& object) {
    static constexpr std::array<std::string_view, 6U> kConfigFields{
        "schema_version",
        "player0_deck",
        "player1_deck",
        "random_seed",
        "first_player_mode",
        "shuffle_decks",
    };
    require_only_fields(object, kConfigFields);
    require_schema(object);
    const DeckIdentity player0 = parse_deck(require_string(object, "player0_deck"));
    const DeckIdentity player1 = parse_deck(require_string(object, "player1_deck"));
    std::uint32_t seed = std::random_device{}();
    if (const Json* value = optional_field(object, "random_seed")) {
        const std::uint64_t candidate = require_unsigned_value(*value);
        if (candidate > std::numeric_limits<std::uint32_t>::max()) {
            fail(SCGS_V05_SCHEMA_MISMATCH, "The random seed is outside the uint32 range.");
        }
        seed = static_cast<std::uint32_t>(candidate);
    }
    std::uint32_t first_player_mode = 0U;
    if (const Json* value = optional_field(object, "first_player_mode")) {
        const std::uint64_t candidate = require_unsigned_value(*value);
        if (candidate > 2U) {
            fail(SCGS_V05_SCHEMA_MISMATCH, "The first-player mode is not supported.");
        }
        first_player_mode = static_cast<std::uint32_t>(candidate);
    }
    bool shuffle_decks = true;
    if (const Json* value = optional_field(object, "shuffle_decks")) {
        shuffle_decks = require_boolean_value(*value);
    }
    return {{{player0, player1}}, seed, first_player_mode, shuffle_decks};
}

const scgs::v2::ProductDeckDefinition& find_locked_deck(const std::string_view deck_id) {
    static const std::vector<scgs::v2::ProductDeckDefinition> decks =
        scgs::v2::make_locked_product_decks();
    const auto found = std::find_if(decks.begin(), decks.end(), [&](const auto& deck) {
        return deck.deck_id == deck_id;
    });
    if (found == decks.end()) {
        fail(SCGS_V05_SCHEMA_MISMATCH, "The requested product deck is not supported by schema 2.");
    }
    return *found;
}

scgs::v2::ProductGameConfig make_game_config(const ParsedConfig& config) {
    scgs::v2::ProductGameConfig result;
    for (std::size_t index = 0; index < scgs::kPlayerCount; ++index) {
        const auto& deck = find_locked_deck(config.decks[index].deck_id);
        result.main_decks[index] = deck.main_deck;
        result.standby_decks[index] = deck.standby;
        result.professions[index] = deck.profession_id;
        result.evolution_charge_policies[index] = deck.profession_id == "oathguard"
            ? scgs::v2::EvolutionChargePolicy::RepairToZero
            : scgs::v2::EvolutionChargePolicy::FutureUseAtLeastTwo;
    }
    result.seed = config.seed;
    result.first_player_mode = static_cast<scgs::FirstPlayerMode>(config.first_player_mode);
    result.shuffle = config.shuffle_decks;
    return result;
}

struct ProductSession final {
    explicit ProductSession(ParsedConfig value)
        : config(std::move(value)),
          game(scgs::v2::make_locked_product_catalog(), make_game_config(config)),
          opaque_namespace(make_opaque_session_namespace()) {}

    void sync_choice_tokens() {
        const auto& pending = game.pending_choice();
        if (!pending.has_value()) {
            internal_choice_id.reset();
            external_choice_id.clear();
            external_to_internal_options.clear();
            internal_to_external_options.clear();
            return;
        }
        if (internal_choice_id == pending->choice_id) {
            return;
        }
        internal_choice_id = pending->choice_id;
        const std::string generation = std::to_string(next_choice_generation++);
        external_choice_id = "choice-" + opaque_namespace + '-' + generation;
        external_to_internal_options.clear();
        internal_to_external_options.clear();
        for (std::size_t index = 0; index < pending->options.size(); ++index) {
            const std::string external = "option-" + opaque_namespace + '-' + generation + '-' +
                std::to_string(index + 1U);
            external_to_internal_options.emplace(external, pending->options[index].option_id);
            internal_to_external_options.emplace(pending->options[index].option_id, external);
        }
    }

    [[nodiscard]] std::string external_option(const std::string_view internal) const {
        const auto found = internal_to_external_options.find(std::string(internal));
        return found == internal_to_external_options.end() ? std::string{} : found->second;
    }

    ParsedConfig config;
    scgs::v2::ProductGame game;
    const std::string opaque_namespace;
    std::optional<scgs::v2::ChoiceId> internal_choice_id;
    std::string external_choice_id;
    std::unordered_map<std::string, std::string> external_to_internal_options;
    std::unordered_map<std::string, std::string> internal_to_external_options;
    std::uint64_t next_choice_generation = 1U;
    std::mutex mutex;
};

std::uint32_t map_engine_code(
    const scgs::v2::ProductGameError code,
    const std::optional<scgs::v2::ActionKind> action = std::nullopt) noexcept {
    using Error = scgs::v2::ProductGameError;
    switch (code) {
        case Error::Ok: return kEngineOk;
        case Error::InvalidPlayer: return kEngineInvalidPlayer;
        case Error::NotStarted: return kEngineMatchNotStarted;
        case Error::AlreadyStarted: return kEngineMatchAlreadyStarted;
        case Error::StaleRevision: return kEngineStaleRevision;
        case Error::MatchFinished: return kEngineGameOver;
        case Error::WrongPhase: return kEngineInvalidPhase;
        case Error::NotActivePlayer: return 2U;
        case Error::InvalidZone: return 5U;
        case Error::InvalidSlot:
        case Error::SlotOccupied: return 7U;
        case Error::MainBoardFull: return 10U;
        case Error::TacticZoneFull: return 11U;
        case Error::InvalidTarget: return 6U;
        case Error::InvalidMode: return kEngineInvalidMode;
        case Error::InsufficientPP: return 8U;
        case Error::AdvanceUnavailable: return 20U;
        case Error::FutureAlreadyUsed: return 19U;
        case Error::EvolutionUnavailable: return 15U;
        case Error::DeploymentUnavailable: return 22U;
        case Error::ReactionUnavailable: return 26U;
        case Error::ChoicePending: return kEngineChoicePending;
        case Error::NoPendingChoice: return kEngineNoPendingChoice;
        case Error::MulliganAlreadyDone: return kEngineMulliganAlreadyDone;
        case Error::ChoiceNotOwned: return kEngineChoiceNotOwned;
        case Error::InvalidSelection:
            if (action == scgs::v2::ActionKind::Mulligan) {
                return 33U;
            }
            if (action == scgs::v2::ActionKind::Deploy) {
                return kEngineInvalidAdditionalCost;
            }
            return kEngineInvalidChoice;
        case Error::InvalidConfiguration:
        case Error::InvalidCommand:
        case Error::InvalidCard:
        case Error::InvalidCardKind:
        case Error::InternalInvariant:
            return kEngineInvalidCard;
    }
    return kEngineInvalidCard;
}

const char* engine_message(const std::uint32_t code) noexcept {
    switch (code) {
        case kEngineOk: return "ok";
        case kEngineInvalidPhase: return "invalid phase";
        case kEngineInvalidPlayer: return "invalid player";
        case kEngineInvalidCard: return "invalid card or command";
        case kEngineGameOver: return "game over";
        case kEngineStaleRevision: return "stale revision";
        case kEngineChoicePending: return "a product choice is pending";
        case kEngineNoPendingChoice: return "no pending choice";
        case kEngineInvalidChoice: return "invalid or unrelated choice fields";
        case kEngineInvalidMode: return "invalid or unrelated mode field";
        case kEngineInvalidAdditionalCost: return "invalid or unrelated additional cost fields";
        case kEngineChoiceNotOwned: return "choice is owned by the other player";
        case kEngineMatchNotStarted: return "match not started";
        case kEngineMulliganAlreadyDone: return "mulligan already done";
        default: return "engine failure";
    }
}

Json make_status(const std::uint32_t code, const std::string_view message) {
    return Json{{"engine_code", code}, {"message", message}};
}

bool trap_is_revealed(const ProductSession& session, const scgs::InstanceId card) {
    return std::any_of(session.game.events().begin(), session.game.events().end(), [&](const auto& event) {
        return event.kind == scgs::v2::ProductEventKind::TrapActivated && event.source == card;
    });
}

Json make_hidden_tactic(const scgs::v2::CardInstance& card) {
    return Json{
        {"name", ""},
        {"owner", static_cast<std::uint32_t>(card.owner)},
        {"controller", static_cast<std::uint32_t>(card.controller)},
        {"zone", static_cast<std::uint32_t>(scgs::v2::Zone::Tactic)},
        {"sequence", 0U},
        {"cost", 0},
        {"current_attack", 0},
        {"current_health", 0},
        {"maximum_health", 0},
        {"printed_keywords", 0U},
        {"permanent_keywords", 0U},
        {"turn_keywords", 0U},
        {"keywords", 0U},
        {"evolved", false},
        {"attacked_this_turn", false},
        {"entered_this_turn", false},
        {"face_down", true},
        {"countdown", 0}};
}

Json make_card(
    const ProductSession& session,
    const scgs::InstanceId card_id,
    const bool force_face_up = false) {
    const auto& board = session.game.board();
    const scgs::v2::CardInstance& card = board.instance(card_id);
    const scgs::v2::CardDefinition& definition = board.catalog().at(card.design_id);
    const bool face_down = !force_face_up && card.zone == scgs::v2::Zone::Tactic &&
        definition.kind == scgs::v2::CardKind::Trap;
    return Json{
        {"instance_id", card.id},
        {"design_id", definition.identity.design_id},
        {"profession_id", definition.identity.profession_id},
        {"series_id", definition.identity.series_id},
        {"neutral", definition.identity.neutral},
        {"kind", static_cast<std::uint32_t>(definition.kind)},
        {"name", definition.name},
        {"owner", static_cast<std::uint32_t>(card.owner)},
        {"controller", static_cast<std::uint32_t>(card.controller)},
        {"zone", static_cast<std::uint32_t>(card.zone)},
        {"sequence", card.sequence},
        {"cost", definition.cost},
        {"current_attack", card.current_attack},
        {"current_health", card.current_health},
        {"maximum_health", card.maximum_health},
        {"printed_keywords", card.keywords.printed},
        {"permanent_keywords", card.keywords.permanent},
        {"turn_keywords", card.keywords.turn},
        {"keywords", card.keywords.effective()},
        {"evolved", card.evolved},
        {"attacked_this_turn", card.attacked_this_turn},
        {"entered_this_turn", card.entered_this_turn},
        {"face_down", face_down},
        {"countdown", card.countdown}};
}

bool action_used_this_turn(
    const ProductSession& session,
    const scgs::PlayerId player,
    const scgs::v2::CardAvailability availability,
    const std::optional<scgs::v2::CardKind> kind = std::nullopt) {
    for (auto iterator = session.game.events().rbegin(); iterator != session.game.events().rend(); ++iterator) {
        if (iterator->kind == scgs::v2::ProductEventKind::TurnStarted && iterator->player == player) {
            break;
        }
        if (iterator->kind != scgs::v2::ProductEventKind::CardPlayed ||
            iterator->player != player || !iterator->source.has_value() ||
            !session.game.board().contains_instance(*iterator->source)) {
            continue;
        }
        const auto& definition = session.game.board().catalog().at(
            session.game.board().instance(*iterator->source).design_id);
        if (definition.availability == availability &&
            (!kind.has_value() || definition.kind == *kind)) {
            return true;
        }
    }
    return false;
}

Json make_player_view(
    const ProductSession& session,
    const std::uint32_t player,
    const std::uint32_t viewer) {
    const auto player_id = static_cast<scgs::PlayerId>(player);
    const auto& state = session.game.board().player(player_id);
    const auto& resources = session.game.resources(player_id);
    Json hand = Json::array();
    if (player == viewer) {
        for (const scgs::InstanceId card : state.hand) {
            hand.push_back(make_card(session, card));
        }
    }
    Json main_board = Json::array();
    for (const std::optional<scgs::InstanceId> card : state.main_board) {
        main_board.push_back(card.has_value() ? make_card(session, *card) : Json(nullptr));
    }
    Json tactics = Json::array();
    for (const std::optional<scgs::InstanceId> card : state.tactics) {
        if (!card.has_value()) {
            tactics.push_back(nullptr);
            continue;
        }
        const auto& instance = session.game.board().instance(*card);
        const auto& definition = session.game.board().catalog().at(instance.design_id);
        const bool hidden = player != viewer && definition.kind == scgs::v2::CardKind::Trap &&
            !trap_is_revealed(session, *card);
        tactics.push_back(hidden ? make_hidden_tactic(instance) :
            make_card(session, *card, player != viewer && definition.kind == scgs::v2::CardKind::Trap));
    }
    const auto public_cards = [&](const std::vector<scgs::InstanceId>& cards) {
        Json values = Json::array();
        for (const scgs::InstanceId card : cards) {
            values.push_back(make_card(session, card, true));
        }
        return values;
    };
    Json result{
        {"player", player},
        {"profession_id", session.config.decks[player].profession_id},
        {"leader_health", state.leader_health},
        {"maximum_leader_health", state.maximum_leader_health},
        {"current_pp", resources.current_pp},
        {"pp_capacity", resources.pp_capacity},
        {"cracks", resources.cracks},
        {"evolution_energy", resources.evolution_energy},
        {"own_turn_number", resources.own_turn_number},
        {"fatigue_count", resources.fatigue_count},
        {"mulligan_done", session.game.mulligan_complete(player_id)},
        {"evolution_used_this_turn", resources.evolved_this_turn},
        {"advance_used_this_turn", resources.future_used_this_turn},
        {"deploy_used_this_turn", action_used_this_turn(
            session, player_id, scgs::v2::CardAvailability::Standby)},
        {"trap_set_this_turn", action_used_this_turn(
            session, player_id, scgs::v2::CardAvailability::MainDeck, scgs::v2::CardKind::Trap)},
        {"deck_count", state.deck.size()},
        {"hand_count", state.hand.size()},
        {"hand", std::move(hand)},
        {"main_board", std::move(main_board)},
        {"tactics", std::move(tactics)},
        {"graveyard", public_cards(state.graveyard)},
        {"archive", public_cards(state.archive)},
        {"standby", public_cards(state.standby)}};
    if (state.field.has_value()) {
        result["field"] = make_card(session, *state.field, true);
    }
    return result;
}

Json target_json(const std::uint32_t kind, const scgs::PlayerId player,
    const std::optional<scgs::InstanceId> permanent = std::nullopt) {
    Json target{{"kind", kind}, {"player", static_cast<std::uint32_t>(player)}};
    if (permanent.has_value()) {
        target["permanent"] = *permanent;
    }
    return target;
}

std::optional<Json> command_target_json(
    const ProductSession& session,
    const scgs::v2::ProductGameCommand& command) {
    if (command.target.has_value() && session.game.board().contains_instance(*command.target)) {
        return target_json(1U, session.game.board().instance(*command.target).controller, command.target);
    }
    if (command.action == scgs::v2::ActionKind::Attack) {
        return target_json(0U, scgs::opponent(command.player));
    }
    return std::nullopt;
}

Json make_reaction(ProductSession& session, const std::uint32_t viewer) {
    const scgs::v2::ProductReactionContext context = session.game.reaction_context();
    Json eligible = Json::array();
    std::size_t eligible_count = 0U;
    if (context.pending && viewer == static_cast<std::uint32_t>(context.priority)) {
        for (const auto& action : session.game.list_legal_actions(context.priority)) {
            if (action.command.action == scgs::v2::ActionKind::ActivateTrap &&
                action.command.source.has_value()) {
                eligible.push_back(make_card(session, *action.command.source));
            }
        }
        eligible_count = eligible.size();
    }
    std::uint32_t window = 0U;
    if (context.pending) {
        window = context.origin_action == scgs::v2::ActionKind::Attack ? 3U :
            context.origin_action == scgs::v2::ActionKind::CastSpell ? 1U : 2U;
    }
    Json result{
        {"pending", context.pending},
        {"window", window},
        {"responder", static_cast<std::uint32_t>(context.pending ? context.priority : session.game.active_player())},
        {"subject", context.origin_source.value_or(0U)},
        {"depth", context.chain_size},
        {"eligible_count", eligible_count},
        {"eligible_traps", std::move(eligible)},
        {"revision", session.game.revision()}};
    if (context.pending && context.origin_source.has_value()) {
        Json origin{
            {"action", static_cast<std::uint32_t>(context.origin_action)},
            {"player", static_cast<std::uint32_t>(context.origin_player)},
            {"source", *context.origin_source}};
        scgs::v2::ProductGameCommand command;
        command.player = context.origin_player;
        command.action = context.origin_action;
        command.target = context.origin_target;
        if (const auto target = command_target_json(session, command)) {
            origin["target"] = *target;
        }
        result["origin"] = std::move(origin);
    }
    return result;
}

Json make_pending_choice(ProductSession& session, const std::uint32_t viewer) {
    session.sync_choice_tokens();
    const auto& pending = session.game.pending_choice();
    if (!pending.has_value()) {
        return Json{{"pending", false}, {"revision", session.game.revision()}};
    }
    Json result{
        {"pending", true},
        {"chooser", static_cast<std::uint32_t>(pending->chooser)},
        {"revision", session.game.revision()}};
    if (viewer != static_cast<std::uint32_t>(pending->chooser)) {
        return result;
    }
    result["choice_id"] = session.external_choice_id;
    result["kind"] = static_cast<std::uint32_t>(pending->kind);
    result["minimum_selections"] = pending->minimum;
    result["maximum_selections"] = pending->maximum;
    result["ordered"] = pending->ordered;
    Json options = Json::array();
    for (const scgs::v2::ChoiceOption& option : pending->options) {
        Json value{{"option_id", session.external_option(option.option_id)}};
        if (option.card.has_value()) {
            const auto& definition = session.game.board().catalog().at(
                session.game.board().instance(*option.card).design_id);
            value["label"] = definition.name;
            value["card"] = make_card(session, *option.card, true);
        } else {
            value["label"] = "option";
        }
        options.push_back(std::move(value));
    }
    result["options"] = std::move(options);
    return result;
}

std::uint32_t phase_value(const scgs::v2::ProductGamePhase phase) noexcept {
    switch (phase) {
        case scgs::v2::ProductGamePhase::NotStarted: return 0U;
        case scgs::v2::ProductGamePhase::Mulligan: return 1U;
        case scgs::v2::ProductGamePhase::Main:
        case scgs::v2::ProductGamePhase::Choice: return 2U;
        case scgs::v2::ProductGamePhase::Reaction: return 3U;
        case scgs::v2::ProductGamePhase::Finished: return 4U;
    }
    return 0U;
}

Json make_view(ProductSession& session, const std::uint32_t viewer) {
    Json players = Json::array();
    players.push_back(make_player_view(session, 0U, viewer));
    players.push_back(make_player_view(session, 1U, viewer));
    return Json{
        {"viewer", viewer},
        {"active_player", static_cast<std::uint32_t>(session.game.active_player())},
        {"first_player", static_cast<std::uint32_t>(session.game.first_player())},
        {"phase", phase_value(session.game.phase())},
        {"result", static_cast<std::uint32_t>(session.game.result())},
        {"revision", session.game.revision()},
        {"players", std::move(players)},
        {"reaction", make_reaction(session, viewer)},
        {"pending_choice", make_pending_choice(session, viewer)}};
}

Json make_envelope(const std::uint64_t revision) {
    return Json{{"schema_version", SCGS_V05_SCHEMA_VERSION}, {"revision", revision}};
}

struct ConvertedCommand final {
    scgs::v2::ProductGameCommand command;
    std::uint32_t forced_code = kEngineOk;
    std::string forced_message = "ok";
};

ConvertedCommand convert_command(ProductSession& session, const RequestShape& request) {
    ConvertedCommand result;
    result.command.player = static_cast<scgs::PlayerId>(request.player);
    result.command.expected_revision = request.expected_revision;
    if (!request.action.has_value()) {
        result.forced_code = kEngineInvalidCard;
        result.forced_message = "action is required";
        return result;
    }
    result.command.action = static_cast<scgs::v2::ActionKind>(*request.action);
    if (request.source.has_value()) {
        result.command.source = *request.source;
    }
    if (request.slot.has_value()) {
        result.command.slot = static_cast<std::size_t>(*request.slot);
    }
    result.command.mode_id = request.mode_id.value_or(std::string{});
    result.command.use_advance = request.use_advance.value_or(false);
    result.command.selected_cards = request.mulligan_cards.value_or(std::vector<std::uint64_t>{});
    result.command.additional_cost_cards =
        request.additional_cost_cards.value_or(std::vector<std::uint64_t>{});

    if (request.target.has_value()) {
        if (request.target->kind == 1U) {
            result.command.target = request.target->permanent;
            if (!request.target->permanent.has_value() ||
                !session.game.board().contains_instance(*request.target->permanent) ||
                session.game.board().instance(*request.target->permanent).controller !=
                    static_cast<scgs::PlayerId>(request.target->player)) {
                result.forced_code = 6U;
                result.forced_message = "target identity and controller do not match";
            }
        } else if (result.command.action != scgs::v2::ActionKind::Attack ||
            request.target->player != static_cast<std::uint32_t>(scgs::opponent(result.command.player))) {
            result.forced_code = 6U;
            result.forced_message = "leader target is invalid for this action";
        }
    }

    if (result.command.action == scgs::v2::ActionKind::ResolveChoice) {
        session.sync_choice_tokens();
        if (!request.choice_id.has_value() || *request.choice_id != session.external_choice_id ||
            !session.internal_choice_id.has_value()) {
            result.forced_code = kEngineInvalidChoice;
            result.forced_message = "choice token does not belong to this session";
            return result;
        }
        result.command.choice_id = *session.internal_choice_id;
        if (!request.selected_option_ids.has_value()) {
            result.forced_code = kEngineInvalidChoice;
            result.forced_message = "choice selections are required";
            return result;
        }
        for (const std::string& external : *request.selected_option_ids) {
            const auto found = session.external_to_internal_options.find(external);
            if (found == session.external_to_internal_options.end()) {
                result.forced_code = kEngineInvalidChoice;
                result.forced_message = "choice option does not belong to this session";
                return result;
            }
            result.command.selected_option_ids.push_back(found->second);
        }
    }
    return result;
}

Json make_payment(
    const ProductSession& session,
    const scgs::PlayerId player,
    const std::uint32_t code,
    const std::string_view message,
    const scgs::v2::ProductPaymentPreview& preview = {}) {
    const auto& before = session.game.resources(player);
    const bool projected = code == kEngineOk;
    return Json{
        {"status", make_status(code, message)},
        {"current_pp_before", before.current_pp},
        {"current_pp_after", projected ? preview.current_pp_after : before.current_pp},
        {"pp_capacity_before", before.pp_capacity},
        {"pp_capacity_after", projected ? preview.pp_capacity_after : before.pp_capacity},
        {"cracks_before", before.cracks},
        {"cracks_after", projected ? preview.cracks_after : before.cracks},
        {"evolution_energy_before", before.evolution_energy},
        {"evolution_energy_after", projected ? preview.evolution_energy_after : before.evolution_energy},
        {"base_cost", preview.base_cost},
        {"burn_cost", preview.burn_cost},
        {"advance_cost", preview.advance_cost},
        {"used_advance", preview.advanced}};
}

Json make_command(ProductSession& session, const scgs::v2::ProductGameCommand& command) {
    Json result{
        {"player", static_cast<std::uint32_t>(command.player)},
        {"action", static_cast<std::uint32_t>(command.action)},
        {"expected_revision", command.expected_revision}};
    const auto add_source = [&] {
        if (command.source.has_value()) {
            result["source"] = *command.source;
        }
    };
    const auto add_target = [&] {
        if (const auto target = command_target_json(session, command)) {
            result["target"] = *target;
        }
    };
    const auto add_mode = [&] {
        if (!command.mode_id.empty()) {
            result["mode_id"] = command.mode_id;
        }
    };
    switch (command.action) {
        case scgs::v2::ActionKind::Mulligan:
            result["mulligan_cards"] = command.selected_cards;
            break;
        case scgs::v2::ActionKind::PlayFollower:
        case scgs::v2::ActionKind::CastSpell:
        case scgs::v2::ActionKind::PlayAmulet:
            add_source(); add_target();
            if (command.slot.has_value()) result["slot"] = *command.slot;
            add_mode(); result["use_advance"] = command.use_advance;
            break;
        case scgs::v2::ActionKind::PlayTrap:
            add_source();
            if (command.slot.has_value()) result["slot"] = *command.slot;
            add_mode(); result["use_advance"] = command.use_advance;
            break;
        case scgs::v2::ActionKind::Attack:
            add_source(); add_target();
            break;
        case scgs::v2::ActionKind::Evolve:
        case scgs::v2::ActionKind::ActivateTrap:
            add_source(); add_target(); add_mode();
            break;
        case scgs::v2::ActionKind::Deploy:
            add_source(); add_target();
            if (command.slot.has_value()) result["slot"] = *command.slot;
            add_mode();
            result["additional_cost_cards"] = command.additional_cost_cards;
            result["use_advance"] = command.use_advance;
            break;
        case scgs::v2::ActionKind::PlayField:
            add_source(); add_target(); add_mode(); result["use_advance"] = command.use_advance;
            break;
        case scgs::v2::ActionKind::ResolveChoice:
            session.sync_choice_tokens();
            result["choice_id"] = session.external_choice_id;
            result["selected_option_ids"] = Json::array();
            for (const std::string& internal : command.selected_option_ids) {
                result["selected_option_ids"].push_back(session.external_option(internal));
            }
            break;
        case scgs::v2::ActionKind::PassReaction:
        case scgs::v2::ActionKind::EndTurn:
        case scgs::v2::ActionKind::Surrender:
            break;
    }
    return result;
}

bool same_target(const Json& lhs, const TargetShape& rhs) {
    if (lhs.at("kind").get<std::uint32_t>() != rhs.kind ||
        lhs.at("player").get<std::uint32_t>() != rhs.player) {
        return false;
    }
    if (rhs.kind == 0U) {
        return true;
    }
    return lhs.at("permanent").get<std::uint64_t>() == rhs.permanent;
}

enum class IgnoredQueryField : std::uint8_t { None, Target, Slot, AdditionalCosts };

bool command_matches_query(
    ProductSession& session,
    const scgs::v2::ProductGameCommand& command,
    const RequestShape& query,
    const IgnoredQueryField ignored = IgnoredQueryField::None) {
    if (query.action.has_value() && *query.action != static_cast<std::uint32_t>(command.action)) return false;
    if (query.source.has_value() && command.source != query.source) return false;
    if (ignored != IgnoredQueryField::Slot && query.slot.has_value() && command.slot != query.slot) return false;
    if (query.mode_id.has_value() && command.mode_id != *query.mode_id) return false;
    if (query.use_advance.has_value() && command.use_advance != *query.use_advance) return false;
    if (query.mulligan_cards.has_value() && command.selected_cards != *query.mulligan_cards) return false;
    if (ignored != IgnoredQueryField::AdditionalCosts && query.additional_cost_cards.has_value() &&
        command.additional_cost_cards != *query.additional_cost_cards) return false;
    if (ignored != IgnoredQueryField::Target && query.target.has_value()) {
        const auto candidate = command_target_json(session, command);
        if (!candidate.has_value() || !same_target(*candidate, *query.target)) return false;
    }
    if (query.choice_id.has_value() || query.selected_option_ids.has_value()) {
        session.sync_choice_tokens();
        if (command.action != scgs::v2::ActionKind::ResolveChoice) return false;
        if (query.choice_id.has_value() && *query.choice_id != session.external_choice_id) return false;
        if (query.selected_option_ids.has_value()) {
            std::vector<std::string> external;
            for (const std::string& option : command.selected_option_ids) {
                external.push_back(session.external_option(option));
            }
            if (external != *query.selected_option_ids) return false;
        }
    }
    return true;
}

void require_valid_query(const ProductSession& session, const RequestShape& query) {
    if (validate_request_shape(query) != kEngineOk) {
        fail(SCGS_V05_SCHEMA_MISMATCH, "The query contains a field unrelated to its selected action.");
    }
    if (session.game.phase() == scgs::v2::ProductGamePhase::NotStarted) {
        fail(SCGS_V05_INVALID_ARGUMENT, "The query requires a started match.");
    }
    if (query.expected_revision != session.game.revision()) {
        fail(SCGS_V05_INVALID_ARGUMENT, "The query revision is stale.");
    }
    if (session.game.phase() == scgs::v2::ProductGamePhase::Finished) {
        fail(SCGS_V05_INVALID_ARGUMENT, "The query cannot inspect a finished match.");
    }
}

std::uint32_t event_type(const scgs::v2::ProductGameEvent& event) noexcept {
    using Kind = scgs::v2::ProductEventKind;
    switch (event.kind) {
        case Kind::MatchStarted: return 0U;
        case Kind::TurnStarted: return 1U;
        case Kind::TurnEnded: return 2U;
        case Kind::CardDrawn: return 3U;
        case Kind::CardArchived: return 5U;
        case Kind::CostPaid: return 6U;
        case Kind::CardPlayed:
        case Kind::CardMoved:
        case Kind::ChoiceResolved: return 8U;
        case Kind::AttackDeclared: return 14U;
        case Kind::AttackCancelled: return 15U;
        case Kind::Damage: return event.target.has_value() ? 10U : 11U;
        case Kind::Healing: return 12U;
        case Kind::Evolved: return 16U;
        case Kind::TrapActivated: return 20U;
        case Kind::ReactionPassed:
        case Kind::ChoiceRequested: return 19U;
        case Kind::PlayerSurrendered: return 22U;
        case Kind::MatchEnded: return 23U;
        case Kind::MulliganSubmitted: return 24U;
    }
    return 8U;
}

bool event_hidden_from_viewer(
    const ProductSession& session,
    const scgs::v2::ProductGameEvent& event,
    const std::uint32_t viewer) {
    const bool opponent_view = viewer != static_cast<std::uint32_t>(event.player);
    if (!opponent_view) {
        return false;
    }
    using Kind = scgs::v2::ProductEventKind;
    if (event.kind == Kind::CardDrawn || event.kind == Kind::MulliganSubmitted ||
        event.kind == Kind::ChoiceRequested || event.kind == Kind::ChoiceResolved) {
        return true;
    }
    if (event.kind == Kind::CardPlayed && event.source.has_value() &&
        session.game.board().contains_instance(*event.source)) {
        const auto& definition = session.game.board().catalog().at(
            session.game.board().instance(*event.source).design_id);
        return definition.kind == scgs::v2::CardKind::Trap;
    }
    return false;
}

std::string event_text(
    const scgs::v2::ProductGameEvent& event,
    const bool hidden) {
    using Kind = scgs::v2::ProductEventKind;
    if (hidden) {
        switch (event.kind) {
            case Kind::CardDrawn: return "opponent drew a card";
            case Kind::CardPlayed: return "opponent set a trap";
            case Kind::MulliganSubmitted: return "opponent completed mulligan";
            case Kind::ChoiceRequested: return "opponent is choosing";
            case Kind::ChoiceResolved: return "opponent completed a private choice";
            default: return "opponent completed a private choice";
        }
    }
    switch (event.kind) {
        case Kind::MatchStarted: return "match started";
        case Kind::MulliganSubmitted: return "mulligan completed";
        case Kind::CardDrawn: return "card drawn";
        case Kind::CardArchived: return "card archived";
        case Kind::TurnStarted: return "turn started";
        case Kind::TurnEnded: return "turn ended";
        case Kind::CostPaid: return "cost paid";
        case Kind::CardPlayed: return "card played";
        case Kind::CardMoved: return "card moved";
        case Kind::AttackDeclared: return "attack declared";
        case Kind::AttackCancelled: return "attack cancelled";
        case Kind::Damage: return "damage dealt";
        case Kind::Healing: return "leader healed";
        case Kind::Evolved: return "follower evolved";
        case Kind::TrapActivated: return "trap activated";
        case Kind::ReactionPassed: return "reaction passed";
        case Kind::ChoiceRequested: return "choice requested";
        case Kind::ChoiceResolved: return "choice resolved";
        case Kind::PlayerSurrendered: return "player surrendered";
        case Kind::MatchEnded: return "match ended";
    }
    return "event";
}

void prepare_output(std::uint64_t* required_bytes) {
    if (required_bytes == nullptr) {
        fail(SCGS_V05_INVALID_ARGUMENT, "The required-bytes output pointer is null.");
    }
    *required_bytes = 0U;
}

scgs_v05_native_code write_bytes(
    const std::string_view payload,
    char* buffer,
    const std::uint64_t capacity,
    std::uint64_t* required_bytes) {
    prepare_output(required_bytes);
    if (payload.size() >= std::numeric_limits<std::uint64_t>::max()) {
        fail(SCGS_V05_INTERNAL_ERROR, "The native output length overflowed.");
    }
    const std::uint64_t required = static_cast<std::uint64_t>(payload.size()) + 1U;
    *required_bytes = required;
    if (capacity < required) {
        fail(SCGS_V05_BUFFER_TOO_SMALL, "The output buffer is too small.");
    }
    if (buffer == nullptr) {
        fail(SCGS_V05_INVALID_ARGUMENT, "The output buffer is null.");
    }
    std::memcpy(buffer, payload.data(), payload.size());
    buffer[payload.size()] = '\0';
    return SCGS_V05_OK;
}

scgs_v05_native_code write_json(
    const Json& value,
    char* buffer,
    const std::uint64_t capacity,
    std::uint64_t* required_bytes) {
    return write_bytes(value.dump(), buffer, capacity, required_bytes);
}

std::mutex g_registry_mutex;
std::unordered_map<scgs_v05_handle, std::shared_ptr<ProductSession>> g_registry;
scgs_v05_handle g_next_handle = 1U;

std::shared_ptr<ProductSession> find_session(const scgs_v05_handle handle) {
    if (handle == 0U) {
        fail(SCGS_V05_INVALID_HANDLE, "The game handle is invalid.");
    }
    const std::lock_guard<std::mutex> lock(g_registry_mutex);
    const auto iterator = g_registry.find(handle);
    if (iterator == g_registry.end()) {
        fail(SCGS_V05_INVALID_HANDLE, "The game handle is invalid or was already destroyed.");
    }
    return iterator->second;
}

scgs_v05_handle add_session(std::shared_ptr<ProductSession> session) {
    const std::lock_guard<std::mutex> lock(g_registry_mutex);
    if (g_next_handle == 0U) {
        fail(SCGS_V05_OUT_OF_MEMORY, "The native handle space has been exhausted.");
    }
    const scgs_v05_handle handle = g_next_handle;
    g_next_handle = handle == std::numeric_limits<scgs_v05_handle>::max() ? 0U : handle + 1U;
    g_registry.emplace(handle, std::move(session));
    return handle;
}

bool abi_is_supported(const std::uint32_t requested) noexcept {
    return (requested >> 16U) == kAbiMajor && (requested & 0xFFFFU) <= kAbiMinor;
}

} // namespace

extern "C" {

uint32_t SCGS_V05_CALL scgs_v05_abi_version(void) {
    clear_error();
    return SCGS_V05_ABI_VERSION;
}

scgs_v05_native_code SCGS_V05_CALL scgs_v05_create(
    const std::uint32_t requested_abi,
    const char* config_json,
    const std::uint64_t config_bytes,
    scgs_v05_handle* out_handle) {
    return protect([&]() -> scgs_v05_native_code {
        if (out_handle == nullptr) {
            fail(SCGS_V05_INVALID_ARGUMENT, "The output handle pointer is null.");
        }
        *out_handle = 0U;
        if (!abi_is_supported(requested_abi)) {
            fail(SCGS_V05_ABI_MISMATCH, "The requested native ABI version is not supported.");
        }
        ParsedConfig config = parse_config(parse_payload(config_json, config_bytes));
        *out_handle = add_session(std::make_shared<ProductSession>(std::move(config)));
        return SCGS_V05_OK;
    });
}

scgs_v05_native_code SCGS_V05_CALL scgs_v05_destroy(const scgs_v05_handle handle) {
    return protect([&]() -> scgs_v05_native_code {
        if (handle == 0U) {
            return SCGS_V05_OK;
        }
        const std::lock_guard<std::mutex> lock(g_registry_mutex);
        const auto iterator = g_registry.find(handle);
        if (iterator == g_registry.end()) {
            fail(SCGS_V05_INVALID_HANDLE, "The game handle is invalid or was already destroyed.");
        }
        g_registry.erase(iterator);
        return SCGS_V05_OK;
    });
}

scgs_v05_native_code SCGS_V05_CALL scgs_v05_start(
    const scgs_v05_handle handle,
    std::uint32_t* out_engine_code) {
    return protect([&]() -> scgs_v05_native_code {
        if (out_engine_code == nullptr) {
            fail(SCGS_V05_INVALID_ARGUMENT, "The engine-code output pointer is null.");
        }
        *out_engine_code = SCGS_V05_NO_ENGINE_CODE;
        const std::shared_ptr<ProductSession> session = find_session(handle);
        const std::lock_guard<std::mutex> lock(session->mutex);
        const scgs::v2::ProductGameStatus status = session->game.start();
        *out_engine_code = map_engine_code(status.code);
        return SCGS_V05_OK;
    });
}

scgs_v05_native_code SCGS_V05_CALL scgs_v05_get_view_json(
    const scgs_v05_handle handle,
    const std::uint32_t viewer,
    char* buffer,
    const std::uint64_t capacity,
    std::uint64_t* required_bytes) {
    return protect([&]() -> scgs_v05_native_code {
        prepare_output(required_bytes);
        const std::uint32_t parsed_viewer = parse_viewer(viewer);
        const std::shared_ptr<ProductSession> session = find_session(handle);
        const std::lock_guard<std::mutex> lock(session->mutex);
        Json output = make_envelope(session->game.revision());
        output["view"] = make_view(*session, parsed_viewer);
        return write_json(output, buffer, capacity, required_bytes);
    });
}

scgs_v05_native_code SCGS_V05_CALL scgs_v05_list_legal_actions_json(
    const scgs_v05_handle handle,
    const char* query_json,
    const std::uint64_t query_bytes,
    char* buffer,
    const std::uint64_t capacity,
    std::uint64_t* required_bytes) {
    return protect([&]() -> scgs_v05_native_code {
        prepare_output(required_bytes);
        const RequestShape query = parse_request(parse_payload(query_json, query_bytes), false);
        const std::shared_ptr<ProductSession> session = find_session(handle);
        const std::lock_guard<std::mutex> lock(session->mutex);
        require_valid_query(*session, query);
        Json actions = Json::array();
        const auto player = static_cast<scgs::PlayerId>(query.player);
        for (const scgs::v2::ProductLegalAction& action : session->game.list_legal_actions(player)) {
            if (!command_matches_query(*session, action.command, query)) {
                continue;
            }
            actions.push_back(Json{
                {"command", make_command(*session, action.command)},
                {"payment", make_payment(*session, player, kEngineOk, "ok", action.payment)}});
        }
        Json output = make_envelope(session->game.revision());
        output["actions"] = std::move(actions);
        return write_json(output, buffer, capacity, required_bytes);
    });
}

scgs_v05_native_code SCGS_V05_CALL scgs_v05_list_valid_targets_json(
    const scgs_v05_handle handle,
    const char* query_json,
    const std::uint64_t query_bytes,
    char* buffer,
    const std::uint64_t capacity,
    std::uint64_t* required_bytes) {
    return protect([&]() -> scgs_v05_native_code {
        prepare_output(required_bytes);
        const RequestShape query = parse_request(parse_payload(query_json, query_bytes), false);
        const std::shared_ptr<ProductSession> session = find_session(handle);
        const std::lock_guard<std::mutex> lock(session->mutex);
        require_valid_query(*session, query);
        Json targets = Json::array();
        std::unordered_set<std::string> seen;
        const auto player = static_cast<scgs::PlayerId>(query.player);
        for (const auto& action : session->game.list_legal_actions(player)) {
            if (!command_matches_query(*session, action.command, query, IgnoredQueryField::Target)) {
                continue;
            }
            const auto target = command_target_json(*session, action.command);
            if (!target.has_value()) {
                continue;
            }
            const std::string key = target->dump();
            if (seen.insert(key).second) {
                targets.push_back(*target);
            }
        }
        Json output = make_envelope(session->game.revision());
        output["targets"] = std::move(targets);
        return write_json(output, buffer, capacity, required_bytes);
    });
}

scgs_v05_native_code SCGS_V05_CALL scgs_v05_list_valid_slots_json(
    const scgs_v05_handle handle,
    const char* query_json,
    const std::uint64_t query_bytes,
    char* buffer,
    const std::uint64_t capacity,
    std::uint64_t* required_bytes) {
    return protect([&]() -> scgs_v05_native_code {
        prepare_output(required_bytes);
        const RequestShape query = parse_request(parse_payload(query_json, query_bytes), false);
        const std::shared_ptr<ProductSession> session = find_session(handle);
        const std::lock_guard<std::mutex> lock(session->mutex);
        require_valid_query(*session, query);
        Json slots = Json::array();
        std::unordered_set<std::size_t> seen;
        const auto player = static_cast<scgs::PlayerId>(query.player);
        for (const auto& action : session->game.list_legal_actions(player)) {
            if (action.command.slot.has_value() &&
                command_matches_query(*session, action.command, query, IgnoredQueryField::Slot) &&
                seen.insert(*action.command.slot).second) {
                slots.push_back(*action.command.slot);
            }
        }
        Json output = make_envelope(session->game.revision());
        output["slots"] = std::move(slots);
        return write_json(output, buffer, capacity, required_bytes);
    });
}

scgs_v05_native_code SCGS_V05_CALL scgs_v05_list_valid_donors_json(
    const scgs_v05_handle handle,
    const char* query_json,
    const std::uint64_t query_bytes,
    char* buffer,
    const std::uint64_t capacity,
    std::uint64_t* required_bytes) {
    return protect([&]() -> scgs_v05_native_code {
        prepare_output(required_bytes);
        const RequestShape query = parse_request(parse_payload(query_json, query_bytes), false);
        const std::shared_ptr<ProductSession> session = find_session(handle);
        const std::lock_guard<std::mutex> lock(session->mutex);
        require_valid_query(*session, query);
        Json donors = Json::array();
        std::unordered_set<scgs::InstanceId> seen;
        const auto player = static_cast<scgs::PlayerId>(query.player);
        for (const auto& action : session->game.list_legal_actions(player)) {
            if (!command_matches_query(
                    *session, action.command, query, IgnoredQueryField::AdditionalCosts)) {
                continue;
            }
            for (const scgs::InstanceId card : action.command.additional_cost_cards) {
                if (seen.insert(card).second) {
                    donors.push_back(card);
                }
            }
        }
        Json output = make_envelope(session->game.revision());
        output["donors"] = std::move(donors);
        return write_json(output, buffer, capacity, required_bytes);
    });
}

scgs_v05_native_code SCGS_V05_CALL scgs_v05_preview_payment_json(
    const scgs_v05_handle handle,
    const char* command_json,
    const std::uint64_t command_bytes,
    char* buffer,
    const std::uint64_t capacity,
    std::uint64_t* required_bytes) {
    return protect([&]() -> scgs_v05_native_code {
        prepare_output(required_bytes);
        const RequestShape command = parse_request(parse_payload(command_json, command_bytes), true);
        const std::shared_ptr<ProductSession> session = find_session(handle);
        const std::lock_guard<std::mutex> lock(session->mutex);
        std::uint32_t code = validate_request_shape(command);
        std::string message = engine_message(code);
        scgs::v2::ProductPaymentPreview payment;
        if (code == kEngineOk) {
            ConvertedCommand converted = convert_command(*session, command);
            code = converted.forced_code;
            message = converted.forced_message;
            if (code == kEngineOk) {
                const scgs::v2::ProductActionPlan plan = session->game.plan_command(converted.command);
                code = map_engine_code(plan.status.code, converted.command.action);
                message = plan.status.message.empty() ? engine_message(code) : plan.status.message;
                payment = plan.payment;
            }
        }
        Json output = make_envelope(session->game.revision());
        output["payment"] = make_payment(
            *session, static_cast<scgs::PlayerId>(command.player), code, message, payment);
        return write_json(output, buffer, capacity, required_bytes);
    });
}

scgs_v05_native_code SCGS_V05_CALL scgs_v05_get_reaction_context_json(
    const scgs_v05_handle handle,
    const std::uint32_t viewer,
    char* buffer,
    const std::uint64_t capacity,
    std::uint64_t* required_bytes) {
    return protect([&]() -> scgs_v05_native_code {
        prepare_output(required_bytes);
        const std::uint32_t parsed_viewer = parse_viewer(viewer);
        const std::shared_ptr<ProductSession> session = find_session(handle);
        const std::lock_guard<std::mutex> lock(session->mutex);
        Json output = make_envelope(session->game.revision());
        output["reaction"] = make_reaction(*session, parsed_viewer);
        output["pending_choice"] = make_pending_choice(*session, parsed_viewer);
        return write_json(output, buffer, capacity, required_bytes);
    });
}

scgs_v05_native_code SCGS_V05_CALL scgs_v05_submit_command_json(
    const scgs_v05_handle handle,
    const char* command_json,
    const std::uint64_t command_bytes,
    std::uint32_t* out_engine_code) {
    return protect([&]() -> scgs_v05_native_code {
        if (out_engine_code == nullptr) {
            fail(SCGS_V05_INVALID_ARGUMENT, "The engine-code output pointer is null.");
        }
        *out_engine_code = SCGS_V05_NO_ENGINE_CODE;
        const RequestShape command = parse_request(parse_payload(command_json, command_bytes), true);
        const std::shared_ptr<ProductSession> session = find_session(handle);
        const std::lock_guard<std::mutex> lock(session->mutex);
        const std::uint32_t shape_code = validate_request_shape(command);
        if (shape_code != kEngineOk) {
            *out_engine_code = shape_code;
            return SCGS_V05_OK;
        }
        ConvertedCommand converted = convert_command(*session, command);
        if (converted.forced_code != kEngineOk) {
            *out_engine_code = converted.forced_code;
            return SCGS_V05_OK;
        }
        const scgs::v2::ProductGameStatus status = session->game.submit_command(converted.command);
        *out_engine_code = map_engine_code(status.code, converted.command.action);
        if (status) {
            session->sync_choice_tokens();
        }
        return SCGS_V05_OK;
    });
}

scgs_v05_native_code SCGS_V05_CALL scgs_v05_read_events_json(
    const scgs_v05_handle handle,
    const std::uint32_t viewer,
    const std::uint64_t after_sequence,
    char* buffer,
    const std::uint64_t capacity,
    std::uint64_t* required_bytes) {
    return protect([&]() -> scgs_v05_native_code {
        prepare_output(required_bytes);
        const std::uint32_t parsed_viewer = parse_viewer(viewer);
        const std::shared_ptr<ProductSession> session = find_session(handle);
        const std::lock_guard<std::mutex> lock(session->mutex);
        Json events = Json::array();
        std::uint64_t last_sequence = after_sequence;
        for (const scgs::v2::ProductGameEvent& event : session->game.read_events(after_sequence)) {
            if (event.sequence <= after_sequence) {
                continue;
            }
            const bool hidden = event_hidden_from_viewer(*session, event, parsed_viewer);
            Json value{
                {"sequence", event.sequence},
                {"type", event_type(event)},
                {"player", static_cast<std::uint32_t>(event.player)},
                {"value", hidden ? 0 : event.value},
                {"secondary_value", hidden ? 0 : event.secondary_value},
                {"hidden_card", hidden},
                {"text", event_text(event, hidden)}};
            if (!hidden && event.source.has_value() &&
                session->game.board().contains_instance(*event.source)) {
                value["card"] = *event.source;
                value["design_id"] = session->game.board().instance(*event.source).design_id;
            }
            if (event.kind == scgs::v2::ProductEventKind::MatchStarted) {
                value["first_player"] = static_cast<std::uint32_t>(session->game.first_player());
            }
            events.push_back(std::move(value));
            last_sequence = event.sequence;
        }
        Json output = make_envelope(session->game.revision());
        output["last_sequence"] = last_sequence;
        output["events"] = std::move(events);
        return write_json(output, buffer, capacity, required_bytes);
    });
}

scgs_v05_native_code SCGS_V05_CALL scgs_v05_get_last_error(
    char* buffer,
    const std::uint64_t capacity,
    std::uint64_t* required_bytes) {
    try {
        if (required_bytes == nullptr) {
            return SCGS_V05_INVALID_ARGUMENT;
        }
        const std::uint64_t required = static_cast<std::uint64_t>(g_last_error.size()) + 1U;
        *required_bytes = required;
        if (capacity < required) {
            return SCGS_V05_BUFFER_TOO_SMALL;
        }
        if (buffer == nullptr) {
            return SCGS_V05_INVALID_ARGUMENT;
        }
        std::memcpy(buffer, g_last_error.data(), g_last_error.size());
        buffer[g_last_error.size()] = '\0';
        return SCGS_V05_OK;
    } catch (...) {
        return SCGS_V05_INTERNAL_ERROR;
    }
}

} // extern "C"
