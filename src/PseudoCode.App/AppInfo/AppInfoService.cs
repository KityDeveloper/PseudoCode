using System.Reflection;

namespace PseudoCode.App;

internal static class AppInfoService
{
    public const string AppName = "PseudoCode";
    public const string Author = "Kity Dev";
    public const string YouTubeUrl = "https://www.youtube.com/@KityDev";
    public const string GitHubUrl = "https://github.com/KityDeveloper";
    public const string WebUrl = "https://kity.dev";

    public static string Version => NormalizeVersion(
        typeof(AppInfoService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? typeof(AppInfoService).Assembly.GetName().Version?.ToString()
        ?? "1.1.4");

    private static string NormalizeVersion(string version)
    {
        var plusIndex = version.IndexOf('+');
        return plusIndex >= 0 ? version[..plusIndex] : version;
    }
}
