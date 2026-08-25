// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

namespace Scgs.GodotClient.Battlefield;

/// <summary>
/// Selects presentation-only battlefield art direction. Gate4BR2 remains the
/// product default until the R3 vertical slice has been approved.
/// </summary>
public enum BattlefieldVisualProfile
{
    Gate4BR2 = 0,
    R3Candidate = 1,
}

internal sealed record ArenaVisualProfile(
    BattlefieldVisualProfile Id,
    string? AuthoredArenaScenePath,
    HandVisualProfile Hand,
    Color NeutralMetal,
    Color MidrangeMetal,
    Color AdvanceMetal,
    Color HiddenMetal,
    Color UnitInlay,
    Color TacticInlay,
    Color PileInlay,
    Color FunctionalAccent,
    Color DestinationAccent,
    Color SelectedAccent,
    bool UsesOpenArena,
    bool UsesShadedCardArtwork)
{
    internal const string R3CandidateScenePath =
        "res://scenes/battlefield/R3ArenaCandidate.tscn";

    internal static ArenaVisualProfile Gate4BR2 { get; } = new(
        BattlefieldVisualProfile.Gate4BR2,
        null,
        HandVisualProfile.Gate4BR2,
        new Color("2b4052"),
        new Color("285d7c"),
        new Color("675084"),
        new Color("101d31"),
        new Color("2f89a7"),
        new Color("8c72b5"),
        new Color("6f91a6"),
        new Color("55ead0"),
        new Color("ffc35d"),
        new Color("ff765f"),
        UsesOpenArena: false,
        UsesShadedCardArtwork: false);

    // R3 keeps faction identity local to card rims and leader cores. The play
    // surface itself is neutral steel, not two luminous color slabs.
    internal static ArenaVisualProfile R3Candidate { get; } = new(
        BattlefieldVisualProfile.R3Candidate,
        R3CandidateScenePath,
        HandVisualProfile.R3Candidate,
        new Color("30383d"),
        new Color("34434a"),
        new Color("4a4249"),
        new Color("1a2024"),
        new Color("778086"),
        new Color("756d78"),
        new Color("8d8578"),
        new Color("d3b36e"),
        new Color("e5d3a5"),
        new Color("c87452"),
        UsesOpenArena: true,
        UsesShadedCardArtwork: true);

    internal static ArenaVisualProfile Resolve(BattlefieldVisualProfile profile) => profile switch
    {
        BattlefieldVisualProfile.Gate4BR2 => Gate4BR2,
        BattlefieldVisualProfile.R3Candidate => R3Candidate,
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown battlefield visual profile."),
    };
}

internal sealed record HandVisualProfile(
    float NearHeightRatio,
    float NearMinimumHeight,
    float NearMaximumHeight,
    float FarHeightRatio,
    float FarMinimumHeight,
    float FarMaximumHeight,
    float NearNominalSpacing,
    float FarNominalSpacing,
    float NearMaximumRoll,
    float FarMaximumRoll,
    float NearMaximumSpanRatio,
    float FarMaximumSpanRatio,
    float HoverScale,
    float SelectedScale,
    float HoverLiftPixels,
    float SelectedLiftPixels,
    float FocusNeighborSpread)
{
    internal static HandVisualProfile Gate4BR2 { get; } = new(
        0.205f, 148.0f, 238.0f,
        0.085f, 60.0f, 104.0f,
        104.0f, 48.0f,
        8.0f, 4.5f,
        0.82f, 0.52f,
        1.12f, 1.07f,
        40.0f, 22.0f, 26.0f);

    internal static HandVisualProfile R3Candidate { get; } = new(
        0.218f, 158.0f, 248.0f,
        0.080f, 58.0f, 98.0f,
        128.0f, 45.0f,
        5.6f, 3.5f,
        0.90f, 0.48f,
        1.10f, 1.065f,
        46.0f, 25.0f, 34.0f);
}
