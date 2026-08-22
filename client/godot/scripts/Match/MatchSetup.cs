namespace Scgs.GodotClient.Match;

public sealed record MatchSetup(string Player0Deck, string Player1Deck)
{
    public static MatchSetup Defaults { get; } = new("midrange", "advance");
}
