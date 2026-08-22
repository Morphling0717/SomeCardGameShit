namespace Scgs.GodotClient.Native;

internal enum GodotDesktopTarget
{
    WindowsX64,
    MacOsArm64,
}

internal static class NativeLibraryLayout
{
    internal static (string PlatformFolder, string FileName) Describe(
        GodotDesktopTarget target) => target switch
        {
            GodotDesktopTarget.WindowsX64 => ("windows-x86_64", "scgs_v04.dll"),
            GodotDesktopTarget.MacOsArm64 => ("macos-arm64", "libscgs_v04.dylib"),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported desktop target."),
        };

    internal static IReadOnlyList<string> CandidatePaths(
        GodotDesktopTarget target,
        string executablePath,
        string applicationBaseDirectory,
        string projectNativeDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectNativeDirectory);

        (string platformFolder, string fileName) = Describe(target);
        string executableDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath)) ??
            throw new ArgumentException("The executable path has no parent directory.", nameof(executablePath));
        string applicationBase = Path.GetFullPath(applicationBaseDirectory);
        string projectNative = Path.GetFullPath(projectNativeDirectory);
        var candidates = new List<string>
        {
            Path.Combine(executableDirectory, fileName),
            Path.Combine(executableDirectory, "native", platformFolder, fileName),
            Path.Combine(applicationBase, fileName),
            Path.Combine(applicationBase, "native", platformFolder, fileName),
        };

        if (target == GodotDesktopTarget.MacOsArm64)
        {
            candidates.Add(Path.Combine(executableDirectory, "..", "Frameworks", fileName));
            candidates.Add(Path.Combine(
                executableDirectory,
                "..",
                "Resources",
                "native",
                platformFolder,
                fileName));
        }

        candidates.Add(Path.Combine(projectNative, platformFolder, fileName));
        return candidates
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
