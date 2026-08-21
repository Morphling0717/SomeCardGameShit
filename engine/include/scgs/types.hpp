// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once

#include <cstddef>
#include <cstdint>
#include <optional>
#include <string>
#include <utility>

namespace scgs {

using CardId = std::uint32_t;
using InstanceId = std::uint64_t;
// KeywordMask is kept as a wire-stable type (protocol.hpp encodes it verbatim).
using KeywordMask = std::uint32_t;

constexpr std::size_t kPlayerCount = 2;
constexpr std::size_t kUnitZoneSize = 5;
// v0.4: 策略区 3 格 per player (facilities and traps share it).
constexpr std::size_t kTacticZoneSize = 3;

// PlayerId deliberately stays byte-sized because YGOPro messages use compact player ids.
enum class PlayerId : std::uint8_t {
    Player0 = 0,
    Player1 = 1,
};

[[nodiscard]] constexpr std::size_t to_index(const PlayerId player) noexcept {
    return static_cast<std::size_t>(player);
}

[[nodiscard]] constexpr PlayerId opponent(const PlayerId player) noexcept {
    return player == PlayerId::Player0 ? PlayerId::Player1 : PlayerId::Player0;
}

enum class CardKind : std::uint8_t {
    Unit,
    Spell,
    Relic,
    Trap,
};

enum class Zone : std::uint8_t {
    None,
    Deck,
    Hand,
    Unit,
    Tactic,
    Graveyard,
    Archive,
    Standby,    // v0.4 战备区 (public standby zone)
};

// Keyword bits are wire-stable (protocol.hpp serialises KeywordMask verbatim).
// v0.4 formal keyword names are not yet finalised; in card definitions these
// semantics are expressed as boolean flags (printed_guard, printed_rush …) and
// mapped to this mask when a unit enters the field so the wire remains valid.
enum class Keyword : KeywordMask {
    None     = 0,
    Guard    = 1U << 0U,   // must be attacked first
    Rush     = 1U << 1U,   // can attack enemy units on entry turn
    Storm    = 1U << 2U,   // can attack leader on entry turn
    Barrier  = 1U << 3U,   // absorbs one hit
    Bane     = 1U << 4U,   // any damage destroys target
    Lifesteal = 1U << 5U,  // damage dealt heals leader
    Ambush   = 1U << 6U,   // cannot be targeted while face-down
};

[[nodiscard]] constexpr KeywordMask mask(const Keyword value) noexcept {
    return static_cast<KeywordMask>(value);
}

[[nodiscard]] constexpr KeywordMask operator|(const Keyword lhs, const Keyword rhs) noexcept {
    return mask(lhs) | mask(rhs);
}

[[nodiscard]] constexpr bool has_keyword(const KeywordMask value, const Keyword keyword) noexcept {
    return (value & mask(keyword)) != 0U;
}

// Imprint is kept as a wire-stable type.  In v0.4 the imprint inheritance
// system is not used; the field is always None on non-protocol paths.
enum class Imprint : std::uint8_t {
    None,
    Guard,
    Rush,
    Barrier,
    Lifesteal,
    LastWordsDrawOne,
};

// v0.4 data-driven effect system -----------------------------------------------

// When an effect fires.
enum class EffectTrigger : std::uint8_t {
    OnPlay,               // spell/unit played from hand (always)
    OnPlayIfAdvanced,     // played from hand using advance (超前)
    OnPlayIfNotAdvanced,  // played from hand without advance (按期)
    OnEntry,              // unit enters the field (after paying cost)
    OnEvolution,          // unit is evolved ("进化时" trigger)
    OnLastWords,          // unit is destroyed
    OnCountdownExpire,    // relic countdown reaches zero
    OnAttackDeclared,     // trap window: opponent declared an attack
    OnEntryEffectPending, // trap window: enemy unit entry effect is about to resolve
};

// What the effect does.
enum class EffectKind : std::uint8_t {
    DrawCards,
    DealDamageToEnemyUnit,
    DealDamageToLeader,
    HealLeader,
    RepairCracks,              // 修复X: remove ≤X cracks, restore capacity
    GainPPCapacity,            // 增长X: directly add to PP capacity
    BuffFriendlyUnit,          // give a friendly unit +amount/+amount
    GrantRush,                 // grant this unit "can attack enemy units this turn"
    CancelAttack,              // trap: cancel the pending attack
    DamageEnteredUnit,         // trap: damage the unit whose entry is pending
};

enum class TargetSpec : std::uint8_t {
    None,
    EnemyUnit,
    FriendlyUnit,
};

struct EffectRecord {
    EffectTrigger trigger = EffectTrigger::OnEntry;
    EffectKind kind = EffectKind::DrawCards;
    int amount = 0;
    TargetSpec target_spec = TargetSpec::None;
};

// AdditionalCost models costs that permanently reduce PP capacity.
// 燃耗X: reduce PP capacity by X as part of the play cost.
struct AdditionalCost {
    int burn_pp_capacity = 0;
};

// ----------------------------------------------------------------------------
// v0.4 战备部署 (standby deployment). Every standby card writes its own
// deployment condition and cost in data; there are no fixed summon categories.
// ----------------------------------------------------------------------------

enum class DeploymentCondition : std::uint8_t {
    None,               // no board condition
    FriendlyUnitsMin,   // own unit zone holds >= amount units
    SpellsThisTurnMin,  // player used >= amount spells this turn
};

struct DeploymentSpec {
    DeploymentCondition condition = DeploymentCondition::None;
    int condition_amount = 0;
    int pp_cost = 0;                         // 部署费用
    bool archive_one_friendly_unit = false;  // 部署代价：封存一个己方单位（组件来源）
};

// ----------------------------------------------------------------------------
// v0.4 组件能力 (component ability): a card that pays a deployment cost can
// grant one ability to the deployed unit. The grant is a runtime modifier on
// CardInstance; invariants: at most one per deployment, no re-transfer.
// ----------------------------------------------------------------------------

struct ComponentSpec {
    bool has_component = false;
    EffectKind granted_kind = EffectKind::GrantRush; // bounded vocabulary
    int granted_amount = 0;
};

// ----------------------------------------------------------------------------
// v0.4 职业进化充能条件 (class evolution charge condition): data-driven
// archetype + parameter attached to the deck/class level. At most one point
// of evolution energy per turn cycle (own turn start → next own turn start).
// ----------------------------------------------------------------------------

enum class ChargeCondition : std::uint8_t {
    None,                    // no charging beyond the initial grant
    FriendlyDeathsPerCycle,  // the Nth friendly unit destroyed this cycle grants 1
    SpellsNoUnitsThisTurn,   // at own end of turn: ≥N spells used and no unit played
};

// ------------------------------------------------------------------------------

enum class Phase : std::uint8_t {
    NotStarted,
    Mulligan,
    Action,
    Reaction,
    Finished,
};

// v0.4 §26 response windows: a response may open when a spell is used, when a
// unit entry effect is about to resolve, when an attack is declared, or on
// card-specified special timings. Payment actions never open a window.
enum class ReactionWindow : std::uint8_t {
    None,
    SpellDeclared,
    EntryEffectPending,
    AttackDeclared,
};

enum class GameResult : std::uint8_t {
    Ongoing,
    Player0Won,
    Player1Won,
    Draw,
};

enum class ErrorCode : std::uint8_t {
    Ok,
    InvalidPhase,
    NotActivePlayer,
    InvalidPlayer,
    InvalidCard,
    InvalidZone,
    InvalidTarget,
    InvalidSlot,
    InsufficientPP,
    HandLimit,
    UnitZoneFull,
    TacticZoneFull,
    SummoningSickness,
    AlreadyAttacked,
    GuardBlocksTarget,
    EvolutionLocked,
    NoEvolutionPoints,
    EvolutionAlreadyUsed,
    AlreadyEvolved,
    AdvanceAlreadyUsed,        // v0.4: 动用未来 already used this turn
    AdvanceWouldExceedCap,     // v0.4: advance would bring pp_capacity below 0
    DeployAlreadyUsed,         // v0.4: deployment already used this turn
    DeployConditionNotMet,     // v0.4: deployment condition not satisfied
    InvalidDeployment,         // v0.4: deployment target/cost/component invalid
    ResponseDepthExceeded,     // v0.4: response stack already at 3 layers
    TrapAlreadySetThisTurn,
    NoPendingReaction,
    TrapNotEligible,
    LeaderSkillLocked,
    LeaderSkillAlreadyUsed,
    MatchAlreadyStarted,
    MatchNotStarted,
    MulliganAlreadyDone,
    DuplicateSelection,
    GameOver,
};

struct Status {
    ErrorCode code = ErrorCode::Ok;
    std::string message;

    [[nodiscard]] constexpr explicit operator bool() const noexcept {
        return code == ErrorCode::Ok;
    }

    [[nodiscard]] static Status ok() {
        return {};
    }

    [[nodiscard]] static Status error(const ErrorCode code_value, std::string message_value) {
        return Status{code_value, std::move(message_value)};
    }
};

struct Target {
    enum class Kind : std::uint8_t {
        Leader,
        Unit,
    };

    Kind kind = Kind::Leader;
    PlayerId player = PlayerId::Player0;
    InstanceId unit = 0;

    [[nodiscard]] static Target leader(const PlayerId player_value) noexcept {
        return Target{Kind::Leader, player_value, 0};
    }

    [[nodiscard]] static Target unit_target(const PlayerId player_value, const InstanceId unit_value) noexcept {
        return Target{Kind::Unit, player_value, unit_value};
    }
};

} // namespace scgs
