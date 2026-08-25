// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.VisualStudio.TestTools.UnitTesting;
using V05 = Scgs.Client.V05;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class V05SessionTests
{
    [TestMethod]
    public void ViewerEventCursorsAreIndependentAndDisposeIsIdempotent()
    {
        var backend = new CursorBackend();
        V05.ScgsV05GameSession session = V05.ScgsV05GameSession.CreateForTesting(backend);

        Assert.AreEqual(2UL, session.ReadNewEvents(V05.PlayerId.Player0).LastSequence);
        Assert.AreEqual(0UL, session.GetEventCursor(V05.PlayerId.Player1));
        Assert.AreEqual(2UL, session.ReadNewEvents(V05.PlayerId.Player1).LastSequence);
        Assert.AreEqual(2UL, session.GetEventCursor(V05.PlayerId.Player0));
        Assert.AreEqual(2UL, session.GetEventCursor(V05.PlayerId.Player1));

        session.Dispose();
        session.Dispose();
        Assert.AreEqual(1, backend.DisposeCount);
        Assert.ThrowsExactly<ObjectDisposedException>(() =>
            session.ReadNewEvents(V05.PlayerId.Player0));
    }

    private sealed class CursorBackend : V05.IScgsV05NativeGameBackend
    {
        public int DisposeCount { get; private set; }

        public V05.EngineStatus Start() => new() { RawCode = 0, Message = "ok" };

        public string ReadEvents(V05.PlayerId viewer, ulong afterSequence)
        {
            _ = viewer;
            return afterSequence == 0
                ? """
                  {"schema_version":2,"revision":3,"last_sequence":2,"events":[
                    {"sequence":1,"type":0,"player":0,"value":0,"secondary_value":0,
                     "hidden_card":false,"text":"match started","first_player":0},
                    {"sequence":2,"type":1,"player":0,"value":0,"secondary_value":0,
                     "hidden_card":false,"text":"turn started"}]}
                  """
                : $"{{\"schema_version\":2,\"revision\":3,\"last_sequence\":{afterSequence},\"events\":[]}}";
        }

        public void Dispose() => ++DisposeCount;

        public string GetView(V05.PlayerId viewer) => throw new NotSupportedException();
        public string ListLegalActions(string queryJson) => throw new NotSupportedException();
        public string ListValidTargets(string queryJson) => throw new NotSupportedException();
        public string ListValidSlots(string queryJson) => throw new NotSupportedException();
        public string ListValidDonors(string queryJson) => throw new NotSupportedException();
        public string PreviewPayment(string commandJson) => throw new NotSupportedException();
        public string GetReactionContext(V05.PlayerId viewer) => throw new NotSupportedException();
        public V05.EngineStatus SubmitCommand(string commandJson) => throw new NotSupportedException();
    }
}
