// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class ReactionOriginCompatibilityTests
{
    [TestMethod]
    public void PendingReactionRequiresOriginAndPreservesUnknownOriginAction()
    {
        const string valid = """
            {
              "schema_version": 1,
              "revision": 9,
              "reaction": {
                "pending": true,
                "window": 1,
                "responder": 1,
                "subject": 44,
                "origin": {
                  "action": 99,
                  "player": 0,
                  "source": 44,
                  "target": {"kind": 0, "player": 1}
                },
                "depth": 1,
                "eligible_count": 0,
                "eligible_traps": [],
                "revision": 9
              }
            }
            """;

        ReactionEnvelope envelope = ScgsJson.DeserializeReaction(valid, PlayerId.Player0);
        Assert.IsNotNull(envelope.Reaction.Origin);
        Assert.AreEqual(99U, (uint)envelope.Reaction.Origin.Action);
        Assert.AreEqual(PlayerId.Player0, envelope.Reaction.Origin.Player);
        Assert.AreEqual(Target.Leader(PlayerId.Player1), envelope.Reaction.Origin.Target);

        string missing = valid.Replace(
            """
                "origin": {
                  "action": 99,
                  "player": 0,
                  "source": 44,
                  "target": {"kind": 0, "player": 1}
                },
            """,
            string.Empty,
            StringComparison.Ordinal);
        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            ScgsJson.DeserializeReaction(missing, PlayerId.Player0));
    }

    [TestMethod]
    public void ReactionOriginPlayerAndTargetAreStructuralWhileIdleOriginIsForbidden()
    {
        const string pending = """
            {"schema_version":1,"revision":4,"reaction":{
              "pending":true,"window":3,"responder":0,"subject":70,
              "origin":{"action":4,"player":0,"source":70,
                        "target":{"kind":1,"player":1,"unit":80}},
              "depth":1,"eligible_count":0,"eligible_traps":[],"revision":4}}
            """;

        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            ScgsJson.DeserializeReaction(
                pending.Replace("\"player\":0,\"source\":70", "\"player\":9,\"source\":70"),
                PlayerId.Player1));
        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            ScgsJson.DeserializeReaction(
                pending.Replace("\"kind\":1", "\"kind\":9"),
                PlayerId.Player1));

        const string idleWithOrigin = """
            {"schema_version":1,"revision":4,"reaction":{
              "pending":false,"window":0,"responder":0,"subject":0,
              "origin":{"action":9,"player":0,"source":0},
              "depth":0,"eligible_count":0,"eligible_traps":[],"revision":4}}
            """;
        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            ScgsJson.DeserializeReaction(idleWithOrigin, PlayerId.Player0));
    }
}
