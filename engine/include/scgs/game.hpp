// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once

#include "scgs/card.hpp"
#include "scgs/types.hpp"

#include <array>
#include <cstdint>
#include <optional>
#include <random>
#include <string>
#include <unordered_map>
#include <vector>

namespace scgs {

enum class EventType : std::uint8_t {
    MatchStarted,
    TurnStarted,
    TurnEnded,
    CardDrawn,
    FatigueDamage,
    HandOverflowArchived,
    PPChanged,
    CracksChanged,      // v0.4: cracks count changed
    CardMoved,
    UnitEntered,
    UnitDamaged,
    LeaderDamaged,
    LeaderHealed,
    UnitDestroyed,
    AttackDeclared,
    AttackCancelled,
    UnitEvolved,
    TrapWindowOpened,
    TrapActivated,
    LeaderSkillUsed,
    PlayerSurrendered,
    MatchEnded,
};

struct GameEvent {
    EventType type = EventType::MatchStarted;
    PlayerId player = PlayerId::Player0;
    InstanceId card = 0;
    int value = 0;
    int secondary_value = 0;
    std::string text;
};

struct CardInstance {
    InstanceId id = 0;
    CardId definition_id = 0;
    PlayerId owner = PlayerId::Player0;
    PlayerId controller = PlayerId::Player0;
    Zone zone = Zone::None;
    std::size_t sequence = 0;

    int current_attack = 0;
    int current_health = 0;
    int maximum_health = 0;
    // keywords is set from CardDefinition printed_xxx flags at unit creation
    // and kept for wire serialisation; game logic reads it directly.
    KeywordMask keywords = mask(Keyword::None);
    // inherited_imprint is kept for wire compatibility; always None in v0.4.
    Imprint inherited_imprint = Imprint::None;

    bool evolved = false;
    bool attacked_this_turn = false;
    bool entered_this_turn = false;
    bool temporary_rush = false; // granted by evolution or advance effect
    bool face_down = false;
    int countdown = 0;
};

struct PlayerState {
    int leader_health = 25;
    int maximum_leader_health = 25;
    int current_pp = 0;
    int pp_capacity = 0;  // v0.4: PP容量, no fixed cap
    int cracks = 0;       // v0.4: 裂痕, from advance/burn costs
    int evolution_points = 0;
    int own_turn_number = 0;
    int fatigue_count = 0;

    bool mulligan_done = false;
    bool evolution_used_this_turn = false;
    bool advance_used_this_turn = false; // v0.4: 动用未来 (advance+burn both count)
    bool trap_set_this_turn = false;
    bool leader_skill_used = false;

    std::vector<InstanceId> deck;
    std::vector<InstanceId> hand;
    std::array<std::optional<InstanceId>, kUnitZoneSize> units{};
    std::array<std::optional<InstanceId>, kTacticZoneSize> tactics{};
    std::vector<InstanceId> graveyard;
    std::vector<InstanceId> archive;
    std::vector<InstanceId> standby; // v0.4 战备区

    LeaderSkillDefinition leader_skill;
};

struct GameConfig {
    std::uint32_t random_seed = 0x5C6A2026U;
    PlayerId first_player = PlayerId::Player0;
    bool shuffle_decks = true;
    int starting_hand_size = 4;
    int hand_limit = 9;
    int leader_health = 25;
};

struct ScenarioPlayer {
    int leader_health = 25;
    int maximum_leader_health = 25;
    int current_pp = 0;
    int pp_capacity = 0;  // v0.4
    int cracks = 0;       // v0.4
    int evolution_points = 0;
    int own_turn_number = 0;
    std::vector<CardId> hand;
    std::vector<CardId> units;
    std::vector<CardId> tactics;
    std::vector<CardId> deck;
    std::vector<CardId> graveyard;
    std::vector<CardId> archive;
    std::vector<CardId> standby;
    LeaderSkillDefinition leader_skill;
};

struct Scenario {
    PlayerId active_player = PlayerId::Player0;
    std::array<ScenarioPlayer, kPlayerCount> players;
};

class Game {
public:
    Game(CardCatalog catalog, DeckList player0_deck, DeckList player1_deck, GameConfig config = {});

    [[nodiscard]] Status start();
    [[nodiscard]] Status mulligan(PlayerId player, const std::vector<InstanceId>& selected_cards);
    [[nodiscard]] Status end_turn(PlayerId player);
    [[nodiscard]] Status surrender(PlayerId player);

    // Play a unit from hand.  ability_target is required for entry effects that
    // target an enemy unit.  If use_advance is true the player is explicitly
    // requesting the advance (预支) mechanic; the engine validates eligibility.
    [[nodiscard]] Status play_unit(
        PlayerId player,
        InstanceId card,
        std::optional<std::size_t> preferred_slot = std::nullopt,
        std::optional<Target> ability_target = std::nullopt,
        bool use_advance = false);

    [[nodiscard]] Status cast_spell(
        PlayerId player,
        InstanceId card,
        std::optional<Target> ability_target = std::nullopt,
        bool use_advance = false);

    [[nodiscard]] Status play_tactic(
        PlayerId player,
        InstanceId card,
        std::size_t slot,
        bool use_advance = false);

    [[nodiscard]] Status attack(PlayerId player, InstanceId attacker, Target target);
    [[nodiscard]] Status evolve(
        PlayerId player,
        InstanceId unit,
        EvolutionMode mode,
        std::optional<Target> ability_target = std::nullopt,
        bool free_evolution = false,
        bool ignore_turn_limit = false);

    [[nodiscard]] Status use_leader_skill(
        PlayerId player,
        std::optional<Target> target = std::nullopt);

    [[nodiscard]] Status activate_trap(
        PlayerId player,
        InstanceId trap,
        std::optional<Target> target = std::nullopt);
    [[nodiscard]] Status pass_reaction(PlayerId player);

    [[nodiscard]] Status load_scenario(const Scenario& scenario);

    [[nodiscard]] const PlayerState& player(PlayerId player) const;
    [[nodiscard]] const CardInstance& instance(InstanceId id) const;
    [[nodiscard]] const CardDefinition& definition(InstanceId id) const;
    [[nodiscard]] const CardCatalog& catalog() const noexcept;

    [[nodiscard]] PlayerId active_player() const noexcept;
    [[nodiscard]] Phase phase() const noexcept;
    [[nodiscard]] GameResult result() const noexcept;
    [[nodiscard]] ReactionWindow reaction_window() const noexcept;
    [[nodiscard]] const std::vector<InstanceId>& eligible_traps() const noexcept;
    [[nodiscard]] std::vector<GameEvent> drain_events();
    [[nodiscard]] std::vector<std::string> validate_invariants() const;

    [[nodiscard]] std::optional<InstanceId> find_in_hand(PlayerId player, CardId card_id) const;
    [[nodiscard]] std::optional<InstanceId> find_on_field(PlayerId player, CardId card_id) const;

private:
    struct PendingAttack {
        PlayerId player = PlayerId::Player0;
        InstanceId attacker = 0;
        Target target;
    };

    struct PendingReaction {
        ReactionWindow window = ReactionWindow::None;
        PlayerId responder = PlayerId::Player0;
        InstanceId subject = 0;
        std::vector<InstanceId> eligible_traps;
        std::optional<PendingAttack> attack;
    };

    CardCatalog catalog_;
    std::array<DeckList, kPlayerCount> deck_lists_;
    GameConfig config_;
    std::array<PlayerState, kPlayerCount> players_{};
    std::unordered_map<InstanceId, CardInstance> instances_;
    InstanceId next_instance_id_ = 1;
    std::mt19937 rng_;
    PlayerId active_player_ = PlayerId::Player0;
    Phase phase_ = Phase::NotStarted;
    GameResult result_ = GameResult::Ongoing;
    std::optional<PendingReaction> pending_reaction_;
    std::vector<GameEvent> events_;

    [[nodiscard]] Status ensure_action_player(PlayerId player) const;
    [[nodiscard]] Status ensure_not_finished() const;

    // Pay card cost, handling advance and burn.  Returns whether advance was used.
    [[nodiscard]] Status pay_card_cost(
        PlayerId player,
        const CardDefinition& def,
        bool use_advance,
        bool& out_advanced);

    InstanceId create_instance(CardId card_id, PlayerId owner, Zone zone);
    void initialize_decks();
    void begin_turn(PlayerId player);
    void ready_units(PlayerId player);
    void process_relic_countdowns(PlayerId player);
    void clear_end_of_turn_state(PlayerId player);

    void draw_cards(PlayerId player, int count);
    void draw_one(PlayerId player);
    void damage_leader(PlayerId player, int amount);
    void heal_leader(PlayerId player, int amount);
    void repair_cracks(PlayerId player, int amount);
    void gain_pp_capacity(PlayerId player, int amount);
    int damage_unit(InstanceId unit, int amount);
    void resolve_deaths();

    // Resolve all effects in `effects` whose trigger matches `trigger`.
    // advanced=true when the card that owns these effects was played with advance.
    [[nodiscard]] Status resolve_effects(
        const std::vector<EffectRecord>& effects,
        EffectTrigger trigger,
        PlayerId actor,
        InstanceId source,
        std::optional<Target> target,
        bool advanced = false);

    [[nodiscard]] std::optional<std::size_t> first_free_unit_slot(PlayerId player) const;
    [[nodiscard]] bool contains_guard(PlayerId player) const;
    [[nodiscard]] bool target_is_guard(const Target& target) const;
    [[nodiscard]] bool can_attack_now(const CardInstance& attacker, const Target& target) const;
    [[nodiscard]] Status validate_attack(PlayerId player, InstanceId attacker, const Target& target) const;
    void resolve_pending_attack();
    void resolve_unit_combat(InstanceId attacker, InstanceId defender);
    void resolve_leader_attack(InstanceId attacker, PlayerId defender);

    void open_reaction_window(
        ReactionWindow window,
        PlayerId responder,
        InstanceId subject,
        std::optional<PendingAttack> attack = std::nullopt);
    [[nodiscard]] std::vector<InstanceId> matching_traps(PlayerId responder, ReactionWindow window) const;
    [[nodiscard]] bool trap_matches_window(const CardDefinition& trap, ReactionWindow window) const;
    void close_reaction_window();

    void move_from_current_zone(InstanceId card);
    void put_in_hand(PlayerId player, InstanceId card);
    void put_in_unit_slot(PlayerId player, InstanceId card, std::size_t slot);
    void put_in_tactic_slot(PlayerId player, InstanceId card, std::size_t slot);
    void put_in_graveyard(PlayerId player, InstanceId card);
    void put_in_archive(PlayerId player, InstanceId card);
    void normalize_sequences(PlayerId player, Zone zone);

    [[nodiscard]] bool vector_contains(const std::vector<InstanceId>& values, InstanceId id) const;
    [[nodiscard]] bool is_controlled_unit(PlayerId player, InstanceId id) const;
    [[nodiscard]] bool is_enemy_unit(PlayerId player, InstanceId id) const;
    [[nodiscard]] bool is_valid_target_for_ability(PlayerId actor, const Target& target, bool require_enemy_unit) const;

    void evaluate_result();
    void emit(EventType type, PlayerId player, InstanceId card = 0, int value = 0, int secondary_value = 0, std::string text = {});
};

} // namespace scgs
