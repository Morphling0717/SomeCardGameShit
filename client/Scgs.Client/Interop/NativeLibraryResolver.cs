// SPDX-License-Identifier: GPL-3.0-or-later
using System.Reflection;
using System.Runtime.InteropServices;

namespace Scgs.Client;

internal static class NativeLibraryResolver
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, string> ConfiguredPaths = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, nint> LoadedLibraries = new(StringComparer.Ordinal);
    private static bool resolverRegistered;

    internal static string Configure(string absoluteLibraryPath) =>
        ConfigureCore(ScgsV04NativeMethods.LibraryName, absoluteLibraryPath);

    internal static string ConfigureV05(string absoluteLibraryPath) =>
        ConfigureCore(V05.ScgsV05NativeMethods.LibraryName, absoluteLibraryPath);

    private static string ConfigureCore(string libraryName, string absoluteLibraryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteLibraryPath);
        if (!Path.IsPathFullyQualified(absoluteLibraryPath))
        {
            throw new ArgumentException(
                $"The {libraryName} native library path must be absolute.",
                nameof(absoluteLibraryPath));
        }

        ValidatePlatform();
        string fullPath = Path.GetFullPath(absoluteLibraryPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"The {libraryName} native library was not found.", fullPath);
        }

        lock (Sync)
        {
            if (ConfiguredPaths.TryGetValue(libraryName, out string? configuredPath))
            {
                if (!PathsEqual(configuredPath, fullPath))
                {
                    throw new InvalidOperationException(
                        $"{libraryName} is already bound to '{configuredPath}' and cannot be rebound.");
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

            ConfiguredPaths.Add(libraryName, fullPath);
            return fullPath;
        }
    }

    private static nint ResolveLibrary(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        _ = assembly;
        _ = searchPath;
        if (libraryName is not ScgsV04NativeMethods.LibraryName and not V05.ScgsV05NativeMethods.LibraryName)
        {
            return nint.Zero;
        }

        lock (Sync)
        {
            if (LoadedLibraries.TryGetValue(libraryName, out nint loadedLibrary))
            {
                return loadedLibrary;
            }

            if (!ConfiguredPaths.TryGetValue(libraryName, out string? configuredPath))
            {
                throw new DllNotFoundException(
                    $"{libraryName} was called before an absolute native library path was configured.");
            }

            try
            {
                loadedLibrary = NativeLibrary.Load(configuredPath);
                LoadedLibraries.Add(libraryName, loadedLibrary);
                return loadedLibrary;
            }
            catch (Exception exception) when (
                exception is DllNotFoundException or BadImageFormatException or FileLoadException)
            {
                throw new DllNotFoundException(
                    $"Could not load {libraryName} from '{configuredPath}'. " +
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
                "The managed client supports scgs_v04/scgs_v05 only on Windows x64 and macOS arm64.");
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
