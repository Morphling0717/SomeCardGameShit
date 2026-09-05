// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.Hotseat.ProductReview;

/// <summary>
/// Layout-only values, deliberately not CardView/GameCommand/session state.
/// A renderer must show the synthetic label and must not use these samples to
/// claim that a real fixed-deck match produced the displayed numbers.
/// </summary>
public sealed record CardFrameSyntheticSample(
    string Key,
    string ReferenceDesignId,
    string FullName,
    int Cost,
    int? Attack,
    int? Health,
    int? MaximumHealth)
{
    public bool Synthetic => true;
    public string EvidenceKind => "synthetic-card-frame-layout";
    public string RequiredVisibleLabel => "合成排版样本 · 非真实对局状态";
}

public static class CardFrameSyntheticSamples
{
    public static IReadOnlyList<CardFrameSyntheticSample> All { get; } = Array.AsReadOnly(new[]
    {
        new CardFrameSyntheticSample("zero-follower", "LO-11", "曜誓大团长·蕾奥妮", 0, 0, 0, 0),
        new CardFrameSyntheticSample("multi-digit-follower", "AP-11", "禁忌毕业生·诺克缇娅", 12, 24, 18, 30),
        new CardFrameSyntheticSample("wounded-follower", "LO-11", "曜誓大团长·蕾奥妮", 10, 8, 3, 8),
        new CardFrameSyntheticSample("long-name", "AP-11", "禁忌毕业生·诺克缇娅（超长完整卡名排版合成样本）", 8, 6, 6, 6),
        new CardFrameSyntheticSample("zero-spell", "NT-04", "界域裁定", 0, null, null, null),
    });
}
