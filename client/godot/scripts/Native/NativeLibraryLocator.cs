using Godot;

namespace Scgs.GodotClient.Native;

internal static class NativeLibraryLocator
{
    private const string OverrideArgument = "--native-library=";
    private const string OverrideEnvironment = "SCGS_NATIVE_LIBRARY";

    public static string ResolveAbsolutePath()
    {
        // Reject unsupported hosts before considering overrides so an explicit
        // path cannot accidentally make Linux look like a supported product.
        GodotDesktopTarget target = PlatformTarget();
        string[] userArgs = OS.GetCmdlineUserArgs();
        for (int index = 0; index < userArgs.Length; index++)
        {
            string argument = userArgs[index];
            if (argument.StartsWith(OverrideArgument, StringComparison.Ordinal))
            {
                return RequireAbsoluteFile(argument[OverrideArgument.Length..], "Godot user argument");
            }

            if (argument == "--native-library" && index + 1 < userArgs.Length)
            {
                return RequireAbsoluteFile(userArgs[index + 1], "Godot user argument");
            }
        }

        string? environmentPath = System.Environment.GetEnvironmentVariable(OverrideEnvironment);
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            return RequireAbsoluteFile(environmentPath, OverrideEnvironment);
        }

        // This path is physical in editor/headless builds. Export CI must pass
        // the absolute post-stage path; native libraries inside a PCK are not
        // considered loadable filesystem artifacts.
        IReadOnlyList<string> candidates = NativeLibraryLayout.CandidatePaths(
            target,
            OS.GetExecutablePath(),
            AppContext.BaseDirectory,
            ProjectSettings.GlobalizePath("res://native"));

        foreach (string absolute in candidates)
        {
            if (File.Exists(absolute))
            {
                return absolute;
            }
        }

        throw new FileNotFoundException(
            "找不到 scgs_v04 原生库。请通过 --native-library=<绝对路径> 或 " +
            $"{OverrideEnvironment} 指定 CI/导出后 staging 产物。已检查：\n" +
            string.Join("\n", candidates));
    }

    private static string RequireAbsoluteFile(string rawPath, string source)
    {
        if (string.IsNullOrWhiteSpace(rawPath) || !Path.IsPathFullyQualified(rawPath))
        {
            throw new ArgumentException($"{source} 必须提供原生库的绝对路径。");
        }

        string absolute = Path.GetFullPath(rawPath);
        if (!File.Exists(absolute))
        {
            throw new FileNotFoundException($"{source} 指定的原生库不存在。", absolute);
        }

        return absolute;
    }

    private static GodotDesktopTarget PlatformTarget() => OS.GetName() switch
    {
        "Windows" => GodotDesktopTarget.WindowsX64,
        "macOS" => GodotDesktopTarget.MacOsArm64,
        string platform => throw new PlatformNotSupportedException($"Gate 3A 不支持平台 {platform}。"),
    };
}
