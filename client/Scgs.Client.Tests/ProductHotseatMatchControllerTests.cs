// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scgs.Hotseat.Product;
using V05 = Scgs.Client.V05;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class ProductHotseatMatchControllerTests
{
    [TestMethod]
    public void ActorPriorityIsChoiceThenReactionThenMulliganThenActivePlayer()
    {
        V05.MatchView choiceAndReaction = ProductHotseatTestModel.View(
            V05.PlayerId.Player0,
            10,
            V05.MatchPhase.Reaction,
            reaction: ProductHotseatTestModel.Reaction(
                10, pending: true, responder: V05.PlayerId.Player0),
            choice: ProductHotseatTestModel.Choice(
                10,
                V05.PlayerId.Player1,
                V05.PendingChoiceKind.Cards,
                false,
                1,
                1,
                ["a"],
                redacted: true));
        Assert.AreEqual(
            V05.PlayerId.Player1,
            ProductHotseatMatchController.DetermineActor(choiceAndReaction));

        V05.MatchView reaction = ProductHotseatTestModel.View(
            V05.PlayerId.Player0,
            11,
            V05.MatchPhase.Reaction,
            reaction: ProductHotseatTestModel.Reaction(
                11, pending: true, responder: V05.PlayerId.Player1));
        Assert.AreEqual(
            V05.PlayerId.Player1,
            ProductHotseatMatchController.DetermineActor(reaction));

        Assert.AreEqual(
            V05.PlayerId.Player0,
            ProductHotseatMatchController.DetermineActor(ProductHotseatTestModel.View(
                V05.PlayerId.Player0,
                12,
                V05.MatchPhase.Mulligan,
                V05.PlayerId.Player1,
                player0MulliganDone: false,
                player1MulliganDone: false)));
        Assert.AreEqual(
            V05.PlayerId.Player1,
            ProductHotseatMatchController.DetermineActor(ProductHotseatTestModel.View(
                V05.PlayerId.Player0,
                13,
                V05.MatchPhase.Mulligan,
                V05.PlayerId.Player0,
                player0MulliganDone: true,
                player1MulliganDone: false)));
        Assert.AreEqual(
            V05.PlayerId.Player1,
            ProductHotseatMatchController.DetermineActor(ProductHotseatTestModel.View(
                V05.PlayerId.Player0,
                14,
                V05.MatchPhase.Action,
                V05.PlayerId.Player1)));
        Assert.IsNull(ProductHotseatMatchController.DetermineActor(ProductHotseatTestModel.View(
            V05.PlayerId.Player0,
            15,
            V05.MatchPhase.Finished,
            result: V05.GameResult.Player0Won)));
    }

    [TestMethod]
    public void OpponentPendingChoiceCausesOpaquePassBeforeAnyPrivateRead()
    {
        V05.LegalAction resolve = ProductHotseatTestModel.Action(
            V05.PlayerId.Player1,
            V05.ActionKind.ResolveChoice,
            7,
            choiceId: "choice-current",
            selectedOptionIds: ["secret-a"]);
        var session = new FakeProductGameSession
        {
            ViewHandler = viewer => ProductHotseatTestModel.View(
                viewer,
                7,
                V05.MatchPhase.Action,
                V05.PlayerId.Player0,
                choice: ProductHotseatTestModel.Choice(
                    7,
                    V05.PlayerId.Player1,
                    V05.PendingChoiceKind.Cards,
                    false,
                    1,
                    1,
                    ["secret-a"],
                    redacted: viewer == V05.PlayerId.Player0)),
            ActionsHandler = query => ProductHotseatTestModel.Filter(query, 7, [resolve]),
            EventsHandler = (viewer, after) => ProductHotseatTestModel.Events(7, after, after, viewer),
        };
        using var controller = new ProductHotseatMatchController(session);

        controller.Reveal();
        Assert.AreEqual(ProductHotseatUiMode.Covered, controller.State.Mode);
        Assert.AreEqual(V05.PlayerId.Player1, controller.State.AwaitingPlayer);
        CollectionAssert.AreEqual(new[] { "view:Player0" }, session.Calls.ToArray());

        controller.Reveal();
        Assert.AreEqual(ProductHotseatUiMode.Choice, controller.State.Mode);
        Assert.AreEqual(V05.PlayerId.Player1, controller.State.Viewer);
        Assert.IsTrue(controller.State.PendingChoice.RequiresInput);
        Assert.AreEqual("secret-a", controller.State.PendingChoice.Options.Single().OptionId);
        Assert.IsTrue(session.Calls.Contains("events:Player1:0", StringComparer.Ordinal));
    }

    [TestMethod]
    public void MulliganUsesCanonicalHandOrderAndKeepsViewerCursorsIndependent()
    {
        int stage = 0;
        V05.LegalAction pass0 = ProductHotseatTestModel.Action(
            V05.PlayerId.Player0, V05.ActionKind.Mulligan, 0);
        V05.LegalAction replace0 = ProductHotseatTestModel.Action(
            V05.PlayerId.Player0,
            V05.ActionKind.Mulligan,
            0,
            mulliganCards: [11, 13]);
        V05.LegalAction pass1 = ProductHotseatTestModel.Action(
            V05.PlayerId.Player1, V05.ActionKind.Mulligan, 1);
        var session = new FakeProductGameSession
        {
            ViewHandler = viewer => stage switch
            {
                0 => ProductHotseatTestModel.View(
                    viewer,
                    0,
                    V05.MatchPhase.Mulligan,
                    V05.PlayerId.Player0,
                    false,
                    false,
                    [11, 12, 13]),
                1 when viewer == V05.PlayerId.Player0 => ProductHotseatTestModel.View(
                    viewer,
                    1,
                    V05.MatchPhase.Mulligan,
                    V05.PlayerId.Player0,
                    true,
                    false,
                    [21, 22, 23]),
                1 => ProductHotseatTestModel.View(
                    viewer,
                    1,
                    V05.MatchPhase.Mulligan,
                    V05.PlayerId.Player0,
                    true,
                    false,
                    [31, 32, 33]),
                _ => throw new AssertFailedException($"Unexpected stage {stage}.")
            },
            ActionsHandler = query => ProductHotseatTestModel.Filter(
                query,
                query.ExpectedRevision,
                query.Player == V05.PlayerId.Player0 ? [pass0, replace0] : [pass1]),
            SubmitHandler = _ =>
            {
                stage = 1;
                return ProductHotseatTestModel.Status(V05.EngineCode.Ok);
            },
            EventsHandler = (viewer, after) => (stage, viewer) switch
            {
                (0, V05.PlayerId.Player0) => ProductHotseatTestModel.Events(0, after, 1, viewer),
                (1, V05.PlayerId.Player0) => ProductHotseatTestModel.Events(1, after, 2, viewer),
                (1, V05.PlayerId.Player1) => ProductHotseatTestModel.Events(1, after, 3, viewer),
                _ => throw new AssertFailedException()
            },
        };
        using var controller = new ProductHotseatMatchController(session);

        controller.Reveal();
        Assert.AreEqual(ProductHotseatUiMode.MulliganSelecting, controller.State.Mode);
        Assert.IsTrue(controller.AcknowledgeEvents());
        Assert.AreEqual(1UL, controller.State.EventCursors.Player0);

        controller.SelectMulliganCards([13, 11]);
        CollectionAssert.AreEqual(new ulong[] { 11, 13 }, controller.State.MulliganCards.ToArray());
        Assert.IsTrue(controller.PrepareSelectedCommand());
        PresentTwoFrames(controller);
        V05.EngineStatus status = controller.SubmitPreparedCommand();
        Assert.IsTrue(status.IsSuccess);
        Assert.AreEqual(ProductHotseatUiMode.MulliganReview, controller.State.Mode);
        CollectionAssert.AreEqual(
            new ulong[] { 21, 22, 23 },
            controller.State.Snapshot!.Players[0].Hand.Select(card => card.InstanceId!.Value).ToArray());
        CollectionAssert.AreEqual(
            new ulong[] { 11, 13 },
            session.SubmittedCommands.Single().MulliganCards.ToArray());
        Assert.IsTrue(controller.AcknowledgeEvents());
        Assert.AreEqual(2UL, controller.State.EventCursors.Player0);
        Assert.AreEqual(0UL, controller.State.EventCursors.Player1);

        controller.CompleteMulliganReview();
        Assert.AreEqual(ProductHotseatUiMode.Covered, controller.State.Mode);
        Assert.AreEqual(V05.PlayerId.Player1, controller.State.AwaitingPlayer);
        int beforeReveal = session.Calls.Count;
        controller.Reveal();
        Assert.AreEqual("events:Player1:0", session.Calls.Last());
        Assert.IsTrue(session.Calls.Skip(beforeReveal).All(call =>
            !call.Contains("Player0", StringComparison.Ordinal)));
        Assert.AreEqual(0UL, controller.State.EventCursors.Player1);
        Assert.AreEqual(3UL, controller.State.PendingEventLastSequence);
    }

    [TestMethod]
    public void ProgressiveActionSelectsModeCostSlotTargetAdvanceAndStepsBack()
    {
        V05.Target target = V05.Target.PermanentTarget(V05.PlayerId.Player1, 990);
        V05.PaymentPreview payment = ProductHotseatTestModel.Payment(4, usedAdvance: true);
        V05.LegalAction action = ProductHotseatTestModel.Action(
            V05.PlayerId.Player0,
            V05.ActionKind.Deploy,
            25,
            source: 500,
            target: target,
            slot: 3,
            modeId: "sacrifice",
            useAdvance: true,
            additionalCostCards: [702, 701],
            payment: payment);
        FakeProductGameSession session = ProductHotseatTestModel.ActionSession(25, [action]);
        using var controller = new ProductHotseatMatchController(session);
        controller.Reveal();

        controller.BeginSourceSelection(500);
        Assert.AreEqual(ProductHotseatSelectionStep.ChooseMode, controller.State.Interaction.Step);
        controller.SelectMode("sacrifice");
        Assert.AreEqual(ProductHotseatSelectionStep.ChooseAdditionalCost, controller.State.Interaction.Step);
        controller.SelectAdditionalCostCards([701, 702]);
        CollectionAssert.AreEqual(
            new ulong[] { 702, 701 },
            controller.State.Selection.AdditionalCostCards.ToArray());
        Assert.AreEqual(ProductHotseatSelectionStep.ChooseSlot, controller.State.Interaction.Step);
        controller.SelectSlot(3);
        Assert.AreEqual(ProductHotseatSelectionStep.ChooseTarget, controller.State.Interaction.Step);
        controller.SelectTarget(target);
        Assert.AreEqual(ProductHotseatSelectionStep.ChooseAdvance, controller.State.Interaction.Step);
        controller.SelectAdvance(true);

        Assert.AreEqual(ProductHotseatSelectionStep.Ready, controller.State.Interaction.Step);
        Assert.AreSame(action, controller.State.SelectedAction);
        Assert.AreSame(payment, controller.PreviewSelectedPayment());
        Assert.IsTrue(controller.StepBackSelection());
        Assert.AreEqual(ProductHotseatSelectionStep.ChooseAdvance, controller.State.Interaction.Step);
        Assert.IsFalse(controller.State.Selection.HasAdvanceChoice);
        Assert.ThrowsExactly<ArgumentException>(() => controller.SelectAdvance(false));
        controller.SelectAdvance(true);
        Assert.AreSame(action, controller.State.Interaction.CanonicalAction);
    }

    [TestMethod]
    public void DifferentActionSelectionOrdersConvergeOnTheEnumeratedCommand()
    {
        V05.Target target = V05.Target.PermanentTarget(V05.PlayerId.Player1, 991);
        V05.LegalAction action = ProductHotseatTestModel.Action(
            V05.PlayerId.Player0,
            V05.ActionKind.PlayUnit,
            26,
            source: 501,
            target: target,
            slot: 2,
            modeId: "oath",
            useAdvance: true,
            additionalCostCards: [800, 801]);

        using var first = new ProductHotseatMatchController(
            ProductHotseatTestModel.ActionSession(26, [action]));
        first.Reveal();
        first.BeginSourceSelection(501);
        first.SelectMode("oath");
        first.SelectAdditionalCostCards([801, 800]);
        first.SelectSlot(2);
        first.SelectTarget(target);
        first.SelectAdvance(true);

        using var second = new ProductHotseatMatchController(
            ProductHotseatTestModel.ActionSession(26, [action]));
        second.Reveal();
        second.BeginSourceSelection(501);
        second.SelectAdvance(true);
        second.SelectTarget(target);
        second.SelectSlot(2);
        second.SelectAdditionalCostCards([800, 801]);
        second.SelectMode("oath");

        Assert.AreSame(action, first.State.SelectedAction);
        Assert.AreSame(action, second.State.SelectedAction);
        Assert.AreEqual(
            first.State.SelectedAction!.Command,
            second.State.SelectedAction!.Command);
    }

    [TestMethod]
    public void UnorderedCardChoiceNormalizesWhileTriggerOrderPreservesPermutationAndUndo()
    {
        V05.LegalAction cards = ChoiceAction(
            V05.PendingChoiceKind.Cards,
            false,
            ["a", "b"],
            30);
        using (var controller = ChoiceController(
                   30,
                   V05.PendingChoiceKind.Cards,
                   false,
                   1,
                   2,
                   ["a", "b", "c"],
                   [cards]))
        {
            controller.Reveal();
            controller.ChooseCards(["b", "a"]);
            CollectionAssert.AreEqual(
                new[] { "a", "b" },
                controller.State.PendingChoice.SelectedOptionIds.ToArray());
            Assert.AreSame(cards, controller.State.SelectedAction);
        }

        V05.LegalAction ab = ChoiceAction(
            V05.PendingChoiceKind.TriggerOrder,
            true,
            ["a", "b"],
            31);
        V05.LegalAction ba = ChoiceAction(
            V05.PendingChoiceKind.TriggerOrder,
            true,
            ["b", "a"],
            31);
        using var ordered = ChoiceController(
            31,
            V05.PendingChoiceKind.TriggerOrder,
            true,
            2,
            2,
            ["a", "b"],
            [ab, ba]);
        ordered.Reveal();
        ordered.OrderTriggers(["a", "b"]);
        Assert.AreSame(ab, ordered.State.SelectedAction);
        ordered.MovePendingChoiceOption(0, 1);
        Assert.AreSame(ba, ordered.State.SelectedAction);
        Assert.IsTrue(ordered.StepBackSelection());
        Assert.AreSame(ab, ordered.State.SelectedAction);
    }

    [TestMethod]
    public void DedicatedModeAndChoiceAdditionalCostEntrypointsRejectWrongKinds()
    {
        V05.LegalAction mode = ChoiceAction(V05.PendingChoiceKind.Mode, false, ["mode-b"], 32);
        using (var controller = ChoiceController(
                   32,
                   V05.PendingChoiceKind.Mode,
                   false,
                   1,
                   1,
                   ["mode-a", "mode-b"],
                   [mode]))
        {
            controller.Reveal();
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                controller.ChooseAdditionalCost(["mode-b"]));
            controller.ChooseMode("mode-b");
            Assert.AreSame(mode, controller.State.SelectedAction);
        }

        V05.LegalAction cost = ChoiceAction(
            V05.PendingChoiceKind.AdditionalCost,
            false,
            ["cost-a"],
            33);
        using var additional = ChoiceController(
            33,
            V05.PendingChoiceKind.AdditionalCost,
            false,
            1,
            1,
            ["cost-a"],
            [cost]);
        additional.Reveal();
        additional.ChooseAdditionalCost(["cost-a"]);
        Assert.AreSame(cost, additional.State.SelectedAction);
    }

    [TestMethod]
    public void SuccessfulCommandPassesToReactionResponderWithoutReadingTheirViewEarly()
    {
        int stage = 0;
        V05.LegalAction cast = ProductHotseatTestModel.Action(
            V05.PlayerId.Player0,
            V05.ActionKind.CastSpell,
            40,
            source: 600,
            slot: 1);
        V05.LegalAction pass = ProductHotseatTestModel.Action(
            V05.PlayerId.Player1,
            V05.ActionKind.PassReaction,
            41);
        var session = new FakeProductGameSession
        {
            ViewHandler = viewer => stage == 0
                ? ProductHotseatTestModel.View(viewer, 40, ownHand: [600])
                : ProductHotseatTestModel.View(
                    viewer,
                    41,
                    V05.MatchPhase.Reaction,
                    V05.PlayerId.Player0,
                    reaction: ProductHotseatTestModel.Reaction(
                        41, pending: true, responder: V05.PlayerId.Player1)),
            ActionsHandler = query => ProductHotseatTestModel.Filter(
                query, query.ExpectedRevision, stage == 0 ? [cast] : [pass]),
            SubmitHandler = _ =>
            {
                stage = 1;
                return ProductHotseatTestModel.Status(V05.EngineCode.Ok);
            },
            EventsHandler = (viewer, after) =>
                ProductHotseatTestModel.Events((ulong)(40 + stage), after, after, viewer),
        };
        using var controller = new ProductHotseatMatchController(session);
        controller.Reveal();
        controller.SelectLegalAction(cast);
        Assert.IsTrue(controller.PrepareSelectedCommand());
        PresentTwoFrames(controller);
        int beforeSubmit = session.Calls.Count;
        controller.SubmitPreparedCommand();

        Assert.AreEqual(ProductHotseatUiMode.Covered, controller.State.Mode);
        Assert.AreEqual(V05.PlayerId.Player1, controller.State.AwaitingPlayer);
        CollectionAssert.AreEqual(
            new[] { "submit:Player0:CastSpell:40", "view:Player0" },
            session.Calls.Skip(beforeSubmit).ToArray());

        controller.Reveal();
        Assert.AreEqual(ProductHotseatUiMode.Reaction, controller.State.Mode);
        Assert.AreSame(pass, controller.State.LegalActions.Single());
    }

    [TestMethod]
    public void ResolvingProjectionErasesPrivateStateAndRequiresTwoDistinctFrames()
    {
        V05.CardView ownTrap = ProductHotseatTestModel.Card(
            710,
            V05.PlayerId.Player0,
            V05.Zone.Tactic,
            "己方私密伏策",
            faceDown: true,
            kind: V05.CardKind.Trap);
        V05.CardView enemyTrap = ProductHotseatTestModel.Card(
            711,
            V05.PlayerId.Player1,
            V05.Zone.Tactic,
            "敌方私密伏策",
            faceDown: true,
            kind: V05.CardKind.Trap);
        V05.LegalAction endTurn = ProductHotseatTestModel.Action(
            V05.PlayerId.Player0, V05.ActionKind.EndTurn, 50);
        FakeProductGameSession session = ProductHotseatTestModel.ActionSession(50, [endTurn], hand: [101, 102]);
        session.ViewHandler = viewer => ProductHotseatTestModel.View(
            viewer,
            50,
            ownHand: [101, 102],
            player0Tactics: [ownTrap, null, null],
            player1Tactics: [enemyTrap, null, null]);
        using var controller = new ProductHotseatMatchController(session);
        controller.Reveal();
        controller.SelectLegalAction(endTurn);

        Assert.IsTrue(controller.PrepareSelectedCommand());
        Assert.AreEqual(ProductHotseatUiMode.Resolving, controller.State.Mode);
        Assert.IsNull(controller.State.Viewer);
        Assert.IsNull(controller.State.Snapshot);
        Assert.IsEmpty(controller.State.LegalActions);
        Assert.IsEmpty(controller.State.Events);
        Assert.IsEmpty(controller.State.PendingEvents);
        Assert.IsNotNull(controller.State.PublicBoard);
        Assert.IsNull(typeof(ProductHotseatPublicPlayerView).GetProperty("Hand"));
        foreach (ProductHotseatPublicCardView trap in new[]
                 {
                     controller.State.PublicBoard!.Players[0].Tactics[0]!,
                     controller.State.PublicBoard.Players[1].Tactics[0]!,
                 })
        {
            Assert.IsTrue(trap.FaceDown);
            Assert.IsNull(trap.InstanceId);
            Assert.IsNull(trap.DesignId);
            Assert.IsNull(trap.Kind);
            Assert.AreEqual(string.Empty, trap.Name);
            Assert.IsFalse(trap.HasKnownIdentity);
        }

        int callsBeforeFrames = session.Calls.Count;
        Assert.IsFalse(controller.NotifyPublicFrameDrawn(100));
        Assert.IsFalse(controller.NotifyPublicFrameDrawn(100));
        Assert.AreEqual(1, controller.PublicFramesDrawn);
        Assert.ThrowsExactly<InvalidOperationException>(() => controller.SubmitPreparedCommand());
        Assert.HasCount(callsBeforeFrames, session.Calls);
        Assert.IsTrue(controller.NotifyPublicFrameDrawn(101));
        Assert.IsTrue(controller.CanSubmitPreparedCommand);
        controller.SubmitPreparedCommand();
        Assert.HasCount(1, session.SubmittedCommands);
    }

    [TestMethod]
    public void RejectedStaleCommandDoesNotReadStateMoveCursorOrExposePrivateData()
    {
        V05.LegalAction endTurn = ProductHotseatTestModel.Action(
            V05.PlayerId.Player0, V05.ActionKind.EndTurn, 60);
        FakeProductGameSession session = ProductHotseatTestModel.ActionSession(60, [endTurn]);
        session.EventsHandler = (viewer, after) =>
            ProductHotseatTestModel.Events(60, after, 1, viewer);
        session.SubmitHandler = _ => ProductHotseatTestModel.Status(V05.EngineCode.StaleRevision);
        using var controller = new ProductHotseatMatchController(session);
        controller.Reveal();
        Assert.IsTrue(controller.AcknowledgeEvents());
        controller.SelectLegalAction(endTurn);
        Assert.IsTrue(controller.PrepareSelectedCommand());
        PresentTwoFrames(controller);
        int callsBeforeSubmit = session.Calls.Count;

        V05.EngineStatus status = controller.SubmitPreparedCommand();

        Assert.AreEqual(V05.EngineCode.StaleRevision, status.Code);
        Assert.AreEqual(ProductHotseatUiMode.Covered, controller.State.Mode);
        Assert.AreEqual(ProductHotseatCoverReason.FailedCommand, controller.State.CoverReason);
        Assert.AreEqual(V05.PlayerId.Player0, controller.State.AwaitingPlayer);
        Assert.AreEqual(1UL, controller.State.EventCursors.Player0);
        Assert.IsNull(controller.State.Snapshot);
        Assert.IsEmpty(controller.State.Events);
        CollectionAssert.AreEqual(
            new[] { "submit:Player0:EndTurn:60" },
            session.Calls.Skip(callsBeforeSubmit).ToArray());
    }

    [TestMethod]
    public void IllegalSelectionIsRejectedLocallyAndStalePartialQueryIsAtomic()
    {
        V05.LegalAction play = ProductHotseatTestModel.Action(
            V05.PlayerId.Player0,
            V05.ActionKind.PlayAmulet,
            70,
            source: 700,
            slot: 2,
            modeId: "countdown");
        FakeProductGameSession session = ProductHotseatTestModel.ActionSession(70, [play]);
        using var controller = new ProductHotseatMatchController(session);
        controller.Reveal();
        controller.BeginSourceSelection(700);
        int beforeInvalid = session.Calls.Count;
        Assert.ThrowsExactly<ArgumentException>(() => controller.SelectSlot(4));
        Assert.HasCount(beforeInvalid, session.Calls);
        Assert.AreEqual(ProductHotseatSelectionStep.ChooseMode, controller.State.Interaction.Step);

        session.ActionsHandler = query => query.ModeId is null
            ? ProductHotseatTestModel.Filter(query, 70, [play])
            : ProductHotseatTestModel.Filter(query, 69, [play]);
        ProductHotseatActionSelection before = controller.State.Selection;
        Assert.ThrowsExactly<ScgsProtocolException>(() => controller.SelectMode("countdown"));
        Assert.AreEqual(before, controller.State.Selection);
        Assert.AreEqual(0UL, controller.State.EventCursors.Player0);
    }

    [TestMethod]
    public void OptionalTargetSkipUsesCanonicalVariantAndCanBeUndoneWithoutSubmitting()
    {
        V05.Target target = V05.Target.PermanentTarget(V05.PlayerId.Player1, 910);
        V05.LegalAction targeted = ProductHotseatTestModel.Action(
            V05.PlayerId.Player0, V05.ActionKind.PlayUnit, 71, source: 710, slot: 2, target: target);
        V05.LegalAction skipped = ProductHotseatTestModel.Action(
            V05.PlayerId.Player0, V05.ActionKind.PlayUnit, 71, source: 710, slot: 2);
        FakeProductGameSession session = ProductHotseatTestModel.ActionSession(71, [targeted, skipped]);
        using var controller = new ProductHotseatMatchController(session);
        controller.Reveal();
        controller.BeginSourceSelection(710);
        controller.SelectSlot(2);
        Assert.AreEqual(ProductHotseatSelectionStep.ChooseTarget, controller.State.Interaction.Step);

        controller.SkipOptionalTarget();

        Assert.IsTrue(controller.State.Selection.HasTarget);
        Assert.IsNull(controller.State.Selection.Target);
        Assert.AreSame(skipped, controller.State.SelectedAction);
        Assert.IsTrue(controller.State.CanPrepare);
        Assert.IsEmpty(session.SubmittedCommands);
        Assert.IsTrue(controller.StepBackSelection());
        Assert.AreEqual(ProductHotseatSelectionStep.ChooseTarget, controller.State.Interaction.Step);
        controller.SelectTarget(target);
        Assert.AreSame(targeted, controller.State.SelectedAction);
        Assert.IsEmpty(session.SubmittedCommands);
    }

    [TestMethod]
    public void RequiredTargetCannotBeSkippedAndStaleSkipQueryIsAtomic()
    {
        V05.LegalAction required = ProductHotseatTestModel.Action(
            V05.PlayerId.Player0, V05.ActionKind.Attack, 72, source: 720,
            target: V05.Target.Leader(V05.PlayerId.Player1));
        FakeProductGameSession session = ProductHotseatTestModel.ActionSession(72, [required]);
        using (var controller = new ProductHotseatMatchController(session))
        {
            controller.Reveal();
            controller.BeginSourceSelection(720);
            ProductHotseatUiState before = controller.State;
            int calls = session.Calls.Count;
            Assert.ThrowsExactly<ArgumentException>(controller.SkipOptionalTarget);
            Assert.AreSame(before, controller.State);
            Assert.HasCount(calls, session.Calls);
        }

        V05.LegalAction optional = ProductHotseatTestModel.Action(
            V05.PlayerId.Player0, V05.ActionKind.PlayUnit, 72, source: 720, slot: 0);
        FakeProductGameSession staleSession = ProductHotseatTestModel.ActionSession(72, [optional]);
        using var stale = new ProductHotseatMatchController(staleSession);
        stale.Reveal();
        stale.BeginSourceSelection(720);
        ProductHotseatUiState unchanged = stale.State;
        staleSession.ActionsHandler = query => ProductHotseatTestModel.Filter(query, 71, [optional]);
        Assert.ThrowsExactly<ScgsProtocolException>(stale.SkipOptionalTarget);
        Assert.AreSame(unchanged, stale.State);
        Assert.IsEmpty(staleSession.SubmittedCommands);
    }

    [TestMethod]
    public void EmptyPendingChoiceRequiresExplicitSkipAndNeverAutoSubmits()
    {
        V05.LegalAction empty = ChoiceAction(V05.PendingChoiceKind.Cards, false, [], 73);
        V05.LegalAction selected = ChoiceAction(V05.PendingChoiceKind.Cards, false, ["a"], 73);
        FakeProductGameSession session = ProductHotseatTestModel.ActionSession(
            73, [empty, selected], choice: ProductHotseatTestModel.Choice(
                73, V05.PlayerId.Player0, V05.PendingChoiceKind.Cards, false, 0, 1, ["a"]));
        using var controller = new ProductHotseatMatchController(session);
        controller.Reveal();

        Assert.AreEqual(ProductHotseatUiMode.Choice, controller.State.Mode);
        Assert.IsNull(controller.State.SelectedAction);
        Assert.IsFalse(controller.PrepareSelectedCommand());
        int calls = session.Calls.Count;
        controller.SkipPendingChoice();
        Assert.AreSame(empty, controller.State.SelectedAction);
        Assert.IsTrue(controller.State.PendingChoice.RequiresInput);
        Assert.IsEmpty(controller.State.PendingChoice.SelectedOptionIds);
        Assert.IsTrue(controller.State.CanPrepare);
        Assert.HasCount(calls, session.Calls);
        Assert.IsEmpty(session.SubmittedCommands);

        Assert.IsTrue(controller.PrepareSelectedCommand());
        Assert.IsEmpty(session.SubmittedCommands);
        PresentTwoFrames(controller);
        Assert.IsTrue(controller.SubmitPreparedCommand().IsSuccess);
        Assert.IsEmpty(session.SubmittedCommands.Single().SelectedOptionIds);
    }

    [TestMethod]
    public void RequiredPendingChoiceCannotBeSkippedOrCancelledAsAnEffect()
    {
        V05.LegalAction selected = ChoiceAction(V05.PendingChoiceKind.Cards, false, ["a"], 74);
        using var controller = ChoiceController(
            74, V05.PendingChoiceKind.Cards, false, 1, 1, ["a"], [selected]);
        controller.Reveal();
        ProductHotseatUiState before = controller.State;
        Assert.ThrowsExactly<InvalidOperationException>(controller.SkipPendingChoice);
        Assert.AreSame(before, controller.State);
        controller.ChooseCards(["a"]);
        controller.CancelSelection();
        Assert.AreEqual(ProductHotseatUiMode.Choice, controller.State.Mode);
        Assert.IsTrue(controller.State.PendingChoice.RequiresInput);
        Assert.AreEqual("choice-current", controller.State.PendingChoice.ChoiceId);
        Assert.IsNull(controller.State.SelectedAction);
    }

    [TestMethod]
    [DataRow(ProductHotseatUiMode.Action)]
    [DataRow(ProductHotseatUiMode.Reaction)]
    [DataRow(ProductHotseatUiMode.Choice)]
    public void ConfirmedSurrenderUsesPublicFrameGateInEveryInteractivePhase(ProductHotseatUiMode mode)
    {
        const ulong revision = 75;
        V05.MatchPhase phase = mode == ProductHotseatUiMode.Reaction
            ? V05.MatchPhase.Reaction : V05.MatchPhase.Action;
        V05.PendingChoiceView? choice = mode == ProductHotseatUiMode.Choice
            ? ProductHotseatTestModel.Choice(
                revision, V05.PlayerId.Player0, V05.PendingChoiceKind.Cards, false, 2, 2, ["a", "b"])
            : null;
        V05.LegalAction surrender = ProductHotseatTestModel.Action(
            V05.PlayerId.Player0, V05.ActionKind.Surrender, revision);
        V05.LegalAction ordinary = mode == ProductHotseatUiMode.Choice
            ? ChoiceAction(V05.PendingChoiceKind.Cards, false, ["a", "b"], revision)
            : ProductHotseatTestModel.Action(
                V05.PlayerId.Player0,
                mode == ProductHotseatUiMode.Reaction ? V05.ActionKind.PassReaction : V05.ActionKind.EndTurn,
                revision);
        bool finished = false;
        FakeProductGameSession session = ProductHotseatTestModel.ActionSession(
            revision, [ordinary, surrender], phase,
            reaction: ProductHotseatTestModel.Reaction(revision,
                pending: mode == ProductHotseatUiMode.Reaction), choice: choice);
        session.ViewHandler = viewer => ProductHotseatTestModel.View(
            viewer, finished ? revision + 1 : revision,
            finished ? V05.MatchPhase.Finished : phase,
            result: finished ? V05.GameResult.Player1Won : V05.GameResult.Ongoing,
            reaction: finished ? null : ProductHotseatTestModel.Reaction(revision,
                pending: mode == ProductHotseatUiMode.Reaction),
            choice: finished ? null : choice);
        session.EventsHandler = (viewer, after) => ProductHotseatTestModel.Events(
            finished ? revision + 1 : revision, after, after, viewer);
        session.SubmitHandler = _ =>
        {
            finished = true;
            return ProductHotseatTestModel.Status(V05.EngineCode.Ok);
        };
        using var controller = new ProductHotseatMatchController(session);
        Assert.IsFalse(controller.PrepareSurrender());
        controller.Reveal();
        if (mode == ProductHotseatUiMode.Choice)
        {
            // One of two required selections is intentionally unfinished.
            controller.SelectPendingChoiceOptions(["a"]);
            Assert.IsFalse(controller.State.CanPrepare);
        }

        Assert.AreEqual(mode, controller.State.Mode);
        Assert.IsEmpty(session.SubmittedCommands);
        Assert.IsTrue(controller.PrepareSurrender());
        Assert.AreEqual(ProductHotseatUiMode.Resolving, controller.State.Mode);
        Assert.IsNull(controller.State.Snapshot);
        Assert.IsEmpty(controller.State.PendingChoice.Options);
        Assert.IsEmpty(session.SubmittedCommands);
        Assert.ThrowsExactly<InvalidOperationException>(() => controller.SubmitPreparedCommand());
        PresentTwoFrames(controller);
        Assert.IsTrue(controller.SubmitPreparedCommand().IsSuccess);
        Assert.AreEqual(V05.ActionKind.Surrender, session.SubmittedCommands.Single().Action);
        Assert.IsEmpty(session.SubmittedCommands.Single().SelectedOptionIds);
        Assert.IsNull(session.SubmittedCommands.Single().ChoiceId);
        Assert.AreEqual(ProductHotseatUiMode.Finished, controller.State.Mode);
    }

    [TestMethod]
    [DataRow(V05.CardKind.Follower)]
    [DataRow(V05.CardKind.Amulet)]
    public void DeploymentCanUseItsAdditionalCostPermanentSlot(V05.CardKind costKind)
    {
        V05.CardView?[] board = Enumerable.Range(0, 5).Select(index =>
            ProductHotseatTestModel.Card((ulong)(760 + index), V05.PlayerId.Player0,
                V05.Zone.MainBoard, kind: index == 3 ? costKind : V05.CardKind.Follower)).ToArray();
        V05.LegalAction deploy = ProductHotseatTestModel.Action(
            V05.PlayerId.Player0, V05.ActionKind.Deploy, 76, source: 800, slot: 3,
            additionalCostCards: [763]);
        FakeProductGameSession session = ProductHotseatTestModel.ActionSession(76, [deploy]);
        session.ViewHandler = viewer => ProductHotseatTestModel.View(viewer, 76, player0MainBoard: board);
        using var controller = new ProductHotseatMatchController(session);
        controller.Reveal();
        controller.BeginSourceSelection(800);
        Assert.AreEqual(ProductHotseatSelectionStep.ChooseAdditionalCost, controller.State.Interaction.Step);
        controller.SelectAdditionalCostCards([763]);
        Assert.AreEqual(ProductHotseatSelectionStep.ChooseSlot, controller.State.Interaction.Step);
        controller.SelectSlot(3);
        Assert.AreSame(deploy, controller.State.SelectedAction);
        Assert.IsEmpty(session.SubmittedCommands);
    }

    [TestMethod]
    public void OccupiedDeploymentSlotOutsideItsAdditionalCostIsRejected()
    {
        V05.CardView occupied = ProductHotseatTestModel.Card(771, V05.PlayerId.Player0, V05.Zone.MainBoard);
        V05.LegalAction invalid = ProductHotseatTestModel.Action(
            V05.PlayerId.Player0, V05.ActionKind.Deploy, 77, source: 800, slot: 1,
            additionalCostCards: [772]);
        FakeProductGameSession session = ProductHotseatTestModel.ActionSession(77, [invalid]);
        session.ViewHandler = viewer => ProductHotseatTestModel.View(
            viewer, 77, player0MainBoard: [null, occupied, null, null, null]);
        using var controller = new ProductHotseatMatchController(session);
        Assert.ThrowsExactly<ScgsProtocolException>(controller.Reveal);
        Assert.AreEqual(ProductHotseatUiMode.Faulted, controller.State.Mode);
        Assert.IsEmpty(session.SubmittedCommands);
    }

    [TestMethod]
    public void AllSchemaTwoActionKindsHaveAProductHotseatEntryPath()
    {
        var seen = new HashSet<V05.ActionKind>();
        foreach (V05.ActionKind kind in Enum.GetValues<V05.ActionKind>())
        {
            ulong revision = 100 + (uint)kind;
            if (kind == V05.ActionKind.Mulligan)
            {
                V05.LegalAction mulligan = ProductHotseatTestModel.Action(
                    V05.PlayerId.Player0, kind, revision);
                using var controller = new ProductHotseatMatchController(
                    ProductHotseatTestModel.ActionSession(
                        revision,
                        [mulligan],
                        V05.MatchPhase.Mulligan,
                        hand: [1, 2, 3, 4]));
                controller.Reveal();
                Assert.AreSame(mulligan, controller.State.SelectedAction);
                seen.Add(kind);
                continue;
            }

            if (kind == V05.ActionKind.ResolveChoice)
            {
                V05.LegalAction resolve = ChoiceAction(
                    V05.PendingChoiceKind.Cards,
                    false,
                    ["a"],
                    revision);
                using var controller = ChoiceController(
                    revision,
                    V05.PendingChoiceKind.Cards,
                    false,
                    1,
                    1,
                    ["a"],
                    [resolve]);
                controller.Reveal();
                controller.ChooseCards(["a"]);
                Assert.AreSame(resolve, controller.State.SelectedAction);
                seen.Add(kind);
                continue;
            }

            bool isReaction = kind is V05.ActionKind.ActivateTrap or V05.ActionKind.PassReaction;
            ulong source = kind is V05.ActionKind.EndTurn or V05.ActionKind.Surrender or
                V05.ActionKind.PassReaction ? 0UL : 800UL + (uint)kind;
            ulong? slot = kind switch
            {
                V05.ActionKind.PlayUnit or V05.ActionKind.PlayAmulet or V05.ActionKind.Deploy => 0,
                V05.ActionKind.CastSpell or V05.ActionKind.PlayTrap => 0,
                _ => null,
            };
            V05.Target? target = kind == V05.ActionKind.Attack
                ? V05.Target.Leader(V05.PlayerId.Player1)
                : null;
            V05.LegalAction action = ProductHotseatTestModel.Action(
                V05.PlayerId.Player0,
                kind,
                revision,
                source,
                target,
                slot);
            using var normal = new ProductHotseatMatchController(
                ProductHotseatTestModel.ActionSession(
                    revision,
                    [action],
                    isReaction ? V05.MatchPhase.Reaction : V05.MatchPhase.Action,
                    reaction: isReaction
                        ? ProductHotseatTestModel.Reaction(
                            revision, pending: true, responder: V05.PlayerId.Player0)
                        : null));
            normal.Reveal();
            normal.SelectLegalAction(action);
            Assert.AreSame(action, normal.State.SelectedAction, $"{kind} did not use its legal action.");
            Assert.AreEqual(
                isReaction ? ProductHotseatUiMode.Reaction : ProductHotseatUiMode.Action,
                normal.State.Mode);
            seen.Add(kind);
        }

        CollectionAssert.AreEquivalent(Enum.GetValues<V05.ActionKind>(), seen.ToArray());
    }

    [TestMethod]
    public void MalformedViewerAndHiddenEventIdentityFaultWithoutBecomingVisible()
    {
        V05.LegalAction endTurn = ProductHotseatTestModel.Action(
            V05.PlayerId.Player0, V05.ActionKind.EndTurn, 80);
        FakeProductGameSession wrongViewer = ProductHotseatTestModel.ActionSession(80, [endTurn]);
        wrongViewer.ViewHandler = _ => ProductHotseatTestModel.View(
            V05.PlayerId.Player1, 80);
        using (var controller = new ProductHotseatMatchController(wrongViewer))
        {
            Assert.ThrowsExactly<ScgsProtocolException>(controller.Reveal);
            Assert.AreEqual(ProductHotseatUiMode.Faulted, controller.State.Mode);
            Assert.IsNull(controller.State.Snapshot);
        }

        FakeProductGameSession hiddenLeak = ProductHotseatTestModel.ActionSession(81, [
            ProductHotseatTestModel.Action(V05.PlayerId.Player0, V05.ActionKind.EndTurn, 81),
        ]);
        hiddenLeak.EventsHandler = (viewer, _) => new V05.EventBatch(
            81,
            1,
            [new V05.GameEventView
            {
                Sequence = 1,
                Type = V05.EventType.CardDrawn,
                Player = viewer,
                Card = 123,
                DesignId = "PRIVATE-CARD",
                Value = 0,
                SecondaryValue = 0,
                HiddenCard = true,
                Text = "一张牌",
            }]);
        using var leaked = new ProductHotseatMatchController(hiddenLeak);
        Assert.ThrowsExactly<ScgsProtocolException>(leaked.Reveal);
        Assert.AreEqual(ProductHotseatUiMode.Faulted, leaked.State.Mode);
        Assert.IsEmpty(leaked.State.Events);
    }

    [TestMethod]
    public void DisposeIsIdempotentAndClosesEveryEntryPoint()
    {
        FakeProductGameSession session = ProductHotseatTestModel.ActionSession(90, [
            ProductHotseatTestModel.Action(V05.PlayerId.Player0, V05.ActionKind.EndTurn, 90),
        ]);
        var controller = new ProductHotseatMatchController(session);
        controller.Dispose();
        controller.Dispose();

        Assert.AreEqual(1, session.DisposeCalls);
        Assert.AreEqual(ProductHotseatUiMode.Disposed, controller.State.Mode);
        Assert.ThrowsExactly<ObjectDisposedException>(controller.Reveal);
        Assert.ThrowsExactly<ObjectDisposedException>(() => controller.NotifyPublicFrameDrawn(1));
    }

    private static V05.LegalAction ChoiceAction(
        V05.PendingChoiceKind kind,
        bool ordered,
        IReadOnlyList<string> optionIds,
        ulong revision)
    {
        _ = kind;
        _ = ordered;
        return ProductHotseatTestModel.Action(
            V05.PlayerId.Player0,
            V05.ActionKind.ResolveChoice,
            revision,
            choiceId: "choice-current",
            selectedOptionIds: optionIds);
    }

    private static ProductHotseatMatchController ChoiceController(
        ulong revision,
        V05.PendingChoiceKind kind,
        bool ordered,
        ulong minimum,
        ulong maximum,
        IReadOnlyList<string> optionIds,
        IReadOnlyList<V05.LegalAction> actions)
    {
        V05.PendingChoiceView choice = ProductHotseatTestModel.Choice(
            revision,
            V05.PlayerId.Player0,
            kind,
            ordered,
            minimum,
            maximum,
            optionIds);
        return new ProductHotseatMatchController(ProductHotseatTestModel.ActionSession(
            revision,
            actions,
            choice: choice));
    }

    private static void PresentTwoFrames(ProductHotseatMatchController controller)
    {
        Assert.IsFalse(controller.NotifyPublicFrameDrawn(1));
        Assert.IsTrue(controller.NotifyPublicFrameDrawn(2));
    }
}
