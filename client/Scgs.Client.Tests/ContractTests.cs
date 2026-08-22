// SPDX-License-Identifier: GPL-3.0-or-later
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class ContractTests
{
    [TestMethod]
    public void FrozenConstantsAndEnumsMatchNativeSchema()
    {
        Assert.AreEqual(
            0x0001_0000U,
            (uint)typeof(ScgsV04Contract).GetField(nameof(ScgsV04Contract.AbiVersion))!
                .GetRawConstantValue()!);
        Assert.AreEqual(
            1U,
            (uint)typeof(ScgsV04Contract).GetField(nameof(ScgsV04Contract.SchemaVersion))!
                .GetRawConstantValue()!);
        Assert.AreEqual(
            0xFFFF_FFFFU,
            (uint)typeof(ScgsV04Contract).GetField(nameof(ScgsV04Contract.NoEngineCode))!
                .GetRawConstantValue()!);

        AssertEnumValues<NativeCode>(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        AssertEnumValues<EngineCode>(
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17,
            18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33,
            34, 35);
        AssertEnumValues<PlayerId>(0, 1);
        AssertEnumValues<FirstPlayerMode>(0, 1, 2);
        AssertEnumValues<ActionKind>(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        AssertEnumValues<TargetKind>(0, 1);
        AssertEnumValues<CardKind>(0, 1, 2, 3);
        AssertEnumValues<Zone>(0, 1, 2, 3, 4, 5, 6, 7);
        AssertEnumValues<MatchPhase>(0, 1, 2, 3, 4);
        AssertEnumValues<ReactionWindow>(0, 1, 2, 3);
        AssertEnumValues<GameResult>(0, 1, 2, 3);
        AssertEnumValues<EffectTrigger>(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        AssertEnumValues<EffectKind>(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        AssertEnumValues<TargetSpec>(0, 1, 2);
        AssertEnumValues<DeploymentCondition>(0, 1, 2);
        AssertEnumValues<EventType>(
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17,
            18, 19, 20, 21, 22, 23, 24);
        AssertEnumValues<Keyword>(0, 1, 2, 4, 8, 16, 32, 64);
    }

    [TestMethod]
    public void LibraryImportSurfaceContainsExactlyFourteenCdeclExports()
    {
        string[] expected =
        [
            "scgs_v04_abi_version",
            "scgs_v04_create",
            "scgs_v04_destroy",
            "scgs_v04_start",
            "scgs_v04_get_view_json",
            "scgs_v04_list_legal_actions_json",
            "scgs_v04_list_valid_targets_json",
            "scgs_v04_list_valid_slots_json",
            "scgs_v04_list_valid_donors_json",
            "scgs_v04_preview_payment_json",
            "scgs_v04_get_reaction_context_json",
            "scgs_v04_submit_command_json",
            "scgs_v04_read_events_json",
            "scgs_v04_get_last_error",
        ];

        MethodInfo[] imports = typeof(ScgsV04NativeMethods)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(method => method.GetCustomAttribute<LibraryImportAttribute>() is not null)
            .ToArray();

        CollectionAssert.AreEquivalent(
            expected,
            imports.Select(method =>
                method.GetCustomAttribute<LibraryImportAttribute>()!.EntryPoint).ToArray());
        Assert.HasCount(14, imports);
        Type u32Ref = typeof(uint).MakeByRefType();
        Type u64Ref = typeof(ulong).MakeByRefType();
        var signatures = new Dictionary<string, Type[]>
        {
            ["scgs_v04_abi_version"] = [],
            ["scgs_v04_create"] = [typeof(uint), typeof(nint), typeof(ulong), u64Ref],
            ["scgs_v04_destroy"] = [typeof(ulong)],
            ["scgs_v04_start"] = [typeof(ulong), u32Ref],
            ["scgs_v04_get_view_json"] =
                [typeof(ulong), typeof(uint), typeof(nint), typeof(ulong), u64Ref],
            ["scgs_v04_list_legal_actions_json"] =
                [typeof(ulong), typeof(nint), typeof(ulong), typeof(nint), typeof(ulong), u64Ref],
            ["scgs_v04_list_valid_targets_json"] =
                [typeof(ulong), typeof(nint), typeof(ulong), typeof(nint), typeof(ulong), u64Ref],
            ["scgs_v04_list_valid_slots_json"] =
                [typeof(ulong), typeof(nint), typeof(ulong), typeof(nint), typeof(ulong), u64Ref],
            ["scgs_v04_list_valid_donors_json"] =
                [typeof(ulong), typeof(nint), typeof(ulong), typeof(nint), typeof(ulong), u64Ref],
            ["scgs_v04_preview_payment_json"] =
                [typeof(ulong), typeof(nint), typeof(ulong), typeof(nint), typeof(ulong), u64Ref],
            ["scgs_v04_get_reaction_context_json"] =
                [typeof(ulong), typeof(uint), typeof(nint), typeof(ulong), u64Ref],
            ["scgs_v04_submit_command_json"] =
                [typeof(ulong), typeof(nint), typeof(ulong), u32Ref],
            ["scgs_v04_read_events_json"] =
                [typeof(ulong), typeof(uint), typeof(ulong), typeof(nint), typeof(ulong), u64Ref],
            ["scgs_v04_get_last_error"] = [typeof(nint), typeof(ulong), u64Ref],
        };
        foreach (MethodInfo method in imports)
        {
            string entryPoint = method.GetCustomAttribute<LibraryImportAttribute>()!.EntryPoint!;
            Assert.AreEqual(typeof(uint), method.ReturnType, entryPoint);
            CollectionAssert.AreEqual(
                signatures[entryPoint],
                method.GetParameters().Select(parameter => parameter.ParameterType).ToArray(),
                entryPoint);
            UnmanagedCallConvAttribute? callConvention =
                method.GetCustomAttribute<UnmanagedCallConvAttribute>();
            Assert.IsNotNull(callConvention, entryPoint);
            CollectionAssert.Contains(callConvention.CallConvs, typeof(CallConvCdecl), entryPoint);
        }

        Assert.IsTrue(typeof(ScgsV04SafeHandle).IsPublic);
    }

    [TestMethod]
    public void RequestSerializationInjectsSchemaAndOmitsNullOptionals()
    {
        var config = new GameConfigRequest("midrange", "advance")
        {
            RandomSeed = 123U,
            FirstPlayerMode = FirstPlayerMode.Player0,
            ShuffleDecks = false,
        };
        using JsonDocument configJson = JsonDocument.Parse(ScgsJson.SerializeConfig(config));
        JsonElement configRoot = configJson.RootElement;
        Assert.AreEqual(1U, configRoot.GetProperty("schema_version").GetUInt32());
        Assert.AreEqual("midrange", configRoot.GetProperty("player0_deck").GetString());
        Assert.AreEqual(123U, configRoot.GetProperty("random_seed").GetUInt32());

        var command = new GameCommandRequest(PlayerId.Player0, ActionKind.EndTurn, 7U);
        using JsonDocument commandJson = JsonDocument.Parse(ScgsJson.SerializeCommand(command));
        JsonElement commandRoot = commandJson.RootElement;
        Assert.AreEqual(1U, commandRoot.GetProperty("schema_version").GetUInt32());
        Assert.AreEqual(0UL, commandRoot.GetProperty("source").GetUInt64());
        Assert.IsFalse(commandRoot.TryGetProperty("target", out _));
        Assert.IsFalse(commandRoot.TryGetProperty("slot", out _));
        Assert.IsFalse(commandRoot.TryGetProperty("component_donor", out _));
        Assert.AreEqual(0, commandRoot.GetProperty("mulligan_cards").GetArrayLength());

        var query = new ActionQueryRequest(PlayerId.Player1, 9U);
        using JsonDocument queryJson = JsonDocument.Parse(ScgsJson.SerializeQuery(query));
        JsonElement queryRoot = queryJson.RootElement;
        Assert.IsFalse(queryRoot.TryGetProperty("action", out _));
        Assert.IsFalse(queryRoot.TryGetProperty("use_advance", out _));
    }

    [TestMethod]
    public void UnknownInboundLegalActionIsPreservedButCannotBeSubmitted()
    {
        const string json = """
            {
              "schema_version": 1,
              "revision": 7,
              "future_optional": true,
              "actions": [{
                "command": {
                  "player": 0,
                  "action": 99,
                  "source": 0,
                  "use_advance": false,
                  "mulligan_cards": [],
                  "expected_revision": 7
                },
                "payment": {
                  "status": {"engine_code": 0, "message": "ok"},
                  "current_pp_before": 1,
                  "current_pp_after": 1,
                  "pp_capacity_before": 1,
                  "pp_capacity_after": 1,
                  "cracks_before": 0,
                  "cracks_after": 0,
                  "evolution_energy_before": 0,
                  "evolution_energy_after": 0,
                  "base_cost": 0,
                  "burn_cost": 0,
                  "advance_cost": 0,
                  "used_advance": false
                }
              }]
            }
            """;

        ActionsEnvelope envelope = ScgsJson.DeserializeActions(json);
        Assert.AreEqual(99U, (uint)envelope.Actions[0].Command.Action);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            ScgsJson.SerializeCommand(envelope.Actions[0].Command));
    }

    [TestMethod]
    public void UnitTargetsRequireAnIdAndLeaderTargetsRejectOne()
    {
        var missingUnit = new GameCommandRequest(PlayerId.Player0, ActionKind.Attack, 1)
        {
            Target = new Target(TargetKind.Unit, PlayerId.Player1),
        };
        Assert.ThrowsExactly<ArgumentException>(() => ScgsJson.SerializeCommand(missingUnit));

        var extraUnit = new GameCommandRequest(PlayerId.Player0, ActionKind.Attack, 1)
        {
            Target = Target.Leader(PlayerId.Player1) with { Unit = 44 },
        };
        Assert.ThrowsExactly<ArgumentException>(() => ScgsJson.SerializeCommand(extraUnit));
    }

    [TestMethod]
    public void AbiCompatibilityUsesFrozenMajorAndBackwardCompatibleMinor()
    {
        ScgsV04NativeBackend.EnsureCompatibleAbi(0x0001_0000U, 0x0001_0000U);
        ScgsV04NativeBackend.EnsureCompatibleAbi(0x0001_0000U, 0x0001_0001U);
        Assert.ThrowsExactly<ScgsAbiMismatchException>(() =>
            ScgsV04NativeBackend.EnsureCompatibleAbi(0x0001_0000U, 0x0002_0000U));
        Assert.ThrowsExactly<ScgsAbiMismatchException>(() =>
            ScgsV04NativeBackend.EnsureCompatibleAbi(0x0001_0001U, 0x0001_0000U));
    }

    [TestMethod]
    public void NativeResolverRejectsRelativePathsBeforeLoading()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            NativeLibraryResolver.Configure("scgs_v04.dll"));
    }

    private static void AssertEnumValues<T>(params uint[] expected)
        where T : struct, Enum
    {
        uint[] actual = Enum.GetValues<T>().Select(value => Convert.ToUInt32(value)).ToArray();
        CollectionAssert.AreEqual(expected, actual, $"Frozen values changed for {typeof(T).Name}.");
    }
}
