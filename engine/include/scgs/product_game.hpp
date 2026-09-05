// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once

#include "scgs/product_runtime.hpp"

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

enum class ProductGamePhase : std::uint8_t {
    NotStarted,
    Mulligan,
    Main,
    Reaction,
    Choice,
    Finished,
};

enum class ProductMatchResult : std::uint8_t {
    Ongoing,
    Player0Won,
    Player1Won,
};

enum class ProductGameError : std::uint8_t {
    Ok,
    InvalidPlayer,
    NotStarted,
    AlreadyStarted,
    InvalidConfiguration,
    StaleRevision,
    MatchFinished,
    WrongPhase,
    NotActivePlayer,
    InvalidCommand,
    InvalidCard,
    InvalidCardKind,
    InvalidZone,
    InvalidSlot,
    SlotOccupied,
    MainBoardFull,
    TacticZoneFull,
    InvalidTarget,
    InvalidMode,
    InvalidSelection,
    InsufficientPP,
    AdvanceUnavailable,
    FutureAlreadyUsed,
    EvolutionUnavailable,
    DeploymentUnavailable,
    ReactionUnavailable,
    ChoicePending,
    NoPendingChoice,
    MulliganAlreadyDone,
    ChoiceNotOwned,
    InternalInvariant,
};

struct ProductGameStatus {
    ProductGameError code = ProductGameError::Ok;
    std::string message;

    [[nodiscard]] explicit constexpr operator bool() const noexcept {
        return code == ProductGameError::Ok;
    }
    [[nodiscard]] static ProductGameStatus ok() { return {}; }
    [[nodiscard]] static ProductGameStatus error(ProductGameError code, std::string message) {
        return ProductGameStatus{code, std::move(message)};
    }
};

struct ProductGameConfig {
    std::array<std::vector<DesignId>, kPlayerCount> main_decks;
    std::array<std::vector<DesignId>, kPlayerCount> standby_decks;
    std::array<std::string, kPlayerCount> professions;
    std::array<EvolutionChargePolicy, kPlayerCount> evolution_charge_policies = {
        EvolutionChargePolicy::RepairToZero,
        EvolutionChargePolicy::FutureUseAtLeastTwo,
    };
    FirstPlayerMode first_player_mode = FirstPlayerMode::Random;
    std::uint64_t seed = 0;
    bool shuffle = true;
    std::size_t required_main_deck_size = 30;
    std::size_t required_standby_size = 4;
    std::size_t starting_hand_size = 4;
};

struct ProductPlayerResources {
    int current_pp = 0;
    int pp_capacity = 0;
    int cracks = 0;
    int evolution_energy = 0;
    int own_turn_number = 0;
    int fatigue_count = 0;
    bool future_used_this_turn = false;
    bool evolved_this_turn = false;
    bool deploy_used_this_turn = false;
    bool evolution_unlocked = false;
    bool profession_charge_used_this_turn = false;
};

struct ProductGameCommand {
    PlayerId player = PlayerId::Player0;
    ActionKind action = ActionKind::EndTurn;
    std::uint64_t expected_revision = 0;
    std::optional<InstanceId> source;
    std::optional<InstanceId> target;
    std::optional<std::size_t> slot;
    bool use_advance = false;
    std::string mode_id;
    ChoiceId choice_id = 0;
    std::vector<std::string> selected_option_ids;
    std::vector<InstanceId> selected_cards;
    std::vector<InstanceId> additional_cost_cards;
};

struct ProductPaymentPreview {
    int base_cost = 0;
    int burn_cost = 0;
    int current_pp_after = 0;
    int pp_capacity_after = 0;
    int cracks_after = 0;
    int evolution_energy_after = 0;
    int advance_cost = 0;
    bool advanced = false;
    bool future_used = false;
};

enum class ProductPlanOperation : std::uint8_t {
    Mulligan,
    PlayMainPermanent,
    CastSpell,
    SetTrap,
    PlayField,
    AttackFollower,
    AttackLeader,
    Evolve,
    Deploy,
    ActivateTrap,
    PassReaction,
    EndTurn,
    Surrender,
    ResolveChoice,
};

struct ProductActionPlan {
    ProductGameStatus status;
    ProductPlanOperation operation = ProductPlanOperation::EndTurn;
    ProductGameCommand command;
    ProductPaymentPreview payment;
    CardKind source_kind = CardKind::Follower;
    bool opens_response = false;

    [[nodiscard]] explicit operator bool() const noexcept { return static_cast<bool>(status); }
};

struct ProductLegalAction {
    ProductGameCommand command;
    ProductPaymentPreview payment;
};

enum class ProductEventKind : std::uint8_t {
    MatchStarted,
    MulliganSubmitted,
    CardDrawn,
    CardArchived,
    TurnStarted,
    TurnEnded,
    CostPaid,
    CardPlayed,
    CardMoved,
    AttackDeclared,
    AttackCancelled,
    Damage,
    Healing,
    Evolved,
    TrapActivated,
    ReactionPassed,
    ChoiceRequested,
    ChoiceResolved,
    PlayerSurrendered,
    MatchEnded,
    Observation,
};

struct ProductGameEvent {
    std::uint64_t sequence = 0;
    std::uint64_t revision = 0;
    ProductEventKind kind = ProductEventKind::MatchStarted;
    PlayerId player = PlayerId::Player0;
    std::optional<InstanceId> source;
    std::optional<InstanceId> target;
    int value = 0;
    int secondary_value = 0;
    std::string text;
    std::optional<ProductObservation> observation;
};

struct ProductReactionContext {
    bool pending = false;
    PlayerId priority = PlayerId::Player0;
    PlayerId origin_player = PlayerId::Player0;
    ActionKind origin_action = ActionKind::EndTurn;
    std::optional<InstanceId> origin_source;
    std::optional<InstanceId> origin_target;
    std::size_t chain_size = 0;
};

// ProductGame is the authoritative schema-2 rules facade.  Queries and
// execution both pass through plan_command(); adapters must never rebuild
// payment, target, slot or timing legality.
class ProductGame {
public:
    ProductGame(CardCatalog catalog, ProductGameConfig config);

    [[nodiscard]] ProductGameStatus start();
    [[nodiscard]] ProductActionPlan plan_command(const ProductGameCommand& command) const;
    [[nodiscard]] std::vector<ProductLegalAction> list_legal_actions(PlayerId player) const;
    [[nodiscard]] ProductGameStatus submit_command(const ProductGameCommand& command);

    [[nodiscard]] ProductGamePhase phase() const noexcept;
    [[nodiscard]] ProductMatchResult result() const noexcept;
    [[nodiscard]] PlayerId active_player() const noexcept;
    [[nodiscard]] PlayerId first_player() const noexcept;
    [[nodiscard]] std::uint64_t revision() const noexcept;
    [[nodiscard]] const ProductBoard& board() const noexcept;
    [[nodiscard]] const ProductPlayerResources& resources(PlayerId player) const;
    [[nodiscard]] bool mulligan_complete(PlayerId player) const;
    [[nodiscard]] const std::optional<PendingChoice>& pending_choice() const noexcept;
    [[nodiscard]] ProductReactionContext reaction_context() const noexcept;
    [[nodiscard]] std::vector<ProductGameEvent> read_events(std::uint64_t after_sequence) const;
    [[nodiscard]] const std::vector<ProductGameEvent>& events() const noexcept;
    [[nodiscard]] std::vector<std::string> validate_invariants() const;

private:
    struct EffectContext {
        PlayerId actor = PlayerId::Player0;
        InstanceId source = 0;
        std::optional<InstanceId> target;
        bool advanced = false;
        std::optional<RepairResult> last_repair;
        std::optional<FutureUseEvent> future_use;
        // Direct play commands have already made their optional-target choice.
        // Triggered abilities leave this false so resolution can pause and ask.
        bool target_selection_complete = false;
        // Mode programs belong to the action that created this context. Keeping
        // the mode here prevents response cards and global triggers from
        // accidentally inheriting the suspended origin's mode.
        std::string mode_id;
        std::uint64_t observation_cause = 0;
    };

    struct EffectTask {
        EffectSpec effect;
        EffectContext context;
        std::optional<InstanceId> cleanup_after;
        bool trigger_once_consumed = false;
    };

    struct TriggerAbilityTask {
        std::string trigger_id;
        std::string equivalence_key;
        PlayerId controller = PlayerId::Player0;
        InstanceId source = 0;
        std::vector<EffectTask> effects;
    };

    struct SuspendedOrigin {
        ProductActionPlan plan;
        PlayerId priority = PlayerId::Player0;
        // A response must retain its complete, already validated selection so
        // target and mode information survives until LIFO resolution.
        std::vector<ProductGameCommand> response_chain;
        std::unordered_set<InstanceId> declared_traps;
        int consecutive_passes = 0;
        bool cancelled = false;
        std::vector<EffectTask> post_direct_triggers;
    };

    enum class ChoiceContinuation : std::uint8_t {
        None,
        SearchToHand,
        PutHandOnBottom,
        DiscardFromHand,
        ResolvePermanentTarget,
        OrderTriggers,
    };

    struct PendingEffectChoice {
        ChoiceContinuation continuation = ChoiceContinuation::None;
        EffectTask task;
        std::unordered_map<std::string, InstanceId> option_cards;
        std::vector<InstanceId> revealed_cards;
        bool randomize_remainder = false;
        std::unordered_map<std::string, TriggerAbilityTask> trigger_options;
    };

    CardCatalog catalog_;
    ProductBoard board_;
    ProductGameConfig config_;
    ProductRuleState rules_;
    std::array<ProductPlayerResources, kPlayerCount> resources_{};
    std::array<bool, kPlayerCount> mulligan_done_{};
    std::unordered_set<std::string> match_once_keys_;
    std::unordered_set<std::string> turn_once_keys_;
    struct EffectOutcome {
        bool succeeded = false;
        bool draw_entered_hand = false;
        std::optional<InstanceId> selected_target;
    };
    std::unordered_map<std::string, EffectOutcome> effect_outcomes_;
    std::unordered_map<InstanceId, std::string> last_effect_keys_;
    std::unordered_map<InstanceId, RepairResult> latest_repairs_;
    ResolutionQueue resolution_;
    std::deque<EffectTask> effect_tasks_;
    std::deque<std::vector<TriggerAbilityTask>> trigger_batches_;
    std::optional<PendingEffectChoice> pending_effect_choice_;
    std::optional<SuspendedOrigin> suspended_origin_;
    std::vector<ProductGameEvent> events_;
    std::size_t observation_cursor_ = 0;
    std::uint64_t observation_cause_ = 0;
    std::unordered_map<InstanceId, std::uint64_t> observation_card_causes_;
    std::uint64_t next_event_sequence_ = 1;
    std::uint64_t next_choice_id_ = 1;
    std::uint64_t revision_ = 0;
    std::uint64_t effective_seed_ = 0;
    ProductGamePhase phase_ = ProductGamePhase::NotStarted;
    ProductMatchResult result_ = ProductMatchResult::Ongoing;
    PlayerId active_player_ = PlayerId::Player0;
    PlayerId first_player_ = PlayerId::Player0;

    [[nodiscard]] ProductGameStatus validate_configuration() const;
    [[nodiscard]] ProductGameStatus validate_common(const ProductGameCommand& command) const;
    [[nodiscard]] ProductGameStatus validate_main_action(const ProductGameCommand& command) const;
    [[nodiscard]] ProductGameStatus validate_target(
        PlayerId actor,
        InstanceId source,
        std::optional<InstanceId> target,
        TargetSpec target_spec,
        const PermanentSelectorSpec& filter) const;
    [[nodiscard]] ProductGameStatus validate_card_in_zone(
        PlayerId player,
        InstanceId card,
        Zone zone) const;
    [[nodiscard]] ProductPaymentPreview project_payment(
        PlayerId player,
        int base_cost,
        int burn_cost,
        bool allow_advance,
        bool use_advance,
        int evolution_energy_cost,
        ProductGameStatus& status) const;
    [[nodiscard]] TargetSpec required_target(const CardDefinition& definition, std::string_view mode) const;
    [[nodiscard]] PermanentSelectorSpec target_filter(
        const CardDefinition& definition,
        std::string_view mode) const;

    void execute_plan(const ProductActionPlan& plan);
    void flush_observations();
    void execute_mulligan(const ProductActionPlan& plan);
    void pay(const ProductPaymentPreview& payment, PlayerId player);
    void begin_turn(PlayerId player);
    void end_turn();
    void open_or_resolve_origin(ProductActionPlan plan, std::vector<EffectTask> post_direct_triggers = {});
    void resolve_suspended_origin();
    [[nodiscard]] std::size_t enqueue_effects(
        const CardDefinition& definition,
        EffectTrigger trigger,
        const EffectContext& context,
        bool front = false);
    [[nodiscard]] std::vector<EffectTask> collect_global_effects(
        EffectTrigger trigger,
        const EffectContext& event_context) const;
    void queue_global_effects(std::vector<EffectTask> tasks);
    [[nodiscard]] bool begin_next_trigger_batch();
    void continue_effect_resolution();
    [[nodiscard]] bool execute_effect(EffectTask& task, EffectOutcome& outcome);
    [[nodiscard]] bool effect_conditions_pass(const EffectSpec& effect, const EffectContext& context) const;
    [[nodiscard]] int effect_value(const EffectSpec& effect, const EffectContext& context) const;
    [[nodiscard]] std::optional<InstanceId> effect_target(const EffectTask& task) const;
    void open_card_choice(const EffectTask& task, ChoiceContinuation continuation, std::span<const InstanceId> cards);
    void apply_effect_choice(std::span<const std::string> selected_option_ids);
    void finish_resolution();
    void finish_match(PlayerId winner, std::string_view reason);
    void process_countdowns(PlayerId player);
    void resolve_countdown_expiry(PlayerId player, InstanceId amulet);
    void handle_fatigue(PlayerId player);
    [[nodiscard]] bool has_available_trap(PlayerId player) const;
    [[nodiscard]] std::vector<InstanceId> available_traps(PlayerId player) const;
    void emit(
        ProductEventKind kind,
        PlayerId player,
        std::optional<InstanceId> source = std::nullopt,
        std::optional<InstanceId> target = std::nullopt,
        int value = 0,
        int secondary_value = 0,
        std::string text = {});
    [[nodiscard]] static ProductGameError translate(ErrorCode code) noexcept;
    [[nodiscard]] static std::string effect_key(InstanceId source, std::string_view effect_id);
    void record_effect_outcome(const EffectTask& task, const EffectOutcome& outcome);
};

} // namespace scgs::v2
