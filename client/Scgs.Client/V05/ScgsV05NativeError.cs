// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace Scgs.Client.V05;

internal static class ScgsV05NativeError
{
    private const int MaximumDiagnosticBytes = 64 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static Exception CreateException(uint nativeCode) =>
        new ScgsV05NativeException(nativeCode, ReadLastError());

    internal static void ThrowIfFailed(uint nativeCode)
    {
        if (nativeCode != (uint)NativeCode.Ok)
        {
            throw CreateException(nativeCode);
        }
    }

    private static unsafe string ReadLastError()
    {
        uint probe = ScgsV05NativeMethods.GetLastError(nint.Zero, 0, out ulong required);
        if (probe != (uint)NativeCode.BufferTooSmall ||
            required is 0 or > MaximumDiagnosticBytes)
        {
            return "The scgs_v05 library did not provide a diagnostic.";
        }

        byte[] bytes = new byte[checked((int)required)];
        try
        {
            fixed (byte* output = bytes)
            {
                uint result = ScgsV05NativeMethods.GetLastError(
                    (nint)output,
                    (ulong)bytes.Length,
                    out ulong actualRequired);
                if (result != (uint)NativeCode.Ok ||
                    actualRequired is 0 or > MaximumDiagnosticBytes ||
                    actualRequired > (ulong)bytes.Length ||
                    bytes[checked((int)actualRequired) - 1] != 0)
                {
                    return "The scgs_v05 library did not provide a readable diagnostic.";
                }

                try
                {
                    return StrictUtf8.GetString(bytes, 0, checked((int)actualRequired) - 1);
                }
                catch (DecoderFallbackException)
                {
                    return "The scgs_v05 library returned a non-UTF-8 diagnostic.";
                }
            }
        }
        finally
        {
            Array.Clear(bytes);
        }
    }
}
