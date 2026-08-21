// SPDX-License-Identifier: GPL-3.0-or-later
#include "scgs/protocol.hpp"

#include <algorithm>
#include <limits>
#include <stdexcept>
#include <string>
#include <type_traits>

namespace scgs::protocol {

namespace {

template <typename T, bool IsEnum = std::is_enum_v<T>>
struct raw_type {
    using type = T;
};

template <typename T>
struct raw_type<T, true> {
    using type = std::underlying_type_t<T>;
};

template <typename T>
using raw_type_t = typename raw_type<T>::type;

template <typename T>
void append_le(std::vector<std::uint8_t>& bytes, const T value) {
    static_assert(std::is_integral_v<T> || std::is_enum_v<T>);
    using Raw = raw_type_t<T>;
    using Unsigned = std::make_unsigned_t<Raw>;
    const Unsigned raw = static_cast<Unsigned>(value);
    for (std::size_t index = 0; index < sizeof(Unsigned); ++index) {
        bytes.push_back(static_cast<std::uint8_t>((raw >> (index * 8U)) & 0xFFU));
    }
}

template <typename T>
T read_le(const std::span<const std::uint8_t> bytes, std::size_t& offset) {
    static_assert(std::is_integral_v<T> || std::is_enum_v<T>);
    using Raw = raw_type_t<T>;
    using Unsigned = std::make_unsigned_t<Raw>;
    if (offset + sizeof(Unsigned) > bytes.size()) {
        throw std::invalid_argument("truncated SCGS protocol message");
    }
    Unsigned raw = 0;
    for (std::size_t index = 0; index < sizeof(Unsigned); ++index) {
        raw |= static_cast<Unsigned>(bytes[offset++]) << (index * 8U);
    }
    if constexpr (std::is_enum_v<T>) {
        return static_cast<T>(static_cast<Raw>(raw));
    } else {
        return static_cast<T>(raw);
    }
}

void validate_exact_size(
    const std::span<const std::uint8_t> bytes,
    const std::size_t expected,
    const char* const name) {
    if (bytes.size() != expected) {
        throw std::invalid_argument(std::string(name) + " has an unexpected length");
    }
}

void validate_message_id(
    const std::span<const std::uint8_t> bytes,
    const Message expected) {
    if (bytes.empty() || static_cast<Message>(bytes.front()) != expected) {
        throw std::invalid_argument("unexpected SCGS message id");
    }
}

void validate_payload_version(
    const std::span<const std::uint8_t> payload,
    std::size_t& offset) {
    if (payload.empty()) {
        throw std::invalid_argument("SCGS protocol payload is too short");
    }
    const std::uint8_t version = read_le<std::uint8_t>(payload, offset);
    if (version != kProtocolVersion) {
        throw std::invalid_argument("unsupported SCGS protocol version");
    }
}

std::int16_t narrow_i16(const int value) {
    if (value < std::numeric_limits<std::int16_t>::min() ||
        value > std::numeric_limits<std::int16_t>::max()) {
        throw std::overflow_error("value does not fit protocol int16");
    }
    return static_cast<std::int16_t>(value);
}

std::uint8_t narrow_u8(const int value) {
    if (value < 0 || value > std::numeric_limits<std::uint8_t>::max()) {
        throw std::overflow_error("value does not fit protocol uint8");
    }
    return static_cast<std::uint8_t>(value);
}

} // namespace

std::vector<std::uint8_t> encode_player_state_payload(const PlayerStateWire& state) {
    std::vector<std::uint8_t> payload;
    payload.reserve(kPlayerStatePayloadSize);
    append_le(payload, kProtocolVersion);
    append_le(payload, state.player);
    append_le(payload, state.leader_health);
    append_le(payload, state.maximum_leader_health);
    append_le(payload, state.current_pp);
    append_le(payload, state.maximum_pp);
    append_le(payload, state.evolution_points);
    append_le(payload, state.own_turn_number);
    append_le(payload, state.flags);
    return payload;
}

std::vector<std::uint8_t> encode_player_state(const PlayerStateWire& state) {
    const std::vector<std::uint8_t> payload = encode_player_state_payload(state);
    std::vector<std::uint8_t> bytes;
    bytes.reserve(kPlayerStateMessageSize);
    append_le(bytes, Message::PlayerState);
    bytes.insert(bytes.end(), payload.begin(), payload.end());
    return bytes;
}

std::vector<std::uint8_t> encode_unit_state_payload(const UnitStateWire& state) {
    std::vector<std::uint8_t> payload;
    payload.reserve(kUnitStatePayloadSize);
    append_le(payload, kProtocolVersion);
    append_le(payload, state.controller);
    append_le(payload, state.sequence);
    append_le(payload, state.instance_id);
    append_le(payload, state.attack);
    append_le(payload, state.health);
    append_le(payload, state.maximum_health);
    append_le(payload, state.keywords);
    append_le(payload, state.inherited_imprint);
    append_le(payload, state.flags);
    return payload;
}

std::vector<std::uint8_t> encode_unit_state(const UnitStateWire& state) {
    const std::vector<std::uint8_t> payload = encode_unit_state_payload(state);
    std::vector<std::uint8_t> bytes;
    bytes.reserve(kUnitStateMessageSize);
    append_le(bytes, Message::UnitState);
    bytes.insert(bytes.end(), payload.begin(), payload.end());
    return bytes;
}

PlayerStateWire decode_player_state_payload(const std::span<const std::uint8_t> payload) {
    validate_exact_size(payload, kPlayerStatePayloadSize, "SCGS player-state payload");
    std::size_t offset = 0;
    validate_payload_version(payload, offset);
    PlayerStateWire state;
    state.player = read_le<PlayerId>(payload, offset);
    state.leader_health = read_le<std::int16_t>(payload, offset);
    state.maximum_leader_health = read_le<std::int16_t>(payload, offset);
    state.current_pp = read_le<std::uint8_t>(payload, offset);
    state.maximum_pp = read_le<std::uint8_t>(payload, offset);
    state.evolution_points = read_le<std::uint8_t>(payload, offset);
    state.own_turn_number = read_le<std::uint8_t>(payload, offset);
    state.flags = read_le<std::uint8_t>(payload, offset);
    return state;
}

PlayerStateWire decode_player_state(const std::span<const std::uint8_t> bytes) {
    validate_exact_size(bytes, kPlayerStateMessageSize, "SCGS player-state message");
    validate_message_id(bytes, Message::PlayerState);
    return decode_player_state_payload(bytes.subspan(1));
}

UnitStateWire decode_unit_state_payload(const std::span<const std::uint8_t> payload) {
    validate_exact_size(payload, kUnitStatePayloadSize, "SCGS unit-state payload");
    std::size_t offset = 0;
    validate_payload_version(payload, offset);
    UnitStateWire state;
    state.controller = read_le<PlayerId>(payload, offset);
    state.sequence = read_le<std::uint8_t>(payload, offset);
    state.instance_id = read_le<InstanceId>(payload, offset);
    state.attack = read_le<std::int16_t>(payload, offset);
    state.health = read_le<std::int16_t>(payload, offset);
    state.maximum_health = read_le<std::int16_t>(payload, offset);
    state.keywords = read_le<KeywordMask>(payload, offset);
    state.inherited_imprint = read_le<Imprint>(payload, offset);
    state.flags = read_le<std::uint8_t>(payload, offset);
    return state;
}

UnitStateWire decode_unit_state(const std::span<const std::uint8_t> bytes) {
    validate_exact_size(bytes, kUnitStateMessageSize, "SCGS unit-state message");
    validate_message_id(bytes, Message::UnitState);
    return decode_unit_state_payload(bytes.subspan(1));
}

// The bridge maps the v0.4 engine state onto the FROZEN legacy v1 wire. The
// wire structs, byte layout and golden vectors never change; only this
// projection adapts. v0.4 semantics nearest to the legacy fields are used:
// bit1 本回合已高级召唤 → 本回合已部署; bit3 高级召唤入场 → 战备部署入场.
PlayerStateWire make_player_state_wire(const PlayerId player_id, const PlayerState& state) {
    std::uint8_t flags = 0;
    flags |= state.evolution_used_this_turn ? 1U << 0U : 0U;
    flags |= state.deploy_used_this_turn ? 1U << 1U : 0U;
    flags |= state.trap_set_this_turn ? 1U << 2U : 0U;
    flags |= state.leader_skill_used ? 1U << 3U : 0U;
    return PlayerStateWire{
        player_id,
        narrow_i16(state.leader_health),
        narrow_i16(state.maximum_leader_health),
        // v0.4 current_pp may legally exceed capacity; the legacy uint8 wire
        // field saturates as well.
        narrow_u8(std::min(state.current_pp, static_cast<int>(std::numeric_limits<std::uint8_t>::max()))),
        // v0.4 pp_capacity is uncapped; the legacy uint8 wire field saturates.
        narrow_u8(std::min(state.pp_capacity, static_cast<int>(std::numeric_limits<std::uint8_t>::max()))),
        narrow_u8(state.evolution_points),
        narrow_u8(state.own_turn_number),
        flags,
    };
}

UnitStateWire make_unit_state_wire(const CardInstance& unit) {
    std::uint8_t flags = 0;
    flags |= unit.evolved ? 1U << 0U : 0U;
    flags |= unit.attacked_this_turn ? 1U << 1U : 0U;
    flags |= unit.entered_this_turn ? 1U << 2U : 0U;
    flags |= (unit.deployed_from_standby && unit.entered_this_turn) ? 1U << 3U : 0U;
    flags |= unit.face_down ? 1U << 4U : 0U;
    return UnitStateWire{
        unit.controller,
        narrow_u8(static_cast<int>(unit.sequence)),
        unit.id,
        narrow_i16(unit.current_attack),
        narrow_i16(unit.current_health),
        narrow_i16(unit.maximum_health),
        unit.keywords,
        unit.inherited_imprint,
        flags,
    };
}

} // namespace scgs::protocol
