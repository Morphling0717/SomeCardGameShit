// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json.Serialization;

namespace Scgs.GodotClient.Ci;

/// <summary>No cards, names, targets, option IDs, seeds or event text belong here.</summary>
internal sealed record ProductSmokeReport
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; init; } = 1;
    [JsonPropertyName("suite")] public string Suite { get; init; } = "product-v05-ui";
    [JsonPropertyName("api")] public string Api { get; init; } = "scgs_v05";
    [JsonPropertyName("abi_major")] public int AbiMajor { get; init; } = 2;
    [JsonPropertyName("engine_schema")] public int EngineSchema { get; init; } = 2;
    [JsonPropertyName("product_scene")] public string ProductScene { get; init; } = "res://scenes/match/ProductMatch.tscn";
    [JsonPropertyName("visual_profile")] public required string VisualProfile { get; init; }
    [JsonPropertyName("run_kind")] public required string RunKind { get; init; }
    [JsonPropertyName("coverage")] public required string Coverage { get; init; }
    [JsonPropertyName("frame_clock")] public required string FrameClock { get; init; }
    [JsonPropertyName("viewport_width")] public int ViewportWidth { get; init; }
    [JsonPropertyName("viewport_height")] public int ViewportHeight { get; init; }
    [JsonPropertyName("player0_deck")] public string Player0Deck { get; init; } = "oathguard_luminous_oath_v1";
    [JsonPropertyName("player1_deck")] public string Player1Deck { get; init; } = "pactmage_abyssal_pact_v1";
    [JsonPropertyName("pointer_inputs")] public int PointerInputs { get; init; }
    [JsonPropertyName("spatial_inputs")] public int SpatialInputs { get; init; }
    [JsonPropertyName("keyboard_inputs")] public int KeyboardInputs { get; init; }
    [JsonPropertyName("invalid_drag_owner_checks")] public int InvalidDragOwnerChecks { get; init; }
    [JsonPropertyName("invalid_drag_zone_checks")] public int InvalidDragZoneChecks { get; init; }
    [JsonPropertyName("selection_back_checks")] public int SelectionBackChecks { get; init; }
    [JsonPropertyName("reaction_surrender_checks")] public int ReactionSurrenderChecks { get; init; }
    [JsonPropertyName("choice_surrender_checks")] public int ChoiceSurrenderChecks { get; init; }
    [JsonPropertyName("commands")] public int Commands { get; init; }
    [JsonPropertyName("action_counts")] public required int[] ActionCounts { get; init; }
    [JsonPropertyName("natural_terminals")] public int NaturalTerminals { get; init; }
    [JsonPropertyName("surrender_terminals")] public int SurrenderTerminals { get; init; }
    [JsonPropertyName("restarts")] public int Restarts { get; init; }
    [JsonPropertyName("disposed_sessions")] public int DisposedSessions { get; init; }
    [JsonPropertyName("covered_samples")] public int CoveredSamples { get; init; }
    [JsonPropertyName("resolving_samples")] public int ResolvingSamples { get; init; }
    [JsonPropertyName("minimum_public_frames")] public int MinimumPublicFrames { get; init; }
    [JsonPropertyName("premature_view_reads")] public int PrematureViewReads { get; init; }
    [JsonPropertyName("unauthorized_private_queries")] public int UnauthorizedPrivateQueries { get; init; }
    [JsonPropertyName("scheduling_queries")] public int SchedulingQueries { get; init; }
    [JsonPropertyName("private_state_leaks")] public int PrivateStateLeaks { get; init; }
    [JsonPropertyName("unattributed_commands")] public int UnattributedCommands { get; init; }
    [JsonPropertyName("engine_failures")] public int EngineFailures { get; init; }
    [JsonPropertyName("terminal_event_checks")] public int TerminalEventChecks { get; init; }
    [JsonPropertyName("terminal_result")] public int TerminalResult { get; init; }
    [JsonPropertyName("final_revision")] public ulong FinalRevision { get; init; }
    [JsonPropertyName("success")] public bool Success { get; init; }
}
