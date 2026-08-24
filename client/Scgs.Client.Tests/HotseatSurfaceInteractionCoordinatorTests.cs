// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scgs.Hotseat;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class HotseatSurfaceInteractionCoordinatorTests
{
    [TestMethod]
    public void FirstSourceClickMapsEveryCardSurfaceWithoutPreparing()
    {
        var cases = new[]
        {
            new SourceCase(
                ActionKind.PlayUnit,
                101,
                HotseatSurfaceRef.HandCard(PlayerId.Player0, 0, 101),
                Slot: 2),
            new SourceCase(
                ActionKind.CastSpell,
                102,
                HotseatSurfaceRef.HandCard(PlayerId.Player0, 1, 102),
                Target: Target.Leader(PlayerId.Player1),
                Slot: 1),
            new SourceCase(
                ActionKind.PlayTactic,
                103,
                HotseatSurfaceRef.HandCard(PlayerId.Player0, 2, 103),
                Slot: 1),
            new SourceCase(
                ActionKind.Attack,
                201,
                HotseatSurfaceRef.Unit(PlayerId.Player0, 0, 201),
                Target: Target.Leader(PlayerId.Player1)),
            new SourceCase(
                ActionKind.Evolve,
                201,
                HotseatSurfaceRef.Unit(PlayerId.Player0, 0, 201)),
            new SourceCase(
                ActionKind.Deploy,
                301,
                HotseatSurfaceRef.StandbyCard(PlayerId.Player0, 0, 301),
                Slot: 0),
        };

        foreach (SourceCase item in cases)
        {
            LegalAction action = HotseatTestModel.Action(
                PlayerId.Player0,
                item.Action,
                70,
                item.Source,
                item.Target,
                item.Slot);
            using SurfaceFixture fixture = CreateActionFixture(70, [action]);

            HotseatSurfaceIntentResult result = fixture.Coordinator.ApplyIntent(new(
                70,
                HotseatSurfaceGesture.Click,
                item.Surface));

            Assert.AreEqual(HotseatSurfaceIntentStatus.Applied, result.Status, item.Action.ToString());
            Assert.AreEqual(item.Source, fixture.Controller.State.Interaction.Source);
            Assert.AreEqual(item.Action, fixture.Controller.State.Interaction.Action);
            Assert.IsFalse(result.CommandPrepared);
            Assert.IsFalse(fixture.Controller.State.CommandPrepared);
            Assert.AreEqual(HotseatUiMode.Action, fixture.Controller.State.Mode);
        }
    }

    [TestMethod]
    public void ReactionTacticClickMapsActivateTrapWithoutPreparing()
    {
        Target target = Target.UnitTarget(PlayerId.Player1, 501);
        LegalAction trap = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.ActivateTrap,
            71,
            source: 401,
            target: target);
        using SurfaceFixture fixture = CreateActionFixture(71, [trap], MatchPhase.Reaction);

        HotseatSurfaceIntentResult result = fixture.Coordinator.ApplyIntent(new(
            71,
            HotseatSurfaceGesture.Click,
            HotseatSurfaceRef.Tactic(PlayerId.Player0, 0, 401)));

        Assert.AreEqual(HotseatSurfaceIntentStatus.Applied, result.Status);
        Assert.AreEqual(HotseatSelectionStep.ChooseTarget, result.NextStep);
        Assert.IsTrue(result.RequiresFurtherSelection);
        Assert.IsFalse(result.CommandPrepared);
    }

    [TestMethod]
    public void ClickSourceThenExactUnitSlotImmediatelyPreparesCanonicalCommand()
    {
        LegalAction play = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.PlayUnit,
            72,
            source: 101,
            slot: 3);
        using SurfaceFixture fixture = CreateActionFixture(72, [play]);

        HotseatSurfaceIntentResult source = fixture.Coordinator.ApplyIntent(new(
            72,
            HotseatSurfaceGesture.Click,
            HotseatSurfaceRef.HandCard(PlayerId.Player0, 0, 101)));
        HotseatSurfaceIntentResult slot = fixture.Coordinator.ApplyIntent(new(
            72,
            HotseatSurfaceGesture.Click,
            HotseatSurfaceRef.UnitSlot(PlayerId.Player0, 3)));

        Assert.AreEqual(HotseatSurfaceIntentStatus.Applied, source.Status);
        Assert.AreEqual(HotseatSelectionStep.ChooseSlot, source.NextStep);
        Assert.AreEqual(HotseatSurfaceIntentStatus.CommandPrepared, slot.Status);
        Assert.AreEqual(HotseatSelectionStep.Ready, slot.NextStep);
        Assert.AreEqual(3UL, slot.CanonicalCommand!.Slot);
        Assert.AreEqual(HotseatUiMode.Resolving, fixture.Controller.State.Mode);
    }

    [TestMethod]
    public void ClickAndDragToSlotConvergeOnIdenticalCanonicalCommand()
    {
        LegalAction play = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.PlayTactic,
            73,
            source: 103,
            slot: 2);
        using SurfaceFixture clickFixture = CreateActionFixture(73, [play]);
        using SurfaceFixture dragFixture = CreateActionFixture(73, [play]);
        HotseatSurfaceRef card = HotseatSurfaceRef.HandCard(PlayerId.Player0, 2, 103);
        HotseatSurfaceRef slot = HotseatSurfaceRef.TacticSlot(PlayerId.Player0, 2);

        _ = clickFixture.Coordinator.ApplyIntent(new(73, HotseatSurfaceGesture.Click, card));
        HotseatSurfaceIntentResult clicked = clickFixture.Coordinator.ApplyIntent(new(
            73,
            HotseatSurfaceGesture.Click,
            slot));
        HotseatSurfaceIntentResult dragged = dragFixture.Coordinator.ApplyIntent(new(
            73,
            HotseatSurfaceGesture.Drag,
            card)
        {
            Destination = slot,
        });

        Assert.AreEqual(HotseatSurfaceIntentStatus.CommandPrepared, clicked.Status);
        Assert.AreEqual(HotseatSurfaceIntentStatus.CommandPrepared, dragged.Status);
        Assert.AreEqual(clicked.CanonicalCommand, dragged.CanonicalCommand);
    }

    [TestMethod]
    public void LeaderAndUnitTargetsMapToExactTargetAndPrepareImmediately()
    {
        LegalAction leaderAttack = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.Attack,
            74,
            source: 201,
            target: Target.Leader(PlayerId.Player1));
        using (SurfaceFixture fixture = CreateActionFixture(74, [leaderAttack]))
        {
            _ = fixture.Coordinator.ApplyIntent(new(
                74,
                HotseatSurfaceGesture.Click,
                HotseatSurfaceRef.Unit(PlayerId.Player0, 0, 201)));
            HotseatSurfaceIntentResult result = fixture.Coordinator.ApplyIntent(new(
                74,
                HotseatSurfaceGesture.Click,
                HotseatSurfaceRef.Leader(PlayerId.Player1)));

            Assert.AreEqual(HotseatSurfaceIntentStatus.CommandPrepared, result.Status);
            Assert.AreEqual(Target.Leader(PlayerId.Player1), result.CanonicalCommand!.Target);
        }

        Target enemyUnit = Target.UnitTarget(PlayerId.Player1, 501);
        LegalAction targetedSpell = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.CastSpell,
            75,
            source: 102,
            target: enemyUnit,
            slot: 1);
        using (SurfaceFixture fixture = CreateActionFixture(75, [targetedSpell]))
        {
            HotseatSurfaceIntentResult source = fixture.Coordinator.ApplyIntent(new(
                75,
                HotseatSurfaceGesture.Click,
                HotseatSurfaceRef.HandCard(PlayerId.Player0, 1, 102)));
            HotseatSurfaceIntentResult slot = fixture.Coordinator.ApplyIntent(new(
                75,
                HotseatSurfaceGesture.Click,
                HotseatSurfaceRef.TacticSlot(PlayerId.Player0, 1)));
            HotseatSurfaceIntentResult result = fixture.Coordinator.ApplyIntent(new(
                75,
                HotseatSurfaceGesture.Click,
                HotseatSurfaceRef.Unit(PlayerId.Player1, 0, 501)));

            Assert.AreEqual(HotseatSelectionStep.ChooseSlot, source.NextStep);
            Assert.AreEqual(HotseatSelectionStep.ChooseTarget, slot.NextStep);
            Assert.AreEqual(HotseatSurfaceIntentStatus.CommandPrepared, result.Status);
            Assert.AreEqual(enemyUnit, result.CanonicalCommand!.Target);
            Assert.AreEqual(1UL, result.CanonicalCommand.Slot);
        }
    }

    [TestMethod]
    public void DeployDonorSurfaceAdvancesWithoutPrematurePreparation()
    {
        LegalAction deploy = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.Deploy,
            76,
            source: 301,
            slot: 0,
            donor: 201);
        using SurfaceFixture fixture = CreateActionFixture(76, [deploy]);

        _ = fixture.Coordinator.ApplyIntent(new(
            76,
            HotseatSurfaceGesture.Click,
            HotseatSurfaceRef.StandbyCard(PlayerId.Player0, 0, 301)));
        HotseatSurfaceIntentResult donor = fixture.Coordinator.ApplyIntent(new(
            76,
            HotseatSurfaceGesture.Click,
            HotseatSurfaceRef.Unit(PlayerId.Player0, 0, 201)));

        Assert.AreEqual(HotseatSurfaceIntentStatus.Applied, donor.Status);
        Assert.AreEqual(HotseatSelectionStep.ChooseSlot, donor.NextStep);
        Assert.IsTrue(donor.RequiresFurtherSelection);
        Assert.IsFalse(fixture.Controller.State.CommandPrepared);

        HotseatSurfaceIntentResult slot = fixture.Coordinator.ApplyIntent(new(
            76,
            HotseatSurfaceGesture.Click,
            HotseatSurfaceRef.UnitSlot(PlayerId.Player0, 0)));
        Assert.AreEqual(HotseatSurfaceIntentStatus.CommandPrepared, slot.Status);
        Assert.AreEqual(201UL, slot.CanonicalCommand!.ComponentDonor);
        Assert.AreEqual(0UL, slot.CanonicalCommand.Slot);
    }

    [TestMethod]
    public void CastSpellClickAndDragChooseSlotBeforeTargetAndConverge()
    {
        Target enemy = Target.UnitTarget(PlayerId.Player1, 501);
        LegalAction cast = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.CastSpell,
            77,
            source: 102,
            target: enemy,
            slot: 2);
        using SurfaceFixture clickFixture = CreateActionFixture(77, [cast]);
        using SurfaceFixture dragFixture = CreateActionFixture(77, [cast]);
        HotseatSurfaceRef card = HotseatSurfaceRef.HandCard(PlayerId.Player0, 1, 102);
        HotseatSurfaceRef slot = HotseatSurfaceRef.TacticSlot(PlayerId.Player0, 2);
        HotseatSurfaceRef target = HotseatSurfaceRef.Unit(PlayerId.Player1, 0, 501);

        _ = clickFixture.Coordinator.ApplyIntent(new(77, HotseatSurfaceGesture.Click, card));
        HotseatSurfaceIntentResult clickedSlot = clickFixture.Coordinator.ApplyIntent(new(
            77,
            HotseatSurfaceGesture.Click,
            slot));
        HotseatSurfaceIntentResult clicked = clickFixture.Coordinator.ApplyIntent(new(
            77,
            HotseatSurfaceGesture.Click,
            target));
        HotseatSurfaceIntentResult draggedSlot = dragFixture.Coordinator.ApplyIntent(new(
            77,
            HotseatSurfaceGesture.Drag,
            card)
        {
            Destination = slot,
        });
        HotseatSurfaceIntentResult dragged = dragFixture.Coordinator.ApplyIntent(new(
            77,
            HotseatSurfaceGesture.Drag,
            card)
        {
            Destination = target,
        });

        Assert.AreEqual(HotseatSelectionStep.ChooseTarget, clickedSlot.NextStep);
        Assert.AreEqual(HotseatSelectionStep.ChooseTarget, draggedSlot.NextStep);
        Assert.AreEqual(HotseatSurfaceIntentStatus.CommandPrepared, clicked.Status);
        Assert.AreEqual(HotseatSurfaceIntentStatus.CommandPrepared, dragged.Status);
        Assert.AreEqual(2UL, clicked.CanonicalCommand!.Slot);
        Assert.AreEqual(enemy, clicked.CanonicalCommand.Target);
        Assert.AreEqual(clicked.CanonicalCommand, dragged.CanonicalCommand);
    }

    [TestMethod]
    public void NoTargetCastClickAndDragToEmptySlotPrepareTheSameSlottedCommand()
    {
        LegalAction cast = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.CastSpell,
            87,
            source: 102,
            slot: 2);
        using SurfaceFixture clickFixture = CreateActionFixture(87, [cast]);
        using SurfaceFixture dragFixture = CreateActionFixture(87, [cast]);
        HotseatSurfaceRef card = HotseatSurfaceRef.HandCard(PlayerId.Player0, 1, 102);
        HotseatSurfaceRef slot = HotseatSurfaceRef.TacticSlot(PlayerId.Player0, 2);

        _ = clickFixture.Coordinator.ApplyIntent(new(87, HotseatSurfaceGesture.Click, card));
        HotseatSurfaceIntentResult clicked = clickFixture.Coordinator.ApplyIntent(new(
            87,
            HotseatSurfaceGesture.Click,
            slot));
        HotseatSurfaceIntentResult dragged = dragFixture.Coordinator.ApplyIntent(new(
            87,
            HotseatSurfaceGesture.Drag,
            card)
        {
            Destination = slot,
        });

        Assert.AreEqual(HotseatSurfaceIntentStatus.CommandPrepared, clicked.Status);
        Assert.AreEqual(HotseatSurfaceIntentStatus.CommandPrepared, dragged.Status);
        Assert.AreEqual(2UL, clicked.CanonicalCommand!.Slot);
        Assert.IsNull(clicked.CanonicalCommand.Target);
        Assert.AreEqual(clicked.CanonicalCommand, dragged.CanonicalCommand);
    }

    [TestMethod]
    public void DirectCastTargetIsRejectedUntilAnEmptyOwnTacticSlotIsChosen()
    {
        Target enemy = Target.UnitTarget(PlayerId.Player1, 501);
        LegalAction cast = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.CastSpell,
            84,
            source: 102,
            target: enemy,
            slot: 1);
        HotseatSurfaceRef card = HotseatSurfaceRef.HandCard(PlayerId.Player0, 1, 102);
        HotseatSurfaceRef target = HotseatSurfaceRef.Unit(PlayerId.Player1, 0, 501);

        using (SurfaceFixture directFixture = CreateActionFixture(84, [cast]))
        {
            HotseatUiState initial = directFixture.Controller.State;
            int calls = directFixture.Session.Calls.Count;
            AssertRejectedWithoutEffects(
                directFixture,
                new HotseatSurfaceIntent(84, HotseatSurfaceGesture.Drag, card)
                {
                    Destination = target,
                },
                HotseatSurfaceIntentStatus.InvalidSurface,
                initial,
                calls);
        }

        using SurfaceFixture selectedFixture = CreateActionFixture(84, [cast]);
        _ = selectedFixture.Coordinator.ApplyIntent(new(
            84,
            HotseatSurfaceGesture.Click,
            card));
        HotseatUiState selected = selectedFixture.Controller.State;
        int selectedCalls = selectedFixture.Session.Calls.Count;
        AssertRejectedWithoutEffects(
            selectedFixture,
            new HotseatSurfaceIntent(84, HotseatSurfaceGesture.Click, target),
            HotseatSurfaceIntentStatus.InvalidSurface,
            selected,
            selectedCalls);
    }

    [TestMethod]
    public void RetiredCastZoneAndNonOwnOrOccupiedTacticSlotsHaveNoSideEffects()
    {
        LegalAction cast = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.CastSpell,
            85,
            source: 102,
            slot: 1);
        using SurfaceFixture fixture = CreateActionFixture(85, [cast]);
        HotseatSurfaceRef card = HotseatSurfaceRef.HandCard(PlayerId.Player0, 1, 102);
        _ = fixture.Coordinator.ApplyIntent(new(85, HotseatSurfaceGesture.Click, card));
        HotseatUiState selected = fixture.Controller.State;
        int calls = fixture.Session.Calls.Count;

        HotseatSurfaceRef[] rejected =
        [
            HotseatSurfaceRef.TacticSlot(PlayerId.Player0, 0),
            HotseatSurfaceRef.TacticSlot(PlayerId.Player1, 1),
            HotseatSurfaceRef.CastZone(),
        ];
        foreach (HotseatSurfaceRef surface in rejected)
        {
            AssertRejectedWithoutEffects(
                fixture,
                new HotseatSurfaceIntent(85, HotseatSurfaceGesture.Click, surface),
                HotseatSurfaceIntentStatus.InvalidSurface,
                selected,
                calls);
        }

        using SurfaceFixture dragFixture = CreateActionFixture(85, [cast]);
        HotseatUiState dragInitial = dragFixture.Controller.State;
        int dragCalls = dragFixture.Session.Calls.Count;
        foreach (HotseatSurfaceRef surface in rejected)
        {
            AssertRejectedWithoutEffects(
                dragFixture,
                new HotseatSurfaceIntent(85, HotseatSurfaceGesture.Drag, card)
                {
                    Destination = surface,
                },
                HotseatSurfaceIntentStatus.InvalidSurface,
                dragInitial,
                dragCalls);
        }
    }

    [TestMethod]
    public void FullTacticRowRejectsSpellCandidatesInsteadOfProvidingAFallback()
    {
        CardView[] fullTactics =
        [
            HotseatTestModel.Card(401, PlayerId.Player0, Zone.Tactic),
            HotseatTestModel.Card(402, PlayerId.Player0, Zone.Tactic),
            HotseatTestModel.Card(403, PlayerId.Player0, Zone.Tactic),
        ];
        LegalAction forged = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.CastSpell,
            86,
            source: 102,
            slot: 1);

        Assert.ThrowsExactly<ScgsProtocolException>(() =>
        {
            using SurfaceFixture _ = CreateActionFixture(86, [forged], player0Tactics: fullTactics);
        });
    }

    [TestMethod]
    public void ExplicitActionButtonDisambiguatesSourceAndPreparesOnlyReadyAction()
    {
        LegalAction attack = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.Attack,
            78,
            source: 201,
            target: Target.Leader(PlayerId.Player1));
        LegalAction evolve = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.Evolve,
            78,
            source: 201);
        using SurfaceFixture fixture = CreateActionFixture(78, [attack, evolve]);
        HotseatSurfaceRef unit = HotseatSurfaceRef.Unit(PlayerId.Player0, 0, 201);

        HotseatSurfaceIntentResult source = fixture.Coordinator.ApplyIntent(new(
            78,
            HotseatSurfaceGesture.Click,
            unit));
        HotseatSurfaceIntentResult action = fixture.Coordinator.ApplyIntent(new(
            78,
            HotseatSurfaceGesture.Click,
            unit)
        {
            Action = ActionKind.Evolve,
        });

        Assert.AreEqual(HotseatSelectionStep.ChooseAction, source.NextStep);
        Assert.AreEqual(HotseatSurfaceIntentStatus.CommandPrepared, action.Status);
        Assert.AreEqual(ActionKind.Evolve, action.CanonicalCommand!.Action);
    }

    [TestMethod]
    public void StaleInvalidAndRejectedModeIntentsHaveNoSideEffects()
    {
        LegalAction play = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.PlayUnit,
            79,
            source: 101,
            slot: 0);
        using SurfaceFixture fixture = CreateActionFixture(79, [play]);
        HotseatUiState initial = fixture.Controller.State;
        int calls = fixture.Session.Calls.Count;

        AssertRejectedWithoutEffects(
            fixture,
            new HotseatSurfaceIntent(
                78,
                HotseatSurfaceGesture.Click,
                HotseatSurfaceRef.HandCard(PlayerId.Player0, 0, 101)),
            HotseatSurfaceIntentStatus.StaleRevision,
            initial,
            calls);
        AssertRejectedWithoutEffects(
            fixture,
            new HotseatSurfaceIntent(
                79,
                HotseatSurfaceGesture.Click,
                default),
            HotseatSurfaceIntentStatus.InvalidSurface,
            initial,
            calls);
        AssertRejectedWithoutEffects(
            fixture,
            new HotseatSurfaceIntent(
                79,
                HotseatSurfaceGesture.Click,
                HotseatSurfaceRef.HandCard((PlayerId)99, 0, 101)),
            HotseatSurfaceIntentStatus.InvalidSurface,
            initial,
            calls);
        AssertRejectedWithoutEffects(
            fixture,
            new HotseatSurfaceIntent(
                79,
                HotseatSurfaceGesture.Click,
                HotseatSurfaceRef.HandCard(PlayerId.Player0, -1, 101)),
            HotseatSurfaceIntentStatus.InvalidSurface,
            initial,
            calls);
        AssertRejectedWithoutEffects(
            fixture,
            new HotseatSurfaceIntent(
                79,
                HotseatSurfaceGesture.Drag,
                HotseatSurfaceRef.HandCard(PlayerId.Player0, 0, 101)),
            HotseatSurfaceIntentStatus.InvalidSurface,
            initial,
            calls);

        using var coveredController = new HotseatMatchController(fixture.Session);
        var covered = new HotseatSurfaceInteractionCoordinator(coveredController);
        HotseatUiState coveredState = coveredController.State;
        int coveredCalls = fixture.Session.Calls.Count;
        HotseatSurfaceIntentResult rejected = covered.ApplyIntent(new(
            79,
            HotseatSurfaceGesture.Click,
            HotseatSurfaceRef.HandCard(PlayerId.Player0, 0, 101)));
        Assert.AreEqual(HotseatSurfaceIntentStatus.RejectedMode, rejected.Status);
        Assert.AreSame(coveredState, coveredController.State);
        Assert.AreEqual(coveredCalls, fixture.Session.Calls.Count);
    }

    [TestMethod]
    public void AmbiguousDragHasNoSideEffects()
    {
        Target leader = Target.Leader(PlayerId.Player1);
        LegalAction attack = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.Attack,
            80,
            source: 201,
            target: leader);
        LegalAction evolve = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.Evolve,
            80,
            source: 201,
            target: leader);
        using SurfaceFixture fixture = CreateActionFixture(80, [attack, evolve]);
        HotseatUiState initial = fixture.Controller.State;
        int calls = fixture.Session.Calls.Count;

        HotseatSurfaceIntentResult result = fixture.Coordinator.ApplyIntent(new(
            80,
            HotseatSurfaceGesture.Drag,
            HotseatSurfaceRef.Unit(PlayerId.Player0, 0, 201))
        {
            Destination = HotseatSurfaceRef.Leader(PlayerId.Player1),
        });

        Assert.AreEqual(HotseatSurfaceIntentStatus.Ambiguous, result.Status);
        Assert.IsFalse(result.StateChanged);
        Assert.AreSame(initial, fixture.Controller.State);
        Assert.AreEqual(calls, fixture.Session.Calls.Count);
    }

    [TestMethod]
    public void DestinationThatCouldMeanDonorOrTargetIsRejectedBeforeSourceSelection()
    {
        LegalAction deploy = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.Deploy,
            81,
            source: 301,
            target: Target.UnitTarget(PlayerId.Player0, 201),
            slot: 0,
            donor: 201);
        using SurfaceFixture fixture = CreateActionFixture(81, [deploy]);
        HotseatUiState initial = fixture.Controller.State;
        int calls = fixture.Session.Calls.Count;

        HotseatSurfaceIntentResult result = fixture.Coordinator.ApplyIntent(new(
            81,
            HotseatSurfaceGesture.Drag,
            HotseatSurfaceRef.StandbyCard(PlayerId.Player0, 0, 301))
        {
            Destination = HotseatSurfaceRef.Unit(PlayerId.Player0, 0, 201),
        });

        Assert.AreEqual(HotseatSurfaceIntentStatus.Ambiguous, result.Status);
        Assert.IsFalse(result.StateChanged);
        Assert.AreSame(initial, fixture.Controller.State);
        Assert.AreEqual(calls, fixture.Session.Calls.Count);
    }

    [TestMethod]
    public void DragAfterChoosingActionUsesThatActionWhenDestinationIsShared()
    {
        Target enemy = Target.UnitTarget(PlayerId.Player1, 501);
        LegalAction attack = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.Attack,
            82,
            source: 201,
            target: enemy);
        LegalAction evolve = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.Evolve,
            82,
            source: 201,
            target: enemy);
        using SurfaceFixture fixture = CreateActionFixture(82, [attack, evolve]);
        HotseatSurfaceRef unit = HotseatSurfaceRef.Unit(PlayerId.Player0, 0, 201);

        _ = fixture.Coordinator.ApplyIntent(new(
            82,
            HotseatSurfaceGesture.Click,
            unit));
        fixture.Controller.ChooseAction(ActionKind.Attack);
        HotseatSurfaceIntentResult result = fixture.Coordinator.ApplyIntent(new(
            82,
            HotseatSurfaceGesture.Drag,
            unit)
        {
            Destination = HotseatSurfaceRef.Unit(PlayerId.Player1, 0, 501),
        });

        Assert.AreEqual(HotseatSurfaceIntentStatus.CommandPrepared, result.Status);
        Assert.AreEqual(ActionKind.Attack, result.CanonicalCommand!.Action);
        Assert.AreEqual(enemy, result.CanonicalCommand.Target);
        Assert.AreEqual(HotseatUiMode.Resolving, fixture.Controller.State.Mode);
    }

    [TestMethod]
    public void DragWithActionConflictingWithChosenActionHasNoSideEffects()
    {
        Target enemy = Target.UnitTarget(PlayerId.Player1, 501);
        LegalAction attack = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.Attack,
            83,
            source: 201,
            target: enemy);
        LegalAction evolve = HotseatTestModel.Action(
            PlayerId.Player0,
            ActionKind.Evolve,
            83,
            source: 201,
            target: enemy);
        using SurfaceFixture fixture = CreateActionFixture(83, [attack, evolve]);
        HotseatSurfaceRef unit = HotseatSurfaceRef.Unit(PlayerId.Player0, 0, 201);

        _ = fixture.Coordinator.ApplyIntent(new(
            83,
            HotseatSurfaceGesture.Click,
            unit));
        fixture.Controller.ChooseAction(ActionKind.Attack);
        HotseatUiState selected = fixture.Controller.State;
        int calls = fixture.Session.Calls.Count;

        HotseatSurfaceIntentResult result = fixture.Coordinator.ApplyIntent(new(
            83,
            HotseatSurfaceGesture.Drag,
            unit)
        {
            Destination = HotseatSurfaceRef.Unit(PlayerId.Player1, 0, 501),
            Action = ActionKind.Evolve,
        });

        Assert.AreEqual(HotseatSurfaceIntentStatus.InvalidSurface, result.Status);
        Assert.IsFalse(result.Accepted);
        Assert.IsFalse(result.StateChanged);
        Assert.AreSame(selected, fixture.Controller.State);
        Assert.AreEqual(calls, fixture.Session.Calls.Count);
    }

    private static void AssertRejectedWithoutEffects(
        SurfaceFixture fixture,
        HotseatSurfaceIntent intent,
        HotseatSurfaceIntentStatus expected,
        HotseatUiState state,
        int calls)
    {
        HotseatSurfaceIntentResult result = fixture.Coordinator.ApplyIntent(intent);
        Assert.AreEqual(expected, result.Status);
        Assert.IsFalse(result.Accepted);
        Assert.IsFalse(result.StateChanged);
        Assert.AreSame(state, fixture.Controller.State);
        Assert.AreEqual(calls, fixture.Session.Calls.Count);
    }

    private static SurfaceFixture CreateActionFixture(
        ulong revision,
        IReadOnlyList<LegalAction> actions,
        MatchPhase phase = MatchPhase.Action,
        IReadOnlyList<CardView?>? player0Tactics = null)
    {
        CardView unit = HotseatTestModel.Card(201, PlayerId.Player0, Zone.Unit);
        CardView enemy = HotseatTestModel.Card(501, PlayerId.Player1, Zone.Unit);
        CardView tactic = HotseatTestModel.Card(
            401,
            PlayerId.Player0,
            Zone.Tactic,
            faceDown: true);
        CardView standby = HotseatTestModel.Card(301, PlayerId.Player0, Zone.Standby);
        var session = new FakeGameSession
        {
            ViewHandler = viewer => HotseatTestModel.View(
                viewer,
                revision,
                phase,
                PlayerId.Player0,
                true,
                true,
                [101, 102, 103],
                responder: PlayerId.Player0,
                player0Units: [unit, null, null, null, null],
                player1Units: [enemy, null, null, null, null],
                player0Tactics: player0Tactics ?? [tactic, null, null],
                player0Standby: [standby]),
            ActionsHandler = query => HotseatTestModel.Filter(query, revision, actions),
            EventsHandler = (viewer, after) =>
                HotseatTestModel.Events(revision, after, after, viewer),
        };
        var controller = new HotseatMatchController(session);
        controller.Reveal();
        return new SurfaceFixture(session, controller);
    }

    private sealed record SourceCase(
        ActionKind Action,
        ulong Source,
        HotseatSurfaceRef Surface,
        Target? Target = null,
        ulong? Slot = null);

    private sealed class SurfaceFixture : IDisposable
    {
        internal SurfaceFixture(FakeGameSession session, HotseatMatchController controller)
        {
            Session = session;
            Controller = controller;
            Coordinator = new HotseatSurfaceInteractionCoordinator(controller);
        }

        internal FakeGameSession Session { get; }

        internal HotseatMatchController Controller { get; }

        internal HotseatSurfaceInteractionCoordinator Coordinator { get; }

        public void Dispose() => Controller.Dispose();
    }
}
