// SPDX-License-Identifier: GPL-3.0-or-later
#include "scgs/native_api_v04.h"

#include "scgs/game.hpp"

#include <nlohmann/json.hpp>

#include <algorithm>
#include <array>
#include <cstdint>
#include <exception>
#include <functional>
#include <iostream>
#include <limits>
#include <memory>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace {

using Json = nlohmann::json;
using OutputCall = std::function<scgs_v04_native_code(char*, std::uint64_t, std::uint64_t*)>;

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

constexpr std::uint32_t kSeed = 0x12345678U;
constexpr unsigned char kGuardByte = 0xA5U;
std::array<bool, 11> g_seen_action_kinds{};

Json fixed_config(
    const std::uint32_t seed = kSeed,
    const std::uint32_t first_player_mode = 1U,
    const bool shuffle = false) {
    return Json{
        {"schema_version", SCGS_V04_SCHEMA_VERSION},
        {"player0_deck", "midrange"},
        {"player1_deck", "advance"},
        {"random_seed", seed},
        {"first_player_mode", first_player_mode},
        {"shuffle_decks", shuffle},
    };
}

Json query_for(const std::uint32_t player, const std::uint64_t revision) {
    return Json{
        {"schema_version", SCGS_V04_SCHEMA_VERSION},
        {"player", player},
        {"expected_revision", revision},
    };
}

scgs_v04_native_code create_raw(
    const std::string_view payload,
    scgs_v04_handle* out_handle,
    const std::uint32_t abi = SCGS_V04_ABI_VERSION) {
    return scgs_v04_create(abi, payload.data(), payload.size(), out_handle);
}

scgs_v04_handle create_game(TestContext& context, const Json& config = fixed_config()) {
    const std::string bytes = config.dump();
    scgs_v04_handle handle = 0;
    EXPECT(context, create_raw(bytes, &handle) == SCGS_V04_OK);
    EXPECT(context, handle != 0U);
    return handle;
}

void destroy_game(TestContext& context, scgs_v04_handle& handle) {
    if (handle != 0U) {
        EXPECT(context, scgs_v04_destroy(handle) == SCGS_V04_OK);
        handle = 0U;
    }
}

void start_game(TestContext& context, const scgs_v04_handle handle) {
    std::uint32_t engine_code = SCGS_V04_NO_ENGINE_CODE;
    EXPECT(context, scgs_v04_start(handle, &engine_code) == SCGS_V04_OK);
    EXPECT(context, engine_code == 0U);
}

struct BufferResult {
    std::string text;
    Json json;
};

BufferResult exercise_buffer_contract(
    TestContext& context,
    const OutputCall& call,
    const bool parse_json = true) {
    std::uint64_t required = 0;
    EXPECT(context, call(nullptr, 0U, nullptr) == SCGS_V04_INVALID_ARGUMENT);
    EXPECT(context, call(nullptr, 0U, &required) == SCGS_V04_BUFFER_TOO_SMALL);
    EXPECT(context, required >= 2U);
    if (required < 2U || required > 16U * 1024U * 1024U) {
        return {};
    }

    const auto size = static_cast<std::size_t>(required);
    std::vector<unsigned char> short_buffer(size + 8U, kGuardByte);
    std::uint64_t short_required = 0;
    EXPECT(context,
           call(
               reinterpret_cast<char*>(short_buffer.data()),
               required - 1U,
               &short_required) == SCGS_V04_BUFFER_TOO_SMALL);
    EXPECT(context, short_required == required);
    EXPECT(context, std::all_of(short_buffer.begin(), short_buffer.end(), [](const unsigned char byte) {
        return byte == kGuardByte;
    }));

    std::vector<unsigned char> exact_buffer(size + 8U, kGuardByte);
    std::uint64_t exact_required = 0;
    EXPECT(context,
           call(
               reinterpret_cast<char*>(exact_buffer.data()),
               required,
               &exact_required) == SCGS_V04_OK);
    EXPECT(context, exact_required == required);
    EXPECT(context, exact_buffer[size - 1U] == 0U);
    EXPECT(context, std::all_of(
        exact_buffer.begin() + static_cast<std::ptrdiff_t>(size),
        exact_buffer.end(),
        [](const unsigned char byte) { return byte == kGuardByte; }));

    const std::string exact_text(
        reinterpret_cast<const char*>(exact_buffer.data()),
        size - 1U);

    std::vector<unsigned char> large_buffer(size + 16U, kGuardByte);
    std::uint64_t large_required = 0;
    EXPECT(context,
           call(
               reinterpret_cast<char*>(large_buffer.data()),
               large_buffer.size(),
               &large_required) == SCGS_V04_OK);
    EXPECT(context, large_required == required);
    EXPECT(context, large_buffer[size - 1U] == 0U);
    EXPECT(context, std::all_of(
        large_buffer.begin() + static_cast<std::ptrdiff_t>(size),
        large_buffer.end(),
        [](const unsigned char byte) { return byte == kGuardByte; }));
    EXPECT(context,
           exact_text == std::string(
                             reinterpret_cast<const char*>(large_buffer.data()),
                             size - 1U));

    BufferResult result;
    result.text = exact_text;
    if (parse_json) {
        EXPECT(context, Json::accept(result.text));
        try {
            result.json = Json::parse(result.text);
        } catch (const std::exception& exception) {
            std::cerr << "JSON parse failed in native test: " << exception.what() << '\n';
            ++context.failures;
        }
    }
    return result;
}

BufferResult get_view(
    TestContext& context,
    const scgs_v04_handle handle,
    const std::uint32_t viewer,
    const bool full_buffer_test = false) {
    const OutputCall call = [=](char* buffer, const std::uint64_t capacity, std::uint64_t* required) {
        return scgs_v04_get_view_json(handle, viewer, buffer, capacity, required);
    };
    if (full_buffer_test) {
        return exercise_buffer_contract(context, call);
    }

    std::uint64_t required = 0;
    EXPECT(context, call(nullptr, 0U, &required) == SCGS_V04_BUFFER_TOO_SMALL);
    if (required < 2U) {
        return {};
    }
    std::vector<char> buffer(static_cast<std::size_t>(required));
    EXPECT(context, call(buffer.data(), required, &required) == SCGS_V04_OK);
    BufferResult result;
    result.text.assign(buffer.data(), buffer.size() - 1U);
    try {
        result.json = Json::parse(result.text);
    } catch (const std::exception& exception) {
        std::cerr << "view parse failed: " << exception.what() << '\n';
        ++context.failures;
    }
    return result;
}

BufferResult call_with_json(
    TestContext& context,
    const Json& input,
    const std::function<scgs_v04_native_code(
        const char*, std::uint64_t, char*, std::uint64_t, std::uint64_t*)>& function,
    const bool full_buffer_test = false) {
    const std::string bytes = input.dump();
    const OutputCall call = [&](char* buffer, const std::uint64_t capacity, std::uint64_t* required) {
        return function(bytes.data(), bytes.size(), buffer, capacity, required);
    };
    if (full_buffer_test) {
        return exercise_buffer_contract(context, call);
    }
    std::uint64_t required = 0;
    EXPECT(context, call(nullptr, 0U, &required) == SCGS_V04_BUFFER_TOO_SMALL);
    if (required < 2U) {
        return {};
    }
    std::vector<char> buffer(static_cast<std::size_t>(required));
    EXPECT(context, call(buffer.data(), required, &required) == SCGS_V04_OK);
    BufferResult result;
    result.text.assign(buffer.data(), buffer.size() - 1U);
    try {
        result.json = Json::parse(result.text);
    } catch (const std::exception& exception) {
        std::cerr << "native output parse failed: " << exception.what() << '\n';
        ++context.failures;
    }
    return result;
}

BufferResult list_actions(
    TestContext& context,
    const scgs_v04_handle handle,
    const Json& query,
    const bool full_buffer_test = false) {
    return call_with_json(
        context,
        query,
        [=](const char* input,
            const std::uint64_t input_size,
            char* output,
            const std::uint64_t capacity,
            std::uint64_t* required) {
            return scgs_v04_list_legal_actions_json(
                handle, input, input_size, output, capacity, required);
        },
        full_buffer_test);
}

BufferResult read_events(
    TestContext& context,
    const scgs_v04_handle handle,
    const std::uint32_t viewer,
    const std::uint64_t after_sequence,
    const bool full_buffer_test = false) {
    const OutputCall call = [=](char* buffer, const std::uint64_t capacity, std::uint64_t* required) {
        return scgs_v04_read_events_json(
            handle, viewer, after_sequence, buffer, capacity, required);
    };
    if (full_buffer_test) {
        return exercise_buffer_contract(context, call);
    }
    std::uint64_t required = 0;
    EXPECT(context, call(nullptr, 0U, &required) == SCGS_V04_BUFFER_TOO_SMALL);
    if (required < 2U) {
        return {};
    }
    std::vector<char> buffer(static_cast<std::size_t>(required));
    EXPECT(context, call(buffer.data(), required, &required) == SCGS_V04_OK);
    BufferResult result;
    result.text.assign(buffer.data(), buffer.size() - 1U);
    try {
        result.json = Json::parse(result.text);
    } catch (const std::exception& exception) {
        std::cerr << "event parse failed: " << exception.what() << '\n';
        ++context.failures;
    }
    return result;
}

std::uint32_t submit(TestContext& context, const scgs_v04_handle handle, Json command) {
    command["schema_version"] = SCGS_V04_SCHEMA_VERSION;
    const std::string bytes = command.dump();
    std::uint32_t engine_code = SCGS_V04_NO_ENGINE_CODE;
    EXPECT(context,
           scgs_v04_submit_command_json(
               handle, bytes.data(), bytes.size(), &engine_code) == SCGS_V04_OK);
    const std::uint32_t action = command.value("action", 99U);
    if (engine_code == 0U && action < g_seen_action_kinds.size()) {
        g_seen_action_kinds[action] = true;
    }
    return engine_code;
}

void expect_envelope(
    TestContext& context,
    const Json& envelope,
    const char* payload_name,
    const std::uint64_t revision) {
    EXPECT(context, envelope.is_object());
    EXPECT(context, envelope.value("schema_version", 0U) == SCGS_V04_SCHEMA_VERSION);
    EXPECT(context, envelope.value("revision", std::numeric_limits<std::uint64_t>::max()) == revision);
    EXPECT(context, envelope.contains(payload_name));
}

const Json* find_action(const Json& actions, const std::uint32_t kind) {
    if (!actions.is_array()) {
        return nullptr;
    }
    const auto iterator = std::find_if(actions.begin(), actions.end(), [kind](const Json& legal) {
        return legal.is_object() && legal.contains("command") &&
               legal["command"].value("action", std::numeric_limits<std::uint32_t>::max()) == kind;
    });
    return iterator == actions.end() ? nullptr : std::addressof(*iterator);
}

std::optional<Json> choose_agent_command(const Json& actions) {
    const auto choose = [&actions](const std::uint32_t action, const bool leader_only) -> std::optional<Json> {
        if (!actions.is_array()) {
            return std::nullopt;
        }
        for (const Json& legal : actions) {
            if (!legal.is_object() || !legal.contains("command")) {
                continue;
            }
            const Json& command = legal["command"];
            if (command.value("action", std::numeric_limits<std::uint32_t>::max()) != action) {
                continue;
            }
            if (leader_only &&
                (!command.contains("target") || command["target"].value("kind", 1U) != 0U)) {
                continue;
            }
            return command;
        }
        return std::nullopt;
    };

    for (const auto& [action, leader_only] : {
             std::pair{4U, true},
             std::pair{4U, false},
             std::pair{5U, false},
             std::pair{1U, false},
             std::pair{2U, false},
             std::pair{6U, false},
             std::pair{3U, false},
             std::pair{7U, false},
             std::pair{8U, false},
             std::pair{9U, false},
             std::pair{0U, false},
         }) {
        if (std::optional<Json> command = choose(action, leader_only); command.has_value()) {
            return command;
        }
    }
    return std::nullopt;
}

std::string source_name(const Json& view, const std::uint32_t player, const std::uint64_t source) {
    const Json& owner = view["players"][player];
    for (const char* zone : {"hand", "units", "tactics", "standby"}) {
        for (const Json& card : owner[zone]) {
            if (!card.is_null() && card.value("instance_id", 0U) == source) {
                return card.value("name", std::string{});
            }
        }
    }
    return {};
}

std::optional<Json> find_command(
    const Json& actions,
    const std::uint32_t action,
    const Json* view = nullptr,
    const std::uint32_t player = 0U,
    const std::string_view required_source_name = {}) {
    if (!actions.is_array()) {
        return std::nullopt;
    }
    for (const Json& legal : actions) {
        if (!legal.contains("command")) {
            continue;
        }
        const Json& command = legal["command"];
        if (command.value("action", 99U) != action) {
            continue;
        }
        if (!required_source_name.empty()) {
            if (view == nullptr ||
                source_name(*view, player, command.value("source", 0U)) != required_source_name) {
                continue;
            }
        }
        return command;
    }
    return std::nullopt;
}

void complete_empty_mulligans(TestContext& context, const scgs_v04_handle handle) {
    for (const std::uint32_t player : {0U, 1U}) {
        const std::uint64_t revision = get_view(context, handle, player).json.value("revision", 0U);
        EXPECT(context,
               submit(
                   context,
                   handle,
                   Json{
                       {"player", player},
                       {"action", 0U},
                       {"mulligan_cards", Json::array()},
                       {"expected_revision", revision},
                   }) == 0U);
    }
}

void advance_to_own_turn(
    TestContext& context,
    const scgs_v04_handle handle,
    const std::uint32_t player,
    const int own_turn_number) {
    for (int guard = 0; guard < 64; ++guard) {
        const BufferResult snapshot = get_view(context, handle, player);
        const Json& view = snapshot.json["view"];
        if (view.value("phase", 0U) == 2U && view.value("active_player", 99U) == player &&
            view["players"][player].value("own_turn_number", 0) >= own_turn_number) {
            return;
        }
        EXPECT(context, view.value("phase", 0U) == 2U);
        const std::uint32_t actor = view.value("active_player", 99U);
        const BufferResult actions = list_actions(
            context, handle, query_for(actor, snapshot.json.value("revision", 0U)));
        const std::optional<Json> end_turn = find_command(actions.json["actions"], 9U);
        EXPECT(context, end_turn.has_value());
        if (!end_turn.has_value()) {
            return;
        }
        EXPECT(context, submit(context, handle, *end_turn) == 0U);
    }
    EXPECT(context, false);
}

BufferResult preview_command(
    TestContext& context,
    const scgs_v04_handle handle,
    Json command) {
    command["schema_version"] = SCGS_V04_SCHEMA_VERSION;
    return call_with_json(
        context,
        command,
        [=](const char* input,
            const std::uint64_t input_size,
            char* output,
            const std::uint64_t capacity,
            std::uint64_t* required) {
            return scgs_v04_preview_payment_json(
                handle, input, input_size, output, capacity, required);
        });
}

void test_version_status_and_lifecycle(TestContext& context) {
    static_assert(sizeof(scgs_v04_handle) == 8U);
    static_assert(sizeof(scgs_v04_native_code) == 4U);
    static_assert(SCGS_V04_OK == 0);
    static_assert(SCGS_V04_INTERNAL_ERROR == 10);
    EXPECT(context, scgs_v04_abi_version() == SCGS_V04_ABI_VERSION);
    EXPECT(context, SCGS_V04_ABI_VERSION == 0x00010000U);
    EXPECT(context, SCGS_V04_SCHEMA_VERSION == 1U);

    const std::string config = fixed_config().dump();
    scgs_v04_handle first = 99U;
    EXPECT(context, create_raw(config, &first, 0x00020000U) == SCGS_V04_ABI_MISMATCH);
    EXPECT(context, first == 0U);
    first = 99U;
    EXPECT(context, create_raw(config, &first, 0x00010001U) == SCGS_V04_ABI_MISMATCH);
    EXPECT(context, first == 0U);
    EXPECT(context, create_raw(config, nullptr) == SCGS_V04_INVALID_ARGUMENT);
    EXPECT(context, scgs_v04_create(SCGS_V04_ABI_VERSION, nullptr, 1U, &first) ==
                        SCGS_V04_INVALID_ARGUMENT);
    EXPECT(context, scgs_v04_create(SCGS_V04_ABI_VERSION, config.data(), 0U, &first) ==
                        SCGS_V04_INVALID_ARGUMENT);

    first = create_game(context);
    scgs_v04_handle second = create_game(context);
    EXPECT(context, first != second);
    EXPECT(context, scgs_v04_destroy(0U) == SCGS_V04_OK);
    EXPECT(context, scgs_v04_start(first, nullptr) == SCGS_V04_INVALID_ARGUMENT);
    start_game(context, first);
    std::uint32_t second_start_code = SCGS_V04_NO_ENGINE_CODE;
    EXPECT(context, scgs_v04_start(first, &second_start_code) == SCGS_V04_OK);
    EXPECT(context,
           second_start_code == static_cast<std::uint32_t>(scgs::ErrorCode::MatchAlreadyStarted));

    const scgs_v04_handle destroyed = first;
    destroy_game(context, first);
    EXPECT(context, scgs_v04_destroy(destroyed) == SCGS_V04_INVALID_HANDLE);
    std::uint32_t engine_code = 123U;
    EXPECT(context, scgs_v04_start(destroyed, &engine_code) == SCGS_V04_INVALID_HANDLE);
    EXPECT(context, engine_code == SCGS_V04_NO_ENGINE_CODE);
    EXPECT(context,
           scgs_v04_get_view_json(
               std::numeric_limits<scgs_v04_handle>::max(), 0U, nullptr, 0U, nullptr) ==
               SCGS_V04_INVALID_ARGUMENT);
    destroy_game(context, second);

    scgs_v04_handle third = create_game(context);
    EXPECT(context, third != destroyed);
    destroy_game(context, third);
}

void test_input_validation_and_safe_errors(TestContext& context) {
    const auto expect_create_code = [&](const std::string_view payload, const scgs_v04_native_code expected) {
        scgs_v04_handle handle = 77U;
        EXPECT(context, create_raw(payload, &handle) == expected);
        EXPECT(context, handle == 0U);
    };

    expect_create_code("{", SCGS_V04_INVALID_JSON);
    expect_create_code("[]", SCGS_V04_SCHEMA_MISMATCH);
    expect_create_code("{}", SCGS_V04_SCHEMA_MISMATCH);
    expect_create_code(
        R"({"schema_version":2,"player0_deck":"midrange","player1_deck":"advance"})",
        SCGS_V04_SCHEMA_MISMATCH);
    expect_create_code(
        R"({"schema_version":1,"player0_deck":"unknown","player1_deck":"advance"})",
        SCGS_V04_SCHEMA_MISMATCH);
    expect_create_code(
        R"({"schema_version":1,"player0_deck":7,"player1_deck":"advance"})",
        SCGS_V04_SCHEMA_MISMATCH);
    expect_create_code(
        R"({"schema_version":1,"player0_deck":"midrange","player1_deck":"advance","random_seed":-1})",
        SCGS_V04_SCHEMA_MISMATCH);
    expect_create_code(
        R"({"schema_version":1,"player0_deck":"midrange","player1_deck":"advance","random_seed":4294967296})",
        SCGS_V04_SCHEMA_MISMATCH);
    expect_create_code(
        R"({"schema_version":1,"player0_deck":"midrange","player1_deck":"advance","first_player_mode":99})",
        SCGS_V04_SCHEMA_MISMATCH);
    expect_create_code(
        R"({"schema_version":1,"player0_deck":"midrange","player1_deck":"advance","shuffle_decks":"false"})",
        SCGS_V04_SCHEMA_MISMATCH);
    expect_create_code(
        R"({"schema_version":1,"player0_deck":"midrange","player1_deck":"advance","random_seed":null})",
        SCGS_V04_SCHEMA_MISMATCH);

    std::string invalid_utf8 =
        R"({"schema_version":1,"player0_deck":"midrange","player1_deck":"advance","note":")";
    invalid_utf8.push_back(static_cast<char>(0xC3));
    invalid_utf8.push_back('(');
    invalid_utf8 += R"("})";
    expect_create_code(invalid_utf8, SCGS_V04_INVALID_UTF8);

    const char one_byte = '{';
    scgs_v04_handle oversized_handle = 55U;
    EXPECT(context,
           scgs_v04_create(
               SCGS_V04_ABI_VERSION,
               &one_byte,
               1024U * 1024U + 1U,
               &oversized_handle) == SCGS_V04_PAYLOAD_TOO_LARGE);
    EXPECT(context, oversized_handle == 0U);

    Json extensible = fixed_config();
    extensible["future_client_hint"] = Json{{"ignored", true}};
    scgs_v04_handle handle = create_game(context, extensible);
    start_game(context, handle);
    const std::uint64_t revision = get_view(context, handle, 0U).json.value("revision", 0U);

    const auto expect_bad_query = [&](Json query) {
        const std::string bytes = query.dump();
        std::uint64_t required = 999U;
        EXPECT(context,
               scgs_v04_list_legal_actions_json(
                   handle, bytes.data(), bytes.size(), nullptr, 0U, &required) ==
                   SCGS_V04_SCHEMA_MISMATCH);
        EXPECT(context, required == 0U);
    };
    expect_bad_query(Json{{"schema_version", 1}, {"player", 99}, {"expected_revision", revision}});
    expect_bad_query(Json{{"schema_version", 1}, {"player", 0}, {"expected_revision", -1}});
    expect_bad_query(
        Json{{"schema_version", 1}, {"player", 0}, {"expected_revision", revision}, {"action", 99}});
    expect_bad_query(
        Json{{"schema_version", 1}, {"player", 0}, {"expected_revision", revision}, {"target", nullptr}});
    expect_bad_query(
        Json{{"schema_version", 1}, {"player", 0}, {"expected_revision", revision}, {"slot", nullptr}});
    expect_bad_query(Json{
        {"schema_version", 1},
        {"player", 0},
        {"expected_revision", revision},
        {"use_advance", "yes"},
    });
    expect_bad_query(Json{
        {"schema_version", 1},
        {"player", 0},
        {"expected_revision", revision},
        {"mulligan_cards", Json::array({"not-an-id"})},
    });

    EXPECT(context,
           scgs_v04_get_view_json(handle, 2U, nullptr, 0U, nullptr) ==
               SCGS_V04_INVALID_ARGUMENT);
    EXPECT(context,
           scgs_v04_get_reaction_context_json(handle, 99U, nullptr, 0U, nullptr) ==
               SCGS_V04_INVALID_ARGUMENT);
    EXPECT(context,
           scgs_v04_read_events_json(handle, 7U, 0U, nullptr, 0U, nullptr) ==
               SCGS_V04_INVALID_ARGUMENT);

    Json invalid_command{
        {"schema_version", 1},
        {"player", 0},
        {"action", 99},
        {"expected_revision", revision},
    };
    const std::string invalid_command_bytes = invalid_command.dump();
    std::uint32_t engine_code = 123U;
    EXPECT(context,
           scgs_v04_submit_command_json(
               handle,
               invalid_command_bytes.data(),
               invalid_command_bytes.size(),
               &engine_code) == SCGS_V04_SCHEMA_MISMATCH);
    EXPECT(context, engine_code == SCGS_V04_NO_ENGINE_CODE);
    EXPECT(context,
           scgs_v04_submit_command_json(
               handle,
               invalid_command_bytes.data(),
               invalid_command_bytes.size(),
               nullptr) == SCGS_V04_INVALID_ARGUMENT);

    const OutputCall last_error_call = [](char* buffer, const std::uint64_t capacity, std::uint64_t* required) {
        return scgs_v04_get_last_error(buffer, capacity, required);
    };
    const BufferResult error = exercise_buffer_contract(context, last_error_call, false);
    EXPECT(context, !error.text.empty());
    EXPECT(context, error.text.find("unknown") == std::string::npos);
    EXPECT(context, error.text.find("midrange") == std::string::npos);

    destroy_game(context, handle);
}

void test_all_output_buffer_contracts(TestContext& context) {
    scgs_v04_handle handle = create_game(context);
    start_game(context, handle);

    const BufferResult view = get_view(context, handle, 0U, true);
    const std::uint64_t revision = view.json.value("revision", 0U);
    expect_envelope(context, view.json, "view", revision);
    EXPECT(context, view.text.find("先驱侦察兵") != std::string::npos);

    const Json query = query_for(0U, revision);
    const BufferResult actions = list_actions(context, handle, query, true);
    expect_envelope(context, actions.json, "actions", revision);
    EXPECT(context, actions.json["actions"].is_array());
    EXPECT(context, !actions.json["actions"].empty());

    const auto input_function = [handle](const auto function) {
        return [handle, function](
                   const char* input,
                   const std::uint64_t input_size,
                   char* output,
                   const std::uint64_t capacity,
                   std::uint64_t* required) {
            return function(handle, input, input_size, output, capacity, required);
        };
    };

    const BufferResult targets = call_with_json(
        context, query, input_function(scgs_v04_list_valid_targets_json), true);
    expect_envelope(context, targets.json, "targets", revision);
    EXPECT(context, targets.json["targets"].is_array());

    const BufferResult slots = call_with_json(
        context, query, input_function(scgs_v04_list_valid_slots_json), true);
    expect_envelope(context, slots.json, "slots", revision);
    EXPECT(context, slots.json["slots"].is_array());

    const BufferResult donors = call_with_json(
        context, query, input_function(scgs_v04_list_valid_donors_json), true);
    expect_envelope(context, donors.json, "donors", revision);
    EXPECT(context, donors.json["donors"].is_array());

    Json command = actions.json["actions"].front()["command"];
    command["schema_version"] = SCGS_V04_SCHEMA_VERSION;
    const BufferResult payment = call_with_json(
        context, command, input_function(scgs_v04_preview_payment_json), true);
    expect_envelope(context, payment.json, "payment", revision);
    EXPECT(context, payment.json["payment"].is_object());

    const OutputCall reaction_call = [=](char* buffer, const std::uint64_t capacity, std::uint64_t* required) {
        return scgs_v04_get_reaction_context_json(handle, 0U, buffer, capacity, required);
    };
    const BufferResult reaction = exercise_buffer_contract(context, reaction_call);
    expect_envelope(context, reaction.json, "reaction", revision);
    EXPECT(context, reaction.json["reaction"].is_object());
    EXPECT(context, !reaction.json["reaction"].contains("origin"));

    const BufferResult events = read_events(context, handle, 0U, 0U, true);
    expect_envelope(context, events.json, "events", revision);
    EXPECT(context, events.json["events"].is_array());
    EXPECT(context, events.json.contains("last_sequence"));

    destroy_game(context, handle);
}

void test_privacy_event_cursors_and_atomicity(TestContext& context) {
    scgs_v04_handle handle = create_game(context);
    start_game(context, handle);

    const BufferResult player0 = get_view(context, handle, 0U);
    const BufferResult player1 = get_view(context, handle, 1U);
    const Json& p0_view = player0.json["view"];
    const Json& p1_view = player1.json["view"];
    EXPECT(context, p0_view["players"][0]["hand"].size() == 4U);
    EXPECT(context, p0_view["players"][1]["hand"].empty());
    EXPECT(context, p1_view["players"][1]["hand"].size() == 4U);
    EXPECT(context, p1_view["players"][0]["hand"].empty());
    EXPECT(context, p0_view["players"][1]["hand_count"] == 4U);
    EXPECT(context, p1_view["players"][0]["hand_count"] == 4U);
    EXPECT(context, player0.text.find("燃耗战士") == std::string::npos);
    EXPECT(context, player1.text.find("先驱侦察兵") == std::string::npos);

    const BufferResult p0_events_first = read_events(context, handle, 0U, 0U);
    const BufferResult p0_events_again = read_events(context, handle, 0U, 0U);
    const BufferResult p1_events_first = read_events(context, handle, 1U, 0U);
    EXPECT(context, p0_events_first.text == p0_events_again.text);
    EXPECT(context, p0_events_first.json["events"].size() == p1_events_first.json["events"].size());

    bool p0_saw_hidden_enemy_draw = false;
    bool p1_saw_own_draw = false;
    for (const Json& event : p0_events_first.json["events"]) {
        if (event.value("type", 99U) == 3U && event.value("player", 99U) == 1U) {
            p0_saw_hidden_enemy_draw = event.value("hidden_card", false) &&
                                       !event.contains("card") && !event.contains("definition_id") &&
                                       event.value("text", std::string{}).find("燃耗") == std::string::npos;
        }
    }
    for (const Json& event : p1_events_first.json["events"]) {
        if (event.value("type", 99U) == 3U && event.value("player", 99U) == 1U) {
            p1_saw_own_draw = !event.value("hidden_card", true) && event.contains("card") &&
                              event.contains("definition_id");
        }
    }
    EXPECT(context, p0_saw_hidden_enemy_draw);
    EXPECT(context, p1_saw_own_draw);
    EXPECT(context, p0_events_first.text.find("燃耗战士") == std::string::npos);

    const std::uint64_t first_cursor = p0_events_first.json.value("last_sequence", 0U);
    const BufferResult no_new_events = read_events(context, handle, 0U, first_cursor);
    EXPECT(context, no_new_events.json["events"].empty());
    EXPECT(context, read_events(context, handle, 1U, 0U).text == p1_events_first.text);

    const std::uint64_t revision = player0.json.value("revision", 0U);
    const std::string snapshot_before = player0.text;
    const std::string events_before = read_events(context, handle, 0U, 0U).text;
    Json stale_command{
        {"schema_version", 1},
        {"player", 0},
        {"action", 0},
        {"mulligan_cards", Json::array()},
        {"expected_revision", revision + 1U},
    };
    const std::string stale_bytes = stale_command.dump();
    std::uint32_t engine_code = SCGS_V04_NO_ENGINE_CODE;
    EXPECT(context,
           scgs_v04_submit_command_json(
               handle, stale_bytes.data(), stale_bytes.size(), &engine_code) == SCGS_V04_OK);
    EXPECT(context, engine_code == static_cast<std::uint32_t>(scgs::ErrorCode::StaleRevision));
    EXPECT(context, get_view(context, handle, 0U).text == snapshot_before);
    EXPECT(context, read_events(context, handle, 0U, 0U).text == events_before);

    const std::string malformed = "{";
    engine_code = 88U;
    EXPECT(context,
           scgs_v04_submit_command_json(
               handle, malformed.data(), malformed.size(), &engine_code) == SCGS_V04_INVALID_JSON);
    EXPECT(context, engine_code == SCGS_V04_NO_ENGINE_CODE);
    EXPECT(context, get_view(context, handle, 0U).text == snapshot_before);
    EXPECT(context, read_events(context, handle, 0U, 0U).text == events_before);

    Json mulligan = p0_view["players"][0]["hand"].front();
    Json complete_mulligan{
        {"player", 0},
        {"action", 0},
        {"mulligan_cards", Json::array({mulligan["instance_id"]})},
        {"expected_revision", revision},
    };
    EXPECT(context, submit(context, handle, complete_mulligan) == 0U);
    const BufferResult late_opponent_events = read_events(context, handle, 1U, first_cursor);
    EXPECT(context, late_opponent_events.json["events"].size() == 1U);
    if (!late_opponent_events.json["events"].empty()) {
        const Json& event = late_opponent_events.json["events"].front();
        EXPECT(context, event.value("type", 99U) == 24U);
        EXPECT(context, event.value("hidden_card", false));
        EXPECT(context, !event.contains("card"));
        EXPECT(context, !event.contains("definition_id"));
        EXPECT(context, event.value("value", 99) == 0);
    }

    destroy_game(context, handle);
}

void compare_card_with_direct(
    TestContext& context,
    const Json& native_card,
    const scgs::CardView& direct_card) {
    EXPECT(context, native_card.contains("instance_id") == direct_card.instance_id.has_value());
    if (direct_card.instance_id.has_value()) {
        EXPECT(context, native_card["instance_id"] == *direct_card.instance_id);
    }
    EXPECT(context, native_card.contains("definition_id") == direct_card.definition_id.has_value());
    if (direct_card.definition_id.has_value()) {
        EXPECT(context, native_card["definition_id"] == *direct_card.definition_id);
    }
    EXPECT(context, native_card.contains("definition") == direct_card.definition.has_value());
    if (direct_card.definition.has_value()) {
        const Json& definition = native_card["definition"];
        EXPECT(context, definition["id"] == direct_card.definition->id);
        EXPECT(context, definition["name"] == direct_card.definition->name);
        EXPECT(context, definition["kind"] == static_cast<std::uint32_t>(direct_card.definition->kind));
        EXPECT(context, definition["cost"] == direct_card.definition->cost);
        EXPECT(context, definition["attack"] == direct_card.definition->attack);
        EXPECT(context, definition["health"] == direct_card.definition->health);
        EXPECT(context, definition["countdown"] == direct_card.definition->countdown);
        EXPECT(context, definition["effects"].size() == direct_card.definition->effects.size());
        EXPECT(context,
               definition.contains("deployment") == direct_card.definition->deployment.has_value());
    }
    EXPECT(context, native_card.contains("kind") == direct_card.kind.has_value());
    if (direct_card.kind.has_value()) {
        EXPECT(context, native_card["kind"] == static_cast<std::uint32_t>(*direct_card.kind));
    }
    EXPECT(context, native_card["name"] == direct_card.name);
    EXPECT(context, native_card["owner"] == static_cast<std::uint32_t>(direct_card.owner));
    EXPECT(context, native_card["controller"] == static_cast<std::uint32_t>(direct_card.controller));
    EXPECT(context, native_card["zone"] == static_cast<std::uint32_t>(direct_card.zone));
    EXPECT(context, native_card["sequence"] == direct_card.sequence);
    EXPECT(context, native_card["cost"] == direct_card.cost);
    EXPECT(context, native_card["current_attack"] == direct_card.current_attack);
    EXPECT(context, native_card["current_health"] == direct_card.current_health);
    EXPECT(context, native_card["maximum_health"] == direct_card.maximum_health);
    EXPECT(context, native_card["keywords"] == direct_card.keywords);
    EXPECT(context, native_card["evolved"] == direct_card.evolved);
    EXPECT(context, native_card["attacked_this_turn"] == direct_card.attacked_this_turn);
    EXPECT(context, native_card["entered_this_turn"] == direct_card.entered_this_turn);
    EXPECT(context, native_card["temporary_rush"] == direct_card.temporary_rush);
    EXPECT(context, native_card["deployed_from_standby"] == direct_card.deployed_from_standby);
    EXPECT(context, native_card["face_down"] == direct_card.face_down);
    EXPECT(context, native_card["countdown"] == direct_card.countdown);
    EXPECT(context,
           native_card["granted_component"]["has_component"] ==
               direct_card.granted_component.has_component);
    EXPECT(context,
           native_card["granted_component"]["granted_kind"] ==
               static_cast<std::uint32_t>(direct_card.granted_component.granted_kind));
    EXPECT(context,
           native_card["granted_component"]["granted_amount"] ==
               direct_card.granted_component.granted_amount);
}

void compare_player_with_direct(
    TestContext& context,
    const Json& native_player,
    const scgs::PlayerView& direct_player) {
    EXPECT(context, native_player["player"] == static_cast<std::uint32_t>(direct_player.player));
    EXPECT(context, native_player["leader_health"] == direct_player.leader_health);
    EXPECT(context, native_player["maximum_leader_health"] == direct_player.maximum_leader_health);
    EXPECT(context, native_player["current_pp"] == direct_player.current_pp);
    EXPECT(context, native_player["pp_capacity"] == direct_player.pp_capacity);
    EXPECT(context, native_player["cracks"] == direct_player.cracks);
    EXPECT(context, native_player["evolution_energy"] == direct_player.evolution_energy);
    EXPECT(context, native_player["own_turn_number"] == direct_player.own_turn_number);
    EXPECT(context, native_player["fatigue_count"] == direct_player.fatigue_count);
    EXPECT(context, native_player["mulligan_done"] == direct_player.mulligan_done);
    EXPECT(context,
           native_player["evolution_used_this_turn"] == direct_player.evolution_used_this_turn);
    EXPECT(context, native_player["advance_used_this_turn"] == direct_player.advance_used_this_turn);
    EXPECT(context, native_player["deploy_used_this_turn"] == direct_player.deploy_used_this_turn);
    EXPECT(context, native_player["trap_set_this_turn"] == direct_player.trap_set_this_turn);
    EXPECT(context, native_player["leader_skill_used"] == direct_player.leader_skill_used);
    EXPECT(context,
           native_player["charge_granted_this_cycle"] == direct_player.charge_granted_this_cycle);
    EXPECT(context,
           native_player["friendly_deaths_this_cycle"] == direct_player.friendly_deaths_this_cycle);
    EXPECT(context, native_player["spells_used_this_turn"] == direct_player.spells_used_this_turn);
    EXPECT(context, native_player["units_played_this_turn"] == direct_player.units_played_this_turn);
    EXPECT(context, native_player["leader_skill"]["name"] == direct_player.leader_skill.name);
    EXPECT(context, native_player["leader_skill"]["cost"] == direct_player.leader_skill.cost);
    EXPECT(context,
           native_player["leader_skill"]["effects"].size() == direct_player.leader_skill.effects.size());
    EXPECT(context, native_player["deck_count"] == direct_player.deck_count);
    EXPECT(context, native_player["hand_count"] == direct_player.hand_count);

    const auto compare_list = [&](const char* name, const std::vector<scgs::CardView>& direct) {
        EXPECT(context, native_player[name].size() == direct.size());
        const std::size_t count = std::min(native_player[name].size(), direct.size());
        for (std::size_t index = 0; index < count; ++index) {
            compare_card_with_direct(context, native_player[name][index], direct[index]);
        }
    };
    compare_list("hand", direct_player.hand);
    compare_list("graveyard", direct_player.graveyard);
    compare_list("archive", direct_player.archive);
    compare_list("standby", direct_player.standby);

    EXPECT(context, native_player["units"].size() == direct_player.units.size());
    for (std::size_t index = 0; index < direct_player.units.size(); ++index) {
        EXPECT(context, native_player["units"][index].is_null() == !direct_player.units[index].has_value());
        if (direct_player.units[index].has_value()) {
            compare_card_with_direct(context, native_player["units"][index], *direct_player.units[index]);
        }
    }
    EXPECT(context, native_player["tactics"].size() == direct_player.tactics.size());
    for (std::size_t index = 0; index < direct_player.tactics.size(); ++index) {
        EXPECT(context,
               native_player["tactics"][index].is_null() ==
                   !direct_player.tactics[index].has_value());
        if (direct_player.tactics[index].has_value()) {
            compare_card_with_direct(
                context, native_player["tactics"][index], *direct_player.tactics[index]);
        }
    }
}

void compare_view_with_direct(
    TestContext& context,
    const Json& native_view,
    const scgs::MatchView& direct_view) {
    EXPECT(context, native_view.value("viewer", 99U) == static_cast<std::uint32_t>(direct_view.viewer));
    EXPECT(context,
           native_view.value("active_player", 99U) ==
               static_cast<std::uint32_t>(direct_view.active_player));
    EXPECT(context,
           native_view.value("first_player", 99U) ==
               static_cast<std::uint32_t>(direct_view.first_player));
    EXPECT(context, native_view.value("random_seed", 0U) == direct_view.random_seed);
    EXPECT(context, native_view.value("phase", 99U) == static_cast<std::uint32_t>(direct_view.phase));
    EXPECT(context, native_view.value("result", 99U) == static_cast<std::uint32_t>(direct_view.result));
    EXPECT(context, native_view.value("revision", 999U) == direct_view.revision);
    EXPECT(context, native_view["players"].size() == direct_view.players.size());
    for (std::size_t index = 0; index < direct_view.players.size(); ++index) {
        compare_player_with_direct(context, native_view["players"][index], direct_view.players[index]);
    }
    EXPECT(context, native_view["reaction"]["pending"] == direct_view.reaction.pending);
    EXPECT(context,
           native_view["reaction"]["window"] ==
               static_cast<std::uint32_t>(direct_view.reaction.window));
    EXPECT(context,
           native_view["reaction"]["responder"] ==
               static_cast<std::uint32_t>(direct_view.reaction.responder));
    EXPECT(context, native_view["reaction"]["subject"] == direct_view.reaction.subject);
    EXPECT(context, native_view["reaction"]["depth"] == direct_view.reaction.depth);
    EXPECT(context, native_view["reaction"]["eligible_count"] == direct_view.reaction.eligible_count);
    EXPECT(context, native_view["reaction"]["revision"] == direct_view.reaction.revision);
    EXPECT(context,
           native_view["reaction"]["eligible_traps"].size() ==
               direct_view.reaction.eligible_traps.size());
    for (std::size_t index = 0; index < direct_view.reaction.eligible_traps.size(); ++index) {
        compare_card_with_direct(
            context,
            native_view["reaction"]["eligible_traps"][index],
            direct_view.reaction.eligible_traps[index]);
    }
    EXPECT(context,
           native_view["reaction"].contains("origin") ==
               direct_view.reaction.origin.has_value());
    if (direct_view.reaction.origin.has_value()) {
        const Json& native_origin = native_view["reaction"]["origin"];
        const scgs::ReactionOrigin& direct_origin = *direct_view.reaction.origin;
        EXPECT(context,
               native_origin["action"] ==
                   static_cast<std::uint32_t>(direct_origin.action));
        EXPECT(context,
               native_origin["player"] ==
                   static_cast<std::uint32_t>(direct_origin.player));
        EXPECT(context, native_origin["source"] == direct_origin.source);
        EXPECT(context,
               native_origin.contains("target") == direct_origin.target.has_value());
        if (direct_origin.target.has_value()) {
            EXPECT(context,
                   native_origin["target"]["kind"] ==
                       static_cast<std::uint32_t>(direct_origin.target->kind));
            EXPECT(context,
                   native_origin["target"]["player"] ==
                       static_cast<std::uint32_t>(direct_origin.target->player));
            EXPECT(context,
                   native_origin["target"].contains("unit") ==
                       (direct_origin.target->kind == scgs::Target::Kind::Unit));
            if (direct_origin.target->kind == scgs::Target::Kind::Unit) {
                EXPECT(context, native_origin["target"]["unit"] == direct_origin.target->unit);
            }
        }
    }
}

void compare_command_with_direct(
    TestContext& context,
    const Json& native_command,
    const scgs::GameCommand& direct_command) {
    EXPECT(context, native_command["player"] == static_cast<std::uint32_t>(direct_command.player));
    EXPECT(context, native_command["action"] == static_cast<std::uint32_t>(direct_command.action));
    EXPECT(context, native_command["source"] == direct_command.source);
    EXPECT(context, native_command["expected_revision"] == direct_command.expected_revision);
    EXPECT(context, native_command["use_advance"] == direct_command.use_advance);
    EXPECT(context, native_command["mulligan_cards"].size() == direct_command.mulligan_cards.size());
    for (std::size_t index = 0; index < direct_command.mulligan_cards.size(); ++index) {
        EXPECT(context, native_command["mulligan_cards"][index] == direct_command.mulligan_cards[index]);
    }
    EXPECT(context, native_command.contains("target") == direct_command.target.has_value());
    if (direct_command.target.has_value()) {
        EXPECT(context,
               native_command["target"]["kind"] ==
                   static_cast<std::uint32_t>(direct_command.target->kind));
        EXPECT(context,
               native_command["target"]["player"] ==
                   static_cast<std::uint32_t>(direct_command.target->player));
        EXPECT(context,
               native_command["target"].contains("unit") ==
                   (direct_command.target->kind == scgs::Target::Kind::Unit));
        if (direct_command.target->kind == scgs::Target::Kind::Unit) {
            EXPECT(context, native_command["target"]["unit"] == direct_command.target->unit);
        }
    }
    EXPECT(context, native_command.contains("slot") == direct_command.slot.has_value());
    if (direct_command.slot.has_value()) {
        EXPECT(context, native_command["slot"] == *direct_command.slot);
    }
    EXPECT(context,
           native_command.contains("component_donor") == direct_command.component_donor.has_value());
    if (direct_command.component_donor.has_value()) {
        EXPECT(context, native_command["component_donor"] == *direct_command.component_donor);
    }
}

void compare_payment_with_direct(
    TestContext& context,
    const Json& native_payment,
    const scgs::PaymentPreview& direct_payment) {
    EXPECT(context,
           native_payment["status"]["engine_code"] ==
               static_cast<std::uint32_t>(direct_payment.status.code));
    EXPECT(context, native_payment["status"]["message"] == direct_payment.status.message);
    EXPECT(context, native_payment["current_pp_before"] == direct_payment.current_pp_before);
    EXPECT(context, native_payment["current_pp_after"] == direct_payment.current_pp_after);
    EXPECT(context, native_payment["pp_capacity_before"] == direct_payment.pp_capacity_before);
    EXPECT(context, native_payment["pp_capacity_after"] == direct_payment.pp_capacity_after);
    EXPECT(context, native_payment["cracks_before"] == direct_payment.cracks_before);
    EXPECT(context, native_payment["cracks_after"] == direct_payment.cracks_after);
    EXPECT(context,
           native_payment["evolution_energy_before"] == direct_payment.evolution_energy_before);
    EXPECT(context,
           native_payment["evolution_energy_after"] == direct_payment.evolution_energy_after);
    EXPECT(context, native_payment["base_cost"] == direct_payment.base_cost);
    EXPECT(context, native_payment["burn_cost"] == direct_payment.burn_cost);
    EXPECT(context, native_payment["advance_cost"] == direct_payment.advance_cost);
    EXPECT(context, native_payment["used_advance"] == direct_payment.used_advance);
}

std::uint64_t compare_event_batch_with_direct(
    TestContext& context,
    const scgs_v04_handle handle,
    const scgs::Game& direct,
    const std::uint32_t viewer,
    const std::uint64_t cursor) {
    const BufferResult native = read_events(context, handle, viewer, cursor);
    const std::vector<scgs::GameEventView> expected =
        direct.read_events(static_cast<scgs::PlayerId>(viewer), cursor);
    EXPECT(context, native.json["events"].size() == expected.size());
    const std::size_t count = std::min(native.json["events"].size(), expected.size());
    for (std::size_t index = 0; index < count; ++index) {
        const Json& event = native.json["events"][index];
        const scgs::GameEventView& direct_event = expected[index];
        EXPECT(context, event["sequence"] == direct_event.sequence);
        EXPECT(context, event["type"] == static_cast<std::uint32_t>(direct_event.type));
        EXPECT(context, event["player"] == static_cast<std::uint32_t>(direct_event.player));
        EXPECT(context, event.contains("card") == direct_event.card.has_value());
        if (direct_event.card.has_value()) {
            EXPECT(context, event["card"] == *direct_event.card);
        }
        EXPECT(context, event.contains("definition_id") == direct_event.definition_id.has_value());
        if (direct_event.definition_id.has_value()) {
            EXPECT(context, event["definition_id"] == *direct_event.definition_id);
        }
        EXPECT(context, event["value"] == direct_event.value);
        EXPECT(context, event["secondary_value"] == direct_event.secondary_value);
        EXPECT(context, event["hidden_card"] == direct_event.hidden_card);
        EXPECT(context, event["text"] == direct_event.text);
        EXPECT(context, event.contains("random_seed") == direct_event.random_seed.has_value());
        if (direct_event.random_seed.has_value()) {
            EXPECT(context, event["random_seed"] == *direct_event.random_seed);
        }
        EXPECT(context, event.contains("first_player") == direct_event.first_player.has_value());
        if (direct_event.first_player.has_value()) {
            EXPECT(context,
                   event["first_player"] ==
                       static_cast<std::uint32_t>(*direct_event.first_player));
        }
    }
    const std::uint64_t expected_last = expected.empty() ? cursor : expected.back().sequence;
    EXPECT(context, native.json["last_sequence"] == expected_last);
    return expected_last;
}

void test_direct_cpp_semantic_parity(TestContext& context) {
    scgs::GameConfig config;
    config.random_seed = kSeed;
    config.first_player_mode = scgs::FirstPlayerMode::Player0;
    config.shuffle_decks = false;
    scgs::Game direct(
        scgs::make_v04_catalog(), scgs::make_midrange_deck(), scgs::make_advance_deck(), config);
    EXPECT(context, direct.start());

    scgs_v04_handle handle = create_game(context);
    start_game(context, handle);
    compare_view_with_direct(context, get_view(context, handle, 0U).json["view"], direct.make_view(scgs::PlayerId::Player0));

    for (const std::uint32_t player : {0U, 1U}) {
        scgs::GameCommand direct_command;
        direct_command.player = static_cast<scgs::PlayerId>(player);
        direct_command.action = scgs::ActionKind::Mulligan;
        direct_command.expected_revision = direct.revision();
        EXPECT(context, direct.submit_command(direct_command));

        Json native_command{
            {"player", player},
            {"action", 0U},
            {"mulligan_cards", Json::array()},
            {"expected_revision", direct_command.expected_revision},
        };
        EXPECT(context, submit(context, handle, native_command) == 0U);
        compare_view_with_direct(
            context,
            get_view(context, handle, player).json["view"],
            direct.make_view(static_cast<scgs::PlayerId>(player)));
    }

    for (int turn = 0; turn < 6; ++turn) {
        const scgs::MatchView direct_view = direct.make_view(scgs::PlayerId::Player0);
        scgs::ActionQuery direct_query;
        direct_query.player = direct_view.active_player;
        direct_query.expected_revision = direct_view.revision;
        const std::vector<scgs::LegalAction> direct_actions = direct.list_legal_actions(direct_query);

        const Json native_query = query_for(
            static_cast<std::uint32_t>(direct_view.active_player), direct_view.revision);
        const BufferResult native_actions = list_actions(context, handle, native_query);
        EXPECT(context, native_actions.json["actions"].size() == direct_actions.size());
        const Json* native_end_turn = find_action(native_actions.json["actions"], 9U);
        EXPECT(context, native_end_turn != nullptr);
        const auto direct_end_turn = std::find_if(
            direct_actions.begin(), direct_actions.end(), [](const scgs::LegalAction& action) {
                return action.command.action == scgs::ActionKind::EndTurn;
            });
        EXPECT(context, direct_end_turn != direct_actions.end());
        if (native_end_turn == nullptr || direct_end_turn == direct_actions.end()) {
            break;
        }

        EXPECT(context, submit(context, handle, (*native_end_turn)["command"]) == 0U);
        EXPECT(context, direct.submit_command(direct_end_turn->command));
        compare_view_with_direct(
            context,
            get_view(context, handle, 0U).json["view"],
            direct.make_view(scgs::PlayerId::Player0));
    }

    destroy_game(context, handle);
}

void test_full_match_direct_semantic_parity(TestContext& context) {
    constexpr std::uint32_t parity_seed = 0xF00DBAADU;
    scgs::GameConfig config;
    config.random_seed = parity_seed;
    config.first_player_mode = scgs::FirstPlayerMode::Player0;
    config.shuffle_decks = true;
    scgs::Game direct(
        scgs::make_v04_catalog(), scgs::make_midrange_deck(), scgs::make_advance_deck(), config);
    EXPECT(context, direct.start());

    scgs_v04_handle handle = create_game(context, fixed_config(parity_seed, 1U, true));
    start_game(context, handle);
    std::array<std::uint64_t, 2> cursors{};
    bool completed = false;

    for (int step = 0; step < 1200; ++step) {
        std::array<BufferResult, 2> native_views{
            get_view(context, handle, 0U),
            get_view(context, handle, 1U),
        };
        std::array<scgs::MatchView, 2> direct_views{
            direct.make_view(scgs::PlayerId::Player0),
            direct.make_view(scgs::PlayerId::Player1),
        };
        for (std::uint32_t viewer = 0; viewer < 2U; ++viewer) {
            EXPECT(context,
                   native_views[viewer].json.value("revision", 999U) == direct.revision());
            compare_view_with_direct(
                context, native_views[viewer].json["view"], direct_views[viewer]);
            cursors[viewer] =
                compare_event_batch_with_direct(context, handle, direct, viewer, cursors[viewer]);
        }

        const scgs::MatchView& public_state = direct_views[0];
        if (public_state.result != scgs::GameResult::Ongoing) {
            completed = true;
            break;
        }

        scgs::PlayerId actor = public_state.active_player;
        if (public_state.phase == scgs::Phase::Mulligan) {
            actor = !public_state.players[0].mulligan_done
                        ? scgs::PlayerId::Player0
                        : scgs::PlayerId::Player1;
        } else if (public_state.phase == scgs::Phase::Reaction) {
            actor = public_state.reaction.responder;
        }

        scgs::ActionQuery direct_query;
        direct_query.player = actor;
        direct_query.expected_revision = direct.revision();
        const std::vector<scgs::LegalAction> direct_actions =
            direct.list_legal_actions(direct_query);
        const BufferResult native_actions = list_actions(
            context,
            handle,
            query_for(static_cast<std::uint32_t>(actor), direct.revision()));
        EXPECT(context, native_actions.json["actions"].size() == direct_actions.size());
        const std::size_t comparable =
            std::min(native_actions.json["actions"].size(), direct_actions.size());
        for (std::size_t index = 0; index < comparable; ++index) {
            compare_command_with_direct(
                context,
                native_actions.json["actions"][index]["command"],
                direct_actions[index].command);
            compare_payment_with_direct(
                context,
                native_actions.json["actions"][index]["payment"],
                direct_actions[index].payment);
        }

        const std::optional<Json> selected =
            choose_agent_command(native_actions.json["actions"]);
        EXPECT(context, selected.has_value());
        if (!selected.has_value()) {
            break;
        }
        std::size_t selected_index = direct_actions.size();
        for (std::size_t index = 0; index < comparable; ++index) {
            if (native_actions.json["actions"][index]["command"] == *selected) {
                selected_index = index;
                break;
            }
        }
        EXPECT(context, selected_index < direct_actions.size());
        if (selected_index >= direct_actions.size()) {
            break;
        }

        const BufferResult native_preview = preview_command(context, handle, *selected);
        const scgs::PaymentPreview direct_preview =
            direct.preview_payment(direct_actions[selected_index].command);
        compare_payment_with_direct(context, native_preview.json["payment"], direct_preview);

        const std::uint32_t native_code = submit(context, handle, *selected);
        const scgs::Status direct_status =
            direct.submit_command(direct_actions[selected_index].command);
        EXPECT(context, native_code == static_cast<std::uint32_t>(direct_status.code));
        EXPECT(context, direct_status);
        EXPECT(context, direct.revision() == public_state.revision + 1U);
    }

    EXPECT(context, completed);
    destroy_game(context, handle);
}

bool expect_hidden_tactics(TestContext& context, const Json& view, std::uint32_t viewer);

void test_cost_only_payment_preview(TestContext& context) {
    scgs_v04_handle handle = create_game(context, fixed_config(0xC0570A1U, 1U, false));
    start_game(context, handle);
    complete_empty_mulligans(context, handle);

    const BufferResult before = get_view(context, handle, 0U);
    const std::uint64_t revision = before.json.value("revision", 0U);
    const Json& player = before.json["view"]["players"][0];
    const BufferResult actions = list_actions(context, handle, query_for(0U, revision));
    const std::optional<Json> end_turn = find_command(actions.json["actions"], 9U);
    EXPECT(context, end_turn.has_value());
    if (end_turn.has_value()) {
        const BufferResult payment = preview_command(context, handle, *end_turn);
        const Json& projected = payment.json["payment"];
        EXPECT(context, projected["status"].value("engine_code", 99U) == 0U);
        EXPECT(context, projected["current_pp_before"] == player["current_pp"]);
        EXPECT(context, projected["current_pp_after"] == player["current_pp"]);
        EXPECT(context, projected["pp_capacity_before"] == player["pp_capacity"]);
        EXPECT(context, projected["pp_capacity_after"] == player["pp_capacity"]);
        EXPECT(context, projected["cracks_before"] == player["cracks"]);
        EXPECT(context, projected["cracks_after"] == player["cracks"]);
        EXPECT(context,
               projected["evolution_energy_before"] == player["evolution_energy"]);
        EXPECT(context,
               projected["evolution_energy_after"] == player["evolution_energy"]);
        EXPECT(context, projected.value("base_cost", -1) == 0);
        EXPECT(context, projected.value("burn_cost", -1) == 0);
        EXPECT(context, projected.value("advance_cost", -1) == 0);

        EXPECT(context, submit(context, handle, *end_turn) == 0U);
        const BufferResult after = get_view(context, handle, 0U);
        EXPECT(context, after.json["view"]["players"][0]["current_pp"] == 0);
    }

    destroy_game(context, handle);
}

void test_deterministic_advance_and_burn(TestContext& context) {
    Json advance_config = fixed_config(0xAD0A0CEU, 1U, false);
    advance_config["player0_deck"] = "advance";
    advance_config["player1_deck"] = "midrange";

    scgs_v04_handle advance_handle = create_game(context, advance_config);
    start_game(context, advance_handle);
    complete_empty_mulligans(context, advance_handle);
    advance_to_own_turn(context, advance_handle, 0U, 2);

    const BufferResult before_advance = get_view(context, advance_handle, 0U);
    const BufferResult advance_actions = list_actions(
        context,
        advance_handle,
        query_for(0U, before_advance.json.value("revision", 0U)));
    const std::optional<Json> advance = find_command(
        advance_actions.json["actions"],
        1U,
        &before_advance.json["view"],
        0U,
        "超前先锋");
    EXPECT(context, advance.has_value());
    if (advance.has_value()) {
        EXPECT(context, advance->value("use_advance", false));
        const BufferResult payment = preview_command(context, advance_handle, *advance);
        const Json& cost = payment.json["payment"];
        EXPECT(context, cost["status"].value("engine_code", 99U) == 0U);
        EXPECT(context, cost.value("base_cost", 0) == 4);
        EXPECT(context, cost.value("burn_cost", -1) == 0);
        EXPECT(context, cost.value("advance_cost", 0) == 2);
        EXPECT(context, cost.value("used_advance", false));
        EXPECT(context, cost.value("pp_capacity_before", 0) == 2);
        EXPECT(context, cost.value("pp_capacity_after", -1) == 0);
        EXPECT(context, cost.value("cracks_after", 0) == 2);

        const std::uint64_t before_revision = before_advance.json.value("revision", 0U);
        const std::uint64_t before_sequence =
            read_events(context, advance_handle, 0U, 0U).json.value("last_sequence", 0U);
        EXPECT(context, submit(context, advance_handle, *advance) == 0U);
        const BufferResult after = get_view(context, advance_handle, 0U);
        EXPECT(context, after.json.value("revision", 0U) == before_revision + 1U);
        EXPECT(context, after.json["view"]["players"][0]["pp_capacity"] == 0);
        EXPECT(context, after.json["view"]["players"][0]["cracks"] == 2);
        bool found_advanced_unit = false;
        for (const Json& unit : after.json["view"]["players"][0]["units"]) {
            if (!unit.is_null() && unit.value("name", std::string{}) == "超前先锋") {
                found_advanced_unit = unit.value("temporary_rush", false);
            }
        }
        EXPECT(context, found_advanced_unit);
        const Json events = read_events(
                                context, advance_handle, 0U, before_sequence)
                                .json["events"];
        EXPECT(context, std::any_of(events.begin(), events.end(), [](const Json& event) {
            return event.value("type", 99U) == 7U;
        }));
        EXPECT(context, std::any_of(events.begin(), events.end(), [](const Json& event) {
            return event.value("type", 99U) == 9U;
        }));
    }
    destroy_game(context, advance_handle);

    scgs_v04_handle burn_handle = create_game(context, advance_config);
    start_game(context, burn_handle);
    complete_empty_mulligans(context, burn_handle);
    advance_to_own_turn(context, burn_handle, 0U, 3);

    const BufferResult before_burn = get_view(context, burn_handle, 0U);
    const BufferResult burn_actions = list_actions(
        context, burn_handle, query_for(0U, before_burn.json.value("revision", 0U)));
    std::optional<Json> burn = find_command(
        burn_actions.json["actions"], 1U, &before_burn.json["view"], 0U, "燃耗战士");
    EXPECT(context, burn.has_value());
    if (burn.has_value()) {
        (*burn)["use_advance"] = false;
        const BufferResult payment = preview_command(context, burn_handle, *burn);
        const Json& cost = payment.json["payment"];
        EXPECT(context, cost["status"].value("engine_code", 99U) == 0U);
        EXPECT(context, cost.value("base_cost", 0) == 1);
        EXPECT(context, cost.value("burn_cost", 0) == 2);
        EXPECT(context, cost.value("advance_cost", -1) == 0);
        EXPECT(context, !cost.value("used_advance", true));
        EXPECT(context, cost.value("current_pp_after", -1) == 2);
        EXPECT(context, cost.value("pp_capacity_after", -1) == 1);
        EXPECT(context, cost.value("cracks_after", 0) == 2);

        const std::uint64_t revision = before_burn.json.value("revision", 0U);
        const std::uint64_t cursor =
            read_events(context, burn_handle, 0U, 0U).json.value("last_sequence", 0U);
        EXPECT(context, submit(context, burn_handle, *burn) == 0U);
        const BufferResult after = get_view(context, burn_handle, 0U);
        EXPECT(context, after.json.value("revision", 0U) == revision + 1U);
        EXPECT(context, after.json["view"]["players"][0]["current_pp"] == 2);
        EXPECT(context, after.json["view"]["players"][0]["pp_capacity"] == 1);
        EXPECT(context, after.json["view"]["players"][0]["cracks"] == 2);
        const Json events = read_events(context, burn_handle, 0U, cursor).json["events"];
        EXPECT(context, std::any_of(events.begin(), events.end(), [](const Json& event) {
            return event.value("type", 99U) == 7U;
        }));
    }
    destroy_game(context, burn_handle);
}

void test_deterministic_donor_query(TestContext& context) {
    scgs_v04_handle handle = create_game(context, fixed_config(0xD0404U, 1U, false));
    start_game(context, handle);
    complete_empty_mulligans(context, handle);

    BufferResult opening = get_view(context, handle, 0U);
    BufferResult opening_actions = list_actions(
        context, handle, query_for(0U, opening.json.value("revision", 0U)));
    std::optional<Json> pioneer = find_command(
        opening_actions.json["actions"], 1U, &opening.json["view"], 0U, "先驱侦察兵");
    EXPECT(context, pioneer.has_value());
    if (pioneer.has_value()) {
        (*pioneer)["use_advance"] = false;
        EXPECT(context, submit(context, handle, *pioneer) == 0U);
    }

    advance_to_own_turn(context, handle, 0U, 2);
    BufferResult turn_two = get_view(context, handle, 0U);
    BufferResult turn_two_actions = list_actions(
        context, handle, query_for(0U, turn_two.json.value("revision", 0U)));
    std::optional<Json> assault = find_command(
        turn_two_actions.json["actions"], 1U, &turn_two.json["view"], 0U, "突击前锋");
    EXPECT(context, assault.has_value());
    if (assault.has_value()) {
        (*assault)["use_advance"] = false;
        EXPECT(context, submit(context, handle, *assault) == 0U);
    }

    advance_to_own_turn(context, handle, 0U, 3);
    BufferResult turn_three = get_view(context, handle, 0U);
    BufferResult turn_three_actions = list_actions(
        context, handle, query_for(0U, turn_three.json.value("revision", 0U)));
    const std::optional<Json> support = find_command(
        turn_three_actions.json["actions"], 2U, &turn_three.json["view"], 0U, "后方支援");
    EXPECT(context, support.has_value());
    if (support.has_value()) {
        const BufferResult support_payment = preview_command(context, handle, *support);
        EXPECT(context,
               support_payment.json["payment"]["status"].value("engine_code", 99U) == 0U);
        EXPECT(context, submit(context, handle, *support) == 0U);
    }

    advance_to_own_turn(context, handle, 0U, 4);

    const BufferResult ready = get_view(context, handle, 0U);
    const Json& ready_view = ready.json["view"];
    EXPECT(context, ready_view.value("active_player", 1U) == 0U);
    EXPECT(context, ready_view["players"][0].value("own_turn_number", 0) >= 4);
    EXPECT(context, ready_view["players"][0].value("current_pp", 0) >= 4);

    std::uint64_t guard_ace = 0;
    for (const Json& standby : ready_view["players"][0]["standby"]) {
        if (standby.value("name", std::string{}) == "戍卫王机") {
            guard_ace = standby.value("instance_id", 0U);
        }
    }
    EXPECT(context, guard_ace != 0U);
    Json donor_query = query_for(0U, ready.json.value("revision", 0U));
    donor_query["action"] = 6U;
    donor_query["source"] = guard_ace;
    const BufferResult donors = call_with_json(
        context,
        donor_query,
        [=](const char* input,
            const std::uint64_t input_size,
            char* output,
            const std::uint64_t capacity,
            std::uint64_t* required) {
            return scgs_v04_list_valid_donors_json(
                handle, input, input_size, output, capacity, required);
        });
    EXPECT(context, !donors.json["donors"].empty());
    for (const Json& donor : donors.json["donors"]) {
        EXPECT(context, donor.is_number_unsigned());
        EXPECT(context, donor.get<std::uint64_t>() != 0U);
    }

    std::uint64_t assault_id = 0U;
    for (const Json& unit : ready_view["players"][0]["units"]) {
        if (!unit.is_null() && unit.value("name", std::string{}) == "突击前锋") {
            assault_id = unit.value("instance_id", 0U);
        }
    }
    EXPECT(context, assault_id != 0U);
    EXPECT(context, std::any_of(
        donors.json["donors"].begin(), donors.json["donors"].end(), [assault_id](const Json& id) {
            return id.get<std::uint64_t>() == assault_id;
        }));

    Json deploy_query = donor_query;
    deploy_query["component_donor"] = assault_id;
    const BufferResult deployments = list_actions(context, handle, deploy_query);
    const std::optional<Json> deploy = find_command(deployments.json["actions"], 6U);
    EXPECT(context, deploy.has_value());
    if (deploy.has_value()) {
        EXPECT(context, deploy->value("component_donor", 0U) == assault_id);
        const BufferResult payment = preview_command(context, handle, *deploy);
        EXPECT(context, payment.json["payment"]["status"].value("engine_code", 99U) == 0U);
        EXPECT(context, payment.json["payment"].value("base_cost", 0) == 4);
        EXPECT(context, payment.json["payment"].value("current_pp_after", -1) == 0);
        const std::uint64_t revision = ready.json.value("revision", 0U);
        const std::uint64_t cursor =
            read_events(context, handle, 0U, 0U).json.value("last_sequence", 0U);
        EXPECT(context, submit(context, handle, *deploy) == 0U);
        const BufferResult deployed = get_view(context, handle, 0U);
        EXPECT(context, deployed.json.value("revision", 0U) == revision + 1U);
        EXPECT(context, deployed.json["view"]["players"][0]["current_pp"] == 0);
        bool donor_archived = false;
        for (const Json& card : deployed.json["view"]["players"][0]["archive"]) {
            donor_archived = donor_archived || card.value("instance_id", 0U) == assault_id;
        }
        EXPECT(context, donor_archived);
        bool component_transferred = false;
        for (const Json& unit : deployed.json["view"]["players"][0]["units"]) {
            if (!unit.is_null() && unit.value("name", std::string{}) == "戍卫王机") {
                component_transferred = unit["granted_component"].value("has_component", false) &&
                                        unit["granted_component"].value("granted_kind", 99U) == 7U;
            }
        }
        EXPECT(context, component_transferred);
        const Json events = read_events(context, handle, 0U, cursor).json["events"];
        EXPECT(context, std::any_of(events.begin(), events.end(), [](const Json& event) {
            return event.value("type", 99U) == 18U;
        }));
    }

    advance_to_own_turn(context, handle, 0U, 5);
    const BufferResult evolve_ready = get_view(context, handle, 0U);
    EXPECT(context, evolve_ready.json["view"]["players"][0]["evolution_energy"] == 2);
    const BufferResult evolve_actions = list_actions(
        context, handle, query_for(0U, evolve_ready.json.value("revision", 0U)));
    const std::optional<Json> evolve = find_command(
        evolve_actions.json["actions"], 5U, &evolve_ready.json["view"], 0U, "戍卫王机");
    EXPECT(context, evolve.has_value());
    if (evolve.has_value()) {
        const std::uint64_t revision = evolve_ready.json.value("revision", 0U);
        const std::uint64_t cursor =
            read_events(context, handle, 0U, 0U).json.value("last_sequence", 0U);
        EXPECT(context, submit(context, handle, *evolve) == 0U);
        const BufferResult evolved = get_view(context, handle, 0U);
        EXPECT(context, evolved.json.value("revision", 0U) == revision + 1U);
        EXPECT(context, evolved.json["view"]["players"][0]["evolution_energy"] == 0);
        bool saw_evolved_guard_ace = false;
        for (const Json& unit : evolved.json["view"]["players"][0]["units"]) {
            saw_evolved_guard_ace = saw_evolved_guard_ace ||
                                    (!unit.is_null() &&
                                     unit.value("name", std::string{}) == "戍卫王机" &&
                                     unit.value("evolved", false));
        }
        EXPECT(context, saw_evolved_guard_ace);
        const Json events = read_events(context, handle, 0U, cursor).json["events"];
        EXPECT(context, std::any_of(events.begin(), events.end(), [](const Json& event) {
            return event.value("type", 99U) == 16U;
        }));
    }

    destroy_game(context, handle);
}

void test_deterministic_trap_privacy_and_reaction(TestContext& context) {
    Json config = fixed_config(0x7A4F00U, 1U, false);
    config["player1_deck"] = "midrange";
    scgs_v04_handle handle = create_game(context, config);
    start_game(context, handle);

    // Replace all four opening cards to bring the first fixed-deck trap sixteen
    // draws closer while retaining deterministic deck order.
    for (const std::uint32_t player : {0U, 1U}) {
        const BufferResult snapshot = get_view(context, handle, player);
        Json selected = Json::array();
        for (const Json& card : snapshot.json["view"]["players"][player]["hand"]) {
            selected.push_back(card["instance_id"]);
        }
        EXPECT(context,
               submit(
                   context,
                   handle,
                   Json{
                       {"player", player},
                       {"action", 0U},
                       {"mulligan_cards", std::move(selected)},
                       {"expected_revision", snapshot.json.value("revision", 0U)},
                   }) == 0U);
    }

    std::optional<std::uint32_t> setter;
    for (int step = 0; step < 300 && !setter.has_value(); ++step) {
        const BufferResult snapshot = get_view(context, handle, 0U);
        const Json& view = snapshot.json["view"];
        if (view.value("phase", 0U) == 4U) {
            break;
        }
        std::uint32_t actor = view.value("active_player", 0U);
        if (view.value("phase", 0U) == 3U) {
            actor = view["reaction"].value("responder", actor);
        }
        const BufferResult actions = list_actions(
            context, handle, query_for(actor, snapshot.json.value("revision", 0U)));

        if (view.value("phase", 0U) == 3U) {
            const std::optional<Json> pass = find_command(actions.json["actions"], 8U);
            EXPECT(context, pass.has_value());
            if (!pass.has_value()) {
                break;
            }
            EXPECT(context, submit(context, handle, *pass) == 0U);
            continue;
        }

        std::optional<Json> selected = find_command(
            actions.json["actions"], 3U, &view, actor, "拦截伏策");
        if (selected.has_value()) {
            EXPECT(context, submit(context, handle, *selected) == 0U);
            setter = actor;
            break;
        }

        // Empty hands without damaging leaders: units first, then non-targeted
        // healing spells and facilities, otherwise advance the turn.
        selected = find_command(actions.json["actions"], 1U);
        if (!selected.has_value()) {
            selected = find_command(
                actions.json["actions"], 2U, &view, actor, "后方支援");
        }
        if (!selected.has_value()) {
            selected = find_command(actions.json["actions"], 2U);
        }
        if (!selected.has_value()) {
            selected = find_command(
                actions.json["actions"], 3U, &view, actor, "战令设施");
        }
        if (!selected.has_value()) {
            selected = find_command(actions.json["actions"], 3U);
        }
        if (!selected.has_value()) {
            selected = find_command(actions.json["actions"], 9U);
        }
        EXPECT(context, selected.has_value());
        if (!selected.has_value()) {
            break;
        }
        EXPECT(context, submit(context, handle, *selected) == 0U);
    }

    EXPECT(context, setter.has_value());
    if (!setter.has_value()) {
        destroy_game(context, handle);
        return;
    }
    const std::uint32_t opponent_viewer = *setter == 0U ? 1U : 0U;
    const BufferResult hidden_snapshot = get_view(context, handle, opponent_viewer);
    EXPECT(context, expect_hidden_tactics(context, hidden_snapshot.json["view"], opponent_viewer));

    // Use the full history because set-trap is the only hidden CardMoved event
    // with this stable generic text; draw events have a different type.
    const BufferResult hidden_events = read_events(context, handle, opponent_viewer, 0U);
    bool saw_safe_set_event = false;
    for (const Json& event : hidden_events.json["events"]) {
        if (event.value("type", 99U) == 8U &&
            event.value("text", std::string{}) == "opponent set a trap") {
            saw_safe_set_event = event.value("hidden_card", false) &&
                                 !event.contains("card") && !event.contains("definition_id");
        }
    }
    EXPECT(context, saw_safe_set_event);
    EXPECT(context, hidden_events.text.find("拦截伏策") == std::string::npos);

    bool opened_reaction = false;
    std::optional<Json> declared_attack;
    for (int step = 0; step < 80 && !opened_reaction; ++step) {
        const BufferResult snapshot = get_view(context, handle, 0U);
        const Json& view = snapshot.json["view"];
        const std::uint32_t actor = view.value("active_player", 0U);
        const BufferResult actions = list_actions(
            context, handle, query_for(actor, snapshot.json.value("revision", 0U)));

        std::optional<Json> selected;
        if (actor == opponent_viewer) {
            selected = find_command(actions.json["actions"], 4U);
        }
        if (!selected.has_value() && actor == opponent_viewer) {
            selected = find_command(actions.json["actions"], 1U);
        }
        if (!selected.has_value()) {
            selected = find_command(actions.json["actions"], 9U);
        }
        EXPECT(context, selected.has_value());
        if (!selected.has_value()) {
            break;
        }
        const bool declaring_attack = selected->value("action", 99U) == 4U;
        EXPECT(context, submit(context, handle, *selected) == 0U);
        if (declaring_attack) {
            const BufferResult after_attack = get_view(context, handle, *setter);
            opened_reaction = after_attack.json["view"].value("phase", 0U) == 3U;
            if (opened_reaction) {
                declared_attack = *selected;
            }
        }
    }
    EXPECT(context, opened_reaction);
    if (opened_reaction) {
        const OutputCall responder_call = [=](
                                                  char* buffer,
                                                  const std::uint64_t capacity,
                                                  std::uint64_t* required) {
            return scgs_v04_get_reaction_context_json(
                handle, *setter, buffer, capacity, required);
        };
        const OutputCall opponent_call = [=](
                                                 char* buffer,
                                                 const std::uint64_t capacity,
                                                 std::uint64_t* required) {
            return scgs_v04_get_reaction_context_json(
                handle, opponent_viewer, buffer, capacity, required);
        };
        const Json responder = exercise_buffer_contract(context, responder_call).json["reaction"];
        const Json non_responder = exercise_buffer_contract(context, opponent_call).json["reaction"];
        EXPECT(context, responder.value("pending", false));
        EXPECT(context, responder.value("responder", 99U) == *setter);
        EXPECT(context, responder.value("eligible_count", 0U) >= 1U);
        EXPECT(context, responder["eligible_traps"].size() == responder["eligible_count"]);
        EXPECT(context, non_responder.value("eligible_count", 0U) == responder["eligible_count"]);
        EXPECT(context, non_responder["eligible_traps"].empty());
        EXPECT(context, non_responder.dump().find("拦截伏策") == std::string::npos);
        EXPECT(context, declared_attack.has_value());
        EXPECT(context, responder.contains("origin"));
        EXPECT(context, non_responder.contains("origin"));
        if (declared_attack.has_value() && responder.contains("origin") &&
            non_responder.contains("origin")) {
            const Json& origin = responder["origin"];
            EXPECT(context, origin == non_responder["origin"]);
            EXPECT(context, origin.value("action", 99U) == 4U);
            EXPECT(context, origin["player"] == (*declared_attack)["player"]);
            EXPECT(context, origin["source"] == (*declared_attack)["source"]);
            EXPECT(context, origin.contains("target"));
            if (origin.contains("target")) {
                EXPECT(context, origin["target"] == (*declared_attack)["target"]);
            }
        }

        const BufferResult reaction_view = get_view(context, handle, *setter);
        const std::uint64_t pass_revision = reaction_view.json.value("revision", 0U);
        const BufferResult reaction_actions = list_actions(
            context, handle, query_for(*setter, pass_revision));
        const std::optional<Json> pass = find_command(reaction_actions.json["actions"], 8U);
        EXPECT(context, pass.has_value());
        if (pass.has_value()) {
            EXPECT(context, submit(context, handle, *pass) == 0U);
            const BufferResult after_pass = get_view(context, handle, *setter);
            EXPECT(context, after_pass.json.value("revision", 0U) == pass_revision + 1U);
            EXPECT(context, after_pass.json["view"].value("phase", 0U) == 2U);
            bool trap_remains = false;
            for (const Json& slot : after_pass.json["view"]["players"][*setter]["tactics"]) {
                trap_remains = trap_remains ||
                               (!slot.is_null() &&
                                slot.value("name", std::string{}) == "拦截伏策");
            }
            EXPECT(context, trap_remains);
        }

        bool reopened = false;
        for (int step = 0; step < 100 && !reopened; ++step) {
            const BufferResult snapshot = get_view(context, handle, 0U);
            const Json& view = snapshot.json["view"];
            EXPECT(context, view.value("result", 0U) == 0U);
            if (view.value("result", 0U) != 0U) {
                break;
            }
            const std::uint32_t actor = view.value("active_player", 0U);
            const BufferResult actions = list_actions(
                context, handle, query_for(actor, snapshot.json.value("revision", 0U)));
            std::optional<Json> selected;
            if (actor == opponent_viewer) {
                selected = find_command(actions.json["actions"], 4U);
                if (!selected.has_value()) {
                    selected = find_command(actions.json["actions"], 1U);
                }
            }
            if (!selected.has_value()) {
                selected = find_command(actions.json["actions"], 9U);
            }
            EXPECT(context, selected.has_value());
            if (!selected.has_value()) {
                break;
            }
            const bool attack = selected->value("action", 99U) == 4U;
            EXPECT(context, submit(context, handle, *selected) == 0U);
            if (attack) {
                reopened = get_view(context, handle, *setter)
                               .json["view"].value("phase", 0U) == 3U;
            }
        }
        EXPECT(context, reopened);
        if (reopened) {
            const BufferResult before_activation = get_view(context, handle, *setter);
            const std::uint64_t revision = before_activation.json.value("revision", 0U);
            const std::uint64_t cursor =
                read_events(context, handle, *setter, 0U).json.value("last_sequence", 0U);
            const BufferResult actions = list_actions(
                context, handle, query_for(*setter, revision));
            const std::optional<Json> activate = find_command(actions.json["actions"], 7U);
            EXPECT(context, activate.has_value());
            if (activate.has_value()) {
                EXPECT(context,
                       source_name(
                           before_activation.json["view"],
                           *setter,
                           activate->value("source", 0U)) == "拦截伏策");
                EXPECT(context, submit(context, handle, *activate) == 0U);
                BufferResult after_activation = get_view(context, handle, *setter);
                EXPECT(context, after_activation.json.value("revision", 0U) == revision + 1U);
                if (after_activation.json["view"].value("phase", 0U) == 3U) {
                    const std::uint32_t counter_responder =
                        after_activation.json["view"]["reaction"].value("responder", 99U);
                    const BufferResult counter_actions = list_actions(
                        context,
                        handle,
                        query_for(
                            counter_responder,
                            after_activation.json.value("revision", 0U)));
                    const std::optional<Json> counter_pass =
                        find_command(counter_actions.json["actions"], 8U);
                    EXPECT(context, counter_pass.has_value());
                    if (counter_pass.has_value()) {
                        EXPECT(context, submit(context, handle, *counter_pass) == 0U);
                        after_activation = get_view(context, handle, *setter);
                    }
                }
                EXPECT(context, after_activation.json["view"].value("phase", 0U) == 2U);
                bool trap_still_set = false;
                for (const Json& slot : after_activation.json["view"]["players"][*setter]["tactics"]) {
                    trap_still_set = trap_still_set ||
                                     (!slot.is_null() &&
                                      slot.value("name", std::string{}) == "拦截伏策");
                }
                EXPECT(context, !trap_still_set);
                const Json events = read_events(context, handle, *setter, cursor).json["events"];
                EXPECT(context, std::any_of(events.begin(), events.end(), [](const Json& event) {
                    return event.value("type", 99U) == 20U;
                }));
                EXPECT(context, std::any_of(events.begin(), events.end(), [](const Json& event) {
                    return event.value("type", 99U) == 15U;
                }));
            }
        }
    }

    destroy_game(context, handle);
}

bool expect_hidden_tactics(TestContext& context, const Json& view, const std::uint32_t viewer) {
    const std::uint32_t opponent = viewer == 0U ? 1U : 0U;
    bool saw_hidden = false;
    for (const Json& slot : view["players"][opponent]["tactics"]) {
        if (slot.is_null() || !slot.value("face_down", false)) {
            continue;
        }
        saw_hidden = true;
        EXPECT(context, !slot.contains("instance_id"));
        EXPECT(context, !slot.contains("definition_id"));
        EXPECT(context, !slot.contains("definition"));
        EXPECT(context, slot.value("name", std::string{}).empty());
    }
    return saw_hidden;
}

void test_surrender_terminal_path(TestContext& context) {
    scgs_v04_handle handle = create_game(context, fixed_config(0x5A44EDEU, 1U, false));
    start_game(context, handle);
    const BufferResult before = get_view(context, handle, 0U);
    const std::uint64_t revision = before.json.value("revision", 0U);
    const std::uint64_t cursor =
        read_events(context, handle, 0U, 0U).json.value("last_sequence", 0U);
    const BufferResult actions = list_actions(context, handle, query_for(0U, revision));
    const std::optional<Json> surrender = find_command(actions.json["actions"], 10U);
    EXPECT(context, surrender.has_value());
    if (surrender.has_value()) {
        EXPECT(context, submit(context, handle, *surrender) == 0U);
        const BufferResult after = get_view(context, handle, 0U);
        EXPECT(context, after.json.value("revision", 0U) == revision + 1U);
        EXPECT(context, after.json["view"].value("phase", 0U) == 4U);
        EXPECT(context, after.json["view"].value("result", 0U) == 2U);
        const Json events = read_events(context, handle, 0U, cursor).json["events"];
        EXPECT(context, std::count_if(events.begin(), events.end(), [](const Json& event) {
            return event.value("type", 99U) == 22U;
        }) == 1);
        EXPECT(context, std::count_if(events.begin(), events.end(), [](const Json& event) {
            return event.value("type", 99U) == 23U;
        }) == 1);
    }
    destroy_game(context, handle);
}

void test_abi_only_fixed_deck_agent(TestContext& context) {
    scgs_v04_handle handle = create_game(context, fixed_config(0xC0DEC0DEU, 1U, true));
    start_game(context, handle);

    std::array<std::uint64_t, 2> cursors{};
    bool completed = false;
    bool saw_targets_query = false;
    bool saw_slots_query = false;
    bool saw_donors_query = false;
    bool saw_payment_query = false;
    bool saw_reaction_query = false;
    bool saw_pending_reaction = false;
    bool saw_hidden_tactic = false;
    bool saw_hidden_trap_event = false;
    std::uint64_t last_revision = 0;

    for (int step = 0; step < 1200; ++step) {
        const BufferResult player0 = get_view(context, handle, 0U);
        const BufferResult player1 = get_view(context, handle, 1U);
        const Json& view0 = player0.json["view"];
        const Json& view1 = player1.json["view"];
        const std::uint64_t revision = player0.json.value("revision", 0U);
        last_revision = revision;
        EXPECT(context, player1.json.value("revision", 1U) == revision);
        EXPECT(context, view0["players"][1]["hand"].empty());
        EXPECT(context, view1["players"][0]["hand"].empty());
        saw_hidden_tactic = expect_hidden_tactics(context, view0, 0U) || saw_hidden_tactic;
        saw_hidden_tactic = expect_hidden_tactics(context, view1, 1U) || saw_hidden_tactic;

        if (view0.value("result", 0U) != 0U) {
            completed = true;
            break;
        }

        std::uint32_t actor = view0.value("active_player", 0U);
        if (view0.value("phase", 0U) == 1U) {
            actor = !view0["players"][0].value("mulligan_done", false) ? 0U : 1U;
        } else if (view0.value("phase", 0U) == 3U) {
            actor = view0["reaction"].value("responder", 0U);
        }

        const Json broad_query = query_for(actor, revision);
        const BufferResult actions = list_actions(context, handle, broad_query);
        const std::optional<Json> selected = choose_agent_command(actions.json["actions"]);
        EXPECT(context, selected.has_value());
        if (!selected.has_value()) {
            break;
        }

        Json focused_query = broad_query;
        focused_query["action"] = (*selected)["action"];
        if (selected->contains("source")) {
            focused_query["source"] = (*selected)["source"];
        }

        const auto input_function = [handle](const auto function) {
            return [handle, function](
                       const char* input,
                       const std::uint64_t input_size,
                       char* output,
                       const std::uint64_t capacity,
                       std::uint64_t* required) {
                return function(handle, input, input_size, output, capacity, required);
            };
        };
        const BufferResult targets = call_with_json(
            context, focused_query, input_function(scgs_v04_list_valid_targets_json));
        const BufferResult slots = call_with_json(
            context, focused_query, input_function(scgs_v04_list_valid_slots_json));
        const BufferResult donors = call_with_json(
            context, focused_query, input_function(scgs_v04_list_valid_donors_json));
        saw_targets_query = saw_targets_query || !targets.json["targets"].empty();
        saw_slots_query = saw_slots_query || !slots.json["slots"].empty();
        saw_donors_query = saw_donors_query || !donors.json["donors"].empty();

        Json command = *selected;
        command["schema_version"] = SCGS_V04_SCHEMA_VERSION;
        const BufferResult payment = call_with_json(
            context, command, input_function(scgs_v04_preview_payment_json));
        saw_payment_query = saw_payment_query || payment.json["payment"].is_object();
        EXPECT(context, payment.json["payment"]["status"].value("engine_code", 99U) == 0U);
        EXPECT(context,
               payment.json["payment"]["current_pp_before"] ==
                   view0["players"][actor]["current_pp"]);
        EXPECT(context,
               payment.json["payment"]["pp_capacity_before"] ==
                   view0["players"][actor]["pp_capacity"]);
        EXPECT(context,
               payment.json["payment"]["cracks_before"] ==
                   view0["players"][actor]["cracks"]);

        const OutputCall reaction_call = [=](
                                               char* buffer,
                                               const std::uint64_t capacity,
                                               std::uint64_t* required) {
            return scgs_v04_get_reaction_context_json(
                handle, actor, buffer, capacity, required);
        };
        std::uint64_t reaction_bytes = 0;
        EXPECT(context,
               reaction_call(nullptr, 0U, &reaction_bytes) == SCGS_V04_BUFFER_TOO_SMALL);
        if (reaction_bytes >= 2U) {
            std::vector<char> reaction_buffer(static_cast<std::size_t>(reaction_bytes));
            EXPECT(context,
                   reaction_call(reaction_buffer.data(), reaction_bytes, &reaction_bytes) == SCGS_V04_OK);
            saw_reaction_query = true;
            const Json reaction = Json::parse(
                reaction_buffer.data(), reaction_buffer.data() + reaction_buffer.size() - 1U);
            saw_pending_reaction =
                saw_pending_reaction || reaction["reaction"].value("pending", false);
        }

        EXPECT(context, submit(context, handle, *selected) == 0U);
        const BufferResult after_command = get_view(context, handle, actor);
        EXPECT(context, after_command.json.value("revision", 0U) == revision + 1U);
        const std::uint32_t action = command.value("action", 99U);
        if (action == 1U || action == 2U || action == 3U || action == 6U) {
            // Printed/deployment PP is committed before effects. Other resource
            // fields may subsequently change through public effect resolution.
            EXPECT(context,
                   after_command.json["view"]["players"][actor]["current_pp"] ==
                       payment.json["payment"]["current_pp_after"]);
        }
        if (action == 5U) {
            EXPECT(context,
                   after_command.json["view"]["players"][actor]["evolution_energy"] ==
                       payment.json["payment"]["evolution_energy_after"]);
        }
        for (std::uint32_t viewer = 0; viewer < 2U; ++viewer) {
            const BufferResult events = read_events(context, handle, viewer, cursors[viewer]);
            for (const Json& event : events.json["events"]) {
                if (event.value("type", 99U) == 8U && event.value("hidden_card", false) &&
                    event.value("text", std::string{}) == "opponent set a trap") {
                    saw_hidden_trap_event = !event.contains("card") &&
                                            !event.contains("definition_id");
                }
            }
            cursors[viewer] = events.json.value("last_sequence", cursors[viewer]);
        }
    }

    EXPECT(context, completed);
    EXPECT(context, last_revision > 0U);
    EXPECT(context, saw_targets_query);
    EXPECT(context, saw_slots_query);
    EXPECT(context, saw_payment_query);
    EXPECT(context, saw_reaction_query);
    // Targeted deterministic tests cover non-empty donor, reaction and hidden
    // trap paths; this proxy is deliberately free to finish via any legal line.
    (void)saw_donors_query;
    (void)saw_pending_reaction;
    (void)saw_hidden_tactic;
    (void)saw_hidden_trap_event;

    const BufferResult final_events = read_events(context, handle, 0U, 0U);
    const auto match_end_count = std::count_if(
        final_events.json["events"].begin(),
        final_events.json["events"].end(),
        [](const Json& event) { return event.value("type", 99U) == 23U; });
    EXPECT(context, match_end_count == 1);

    destroy_game(context, handle);
}

void test_every_action_kind_was_submitted(TestContext& context) {
    static constexpr std::array<const char*, 11> names{
        "Mulligan",
        "PlayUnit",
        "CastSpell",
        "PlayTactic",
        "Attack",
        "Evolve",
        "Deploy",
        "ActivateTrap",
        "PassReaction",
        "EndTurn",
        "Surrender",
    };
    for (std::size_t index = 0; index < g_seen_action_kinds.size(); ++index) {
        if (!g_seen_action_kinds[index]) {
            std::cerr << "ABI action was never successfully submitted: " << names[index] << '\n';
        }
        EXPECT(context, g_seen_action_kinds[index]);
    }
}

} // namespace

int main() {
    TestContext context;
    test_version_status_and_lifecycle(context);
    test_input_validation_and_safe_errors(context);
    test_all_output_buffer_contracts(context);
    test_privacy_event_cursors_and_atomicity(context);
    test_direct_cpp_semantic_parity(context);
    test_full_match_direct_semantic_parity(context);
    test_cost_only_payment_preview(context);
    test_deterministic_advance_and_burn(context);
    test_deterministic_donor_query(context);
    test_deterministic_trap_privacy_and_reaction(context);
    test_surrender_terminal_path(context);
    test_abi_only_fixed_deck_agent(context);
    test_every_action_kind_was_submitted(context);

    if (context.failures != 0) {
        std::cerr << context.failures << " of " << context.assertions
                  << " native ABI assertions failed\n";
        return 1;
    }
    std::cout << "native ABI contract passed: " << context.assertions << " assertions\n";
    return 0;
}
