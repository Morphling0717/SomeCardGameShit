// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.Client;

public sealed class ScgsNativeException : Exception
{
    public ScgsNativeException(uint rawCode, string diagnostic)
        : base(string.IsNullOrWhiteSpace(diagnostic)
            ? $"The scgs_v04 native call failed with code {rawCode}."
            : diagnostic)
    {
        RawCode = rawCode;
    }

    public uint RawCode { get; }

    public NativeCode Code => (NativeCode)RawCode;

    public bool IsKnown => RawCode <= (uint)NativeCode.InternalError;
}

public sealed class ScgsAbiMismatchException : Exception
{
    public ScgsAbiMismatchException(uint requested, uint reported)
        : base($"The scgs_v04 ABI is incompatible: requested 0x{requested:X8}, reported 0x{reported:X8}.")
    {
        Requested = requested;
        Reported = reported;
    }

    public uint Requested { get; }

    public uint Reported { get; }
}

public sealed class ScgsProtocolException : Exception
{
    public ScgsProtocolException(string message)
        : base(message)
    {
    }

    public ScgsProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
