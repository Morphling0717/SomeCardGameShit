// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Scgs.Client.Tests;

[TestClass]
[DoNotParallelize]
public sealed class NativeIntegrationTests
{
    private static readonly HashSet<string> SafeHiddenEventTexts =
    [
        "opponent drew a card",
        "opponent set a trap",
        "opponent completed mulligan",
    ];

    [TestMethod]
    [TestCategory("NativeIntegration")]
    public void SameCommitLibrarySupportsStartedViewerSafeSession()
    {
        string nativePath = GetNativeLibraryPath();

        var config = new GameConfigRequest("synthetic_alpha", "synthetic_beta")
        {
            RandomSeed = 7,
            FirstPlayerMode = FirstPlayerMode.Player0,
            ShuffleDecks = false,
        };
        ScgsGameSession session = ScgsGameSession.Create(config, nativePath);

        EngineStatus started = session.Start();
        Assert.IsTrue(started.IsSuccess);
        EngineStatus duplicateStart = session.Start();
        Assert.AreEqual(EngineCode.MatchAlreadyStarted, duplicateStart.Code);

        MatchView player0 = session.GetView(PlayerId.Player0);
        MatchView player1 = session.GetView(PlayerId.Player1);
        Assert.AreEqual(MatchPhase.Mulligan, player0.Phase);
        Assert.AreEqual(PlayerId.Player0, player0.FirstPlayer);
        Assert.HasCount(2, player0.Players);
        Assert.HasCount(0, player0.Players[1].Hand);
        Assert.HasCount(0, player1.Players[0].Hand);
        Assert.IsGreaterThan(0UL, player0.Players[1].HandCount);
        Assert.IsTrue(player0.Players[0].Hand.All(card =>
            card.DefinitionId.HasValue &&
            card.Name.StartsWith("synthetic ", StringComparison.Ordinal) &&
            card.Name.EndsWith($" {card.DefinitionId.Value}", StringComparison.Ordinal)),
            "The v04 regression library must expose only synthetic fixture identities.");

        var query = new ActionQueryRequest(PlayerId.Player0, player0.Revision);
        LegalActionsResult legal = session.ListLegalActions(query);
        ValidTargetsResult targets = session.ListValidTargets(query);
        ValidSlotsResult slots = session.ListValidSlots(query);
        ValidDonorsResult donors = session.ListValidDonors(query);
        ReactionContext reaction0 = session.GetReactionContext(PlayerId.Player0);
        ReactionContext reaction1 = session.GetReactionContext(PlayerId.Player1);
        Assert.AreEqual(player0.Revision, legal.Revision);
        Assert.AreEqual(player0.Revision, targets.Revision);
        Assert.AreEqual(player0.Revision, slots.Revision);
        Assert.AreEqual(player0.Revision, donors.Revision);
        Assert.AreEqual(player0.Revision, reaction0.Revision);
        Assert.AreEqual(player0.Revision, reaction1.Revision);
        Assert.IsFalse(reaction0.Pending);
        Assert.IsFalse(reaction1.Pending);

        LegalAction mulligan = legal.Actions.First(action =>
            action.Command.Action == ActionKind.Mulligan &&
            action.Command.MulliganCards.Count == 1);
        PaymentResult payment = session.PreviewPayment(mulligan.Command);
        Assert.AreEqual(player0.Revision, payment.Revision);
        Assert.IsTrue(payment.Payment.Status.IsSuccess);

        EngineStatus invalidPhase = session.SubmitCommand(
            new GameCommandRequest(PlayerId.Player0, ActionKind.EndTurn, player0.Revision));
        Assert.AreEqual(EngineCode.InvalidPhase, invalidPhase.Code);
        Assert.AreEqual(player0.Revision, session.GetView(PlayerId.Player0).Revision);

        ulong beforeMulliganSequence = session.ReadEvents(PlayerId.Player1, 0).LastSequence;
        Assert.IsTrue(session.SubmitCommand(mulligan.Command).IsSuccess);
        MatchView afterMulligan = session.GetView(PlayerId.Player0);
        Assert.AreEqual(player0.Revision + 1, afterMulligan.Revision);

        EventBatch player0Events = session.ReadNewEvents(PlayerId.Player0);
        EventBatch player1Events = session.ReadNewEvents(PlayerId.Player1);
        Assert.IsGreaterThan(0, player0Events.Events.Count);
        Assert.IsGreaterThan(0, player1Events.Events.Count);
        Assert.AreEqual(player0Events.LastSequence, session.GetEventCursor(PlayerId.Player0));
        Assert.AreEqual(player1Events.LastSequence, session.GetEventCursor(PlayerId.Player1));
        Assert.IsTrue(player0Events.Events.Any(gameEvent =>
            gameEvent.HiddenCard && !gameEvent.Card.HasValue && !gameEvent.DefinitionId.HasValue));
        Assert.IsTrue(player1Events.Events.Any(gameEvent =>
            gameEvent.HiddenCard && !gameEvent.Card.HasValue && !gameEvent.DefinitionId.HasValue));
        foreach (GameEventView gameEvent in player0Events.Events.Concat(player1Events.Events)
                     .Where(gameEvent => gameEvent.HiddenCard))
        {
            Assert.IsTrue(
                SafeHiddenEventTexts.Contains(gameEvent.Text),
                $"Hidden event text is not an approved identity-free diagnostic: {gameEvent.Text}");
        }
        Assert.IsTrue(player1Events.Events.Any(gameEvent =>
            gameEvent.Type == EventType.MulliganCompleted &&
            gameEvent.Player == PlayerId.Player0 &&
            gameEvent.HiddenCard &&
            gameEvent.Value == 0 &&
            gameEvent.Text == "opponent completed mulligan"));
        Assert.IsFalse(player1Events.Events.Any(gameEvent =>
            gameEvent.Sequence > beforeMulliganSequence &&
            gameEvent.Type == EventType.CardDrawn &&
            gameEvent.Player == PlayerId.Player0));

        session.Dispose();
        session.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => session.GetView(PlayerId.Player0));
    }

    [TestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow("midrange")]
    [DataRow("advance")]
    public void ProtocolFixtureLibraryDoesNotAliasRetiredProductDecks(string retiredDeck)
    {
        string nativePath = GetNativeLibraryPath();
        ScgsNativeException error = Assert.ThrowsExactly<ScgsNativeException>(() =>
        {
            using ScgsGameSession unexpected = ScgsGameSession.Create(
                new GameConfigRequest(retiredDeck, "synthetic_beta"), nativePath);
        });
        Assert.AreEqual(NativeCode.SchemaMismatch, error.Code);
    }

    [TestMethod]
    [TestCategory("NativeIntegration")]
    public void NativeFailureRetainsSameThreadDiagnosticAndDoesNotBecomeEngineStatus()
    {
        string nativePath = GetNativeLibraryPath();

        NativeLibraryResolver.Configure(nativePath);
        uint nativeCode = ScgsV04NativeMethods.GetViewJson(
            ulong.MaxValue,
            (uint)PlayerId.Player0,
            nint.Zero,
            0,
            out ulong required);
        Assert.AreEqual((uint)NativeCode.InvalidHandle, nativeCode);
        Assert.AreEqual(0UL, required);

        ScgsNativeException exception = (ScgsNativeException)NativeError.CreateException(nativeCode);
        Assert.AreEqual(NativeCode.InvalidHandle, exception.Code);
        Assert.IsFalse(string.IsNullOrWhiteSpace(exception.Message));
    }

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

        Assert.Inconclusive("Set SCGS_NATIVE_LIBRARY to the v04 synthetic fixture library to run integration tests.");
        throw new InvalidOperationException("MSTest did not terminate an inconclusive test.");
    }
}
