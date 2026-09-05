// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.Hotseat.ProductReview;

public enum ProductReviewEntryKind
{
    BattlePresentation,
    CardFrame,
}

/// <summary>
/// Explicit developer review lanes. Parsing does not construct a session or
/// make an ordinary product launch opt into either candidate presentation.
/// </summary>
public sealed record ProductReviewLaunchOptions
{
    private ProductReviewLaunchOptions(ProductReviewEntryKind entry, string? sourceSha)
    {
        Entry = entry;
        SourceSha = sourceSha;
    }

    public ProductReviewEntryKind Entry { get; }
    public string? SourceSha { get; }
    public bool EnableBattlePresentation => Entry == ProductReviewEntryKind.BattlePresentation;
    public bool EnableCardFrame => Entry == ProductReviewEntryKind.CardFrame;
    // Both lanes retain the existing public observation playback so a spell
    // which enters and leaves a slot in one command can actually be inspected.
    // This does not mean the card-frame lane introduces new effects.
    public bool EnablePresentationPlayback => true;
    public string EvidenceSuite => EnableCardFrame
        ? "real-product-card-frame-review"
        : "real-product-battle-presentation-review";

    public static ProductReviewLaunchOptions? Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        bool battle = arguments.Contains("--battle-presentation-review");
        bool frame = arguments.Contains("--card-frame-review");
        if (!battle && !frame) return null;
        if (battle && frame)
            throw new ArgumentException("Choose one review entry; card frame and battle presentation are independent.");
        if (arguments.Contains("--ci-product-smoke"))
            throw new ArgumentException("An independent review entry cannot replace product CI smoke.");

        string[] values = arguments.Where(value =>
            value.StartsWith("--review-source-sha=", StringComparison.Ordinal)).ToArray();
        if (values.Length > 1)
            throw new ArgumentException("Only one --review-source-sha may be supplied.");
        string? sourceSha = values.Length == 0 ? null : values[0]["--review-source-sha=".Length..];
        if (sourceSha is not null &&
            (sourceSha.Length != 40 || sourceSha.Any(character => !Uri.IsHexDigit(character))))
            throw new ArgumentException("--review-source-sha requires a full 40-character commit hash.");

        return new ProductReviewLaunchOptions(frame ? ProductReviewEntryKind.CardFrame :
            ProductReviewEntryKind.BattlePresentation, sourceSha?.ToLowerInvariant());
    }

    public static bool IsRepresentativeCard(string? designId) =>
        designId is "LO-11" or "AP-11" or "NT-04";
}
