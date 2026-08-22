// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class HotseatMatchControllerTests
{
    [TestMethod]
    public void MulliganIsCanonicalAndReviewsReplacementHandBeforePlayer1IsTouched()
    {
        int stage = 0;
        LegalAction pass0 = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.Mulligan, 0);
        LegalAction replace0 = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.Mulligan,
            0,
            mulliganCards: [11, 13]);
        LegalAction pass1 = HotseatTestModel.Action(
            PlayerId.Player1, ActionKind.Mulligan, 1);
        var session = new FakeGameSession
        {
            ViewHandler = viewer => (stage, viewer) switch
            {
                (0, PlayerId.Player0) => HotseatTestModel.View(
                    viewer, 0, MatchPhase.Mulligan, PlayerId.Player0, false, false,
                    [11, 12, 13]),
                (1, PlayerId.Player0) => HotseatTestModel.View(
                    viewer, 1, MatchPhase.Mulligan, PlayerId.Player0, true, false,
                    [21, 22, 23]),
                (1, PlayerId.Player1) => HotseatTestModel.View(
                    viewer, 1, MatchPhase.Mulligan, PlayerId.Player0, true, false,
                    [31, 32, 33]),
                _ => throw new AssertFailedException($"Unexpected view {stage}/{viewer}."),
            },
            ActionsHandler = query => HotseatTestModel.Filter(
                query,
                query.ExpectedRevision,
                query.Player == PlayerId.Player0 ? [pass0, replace0] : [pass1]),
            SubmitHandler = command =>
            {
                Assert.AreEqual(PlayerId.Player0, command.Player);
                stage = 1;
                return HotseatTestModel.Status(EngineCode.Ok);
            },
            EventsHandler = (viewer, after) => (stage, viewer) switch
            {
                (0, PlayerId.Player0) => HotseatTestModel.Events(0, after, 1, viewer),
                (1, PlayerId.Player0) => HotseatTestModel.Events(1, after, 2, viewer),
                (1, PlayerId.Player1) => HotseatTestModel.Events(1, after, 1, viewer),
                _ => throw new AssertFailedException($"Unexpected events {stage}/{viewer}."),
            },
        };
        using var controller = new HotseatMatchController(session);

        Assert.AreEqual(HotseatUiMode.Covered, controller.State.Mode);
        Assert.AreEqual(PlayerId.Player0, controller.State.AwaitingPlayer);
        Assert.IsEmpty(session.Calls);

        controller.Reveal();
        Assert.AreEqual(HotseatUiMode.MulliganSelecting, controller.State.Mode);
        Assert.AreEqual(0UL, controller.State.EventCursors.Player0);
        Assert.AreEqual(1UL, controller.State.PendingEventLastSequence);
        Assert.IsTrue(controller.AcknowledgeEvents());
        Assert.AreEqual(1UL, controller.State.EventCursors.Player0);

        controller.SelectMulliganCards([13, 11]);
        CollectionAssert.AreEqual(
            new ulong[] { 11, 13 },
            controller.State.MulliganCards.ToArray());
        ActionQueryRequest partial = session.Queries.Last();
        CollectionAssert.AreEqual(
            new ulong[] { 11, 13 },
            partial.MulliganCards!.ToArray());

        Assert.IsTrue(controller.ConfirmSelection());
        Assert.IsTrue(controller.State.IsCovered);
        Assert.IsNull(controller.State.Snapshot);
        Assert.IsTrue(controller.State.CommandPrepared);
        Assert.IsFalse(controller.ConfirmSelection());

        EngineStatus status = controller.SubmitPreparedCommand();
        Assert.IsTrue(status.IsSuccess);
        Assert.AreEqual(HotseatUiMode.MulliganReview, controller.State.Mode);
        Assert.AreEqual(PlayerId.Player0, controller.State.Viewer);
        CollectionAssert.AreEqual(
            new ulong[] { 21, 22, 23 },
            controller.State.Snapshot!.Players[0].Hand
                .Select(card => card.InstanceId!.Value).ToArray());
        CollectionAssert.AreEqual(
            new ulong[] { 11, 13 },
            session.SubmittedCommands.Single().MulliganCards.ToArray());
        Assert.IsFalse(session.Calls.Any(call => call.Contains("Player1", StringComparison.Ordinal)));

        Assert.IsTrue(controller.AcknowledgeEvents());
        Assert.AreEqual(2UL, controller.State.EventCursors.Player0);
        controller.CompleteMulliganReview();
        Assert.AreEqual(HotseatUiMode.Covered, controller.State.Mode);
        Assert.AreEqual(PlayerId.Player1, controller.State.AwaitingPlayer);
        int beforeReveal = session.Calls.Count;
        Assert.IsFalse(session.Calls.Skip(beforeReveal).Any());

        controller.Reveal();
        Assert.AreEqual(HotseatUiMode.MulliganSelecting, controller.State.Mode);
        Assert.AreEqual(PlayerId.Player1, controller.State.Viewer);
        Assert.AreEqual(2UL, controller.State.EventCursors.Player0);
        Assert.AreEqual(0UL, controller.State.EventCursors.Player1);
        Assert.AreEqual("events:Player1:0", session.Calls.Last());
    }

    [TestMethod]
    public void Player1MulliganReviewEntersActionDirectlyWhenPlayer1IsFirst()
    {
        int stage = 0;
        LegalAction pass0 = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.Mulligan, 0);
        LegalAction pass1 = HotseatTestModel.Action(
            PlayerId.Player1, ActionKind.Mulligan, 1);
        LegalAction endTurn1 = HotseatTestModel.Action(
            PlayerId.Player1, ActionKind.EndTurn, 2);
        var session = new FakeGameSession
        {
            ViewHandler = viewer => stage switch
            {
                0 => HotseatTestModel.View(
                    viewer, 0, MatchPhase.Mulligan, PlayerId.Player1, false, false,
                    [10, 11, 12], firstPlayer: PlayerId.Player1),
                1 => HotseatTestModel.View(
                    viewer, 1, MatchPhase.Mulligan, PlayerId.Player1, true, false,
                    viewer == PlayerId.Player0 ? [20, 21, 22] : [30, 31, 32],
                    firstPlayer: PlayerId.Player1),
                2 => HotseatTestModel.View(
                    viewer, 2, MatchPhase.Action, PlayerId.Player1, true, true,
                    [40, 41, 42], firstPlayer: PlayerId.Player1),
                _ => throw new AssertFailedException(),
            },
            ActionsHandler = query => HotseatTestModel.Filter(
                query,
                query.ExpectedRevision,
                stage switch
                {
                    0 => [pass0],
                    1 => [pass1],
                    2 => [endTurn1],
                    _ => [],
                }),
            SubmitHandler = command =>
            {
                stage = command.Player == PlayerId.Player0 ? 1 : 2;
                return HotseatTestModel.Status(EngineCode.Ok);
            },
            EventsHandler = (viewer, after) =>
                HotseatTestModel.Events((ulong)stage, after, after, viewer),
        };
        using var controller = new HotseatMatchController(session);

        controller.Reveal();
        Assert.IsTrue(controller.ConfirmSelection());
        controller.SubmitPreparedCommand();
        Assert.AreEqual(HotseatUiMode.MulliganReview, controller.State.Mode);
        controller.CompleteMulliganReview();
        Assert.AreEqual(PlayerId.Player1, controller.State.AwaitingPlayer);

        controller.Reveal();
        Assert.IsTrue(controller.ConfirmSelection());
        controller.SubmitPreparedCommand();
        Assert.AreEqual(HotseatUiMode.MulliganReview, controller.State.Mode);
        Assert.AreEqual(PlayerId.Player1, controller.State.Viewer);

        int player0ViewsBeforeReviewCompletion = session.Calls.Count(call =>
            call == "view:Player0");
        controller.CompleteMulliganReview();
        Assert.AreEqual(HotseatUiMode.Action, controller.State.Mode);
        Assert.AreEqual(PlayerId.Player1, controller.State.Viewer);
        Assert.AreEqual(
            player0ViewsBeforeReviewCompletion,
            session.Calls.Count(call => call == "view:Player0"));
    }

    [TestMethod]
    public void ReactionResponderRoutingDoesNotTouchTheNextViewerBeforeReveal()
    {
        int stage = 0;
        LegalAction cast = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.CastSpell,
            5,
            source: 500,
            target: Target.Leader(PlayerId.Player1));
        LegalAction trap = HotseatTestModel.Action(
            PlayerId.Player1, ActionKind.ActivateTrap, 6, source: 600);
        LegalAction pass1 = HotseatTestModel.Action(
            PlayerId.Player1, ActionKind.PassReaction, 6);
        LegalAction pass0 = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.PassReaction, 7);
        var session = new FakeGameSession
        {
            ViewHandler = viewer => stage switch
            {
                0 => HotseatTestModel.View(
                    viewer, 5, MatchPhase.Action, PlayerId.Player0, true, true, [10]),
                1 => HotseatTestModel.View(
                    viewer, 6, MatchPhase.Reaction, PlayerId.Player0, true, true, [20],
                    responder: PlayerId.Player1),
                2 => HotseatTestModel.View(
                    viewer, 7, MatchPhase.Reaction, PlayerId.Player0, true, true, [30],
                    responder: PlayerId.Player0),
                _ => throw new AssertFailedException(),
            },
            ActionsHandler = query => HotseatTestModel.Filter(
                query,
                query.ExpectedRevision,
                stage switch
                {
                    0 => [cast],
                    1 => [trap, pass1],
                    2 => [pass0],
                    _ => [],
                }),
            SubmitHandler = command =>
            {
                stage = command.Action == ActionKind.CastSpell ? 1 : 2;
                return HotseatTestModel.Status(EngineCode.Ok);
            },
            EventsHandler = (viewer, after) =>
                HotseatTestModel.Events((ulong)(stage + 5), after, after + 1, viewer),
        };
        using var controller = new HotseatMatchController(session);

        controller.Reveal();
        Assert.IsTrue(controller.AcknowledgeEvents());
        controller.SelectLegalAction(cast);
        Assert.IsTrue(controller.ConfirmSelection());
        controller.SubmitPreparedCommand();
        Assert.AreEqual(PlayerId.Player1, controller.State.AwaitingPlayer);
        Assert.IsFalse(session.Calls.Any(call => call == "view:Player1"));

        controller.Reveal();
        Assert.AreEqual(HotseatUiMode.Reaction, controller.State.Mode);
        Assert.AreEqual(PlayerId.Player1, controller.State.Viewer);
        controller.SelectLegalAction(trap);
        Assert.IsTrue(controller.ConfirmSelection());
        int player0ViewsBeforeCounterReveal = session.Calls.Count(call =>
            call == "view:Player0");
        controller.SubmitPreparedCommand();
        Assert.AreEqual(PlayerId.Player0, controller.State.AwaitingPlayer);
        Assert.AreEqual(
            player0ViewsBeforeCounterReveal,
            session.Calls.Count(call => call == "view:Player0"));

        controller.Reveal();
        Assert.AreEqual(PlayerId.Player0, controller.State.Viewer);
        Assert.AreEqual("events:Player0:1", session.Calls.Last());
    }

    [TestMethod]
    public void ProgressiveSelectionQueriesEveryStepAndUsesEnginePayment()
    {
        Target leader = Target.Leader(PlayerId.Player1);
        PaymentPreview payment = HotseatTestModel.Payment(baseCost: 2);
        LegalAction noTargetNoDonor = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.Deploy, 10, source: 200, slot: 0,
            payment: payment);
        LegalAction noTargetWithDonor = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.Deploy, 10, source: 200, slot: 0,
            donor: 300, payment: payment);
        LegalAction targetWithDonor = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.Deploy, 10, source: 200, target: leader,
            slot: 0, donor: 300, payment: payment);
        LegalAction advance = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.Deploy, 10, source: 200, slot: 0,
            useAdvance: true, payment: HotseatTestModel.Payment(2, usedAdvance: true));
        LegalAction[] actions =
            [noTargetNoDonor, noTargetWithDonor, targetWithDonor, advance];
        var session = new FakeGameSession
        {
            ViewHandler = viewer => HotseatTestModel.View(
                viewer, 10, MatchPhase.Action, PlayerId.Player0, true, true, [10]),
            ActionsHandler = query => HotseatTestModel.Filter(query, 10, actions),
            PaymentHandler = command => new PaymentResult(10, payment),
            EventsHandler = (viewer, after) => HotseatTestModel.Events(10, after, after, viewer),
        };
        using var controller = new HotseatMatchController(session);
        controller.Reveal();

        controller.BeginActionSelection(ActionKind.Deploy, 200);
        controller.SelectTarget(null);
        controller.SelectSlot(0);
        controller.SelectDonor(null);
        controller.SelectAdvance(false);
        Assert.AreSame(noTargetNoDonor, controller.State.SelectedAction);
        Assert.AreEqual(1, controller.State.CandidateOptions.Actions.Count);
        Assert.AreSame(payment, controller.PreviewSelectedPayment());

        ActionQueryRequest[] progressive = session.Queries.Skip(1).ToArray();
        Assert.HasCount(5, progressive);
        Assert.IsTrue(progressive.All(query => query.ExpectedRevision == 10));
        Assert.IsTrue(progressive.All(query => query.Action == ActionKind.Deploy));
        Assert.IsTrue(progressive.All(query => query.Source == 200));
        Assert.IsNull(progressive[1].Target);
        Assert.IsNull(progressive[3].ComponentDonor);
        Assert.AreEqual(false, progressive[4].UseAdvance);

        controller.CancelSelection();
        controller.BeginActionSelection(ActionKind.Deploy, 200);
        controller.SelectTarget(null);
        controller.SelectSlot(0);
        controller.SelectDonor(300);
        Assert.AreSame(noTargetWithDonor, controller.State.SelectedAction);
        Assert.AreEqual(300UL, session.Queries.Last().ComponentDonor);

        LegalAction forged = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.Deploy, 10, source: 200, slot: 9);
        Assert.ThrowsExactly<ArgumentException>(() => controller.SelectLegalAction(forged));
    }

    [TestMethod]
    public void QueryRevisionDriftRefreshesAndClearsSelectionWithoutBecomingFatal()
    {
        ulong currentRevision = 10;
        LegalAction deploy10 = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.Deploy, 10, source: 200, slot: 0);
        LegalAction endTurn11 = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.EndTurn, 11);
        var session = new FakeGameSession
        {
            ViewHandler = viewer => HotseatTestModel.View(
                viewer, currentRevision, MatchPhase.Action, PlayerId.Player0,
                true, true, [10]),
            ActionsHandler = query =>
            {
                if (query.ExpectedRevision == 10 && query.Action.HasValue)
                {
                    currentRevision = 11;
                    return new LegalActionsResult(11, [endTurn11]);
                }

                return HotseatTestModel.Filter(
                    query,
                    currentRevision,
                    currentRevision == 10 ? [deploy10] : [endTurn11]);
            },
            EventsHandler = (viewer, after) =>
                HotseatTestModel.Events(currentRevision, after, after, viewer),
        };
        using var controller = new HotseatMatchController(session);
        controller.Reveal();

        controller.BeginActionSelection(ActionKind.Deploy, 200);
        Assert.AreEqual(HotseatUiMode.Action, controller.State.Mode);
        Assert.AreEqual(11UL, controller.State.Snapshot!.Revision);
        Assert.AreEqual((uint)EngineCode.StaleRevision, controller.State.LastEngineCode);
        Assert.IsNull(controller.State.Selection.Action);
        Assert.IsNull(controller.State.SelectedAction);
        Assert.IsFalse(session.Calls.Any(call => call.StartsWith("submit:", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void StaleSubmitIsDebouncedResetAndNeverAutomaticallyRetried()
    {
        LegalAction endTurn = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.EndTurn, 4);
        HotseatMatchController? controller = null;
        var session = new FakeGameSession
        {
            ViewHandler = viewer => HotseatTestModel.View(
                viewer, 4, MatchPhase.Action, PlayerId.Player0, true, true, [10]),
            ActionsHandler = query => HotseatTestModel.Filter(query, 4, [endTurn]),
            SubmitHandler = _ =>
            {
                Assert.IsFalse(controller!.ConfirmSelection());
                Assert.ThrowsExactly<InvalidOperationException>(() =>
                    controller.SubmitPreparedCommand());
                return HotseatTestModel.Status(EngineCode.StaleRevision);
            },
            EventsHandler = (viewer, after) => HotseatTestModel.Events(4, after, after, viewer),
        };
        using (controller = new HotseatMatchController(session))
        {
            controller.Reveal();
            controller.SelectLegalAction(endTurn);
            Assert.IsTrue(controller.ConfirmSelection());
            Assert.IsFalse(controller.ConfirmSelection());

            EngineStatus status = controller.SubmitPreparedCommand();
            Assert.AreEqual(EngineCode.StaleRevision, status.Code);
            Assert.AreEqual(1, session.SubmittedCommands.Count);
            Assert.AreEqual(HotseatUiMode.Action, controller.State.Mode);
            Assert.AreEqual((uint)EngineCode.StaleRevision, controller.State.LastEngineCode);
            Assert.IsNull(controller.State.SelectedAction);
            Assert.IsNull(controller.State.Selection.Action);
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                controller.SubmitPreparedCommand());

            controller.SelectLegalAction(endTurn);
            Assert.IsTrue(controller.ConfirmSelection());
            Assert.ThrowsExactly<InvalidOperationException>(() => controller.CancelSelection());
            Assert.AreEqual(HotseatUiMode.Covered, controller.State.Mode);
            Assert.IsNull(controller.State.Snapshot);
            Assert.IsNull(controller.State.Viewer);
            Assert.AreEqual(1, session.SubmittedCommands.Count);
        }
    }

    [TestMethod]
    public void EventCursorsAdvanceOnlyAfterAcknowledgementAndRemainIndependent()
    {
        int stage = 0;
        LegalAction end0 = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.EndTurn, 1);
        LegalAction end1 = HotseatTestModel.Action(
            PlayerId.Player1, ActionKind.EndTurn, 2);
        LegalAction again0 = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.EndTurn, 3);
        var session = new FakeGameSession
        {
            ViewHandler = viewer => stage switch
            {
                0 => HotseatTestModel.View(
                    viewer, 1, MatchPhase.Action, PlayerId.Player0, true, true, [10]),
                1 => HotseatTestModel.View(
                    viewer, 2, MatchPhase.Action, PlayerId.Player1, true, true, [20]),
                2 => HotseatTestModel.View(
                    viewer, 3, MatchPhase.Action, PlayerId.Player0, true, true, [30]),
                _ => throw new AssertFailedException(),
            },
            ActionsHandler = query => HotseatTestModel.Filter(
                query,
                query.ExpectedRevision,
                stage switch
                {
                    0 => [end0],
                    1 => [end1],
                    2 => [again0],
                    _ => [],
                }),
            SubmitHandler = _ =>
            {
                ++stage;
                return HotseatTestModel.Status(EngineCode.Ok);
            },
            EventsHandler = (viewer, after) => stage switch
            {
                0 => HotseatTestModel.Events(1, after, 2, viewer),
                1 => HotseatTestModel.Events(2, after, 3, viewer),
                2 => HotseatTestModel.Events(3, after, 4, viewer),
                _ => throw new AssertFailedException(),
            },
        };
        using var controller = new HotseatMatchController(session);

        controller.Reveal();
        Assert.AreEqual(0UL, controller.State.EventCursors.Player0);
        Assert.AreEqual(2UL, controller.State.PendingEventLastSequence);
        controller.SelectLegalAction(end0);
        Assert.IsTrue(controller.ConfirmSelection());
        Assert.IsEmpty(controller.State.Events);
        controller.SubmitPreparedCommand();
        Assert.AreEqual(PlayerId.Player1, controller.State.AwaitingPlayer);

        controller.Reveal();
        Assert.AreEqual(0UL, controller.State.EventCursors.Player0);
        Assert.AreEqual(0UL, controller.State.EventCursors.Player1);
        Assert.IsTrue(controller.AcknowledgeEvents());
        Assert.AreEqual(3UL, controller.State.EventCursors.Player1);
        controller.SelectLegalAction(end1);
        Assert.IsTrue(controller.ConfirmSelection());
        controller.SubmitPreparedCommand();

        controller.Reveal();
        CollectionAssert.AreEqual(
            new ulong[] { 1, 2, 3, 4 },
            controller.State.PendingEvents.Select(item => item.Sequence).ToArray());
        Assert.AreEqual(
            controller.State.PendingEvents.Count,
            controller.State.PendingEvents.Select(item => item.Sequence).Distinct().Count());
        Assert.AreEqual("events:Player0:0", session.Calls.Last());
        Assert.IsTrue(controller.AcknowledgeEvents());
        Assert.AreEqual(4UL, controller.State.EventCursors.Player0);
        Assert.AreEqual(3UL, controller.State.EventCursors.Player1);
        Assert.IsEmpty(controller.State.PendingEvents);
    }

    [TestMethod]
    public void NativeFailureKeepsSensitiveStateClearedAndDisposeIsIdempotent()
    {
        LegalAction endTurn = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.EndTurn, 1);
        var session = new FakeGameSession
        {
            ViewHandler = viewer => HotseatTestModel.View(
                viewer, 1, MatchPhase.Action, PlayerId.Player0, true, true, [10]),
            ActionsHandler = query => HotseatTestModel.Filter(query, 1, [endTurn]),
            SubmitHandler = _ => throw new ScgsNativeException(
                (uint)NativeCode.InternalError,
                "native exploded"),
            EventsHandler = (viewer, after) => HotseatTestModel.Events(1, after, after, viewer),
        };
        var controller = new HotseatMatchController(session);
        controller.Reveal();
        controller.SelectLegalAction(endTurn);
        Assert.IsTrue(controller.ConfirmSelection());

        Assert.ThrowsExactly<ScgsNativeException>(() => controller.SubmitPreparedCommand());
        Assert.AreEqual(HotseatUiMode.Faulted, controller.State.Mode);
        Assert.IsNull(controller.State.Snapshot);
        Assert.IsEmpty(controller.State.LegalActions);
        Assert.IsEmpty(controller.State.Events);

        controller.Dispose();
        controller.Dispose();
        Assert.AreEqual(1, session.DisposeCalls);
    }

    [TestMethod]
    public void FutureLegalActionRemainsVisibleButCannotEnterTheSubmitPath()
    {
        LegalAction known = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.EndTurn, 1);
        LegalAction future = HotseatTestModel.Action(
            PlayerId.Player0, (ActionKind)99U, 1, source: 900);
        var session = new FakeGameSession
        {
            ViewHandler = viewer => HotseatTestModel.View(
                viewer, 1, MatchPhase.Action, PlayerId.Player0, true, true, [10]),
            ActionsHandler = query => HotseatTestModel.Filter(query, 1, [known, future]),
            EventsHandler = (viewer, after) => HotseatTestModel.Events(1, after, after, viewer),
        };
        using var controller = new HotseatMatchController(session);

        controller.Reveal();
        Assert.IsTrue(controller.State.LegalActions.Any(action =>
            (uint)action.Command.Action == 99U));
        Assert.IsTrue(controller.State.CandidateOptions.ActionKinds.Any(action =>
            (uint)action == 99U));
        Assert.ThrowsExactly<NotSupportedException>(() =>
            controller.SelectLegalAction(future));
        Assert.IsFalse(controller.State.CommandPrepared);
        Assert.IsFalse(session.Calls.Any(call =>
            call.StartsWith("submit:", StringComparison.Ordinal)));
    }
}
