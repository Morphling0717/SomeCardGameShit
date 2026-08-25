// SPDX-License-Identifier: GPL-3.0-or-later
#include "scgs/native_api_v05.h"

#include <nlohmann/json.hpp>

#include <array>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <string>
#include <string_view>
#include <vector>

namespace {

using Json = nlohmann::ordered_json;

int fail(const std::string& message) {
    std::cerr << "v05 schema contract failed: " << message << '\n';
    return 1;
}

bool action_allows(const std::uint32_t action, const std::string_view field) {
    if (action == 0U) {
        return field == "mulligan_cards";
    }
    if (action == 1U || action == 2U || action == 11U) {
        return field == "source" || field == "target" || field == "slot" ||
            field == "mode_id" || field == "use_advance";
    }
    if (action == 3U) {
        return field == "source" || field == "slot" || field == "mode_id" ||
            field == "use_advance";
    }
    if (action == 4U) {
        return field == "source" || field == "target";
    }
    if (action == 5U || action == 7U) {
        return field == "source" || field == "target" || field == "mode_id";
    }
    if (action == 6U) {
        return field == "source" || field == "target" || field == "slot" ||
            field == "mode_id" || field == "additional_cost_cards" ||
            field == "use_advance";
    }
    if (action == 12U) {
        return field == "source" || field == "target" || field == "mode_id" ||
            field == "use_advance";
    }
    if (action == 13U) {
        return field == "choice_id" || field == "selected_option_ids";
    }
    return false;
}

template <typename Call>
std::string read_json(Call&& call) {
    std::uint64_t required = 0U;
    if (call(nullptr, 0U, &required) != SCGS_V05_BUFFER_TOO_SMALL || required < 2U) {
        return {};
    }
    std::vector<char> buffer(static_cast<std::size_t>(required));
    if (call(buffer.data(), buffer.size(), &required) != SCGS_V05_OK ||
        required != buffer.size() || buffer.back() != '\0') {
        return {};
    }
    return std::string(buffer.data(), buffer.size() - 1U);
}

} // namespace

int main() {
    static constexpr std::string_view config =
        R"({"schema_version":2,"player0_deck":"oathguard_luminous_oath_v1","player1_deck":"pactmage_abyssal_pact_v1","random_seed":99,"first_player_mode":1,"shuffle_decks":false})";
    scgs_v05_handle handle = 0U;
    if (scgs_v05_create(
            SCGS_V05_ABI_VERSION,
            config.data(),
            config.size(),
            &handle) != SCGS_V05_OK ||
        handle == 0U) {
        return fail("create failed");
    }

    std::uint32_t engine_code = SCGS_V05_NO_ENGINE_CODE;
    if (scgs_v05_start(handle, &engine_code) != SCGS_V05_OK || engine_code != 0U) {
        return fail("start failed");
    }

    const std::string player0_text = read_json([&](char* output, std::uint64_t capacity, std::uint64_t* required) {
        return scgs_v05_get_view_json(handle, 0U, output, capacity, required);
    });
    const std::string player1_text = read_json([&](char* output, std::uint64_t capacity, std::uint64_t* required) {
        return scgs_v05_get_view_json(handle, 1U, output, capacity, required);
    });
    if (player0_text.empty() || player1_text.empty()) {
        return fail("view retrieval failed");
    }
    const Json player0 = Json::parse(player0_text);
    const Json player1 = Json::parse(player1_text);
    if (player0.at("schema_version") != 2U ||
        player0_text.find("\"random_seed\"") != std::string::npos ||
        player1_text.find("\"random_seed\"") != std::string::npos) {
        return fail("schema or seed-redaction contract failed");
    }
    const Json& view0 = player0.at("view");
    if (view0.at("players").at(0).at("hand").size() != 4U ||
        !view0.at("players").at(1).at("hand").empty() ||
        view0.at("players").at(0).at("main_board").size() != 5U ||
        view0.at("players").at(0).at("tactics").size() != 3U ||
        view0.at("players").at(0).at("main_board").at(0).at("design_id") != "LO-03" ||
        view0.at("players").at(0).at("main_board").at(0).at("kind") != 2U ||
        view0.at("players").at(0).at("main_board").at(1).at("design_id") != "LO-04" ||
        view0.at("players").at(0).at("field").at("design_id") != "LO-10" ||
        view0.at("players").at(0).at("field").at("kind") != 4U ||
        view0.at("players").at(0).at("tactics").at(0).at("design_id") != "LO-07" ||
        player1.at("view").at("players").at(1).at("field").at("design_id") != "AP-05" ||
        view0.at("players").at(0).at("hand").at(0).at("design_id") != "LO-01" ||
        player1.at("view").at("players").at(1).at("hand").at(0).at("design_id") != "AP-01") {
        return fail("viewer-safe card/board shape failed");
    }
    const Json& hidden_trap = player1.at("view").at("players").at(0).at("tactics").at(0);
    if (hidden_trap.contains("instance_id") || hidden_trap.contains("design_id") ||
        hidden_trap.contains("profession_id") || hidden_trap.contains("series_id") ||
        hidden_trap.contains("neutral") || hidden_trap.contains("kind") ||
        hidden_trap.at("sequence") != 0U || hidden_trap.at("cost") != 0 ||
        hidden_trap.at("keywords") != 0U || !hidden_trap.at("face_down").get<bool>()) {
        return fail("opponent face-down tactic leaked identity-derived data");
    }
    const Json& owner_choice = view0.at("pending_choice");
    const Json& opponent_choice = player1.at("view").at("pending_choice");
    if (!owner_choice.at("pending").get<bool>() || owner_choice.at("chooser") != 0U ||
        owner_choice.at("kind") != 1U || owner_choice.at("options").size() != 2U ||
        !owner_choice.at("options").at(0).contains("card") ||
        !opponent_choice.at("pending").get<bool>() || opponent_choice.at("chooser") != 0U ||
        opponent_choice.contains("choice_id") || opponent_choice.contains("kind") ||
        opponent_choice.contains("minimum_selections") || opponent_choice.contains("options")) {
        return fail("pending-choice owner projection or opponent redaction failed");
    }

    const std::uint64_t revision = view0.at("revision").get<std::uint64_t>();
    const std::string query = Json{
        {"schema_version", 2U},
        {"player", 0U},
        {"expected_revision", revision}}
        .dump();
    const std::string actions_text = read_json([&](char* output, std::uint64_t capacity, std::uint64_t* required) {
        return scgs_v05_list_legal_actions_json(
            handle, query.data(), query.size(), output, capacity, required);
    });
    const Json actions = Json::parse(actions_text);
    if (actions.at("actions").size() != 3U ||
        actions.at("actions").at(0).at("command").at("action") != 13U ||
        actions.at("actions").at(1).at("command").at("action") != 13U ||
        actions.at("actions").at(2).at("command").at("action") != 10U ||
        !actions.at("actions").at(0).at("command").contains("choice_id") ||
        !actions.at("actions").at(0).at("command").contains("selected_option_ids") ||
        actions.at("actions").at(0).at("command").contains("source") ||
        actions.at("actions").at(0).at("command").contains("use_advance") ||
        actions.at("actions").at(0).at("command").contains("mulligan_cards") ||
        actions.at("actions").at(0).at("command").contains("additional_cost_cards")) {
        return fail("canonical action-specific command shape failed");
    }

    scgs_v05_handle token_isolation_handle = 0U;
    engine_code = SCGS_V05_NO_ENGINE_CODE;
    if (scgs_v05_create(
            SCGS_V05_ABI_VERSION,
            config.data(),
            config.size(),
            &token_isolation_handle) != SCGS_V05_OK ||
        scgs_v05_start(token_isolation_handle, &engine_code) != SCGS_V05_OK ||
        engine_code != 0U) {
        return fail("could not create a second session for choice-token isolation");
    }
    const Json isolated_view = Json::parse(read_json(
        [&](char* output, std::uint64_t capacity, std::uint64_t* required) {
            return scgs_v05_get_view_json(
                token_isolation_handle, 0U, output, capacity, required);
        }));
    const Json& isolated_choice = isolated_view.at("view").at("pending_choice");
    if (isolated_choice.at("choice_id") == owner_choice.at("choice_id") ||
        isolated_choice.at("options").at(0).at("option_id") ==
            owner_choice.at("options").at(0).at("option_id") ||
        isolated_choice.at("options").at(1).at("option_id") ==
            owner_choice.at("options").at(1).at("option_id")) {
        return fail("choice or option tokens were reused across sessions");
    }
    const std::string isolated_events_before = read_json(
        [&](char* output, std::uint64_t capacity, std::uint64_t* required) {
            return scgs_v05_read_events_json(
                token_isolation_handle, 0U, 0U, output, capacity, required);
        });
    Json replayed_command = actions.at("actions").at(0).at("command");
    replayed_command["schema_version"] = 2U;
    const std::string replayed_command_text = replayed_command.dump();
    engine_code = SCGS_V05_NO_ENGINE_CODE;
    if (scgs_v05_submit_command_json(
            token_isolation_handle,
            replayed_command_text.data(),
            replayed_command_text.size(),
            &engine_code) != SCGS_V05_OK ||
        engine_code != 38U) {
        return fail("a choice token from another session was accepted");
    }
    const std::string isolated_view_after = read_json(
        [&](char* output, std::uint64_t capacity, std::uint64_t* required) {
            return scgs_v05_get_view_json(
                token_isolation_handle, 0U, output, capacity, required);
        });
    const std::string isolated_events_after = read_json(
        [&](char* output, std::uint64_t capacity, std::uint64_t* required) {
            return scgs_v05_read_events_json(
                token_isolation_handle, 0U, 0U, output, capacity, required);
        });
    if (isolated_view_after != isolated_view.dump() ||
        isolated_events_after != isolated_events_before ||
        scgs_v05_destroy(token_isolation_handle) != SCGS_V05_OK) {
        return fail("replayed choice token changed the isolated session");
    }

    for (std::size_t index = 0U; index < actions.at("actions").size(); ++index) {
        scgs_v05_handle copy_handle = 0U;
        engine_code = SCGS_V05_NO_ENGINE_CODE;
        if (scgs_v05_create(
                SCGS_V05_ABI_VERSION,
                config.data(),
                config.size(),
                &copy_handle) != SCGS_V05_OK ||
            scgs_v05_start(copy_handle, &engine_code) != SCGS_V05_OK || engine_code != 0U) {
            return fail("could not create a same-revision choice submission copy");
        }
        const std::string copy_actions_text = read_json(
            [&](char* output, std::uint64_t capacity, std::uint64_t* required) {
                return scgs_v05_list_legal_actions_json(
                    copy_handle, query.data(), query.size(), output, capacity, required);
            });
        Json copy_command = Json::parse(copy_actions_text).at("actions").at(index).at("command");
        copy_command["schema_version"] = 2U;
        const std::string copy_command_text = copy_command.dump();
        engine_code = SCGS_V05_NO_ENGINE_CODE;
        if (scgs_v05_submit_command_json(
                copy_handle,
                copy_command_text.data(),
                copy_command_text.size(),
                &engine_code) != SCGS_V05_OK ||
            engine_code != 0U || scgs_v05_destroy(copy_handle) != SCGS_V05_OK) {
            return fail("an enumerated choice action was not submit-able on a same-revision copy");
        }
    }

    const std::string opponent_query = Json{
        {"schema_version", 2U},
        {"player", 1U},
        {"expected_revision", revision}}
        .dump();
    const Json opponent_actions = Json::parse(read_json(
        [&](char* output, std::uint64_t capacity, std::uint64_t* required) {
            return scgs_v05_list_legal_actions_json(
                handle,
                opponent_query.data(),
                opponent_query.size(),
                output,
                capacity,
                required);
        }));
    if (opponent_actions.at("actions").size() != 1U ||
        opponent_actions.at("actions").at(0).at("command").at("action") != 10U) {
        return fail("the non-choosing player could not enumerate surrender during a choice");
    }
    scgs_v05_handle opponent_copy = 0U;
    engine_code = SCGS_V05_NO_ENGINE_CODE;
    if (scgs_v05_create(
            SCGS_V05_ABI_VERSION,
            config.data(),
            config.size(),
            &opponent_copy) != SCGS_V05_OK ||
        scgs_v05_start(opponent_copy, &engine_code) != SCGS_V05_OK || engine_code != 0U) {
        return fail("could not create the opponent surrender submission copy");
    }
    Json opponent_surrender = opponent_actions.at("actions").at(0).at("command");
    opponent_surrender["schema_version"] = 2U;
    const std::string opponent_surrender_text = opponent_surrender.dump();
    if (scgs_v05_submit_command_json(
            opponent_copy,
            opponent_surrender_text.data(),
            opponent_surrender_text.size(),
            &engine_code) != SCGS_V05_OK ||
        engine_code != 0U || scgs_v05_destroy(opponent_copy) != SCGS_V05_OK) {
        return fail("the enumerated opponent surrender was not submit-able");
    }

    const std::string product_slot_query = Json{
        {"schema_version", 2U},
        {"player", 0U},
        {"action", 1U},
        {"source", 100U},
        {"expected_revision", revision}}
        .dump();
    const std::string slots_text = read_json(
        [&](char* output, std::uint64_t capacity, std::uint64_t* required) {
            return scgs_v05_list_valid_slots_json(
                handle,
                product_slot_query.data(),
                product_slot_query.size(),
                output,
                capacity,
                required);
        });
    if (slots_text.empty() || !Json::parse(slots_text).at("slots").empty()) {
        return fail("foundation enumerated a product slot that submit cannot accept");
    }

    const std::string events_text = read_json(
        [&](char* output, std::uint64_t capacity, std::uint64_t* event_required) {
            return scgs_v05_read_events_json(
                handle, 0U, 0U, output, capacity, event_required);
        });
    if (events_text.empty() ||
        events_text.find("\"random_seed\"") != std::string::npos) {
        return fail("event stream leaked the product random seed");
    }
    const std::uint64_t initial_last_sequence =
        Json::parse(events_text).at("last_sequence").get<std::uint64_t>();

    const std::array query_functions{
        &scgs_v05_list_legal_actions_json,
        &scgs_v05_list_valid_targets_json,
        &scgs_v05_list_valid_slots_json,
        &scgs_v05_list_valid_donors_json,
    };
    const std::string invalid_shape_query = Json{
        {"schema_version", 2U},
        {"player", 0U},
        {"action", 9U},
        {"slot", 0U},
        {"expected_revision", revision}}
        .dump();
    const std::string stale_query = Json{
        {"schema_version", 2U},
        {"player", 0U},
        {"expected_revision", revision + 1U}}
        .dump();
    for (const auto function : query_functions) {
        std::uint64_t query_required = 99U;
        if (function(
                handle,
                invalid_shape_query.data(),
                invalid_shape_query.size(),
                nullptr,
                0U,
                &query_required) != SCGS_V05_SCHEMA_MISMATCH ||
            query_required != 0U) {
            return fail("an action-invalid query was accepted as an empty result");
        }
        query_required = 99U;
        if (function(
                handle,
                stale_query.data(),
                stale_query.size(),
                nullptr,
                0U,
                &query_required) != SCGS_V05_INVALID_ARGUMENT ||
            query_required != 0U) {
            return fail("a stale query was accepted as an empty result");
        }
    }
    const std::string view_after_rejected_queries = read_json(
        [&](char* output, std::uint64_t capacity, std::uint64_t* required) {
            return scgs_v05_get_view_json(handle, 0U, output, capacity, required);
        });
    const std::string events_after_rejected_queries = read_json(
        [&](char* output, std::uint64_t capacity, std::uint64_t* required) {
            return scgs_v05_read_events_json(
                handle, 0U, initial_last_sequence, output, capacity, required);
        });
    if (view_after_rejected_queries != player0_text ||
        !Json::parse(events_after_rejected_queries).at("events").empty()) {
        return fail("a rejected query changed state or emitted an event");
    }

    scgs_v05_handle query_lifecycle_handle = 0U;
    if (scgs_v05_create(
            SCGS_V05_ABI_VERSION,
            config.data(),
            config.size(),
            &query_lifecycle_handle) != SCGS_V05_OK) {
        return fail("could not create the query-lifecycle session");
    }
    const std::string not_started_query = Json{
        {"schema_version", 2U}, {"player", 0U}, {"expected_revision", 0U}}
        .dump();
    std::uint64_t query_required = 99U;
    if (scgs_v05_list_legal_actions_json(
            query_lifecycle_handle,
            not_started_query.data(),
            not_started_query.size(),
            nullptr,
            0U,
            &query_required) != SCGS_V05_INVALID_ARGUMENT ||
        query_required != 0U) {
        return fail("a query against a non-started session was accepted");
    }
    engine_code = SCGS_V05_NO_ENGINE_CODE;
    if (scgs_v05_start(query_lifecycle_handle, &engine_code) != SCGS_V05_OK ||
        engine_code != 0U) {
        return fail("could not start the query-lifecycle session");
    }
    const std::string lifecycle_surrender = Json{
        {"schema_version", 2U},
        {"player", 0U},
        {"action", 10U},
        {"expected_revision", 1U}}
        .dump();
    if (scgs_v05_submit_command_json(
            query_lifecycle_handle,
            lifecycle_surrender.data(),
            lifecycle_surrender.size(),
            &engine_code) != SCGS_V05_OK ||
        engine_code != 0U) {
        return fail("could not finish the query-lifecycle session");
    }
    const std::string finished_query = Json{
        {"schema_version", 2U}, {"player", 0U}, {"expected_revision", 2U}}
        .dump();
    query_required = 99U;
    if (scgs_v05_list_legal_actions_json(
            query_lifecycle_handle,
            finished_query.data(),
            finished_query.size(),
            nullptr,
            0U,
            &query_required) != SCGS_V05_INVALID_ARGUMENT ||
        query_required != 0U ||
        scgs_v05_destroy(query_lifecycle_handle) != SCGS_V05_OK) {
        return fail("a query against a finished session was accepted");
    }

    struct FieldCase final {
        std::string name;
        Json value;
        std::uint32_t expected_code;
    };
    const std::vector<FieldCase> fields{
        {"source", 100U, 4U},
        {"target", Json{{"kind", 0U}, {"player", 1U}}, 6U},
        {"slot", 0U, 7U},
        {"mode_id", "mode", 40U},
        {"choice_id", "choice", 38U},
        {"mulligan_cards", Json::array(), 4U},
        {"selected_option_ids", Json::array(), 38U},
        {"additional_cost_cards", Json::array(), 41U},
        {"use_advance", false, 4U},
    };
    for (std::uint32_t action = 0U; action <= 13U; ++action) {
        for (const FieldCase& field : fields) {
            if (action_allows(action, field.name)) {
                continue;
            }
            Json command{
                {"schema_version", 2U},
                {"player", 0U},
                {"action", action},
                {"expected_revision", revision},
                {field.name, field.value}};
            if (action == 0U && field.name != "mulligan_cards") {
                command["mulligan_cards"] = Json::array();
            }
            const std::string command_text = command.dump();
            engine_code = SCGS_V05_NO_ENGINE_CODE;
            if (scgs_v05_submit_command_json(
                    handle,
                    command_text.data(),
                    command_text.size(),
                    &engine_code) != SCGS_V05_OK ||
                engine_code != field.expected_code) {
                return fail(
                    "action/field rejection matrix failed for action " +
                    std::to_string(action) + " field " + field.name);
            }
        }
    }

    const std::string after_matrix_view = read_json(
        [&](char* output, std::uint64_t capacity, std::uint64_t* required) {
            return scgs_v05_get_view_json(handle, 0U, output, capacity, required);
        });
    const std::string after_matrix_events = read_json(
        [&](char* output, std::uint64_t capacity, std::uint64_t* required) {
            return scgs_v05_read_events_json(
                handle, 0U, initial_last_sequence, output, capacity, required);
        });
    if (after_matrix_view.empty() || after_matrix_events.empty() ||
        Json::parse(after_matrix_view).at("revision") != revision ||
        !Json::parse(after_matrix_events).at("events").empty()) {
        return fail("rejected action fields changed revision or emitted events");
    }

    Json resolve_command = actions.at("actions").at(0).at("command");
    resolve_command["schema_version"] = 2U;
    const std::string resolve_text = resolve_command.dump();
    engine_code = SCGS_V05_NO_ENGINE_CODE;
    if (scgs_v05_submit_command_json(
            handle,
            resolve_text.data(),
            resolve_text.size(),
            &engine_code) != SCGS_V05_OK ||
        engine_code != 0U) {
        return fail("foundation pending choice could not be resolved");
    }
    const std::string resolved_view_text = read_json(
        [&](char* output, std::uint64_t capacity, std::uint64_t* required) {
            return scgs_v05_get_view_json(handle, 0U, output, capacity, required);
        });
    const Json resolved_view = Json::parse(resolved_view_text);
    if (resolved_view.at("revision") != revision + 1U ||
        resolved_view.at("view").at("pending_choice").at("pending").get<bool>()) {
        return fail("resolved choice did not clear once or increment revision exactly once");
    }
    const std::string owner_choice_events = read_json(
        [&](char* output, std::uint64_t capacity, std::uint64_t* required) {
            return scgs_v05_read_events_json(
                handle, 0U, initial_last_sequence, output, capacity, required);
        });
    const std::string opponent_choice_events = read_json(
        [&](char* output, std::uint64_t capacity, std::uint64_t* required) {
            return scgs_v05_read_events_json(
                handle, 1U, initial_last_sequence, output, capacity, required);
        });
    const Json owner_events = Json::parse(owner_choice_events);
    const Json opponent_events = Json::parse(opponent_choice_events);
    if (owner_events.at("events").size() != 1U ||
        opponent_events.at("events").size() != 1U ||
        owner_events.at("last_sequence") != opponent_events.at("last_sequence") ||
        opponent_events.at("events").at(0).at("type") != 25U ||
        !opponent_events.at("events").at(0).at("hidden_card").get<bool>() ||
        opponent_choice_events.find("foundation-option") != std::string::npos) {
        return fail("choice event cursors or opponent redaction failed");
    }

    static constexpr std::string_view wrong_schema =
        R"({"schema_version":1,"player":0,"expected_revision":1})";
    std::uint64_t required = 0U;
    if (scgs_v05_list_legal_actions_json(
            handle,
            wrong_schema.data(),
            wrong_schema.size(),
            nullptr,
            0U,
            &required) != SCGS_V05_SCHEMA_MISMATCH ||
        required != 0U) {
        return fail("schema-1 request was not rejected");
    }

    const std::string schema_error = read_json(
        [](char* output, std::uint64_t capacity, std::uint64_t* error_required) {
            return scgs_v05_get_last_error(output, capacity, error_required);
        });
    if (schema_error.find("schema version") == std::string::npos) {
        return fail("same-thread last-error did not preserve the schema diagnostic");
    }

    static constexpr std::string_view invalid_json = "{";
    engine_code = 123U;
    if (scgs_v05_submit_command_json(
            handle,
            invalid_json.data(),
            invalid_json.size(),
            &engine_code) != SCGS_V05_INVALID_JSON ||
        engine_code != SCGS_V05_NO_ENGINE_CODE) {
        return fail("malformed JSON was not rejected at the native boundary");
    }
    const std::string view_after_invalid_json = read_json(
        [&](char* output, std::uint64_t capacity, std::uint64_t* error_required) {
            return scgs_v05_get_view_json(handle, 0U, output, capacity, error_required);
        });
    if (view_after_invalid_json != resolved_view_text) {
        return fail("malformed JSON changed the foundation session");
    }
    const std::string events_after_invalid_json = read_json(
        [&](char* output, std::uint64_t capacity, std::uint64_t* error_required) {
            return scgs_v05_read_events_json(
                handle,
                0U,
                owner_events.at("last_sequence").get<std::uint64_t>(),
                output,
                capacity,
                error_required);
        });
    if (!Json::parse(events_after_invalid_json).at("events").empty()) {
        return fail("malformed JSON emitted a foundation event");
    }

    const std::array<Json, 3U> malformed_commands{{
        Json{{"schema_version", 2U}, {"player", 0U}, {"action", 99U},
             {"expected_revision", revision + 1U}},
        Json{{"schema_version", 2U}, {"player", 99U}, {"action", 9U},
             {"expected_revision", revision + 1U}},
        Json{{"schema_version", 2U}, {"player", 0U}, {"action", 9U},
             {"expected_revision", revision + 1U}, {"future_input", true}},
    }};
    for (const Json& malformed : malformed_commands) {
        const std::string malformed_text = malformed.dump();
        engine_code = 123U;
        if (scgs_v05_submit_command_json(
                handle,
                malformed_text.data(),
                malformed_text.size(),
                &engine_code) != SCGS_V05_SCHEMA_MISMATCH ||
            engine_code != SCGS_V05_NO_ENGINE_CODE) {
            return fail("unknown structural enum or input field was not rejected safely");
        }
    }

    scgs_v05_handle rejected_handle = 7U;
    if (scgs_v05_create(
            0x00010000U,
            config.data(),
            config.size(),
            &rejected_handle) != SCGS_V05_ABI_MISMATCH ||
        rejected_handle != 0U) {
        return fail("ABI-major mismatch was not rejected safely");
    }

    Json config_with_unknown_field = Json::parse(config);
    config_with_unknown_field["future_config"] = true;
    const std::string unknown_config_text = config_with_unknown_field.dump();
    rejected_handle = 7U;
    if (scgs_v05_create(
            SCGS_V05_ABI_VERSION,
            unknown_config_text.data(),
            unknown_config_text.size(),
            &rejected_handle) != SCGS_V05_SCHEMA_MISMATCH ||
        rejected_handle != 0U) {
        return fail("an unknown configuration field was not rejected safely");
    }

    static constexpr char invalid_utf8[] = {'{', static_cast<char>(0xC3), '}', '\0'};
    if (scgs_v05_create(
            SCGS_V05_ABI_VERSION,
            invalid_utf8,
            sizeof(invalid_utf8) - 1U,
            &rejected_handle) != SCGS_V05_INVALID_UTF8 ||
        rejected_handle != 0U) {
        return fail("invalid UTF-8 input was not rejected safely");
    }

    if (scgs_v05_destroy(handle) != SCGS_V05_OK) {
        return fail("destroy failed");
    }

    static constexpr std::string_view same_deck_config =
        R"({"schema_version":2,"player0_deck":"oathguard_luminous_oath_v1","player1_deck":"oathguard_luminous_oath_v1","random_seed":3,"first_player_mode":1,"shuffle_decks":false})";
    scgs_v05_handle same_deck_handle = 0U;
    if (scgs_v05_create(
            SCGS_V05_ABI_VERSION,
            same_deck_config.data(),
            same_deck_config.size(),
            &same_deck_handle) != SCGS_V05_OK ||
        scgs_v05_start(same_deck_handle, &engine_code) != SCGS_V05_OK || engine_code != 0U) {
        return fail("same-deck foundation fixture failed to start");
    }
    const std::string same_deck_view_text = read_json(
        [&](char* output, std::uint64_t capacity, std::uint64_t* required) {
            return scgs_v05_get_view_json(
                same_deck_handle, 1U, output, capacity, required);
        });
    const Json same_deck_view = Json::parse(same_deck_view_text).at("view");
    if (same_deck_view.at("players").at(1).at("profession_id") != "oathguard" ||
        same_deck_view.at("players").at(1).at("hand").at(0).at("design_id") != "LO-01" ||
        same_deck_view.at("players").at(1).at("field").at("design_id") != "LO-10" ||
        scgs_v05_destroy(same_deck_handle) != SCGS_V05_OK) {
        return fail("same-deck fixture ignored configured deck identity");
    }
    std::cout << "v05 schema-2 contract passed\n";
    return EXIT_SUCCESS;
}
