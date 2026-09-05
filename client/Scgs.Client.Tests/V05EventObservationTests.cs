// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using V05 = Scgs.Client.V05;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class V05EventObservationTests
{
    [TestMethod]
    public void PreObservationSchemaTwoEventsRemainCompatibleAndOmitTheOptionalMember()
    {
        JsonObject payload = Payload();
        Event(payload).Remove("observation");
        V05.GameEventView result = Read(payload);
        Assert.IsNull(result.Observation);
        Assert.IsFalse(JsonSerializer.Serialize(result, V05.ScgsV05Json.Options).Contains("observation", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MoveFactsPreserveEndpointsLocationCauseAndOccurrenceRevision()
    {
        V05.ProductEventObservation result = Read(Payload()).Observation!;
        Assert.AreEqual(1U, result.Version);
        Assert.AreEqual(7UL, result.Revision);
        Assert.AreEqual(3UL, result.CauseSequence);
        Assert.IsTrue(result.PublicToAll);
        Assert.IsTrue(result.IsKnownKind);
        Assert.AreEqual("move", result.Kind);
        Assert.AreEqual("LO-11", result.Subject!.DesignId);
        Assert.AreEqual(77UL, result.Subject.Card);
        Assert.AreEqual(V05.Zone.Hand, result.From!.Zone);
        Assert.IsNull(result.From.Slot);
        Assert.AreEqual(V05.Zone.MainBoard, result.To!.Zone);
        Assert.AreEqual(4UL, result.To.Slot);
    }

    [TestMethod]
    public void AllKnownObservationKindsDecodeWithoutDerivingResultsFromText()
    {
        foreach (string kind in new[] { "move", "damage", "heal", "evolve", "state_change", "declaration" })
        {
            JsonObject payload = Payload();
            JsonObject observation = Observation(payload);
            observation["kind"] = kind;
            observation["source"] = PublicCard();
            observation["actual_amount"] = 3;
            observation["damage_kind"] = "combat";
            observation["barrier_consumed"] = false;
            observation["before"] = new JsonObject { ["health"] = 6, ["evolved"] = false };
            observation["after"] = new JsonObject { ["health"] = 3, ["evolved"] = true };
            Event(payload)["text"] = "This text is intentionally not an animation instruction.";
            V05.ProductEventObservation result = Read(payload).Observation!;
            Assert.AreEqual(kind, result.Kind);
            Assert.IsTrue(result.IsKnownKind);
            Assert.AreEqual(3, result.ActualAmount);
            Assert.AreEqual(6, result.Before!.Health);
            Assert.AreEqual(3, result.After!.Health);
        }
    }

    [TestMethod]
    public void UnknownKindAndUnknownOutputFieldsAreSafeGenericForwardCompatibility()
    {
        JsonObject payload = Payload();
        JsonObject observation = Observation(payload);
        observation["kind"] = "future_effect";
        observation["future_output"] = new JsonObject { ["value"] = null };
        V05.ProductEventObservation result = Read(payload).Observation!;
        Assert.AreEqual("future_effect", result.Kind);
        Assert.IsFalse(result.IsKnownKind);
        Assert.IsFalse(JsonSerializer.Serialize(result, V05.ScgsV05Json.Options).Contains("is_known_kind", StringComparison.Ordinal));

        observation["subject"]!["player"] = 42;
        Reject(payload); // Unknown does not mean that structural safety is skipped.
    }

    [TestMethod]
    public void DeclarationSubkindIsPreservedAndPublicFactsCannotContainHiddenEndpoints()
    {
        JsonObject payload = Payload();
        Observation(payload)["kind"] = "declaration";
        Observation(payload)["source"] = PublicCard();
        Observation(payload)["declaration_kind"] = "attack_cancelled";
        Assert.AreEqual("attack_cancelled", Read(payload).Observation!.DeclarationKind);
        Observation(payload)["source"] = new JsonObject
        {
            ["kind"] = "card", ["player"] = 0, ["hidden"] = true,
        };
        Reject(payload);
    }

    [TestMethod]
    public void MissingRequiredFieldsUnknownVersionsAndExplicitNullAreRejected()
    {
        foreach (string property in new[] { "version", "revision", "kind", "cause_sequence", "public_to_all" })
        {
            JsonObject payload = Payload();
            Observation(payload).Remove(property);
            Reject(payload);
        }

        foreach (uint version in new[] { 0U, 2U, uint.MaxValue })
        {
            JsonObject payload = Payload();
            Observation(payload)["version"] = version;
            Reject(payload);
        }

        foreach (string property in new[] { "source", "target", "before", "actual_amount", "move_reason" })
        {
            JsonObject payload = Payload();
            Observation(payload)[property] = null;
            Reject(payload);
        }

        JsonObject nullObservation = Payload();
        Event(nullObservation)["observation"] = null;
        Reject(nullObservation);
    }

    [TestMethod]
    public void CauseAndRevisionCannotReferToFutureButZeroCauseIsValid()
    {
        JsonObject payload = Payload();
        Observation(payload)["cause_sequence"] = 0UL;
        Assert.AreEqual(0UL, Read(payload).Observation!.CauseSequence);
        Observation(payload)["cause_sequence"] = 11UL;
        Reject(payload);
        Observation(payload)["cause_sequence"] = 3UL;
        Observation(payload)["revision"] = 10UL;
        Reject(payload);
    }

    [TestMethod]
    public void LeaderEndpointsAndVisibleCardIdentitiesHaveDistinctStrictShapes()
    {
        JsonObject payload = Payload();
        Observation(payload)["kind"] = "declaration";
        Observation(payload)["source"] = PublicCard();
        Observation(payload)["target"] = Leader();
        Assert.AreEqual("leader", Read(payload).Observation!.Target!.Kind);

        foreach (string property in new[] { "kind", "player", "hidden" })
        {
            JsonObject invalid = Payload();
            Observation(invalid)["subject"]!.AsObject().Remove(property);
            Reject(invalid);
        }

        foreach (JsonObject endpoint in new[]
        {
            new JsonObject { ["kind"] = "leader", ["player"] = 0, ["hidden"] = true },
            new JsonObject { ["kind"] = "leader", ["player"] = 0, ["hidden"] = false, ["card"] = 77UL },
            new JsonObject { ["kind"] = "card", ["player"] = 0, ["hidden"] = false },
            new JsonObject { ["kind"] = "card", ["player"] = 0, ["hidden"] = false, ["card"] = 0UL, ["design_id"] = "LO-11" },
            new JsonObject { ["kind"] = "card", ["player"] = 0, ["hidden"] = false, ["card"] = 77UL, ["design_id"] = " " },
            new JsonObject { ["kind"] = "card", ["player"] = 9, ["hidden"] = true },
            new JsonObject { ["kind"] = "future_endpoint", ["player"] = 0, ["hidden"] = false },
        })
        {
            Observation(payload)["target"] = endpoint;
            Reject(payload);
        }
    }

    [TestMethod]
    public void PrivateSlotsInvalidZonesAndMissingPublicSlotsAreRejected()
    {
        foreach ((int zone, ulong? slot) in new (int, ulong?)[]
        {
            (1, 0), (2, 0), (3, null), (3, 5), (4, null), (4, 3),
            (5, 0), (6, 0), (7, 0), (8, 0), (99, null),
        })
        {
            JsonObject payload = Payload();
            var location = new JsonObject { ["player"] = 0, ["zone"] = zone };
            if (slot.HasValue)
            {
                location["slot"] = slot.Value;
            }

            Observation(payload)["to"] = location;
            Reject(payload);
        }

        JsonObject fieldPayload = Payload();
        Observation(fieldPayload)["to"] = new JsonObject { ["player"] = 0, ["zone"] = 8 };
        Assert.AreEqual(V05.Zone.Field, Read(fieldPayload).Observation!.To!.Zone);
    }

    [TestMethod]
    public void HiddenObservationsCarryNoStableIdentityOrDerivedState()
    {
        JsonObject payload = HiddenPayload();
        V05.ProductEventObservation result = Read(payload).Observation!;
        Assert.IsTrue(result.Subject!.Hidden);
        Assert.IsNull(result.Subject.Card);
        Assert.IsNull(result.Subject.DesignId);
        Assert.IsNull(result.Before);
        Assert.IsNull(result.After);

        foreach (string endpointProperty in new[] { "source", "subject", "target" })
        {
            JsonObject invalid = HiddenPayload();
            Observation(invalid)[endpointProperty] = PublicCard();
            Reject(invalid);
        }

        foreach (string stateProperty in new[] { "before", "after" })
        {
            JsonObject invalid = HiddenPayload();
            Observation(invalid)[stateProperty] = new JsonObject { ["health"] = 8 };
            Reject(invalid);
        }

        foreach (string identity in new[] { "card", "design_id" })
        {
            JsonObject invalid = HiddenPayload();
            Observation(invalid)["subject"]![identity] = identity == "card"
                ? JsonValue.Create(77UL) : JsonValue.Create("LO-11");
            Reject(invalid);
        }
    }

    [TestMethod]
    public void RedactedAndOwnerObservationsCanBeReadIndependentlyWithoutMutatingEachOther()
    {
        JsonObject ownerPayload = Payload();
        Observation(ownerPayload)["public_to_all"] = false;
        V05.ProductEventObservation owner = Read(ownerPayload, V05.PlayerId.Player0).Observation!;
        JsonObject opponentPayload = HiddenPayload();
        Observation(opponentPayload)["public_to_all"] = false;
        V05.ProductEventObservation opponent = Read(opponentPayload, V05.PlayerId.Player1).Observation!;
        Assert.AreEqual("LO-11", owner.Subject!.DesignId);
        Assert.IsNull(opponent.Subject!.DesignId);
        Assert.IsFalse(owner.PublicToAll);
        Assert.IsFalse(opponent.PublicToAll);
        Assert.AreEqual("LO-11", Read(ownerPayload, V05.PlayerId.Player0).Observation!.Subject!.DesignId);
    }

    [TestMethod]
    public void StatePreservesZeroMultiDigitValuesAndUnknownKeywordBits()
    {
        JsonObject payload = Payload();
        Observation(payload)["kind"] = "state_change";
        Observation(payload)["before"] = new JsonObject { ["health"] = 0, ["attack"] = 0 };
        Observation(payload)["after"] = new JsonObject
        {
            ["health"] = 120, ["max_health"] = 150, ["attack"] = 123,
            ["countdown"] = 12, ["evolved"] = true, ["keywords"] = 0x8000_0000U,
        };
        V05.ProductEventObservation result = Read(payload).Observation!;
        Assert.AreEqual(0, result.Before!.Attack);
        Assert.AreEqual(120, result.After!.Health);
        Assert.AreEqual(150, result.After.MaxHealth);
        Assert.AreEqual(123, result.After.Attack);
        Assert.AreEqual(12, result.After.Countdown);
        Assert.AreEqual(0x8000_0000U, (uint)result.After.Keywords!.Value);
    }

    [TestMethod]
    public void KnownKindsMustContainRequiredFactsAndAmountsCannotBeNegative()
    {
        foreach ((string kind, string missing) in new[]
        {
            ("move", "from"), ("move", "to"), ("move", "move_reason"),
            ("move", "subject"), ("damage", "actual_amount"), ("heal", "actual_amount"),
            ("evolve", "before"), ("state_change", "before"), ("declaration", "source"),
        })
        {
            JsonObject payload = Payload();
            Observation(payload)["kind"] = kind;
            Observation(payload).Remove(missing);
            Reject(payload);
        }

        JsonObject negative = Payload();
        Observation(negative)["actual_amount"] = -1;
        Reject(negative);
    }

    private static JsonObject Payload() => JsonNode.Parse("""
        {
          "schema_version": 2, "revision": 9, "last_sequence": 10,
          "events": [{
            "sequence": 10, "type": 8, "player": 0, "value": 0,
            "secondary_value": 0, "hidden_card": false, "text": "played",
            "card": 77, "design_id": "LO-11",
            "observation": {
              "version": 1, "revision": 7, "kind": "move",
              "cause_sequence": 3, "public_to_all": true,
              "subject": { "kind": "card", "player": 0, "hidden": false, "card": 77, "design_id": "LO-11" },
              "from": { "player": 0, "zone": 2 },
              "to": { "player": 0, "zone": 3, "slot": 4 },
              "move_reason": "play"
            }
          }]
        }
        """)!.AsObject();

    private static JsonObject HiddenPayload()
    {
        JsonObject payload = Payload();
        JsonObject gameEvent = Event(payload);
        gameEvent["hidden_card"] = true;
        gameEvent["text"] = "opponent drew a card";
        gameEvent.Remove("card");
        gameEvent.Remove("design_id");
        JsonObject observation = Observation(payload);
        observation["subject"] = new JsonObject { ["kind"] = "card", ["player"] = 0, ["hidden"] = true };
        observation["public_to_all"] = false;
        observation["from"] = new JsonObject { ["player"] = 0, ["zone"] = 1 };
        observation["to"] = new JsonObject { ["player"] = 0, ["zone"] = 2 };
        return payload;
    }

    private static JsonObject PublicCard() => new()
    {
        ["kind"] = "card", ["player"] = 0, ["hidden"] = false,
        ["card"] = 77UL, ["design_id"] = "LO-11",
    };

    private static JsonObject Leader() => new()
    {
        ["kind"] = "leader", ["player"] = 1, ["hidden"] = false,
    };

    private static JsonObject Event(JsonObject payload) => payload["events"]![0]!.AsObject();
    private static JsonObject Observation(JsonObject payload) => Event(payload)["observation"]!.AsObject();

    private static V05.GameEventView Read(JsonObject payload, V05.PlayerId viewer = V05.PlayerId.Player0) =>
        V05.ScgsV05Json.DeserializeEvents(payload.ToJsonString(), viewer, 9).Events.Single();

    private static void Reject(JsonObject payload) =>
        Assert.ThrowsExactly<ScgsProtocolException>(() => Read(payload));
}
