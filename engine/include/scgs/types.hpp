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
using TraitMask = std::uint32_t;
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
    SummonUnit,
};

enum class Zone : std::uint8_t {
    None,
    Deck,
    Hand,
    Unit,
    Tactic,
    Graveyard,
    Archive,
    SummonDeck,
};

enum class Trait : TraitMask {
    None = 0,
    Soldier = 1U << 0U,
    Knight = 1U << 1U,
    Machine = 1U << 2U,
    Part = 1U << 3U,
    Construct = 1U << 4U,
};

[[nodiscard]] constexpr TraitMask mask(const Trait value) noexcept {
    return static_cast<TraitMask>(value);
}

[[nodiscard]] constexpr TraitMask operator|(const Trait lhs, const Trait rhs) noexcept {
    return mask(lhs) | mask(rhs);
}

[[nodiscard]] constexpr bool has_all_traits(const TraitMask value, const TraitMask required) noexcept {
    return (value & required) == required;
}

enum class Keyword : KeywordMask {
    None = 0,
    Guard = 1U << 0U,
    Rush = 1U << 1U,
    Storm = 1U << 2U,
    Barrier = 1U << 3U,
    Bane = 1U << 4U,
    Lifesteal = 1U << 5U,
    Ambush = 1U << 6U,
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

enum class Imprint : std::uint8_t {
    None,
    Guard,
    Rush,
    Barrier,
    Lifesteal,
    LastWordsDrawOne,
};

enum class Ability : std::uint8_t {
    None,
    DrawOne,
    DealTwoToEnemyUnit,
    DealThreeToEnemyUnit,
    HealLeaderThree,
    GiveFriendlyUnitOneOne,
    CreateRushPartInHand,
    TrapCancelAttack,
    TrapDamageSummonedUnitTwo,
};

enum class AdvancedSummonKind : std::uint8_t {
    None,
    Tribute,
    Construct,
};

enum class EvolutionMode : std::uint8_t {
    Combat,
    Ability,
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
    AdvancedSummonAlreadyUsed,
    InvalidMaterials,
    InvalidImprint,
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
