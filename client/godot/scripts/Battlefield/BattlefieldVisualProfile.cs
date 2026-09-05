// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

namespace Scgs.GodotClient.Battlefield;

/// <summary>
/// AnimeV1 is the only runtime presentation. Old numeric identifiers exist
/// solely so archived diagnostic code can reject them explicitly.
/// </summary>
public enum BattlefieldVisualProfile
{
    Gate4BR2 = 0,
    R3Candidate = 1,
    AnimeV1 = 2,
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
    internal static ArenaVisualProfile AnimeV1 { get; } = new(
        BattlefieldVisualProfile.AnimeV1,
        "res://scenes/battlefield/AnimeV1Arena.tscn",
        HandVisualProfile.AnimeV1,
        new Color("83789b"), new Color("d6c597"), new Color("655078"),
        new Color("211838"), new Color("c3b594"), new Color("ac99bd"),
        new Color("b5a790"), new Color("e8cd87"), new Color("b6cdee"),
        new Color("f3dfaf"), UsesOpenArena: true, UsesShadedCardArtwork: false);

    internal static ArenaVisualProfile Resolve(BattlefieldVisualProfile profile) => profile switch
    {
        BattlefieldVisualProfile.AnimeV1 => AnimeV1,
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
    internal static HandVisualProfile AnimeV1 { get; } = new(
        0.218f, 158.0f, 248.0f,
        0.080f, 58.0f, 98.0f,
        128.0f, 45.0f,
        5.6f, 3.5f,
        0.90f, 0.48f,
        1.10f, 1.065f,
        46.0f, 25.0f, 34.0f);
}
