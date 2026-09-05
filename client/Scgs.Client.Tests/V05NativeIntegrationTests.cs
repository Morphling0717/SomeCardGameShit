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
    public void ProductLibrarySupportsSchemaTwoViewerSafeLifecycle()
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
        Assert.IsTrue(player0.Players[0].Hand.All(card =>
            card.DesignId is not null && card.InstanceId.HasValue));
        Assert.IsTrue(player1.Players[1].Hand.All(card =>
            card.DesignId is not null && card.InstanceId.HasValue));
        Assert.HasCount(5, player0.Players[0].MainBoard);
        Assert.HasCount(3, player0.Players[0].Tactics);
        Assert.IsTrue(player0.Players.All(player => player.MainBoard.All(card => card is null)));
        Assert.IsTrue(player0.Players.All(player => player.Tactics.All(card => card is null)));
        Assert.IsTrue(player0.Players.All(player => player.Field is null));
        Assert.IsFalse(player0.PendingChoice.Pending);
        Assert.IsFalse(player1.PendingChoice.Pending);

        var query = new V05.ActionQueryRequest(V05.PlayerId.Player0, player0.Revision);
        V05.LegalActionsResult legal = session.ListLegalActions(query);
        Assert.HasCount(17, legal.Actions);
        Assert.HasCount(16, legal.Actions.Where(action =>
            action.Command.Action == V05.ActionKind.Mulligan));
        Assert.AreEqual(V05.ActionKind.Surrender, legal.Actions[^1].Command.Action);
        V05.LegalActionsResult opponentLegal = session.ListLegalActions(
            new V05.ActionQueryRequest(V05.PlayerId.Player1, player1.Revision));
        Assert.HasCount(17, opponentLegal.Actions);
        Assert.HasCount(16, opponentLegal.Actions.Where(action =>
            action.Command.Action == V05.ActionKind.Mulligan));
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
            Assert.AreEqual(V05.MatchPhase.Mulligan, isolatedView.Phase);
            Assert.IsFalse(isolatedView.PendingChoice.Pending);
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
        Assert.IsFalse(reaction.PendingChoice.Pending);

        var invalidProductPlay = new V05.GameCommandRequest(
            V05.PlayerId.Player0,
            V05.ActionKind.PlayUnit,
            player0.Revision)
        {
            Source = player0.Players[0].Hand[0].InstanceId!.Value,
            Slot = 0,
        };
        Assert.AreEqual(
            V05.EngineCode.InvalidPhase,
            session.PreviewPayment(invalidProductPlay).Payment.Status.Code);

        V05.LegalAction player0Mulligan = legal.Actions.Single(action =>
            action.Command.Action == V05.ActionKind.Mulligan &&
            action.Command.MulliganCards.Count == 0);
        Assert.IsTrue(session.PreviewPayment(player0Mulligan.Command).Payment.Status.IsSuccess);
        Assert.IsTrue(session.SubmitCommand(player0Mulligan.Command).IsSuccess);

        V05.MatchView afterPlayer0 = session.GetView(V05.PlayerId.Player0);
        Assert.AreEqual(player0.Revision + 1, afterPlayer0.Revision);
        Assert.IsTrue(afterPlayer0.Players[0].MulliganDone);
        Assert.IsFalse(afterPlayer0.Players[1].MulliganDone);

        V05.MatchView player1Turn = session.GetView(V05.PlayerId.Player1);
        V05.LegalAction player1Mulligan = session.ListLegalActions(
            new V05.ActionQueryRequest(V05.PlayerId.Player1, player1Turn.Revision))
            .Actions.Single(action =>
                action.Command.Action == V05.ActionKind.Mulligan &&
                action.Command.MulliganCards.Count == 0);
        Assert.IsTrue(session.SubmitCommand(player1Mulligan.Command).IsSuccess);

        V05.MatchView actionView = session.GetView(V05.PlayerId.Player0);
        Assert.AreEqual(V05.MatchPhase.Action, actionView.Phase);
        Assert.AreEqual(V05.PlayerId.Player0, actionView.ActivePlayer);
        V05.LegalActionsResult actionLegal = session.ListLegalActions(
            new V05.ActionQueryRequest(V05.PlayerId.Player0, actionView.Revision));
        Assert.IsGreaterThan(1, actionLegal.Actions.Count);
        Assert.IsTrue(actionLegal.Actions.Any(action => action.Command.Action == V05.ActionKind.EndTurn));
        Assert.IsTrue(actionLegal.Actions.All(action =>
            action.Command.ExpectedRevision == actionView.Revision));

        using (V05.ScgsV05GameSession sameDeckSession = V05.ScgsV05GameSession.Create(
                   config with { Player1Deck = "oathguard_luminous_oath_v1" },
                   nativePath))
        {
            Assert.IsTrue(sameDeckSession.Start().IsSuccess);
            V05.MatchView sameDeckView = sameDeckSession.GetView(V05.PlayerId.Player0);
            Assert.AreEqual("oathguard", sameDeckView.Players[0].ProfessionId);
            Assert.AreEqual("oathguard", sameDeckView.Players[1].ProfessionId);
        }

        V05.EventBatch viewer0 = session.ReadNewEvents(V05.PlayerId.Player0);
        V05.EventBatch viewer1 = session.ReadNewEvents(V05.PlayerId.Player1);
        Assert.IsGreaterThan(0, viewer0.Events.Count);
        Assert.IsTrue(viewer1.Events.Any(gameEvent =>
            gameEvent.HiddenCard &&
            gameEvent.Text == "opponent completed mulligan" &&
            !gameEvent.Card.HasValue &&
            gameEvent.DesignId is null));
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
