// SPDX-License-Identifier: GPL-3.0-or-later
#include "scgs/product_runtime.hpp"

#include <algorithm>
#include <array>
#include <cstdlib>
#include <exception>
#include <iostream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <unordered_set>
#include <vector>

namespace {

using namespace scgs;
namespace product = scgs::v2;

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
#define EXPECT_CODE(ctx, status, expected) \
    (ctx).expect((status).code == (expected), #status ".code == " #expected, __FILE__, __LINE__)

class CapabilityCoverage {
public:
    CapabilityCoverage() {
        for (const std::string_view id : product::required_product_capability_ids()) {
            required_.emplace(id);
        }
    }

    void prove(
        TestContext& context,
        const std::string_view capability_id,
        const bool executable_assertion,
        const char* expression,
        const char* file,
        const int line) {
        context.expect(executable_assertion, expression, file, line);
        if (!required_.contains(std::string(capability_id))) {
            unexpected_.emplace(capability_id);
        } else if (executable_assertion) {
            covered_.emplace(capability_id);
        }
    }

    void verify(TestContext& context) const {
        EXPECT(context, required_.size() == 42U);
        EXPECT(context, unexpected_.empty());
        for (const std::string& id : required_) {
            if (!covered_.contains(id)) {
                std::cerr << "missing executable product capability evidence: " << id << '\n';
            }
            EXPECT(context, covered_.contains(id));
        }
        EXPECT(context, covered_.size() == required_.size());
    }

private:
    std::unordered_set<std::string> required_;
    std::unordered_set<std::string> covered_;
    std::unordered_set<std::string> unexpected_;
};

CapabilityCoverage* g_capabilities = nullptr;

#define EXPECT_CAP(ctx, capability_id, expression) \
    do { \
        const bool capability_result = static_cast<bool>(expression); \
        g_capabilities->prove( \
            (ctx), (capability_id), capability_result, #expression, __FILE__, __LINE__); \
    } while (false)

void expect_valid(TestContext& context, const product::ProductBoard& board) {
    const std::vector<std::string> problems = board.validate_invariants();
    for (const std::string& problem : problems) {
        std::cerr << "product invariant: " << problem << '\n';
    }
    EXPECT(context, problems.empty());
}

void test_schema_two_frozen_domain(TestContext& context) {
    EXPECT(context, static_cast<int>(product::CardKind::Follower) == 0);
    EXPECT(context, static_cast<int>(product::CardKind::Field) == 4);
    EXPECT(context, static_cast<int>(product::Zone::MainBoard) == 3);
    EXPECT(context, static_cast<int>(product::Zone::Field) == 8);
    EXPECT(context, static_cast<int>(product::ActionKind::Surrender) == 10);
    EXPECT(context, static_cast<int>(product::ActionKind::PlayAmulet) == 11);
    EXPECT(context, static_cast<int>(product::ActionKind::ResolveChoice) == 13);

    product::CardIdentity class_card{"LO-FIXTURE", "oathguard", "luminous_oath", false};
    product::CardIdentity neutral{"NT-FIXTURE", "neutral", "neutral", true};
    EXPECT(context, class_card.is_constructible_for("oathguard"));
    EXPECT(context, !class_card.is_constructible_for("pactmage"));
    EXPECT(context, neutral.is_constructible_for("oathguard"));
    EXPECT(context, neutral.is_constructible_for("pactmage"));
    EXPECT_CAP(
        context,
        "profession_series_neutral_tags",
        class_card.is_constructible_for("oathguard") &&
            !class_card.is_constructible_for("pactmage") && neutral.is_constructible_for("pactmage"));
}

void test_generated_locked_product_catalog(TestContext& context) {
    const product::CardCatalog catalog = product::make_locked_product_catalog();
    EXPECT(context, catalog.size() == 35);

    std::size_t main_count = 0;
    std::size_t standby_count = 0;
    std::size_t token_count = 0;
    std::array<std::size_t, 5> kind_counts{};
    for (const auto& [design_id, card] : catalog.definitions()) {
        EXPECT(context, design_id == card.identity.design_id);
        EXPECT(context, !card.name.empty());
        EXPECT(context, !card.canonical_rules_text.empty());
        EXPECT(context, card.identity.neutral ||
            (!card.identity.profession_id.empty() && !card.identity.series_id.empty()));
        EXPECT(context,
            card.implementation_status == product::CardImplementationStatus::LockedNotImplemented);
        EXPECT(context, !card.effects_compiled);
        EXPECT(context, !card.is_executable());
        EXPECT(context, card.effects.empty());
        EXPECT(context, std::all_of(card.modes.begin(), card.modes.end(), [](const product::ModeSpec& mode) {
            return mode.effects.empty();
        }));
        ++kind_counts[static_cast<std::size_t>(card.kind)];
        switch (card.availability) {
            case product::CardAvailability::MainDeck:
                ++main_count;
                EXPECT(context, card.cost > 0);
                EXPECT(context, !card.standby.has_value());
                break;
            case product::CardAvailability::Standby:
                ++standby_count;
                EXPECT(context, card.cost == 0);
                EXPECT(context, card.standby.has_value());
                EXPECT(context, card.standby->pp_cost > 0);
                EXPECT(context, !card.standby->conditions.empty());
                EXPECT(context, !card.can_advance);
                break;
            case product::CardAvailability::Token:
                ++token_count;
                EXPECT(context, card.cost == 0);
                EXPECT(context, !card.can_advance);
                EXPECT(context, !card.standby.has_value());
                break;
        }
    }
    EXPECT(context, main_count == 26);
    EXPECT(context, standby_count == 8);
    EXPECT(context, main_count + standby_count == 34);
    EXPECT(context, token_count == 1);
    EXPECT(context, kind_counts[static_cast<std::size_t>(product::CardKind::Follower)] == 25);
    EXPECT(context, kind_counts[static_cast<std::size_t>(product::CardKind::Spell)] == 4);
    EXPECT(context, kind_counts[static_cast<std::size_t>(product::CardKind::Amulet)] == 3);
    EXPECT(context, kind_counts[static_cast<std::size_t>(product::CardKind::Trap)] == 1);
    EXPECT(context, kind_counts[static_cast<std::size_t>(product::CardKind::Field)] == 2);

    const product::CardDefinition& bell = catalog.at("LO-03");
    EXPECT(context, bell.kind == product::CardKind::Amulet);
    EXPECT(context, bell.cost == 2);
    EXPECT(context, bell.countdown == 3);
    EXPECT(context, !bell.can_advance);
    EXPECT(context, bell.identity.profession_id == "oathguard");
    EXPECT(context, bell.identity.series_id == "luminous_oath");

    const product::CardDefinition& burning_student = catalog.at("AP-02");
    EXPECT(context, burning_student.burn_pp_capacity == 1);
    EXPECT(context, burning_student.can_advance);

    const product::CardDefinition& final_creditor = catalog.at("AP-S04");
    EXPECT(context, final_creditor.availability == product::CardAvailability::Standby);
    EXPECT(context, final_creditor.standby.has_value());
    EXPECT(context, final_creditor.standby->pp_cost == 6);
    EXPECT(context, final_creditor.standby->requires_additional_cost);
    EXPECT(context, final_creditor.standby->additional_cost_target == product::TargetSpec::FriendlyPermanent);
    EXPECT(context, final_creditor.standby->additional_cost_minimum == 1);
    EXPECT(context, final_creditor.standby->additional_cost_maximum == 1);
    EXPECT(context, !final_creditor.standby->condition_text.empty());
    EXPECT(context, !final_creditor.standby->additional_cost_text.empty());

    const product::CardDefinition& token = catalog.at("LO-T01");
    EXPECT(context, token.availability == product::CardAvailability::Token);
    EXPECT(context, token.attack == 3 && token.health == 3);
    EXPECT(context, product::contains(token.printed_keywords, product::Keyword::Ward));

    const product::CardDefinition& neutral = catalog.at("NT-04");
    EXPECT(context, neutral.identity.neutral);
    EXPECT(context, neutral.identity.is_constructible_for("oathguard"));
    EXPECT(context, neutral.identity.is_constructible_for("pactmage"));
}

void test_locked_runtime_shape_and_execution_gate(TestContext& context) {
    product::ProductBoard board(product::make_locked_product_catalog());
    const auto& catalog = board.catalog();

    const auto find_mode = [](const product::CardDefinition& card, const std::string_view id) {
        return std::find_if(card.modes.begin(), card.modes.end(), [&](const product::ModeSpec& mode) {
            return mode.mode_id == id;
        });
    };

    const product::CardDefinition& settlement = catalog.at("AP-08");
    EXPECT(context, settlement.modes.size() == 2);
    const auto repair = find_mode(settlement, "repair");
    const auto empower = find_mode(settlement, "empower");
    EXPECT(context, repair != settlement.modes.end());
    EXPECT(context, empower != settlement.modes.end());
    EXPECT(context, repair->target == product::TargetSpec::None);
    EXPECT(context, empower->target == product::TargetSpec::FriendlyFollower);
    EXPECT(context, empower->target_filter.allowed_kinds ==
        std::vector<product::CardKind>({product::CardKind::Follower}));
    EXPECT(context, empower->target_filter.profession_id == "pactmage");
    EXPECT(context, empower->target_filter.series_id == "abyssal_pact");
    EXPECT(context, empower->target_filter.include_main_board);
    EXPECT(context, !empower->target_filter.include_field);

    const product::CardDefinition& judgment = catalog.at("NT-04");
    EXPECT(context, judgment.modes.size() == 2);
    const auto damage = find_mode(judgment, "damage_follower");
    const auto destroy = find_mode(judgment, "destroy_amulet_or_field");
    EXPECT(context, damage != judgment.modes.end());
    EXPECT(context, destroy != judgment.modes.end());
    EXPECT(context, damage->target == product::TargetSpec::EnemyFollower);
    EXPECT(context, destroy->target == product::TargetSpec::EnemyPermanent);
    EXPECT(context, destroy->target_filter.allowed_kinds ==
        std::vector<product::CardKind>({product::CardKind::Amulet, product::CardKind::Field}));
    EXPECT(context, destroy->target_filter.include_main_board);
    EXPECT(context, destroy->target_filter.include_field);

    constexpr std::array<std::string_view, 8> standby_ids = {
        "LO-S01", "LO-S02", "LO-S03", "LO-S04",
        "AP-S01", "AP-S02", "AP-S03", "AP-S04",
    };
    for (const std::string_view id : standby_ids) {
        const product::CardDefinition& standby = catalog.at(id);
        EXPECT(context, standby.standby.has_value());
        EXPECT(context, !standby.standby->conditions.empty());
        for (const product::ConditionSpec& condition : standby.standby->conditions) {
            EXPECT(context, !condition.condition_id.empty());
        }
    }

    const product::StandbySpec& abaddon = *catalog.at("AP-S04").standby;
    EXPECT(context, abaddon.requires_additional_cost);
    const product::PermanentFilter abaddon_filter =
        product::PermanentFilter::from_spec(abaddon.additional_cost_filter);

    const InstanceId pact_follower = board.create_instance(
        "AP-01", PlayerId::Player0, product::Zone::Hand);
    const InstanceId pact_amulet = board.create_instance(
        "AP-04", PlayerId::Player0, product::Zone::Hand);
    const InstanceId oath_follower = board.create_instance(
        "LO-04", PlayerId::Player0, product::Zone::Hand);
    const InstanceId pact_field = board.create_instance(
        "AP-05", PlayerId::Player0, product::Zone::Hand);
    EXPECT(context, board.place_main(PlayerId::Player0, pact_follower, 0));
    EXPECT(context, board.place_main(PlayerId::Player0, pact_amulet, 1));
    EXPECT(context, board.place_main(PlayerId::Player0, oath_follower, 2));
    EXPECT(context, board.play_field(PlayerId::Player0, pact_field));
    EXPECT(context, board.list_permanents(PlayerId::Player0, abaddon_filter) ==
        std::vector<InstanceId>({pact_follower, pact_amulet}));
    EXPECT(context, abaddon_filter.matches(catalog.at("AP-01")));
    EXPECT(context, abaddon_filter.matches(catalog.at("AP-04")));
    EXPECT(context, !abaddon_filter.matches(catalog.at("LO-04")));
    EXPECT(context, !abaddon_filter.matches(catalog.at("AP-05")));

    EXPECT(context, board.list_payable_definitions(product::CardAvailability::MainDeck).empty());
    EXPECT(context, board.list_payable_definitions(product::CardAvailability::Standby).empty());
    EXPECT_CODE(
        context,
        board.validate_payable("AP-08", product::CardAvailability::MainDeck),
        product::ErrorCode::InvalidCard);
    EXPECT_CODE(
        context,
        board.validate_mode("AP-08", std::string_view("repair")),
        product::ErrorCode::InvalidCard);
    product::ConditionEvaluationContext eligible;
    eligible.cracks = 9;
    eligible.controlled_series.push_back("abyssal_pact");
    EXPECT_CODE(
        context,
        board.validate_standby("AP-S04", eligible),
        product::ErrorCode::InvalidCard);
    expect_valid(context, board);
}

void test_mixed_main_board_and_permanent_rules(TestContext& context) {
    product::ProductBoard board(product::make_synthetic_product_catalog());
    const InstanceId follower = board.create_instance(
        product::synthetic::kFollower, PlayerId::Player0, product::Zone::Hand);
    const InstanceId amulet = board.create_instance(
        product::synthetic::kAmulet, PlayerId::Player0, product::Zone::Hand);
    EXPECT(context, board.place_main(PlayerId::Player0, follower, 0));
    EXPECT(context, board.place_main(PlayerId::Player0, amulet, 1));
    EXPECT(context, board.player(PlayerId::Player0).main_board[0] == follower);
    EXPECT(context, board.player(PlayerId::Player0).main_board[1] == amulet);
    EXPECT_CAP(
        context,
        "amulet_main_board",
        board.instance(amulet).zone == product::Zone::MainBoard &&
            board.catalog().at(board.instance(amulet).design_id).kind == product::CardKind::Amulet);
    EXPECT_CAP(
        context,
        "countdown_permanent",
        board.instance(amulet).countdown == 1 && board.player(PlayerId::Player0).main_board[1] == amulet);
    EXPECT_CODE(context, board.validate_attack_source(PlayerId::Player0, amulet), product::ErrorCode::InvalidKind);
    EXPECT_CODE(context, board.validate_evolve(PlayerId::Player0, amulet), product::ErrorCode::InvalidKind);

    const InstanceId enemy_amulet = board.create_instance(
        product::synthetic::kAmulet, PlayerId::Player1, product::Zone::Hand);
    EXPECT(context, board.place_main(PlayerId::Player1, enemy_amulet, 0));
    EXPECT_CODE(
        context,
        board.validate_attack_target(PlayerId::Player0, enemy_amulet),
        product::ErrorCode::InvalidKind);

    for (std::size_t slot = 2; slot < product::kMainBoardSize; ++slot) {
        const InstanceId extra = board.create_instance(
            product::synthetic::kFollower, PlayerId::Player0, product::Zone::Hand);
        EXPECT(context, board.place_main(PlayerId::Player0, extra, slot));
    }
    const InstanceId sixth = board.create_instance(
        product::synthetic::kFollower, PlayerId::Player0, product::Zone::Hand);
    EXPECT_CODE(context, board.place_main(PlayerId::Player0, sixth, 0), product::ErrorCode::MainBoardFull);

    const InstanceId spell = board.create_instance(
        product::synthetic::kSpell, PlayerId::Player0, product::Zone::Hand);
    EXPECT_CODE(context, board.place_main(PlayerId::Player0, spell, 0), product::ErrorCode::InvalidKind);
    expect_valid(context, board);
}

void test_independent_field_replacement(TestContext& context) {
    product::ProductBoard board(product::make_synthetic_product_catalog());
    const InstanceId field_a = board.create_instance(
        product::synthetic::kFieldA, PlayerId::Player0, product::Zone::Hand);
    const InstanceId field_b = board.create_instance(
        product::synthetic::kFieldB, PlayerId::Player0, product::Zone::Hand);
    const InstanceId enemy_field = board.create_instance(
        product::synthetic::kFieldA, PlayerId::Player1, product::Zone::Hand);
    EXPECT(context, board.play_field(PlayerId::Player0, field_a));
    EXPECT(context, board.play_field(PlayerId::Player1, enemy_field));
    EXPECT(context, board.play_field(PlayerId::Player0, field_b));
    EXPECT(context, board.player(PlayerId::Player0).field == field_b);
    EXPECT(context, board.player(PlayerId::Player1).field == enemy_field);
    EXPECT(context, board.instance(field_a).zone == product::Zone::Graveyard);
    EXPECT(context, board.player(PlayerId::Player0).main_board[0] == std::nullopt);

    const product::MoveRecord& replacement = board.moves()[board.moves().size() - 2];
    EXPECT(context, replacement.card == field_a);
    EXPECT(context, replacement.reason == product::MoveReason::FieldReplaced);
    EXPECT(context, !replacement.destroyed);
    EXPECT_CAP(
        context,
        "field_zone",
        board.player(PlayerId::Player0).field == field_b &&
            board.player(PlayerId::Player1).field == enemy_field);
    EXPECT_CAP(
        context,
        "field_replacement_without_destroy",
        board.instance(field_a).zone == product::Zone::Graveyard &&
            replacement.reason == product::MoveReason::FieldReplaced && !replacement.destroyed);
    expect_valid(context, board);
}

void test_countdown_reserves_original_token_slot(TestContext& context) {
    product::ProductBoard board(product::make_synthetic_product_catalog());
    const InstanceId amulet = board.create_instance(
        product::synthetic::kAmulet, PlayerId::Player0, product::Zone::Hand);
    EXPECT(context, board.place_main(PlayerId::Player0, amulet, 3));
    EXPECT(context, board.expire_amulet_and_reserve(amulet, 42));
    EXPECT(context, board.instance(amulet).zone == product::Zone::Graveyard);
    EXPECT(context, board.reserved_by(PlayerId::Player0, 3) == 42);
    EXPECT(context, !board.player(PlayerId::Player0).main_board[3].has_value());

    const InstanceId interloper = board.create_instance(
        product::synthetic::kFollower, PlayerId::Player0, product::Zone::Hand);
    EXPECT_CODE(context, board.place_main(PlayerId::Player0, interloper, 3), product::ErrorCode::SlotReserved);

    InstanceId token = 0;
    EXPECT_CODE(
        context,
        board.summon_token_in_reserved_slot(
            PlayerId::Player0, product::synthetic::kToken, 3, 41, token),
        product::ErrorCode::SlotReserved);
    EXPECT(context, token == 0);
    EXPECT(context, board.summon_token_in_reserved_slot(
        PlayerId::Player0, product::synthetic::kToken, 3, 42, token));
    EXPECT(context, token != 0);
    EXPECT(context, board.player(PlayerId::Player0).main_board[3] == token);
    EXPECT(context, board.instance(token).keywords.has(product::Keyword::Ward));
    EXPECT(context, !board.reserved_by(PlayerId::Player0, 3).has_value());

    const product::MoveRecord& expiry = board.moves()[2];
    EXPECT(context, expiry.card == amulet);
    EXPECT(context, expiry.reason == product::MoveReason::CountdownExpired);
    EXPECT(context, expiry.destroyed);
    EXPECT_CAP(
        context,
        "summon_token_original_slot",
        board.player(PlayerId::Player0).main_board[3] == token &&
            board.instance(token).zone == product::Zone::MainBoard &&
            !board.reserved_by(PlayerId::Player0, 3).has_value());
    expect_valid(context, board);
}

void test_explicit_move_reasons(TestContext& context) {
    product::ProductBoard board(product::make_synthetic_product_catalog());
    const InstanceId cost = board.create_instance(
        product::synthetic::kFollower, PlayerId::Player0, product::Zone::Hand);
    EXPECT(context, board.move_to_archive(cost, product::MoveReason::AdditionalCost));
    const product::MoveRecord& archived = board.moves().back();
    EXPECT(context, archived.from == product::Zone::Hand);
    EXPECT(context, archived.to == product::Zone::Archive);
    EXPECT(context, archived.reason == product::MoveReason::AdditionalCost);
    EXPECT(context, !archived.destroyed);

    const InstanceId destroyed_amulet = board.create_instance(
        product::synthetic::kAmulet, PlayerId::Player0, product::Zone::Hand);
    EXPECT(context, board.place_main(PlayerId::Player0, destroyed_amulet, 0));
    EXPECT(context, board.move_to_graveyard(destroyed_amulet, product::MoveReason::Destroyed, true));
    const product::MoveRecord& destroyed = board.moves().back();
    EXPECT(context, destroyed.reason == product::MoveReason::Destroyed);
    EXPECT(context, destroyed.destroyed);

    const InstanceId mismatch = board.create_instance(
        product::synthetic::kFollower, PlayerId::Player0, product::Zone::Hand);
    EXPECT_CODE(
        context,
        board.move_to_graveyard(mismatch, product::MoveReason::Discarded, true),
        product::ErrorCode::InvalidZone);
    EXPECT(context, board.instance(mismatch).zone == product::Zone::Hand);
    expect_valid(context, board);
}

void test_layered_keywords_and_product_combat(TestContext& context) {
    product::KeywordState layers;
    layers.printed = product::mask(product::Keyword::Ward);
    layers.grant_permanent(product::Keyword::Barrier);
    layers.grant_for_turn(product::Keyword::Rush);
    EXPECT(context, layers.has(product::Keyword::Ward));
    EXPECT(context, layers.has(product::Keyword::Barrier));
    EXPECT(context, layers.has(product::Keyword::Rush));
    EXPECT(context, layers.consume(product::Keyword::Barrier));
    EXPECT(context, !layers.has(product::Keyword::Barrier));
    layers.clear_turn();
    EXPECT(context, !layers.has(product::Keyword::Rush));
    EXPECT(context, layers.has(product::Keyword::Ward));
    layers.grant_permanent(product::Keyword::Barrier);
    EXPECT(context, layers.has(product::Keyword::Barrier));
    EXPECT_CAP(context, "permanent_keyword_grant", layers.has(product::Keyword::Barrier));

    product::ProductBoard lifesteal_board(product::make_synthetic_product_catalog());
    lifesteal_board.player(PlayerId::Player0).leader_health = 10;
    lifesteal_board.player(PlayerId::Player1).leader_health = 10;
    const InstanceId attacker = lifesteal_board.create_instance(
        product::synthetic::kLifestealFollower, PlayerId::Player0, product::Zone::Hand);
    const InstanceId defending_lifesteal = lifesteal_board.create_instance(
        product::synthetic::kLifestealFollower, PlayerId::Player1, product::Zone::Hand);
    EXPECT(context, lifesteal_board.place_main(PlayerId::Player0, attacker, 0));
    EXPECT(context, lifesteal_board.place_main(PlayerId::Player1, defending_lifesteal, 0));
    lifesteal_board.ready_starting_turn_permanents(PlayerId::Player0);
    const product::CombatResult lifesteal = lifesteal_board.resolve_follower_combat(attacker, defending_lifesteal);
    EXPECT(context, lifesteal.damage_to_defender.actual_damage == 3);
    EXPECT(context, lifesteal.damage_to_attacker.actual_damage == 3);
    EXPECT(context, lifesteal.attacker_healed == 3);
    EXPECT(context, lifesteal_board.player(PlayerId::Player0).leader_health == 13);
    EXPECT(context, lifesteal_board.player(PlayerId::Player1).leader_health == 10);
    EXPECT(context, lifesteal_board.instance(attacker).attacked_this_turn);
    EXPECT_CAP(
        context,
        "lifesteal_active_attack_only",
        lifesteal.attacker_healed == 3 &&
            lifesteal_board.player(PlayerId::Player1).leader_health == 10);

    product::ProductBoard bane_barrier_board(product::make_synthetic_product_catalog());
    const InstanceId bane = bane_barrier_board.create_instance(
        product::synthetic::kBaneFollower, PlayerId::Player0, product::Zone::Hand);
    const InstanceId barrier = bane_barrier_board.create_instance(
        product::synthetic::kBarrierFollower, PlayerId::Player1, product::Zone::Hand);
    EXPECT(context, bane_barrier_board.place_main(PlayerId::Player0, bane, 0));
    EXPECT(context, bane_barrier_board.place_main(PlayerId::Player1, barrier, 0));
    bane_barrier_board.ready_starting_turn_permanents(PlayerId::Player0);
    const product::CombatResult blocked = bane_barrier_board.resolve_follower_combat(bane, barrier);
    EXPECT(context, blocked.damage_to_defender.actual_damage == 0);
    EXPECT(context, blocked.damage_to_defender.barrier_consumed);
    EXPECT(context, !blocked.defender_destroyed);
    EXPECT(context, bane_barrier_board.instance(barrier).current_health == 4);
    EXPECT(context, !bane_barrier_board.instance(barrier).keywords.has(product::Keyword::Barrier));
    EXPECT_CAP(
        context,
        "printed_barrier",
        blocked.damage_to_defender.actual_damage == 0 && blocked.damage_to_defender.barrier_consumed &&
            !bane_barrier_board.instance(barrier).keywords.has(product::Keyword::Barrier));
    expect_valid(context, lifesteal_board);
    expect_valid(context, bane_barrier_board);
}

void test_definition_and_deck_operation_capabilities(TestContext& context) {
    product::ProductBoard board(product::make_synthetic_product_catalog());
    EXPECT_CODE(
        context,
        board.validate_advance(product::synthetic::kNoAdvanceFollower, true),
        product::ErrorCode::InvalidChoice);
    EXPECT_CAP(
        context,
        "advance_prohibition_per_card",
        board.validate_advance(product::synthetic::kNoAdvanceFollower, false) &&
            !board.validate_advance(product::synthetic::kNoAdvanceFollower, true));

    EXPECT_CODE(
        context,
        board.validate_mode(product::synthetic::kModalSpell, std::nullopt),
        product::ErrorCode::InvalidChoice);
    EXPECT_CODE(
        context,
        board.validate_mode(product::synthetic::kModalSpell, std::string_view("missing")),
        product::ErrorCode::InvalidChoice);
    EXPECT_CAP(
        context,
        "modal_choice",
        board.validate_mode(product::synthetic::kModalSpell, std::string_view("repair")) &&
            !board.validate_mode(product::synthetic::kModalSpell, std::nullopt));

    const auto make_deck = [](product::ProductBoard& target) {
        std::array<InstanceId, 4> cards{};
        cards[0] = target.create_instance(
            product::synthetic::kOathFollower, PlayerId::Player0, product::Zone::Deck);
        cards[1] = target.create_instance(
            product::synthetic::kOathSpell, PlayerId::Player0, product::Zone::Deck);
        cards[2] = target.create_instance(
            product::synthetic::kOtherSpell, PlayerId::Player0, product::Zone::Deck);
        cards[3] = target.create_instance(
            product::synthetic::kOathSpell, PlayerId::Player0, product::Zone::Deck);
        return cards;
    };
    const std::array<InstanceId, 4> deck = make_deck(board);
    product::CardFilter oath_non_follower;
    oath_non_follower.excluded_kind = product::CardKind::Follower;
    oath_non_follower.series_id = "luminous_oath";
    const std::vector<InstanceId> candidates = board.reveal_top_matching(PlayerId::Player0, 4, oath_non_follower);
    EXPECT_CAP(
        context,
        "filtered_top_deck_search",
        candidates == std::vector<InstanceId>({deck[3], deck[1]}));

    product::ProductBoard replay(product::make_synthetic_product_catalog());
    const std::array<InstanceId, 4> replay_deck = make_deck(replay);
    const std::array<InstanceId, 2> selected = {deck[3], deck[1]};
    const std::array<InstanceId, 2> replay_selected = {replay_deck[3], replay_deck[1]};
    EXPECT(context, board.put_deck_cards_on_bottom(PlayerId::Player0, selected, true, 0x5A17U));
    EXPECT(context, replay.put_deck_cards_on_bottom(PlayerId::Player0, replay_selected, true, 0x5A17U));
    EXPECT_CAP(
        context,
        "randomized_deck_bottom",
        board.player(PlayerId::Player0).deck == replay.player(PlayerId::Player0).deck &&
            std::unordered_set<InstanceId>(
                board.player(PlayerId::Player0).deck.begin(),
                board.player(PlayerId::Player0).deck.begin() + 2) ==
                std::unordered_set<InstanceId>(selected.begin(), selected.end()));

    const InstanceId hand_bottom = board.create_instance(
        product::synthetic::kFollower, PlayerId::Player0, product::Zone::Hand);
    EXPECT(context, board.move_hand_card_to_deck_bottom(PlayerId::Player0, hand_bottom));
    EXPECT_CAP(
        context,
        "hand_to_deck_bottom",
        board.player(PlayerId::Player0).deck.front() == hand_bottom &&
            board.moves().back().reason == product::MoveReason::ReturnedToDeckBottom);

    const InstanceId discarded = board.create_instance(
        product::synthetic::kFollower, PlayerId::Player0, product::Zone::Hand);
    EXPECT(context, board.discard_from_hand(PlayerId::Player0, discarded));
    EXPECT_CAP(
        context,
        "discard_from_hand",
        board.instance(discarded).zone == product::Zone::Graveyard &&
            board.moves().back().reason == product::MoveReason::Discarded);
    expect_valid(context, board);
    expect_valid(context, replay);
}

void test_product_attack_keyword_semantics(TestContext& context) {
    product::ProductBoard board(product::make_synthetic_product_catalog());
    const InstanceId normal = board.create_instance(
        product::synthetic::kFollower, PlayerId::Player0, product::Zone::Hand);
    const InstanceId rush = board.create_instance(
        product::synthetic::kRushFollower, PlayerId::Player0, product::Zone::Hand);
    const InstanceId storm = board.create_instance(
        product::synthetic::kStormFollower, PlayerId::Player0, product::Zone::Hand);
    const InstanceId target = board.create_instance(
        product::synthetic::kFollower, PlayerId::Player1, product::Zone::Hand);
    EXPECT(context, board.place_main(PlayerId::Player0, normal, 0));
    EXPECT(context, board.place_main(PlayerId::Player0, rush, 1));
    EXPECT(context, board.place_main(PlayerId::Player0, storm, 2));
    EXPECT(context, board.place_main(PlayerId::Player1, target, 0));

    EXPECT_CODE(
        context,
        board.validate_attack(PlayerId::Player0, normal, target),
        product::ErrorCode::InvalidChoice);
    EXPECT_CODE(
        context,
        board.validate_attack(PlayerId::Player0, normal, std::nullopt),
        product::ErrorCode::InvalidChoice);
    EXPECT(context, board.validate_attack(PlayerId::Player0, rush, target));
    EXPECT_CODE(
        context,
        board.validate_attack(PlayerId::Player0, rush, std::nullopt),
        product::ErrorCode::InvalidChoice);
    EXPECT(context, board.validate_attack(PlayerId::Player0, storm, target));
    EXPECT(context, board.validate_attack(PlayerId::Player0, storm, std::nullopt));

    // An accepted declaration spends the attack before a response window. No
    // combat is resolved here, modeling a paused or cancelled declaration;
    // the attempt must nevertheless remain consumed.
    const int target_health_before = board.instance(target).current_health;
    EXPECT(context, board.accept_attack_declaration(PlayerId::Player0, rush, target));
    EXPECT(context, board.instance(rush).attacked_this_turn);
    EXPECT(context, board.instance(target).current_health == target_health_before);
    EXPECT_CODE(
        context,
        board.validate_attack(PlayerId::Player0, rush, target),
        product::ErrorCode::AlreadyAttacked);
    EXPECT(context, board.accept_attack_declaration(PlayerId::Player0, storm, std::nullopt));
    EXPECT(context, board.instance(storm).attacked_this_turn);
    EXPECT_CODE(
        context,
        board.validate_attack(PlayerId::Player0, storm, target),
        product::ErrorCode::AlreadyAttacked);

    board.ready_starting_turn_permanents(PlayerId::Player0);
    EXPECT(context, !board.instance(rush).attacked_this_turn);
    EXPECT(context, !board.instance(storm).attacked_this_turn);
    EXPECT(context, board.validate_attack(PlayerId::Player0, normal, target));
    EXPECT(context, board.validate_attack(PlayerId::Player0, normal, std::nullopt));
    EXPECT(context, board.accept_attack_declaration(PlayerId::Player0, normal, target));
    EXPECT(context, board.instance(normal).attacked_this_turn);
    EXPECT_CODE(
        context,
        board.validate_attack(PlayerId::Player0, normal, target),
        product::ErrorCode::AlreadyAttacked);
    board.ready_starting_turn_permanents(PlayerId::Player1);
    EXPECT(context, board.instance(normal).attacked_this_turn);
    board.ready_starting_turn_permanents(PlayerId::Player0);
    EXPECT(context, !board.instance(normal).attacked_this_turn);
    EXPECT(context, board.validate_attack(PlayerId::Player0, normal, target));

    product::ProductBoard ward_board(product::make_synthetic_product_catalog());
    const InstanceId ready_attacker = ward_board.create_instance(
        product::synthetic::kFollower, PlayerId::Player0, product::Zone::Hand);
    const InstanceId ward = ward_board.create_instance(
        product::synthetic::kToken, PlayerId::Player1, product::Zone::Hand);
    const InstanceId nonward = ward_board.create_instance(
        product::synthetic::kFollower, PlayerId::Player1, product::Zone::Hand);
    EXPECT(context, ward_board.place_main(PlayerId::Player0, ready_attacker, 0));
    EXPECT(context, ward_board.place_main(PlayerId::Player1, ward, 0));
    EXPECT(context, ward_board.place_main(PlayerId::Player1, nonward, 1));
    ward_board.ready_starting_turn_permanents(PlayerId::Player0);
    EXPECT(context, ward_board.validate_attack(PlayerId::Player0, ready_attacker, ward));
    EXPECT_CODE(
        context,
        ward_board.validate_attack(PlayerId::Player0, ready_attacker, nonward),
        product::ErrorCode::InvalidChoice);
    EXPECT_CODE(
        context,
        ward_board.validate_attack(PlayerId::Player0, ready_attacker, std::nullopt),
        product::ErrorCode::InvalidChoice);
    expect_valid(context, board);
    expect_valid(context, ward_board);
}

void test_permanent_target_and_standby_capabilities(TestContext& context) {
    product::ProductBoard board(product::make_synthetic_product_catalog());
    const InstanceId oath_follower = board.create_instance(
        product::synthetic::kOathFollower, PlayerId::Player0, product::Zone::Hand);
    const InstanceId oath_amulet = board.create_instance(
        product::synthetic::kOathAmulet, PlayerId::Player0, product::Zone::Hand);
    const InstanceId enemy_amulet = board.create_instance(
        product::synthetic::kAmulet, PlayerId::Player1, product::Zone::Hand);
    const InstanceId enemy_field = board.create_instance(
        product::synthetic::kFieldA, PlayerId::Player1, product::Zone::Hand);
    const InstanceId enemy_follower = board.create_instance(
        product::synthetic::kFollower, PlayerId::Player1, product::Zone::Hand);
    EXPECT(context, board.place_main(PlayerId::Player0, oath_follower, 0));
    EXPECT(context, board.place_main(PlayerId::Player0, oath_amulet, 1));
    EXPECT(context, board.place_main(PlayerId::Player1, enemy_amulet, 0));
    EXPECT(context, board.place_main(PlayerId::Player1, enemy_follower, 1));
    EXPECT(context, board.play_field(PlayerId::Player1, enemy_field));

    product::PermanentFilter destructible;
    destructible.allowed_kinds = {product::CardKind::Amulet, product::CardKind::Field};
    EXPECT(context, board.validate_permanent_target(PlayerId::Player0, enemy_amulet, false, destructible));
    EXPECT(context, board.validate_permanent_target(PlayerId::Player0, enemy_field, false, destructible));
    EXPECT_CODE(
        context,
        board.validate_permanent_target(PlayerId::Player0, enemy_follower, false, destructible),
        product::ErrorCode::InvalidKind);
    EXPECT_CAP(
        context,
        "permanent_targeting",
        board.list_permanents(PlayerId::Player1, destructible) ==
            std::vector<InstanceId>({enemy_amulet, enemy_field}));
    EXPECT(context, board.destroy_permanent(enemy_field));
    EXPECT_CAP(
        context,
        "destroy_amulet_or_field",
        board.instance(enemy_field).zone == product::Zone::Graveyard &&
            board.moves().back().reason == product::MoveReason::Destroyed && board.moves().back().destroyed);

    product::PermanentFilter oath_follower_only;
    oath_follower_only.allowed_kinds = {product::CardKind::Follower};
    oath_follower_only.series_id = "luminous_oath";
    oath_follower_only.include_field = false;
    EXPECT_CAP(
        context,
        "target_friendly_archetype_follower",
        board.list_permanents(PlayerId::Player0, oath_follower_only) ==
            std::vector<InstanceId>({oath_follower}));

    EXPECT(context, board.validate_optional_enemy_follower_target(PlayerId::Player0, std::nullopt));
    EXPECT(context, board.validate_optional_enemy_follower_target(PlayerId::Player0, enemy_follower));
    EXPECT_CAP(
        context,
        "optional_enemy_follower_target",
        board.validate_optional_enemy_follower_target(PlayerId::Player0, std::nullopt) &&
            board.validate_optional_enemy_follower_target(PlayerId::Player0, enemy_follower) &&
            !board.validate_optional_enemy_follower_target(PlayerId::Player0, enemy_amulet));

    product::ProductRuleState rules;
    rules.set_cracks(PlayerId::Player0, 4);
    const product::ConditionEvaluationContext eligible =
        rules.make_condition_context(PlayerId::Player0, board);
    EXPECT(context, board.validate_standby(product::synthetic::kStandbyFollower, eligible));
    rules.set_cracks(PlayerId::Player0, 3);
    const product::ConditionEvaluationContext ineligible =
        rules.make_condition_context(PlayerId::Player0, board);
    EXPECT_CAP(
        context,
        "standby_custom_condition",
        board.validate_standby(product::synthetic::kStandbyFollower, eligible) &&
            !board.validate_standby(product::synthetic::kStandbyFollower, ineligible));

    product::PermanentFilter additional_cost;
    additional_cost.allowed_kinds = {product::CardKind::Follower, product::CardKind::Amulet};
    additional_cost.series_id = "luminous_oath";
    additional_cost.include_field = false;
    EXPECT(context, board.pay_additional_archive_cost(PlayerId::Player0, oath_amulet, additional_cost));
    EXPECT_CAP(
        context,
        "archive_follower_or_amulet_cost",
        board.instance(oath_amulet).zone == product::Zone::Archive &&
            board.moves().back().reason == product::MoveReason::AdditionalCost &&
            board.instance(oath_follower).zone == product::Zone::MainBoard);
    expect_valid(context, board);
}

void test_rule_events_conditions_and_profession_charge(TestContext& context) {
    product::ProductBoard board(product::make_synthetic_product_catalog());
    product::ProductRuleState rules;

    const product::FutureUseEvent paid = rules.use_future(PlayerId::Player0, 2, 1);
    product::ConditionEvaluationContext after_payment =
        rules.make_condition_context(PlayerId::Player0, board, std::nullopt, paid, true);
    product::ConditionSpec debt_condition{
        product::ConditionKind::CracksAtLeast, "debt", 3, 0, {}, {}};
    EXPECT_CAP(
        context,
        "resolution_condition",
        paid.total_cracks() == 3 && product::evaluate_condition(debt_condition, after_payment));
    EXPECT_CAP(
        context,
        "future_use_trigger",
        paid.advance_cracks == 2 && paid.burn_cracks == 1 &&
            rules.turn_history(PlayerId::Player0).future_cracks_added == 3);

    rules.set_cracks(PlayerId::Player0, 7);
    after_payment = rules.make_condition_context(PlayerId::Player0, board);
    product::ConditionSpec threshold{
        product::ConditionKind::CracksAtLeast, "negative_contract", 4, 0, {}, {}};
    EXPECT_CAP(context, "dynamic_crack_threshold", product::evaluate_condition(threshold, after_payment));
    product::ConditionSpec capped{
        product::ConditionKind::CracksAtLeast, "capped_cracks", 6, 5, {}, {}};
    EXPECT_CAP(
        context,
        "crack_scaling_cap_five",
        rules.cracks_capped(PlayerId::Player0) == 5 && !product::evaluate_condition(capped, after_payment));

    const product::RepairResult partial = rules.repair(PlayerId::Player0, 2);
    const product::RepairResult cleared = rules.repair(PlayerId::Player0, 99);
    EXPECT_CAP(
        context,
        "actual_repair_amount",
        partial.before == 7 && partial.after == 5 && partial.actual_repaired == 2);
    EXPECT_CAP(
        context,
        "repair_to_zero_trigger",
        cleared.actual_repaired == 5 && cleared.repaired_to_zero && cleared.after == 0);

    rules.record_barrier_granted(PlayerId::Player0);
    rules.record_countdown_expired(PlayerId::Player0);
    EXPECT_CAP(
        context,
        "turn_history",
        rules.turn_history(PlayerId::Player0).actual_repaired == 7 &&
            rules.turn_history(PlayerId::Player0).future_cracks_added == 3 &&
            rules.turn_history(PlayerId::Player0).barrier_granted &&
            rules.turn_history(PlayerId::Player0).countdown_expired == 1);
    const int match_repairs = rules.match_history(PlayerId::Player0).repair_to_zero_count;
    rules.begin_owner_turn(PlayerId::Player0);
    EXPECT_CAP(
        context,
        "match_history",
        match_repairs == 1 && rules.match_history(PlayerId::Player0).repair_to_zero_count == 1 &&
            rules.match_history(PlayerId::Player0).countdown_expired == 1 &&
            rules.turn_history(PlayerId::Player0).actual_repaired == 0);
    const bool first_once = rules.consume_once_per_owner_turn(PlayerId::Player0, "listener-a");
    const bool second_once = rules.consume_once_per_owner_turn(PlayerId::Player0, "listener-a");
    rules.begin_owner_turn(PlayerId::Player0);
    const bool reset_once = rules.consume_once_per_owner_turn(PlayerId::Player0, "listener-a");
    EXPECT_CAP(context, "once_per_owner_turn_trigger", first_once && !second_once && reset_once);

    (void)rules.use_future(PlayerId::Player1, 1, 0);
    const product::ProductListenerToken listener = rules.arm_listener(
        PlayerId::Player1, product::ProductRuleEvent::Kind::FutureUse);
    const product::FutureUseEvent visible = rules.use_future(PlayerId::Player1, 0, 2);
    const std::vector<product::ProductRuleEvent> observed = rules.events_observed_by(listener);
    EXPECT_CAP(
        context,
        "no_retroactive_self_trigger",
        observed.size() == 1 && observed.front().sequence == visible.sequence && observed.front().amount == 2);

    product::ProductRuleState evolution;
    evolution.configure_evolution_charge(PlayerId::Player0, product::EvolutionChargePolicy::RepairToZero);
    evolution.set_cracks(PlayerId::Player0, 1);
    (void)evolution.repair(PlayerId::Player0, 1);
    const bool locked_did_not_charge = evolution.evolution_energy(PlayerId::Player0) == 0;
    evolution.set_evolution_unlocked(PlayerId::Player0, true);
    evolution.begin_owner_turn(PlayerId::Player0);
    evolution.set_cracks(PlayerId::Player0, 1);
    (void)evolution.repair(PlayerId::Player0, 1);
    evolution.set_cracks(PlayerId::Player0, 1);
    (void)evolution.repair(PlayerId::Player0, 1);
    const bool oath_once = evolution.evolution_energy(PlayerId::Player0) == 1;

    evolution.configure_evolution_charge(
        PlayerId::Player1, product::EvolutionChargePolicy::FutureUseAtLeastTwo);
    evolution.set_evolution_unlocked(PlayerId::Player1, true);
    evolution.begin_owner_turn(PlayerId::Player1);
    (void)evolution.use_future(PlayerId::Player1, 1, 0);
    (void)evolution.use_future(PlayerId::Player1, 1, 1);
    (void)evolution.use_future(PlayerId::Player1, 0, 3);
    const bool pact_once = evolution.evolution_energy(PlayerId::Player1) == 1;
    evolution.begin_owner_turn(PlayerId::Player1);
    (void)evolution.use_future(PlayerId::Player1, 2, 0);
    EXPECT_CAP(
        context,
        "profession_evolution_charge",
        locked_did_not_charge && oath_once && pact_once &&
            evolution.evolution_energy(PlayerId::Player1) == 2);
}

void test_stat_combat_and_conditional_effect_capabilities(TestContext& context) {
    product::ProductBoard board(product::make_synthetic_product_catalog());
    const InstanceId temporary = board.create_instance(
        product::synthetic::kFollower, PlayerId::Player0, product::Zone::Hand);
    EXPECT(context, board.place_main(PlayerId::Player0, temporary, 0));
    EXPECT(context, board.grant_temporary_attack(temporary, 3));
    const bool temporary_applied = board.instance(temporary).current_attack == 5;
    board.clear_turn_keyword_grants(PlayerId::Player0);
    EXPECT_CAP(
        context,
        "temporary_attack_buff",
        temporary_applied && board.instance(temporary).current_attack == 2 &&
            board.instance(temporary).turn_attack_bonus == 0);

    const InstanceId permanent = board.create_instance(
        product::synthetic::kFollower, PlayerId::Player0, product::Zone::Hand);
    EXPECT(context, board.place_main(PlayerId::Player0, permanent, 1));
    EXPECT(context, board.damage_follower(permanent, 1).actual_damage == 1);
    EXPECT(context, board.grant_permanent_stats(permanent, 2, 2));
    EXPECT_CAP(
        context,
        "permanent_stat_buff",
        board.instance(permanent).current_attack == 4 &&
            board.instance(permanent).maximum_health == 4 &&
            board.instance(permanent).current_health == 3 &&
            board.instance(permanent).permanent_attack_bonus == 2);

    product::ProductRuleState rules;
    rules.set_cracks(PlayerId::Player0, 4);
    const product::ConditionEvaluationContext context_at_four =
        rules.make_condition_context(PlayerId::Player0, board);
    const product::ConditionSpec at_four{
        product::ConditionKind::CracksAtLeast, "at_four", 4, 0, {}, {}};

    const InstanceId draw_card = board.create_instance(
        product::synthetic::kSpell, PlayerId::Player0, product::Zone::Deck);
    const std::size_t hand_before = board.player(PlayerId::Player0).hand.size();
    product::DrawResult drawn;
    if (product::evaluate_condition(at_four, context_at_four)) {
        drawn = board.draw_one(PlayerId::Player0);
    }
    EXPECT_CAP(
        context,
        "conditional_draw",
        drawn.card == draw_card && drawn.entered_hand &&
            board.player(PlayerId::Player0).hand.size() == hand_before + 1U);

    board.player(PlayerId::Player0).leader_health = 10;
    int healed = 0;
    if (product::evaluate_condition(at_four, context_at_four)) {
        healed = board.heal_leader(PlayerId::Player0, 3);
    }
    EXPECT_CAP(
        context,
        "conditional_heal",
        healed == 3 && board.player(PlayerId::Player0).leader_health == 13);

    const InstanceId damaged = board.create_instance(
        product::synthetic::kBarrierFollower, PlayerId::Player1, product::Zone::Hand);
    EXPECT(context, board.place_main(PlayerId::Player1, damaged, 0));
    product::DamageResult first_damage;
    product::DamageResult second_damage;
    if (product::evaluate_condition(at_four, context_at_four)) {
        first_damage = board.damage_follower(damaged, 2);
        second_damage = board.damage_follower(damaged, 2);
    }
    EXPECT_CAP(
        context,
        "conditional_damage",
        first_damage.barrier_consumed && first_damage.actual_damage == 0 &&
            second_damage.actual_damage == 2 && board.instance(damaged).current_health == 2);

    const InstanceId countdown = board.create_instance(
        product::synthetic::kOathAmulet, PlayerId::Player0, product::Zone::Hand);
    EXPECT(context, board.place_main(PlayerId::Player0, countdown, 2));
    if (product::evaluate_condition(at_four, context_at_four)) {
        EXPECT(context, board.change_countdown(countdown, -1));
    }
    EXPECT_CAP(
        context,
        "conditional_countdown_change",
        board.instance(countdown).countdown == 1);

    product::ProductBoard combat(product::make_synthetic_product_catalog());
    const InstanceId attacker = combat.create_instance(
        product::synthetic::kOathFollower, PlayerId::Player0, product::Zone::Hand);
    const InstanceId defender = combat.create_instance(
        product::synthetic::kFollower, PlayerId::Player1, product::Zone::Hand);
    EXPECT(context, combat.place_main(PlayerId::Player0, attacker, 0));
    EXPECT(context, combat.place_main(PlayerId::Player1, defender, 0));
    combat.ready_starting_turn_permanents(PlayerId::Player0);
    const product::CombatResult combat_result = combat.resolve_follower_combat(attacker, defender);
    EXPECT_CAP(
        context,
        "combat_kill_survive_trigger",
        combat_result.attacker_killed_follower_and_survived && combat_result.defender_destroyed &&
            !combat_result.attacker_destroyed && combat.instance(attacker).zone == product::Zone::MainBoard);
    expect_valid(context, board);
    expect_valid(context, combat);
}

void test_board_predicates_and_draw_transaction(TestContext& context) {
    product::ProductBoard board(product::make_synthetic_product_catalog());
    const InstanceId own = board.create_instance(
        product::synthetic::kFollower, PlayerId::Player0, product::Zone::Hand);
    const InstanceId enemy_a = board.create_instance(
        product::synthetic::kFollower, PlayerId::Player1, product::Zone::Hand);
    const InstanceId enemy_b = board.create_instance(
        product::synthetic::kAmulet, PlayerId::Player1, product::Zone::Hand);
    const InstanceId field = board.create_instance(
        product::synthetic::kFieldA, PlayerId::Player0, product::Zone::Hand);
    EXPECT(context, board.place_main(PlayerId::Player0, own, 0));
    EXPECT(context, board.place_main(PlayerId::Player1, enemy_a, 0));
    EXPECT(context, board.place_main(PlayerId::Player1, enemy_b, 1));
    EXPECT(context, board.play_field(PlayerId::Player0, field));
    product::ProductRuleState rules;
    const product::ConditionEvaluationContext predicates =
        rules.make_condition_context(PlayerId::Player0, board);
    const product::ConditionSpec less_board{
        product::ConditionKind::BoardCountLessThanOpponent, "less_board", 0, 0, {}, {}};
    const product::ConditionSpec field_identity{
        product::ConditionKind::FieldIs,
        "field_identity",
        0,
        0,
        std::string(product::synthetic::kFieldA),
        {},
    };
    EXPECT_CAP(
        context,
        "board_card_count_comparison",
        board.main_board_count(PlayerId::Player0) == 1 &&
            board.main_board_count(PlayerId::Player1) == 2 && product::evaluate_condition(less_board, predicates));
    EXPECT_CAP(
        context,
        "field_identity_check",
        board.field_is(PlayerId::Player0, product::synthetic::kFieldA) &&
            product::evaluate_condition(field_identity, predicates));

    const InstanceId prior_hand = board.create_instance(
        product::synthetic::kFollower, PlayerId::Player0, product::Zone::Hand);
    const InstanceId deck_card = board.create_instance(
        product::synthetic::kSpell, PlayerId::Player0, product::Zone::Deck);
    const product::DrawThenBottomResult successful =
        board.draw_then_prepare_bottom(PlayerId::Player0);

    product::ProductBoard overflow(product::make_synthetic_product_catalog());
    for (std::size_t index = 0; index < 9U; ++index) {
        (void)overflow.create_instance(
            product::synthetic::kFollower, PlayerId::Player0, product::Zone::Hand);
    }
    const InstanceId overflow_card = overflow.create_instance(
        product::synthetic::kSpell, PlayerId::Player0, product::Zone::Deck);
    const product::DrawThenBottomResult failed =
        overflow.draw_then_prepare_bottom(PlayerId::Player0);
    EXPECT_CAP(
        context,
        "draw_then_bottom_if_draw_succeeds",
        successful.draw.card == deck_card && successful.requires_bottom_choice() &&
            std::find(successful.bottom_candidates.begin(), successful.bottom_candidates.end(), prior_hand) !=
                successful.bottom_candidates.end() &&
            !failed.draw.entered_hand && !failed.requires_bottom_choice() &&
            overflow.instance(overflow_card).zone == product::Zone::Archive);
    expect_valid(context, board);
    expect_valid(context, overflow);
}

void test_pending_choice_blocks_and_resumes(TestContext& context) {
    product::ResolutionQueue queue;
    queue.enqueue(product::ResolutionFrame{
        16,
        PlayerId::Player0,
        899,
        "earlier_unrelated_frame",
        product::ResolutionFrameKind::GlobalTrigger,
    });
    queue.enqueue(product::ResolutionFrame{
        17,
        PlayerId::Player0,
        900,
        "response_choose_target",
        product::ResolutionFrameKind::ResponseEffect,
    });
    queue.enqueue(product::ResolutionFrame{
        18,
        PlayerId::Player1,
        901,
        "later_unrelated_frame",
        product::ResolutionFrameKind::Continuation,
    });
    product::PendingChoice choice;
    choice.choice_id = 71;
    choice.chooser = PlayerId::Player0;
    choice.kind = product::ChoiceKind::Cards;
    choice.suspended_frame_id = 17;
    choice.minimum = 1;
    choice.maximum = 2;
    choice.ordered = true;
    choice.options = {{"opt-a", 101}, {"opt-b", 102}, {"opt-c", 103}};
    EXPECT(context, queue.suspend_for_choice(choice));
    EXPECT(context, queue.input_blocked());
    EXPECT(context, !queue.permits(product::ActionKind::EndTurn));
    EXPECT(context, queue.permits(product::ActionKind::ResolveChoice));
    EXPECT(context, queue.permits(product::ActionKind::Surrender));
    EXPECT(context, !queue.pop_ready_frame().has_value());

    const std::uint64_t before = queue.revision();
    const std::array<std::string, 1> one = {"opt-a"};
    EXPECT_CODE(
        context,
        queue.resolve_choice(PlayerId::Player1, 71, one),
        product::ErrorCode::NotChoiceOwner);
    const std::array<std::string, 2> duplicate = {"opt-a", "opt-a"};
    EXPECT_CODE(
        context,
        queue.resolve_choice(PlayerId::Player0, 71, duplicate),
        product::ErrorCode::DuplicateSelection);
    const std::array<std::string, 1> unknown = {"not-an-option"};
    EXPECT_CODE(
        context,
        queue.resolve_choice(PlayerId::Player0, 71, unknown),
        product::ErrorCode::InvalidChoice);
    EXPECT(context, queue.revision() == before);
    EXPECT(context, queue.pending_choice().has_value());

    const std::array<std::string, 2> selected = {"opt-c", "opt-a"};
    EXPECT(context, queue.resolve_choice(PlayerId::Player0, 71, selected));
    EXPECT(context, queue.revision() == before + 1);
    EXPECT(context, !queue.input_blocked());
    const std::optional<product::ChoiceResolution> resolved = queue.take_resolved_choice();
    EXPECT(context, resolved.has_value());
    EXPECT(context, resolved->suspended_frame_id == 17);
    EXPECT(context, resolved->selected_option_ids == std::vector<std::string>({"opt-c", "opt-a"}));
    const std::optional<product::ResolutionFrame> frame = queue.pop_ready_frame();
    EXPECT(context, frame.has_value() && frame->frame_id == 17);
    EXPECT(context, frame->kind == product::ResolutionFrameKind::ResponseEffect);
    const std::optional<product::ResolutionFrame> earlier = queue.pop_ready_frame();
    const std::optional<product::ResolutionFrame> later = queue.pop_ready_frame();
    EXPECT(context, earlier.has_value() && earlier->frame_id == 16);
    EXPECT(context, later.has_value() && later->frame_id == 18);

    product::PendingChoice unknown_frame = choice;
    unknown_frame.choice_id = 72;
    unknown_frame.suspended_frame_id = 999;
    EXPECT_CODE(
        context,
        queue.suspend_for_choice(std::move(unknown_frame)),
        product::ErrorCode::InvalidChoice);
    EXPECT(context, !queue.input_blocked());
}

void test_entry_pending_window_and_response_order(TestContext& context) {
    product::ResolutionQueue queue;
    queue.enqueue_entry_pending(
        product::ResolutionFrame{
            81,
            PlayerId::Player0,
            501,
            "entry_effect_pending",
            product::ResolutionFrameKind::EntryEffectPending,
        },
        product::ResolutionFrame{
            82,
            PlayerId::Player0,
            501,
            "resolve_entry_effect",
            product::ResolutionFrameKind::Continuation,
        });
    const std::optional<product::ResolutionFrame> window = queue.pop_ready_frame();
    EXPECT(context, window.has_value() && window->frame_id == 81);
    EXPECT(context, window->kind == product::ResolutionFrameKind::EntryEffectPending);

    queue.enqueue_response(product::ResolutionFrame{
        83,
        PlayerId::Player1,
        777,
        "respond_to_entry",
        product::ResolutionFrameKind::ResponseEffect,
    });
    const std::optional<product::ResolutionFrame> response = queue.pop_ready_frame();
    const std::optional<product::ResolutionFrame> continuation = queue.pop_ready_frame();
    EXPECT(context, response.has_value() && response->frame_id == 83);
    EXPECT(context, response->kind == product::ResolutionFrameKind::ResponseEffect);
    EXPECT(context, continuation.has_value() && continuation->frame_id == 82);
    EXPECT(context, continuation->kind == product::ResolutionFrameKind::Continuation);
}

void test_terminal_resolution_cleanup_is_idempotent(TestContext& context) {
    product::ResolutionQueue queue;
    queue.enqueue(product::ResolutionFrame{
        18,
        PlayerId::Player1,
        901,
        "terminal_must_not_run",
        product::ResolutionFrameKind::Continuation,
    });
    product::PendingChoice choice;
    choice.choice_id = 73;
    choice.chooser = PlayerId::Player1;
    choice.kind = product::ChoiceKind::AdditionalCost;
    choice.suspended_frame_id = 18;
    choice.minimum = 1;
    choice.maximum = 1;
    choice.options = {{"archive-card", 404}};
    EXPECT(context, queue.suspend_for_choice(choice));

    const std::uint64_t before_finish = queue.revision();
    queue.finish_match();
    EXPECT(context, queue.finished());
    EXPECT(context, queue.revision() == before_finish + 1);
    EXPECT(context, queue.frame_count() == 0);
    EXPECT(context, !queue.pending_choice().has_value());
    EXPECT(context, !queue.take_resolved_choice().has_value());
    EXPECT(context, !queue.pop_ready_frame().has_value());
    EXPECT(context, !queue.permits(product::ActionKind::ResolveChoice));
    EXPECT(context, !queue.permits(product::ActionKind::Surrender));
    const std::array<std::string, 1> selection = {"archive-card"};
    EXPECT_CODE(
        context,
        queue.resolve_choice(PlayerId::Player1, 73, selection),
        product::ErrorCode::ResolutionFinished);
    EXPECT_CODE(
        context,
        queue.suspend_for_choice(std::move(choice)),
        product::ErrorCode::ResolutionFinished);

    queue.finish_match();
    EXPECT(context, queue.revision() == before_finish + 1);

    bool enqueue_rejected = false;
    try {
        queue.enqueue(product::ResolutionFrame{19, PlayerId::Player0, 0, "after_terminal"});
    } catch (const std::logic_error&) {
        enqueue_rejected = true;
    }
    EXPECT(context, enqueue_rejected);
}

void test_deterministic_manual_trigger_order(TestContext& context) {
    std::vector<product::TriggeredAbility> triggers = {
        {"p0-second", PlayerId::Player0, 20, 2, ""},
        {"p1-first", PlayerId::Player1, 11, 1, ""},
        {"p0-first", PlayerId::Player0, 10, 1, ""},
        {"p1-second", PlayerId::Player1, 21, 2, ""},
    };
    product::TriggerOrderPlanner planner(PlayerId::Player1, std::move(triggers));
    EXPECT(context, !planner.complete());
    EXPECT(context, planner.pending_choice().has_value());
    EXPECT(context, planner.pending_choice()->chooser == PlayerId::Player1);
    const product::ChoiceId first_choice = planner.pending_choice()->choice_id;
    const std::array<std::string, 2> active_order = {"p1-second", "p1-first"};
    EXPECT(context, planner.resolve_order(PlayerId::Player1, first_choice, active_order));
    EXPECT(context, planner.pending_choice().has_value());
    EXPECT(context, planner.pending_choice()->chooser == PlayerId::Player0);

    const product::ChoiceId second_choice = planner.pending_choice()->choice_id;
    const std::array<std::string, 2> duplicate = {"p0-first", "p0-first"};
    EXPECT_CODE(
        context,
        planner.resolve_order(PlayerId::Player0, second_choice, duplicate),
        product::ErrorCode::DuplicateSelection);
    EXPECT(context, planner.pending_choice().has_value());
    const std::array<std::string, 2> nonactive_order = {"p0-second", "p0-first"};
    EXPECT(context, planner.resolve_order(PlayerId::Player0, second_choice, nonactive_order));
    EXPECT(context, planner.complete());
    const auto& ordered = planner.ordered_triggers();
    EXPECT(context, ordered.size() == 4);
    EXPECT(context, ordered[0].trigger_id == "p1-second");
    EXPECT(context, ordered[1].trigger_id == "p1-first");
    EXPECT(context, ordered[2].trigger_id == "p0-second");
    EXPECT(context, ordered[3].trigger_id == "p0-first");

    std::vector<product::TriggeredAbility> equivalent = {
        {"same-b", PlayerId::Player0, 2, 2, "draw-one"},
        {"same-a", PlayerId::Player0, 1, 1, "draw-one"},
    };
    product::TriggerOrderPlanner automatic(PlayerId::Player0, std::move(equivalent));
    EXPECT(context, automatic.complete());
    EXPECT(context, !automatic.pending_choice().has_value());
    EXPECT(context, automatic.ordered_triggers()[0].trigger_id == "same-a");
    EXPECT(context, automatic.ordered_triggers()[1].trigger_id == "same-b");

    bool duplicate_rejected = false;
    try {
        std::vector<product::TriggeredAbility> duplicate_ids = {
            {"same-id", PlayerId::Player0, 1, 1, ""},
            {"same-id", PlayerId::Player1, 2, 1, ""},
        };
        (void)product::TriggerOrderPlanner(PlayerId::Player0, std::move(duplicate_ids));
    } catch (const std::invalid_argument&) {
        duplicate_rejected = true;
    }
    EXPECT(context, duplicate_rejected);
}

void test_exact_executable_capability_registry(TestContext& context) {
    g_capabilities->verify(context);
}

struct TestCase {
    std::string_view name;
    void (*function)(TestContext&);
};

} // namespace

int main() {
    const std::vector<TestCase> tests = {
        {"schema_two_frozen_domain", test_schema_two_frozen_domain},
        {"generated_locked_product_catalog", test_generated_locked_product_catalog},
        {"locked_runtime_shape_and_execution_gate", test_locked_runtime_shape_and_execution_gate},
        {"mixed_main_board_and_permanent_rules", test_mixed_main_board_and_permanent_rules},
        {"independent_field_replacement", test_independent_field_replacement},
        {"countdown_reserves_original_token_slot", test_countdown_reserves_original_token_slot},
        {"explicit_move_reasons", test_explicit_move_reasons},
        {"layered_keywords_and_product_combat", test_layered_keywords_and_product_combat},
        {"definition_and_deck_operation_capabilities", test_definition_and_deck_operation_capabilities},
        {"product_attack_keyword_semantics", test_product_attack_keyword_semantics},
        {"permanent_target_and_standby_capabilities", test_permanent_target_and_standby_capabilities},
        {"rule_events_conditions_and_profession_charge", test_rule_events_conditions_and_profession_charge},
        {"stat_combat_and_conditional_effect_capabilities", test_stat_combat_and_conditional_effect_capabilities},
        {"board_predicates_and_draw_transaction", test_board_predicates_and_draw_transaction},
        {"pending_choice_blocks_and_resumes", test_pending_choice_blocks_and_resumes},
        {"entry_pending_window_and_response_order", test_entry_pending_window_and_response_order},
        {"terminal_resolution_cleanup_is_idempotent", test_terminal_resolution_cleanup_is_idempotent},
        {"deterministic_manual_trigger_order", test_deterministic_manual_trigger_order},
        {"exact_executable_capability_registry", test_exact_executable_capability_registry},
    };

    TestContext context;
    CapabilityCoverage capability_coverage;
    g_capabilities = &capability_coverage;
    for (const TestCase& test : tests) {
        try {
            test.function(context);
        } catch (const std::exception& exception) {
            ++context.failures;
            std::cerr << "test threw: " << test.name << ": " << exception.what() << '\n';
        }
    }
    g_capabilities = nullptr;
    std::cout << tests.size() << " product runtime test cases\n"
              << context.assertions << " assertions\n"
              << context.failures << " failures\n";
    return context.failures == 0 ? EXIT_SUCCESS : EXIT_FAILURE;
}
