// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Scgs.Client.Tests;

[TestClass]
[DoNotParallelize]
public sealed class Gate3BRealNativeFullMatchTests
{
    private const int MaximumCommandsPerMatch = 1200;

    private static readonly HashSet<string> SafeHiddenEventTexts =
    [
        "opponent drew a card",
        "opponent set a trap",
        "opponent completed mulligan",
    ];

    private static readonly ActionKind[] CoveragePriority =
    [
        ActionKind.PlayTactic,
        ActionKind.CastSpell,
        ActionKind.PlayUnit,
        ActionKind.Deploy,
        ActionKind.Evolve,
        ActionKind.Attack,
        ActionKind.EndTurn,
    ];

    [TestMethod]
    [TestCategory("NativeIntegration")]
    public void SyntheticFixtureMatrixCompletesWithPrivacyIndependentCursorsAndAllCommands()
    {
        string nativePath = GetNativeLibraryPath();
        (string Player0, string Player1)[] deckPairs =
        [
            ("synthetic_alpha", "synthetic_alpha"),
            ("synthetic_alpha", "synthetic_beta"),
            ("synthetic_beta", "synthetic_alpha"),
            ("synthetic_beta", "synthetic_beta"),
        ];
        FirstPlayerMode[] firstPlayers =
        [
            FirstPlayerMode.Player0,
            FirstPlayerMode.Player1,
        ];

        var submittedKinds = new HashSet<ActionKind>();
        uint seed = 0x3B00_0000U;
        foreach ((string player0Deck, string player1Deck) in deckPairs)
        {
            foreach (FirstPlayerMode firstPlayerMode in firstPlayers)
            {
                RunFullMatch(
                    nativePath,
                    player0Deck,
                    player1Deck,
                    seed++,
                    firstPlayerMode,
                    submittedKinds);
            }
        }

        // Surrender is deliberately excluded from the full-match agent. Exercise
        // it in its own terminal-path session so the eight matrix games still
        // prove that the protocol fixtures can reach a natural engine result.
        RunSurrenderTerminalPath(nativePath, seed, submittedKinds);

        ActionKind[] missing = Enum.GetValues<ActionKind>()
            .Where(action => !submittedKinds.Contains(action))
            .ToArray();
        Assert.AreEqual(
            0,
            missing.Length,
            $"No real-native session successfully submitted: {string.Join(", ", missing)}");
    }

    private static void RunFullMatch(
        string nativePath,
        string player0Deck,
        string player1Deck,
        uint seed,
        FirstPlayerMode firstPlayerMode,
        HashSet<ActionKind> submittedKinds)
    {
        var config = new GameConfigRequest(player0Deck, player1Deck)
        {
            RandomSeed = seed,
            FirstPlayerMode = firstPlayerMode,
            // Fixed ordering makes this matrix deterministic across the Windows
            // and macOS standard-library implementations used by CI.
            ShuffleDecks = false,
        };
        using IScgsGameSession session = ScgsGameSession.Create(config, nativePath);
        Assert.IsTrue(session.Start().IsSuccess);

        MatchView player0View = session.GetView(PlayerId.Player0);
        MatchView player1View = session.GetView(PlayerId.Player1);
        Assert.AreEqual(seed, player0View.RandomSeed);
        Assert.AreEqual(ExpectedFirstPlayer(firstPlayerMode), player0View.FirstPlayer);
        AssertViewerPrivacy(player0View, player1View);

        int[] matchEndedCounts = new int[2];
        DrainBothViewers(session, player0View.Revision, matchEndedCounts);

        var reactionProbeTurns = new HashSet<(PlayerId Player, int OwnTurn)>();
        bool completed = false;
        int submittedCommands = 0;
        for (int step = 0; step < MaximumCommandsPerMatch; ++step)
        {
            player0View = session.GetView(PlayerId.Player0);
            player1View = session.GetView(PlayerId.Player1);
            AssertViewerPrivacy(player0View, player1View);
            if (player0View.Result != GameResult.Ongoing)
            {
                completed = true;
                break;
            }

            PlayerId actor = CurrentActor(player0View);
            var query = new ActionQueryRequest(actor, player0View.Revision);
            LegalActionsResult legal = session.ListLegalActions(query);
            Assert.AreEqual(player0View.Revision, legal.Revision);
            Assert.IsGreaterThan(
                0,
                legal.Actions.Count,
                $"No legal command for {player0Deck}/{player1Deck} at revision {player0View.Revision}.");

            LegalAction? selected = SelectCommand(
                legal.Actions,
                player0View,
                actor,
                submittedKinds,
                reactionProbeTurns);
            if (selected is null)
            {
                Assert.Fail(
                    $"Selector found no non-surrender command for {player0Deck}/{player1Deck} " +
                    $"at revision {player0View.Revision} ({player0View.Phase}).");
            }

            GameCommandRequest command = selected!.Command;
            Assert.AreNotEqual(ActionKind.Surrender, command.Action);
            Assert.AreEqual(actor, command.Player);
            Assert.IsTrue(command.ExpectedRevision == player0View.Revision);

            PaymentResult payment = session.PreviewPayment(command);
            Assert.AreEqual(player0View.Revision, payment.Revision);
            Assert.IsTrue(
                payment.Payment.Status.IsSuccess,
                $"Preview rejected enumerated {command.Action}: {payment.Payment.Status.Message}");

            ulong beforeRevision = player0View.Revision;
            EngineStatus status = session.SubmitCommand(command);
            Assert.IsTrue(status.IsSuccess, $"Submit rejected {command.Action}: {status.Message}");
            submittedKinds.Add(command.Action);
            ++submittedCommands;

            MatchView after0 = session.GetView(PlayerId.Player0);
            MatchView after1 = session.GetView(PlayerId.Player1);
            Assert.AreEqual(beforeRevision + 1U, after0.Revision);
            Assert.AreEqual(after0.Revision, after1.Revision);
            AssertViewerPrivacy(after0, after1);
            DrainBothViewers(session, after0.Revision, matchEndedCounts);
        }

        MatchView final0 = session.GetView(PlayerId.Player0);
        MatchView final1 = session.GetView(PlayerId.Player1);
        Assert.IsTrue(completed || final0.Result != GameResult.Ongoing);
        Assert.AreEqual(MatchPhase.Finished, final0.Phase);
        Assert.AreNotEqual(GameResult.Ongoing, final0.Result);
        Assert.AreEqual(final0.Result, final1.Result);
        Assert.IsGreaterThan(10, submittedCommands, "A matrix match terminated before exercising normal play.");
        AssertViewerPrivacy(final0, final1);
        AssertSingleMatchEnded(session, matchEndedCounts);
    }

    private static void RunSurrenderTerminalPath(
        string nativePath,
        uint seed,
        HashSet<ActionKind> submittedKinds)
    {
        var config = new GameConfigRequest("synthetic_alpha", "synthetic_beta")
        {
            RandomSeed = seed,
            FirstPlayerMode = FirstPlayerMode.Player0,
            ShuffleDecks = false,
        };
        using IScgsGameSession session = ScgsGameSession.Create(config, nativePath);
        Assert.IsTrue(session.Start().IsSuccess);

        MatchView before = session.GetView(PlayerId.Player0);
        MatchView beforeOther = session.GetView(PlayerId.Player1);
        AssertViewerPrivacy(before, beforeOther);
        int[] matchEndedCounts = new int[2];
        DrainBothViewers(session, before.Revision, matchEndedCounts);

        LegalActionsResult legal = session.ListLegalActions(
            new ActionQueryRequest(PlayerId.Player0, before.Revision)
            {
                Action = ActionKind.Surrender,
            });
        LegalAction surrender = legal.Actions.Single();
        Assert.AreEqual(ActionKind.Surrender, surrender.Command.Action);
        Assert.IsTrue(session.PreviewPayment(surrender.Command).Payment.Status.IsSuccess);
        Assert.IsTrue(session.SubmitCommand(surrender.Command).IsSuccess);
        submittedKinds.Add(ActionKind.Surrender);

        MatchView after = session.GetView(PlayerId.Player0);
        MatchView afterOther = session.GetView(PlayerId.Player1);
        Assert.AreEqual(before.Revision + 1U, after.Revision);
        Assert.AreEqual(MatchPhase.Finished, after.Phase);
        Assert.AreEqual(GameResult.Player1Won, after.Result);
        AssertViewerPrivacy(after, afterOther);
        DrainBothViewers(session, after.Revision, matchEndedCounts);
        AssertSingleMatchEnded(session, matchEndedCounts);
    }

    private static LegalAction? SelectCommand(
        IReadOnlyList<LegalAction> actions,
        MatchView view,
        PlayerId actor,
        HashSet<ActionKind> submittedKinds,
        HashSet<(PlayerId Player, int OwnTurn)> reactionProbeTurns)
    {
        if (view.Phase == MatchPhase.Mulligan)
        {
            return actions.FirstOrDefault(action =>
                       action.Command.Action == ActionKind.Mulligan &&
                       action.Command.MulliganCards.Count == 0) ??
                   actions.FirstOrDefault(action => action.Command.Action == ActionKind.Mulligan);
        }

        if (view.Phase == MatchPhase.Reaction)
        {
            ActionKind first = submittedKinds.Contains(ActionKind.ActivateTrap)
                ? ActionKind.PassReaction
                : ActionKind.ActivateTrap;
            return FindAction(actions, first) ??
                   FindAction(actions, ActionKind.ActivateTrap) ??
                   FindAction(actions, ActionKind.PassReaction);
        }

        foreach (ActionKind kind in CoveragePriority)
        {
            if (submittedKinds.Contains(kind))
            {
                continue;
            }

            LegalAction? unseen = kind == ActionKind.Attack
                ? FindUnitAttack(actions) ?? FindLeaderAttack(actions)
                : FindAction(actions, kind);
            if (unseen is not null)
            {
                return unseen;
            }
        }

        bool needsReactionCoverage =
            !submittedKinds.Contains(ActionKind.ActivateTrap) ||
            !submittedKinds.Contains(ActionKind.PassReaction);
        PlayerId opponent = Opponent(actor);
        bool opponentHasFaceDownTactic = view.Players[(int)opponent].Tactics.Any(
            card => card is { FaceDown: true });
        int ownTurn = view.Players[(int)actor].OwnTurnNumber;
        if (needsReactionCoverage && opponentHasFaceDownTactic &&
            reactionProbeTurns.Add((actor, ownTurn)))
        {
            LegalAction? probe = FindLeaderAttack(actions);
            if (probe is not null)
            {
                return probe;
            }
        }

        bool needsOrdinaryCoverage = Enum.GetValues<ActionKind>()
            .Where(action => action != ActionKind.Surrender)
            .Any(action => !submittedKinds.Contains(action));
        if (needsOrdinaryCoverage)
        {
            // Keep both leaders alive while drawing toward traps, evolution and
            // standby deployment. Unit combat frees board slots without turning
            // this into a passive end-turn-only simulation.
            return FindAction(actions, ActionKind.PlayTactic) ??
                   FindAction(actions, ActionKind.CastSpell) ??
                   FindAction(actions, ActionKind.Deploy) ??
                   FindAction(actions, ActionKind.Evolve) ??
                   FindAction(actions, ActionKind.PlayUnit) ??
                   FindUnitAttack(actions) ??
                   FindAction(actions, ActionKind.EndTurn);
        }

        // Once every non-terminal command has succeeded, finish aggressively.
        return FindLeaderAttack(actions) ??
               FindUnitAttack(actions) ??
               FindAction(actions, ActionKind.Evolve) ??
               FindAction(actions, ActionKind.PlayUnit) ??
               FindAction(actions, ActionKind.CastSpell) ??
               FindAction(actions, ActionKind.Deploy) ??
               FindAction(actions, ActionKind.PlayTactic) ??
               FindAction(actions, ActionKind.EndTurn);
    }

    private static LegalAction? FindAction(
        IEnumerable<LegalAction> actions,
        ActionKind kind) =>
        actions.FirstOrDefault(action => action.Command.Action == kind);

    private static LegalAction? FindLeaderAttack(IEnumerable<LegalAction> actions) =>
        actions.FirstOrDefault(action =>
            action.Command.Action == ActionKind.Attack &&
            action.Command.Target?.Kind == TargetKind.Leader);

    private static LegalAction? FindUnitAttack(IEnumerable<LegalAction> actions) =>
        actions.FirstOrDefault(action =>
            action.Command.Action == ActionKind.Attack &&
            action.Command.Target?.Kind == TargetKind.Unit);

    private static PlayerId CurrentActor(MatchView view)
    {
        if (view.Phase == MatchPhase.Mulligan)
        {
            return !view.Players[0].MulliganDone ? PlayerId.Player0 : PlayerId.Player1;
        }

        return view.Phase == MatchPhase.Reaction
            ? view.Reaction.Responder
            : view.ActivePlayer;
    }

    private static void AssertViewerPrivacy(MatchView player0, MatchView player1)
    {
        Assert.AreEqual(PlayerId.Player0, player0.Viewer);
        Assert.AreEqual(PlayerId.Player1, player1.Viewer);
        Assert.AreEqual(player0.Revision, player1.Revision);
        Assert.AreEqual(player0.Phase, player1.Phase);
        Assert.AreEqual(player0.Result, player1.Result);
        Assert.AreEqual(player0.ActivePlayer, player1.ActivePlayer);
        Assert.AreEqual(player0.FirstPlayer, player1.FirstPlayer);
        Assert.AreEqual(player0.RandomSeed, player1.RandomSeed);

        AssertPrivateHandAndTactics(player0, PlayerId.Player0);
        AssertPrivateHandAndTactics(player1, PlayerId.Player1);
        Assert.AreEqual(player0.Players[0].HandCount, player1.Players[0].HandCount);
        Assert.AreEqual(player0.Players[1].HandCount, player1.Players[1].HandCount);

        Assert.AreEqual(player0.Reaction.Pending, player1.Reaction.Pending);
        Assert.AreEqual(player0.Reaction.Window, player1.Reaction.Window);
        Assert.AreEqual(player0.Reaction.Responder, player1.Reaction.Responder);
        Assert.AreEqual(player0.Reaction.EligibleCount, player1.Reaction.EligibleCount);
        if (player0.Reaction.Pending)
        {
            Assert.IsNotNull(player0.Reaction.Origin);
            Assert.IsNotNull(player1.Reaction.Origin);
            AssertReactionOriginEqual(player0.Reaction.Origin, player1.Reaction.Origin);

            ReactionContext responder = player0.Reaction.Responder == PlayerId.Player0
                ? player0.Reaction
                : player1.Reaction;
            ReactionContext observer = player0.Reaction.Responder == PlayerId.Player0
                ? player1.Reaction
                : player0.Reaction;
            Assert.AreEqual((ulong)responder.EligibleTraps.Length, responder.EligibleCount);
            Assert.AreEqual(0, observer.EligibleTraps.Length);
        }
        else
        {
            Assert.IsNull(player0.Reaction.Origin);
            Assert.IsNull(player1.Reaction.Origin);
        }
    }

    private static void AssertPrivateHandAndTactics(MatchView view, PlayerId viewer)
    {
        int ownIndex = (int)viewer;
        int opponentIndex = (int)Opponent(viewer);
        PlayerView own = view.Players[ownIndex];
        PlayerView opponent = view.Players[opponentIndex];
        Assert.AreEqual((ulong)own.Hand.Length, own.HandCount);
        Assert.AreEqual(0, opponent.Hand.Length);
        Assert.IsTrue(own.Hand.All(card =>
            card.InstanceId.HasValue &&
            card.DefinitionId.HasValue &&
            card.Definition is not null &&
            card.Kind.HasValue &&
            card.Name.Length > 0));

        foreach (CardView? tactic in opponent.Tactics)
        {
            if (tactic is not { FaceDown: true })
            {
                continue;
            }

            Assert.IsFalse(tactic.InstanceId.HasValue);
            Assert.IsFalse(tactic.DefinitionId.HasValue);
            Assert.IsNull(tactic.Definition);
            Assert.IsFalse(tactic.Kind.HasValue);
            Assert.AreEqual(string.Empty, tactic.Name);
        }
    }

    private static void AssertReactionOriginEqual(ReactionOrigin first, ReactionOrigin second)
    {
        Assert.AreEqual(first.Action, second.Action);
        Assert.AreEqual(first.Player, second.Player);
        Assert.AreEqual(first.Source, second.Source);
        Assert.AreEqual(first.Target?.Kind, second.Target?.Kind);
        Assert.AreEqual(first.Target?.Player, second.Target?.Player);
        Assert.AreEqual(first.Target?.Unit, second.Target?.Unit);
    }

    private static void DrainBothViewers(
        IScgsGameSession session,
        ulong expectedRevision,
        int[] matchEndedCounts)
    {
        ulong player0Before = session.GetEventCursor(PlayerId.Player0);
        ulong player1Before = session.GetEventCursor(PlayerId.Player1);

        EventBatch player0 = session.ReadNewEvents(PlayerId.Player0);
        Assert.AreEqual(expectedRevision, player0.Revision);
        Assert.AreEqual(player0.LastSequence, session.GetEventCursor(PlayerId.Player0));
        Assert.AreEqual(player1Before, session.GetEventCursor(PlayerId.Player1));
        AssertEventPrivacy(player0, PlayerId.Player0);
        EventBatch player0Replay = session.ReadEvents(PlayerId.Player0, player0Before);
        AssertSameSequences(player0, player0Replay);
        Assert.AreEqual(player0.LastSequence, session.GetEventCursor(PlayerId.Player0));

        EventBatch player1 = session.ReadNewEvents(PlayerId.Player1);
        Assert.AreEqual(expectedRevision, player1.Revision);
        Assert.AreEqual(player1.LastSequence, session.GetEventCursor(PlayerId.Player1));
        Assert.AreEqual(player0.LastSequence, session.GetEventCursor(PlayerId.Player0));
        AssertEventPrivacy(player1, PlayerId.Player1);
        EventBatch player1Replay = session.ReadEvents(PlayerId.Player1, player1Before);
        AssertSameSequences(player1, player1Replay);
        Assert.AreEqual(player1.LastSequence, session.GetEventCursor(PlayerId.Player1));

        matchEndedCounts[0] += player0.Events.Count(gameEvent =>
            gameEvent.Type == EventType.MatchEnded);
        matchEndedCounts[1] += player1.Events.Count(gameEvent =>
            gameEvent.Type == EventType.MatchEnded);
    }

    private static void AssertSameSequences(EventBatch expected, EventBatch actual)
    {
        Assert.AreEqual(expected.Revision, actual.Revision);
        Assert.AreEqual(expected.LastSequence, actual.LastSequence);
        CollectionAssert.AreEqual(
            expected.Events.Select(gameEvent => gameEvent.Sequence).ToArray(),
            actual.Events.Select(gameEvent => gameEvent.Sequence).ToArray());
    }

    private static void AssertEventPrivacy(EventBatch batch, PlayerId viewer)
    {
        foreach (GameEventView gameEvent in batch.Events)
        {
            if (gameEvent.HiddenCard)
            {
                Assert.IsFalse(gameEvent.Card.HasValue);
                Assert.IsFalse(gameEvent.DefinitionId.HasValue);
                Assert.IsTrue(
                    SafeHiddenEventTexts.Contains(gameEvent.Text),
                    $"Hidden event text exposed an unexpected diagnostic: {gameEvent.Text}");
            }

            if (gameEvent.Type == EventType.CardDrawn && gameEvent.Player != viewer)
            {
                Assert.IsTrue(gameEvent.HiddenCard);
            }

            if (gameEvent.Type == EventType.MulliganCompleted && gameEvent.Player != viewer)
            {
                Assert.IsTrue(gameEvent.HiddenCard);
                Assert.AreEqual(0, gameEvent.Value);
                Assert.AreEqual(0, gameEvent.SecondaryValue);
            }
        }
    }

    private static void AssertSingleMatchEnded(
        IScgsGameSession session,
        IReadOnlyList<int> drainedCounts)
    {
        for (int index = 0; index < 2; ++index)
        {
            PlayerId viewer = (PlayerId)index;
            Assert.AreEqual(1, drainedCounts[index]);
            ulong cursorBefore = session.GetEventCursor(viewer);
            EventBatch history = session.ReadEvents(viewer, 0);
            Assert.AreEqual(
                1,
                history.Events.Count(gameEvent => gameEvent.Type == EventType.MatchEnded));
            Assert.AreEqual(cursorBefore, session.GetEventCursor(viewer));
            AssertEventPrivacy(history, viewer);
        }
    }

    private static PlayerId ExpectedFirstPlayer(FirstPlayerMode mode) => mode switch
    {
        FirstPlayerMode.Player0 => PlayerId.Player0,
        FirstPlayerMode.Player1 => PlayerId.Player1,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "A forced first player is required."),
    };

    private static PlayerId Opponent(PlayerId player) => player switch
    {
        PlayerId.Player0 => PlayerId.Player1,
        PlayerId.Player1 => PlayerId.Player0,
        _ => throw new ArgumentOutOfRangeException(nameof(player), player, "Unsupported player value."),
    };

    private static string GetNativeLibraryPath()
    {
        string? nativePath = Environment.GetEnvironmentVariable("SCGS_NATIVE_LIBRARY");
        if (!string.IsNullOrWhiteSpace(nativePath))
        {
            return nativePath;
        }

        bool isCi = string.Equals(
            Environment.GetEnvironmentVariable("CI"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        if (isCi)
        {
            Assert.Fail("SCGS_NATIVE_LIBRARY must identify the same-commit v04 synthetic fixture library in CI.");
        }

        Assert.Inconclusive("Set SCGS_NATIVE_LIBRARY to the v04 synthetic fixture library to run its matrix.");
        throw new InvalidOperationException("MSTest did not terminate an inconclusive test.");
    }
}
