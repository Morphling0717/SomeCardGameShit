// SPDX-License-Identifier: GPL-3.0-or-later

#include "scgs/native_api_v05.h"
#include "scgs/product_runtime.hpp"

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

void validate_target(const Json& value) {
    if (!value.is_object()) {
        fail(SCGS_V05_SCHEMA_MISMATCH, "A JSON target must be an object.");
    }
    static constexpr std::array<std::string_view, 3U> kTargetFields{
        "kind", "player", "permanent"};
    require_only_fields(value, kTargetFields);
    const std::uint64_t kind = require_unsigned(value, "kind");
    (void)parse_player(value, "player");
    if (kind == 0U) {
        if (optional_field(value, "permanent") != nullptr) {
            fail(SCGS_V05_SCHEMA_MISMATCH, "A leader target cannot name a permanent.");
        }
        return;
    }
    if (kind == 1U) {
        (void)require_unsigned(value, "permanent");
        return;
    }
    fail(SCGS_V05_SCHEMA_MISMATCH, "The JSON target kind is not supported.");
}

struct RequestShape final {
    std::uint32_t player = 0U;
    std::optional<std::uint32_t> action;
    std::optional<std::uint64_t> source;
    bool has_target = false;
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
        validate_target(*value);
        result.has_target = true;
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
        if (request.source.has_value() || request.has_target || request.slot.has_value() ||
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
        {RequestField::Target, request.has_target},
        {RequestField::Slot, request.slot.has_value()},
        {RequestField::Mode, request.mode_id.has_value()},
        {RequestField::Choice, request.choice_id.has_value()},
        {RequestField::MulliganCards, request.mulligan_cards.has_value()},
        {RequestField::SelectedOptions, request.selected_option_ids.has_value()},
        {RequestField::AdditionalCosts, request.additional_cost_cards.has_value()},
        {RequestField::UseAdvance, request.use_advance.has_value()},
    }};
    for (const auto [field, present] : fields) {
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

struct StoredEvent final {
    std::uint64_t sequence;
    std::uint32_t type;
    std::uint32_t player;
    std::string public_text;
    std::string opponent_text;
    bool hide_from_opponent;
    std::optional<std::uint32_t> first_player;
};

struct FoundationSession final {
    explicit FoundationSession(ParsedConfig value)
        : config(std::move(value)),
          board(scgs::v2::make_locked_product_catalog()),
          opaque_namespace(make_opaque_session_namespace()) {
        for (std::uint32_t index = 0U; index < 2U; ++index) {
            const auto player = static_cast<scgs::PlayerId>(index);
            const bool oathguard = config.decks[index].profession_id == "oathguard";
            const std::string_view hand_card = oathguard ? "LO-01" : "AP-01";
            const std::string_view amulet_design = oathguard ? "LO-03" : "AP-04";
            const std::string_view follower_design = oathguard ? "LO-04" : "AP-02";
            const std::string_view field_design = oathguard ? "LO-10" : "AP-05";
            for (std::size_t card = 0U; card < 4U; ++card) {
                (void)board.create_instance(
                    hand_card, player, scgs::v2::Zone::Hand);
            }
            for (std::size_t card = 0U; card < 26U; ++card) {
                (void)board.create_instance(
                    hand_card, player, scgs::v2::Zone::Deck);
            }
            const scgs::InstanceId amulet = board.create_instance(amulet_design, player);
            const scgs::InstanceId follower = board.create_instance(follower_design, player);
            const scgs::InstanceId field = board.create_instance(field_design, player);
            if (!board.place_main(
                    player, amulet, 0U, scgs::v2::MoveReason::ScenarioSetup) ||
                !board.place_main(
                    player, follower, 1U, scgs::v2::MoveReason::ScenarioSetup) ||
                !board.play_field(player, field)) {
                throw std::logic_error("failed to construct the v05 product foundation fixture");
            }
            if (oathguard) {
                const scgs::InstanceId trap = board.create_instance("LO-07", player);
                if (!board.place_tactic(
                        player, trap, 0U, scgs::v2::MoveReason::ScenarioSetup)) {
                    throw std::logic_error("failed to construct the v05 hidden-tactic fixture");
                }
            }
        }
    }

    void begin_choice_tokens() {
        const std::string generation = std::to_string(next_choice_generation++);
        active_choice_id = "choice-" + opaque_namespace + "-" + generation;
        active_option_ids = {
            "option-" + opaque_namespace + "-" + generation + "-0",
            "option-" + opaque_namespace + "-" + generation + "-1"};
    }

    void clear_choice_tokens() noexcept {
        active_choice_id.clear();
        for (std::string& option : active_option_ids) {
            option.clear();
        }
    }

    ParsedConfig config;
    scgs::v2::ProductBoard board;
    scgs::v2::ResolutionQueue resolution;
    const std::string opaque_namespace;
    std::string active_choice_id;
    std::array<std::string, 2U> active_option_ids;
    std::uint64_t next_choice_generation = 1U;
    bool started = false;
    bool finished = false;
    std::array<bool, 2U> mulligan_done{false, false};
    std::uint32_t first_player = 0U;
    std::uint32_t active_player = 0U;
    std::uint32_t result = 0U;
    std::uint64_t revision = 0U;
    std::uint64_t next_sequence = 1U;
    std::vector<StoredEvent> events;
    std::mutex mutex;
};

Json make_status(const std::uint32_t code, const char* message) {
    return Json{{"engine_code", code}, {"message", message}};
}

Json make_payment(const std::uint32_t status_code, const char* message) {
    return Json{
        {"status", make_status(status_code, message)},
        {"current_pp_before", 0},
        {"current_pp_after", 0},
        {"pp_capacity_before", 0},
        {"pp_capacity_after", 0},
        {"cracks_before", 0},
        {"cracks_after", 0},
        {"evolution_energy_before", 0},
        {"evolution_energy_after", 0},
        {"base_cost", 0},
        {"burn_cost", 0},
        {"advance_cost", 0},
        {"used_advance", false}};
}

Json make_command(const std::uint32_t player, const std::uint32_t action, const std::uint64_t revision) {
    Json result{
        {"player", player},
        {"action", action},
        {"expected_revision", revision}};
    if (action == 0U) {
        result["mulligan_cards"] = Json::array();
    }
    return result;
}

Json make_resolve_choice_command(
    const FoundationSession& session,
    const std::uint32_t player,
    const std::uint64_t revision,
    const std::string& option_id) {
    Json result = make_command(player, 13U, revision);
    result["choice_id"] = session.active_choice_id;
    result["selected_option_ids"] = Json::array({option_id});
    return result;
}

Json make_hidden_tactic(const scgs::v2::CardInstance& card) {
    return Json{
        {"name", ""},
        {"owner", static_cast<std::uint32_t>(card.owner)},
        {"controller", static_cast<std::uint32_t>(card.controller)},
        {"zone", static_cast<std::uint32_t>(scgs::v2::Zone::Tactic)},
        // The slot is already represented by the tactics array index. Even
        // sequence is identity-derived for a hidden card and must be zero.
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

Json make_card(const FoundationSession& session, const scgs::InstanceId card_id) {
    const scgs::v2::CardInstance& card = session.board.instance(card_id);
    const scgs::v2::CardDefinition& definition = session.board.catalog().at(card.design_id);
    const bool face_down = card.zone == scgs::v2::Zone::Tactic &&
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

Json make_player_view(
    const FoundationSession& session,
    const std::uint32_t player,
    const std::uint32_t viewer) {
    const DeckIdentity& identity = session.config.decks[player];
    const auto player_id = static_cast<scgs::PlayerId>(player);
    const scgs::v2::PlayerState& state = session.board.player(player_id);
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
        const scgs::v2::CardInstance& instance = session.board.instance(*card);
        const scgs::v2::CardDefinition& definition =
            session.board.catalog().at(instance.design_id);
        const bool hidden = player != viewer && definition.kind == scgs::v2::CardKind::Trap;
        tactics.push_back(hidden ? make_hidden_tactic(instance) : make_card(session, *card));
    }
    const auto public_cards = [&](const std::vector<scgs::InstanceId>& cards) {
        Json values = Json::array();
        for (const scgs::InstanceId card : cards) {
            values.push_back(make_card(session, card));
        }
        return values;
    };
    Json result{
        {"player", player},
        {"profession_id", identity.profession_id},
        {"leader_health", 25},
        {"maximum_leader_health", 25},
        {"current_pp", 0},
        {"pp_capacity", 0},
        {"cracks", 0},
        {"evolution_energy", 0},
        {"own_turn_number", 0},
        {"fatigue_count", 0},
        {"mulligan_done", session.mulligan_done[player]},
        {"evolution_used_this_turn", false},
        {"advance_used_this_turn", false},
        {"deploy_used_this_turn", false},
        {"trap_set_this_turn", false},
        {"deck_count", state.deck.size()},
        {"hand_count", state.hand.size()},
        {"hand", std::move(hand)},
        {"main_board", std::move(main_board)},
        {"tactics", std::move(tactics)},
        {"graveyard", public_cards(state.graveyard)},
        {"archive", public_cards(state.archive)},
        {"standby", public_cards(state.standby)}};
    if (state.field.has_value()) {
        result["field"] = make_card(session, *state.field);
    }
    // `field` is optional in schema 2 and must be omitted, not null, when empty.
    return result;
}

Json make_reaction(const FoundationSession& session) {
    return Json{
        {"pending", false},
        {"window", 0U},
        {"responder", session.active_player},
        {"subject", 0U},
        {"depth", 0U},
        {"eligible_count", 0U},
        {"eligible_traps", Json::array()},
        {"revision", session.revision}};
}

Json make_pending_choice(const FoundationSession& session, const std::uint32_t viewer) {
    const auto& pending = session.resolution.pending_choice();
    if (!pending.has_value()) {
        return Json{{"pending", false}, {"revision", session.revision}};
    }
    Json result{
        {"pending", true},
        {"chooser", static_cast<std::uint32_t>(pending->chooser)},
        {"revision", session.revision}};
    if (viewer != static_cast<std::uint32_t>(pending->chooser)) {
        return result;
    }
    result["choice_id"] = session.active_choice_id;
    result["kind"] = static_cast<std::uint32_t>(pending->kind);
    result["minimum_selections"] = pending->minimum;
    result["maximum_selections"] = pending->maximum;
    result["ordered"] = pending->ordered;
    Json options = Json::array();
    for (const scgs::v2::ChoiceOption& option : pending->options) {
        Json value{{"option_id", option.option_id}, {"label", "foundation card"}};
        if (option.card.has_value()) {
            value["card"] = make_card(session, *option.card);
        }
        options.push_back(std::move(value));
    }
    result["options"] = std::move(options);
    return result;
}

Json make_view(const FoundationSession& session, const std::uint32_t viewer) {
    std::uint32_t phase = 0U;
    if (session.started) {
        phase = session.finished ? 4U : (session.mulligan_done[0] && session.mulligan_done[1] ? 2U : 1U);
    }
    Json players = Json::array();
    players.push_back(make_player_view(session, 0U, viewer));
    players.push_back(make_player_view(session, 1U, viewer));
    // Seed is intentionally absent from both the envelope and view.
    return Json{
        {"viewer", viewer},
        {"active_player", session.active_player},
        {"first_player", session.first_player},
        {"phase", phase},
        {"result", session.result},
        {"revision", session.revision},
        {"players", std::move(players)},
        {"reaction", make_reaction(session)},
        {"pending_choice", make_pending_choice(session, viewer)}};
}

Json make_envelope(const std::uint64_t revision) {
    return Json{{"schema_version", SCGS_V05_SCHEMA_VERSION}, {"revision", revision}};
}

void append_event(
    FoundationSession& session,
    const std::uint32_t type,
    const std::uint32_t player,
    std::string public_text,
    std::string opponent_text = {},
    const bool hide_from_opponent = false,
    const std::optional<std::uint32_t> first_player = std::nullopt) {
    session.events.push_back(StoredEvent{
        session.next_sequence++,
        type,
        player,
        std::move(public_text),
        std::move(opponent_text),
        hide_from_opponent,
        first_player});
}

std::uint32_t validate_command(const FoundationSession& session, const RequestShape& command) {
    if (command.player > 1U) {
        return kEngineInvalidPlayer;
    }
    if (!session.started) {
        return kEngineMatchNotStarted;
    }
    if (command.expected_revision != session.revision) {
        return kEngineStaleRevision;
    }
    if (session.finished) {
        return kEngineGameOver;
    }
    if (const std::uint32_t shape_code = validate_request_shape(command);
        shape_code != kEngineOk) {
        return shape_code;
    }
    const std::uint32_t action = command.action.value_or(99U);
    if (session.resolution.input_blocked() && action != 10U && action != 13U) {
        return kEngineChoicePending;
    }
    if (action == 10U) {
        return kEngineOk;
    }
    if (action == 13U) {
        const auto& pending = session.resolution.pending_choice();
        if (!pending.has_value()) {
            return kEngineNoPendingChoice;
        }
        if (command.player != static_cast<std::uint32_t>(pending->chooser)) {
            return kEngineChoiceNotOwned;
        }
        if (!command.choice_id.has_value() ||
            *command.choice_id != session.active_choice_id ||
            !command.selected_option_ids.has_value()) {
            return kEngineInvalidChoice;
        }
        if (command.selected_option_ids->size() < pending->minimum ||
            command.selected_option_ids->size() > pending->maximum) {
            return kEngineInvalidChoice;
        }
        std::vector<std::string> available;
        for (const scgs::v2::ChoiceOption& option : pending->options) {
            available.push_back(option.option_id);
        }
        std::vector<std::string> selected;
        for (const std::string& option : *command.selected_option_ids) {
            if (std::find(available.begin(), available.end(), option) == available.end() ||
                std::find(selected.begin(), selected.end(), option) != selected.end()) {
                return kEngineInvalidChoice;
            }
            selected.push_back(option);
        }
        return kEngineOk;
    }
    const bool mulligan_phase = !(session.mulligan_done[0] && session.mulligan_done[1]);
    if (action == 0U) {
        if (!mulligan_phase) {
            return kEngineInvalidPhase;
        }
        if (session.mulligan_done[command.player]) {
            return kEngineMulliganAlreadyDone;
        }
        if (!command.mulligan_cards.has_value()) {
            return kEngineInvalidCard;
        }
        const auto player_id = static_cast<scgs::PlayerId>(command.player);
        const std::vector<scgs::InstanceId>& hand = session.board.player(player_id).hand;
        std::vector<std::uint64_t> unique;
        for (const std::uint64_t card : *command.mulligan_cards) {
            if (std::find(hand.begin(), hand.end(), card) == hand.end()) {
                return kEngineInvalidCard;
            }
            if (std::find(unique.begin(), unique.end(), card) != unique.end()) {
                return 33U;
            }
            unique.push_back(card);
        }
        return kEngineOk;
    }
    if (action == 9U) {
        if (mulligan_phase || command.player != session.active_player) {
            return kEngineInvalidPhase;
        }
        return kEngineOk;
    }
    return kEngineInvalidCard;
}

const char* engine_message(const std::uint32_t code) noexcept {
    switch (code) {
        case kEngineOk:
            return "ok";
        case kEngineInvalidPhase:
            return "invalid phase";
        case kEngineInvalidPlayer:
            return "invalid player";
        case kEngineInvalidCard:
            return "product cards are not enabled in the Gate 5B foundation adapter";
        case kEngineGameOver:
            return "game over";
        case kEngineStaleRevision:
            return "stale revision";
        case kEngineChoicePending:
            return "a product choice is pending";
        case kEngineNoPendingChoice:
            return "no pending choice";
        case kEngineInvalidChoice:
            return "invalid or unrelated choice fields";
        case kEngineInvalidMode:
            return "invalid or unrelated mode field";
        case kEngineInvalidAdditionalCost:
            return "invalid or unrelated additional cost fields";
        case kEngineChoiceNotOwned:
            return "choice is owned by the other player";
        case kEngineMatchNotStarted:
            return "match not started";
        case kEngineMulliganAlreadyDone:
            return "mulligan already done";
        default:
            return "engine failure";
    }
}

void require_valid_query(
    const FoundationSession& session,
    const RequestShape& query) {
    if (validate_request_shape(query) != kEngineOk) {
        fail(
            SCGS_V05_SCHEMA_MISMATCH,
            "The query contains a field unrelated to its selected action.");
    }
    if (!session.started) {
        fail(SCGS_V05_INVALID_ARGUMENT, "The query requires a started match.");
    }
    if (query.expected_revision != session.revision) {
        fail(SCGS_V05_INVALID_ARGUMENT, "The query revision is stale.");
    }
    if (session.finished) {
        fail(SCGS_V05_INVALID_ARGUMENT, "The query cannot inspect a finished match.");
    }
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
std::unordered_map<scgs_v05_handle, std::shared_ptr<FoundationSession>> g_registry;
scgs_v05_handle g_next_handle = 1U;

std::shared_ptr<FoundationSession> find_session(const scgs_v05_handle handle) {
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

scgs_v05_handle add_session(std::shared_ptr<FoundationSession> session) {
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
        *out_handle = add_session(std::make_shared<FoundationSession>(std::move(config)));
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
        const std::shared_ptr<FoundationSession> session = find_session(handle);
        const std::lock_guard<std::mutex> lock(session->mutex);
        if (session->started) {
            *out_engine_code = kEngineMatchAlreadyStarted;
            return SCGS_V05_OK;
        }
        session->begin_choice_tokens();
        session->started = true;
        session->first_player = session->config.first_player_mode == 0U
            ? session->config.seed % 2U
            : session->config.first_player_mode - 1U;
        session->active_player = session->first_player;
        session->revision = 1U;
        const std::vector<scgs::InstanceId>& hand =
            session->board.player(scgs::PlayerId::Player0).hand;
        scgs::v2::PendingChoice choice;
        choice.choice_id = 1U;
        choice.chooser = scgs::PlayerId::Player0;
        choice.kind = scgs::v2::ChoiceKind::Cards;
        choice.minimum = 1U;
        choice.maximum = 1U;
        choice.options = {
            scgs::v2::ChoiceOption{session->active_option_ids.at(0), hand.at(0)},
            scgs::v2::ChoiceOption{session->active_option_ids.at(1), hand.at(1)},
        };
        if (!session->resolution.suspend_for_choice(std::move(choice))) {
            throw std::logic_error("failed to open the v05 foundation pending choice");
        }
        append_event(
            *session,
            0U,
            session->first_player,
            "match started",
            {},
            false,
            session->first_player);
        *out_engine_code = kEngineOk;
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
        const std::shared_ptr<FoundationSession> session = find_session(handle);
        const std::lock_guard<std::mutex> lock(session->mutex);
        Json output = make_envelope(session->revision);
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
        const std::shared_ptr<FoundationSession> session = find_session(handle);
        const std::lock_guard<std::mutex> lock(session->mutex);
        require_valid_query(*session, query);
        Json actions = Json::array();
        const auto& pending = session->resolution.pending_choice();
        if (pending.has_value()) {
            if (query.player == static_cast<std::uint32_t>(pending->chooser) &&
                (!query.action.has_value() || *query.action == 13U)) {
                for (const scgs::v2::ChoiceOption& option : pending->options) {
                    Json command = make_resolve_choice_command(
                        *session, query.player, session->revision, option.option_id);
                    actions.push_back(
                        Json{{"command", std::move(command)}, {"payment", make_payment(0U, "ok")}});
                }
            }
        } else {
            const bool in_mulligan = !(session->mulligan_done[0] && session->mulligan_done[1]);
            if (in_mulligan && !session->mulligan_done[query.player] &&
                (!query.action.has_value() || *query.action == 0U)) {
                Json command = make_command(query.player, 0U, session->revision);
                actions.push_back(Json{{"command", std::move(command)}, {"payment", make_payment(0U, "ok")}});
            } else if (!in_mulligan && query.player == session->active_player) {
                Json end_turn = make_command(query.player, 9U, session->revision);
                if (!query.action.has_value() || *query.action == 9U) {
                    actions.push_back(Json{{"command", std::move(end_turn)}, {"payment", make_payment(0U, "ok")}});
                }
            }
        }
        if (!query.action.has_value() || *query.action == 10U) {
            Json surrender = make_command(query.player, 10U, session->revision);
            actions.push_back(
                Json{{"command", std::move(surrender)}, {"payment", make_payment(0U, "ok")}});
        }
        Json output = make_envelope(session->revision);
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
        const std::shared_ptr<FoundationSession> session = find_session(handle);
        const std::lock_guard<std::mutex> lock(session->mutex);
        require_valid_query(*session, query);
        // The foundation adapter exposes no product action until the product
        // executor is connected. Returning no targets is therefore the only
        // result that can stay in lockstep with submit_command.
        Json output = make_envelope(session->revision);
        output["targets"] = Json::array();
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
        const std::shared_ptr<FoundationSession> session = find_session(handle);
        const std::lock_guard<std::mutex> lock(session->mutex);
        require_valid_query(*session, query);
        // No product play command is submit-able by this adapter yet, so
        // enumerating nominal board indices would violate query/submit parity.
        Json slots = Json::array();
        Json output = make_envelope(session->revision);
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
        const std::shared_ptr<FoundationSession> session = find_session(handle);
        const std::lock_guard<std::mutex> lock(session->mutex);
        require_valid_query(*session, query);
        Json output = make_envelope(session->revision);
        output["donors"] = Json::array();
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
        const std::shared_ptr<FoundationSession> session = find_session(handle);
        const std::lock_guard<std::mutex> lock(session->mutex);
        const std::uint32_t code = validate_command(*session, command);
        Json output = make_envelope(session->revision);
        output["payment"] = make_payment(code, engine_message(code));
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
        (void)parse_viewer(viewer);
        const std::shared_ptr<FoundationSession> session = find_session(handle);
        const std::lock_guard<std::mutex> lock(session->mutex);
        Json output = make_envelope(session->revision);
        output["reaction"] = make_reaction(*session);
        output["pending_choice"] = make_pending_choice(*session, viewer);
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
        const std::shared_ptr<FoundationSession> session = find_session(handle);
        const std::lock_guard<std::mutex> lock(session->mutex);
        const std::uint32_t code = validate_command(*session, command);
        *out_engine_code = code;
        if (code != kEngineOk) {
            return SCGS_V05_OK;
        }
        const std::uint32_t action = *command.action;
        if (action == 0U) {
            session->mulligan_done[command.player] = true;
            append_event(
                *session,
                24U,
                command.player,
                "mulligan completed",
                "opponent completed mulligan",
                true);
        } else if (action == 9U) {
            append_event(*session, 2U, command.player, "turn ended");
            session->active_player = 1U - session->active_player;
            append_event(*session, 1U, session->active_player, "turn started");
        } else if (action == 10U) {
            session->resolution.finish_match();
            session->clear_choice_tokens();
            session->finished = true;
            session->result = command.player == 0U ? 2U : 1U;
            append_event(*session, 22U, command.player, "player surrendered");
            append_event(*session, 23U, 1U - command.player, "match ended");
        } else if (action == 13U) {
            const scgs::v2::Status status = session->resolution.resolve_choice(
                static_cast<scgs::PlayerId>(command.player),
                1U,
                *command.selected_option_ids);
            if (!status) {
                *out_engine_code = kEngineInvalidChoice;
                return SCGS_V05_OK;
            }
            (void)session->resolution.take_resolved_choice();
            session->clear_choice_tokens();
            append_event(
                *session,
                25U,
                command.player,
                "private choice completed",
                "opponent completed a private choice",
                true);
        }
        ++session->revision;
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
        const std::shared_ptr<FoundationSession> session = find_session(handle);
        const std::lock_guard<std::mutex> lock(session->mutex);
        Json events = Json::array();
        std::uint64_t last_sequence = after_sequence;
        for (const StoredEvent& event : session->events) {
            if (event.sequence <= after_sequence) {
                continue;
            }
            const bool hidden = event.hide_from_opponent && parsed_viewer != event.player;
            Json value{
                {"sequence", event.sequence},
                {"type", event.type},
                {"player", event.player},
                {"value", 0},
                {"secondary_value", 0},
                {"hidden_card", hidden},
                {"text", hidden ? event.opponent_text : event.public_text}};
            if (event.first_player.has_value()) {
                value["first_player"] = *event.first_player;
            }
            // No event emitted by v05 contains a random_seed field.
            events.push_back(std::move(value));
            last_sequence = event.sequence;
        }
        Json output = make_envelope(session->revision);
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
