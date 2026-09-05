// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scgs.Hotseat.Product;
using Scgs.Hotseat.ProductReview;
using V05 = Scgs.Client.V05;

namespace Scgs.Client.Tests;

[TestClass]
[DoNotParallelize]
public sealed class PresentationReviewScenarioTests
{
    private const string ImplementationSha = "90e361eaafec8a6a57ddba3892cc37702719d90f";

    [TestMethod]
    public void InvalidReviewRequestsFailBeforeCreatingNativeSessions()
    {
        int calls = 0;
        V05.IScgsV05GameSession Create(V05.GameConfigRequest _)
        {
            ++calls;
            throw new AssertFailedException("Invalid requests must not create a native session.");
        }

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PresentationReviewScenario.Prepare(
            (PresentationReviewKind)99, Create, ImplementationSha));
        Assert.ThrowsExactly<ArgumentException>(() => PresentationReviewScenario.Prepare(
            PresentationReviewKind.Oathguard, Create, "short-sha"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PresentationReviewScenario.Prepare(
            PresentationReviewKind.Oathguard, Create, ImplementationSha, maximumSeedAttempts: 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PresentationReviewScenario.Prepare(
            PresentationReviewKind.Oathguard, Create, ImplementationSha, seedStart: uint.MaxValue));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    [TestCategory("NativeIntegrationV05")]
    [DataRow(PresentationReviewKind.Oathguard)]
    [DataRow(PresentationReviewKind.Pactmage)]
    [DataRow(PresentationReviewKind.Spell)]
    public void RealFixedDeckReviewReplaysEveryCommandAndKeepsHandoffCovered(PresentationReviewKind kind)
    {
        string native = NativePath();
        using PreparedPresentationReview prepared = PresentationReviewScenario.Prepare(
            kind, config => V05.ScgsV05GameSession.Create(config, native), ImplementationSha);
        Assert.IsTrue(PresentationReviewScenario.ValidateTrace(prepared));
        Assert.IsGreaterThan(0, prepared.Trace.Count);
        Assert.IsLessThanOrEqualTo(PresentationReviewScenario.MaximumCommands, prepared.Trace.Count);
        Assert.IsTrue(prepared.Config.ShuffleDecks);
        Assert.AreEqual(V05.FirstPlayerMode.Player0, prepared.Config.FirstPlayerMode);
        CollectionAssert.AreEquivalent(
            new[] { PresentationReviewScenario.OathguardDeck, PresentationReviewScenario.PactmageDeck },
            new[] { prepared.Config.Player0Deck, prepared.Config.Player1Deck });

        using V05.ScgsV05GameSession replay = V05.ScgsV05GameSession.Create(prepared.Config, native);
        Assert.IsTrue(replay.Start().IsSuccess);
        foreach (PresentationReviewTraceEntry entry in prepared.Trace)
        {
            V05.MatchView before = replay.GetView(entry.Command.Player);
            Assert.AreEqual(entry.Command.ExpectedRevision, before.Revision);
            V05.LegalActionsResult legal = replay.ListLegalActions(
                new V05.ActionQueryRequest(entry.Command.Player, before.Revision));
            string actual = V05.ScgsV05Json.SerializeCommand(entry.Command);
            Assert.IsTrue(legal.Actions.Any(action => V05.ScgsV05Json.SerializeCommand(action.Command) == actual),
                $"Trace command {entry.Index} was not enumerated legal.");
            Assert.IsTrue(replay.SubmitCommand(entry.Command).IsSuccess);
            Assert.AreEqual(entry.RevisionAfter, replay.GetView(entry.Command.Player).Revision);
        }

        V05.MatchView readyView = replay.GetView(prepared.Viewer);
        Assert.AreEqual(prepared.ReadyAction.Command.ExpectedRevision, readyView.Revision);
        Assert.IsTrue(readyView.Players[0].Hand.Any(card =>
            card.DesignId == PresentationReviewScenario.DesignId(kind) &&
            card.InstanceId == prepared.ReadyAction.Command.Source));
        Assert.IsFalse(prepared.ReadyAction.Command.UseAdvance);
        Assert.IsTrue(replay.SubmitCommand(prepared.ReadyAction.Command).IsSuccess);
        V05.MatchView afterPlay = replay.GetView(prepared.Viewer);
        if (kind == PresentationReviewKind.Spell)
        {
            Assert.AreEqual(V05.ActionKind.CastSpell, prepared.ReadyAction.Command.Action);
            Assert.AreEqual(V05.TargetKind.Permanent, prepared.ReadyAction.Command.Target!.Kind);
            Assert.AreEqual(V05.PlayerId.Player1, prepared.ReadyAction.Command.Target.Player);
            Assert.IsTrue(readyView.Players[1].MainBoard.Any(card =>
                card?.InstanceId == prepared.ReadyAction.Command.Target.Permanent && card?.Kind == V05.CardKind.Follower));
            V05.EventBatch events = replay.ReadEvents(prepared.Viewer, 0);
            Assert.IsTrue(events.Events.Any(item => item.Type == V05.EventType.PermanentDamaged));
        }
        else
        {
            V05.LegalAction evolution = replay.ListLegalActions(new V05.ActionQueryRequest(prepared.Viewer, afterPlay.Revision))
                .Actions.First(action => action.Command.Action == V05.ActionKind.Evolve &&
                    action.Command.Source == prepared.ReadyAction.Command.Source);
            Assert.IsTrue(replay.SubmitCommand(evolution.Command).IsSuccess);
            Assert.IsTrue(replay.GetView(prepared.Viewer).Players[0].MainBoard.Any(card =>
                card?.InstanceId == evolution.Command.Source && card.Evolved));
        }

        V05.IScgsV05GameSession session = prepared.TakeSession();
        Assert.AreEqual(0UL, session.GetEventCursor(V05.PlayerId.Player0));
        Assert.AreEqual(0UL, session.GetEventCursor(V05.PlayerId.Player1));
        using var controller = new ProductHotseatMatchController(session);
        Assert.AreEqual(ProductHotseatUiMode.Covered, controller.State.Mode);
        Assert.IsNull(controller.State.Snapshot);
        Assert.IsNull(controller.State.Viewer);
        Assert.IsEmpty(controller.State.LegalActions);
        Assert.ThrowsExactly<InvalidOperationException>(() => prepared.TakeSession());
        controller.Reveal();
        Assert.AreEqual(ProductHotseatUiMode.Action, controller.State.Mode);
        Assert.AreEqual(prepared.ReadyAction.Command.ExpectedRevision, controller.State.Snapshot!.Revision);
        Assert.IsTrue(controller.State.LegalActions.Any(action =>
            V05.ScgsV05Json.SerializeCommand(action.Command) == V05.ScgsV05Json.SerializeCommand(prepared.ReadyAction.Command)));
        Console.WriteLine($"review={kind} seed={prepared.Config.RandomSeed} commands={prepared.Trace.Count} " +
            $"revision={prepared.ReadyAction.Command.ExpectedRevision} trace_sha256={prepared.TraceSha256}");
    }

    private static string NativePath()
    {
        string? path = Environment.GetEnvironmentVariable("SCGS_NATIVE_V05_LIBRARY");
        if (string.IsNullOrWhiteSpace(path))
        {
            Assert.Inconclusive("Set SCGS_NATIVE_V05_LIBRARY to verify real presentation review scenes.");
        }

        return path!;
    }
}
