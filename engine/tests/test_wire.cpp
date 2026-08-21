// SPDX-License-Identifier: GPL-3.0-or-later
//
// Legacy v1 wire freeze regression (interview decision: the protocol wire is
// frozen as a legacy slice). This binary pins the exact byte layout, the
// golden vectors from docs/protocol.md, and the v0.4 bridge projection
// (capacity saturation at uint8 max).
#include "scgs/game.hpp"
#include "scgs/protocol.hpp"

#include <cstdlib>
#include <iostream>
#include <span>
#include <string>
#include <vector>

namespace {

using namespace scgs;
using namespace scgs::protocol;

int failures = 0;
int assertions = 0;

void expect(const bool condition, const std::string& what) {
    ++assertions;
    if (!condition) {
        ++failures;
        std::cerr << "expectation failed: " << what << '\n';
    }
}

bool bytes_equal(const std::vector<std::uint8_t>& lhs, const std::vector<std::uint8_t>& rhs) {
    return lhs == rhs;
}

void test_player_state_golden() {
    // docs/protocol.md fixed vector:
    // D3 01 01 11 00 19 00 03 07 02 06 03
    const std::vector<std::uint8_t> golden = {
        0xD3, 0x01, 0x01, 0x11, 0x00, 0x19, 0x00, 0x03, 0x07, 0x02, 0x06, 0x03,
    };
    const PlayerStateWire decoded = decode_player_state(golden);
    expect(decoded.player == PlayerId::Player1, "golden player id");
    expect(decoded.leader_health == 17, "golden leader health");
    expect(decoded.maximum_leader_health == 25, "golden max health");
    expect(decoded.current_pp == 3, "golden current pp");
    expect(decoded.maximum_pp == 7, "golden maximum pp");
    expect(decoded.evolution_points == 2, "golden evolution points");
    expect(decoded.own_turn_number == 6, "golden turn number");
    expect(decoded.flags == 3, "golden flags");
    const std::vector<std::uint8_t> reencoded = encode_player_state(decoded);
    expect(bytes_equal(reencoded, golden), "player state golden round trip");
}

void test_unit_state_golden() {
    // docs/protocol.md fixed vector:
    // D4 01 00 03 08 07 06 05 04 03 02 01
    // 07 00 05 00 08 00 03 00 00 00 01 09
    const std::vector<std::uint8_t> golden = {
        0xD4, 0x01, 0x00, 0x03, 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
        0x07, 0x00, 0x05, 0x00, 0x08, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x09,
    };
    const UnitStateWire decoded = decode_unit_state(golden);
    expect(decoded.controller == PlayerId::Player0, "golden unit controller");
    expect(decoded.sequence == 3, "golden unit sequence");
    expect(decoded.instance_id == 0x0102030405060708ULL, "golden unit instance id");
    expect(decoded.attack == 7, "golden unit attack");
    expect(decoded.health == 5, "golden unit health");
    expect(decoded.maximum_health == 8, "golden unit max health");
    expect(decoded.keywords == 0x00000003U, "golden unit keywords");
    expect(decoded.inherited_imprint == Imprint::Guard, "golden unit imprint");
    expect(decoded.flags == 9, "golden unit flags");
    const std::vector<std::uint8_t> reencoded = encode_unit_state(decoded);
    expect(bytes_equal(reencoded, golden), "unit state golden round trip");
}

void test_validation_rejects_bad_input() {
    bool threw = false;
    try {
        const std::vector<std::uint8_t> truncated = {0xD3, 0x01, 0x01};
        (void)decode_player_state(truncated);
    } catch (const std::exception&) {
        threw = true;
    }
    expect(threw, "truncated player state rejected");

    threw = false;
    try {
        const std::vector<std::uint8_t> wrong_id = {
            0xD2, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };
        (void)decode_player_state(wrong_id);
    } catch (const std::exception&) {
        threw = true;
    }
    expect(threw, "wrong message id rejected");

    threw = false;
    try {
        const std::vector<std::uint8_t> bad_version = {
            0x02, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };
        (void)decode_player_state_payload(bad_version);
    } catch (const std::exception&) {
        threw = true;
    }
    expect(threw, "unsupported protocol version rejected");
}

void test_bridge_projection_saturates_capacity() {
    // The frozen legacy wire keeps a uint8 maximum_pp field while v0.4 capacity
    // is uncapped; the bridge saturates instead of overflowing (frozen slice).
    PlayerState state;
    state.pp_capacity = 300;
    state.current_pp = 300;
    const PlayerStateWire wire = make_player_state_wire(PlayerId::Player0, state);
    expect(wire.maximum_pp == 255, "bridge saturates capacity at uint8 max");
    expect(wire.current_pp == 255, "bridge saturates current pp at uint8 max");

    // v0.4 flags projection: deployment maps onto the legacy advanced-summon bit.
    PlayerState state2;
    state2.deploy_used_this_turn = true;
    const PlayerStateWire wire2 = make_player_state_wire(PlayerId::Player0, state2);
    expect((wire2.flags & (1U << 1U)) != 0, "deployment maps to legacy bit 1");
}

void test_round_trip_arbitrary() {
    PlayerStateWire player;
    player.player = PlayerId::Player1;
    player.leader_health = 22;
    player.maximum_leader_health = 25;
    player.current_pp = 9;
    player.maximum_pp = 12;
    player.evolution_points = 4;
    player.own_turn_number = 11;
    player.flags = 5;
    const std::vector<std::uint8_t> full = encode_player_state(player);
    const PlayerStateWire decoded = decode_player_state(full);
    expect(decoded.player == player.player, "round trip player id");
    expect(decoded.current_pp == 9, "round trip current pp");
    expect(decoded.flags == 5, "round trip flags");

    UnitStateWire unit;
    unit.controller = PlayerId::Player0;
    unit.sequence = 4;
    unit.instance_id = 0xDEADBEEFCAFEF00DULL;
    unit.attack = 7;
    unit.health = 2;
    unit.maximum_health = 9;
    unit.keywords = 0x12345678U;
    unit.inherited_imprint = Imprint::Lifesteal;
    unit.flags = 0x1F;
    const std::vector<std::uint8_t> unit_full = encode_unit_state(unit);
    const UnitStateWire unit_decoded = decode_unit_state(unit_full);
    expect(unit_decoded.instance_id == unit.instance_id, "round trip unit id");
    expect(unit_decoded.keywords == unit.keywords, "round trip keywords");
    expect(unit_decoded.flags == unit.flags, "round trip unit flags");
}

} // namespace

int main() {
    test_player_state_golden();
    test_unit_state_golden();
    test_validation_rejects_bad_input();
    test_bridge_projection_saturates_capacity();
    test_round_trip_arbitrary();

    std::cout << "wire legacy v1: " << assertions << " assertions, " << failures << " failures\n";
    return failures == 0 ? EXIT_SUCCESS : EXIT_FAILURE;
}
