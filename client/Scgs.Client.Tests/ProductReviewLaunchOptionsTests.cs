// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scgs.Hotseat.ProductReview;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class ProductReviewLaunchOptionsTests
{
    [TestMethod]
    public void OrdinaryProductAndSimilarArgumentsDoNotEnableEitherReview()
    {
        Assert.IsNull(ProductReviewLaunchOptions.Parse([]));
        Assert.IsNull(ProductReviewLaunchOptions.Parse(["--show-debug-ui"]));
        Assert.IsNull(ProductReviewLaunchOptions.Parse(["--card-frame-review=1"]));
        Assert.IsNull(ProductReviewLaunchOptions.Parse(["--battle-presentation-review-extra"]));
        Assert.IsNull(ProductReviewLaunchOptions.Parse(["--review-source-sha=not-a-review"]));
    }

    [TestMethod]
    public void OriginalBattleReviewKeepsItsVisualAndPlaybackLane()
    {
        ProductReviewLaunchOptions options = ProductReviewLaunchOptions.Parse(["--battle-presentation-review"])!;
        Assert.AreEqual(ProductReviewEntryKind.BattlePresentation, options.Entry);
        Assert.IsTrue(options.EnableBattlePresentation);
        Assert.IsTrue(options.EnablePresentationPlayback);
        Assert.IsFalse(options.EnableCardFrame);
        Assert.IsNull(options.SourceSha);
        Assert.AreEqual("real-product-battle-presentation-review", options.EvidenceSuite);
    }

    [TestMethod]
    public void CardFrameReviewUsesIndependentSkinButRetainsExistingObservationPlayback()
    {
        ProductReviewLaunchOptions options = ProductReviewLaunchOptions.Parse(["--card-frame-review"])!;
        Assert.AreEqual(ProductReviewEntryKind.CardFrame, options.Entry);
        Assert.IsTrue(options.EnableCardFrame);
        Assert.IsTrue(options.EnablePresentationPlayback);
        Assert.IsFalse(options.EnableBattlePresentation);
        Assert.AreEqual("real-product-card-frame-review", options.EvidenceSuite);
    }

    [TestMethod]
    public void ReviewModesCannotBeCombinedOrSubstituteForProductSmoke()
    {
        Assert.ThrowsExactly<ArgumentException>(() => ProductReviewLaunchOptions.Parse(
            ["--card-frame-review", "--battle-presentation-review"]));
        Assert.ThrowsExactly<ArgumentException>(() => ProductReviewLaunchOptions.Parse(
            ["--card-frame-review", "--ci-product-smoke"]));
        Assert.ThrowsExactly<ArgumentException>(() => ProductReviewLaunchOptions.Parse(
            ["--battle-presentation-review", "--ci-product-smoke"]));
    }

    [TestMethod]
    public void ExplicitReviewShaIsValidatedAndNormalizedWithoutInferringOne()
    {
        const string sha = "ABCDEF0123456789ABCDEF0123456789ABCDEF01";
        foreach (string flag in new[] { "--card-frame-review", "--battle-presentation-review" })
        {
            Assert.AreEqual(sha.ToLowerInvariant(), ProductReviewLaunchOptions.Parse(
                [flag, "--review-source-sha=" + sha])!.SourceSha);
            Assert.ThrowsExactly<ArgumentException>(() => ProductReviewLaunchOptions.Parse(
                [flag, "--review-source-sha=short"]));
            Assert.ThrowsExactly<ArgumentException>(() => ProductReviewLaunchOptions.Parse(
                [flag, "--review-source-sha=" + new string('z', 40)]));
            Assert.ThrowsExactly<ArgumentException>(() => ProductReviewLaunchOptions.Parse(
                [flag, "--review-source-sha=" + sha, "--review-source-sha=" + sha]));
        }
    }

    [TestMethod]
    public void OnlyThreeExactExistingDefinitionsAreFrameRepresentatives()
    {
        foreach (string id in new[] { "LO-11", "AP-11", "NT-04" })
            Assert.IsTrue(ProductReviewLaunchOptions.IsRepresentativeCard(id));
        foreach (string? id in new[] { null, "", "LO-01", "AP-10", "NT-03", "lo-11", "LO-11-evolved" })
            Assert.IsFalse(ProductReviewLaunchOptions.IsRepresentativeCard(id));
    }

    [TestMethod]
    public void SyntheticSamplesAlwaysIdentifyThemselvesAndNeverImpersonateSnapshotDtos()
    {
        Assert.HasCount(5, CardFrameSyntheticSamples.All);
        Assert.AreEqual(CardFrameSyntheticSamples.All.Count,
            CardFrameSyntheticSamples.All.Select(sample => sample.Key).Distinct(StringComparer.Ordinal).Count());
        foreach (CardFrameSyntheticSample sample in CardFrameSyntheticSamples.All)
        {
            Assert.IsTrue(sample.Synthetic);
            Assert.AreEqual("synthetic-card-frame-layout", sample.EvidenceKind);
            Assert.Contains("非真实对局", sample.RequiredVisibleLabel);
            Assert.IsTrue(ProductReviewLaunchOptions.IsRepresentativeCard(sample.ReferenceDesignId));
            Assert.IsFalse(string.IsNullOrWhiteSpace(sample.FullName));
        }
        Assert.IsFalse(typeof(Scgs.Client.V05.CardView).IsAssignableFrom(typeof(CardFrameSyntheticSample)));
    }

    [TestMethod]
    public void SyntheticValuesCoverZeroMultipleDigitsWoundsAndUnabridgedLongNames()
    {
        CardFrameSyntheticSample zero = CardFrameSyntheticSamples.All.Single(sample => sample.Key == "zero-follower");
        Assert.AreEqual(0, zero.Cost);
        Assert.AreEqual(0, zero.Attack);
        Assert.AreEqual(0, zero.Health);
        CardFrameSyntheticSample multi = CardFrameSyntheticSamples.All.Single(sample => sample.Key == "multi-digit-follower");
        Assert.IsGreaterThanOrEqualTo(10, multi.Cost);
        Assert.IsGreaterThanOrEqualTo(10, multi.Attack!.Value);
        Assert.IsGreaterThanOrEqualTo(10, multi.Health!.Value);
        CardFrameSyntheticSample wounded = CardFrameSyntheticSamples.All.Single(sample => sample.Key == "wounded-follower");
        Assert.IsLessThan(wounded.MaximumHealth!.Value, wounded.Health!.Value);
        CardFrameSyntheticSample longName = CardFrameSyntheticSamples.All.Single(sample => sample.Key == "long-name");
        Assert.IsGreaterThan(20, longName.FullName.Length);
        Assert.DoesNotContain("…", longName.FullName);
        CardFrameSyntheticSample spell = CardFrameSyntheticSamples.All.Single(sample => sample.Key == "zero-spell");
        Assert.IsNull(spell.Attack);
        Assert.IsNull(spell.Health);
        Assert.IsNull(spell.MaximumHealth);
    }
}
