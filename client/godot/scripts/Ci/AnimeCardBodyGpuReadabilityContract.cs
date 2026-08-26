// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.GodotClient.Ci;

/// <summary>
/// Pure-data policy shared by the Godot producer and managed tests. Geometry
/// alone is insufficient: a required badge must also occupy a bounded final
/// screen ROI containing bright glyph pixels in the captured GPU frame.
/// </summary>
internal static class AnimeCardBodyGpuReadabilityPolicy
{
    private static readonly HashSet<string> RequiredStates = new(StringComparer.Ordinal)
    {
        "hand-one",
        "hand-five",
        "hand-ten",
        "hand-hover",
        "values",
    };

    internal static bool RequiresEvidence(string state) => RequiredStates.Contains(state);

    internal static int MinimumBadgePixelHeight(int viewportHeight)
    {
        if (viewportHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportHeight));
        }

        // 14 px at the formal 1280x720 minimum, scaling with the captured
        // viewport. The macOS runner's structural 1024x684 mode resolves to
        // 13 px; it remains stricter than mere on-screen presence.
        return Math.Max(10, (int)MathF.Floor(viewportHeight * (14.0f / 720.0f)));
    }

    internal static int MinimumGlyphPixelHeight(int minimumBadgePixelHeight)
    {
        if (minimumBadgePixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumBadgePixelHeight));
        }

        // A projected Label3D rectangle does not prove that any glyph reached
        // the framebuffer. Require a substantial portion of that height to be
        // occupied by label-specific pixels in the final image.
        return Math.Max(6, (int)MathF.Floor(minimumBadgePixelHeight * 0.45f));
    }

    internal static int MinimumGlyphPixelWidth(string text) =>
        string.IsNullOrWhiteSpace(text) ? int.MaxValue : Math.Max(2, text.Trim().Length * 3);

    internal static int MinimumNameGlyphPixelHeight(int viewportHeight)
    {
        if (viewportHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportHeight));
        }
        return Math.Max(6, (int)MathF.Floor(viewportHeight * (7.0f / 720.0f)));
    }

    internal static int MinimumNameGlyphPixelWidth(string text) =>
        string.IsNullOrWhiteSpace(text) ? int.MaxValue : Math.Max(3, text.Trim().Length * 3);

    internal static float MaximumNameCenterDeltaPixels(int viewportWidth, int viewportHeight)
    {
        if (viewportWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        }
        if (viewportHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportHeight));
        }
        float uniformViewportScale = MathF.Min(
            viewportWidth / 1280.0f,
            viewportHeight / 720.0f);
        return 2.0f * uniformViewportScale;
    }

    internal static float MinimumNamePlateHorizontalInsetPixels(
        int viewportWidth,
        int viewportHeight)
    {
        if (viewportWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        }
        if (viewportHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportHeight));
        }
        float uniformViewportScale = MathF.Min(
            viewportWidth / 1280.0f,
            viewportHeight / 720.0f);
        return 4.0f * uniformViewportScale;
    }

    internal static bool IsBadgeReadable(
        AnimeCardBodyBadgeGpuEvidence badge,
        int minimumPixelHeight)
    {
        int minimumGlyphHeight = MinimumGlyphPixelHeight(minimumPixelHeight);
        int minimumGlyphWidth = MinimumGlyphPixelWidth(badge.Text);
        int minimumDifferencePixels = Math.Max(8, badge.GlyphPixelHeight);
        int minimumHighContrastPixels = Math.Max(3, badge.GlyphPixelHeight / 3);
        bool glyphInsideRoi =
            badge.GlyphPixelWidth > 0 && badge.GlyphPixelHeight > 0 &&
            badge.GlyphRoiX >= badge.RoiX && badge.GlyphRoiY >= badge.RoiY &&
            badge.GlyphRoiX + badge.GlyphPixelWidth <= badge.RoiX + badge.RoiWidth &&
            badge.GlyphRoiY + badge.GlyphPixelHeight <= badge.RoiY + badge.RoiHeight;

        return badge.Expected &&
               !string.IsNullOrWhiteSpace(badge.Text) &&
               !string.IsNullOrWhiteSpace(badge.ReferenceActorName) &&
               badge.FullyInsideViewport &&
               badge.PixelHeight >= minimumPixelHeight &&
               badge.RoiWidth > 0 && badge.RoiHeight > 0 &&
               badge.SocketFullyInsideViewport &&
               badge.SocketScreenWidth > 0.0f && badge.SocketScreenHeight > 0.0f &&
               badge.RequiredSocketInsetPixels == 1 &&
               badge.GlyphInsideSocket &&
               badge.GlyphSocketInsetLeft >= badge.RequiredSocketInsetPixels &&
               badge.GlyphSocketInsetTop >= badge.RequiredSocketInsetPixels &&
               badge.GlyphSocketInsetRight >= badge.RequiredSocketInsetPixels &&
               badge.GlyphSocketInsetBottom >= badge.RequiredSocketInsetPixels &&
               glyphInsideRoi &&
               badge.GlyphPixelWidth >= minimumGlyphWidth &&
               badge.GlyphPixelHeight >= minimumGlyphHeight &&
               badge.BrightPixelCount >= 2 &&
               badge.ColorBucketCount >= 2 &&
               badge.GlyphDifferencePixelCount >= minimumDifferencePixels &&
               badge.BrightGlyphDifferencePixelCount >= 2 &&
               badge.HighContrastGlyphDifferencePixelCount >= minimumHighContrastPixels &&
               badge.MaximumGlyphContrast >= 0.18f &&
               badge.MaximumGlyphContrast <= 1.0f;
    }

    internal static bool IsNameReadable(
        AnimeCardBodyNameGpuEvidence name,
        int viewportWidth,
        int viewportHeight)
    {
        int minimumGlyphHeight = MinimumNameGlyphPixelHeight(viewportHeight);
        int textLength = string.IsNullOrWhiteSpace(name.Text) ? 0 : name.Text.Trim().Length;
        int minimumGlyphWidth = MinimumNameGlyphPixelWidth(name.Text);
        int minimumDifferencePixels = Math.Max(
            Math.Max(12, name.GlyphPixelHeight * 2),
            textLength * 6);
        int minimumBrightDifferencePixels = Math.Max(2, (textLength + 1) / 2);
        int minimumHighContrastPixels = Math.Max(
            Math.Max(4, name.GlyphPixelHeight / 2),
            textLength);
        bool glyphInsideRoi =
            name.GlyphPixelWidth > 0 && name.GlyphPixelHeight > 0 &&
            name.GlyphRoiX >= name.RoiX && name.GlyphRoiY >= name.RoiY &&
            name.GlyphRoiX + name.GlyphPixelWidth <= name.RoiX + name.RoiWidth &&
            name.GlyphRoiY + name.GlyphPixelHeight <= name.RoiY + name.RoiHeight;
        float maximumCenterDelta = MaximumNameCenterDeltaPixels(
            viewportWidth,
            viewportHeight);
        float minimumHorizontalPlateInset = MinimumNamePlateHorizontalInsetPixels(
            viewportWidth,
            viewportHeight);
        bool fullNameMatches =
            string.Equals(name.Text, name.SourceText, StringComparison.Ordinal) &&
            !name.Text.Contains('…') &&
            !name.SourceText.Contains('…');

        return name.Expected &&
               !string.IsNullOrWhiteSpace(name.Text) &&
               !string.IsNullOrWhiteSpace(name.SourceText) &&
               !string.IsNullOrWhiteSpace(name.ReferenceActorName) &&
               name.FontSize >= 14 &&
               name.FullNameMatchesSource &&
               fullNameMatches &&
               name.ScreenFullyInsideViewport &&
               name.TextSocketFullyInsideViewport &&
               name.NamePlateFullyInsideViewport &&
               name.ScreenWidth > 0.0f && name.ScreenHeight > 0.0f &&
               name.TextSocketScreenWidth > 0.0f && name.TextSocketScreenHeight > 0.0f &&
               name.NamePlateScreenWidth > 0.0f && name.NamePlateScreenHeight > 0.0f &&
               name.RequiredSocketInsetPixels == 1 &&
               MathF.Abs(
                   name.RequiredNamePlateHorizontalInsetPixels -
                   minimumHorizontalPlateInset) <= 0.01f &&
               name.TextSocketInsideNamePlate &&
               name.TextSocketNamePlateInsetLeft >= minimumHorizontalPlateInset &&
               name.TextSocketNamePlateInsetTop >= name.RequiredSocketInsetPixels &&
               name.TextSocketNamePlateInsetRight >= minimumHorizontalPlateInset &&
               name.TextSocketNamePlateInsetBottom >= name.RequiredSocketInsetPixels &&
               name.GlyphInsideTextSocket &&
               name.GlyphSocketInsetLeft >= name.RequiredSocketInsetPixels &&
               name.GlyphSocketInsetTop >= name.RequiredSocketInsetPixels &&
               name.GlyphSocketInsetRight >= name.RequiredSocketInsetPixels &&
               name.GlyphSocketInsetBottom >= name.RequiredSocketInsetPixels &&
               name.GlyphCenteredInTextSocket &&
               MathF.Abs(name.MaximumGlyphSocketCenterDeltaPixels - maximumCenterDelta) <= 0.01f &&
               name.GlyphSocketCenterDeltaX >= 0.0f &&
               name.GlyphSocketCenterDeltaY >= 0.0f &&
               name.GlyphSocketCenterDeltaX <= maximumCenterDelta &&
               name.GlyphSocketCenterDeltaY <= maximumCenterDelta &&
               glyphInsideRoi &&
               name.GlyphPixelWidth >= minimumGlyphWidth &&
               name.GlyphPixelHeight >= minimumGlyphHeight &&
               name.GlyphDifferencePixelCount >= minimumDifferencePixels &&
               name.BrightGlyphDifferencePixelCount >= minimumBrightDifferencePixels &&
               name.HighContrastGlyphDifferencePixelCount >= minimumHighContrastPixels &&
               name.MaximumGlyphContrast >= 0.18f &&
               name.MaximumGlyphContrast <= 1.0f;
    }

    internal static bool IsCaptureReadable(AnimeCardBodyGpuReadabilityEvidence evidence)
    {
        if (!evidence.Required)
        {
            return evidence.ActorCount == 0 && evidence.RequiredBadgeCount == 0 &&
                   evidence.RequiredNameCount == 0 && evidence.CompleteNameCount == 0 &&
                   evidence.Actors.Count == 0 && evidence.AllRequiredBadgesReadable &&
                   evidence.AllRequiredNamesReadable;
        }

        if (evidence.ActorCount <= 0 || evidence.ActorCount != evidence.Actors.Count ||
            evidence.RequiredBadgeCount <= 0 ||
            evidence.RequiredNameCount != evidence.ActorCount ||
            evidence.CompleteNameCount != evidence.ActorCount ||
            !evidence.AllRequiredBadgesReadable || !evidence.AllRequiredNamesReadable)
        {
            return false;
        }

        int badgeCount = 0;
        int completeNameCount = 0;
        foreach (AnimeCardBodyActorGpuEvidence actor in evidence.Actors)
        {
            if (!actor.LocalCompositionReadable ||
                actor.RequiredBadgeCount <= 0 ||
                actor.RequiredBadgeCount != actor.Badges.Count ||
                !actor.AllRequiredBadgesReadable ||
                !actor.NameReadable ||
                !string.Equals(
                    actor.Name.ReferenceActorName,
                    actor.ActorName,
                    StringComparison.Ordinal) ||
                !IsNameReadable(
                    actor.Name,
                    evidence.ViewportWidth,
                    evidence.ViewportHeight))
            {
                return false;
            }

            if (actor.Name.FullNameMatchesSource)
            {
                completeNameCount++;
            }

            foreach (AnimeCardBodyBadgeGpuEvidence badge in actor.Badges)
            {
                if (!badge.Readable ||
                    !string.Equals(
                        badge.ReferenceActorName,
                        actor.ActorName,
                        StringComparison.Ordinal) ||
                    !IsBadgeReadable(badge, evidence.MinimumBadgePixelHeight))
                {
                    return false;
                }
                badgeCount++;
            }
        }

        return badgeCount == evidence.RequiredBadgeCount &&
               completeNameCount == evidence.CompleteNameCount;
    }
}

internal sealed record AnimeCardBodyGpuReadabilityEvidence
{
    public required string State { get; init; }
    public required bool Required { get; init; }
    public required int MinimumBadgePixelHeight { get; init; }
    public required int ViewportWidth { get; init; }
    public required int ViewportHeight { get; init; }
    public required int ActorCount { get; init; }
    public required int RequiredBadgeCount { get; init; }
    public required int RequiredNameCount { get; init; }
    public required int CompleteNameCount { get; init; }
    public required bool AllRequiredBadgesReadable { get; init; }
    public required bool AllRequiredNamesReadable { get; init; }
    public required IReadOnlyList<AnimeCardBodyActorGpuEvidence> Actors { get; init; }
}

internal sealed record AnimeCardBodyActorGpuEvidence
{
    public required string ActorName { get; init; }
    public required string DesignId { get; init; }
    public required string ProductKind { get; init; }
    public required bool LocalCompositionReadable { get; init; }
    public required int RequiredBadgeCount { get; init; }
    public required bool AllRequiredBadgesReadable { get; init; }
    public required bool NameReadable { get; init; }
    public required IReadOnlyList<AnimeCardBodyBadgeGpuEvidence> Badges { get; init; }
    public required AnimeCardBodyNameGpuEvidence Name { get; init; }
}

internal sealed record AnimeCardBodyBadgeGpuEvidence
{
    public required string Role { get; init; }
    public required string Text { get; init; }
    public required string ReferenceActorName { get; init; }
    public required bool Expected { get; init; }
    public required float ScreenX { get; init; }
    public required float ScreenY { get; init; }
    public required float ScreenWidth { get; init; }
    public required float ScreenHeight { get; init; }
    public required int PixelHeight { get; init; }
    public required bool FullyInsideViewport { get; init; }
    public required int RoiX { get; init; }
    public required int RoiY { get; init; }
    public required int RoiWidth { get; init; }
    public required int RoiHeight { get; init; }
    public required int BrightPixelCount { get; init; }
    public required int ColorBucketCount { get; init; }
    public required int GlyphDifferencePixelCount { get; init; }
    public required int BrightGlyphDifferencePixelCount { get; init; }
    public required float SocketScreenX { get; init; }
    public required float SocketScreenY { get; init; }
    public required float SocketScreenWidth { get; init; }
    public required float SocketScreenHeight { get; init; }
    public required bool SocketFullyInsideViewport { get; init; }
    public required int RequiredSocketInsetPixels { get; init; }
    public required float GlyphSocketInsetLeft { get; init; }
    public required float GlyphSocketInsetTop { get; init; }
    public required float GlyphSocketInsetRight { get; init; }
    public required float GlyphSocketInsetBottom { get; init; }
    public required bool GlyphInsideSocket { get; init; }
    public required int GlyphRoiX { get; init; }
    public required int GlyphRoiY { get; init; }
    public required int GlyphPixelWidth { get; init; }
    public required int GlyphPixelHeight { get; init; }
    public required int HighContrastGlyphDifferencePixelCount { get; init; }
    public required float MaximumGlyphContrast { get; init; }
    public required bool Readable { get; init; }
}

internal sealed record AnimeCardBodyNameGpuEvidence
{
    public required string Text { get; init; }
    public required string SourceText { get; init; }
    public required bool FullNameMatchesSource { get; init; }
    public required string ReferenceActorName { get; init; }
    public required bool Expected { get; init; }
    public required int FontSize { get; init; }
    public required float ScreenX { get; init; }
    public required float ScreenY { get; init; }
    public required float ScreenWidth { get; init; }
    public required float ScreenHeight { get; init; }
    public required bool ScreenFullyInsideViewport { get; init; }
    public required float TextSocketScreenX { get; init; }
    public required float TextSocketScreenY { get; init; }
    public required float TextSocketScreenWidth { get; init; }
    public required float TextSocketScreenHeight { get; init; }
    public required bool TextSocketFullyInsideViewport { get; init; }
    public required float NamePlateScreenX { get; init; }
    public required float NamePlateScreenY { get; init; }
    public required float NamePlateScreenWidth { get; init; }
    public required float NamePlateScreenHeight { get; init; }
    public required bool NamePlateFullyInsideViewport { get; init; }
    public required int RequiredSocketInsetPixels { get; init; }
    public required float RequiredNamePlateHorizontalInsetPixels { get; init; }
    public required float TextSocketNamePlateInsetLeft { get; init; }
    public required float TextSocketNamePlateInsetTop { get; init; }
    public required float TextSocketNamePlateInsetRight { get; init; }
    public required float TextSocketNamePlateInsetBottom { get; init; }
    public required bool TextSocketInsideNamePlate { get; init; }
    public required int RoiX { get; init; }
    public required int RoiY { get; init; }
    public required int RoiWidth { get; init; }
    public required int RoiHeight { get; init; }
    public required int GlyphDifferencePixelCount { get; init; }
    public required int BrightGlyphDifferencePixelCount { get; init; }
    public required int GlyphRoiX { get; init; }
    public required int GlyphRoiY { get; init; }
    public required int GlyphPixelWidth { get; init; }
    public required int GlyphPixelHeight { get; init; }
    public required int HighContrastGlyphDifferencePixelCount { get; init; }
    public required float MaximumGlyphContrast { get; init; }
    public required float GlyphSocketInsetLeft { get; init; }
    public required float GlyphSocketInsetTop { get; init; }
    public required float GlyphSocketInsetRight { get; init; }
    public required float GlyphSocketInsetBottom { get; init; }
    public required bool GlyphInsideTextSocket { get; init; }
    public required float MaximumGlyphSocketCenterDeltaPixels { get; init; }
    public required float GlyphSocketCenterDeltaX { get; init; }
    public required float GlyphSocketCenterDeltaY { get; init; }
    public required bool GlyphCenteredInTextSocket { get; init; }
    public required bool Readable { get; init; }
}

internal static class AnimeCardBodySilhouettePolicy
{
    // Side probes compare a rendered card with every product layer disabled at
    // the exact same pixel. Two 8-bit channel steps allow quantization noise,
    // but an unmasked 10%-opacity material rectangle must fail.
    internal const float MaximumCornerBackgroundDelta = 2.0f / 255.0f;

    internal static bool RequiresEvidence(string state) =>
        state is "representatives" or "values";

    internal static bool IsCaptureIsolated(AnimeCardBodySilhouetteEvidence evidence)
    {
        if (!evidence.Required)
        {
            return evidence.ActorCount == 0 && evidence.ProbeCount == 0 &&
                   evidence.InteriorProbeCount == 0 && evidence.Probes.Count == 0 &&
                   evidence.InteriorProbes.Count == 0 &&
                   evidence.AllRectangularBasesHidden &&
                   evidence.AllCornerProbesMatchBackground &&
                   evidence.AllInteriorProbesShowProductFace;
        }

        return evidence.ActorCount > 0 &&
               evidence.ProbeCount == evidence.ActorCount * 4 &&
               evidence.ProbeCount == evidence.Probes.Count &&
               evidence.InteriorProbeCount == evidence.ActorCount &&
               evidence.InteriorProbeCount == evidence.InteriorProbes.Count &&
               evidence.AllRectangularBasesHidden &&
               evidence.AllCornerProbesMatchBackground &&
               evidence.AllInteriorProbesShowProductFace &&
               evidence.Probes.All(probe =>
                   probe.FullyInsideViewport && probe.Passed &&
                   probe.CornerBackgroundColorDelta <= MaximumCornerBackgroundDelta) &&
               evidence.InteriorProbes.All(probe =>
                   probe.FullyInsideViewport && probe.Passed &&
                   probe.ProductLayerDifferencePixelCount >= 4);
    }
}

internal sealed record AnimeCardBodySilhouetteEvidence
{
    public required string State { get; init; }
    public required bool Required { get; init; }
    public required int ActorCount { get; init; }
    public required int ProbeCount { get; init; }
    public required int InteriorProbeCount { get; init; }
    public required bool AllRectangularBasesHidden { get; init; }
    public required bool AllCornerProbesMatchBackground { get; init; }
    public required bool AllInteriorProbesShowProductFace { get; init; }
    public required IReadOnlyList<AnimeCardBodySilhouetteProbeEvidence> Probes { get; init; }
    public required IReadOnlyList<AnimeCardBodyInteriorProbeEvidence> InteriorProbes { get; init; }
}

internal sealed record AnimeCardBodyInteriorProbeEvidence
{
    public required string ActorName { get; init; }
    public required float ScreenX { get; init; }
    public required float ScreenY { get; init; }
    public required bool FullyInsideViewport { get; init; }
    public required int RoiX { get; init; }
    public required int RoiY { get; init; }
    public required int RoiWidth { get; init; }
    public required int RoiHeight { get; init; }
    public required int ProductLayerDifferencePixelCount { get; init; }
    public required bool Passed { get; init; }
}

internal sealed record AnimeCardBodySilhouetteProbeEvidence
{
    public required string ActorName { get; init; }
    public required string Corner { get; init; }
    public required float ScreenX { get; init; }
    public required float ScreenY { get; init; }
    public required float ReferenceX { get; init; }
    public required float ReferenceY { get; init; }
    public required bool FullyInsideViewport { get; init; }
    public required float CornerBackgroundColorDelta { get; init; }
    public required bool Passed { get; init; }
}
