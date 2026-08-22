// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class ModelValidationTests
{
    [TestMethod]
    public void SchemaMismatchAndMissingRequiredShapeAreRejected()
    {
        JsonObject wrongSchema = ParseFixture();
        wrongSchema["schema_version"] = 2;
        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            ScgsJson.DeserializeView(wrongSchema.ToJsonString(), PlayerId.Player0));

        JsonObject missingPlayers = ParseFixture();
        missingPlayers["view"]!.AsObject().Remove("players");
        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            ScgsJson.DeserializeView(missingPlayers.ToJsonString(), PlayerId.Player0));
    }

    [TestMethod]
    public void UnknownOutputFieldsAreIgnoredAndUnknownKeywordBitsArePreserved()
    {
        JsonObject root = ParseFixture();
        root["future_root"] = "ignored";
        root["view"]!["future_view"] = 42;
        root["view"]!["players"]![0]!["hand"]![0]!["keywords"] = 0x8000_0040U;

        ViewEnvelope envelope = ScgsJson.DeserializeView(
            root.ToJsonString(),
            PlayerId.Player0);

        Assert.AreEqual(
            0x8000_0040U,
            (uint)envelope.View.Players[0].Hand[0].Keywords);
    }

    [TestMethod]
    public void UnknownPhasePlayerAndZoneAreRejected()
    {
        JsonObject phase = ParseFixture();
        phase["view"]!["phase"] = 99;
        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            ScgsJson.DeserializeView(phase.ToJsonString(), PlayerId.Player0));

        JsonObject player = ParseFixture();
        player["view"]!["active_player"] = 99;
        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            ScgsJson.DeserializeView(player.ToJsonString(), PlayerId.Player0));

        JsonObject zone = ParseFixture();
        zone["view"]!["players"]![0]!["hand"]![0]!["zone"] = 99;
        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            ScgsJson.DeserializeView(zone.ToJsonString(), PlayerId.Player0));
    }

    [TestMethod]
    public void UnknownEventTypeIsPreservedWhileHiddenIdentifiersAreRejected()
    {
        const string futureEvent = """
            {"schema_version":1,"revision":2,"last_sequence":1,"events":[
              {"sequence":1,"type":99,"player":0,"value":0,"secondary_value":0,
               "hidden_card":false,"text":"future event"}
            ]}
            """;
        EventsEnvelope parsed = ScgsJson.DeserializeEvents(
            futureEvent,
            PlayerId.Player0,
            0);
        Assert.AreEqual(99U, (uint)parsed.Events[0].Type);

        const string leakedEvent = """
            {"schema_version":1,"revision":2,"last_sequence":1,"events":[
              {"sequence":1,"type":3,"player":1,"card":77,"definition_id":8,
               "value":0,"secondary_value":0,"hidden_card":true,"text":"opponent drew a card"}
            ]}
            """;
        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            ScgsJson.DeserializeEvents(leakedEvent, PlayerId.Player0, 0));
    }

    [TestMethod]
    public void ExplicitNullsMissingVisibleIdentityAndUnknownStructuralEnumsAreRejected()
    {
        JsonObject nullSkill = ParseFixture();
        nullSkill["view"]!["players"]![0]!["leader_skill"] = null;
        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            ScgsJson.DeserializeView(nullSkill.ToJsonString(), PlayerId.Player0));

        JsonObject missingIdentity = ParseFixture();
        missingIdentity["view"]!["players"]![0]!["hand"]![0]!.AsObject()
            .Remove("instance_id");
        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            ScgsJson.DeserializeView(missingIdentity.ToJsonString(), PlayerId.Player0));

        JsonObject nullNestedDefinition = ParseFixture();
        nullNestedDefinition["view"]!["players"]![0]!["hand"]![0]!["definition"]!["effects"] = null;
        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            ScgsJson.DeserializeView(nullNestedDefinition.ToJsonString(), PlayerId.Player0));

        JsonObject unknownDefinitionKind = ParseFixture();
        unknownDefinitionKind["view"]!["players"]![0]!["hand"]![0]!["definition"]!["kind"] = 99;
        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            ScgsJson.DeserializeView(unknownDefinitionKind.ToJsonString(), PlayerId.Player0));
    }

    [TestMethod]
    public void OpponentFaceDownTacticAcceptsOnlyTheAnonymousCardShape()
    {
        JsonObject root = ParseFixture();
        JsonObject hidden = root["view"]!["players"]![0]!["hand"]![0]!
            .DeepClone().AsObject();
        hidden.Remove("instance_id");
        hidden.Remove("definition_id");
        hidden.Remove("definition");
        hidden.Remove("kind");
        hidden["name"] = string.Empty;
        hidden["owner"] = 1;
        hidden["controller"] = 1;
        hidden["zone"] = 4;
        hidden["face_down"] = true;
        root["view"]!["players"]![1]!["tactics"]![0] = hidden;

        ViewEnvelope envelope = ScgsJson.DeserializeView(
            root.ToJsonString(),
            PlayerId.Player0);
        CardView parsed = envelope.View.Players[1].Tactics[0]!;
        Assert.IsTrue(parsed.FaceDown);
        Assert.IsFalse(parsed.InstanceId.HasValue);
        Assert.IsFalse(parsed.DefinitionId.HasValue);

        hidden["name"] = "leaked";
        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            ScgsJson.DeserializeView(root.ToJsonString(), PlayerId.Player0));
    }

    private static JsonObject ParseFixture() =>
        JsonNode.Parse(CreateFixture())!.AsObject();

    private static string CreateFixture()
    {
        var emptyComponent = new ComponentSpec
        {
            HasComponent = false,
            GrantedKind = EffectKind.DrawCards,
            GrantedAmount = 0,
        };
        var definition = new CardDefinition
        {
            Id = 1,
            Name = "fixture",
            Kind = CardKind.Unit,
            Cost = 1,
            Attack = 1,
            Health = 1,
            Countdown = 0,
            PrintedGuard = false,
            PrintedRush = false,
            PrintedStorm = false,
            PrintedBarrier = false,
            PrintedLifesteal = false,
            PrintedBane = false,
            EvolvedAttack = 0,
            EvolvedHealth = 0,
            AdditionalCost = new AdditionalCost { BurnPpCapacity = 0 },
            Component = emptyComponent,
            Effects = [],
        };
        var card = new CardView
        {
            InstanceId = 10,
            DefinitionId = 1,
            Definition = definition,
            Kind = CardKind.Unit,
            Name = "fixture",
            Owner = PlayerId.Player0,
            Controller = PlayerId.Player0,
            Zone = Zone.Hand,
            Sequence = 0,
            Cost = 1,
            CurrentAttack = 1,
            CurrentHealth = 1,
            MaximumHealth = 1,
            Keywords = Keyword.None,
            Evolved = false,
            AttackedThisTurn = false,
            EnteredThisTurn = false,
            TemporaryRush = false,
            DeployedFromStandby = false,
            FaceDown = false,
            Countdown = 0,
            GrantedComponent = emptyComponent,
        };
        var skill = new LeaderSkillDefinition
        {
            Name = "fixture skill",
            Cost = 0,
            Effects = [],
        };

        PlayerView MakePlayer(PlayerId player) => new()
        {
            Player = player,
            LeaderHealth = 20,
            MaximumLeaderHealth = 20,
            CurrentPp = 0,
            PpCapacity = 0,
            Cracks = 0,
            EvolutionEnergy = 0,
            OwnTurnNumber = 0,
            FatigueCount = 0,
            MulliganDone = false,
            EvolutionUsedThisTurn = false,
            AdvanceUsedThisTurn = false,
            DeployUsedThisTurn = false,
            TrapSetThisTurn = false,
            LeaderSkillUsed = false,
            ChargeGrantedThisCycle = false,
            FriendlyDeathsThisCycle = 0,
            SpellsUsedThisTurn = 0,
            UnitsPlayedThisTurn = 0,
            LeaderSkill = skill,
            DeckCount = 37,
            HandCount = player == PlayerId.Player0 ? 1UL : 3UL,
            Hand = player == PlayerId.Player0 ? [card] : [],
            Units = new CardView?[5],
            Tactics = new CardView?[3],
            Graveyard = [],
            Archive = [],
            Standby = [],
        };

        var reaction = new ReactionContext
        {
            Pending = false,
            Window = ReactionWindow.None,
            Responder = PlayerId.Player0,
            Subject = 0,
            Depth = 0,
            EligibleCount = 0,
            EligibleTraps = [],
            Revision = 5,
        };
        var envelope = new ViewEnvelope
        {
            SchemaVersion = ScgsV04Contract.SchemaVersion,
            Revision = 5,
            View = new MatchView
            {
                Viewer = PlayerId.Player0,
                ActivePlayer = PlayerId.Player0,
                FirstPlayer = PlayerId.Player0,
                RandomSeed = 7,
                Phase = MatchPhase.Mulligan,
                Result = GameResult.Ongoing,
                Revision = 5,
                Players = [MakePlayer(PlayerId.Player0), MakePlayer(PlayerId.Player1)],
                Reaction = reaction,
            },
        };
        return JsonSerializer.Serialize(envelope, ScgsJson.Options);
    }
}
