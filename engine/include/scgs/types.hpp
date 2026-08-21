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
constexpr std::size_t kTacticZoneSize = 2;

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
    OnEvolution,          // unit is evolved (ability trigger)
    OnLastWords,          // unit is destroyed
    OnCountdownExpire,    // relic countdown reaches zero
    OnTrapBeforeAttack,   // trap window: before attack damage resolves
    OnTrapAfterEnemyUnit, // trap window: after enemy unit enters field
};

// What the effect does.
enum class EffectKind : std::uint8_t {
    DrawCards,
    DealDamageToEnemyUnit,
    DealDamageToLeader,
    HealLeader,
    RepairCracks,              // 修复X: remove ≤X cracks, restore capacity
    GainPPCapacity,            // 增长X: directly add to PP capacity
    BuffFriendlyUnit,          // give friendly unit +amount/+amount
    CancelAttack,              // trap: cancel the pending attack
    DamageEnteredUnit,         // trap: damage the unit that just entered
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

// ------------------------------------------------------------------------------

enum class EvolutionMode : std::uint8_t {
    Combat,   // +2/+2, temporary rush to units
    Ability,  // +1/+1, trigger on-evolution effect
};

enum class Phase : std::uint8_t {
    NotStarted,
    Mulligan,
    Action,
    Reaction,
    Finished,
};

enum class ReactionWindow : std::uint8_t {
    None,
    AfterEnemyUnitSummoned,
    AfterEnemySpellResolved,
    BeforeAttackDamage,
    AfterFriendlyUnitDestroyed,
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
    AbilityEvolutionUnavailable,
    AdvanceAlreadyUsed,      // v0.4: 动用未来 already used this turn
    AdvanceWouldExceedCap,   // v0.4: advance would bring pp_capacity below 0
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
