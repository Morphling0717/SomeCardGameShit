// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.VisualStudio.TestTools.UnitTesting;
using V05 = Scgs.Client.V05;

namespace Scgs.Client.Tests;

[TestClass]
[DoNotParallelize]
public sealed class V05NativeIntegrationTests
{
    [TestMethod]
    [TestCategory("NativeIntegrationV05")]
    public void FoundationLibrarySupportsSchemaTwoViewerSafeLifecycle()
    {
        string nativePath = GetNativeLibraryPath();
        var config = new V05.GameConfigRequest(
            "oathguard_luminous_oath_v1",
            "pactmage_abyssal_pact_v1")
        {
            RandomSeed = 7,
            FirstPlayerMode = V05.FirstPlayerMode.Player0,
            ShuffleDecks = false,
        };
        using V05.ScgsV05GameSession session = V05.ScgsV05GameSession.Create(config, nativePath);
        Assert.IsTrue(session.Start().IsSuccess);
        Assert.AreEqual(V05.EngineCode.MatchAlreadyStarted, session.Start().Code);

        V05.MatchView player0 = session.GetView(V05.PlayerId.Player0);
        V05.MatchView player1 = session.GetView(V05.PlayerId.Player1);
        Assert.AreEqual(V05.MatchPhase.Mulligan, player0.Phase);
        Assert.HasCount(4, player0.Players[0].Hand);
        Assert.HasCount(0, player0.Players[1].Hand);
        Assert.HasCount(0, player1.Players[0].Hand);
        Assert.HasCount(4, player1.Players[1].Hand);
        Assert.AreEqual("LO-01", player0.Players[0].Hand[0].DesignId);
        Assert.AreEqual("AP-01", player1.Players[1].Hand[0].DesignId);
        Assert.HasCount(5, player0.Players[0].MainBoard);
        Assert.HasCount(3, player0.Players[0].Tactics);
        Assert.AreEqual("LO-03", player0.Players[0].MainBoard[0]!.DesignId);
        Assert.AreEqual(V05.CardKind.Amulet, player0.Players[0].MainBoard[0]!.Kind);
        Assert.AreEqual("LO-04", player0.Players[0].MainBoard[1]!.DesignId);
        Assert.AreEqual("LO-10", player0.Players[0].Field!.DesignId);
        Assert.AreEqual(V05.CardKind.Field, player0.Players[0].Field!.Kind);
        Assert.AreEqual("AP-05", player1.Players[1].Field!.DesignId);
        Assert.AreEqual("LO-07", player0.Players[0].Tactics[0]!.DesignId);
        Assert.IsTrue(player1.Players[0].Tactics[0]!.FaceDown);
        Assert.IsNull(player1.Players[0].Tactics[0]!.InstanceId);
        Assert.AreEqual(0UL, player1.Players[0].Tactics[0]!.Sequence);
        Assert.AreEqual(0, player1.Players[0].Tactics[0]!.Cost);
        Assert.IsTrue(player0.PendingChoice.Pending);
        Assert.AreEqual(V05.PlayerId.Player0, player0.PendingChoice.Chooser);
        Assert.AreEqual(V05.PendingChoiceKind.Cards, player0.PendingChoice.Kind);
        Assert.HasCount(2, player0.PendingChoice.Options);
        Assert.IsTrue(player1.PendingChoice.Pending);
        Assert.AreEqual(V05.PlayerId.Player0, player1.PendingChoice.Chooser);
        Assert.IsNull(player1.PendingChoice.ChoiceId);
        Assert.IsNull(player1.PendingChoice.Kind);
        Assert.IsEmpty(player1.PendingChoice.Options);

        var query = new V05.ActionQueryRequest(V05.PlayerId.Player0, player0.Revision);
        V05.LegalActionsResult legal = session.ListLegalActions(query);
        Assert.HasCount(3, legal.Actions);
        Assert.AreEqual(V05.ActionKind.ResolveChoice, legal.Actions[0].Command.Action);
        Assert.HasCount(2, legal.Actions.Where(action =>
            action.Command.Action == V05.ActionKind.ResolveChoice));
        Assert.AreEqual(V05.ActionKind.Surrender, legal.Actions[^1].Command.Action);
        V05.LegalAction opponentSurrender = session.ListLegalActions(
            new V05.ActionQueryRequest(V05.PlayerId.Player1, player1.Revision)).Actions.Single();
        Assert.AreEqual(V05.ActionKind.Surrender, opponentSurrender.Command.Action);
        Assert.IsTrue(session.PreviewPayment(opponentSurrender.Command).Payment.Status.IsSuccess);
        V05.ScgsV05NativeException staleQuery = Assert.ThrowsExactly<V05.ScgsV05NativeException>(() =>
            session.ListLegalActions(
                new V05.ActionQueryRequest(V05.PlayerId.Player0, player0.Revision + 1)));
        Assert.AreEqual(V05.NativeCode.InvalidArgument, staleQuery.Code);
        Assert.AreEqual(player0.Revision, session.GetView(V05.PlayerId.Player0).Revision);

        using (V05.ScgsV05GameSession isolatedSession =
               V05.ScgsV05GameSession.Create(config, nativePath))
        {
            V05.ScgsV05NativeException notStartedQuery =
                Assert.ThrowsExactly<V05.ScgsV05NativeException>(() =>
                    isolatedSession.ListLegalActions(
                        new V05.ActionQueryRequest(V05.PlayerId.Player0, 0)));
            Assert.AreEqual(V05.NativeCode.InvalidArgument, notStartedQuery.Code);
            Assert.IsTrue(isolatedSession.Start().IsSuccess);
            V05.MatchView isolatedView = isolatedSession.GetView(V05.PlayerId.Player0);
            Assert.AreNotEqual(player0.PendingChoice.ChoiceId, isolatedView.PendingChoice.ChoiceId);
            CollectionAssert.AreNotEquivalent(
                player0.PendingChoice.Options.Select(option => option.OptionId).ToArray(),
                isolatedView.PendingChoice.Options.Select(option => option.OptionId).ToArray());
            V05.EventBatch isolatedEventsBefore =
                isolatedSession.ReadEvents(V05.PlayerId.Player0, 0);
            Assert.AreEqual(
                V05.EngineCode.InvalidChoice,
                isolatedSession.SubmitCommand(legal.Actions[0].Command).Code);
            Assert.AreEqual(
                isolatedView.Revision,
                isolatedSession.GetView(V05.PlayerId.Player0).Revision);
            V05.EventBatch isolatedEventsAfter =
                isolatedSession.ReadEvents(V05.PlayerId.Player0, 0);
            Assert.AreEqual(isolatedEventsBefore.LastSequence, isolatedEventsAfter.LastSequence);
            Assert.AreEqual(isolatedEventsBefore.Events.Count, isolatedEventsAfter.Events.Count);
        }

        V05.ValidTargetsResult targets = session.ListValidTargets(query with
        {
            Action = V05.ActionKind.Attack,
            Source = player0.Players[0].Hand[0].InstanceId,
        });
        V05.ValidSlotsResult slots = session.ListValidSlots(query with
        {
            Action = V05.ActionKind.PlayUnit,
            Source = player0.Players[0].Hand[0].InstanceId,
        });
        V05.ValidDonorsResult donors = session.ListValidDonors(query with
        {
            Action = V05.ActionKind.Deploy,
            Source = player0.Players[0].Hand[0].InstanceId,
        });
        V05.ReactionAndChoiceResult reaction = session.GetReactionContext(V05.PlayerId.Player0);
        Assert.AreEqual(player0.Revision, targets.Revision);
        Assert.AreEqual(player0.Revision, slots.Revision);
        Assert.AreEqual(player0.Revision, donors.Revision);
        Assert.AreEqual(player0.Revision, reaction.Revision);
        Assert.IsEmpty(targets.Targets);
        Assert.IsEmpty(slots.Slots);
        Assert.IsEmpty(donors.Donors);
        Assert.IsFalse(reaction.Reaction.Pending);
        Assert.IsTrue(reaction.PendingChoice.Pending);

        var unsupportedProductPlay = new V05.GameCommandRequest(
            V05.PlayerId.Player0,
            V05.ActionKind.PlayUnit,
            player0.Revision)
        {
            Source = player0.Players[0].Hand[0].InstanceId!.Value,
            Slot = 0,
        };
        Assert.AreEqual(
            V05.EngineCode.ChoicePending,
            session.PreviewPayment(unsupportedProductPlay).Payment.Status.Code);
        Assert.IsTrue(session.PreviewPayment(legal.Actions[0].Command).Payment.Status.IsSuccess);
        Assert.IsTrue(session.SubmitCommand(legal.Actions[0].Command).IsSuccess);

        V05.MatchView afterChoice = session.GetView(V05.PlayerId.Player0);
        Assert.AreEqual(player0.Revision + 1, afterChoice.Revision);
        Assert.IsFalse(afterChoice.PendingChoice.Pending);
        V05.ValidSlotsResult afterChoiceSlots = session.ListValidSlots(
            new V05.ActionQueryRequest(V05.PlayerId.Player0, afterChoice.Revision)
            {
                Action = V05.ActionKind.PlayUnit,
                Source = afterChoice.Players[0].Hand[0].InstanceId,
            });
        Assert.IsEmpty(afterChoiceSlots.Slots);
        Assert.AreEqual(
            V05.EngineCode.InvalidCard,
            session.PreviewPayment(unsupportedProductPlay with
            {
                ExpectedRevision = afterChoice.Revision,
            }).Payment.Status.Code);
        V05.LegalActionsResult afterChoiceLegal = session.ListLegalActions(
            new V05.ActionQueryRequest(V05.PlayerId.Player0, afterChoice.Revision));
        V05.LegalAction mulligan = afterChoiceLegal.Actions.Single(
            action => action.Command.Action == V05.ActionKind.Mulligan);
        Assert.IsTrue(session.PreviewPayment(mulligan.Command).Payment.Status.IsSuccess);
        Assert.IsTrue(session.SubmitCommand(mulligan.Command).IsSuccess);

        V05.EventBatch viewer0 = session.ReadNewEvents(V05.PlayerId.Player0);
        V05.EventBatch viewer1 = session.ReadNewEvents(V05.PlayerId.Player1);
        Assert.IsGreaterThan(0, viewer0.Events.Count);
        Assert.IsTrue(viewer1.Events.Any(gameEvent =>
            gameEvent.HiddenCard &&
            gameEvent.Text == "opponent completed mulligan" &&
            !gameEvent.Card.HasValue &&
            gameEvent.DesignId is null));
        Assert.IsTrue(viewer1.Events.Any(gameEvent =>
            gameEvent.HiddenCard &&
            gameEvent.Text == "opponent completed a private choice" &&
            !gameEvent.Card.HasValue &&
            gameEvent.DesignId is null &&
            !gameEvent.Text.Contains("foundation-option", StringComparison.Ordinal)));
        Assert.AreEqual(viewer0.LastSequence, session.GetEventCursor(V05.PlayerId.Player0));
        Assert.AreEqual(viewer1.LastSequence, session.GetEventCursor(V05.PlayerId.Player1));
    }

    private static string GetNativeLibraryPath()
    {
        string? explicitPath = Environment.GetEnvironmentVariable("SCGS_NATIVE_V05_LIBRARY");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        string? v04Path = Environment.GetEnvironmentVariable("SCGS_NATIVE_LIBRARY");
        if (!string.IsNullOrWhiteSpace(v04Path))
        {
            string candidate = Path.Combine(
                Path.GetDirectoryName(v04Path)!,
                OperatingSystem.IsWindows() ? "scgs_v05.dll" : "libscgs_v05.dylib");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        Assert.Inconclusive("Set SCGS_NATIVE_V05_LIBRARY to run the v05 integration test.");
        throw new InvalidOperationException("MSTest did not terminate an inconclusive test.");
    }
}
