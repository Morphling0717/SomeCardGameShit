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
        Assert.AreEqual(HotseatUiMode.Resolving, controller.State.Mode);
        Assert.IsFalse(controller.State.IsCovered);
        Assert.IsNull(controller.State.Snapshot);
        Assert.IsNotNull(controller.State.PublicBoard);
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
            Assert.AreEqual(HotseatUiMode.Resolving, controller.State.Mode);
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

    [TestMethod]
    public void SourceFirstSelectionAutoCompletesOnlySafeDefaultsAndRequiresExactSlot()
    {
        PaymentPreview payment = HotseatTestModel.Payment(baseCost: 1);
        LegalAction slot0 = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.PlayUnit, 12, source: 100, slot: 0,
            payment: payment);
        LegalAction slot1 = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.PlayUnit, 12, source: 100, slot: 1,
            payment: payment);
        var session = ActionSession(12, [slot0, slot1]);
        using var controller = new HotseatMatchController(session);
        controller.Reveal();

        controller.BeginSourceSelection(100);

        Assert.AreEqual(ActionKind.PlayUnit, controller.State.Interaction.Action);
        Assert.AreEqual(HotseatSelectionStep.ChooseSlot, controller.State.Interaction.Step);
        Assert.IsTrue(controller.State.Selection.HasTarget);
        Assert.IsNull(controller.State.Selection.Target);
        Assert.IsTrue(controller.State.Selection.HasDonor);
        Assert.IsNull(controller.State.Selection.Donor);
        Assert.IsTrue(controller.State.Selection.HasAdvanceChoice);
        Assert.IsFalse(controller.State.Selection.UseAdvance);
        Assert.IsFalse(controller.State.Selection.HasSlot);
        Assert.IsNull(controller.State.SelectedAction);
        Assert.AreSame(payment, controller.State.Interaction.Payment);
        Assert.IsFalse(controller.PrepareSelectedCommand());

        controller.SelectSlot(1);

        Assert.AreEqual(HotseatSelectionStep.Ready, controller.State.Interaction.Step);
        Assert.AreSame(slot1, controller.State.Interaction.CanonicalAction);
        Assert.AreSame(payment, controller.State.Interaction.Payment);
        Assert.AreEqual(1UL, controller.State.SelectedAction!.Command.Slot);
    }

    [TestMethod]
    public void FirstSourceClickNeverPreparesAnOtherwiseCompleteCommand()
    {
        LegalAction endTurn = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.EndTurn, 18);
        var session = ActionSession(18, [endTurn]);
        using var controller = new HotseatMatchController(session);
        controller.Reveal();

        controller.BeginSourceSelection(0);

        Assert.AreEqual(HotseatSelectionStep.Ready, controller.State.Interaction.Step);
        Assert.AreSame(endTurn, controller.State.SelectedAction);
        Assert.AreEqual(HotseatUiMode.Action, controller.State.Mode);
        Assert.IsFalse(controller.State.CommandPrepared);
        Assert.IsEmpty(session.SubmittedCommands);

        Assert.IsTrue(controller.PrepareSelectedCommand());
        Assert.AreEqual(HotseatUiMode.Resolving, controller.State.Mode);
    }

    [TestMethod]
    public void MultipleSourceVerbsUseContextChoiceAndStepBackTracksOnlyExplicitChoices()
    {
        Target enemyLeader = Target.Leader(PlayerId.Player1);
        LegalAction attack = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.Attack, 20, source: 500, target: enemyLeader);
        LegalAction evolve = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.Evolve, 20, source: 500);
        var session = ActionSession(20, [attack, evolve]);
        using var controller = new HotseatMatchController(session);
        controller.Reveal();

        controller.BeginSourceSelection(500);
        Assert.AreEqual(HotseatSelectionStep.ChooseAction, controller.State.Interaction.Step);
        Assert.IsNull(controller.State.Interaction.Action);
        CollectionAssert.AreEquivalent(
            new[] { ActionKind.Attack, ActionKind.Evolve },
            controller.State.Interaction.Actions.ToArray());

        controller.ChooseAction(ActionKind.Attack);
        Assert.AreEqual(HotseatSelectionStep.ChooseTarget, controller.State.Interaction.Step);
        Assert.IsTrue(controller.State.Interaction.CanStepBack);

        Assert.IsTrue(controller.StepBackSelection());
        Assert.AreEqual(HotseatSelectionStep.ChooseAction, controller.State.Interaction.Step);
        Assert.AreEqual(500UL, controller.State.Interaction.Source);
        Assert.IsNull(controller.State.Interaction.Action);

        Assert.IsTrue(controller.StepBackSelection());
        Assert.AreEqual(HotseatSelectionStep.None, controller.State.Interaction.Step);
        Assert.IsNull(controller.State.Interaction.Source);
        Assert.IsFalse(controller.State.Interaction.CanStepBack);
        Assert.IsFalse(controller.StepBackSelection());
    }

    [TestMethod]
    public void ForcedAdvanceIsAutoCompletedWhileTheOnlyNonNullTargetStaysExplicit()
    {
        Target enemy = Target.UnitTarget(PlayerId.Player1, 620);
        LegalAction forcedAdvance = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.CastSpell, 25, source: 610, target: enemy,
            useAdvance: true,
            payment: HotseatTestModel.Payment(baseCost: 2, usedAdvance: true));
        using var controller = new HotseatMatchController(ActionSession(25, [forcedAdvance]));
        controller.Reveal();

        controller.BeginSourceSelection(610);

        Assert.AreEqual(ActionKind.CastSpell, controller.State.Interaction.Action);
        Assert.IsTrue(controller.State.Selection.HasAdvanceChoice);
        Assert.IsTrue(controller.State.Selection.UseAdvance);
        Assert.IsFalse(controller.State.Selection.HasTarget);
        Assert.AreEqual(HotseatSelectionStep.ChooseTarget, controller.State.Interaction.Step);
        Assert.IsFalse(controller.PrepareSelectedCommand());

        controller.SelectTarget(enemy);
        Assert.AreEqual(HotseatSelectionStep.Ready, controller.State.Interaction.Step);
        Assert.AreSame(forcedAdvance, controller.State.Interaction.CanonicalAction);
    }

    [TestMethod]
    public void DeploySelectionRequiresDonorSlotTargetThenAdvanceEvenWhenEachIsUnique()
    {
        Target enemy = Target.UnitTarget(PlayerId.Player1, 901);
        LegalAction withoutDonor = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.Deploy, 30, source: 700, slot: 0);
        LegalAction withDonor = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.Deploy, 30, source: 700, target: enemy,
            slot: 0, donor: 800);
        LegalAction withAdvance = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.Deploy, 30, source: 700, target: enemy,
            slot: 0, donor: 800, useAdvance: true,
            payment: HotseatTestModel.Payment(usedAdvance: true));
        var session = ActionSession(30, [withoutDonor, withDonor, withAdvance]);
        using var controller = new HotseatMatchController(session);
        controller.Reveal();

        controller.BeginSourceSelection(700);
        Assert.AreEqual(HotseatSelectionStep.ChooseDonor, controller.State.Interaction.Step);

        controller.SelectDonor(800);
        Assert.AreEqual(HotseatSelectionStep.ChooseSlot, controller.State.Interaction.Step);
        Assert.IsFalse(controller.State.Selection.HasSlot);

        controller.SelectSlot(0);
        Assert.AreEqual(HotseatSelectionStep.ChooseTarget, controller.State.Interaction.Step);
        Assert.IsFalse(controller.State.Selection.HasTarget);

        controller.SelectTarget(enemy);
        Assert.AreEqual(HotseatSelectionStep.ChooseAdvance, controller.State.Interaction.Step);

        controller.SelectAdvance(false);
        Assert.AreEqual(HotseatSelectionStep.Ready, controller.State.Interaction.Step);
        Assert.AreSame(withDonor, controller.State.SelectedAction);

        Assert.IsTrue(controller.StepBackSelection());
        Assert.AreEqual(HotseatSelectionStep.ChooseAdvance, controller.State.Interaction.Step);
        Assert.IsFalse(controller.State.Selection.HasAdvanceChoice);
    }

    [TestMethod]
    public void ResolvingUsesNeutralProjectionAndMakesAllPrivateStateUnreachable()
    {
        CardView publicUnit = HotseatTestModel.Card(
            401, PlayerId.Player0, Zone.Unit, "公开单位");
        CardView ownSecretTrap = HotseatTestModel.Card(
            402, PlayerId.Player0, Zone.Tactic, "己方绝密伏策", faceDown: true);
        CardView enemySecretTrap = HotseatTestModel.Card(
            403, PlayerId.Player1, Zone.Tactic, "敌方绝密伏策", faceDown: true);
        LegalAction endTurn = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.EndTurn, 40);
        var session = new FakeGameSession
        {
            ViewHandler = viewer => HotseatTestModel.View(
                viewer, 40, MatchPhase.Action, PlayerId.Player0, true, true,
                [101, 102],
                player0Units: [publicUnit, null, null, null, null],
                player0Tactics: [ownSecretTrap, null, null],
                player1Tactics: [enemySecretTrap, null, null]),
            ActionsHandler = query => HotseatTestModel.Filter(query, 40, [endTurn]),
            EventsHandler = (viewer, after) => HotseatTestModel.Events(40, after, 2, viewer),
        };
        using var controller = new HotseatMatchController(session);
        controller.Reveal();
        controller.SelectLegalAction(endTurn);

        Assert.IsTrue(controller.PrepareSelectedCommand());
        HotseatUiState state = controller.State;

        Assert.AreEqual(HotseatUiMode.Resolving, state.Mode);
        Assert.IsNull(state.CoverReason);
        Assert.IsNull(state.Viewer);
        Assert.IsNull(state.AwaitingPlayer);
        Assert.IsNull(state.Snapshot);
        Assert.IsEmpty(state.LegalActions);
        Assert.IsEmpty(state.CandidateOptions.Actions);
        Assert.IsEmpty(state.Events);
        Assert.IsEmpty(state.PendingEvents);
        Assert.IsFalse(state.PendingEventLastSequence.HasValue);
        Assert.AreEqual(HotseatSelectionStep.None, state.Interaction.Step);
        Assert.IsTrue(state.CommandPrepared);
        Assert.ThrowsExactly<InvalidOperationException>(() => controller.AcknowledgeEvents());

        HotseatPublicBoardView board = state.PublicBoard!;
        Assert.AreEqual(40UL, board.Revision);
        Assert.IsNull(typeof(HotseatPublicPlayerView).GetProperty("Hand"));
        HotseatPublicCardView projectedUnit = board.Players[0].Units[0]!;
        Assert.AreEqual(401UL, projectedUnit.InstanceId);
        Assert.AreEqual("公开单位", projectedUnit.Name);

        HotseatPublicCardView projectedOwnTrap = board.Players[0].Tactics[0]!;
        HotseatPublicCardView projectedEnemyTrap = board.Players[1].Tactics[0]!;
        foreach (HotseatPublicCardView trap in new[] { projectedOwnTrap, projectedEnemyTrap })
        {
            Assert.IsTrue(trap.FaceDown);
            Assert.IsNull(trap.InstanceId);
            Assert.IsNull(trap.DefinitionId);
            Assert.IsNull(trap.Kind);
            Assert.AreEqual(string.Empty, trap.Name);
            Assert.AreEqual(0, trap.Cost);
            Assert.AreEqual(0, trap.CurrentAttack);
            Assert.IsFalse(trap.HasKnownIdentity);
        }
    }

    [TestMethod]
    public void DifferentExplicitSelectionOrdersConvergeOnTheSameCanonicalCommand()
    {
        Target enemy = Target.UnitTarget(PlayerId.Player1, 991);
        LegalAction action = HotseatTestModel.Action(
            PlayerId.Player0, ActionKind.PlayUnit, 50, source: 100, target: enemy,
            slot: 3);

        using var first = new HotseatMatchController(ActionSession(50, [action]));
        first.Reveal();
        first.BeginSourceSelection(100);
        first.SelectSlot(3);
        first.SelectTarget(enemy);
        GameCommandRequest firstCommand = first.State.Interaction.CanonicalAction!.Command;

        using var second = new HotseatMatchController(ActionSession(50, [action]));
        second.Reveal();
        second.BeginSourceSelection(100);
        second.SelectTarget(enemy);
        second.SelectSlot(3);
        GameCommandRequest secondCommand = second.State.Interaction.CanonicalAction!.Command;

        Assert.AreEqual(firstCommand, secondCommand);
        Assert.AreEqual(HotseatSelectionStep.Ready, first.State.Interaction.Step);
        Assert.AreEqual(HotseatSelectionStep.Ready, second.State.Interaction.Step);
    }

    [TestMethod]
    public void FrozenActionKindsEnterThroughTheirExpectedPhaseSourceAndSelectionStep()
    {
        Target enemy = Target.UnitTarget(PlayerId.Player1, 9_001);
        var seen = new List<ActionKind>();

        LegalAction mulligan = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.Mulligan,
            60);
        using (var controller = new HotseatMatchController(
                   ActionSession(60, [mulligan], MatchPhase.Mulligan)))
        {
            controller.Reveal();
            Assert.AreEqual(HotseatUiMode.MulliganSelecting, controller.State.Mode);
            Assert.AreEqual(HotseatSelectionStep.Ready, controller.State.Interaction.Step);
            Assert.AreEqual(0UL, controller.State.SelectedAction!.Command.Source);
            Assert.IsFalse(controller.State.CommandPrepared);
            seen.Add(ActionKind.Mulligan);
        }

        var cases = new[]
        {
            (ActionKind.PlayUnit, MatchPhase.Action, 1_001UL, (Target?)null, (ulong?)2, (ulong?)null,
                HotseatSelectionStep.ChooseSlot),
            (ActionKind.CastSpell, MatchPhase.Action, 1_002UL, enemy, (ulong?)null, (ulong?)null,
                HotseatSelectionStep.ChooseTarget),
            (ActionKind.PlayTactic, MatchPhase.Action, 1_003UL, (Target?)null, (ulong?)1, (ulong?)null,
                HotseatSelectionStep.ChooseSlot),
            (ActionKind.Attack, MatchPhase.Action, 1_004UL, enemy, (ulong?)null, (ulong?)null,
                HotseatSelectionStep.ChooseTarget),
            (ActionKind.Evolve, MatchPhase.Action, 1_005UL, (Target?)null, (ulong?)null, (ulong?)null,
                HotseatSelectionStep.Ready),
            (ActionKind.Deploy, MatchPhase.Action, 1_006UL, enemy, (ulong?)2, (ulong?)8_001,
                HotseatSelectionStep.ChooseDonor),
            (ActionKind.ActivateTrap, MatchPhase.Reaction, 1_007UL, enemy, (ulong?)null, (ulong?)null,
                HotseatSelectionStep.ChooseTarget),
            (ActionKind.PassReaction, MatchPhase.Reaction, 0UL, (Target?)null, (ulong?)null, (ulong?)null,
                HotseatSelectionStep.Ready),
            (ActionKind.EndTurn, MatchPhase.Action, 0UL, (Target?)null, (ulong?)null, (ulong?)null,
                HotseatSelectionStep.Ready),
            (ActionKind.Surrender, MatchPhase.Action, 0UL, (Target?)null, (ulong?)null, (ulong?)null,
                HotseatSelectionStep.Ready),
        };

        foreach ((ActionKind kind, MatchPhase phase, ulong source, Target? target,
                     ulong? slot, ulong? donor, HotseatSelectionStep expectedStep) in cases)
        {
            LegalAction action = HotseatTestModel.Action(
                PlayerId.Player0,
                kind,
                60 + (uint)kind,
                source,
                target,
                slot,
                donor);

            using var controller = new HotseatMatchController(
                ActionSession(action.Command.ExpectedRevision, [action], phase));
            controller.Reveal();
            controller.BeginSourceSelection(source);

            Assert.AreEqual(
                phase == MatchPhase.Reaction ? HotseatUiMode.Reaction : HotseatUiMode.Action,
                controller.State.Mode,
                $"{kind} entered through the wrong hot-seat phase.");
            Assert.AreEqual(source, controller.State.Interaction.Source, $"{kind} source mapping changed.");
            Assert.AreEqual(expectedStep, controller.State.Interaction.Step, $"{kind} began at the wrong step.");
            Assert.IsFalse(controller.State.CommandPrepared, $"{kind} submitted on its first source click.");

            for (int guard = 0; guard < 5 &&
                 controller.State.Interaction.Step != HotseatSelectionStep.Ready; guard++)
            {
                switch (controller.State.Interaction.Step)
                {
                    case HotseatSelectionStep.ChooseDonor:
                        controller.SelectDonor(donor);
                        break;
                    case HotseatSelectionStep.ChooseSlot:
                        controller.SelectSlot(slot);
                        break;
                    case HotseatSelectionStep.ChooseTarget:
                        controller.SelectTarget(target);
                        break;
                    case HotseatSelectionStep.ChooseAdvance:
                        controller.SelectAdvance(action.Command.UseAdvance);
                        break;
                    default:
                        Assert.Fail(
                            $"{kind} stopped at unexpected step {controller.State.Interaction.Step}.");
                        break;
                }
            }

            Assert.AreEqual(
                HotseatSelectionStep.Ready,
                controller.State.Interaction.Step,
                $"{kind} did not converge to a ready command.");
            Assert.AreEqual(kind, controller.State.Interaction.Action);
            Assert.AreSame(action, controller.State.Interaction.CanonicalAction);
            Assert.IsNotNull(controller.State.Interaction.Payment);
            seen.Add(kind);
        }

        ActionKind[] covered = seen.OrderBy(kind => kind).ToArray();
        CollectionAssert.AreEqual(Enum.GetValues<ActionKind>(), covered);
    }

    private static FakeGameSession ActionSession(
        ulong revision,
        IReadOnlyList<LegalAction> actions,
        MatchPhase phase = MatchPhase.Action) => new()
        {
            ViewHandler = viewer => HotseatTestModel.View(
                viewer, revision, phase, PlayerId.Player0, true, true, [10]),
            ActionsHandler = query => HotseatTestModel.Filter(query, revision, actions),
            EventsHandler = (viewer, after) =>
                HotseatTestModel.Events(revision, after, after, viewer),
        };
}
