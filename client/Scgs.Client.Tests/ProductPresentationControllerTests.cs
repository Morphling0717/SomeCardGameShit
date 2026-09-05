// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scgs.Hotseat.Product;
using V05 = Scgs.Client.V05;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class ProductPresentationControllerTests
{
    [TestMethod]
    public void OptInPresentationPreservesTwoPublicFramesAndLocksEveryInput()
    {
        var model = new PresentationModel();
        using var controller = new ProductHotseatMatchController(model.Session, enablePresentation: true);
        controller.Reveal();
        controller.SelectLegalAction(controller.State.LegalActions.Single());
        Assert.IsTrue(controller.PrepareSelectedCommand());
        int calls = model.Session.Calls.Count;
        Assert.ThrowsExactly<InvalidOperationException>(() => controller.SubmitPreparedCommand());
        Assert.IsFalse(controller.NotifyPublicFrameDrawn(8));
        Assert.IsFalse(controller.NotifyPublicFrameDrawn(8));
        Assert.ThrowsExactly<InvalidOperationException>(() => controller.SubmitPreparedCommand());
        Assert.AreEqual(calls, model.Session.Calls.Count);
        Assert.IsTrue(controller.NotifyPublicFrameDrawn(9));
        Assert.IsTrue(controller.SubmitPreparedCommand().IsSuccess);

        AssertPublicPresentation(controller.State);
        calls = model.Session.Calls.Count;
        Assert.ThrowsExactly<InvalidOperationException>(() => controller.AcknowledgeEvents());
        Assert.ThrowsExactly<InvalidOperationException>(() => controller.Reveal());
        Assert.ThrowsExactly<InvalidOperationException>(() => controller.BeginSourceSelection(901));
        Assert.ThrowsExactly<InvalidOperationException>(() => controller.BeginActionSelection(V05.ActionKind.EndTurn));
        Assert.ThrowsExactly<InvalidOperationException>(() => controller.SubmitPreparedCommand());
        Assert.ThrowsExactly<InvalidOperationException>(() => controller.NotifyPublicFrameDrawn(10));
        Assert.IsFalse(controller.PrepareSelectedCommand());
        Assert.IsFalse(controller.PrepareSurrender());
        Assert.AreEqual(calls, model.Session.Calls.Count);
        Assert.HasCount(1, model.Session.SubmittedCommands);
    }

    [TestMethod]
    public void PresentationContainsOnlyOccurrenceTimePublicFactsAndNeutralBoards()
    {
        var model = new PresentationModel();
        using var controller = new ProductHotseatMatchController(model.Session, enablePresentation: true);
        controller.Reveal();
        Submit(controller);

        ProductPresentationBatch presentation = controller.State.Presentation!;
        Assert.AreEqual(10UL, presentation.PreviousRevision);
        Assert.AreEqual(11UL, presentation.Revision);
        Assert.AreEqual(11UL, controller.State.Interaction.Revision);
        Assert.AreEqual(V05.PlayerId.Player0, presentation.PerspectivePlayer);
        Assert.HasCount(1, presentation.Observations);
        Assert.AreEqual(2UL, presentation.Observations[0].Sequence);
        Assert.AreEqual("declaration", presentation.Observations[0].Observation.Kind);
        string publicJson = JsonSerializer.Serialize(presentation);
        Assert.DoesNotContain("PRIVATE-IDENTITY", publicJson);
        Assert.DoesNotContain("PRIVATE-EVENT-TEXT", publicJson);
        Assert.DoesNotContain("7001", publicJson);
        Assert.DoesNotContain("7002", publicJson);
        foreach (ProductHotseatPublicBoardView board in new[] { presentation.Before, presentation.After })
        {
            Assert.IsNull(board.Players[0].Tactics[0]!.InstanceId);
            Assert.IsNull(board.Players[0].Tactics[0]!.DesignId);
            Assert.AreEqual(string.Empty, board.Players[0].Tactics[0]!.Name);
            Assert.AreEqual(0UL, board.Players[0].Tactics[0]!.Sequence);
        }

        Assert.AreEqual(new ProductHotseatEventCursors(0, 0), controller.State.EventCursors);
    }

    [TestMethod]
    public void PresentationCompletionRestoresSameViewerWithoutAcknowledgingPrivateEvents()
    {
        var model = new PresentationModel();
        using var controller = new ProductHotseatMatchController(model.Session, enablePresentation: true);
        controller.Reveal();
        Submit(controller);
        ProductPresentationBatch presentation = controller.State.Presentation!;
        Assert.IsTrue(controller.CompletePresentation(presentation.Id, presentation.Revision));
        Assert.AreEqual(ProductHotseatUiMode.Action, controller.State.Mode);
        Assert.AreEqual(V05.PlayerId.Player0, controller.State.Viewer);
        Assert.IsNull(controller.State.Presentation);
        Assert.AreEqual(new ProductHotseatEventCursors(0, 0), controller.State.EventCursors);
        Assert.HasCount(3, controller.State.PendingEvents);
        Assert.IsTrue(controller.State.PendingEvents.Any(item => item.Text == "PRIVATE-EVENT-TEXT"));
        Assert.IsTrue(controller.AcknowledgeEvents());
        Assert.AreEqual(new ProductHotseatEventCursors(3, 0), controller.State.EventCursors);
    }

    [TestMethod]
    public void ChangingActorNeverReadsNextViewerUntilOpaqueCoverIsExplicitlyRevealed()
    {
        var model = new PresentationModel { NextActor = V05.PlayerId.Player1 };
        using var controller = new ProductHotseatMatchController(model.Session, enablePresentation: true);
        controller.Reveal();
        Submit(controller);
        AssertPublicPresentation(controller.State);
        ProductPresentationBatch presentation = controller.State.Presentation!;
        Assert.AreEqual(V05.PlayerId.Player0, presentation.PerspectivePlayer);
        int calls = model.Session.Calls.Count;
        Assert.IsTrue(controller.CompletePresentation(presentation.Id, presentation.Revision));
        Assert.AreEqual(calls, model.Session.Calls.Count);
        Assert.AreEqual(ProductHotseatUiMode.Covered, controller.State.Mode);
        Assert.AreEqual(V05.PlayerId.Player1, controller.State.AwaitingPlayer);
        Assert.IsFalse(model.Session.Calls.Any(item => item.Contains("Player1", StringComparison.Ordinal)));
        Assert.AreEqual(new ProductHotseatEventCursors(0, 0), controller.State.EventCursors);
        controller.Reveal();
        Assert.AreEqual(V05.PlayerId.Player1, controller.State.Viewer);
        Assert.IsTrue(model.Session.Calls.Contains("view:Player1", StringComparer.Ordinal));
        Assert.AreEqual(0UL, controller.State.EventCursors.Player0);
    }

    [TestMethod]
    public void StaleOrDuplicateCompletionIsReadFreeAndCannotCompleteAnotherBatch()
    {
        var model = new PresentationModel();
        using var controller = new ProductHotseatMatchController(model.Session, enablePresentation: true);
        controller.Reveal();
        Submit(controller);
        ProductPresentationBatch first = controller.State.Presentation!;
        int calls = model.Session.Calls.Count;
        Assert.IsFalse(controller.CompletePresentation(first.Id + 1, first.Revision));
        Assert.IsFalse(controller.CompletePresentation(first.Id, first.Revision - 1));
        Assert.AreEqual(calls, model.Session.Calls.Count);
        Assert.IsTrue(controller.CompletePresentation(first.Id, first.Revision));
        calls = model.Session.Calls.Count;
        Assert.IsFalse(controller.CompletePresentation(first.Id, first.Revision));
        Assert.AreEqual(calls, model.Session.Calls.Count);
        Submit(controller);
        ProductPresentationBatch second = controller.State.Presentation!;
        Assert.IsTrue(second.Id > first.Id);
        Assert.AreEqual(first.Revision + 1, second.Revision);
        CollectionAssert.AreEqual(new ulong[] { 4 }, second.Observations.Select(item => item.Sequence).ToArray());
        calls = model.Session.Calls.Count;
        Assert.IsFalse(controller.CompletePresentation(first.Id, first.Revision));
        Assert.AreEqual(calls, model.Session.Calls.Count);
        Assert.AreEqual(second.Id, controller.State.Presentation!.Id);
        Assert.AreEqual(new ProductHotseatEventCursors(0, 0), controller.State.EventCursors);
    }

    [TestMethod]
    public void RejectedCommandDoesNotReadPresentOrAdvanceCursors()
    {
        var model = new PresentationModel { Reject = true };
        using var controller = new ProductHotseatMatchController(model.Session, enablePresentation: true);
        controller.Reveal();
        controller.SelectLegalAction(controller.State.LegalActions.Single());
        Assert.IsTrue(controller.PrepareSelectedCommand());
        controller.NotifyPublicFrameDrawn(1);
        controller.NotifyPublicFrameDrawn(2);
        int calls = model.Session.Calls.Count;
        Assert.IsFalse(controller.SubmitPreparedCommand().IsSuccess);
        Assert.AreEqual(calls + 1, model.Session.Calls.Count);
        Assert.AreEqual(ProductHotseatUiMode.Covered, controller.State.Mode);
        Assert.AreEqual(ProductHotseatCoverReason.FailedCommand, controller.State.CoverReason);
        Assert.IsNull(controller.State.Presentation);
        Assert.AreEqual(new ProductHotseatEventCursors(0, 0), controller.State.EventCursors);
    }

    [TestMethod]
    public void UnknownOrPrivateObservationsDoNotBecomeGuessedAnimations()
    {
        var model = new PresentationModel { ObservationKind = "future_visual_fact" };
        using var controller = new ProductHotseatMatchController(model.Session, enablePresentation: true);
        controller.Reveal();
        Submit(controller);
        AssertPublicPresentation(controller.State);
        Assert.IsEmpty(controller.State.Presentation!.Observations);
    }

    [TestMethod]
    public void CorruptObservationRevisionFaultsWithoutExposingPartialPresentation()
    {
        var model = new PresentationModel { CorruptObservationRevision = true };
        using var controller = new ProductHotseatMatchController(model.Session, enablePresentation: true);
        controller.Reveal();
        Assert.ThrowsExactly<ScgsProtocolException>(() => Submit(controller));
        Assert.AreEqual(ProductHotseatUiMode.Faulted, controller.State.Mode);
        Assert.IsNull(controller.State.Presentation);
        Assert.IsNull(controller.State.PublicBoard);
        Assert.IsNull(controller.State.Snapshot);
        Assert.AreEqual(new ProductHotseatEventCursors(0, 0), controller.State.EventCursors);
    }

    [TestMethod]
    public void FinishedMatchIsPresentedBeforeResultAndDisposedCallbackIsHarmless()
    {
        var model = new PresentationModel { Finish = true };
        var controller = new ProductHotseatMatchController(model.Session, enablePresentation: true);
        controller.Reveal();
        Submit(controller);
        ProductPresentationBatch presentation = controller.State.Presentation!;
        Assert.AreEqual(V05.GameResult.Player0Won, presentation.After.Result);
        Assert.AreEqual(ProductHotseatUiMode.Presenting, controller.State.Mode);
        Assert.IsTrue(controller.CompletePresentation(presentation.Id, presentation.Revision));
        Assert.AreEqual(ProductHotseatUiMode.Finished, controller.State.Mode);
        controller.Dispose();
        int calls = model.Session.Calls.Count;
        Assert.IsFalse(controller.CompletePresentation(presentation.Id, presentation.Revision));
        controller.Dispose();
        Assert.AreEqual(calls, model.Session.Calls.Count);
        Assert.AreEqual(1, model.Session.DisposeCalls);
        Assert.IsNull(controller.State.Presentation);
    }

    [TestMethod]
    public void DisposingDuringPresentationDropsEveryPublicAndPrivatePayload()
    {
        var model = new PresentationModel();
        var controller = new ProductHotseatMatchController(model.Session, enablePresentation: true);
        controller.Reveal();
        Submit(controller);
        ProductPresentationBatch presentation = controller.State.Presentation!;
        controller.Dispose();
        Assert.AreEqual(ProductHotseatUiMode.Disposed, controller.State.Mode);
        Assert.IsNull(controller.State.Presentation);
        Assert.IsNull(controller.State.PublicBoard);
        int calls = model.Session.Calls.Count;
        Assert.IsFalse(controller.CompletePresentation(presentation.Id, presentation.Revision));
        Assert.AreEqual(calls, model.Session.Calls.Count);
    }

    [TestMethod]
    public void MulliganPresentationCompletesIntoOriginalPlayersPrivateReviewBeforePassingDevice()
    {
        var model = new PresentationModel { CommandAction = V05.ActionKind.Mulligan };
        using var controller = new ProductHotseatMatchController(model.Session, enablePresentation: true);
        controller.Reveal();
        Assert.AreEqual(ProductHotseatUiMode.MulliganSelecting, controller.State.Mode);
        Submit(controller);
        AssertPublicPresentation(controller.State);
        ProductPresentationBatch batch = controller.State.Presentation!;
        Assert.IsTrue(controller.CompletePresentation(batch.Id, batch.Revision));
        Assert.AreEqual(ProductHotseatUiMode.MulliganReview, controller.State.Mode);
        Assert.AreEqual(V05.PlayerId.Player0, controller.State.Viewer);
        Assert.HasCount(1, controller.State.Snapshot!.Players[0].Hand);
        Assert.AreEqual(0UL, controller.State.EventCursors.Player0);
        controller.CompleteMulliganReview();
        Assert.AreEqual(ProductHotseatUiMode.Covered, controller.State.Mode);
        Assert.AreEqual(V05.PlayerId.Player1, controller.State.AwaitingPlayer);
        Assert.IsFalse(model.Session.Calls.Any(item => item.Contains("Player1", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ReactionAndPrivateChoiceBoundariesWaitForNextViewerReveal()
    {
        foreach (bool choice in new[] { false, true })
        {
            var model = new PresentationModel
            {
                AfterReaction = !choice,
                AfterChoice = choice,
                NextActor = V05.PlayerId.Player1,
            };
            using var controller = new ProductHotseatMatchController(model.Session, enablePresentation: true);
            controller.Reveal();
            Submit(controller);
            ProductPresentationBatch batch = controller.State.Presentation!;
            Assert.AreEqual(choice, batch.After.ChoicePending);
            Assert.AreEqual(!choice, batch.After.ReactionPending);
            string json = JsonSerializer.Serialize(batch);
            Assert.DoesNotContain("PRIVATE-CHOICE", json);
            Assert.IsTrue(controller.CompletePresentation(batch.Id, batch.Revision));
            Assert.AreEqual(ProductHotseatUiMode.Covered, controller.State.Mode);
            Assert.AreEqual(V05.PlayerId.Player1, controller.State.AwaitingPlayer);
            Assert.IsFalse(model.Session.Calls.Any(item => item.Contains("Player1", StringComparison.Ordinal)));
        }
    }

    private static void Submit(ProductHotseatMatchController controller)
    {
        if (controller.State.Mode != ProductHotseatUiMode.MulliganSelecting)
        {
            controller.SelectLegalAction(controller.State.LegalActions.Single());
        }

        Assert.IsTrue(controller.PrepareSelectedCommand());
        controller.NotifyPublicFrameDrawn(1);
        controller.NotifyPublicFrameDrawn(2);
        Assert.IsTrue(controller.SubmitPreparedCommand().IsSuccess);
    }

    private static void AssertPublicPresentation(ProductHotseatUiState state)
    {
        Assert.AreEqual(ProductHotseatUiMode.Presenting, state.Mode);
        Assert.IsNull(state.Snapshot);
        Assert.IsNull(state.Viewer);
        Assert.IsNull(state.AwaitingPlayer);
        Assert.IsNotNull(state.PublicBoard);
        Assert.IsNotNull(state.Presentation);
        Assert.IsEmpty(state.LegalActions);
        Assert.IsEmpty(state.Events);
        Assert.IsEmpty(state.PendingEvents);
        Assert.IsEmpty(state.Interaction.Options.Actions);
        Assert.IsFalse(state.CommandPrepared);
        Assert.IsFalse(state.HasUnacknowledgedEvents);
    }

    private sealed class PresentationModel
    {
        private ulong revision = 10;
        internal V05.PlayerId NextActor { get; init; } = V05.PlayerId.Player0;
        internal bool Reject { get; init; }
        internal bool Finish { get; init; }
        internal bool CorruptObservationRevision { get; init; }
        internal V05.ActionKind CommandAction { get; init; } = V05.ActionKind.EndTurn;
        internal bool AfterReaction { get; init; }
        internal bool AfterChoice { get; init; }
        internal string ObservationKind { get; init; } = "declaration";
        internal FakeProductGameSession Session { get; }

        internal PresentationModel()
        {
            Session = new FakeProductGameSession
            {
                ViewHandler = viewer => ProductHotseatTestModel.View(
                    viewer,
                    revision,
                    Finish && revision > 10 ? V05.MatchPhase.Finished :
                        CommandAction == V05.ActionKind.Mulligan ? V05.MatchPhase.Mulligan :
                        AfterReaction && revision > 10 ? V05.MatchPhase.Reaction : V05.MatchPhase.Action,
                    revision == 10 ? V05.PlayerId.Player0 : NextActor,
                    player0MulliganDone: CommandAction != V05.ActionKind.Mulligan || revision > 10,
                    player1MulliganDone: CommandAction != V05.ActionKind.Mulligan,
                    ownHand: [7001],
                    result: Finish && revision > 10 ? V05.GameResult.Player0Won : V05.GameResult.Ongoing,
                    reaction: AfterReaction && revision > 10 ? ProductHotseatTestModel.Reaction(
                        revision, pending: true, responder: NextActor) : null,
                    choice: AfterChoice && revision > 10 ? ProductHotseatTestModel.Choice(
                        revision, NextActor, V05.PendingChoiceKind.Cards, false, 1, 1,
                        ["PRIVATE-CHOICE"], redacted: viewer != NextActor) : null,
                    player0MainBoard: [ProductHotseatTestModel.Card(901, V05.PlayerId.Player0, V05.Zone.MainBoard), null, null, null, null],
                    player0Tactics: [ProductHotseatTestModel.Card(7002, V05.PlayerId.Player0, V05.Zone.Tactic,
                        name: "PRIVATE-IDENTITY", faceDown: true, kind: V05.CardKind.Trap), null, null]),
                ActionsHandler = query => ProductHotseatTestModel.Filter(query, revision,
                    [ProductHotseatTestModel.Action(query.Player, CommandAction, revision)]),
                SubmitHandler = _ =>
                {
                    if (Reject) return ProductHotseatTestModel.Status(V05.EngineCode.StaleRevision);
                    ++revision;
                    return ProductHotseatTestModel.Status(V05.EngineCode.Ok);
                },
                EventsHandler = (viewer, after) =>
                {
                    ulong last = 1 + (revision - 10) * 2;
                    var events = new List<V05.GameEventView>();
                    for (ulong sequence = after + 1; sequence <= last; ++sequence)
                    {
                        bool publicFact = sequence % 2 == 0;
                        events.Add(new V05.GameEventView
                        {
                            Sequence = sequence,
                            Type = publicFact ? V05.EventType.TurnEnded : V05.EventType.CardDrawn,
                            Player = viewer,
                            Card = publicFact ? 901UL : 7001UL,
                            DesignId = publicFact ? "LO-11" : "PRIVATE-IDENTITY",
                            Value = 0,
                            SecondaryValue = 0,
                            HiddenCard = false,
                            Text = "PRIVATE-EVENT-TEXT",
                            Observation = new V05.ProductEventObservation
                            {
                                Version = 1,
                                Revision = CorruptObservationRevision ? revision + 1 : 10 + sequence / 2,
                                CauseSequence = sequence,
                                Kind = ObservationKind,
                                PublicToAll = publicFact,
                                Source = new V05.EventObservationEndpoint
                                {
                                    Kind = "card",
                                    Player = viewer,
                                    Hidden = false,
                                    Card = publicFact ? 901UL : 7001UL,
                                    DesignId = publicFact ? "LO-11" : "PRIVATE-IDENTITY",
                                },
                            },
                        });
                    }

                    return new V05.EventBatch(revision, last, events);
                },
            };
        }
    }
}
