// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once

#include "scgs/types.hpp"

#include <array>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>
#include <utility>
#include <vector>

// Product rules deliberately live beside, rather than inside, the frozen v0.4
// model.  scgs_v04 continues to compile against scgs::{CardKind, Zone, Game};
// schema-2/native-v05 adapters consume the types below.
namespace scgs::v2 {

using DesignId = std::string;
using ResolutionFrameId = std::uint64_t;
using ChoiceId = std::uint64_t;
using KeywordMask = std::uint32_t;

inline constexpr std::size_t kMainBoardSize = 5;
inline constexpr std::size_t kStrategyZoneSize = 3;

enum class CardKind : std::uint8_t {
    Follower = 0,
    Spell = 1,
    Amulet = 2,
    Trap = 3,
    Field = 4,
};

enum class CardAvailability : std::uint8_t {
    MainDeck,
    Standby,
    Token,
};

// Keeping implementation state on every definition prevents identity-only
// catalog rows from accidentally becoming payable actions. SyntheticFixture
// is reserved for rule-kernel tests; ExecutableProduct identifies a definition
// whose generated, strongly typed effect program is ready for ProductGame.
enum class CardImplementationStatus : std::uint8_t {
    LockedNotImplemented,
    SyntheticFixture,
    ExecutableProduct,
};

enum class Zone : std::uint8_t {
    None = 0,
    Deck = 1,
    Hand = 2,
    MainBoard = 3,
    Tactic = 4,
    Graveyard = 5,
    Archive = 6,
    Standby = 7,
    Field = 8,
};

enum class ActionKind : std::uint8_t {
    Mulligan = 0,
    PlayFollower = 1,
    CastSpell = 2,
    PlayTrap = 3,
    Attack = 4,
    Evolve = 5,
    Deploy = 6,
    ActivateTrap = 7,
    PassReaction = 8,
    EndTurn = 9,
    Surrender = 10,
    PlayAmulet = 11,
    PlayField = 12,
    ResolveChoice = 13,
};

// Every transition out of a zone has an explicit cause.  Destroyed is a
// derived semantic carried by MoveRecord; replacement/archive never pretend
// to be destruction and therefore never fire last words.
enum class MoveReason : std::uint8_t {
    ScenarioSetup,
    Drawn,
    Played,
    Resolved,
    Destroyed,
    CountdownExpired,
    FieldReplaced,
    Discarded,
    Archived,
    AdditionalCost,
    HandOverflow,
    ReturnedToDeckBottom,
    TokenSummoned,
    TerminalCleanup,
};

enum class Keyword : KeywordMask {
    None = 0,
    Ward = 1U << 0U,
    Rush = 1U << 1U,
    Storm = 1U << 2U,
    Barrier = 1U << 3U,
    Bane = 1U << 4U,
    Lifesteal = 1U << 5U,
};

[[nodiscard]] constexpr KeywordMask mask(const Keyword keyword) noexcept {
    return static_cast<KeywordMask>(keyword);
}

[[nodiscard]] constexpr bool contains(const KeywordMask keywords, const Keyword keyword) noexcept {
    return (keywords & mask(keyword)) != 0U;
}

// Printed, permanent and turn-only grants are kept separate so clearing the
// turn can never erase a permanent grant. Consumed records one-shot keywords
// such as Barrier which have already been spent on this permanent.
struct KeywordState {
    KeywordMask printed = mask(Keyword::None);
    KeywordMask permanent = mask(Keyword::None);
    KeywordMask turn = mask(Keyword::None);
    KeywordMask consumed = mask(Keyword::None);

    [[nodiscard]] KeywordMask effective() const noexcept;
    [[nodiscard]] bool has(Keyword keyword) const noexcept;
    void grant_permanent(Keyword keyword) noexcept;
    void grant_for_turn(Keyword keyword) noexcept;
    [[nodiscard]] bool consume(Keyword keyword) noexcept;
    void clear_turn() noexcept;
};

struct CardIdentity {
    DesignId design_id;
    std::string profession_id;
    std::string series_id;
    bool neutral = false;

    [[nodiscard]] bool is_constructible_for(std::string_view profession) const noexcept;
};

enum class EffectTrigger : std::uint8_t {
    OnPlay,
    OnEntry,
    OnEvolve,
    OnLastWords,
    OnCountdownEnd,
    OnRepairToZero,
    OnFutureUsed,
    OnCombatKillSurvived,
    OnAttackDeclared,
    OnActualRepair,
};

enum class EffectKind : std::uint8_t {
    Draw,
    HealLeader,
    DamageFollower,
    RepairCracks,
    ModifyStats,
    GrantKeyword,
    ChangeCountdown,
    SummonToken,
    SearchTop,
    PutOnDeckBottom,
    Discard,
    DestroyPermanent,
    CancelAttack,
};

enum class ConditionKind : std::uint8_t {
    Always,
    CracksAtLeast,
    CracksAtMost,
    Advanced,
    OnTime,
    ActualRepairAtLeast,
    RepairToZero,
    FutureUseAtLeast,
    TurnRepairAtLeast,
    TurnFutureUseAtLeast,
    TurnBarrierGranted,
    TurnCountdownExpired,
    MatchRepairToZeroAtLeast,
    MatchCountdownExpiredAtLeast,
    LeaderHealthAtMost,
    BoardCountLessThanOpponent,
    FieldIs,
    ControlsSeriesPermanent,
};

enum class TargetSpec : std::uint8_t {
    None,
    Self,
    FriendlyFollower,
    EnemyFollower,
    FriendlyPermanent,
    EnemyPermanent,
};

struct PermanentSelectorSpec {
    std::vector<CardKind> allowed_kinds;
    std::string profession_id;
    std::string series_id;
    bool include_main_board = true;
    bool include_field = true;
    bool exclude_source = false;
};

// Search/hand effects select cards from non-permanent zones, so their filter
// is deliberately separate from PermanentSelectorSpec.  An empty
// allowed_kinds vector means every printed card kind is accepted.
struct CardSelectorSpec {
    std::vector<CardKind> allowed_kinds;
    std::vector<CardKind> excluded_kinds;
    std::string profession_id;
    std::string series_id;
    std::optional<bool> neutral;
};

struct ConditionSpec {
    ConditionKind kind = ConditionKind::Always;
    std::string condition_id;
    int threshold = 0;
    int read_cap = 0;
    std::string parameter;
    PermanentSelectorSpec permanent_filter;
};

// A compact disjunctive-normal expression that is sufficient for the locked
// product cards without embedding card identity in the interpreter.  Every
// entry in `all` must pass. When `any` is non-empty, at least one entry must
// also pass. Individual predicates remain the same strongly typed conditions
// used by standby validation.
struct ConditionExpr {
    std::vector<ConditionSpec> all;
    std::vector<ConditionSpec> any;
};

enum class AmountSource : std::uint8_t {
    Fixed,
    ActualRepair,
    Cracks,
};

struct ValueSpec {
    AmountSource source = AmountSource::Fixed;
    int fixed = 0;
    int multiplier = 1;
    int cap = 0;
};

enum class EffectDuration : std::uint8_t {
    Immediate,
    OwnerTurn,
    Permanent,
};

enum class OnceScope : std::uint8_t {
    None,
    OwnerTurn,
    Match,
    SourceOwnerTurn,
    SourceTurn,
};

enum class EffectDependency : std::uint8_t {
    None,
    PreviousEffectSucceeded,
    PreviousDrawEnteredHand,
};

enum class TriggerPlayerRelation : std::uint8_t {
    Any,
    SourceController,
    OpponentOfSourceController,
};

enum class OnceConsumption : std::uint8_t {
    OnResolution,
    OnTrigger,
};

struct EffectSpec {
    EffectTrigger trigger = EffectTrigger::OnPlay;
    EffectKind kind = EffectKind::Draw;
    int amount = 0;
    TargetSpec target = TargetSpec::None;
    std::optional<ConditionSpec> condition;
    std::string parameter;
    // Gate 5C program metadata is appended after the frozen Gate 5B fields so
    // existing synthetic aggregate initialization remains source compatible.
    std::string effect_id;
    ConditionExpr conditions;
    ValueSpec value;
    bool uses_value_spec = false;
    int secondary_amount = 0;
    EffectDuration duration = EffectDuration::Immediate;
    OnceScope once_scope = OnceScope::None;
    std::string once_key;
    bool optional = false;
    EffectDependency dependency = EffectDependency::None;
    std::string depends_on_effect_id;
    PermanentSelectorSpec target_filter;
    CardSelectorSpec card_filter;
    Keyword granted_keyword = Keyword::None;
    std::size_t reveal_count = 0;
    std::size_t selection_minimum = 0;
    std::size_t selection_maximum = 0;
    bool randomize_remainder = false;
    bool preserve_source_slot = false;
    // Reuse the target selected by an earlier successful effect in the same
    // sequential program (for example, buff then grant Barrier to that card).
    std::string target_from_effect_id;
    // Distinguishes an explicitly printed +X/+0 from the legacy symmetric
    // `amount` form where secondary_amount was omitted.
    bool uses_secondary_amount = false;
    TriggerPlayerRelation trigger_player_relation = TriggerPlayerRelation::Any;
    OnceConsumption once_consumption = OnceConsumption::OnResolution;
    // Independent from once-per-cycle history: printed "on your turn" effects
    // are ineligible on the opponent's turn, while profession charging is not.
    bool trigger_owner_turn_only = false;
};

struct ModeSpec {
    std::string mode_id;
    std::string label;
    std::vector<EffectSpec> effects;
    TargetSpec target = TargetSpec::None;
    PermanentSelectorSpec target_filter;
};

struct StandbySpec {
    int pp_cost = 0;
    std::vector<ConditionSpec> conditions;
    bool requires_additional_cost = false;
    TargetSpec additional_cost_target = TargetSpec::None;
    PermanentSelectorSpec additional_cost_filter;
    std::size_t additional_cost_minimum = 0;
    std::size_t additional_cost_maximum = 0;
    // Gate 5B preserves the locked design wording until the generic condition
    // interpreter lands. Runtime code must not branch on these strings.
    std::string condition_text;
    std::string additional_cost_text;
};

struct CardDefinition {
    CardIdentity identity;
    std::string name;
    CardAvailability availability = CardAvailability::MainDeck;
    CardKind kind = CardKind::Follower;
    int cost = 0;
    int attack = 0;
    int health = 0;
    int countdown = 0;
    bool can_advance = true;
    int burn_pp_capacity = 0;
    KeywordMask printed_keywords = mask(Keyword::None);
    CardImplementationStatus implementation_status =
        CardImplementationStatus::LockedNotImplemented;
    bool effects_compiled = false;
    std::vector<EffectSpec> effects;
    std::vector<ModeSpec> modes;
    std::optional<StandbySpec> standby;
    std::string canonical_rules_text;

    [[nodiscard]] bool is_executable() const noexcept {
        return implementation_status != CardImplementationStatus::LockedNotImplemented &&
            effects_compiled;
    }
};

struct ProductDeckDefinition {
    std::string deck_id;
    std::string name;
    std::string profession_id;
    std::string series_id;
    std::string leader_id;
    std::vector<DesignId> main_deck;
    std::vector<DesignId> standby;
};

class CardCatalog {
public:
    void add(CardDefinition definition);
    [[nodiscard]] bool contains(std::string_view design_id) const noexcept;
    [[nodiscard]] const CardDefinition& at(std::string_view design_id) const;
    [[nodiscard]] std::size_t size() const noexcept;
    [[nodiscard]] std::vector<DesignId> list_executable(CardAvailability availability) const;
    [[nodiscard]] const std::unordered_map<DesignId, CardDefinition>& definitions() const noexcept;

private:
    std::unordered_map<DesignId, CardDefinition> definitions_;
};

struct CardInstance {
    InstanceId id = 0;
    DesignId design_id;
    PlayerId owner = PlayerId::Player0;
    PlayerId controller = PlayerId::Player0;
    Zone zone = Zone::None;
    std::size_t sequence = 0;
    int current_attack = 0;
    int current_health = 0;
    int maximum_health = 0;
    int countdown = 0;
    int permanent_attack_bonus = 0;
    int permanent_health_bonus = 0;
    int turn_attack_bonus = 0;
    KeywordState keywords;
    bool evolved = false;
    bool entered_this_turn = false;
    bool attacked_this_turn = false;
};

struct PlayerState {
    int leader_health = 25;
    int maximum_leader_health = 25;
    std::array<std::optional<InstanceId>, kMainBoardSize> main_board{};
    std::array<std::optional<InstanceId>, kStrategyZoneSize> tactics{};
    std::optional<InstanceId> field;
    std::vector<InstanceId> deck;
    std::vector<InstanceId> hand;
    std::vector<InstanceId> graveyard;
    std::vector<InstanceId> archive;
    std::vector<InstanceId> standby;
};

struct MoveRecord {
    InstanceId card = 0;
    PlayerId controller = PlayerId::Player0;
    Zone from = Zone::None;
    Zone to = Zone::None;
    MoveReason reason = MoveReason::ScenarioSetup;
    bool destroyed = false;
};

// Observation-only, immutable facts captured at each mutation. These never
// participate in validation, payment, targeting or rule resolution.
struct ObservationEndpoint {
    PlayerId player = PlayerId::Player0;
    bool leader = false;
    std::optional<InstanceId> card;
    std::string design_id;
    std::uint8_t visibility = 3; // bit 0/1: identity visible to that viewer
};

struct ObservationLocation {
    PlayerId player = PlayerId::Player0;
    Zone zone = Zone::None;
    std::optional<std::size_t> slot;
};

struct ObservationState {
    std::optional<int> health;
    std::optional<int> max_health;
    std::optional<int> attack;
    std::optional<int> countdown;
    std::optional<bool> evolved;
    std::optional<KeywordMask> keywords;
};

struct ProductObservation {
    std::string kind;
    std::uint64_t cause_sequence = 0;
    std::optional<ObservationEndpoint> source;
    std::optional<ObservationEndpoint> subject;
    std::optional<ObservationEndpoint> target;
    std::optional<ObservationLocation> from;
    std::optional<ObservationLocation> to;
    std::optional<MoveReason> move_reason;
    std::optional<int> actual_amount;
    std::string damage_kind;
    std::string declaration_kind;
    std::optional<bool> barrier_consumed;
    std::optional<ObservationState> before;
    std::optional<ObservationState> after;
};

enum class ErrorCode : std::uint8_t {
    Ok,
    InvalidPlayer,
    InvalidCard,
    InvalidKind,
    InvalidZone,
    InvalidSlot,
    SlotOccupied,
    SlotReserved,
    MainBoardFull,
    NoPendingChoice,
    ChoicePending,
    NotChoiceOwner,
    InvalidChoice,
    DuplicateSelection,
    WrongSelectionCount,
    ResolutionFinished,
    AlreadyAttacked,
};

struct Status {
    ErrorCode code = ErrorCode::Ok;
    std::string message;

    [[nodiscard]] explicit constexpr operator bool() const noexcept {
        return code == ErrorCode::Ok;
    }

    [[nodiscard]] static Status ok() { return {}; }
    [[nodiscard]] static Status error(ErrorCode code, std::string message) {
        return Status{code, std::move(message)};
    }
};

} // namespace scgs::v2
