// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.GodotClient.Preview;

internal readonly record struct AnimeCardBodyHandLayout(
    float Spacing,
    float RestingScale);

/// <summary>
/// Pure responsive layout for the real-actor card-body approval hand. Card
/// scale is fixed by readability; only dense-hand spacing contracts when a
/// capture has less horizontal room than the 16:9 product baseline.
/// </summary>
internal static class AnimeCardBodyHandLayoutPolicy
{
    internal const float DenseHandRestingScale = 1.22f;
    internal const float StandardHandRestingScale = 1.16f;

    internal static AnimeCardBodyHandLayout Resolve(
        int cardCount,
        int viewportWidth,
        int viewportHeight)
    {
        if (cardCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cardCount));
        }
        if (viewportWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        }
        if (viewportHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportHeight));
        }

        float aspect = viewportWidth / (float)viewportHeight;
        float horizontalFit = Math.Clamp(aspect / (16.0f / 9.0f), 0.0f, 1.0f);
        float tenCardSpacing = Math.Max(
            1.45f,
            1.90f - ((1.0f - horizontalFit) * 2.40f));
        float spacing = cardCount switch
        {
            >= 9 => tenCardSpacing,
            >= 5 => 1.55f,
            _ => 2.20f,
        };

        return new AnimeCardBodyHandLayout(
            spacing,
            cardCount >= 9 ? DenseHandRestingScale : StandardHandRestingScale);
    }
}
