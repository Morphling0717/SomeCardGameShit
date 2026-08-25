// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;

namespace Scgs.Client.V05;

public sealed class ScgsV05SafeHandle : SafeHandle
{
    private readonly Func<ulong, uint> destroy;

    internal ScgsV05SafeHandle(ulong token)
        : this(token, ScgsV05NativeMethods.Destroy)
    {
    }

    internal ScgsV05SafeHandle(ulong token, Func<ulong, uint> destroy)
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
            // A SafeHandle finalizer must never surface cleanup failures.
        }

        return true;
    }
}
