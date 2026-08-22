// SPDX-License-Identifier: GPL-3.0-or-later
using System.Buffers;
using System.Text;

namespace Scgs.Client;

internal delegate uint NativeBufferCall(nint buffer, ulong capacity, out ulong requiredBytes);

internal static class NativeBufferReader
{
    internal const int MaximumOutputBytes = 16 * 1024 * 1024;
    private const int MaximumGrowthRetries = 3;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static unsafe string Read(
        NativeBufferCall call,
        Func<uint, Exception> nativeExceptionFactory)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(nativeExceptionFactory);

        uint probeCode = call(nint.Zero, 0, out ulong required);
        if (probeCode != (uint)NativeCode.BufferTooSmall)
        {
            if (probeCode != (uint)NativeCode.Ok)
            {
                throw nativeExceptionFactory(probeCode);
            }

            throw new ScgsProtocolException(
                "A two-pass native output unexpectedly succeeded without a buffer.");
        }

        int growthRetries = 0;
        while (true)
        {
            int capacity = ValidateRequiredSize(required);
            byte[] rented = ArrayPool<byte>.Shared.Rent(capacity);
            try
            {
                fixed (byte* output = rented)
                {
                    uint code = call((nint)output, (ulong)capacity, out ulong actualRequired);
                    if (code == (uint)NativeCode.BufferTooSmall)
                    {
                        if (actualRequired <= (ulong)capacity)
                        {
                            throw new ScgsProtocolException(
                                "The native library rejected a buffer that met its reported size.");
                        }

                        if (growthRetries == MaximumGrowthRetries)
                        {
                            throw new ScgsProtocolException(
                                "The native output size did not stabilize after three growth retries.");
                        }

                        ++growthRetries;
                        required = actualRequired;
                        continue;
                    }

                    if (code != (uint)NativeCode.Ok)
                    {
                        throw nativeExceptionFactory(code);
                    }

                    int lengthWithNull = ValidateRequiredSize(actualRequired);
                    if (lengthWithNull > capacity || rented[lengthWithNull - 1] != 0)
                    {
                        throw new ScgsProtocolException(
                            "The native UTF-8 output has an invalid length or missing NUL terminator.");
                    }

                    try
                    {
                        return StrictUtf8.GetString(rented, 0, lengthWithNull - 1);
                    }
                    catch (DecoderFallbackException exception)
                    {
                        throw new ScgsProtocolException(
                            "The native output is not valid UTF-8.",
                            exception);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented, clearArray: true);
            }
        }
    }

    private static int ValidateRequiredSize(ulong required)
    {
        if (required is 0 or > MaximumOutputBytes)
        {
            throw new ScgsProtocolException(
                $"The native output requested an invalid buffer size of {required} bytes.");
        }

        return checked((int)required);
    }
}
