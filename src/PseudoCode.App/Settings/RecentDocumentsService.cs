using System.Text.Json;
using System.Text.Json.Serialization;

namespace PseudoCode.App;

internal sealed record RecentDocumentInfo(string Path, string DisplayName, DateTimeOffset LastOpenedUtc);

internal static class RecentDocumentsService
{
    private const int MaxItems = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static List<RecentDocumentInfo> Load(string userSettingsPath)
    {
        var path = GetRecentPath(userSettingsPath);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<RecentDocumentDto>>(File.ReadAllText(path), JsonOptions) ?? [];
            return items
                .Where(item => !string.IsNullOrWhiteSpace(item.Path))
                .Select(item => new RecentDocumentInfo(
                    item.Path.Trim(),
                    string.IsNullOrWhiteSpace(item.DisplayName) ? Path.GetFileName(item.Path) : item.DisplayName.Trim(),
                    item.LastOpenedUtc == default ? DateTimeOffset.UtcNow : item.LastOpenedUtc))
                .OrderByDescending(item => item.LastOpenedUtc)
                .Take(MaxItems)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static List<RecentDocumentInfo> Add(string userSettingsPath, IEnumerable<RecentDocumentInfo> current, string path, string displayName)
    {
        var normalizedPath = NormalizePath(path);
        var updated = current
            .Where(item => !PathsEqual(item.Path, normalizedPath))
            .Prepend(new RecentDocumentInfo(normalizedPath, displayName, DateTimeOffset.UtcNow))
            .Take(MaxItems)
            .ToList();

        Save(userSettingsPath, updated);
        return updated;
    }

    public static List<RecentDocumentInfo> Remove(string userSettingsPath, IEnumerable<RecentDocumentInfo> current, string path)
    {
        var normalizedPath = NormalizePath(path);
        var updated = current
            .Where(item => !PathsEqual(item.Path, normalizedPath))
            .Take(MaxItems)
            .ToList();

        Save(userSettingsPath, updated);
        return updated;
    }

    public static void Clear(string userSettingsPath)
    {
        Save(userSettingsPath, []);
    }

    private static void Save(string userSettingsPath, IReadOnlyList<RecentDocumentInfo> items)
    {
        Directory.CreateDirectory(userSettingsPath);
        var dto = items.Select(item => new RecentDocumentDto
        {
            Path = item.Path,
            DisplayName = item.DisplayName,
            LastOpenedUtc = item.LastOpenedUtc
        });
        File.WriteAllText(GetRecentPath(userSettingsPath), JsonSerializer.Serialize(dto, JsonOptions) + Environment.NewLine);
    }

    private static string GetRecentPath(string userSettingsPath) =>
        Path.Combine(userSettingsPath, "recent-documents.json");

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path);

    private static bool PathsEqual(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);

    private sealed class RecentDocumentDto
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("lastOpenedUtc")]
        public DateTimeOffset LastOpenedUtc { get; set; }
    }
}
