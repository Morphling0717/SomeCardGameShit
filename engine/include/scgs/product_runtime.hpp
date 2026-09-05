// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once

#include "scgs/product_types.hpp"

#include <array>
#include <cstdint>
#include <deque>
#include <optional>
#include <span>
#include <string>
#include <string_view>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace scgs::v2 {

namespace synthetic {
inline constexpr std::string_view kFollower = "SYN-FOLLOWER";
inline constexpr std::string_view kAmulet = "SYN-AMULET";
inline constexpr std::string_view kToken = "SYN-TOKEN";
inline constexpr std::string_view kFieldA = "SYN-FIELD-A";
inline constexpr std::string_view kFieldB = "SYN-FIELD-B";
inline constexpr std::string_view kBarrierFollower = "SYN-BARRIER";
inline constexpr std::string_view kBaneFollower = "SYN-BANE";
inline constexpr std::string_view kLifestealFollower = "SYN-LIFESTEAL";
inline constexpr std::string_view kRushFollower = "SYN-RUSH";
inline constexpr std::string_view kStormFollower = "SYN-STORM";
inline constexpr std::string_view kSpell = "SYN-SPELL";
inline constexpr std::string_view kTrap = "SYN-TRAP";
inline constexpr std::string_view kNoAdvanceFollower = "SYN-NO-ADVANCE";
inline constexpr std::string_view kOathFollower = "SYN-OATH-FOLLOWER";
inline constexpr std::string_view kOathAmulet = "SYN-OATH-AMULET";
inline constexpr std::string_view kOathSpell = "SYN-OATH-SPELL";
inline constexpr std::string_view kOtherSpell = "SYN-OTHER-SPELL";
inline constexpr std::string_view kModalSpell = "SYN-MODAL-SPELL";
inline constexpr std::string_view kStandbyFollower = "SYN-STANDBY";
} // namespace synthetic

[[nodiscard]] CardCatalog make_synthetic_product_catalog();
// Generated from the locked product design and executable-effect manifests.
[[nodiscard]] CardCatalog make_locked_product_catalog();
// Exact expanded 30-card main and four-card standby lists for both products.
[[nodiscard]] std::vector<ProductDeckDefinition> make_locked_product_decks();
// Generated from every Gate 5A capability whose default status is `fix` or
// `new`. Tests compare their executable evidence registry against this exact
// list, so a manifest addition cannot silently escape product-rule coverage.
[[nodiscard]] std::span<const std::string_view> required_product_capability_ids() noexcept;

struct CardFilter {
    std::optional<CardKind> required_kind;
    std::optional<CardKind> excluded_kind;
    std::string profession_id;
    std::string series_id;
    std::optional<bool> neutral;

    [[nodiscard]] bool matches(const CardDefinition& definition) const noexcept;
};

struct PermanentFilter {
    std::vector<CardKind> allowed_kinds;
    std::string profession_id;
    std::string series_id;
    bool include_main_board = true;
    bool include_field = true;

    [[nodiscard]] bool matches(const CardDefinition& definition) const noexcept;
    [[nodiscard]] static PermanentFilter from_spec(const PermanentSelectorSpec& spec);
};

struct DamageResult {
    int actual_damage = 0;
    bool barrier_consumed = false;
};

struct CombatResult {
    DamageResult damage_to_defender;
    DamageResult damage_to_attacker;
    int attacker_healed = 0;
    bool attacker_destroyed = false;
    bool defender_destroyed = false;
    bool attacker_killed_follower_and_survived = false;
};

struct DrawResult {
    std::optional<InstanceId> card;
    bool entered_hand = false;
    bool deck_empty = false;
};

struct DrawThenBottomResult {
    DrawResult draw;
    std::vector<InstanceId> bottom_candidates;

    [[nodiscard]] bool requires_bottom_choice() const noexcept {
        return draw.entered_hand && !bottom_candidates.empty();
    }
};

// A small, deterministic product-state kernel.  It owns only zone/permanent
// invariants; the future full product Game composes payment, response and effect
// interpreters around it instead of duplicating these rules in adapters.
class ProductBoard {
public:
    explicit ProductBoard(CardCatalog catalog);

    [[nodiscard]] InstanceId create_instance(
        std::string_view design_id,
        PlayerId owner,
        Zone initial_zone = Zone::None,
        MoveReason reason = MoveReason::ScenarioSetup);

    [[nodiscard]] Status place_main(
        PlayerId player,
        InstanceId card,
        std::size_t slot,
        MoveReason reason = MoveReason::Played);
    [[nodiscard]] Status place_tactic(
        PlayerId player,
        InstanceId card,
        std::size_t slot,
        MoveReason reason = MoveReason::Played);
    [[nodiscard]] Status play_field(PlayerId player, InstanceId card);
    [[nodiscard]] Status move_to_graveyard(InstanceId card, MoveReason reason, bool destroyed);
    [[nodiscard]] Status move_to_archive(InstanceId card, MoveReason reason);
    [[nodiscard]] Status move_hand_card_to_deck_bottom(PlayerId player, InstanceId card);
    [[nodiscard]] Status discard_from_hand(PlayerId player, InstanceId card);
    [[nodiscard]] Status put_deck_cards_on_bottom(
        PlayerId player,
        std::span<const InstanceId> cards,
        bool randomize,
        std::uint64_t seed);
    [[nodiscard]] std::vector<InstanceId> reveal_top_matching(
        PlayerId player,
        std::size_t count,
        const CardFilter& filter) const;
    [[nodiscard]] std::vector<InstanceId> reveal_top(PlayerId player, std::size_t count) const;
    [[nodiscard]] DrawResult draw_one(PlayerId player);
    [[nodiscard]] Status exchange_mulligan(
        PlayerId player,
        std::span<const InstanceId> selected_cards,
        bool shuffle,
        std::uint64_t seed,
        std::vector<DrawResult>& replacements);
    [[nodiscard]] DrawThenBottomResult draw_then_prepare_bottom(PlayerId player);
    [[nodiscard]] Status move_deck_card_to_hand(PlayerId player, InstanceId card);

    [[nodiscard]] std::vector<InstanceId> list_permanents(
        PlayerId controller,
        const PermanentFilter& filter = {}) const;
    [[nodiscard]] Status validate_permanent_target(
        PlayerId acting_player,
        InstanceId target,
        bool friendly,
        const PermanentFilter& filter = {}) const;
    [[nodiscard]] Status validate_optional_enemy_follower_target(
        PlayerId acting_player,
        std::optional<InstanceId> target) const;
    [[nodiscard]] Status destroy_permanent(InstanceId target);
    [[nodiscard]] Status pay_additional_archive_cost(
        PlayerId player,
        InstanceId target,
        const PermanentFilter& filter);

    // Identity/base metadata may exist before a card has an executable effect
    // program.  Every future payment/action enumeration path must pass through
    // this gate rather than treating a generated catalog row as playable.
    [[nodiscard]] Status validate_payable(
        std::string_view design_id,
        CardAvailability availability) const;
    [[nodiscard]] std::vector<DesignId> list_payable_definitions(
        CardAvailability availability) const;

    [[nodiscard]] Status validate_advance(std::string_view design_id, bool use_advance) const;
    [[nodiscard]] Status validate_mode(
        std::string_view design_id,
        std::optional<std::string_view> mode_id) const;
    [[nodiscard]] Status validate_standby(
        std::string_view design_id,
        const struct ConditionEvaluationContext& context) const;

    [[nodiscard]] Status grant_permanent_stats(InstanceId card, int attack, int health);
    [[nodiscard]] Status grant_temporary_attack(InstanceId card, int attack);
    [[nodiscard]] Status grant_permanent_keyword(InstanceId card, Keyword keyword);
    [[nodiscard]] DamageResult damage_follower(InstanceId card, int amount);
    [[nodiscard]] int heal_leader(PlayerId player, int amount);
    [[nodiscard]] Status change_countdown(InstanceId card, int delta);

    // Countdown expiry is two operations so a resolution queue may pause after
    // the amulet leaves. The original slot remains reserved for frame_id and no
    // unrelated permanent can interleave before the token summon.
    [[nodiscard]] Status expire_amulet_and_reserve(InstanceId amulet, ResolutionFrameId frame_id);
    [[nodiscard]] Status summon_token_in_reserved_slot(
        PlayerId player,
        std::string_view token_design_id,
        std::size_t slot,
        ResolutionFrameId frame_id,
        InstanceId& out_token);
    void release_reservations(ResolutionFrameId frame_id) noexcept;

    [[nodiscard]] Status validate_attack_source(PlayerId player, InstanceId card) const;
    [[nodiscard]] Status validate_attack_target(PlayerId player, InstanceId card) const;
    [[nodiscard]] Status validate_attack(
        PlayerId player,
        InstanceId attacker,
        std::optional<InstanceId> follower_target) const;
    // Accepting an attack declaration consumes the follower's one attack for
    // the turn before any response window opens. A later cancellation or
    // suspended resolution deliberately does not refund it.
    [[nodiscard]] Status accept_attack_declaration(
        PlayerId player,
        InstanceId attacker,
        std::optional<InstanceId> follower_target);
    [[nodiscard]] Status validate_evolve(PlayerId player, InstanceId card) const;
    [[nodiscard]] CombatResult resolve_follower_combat(InstanceId attacker, InstanceId defender);
    // ProductGame accepts an attack before opening the response window. This
    // resolves that already-accepted declaration without spending it twice.
    [[nodiscard]] CombatResult resolve_accepted_follower_combat(
        InstanceId attacker,
        InstanceId defender);
    void clear_turn_keyword_grants(PlayerId player) noexcept;
    void ready_starting_turn_permanents(PlayerId player) noexcept;

    [[nodiscard]] const CardCatalog& catalog() const noexcept;
    [[nodiscard]] bool contains_instance(InstanceId card) const noexcept;
    [[nodiscard]] const CardInstance& instance(InstanceId card) const;
    [[nodiscard]] CardInstance& instance(InstanceId card);
    [[nodiscard]] const PlayerState& player(PlayerId player) const;
    [[nodiscard]] PlayerState& player(PlayerId player);
    [[nodiscard]] const std::vector<MoveRecord>& moves() const noexcept;
    [[nodiscard]] std::optional<ResolutionFrameId> reserved_by(PlayerId player, std::size_t slot) const;
    [[nodiscard]] std::size_t main_board_count(PlayerId player) const;
    [[nodiscard]] bool field_is(PlayerId player, std::string_view design_id) const;
    [[nodiscard]] std::vector<std::string> validate_invariants() const;

private:
    CardCatalog catalog_;
    std::array<PlayerState, kPlayerCount> players_{};
    std::unordered_map<InstanceId, CardInstance> instances_;
    std::array<std::array<std::optional<ResolutionFrameId>, kMainBoardSize>, kPlayerCount> reservations_{};
    std::vector<MoveRecord> moves_;
    InstanceId next_instance_id_ = 1;

    [[nodiscard]] Status ensure_card(InstanceId card) const;
    [[nodiscard]] Status ensure_controller(PlayerId player, InstanceId card) const;
    [[nodiscard]] std::optional<std::size_t> main_slot_of(InstanceId card) const;
    void detach(InstanceId card);
    void attach_vector(std::vector<InstanceId>& destination, InstanceId card);
    void record_move(InstanceId card, Zone from, Zone to, MoveReason reason, bool destroyed);
    [[nodiscard]] DamageResult deal_positive_damage(CardInstance& target, int amount);
};

struct RepairResult {
    int before = 0;
    int after = 0;
    int actual_repaired = 0;
    bool repaired_to_zero = false;
};

struct FutureUseEvent {
    std::uint64_t sequence = 0;
    PlayerId player = PlayerId::Player0;
    int advance_cracks = 0;
    int burn_cracks = 0;

    [[nodiscard]] int total_cracks() const noexcept { return advance_cracks + burn_cracks; }
};

struct ProductTurnHistory {
    struct PermanentRecord {
        CardKind kind = CardKind::Follower;
        std::string profession_id;
        std::string series_id;
    };
    int actual_repaired = 0;
    int future_cracks_added = 0;
    int countdown_expired = 0;
    bool barrier_granted = false;
    std::vector<PermanentRecord> barrier_sources;
    std::vector<PermanentRecord> countdown_sources;
    std::unordered_set<std::string> consumed_once_keys;
};

struct ProductMatchHistory {
    int repair_to_zero_count = 0;
    int countdown_expired = 0;
    std::vector<ProductTurnHistory::PermanentRecord> countdown_sources;
};

enum class EvolutionChargePolicy : std::uint8_t {
    None,
    RepairToZero,
    FutureUseAtLeastTwo,
};

struct ProductRuleEvent {
    enum class Kind : std::uint8_t { Repair, FutureUse, CountdownExpired, BarrierGranted };

    std::uint64_t sequence = 0;
    PlayerId player = PlayerId::Player0;
    Kind kind = Kind::Repair;
    int amount = 0;
    bool flag = false;
};

struct ProductListenerToken {
    PlayerId player = PlayerId::Player0;
    ProductRuleEvent::Kind kind = ProductRuleEvent::Kind::FutureUse;
    std::uint64_t armed_after_sequence = 0;
};

struct ConditionEvaluationContext {
    int cracks = 0;
    bool advanced = false;
    int actual_repair = 0;
    bool repaired_to_zero = false;
    int future_use_amount = 0;
    ProductTurnHistory turn;
    ProductMatchHistory match;
    int leader_health = 25;
    std::size_t own_board_count = 0;
    std::size_t enemy_board_count = 0;
    std::string field_design_id;
    std::vector<std::string> controlled_series;
};

[[nodiscard]] bool evaluate_condition(
    const ConditionSpec& condition,
    const ConditionEvaluationContext& context) noexcept;

class ProductRuleState {
public:
    void set_cracks(PlayerId player, int cracks);
    [[nodiscard]] int cracks(PlayerId player) const;
    [[nodiscard]] int cracks_capped(PlayerId player, int cap = 5) const;
    [[nodiscard]] RepairResult repair(PlayerId player, int amount);
    [[nodiscard]] FutureUseEvent use_future(PlayerId player, int advance_cracks, int burn_cracks);
    void record_barrier_granted(PlayerId player, const CardDefinition* source = nullptr);
    void record_countdown_expired(PlayerId player, const CardDefinition* source = nullptr);
    void begin_owner_turn(PlayerId player);
    [[nodiscard]] bool consume_once_per_owner_turn(PlayerId player, std::string_view key);

    void configure_evolution_charge(PlayerId player, EvolutionChargePolicy policy);
    void set_evolution_unlocked(PlayerId player, bool unlocked);
    [[nodiscard]] int evolution_energy(PlayerId player) const;

    [[nodiscard]] ProductListenerToken arm_listener(
        PlayerId player,
        ProductRuleEvent::Kind kind) const;
    [[nodiscard]] std::vector<ProductRuleEvent> events_observed_by(
        const ProductListenerToken& listener) const;
    [[nodiscard]] const ProductTurnHistory& turn_history(PlayerId player) const;
    [[nodiscard]] const ProductMatchHistory& match_history(PlayerId player) const;
    [[nodiscard]] ConditionEvaluationContext make_condition_context(
        PlayerId player,
        const ProductBoard& board,
        std::optional<RepairResult> repair = std::nullopt,
        std::optional<FutureUseEvent> future_use = std::nullopt,
        bool advanced = false) const;

private:
    struct PlayerRules {
        int cracks = 0;
        int evolution_energy = 0;
        bool evolution_unlocked = false;
        bool evolution_charged_this_owner_turn = false;
        EvolutionChargePolicy evolution_policy = EvolutionChargePolicy::None;
        ProductTurnHistory turn;
        ProductMatchHistory match;
    };

    std::array<PlayerRules, kPlayerCount> players_{};
    std::vector<ProductRuleEvent> events_;
    std::uint64_t next_event_sequence_ = 1;

    [[nodiscard]] PlayerRules& rules(PlayerId player);
    [[nodiscard]] const PlayerRules& rules(PlayerId player) const;
    void maybe_charge_evolution(PlayerRules& player, bool condition) noexcept;
    void append_event(PlayerId player, ProductRuleEvent::Kind kind, int amount, bool flag);
};

enum class ChoiceKind : std::uint8_t {
    Mode,
    Cards,
    TriggerOrder,
    AdditionalCost,
};

struct ChoiceOption {
    std::string option_id; // short-lived opaque identifier at the client edge
    std::optional<InstanceId> card;
};

struct PendingChoice {
    ChoiceId choice_id = 0;
    PlayerId chooser = PlayerId::Player0;
    ChoiceKind kind = ChoiceKind::Mode;
    // Non-zero when an already-declared effect is paused awaiting this
    // choice. The frame stays in the queue and resumes only after the choice
    // succeeds; clients never receive this internal identifier.
    ResolutionFrameId suspended_frame_id = 0;
    std::size_t minimum = 1;
    std::size_t maximum = 1;
    bool ordered = false;
    std::vector<ChoiceOption> options;
};

struct ChoiceResolution {
    ChoiceId choice_id = 0;
    ResolutionFrameId suspended_frame_id = 0;
    std::vector<std::string> selected_option_ids;
};

enum class ResolutionFrameKind : std::uint8_t {
    DirectEffect,
    EntryEffectPending,
    ResponseEffect,
    GlobalTrigger,
    Continuation,
};

struct ResolutionFrame {
    ResolutionFrameId frame_id = 0;
    PlayerId controller = PlayerId::Player0;
    InstanceId source = 0;
    std::string operation;
    ResolutionFrameKind kind = ResolutionFrameKind::DirectEffect;
};

class ResolutionQueue {
public:
    void enqueue(ResolutionFrame frame);
    void enqueue_response(ResolutionFrame frame);
    void enqueue_entry_pending(ResolutionFrame window, ResolutionFrame continuation);
    [[nodiscard]] Status suspend_for_choice(PendingChoice choice);
    [[nodiscard]] bool input_blocked() const noexcept;
    [[nodiscard]] bool permits(ActionKind action) const noexcept;
    [[nodiscard]] const std::optional<PendingChoice>& pending_choice() const noexcept;
    [[nodiscard]] Status resolve_choice(
        PlayerId player,
        ChoiceId choice_id,
        std::span<const std::string> selected_option_ids);
    [[nodiscard]] std::optional<ChoiceResolution> take_resolved_choice();
    [[nodiscard]] std::optional<ResolutionFrame> pop_ready_frame();
    void finish_match() noexcept;
    [[nodiscard]] bool finished() const noexcept;
    [[nodiscard]] std::uint64_t revision() const noexcept;
    [[nodiscard]] std::size_t frame_count() const noexcept;

private:
    std::deque<ResolutionFrame> frames_;
    std::optional<PendingChoice> pending_choice_;
    std::optional<ChoiceResolution> resolved_choice_;
    std::optional<ResolutionFrameId> resume_frame_id_;
    bool finished_ = false;
    std::uint64_t revision_ = 0;
};

struct TriggeredAbility {
    std::string trigger_id;
    PlayerId controller = PlayerId::Player0;
    InstanceId source = 0;
    std::size_t printed_order = 0;
    // Non-empty and equal means the effects are semantically interchangeable,
    // allowing deterministic automatic ordering.
    std::string equivalence_key;
};

// Resolves one simultaneous trigger batch. The active player's group is always
// fixed before the non-active player's group. Each non-equivalent multi-trigger
// group becomes an explicit ordered choice owned by that player.
class TriggerOrderPlanner {
public:
    TriggerOrderPlanner(PlayerId active_player, std::vector<TriggeredAbility> triggers);

    [[nodiscard]] bool complete() const noexcept;
    [[nodiscard]] const std::optional<PendingChoice>& pending_choice() const noexcept;
    [[nodiscard]] Status resolve_order(
        PlayerId player,
        ChoiceId choice_id,
        std::span<const std::string> ordered_trigger_ids);
    [[nodiscard]] const std::vector<TriggeredAbility>& ordered_triggers() const noexcept;

private:
    std::array<std::vector<TriggeredAbility>, kPlayerCount> groups_;
    std::size_t group_index_ = 0;
    std::vector<TriggeredAbility> ordered_;
    std::optional<PendingChoice> pending_choice_;

    void advance();
};

} // namespace scgs::v2
