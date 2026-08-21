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
    EvolutionEnergyChanged, // v0.4: energy gained from a class charge condition
    UnitDeployed,          // v0.4: standby deployment resolved
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
    bool temporary_rush = false; // granted by evolution or card effects
    bool deployed_from_standby = false; // v0.4: entered via 部署; leaves to archive
    bool face_down = false;
    int countdown = 0;

    // v0.4 组件能力: runtime granted modifier from the card that paid the
    // deployment cost. Cleared when the unit leaves the field; never re-granted.
    ComponentSpec granted_component;
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
    bool deploy_used_this_turn = false;  // v0.4: 战备部署 once per turn
    bool trap_set_this_turn = false;
    bool leader_skill_used = false;

    // v0.4 charge-condition bookkeeping (turn cycle = own turn start to next
    // own turn start; at most one energy point granted per cycle).
    bool charge_granted_this_cycle = false;
    int friendly_deaths_this_cycle = 0;
    int spells_used_this_turn = 0;
    int units_played_this_turn = 0;

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

    // v0.4 §5: the tactic zone never auto-replaces; a full zone rejects the
    // placement unless an effect removes a card first.
    [[nodiscard]] Status play_tactic(
        PlayerId player,
        InstanceId card,
        std::size_t slot,
        bool use_advance = false);

    [[nodiscard]] Status attack(PlayerId player, InstanceId attacker, Target target);

    // v0.4 §22: single evolution form. Costs 2 evolution energy, at most once
    // per turn, unlocked on own turn 5 (first) / 4 (second). The unit changes
    // to its evolution state (per-card evolved stats, default +2/+2), triggers
    // "进化时" effects and may attack enemy units this turn.
    [[nodiscard]] Status evolve(
        PlayerId player,
        InstanceId unit,
        std::optional<Target> ability_target = std::nullopt);

    // v0.4 §25 战备部署: deploy a standby card. Deployment cannot use advance.
    // component_donor is the friendly unit archived as the deployment cost;
    // its printed component ability is granted to the deployed unit.
    [[nodiscard]] Status deploy(
        PlayerId player,
        InstanceId standby_card,
        std::optional<std::size_t> preferred_slot = std::nullopt,
        std::optional<InstanceId> component_donor = std::nullopt,
        std::optional<Target> ability_target = std::nullopt);

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
    [[nodiscard]] std::size_t response_depth() const noexcept; // v0.4: layers on the stack
    [[nodiscard]] std::vector<GameEvent> drain_events();
    [[nodiscard]] std::vector<std::string> validate_invariants() const;

    [[nodiscard]] std::optional<InstanceId> find_in_hand(PlayerId player, CardId card_id) const;
    [[nodiscard]] std::optional<InstanceId> find_on_field(PlayerId player, CardId card_id) const;
    [[nodiscard]] std::optional<InstanceId> find_in_standby(PlayerId player, CardId card_id) const;

private:
    struct PendingAttack {
        PlayerId player = PlayerId::Player0;
        InstanceId attacker = 0;
        Target target;
    };

    // v0.4 §26: the suspended original action that a response layer may wrap.
    struct SuspendedAction {
        enum class Kind : std::uint8_t {
            None,
            Spell,        // spell OnPlay effects are about to resolve
            EntryEffect,  // unit OnEntry effects are about to resolve
            Attack,       // attack damage is about to resolve
        };
        Kind kind = Kind::None;
        PlayerId player = PlayerId::Player0;
        InstanceId card = 0;
        std::optional<Target> target;
        bool advanced = false;
        PendingAttack attack;
    };

    // One layer of the v0.4 response stack (原行动 → 响应 → 反制, max 3).
    struct ResponseLayer {
        ReactionWindow window = ReactionWindow::None;
        PlayerId responder = PlayerId::Player0; // who may act on this layer
        InstanceId subject = 0;
        std::vector<InstanceId> eligible_traps;
        std::optional<InstanceId> activated_trap; // declared but unresolved (LIFO)
        SuspendedAction suspended;                // non-None only on the base layer
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
    std::vector<ResponseLayer> response_stack_;
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
    void grant_evolution_energy(PlayerId player, int amount);
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
    void resolve_pending_attack(const PendingAttack& attack);
    void resolve_unit_combat(InstanceId attacker, InstanceId defender);
    void resolve_leader_attack(InstanceId attacker, PlayerId defender);

public:
    // Legality query for clients: full validation of an attack command without
    // mutating state (returns the same ErrorCode attack() would).
    [[nodiscard]] Status validate_attack(PlayerId player, InstanceId attacker, const Target& target) const;

private:

    // v0.4 response stack -----------------------------------------------------
    // Opens a window for `responder`, suspending `suspended`. When the responder
    // has no eligible trap the suspended action resolves immediately.
    void open_response_window(
        ReactionWindow window,
        PlayerId responder,
        InstanceId subject,
        SuspendedAction suspended);
    [[nodiscard]] std::vector<InstanceId> matching_traps(PlayerId responder, ReactionWindow window) const;
    [[nodiscard]] bool trap_matches_window(const CardDefinition& trap, ReactionWindow window) const;
    // Resolve the whole stack LIFO: counter → response → original action.
    void resolve_response_chain();
    // Resolve one suspended original action (base layer of the chain).
    void resolve_suspended_action(const SuspendedAction& suspended);
    void close_reaction_window();

    void move_from_current_zone(InstanceId card);
    void put_in_hand(PlayerId player, InstanceId card);
    void put_in_unit_slot(PlayerId player, InstanceId card, std::size_t slot, bool deployed_from_standby = false);
    void put_in_tactic_slot(PlayerId player, InstanceId card, std::size_t slot);
    void put_in_graveyard(PlayerId player, InstanceId card);
    void put_in_archive(PlayerId player, InstanceId card);
    void normalize_sequences(PlayerId player, Zone zone);

    [[nodiscard]] bool vector_contains(const std::vector<InstanceId>& values, InstanceId id) const;
    [[nodiscard]] bool is_controlled_unit(PlayerId player, InstanceId id) const;
    [[nodiscard]] bool is_enemy_unit(PlayerId player, InstanceId id) const;
    [[nodiscard]] bool is_valid_target_for_ability(PlayerId actor, const Target& target, bool require_enemy_unit) const;
    [[nodiscard]] bool deployment_condition_met(PlayerId player, const DeploymentSpec& spec) const;

    void evaluate_result();
    void emit(EventType type, PlayerId player, InstanceId card = 0, int value = 0, int secondary_value = 0, std::string text = {});
};

} // namespace scgs
