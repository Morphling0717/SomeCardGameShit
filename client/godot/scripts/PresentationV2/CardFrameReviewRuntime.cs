// SPDX-License-Identifier: GPL-3.0-or-later
using Scgs.Hotseat.ProductReview;

namespace Scgs.GodotClient.PresentationV2;

/// <summary>Independent card-frame selector; the review entry configures inherited playback separately.</summary>
internal static class CardFrameReviewRuntime
{
    internal static bool Enabled { get; private set; }
    internal static void Configure(bool enabled) => Enabled = enabled;
    internal static bool UsesRefinedFace(string? designId) =>
        Enabled && ProductReviewLaunchOptions.IsRepresentativeCard(designId);
}
