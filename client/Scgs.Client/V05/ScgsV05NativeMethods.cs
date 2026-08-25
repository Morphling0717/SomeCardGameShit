// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Scgs.Client.V05;

internal static partial class ScgsV05NativeMethods
{
    internal const string LibraryName = "scgs_v05";

    [LibraryImport(LibraryName, EntryPoint = "scgs_v05_abi_version")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint AbiVersion();

    [LibraryImport(LibraryName, EntryPoint = "scgs_v05_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint Create(
        uint requestedAbi,
        nint configJson,
        ulong configBytes,
        out ulong handle);

    [LibraryImport(LibraryName, EntryPoint = "scgs_v05_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint Destroy(ulong handle);

    [LibraryImport(LibraryName, EntryPoint = "scgs_v05_start")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint Start(ulong handle, out uint engineCode);

    [LibraryImport(LibraryName, EntryPoint = "scgs_v05_get_view_json")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint GetViewJson(
        ulong handle,
        uint viewer,
        nint buffer,
        ulong capacity,
        out ulong requiredBytes);

    [LibraryImport(LibraryName, EntryPoint = "scgs_v05_list_legal_actions_json")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint ListLegalActionsJson(
        ulong handle,
        nint queryJson,
        ulong queryBytes,
        nint buffer,
        ulong capacity,
        out ulong requiredBytes);

    [LibraryImport(LibraryName, EntryPoint = "scgs_v05_list_valid_targets_json")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint ListValidTargetsJson(
        ulong handle,
        nint queryJson,
        ulong queryBytes,
        nint buffer,
        ulong capacity,
        out ulong requiredBytes);

    [LibraryImport(LibraryName, EntryPoint = "scgs_v05_list_valid_slots_json")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint ListValidSlotsJson(
        ulong handle,
        nint queryJson,
        ulong queryBytes,
        nint buffer,
        ulong capacity,
        out ulong requiredBytes);

    [LibraryImport(LibraryName, EntryPoint = "scgs_v05_list_valid_donors_json")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint ListValidDonorsJson(
        ulong handle,
        nint queryJson,
        ulong queryBytes,
        nint buffer,
        ulong capacity,
        out ulong requiredBytes);

    [LibraryImport(LibraryName, EntryPoint = "scgs_v05_preview_payment_json")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint PreviewPaymentJson(
        ulong handle,
        nint commandJson,
        ulong commandBytes,
        nint buffer,
        ulong capacity,
        out ulong requiredBytes);

    [LibraryImport(LibraryName, EntryPoint = "scgs_v05_get_reaction_context_json")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint GetReactionContextJson(
        ulong handle,
        uint viewer,
        nint buffer,
        ulong capacity,
        out ulong requiredBytes);

    [LibraryImport(LibraryName, EntryPoint = "scgs_v05_submit_command_json")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint SubmitCommandJson(
        ulong handle,
        nint commandJson,
        ulong commandBytes,
        out uint engineCode);

    [LibraryImport(LibraryName, EntryPoint = "scgs_v05_read_events_json")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint ReadEventsJson(
        ulong handle,
        uint viewer,
        ulong afterSequence,
        nint buffer,
        ulong capacity,
        out ulong requiredBytes);

    [LibraryImport(LibraryName, EntryPoint = "scgs_v05_get_last_error")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint GetLastError(
        nint buffer,
        ulong capacity,
        out ulong requiredBytes);
}
