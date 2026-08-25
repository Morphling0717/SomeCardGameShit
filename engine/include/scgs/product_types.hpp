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

// Product definitions are generated before their complete effect programs are
// compiled.  Keeping that state on every definition prevents identity-only
// catalog rows from accidentally becoming payable actions.  SyntheticFixture
// is reserved for the rule-kernel tests; ExecutableProduct is the future Gate
// 5C state for a fully compiled product card.
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
};

struct ConditionSpec {
    ConditionKind kind = ConditionKind::Always;
    std::string condition_id;
    int threshold = 0;
    int read_cap = 0;
    std::string parameter;
    PermanentSelectorSpec permanent_filter;
};

struct EffectSpec {
    EffectTrigger trigger = EffectTrigger::OnPlay;
    EffectKind kind = EffectKind::Draw;
    int amount = 0;
    TargetSpec target = TargetSpec::None;
    std::optional<ConditionSpec> condition;
    std::string parameter;
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
