// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;

namespace Scgs.Client;

public sealed class ScgsV04SafeHandle : SafeHandle
{
    private readonly Func<ulong, uint> destroy;

    internal ScgsV04SafeHandle(ulong token)
        : this(token, ScgsV04NativeMethods.Destroy)
    {
    }

    internal ScgsV04SafeHandle(ulong token, Func<ulong, uint> destroy)
        : base(nint.Zero, ownsHandle: true)
    {
        if (token == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(token), "A native game token cannot be zero.");
        }

        this.destroy = destroy ?? throw new ArgumentNullException(nameof(destroy));
        SetHandle(new nint(unchecked((long)token)));
    }

    public override bool IsInvalid => handle == nint.Zero;

    internal ulong Token => unchecked((ulong)handle.ToInt64());

    protected override bool ReleaseHandle()
    {
        try
        {
            _ = destroy(Token);
        }
        catch
        {
            // SafeHandle cleanup must never surface an exception from a finalizer.
        }

        return true;
    }
}
