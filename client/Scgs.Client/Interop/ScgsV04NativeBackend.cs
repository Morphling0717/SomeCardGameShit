// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;

namespace Scgs.Client;

internal delegate uint NativeJsonOutputCall(
    ulong handle,
    nint inputJson,
    ulong inputBytes,
    nint output,
    ulong capacity,
    out ulong requiredBytes);

internal sealed class ScgsV04NativeBackend : IScgsNativeGameBackend
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly ScgsV04SafeHandle handle;
    private bool disposed;

    private ScgsV04NativeBackend(ScgsV04SafeHandle handle)
    {
        this.handle = handle;
    }

    internal static unsafe ScgsV04NativeBackend Create(
        GameConfigRequest config,
        string absoluteNativeLibraryPath)
    {
        NativeLibraryResolver.Configure(absoluteNativeLibraryPath);
        uint reportedAbi = ScgsV04NativeMethods.AbiVersion();
        EnsureCompatibleAbi(ScgsV04Contract.AbiVersion, reportedAbi);

        string configJson = ScgsJson.SerializeConfig(config);
        byte[] payload = EncodeInput(configJson);
        try
        {
            fixed (byte* input = payload)
            {
                uint nativeCode = ScgsV04NativeMethods.Create(
                    ScgsV04Contract.AbiVersion,
                    (nint)input,
                    (ulong)payload.Length,
                    out ulong token);
                NativeError.ThrowIfFailed(nativeCode);
                if (token == 0)
                {
                    throw new ScgsProtocolException(
                        "The native create call succeeded without returning a handle.");
                }

                ScgsV04SafeHandle? safeHandle = null;
                try
                {
                    safeHandle = new ScgsV04SafeHandle(token);
                    return new ScgsV04NativeBackend(safeHandle);
                }
                catch
                {
                    if (safeHandle is null)
                    {
                        _ = ScgsV04NativeMethods.Destroy(token);
                    }
                    else
                    {
                        safeHandle.Dispose();
                    }

                    throw;
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public EngineStatus Start() => WithToken(token =>
    {
        uint nativeCode = ScgsV04NativeMethods.Start(token, out uint engineCode);
        NativeError.ThrowIfFailed(nativeCode);
        return RequireEngineCode(engineCode);
    });

    public string GetView(PlayerId viewer) => WithToken(token =>
        NativeBufferReader.Read(
            (nint output, ulong capacity, out ulong required) =>
                ScgsV04NativeMethods.GetViewJson(
                    token,
                    (uint)viewer,
                    output,
                    capacity,
                    out required),
            NativeError.CreateException));

    public string ListLegalActions(string queryJson) => InvokeJsonOutput(
        queryJson,
        ScgsV04NativeMethods.ListLegalActionsJson);

    public string ListValidTargets(string queryJson) => InvokeJsonOutput(
        queryJson,
        ScgsV04NativeMethods.ListValidTargetsJson);

    public string ListValidSlots(string queryJson) => InvokeJsonOutput(
        queryJson,
        ScgsV04NativeMethods.ListValidSlotsJson);

    public string ListValidDonors(string queryJson) => InvokeJsonOutput(
        queryJson,
        ScgsV04NativeMethods.ListValidDonorsJson);

    public string PreviewPayment(string commandJson) => InvokeJsonOutput(
        commandJson,
        ScgsV04NativeMethods.PreviewPaymentJson);

    public string GetReactionContext(PlayerId viewer) => WithToken(token =>
        NativeBufferReader.Read(
            (nint output, ulong capacity, out ulong required) =>
                ScgsV04NativeMethods.GetReactionContextJson(
                    token,
                    (uint)viewer,
                    output,
                    capacity,
                    out required),
            NativeError.CreateException));

    public unsafe EngineStatus SubmitCommand(string commandJson)
    {
        byte[] payload = EncodeInput(commandJson);
        try
        {
            fixed (byte* input = payload)
            {
                nint inputAddress = (nint)input;
                return WithToken(token =>
                {
                    uint nativeCode = ScgsV04NativeMethods.SubmitCommandJson(
                        token,
                        inputAddress,
                        (ulong)payload.Length,
                        out uint engineCode);
                    NativeError.ThrowIfFailed(nativeCode);
                    return RequireEngineCode(engineCode);
                });
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public string ReadEvents(PlayerId viewer, ulong afterSequence) => WithToken(token =>
        NativeBufferReader.Read(
            (nint output, ulong capacity, out ulong required) =>
                ScgsV04NativeMethods.ReadEventsJson(
                    token,
                    (uint)viewer,
                    afterSequence,
                    output,
                    capacity,
                    out required),
            NativeError.CreateException));

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        handle.Dispose();
    }

    internal static void EnsureCompatibleAbi(uint requested, uint reported)
    {
        uint requestedMajor = requested >> 16;
        uint requestedMinor = requested & 0xFFFFU;
        uint reportedMajor = reported >> 16;
        uint reportedMinor = reported & 0xFFFFU;
        if (requestedMajor != reportedMajor || requestedMinor > reportedMinor)
        {
            throw new ScgsAbiMismatchException(requested, reported);
        }
    }

    private static EngineStatus RequireEngineCode(uint engineCode)
    {
        if (engineCode == ScgsV04Contract.NoEngineCode)
        {
            throw new ScgsProtocolException(
                "A successful native call did not return an engine status code.");
        }

        return new EngineStatus { RawCode = engineCode, Message = string.Empty };
    }

    internal static byte[] EncodeInput(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        byte[] payload;
        try
        {
            payload = StrictUtf8.GetBytes(json);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ScgsProtocolException("A managed request is not valid UTF-8.", exception);
        }

        if (payload.Length == 0 || payload.Length > ScgsV04Contract.MaximumInputBytes)
        {
            CryptographicOperations.ZeroMemory(payload);
            throw new ScgsProtocolException(
                $"A managed request must contain 1 to {ScgsV04Contract.MaximumInputBytes} UTF-8 bytes.");
        }

        return payload;
    }

    private unsafe string InvokeJsonOutput(string json, NativeJsonOutputCall call)
    {
        byte[] payload = EncodeInput(json);
        try
        {
            fixed (byte* input = payload)
            {
                nint inputAddress = (nint)input;
                return WithToken(token => NativeBufferReader.Read(
                    (nint output, ulong capacity, out ulong required) =>
                        call(
                            token,
                            inputAddress,
                            (ulong)payload.Length,
                            output,
                            capacity,
                            out required),
                    NativeError.CreateException));
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private T WithToken<T>(Func<ulong, T> action)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        bool addRefSucceeded = false;
        try
        {
            handle.DangerousAddRef(ref addRefSucceeded);
            if (handle.IsInvalid)
            {
                throw new ObjectDisposedException(nameof(ScgsV04NativeBackend));
            }

            return action(handle.Token);
        }
        finally
        {
            if (addRefSucceeded)
            {
                handle.DangerousRelease();
            }
        }
    }
}
