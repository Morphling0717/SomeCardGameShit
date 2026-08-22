// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once

#include "scgs/card.hpp"
#include "scgs/types.hpp"

#include <array>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace scgs {

enum class EventType : std::uint8_t;

// The command vocabulary deliberately contains only actions supported by the
// first Godot hot-seat client. Leader skills and other active abilities remain
// available to engine tests through the strongly typed Game API.
enum class ActionKind : std::uint8_t {
    Mulligan,
    PlayUnit,
    CastSpell,
    PlayTactic,
    Attack,
    Evolve,
    Deploy,
    ActivateTrap,
    PassReaction,
    EndTurn,
    Surrender,
};

// A value-type command that is safe to carry across a native client boundary.
// `source` is the card/unit/trap acted on. Fields unused by an ActionKind must
// retain their default value and are ignored by the dispatcher.
struct GameCommand {
    PlayerId player = PlayerId::Player0;
    ActionKind action = ActionKind::EndTurn;
    InstanceId source = 0;
    std::optional<Target> target;
    std::optional<std::size_t> slot;
    std::optional<InstanceId> component_donor;
    bool use_advance = false;
    std::vector<InstanceId> mulligan_cards;
    std::uint64_t expected_revision = 0;
};

// Filters the legal-action list without weakening validation. Every populated
// field is an exact-match filter; omitted fields are expanded by the query.
struct ActionQuery {
    PlayerId player = PlayerId::Player0;
    std::optional<ActionKind> action;
    std::optional<InstanceId> source;
    std::optional<Target> target;
    std::optional<std::size_t> slot;
    std::optional<InstanceId> component_donor;
    std::optional<bool> use_advance;
    std::vector<InstanceId> mulligan_cards;
    std::uint64_t expected_revision = 0;
};

struct PaymentPreview {
    Status status;
    int current_pp_before = 0;
    int current_pp_after = 0;
    int pp_capacity_before = 0;
    int pp_capacity_after = 0;
    int cracks_before = 0;
    int cracks_after = 0;
    int evolution_energy_before = 0;
    int evolution_energy_after = 0;
    int base_cost = 0;
    int burn_cost = 0;
    int advance_cost = 0;
    bool used_advance = false;
};

struct LegalAction {
    GameCommand command;
    PaymentPreview payment;
};

// Optional identities are the privacy boundary. A face-down opposing trap is
// represented by its occupied slot and face-down state, but both IDs and all
// definition data are absent.
struct CardView {
    std::optional<InstanceId> instance_id;
    std::optional<CardId> definition_id;
    std::optional<CardDefinition> definition;
    std::optional<CardKind> kind;
    std::string name;

    PlayerId owner = PlayerId::Player0;
    PlayerId controller = PlayerId::Player0;
    Zone zone = Zone::None;
    std::size_t sequence = 0;

    int cost = 0;
    int current_attack = 0;
    int current_health = 0;
    int maximum_health = 0;
    KeywordMask keywords = mask(Keyword::None);
    bool evolved = false;
    bool attacked_this_turn = false;
    bool entered_this_turn = false;
    bool temporary_rush = false;
    bool deployed_from_standby = false;
    bool face_down = false;
    int countdown = 0;
    ComponentSpec granted_component;
};

struct PlayerView {
    PlayerId player = PlayerId::Player0;
    int leader_health = 0;
    int maximum_leader_health = 0;
    int current_pp = 0;
    int pp_capacity = 0;
    int cracks = 0;
    int evolution_energy = 0;
    int own_turn_number = 0;
    int fatigue_count = 0;

    bool mulligan_done = false;
    bool evolution_used_this_turn = false;
    bool advance_used_this_turn = false;
    bool deploy_used_this_turn = false;
    bool trap_set_this_turn = false;
    bool leader_skill_used = false;
    bool charge_granted_this_cycle = false;
    int friendly_deaths_this_cycle = 0;
    int spells_used_this_turn = 0;
    int units_played_this_turn = 0;
    LeaderSkillDefinition leader_skill;

    std::size_t deck_count = 0;
    std::size_t hand_count = 0;
    std::vector<CardView> hand; // populated only when player == viewer
    std::array<std::optional<CardView>, kUnitZoneSize> units{};
    std::array<std::optional<CardView>, kTacticZoneSize> tactics{};
    std::vector<CardView> graveyard;
    std::vector<CardView> archive;
    std::vector<CardView> standby;
};

struct ReactionContext {
    bool pending = false;
    ReactionWindow window = ReactionWindow::None;
    PlayerId responder = PlayerId::Player0;
    InstanceId subject = 0; // public spell/unit/attack subject, or 0
    std::size_t depth = 0;
    std::size_t eligible_count = 0;
    std::vector<CardView> eligible_traps; // populated only for the responder
    std::uint64_t revision = 0;
};

struct MatchView {
    PlayerId viewer = PlayerId::Player0;
    PlayerId active_player = PlayerId::Player0;
    PlayerId first_player = PlayerId::Player0;
    std::uint32_t random_seed = 0;
    Phase phase = Phase::NotStarted;
    GameResult result = GameResult::Ongoing;
    std::uint64_t revision = 0;
    std::array<PlayerView, kPlayerCount> players{};
    ReactionContext reaction;
};

struct GameEventView {
    std::uint64_t sequence = 0;
    EventType type = static_cast<EventType>(0);
    PlayerId player = PlayerId::Player0;
    std::optional<InstanceId> card;
    std::optional<CardId> definition_id;
    int value = 0;
    int secondary_value = 0;
    bool hidden_card = false;
    std::string text;
    std::optional<std::uint32_t> random_seed; // populated on MatchStarted
    std::optional<PlayerId> first_player;     // populated on MatchStarted
};

} // namespace scgs
