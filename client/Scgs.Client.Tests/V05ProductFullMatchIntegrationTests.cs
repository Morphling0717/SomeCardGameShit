// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scgs.Hotseat.Product;
using V05 = Scgs.Client.V05;

namespace Scgs.Client.Tests;

[TestClass]
[DoNotParallelize]
public sealed class V05ProductFullMatchIntegrationTests
{
    [TestMethod]
    [TestCategory("NativeIntegrationV05")]
    public void LockedProductDecksReachNaturalTerminalThroughViewerSafeApi()
    {
        string nativePath = GetNativeLibraryPath();
        var config = new V05.GameConfigRequest(
            "oathguard_luminous_oath_v1",
            "pactmage_abyssal_pact_v1")
        {
            RandomSeed = 0xA11CE,
            FirstPlayerMode = V05.FirstPlayerMode.Player0,
            ShuffleDecks = true,
        };

        using V05.ScgsV05GameSession session = V05.ScgsV05GameSession.Create(config, nativePath);
        Assert.IsTrue(session.Start().IsSuccess);

        ulong[] cursors = [0, 0];
        int submittedCommands = 0;
        var observedActions = new HashSet<V05.ActionKind>();
        while (submittedCommands < 2_000)
        {
            V05.MatchView publicView = session.GetView(V05.PlayerId.Player0);
            AssertViewerPrivacy(publicView);
            ReadAndValidateEvents(session, V05.PlayerId.Player0, cursors);
            ReadAndValidateEvents(session, V05.PlayerId.Player1, cursors);

            if (publicView.Result != V05.GameResult.Ongoing)
            {
                break;
            }

            V05.PlayerId actor = ProductHotseatMatchController.DetermineActor(publicView) ??
                throw new AssertFailedException("An ongoing product match has no command actor.");
            V05.MatchView actorView = session.GetView(actor);
            AssertViewerPrivacy(actorView);
            V05.LegalActionsResult legal = session.ListLegalActions(
                new V05.ActionQueryRequest(actor, actorView.Revision));
            V05.LegalAction selected = legal.Actions.FirstOrDefault(action =>
                    action.Command.Action != V05.ActionKind.Surrender) ??
                throw new AssertFailedException("The product match has no non-surrender continuation.");

            V05.PaymentResult payment = session.PreviewPayment(selected.Command);
            Assert.IsTrue(payment.Payment.Status.IsSuccess, payment.Payment.Status.Message);
            V05.EngineStatus status = session.SubmitCommand(selected.Command);
            Assert.IsTrue(status.IsSuccess, status.Message);
            observedActions.Add(selected.Command.Action);
            ++submittedCommands;
        }

        Assert.IsLessThan(2_000, submittedCommands, "The product agent did not terminate.");
        V05.MatchView finished0 = session.GetView(V05.PlayerId.Player0);
        V05.MatchView finished1 = session.GetView(V05.PlayerId.Player1);
        Assert.AreEqual(V05.MatchPhase.Finished, finished0.Phase);
        Assert.AreNotEqual(V05.GameResult.Ongoing, finished0.Result);
        Assert.AreEqual(finished0.Result, finished1.Result);
        Assert.IsTrue(observedActions.Contains(V05.ActionKind.Mulligan));
        Assert.IsTrue(observedActions.Contains(V05.ActionKind.EndTurn));

        ReadAndValidateEvents(session, V05.PlayerId.Player0, cursors);
        ReadAndValidateEvents(session, V05.PlayerId.Player1, cursors);
        V05.EventBatch allEvents = session.ReadEvents(V05.PlayerId.Player0, 0);
        Assert.AreEqual(V05.EventType.MatchEnded, allEvents.Events[^1].Type);
        Assert.HasCount(1, allEvents.Events.Where(gameEvent =>
            gameEvent.Type == V05.EventType.MatchEnded));
    }

    [TestMethod]
    [TestCategory("NativeIntegrationV05")]
    [DataRow(false)]
    [DataRow(true)]
    public void RealProductSearchChoiceCanExplicitlySkipOrSurrender(bool surrender)
    {
        (V05.ScgsV05GameSession native, ProductHotseatMatchController hotseat) = OpenRealProductSearchChoice();
        using V05.ScgsV05GameSession session = native;
        using ProductHotseatMatchController controller = hotseat;
        Assert.AreEqual(ProductHotseatUiMode.Choice, controller.State.Mode);
        Assert.AreEqual(0UL, controller.State.PendingChoice.MinimumSelections);
        Assert.AreEqual(1UL, controller.State.PendingChoice.MaximumSelections);
        Assert.IsNotEmpty(controller.State.PendingChoice.Options);
        Assert.IsNull(controller.State.SelectedAction);
        Assert.IsFalse(controller.PrepareSelectedCommand());
        ulong paidRevision = controller.State.Snapshot!.Revision;
        int paidPp = controller.State.Snapshot.Players[0].CurrentPp;

        if (surrender)
        {
            Assert.IsTrue(controller.PrepareSurrender());
            Assert.IsEmpty(controller.State.PendingChoice.Options);
            SubmitPrepared(controller);
            Assert.AreEqual(ProductHotseatUiMode.Finished, controller.State.Mode);
            Assert.AreEqual(V05.GameResult.Player1Won, controller.State.Snapshot!.Result);
            Assert.IsFalse(controller.State.Snapshot.PendingChoice.Pending);
            V05.EventBatch terminal = session.ReadEvents(V05.PlayerId.Player0, 0);
            Assert.AreEqual(V05.EventType.MatchEnded, terminal.Events[^1].Type);
            Assert.HasCount(1, terminal.Events.Where(item => item.Type == V05.EventType.MatchEnded));
        }
        else
        {
            int handCount = controller.State.Snapshot.Players[0].Hand.Length;
            controller.SkipPendingChoice();
            Assert.AreEqual(paidRevision, controller.State.Snapshot!.Revision);
            Assert.AreEqual(paidPp, controller.State.Snapshot.Players[0].CurrentPp);
            Assert.IsTrue(controller.State.PendingChoice.Pending);
            Assert.IsTrue(controller.State.CanPrepare);
            Assert.IsEmpty(controller.State.SelectedAction!.Command.SelectedOptionIds);
            SubmitCurrent(controller);
            Assert.AreEqual(ProductHotseatUiMode.Action, controller.State.Mode);
            Assert.AreEqual(paidRevision + 1, controller.State.Snapshot!.Revision);
            Assert.AreEqual(paidPp, controller.State.Snapshot.Players[0].CurrentPp);
            Assert.AreEqual(handCount, controller.State.Snapshot.Players[0].Hand.Length);
            Assert.IsFalse(controller.State.Snapshot.PendingChoice.Pending);
        }
    }

    [TestMethod]
    [TestCategory("NativeIntegrationV05")]
    [DataRow(V05.FirstPlayerMode.Player0, false)]
    [DataRow(V05.FirstPlayerMode.Player1, false)]
    [DataRow(V05.FirstPlayerMode.Player0, true)]
    [DataRow(V05.FirstPlayerMode.Player1, true)]
    public void LockedProductDecksReachNaturalTerminalThroughHotseatController(
        V05.FirstPlayerMode firstPlayer,
        bool enablePresentation)
    {
        var config = new V05.GameConfigRequest(
            "oathguard_luminous_oath_v1", "pactmage_abyssal_pact_v1")
        {
            RandomSeed = 0xA11CE,
            FirstPlayerMode = firstPlayer,
            ShuffleDecks = true,
        };
        using V05.ScgsV05GameSession session = V05.ScgsV05GameSession.Create(config, GetNativeLibraryPath());
        Assert.IsTrue(session.Start().IsSuccess);
        using var controller = new ProductHotseatMatchController(session, enablePresentation);
        int submissions = 0;
        int reveals = 0;
        int presentations = 0;
        ulong lastPresentationId = 0;
        ulong lastObservationSequence = 0;
        while (submissions < 2_000)
        {
            ProductHotseatUiState state = controller.State;
            switch (state.Mode)
            {
                case ProductHotseatUiMode.Covered:
                    Assert.IsNull(state.Snapshot);
                    Assert.IsNull(state.Viewer);
                    Assert.IsEmpty(state.LegalActions);
                    controller.Reveal();
                    ++reveals;
                    break;
                case ProductHotseatUiMode.MulliganSelecting:
                    SubmitCurrent(controller);
                    ++submissions;
                    break;
                case ProductHotseatUiMode.MulliganReview:
                    controller.CompleteMulliganReview();
                    break;
                case ProductHotseatUiMode.Action:
                case ProductHotseatUiMode.Reaction:
                case ProductHotseatUiMode.Choice:
                    AssertViewerPrivacy(state.Snapshot!);
                    V05.LegalAction action = state.LegalActions.First(item => item.Command.Action != V05.ActionKind.Surrender);
                    SelectProgressively(controller, action);
                    Assert.IsNotNull(controller.PreviewSelectedPayment());
                    SubmitCurrent(controller);
                    ++submissions;
                    break;
                case ProductHotseatUiMode.Finished:
                    Assert.AreNotEqual(V05.GameResult.Ongoing, state.Snapshot!.Result);
                    Assert.IsGreaterThan(1, reveals);
                    Assert.AreEqual(enablePresentation ? submissions : 0, presentations);
                    V05.EventBatch terminal = session.ReadEvents(state.Snapshot.Viewer, 0);
                    Assert.AreEqual(V05.EventType.MatchEnded, terminal.Events[^1].Type);
                    Assert.HasCount(1, terminal.Events.Where(item => item.Type == V05.EventType.MatchEnded));
                    return;
                case ProductHotseatUiMode.Presenting:
                    Assert.IsTrue(enablePresentation);
                    Assert.IsNull(state.Snapshot);
                    Assert.IsNull(state.Viewer);
                    Assert.IsEmpty(state.Events);
                    Assert.IsEmpty(state.PendingEvents);
                    Assert.IsEmpty(state.LegalActions);
                    ProductPresentationBatch presentation = state.Presentation!;
                    Assert.IsGreaterThan(lastPresentationId, presentation.Id);
                    Assert.AreEqual(presentation.PreviousRevision + 1, presentation.Revision);
                    ProductHotseatEventCursors cursors = state.EventCursors;
                    foreach (ProductPresentationObservation observation in presentation.Observations)
                    {
                        Assert.IsGreaterThan(lastObservationSequence, observation.Sequence);
                        Assert.IsTrue(observation.Observation.PublicToAll);
                        Assert.AreEqual(presentation.Revision, observation.Observation.Revision);
                        lastObservationSequence = observation.Sequence;
                    }

                    Assert.IsTrue(controller.CompletePresentation(presentation.Id, presentation.Revision));
                    Assert.AreEqual(cursors, controller.State.EventCursors);
                    lastPresentationId = presentation.Id;
                    ++presentations;
                    break;
                default:
                    Assert.Fail($"Product controller entered {state.Mode}: {state.FailureText}");
                    break;
            }

            if (controller.State.HasUnacknowledgedEvents)
            {
                Assert.IsTrue(controller.AcknowledgeEvents());
            }
        }

        Assert.Fail("The product hotseat controller did not reach a natural terminal within 2,000 submissions.");
    }

    private static (V05.ScgsV05GameSession, ProductHotseatMatchController) OpenRealProductSearchChoice()
    {
        // The product does not promise std::shuffle parity across standard
        // libraries. Find an actual reachable opening from a bounded seed set,
        // using only the fixed decks and the controller's explicit reveal path.
        string nativePath = GetNativeLibraryPath();
        for (uint seed = 0; seed < 64; ++seed)
        {
            var config = new V05.GameConfigRequest(
                "oathguard_luminous_oath_v1", "pactmage_abyssal_pact_v1")
            {
                RandomSeed = seed,
                FirstPlayerMode = V05.FirstPlayerMode.Player0,
                ShuffleDecks = true,
            };
            V05.ScgsV05GameSession session = V05.ScgsV05GameSession.Create(config, nativePath);
            var controller = new ProductHotseatMatchController(session);
            try
            {
                Assert.IsTrue(session.Start().IsSuccess);
                ReachInitialAction(controller);
                V05.CardView? searcher = controller.State.Snapshot!.Players[0].Hand
                    .FirstOrDefault(card => card.DesignId == "LO-01");
                if (searcher is not null)
                {
                    V05.LegalAction play = controller.State.LegalActions.First(action =>
                        action.Command.Source == searcher.InstanceId);
                    SelectProgressively(controller, play);
                    SubmitCurrent(controller);
                    if (controller.State.Mode == ProductHotseatUiMode.Choice &&
                        controller.State.PendingChoice.MinimumSelections == 0 &&
                        controller.State.PendingChoice.MaximumSelections == 1 &&
                        controller.State.PendingChoice.Options.Count != 0)
                    {
                        return (session, controller);
                    }
                }
            }
            catch
            {
                controller.Dispose();
                throw;
            }
            controller.Dispose();
        }

        throw new AssertFailedException("No real optional LO-01 search was reached within 64 opening seeds.");
    }

    private static void ReachInitialAction(ProductHotseatMatchController controller)
    {
        for (int transition = 0; transition < 12; ++transition)
        {
            switch (controller.State.Mode)
            {
                case ProductHotseatUiMode.Covered:
                    controller.Reveal();
                    break;
                case ProductHotseatUiMode.MulliganSelecting:
                    SubmitCurrent(controller);
                    break;
                case ProductHotseatUiMode.MulliganReview:
                    controller.CompleteMulliganReview();
                    break;
                case ProductHotseatUiMode.Action:
                    return;
                default:
                    Assert.Fail($"Unexpected opening product state {controller.State.Mode}.");
                    break;
            }
        }

        Assert.Fail("The real product controller did not complete both opening mulligans.");
    }

    private static void SelectProgressively(ProductHotseatMatchController controller, V05.LegalAction action)
    {
        V05.GameCommandRequest command = action.Command;
        if (command.Action == V05.ActionKind.ResolveChoice)
        {
            if (command.SelectedOptionIds.Count == 0)
            {
                controller.SkipPendingChoice();
            }
            else
            {
                controller.SelectPendingChoiceOptions(command.SelectedOptionIds);
            }
            return;
        }

        if (command.Source == 0)
        {
            controller.BeginActionSelection(command.Action);
        }
        else
        {
            controller.BeginSourceSelection(command.Source);
        }
        for (int step = 0; step < 10 && !controller.State.CanPrepare; ++step)
        {
            switch (controller.State.Interaction.Step)
            {
                case ProductHotseatSelectionStep.ChooseAction:
                    controller.ChooseAction(command.Action);
                    break;
                case ProductHotseatSelectionStep.ChooseMode:
                    controller.SelectMode(command.ModeId!);
                    break;
                case ProductHotseatSelectionStep.ChooseAdditionalCost:
                    controller.SelectAdditionalCostCards(command.AdditionalCostCards);
                    break;
                case ProductHotseatSelectionStep.ChooseSlot:
                    controller.SelectSlot(command.Slot!.Value);
                    break;
                case ProductHotseatSelectionStep.ChooseTarget:
                    if (command.Target is null) controller.SkipOptionalTarget();
                    else controller.SelectTarget(command.Target);
                    break;
                case ProductHotseatSelectionStep.ChooseAdvance:
                    controller.SelectAdvance(command.UseAdvance);
                    break;
                default:
                    Assert.Fail($"Unable to select {command.Action}: {controller.State.Interaction.Step}.");
                    break;
            }
        }
        Assert.IsTrue(controller.State.CanPrepare);
        Assert.IsTrue(ProductHotseatTestModel.CommandsEqual(command, controller.State.SelectedAction!.Command));
    }

    private static void SubmitCurrent(ProductHotseatMatchController controller)
    {
        Assert.IsTrue(controller.PrepareSelectedCommand());
        SubmitPrepared(controller);
    }

    private static void SubmitPrepared(ProductHotseatMatchController controller)
    {
        Assert.AreEqual(ProductHotseatUiMode.Resolving, controller.State.Mode);
        Assert.IsNull(controller.State.Viewer);
        Assert.IsNull(controller.State.Snapshot);
        Assert.IsEmpty(controller.State.LegalActions);
        Assert.IsEmpty(controller.State.Events);
        Assert.IsFalse(controller.NotifyPublicFrameDrawn(1));
        Assert.IsTrue(controller.NotifyPublicFrameDrawn(2));
        V05.EngineStatus status = controller.SubmitPreparedCommand();
        Assert.IsTrue(status.IsSuccess, status.Message);
    }

    private static void ReadAndValidateEvents(
        V05.IScgsV05GameSession session,
        V05.PlayerId viewer,
        ulong[] cursors)
    {
        int index = (int)viewer;
        V05.EventBatch batch = session.ReadEvents(viewer, cursors[index]);
        ulong previous = cursors[index];
        foreach (V05.GameEventView gameEvent in batch.Events)
        {
            Assert.IsGreaterThan(previous, gameEvent.Sequence);
            if (gameEvent.HiddenCard)
            {
                Assert.IsNull(gameEvent.Card);
                Assert.IsNull(gameEvent.DesignId);
                Assert.IsFalse(gameEvent.Text.Contains("LO-", StringComparison.Ordinal));
                Assert.IsFalse(gameEvent.Text.Contains("AP-", StringComparison.Ordinal));
                Assert.IsFalse(gameEvent.Text.Contains("NT-", StringComparison.Ordinal));
            }
            previous = gameEvent.Sequence;
        }
        Assert.AreEqual(previous, batch.LastSequence);
        cursors[index] = batch.LastSequence;
    }

    private static void AssertViewerPrivacy(V05.MatchView view)
    {
        V05.PlayerView opponent = view.Players.Single(player => player.Player != view.Viewer);
        Assert.IsEmpty(opponent.Hand);
        Assert.IsTrue(opponent.HandCount >= (ulong)opponent.Hand.Length);
        foreach (V05.CardView tactic in opponent.Tactics.OfType<V05.CardView>())
        {
            if (tactic.FaceDown)
            {
                Assert.IsNull(tactic.InstanceId);
                Assert.IsNull(tactic.DesignId);
                Assert.IsNull(tactic.ProfessionId);
                Assert.IsNull(tactic.SeriesId);
                Assert.IsNull(tactic.Neutral);
                Assert.IsNull(tactic.Kind);
                Assert.AreEqual(string.Empty, tactic.Name);
                Assert.AreEqual(opponent.Player, tactic.Owner);
                Assert.AreEqual(opponent.Player, tactic.Controller);
                Assert.AreEqual(V05.Zone.Tactic, tactic.Zone);
                Assert.AreEqual(0UL, tactic.Sequence);
                Assert.AreEqual(0, tactic.Cost);
                Assert.AreEqual(0, tactic.CurrentAttack);
                Assert.AreEqual(0, tactic.CurrentHealth);
                Assert.AreEqual(0, tactic.MaximumHealth);
                Assert.AreEqual(V05.Keyword.None, tactic.PrintedKeywords);
                Assert.AreEqual(V05.Keyword.None, tactic.PermanentKeywords);
                Assert.AreEqual(V05.Keyword.None, tactic.TurnKeywords);
                Assert.AreEqual(V05.Keyword.None, tactic.Keywords);
                Assert.IsFalse(tactic.Evolved);
                Assert.IsFalse(tactic.AttackedThisTurn);
                Assert.IsFalse(tactic.EnteredThisTurn);
                Assert.AreEqual(0, tactic.Countdown);
            }
        }
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

        Assert.Inconclusive("Set SCGS_NATIVE_V05_LIBRARY to run the product full-match test.");
        throw new InvalidOperationException("MSTest did not terminate an inconclusive test.");
    }
}
