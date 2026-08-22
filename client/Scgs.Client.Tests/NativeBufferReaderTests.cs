// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class NativeBufferReaderTests
{
    [TestMethod]
    public void ReadsExactUtf8PayloadWithTrailingNull()
    {
        byte[] payload = Encoding.UTF8.GetBytes("{\"name\":\"卡牌\"}\0");
        int calls = 0;
        string value = NativeBufferReader.Read(Call, code => new ScgsNativeException(code, "failure"));

        Assert.AreEqual("{\"name\":\"卡牌\"}", value);
        Assert.AreEqual(2, calls);
        return;

        uint Call(nint buffer, ulong capacity, out ulong required)
        {
            ++calls;
            required = (ulong)payload.Length;
            if (capacity < required)
            {
                return (uint)NativeCode.BufferTooSmall;
            }

            Marshal.Copy(payload, 0, buffer, payload.Length);
            return (uint)NativeCode.Ok;
        }
    }

    [TestMethod]
    public void RetriesWhenPayloadGrowsBetweenPasses()
    {
        byte[] payload = Encoding.UTF8.GetBytes("hello\0");
        int calls = 0;
        string value = NativeBufferReader.Read(Call, code => new ScgsNativeException(code, "failure"));

        Assert.AreEqual("hello", value);
        Assert.AreEqual(3, calls);
        return;

        uint Call(nint buffer, ulong capacity, out ulong required)
        {
            ++calls;
            if (calls == 1)
            {
                required = 2;
                return (uint)NativeCode.BufferTooSmall;
            }

            required = (ulong)payload.Length;
            if (capacity < required)
            {
                return (uint)NativeCode.BufferTooSmall;
            }

            Marshal.Copy(payload, 0, buffer, payload.Length);
            return (uint)NativeCode.Ok;
        }
    }

    [TestMethod]
    public void RejectsMissingNullAndOversizedBuffers()
    {
        Assert.ThrowsExactly<ScgsProtocolException>(() => NativeBufferReader.Read(
            MissingNull,
            code => new ScgsNativeException(code, "failure")));
        Assert.ThrowsExactly<ScgsProtocolException>(() => NativeBufferReader.Read(
            Oversized,
            code => new ScgsNativeException(code, "failure")));
        return;

        static uint MissingNull(nint buffer, ulong capacity, out ulong required)
        {
            required = 2;
            if (capacity < required)
            {
                return (uint)NativeCode.BufferTooSmall;
            }

            Marshal.Copy(new byte[] { (byte)'x', (byte)'y' }, 0, buffer, 2);
            return (uint)NativeCode.Ok;
        }

        static uint Oversized(nint buffer, ulong capacity, out ulong required)
        {
            _ = buffer;
            _ = capacity;
            required = (ulong)NativeBufferReader.MaximumOutputBytes + 1;
            return (uint)NativeCode.BufferTooSmall;
        }
    }

    [TestMethod]
    public void RejectsInvalidUtf8AndInconsistentSuccessLength()
    {
        Assert.ThrowsExactly<ScgsProtocolException>(() => NativeBufferReader.Read(
            InvalidUtf8,
            code => new ScgsNativeException(code, "failure")));
        Assert.ThrowsExactly<ScgsProtocolException>(() => NativeBufferReader.Read(
            InconsistentLength,
            code => new ScgsNativeException(code, "failure")));
        return;

        static uint InvalidUtf8(nint buffer, ulong capacity, out ulong required)
        {
            byte[] bytes = [0xC3, 0x28, 0x00];
            required = (ulong)bytes.Length;
            if (capacity < required)
            {
                return (uint)NativeCode.BufferTooSmall;
            }

            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            return (uint)NativeCode.Ok;
        }

        static uint InconsistentLength(nint buffer, ulong capacity, out ulong required)
        {
            _ = buffer;
            if (capacity == 0)
            {
                required = 2;
                return (uint)NativeCode.BufferTooSmall;
            }

            required = capacity + 1;
            return (uint)NativeCode.Ok;
        }
    }

    [TestMethod]
    public void RejectsAFourthConsecutiveGrowthAndOversizedManagedInput()
    {
        int calls = 0;
        Assert.ThrowsExactly<ScgsProtocolException>(() => NativeBufferReader.Read(
            GrowForever,
            code => new ScgsNativeException(code, "failure")));
        Assert.AreEqual(5, calls);

        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            ScgsV04NativeBackend.EncodeInput(
                new string('x', ScgsV04Contract.MaximumInputBytes + 1)));
        Assert.ThrowsExactly<ScgsProtocolException>(() =>
            ScgsV04NativeBackend.EncodeInput("\uD800"));
        return;

        uint GrowForever(nint buffer, ulong capacity, out ulong required)
        {
            _ = buffer;
            ++calls;
            required = capacity + 1;
            return (uint)NativeCode.BufferTooSmall;
        }
    }

    [TestMethod]
    public void ConvertsNativeFailureImmediately()
    {
        int exceptionCalls = 0;
        ScgsNativeException exception = Assert.ThrowsExactly<ScgsNativeException>(() =>
            NativeBufferReader.Read(
                Fail,
                code =>
                {
                    ++exceptionCalls;
                    return new ScgsNativeException(code, "captured");
                }));

        Assert.AreEqual((uint)NativeCode.InvalidHandle, exception.RawCode);
        Assert.AreEqual(1, exceptionCalls);
        return;

        static uint Fail(nint buffer, ulong capacity, out ulong required)
        {
            _ = buffer;
            _ = capacity;
            required = 0;
            return (uint)NativeCode.InvalidHandle;
        }
    }

    [TestMethod]
    public void RejectsBufferTooSmallWithoutReportedGrowth()
    {
        Assert.ThrowsExactly<ScgsProtocolException>(() => NativeBufferReader.Read(
            Call,
            code => new ScgsNativeException(code, "failure")));
        return;

        static uint Call(nint buffer, ulong capacity, out ulong required)
        {
            _ = buffer;
            required = capacity == 0 ? 8UL : capacity;
            return (uint)NativeCode.BufferTooSmall;
        }
    }
}
