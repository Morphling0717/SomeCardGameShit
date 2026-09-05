// SPDX-License-Identifier: GPL-3.0-or-later
#include "scgs/native_api_v05.h"

#include <nlohmann/json.hpp>

#include <algorithm>
#include <array>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace {

using Json = nlohmann::ordered_json;

int fail(const std::string& message) {
    std::cerr << "v05 product adapter contract failed: " << message << '\n';
    return EXIT_FAILURE;
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

Json view(const scgs_v05_handle handle, const std::uint32_t viewer) {
    const std::string text = read_json([&](char* output, std::uint64_t capacity, std::uint64_t* required) {
        return scgs_v05_get_view_json(handle, viewer, output, capacity, required);
    });
    return text.empty() ? Json{} : Json::parse(text);
}

Json actions(
    const scgs_v05_handle handle,
    const std::uint32_t player,
    const std::uint64_t revision,
    const std::optional<std::uint32_t> action = std::nullopt) {
    Json query{
        {"schema_version", 2U},
        {"player", player},
        {"expected_revision", revision}};
    if (action.has_value()) {
        query["action"] = *action;
    }
    const std::string input = query.dump();
    const std::string text = read_json([&](char* output, std::uint64_t capacity, std::uint64_t* required) {
        return scgs_v05_list_legal_actions_json(
            handle, input.data(), input.size(), output, capacity, required);
    });
    return text.empty() ? Json{} : Json::parse(text);
}

Json events(
    const scgs_v05_handle handle,
    const std::uint32_t viewer,
    const std::uint64_t after_sequence) {
    const std::string text = read_json([&](char* output, std::uint64_t capacity, std::uint64_t* required) {
        return scgs_v05_read_events_json(
            handle, viewer, after_sequence, output, capacity, required);
    });
    return text.empty() ? Json{} : Json::parse(text);
}

bool observations_are_safe(const Json& batch0, const Json& batch1) {
    const auto& left = batch0.at("events");
    const auto& right = batch1.at("events");
    if (left.size() != right.size()) return false;
    for (std::size_t index = 0; index < left.size(); ++index) {
        for (const Json* event : {&left[index], &right[index]}) {
            if (!event->contains("observation")) continue;
            const auto& fact = event->at("observation");
            if (fact.at("version") != 1U || fact.at("revision") > batch0.at("revision") ||
                fact.at("cause_sequence") > event->at("sequence")) return false;
            for (const char* key : {"source", "subject", "target"}) {
                if (!fact.contains(key)) continue;
                const auto& endpoint = fact.at(key);
                if (endpoint.at("hidden") == true &&
                    (endpoint.contains("card") || endpoint.contains("design_id"))) return false;
                if (fact.at("public_to_all") == true && endpoint.at("hidden") == true) return false;
                if (event->at("hidden_card") == true &&
                    (endpoint.contains("card") || endpoint.contains("design_id"))) return false;
            }
            if (event->at("hidden_card") == true && (fact.contains("before") || fact.contains("after"))) return false;
            for (const char* key : {"from", "to"}) {
                if (!fact.contains(key)) continue;
                const auto& location = fact.at(key);
                const auto zone = location.at("zone").get<unsigned>();
                if (zone == 3U || zone == 4U) {
                    if (!location.contains("slot") || location.at("slot") >= (zone == 3U ? 5U : 3U)) return false;
                } else if (location.contains("slot")) return false;
            }
            if (fact.at("public_to_all") == true &&
                left[index].at("observation") != right[index].at("observation")) return false;
        }
    }
    return true;
}

std::uint32_t submit(const scgs_v05_handle handle, Json command) {
    command["schema_version"] = 2U;
    const std::string input = command.dump();
    std::uint32_t engine_code = SCGS_V05_NO_ENGINE_CODE;
    if (scgs_v05_submit_command_json(
            handle, input.data(), input.size(), &engine_code) != SCGS_V05_OK) {
        return SCGS_V05_NO_ENGINE_CODE;
    }
    return engine_code;
}

Json preview(const scgs_v05_handle handle, Json command) {
    command["schema_version"] = 2U;
    const std::string input = command.dump();
    const std::string text = read_json([&](char* output, std::uint64_t capacity, std::uint64_t* required) {
        return scgs_v05_preview_payment_json(
            handle, input.data(), input.size(), output, capacity, required);
    });
    return text.empty() ? Json{} : Json::parse(text);
}

Json query_projection(
    const scgs_v05_handle handle,
    Json command,
    const std::string_view projected_field,
    const std::string_view result_field,
    scgs_v05_native_code(SCGS_V05_CALL* function)(
        scgs_v05_handle, const char*, std::uint64_t, char*, std::uint64_t, std::uint64_t*)) {
    command["schema_version"] = 2U;
    command.erase(std::string(projected_field));
    const std::string input = command.dump();
    const std::string text = read_json([&](char* output, std::uint64_t capacity, std::uint64_t* required) {
        return function(handle, input.data(), input.size(), output, capacity, required);
    });
    if (text.empty()) {
        return {};
    }
    return Json::parse(text).at(std::string(result_field));
}

bool array_contains(const Json& values, const Json& expected) {
    return std::any_of(values.begin(), values.end(), [&](const Json& value) {
        return value == expected;
    });
}

const Json* first_non_surrender(const Json& action_list) {
    const auto found = std::find_if(action_list.begin(), action_list.end(), [](const Json& value) {
        return value.at("command").at("action") != 10U;
    });
    return found == action_list.end() ? nullptr : std::addressof(*found);
}

std::optional<std::string> find_design_id(const Json& value, const std::uint64_t instance) {
    if (value.is_object()) {
        if (value.contains("instance_id") && value.at("instance_id") == instance &&
            value.contains("design_id")) {
            return value.at("design_id").get<std::string>();
        }
        for (const auto& [name, child] : value.items()) {
            (void)name;
            if (const auto found = find_design_id(child, instance)) {
                return found;
            }
        }
    } else if (value.is_array()) {
        for (const Json& child : value) {
            if (const auto found = find_design_id(child, instance)) {
                return found;
            }
        }
    }
    return std::nullopt;
}

} // namespace

int main() {
    static constexpr std::string_view config =
        R"({"schema_version":2,"player0_deck":"oathguard_luminous_oath_v1","player1_deck":"pactmage_abyssal_pact_v1","random_seed":99,"first_player_mode":1,"shuffle_decks":false})";

    if (scgs_v05_abi_version() != SCGS_V05_ABI_VERSION) {
        return fail("ABI 2.0 changed");
    }
    scgs_v05_handle rejected = 7U;
    static constexpr std::string_view malformed_json = "{";
    if (scgs_v05_create(
            SCGS_V05_ABI_VERSION,
            malformed_json.data(),
            malformed_json.size(),
            &rejected) != SCGS_V05_INVALID_JSON || rejected != 0U) {
        return fail("malformed config JSON was not rejected safely");
    }
    const std::array<char, 2U> invalid_utf8{
        static_cast<char>(0xC3), static_cast<char>(0x28)};
    rejected = 7U;
    if (scgs_v05_create(
            SCGS_V05_ABI_VERSION,
            invalid_utf8.data(),
            invalid_utf8.size(),
            &rejected) != SCGS_V05_INVALID_UTF8 || rejected != 0U) {
        return fail("invalid UTF-8 crossed the config boundary");
    }
    static constexpr std::string_view invalid_first_player =
        R"({"schema_version":2,"player0_deck":"oathguard_luminous_oath_v1","player1_deck":"pactmage_abyssal_pact_v1","first_player_mode":3})";
    rejected = 7U;
    if (scgs_v05_create(
            SCGS_V05_ABI_VERSION,
            invalid_first_player.data(),
            invalid_first_player.size(),
            &rejected) != SCGS_V05_SCHEMA_MISMATCH || rejected != 0U) {
        return fail("unknown first-player enum was accepted");
    }

    scgs_v05_handle handle = 0U;
    if (scgs_v05_create(SCGS_V05_ABI_VERSION, config.data(), config.size(), &handle) !=
            SCGS_V05_OK || handle == 0U) {
        return fail("create rejected the locked product decks");
    }
    const Json prestart = view(handle, 0U);
    if (prestart.empty() || prestart.at("revision") != 0U ||
        prestart.at("view").at("phase") != 0U) {
        return fail("pre-start lifecycle snapshot is not controlled");
    }
    Json prestart_mulligan{
        {"player", 0U}, {"action", 0U}, {"mulligan_cards", Json::array()},
        {"expected_revision", 0U}};
    if (submit(handle, prestart_mulligan) != 31U || view(handle, 0U) != prestart) {
        return fail("pre-start command was not rejected atomically");
    }
    const std::string prestart_query = Json{
        {"schema_version", 2U}, {"player", 0U}, {"expected_revision", 0U}}.dump();
    std::uint64_t prestart_required = 99U;
    if (scgs_v05_list_legal_actions_json(
            handle,
            prestart_query.data(),
            prestart_query.size(),
            nullptr,
            0U,
            &prestart_required) != SCGS_V05_INVALID_ARGUMENT || prestart_required != 0U) {
        return fail("pre-start legal query did not fail at the lifecycle boundary");
    }
    std::uint32_t engine_code = SCGS_V05_NO_ENGINE_CODE;
    if (scgs_v05_start(handle, &engine_code) != SCGS_V05_OK || engine_code != 0U) {
        return fail("real ProductGame did not start");
    }
    if (scgs_v05_start(handle, &engine_code) != SCGS_V05_OK || engine_code != 30U) {
        return fail("start was not idempotently rejected by ProductGame");
    }

    const Json initial0 = view(handle, 0U);
    const Json initial1 = view(handle, 1U);
    if (initial0.empty() || initial1.empty() || initial0.at("schema_version") != 2U ||
        initial0.dump().find("random_seed") != std::string::npos ||
        initial1.dump().find("random_seed") != std::string::npos) {
        return fail("view envelope or random-seed redaction failed");
    }
    const Json& view0 = initial0.at("view");
    const Json& view1 = initial1.at("view");
    if (view0.at("revision") != 1U || view0.at("phase") != 1U ||
        view0.at("first_player") != 0U || view0.at("active_player") != 0U ||
        view0.at("players").at(0).at("hand").size() != 4U ||
        !view0.at("players").at(1).at("hand").empty() ||
        view1.at("players").at(1).at("hand").size() != 4U ||
        !view1.at("players").at(0).at("hand").empty() ||
        view0.at("players").at(0).at("deck_count") != 26U ||
        view0.at("players").at(0).at("standby").size() != 4U ||
        view0.at("players").at(0).at("main_board").size() != 5U ||
        view0.at("players").at(0).at("tactics").size() != 3U ||
        view0.at("pending_choice").at("pending").get<bool>()) {
        return fail("started product snapshot is not a real empty-board 30+4 match");
    }

    const Json initial_events0 = events(handle, 0U, 0U);
    const Json initial_events1 = events(handle, 1U, 0U);
    if (initial_events0.empty() ||
        initial_events0.at("last_sequence") != initial_events1.at("last_sequence") ||
        initial_events0.at("events").size() != initial_events1.at("events").size() ||
        initial_events0.dump().find("random_seed") != std::string::npos) {
        return fail("two viewer event cursors diverged or leaked the seed");
    }
    for (const Json& event : initial_events0.at("events")) {
        if (event.at("hidden_card").get<bool>() &&
            (event.contains("card") || event.contains("design_id"))) {
            return fail("opponent draw event leaked a stable card identity");
        }
    }
    if (events(handle, 0U, 0U) != initial_events0) {
        return fail("event reads are destructive");
    }

    const Json mulligan0 = actions(handle, 0U, 1U, 0U);
    if (mulligan0.empty() || mulligan0.at("actions").size() != 16U) {
        return fail("mulligan legal actions were not generated from the real four-card hand: " +
            mulligan0.dump());
    }
    const Json empty_mulligan = mulligan0.at("actions").at(0).at("command");
    std::vector<Json> successful_commands;
    const Json empty_preview = preview(handle, empty_mulligan);
    if (empty_preview.at("payment").at("status").at("engine_code") != 0U ||
        empty_preview.at("payment").at("current_pp_before") != 0 ||
        empty_preview.at("payment").at("current_pp_after") != 0) {
        return fail("mulligan payment preview does not match ProductGame");
    }

    Json stale = empty_mulligan;
    stale["expected_revision"] = 2U;
    const Json before_stale_view = view(handle, 0U);
    const Json before_stale_events = events(handle, 0U, 0U);
    if (submit(handle, stale) != 35U || view(handle, 0U) != before_stale_view ||
        events(handle, 0U, 0U) != before_stale_events) {
        return fail("stale command was not failure-atomic");
    }
    if (submit(handle, empty_mulligan) != 0U) {
        return fail("player 0 empty mulligan failed");
    }
    successful_commands.push_back(empty_mulligan);
    const Json after_p0 = view(handle, 0U);
    if (after_p0.at("revision") != 2U ||
        !after_p0.at("view").at("players").at(0).at("mulligan_done").get<bool>() ||
        after_p0.at("view").at("phase") != 1U) {
        return fail("successful mulligan did not increment exactly one revision");
    }
    Json repeated_mulligan = empty_mulligan;
    repeated_mulligan["expected_revision"] = 2U;
    const Json before_repeated_mulligan = view(handle, 0U);
    const Json before_repeated_events = events(handle, 0U, 0U);
    if (submit(handle, repeated_mulligan) != 32U ||
        view(handle, 0U) != before_repeated_mulligan ||
        events(handle, 0U, 0U) != before_repeated_events) {
        return fail("completed mulligan was not rejected with code 32 atomically");
    }
    const Json mulligan1 = actions(handle, 1U, 2U, 0U);
    if (mulligan1.empty()) {
        return fail("player 1 mulligan query failed");
    }
    const Json player1_mulligan = mulligan1.at("actions").at(0).at("command");
    if (submit(handle, player1_mulligan) != 0U) {
        return fail("player 1 empty mulligan failed");
    }
    successful_commands.push_back(player1_mulligan);

    Json current = view(handle, 0U);
    if (current.at("revision") != 3U || current.at("view").at("phase") != 2U ||
        current.at("view").at("players").at(0).at("current_pp") != 1 ||
        current.at("view").at("players").at(0).at("pp_capacity") != 1) {
        return fail("first real product turn did not begin");
    }

    const Json before_invalid_shape = current;
    const Json before_invalid_shape_events = events(handle, 0U, 0U);
    Json unrelated_mode{
        {"schema_version", 2U}, {"player", 0U}, {"action", 8U},
        {"mode_id", "ignored-mode"}, {"expected_revision", 3U}};
    std::uint32_t invalid_shape_code = SCGS_V05_NO_ENGINE_CODE;
    const std::string unrelated_mode_text = unrelated_mode.dump();
    if (scgs_v05_submit_command_json(
            handle,
            unrelated_mode_text.data(),
            unrelated_mode_text.size(),
            &invalid_shape_code) != SCGS_V05_OK || invalid_shape_code != 40U ||
        view(handle, 0U) != before_invalid_shape ||
        events(handle, 0U, 0U) != before_invalid_shape_events) {
        return fail("unrelated command field was not rejected atomically");
    }
    Json unknown_action{
        {"schema_version", 2U}, {"player", 0U}, {"action", 99U},
        {"expected_revision", 3U}};
    const std::string unknown_action_text = unknown_action.dump();
    invalid_shape_code = 123U;
    if (scgs_v05_submit_command_json(
            handle,
            unknown_action_text.data(),
            unknown_action_text.size(),
            &invalid_shape_code) != SCGS_V05_SCHEMA_MISMATCH ||
        invalid_shape_code != SCGS_V05_NO_ENGINE_CODE ||
        view(handle, 0U) != before_invalid_shape) {
        return fail("unknown action enum crossed schema 2 or mutated state");
    }

    bool slot_projection_seen = false;
    bool target_projection_seen = false;
    bool donor_projection_seen = false;
    bool choice_seen = false;
    bool reaction_seen = false;
    bool choice_boundary_checked = false;
    std::uint64_t event_cursor = initial_events0.at("last_sequence").get<std::uint64_t>();
    std::size_t steps = 0U;
    while (current.at("view").at("phase") != 4U && steps < 2000U) {
        const Json& state = current.at("view");
        std::uint32_t actor = state.at("active_player").get<std::uint32_t>();
        if (state.at("pending_choice").at("pending").get<bool>()) {
            actor = state.at("pending_choice").at("chooser").get<std::uint32_t>();
            choice_seen = true;
            const Json owner_context = Json::parse(read_json(
                [&](char* output, std::uint64_t capacity, std::uint64_t* required) {
                    return scgs_v05_get_reaction_context_json(
                        handle, actor, output, capacity, required);
                }));
            const Json opponent_context = Json::parse(read_json(
                [&](char* output, std::uint64_t capacity, std::uint64_t* required) {
                    return scgs_v05_get_reaction_context_json(
                        handle, 1U - actor, output, capacity, required);
                }));
            const Json& owner_choice = owner_context.at("pending_choice");
            const Json& opponent_choice = opponent_context.at("pending_choice");
            if (!owner_choice.contains("choice_id") || !owner_choice.contains("options") ||
                opponent_choice.contains("choice_id") || opponent_choice.contains("options")) {
                return fail("pending choice was not owner-only and opaque");
            }
        } else if (state.at("reaction").at("pending").get<bool>()) {
            actor = state.at("reaction").at("responder").get<std::uint32_t>();
            reaction_seen = true;
            const Json responder = Json::parse(read_json(
                [&](char* output, std::uint64_t capacity, std::uint64_t* required) {
                    return scgs_v05_get_reaction_context_json(
                        handle, actor, output, capacity, required);
                }));
            const Json observer = Json::parse(read_json(
                [&](char* output, std::uint64_t capacity, std::uint64_t* required) {
                    return scgs_v05_get_reaction_context_json(
                        handle, 1U - actor, output, capacity, required);
                }));
            if (!responder.at("reaction").at("pending").get<bool>() ||
                responder.at("reaction").at("eligible_count") !=
                    responder.at("reaction").at("eligible_traps").size() ||
                observer.at("reaction").at("eligible_count") != 0U ||
                !observer.at("reaction").at("eligible_traps").empty()) {
                return fail("reaction context leaked responder trap identities");
            }
        }

        const std::uint64_t revision = current.at("revision").get<std::uint64_t>();
        const Json legal = actions(handle, actor, revision);
        if (legal.empty() || legal.at("revision") != revision || legal.at("actions").empty()) {
            return fail("real ProductGame produced no legal transport action");
        }
        const Json actor_view = view(handle, actor);
        const Json* selected = nullptr;
        if (!choice_seen) {
            const auto preferred_choice_source = std::find_if(
                legal.at("actions").begin(),
                legal.at("actions").end(),
                [&](const Json& candidate) {
                    const Json& candidate_command = candidate.at("command");
                    if (!candidate_command.contains("source")) {
                        return false;
                    }
                    const auto design = find_design_id(
                        actor_view, candidate_command.at("source").get<std::uint64_t>());
                    return design == "AP-05" || design == "LO-01" || design == "AP-09";
                });
            if (preferred_choice_source != legal.at("actions").end()) {
                selected = std::addressof(*preferred_choice_source);
            } else {
                const auto non_attack = std::find_if(
                    legal.at("actions").begin(),
                    legal.at("actions").end(),
                    [](const Json& candidate) {
                        const std::uint32_t action = candidate.at("command").at("action");
                        return action != 4U && action != 10U;
                    });
                if (non_attack != legal.at("actions").end()) {
                    selected = std::addressof(*non_attack);
                }
            }
        }
        if (selected == nullptr) {
            selected = first_non_surrender(legal.at("actions"));
        }
        if (selected == nullptr) {
            return fail("only surrender remained before the natural terminal");
        }
        const Json& command = selected->at("command");
        if (selected->at("payment").at("status").at("engine_code") != 0U ||
            preview(handle, command).at("payment").at("status").at("engine_code") != 0U) {
            return fail("enumerated action and payment preview disagreed");
        }

        if (command.at("action") == 13U && !choice_boundary_checked) {
            scgs_v05_handle shadow = 0U;
            if (scgs_v05_create(
                    SCGS_V05_ABI_VERSION,
                    config.data(),
                    config.size(),
                    &shadow) != SCGS_V05_OK || shadow == 0U) {
                return fail("could not create same-revision choice shadow session");
            }
            std::uint32_t shadow_start = SCGS_V05_NO_ENGINE_CODE;
            if (scgs_v05_start(shadow, &shadow_start) != SCGS_V05_OK || shadow_start != 0U) {
                (void)scgs_v05_destroy(shadow);
                return fail("choice shadow session did not start");
            }
            for (const Json& replay : successful_commands) {
                if (submit(shadow, replay) != 0U) {
                    (void)scgs_v05_destroy(shadow);
                    return fail("same-revision command copy diverged before pending choice");
                }
            }
            const Json shadow_before = view(shadow, actor);
            const Json shadow_events_before = events(shadow, actor, 0U);
            if (shadow_before.at("revision") != revision ||
                !shadow_before.at("view").at("pending_choice").at("pending").get<bool>()) {
                (void)scgs_v05_destroy(shadow);
                return fail("replayed shadow did not reach the same pending choice");
            }
            if (submit(shadow, command) != 38U || view(shadow, actor) != shadow_before ||
                events(shadow, actor, 0U) != shadow_events_before) {
                (void)scgs_v05_destroy(shadow);
                return fail("cross-session opaque choice token was accepted or mutated state");
            }

            const Json shadow_choices = actions(shadow, actor, revision, 13U);
            if (shadow_choices.empty() || shadow_choices.at("actions").empty()) {
                (void)scgs_v05_destroy(shadow);
                return fail("shadow choice did not enumerate accepted selections");
            }
            Json wrong_owner = shadow_choices.at("actions").at(0).at("command");
            wrong_owner["player"] = 1U - actor;
            if (submit(shadow, wrong_owner) != 39U || view(shadow, actor) != shadow_before ||
                events(shadow, actor, 0U) != shadow_events_before) {
                (void)scgs_v05_destroy(shadow);
                return fail("non-owner choice was not rejected with code 39 atomically");
            }

            Json choice_surrender{
                {"player", 1U - actor}, {"action", 10U}, {"expected_revision", revision}};
            if (submit(shadow, choice_surrender) != 0U) {
                (void)scgs_v05_destroy(shadow);
                return fail("surrender was blocked by a pending paid choice");
            }
            const Json shadow_finished = view(shadow, actor);
            const Json shadow_finished_events = events(shadow, actor, 0U);
            const auto ended = std::count_if(
                shadow_finished_events.at("events").begin(),
                shadow_finished_events.at("events").end(), [](const Json& event) {
                    return event.at("type") == 23U;
                });
            if (shadow_finished.at("view").at("phase") != 4U || ended != 1 ||
                shadow_finished_events.at("events").back().at("type") != 23U ||
                scgs_v05_destroy(shadow) != SCGS_V05_OK) {
                return fail("choice-time surrender did not terminate exactly once");
            }
            choice_boundary_checked = true;
        }

        if (!slot_projection_seen && command.contains("slot")) {
            Json slots = query_projection(
                handle, command, "slot", "slots", &scgs_v05_list_valid_slots_json);
            if (!array_contains(slots, command.at("slot"))) {
                return fail("selected legal slot was absent from the query projection");
            }
            slot_projection_seen = true;
        }
        if (!target_projection_seen && command.contains("target")) {
            Json targets = query_projection(
                handle, command, "target", "targets", &scgs_v05_list_valid_targets_json);
            if (!array_contains(targets, command.at("target"))) {
                return fail("selected legal leader/permanent target was absent from the query projection");
            }
            target_projection_seen = true;
        }
        if (!donor_projection_seen && command.contains("additional_cost_cards") &&
            !command.at("additional_cost_cards").empty()) {
            Json donors = query_projection(
                handle,
                command,
                "additional_cost_cards",
                "donors",
                &scgs_v05_list_valid_donors_json);
            if (!array_contains(donors, command.at("additional_cost_cards").at(0))) {
                return fail("selected deployment additional cost was absent from donors");
            }
            donor_projection_seen = true;
        }

        const bool resolving_choice = command.at("action") == 13U;
        if (submit(handle, command) != 0U) {
            return fail("enumerated action was not submit-able at the same revision");
        }
        successful_commands.push_back(command);
        current = view(handle, 0U);
        if (current.at("revision") != revision + 1U) {
            return fail("successful command did not advance exactly one revision");
        }
        if (resolving_choice) {
            const Json before_replay = current;
            const Json before_replay_events = events(handle, 0U, 0U);
            if (submit(handle, command) != 38U || view(handle, 0U) != before_replay ||
                events(handle, 0U, 0U) != before_replay_events) {
                return fail("resolved opaque choice token was replayable or not failure-atomic");
            }
        }
        const Json batch0 = events(handle, 0U, event_cursor);
        const Json batch1 = events(handle, 1U, event_cursor);
        if (!observations_are_safe(batch0, batch1)) {
            return fail("versioned mutation observations lost privacy, temporal identity or public viewer equivalence");
        }
        if (batch0.at("last_sequence") != batch1.at("last_sequence")) {
            return fail("viewer event cursors diverged while playing the match");
        }
        event_cursor = batch0.at("last_sequence").get<std::uint64_t>();
        ++steps;
    }

    if (steps >= 2000U || current.at("view").at("phase") != 4U ||
        current.at("view").at("result") == 0U) {
        return fail("the native product match did not reach a natural terminal");
    }
    if (!slot_projection_seen || !target_projection_seen || !choice_seen || !reaction_seen) {
        return fail(
            "full match missed required adapter coverage (slot=" +
            std::to_string(slot_projection_seen) + ", target=" +
            std::to_string(target_projection_seen) + ", choice=" +
            std::to_string(choice_seen) + ", reaction=" +
            std::to_string(reaction_seen) + ")");
    }
    const Json final_events = events(handle, 0U, 0U);
    if (!observations_are_safe(final_events, events(handle, 1U, 0U))) {
        return fail("final observation history failed the two-viewer privacy contract");
    }
    for (std::size_t index = 0; index < initial_events0.at("events").size(); ++index) {
        const auto& initial = initial_events0.at("events")[index];
        if (initial.contains("observation") &&
            initial.at("observation") != final_events.at("events")[index].at("observation")) {
            return fail("later play/reveal retroactively changed an earlier private observation");
        }
    }
    if (std::none_of(final_events.at("events").begin(), final_events.at("events").end(), [](const Json& event) {
        return event.contains("observation") && event.at("observation").at("public_to_all") == true;
    })) return fail("real product game produced no public mutation observations");
    const auto match_ended = std::count_if(
        final_events.at("events").begin(), final_events.at("events").end(), [](const Json& event) {
            return event.at("type") == 23U;
        });
    if (match_ended != 1 || final_events.at("events").back().at("type") != 23U) {
        return fail("MatchEnded was not unique and final through v05");
    }
    const Json finished_query{
        {"schema_version", 2U},
        {"player", 0U},
        {"expected_revision", current.at("revision")}};
    const std::string finished_input = finished_query.dump();
    std::uint64_t required = 99U;
    if (scgs_v05_list_legal_actions_json(
            handle,
            finished_input.data(),
            finished_input.size(),
            nullptr,
            0U,
            &required) != SCGS_V05_INVALID_ARGUMENT || required != 0U) {
        return fail("finished match query was not rejected");
    }

    static constexpr std::string_view wrong_schema =
        R"({"schema_version":1,"player":0,"expected_revision":1})";
    required = 99U;
    if (scgs_v05_list_legal_actions_json(
            handle,
            wrong_schema.data(),
            wrong_schema.size(),
            nullptr,
            0U,
            &required) != SCGS_V05_SCHEMA_MISMATCH || required != 0U) {
        return fail("schema 1 crossed the v05 boundary");
    }
    const std::string last_error = read_json(
        [](char* output, std::uint64_t capacity, std::uint64_t* error_required) {
            return scgs_v05_get_last_error(output, capacity, error_required);
        });
    if (last_error.find("schema version") == std::string::npos) {
        return fail("same-thread last_error lost the schema diagnostic");
    }

    if (scgs_v05_destroy(handle) != SCGS_V05_OK ||
        scgs_v05_destroy(handle) != SCGS_V05_INVALID_HANDLE ||
        scgs_v05_destroy(0U) != SCGS_V05_OK) {
        return fail("safe-handle destruction contract changed");
    }

    rejected = 7U;
    if (scgs_v05_create(0x00010000U, config.data(), config.size(), &rejected) !=
            SCGS_V05_ABI_MISMATCH || rejected != 0U) {
        return fail("ABI-major mismatch was not rejected safely");
    }
    static constexpr std::string_view invalid_deck =
        R"({"schema_version":2,"player0_deck":"midrange","player1_deck":"advance"})";
    rejected = 7U;
    if (scgs_v05_create(
            SCGS_V05_ABI_VERSION,
            invalid_deck.data(),
            invalid_deck.size(),
            &rejected) != SCGS_V05_SCHEMA_MISMATCH || rejected != 0U) {
        return fail("retired product deck keys were accepted by v05");
    }

    static constexpr std::string_view same_deck_config =
        R"({"schema_version":2,"player0_deck":"oathguard_luminous_oath_v1","player1_deck":"oathguard_luminous_oath_v1","random_seed":17,"first_player_mode":2,"shuffle_decks":true})";
    scgs_v05_handle same_deck = 0U;
    std::uint32_t same_deck_start = SCGS_V05_NO_ENGINE_CODE;
    if (scgs_v05_create(
            SCGS_V05_ABI_VERSION,
            same_deck_config.data(),
            same_deck_config.size(),
            &same_deck) != SCGS_V05_OK || same_deck == 0U ||
        scgs_v05_start(same_deck, &same_deck_start) != SCGS_V05_OK ||
        same_deck_start != 0U || view(same_deck, 0U).at("view").at("first_player") != 1U ||
        scgs_v05_destroy(same_deck) != SCGS_V05_OK) {
        return fail("same-deck hotseat configuration or forced Player1 start failed");
    }

    std::cout << "v05 real-product schema-2 adapter passed in " << steps
              << " commands; choice-boundary=" << choice_boundary_checked
              << "; donor_projection=" << donor_projection_seen << '\n';
    return EXIT_SUCCESS;
}
