// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;

namespace Scgs.Client.V05;

internal delegate uint NativeJsonOutputCall(
    ulong handle,
    nint inputJson,
    ulong inputBytes,
    nint output,
    ulong capacity,
    out ulong requiredBytes);

internal sealed class ScgsV05NativeBackend : IScgsV05NativeGameBackend
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly ScgsV05SafeHandle handle;
    private bool disposed;

    private ScgsV05NativeBackend(ScgsV05SafeHandle handle)
    {
        this.handle = handle;
    }

    internal static unsafe ScgsV05NativeBackend Create(
        GameConfigRequest config,
        string absoluteNativeLibraryPath)
    {
        NativeLibraryResolver.ConfigureV05(absoluteNativeLibraryPath);
        uint reportedAbi = ScgsV05NativeMethods.AbiVersion();
        EnsureCompatibleAbi(ScgsV05Contract.AbiVersion, reportedAbi);

        byte[] payload = EncodeInput(ScgsV05Json.SerializeConfig(config));
        try
        {
            fixed (byte* input = payload)
            {
                uint nativeCode = ScgsV05NativeMethods.Create(
                    ScgsV05Contract.AbiVersion,
                    (nint)input,
                    (ulong)payload.Length,
                    out ulong token);
                ScgsV05NativeError.ThrowIfFailed(nativeCode);
                if (token == 0)
                {
                    throw new ScgsProtocolException(
                        "The scgs_v05 create call succeeded without returning a handle.");
                }

                ScgsV05SafeHandle? safeHandle = null;
                try
                {
                    safeHandle = new ScgsV05SafeHandle(token);
                    return new ScgsV05NativeBackend(safeHandle);
                }
                catch
                {
                    if (safeHandle is null)
                    {
                        _ = ScgsV05NativeMethods.Destroy(token);
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
        uint nativeCode = ScgsV05NativeMethods.Start(token, out uint engineCode);
        ScgsV05NativeError.ThrowIfFailed(nativeCode);
        return RequireEngineCode(engineCode);
    });

    public string GetView(PlayerId viewer) => WithToken(token =>
        NativeBufferReader.Read(
            (nint output, ulong capacity, out ulong required) =>
                ScgsV05NativeMethods.GetViewJson(
                    token,
                    (uint)viewer,
                    output,
                    capacity,
                    out required),
            ScgsV05NativeError.CreateException));

    public string ListLegalActions(string queryJson) => InvokeJsonOutput(
        queryJson,
        ScgsV05NativeMethods.ListLegalActionsJson);

    public string ListValidTargets(string queryJson) => InvokeJsonOutput(
        queryJson,
        ScgsV05NativeMethods.ListValidTargetsJson);

    public string ListValidSlots(string queryJson) => InvokeJsonOutput(
        queryJson,
        ScgsV05NativeMethods.ListValidSlotsJson);

    public string ListValidDonors(string queryJson) => InvokeJsonOutput(
        queryJson,
        ScgsV05NativeMethods.ListValidDonorsJson);

    public string PreviewPayment(string commandJson) => InvokeJsonOutput(
        commandJson,
        ScgsV05NativeMethods.PreviewPaymentJson);

    public string GetReactionContext(PlayerId viewer) => WithToken(token =>
        NativeBufferReader.Read(
            (nint output, ulong capacity, out ulong required) =>
                ScgsV05NativeMethods.GetReactionContextJson(
                    token,
                    (uint)viewer,
                    output,
                    capacity,
                    out required),
            ScgsV05NativeError.CreateException));

    public unsafe EngineStatus SubmitCommand(string commandJson)
    {
        byte[] payload = EncodeInput(commandJson);
        try
        {
            fixed (byte* input = payload)
            {
                nint address = (nint)input;
                return WithToken(token =>
                {
                    uint nativeCode = ScgsV05NativeMethods.SubmitCommandJson(
                        token,
                        address,
                        (ulong)payload.Length,
                        out uint engineCode);
                    ScgsV05NativeError.ThrowIfFailed(nativeCode);
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
                ScgsV05NativeMethods.ReadEventsJson(
                    token,
                    (uint)viewer,
                    afterSequence,
                    output,
                    capacity,
                    out required),
            ScgsV05NativeError.CreateException));

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
            throw new ScgsV05AbiMismatchException(requested, reported);
        }
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
            throw new ScgsProtocolException("A managed v05 request is not valid UTF-8.", exception);
        }

        if (payload.Length == 0 || payload.Length > ScgsV05Contract.MaximumInputBytes)
        {
            CryptographicOperations.ZeroMemory(payload);
            throw new ScgsProtocolException(
                $"A managed v05 request must contain 1 to {ScgsV05Contract.MaximumInputBytes} UTF-8 bytes.");
        }

        return payload;
    }

    private static EngineStatus RequireEngineCode(uint engineCode)
    {
        if (engineCode == ScgsV05Contract.NoEngineCode)
        {
            throw new ScgsProtocolException(
                "A successful scgs_v05 call did not return an engine status code.");
        }

        return new EngineStatus { RawCode = engineCode, Message = string.Empty };
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
                    ScgsV05NativeError.CreateException));
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
                throw new ObjectDisposedException(nameof(ScgsV05NativeBackend));
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
