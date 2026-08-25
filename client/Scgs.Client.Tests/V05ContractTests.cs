// SPDX-License-Identifier: GPL-3.0-or-later
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using V05 = Scgs.Client.V05;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class V05ContractTests
{
    [TestMethod]
    public void FrozenSchemaTwoConstantsAndEnumsMatchNativeHeader()
    {
        Assert.AreEqual(
            0x0002_0000U,
            (uint)typeof(V05.ScgsV05Contract)
                .GetField(nameof(V05.ScgsV05Contract.AbiVersion))!.GetRawConstantValue()!);
        Assert.AreEqual(
            2U,
            (uint)typeof(V05.ScgsV05Contract)
                .GetField(nameof(V05.ScgsV05Contract.SchemaVersion))!.GetRawConstantValue()!);
        Assert.AreEqual(
            0xFFFF_FFFFU,
            (uint)typeof(V05.ScgsV05Contract)
                .GetField(nameof(V05.ScgsV05Contract.NoEngineCode))!.GetRawConstantValue()!);
        AssertEnumValues<V05.NativeCode>(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        AssertEnumValues<V05.PlayerId>(0, 1);
        AssertEnumValues<V05.FirstPlayerMode>(0, 1, 2);
        AssertEnumValues<V05.ActionKind>(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13);
        AssertEnumValues<V05.TargetKind>(0, 1);
        AssertEnumValues<V05.CardKind>(0, 1, 2, 3, 4);
        AssertEnumValues<V05.Zone>(0, 1, 2, 3, 4, 5, 6, 7, 8);
        AssertEnumValues<V05.Keyword>(0, 1, 2, 4, 8, 16, 32, 64);
    }

    [TestMethod]
    public void UnknownFutureKeywordBitsRoundTripWithoutBeingDiscarded()
    {
        const uint futureBit = 0x8000_0000U;
        string json = JsonSerializer.Serialize(
            (V05.Keyword)futureBit,
            V05.ScgsV05Json.Options);
        V05.Keyword restored = JsonSerializer.Deserialize<V05.Keyword>(
            json,
            V05.ScgsV05Json.Options);
        Assert.AreEqual(futureBit, (uint)restored);
    }

    [TestMethod]
    public void LibraryImportSurfaceContainsExactlyFourteenV05CdeclExports()
    {
        string[] expected =
        [
            "scgs_v05_abi_version",
            "scgs_v05_create",
            "scgs_v05_destroy",
            "scgs_v05_start",
            "scgs_v05_get_view_json",
            "scgs_v05_list_legal_actions_json",
            "scgs_v05_list_valid_targets_json",
            "scgs_v05_list_valid_slots_json",
            "scgs_v05_list_valid_donors_json",
            "scgs_v05_preview_payment_json",
            "scgs_v05_get_reaction_context_json",
            "scgs_v05_submit_command_json",
            "scgs_v05_read_events_json",
            "scgs_v05_get_last_error",
        ];

        MethodInfo[] imports = typeof(V05.ScgsV05NativeMethods)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(method => method.GetCustomAttribute<LibraryImportAttribute>() is not null)
            .ToArray();
        Assert.HasCount(14, imports);
        CollectionAssert.AreEquivalent(
            expected,
            imports.Select(method =>
                method.GetCustomAttribute<LibraryImportAttribute>()!.EntryPoint).ToArray());
        foreach (MethodInfo method in imports)
        {
            Assert.AreEqual(typeof(uint), method.ReturnType);
            UnmanagedCallConvAttribute? convention =
                method.GetCustomAttribute<UnmanagedCallConvAttribute>();
            Assert.IsNotNull(convention);
            CollectionAssert.Contains(convention.CallConvs, typeof(CallConvCdecl));
            Assert.IsTrue(method.GetParameters().All(parameter =>
                parameter.ParameterType == typeof(uint) ||
                parameter.ParameterType == typeof(ulong) ||
                parameter.ParameterType == typeof(nint) ||
                parameter.ParameterType == typeof(uint).MakeByRefType() ||
                parameter.ParameterType == typeof(ulong).MakeByRefType()));
        }
    }

    [TestMethod]
    public void ExtendedRequestsUseSchemaTwoAndOmitAbsentOptionals()
    {
        var config = new V05.GameConfigRequest(
            "oathguard_luminous_oath_v1",
            "pactmage_abyssal_pact_v1")
        {
            RandomSeed = 7,
            FirstPlayerMode = V05.FirstPlayerMode.Player0,
            ShuffleDecks = false,
        };
        using JsonDocument configJson = JsonDocument.Parse(V05.ScgsV05Json.SerializeConfig(config));
        Assert.AreEqual(2U, configJson.RootElement.GetProperty("schema_version").GetUInt32());

        var command = new V05.GameCommandRequest(
            V05.PlayerId.Player0,
            V05.ActionKind.ResolveChoice,
            9)
        {
            ChoiceId = "choice-9",
            SelectedOptionIds = ["option-b", "option-a"],
        };
        using JsonDocument commandJson = JsonDocument.Parse(V05.ScgsV05Json.SerializeCommand(command));
        JsonElement root = commandJson.RootElement;
        Assert.AreEqual(2U, root.GetProperty("schema_version").GetUInt32());
        Assert.AreEqual("choice-9", root.GetProperty("choice_id").GetString());
        CollectionAssert.AreEqual(
            new[] { "option-b", "option-a" },
            root.GetProperty("selected_option_ids")
                .EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.IsFalse(root.TryGetProperty("mode_id", out _));
        Assert.IsFalse(root.TryGetProperty("additional_cost_cards", out _));
        Assert.IsFalse(root.TryGetProperty("source", out _));
        Assert.IsFalse(root.TryGetProperty("use_advance", out _));
        Assert.IsFalse(root.TryGetProperty("mulligan_cards", out _));
        Assert.IsFalse(root.TryGetProperty("target", out _));
        Assert.IsFalse(root.TryGetProperty("slot", out _));

        var query = new V05.ActionQueryRequest(V05.PlayerId.Player0, 9)
        {
            Action = V05.ActionKind.Deploy,
            Source = 77,
            ModeId = "repair",
            AdditionalCostCards = [101],
            UseAdvance = false,
        };
        using JsonDocument queryJson = JsonDocument.Parse(V05.ScgsV05Json.SerializeQuery(query));
        JsonElement queryRoot = queryJson.RootElement;
        Assert.AreEqual(2U, queryRoot.GetProperty("schema_version").GetUInt32());
        Assert.AreEqual("repair", queryRoot.GetProperty("mode_id").GetString());
        Assert.AreEqual(77UL, queryRoot.GetProperty("source").GetUInt64());
        Assert.AreEqual(101UL, queryRoot.GetProperty("additional_cost_cards")[0].GetUInt64());
        Assert.IsFalse(queryRoot.TryGetProperty("choice_id", out _));
        Assert.IsFalse(queryRoot.TryGetProperty("selected_option_ids", out _));
    }

    [TestMethod]
    public void UnrelatedActionFieldsAreRejectedBeforeNativeAccess()
    {
        var endTurnWithMode = new V05.GameCommandRequest(
            V05.PlayerId.Player0,
            V05.ActionKind.EndTurn,
            4)
        {
            ModeId = "not-relevant",
        };
        var mulliganWithAdvance = new V05.GameCommandRequest(
            V05.PlayerId.Player0,
            V05.ActionKind.Mulligan,
            4)
        {
            UseAdvance = true,
        };
        var resolveWithAdditionalCost = new V05.GameCommandRequest(
            V05.PlayerId.Player0,
            V05.ActionKind.ResolveChoice,
            4)
        {
            ChoiceId = "choice",
            AdditionalCostCards = [101],
        };
        var unscopedQuery = new V05.ActionQueryRequest(V05.PlayerId.Player0, 4)
        {
            Slot = 0,
        };

        Assert.ThrowsExactly<ArgumentException>(() =>
            V05.ScgsV05Json.SerializeCommand(endTurnWithMode));
        Assert.ThrowsExactly<ArgumentException>(() =>
            V05.ScgsV05Json.SerializeCommand(mulliganWithAdvance));
        Assert.ThrowsExactly<ArgumentException>(() =>
            V05.ScgsV05Json.SerializeCommand(resolveWithAdditionalCost));
        Assert.ThrowsExactly<ArgumentException>(() =>
            V05.ScgsV05Json.SerializeQuery(unscopedQuery));
    }

    [TestMethod]
    public void PermanentTargetsAndResolveChoiceAreStructurallyValidated()
    {
        var missingPermanent = new V05.GameCommandRequest(
            V05.PlayerId.Player0,
            V05.ActionKind.Attack,
            1)
        {
            Target = new V05.Target(V05.TargetKind.Permanent, V05.PlayerId.Player1),
        };
        Assert.ThrowsExactly<ArgumentException>(() =>
            V05.ScgsV05Json.SerializeCommand(missingPermanent));

        var missingChoice = new V05.GameCommandRequest(
            V05.PlayerId.Player0,
            V05.ActionKind.ResolveChoice,
            1);
        Assert.ThrowsExactly<ArgumentException>(() =>
            V05.ScgsV05Json.SerializeCommand(missingChoice));
    }

    [TestMethod]
    public void UnknownInboundStructuralEnumIsReportedAsProtocolFailure()
    {
        const string targets = """
            {
              "schema_version":2,
              "revision":1,
              "targets":[{"kind":99,"player":0}]
            }
            """;
        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            V05.ScgsV05Json.DeserializeTargets(targets));
    }

    [TestMethod]
    public void UnknownOutputFieldsAreIgnoredWithoutWeakeningStructuralValidation()
    {
        const string futureOutput = """
            {
              "schema_version":2,
              "revision":1,
              "future_envelope_value":"safe",
              "future_nullable":null,
              "targets":[
                {"kind":0,"player":1,"future_target_value":{"version":3}}
              ]
            }
            """;

        var envelope = V05.ScgsV05Json.DeserializeTargets(futureOutput);
        Assert.HasCount(1, envelope.Targets);
        Assert.AreEqual(V05.TargetKind.Leader, envelope.Targets[0].Kind);
        Assert.AreEqual(V05.PlayerId.Player1, envelope.Targets[0].Player);
    }

    [TestMethod]
    public void ViewerPayloadsRejectSeedAndOpponentChoiceTokens()
    {
        const string seedLeak = """
            {"schema_version":2,"revision":1,"random_seed":7,"view":{}}
            """;
        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            V05.ScgsV05Json.DeserializeView(seedLeak, V05.PlayerId.Player0));

        const string choiceLeak = """
            {
              "schema_version":2,
              "revision":4,
              "reaction":{
                "pending":false,"window":0,"responder":0,"subject":0,
                "depth":0,"eligible_count":0,"eligible_traps":[],"revision":4
              },
              "pending_choice":{
                "pending":true,"chooser":0,"choice_id":"secret-choice","kind":1,
                "minimum_selections":1,"maximum_selections":1,"ordered":false,
                "options":[{"option_id":"secret-option"}],"revision":4
              }
            }
            """;
        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            V05.ScgsV05Json.DeserializeReaction(choiceLeak, V05.PlayerId.Player1));

        const string choiceShapeLeak = """
            {
              "schema_version":2,
              "revision":4,
              "reaction":{
                "pending":false,"window":0,"responder":0,"subject":0,
                "depth":0,"eligible_count":0,"eligible_traps":[],"revision":4
              },
              "pending_choice":{
                "pending":true,"chooser":0,"kind":1,
                "minimum_selections":1,"maximum_selections":1,"ordered":false,
                "options":[],"revision":4
              }
            }
            """;
        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            V05.ScgsV05Json.DeserializeReaction(choiceShapeLeak, V05.PlayerId.Player1));

        const string redactedChoice = """
            {
              "schema_version":2,
              "revision":4,
              "reaction":{
                "pending":false,"window":0,"responder":0,"subject":0,
                "depth":0,"eligible_count":0,"eligible_traps":[],"revision":4
              },
              "pending_choice":{"pending":true,"chooser":0,"revision":4}
            }
            """;
        var safe = V05.ScgsV05Json.DeserializeReaction(
            redactedChoice,
            V05.PlayerId.Player1);
        Assert.IsTrue(safe.PendingChoice.Pending);
        Assert.AreEqual(V05.PlayerId.Player0, safe.PendingChoice.Chooser);
        Assert.IsNull(safe.PendingChoice.ChoiceId);
        Assert.IsEmpty(safe.PendingChoice.Options);
    }

    [TestMethod]
    public void OpponentFaceDownTacticCannotCarryIdentityDerivedGameplayData()
    {
        const string leaked = """
            {
              "schema_version":2,"revision":6,
              "view":{
                "viewer":0,"active_player":0,"first_player":0,"phase":2,"result":0,
                "revision":6,
                "players":[
                  {
                    "player":0,"profession_id":"oathguard","leader_health":25,
                    "maximum_leader_health":25,"current_pp":1,"pp_capacity":1,
                    "cracks":0,"evolution_energy":0,"own_turn_number":1,"fatigue_count":0,
                    "mulligan_done":true,"evolution_used_this_turn":false,
                    "advance_used_this_turn":false,"deploy_used_this_turn":false,
                    "trap_set_this_turn":false,"deck_count":26,"hand_count":4,"hand":[],
                    "main_board":[null,null,null,null,null],"tactics":[null,null,null],
                    "graveyard":[],"archive":[],"standby":[]
                  },
                  {
                    "player":1,"profession_id":"pactmage","leader_health":25,
                    "maximum_leader_health":25,"current_pp":0,"pp_capacity":0,
                    "cracks":0,"evolution_energy":0,"own_turn_number":0,"fatigue_count":0,
                    "mulligan_done":true,"evolution_used_this_turn":false,
                    "advance_used_this_turn":false,"deploy_used_this_turn":false,
                    "trap_set_this_turn":true,"deck_count":26,"hand_count":3,"hand":[],
                    "main_board":[null,null,null,null,null],
                    "tactics":[{
                      "name":"","owner":1,"controller":1,"zone":4,"sequence":31337,
                      "cost":7,"current_attack":0,"current_health":0,"maximum_health":0,
                      "printed_keywords":0,"permanent_keywords":0,"turn_keywords":0,
                      "keywords":0,"evolved":false,"attacked_this_turn":false,
                      "entered_this_turn":false,"face_down":true,"countdown":0
                    },null,null],
                    "graveyard":[],"archive":[],"standby":[]
                  }
                ],
                "reaction":{
                  "pending":false,"window":0,"responder":0,"subject":0,"depth":0,
                  "eligible_count":0,"eligible_traps":[],"revision":6
                },
                "pending_choice":{"pending":false,"revision":6}
              }
            }
            """;

        string sequenceLeak = leaked.Replace("\"cost\":7", "\"cost\":0", StringComparison.Ordinal);
        string costLeak = leaked.Replace(
            "\"sequence\":31337",
            "\"sequence\":0",
            StringComparison.Ordinal);
        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            V05.ScgsV05Json.DeserializeView(sequenceLeak, V05.PlayerId.Player0));
        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            V05.ScgsV05Json.DeserializeView(costLeak, V05.PlayerId.Player0));

        string safe = leaked
            .Replace("\"sequence\":31337", "\"sequence\":0", StringComparison.Ordinal)
            .Replace("\"cost\":7", "\"cost\":0", StringComparison.Ordinal);
        V05.MatchView view = V05.ScgsV05Json.DeserializeView(safe, V05.PlayerId.Player0);
        Assert.IsTrue(view.Players[1].Tactics[0]!.FaceDown);
        Assert.IsNull(view.Players[1].Tactics[0]!.InstanceId);

        string wrongController = safe.Replace(
            "\"owner\":1,\"controller\":1",
            "\"owner\":1,\"controller\":0",
            StringComparison.Ordinal);
        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            V05.ScgsV05Json.DeserializeView(wrongController, V05.PlayerId.Player0));
    }

    [TestMethod]
    public void EveryPlayerCardContainerRequiresMatchingControllerAndZone()
    {
        (string Container, V05.Zone Zone)[] placements =
        [
            ("hand", V05.Zone.Hand),
            ("main_board", V05.Zone.MainBoard),
            ("tactics", V05.Zone.Tactic),
            ("field", V05.Zone.Field),
            ("graveyard", V05.Zone.Graveyard),
            ("archive", V05.Zone.Archive),
            ("standby", V05.Zone.Standby),
        ];

        foreach ((string container, V05.Zone zone) in placements)
        {
            V05.MatchView valid = V05.ScgsV05Json.DeserializeView(
                BuildPlacedCardViewPayload(container, zone, V05.PlayerId.Player0),
                V05.PlayerId.Player0);
            Assert.AreEqual(V05.PlayerId.Player0, valid.Players[0].Player);

            Assert.ThrowsExactly<ScgsProtocolException>(() =>
                V05.ScgsV05Json.DeserializeView(
                    BuildPlacedCardViewPayload(container, V05.Zone.None, V05.PlayerId.Player0),
                    V05.PlayerId.Player0),
                $"{container} accepted a card whose zone disagreed with its container.");
            Assert.ThrowsExactly<ScgsProtocolException>(() =>
                V05.ScgsV05Json.DeserializeView(
                    BuildPlacedCardViewPayload(container, zone, V05.PlayerId.Player1),
                    V05.PlayerId.Player0),
                $"{container} accepted a card controlled by the other player.");
        }
    }

    [TestMethod]
    public void SafeHandleKeepsFullTokenAndDestroysExactlyOnce()
    {
        const ulong token = 0xFEDC_BA98_7654_3210UL;
        var destroyed = new List<ulong>();
        var handle = new V05.ScgsV05SafeHandle(token, value =>
        {
            destroyed.Add(value);
            return 0U;
        });
        Assert.AreEqual(token, handle.Token);
        handle.Dispose();
        handle.Dispose();
        CollectionAssert.AreEqual(new[] { token }, destroyed);
    }

    [TestMethod]
    public void AbiCompatibilityRequiresMajorAndSupportsNewerMinor()
    {
        V05.ScgsV05NativeBackend.EnsureCompatibleAbi(0x0002_0000U, 0x0002_0000U);
        V05.ScgsV05NativeBackend.EnsureCompatibleAbi(0x0002_0000U, 0x0002_0001U);
        Assert.ThrowsExactly<V05.ScgsV05AbiMismatchException>(() =>
            V05.ScgsV05NativeBackend.EnsureCompatibleAbi(0x0002_0000U, 0x0001_0000U));
    }

    private static string BuildPlacedCardViewPayload(
        string container,
        V05.Zone zone,
        V05.PlayerId controller)
    {
        V05.CardKind kind = container switch
        {
            "field" => V05.CardKind.Field,
            "tactics" => V05.CardKind.Trap,
            _ => V05.CardKind.Follower,
        };
        object card = new
        {
            instance_id = 700UL,
            design_id = kind == V05.CardKind.Field ? "LO-10" : "LO-04",
            profession_id = "oathguard",
            series_id = "luminous_oath",
            neutral = false,
            kind,
            name = "placement sentinel",
            owner = V05.PlayerId.Player0,
            controller,
            zone,
            sequence = 1UL,
            cost = 1,
            current_attack = 1,
            current_health = 1,
            maximum_health = 1,
            printed_keywords = 0U,
            permanent_keywords = 0U,
            turn_keywords = 0U,
            keywords = 0U,
            evolved = false,
            attacked_this_turn = false,
            entered_this_turn = false,
            face_down = false,
            countdown = 0,
        };
        object[] hand = container == "hand" ? [card] : [];
        object?[] mainBoard = [null, null, null, null, null];
        object?[] tactics = [null, null, null];
        object[] graveyard = container == "graveyard" ? [card] : [];
        object[] archive = container == "archive" ? [card] : [];
        object[] standby = container == "standby" ? [card] : [];
        if (container == "main_board")
        {
            mainBoard[0] = card;
        }
        else if (container == "tactics")
        {
            tactics[0] = card;
        }

        object player0 = new
        {
            player = V05.PlayerId.Player0,
            profession_id = "oathguard",
            leader_health = 25,
            maximum_leader_health = 25,
            current_pp = 1,
            pp_capacity = 1,
            cracks = 0,
            evolution_energy = 0,
            own_turn_number = 1,
            fatigue_count = 0,
            mulligan_done = true,
            evolution_used_this_turn = false,
            advance_used_this_turn = false,
            deploy_used_this_turn = false,
            trap_set_this_turn = false,
            deck_count = 26UL,
            hand_count = (ulong)hand.Length,
            hand,
            main_board = mainBoard,
            tactics,
            field = container == "field" ? card : null,
            graveyard,
            archive,
            standby,
        };
        object player1 = new
        {
            player = V05.PlayerId.Player1,
            profession_id = "pactmage",
            leader_health = 25,
            maximum_leader_health = 25,
            current_pp = 0,
            pp_capacity = 0,
            cracks = 0,
            evolution_energy = 0,
            own_turn_number = 0,
            fatigue_count = 0,
            mulligan_done = true,
            evolution_used_this_turn = false,
            advance_used_this_turn = false,
            deploy_used_this_turn = false,
            trap_set_this_turn = false,
            deck_count = 26UL,
            hand_count = 0UL,
            hand = Array.Empty<object>(),
            main_board = new object?[] { null, null, null, null, null },
            tactics = new object?[] { null, null, null },
            graveyard = Array.Empty<object>(),
            archive = Array.Empty<object>(),
            standby = Array.Empty<object>(),
        };
        return JsonSerializer.Serialize(
            new
            {
                schema_version = 2U,
                revision = 6UL,
                view = new
                {
                    viewer = V05.PlayerId.Player0,
                    active_player = V05.PlayerId.Player0,
                    first_player = V05.PlayerId.Player0,
                    phase = V05.MatchPhase.Action,
                    result = V05.GameResult.Ongoing,
                    revision = 6UL,
                    players = new[] { player0, player1 },
                    reaction = new
                    {
                        pending = false,
                        window = V05.ReactionWindow.None,
                        responder = V05.PlayerId.Player0,
                        subject = 0UL,
                        depth = 0UL,
                        eligible_count = 0UL,
                        eligible_traps = Array.Empty<object>(),
                        revision = 6UL,
                    },
                    pending_choice = new { pending = false, revision = 6UL },
                },
            },
            V05.ScgsV05Json.Options);
    }

    private static void AssertEnumValues<T>(params uint[] expected)
        where T : struct, Enum
    {
        uint[] actual = Enum.GetValues<T>().Select(value => Convert.ToUInt32(value)).ToArray();
        CollectionAssert.AreEqual(expected, actual, $"Frozen values changed for {typeof(T).Name}.");
    }

}
