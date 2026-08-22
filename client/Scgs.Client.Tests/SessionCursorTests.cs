// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class SessionCursorTests
{
    [TestMethod]
    public void ViewerCursorsAdvanceIndependentlyAndDisposeOnce()
    {
        var backend = new CursorBackend();
        var session = ScgsGameSession.CreateForTesting(backend);

        EventBatch firstPlayer0 = session.ReadNewEvents(PlayerId.Player0);
        EventBatch secondPlayer0 = session.ReadNewEvents(PlayerId.Player0);
        EventBatch firstPlayer1 = session.ReadNewEvents(PlayerId.Player1);

        Assert.AreEqual(1UL, firstPlayer0.LastSequence);
        Assert.HasCount(1, firstPlayer0.Events);
        Assert.HasCount(0, secondPlayer0.Events);
        Assert.AreEqual(1UL, firstPlayer1.LastSequence);
        Assert.AreEqual(1UL, session.GetEventCursor(PlayerId.Player0));
        Assert.AreEqual(1UL, session.GetEventCursor(PlayerId.Player1));
        CollectionAssert.AreEqual(
            new[]
            {
                (PlayerId.Player0, 0UL),
                (PlayerId.Player0, 1UL),
                (PlayerId.Player1, 0UL),
            },
            backend.Reads);

        session.Dispose();
        session.Dispose();
        Assert.AreEqual(1, backend.DisposeCount);
        Assert.ThrowsExactly<ObjectDisposedException>(() => session.GetEventCursor(PlayerId.Player0));
    }

    [TestMethod]
    public void SafeHandleInvokesDestroyExactlyOnceAndNeverRetriesFailure()
    {
        const ulong token = 0xF123_4567_89AB_CDEFUL;
        int destroys = 0;
        var handle = new ScgsV04SafeHandle(
            token,
            observedToken =>
            {
                Assert.AreEqual(token, observedToken);
                ++destroys;
                return (uint)NativeCode.InvalidHandle;
            });

        handle.Dispose();
        handle.Dispose();

        Assert.AreEqual(1, destroys);
        Assert.IsTrue(handle.IsClosed);
    }

    [TestMethod]
    public void InvalidEventPayloadDoesNotAdvanceViewerCursor()
    {
        var backend = new CursorBackend { FailNextRead = true };
        using var session = ScgsGameSession.CreateForTesting(backend);

        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            session.ReadNewEvents(PlayerId.Player0));
        Assert.AreEqual(0UL, session.GetEventCursor(PlayerId.Player0));

        EventBatch recovered = session.ReadNewEvents(PlayerId.Player0);
        Assert.AreEqual(1UL, recovered.LastSequence);
        Assert.AreEqual(1UL, session.GetEventCursor(PlayerId.Player0));
        CollectionAssert.AreEqual(
            new[] { (PlayerId.Player0, 0UL), (PlayerId.Player0, 0UL) },
            backend.Reads);
    }

    private sealed class CursorBackend : IScgsNativeGameBackend
    {
        internal List<(PlayerId Viewer, ulong After)> Reads { get; } = [];

        internal int DisposeCount { get; private set; }

        internal bool FailNextRead { get; set; }

        public EngineStatus Start() => new() { RawCode = 0, Message = string.Empty };

        public string ReadEvents(PlayerId viewer, ulong afterSequence)
        {
            Reads.Add((viewer, afterSequence));
            if (FailNextRead)
            {
                FailNextRead = false;
                return "{\"schema_version\":1,\"revision\":0,\"last_sequence\":1,\"events\":null}";
            }

            string events = afterSequence == 0
                ? $$"""[{"sequence":1,"type":0,"player":{{(uint)viewer}},"value":0,"secondary_value":0,"hidden_card":false,"text":"started","random_seed":7,"first_player":0}]"""
                : "[]";
            ulong last = afterSequence == 0 ? 1UL : afterSequence;
            return $$"""{"schema_version":1,"revision":0,"last_sequence":{{last}},"events":{{events}}}""";
        }

        public void Dispose() => ++DisposeCount;

        public string GetView(PlayerId viewer) => throw new NotSupportedException();

        public string ListLegalActions(string queryJson) => throw new NotSupportedException();

        public string ListValidTargets(string queryJson) => throw new NotSupportedException();

        public string ListValidSlots(string queryJson) => throw new NotSupportedException();

        public string ListValidDonors(string queryJson) => throw new NotSupportedException();

        public string PreviewPayment(string commandJson) => throw new NotSupportedException();

        public string GetReactionContext(PlayerId viewer) => throw new NotSupportedException();

        public EngineStatus SubmitCommand(string commandJson) => throw new NotSupportedException();
    }
}
