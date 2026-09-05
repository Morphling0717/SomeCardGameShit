// SPDX-License-Identifier: GPL-3.0-or-later
#include "scgs/product_game.hpp"

#include <algorithm>
#include <cstdlib>
#include <exception>
#include <iostream>
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

    void expect(const bool value, const char* expression, const char* file, const int line) {
        ++assertions;
        if (!value) {
            ++failures;
            std::cerr << file << ':' << line << ": expectation failed: " << expression << '\n';
        }
    }
};

#define EXPECT(ctx, expression) (ctx).expect(static_cast<bool>(expression), #expression, __FILE__, __LINE__)
#define EXPECT_GAME_CODE(ctx, status, expected) \
    (ctx).expect((status).code == (expected), #status ".code == " #expected, __FILE__, __LINE__)

constexpr std::string_view kFollower = "GAME-FOLLOWER";
constexpr std::string_view kStorm = "GAME-STORM";
constexpr std::string_view kAdvance = "GAME-ADVANCE";
constexpr std::string_view kSpell = "GAME-SPELL";
constexpr std::string_view kChoiceSpell = "GAME-CHOICE";
constexpr std::string_view kMultiChoiceSpell = "GAME-MULTI-CHOICE";
constexpr std::string_view kRepairSpell = "GAME-REPAIR";
constexpr std::string_view kTrap = "GAME-TRAP";
constexpr std::string_view kAmulet = "GAME-AMULET";
constexpr std::string_view kField = "GAME-FIELD";
constexpr std::string_view kToken = "GAME-TOKEN";
constexpr std::string_view kStandby = "GAME-STANDBY";
constexpr std::string_view kLastWords = "GAME-LAST-WORDS";
constexpr std::string_view kTriggerA = "GAME-TRIGGER-A";
constexpr std::string_view kTriggerB = "GAME-TRIGGER-B";
constexpr std::string_view kOptionalTarget = "GAME-OPTIONAL-TARGET";
constexpr std::string_view kGlobalTargetField = "GAME-GLOBAL-TARGET-FIELD";
constexpr std::string_view kArchiveStandby = "GAME-ARCHIVE-STANDBY";

product::CardDefinition make_card(
    const std::string_view id,
    const product::CardKind kind,
    const int cost = 1) {
    product::CardDefinition card;
    card.identity = product::CardIdentity{std::string(id), "fixture", "game", false};
    card.name = std::string(id);
    card.kind = kind;
    card.cost = cost;
    card.implementation_status = product::CardImplementationStatus::SyntheticFixture;
    card.effects_compiled = true;
    card.canonical_rules_text = std::string(id);
    if (kind == product::CardKind::Follower) {
        card.attack = 2;
        card.health = 2;
    }
    return card;
}

product::CardCatalog make_game_catalog() {
    product::CardCatalog catalog;
    catalog.add(make_card(kFollower, product::CardKind::Follower));

    product::CardDefinition last_words = make_card(kLastWords, product::CardKind::Follower);
    last_words.attack = 1;
    last_words.health = 1;
    product::EffectSpec last_words_draw;
    last_words_draw.trigger = product::EffectTrigger::OnLastWords;
    last_words_draw.kind = product::EffectKind::Draw;
    last_words_draw.amount = 1;
    last_words_draw.effect_id = "last-words-draw";
    last_words.effects.push_back(std::move(last_words_draw));
    catalog.add(std::move(last_words));

    for (const auto [id, effect_id] : {
             std::pair{kTriggerA, std::string_view("trigger-a")},
             std::pair{kTriggerB, std::string_view("trigger-b")},
         }) {
        product::CardDefinition trigger = make_card(id, product::CardKind::Follower, 0);
        product::EffectSpec barrier;
        barrier.trigger = product::EffectTrigger::OnActualRepair;
        barrier.kind = product::EffectKind::GrantKeyword;
        barrier.target = product::TargetSpec::Self;
        barrier.granted_keyword = product::Keyword::Barrier;
        barrier.duration = product::EffectDuration::Permanent;
        barrier.effect_id = std::string(effect_id);
        barrier.trigger_player_relation = product::TriggerPlayerRelation::SourceController;
        trigger.effects.push_back(std::move(barrier));
        catalog.add(std::move(trigger));
    }

    product::CardDefinition storm = make_card(kStorm, product::CardKind::Follower);
    storm.attack = 3;
    storm.health = 3;
    storm.printed_keywords = product::mask(product::Keyword::Storm);
    catalog.add(std::move(storm));

    product::CardDefinition optional_target = make_card(kOptionalTarget, product::CardKind::Follower, 0);
    product::EffectSpec optional_damage;
    optional_damage.trigger = product::EffectTrigger::OnEntry;
    optional_damage.kind = product::EffectKind::DamageFollower;
    optional_damage.amount = 1;
    optional_damage.target = product::TargetSpec::EnemyFollower;
    optional_damage.optional = true;
    optional_damage.effect_id = "optional-entry-damage";
    optional_target.effects.push_back(std::move(optional_damage));
    catalog.add(std::move(optional_target));

    product::CardDefinition global_target_field = make_card(
        kGlobalTargetField, product::CardKind::Field, 0);
    product::EffectSpec global_buff;
    global_buff.trigger = product::EffectTrigger::OnActualRepair;
    global_buff.kind = product::EffectKind::ModifyStats;
    global_buff.amount = 1;
    global_buff.target = product::TargetSpec::FriendlyFollower;
    global_buff.effect_id = "later-repair-target";
    global_target_field.effects.push_back(std::move(global_buff));
    catalog.add(std::move(global_target_field));

    product::CardDefinition advance = make_card(kAdvance, product::CardKind::Follower, 2);
    catalog.add(std::move(advance));

    product::CardDefinition spell = make_card(kSpell, product::CardKind::Spell);
    product::EffectSpec damage;
    damage.trigger = product::EffectTrigger::OnPlay;
    damage.kind = product::EffectKind::DamageFollower;
    damage.amount = 3;
    damage.target = product::TargetSpec::EnemyFollower;
    damage.effect_id = "damage";
    damage.selection_minimum = 1;
    damage.selection_maximum = 1;
    spell.effects.push_back(std::move(damage));
    catalog.add(std::move(spell));

    product::CardDefinition choice = make_card(kChoiceSpell, product::CardKind::Spell);
    product::EffectSpec bottom;
    bottom.trigger = product::EffectTrigger::OnPlay;
    bottom.kind = product::EffectKind::PutOnDeckBottom;
    bottom.effect_id = "bottom";
    bottom.selection_minimum = 1;
    bottom.selection_maximum = 1;
    choice.effects.push_back(std::move(bottom));
    catalog.add(std::move(choice));

    product::CardDefinition multi_choice = make_card(kMultiChoiceSpell, product::CardKind::Spell, 0);
    product::EffectSpec multi_bottom;
    multi_bottom.trigger = product::EffectTrigger::OnPlay;
    multi_bottom.kind = product::EffectKind::PutOnDeckBottom;
    multi_bottom.effect_id = "multi-bottom";
    multi_bottom.selection_minimum = 0;
    multi_bottom.selection_maximum = 2;
    multi_choice.effects.push_back(std::move(multi_bottom));
    catalog.add(std::move(multi_choice));

    product::CardDefinition repair = make_card(kRepairSpell, product::CardKind::Spell);
    product::EffectSpec repair_effect;
    repair_effect.trigger = product::EffectTrigger::OnPlay;
    repair_effect.kind = product::EffectKind::RepairCracks;
    repair_effect.amount = 2;
    repair_effect.effect_id = "repair";
    repair.effects.push_back(std::move(repair_effect));
    catalog.add(std::move(repair));

    product::CardDefinition trap = make_card(kTrap, product::CardKind::Trap);
    trap.can_advance = false;
    product::EffectSpec cancel;
    cancel.trigger = product::EffectTrigger::OnAttackDeclared;
    cancel.kind = product::EffectKind::CancelAttack;
    cancel.effect_id = "cancel";
    trap.effects.push_back(std::move(cancel));
    catalog.add(std::move(trap));

    product::CardDefinition token = make_card(kToken, product::CardKind::Follower, 0);
    token.availability = product::CardAvailability::Token;
    token.can_advance = false;
    token.attack = 3;
    token.health = 3;
    catalog.add(std::move(token));

    product::CardDefinition amulet = make_card(kAmulet, product::CardKind::Amulet);
    amulet.can_advance = false;
    amulet.countdown = 1;
    product::EffectSpec summon;
    summon.trigger = product::EffectTrigger::OnCountdownEnd;
    summon.kind = product::EffectKind::SummonToken;
    summon.parameter = std::string(kToken);
    summon.effect_id = "summon";
    summon.preserve_source_slot = true;
    amulet.effects.push_back(std::move(summon));
    catalog.add(std::move(amulet));

    catalog.add(make_card(kField, product::CardKind::Field));

    product::CardDefinition standby = make_card(kStandby, product::CardKind::Follower, 0);
    standby.availability = product::CardAvailability::Standby;
    standby.can_advance = false;
    standby.attack = 4;
    standby.health = 4;
    product::StandbySpec standby_spec;
    standby_spec.pp_cost = 1;
    product::ConditionSpec always;
    always.kind = product::ConditionKind::Always;
    always.condition_id = "always";
    standby_spec.conditions.push_back(std::move(always));
    standby.standby = std::move(standby_spec);
    catalog.add(std::move(standby));

    product::CardDefinition archive_standby = make_card(kArchiveStandby, product::CardKind::Follower, 0);
    archive_standby.availability = product::CardAvailability::Standby;
    archive_standby.can_advance = false;
    archive_standby.attack = 6;
    archive_standby.health = 6;
    product::StandbySpec archive_spec;
    archive_spec.pp_cost = 1;
    archive_spec.conditions.push_back(always);
    archive_spec.requires_additional_cost = true;
    archive_spec.additional_cost_target = product::TargetSpec::FriendlyPermanent;
    archive_spec.additional_cost_filter.allowed_kinds = {product::CardKind::Follower};
    archive_spec.additional_cost_filter.include_field = false;
    archive_spec.additional_cost_minimum = 1;
    archive_spec.additional_cost_maximum = 1;
    archive_standby.standby = std::move(archive_spec);
    catalog.add(std::move(archive_standby));
    return catalog;
}

product::ProductGameConfig make_config() {
    product::ProductGameConfig config;
    // ProductBoard draws from the vector back. Keep all interaction fixtures
    // in the opening four while retaining enough cards for turn tests.
    const std::vector<product::DesignId> deck = {
        std::string(kFollower), std::string(kStorm), std::string(kAmulet), std::string(kField),
        std::string(kRepairSpell), std::string(kSpell), std::string(kFollower), std::string(kStorm),
        std::string(kChoiceSpell), std::string(kAdvance), std::string(kTrap), std::string(kFollower),
    };
    config.main_decks = {deck, deck};
    config.standby_decks[0] = {std::string(kStandby)};
    config.standby_decks[1] = {std::string(kStandby)};
    config.professions = {"fixture", "fixture"};
    config.first_player_mode = FirstPlayerMode::Player0;
    config.seed = 0x5C6A2026U;
    config.shuffle = false;
    config.required_main_deck_size = deck.size();
    config.required_standby_size = 1;
    config.starting_hand_size = 4;
    return config;
}

product::ProductGameCommand command(
    const product::ProductGame& game,
    const PlayerId player,
    const product::ActionKind action) {
    product::ProductGameCommand result;
    result.player = player;
    result.action = action;
    result.expected_revision = game.revision();
    return result;
}

InstanceId find_card(
    const product::ProductGame& game,
    const PlayerId player,
    const product::Zone zone,
    const std::string_view design_id) {
    const product::PlayerState& state = game.board().player(player);
    const std::vector<InstanceId>* cards = nullptr;
    switch (zone) {
        case product::Zone::Hand: cards = &state.hand; break;
        case product::Zone::Standby: cards = &state.standby; break;
        case product::Zone::Graveyard: cards = &state.graveyard; break;
        default: throw std::invalid_argument("test helper supports vector zones only");
    }
    const auto found = std::find_if(cards->begin(), cards->end(), [&](const InstanceId card) {
        return game.board().instance(card).design_id == design_id;
    });
    if (found == cards->end()) {
        throw std::runtime_error("fixture card not found in requested zone");
    }
    return *found;
}

void finish_mulligan(product::ProductGame& game) {
    auto first = command(game, PlayerId::Player0, product::ActionKind::Mulligan);
    if (!game.submit_command(first)) {
        throw std::runtime_error("player 0 mulligan failed");
    }
    auto second = command(game, PlayerId::Player1, product::ActionKind::Mulligan);
    if (!game.submit_command(second)) {
        throw std::runtime_error("player 1 mulligan failed");
    }
}

void expect_invariants(TestContext& context, const product::ProductGame& game) {
    const auto problems = game.validate_invariants();
    for (const std::string& problem : problems) {
        std::cerr << "product game invariant: " << problem << '\n';
    }
    EXPECT(context, problems.empty());
}

// Arrange deterministic openings without mutating production definitions or
// exposing privileged state setters through the game/native API.
product::ProductGameConfig locked_config(
    const std::size_t deck_index, const std::vector<product::DesignId>& opening) {
    const auto decks = product::make_locked_product_decks();
    product::ProductGameConfig config;
    config.main_decks = {decks.at(deck_index).main_deck, decks.at(deck_index).main_deck};
    config.standby_decks = {decks.at(deck_index).standby, decks.at(deck_index).standby};
    config.professions = {decks.at(deck_index).profession_id, decks.at(deck_index).profession_id};
    config.evolution_charge_policies.fill(config.professions[0] == "oathguard"
        ? product::EvolutionChargePolicy::RepairToZero
        : product::EvolutionChargePolicy::FutureUseAtLeastTwo);
    auto& deck = config.main_decks[0];
    for (const auto& id : opening) {
        const auto found = std::find(deck.begin(), deck.end(), id);
        if (found == deck.end()) {
            throw std::logic_error("locked opening requests an unavailable card");
        }
        deck.erase(found);
    }
    for (auto item = opening.rbegin(); item != opening.rend(); ++item) {
        deck.push_back(*item);
    }
    config.first_player_mode = FirstPlayerMode::Player0;
    config.seed = 50;
    config.shuffle = false;
    return config;
}

void pass_to_owner_turn(product::ProductGame& game, const int turn) {
    for (int guard = 0; guard < 40; ++guard) {
        if (game.active_player() == PlayerId::Player0 &&
            game.resources(PlayerId::Player0).own_turn_number == turn &&
            game.phase() == product::ProductGamePhase::Main) {
            return;
        }
        if (!game.submit_command(command(game, game.active_player(), product::ActionKind::EndTurn))) {
            throw std::runtime_error("test turn setup is blocked by an unresolved effect");
        }
    }
    throw std::runtime_error("test turn setup exceeded its bounded turn count");
}

InstanceId play_from_hand(product::ProductGame& game, const std::string_view id,
    const product::ActionKind action, const std::optional<std::size_t> slot,
    const bool advance = false) {
    auto play = command(game, PlayerId::Player0, action);
    play.source = find_card(game, PlayerId::Player0, product::Zone::Hand, id);
    play.slot = slot;
    play.use_advance = advance;
    const auto status = game.submit_command(play);
    if (!status) {
        throw std::runtime_error("failed to play " + std::string(id) + ": " + status.message);
    }
    return *play.source;
}

product::CardCatalog semantic_catalog() {
    auto catalog = product::make_locked_product_catalog();
    const auto add_support = [&](product::CardDefinition card) {
        card.identity.neutral = true;
        card.identity.profession_id = "neutral";
        card.identity.series_id = "neutral";
        catalog.add(std::move(card));
    };
    for (int index = 0; index < 10; ++index) {
        add_support(make_card("SEM-FILL-" + std::to_string(index), product::CardKind::Spell, 0));
    }
    auto target = make_card("SEM-TARGET", product::CardKind::Follower, 0);
    target.attack = 2;
    target.health = 20;
    target.printed_keywords = product::mask(product::Keyword::Storm);
    add_support(std::move(target));
    add_support(make_card("SEM-FIELD", product::CardKind::Field, 0));
    for (const int amount : {1, 2, 6}) {
        auto burn = make_card("SEM-BURN-" + std::to_string(amount), product::CardKind::Spell, 0);
        burn.burn_pp_capacity = amount;
        add_support(std::move(burn));
        auto repair = make_card("SEM-REPAIR-" + std::to_string(amount), product::CardKind::Spell, 0);
        product::EffectSpec effect;
        effect.trigger = product::EffectTrigger::OnPlay;
        effect.kind = product::EffectKind::RepairCracks;
        effect.amount = amount;
        effect.effect_id = "synthetic-repair";
        repair.effects.push_back(effect);
        add_support(std::move(repair));
    }
    return catalog;
}

product::ProductGameConfig semantic_config(const std::size_t deck_index,
    const std::vector<product::DesignId>& opening, const std::size_t hand_size = 4U) {
    auto config = locked_config(deck_index, {});
    std::vector<product::DesignId> filler;
    for (int index = 0; index < 10; ++index) {
        for (int copy = 0; copy < 3; ++copy) {
            filler.push_back("SEM-FILL-" + std::to_string(index));
        }
    }
    config.main_decks = {filler, filler};
    config.main_decks[0].resize(filler.size() - opening.size());
    config.main_decks[0].insert(config.main_decks[0].end(), opening.rbegin(), opening.rend());
    config.starting_hand_size = hand_size;
    return config;
}

void select_choice_card(product::ProductGame& game, const InstanceId card) {
    if (!game.pending_choice()) {
        throw std::runtime_error("expected a pending card choice");
    }
    auto resolve = command(game, game.pending_choice()->chooser, product::ActionKind::ResolveChoice);
    resolve.choice_id = game.pending_choice()->choice_id;
    for (const auto& option : game.pending_choice()->options) {
        if (option.card == card) {
            resolve.selected_option_ids = {option.option_id};
        }
    }
    if (resolve.selected_option_ids.empty() || !game.submit_command(resolve)) {
        throw std::runtime_error("failed to resolve the requested card choice");
    }
}

void test_locked_search_filters_and_moves_all_remainder_to_bottom(TestContext& context) {
    product::ProductGame game(product::make_locked_product_catalog(), locked_config(0,
        {"LO-01", "LO-02", "LO-04", "NT-01", "LO-03", "LO-07", "LO-05", "NT-04"}));
    EXPECT(context, game.start());
    finish_mulligan(game);
    const auto& deck_before = game.board().player(PlayerId::Player0).deck;
    const std::vector<InstanceId> revealed(deck_before.end() - 4, deck_before.end());
    (void)play_from_hand(game, "LO-01", product::ActionKind::PlayFollower, 0U);
    EXPECT(context, game.phase() == product::ProductGamePhase::Choice);
    EXPECT(context, game.pending_choice()->minimum == 0U);
    EXPECT(context, game.pending_choice()->maximum == 1U);
    EXPECT(context, game.pending_choice()->options.size() == 2U);
    for (const auto& option : game.pending_choice()->options) {
        const auto& id = game.board().instance(*option.card).design_id;
        EXPECT(context, id == "LO-03" || id == "LO-07");
    }
    const auto selected = *game.pending_choice()->options.front().card;
    select_choice_card(game, selected);
    EXPECT(context, game.board().instance(selected).zone == product::Zone::Hand);
    EXPECT(context, game.board().player(PlayerId::Player0).hand.size() == 4U);
    const auto& deck = game.board().player(PlayerId::Player0).deck;
    EXPECT(context, deck.size() == 25U);
    for (const auto card : revealed) {
        if (card != selected) {
            EXPECT(context, std::find(deck.begin(), deck.begin() + 3, card) != deck.begin() + 3);
        }
    }
    expect_invariants(context, game);
}

void test_locked_opponent_turn_repair_charges_cycle_but_not_owner_turn_listeners(TestContext& context) {
    auto config = semantic_config(0, {"LO-07", "LO-10", "LO-04", "LO-03", "SEM-BURN-1"}, 5U);
    config.main_decks[1].back() = "NT-01";
    product::ProductGame game(semantic_catalog(), std::move(config));
    EXPECT(context, game.start());
    finish_mulligan(game);
    pass_to_owner_turn(game, 3);
    (void)play_from_hand(game, "LO-10", product::ActionKind::PlayField, std::nullopt);
    pass_to_owner_turn(game, 4);
    const auto knight = play_from_hand(game, "LO-04", product::ActionKind::PlayFollower, 0U);
    pass_to_owner_turn(game, 5);
    const auto bell = play_from_hand(game, "LO-03", product::ActionKind::PlayAmulet, 4U);
    (void)play_from_hand(game, "SEM-BURN-1", product::ActionKind::CastSpell, 0U);
    const auto trap = play_from_hand(game, "LO-07", product::ActionKind::PlayTrap, 1U);
    EXPECT(context, game.submit_command(command(game, PlayerId::Player0, product::ActionKind::EndTurn)));
    auto enemy_play = command(game, PlayerId::Player1, product::ActionKind::PlayFollower);
    enemy_play.source = find_card(game, PlayerId::Player1, product::Zone::Hand, "NT-01");
    enemy_play.slot = 0U;
    EXPECT(context, game.submit_command(enemy_play));
    EXPECT(context, game.submit_command(command(game, PlayerId::Player1, product::ActionKind::EndTurn)));
    EXPECT(context, game.board().instance(bell).countdown == 2);
    EXPECT(context, game.submit_command(command(game, PlayerId::Player0, product::ActionKind::EndTurn)));
    auto attack = command(game, PlayerId::Player1, product::ActionKind::Attack);
    attack.source = enemy_play.source;
    attack.target = knight;
    EXPECT(context, game.submit_command(attack));
    auto activate = command(game, PlayerId::Player0, product::ActionKind::ActivateTrap);
    activate.source = trap;
    EXPECT(context, game.submit_command(activate));
    for (int count = 0; count < 4 && game.phase() == product::ProductGamePhase::Reaction; ++count) {
        EXPECT(context, game.submit_command(command(
            game, game.reaction_context().priority, product::ActionKind::PassReaction)));
    }
    EXPECT(context, game.resources(PlayerId::Player0).cracks == 0);
    EXPECT(context, game.resources(PlayerId::Player0).evolution_energy == 3);
    EXPECT(context, game.board().instance(bell).countdown == 2);
    EXPECT(context, game.board().instance(knight).current_attack == 2);
    EXPECT(context, !game.pending_choice());
    EXPECT(context, game.phase() == product::ProductGamePhase::Main);
    EXPECT(context, game.board().instance(trap).zone == product::Zone::Graveyard);
    EXPECT(context, game.board().instance(*attack.source).attacked_this_turn);
    expect_invariants(context, game);
}

void test_locked_surviving_defender_kill_repairs_once_per_turn(TestContext& context) {
    auto config = semantic_config(0, {"LO-08", "SEM-BURN-2", "SEM-FILL-9", "SEM-FILL-9"});
    config.main_decks[1].back() = "NT-01";
    config.main_decks[1][28] = "NT-01";
    product::ProductGame game(semantic_catalog(), std::move(config));
    EXPECT(context, game.start());
    finish_mulligan(game);
    pass_to_owner_turn(game, 5);
    const auto knight = play_from_hand(game, "LO-08", product::ActionKind::PlayFollower, 0U);
    EXPECT(context, game.submit_command(command(game, PlayerId::Player0, product::ActionKind::EndTurn)));
    std::vector<InstanceId> enemies;
    for (std::size_t slot = 0; slot < 2U; ++slot) {
        auto play = command(game, PlayerId::Player1, product::ActionKind::PlayFollower);
        play.source = find_card(game, PlayerId::Player1, product::Zone::Hand, "NT-01");
        play.slot = slot;
        EXPECT(context, game.submit_command(play));
        enemies.push_back(*play.source);
    }
    EXPECT(context, game.submit_command(command(game, PlayerId::Player1, product::ActionKind::EndTurn)));
    (void)play_from_hand(game, "SEM-BURN-2", product::ActionKind::CastSpell, 0U);
    auto evolve = command(game, PlayerId::Player0, product::ActionKind::Evolve);
    evolve.source = knight;
    EXPECT(context, game.submit_command(evolve));
    EXPECT(context, game.submit_command(command(game, PlayerId::Player0, product::ActionKind::EndTurn)));
    for (const auto enemy : enemies) {
        auto attack = command(game, PlayerId::Player1, product::ActionKind::Attack);
        attack.source = enemy;
        attack.target = knight;
        EXPECT(context, game.submit_command(attack));
        EXPECT(context, game.board().instance(enemy).zone == product::Zone::Graveyard);
        EXPECT(context, game.resources(PlayerId::Player0).cracks == 1);
    }
    EXPECT(context, game.board().instance(knight).current_health == 2);
    EXPECT(context, !game.board().instance(knight).keywords.has(product::Keyword::Barrier));
    expect_invariants(context, game);
}

void test_locked_combat_repair_resets_each_players_turn_not_profession_cycle(TestContext& context) {
    for (const bool executioner : {false, true}) {
        auto config = semantic_config(0, {"LO-10", "LO-02", executioner ? "SEM-FILL-9" : "LO-08",
            "SEM-BURN-1", "SEM-BURN-2"}, 5U);
        config.main_decks[1].back() = "NT-01";
        config.main_decks[1][28] = "NT-01";
        product::ProductGame game(semantic_catalog(), std::move(config));
        EXPECT(context, game.start());
        finish_mulligan(game);
        pass_to_owner_turn(game, 8);
        (void)play_from_hand(game, "LO-10", product::ActionKind::PlayField, std::nullopt);
        (void)play_from_hand(game, "SEM-BURN-1", product::ActionKind::CastSpell, 0U);
        EXPECT(context, game.submit_command(command(game, PlayerId::Player0, product::ActionKind::EndTurn)));
        std::vector<InstanceId> enemies;
        for (std::size_t slot = 0; slot < 2U; ++slot) {
            auto play = command(game, PlayerId::Player1, product::ActionKind::PlayFollower);
            play.source = find_card(game, PlayerId::Player1, product::Zone::Hand, "NT-01");
            play.slot = slot;
            EXPECT(context, game.submit_command(play));
            enemies.push_back(*play.source);
        }
        EXPECT(context, game.submit_command(command(game, PlayerId::Player1, product::ActionKind::EndTurn)));
        const auto priest = play_from_hand(game, "LO-02", product::ActionKind::PlayFollower, 0U);
        select_choice_card(game, priest);
        EXPECT(context, game.board().instance(priest).keywords.has(product::Keyword::Barrier));
        EXPECT(context, game.resources(PlayerId::Player0).profession_charge_used_this_turn);
        (void)play_from_hand(game, "SEM-BURN-2", product::ActionKind::CastSpell, 0U);
        InstanceId knight = 0;
        if (executioner) {
            auto deploy = command(game, PlayerId::Player0, product::ActionKind::Deploy);
            deploy.source = find_card(game, PlayerId::Player0, product::Zone::Standby, "LO-S02");
            deploy.slot = 1U;
            EXPECT(context, game.submit_command(deploy));
            knight = *deploy.source;
        } else {
            knight = play_from_hand(game, "LO-08", product::ActionKind::PlayFollower, 1U);
        }
        auto evolve = command(game, PlayerId::Player0, product::ActionKind::Evolve);
        evolve.source = knight;
        EXPECT(context, game.submit_command(evolve));
        const auto energy = game.resources(PlayerId::Player0).evolution_energy;
        const auto own_turn = game.resources(PlayerId::Player0).own_turn_number;
        auto attack = command(game, PlayerId::Player0, product::ActionKind::Attack);
        attack.source = knight;
        attack.target = enemies.front();
        EXPECT(context, game.submit_command(attack));
        EXPECT(context, game.resources(PlayerId::Player0).cracks == 1);
        EXPECT(context, game.board().instance(enemies.front()).zone == product::Zone::Graveyard);
        EXPECT(context, game.submit_command(command(game, PlayerId::Player0, product::ActionKind::EndTurn)));
        attack = command(game, PlayerId::Player1, product::ActionKind::Attack);
        attack.source = enemies.back();
        attack.target = knight;
        EXPECT(context, game.submit_command(attack));
        EXPECT(context, game.board().instance(enemies.back()).zone == product::Zone::Graveyard);
        EXPECT(context, game.board().instance(knight).zone == product::Zone::MainBoard);
        EXPECT(context, game.resources(PlayerId::Player0).cracks == 0);
        EXPECT(context, game.resources(PlayerId::Player0).own_turn_number == own_turn);
        EXPECT(context, game.resources(PlayerId::Player0).evolution_energy == energy);
        EXPECT(context, game.resources(PlayerId::Player0).profession_charge_used_this_turn);
        EXPECT(context, game.board().instance(knight).keywords.has(product::Keyword::Barrier) == !executioner);
        EXPECT(context, !game.pending_choice());
        expect_invariants(context, game);
    }
}

void test_locked_field_cycles_after_successful_draw_and_skips_overflow_bottom(TestContext& context) {
    // Ordinary entry consumes a real hand card, draws, then pauses for bottoming.
    product::ProductGame game(semantic_catalog(), semantic_config(1,
        {"AP-05", "AP-02", "SEM-BURN-1", "SEM-FILL-9"}));
    EXPECT(context, game.start());
    finish_mulligan(game);
    pass_to_owner_turn(game, 3);
    const auto field = play_from_hand(game, "AP-05", product::ActionKind::PlayField, std::nullopt);
    EXPECT(context, game.pending_choice().has_value());
    auto to_bottom = find_card(game, PlayerId::Player0, product::Zone::Hand, "SEM-FILL-9");
    select_choice_card(game, to_bottom);
    EXPECT(context, game.board().player(PlayerId::Player0).deck.front() == to_bottom);
    pass_to_owner_turn(game, 4);
    const auto student = play_from_hand(game, "AP-02", product::ActionKind::PlayFollower, 0U);
    EXPECT(context, game.board().instance(student).keywords.has(product::Keyword::Barrier));
    EXPECT(context, game.pending_choice().has_value());
    select_choice_card(game, *game.pending_choice()->options.front().card);
    EXPECT(context, game.phase() == product::ProductGamePhase::Main);
    EXPECT(context, game.board().player(PlayerId::Player0).field == field);
    expect_invariants(context, game);

    // Real book + field listeners: resolve the expiring book first so its draw
    // fills the space freed by the spell. The field's draw must then overflow
    // without asking the player to bottom an unrelated existing hand card.
    product::ProductGame overflow(semantic_catalog(), semantic_config(1,
        {"AP-05", "AP-04", "SEM-BURN-1", "SEM-FILL-9"}, 7U));
    EXPECT(context, overflow.start());
    finish_mulligan(overflow);
    pass_to_owner_turn(overflow, 3);
    const auto overflow_field = play_from_hand(
        overflow, "AP-05", product::ActionKind::PlayField, std::nullopt);
    select_choice_card(overflow, find_card(overflow, PlayerId::Player0, product::Zone::Hand, "SEM-FILL-9"));
    pass_to_owner_turn(overflow, 4);
    const auto book = play_from_hand(overflow, "AP-04", product::ActionKind::PlayAmulet, 3U);
    select_choice_card(overflow, *overflow.pending_choice()->options.back().card);
    pass_to_owner_turn(overflow, 6);
    EXPECT(context, overflow.board().player(PlayerId::Player0).hand.size() == 9U);
    EXPECT(context, overflow.board().instance(book).countdown == 1);
    const auto archives = overflow.board().player(PlayerId::Player0).archive.size();
    (void)play_from_hand(overflow, "SEM-BURN-1", product::ActionKind::CastSpell, 0U);
    EXPECT(context, overflow.pending_choice().has_value());
    EXPECT(context, overflow.pending_choice()->kind == product::ChoiceKind::TriggerOrder);
    auto order = command(overflow, PlayerId::Player0, product::ActionKind::ResolveChoice);
    order.choice_id = overflow.pending_choice()->choice_id;
    for (const auto source : {book, overflow_field}) {
        for (const auto& option : overflow.pending_choice()->options) {
            if (option.card == source) {
                order.selected_option_ids.push_back(option.option_id);
            }
        }
    }
    EXPECT(context, overflow.submit_command(order));
    EXPECT(context, !overflow.pending_choice());
    EXPECT(context, overflow.board().instance(book).zone == product::Zone::Graveyard);
    EXPECT(context, overflow.board().player(PlayerId::Player0).hand.size() == 9U);
    EXPECT(context, overflow.board().player(PlayerId::Player0).archive.size() == archives + 1U);
    expect_invariants(context, overflow);
}

void test_locked_abaddon_mixed_additional_cost_and_vacated_slot(TestContext& context) {
    for (const bool use_amulet : {false, true}) {
        product::ProductGame game(semantic_catalog(), semantic_config(1,
            {use_amulet ? "AP-04" : "AP-01", "SEM-BURN-6", "NT-01", "SEM-FILL-9"}));
        EXPECT(context, game.start());
        finish_mulligan(game);
        pass_to_owner_turn(game, 10);
        const auto neutral = play_from_hand(game, "NT-01", product::ActionKind::PlayFollower, 0U);
        pass_to_owner_turn(game, 11);
        (void)play_from_hand(game, "SEM-BURN-6", product::ActionKind::CastSpell, 0U);
        pass_to_owner_turn(game, 12);
        const auto cost = play_from_hand(game, use_amulet ? "AP-04" : "AP-01",
            use_amulet ? product::ActionKind::PlayAmulet : product::ActionKind::PlayFollower, 4U);
        auto deploy = command(game, PlayerId::Player0, product::ActionKind::Deploy);
        deploy.source = find_card(game, PlayerId::Player0, product::Zone::Standby, "AP-S04");
        deploy.slot = 4U;
        deploy.additional_cost_cards = {neutral};
        EXPECT_GAME_CODE(context, game.plan_command(deploy).status, product::ProductGameError::InvalidSelection);
        deploy.additional_cost_cards = {cost};
        pass_to_owner_turn(game, 13);
        deploy.expected_revision = game.revision();
        EXPECT(context, game.plan_command(deploy));
        EXPECT(context, game.submit_command(deploy));
        EXPECT(context, game.board().instance(cost).zone == product::Zone::Archive);
        EXPECT(context, game.board().player(PlayerId::Player0).main_board[4] == deploy.source);
        EXPECT(context, game.board().instance(*deploy.source).keywords.has(product::Keyword::Storm));
        const auto& moves = game.board().moves();
        const auto archived = std::find_if(moves.rbegin(), moves.rend(), [&](const auto& move) {
            return move.card == cost;
        });
        EXPECT(context, archived->reason == product::MoveReason::AdditionalCost && !archived->destroyed);
        expect_invariants(context, game);
    }
}

void test_all_locked_main_followers_execute_printed_baseline(TestContext& context) {
    struct Row { const char* id; std::size_t deck; int attack; int health; product::KeywordMask keywords; };
    using K = product::Keyword;
    const std::vector<Row> rows = {
        {"LO-01", 0, 1, 1, 0}, {"LO-02", 0, 2, 2, 0},
        {"LO-04", 0, 2, 2, product::mask(K::Ward) | product::mask(K::Barrier)},
        {"LO-05", 0, 3, 2, product::mask(K::Rush)}, {"LO-08", 0, 4, 4, product::mask(K::Rush)},
        {"LO-09", 0, 4, 6, product::mask(K::Ward)},
        {"LO-11", 0, 8, 8, product::mask(K::Ward) | product::mask(K::Storm) | product::mask(K::Barrier)},
        {"AP-01", 1, 1, 2, 0}, {"AP-02", 1, 2, 2, 0}, {"AP-06", 1, 3, 2, product::mask(K::Bane)},
        {"AP-07", 1, 3, 5, product::mask(K::Lifesteal)},
        {"AP-09", 1, 4, 5, product::mask(K::Barrier)}, {"AP-10", 1, 4, 6, product::mask(K::Ward)},
        {"AP-11", 1, 6, 6, product::mask(K::Rush)},
        {"NT-01", 0, 2, 2, 0}, {"NT-02", 0, 2, 4, product::mask(K::Ward)},
    };
    std::unordered_set<std::string> covered;
    for (const auto& row : rows) {
        product::ProductGame game(semantic_catalog(), semantic_config(row.deck,
            {row.id, "SEM-FILL-9", "SEM-FILL-9", "SEM-FILL-8"}));
        EXPECT(context, game.start());
        finish_mulligan(game);
        pass_to_owner_turn(game, 12);
        const auto instance = play_from_hand(game, row.id, product::ActionKind::PlayFollower, 3U);
        const auto& follower = game.board().instance(instance);
        EXPECT(context, follower.current_attack == row.attack);
        EXPECT(context, follower.current_health == row.health);
        EXPECT(context, follower.maximum_health == row.health);
        EXPECT(context, follower.keywords.effective() == row.keywords);
        EXPECT(context, follower.zone == product::Zone::MainBoard && follower.sequence == 3U);
        EXPECT(context, game.phase() == product::ProductGamePhase::Main);
        auto attack = command(game, PlayerId::Player0, product::ActionKind::Attack);
        attack.source = instance;
        EXPECT(context, static_cast<bool>(game.plan_command(attack)) == product::contains(row.keywords, K::Storm));
        expect_invariants(context, game);
        covered.insert(row.id);
    }
    const auto catalog = product::make_locked_product_catalog();
    for (const auto& [id, definition] : catalog.definitions()) {
        if (definition.availability == product::CardAvailability::MainDeck &&
            definition.kind == product::CardKind::Follower) {
            EXPECT(context, covered.contains(id));
        }
    }
    EXPECT(context, covered.size() == 16U);
}

void test_locked_finishers_advance_cannot_bypass_on_time_condition(TestContext& context) {
    for (const std::string id : {"LO-11", "AP-11"}) {
        const std::size_t deck = id == "LO-11" ? 0U : 1U;
        product::ProductGame advanced(semantic_catalog(), semantic_config(deck,
            {id, "SEM-BURN-2", "SEM-FILL-9", "SEM-FILL-9"}));
        EXPECT(context, advanced.start());
        finish_mulligan(advanced);
        if (deck == 1U) {
            pass_to_owner_turn(advanced, 4);
            (void)play_from_hand(advanced, "SEM-BURN-2", product::ActionKind::CastSpell, 0U);
        }
        pass_to_owner_turn(advanced, deck == 0U ? 7 : 6);
        const auto finisher = play_from_hand(advanced, id, product::ActionKind::PlayFollower, 0U, true);
        EXPECT(context, advanced.board().instance(finisher).keywords.has(product::Keyword::Rush));
        EXPECT(context, !advanced.board().instance(finisher).keywords.has(product::Keyword::Storm));
        EXPECT(context, !advanced.board().instance(finisher).keywords.has(product::Keyword::Barrier));
        EXPECT(context, advanced.board().instance(finisher).current_attack == (deck == 0U ? 8 : 6));
        auto attack = command(advanced, PlayerId::Player0, product::ActionKind::Attack);
        attack.source = finisher;
        EXPECT(context, !advanced.plan_command(attack));
        EXPECT(context, advanced.submit_command(command(advanced, PlayerId::Player0, product::ActionKind::EndTurn)));
        EXPECT(context, advanced.board().instance(finisher).keywords.has(product::Keyword::Rush));
        expect_invariants(context, advanced);
    }
    product::ProductGame oath_on_time(semantic_catalog(), semantic_config(0,
        {"LO-11", "SEM-FILL-9", "SEM-FILL-9", "SEM-FILL-8"}));
    EXPECT(context, oath_on_time.start());
    finish_mulligan(oath_on_time);
    pass_to_owner_turn(oath_on_time, 10);
    const auto oath_finisher = play_from_hand(oath_on_time, "LO-11", product::ActionKind::PlayFollower, 0U);
    EXPECT(context, oath_on_time.board().instance(oath_finisher).keywords.has(product::Keyword::Storm));
    EXPECT(context, oath_on_time.submit_command(command(oath_on_time, PlayerId::Player0, product::ActionKind::EndTurn)));
    EXPECT(context, oath_on_time.board().instance(oath_finisher).keywords.has(product::Keyword::Storm));
    EXPECT(context, oath_on_time.board().instance(oath_finisher).keywords.has(product::Keyword::Barrier));
    expect_invariants(context, oath_on_time);
    product::ProductGame on_time(semantic_catalog(), semantic_config(1,
        {"AP-11", "SEM-BURN-6", "SEM-FILL-9", "SEM-FILL-9"}));
    EXPECT(context, on_time.start());
    finish_mulligan(on_time);
    pass_to_owner_turn(on_time, 6);
    (void)play_from_hand(on_time, "SEM-BURN-6", product::ActionKind::CastSpell, 0U);
    pass_to_owner_turn(on_time, 14);
    const auto finisher = play_from_hand(on_time, "AP-11", product::ActionKind::PlayFollower, 0U);
    EXPECT(context, on_time.board().instance(finisher).current_attack == 8);
    EXPECT(context, on_time.board().instance(finisher).keywords.has(product::Keyword::Storm));
    auto attack = command(on_time, PlayerId::Player0, product::ActionKind::Attack);
    attack.source = finisher;
    EXPECT(context, on_time.plan_command(attack));
    EXPECT(context, on_time.submit_command(command(on_time, PlayerId::Player0, product::ActionKind::EndTurn)));
    EXPECT(context, on_time.board().instance(finisher).current_attack == 6);
    EXPECT(context, !on_time.board().instance(finisher).keywords.has(product::Keyword::Storm));
    expect_invariants(context, on_time);
}

void test_locked_profession_unlock_charge_and_cap_are_not_precharged(TestContext& context) {
    product::ProductGame game(semantic_catalog(), semantic_config(1,
        {"SEM-BURN-2", "SEM-BURN-2", "SEM-BURN-2", "SEM-FILL-9"}));
    EXPECT(context, game.start());
    finish_mulligan(game);
    pass_to_owner_turn(game, 3);
    (void)play_from_hand(game, "SEM-BURN-2", product::ActionKind::CastSpell, 0U);
    EXPECT(context, game.resources(PlayerId::Player0).evolution_energy == 0);
    pass_to_owner_turn(game, 5);
    EXPECT(context, game.resources(PlayerId::Player0).evolution_energy == 2);
    (void)play_from_hand(game, "SEM-BURN-2", product::ActionKind::CastSpell, 0U);
    EXPECT(context, game.resources(PlayerId::Player0).evolution_energy == 3);
    pass_to_owner_turn(game, 6);
    (void)play_from_hand(game, "SEM-BURN-2", product::ActionKind::CastSpell, 0U);
    EXPECT(context, game.resources(PlayerId::Player0).evolution_energy == 4);
    EXPECT(context, game.resources(PlayerId::Player0).cracks == 6);
    expect_invariants(context, game);
}

void test_capacity_burn_preserves_available_current_pp(TestContext& context) {
    product::ProductGame game(semantic_catalog(), semantic_config(0,
        {"SEM-BURN-2", "SEM-FILL-9", "SEM-FILL-9", "SEM-FILL-8"}));
    EXPECT(context, game.start());
    finish_mulligan(game);
    pass_to_owner_turn(game, 3);
    auto burn = command(game, PlayerId::Player0, product::ActionKind::CastSpell);
    burn.source = find_card(game, PlayerId::Player0, product::Zone::Hand, "SEM-BURN-2");
    burn.slot = 0U;
    const auto planned = game.plan_command(burn);
    EXPECT(context, planned);
    EXPECT(context, planned.payment.current_pp_after == 3);
    EXPECT(context, planned.payment.pp_capacity_after == 1);
    EXPECT(context, game.submit_command(burn));
    EXPECT(context, game.resources(PlayerId::Player0).current_pp == 3);
    EXPECT(context, game.resources(PlayerId::Player0).pp_capacity == 1);
    expect_invariants(context, game);
    pass_to_owner_turn(game, 4);
    EXPECT(context, game.resources(PlayerId::Player0).current_pp == 2);
}

void test_locked_lifesteal_lethal_uses_actual_damage_and_final_event(TestContext& context) {
    auto config = semantic_config(1, {"AP-07", "SEM-FILL-9", "SEM-FILL-9", "SEM-FILL-8"});
    config.main_decks[1].back() = "NT-01";
    product::ProductGame game(semantic_catalog(), std::move(config));
    EXPECT(context, game.start());
    finish_mulligan(game);
    pass_to_owner_turn(game, 4);
    const auto lifesteal = play_from_hand(game, "AP-07", product::ActionKind::PlayFollower, 0U);
    EXPECT(context, game.submit_command(command(game, PlayerId::Player0, product::ActionKind::EndTurn)));
    auto play_enemy = command(game, PlayerId::Player1, product::ActionKind::PlayFollower);
    play_enemy.source = find_card(game, PlayerId::Player1, product::Zone::Hand, "NT-01");
    play_enemy.slot = 0U;
    EXPECT(context, game.submit_command(play_enemy));
    EXPECT(context, game.submit_command(command(game, PlayerId::Player1, product::ActionKind::EndTurn)));
    for (int index = 0; index < 9; ++index) {
        auto attack = command(game, PlayerId::Player0, product::ActionKind::Attack);
        attack.source = lifesteal;
        EXPECT(context, game.submit_command(attack));
        if (index == 8) {
            break;
        }
        EXPECT(context, game.submit_command(command(game, PlayerId::Player0, product::ActionKind::EndTurn)));
        auto counter = command(game, PlayerId::Player1, product::ActionKind::Attack);
        counter.source = play_enemy.source;
        EXPECT(context, game.submit_command(counter));
        EXPECT(context, game.submit_command(command(game, PlayerId::Player1, product::ActionKind::EndTurn)));
    }
    EXPECT(context, game.phase() == product::ProductGamePhase::Finished);
    EXPECT(context, game.board().player(PlayerId::Player0).leader_health == 24);
    const auto& events = game.events();
    EXPECT(context, events.back().kind == product::ProductEventKind::MatchEnded);
    EXPECT(context, events[events.size() - 2U].kind == product::ProductEventKind::Healing);
    EXPECT(context, events[events.size() - 2U].value == 1);
    EXPECT(context, events[events.size() - 3U].kind == product::ProductEventKind::Damage);
    EXPECT(context, events[events.size() - 3U].value == 1);
    const auto revision = game.revision();
    const auto count = events.size();
    EXPECT_GAME_CODE(context, game.submit_command(command(game, PlayerId::Player1, product::ActionKind::Surrender)),
        product::ProductGameError::MatchFinished);
    EXPECT(context, game.revision() == revision && game.events().size() == count);
    expect_invariants(context, game);
}

InstanceId prepare_enemy_permanent(product::ProductGame& game, const std::string_view id,
    const product::ActionKind kind) {
    if (!game.submit_command(command(game, PlayerId::Player0, product::ActionKind::EndTurn))) {
        throw std::runtime_error("enemy setup requires a clear main phase");
    }
    auto play = command(game, PlayerId::Player1, kind);
    play.source = find_card(game, PlayerId::Player1, product::Zone::Hand, id);
    if (kind != product::ActionKind::PlayField) {
        play.slot = 0U;
    }
    if (!game.submit_command(play) ||
        !game.submit_command(command(game, PlayerId::Player1, product::ActionKind::EndTurn))) {
        throw std::runtime_error("enemy setup failed");
    }
    return *play.source;
}

void test_locked_damage_threshold_optional_target_and_modes(TestContext& context) {
    for (const std::string id : {"AP-03", "AP-10", "NT-04"}) {
        for (const bool debt : {false, true}) {
            auto config = semantic_config(1, {id, "SEM-BURN-6", "SEM-FILL-9", "SEM-FILL-9"});
            config.main_decks[1].back() = "SEM-TARGET";
            product::ProductGame game(semantic_catalog(), std::move(config));
            EXPECT(context, game.start());
            finish_mulligan(game);
            pass_to_owner_turn(game, 12);
            if (debt) {
                (void)play_from_hand(game, "SEM-BURN-6", product::ActionKind::CastSpell, 0U);
            }
            const auto target = prepare_enemy_permanent(game, "SEM-TARGET", product::ActionKind::PlayFollower);
            auto play = command(game, PlayerId::Player0,
                id == "AP-10" ? product::ActionKind::PlayFollower : product::ActionKind::CastSpell);
            play.source = find_card(game, PlayerId::Player0, product::Zone::Hand, id);
            play.slot = 2U;
            if (id == "NT-04") {
                play.mode_id = "damage_follower";
            }
            if (id == "AP-10") {
                auto declined = game;
                EXPECT(context, declined.submit_command(play));
                EXPECT(context, declined.board().instance(target).current_health == 20);
                EXPECT(context, !declined.pending_choice());
            }
            play.target = target;
            EXPECT(context, game.submit_command(play));
            const int damage = id == "AP-03" ? (debt ? 5 : 3) : id == "AP-10" ? (debt ? 5 : 0) : 4;
            EXPECT(context, game.board().instance(target).current_health == 20 - damage);
            EXPECT(context, game.board().instance(*play.source).zone ==
                (id == "AP-10" ? product::Zone::MainBoard : product::Zone::Graveyard));
            EXPECT(context, game.phase() == product::ProductGamePhase::Main);
            expect_invariants(context, game);
        }
    }
    auto config = semantic_config(0, {"NT-04", "SEM-FILL-9", "SEM-FILL-9", "SEM-FILL-8"});
    config.main_decks[1].back() = "SEM-FIELD";
    product::ProductGame game(semantic_catalog(), std::move(config));
    EXPECT(context, game.start());
    finish_mulligan(game);
    pass_to_owner_turn(game, 5);
    const auto field = prepare_enemy_permanent(game, "SEM-FIELD", product::ActionKind::PlayField);
    auto destroy = command(game, PlayerId::Player0, product::ActionKind::CastSpell);
    destroy.source = find_card(game, PlayerId::Player0, product::Zone::Hand, "NT-04");
    destroy.slot = 1U;
    destroy.mode_id = "damage_follower";
    destroy.target = field;
    EXPECT_GAME_CODE(context, game.submit_command(destroy), product::ProductGameError::InvalidTarget);
    destroy.mode_id = "destroy_amulet_or_field";
    EXPECT(context, game.submit_command(destroy));
    EXPECT(context, game.board().instance(field).zone == product::Zone::Graveyard);
    EXPECT(context, !game.board().player(PlayerId::Player1).field);
    expect_invariants(context, game);
}

void test_locked_repair_rewards_and_advanced_follower_programs(TestContext& context) {
    for (const std::string id : {"LO-05", "LO-06", "LO-09", "AP-06"}) {
        const auto deck = id.starts_with("LO") ? 0U : 1U;
        product::ProductGame game(semantic_catalog(), semantic_config(deck,
            {id, "SEM-BURN-2", "SEM-REPAIR-2", "SEM-FILL-9"}));
        EXPECT(context, game.start());
        finish_mulligan(game);
        pass_to_owner_turn(game, 8);
        (void)play_from_hand(game, "SEM-BURN-2", product::ActionKind::CastSpell, 0U);
        if (id == "LO-05") {
            (void)play_from_hand(game, "SEM-REPAIR-2", product::ActionKind::CastSpell, 0U);
        }
        const auto before_hand = game.board().player(PlayerId::Player0).hand.size();
        const auto source = play_from_hand(game, id,
            id == "LO-06" ? product::ActionKind::CastSpell : product::ActionKind::PlayFollower, 1U);
        if (id == "LO-05") {
            EXPECT(context, game.board().instance(source).current_attack == 4);
            EXPECT(context, game.board().instance(source).current_health == 3);
        } else if (id == "LO-06") {
            EXPECT(context, game.resources(PlayerId::Player0).cracks == 0);
            EXPECT(context, game.board().player(PlayerId::Player0).hand.size() == before_hand);
        } else if (id == "LO-09") {
            EXPECT(context, game.resources(PlayerId::Player0).cracks == 0);
            EXPECT(context, game.board().instance(source).keywords.has(product::Keyword::Barrier));
        } else {
            EXPECT(context, game.board().instance(source).keywords.has(product::Keyword::Rush));
            EXPECT(context, game.board().instance(source).keywords.has(product::Keyword::Bane));
        }
        expect_invariants(context, game);
    }
    for (const std::string id : {"AP-07", "AP-09"}) {
        product::ProductGame game(semantic_catalog(), semantic_config(1,
            {id, "SEM-FILL-9", "SEM-FILL-9", "SEM-FILL-8"}));
        EXPECT(context, game.start());
        finish_mulligan(game);
        pass_to_owner_turn(game, 3);
        const auto before = game.board().player(PlayerId::Player0).hand.size();
        const auto card = play_from_hand(game, id, product::ActionKind::PlayFollower, 0U, true);
        if (id == "AP-09") {
            EXPECT(context, game.pending_choice().has_value());
            EXPECT(context, game.board().player(PlayerId::Player0).hand.size() == before + 1U);
            const auto discarded = *game.pending_choice()->options.front().card;
            select_choice_card(game, discarded);
            EXPECT(context, game.board().instance(discarded).zone == product::Zone::Graveyard);
            EXPECT(context, game.board().player(PlayerId::Player0).hand.size() == before);
            const auto& moves = game.board().moves();
            EXPECT(context, moves.back().reason == product::MoveReason::Discarded);
            EXPECT(context, !game.board().instance(card).keywords.has(product::Keyword::Barrier));
        }
        EXPECT(context, game.board().instance(card).keywords.has(product::Keyword::Rush));
        EXPECT(context, game.submit_command(command(game, PlayerId::Player0, product::ActionKind::EndTurn)));
        EXPECT(context, !game.board().instance(card).keywords.has(product::Keyword::Rush));
        expect_invariants(context, game);
    }
}

void test_locked_standby_conditions_and_entry_rewards(TestContext& context) {
    for (const bool executioner : {false, true}) {
        auto config = semantic_config(0, {executioner ? "LO-04" : "LO-02",
            "SEM-BURN-1", "SEM-REPAIR-1", "SEM-FILL-9"});
        config.main_decks[1].back() = "NT-01";
        product::ProductGame game(semantic_catalog(), std::move(config));
        EXPECT(context, game.start());
        finish_mulligan(game);
        pass_to_owner_turn(game, 3);
        const auto enemy = prepare_enemy_permanent(game, "NT-01", product::ActionKind::PlayFollower);
        pass_to_owner_turn(game, 5);
        auto deploy = command(game, PlayerId::Player0, product::ActionKind::Deploy);
        deploy.source = find_card(game, PlayerId::Player0, product::Zone::Standby,
            executioner ? "LO-S02" : "LO-S01");
        deploy.slot = 1U;
        EXPECT_GAME_CODE(context, game.plan_command(deploy).status, product::ProductGameError::DeploymentUnavailable);
        const auto friend_card = play_from_hand(game, executioner ? "LO-04" : "LO-02",
            product::ActionKind::PlayFollower, 0U);
        (void)play_from_hand(game, "SEM-BURN-1", product::ActionKind::CastSpell, 0U);
        if (!executioner) {
            (void)play_from_hand(game, "SEM-REPAIR-1", product::ActionKind::CastSpell, 0U);
            deploy.target = friend_card;
        }
        deploy.expected_revision = game.revision();
        EXPECT(context, game.submit_command(deploy));
        if (executioner) {
            auto attack = command(game, PlayerId::Player0, product::ActionKind::Attack);
            attack.source = deploy.source;
            attack.target = enemy;
            EXPECT(context, game.submit_command(attack));
            EXPECT(context, game.board().instance(enemy).zone == product::Zone::Graveyard);
            EXPECT(context, game.resources(PlayerId::Player0).cracks == 0);
        } else {
            EXPECT(context, game.board().instance(friend_card).current_attack == 3);
            EXPECT(context, game.board().instance(friend_card).current_health == 3);
        }
        expect_invariants(context, game);
    }
    product::ProductGame pegasus(semantic_catalog(), semantic_config(0,
        {"SEM-BURN-1", "SEM-REPAIR-1", "SEM-BURN-1", "SEM-REPAIR-1"}));
    EXPECT(context, pegasus.start());
    finish_mulligan(pegasus);
    for (const int turn : {9, 10}) {
        pass_to_owner_turn(pegasus, turn);
        (void)play_from_hand(pegasus, "SEM-BURN-1", product::ActionKind::CastSpell, 0U);
        (void)play_from_hand(pegasus, "SEM-REPAIR-1", product::ActionKind::CastSpell, 0U);
        auto deploy = command(pegasus, PlayerId::Player0, product::ActionKind::Deploy);
        deploy.source = find_card(pegasus, PlayerId::Player0, product::Zone::Standby, "LO-S04");
        deploy.slot = 2U;
        if (turn == 9) {
            EXPECT_GAME_CODE(context, pegasus.plan_command(deploy).status, product::ProductGameError::DeploymentUnavailable);
        } else {
            EXPECT(context, pegasus.submit_command(deploy));
            EXPECT(context, pegasus.board().instance(*deploy.source).keywords.has(product::Keyword::Storm));
        }
    }
    expect_invariants(context, pegasus);

    product::ProductGame hound(semantic_catalog(), semantic_config(1,
        {"SEM-BURN-1", "SEM-FILL-9", "SEM-FILL-9", "SEM-FILL-8"}));
    EXPECT(context, hound.start());
    finish_mulligan(hound);
    pass_to_owner_turn(hound, 3);
    auto deploy_hound = command(hound, PlayerId::Player0, product::ActionKind::Deploy);
    deploy_hound.source = find_card(hound, PlayerId::Player0, product::Zone::Standby, "AP-S01");
    deploy_hound.slot = 0U;
    EXPECT_GAME_CODE(context, hound.plan_command(deploy_hound).status, product::ProductGameError::DeploymentUnavailable);
    (void)play_from_hand(hound, "SEM-BURN-1", product::ActionKind::CastSpell, 0U);
    deploy_hound.expected_revision = hound.revision();
    EXPECT(context, hound.submit_command(deploy_hound));
    EXPECT(context, hound.board().instance(*deploy_hound.source).keywords.has(product::Keyword::Bane));
    EXPECT(context, hound.board().instance(*deploy_hound.source).keywords.has(product::Keyword::Rush));
    expect_invariants(context, hound);

    product::ProductGame binder(semantic_catalog(), semantic_config(1,
        {"AP-04", "SEM-BURN-1", "SEM-FILL-9", "SEM-FILL-9"}));
    EXPECT(context, binder.start());
    finish_mulligan(binder);
    pass_to_owner_turn(binder, 6);
    (void)play_from_hand(binder, "AP-04", product::ActionKind::PlayAmulet, 3U);
    pass_to_owner_turn(binder, 8);
    auto deploy_binder = command(binder, PlayerId::Player0, product::ActionKind::Deploy);
    deploy_binder.source = find_card(binder, PlayerId::Player0, product::Zone::Standby, "AP-S03");
    deploy_binder.slot = 3U;
    EXPECT_GAME_CODE(context, binder.plan_command(deploy_binder).status, product::ProductGameError::DeploymentUnavailable);
    (void)play_from_hand(binder, "SEM-BURN-1", product::ActionKind::CastSpell, 0U);
    EXPECT(context, binder.resources(PlayerId::Player0).cracks == 2);
    deploy_binder.expected_revision = binder.revision();
    EXPECT(context, binder.submit_command(deploy_binder));
    EXPECT(context, binder.resources(PlayerId::Player0).cracks == 1);
    EXPECT(context, binder.board().player(PlayerId::Player0).main_board[3] == deploy_binder.source);
    expect_invariants(context, binder);
}

void test_locked_dragon_requires_both_debt_and_low_health(TestContext& context) {
    auto config = semantic_config(1, {"SEM-BURN-6", "SEM-FILL-9", "SEM-FILL-9", "SEM-FILL-8"});
    config.main_decks[1].back() = "SEM-TARGET";
    product::ProductGame game(semantic_catalog(), std::move(config));
    EXPECT(context, game.start());
    finish_mulligan(game);
    const auto enemy = prepare_enemy_permanent(game, "SEM-TARGET", product::ActionKind::PlayFollower);
    for (int count = 0; count < 5; ++count) {
        EXPECT(context, game.submit_command(command(game, PlayerId::Player0, product::ActionKind::EndTurn)));
        auto attack = command(game, PlayerId::Player1, product::ActionKind::Attack);
        attack.source = enemy;
        EXPECT(context, game.submit_command(attack));
        EXPECT(context, game.submit_command(command(game, PlayerId::Player1, product::ActionKind::EndTurn)));
    }
    EXPECT(context, game.board().player(PlayerId::Player0).leader_health == 15);
    auto deploy = command(game, PlayerId::Player0, product::ActionKind::Deploy);
    deploy.source = find_card(game, PlayerId::Player0, product::Zone::Standby, "AP-S02");
    deploy.slot = 1U;
    EXPECT_GAME_CODE(context, game.plan_command(deploy).status, product::ProductGameError::DeploymentUnavailable);
    (void)play_from_hand(game, "SEM-BURN-6", product::ActionKind::CastSpell, 0U);
    deploy.expected_revision = game.revision();
    EXPECT(context, game.submit_command(deploy));
    for (const auto keyword : {product::Keyword::Ward, product::Keyword::Barrier, product::Keyword::Lifesteal}) {
        EXPECT(context, game.board().instance(*deploy.source).keywords.has(keyword));
    }
    expect_invariants(context, game);
}

void test_locked_campfire_only_expires_not_early_destruction(TestContext& context) {
    for (const bool destroyed : {false, true}) {
        auto config = semantic_config(0, {"NT-03", "SEM-FILL-9", "SEM-FILL-9", "SEM-FILL-8"});
        config.main_decks[1].back() = "NT-04";
        product::ProductGame game(semantic_catalog(), std::move(config));
        EXPECT(context, game.start());
        finish_mulligan(game);
        pass_to_owner_turn(game, 5);
        const auto fire = play_from_hand(game, "NT-03", product::ActionKind::PlayAmulet, 2U);
        const auto before = game.board().player(PlayerId::Player0).hand.size();
        EXPECT(context, game.submit_command(command(game, PlayerId::Player0, product::ActionKind::EndTurn)));
        if (destroyed) {
            auto removal = command(game, PlayerId::Player1, product::ActionKind::CastSpell);
            removal.source = find_card(game, PlayerId::Player1, product::Zone::Hand, "NT-04");
            removal.slot = 1U;
            removal.mode_id = "destroy_amulet_or_field";
            removal.target = fire;
            EXPECT(context, game.submit_command(removal));
            EXPECT(context, game.board().player(PlayerId::Player0).hand.size() == before);
        }
        EXPECT(context, game.submit_command(command(game, PlayerId::Player1, product::ActionKind::EndTurn)));
        EXPECT(context, game.board().instance(fire).zone == product::Zone::Graveyard);
        EXPECT(context, game.board().player(PlayerId::Player0).hand.size() == before + (destroyed ? 1U : 2U));
        expect_invariants(context, game);
    }
}

void test_locked_healers_use_actual_repair_and_evolution_continues(TestContext& context) {
    for (const std::string id : {"LO-02", "LO-09"}) {
        auto config = semantic_config(0, {id, "SEM-BURN-1", "SEM-BURN-1", "SEM-FILL-9"});
        config.main_decks[1].back() = "SEM-TARGET";
        product::ProductGame game(semantic_catalog(), std::move(config));
        EXPECT(context, game.start());
        finish_mulligan(game);
        const auto enemy = prepare_enemy_permanent(game, "SEM-TARGET", product::ActionKind::PlayFollower);
        EXPECT(context, game.submit_command(command(game, PlayerId::Player0, product::ActionKind::EndTurn)));
        auto hit = command(game, PlayerId::Player1, product::ActionKind::Attack);
        hit.source = enemy;
        EXPECT(context, game.submit_command(hit));
        EXPECT(context, game.submit_command(command(game, PlayerId::Player1, product::ActionKind::EndTurn)));
        pass_to_owner_turn(game, 7);
        EXPECT(context, game.board().player(PlayerId::Player0).leader_health == 23);
        (void)play_from_hand(game, "SEM-BURN-1", product::ActionKind::CastSpell, 0U);
        const auto card = play_from_hand(game, id, product::ActionKind::PlayFollower, 0U);
        EXPECT(context, game.resources(PlayerId::Player0).cracks == 0);
        EXPECT(context, game.board().player(PlayerId::Player0).leader_health == (id == "LO-02" ? 25 : 24));
        if (id == "LO-02") {
            pass_to_owner_turn(game, 8);
            (void)play_from_hand(game, "SEM-BURN-1", product::ActionKind::CastSpell, 0U);
            auto evolve = command(game, PlayerId::Player0, product::ActionKind::Evolve);
            evolve.source = card;
            EXPECT(context, game.submit_command(evolve));
            EXPECT(context, game.resources(PlayerId::Player0).cracks == 0);
            EXPECT(context, game.board().instance(card).evolved);
        }
        expect_invariants(context, game);
    }
}

void test_locked_last_words_debt_and_neutral_outnumbered_rush(TestContext& context) {
    for (const bool debt : {false, true}) {
        auto config = semantic_config(1, {"AP-01", "SEM-BURN-2", "SEM-FILL-9", "SEM-FILL-9"});
        config.main_decks[1].back() = "SEM-TARGET";
        product::ProductGame game(semantic_catalog(), std::move(config));
        EXPECT(context, game.start());
        finish_mulligan(game);
        const auto enemy = prepare_enemy_permanent(game, "SEM-TARGET", product::ActionKind::PlayFollower);
        pass_to_owner_turn(game, 3);
        if (debt) {
            (void)play_from_hand(game, "SEM-BURN-2", product::ActionKind::CastSpell, 0U);
        }
        const auto familiar = play_from_hand(game, "AP-01", product::ActionKind::PlayFollower, 0U);
        const auto hand = game.board().player(PlayerId::Player0).hand.size();
        EXPECT(context, game.submit_command(command(game, PlayerId::Player0, product::ActionKind::EndTurn)));
        auto hit = command(game, PlayerId::Player1, product::ActionKind::Attack);
        hit.source = enemy;
        hit.target = familiar;
        EXPECT(context, game.submit_command(hit));
        EXPECT(context, game.board().instance(familiar).zone == product::Zone::Graveyard);
        EXPECT(context, game.board().player(PlayerId::Player0).hand.size() == hand + (debt ? 1U : 0U));
        expect_invariants(context, game);
    }
    auto config = semantic_config(0, {"NT-01", "SEM-FILL-9", "SEM-FILL-9", "SEM-FILL-8"});
    config.main_decks[1].back() = "SEM-TARGET";
    config.main_decks[1][28] = "SEM-TARGET";
    product::ProductGame game(semantic_catalog(), std::move(config));
    EXPECT(context, game.start());
    finish_mulligan(game);
    EXPECT(context, game.submit_command(command(game, PlayerId::Player0, product::ActionKind::EndTurn)));
    for (std::size_t slot = 0; slot < 2; ++slot) {
        auto play = command(game, PlayerId::Player1, product::ActionKind::PlayFollower);
        play.source = find_card(game, PlayerId::Player1, product::Zone::Hand, "SEM-TARGET");
        play.slot = slot;
        EXPECT(context, game.submit_command(play));
    }
    EXPECT(context, game.submit_command(command(game, PlayerId::Player1, product::ActionKind::EndTurn)));
    const auto ranger = play_from_hand(game, "NT-01", product::ActionKind::PlayFollower, 0U);
    EXPECT(context, game.board().instance(ranger).keywords.has(product::Keyword::Rush));
    expect_invariants(context, game);
}

void test_locked_debt_four_book_draws_two_and_repair_mode_reduces_two(TestContext& context) {
    product::ProductGame game(semantic_catalog(), semantic_config(1,
        {"AP-04", "SEM-BURN-6", "AP-08", "SEM-FILL-9"}));
    EXPECT(context, game.start());
    finish_mulligan(game);
    pass_to_owner_turn(game, 7);
    (void)play_from_hand(game, "SEM-BURN-6", product::ActionKind::CastSpell, 0U);
    pass_to_owner_turn(game, 8);
    const auto book = play_from_hand(game, "AP-04", product::ActionKind::PlayAmulet, 0U);
    pass_to_owner_turn(game, 10);
    const auto cursor = game.events().back().sequence;
    pass_to_owner_turn(game, 11);
    EXPECT(context, game.board().instance(book).zone == product::Zone::Graveyard);
    const auto events = game.read_events(cursor);
    EXPECT(context, std::count_if(events.begin(), events.end(), [](const auto& event) {
        return event.player == PlayerId::Player0 &&
            (event.kind == product::ProductEventKind::CardDrawn || event.kind == product::ProductEventKind::CardArchived);
    }) == 3); // one turn draw + the book's two effect draws, including overflow.
    auto repair = command(game, PlayerId::Player0, product::ActionKind::CastSpell);
    repair.source = find_card(game, PlayerId::Player0, product::Zone::Hand, "AP-08");
    repair.slot = 2U;
    repair.mode_id = "repair";
    const auto before = game.resources(PlayerId::Player0).cracks;
    EXPECT(context, game.submit_command(repair));
    EXPECT(context, game.resources(PlayerId::Player0).cracks == before - 2);
    expect_invariants(context, game);
}

void test_locked_empower_rejects_neutral_and_preserves_failure_atomicity(TestContext& context) {
    product::ProductGame game(product::make_locked_product_catalog(),
        locked_config(1, {"AP-01", "NT-01", "AP-08", "AP-08"}));
    EXPECT(context, game.start());
    finish_mulligan(game);
    const InstanceId pact = play_from_hand(game, "AP-01", product::ActionKind::PlayFollower, 0U);
    pass_to_owner_turn(game, 2);
    const InstanceId neutral = play_from_hand(game, "NT-01", product::ActionKind::PlayFollower, 1U);
    pass_to_owner_turn(game, 3);

    auto empower = command(game, PlayerId::Player0, product::ActionKind::CastSpell);
    empower.source = find_card(game, PlayerId::Player0, product::Zone::Hand, "AP-08");
    empower.slot = 2U;
    empower.mode_id = "empower";
    empower.target = neutral;
    const auto revision = game.revision();
    const auto event_count = game.events().size();
    const auto move_count = game.board().moves().size();
    const auto pp = game.resources(PlayerId::Player0).current_pp;
    EXPECT_GAME_CODE(context, game.plan_command(empower).status, product::ProductGameError::InvalidTarget);
    EXPECT_GAME_CODE(context, game.submit_command(empower), product::ProductGameError::InvalidTarget);
    EXPECT(context, game.revision() == revision);
    EXPECT(context, game.events().size() == event_count);
    EXPECT(context, game.board().moves().size() == move_count);
    EXPECT(context, game.resources(PlayerId::Player0).current_pp == pp);
    EXPECT(context, game.board().instance(*empower.source).zone == product::Zone::Hand);
    const auto legal = game.list_legal_actions(PlayerId::Player0);
    EXPECT(context, std::none_of(legal.begin(), legal.end(), [&](const auto& action) {
        return action.command.source == empower.source && action.command.target == neutral;
    }));
    empower.target = pact;
    const auto preview = game.plan_command(empower);
    EXPECT(context, preview);
    EXPECT(context, game.submit_command(empower));
    EXPECT(context, game.revision() == revision + 1U);
    EXPECT(context, game.resources(PlayerId::Player0).current_pp == preview.payment.current_pp_after);
    EXPECT(context, game.board().instance(pact).current_attack == 3);
    EXPECT(context, game.board().instance(pact).current_health == 4);
    EXPECT(context, game.board().instance(pact).keywords.has(product::Keyword::Barrier));
    EXPECT(context, game.board().instance(neutral).current_attack == 2);
    EXPECT(context, game.board().instance(*empower.source).zone == product::Zone::Graveyard);
    EXPECT(context, game.phase() == product::ProductGamePhase::Main);
    expect_invariants(context, game);
}

void test_locked_field_repair_opens_fresh_target_choice_and_resumes(TestContext& context) {
    product::ProductGame game(product::make_locked_product_catalog(),
        locked_config(0, {"LO-04", "LO-10", "LO-02", "NT-01"}));
    EXPECT(context, game.start());
    finish_mulligan(game);
    const InstanceId knight = play_from_hand(game, "LO-04", product::ActionKind::PlayFollower, 0U, true);
    EXPECT(context, game.resources(PlayerId::Player0).cracks == 1);
    EXPECT(context, !game.board().instance(knight).keywords.has(product::Keyword::Barrier));
    pass_to_owner_turn(game, 4);
    const InstanceId field = play_from_hand(game, "LO-10", product::ActionKind::PlayField, std::nullopt);
    pass_to_owner_turn(game, 5);
    const InstanceId neutral = play_from_hand(game, "NT-01", product::ActionKind::PlayFollower, 1U);
    const InstanceId priest = play_from_hand(game, "LO-02", product::ActionKind::PlayFollower, 2U);
    EXPECT(context, game.resources(PlayerId::Player0).cracks == 0);
    EXPECT(context, game.phase() == product::ProductGamePhase::Choice);
    EXPECT(context, game.pending_choice().has_value());
    if (!game.pending_choice()) {
        return;
    }
    EXPECT(context, game.pending_choice()->chooser == PlayerId::Player0);
    EXPECT(context, game.pending_choice()->options.size() == 2U);
    for (const auto& option : game.pending_choice()->options) {
        EXPECT(context, option.card == knight || option.card == priest);
        EXPECT(context, option.card != neutral && option.card != field);
    }
    const auto revision = game.revision();
    const auto event_count = game.events().size();
    EXPECT_GAME_CODE(context,
        game.submit_command(command(game, PlayerId::Player0, product::ActionKind::EndTurn)),
        product::ProductGameError::ChoicePending);
    EXPECT(context, game.revision() == revision && game.events().size() == event_count);
    auto resolve = command(game, PlayerId::Player0, product::ActionKind::ResolveChoice);
    resolve.choice_id = game.pending_choice()->choice_id;
    for (const auto& option : game.pending_choice()->options) {
        if (option.card == knight) {
            resolve.selected_option_ids = {option.option_id};
        }
    }
    EXPECT(context, game.submit_command(resolve));
    EXPECT(context, game.revision() == revision + 1U);
    EXPECT(context, game.phase() == product::ProductGamePhase::Main);
    EXPECT(context, !game.pending_choice());
    EXPECT(context, game.board().instance(knight).current_attack == 3);
    EXPECT(context, game.board().instance(knight).current_health == 3);
    EXPECT(context, game.board().instance(knight).keywords.has(product::Keyword::Barrier));
    EXPECT(context, game.board().instance(priest).current_attack == 2);
    EXPECT(context, game.board().instance(neutral).current_attack == 2);
    EXPECT(context, game.board().player(PlayerId::Player0).field == field);
    expect_invariants(context, game);
}

void test_deployment_once_per_turn_failed_cost_does_not_consume(TestContext& context) {
    auto config = make_config();
    config.standby_decks = {{{std::string(kStandby), std::string(kArchiveStandby)},
        {std::string(kStandby), std::string(kArchiveStandby)}}};
    config.required_standby_size = 2;
    product::ProductGame game(make_game_catalog(), std::move(config));
    EXPECT(context, game.start());
    finish_mulligan(game);
    auto invalid = command(game, PlayerId::Player0, product::ActionKind::Deploy);
    invalid.source = find_card(game, PlayerId::Player0, product::Zone::Standby, kArchiveStandby);
    invalid.slot = 0U;
    EXPECT_GAME_CODE(context, game.submit_command(invalid), product::ProductGameError::InvalidSelection);
    EXPECT(context, !game.resources(PlayerId::Player0).deploy_used_this_turn);
    auto deploy = invalid;
    deploy.source = find_card(game, PlayerId::Player0, product::Zone::Standby, kStandby);
    EXPECT(context, game.submit_command(deploy));
    EXPECT(context, game.resources(PlayerId::Player0).deploy_used_this_turn);
    invalid.expected_revision = game.revision();
    invalid.additional_cost_cards = {*deploy.source};
    const auto revision = game.revision();
    const auto events = game.events().size();
    EXPECT_GAME_CODE(context, game.submit_command(invalid), product::ProductGameError::DeploymentUnavailable);
    EXPECT(context, game.revision() == revision && game.events().size() == events);
    EXPECT(context, game.board().instance(*deploy.source).zone == product::Zone::MainBoard);
    const auto legal = game.list_legal_actions(PlayerId::Player0);
    EXPECT(context, std::none_of(legal.begin(), legal.end(), [](const auto& action) {
        return action.command.action == product::ActionKind::Deploy;
    }));
    pass_to_owner_turn(game, 2);
    EXPECT(context, !game.resources(PlayerId::Player0).deploy_used_this_turn);
    invalid.expected_revision = game.revision();
    EXPECT(context, game.submit_command(invalid));
    EXPECT(context, game.board().instance(*deploy.source).zone == product::Zone::Archive);
    EXPECT(context, game.board().moves().back().card == invalid.source);
    EXPECT(context, game.resources(PlayerId::Player0).deploy_used_this_turn);
    expect_invariants(context, game);
}

void test_destroyed_standby_combat_archives_without_last_words(TestContext& context) {
    auto catalog = make_game_catalog();
    auto standby = catalog.at(kStandby);
    standby.identity.design_id = "GAME-STANDBY-LASTWORDS";
    standby.attack = 2;
    standby.health = 1;
    standby.effects = catalog.at(kLastWords).effects;
    catalog.add(std::move(standby));
    auto config = make_config();
    config.standby_decks = {{{"GAME-STANDBY-LASTWORDS"}, {"GAME-STANDBY-LASTWORDS"}}};
    product::ProductGame game(std::move(catalog), std::move(config));
    EXPECT(context, game.start());
    finish_mulligan(game);
    const auto deploy_for = [&](const PlayerId player) {
        auto deploy = command(game, player, product::ActionKind::Deploy);
        deploy.source = find_card(game, player, product::Zone::Standby, "GAME-STANDBY-LASTWORDS");
        deploy.slot = 0U;
        EXPECT(context, game.submit_command(deploy));
        return *deploy.source;
    };
    const auto attacker = deploy_for(PlayerId::Player0);
    EXPECT(context, game.submit_command(command(game, PlayerId::Player0, product::ActionKind::EndTurn)));
    const auto defender = deploy_for(PlayerId::Player1);
    EXPECT(context, game.submit_command(command(game, PlayerId::Player1, product::ActionKind::EndTurn)));
    const auto hand0 = game.board().player(PlayerId::Player0).hand.size();
    const auto hand1 = game.board().player(PlayerId::Player1).hand.size();
    auto attack = command(game, PlayerId::Player0, product::ActionKind::Attack);
    attack.source = attacker;
    attack.target = defender;
    EXPECT(context, game.submit_command(attack));
    EXPECT(context, game.board().instance(attacker).zone == product::Zone::Archive);
    EXPECT(context, game.board().instance(defender).zone == product::Zone::Archive);
    EXPECT(context, game.board().player(PlayerId::Player0).hand.size() == hand0);
    EXPECT(context, game.board().player(PlayerId::Player1).hand.size() == hand1);
    for (const auto card : {attacker, defender}) {
        const auto& moves = game.board().moves();
        const auto moved = std::find_if(moves.rbegin(), moves.rend(), [&](const auto& move) {
            return move.card == card;
        });
        EXPECT(context, moved != moves.rend());
        EXPECT(context, moved->from == product::Zone::MainBoard);
        EXPECT(context, moved->to == product::Zone::Archive);
        EXPECT(context, moved->reason == product::MoveReason::Destroyed);
        EXPECT(context, moved->destroyed);
    }
    expect_invariants(context, game);
}

void test_locked_amulet_repair_expiry_and_token_original_slot(TestContext& context) {
    product::ProductGame game(product::make_locked_product_catalog(),
        locked_config(0, {"LO-04", "LO-03", "NT-01", "LO-05"}));
    EXPECT(context, game.start());
    finish_mulligan(game);
    (void)play_from_hand(game, "LO-04", product::ActionKind::PlayFollower, 0U, true);
    pass_to_owner_turn(game, 3);
    const auto bell = play_from_hand(game, "LO-03", product::ActionKind::PlayAmulet, 4U);
    EXPECT(context, game.resources(PlayerId::Player0).cracks == 0);
    EXPECT(context, game.board().instance(bell).countdown == 2);
    EXPECT(context, game.resources(PlayerId::Player0).evolution_energy == 0);
    pass_to_owner_turn(game, 4);
    EXPECT(context, game.board().instance(bell).countdown == 1);
    pass_to_owner_turn(game, 5);
    EXPECT(context, game.board().instance(bell).zone == product::Zone::Graveyard);
    EXPECT(context, game.board().player(PlayerId::Player0).main_board[4].has_value());
    const auto token = *game.board().player(PlayerId::Player0).main_board[4];
    EXPECT(context, game.board().instance(token).design_id == "LO-T01");
    EXPECT(context, game.board().instance(token).current_attack == 3);
    EXPECT(context, game.board().instance(token).current_health == 3);
    EXPECT(context, game.board().instance(token).keywords.has(product::Keyword::Ward));
    EXPECT(context, game.resources(PlayerId::Player0).evolution_energy == 2);
    const auto& moves = game.board().moves();
    const auto expiry = std::find_if(moves.begin(), moves.end(), [&](const auto& move) {
        return move.card == bell && move.reason == product::MoveReason::CountdownExpired;
    });
    EXPECT(context, expiry != moves.end());
    EXPECT(context, expiry->destroyed);
    EXPECT(context, std::next(expiry) != moves.end() && std::next(expiry)->card == token);
    auto deploy = command(game, PlayerId::Player0, product::ActionKind::Deploy);
    deploy.source = find_card(game, PlayerId::Player0, product::Zone::Standby, "LO-S03");
    deploy.slot = 1U;
    EXPECT(context, game.submit_command(deploy));
    EXPECT(context, game.board().instance(*deploy.source).current_attack == 5);
    EXPECT(context, game.board().instance(*deploy.source).current_health == 7);
    EXPECT(context, game.board().instance(*deploy.source).keywords.has(product::Keyword::Barrier));
    expect_invariants(context, game);
}

void test_locked_book_does_not_retroactively_trigger_own_burn(TestContext& context) {
    product::ProductGame game(product::make_locked_product_catalog(),
        locked_config(1, {"AP-04", "AP-02", "AP-01", "AP-08"}));
    EXPECT(context, game.start());
    finish_mulligan(game);
    const auto book = play_from_hand(game, "AP-04", product::ActionKind::PlayAmulet, 3U);
    EXPECT(context, game.resources(PlayerId::Player0).cracks == 1);
    EXPECT(context, game.board().instance(book).countdown == 3);
    pass_to_owner_turn(game, 2);
    EXPECT(context, game.board().instance(book).countdown == 2);
    pass_to_owner_turn(game, 3);
    EXPECT(context, game.board().instance(book).countdown == 1);
    const auto before = game.board().player(PlayerId::Player0).hand.size();
    const auto student = play_from_hand(game, "AP-02", product::ActionKind::PlayFollower, 0U);
    EXPECT(context, game.resources(PlayerId::Player0).cracks == 2);
    EXPECT(context, game.board().instance(book).zone == product::Zone::Graveyard);
    EXPECT(context, game.board().player(PlayerId::Player0).hand.size() == before);
    EXPECT(context, !game.board().instance(student).keywords.has(product::Keyword::Barrier));
    EXPECT(context, game.phase() == product::ProductGamePhase::Main);
    expect_invariants(context, game);
}

void test_start_mulligan_turn_and_query_contract(TestContext& context) {
    product::ProductGame game(make_game_catalog(), make_config());
    EXPECT(context, game.phase() == product::ProductGamePhase::NotStarted);
    EXPECT(context, game.start());
    EXPECT(context, game.revision() == 1U);
    EXPECT(context, game.phase() == product::ProductGamePhase::Mulligan);
    EXPECT(context, game.first_player() == PlayerId::Player0);
    EXPECT(context, game.board().player(PlayerId::Player0).hand.size() == 4U);
    EXPECT(context, game.board().player(PlayerId::Player1).hand.size() == 4U);
    EXPECT(context, game.list_legal_actions(PlayerId::Player0).size() == 17U);

    auto p0 = command(game, PlayerId::Player0, product::ActionKind::Mulligan);
    const InstanceId returned = game.board().player(PlayerId::Player0).hand.front();
    p0.selected_cards = {returned};
    EXPECT(context, game.submit_command(p0));
    EXPECT(context, game.board().instance(returned).zone == product::Zone::Deck);
    EXPECT(context, game.revision() == 2U);
    EXPECT(context, game.phase() == product::ProductGamePhase::Mulligan);

    auto repeated = command(game, PlayerId::Player0, product::ActionKind::Mulligan);
    const std::size_t repeated_events = game.events().size();
    EXPECT_GAME_CODE(context,
        game.submit_command(repeated),
        product::ProductGameError::MulliganAlreadyDone);
    EXPECT(context, game.revision() == 2U);
    EXPECT(context, game.events().size() == repeated_events);

    const std::uint64_t before_revision = game.revision();
    const std::size_t before_events = game.events().size();
    EXPECT_GAME_CODE(context, game.submit_command(p0), product::ProductGameError::StaleRevision);
    EXPECT(context, game.revision() == before_revision);
    EXPECT(context, game.events().size() == before_events);

    auto p1 = command(game, PlayerId::Player1, product::ActionKind::Mulligan);
    EXPECT(context, game.submit_command(p1));
    EXPECT(context, game.phase() == product::ProductGamePhase::Main);
    EXPECT(context, game.active_player() == PlayerId::Player0);
    EXPECT(context, game.resources(PlayerId::Player0).current_pp == 1);
    EXPECT(context, game.resources(PlayerId::Player0).pp_capacity == 1);
    EXPECT(context, game.resources(PlayerId::Player0).own_turn_number == 1);

    const auto legal = game.list_legal_actions(PlayerId::Player0);
    EXPECT(context, !legal.empty());
    for (const product::ProductLegalAction& action : legal) {
        EXPECT(context, game.plan_command(action.command));
        EXPECT(context, action.command.expected_revision == game.revision());
    }
    EXPECT(context, game.list_legal_actions(PlayerId::Player1).size() == 1U);
    expect_invariants(context, game);
}

void test_payment_play_zones_and_failure_atomicity(TestContext& context) {
    product::ProductGame game(make_game_catalog(), make_config());
    EXPECT(context, game.start());
    finish_mulligan(game);

    const InstanceId follower = find_card(game, PlayerId::Player0, product::Zone::Hand, kFollower);
    auto play = command(game, PlayerId::Player0, product::ActionKind::PlayFollower);
    play.source = follower;
    play.slot = 2U;
    EXPECT(context, game.submit_command(play));
    EXPECT(context, game.board().player(PlayerId::Player0).main_board[2] == follower);
    EXPECT(context, game.resources(PlayerId::Player0).current_pp == 0);

    const InstanceId spell = find_card(game, PlayerId::Player0, product::Zone::Hand, kChoiceSpell);
    auto invalid = command(game, PlayerId::Player0, product::ActionKind::CastSpell);
    invalid.source = spell;
    invalid.slot = 0U;
    invalid.target = follower; // friendly, while the spell requires enemy.
    const std::uint64_t revision = game.revision();
    const std::size_t events = game.events().size();
    EXPECT_GAME_CODE(context, game.submit_command(invalid), product::ProductGameError::InvalidTarget);
    EXPECT(context, game.revision() == revision);
    EXPECT(context, game.events().size() == events);
    EXPECT(context, game.board().instance(spell).zone == product::Zone::Hand);

    auto end = command(game, PlayerId::Player0, product::ActionKind::EndTurn);
    EXPECT(context, game.submit_command(end));
    EXPECT(context, game.resources(PlayerId::Player0).current_pp == 0);
    EXPECT(context, game.active_player() == PlayerId::Player1);

    const InstanceId standby = find_card(game, PlayerId::Player1, product::Zone::Standby, kStandby);
    auto deploy = command(game, PlayerId::Player1, product::ActionKind::Deploy);
    deploy.source = standby;
    deploy.slot = 4U;
    EXPECT(context, game.submit_command(deploy));
    EXPECT(context, game.board().player(PlayerId::Player1).main_board[4] == standby);
    EXPECT(context, game.board().instance(standby).zone == product::Zone::MainBoard);
    expect_invariants(context, game);
}

void test_optional_targets_and_deferred_trigger_targets_are_queried_correctly(TestContext& context) {
    product::ProductGameConfig config = make_config();
    config.main_decks[0].back() = std::string(kOptionalTarget);
    product::ProductGame game(make_game_catalog(), std::move(config));
    EXPECT(context, game.start());
    finish_mulligan(game);
    EXPECT(context, game.mulligan_complete(PlayerId::Player0));
    EXPECT(context, game.mulligan_complete(PlayerId::Player1));

    EXPECT(context, game.submit_command(command(game, PlayerId::Player0, product::ActionKind::EndTurn)));
    const InstanceId victim = find_card(game, PlayerId::Player1, product::Zone::Hand, kFollower);
    auto play_victim = command(game, PlayerId::Player1, product::ActionKind::PlayFollower);
    play_victim.source = victim;
    play_victim.slot = 0U;
    EXPECT(context, game.submit_command(play_victim));
    EXPECT(context, game.submit_command(command(game, PlayerId::Player1, product::ActionKind::EndTurn)));

    const InstanceId optional = find_card(game, PlayerId::Player0, product::Zone::Hand, kOptionalTarget);
    const auto legal = game.list_legal_actions(PlayerId::Player0);
    const auto is_optional_play = [&](const product::ProductLegalAction& action) {
        return action.command.action == product::ActionKind::PlayFollower &&
            action.command.source == optional && action.command.slot == 0U;
    };
    const auto declined = std::find_if(legal.begin(), legal.end(), [&](const auto& action) {
        return is_optional_play(action) && !action.command.target.has_value();
    });
    EXPECT(context, declined != legal.end());
    const auto targeted = std::find_if(legal.begin(), legal.end(), [&](const auto& action) {
        return is_optional_play(action) && action.command.target == victim;
    });
    EXPECT(context, targeted != legal.end());
    product::ProductGame declined_game = game;
    EXPECT(context, declined_game.submit_command(declined->command));
    EXPECT(context, declined_game.phase() == product::ProductGamePhase::Main);
    EXPECT(context, !declined_game.pending_choice().has_value());
    EXPECT(context, declined_game.board().instance(victim).current_health == 2);
    EXPECT(context, game.submit_command(targeted->command));
    EXPECT(context, game.board().instance(victim).current_health == 1);
    expect_invariants(context, game);

    product::ProductGameConfig deferred_config = make_config();
    deferred_config.main_decks[0].back() = std::string(kGlobalTargetField);
    product::ProductGame deferred(make_game_catalog(), std::move(deferred_config));
    EXPECT(context, deferred.start());
    finish_mulligan(deferred);
    const InstanceId field = find_card(
        deferred, PlayerId::Player0, product::Zone::Hand, kGlobalTargetField);
    auto play_field = command(deferred, PlayerId::Player0, product::ActionKind::PlayField);
    play_field.source = field;
    EXPECT(context, deferred.plan_command(play_field));
    play_field.target = deferred.board().player(PlayerId::Player0).hand.front();
    EXPECT_GAME_CODE(context, deferred.plan_command(play_field).status, product::ProductGameError::InvalidTarget);
}

void test_deployment_additional_cost_can_vacate_selected_slot(TestContext& context) {
    product::ProductGameConfig config = make_config();
    config.standby_decks[0] = {std::string(kArchiveStandby)};
    product::ProductGame game(make_game_catalog(), std::move(config));
    EXPECT(context, game.start());
    finish_mulligan(game);

    const InstanceId follower = find_card(game, PlayerId::Player0, product::Zone::Hand, kFollower);
    auto play = command(game, PlayerId::Player0, product::ActionKind::PlayFollower);
    play.source = follower;
    play.slot = 2U;
    EXPECT(context, game.submit_command(play));
    EXPECT(context, game.submit_command(command(game, PlayerId::Player0, product::ActionKind::EndTurn)));
    EXPECT(context, game.submit_command(command(game, PlayerId::Player1, product::ActionKind::EndTurn)));

    const InstanceId standby = find_card(game, PlayerId::Player0, product::Zone::Standby, kArchiveStandby);
    const auto legal = game.list_legal_actions(PlayerId::Player0);
    const auto deploy = std::find_if(legal.begin(), legal.end(), [&](const product::ProductLegalAction& action) {
        return action.command.action == product::ActionKind::Deploy &&
            action.command.source == standby && action.command.slot == 2U &&
            action.command.additional_cost_cards == std::vector<InstanceId>{follower};
    });
    EXPECT(context, deploy != legal.end());
    EXPECT(context, game.submit_command(deploy->command));
    EXPECT(context, game.board().player(PlayerId::Player0).main_board[2] == standby);
    EXPECT(context, game.board().instance(follower).zone == product::Zone::Archive);
    expect_invariants(context, game);
}

void test_reaction_cancel_lifo_and_attack_spent(TestContext& context) {
    product::ProductGame game(make_game_catalog(), make_config());
    EXPECT(context, game.start());
    finish_mulligan(game);

    const InstanceId follower = find_card(game, PlayerId::Player0, product::Zone::Hand, kFollower);
    auto play = command(game, PlayerId::Player0, product::ActionKind::PlayFollower);
    play.source = follower;
    play.slot = 0U;
    EXPECT(context, game.submit_command(play));
    EXPECT(context, game.submit_command(command(game, PlayerId::Player0, product::ActionKind::EndTurn)));

    const InstanceId trap = find_card(game, PlayerId::Player1, product::Zone::Hand, kTrap);
    auto set = command(game, PlayerId::Player1, product::ActionKind::PlayTrap);
    set.source = trap;
    set.slot = 0U;
    EXPECT(context, game.submit_command(set));
    EXPECT(context, game.submit_command(command(game, PlayerId::Player1, product::ActionKind::EndTurn)));

    const int leader_before = game.board().player(PlayerId::Player1).leader_health;
    auto attack = command(game, PlayerId::Player0, product::ActionKind::Attack);
    attack.source = follower;
    EXPECT(context, game.submit_command(attack));
    EXPECT(context, game.phase() == product::ProductGamePhase::Reaction);
    EXPECT(context, game.reaction_context().priority == PlayerId::Player1);
    EXPECT(context, game.board().instance(follower).attacked_this_turn);

    auto invalid_target = command(game, PlayerId::Player1, product::ActionKind::ActivateTrap);
    invalid_target.source = trap;
    invalid_target.target = follower;
    const std::uint64_t reaction_revision = game.revision();
    const std::size_t reaction_events = game.events().size();
    EXPECT_GAME_CODE(context,
        game.submit_command(invalid_target),
        product::ProductGameError::InvalidTarget);
    EXPECT(context, game.revision() == reaction_revision);
    EXPECT(context, game.events().size() == reaction_events);

    auto invalid_mode = command(game, PlayerId::Player1, product::ActionKind::ActivateTrap);
    invalid_mode.source = trap;
    invalid_mode.mode_id = "not-a-trap-mode";
    EXPECT_GAME_CODE(context,
        game.submit_command(invalid_mode),
        product::ProductGameError::InvalidMode);
    EXPECT(context, game.revision() == reaction_revision);
    EXPECT(context, game.events().size() == reaction_events);

    auto activate = command(game, PlayerId::Player1, product::ActionKind::ActivateTrap);
    activate.source = trap;
    EXPECT(context, game.submit_command(activate));
    EXPECT(context, game.reaction_context().chain_size == 1U);
    EXPECT(context, game.submit_command(command(game, PlayerId::Player0, product::ActionKind::PassReaction)));
    EXPECT(context, game.submit_command(command(game, PlayerId::Player1, product::ActionKind::PassReaction)));
    EXPECT(context, game.phase() == product::ProductGamePhase::Main);
    EXPECT(context, game.board().player(PlayerId::Player1).leader_health == leader_before);
    EXPECT(context, game.board().instance(trap).zone == product::Zone::Graveyard);
    EXPECT(context, std::count_if(game.events().begin(), game.events().end(), [](const auto& event) {
        return event.kind == product::ProductEventKind::AttackCancelled;
    }) == 1);
    expect_invariants(context, game);
}

void test_advance_payment_and_repair_resource_loop(TestContext& context) {
    product::ProductGame game(make_game_catalog(), make_config());
    EXPECT(context, game.start());
    finish_mulligan(game);
    const InstanceId advance = find_card(game, PlayerId::Player0, product::Zone::Hand, kAdvance);
    auto play = command(game, PlayerId::Player0, product::ActionKind::PlayFollower);
    play.source = advance;
    play.slot = 0U;
    EXPECT_GAME_CODE(context, game.submit_command(play), product::ProductGameError::InsufficientPP);
    play.expected_revision = game.revision();
    play.use_advance = true;
    EXPECT(context, game.submit_command(play));
    EXPECT(context, game.resources(PlayerId::Player0).current_pp == 0);
    EXPECT(context, game.resources(PlayerId::Player0).pp_capacity == 0);
    EXPECT(context, game.resources(PlayerId::Player0).cracks == 1);
    EXPECT(context, game.resources(PlayerId::Player0).future_used_this_turn);
    expect_invariants(context, game);
}

void test_countdown_original_slot_and_effect_last_words(TestContext& context) {
    product::ProductGameConfig countdown_config = make_config();
    countdown_config.main_decks[0].back() = std::string(kAmulet);
    product::ProductGame countdown_game(make_game_catalog(), countdown_config);
    EXPECT(context, countdown_game.start());
    finish_mulligan(countdown_game);
    const InstanceId amulet = find_card(
        countdown_game, PlayerId::Player0, product::Zone::Hand, kAmulet);
    auto play_amulet = command(countdown_game, PlayerId::Player0, product::ActionKind::PlayAmulet);
    play_amulet.source = amulet;
    play_amulet.slot = 3U;
    EXPECT(context, countdown_game.submit_command(play_amulet));
    EXPECT(context, countdown_game.submit_command(command(
        countdown_game, PlayerId::Player0, product::ActionKind::EndTurn)));
    EXPECT(context, countdown_game.submit_command(command(
        countdown_game, PlayerId::Player1, product::ActionKind::EndTurn)));
    EXPECT(context, countdown_game.board().instance(amulet).zone == product::Zone::Graveyard);
    EXPECT(context, countdown_game.board().player(PlayerId::Player0).main_board[3].has_value());
    const InstanceId token = *countdown_game.board().player(PlayerId::Player0).main_board[3];
    EXPECT(context, countdown_game.board().instance(token).design_id == kToken);
    expect_invariants(context, countdown_game);

    product::ProductGameConfig last_words_config = make_config();
    last_words_config.main_decks[0][last_words_config.main_decks[0].size() - 2U] = std::string(kSpell);
    last_words_config.main_decks[1].back() = std::string(kLastWords);
    product::ProductGame last_words_game(make_game_catalog(), std::move(last_words_config));
    EXPECT(context, last_words_game.start());
    finish_mulligan(last_words_game);
    EXPECT(context, last_words_game.submit_command(command(
        last_words_game, PlayerId::Player0, product::ActionKind::EndTurn)));
    const InstanceId victim = find_card(
        last_words_game, PlayerId::Player1, product::Zone::Hand, kLastWords);
    auto play_victim = command(last_words_game, PlayerId::Player1, product::ActionKind::PlayFollower);
    play_victim.source = victim;
    play_victim.slot = 0U;
    EXPECT(context, last_words_game.submit_command(play_victim));
    EXPECT(context, last_words_game.submit_command(command(
        last_words_game, PlayerId::Player1, product::ActionKind::EndTurn)));
    const InstanceId removal = find_card(
        last_words_game, PlayerId::Player0, product::Zone::Hand, kSpell);
    const std::size_t hand_before = last_words_game.board().player(PlayerId::Player1).hand.size();
    auto cast = command(last_words_game, PlayerId::Player0, product::ActionKind::CastSpell);
    cast.source = removal;
    cast.target = victim;
    cast.slot = 0U;
    EXPECT(context, last_words_game.submit_command(cast));
    EXPECT(context, last_words_game.board().instance(victim).zone == product::Zone::Graveyard);
    EXPECT(context, last_words_game.board().player(PlayerId::Player1).hand.size() == hand_before + 1U);
    expect_invariants(context, last_words_game);
}

void test_paid_choice_blocks_resumes_and_cleans_spell(TestContext& context) {
    product::ProductGame game(make_game_catalog(), make_config());
    EXPECT(context, game.start());
    finish_mulligan(game);

    const InstanceId choice_spell = find_card(game, PlayerId::Player0, product::Zone::Hand, kChoiceSpell);
    auto play = command(game, PlayerId::Player0, product::ActionKind::CastSpell);
    play.source = choice_spell;
    play.slot = 1U;
    EXPECT(context, game.submit_command(play));
    EXPECT(context, game.phase() == product::ProductGamePhase::Choice);
    EXPECT(context, game.pending_choice().has_value());
    EXPECT(context, game.board().instance(choice_spell).zone == product::Zone::Tactic);

    const std::uint64_t blocked_revision = game.revision();
    const std::size_t blocked_events = game.events().size();
    EXPECT_GAME_CODE(context,
        game.submit_command(command(game, PlayerId::Player0, product::ActionKind::EndTurn)),
        product::ProductGameError::ChoicePending);
    EXPECT(context, game.revision() == blocked_revision);
    EXPECT(context, game.events().size() == blocked_events);

    const product::PendingChoice& pending = *game.pending_choice();
    EXPECT(context, pending.minimum == 1U && pending.maximum == 1U);
    const auto choice_actions = game.list_legal_actions(PlayerId::Player0);
    EXPECT(context, choice_actions.size() == pending.options.size() + 1U);
    EXPECT(context, game.list_legal_actions(PlayerId::Player1).size() == 1U);

    auto wrong_owner = command(game, PlayerId::Player1, product::ActionKind::ResolveChoice);
    wrong_owner.choice_id = pending.choice_id;
    wrong_owner.selected_option_ids = {pending.options.front().option_id};
    EXPECT_GAME_CODE(context,
        game.submit_command(wrong_owner),
        product::ProductGameError::ChoiceNotOwned);
    EXPECT(context, game.revision() == blocked_revision);
    EXPECT(context, game.events().size() == blocked_events);

    auto wrong_token = command(game, PlayerId::Player0, product::ActionKind::ResolveChoice);
    wrong_token.choice_id = pending.choice_id + 1U;
    wrong_token.selected_option_ids = {pending.options.front().option_id};
    EXPECT_GAME_CODE(context,
        game.submit_command(wrong_token),
        product::ProductGameError::InvalidSelection);
    EXPECT(context, game.revision() == blocked_revision);
    EXPECT(context, game.events().size() == blocked_events);

    product::ProductGame surrendered = game;
    auto surrender = command(surrendered, PlayerId::Player1, product::ActionKind::Surrender);
    EXPECT(context, surrendered.submit_command(surrender));
    EXPECT(context, surrendered.phase() == product::ProductGamePhase::Finished);
    EXPECT(context, surrendered.events().back().kind == product::ProductEventKind::MatchEnded);
    EXPECT(context, std::count_if(
        surrendered.events().begin(), surrendered.events().end(), [](const auto& event) {
            return event.kind == product::ProductEventKind::MatchEnded;
        }) == 1);
    expect_invariants(context, surrendered);

    auto choose = command(game, PlayerId::Player0, product::ActionKind::ResolveChoice);
    choose.choice_id = pending.choice_id;
    choose.selected_option_ids = {pending.options.front().option_id};
    const std::size_t deck_before = game.board().player(PlayerId::Player0).deck.size();
    EXPECT(context, game.submit_command(choose));
    EXPECT(context, game.phase() == product::ProductGamePhase::Main);
    EXPECT(context, !game.pending_choice().has_value());
    EXPECT(context, game.board().instance(choice_spell).zone == product::Zone::Graveyard);
    EXPECT(context, game.board().player(PlayerId::Player0).deck.size() == deck_before + 1U);
    expect_invariants(context, game);
}

void test_choice_legal_actions_cover_all_unordered_combinations(TestContext& context) {
    product::ProductGameConfig config = make_config();
    config.main_decks[0].back() = std::string(kMultiChoiceSpell);
    product::ProductGame game(make_game_catalog(), std::move(config));
    EXPECT(context, game.start());
    finish_mulligan(game);

    const InstanceId spell = find_card(
        game, PlayerId::Player0, product::Zone::Hand, kMultiChoiceSpell);
    auto cast = command(game, PlayerId::Player0, product::ActionKind::CastSpell);
    cast.source = spell;
    cast.slot = 0U;
    EXPECT(context, game.submit_command(cast));
    EXPECT(context, game.phase() == product::ProductGamePhase::Choice);
    EXPECT(context, game.pending_choice().has_value());
    const product::PendingChoice& pending = *game.pending_choice();
    EXPECT(context, !pending.ordered);
    EXPECT(context, pending.minimum == 0U);
    EXPECT(context, pending.maximum == 2U);
    EXPECT(context, pending.options.size() == 3U);

    const auto legal = game.list_legal_actions(PlayerId::Player0);
    // C(3,0) + C(3,1) + C(3,2), plus surrender.
    EXPECT(context, legal.size() == 8U);
    std::vector<std::vector<std::string>> selections;
    for (const product::ProductLegalAction& action : legal) {
        if (action.command.action != product::ActionKind::ResolveChoice) {
            continue;
        }
        EXPECT(context, game.plan_command(action.command));
        selections.push_back(action.command.selected_option_ids);
        product::ProductGame copy = game;
        EXPECT(context, copy.submit_command(action.command));
        EXPECT(context, copy.phase() == product::ProductGamePhase::Main);
        expect_invariants(context, copy);
    }
    std::sort(selections.begin(), selections.end());
    EXPECT(context, std::adjacent_find(selections.begin(), selections.end()) == selections.end());
    EXPECT(context, selections.size() == 7U);
    expect_invariants(context, game);
}

void test_simultaneous_non_equivalent_triggers_require_order(TestContext& context) {
    product::ProductGameConfig config = make_config();
    const std::size_t size = config.main_decks[0].size();
    config.main_decks[0][size - 4U] = std::string(kRepairSpell);
    config.main_decks[0][size - 3U] = std::string(kAdvance);
    config.main_decks[0][size - 2U] = std::string(kTriggerB);
    config.main_decks[0][size - 1U] = std::string(kTriggerA);
    product::ProductGame game(make_game_catalog(), std::move(config));
    EXPECT(context, game.start());
    finish_mulligan(game);

    const InstanceId trigger_a = find_card(game, PlayerId::Player0, product::Zone::Hand, kTriggerA);
    const InstanceId trigger_b = find_card(game, PlayerId::Player0, product::Zone::Hand, kTriggerB);
    for (const auto [card, slot] : {std::pair{trigger_a, 0U}, std::pair{trigger_b, 1U}}) {
        auto play = command(game, PlayerId::Player0, product::ActionKind::PlayFollower);
        play.source = card;
        play.slot = slot;
        EXPECT(context, game.submit_command(play));
    }
    const InstanceId advance = find_card(game, PlayerId::Player0, product::Zone::Hand, kAdvance);
    auto play_advance = command(game, PlayerId::Player0, product::ActionKind::PlayFollower);
    play_advance.source = advance;
    play_advance.slot = 2U;
    play_advance.use_advance = true;
    EXPECT(context, game.submit_command(play_advance));
    EXPECT(context, game.resources(PlayerId::Player0).cracks == 1);
    EXPECT(context, game.submit_command(command(game, PlayerId::Player0, product::ActionKind::EndTurn)));
    EXPECT(context, game.submit_command(command(game, PlayerId::Player1, product::ActionKind::EndTurn)));

    const InstanceId repair = find_card(game, PlayerId::Player0, product::Zone::Hand, kRepairSpell);
    auto cast = command(game, PlayerId::Player0, product::ActionKind::CastSpell);
    cast.source = repair;
    cast.slot = 0U;
    EXPECT(context, game.submit_command(cast));
    EXPECT(context, game.phase() == product::ProductGamePhase::Choice);
    EXPECT(context, game.pending_choice().has_value());
    EXPECT(context, game.pending_choice()->kind == product::ChoiceKind::TriggerOrder);
    EXPECT(context, game.pending_choice()->ordered);
    EXPECT(context, game.pending_choice()->options.size() == 2U);
    const auto legal_orders = game.list_legal_actions(PlayerId::Player0);
    const std::size_t order_count = static_cast<std::size_t>(std::count_if(
        legal_orders.begin(), legal_orders.end(), [](const product::ProductLegalAction& action) {
            return action.command.action == product::ActionKind::ResolveChoice;
        }));
    EXPECT(context, order_count == 2U);
    auto order = command(game, PlayerId::Player0, product::ActionKind::ResolveChoice);
    order.choice_id = game.pending_choice()->choice_id;
    order.selected_option_ids = {
        game.pending_choice()->options[1].option_id,
        game.pending_choice()->options[0].option_id,
    };
    EXPECT(context, game.submit_command(order));
    EXPECT(context, game.phase() == product::ProductGamePhase::Main);
    EXPECT(context, game.board().instance(trigger_a).keywords.has(product::Keyword::Barrier));
    EXPECT(context, game.board().instance(trigger_b).keywords.has(product::Keyword::Barrier));
    expect_invariants(context, game);
}

void test_evolution_unlock_attack_damage_and_terminal_idempotence(TestContext& context) {
    product::ProductGame game(make_game_catalog(), make_config());
    EXPECT(context, game.start());
    finish_mulligan(game);

    const InstanceId follower = find_card(game, PlayerId::Player0, product::Zone::Hand, kFollower);
    auto play = command(game, PlayerId::Player0, product::ActionKind::PlayFollower);
    play.source = follower;
    play.slot = 0U;
    EXPECT(context, game.submit_command(play));

    // Advance alternating turns until the first player's fifth owner turn.
    while (!(game.active_player() == PlayerId::Player0 &&
             game.resources(PlayerId::Player0).own_turn_number == 5)) {
        EXPECT(context, game.submit_command(command(game, game.active_player(), product::ActionKind::EndTurn)));
    }
    EXPECT(context, game.resources(PlayerId::Player0).evolution_unlocked);
    EXPECT(context, game.resources(PlayerId::Player0).evolution_energy == 2);

    const std::uint64_t evolve_revision = game.revision();
    const std::size_t evolve_events = game.events().size();
    auto invalid_evolve_mode = command(game, PlayerId::Player0, product::ActionKind::Evolve);
    invalid_evolve_mode.source = follower;
    invalid_evolve_mode.mode_id = "not-an-evolve-mode";
    EXPECT_GAME_CODE(context,
        game.submit_command(invalid_evolve_mode),
        product::ProductGameError::InvalidMode);
    EXPECT(context, game.revision() == evolve_revision);
    EXPECT(context, game.events().size() == evolve_events);

    auto invalid_evolve_target = command(game, PlayerId::Player0, product::ActionKind::Evolve);
    invalid_evolve_target.source = follower;
    invalid_evolve_target.target = follower;
    EXPECT_GAME_CODE(context,
        game.submit_command(invalid_evolve_target),
        product::ProductGameError::InvalidTarget);
    EXPECT(context, game.revision() == evolve_revision);
    EXPECT(context, game.events().size() == evolve_events);

    auto evolve = command(game, PlayerId::Player0, product::ActionKind::Evolve);
    evolve.source = follower;
    EXPECT(context, game.submit_command(evolve));
    EXPECT(context, game.board().instance(follower).evolved);
    EXPECT(context, game.board().instance(follower).current_attack == 4);
    EXPECT(context, game.resources(PlayerId::Player0).evolution_energy == 0);

    const int leader_before = game.board().player(PlayerId::Player1).leader_health;
    auto attack = command(game, PlayerId::Player0, product::ActionKind::Attack);
    attack.source = follower;
    EXPECT(context, game.submit_command(attack));
    EXPECT(context, game.board().player(PlayerId::Player1).leader_health == leader_before - 4);

    auto surrender = command(game, PlayerId::Player1, product::ActionKind::Surrender);
    EXPECT(context, game.submit_command(surrender));
    EXPECT(context, game.phase() == product::ProductGamePhase::Finished);
    EXPECT(context, game.result() == product::ProductMatchResult::Player0Won);
    EXPECT(context, game.events().back().kind == product::ProductEventKind::MatchEnded);
    EXPECT(context, std::count_if(game.events().begin(), game.events().end(), [](const auto& event) {
        return event.kind == product::ProductEventKind::MatchEnded;
    }) == 1);
    const std::uint64_t revision = game.revision();
    const std::size_t events = game.events().size();
    EXPECT_GAME_CODE(context, game.submit_command(surrender), product::ProductGameError::MatchFinished);
    EXPECT(context, game.revision() == revision);
    EXPECT(context, game.events().size() == events);
    expect_invariants(context, game);
}

void test_locked_product_decks_start_as_executable_games(TestContext& context) {
    const std::vector<product::ProductDeckDefinition> decks = product::make_locked_product_decks();
    EXPECT(context, decks.size() == 2U);
    product::ProductGameConfig config;
    config.main_decks = {decks[0].main_deck, decks[1].main_deck};
    config.standby_decks = {decks[0].standby, decks[1].standby};
    config.professions = {decks[0].profession_id, decks[1].profession_id};
    config.first_player_mode = FirstPlayerMode::Player0;
    config.seed = 12345U;
    config.shuffle = false;

    product::ProductGame game(product::make_locked_product_catalog(), std::move(config));
    EXPECT(context, game.start());
    finish_mulligan(game);
    EXPECT(context, game.phase() == product::ProductGamePhase::Main);
    EXPECT(context, game.board().player(PlayerId::Player0).deck.size() == 26U);
    EXPECT(context, game.board().player(PlayerId::Player1).deck.size() == 26U);
    EXPECT(context, game.board().player(PlayerId::Player0).standby.size() == 4U);
    const auto actions = game.list_legal_actions(PlayerId::Player0);
    EXPECT(context, std::any_of(actions.begin(), actions.end(), [](const product::ProductLegalAction& action) {
        return action.command.action == product::ActionKind::EndTurn;
    }));
    expect_invariants(context, game);
}

void test_locked_product_game_reaches_a_natural_terminal(TestContext& context) {
    const std::vector<product::ProductDeckDefinition> decks = product::make_locked_product_decks();
    product::ProductGameConfig config;
    config.main_decks = {decks[0].main_deck, decks[1].main_deck};
    config.standby_decks = {decks[0].standby, decks[1].standby};
    config.professions = {decks[0].profession_id, decks[1].profession_id};
    config.first_player_mode = FirstPlayerMode::Player0;
    config.seed = 0xA11CEU;
    config.shuffle = true;
    product::ProductGame game(product::make_locked_product_catalog(), std::move(config));
    EXPECT(context, game.start());
    finish_mulligan(game);

    std::size_t steps = 0;
    while (game.phase() != product::ProductGamePhase::Finished && steps < 2000U) {
        PlayerId actor = game.active_player();
        if (game.phase() == product::ProductGamePhase::Reaction) {
            actor = game.reaction_context().priority;
        } else if (game.phase() == product::ProductGamePhase::Choice) {
            actor = game.pending_choice()->chooser;
        }
        const auto legal = game.list_legal_actions(actor);
        const auto selected = std::find_if(legal.begin(), legal.end(), [](const product::ProductLegalAction& action) {
            return action.command.action != product::ActionKind::Surrender;
        });
        EXPECT(context, selected != legal.end());
        if (selected == legal.end()) {
            break;
        }
        const product::ProductGameStatus submitted = game.submit_command(selected->command);
        EXPECT(context, submitted);
        if (!submitted) {
            break;
        }
        ++steps;
    }
    EXPECT(context, steps < 2000U);
    EXPECT(context, game.phase() == product::ProductGamePhase::Finished);
    EXPECT(context, game.result() != product::ProductMatchResult::Ongoing);
    EXPECT(context, game.events().back().kind == product::ProductEventKind::MatchEnded);
    expect_invariants(context, game);
}

void test_locked_product_catalog_and_multiseed_matches(TestContext& context) {
    const product::CardCatalog catalog = product::make_locked_product_catalog();
    const std::vector<product::ProductDeckDefinition> decks = product::make_locked_product_decks();
    EXPECT(context, catalog.size() == 35U);
    EXPECT(context, catalog.contains("LO-T01"));
    std::unordered_set<std::string> constructible;
    for (const product::ProductDeckDefinition& deck : decks) {
        constructible.insert(deck.main_deck.begin(), deck.main_deck.end());
        constructible.insert(deck.standby.begin(), deck.standby.end());
    }
    EXPECT(context, constructible.size() == 34U);
    for (const std::string& design_id : constructible) {
        EXPECT(context, catalog.contains(design_id));
        EXPECT(context, catalog.at(design_id).is_executable());
    }

    std::size_t completed = 0U;
    std::size_t commands = 0U;
    for (std::uint64_t seed = 1U; seed <= 32U; ++seed) {
        const std::size_t first_deck = static_cast<std::size_t>(seed & 1U);
        const std::size_t second_deck = 1U - first_deck;
        product::ProductGameConfig config;
        config.main_decks = {decks[first_deck].main_deck, decks[second_deck].main_deck};
        config.standby_decks = {decks[first_deck].standby, decks[second_deck].standby};
        config.professions = {decks[first_deck].profession_id, decks[second_deck].profession_id};
        for (std::size_t player = 0; player < kPlayerCount; ++player) {
            config.evolution_charge_policies[player] = config.professions[player] == "oathguard"
                ? product::EvolutionChargePolicy::RepairToZero
                : product::EvolutionChargePolicy::FutureUseAtLeastTwo;
        }
        config.first_player_mode = (seed & 2U) == 0U
            ? FirstPlayerMode::Player0
            : FirstPlayerMode::Player1;
        config.seed = seed * 0x9E3779B9U;
        config.shuffle = true;
        product::ProductGame game(catalog, std::move(config));
        EXPECT(context, game.start());
        finish_mulligan(game);

        std::size_t match_commands = 0U;
        while (game.phase() != product::ProductGamePhase::Finished && match_commands < 2000U) {
            PlayerId actor = game.active_player();
            if (game.phase() == product::ProductGamePhase::Reaction) {
                actor = game.reaction_context().priority;
            } else if (game.phase() == product::ProductGamePhase::Choice) {
                actor = game.pending_choice()->chooser;
            }
            const auto legal = game.list_legal_actions(actor);
            const auto selected = std::find_if(
                legal.begin(), legal.end(), [](const product::ProductLegalAction& action) {
                    return action.command.action != product::ActionKind::Surrender;
                });
            if (selected == legal.end() || !game.submit_command(selected->command)) {
                break;
            }
            ++match_commands;
        }
        EXPECT(context, match_commands < 2000U);
        EXPECT(context, game.phase() == product::ProductGamePhase::Finished);
        EXPECT(context, game.events().back().kind == product::ProductEventKind::MatchEnded);
        expect_invariants(context, game);
        if (game.phase() == product::ProductGamePhase::Finished) {
            ++completed;
        }
        commands += match_commands;
    }
    EXPECT(context, completed == 32U);
    EXPECT(context, commands > 1000U);
}

struct TestCase {
    std::string_view name;
    void (*function)(TestContext&);
    std::vector<std::string> locked_definitions;
};

} // namespace

int main() {
    const std::vector<TestCase> tests = {
        {"locked_healers_use_actual_repair_and_evolution_continues", test_locked_healers_use_actual_repair_and_evolution_continues, {"LO-02", "LO-09"}},
        {"locked_last_words_debt_and_neutral_outnumbered_rush", test_locked_last_words_debt_and_neutral_outnumbered_rush, {"AP-01", "NT-01"}},
        {"locked_debt_four_book_draws_two_and_repair_mode_reduces_two", test_locked_debt_four_book_draws_two_and_repair_mode_reduces_two, {"AP-04", "AP-08"}},
        {"locked_standby_conditions_and_entry_rewards", test_locked_standby_conditions_and_entry_rewards, {"LO-S01", "LO-S02", "LO-S04", "AP-S01", "AP-S03"}},
        {"locked_dragon_requires_both_debt_and_low_health", test_locked_dragon_requires_both_debt_and_low_health, {"AP-S02"}},
        {"locked_campfire_only_expires_not_early_destruction", test_locked_campfire_only_expires_not_early_destruction, {"NT-03"}},
        {"locked_damage_threshold_optional_target_and_modes", test_locked_damage_threshold_optional_target_and_modes, {"AP-03", "AP-10", "NT-04"}},
        {"locked_repair_rewards_and_advanced_follower_programs", test_locked_repair_rewards_and_advanced_follower_programs, {"LO-05", "LO-06", "AP-06", "AP-07", "AP-09"}},
        {"capacity_burn_preserves_available_current_pp", test_capacity_burn_preserves_available_current_pp},
        {"locked_lifesteal_lethal_uses_actual_damage_and_final_event",
            test_locked_lifesteal_lethal_uses_actual_damage_and_final_event},
        {"all_locked_main_followers_execute_printed_baseline",
            test_all_locked_main_followers_execute_printed_baseline, {"NT-02"}},
        {"locked_finishers_advance_cannot_bypass_on_time_condition",
            test_locked_finishers_advance_cannot_bypass_on_time_condition, {"LO-11", "AP-11"}},
        {"locked_profession_unlock_charge_and_cap_are_not_precharged",
            test_locked_profession_unlock_charge_and_cap_are_not_precharged},
        {"locked_search_filters_and_moves_all_remainder_to_bottom",
            test_locked_search_filters_and_moves_all_remainder_to_bottom, {"LO-01"}},
        {"locked_opponent_turn_repair_charges_cycle_but_not_owner_turn_listeners",
            test_locked_opponent_turn_repair_charges_cycle_but_not_owner_turn_listeners, {"LO-07"}},
        {"locked_surviving_defender_kill_repairs_once_per_turn",
            test_locked_surviving_defender_kill_repairs_once_per_turn, {"LO-08"}},
        {"locked_combat_repair_resets_each_players_turn_not_profession_cycle",
            test_locked_combat_repair_resets_each_players_turn_not_profession_cycle, {"LO-08", "LO-S02"}},
        {"locked_field_cycles_after_successful_draw_and_skips_overflow_bottom",
            test_locked_field_cycles_after_successful_draw_and_skips_overflow_bottom, {"AP-05", "AP-02"}},
        {"locked_abaddon_mixed_additional_cost_and_vacated_slot",
            test_locked_abaddon_mixed_additional_cost_and_vacated_slot, {"AP-S04"}},
        {"locked_empower_rejects_neutral_and_preserves_failure_atomicity",
            test_locked_empower_rejects_neutral_and_preserves_failure_atomicity, {"AP-08"}},
        {"locked_field_repair_opens_fresh_target_choice_and_resumes",
            test_locked_field_repair_opens_fresh_target_choice_and_resumes, {"LO-10", "LO-04"}},
        {"deployment_once_per_turn_failed_cost_does_not_consume",
            test_deployment_once_per_turn_failed_cost_does_not_consume},
        {"destroyed_standby_combat_archives_without_last_words",
            test_destroyed_standby_combat_archives_without_last_words},
        {"locked_amulet_repair_expiry_and_token_original_slot",
            test_locked_amulet_repair_expiry_and_token_original_slot, {"LO-03", "LO-T01", "LO-S03"}},
        {"locked_book_does_not_retroactively_trigger_own_burn",
            test_locked_book_does_not_retroactively_trigger_own_burn},
        {"start_mulligan_turn_and_query_contract", test_start_mulligan_turn_and_query_contract},
        {"payment_play_zones_and_failure_atomicity", test_payment_play_zones_and_failure_atomicity},
        {"optional_targets_and_deferred_trigger_targets_are_queried_correctly",
            test_optional_targets_and_deferred_trigger_targets_are_queried_correctly},
        {"deployment_additional_cost_can_vacate_selected_slot",
            test_deployment_additional_cost_can_vacate_selected_slot},
        {"reaction_cancel_lifo_and_attack_spent", test_reaction_cancel_lifo_and_attack_spent},
        {"advance_payment_and_repair_resource_loop", test_advance_payment_and_repair_resource_loop},
        {"countdown_original_slot_and_effect_last_words", test_countdown_original_slot_and_effect_last_words},
        {"paid_choice_blocks_resumes_and_cleans_spell", test_paid_choice_blocks_resumes_and_cleans_spell},
        {"choice_legal_actions_cover_all_unordered_combinations",
            test_choice_legal_actions_cover_all_unordered_combinations},
        {"simultaneous_non_equivalent_triggers_require_order", test_simultaneous_non_equivalent_triggers_require_order},
        {"evolution_unlock_attack_damage_and_terminal_idempotence", test_evolution_unlock_attack_damage_and_terminal_idempotence},
        {"locked_product_decks_start_as_executable_games", test_locked_product_decks_start_as_executable_games},
        {"locked_product_game_reaches_a_natural_terminal", test_locked_product_game_reaches_a_natural_terminal},
        {"locked_product_catalog_and_multiseed_matches", test_locked_product_catalog_and_multiseed_matches},
    };
    TestContext context;
    std::unordered_set<std::string> semantic_coverage;
    for (const TestCase& test : tests) {
        const int failures_before = context.failures;
        try {
            test.function(context);
            if (context.failures == failures_before) {
                semantic_coverage.insert(test.locked_definitions.begin(), test.locked_definitions.end());
            }
        } catch (const std::exception& exception) {
            ++context.failures;
            std::cerr << "test threw: " << test.name << ": " << exception.what() << '\n';
        }
    }
    // This registry proves named, successful gameplay scenarios for every
    // locked definition. It does not claim every possible cross-card ordering
    // or win-rate target has been exhaustively explored.
    const auto locked = product::make_locked_product_catalog();
    for (const auto& [id, definition] : locked.definitions()) {
        (void)definition;
        if (!semantic_coverage.contains(id)) {
            std::cerr << "missing locked semantic scenario: " << id << '\n';
        }
        EXPECT(context, semantic_coverage.contains(id));
    }
    EXPECT(context, semantic_coverage.size() == locked.size());
    std::cout << semantic_coverage.size() << " locked definitions with passing semantic scenarios\n";
    std::cout << tests.size() << " product game test cases\n"
              << context.assertions << " assertions\n"
              << context.failures << " failures\n";
    return context.failures == 0 ? EXIT_SUCCESS : EXIT_FAILURE;
}
