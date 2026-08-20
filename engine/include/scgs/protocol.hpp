// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once

#include "scgs/game.hpp"

#include <cstddef>
#include <cstdint>
#include <span>
#include <vector>

namespace scgs::protocol {

inline constexpr std::uint8_t kProtocolVersion = 1;

// YGOPro2 currently uses values through 200 for core messages and 230+ for its
// own Sibyl messages. The prototype reserves 210-219 and documents that range.
enum class Message : std::uint8_t {
    GameMode = 210,
    PlayerState = 211,
    UnitState = 212,
    EvolutionState = 213,
    AdvancedSummonState = 214,
    RequestEvolutionMode = 215,
    RequestMaterials = 216,
    RequestImprint = 217,
    TacticWindow = 218,
    MatchStatistics = 219,
};

// A complete SCGS wire message is [message id][payload]. YGOPro2 stores the
// first byte separately in Package.Fuction, so its Package.Data contains only
// the payload beginning with kProtocolVersion.
inline constexpr std::size_t kPlayerStatePayloadSize = 11;
inline constexpr std::size_t kPlayerStateMessageSize = 12;
inline constexpr std::size_t kUnitStatePayloadSize = 23;
inline constexpr std::size_t kUnitStateMessageSize = 24;

struct PlayerStateWire {
    PlayerId player = PlayerId::Player0;
    std::int16_t leader_health = 0;
    std::int16_t maximum_leader_health = 0;
    std::uint8_t current_pp = 0;
    std::uint8_t maximum_pp = 0;
    std::uint8_t evolution_points = 0;
    std::uint8_t own_turn_number = 0;
    std::uint8_t flags = 0;
};

struct UnitStateWire {
    PlayerId controller = PlayerId::Player0;
    std::uint8_t sequence = 0;
    InstanceId instance_id = 0;
    std::int16_t attack = 0;
    std::int16_t health = 0;
    std::int16_t maximum_health = 0;
    KeywordMask keywords = 0;
    Imprint inherited_imprint = Imprint::None;
    std::uint8_t flags = 0;
};

[[nodiscard]] std::vector<std::uint8_t> encode_player_state(const PlayerStateWire& state);
[[nodiscard]] std::vector<std::uint8_t> encode_player_state_payload(const PlayerStateWire& state);
[[nodiscard]] std::vector<std::uint8_t> encode_unit_state(const UnitStateWire& state);
[[nodiscard]] std::vector<std::uint8_t> encode_unit_state_payload(const UnitStateWire& state);

[[nodiscard]] PlayerStateWire decode_player_state(std::span<const std::uint8_t> bytes);
[[nodiscard]] PlayerStateWire decode_player_state_payload(std::span<const std::uint8_t> payload);
[[nodiscard]] UnitStateWire decode_unit_state(std::span<const std::uint8_t> bytes);
[[nodiscard]] UnitStateWire decode_unit_state_payload(std::span<const std::uint8_t> payload);

[[nodiscard]] PlayerStateWire make_player_state_wire(PlayerId player, const PlayerState& state);
[[nodiscard]] UnitStateWire make_unit_state_wire(const CardInstance& unit);

} // namespace scgs::protocol
