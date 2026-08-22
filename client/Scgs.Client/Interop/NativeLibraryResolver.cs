// SPDX-License-Identifier: GPL-3.0-or-later
using System.Reflection;
using System.Runtime.InteropServices;

namespace Scgs.Client;

internal static class NativeLibraryResolver
{
    private static readonly object Sync = new();
    private static string? configuredPath;
    private static nint loadedLibrary;
    private static bool resolverRegistered;

    internal static string Configure(string absoluteLibraryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteLibraryPath);
        if (!Path.IsPathFullyQualified(absoluteLibraryPath))
        {
            throw new ArgumentException(
                "The scgs_v04 native library path must be absolute.",
                nameof(absoluteLibraryPath));
        }

        ValidatePlatform();
        string fullPath = Path.GetFullPath(absoluteLibraryPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The scgs_v04 native library was not found.", fullPath);
        }

        lock (Sync)
        {
            if (configuredPath is not null)
            {
                if (!PathsEqual(configuredPath, fullPath))
                {
                    throw new InvalidOperationException(
                        $"scgs_v04 is already bound to '{configuredPath}' and cannot be rebound.");
                }

                return configuredPath;
            }

            if (!resolverRegistered)
            {
                NativeLibrary.SetDllImportResolver(
                    typeof(ScgsV04NativeMethods).Assembly,
                    ResolveLibrary);
                resolverRegistered = true;
            }

            configuredPath = fullPath;
            return configuredPath;
        }
    }

    private static nint ResolveLibrary(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        _ = assembly;
        _ = searchPath;
        if (!string.Equals(
                libraryName,
                ScgsV04NativeMethods.LibraryName,
                StringComparison.Ordinal))
        {
            return nint.Zero;
        }

        lock (Sync)
        {
            if (loadedLibrary != nint.Zero)
            {
                return loadedLibrary;
            }

            if (configuredPath is null)
            {
                throw new DllNotFoundException(
                    "scgs_v04 was called before an absolute native library path was configured.");
            }

            try
            {
                loadedLibrary = NativeLibrary.Load(configuredPath);
                return loadedLibrary;
            }
            catch (Exception exception) when (
                exception is DllNotFoundException or BadImageFormatException or FileLoadException)
            {
                throw new DllNotFoundException(
                    $"Could not load scgs_v04 from '{configuredPath}'. " +
                    $"Process architecture: {RuntimeInformation.ProcessArchitecture}.",
                    exception);
            }
        }
    }

    private static void ValidatePlatform()
    {
        bool supportedArchitecture = RuntimeInformation.ProcessArchitecture is
            Architecture.X64 or Architecture.Arm64;
        bool supportedOperatingSystem = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
        if (!supportedArchitecture || !supportedOperatingSystem)
        {
            throw new PlatformNotSupportedException(
                "The managed client supports scgs_v04 only on Windows x64 and macOS arm64.");
        }

        if (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException("The Windows client requires x64.");
        }

        if (OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
        {
            throw new PlatformNotSupportedException("The macOS client requires arm64.");
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left,
            right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
