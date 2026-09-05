namespace Scgs.GodotClient.Match;

public sealed record MatchSetup(string Player0Deck, string Player1Deck)
{
    public static MatchSetup ProductDefaults { get; } = new(
        "oathguard_luminous_oath_v1",
        "pactmage_abyssal_pact_v1");

    // Test-only v04 fixture; retired product deck keys are not aliases.
    public static MatchSetup LegacyDefaults { get; } = new("synthetic_alpha", "synthetic_beta");

    public static MatchSetup Defaults => ProductDefaults;
}
