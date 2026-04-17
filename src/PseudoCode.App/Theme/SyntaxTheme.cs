using System.Text.Json.Serialization;
using Avalonia.Media;

namespace PseudoCode.App;

internal sealed class SyntaxTheme
{
    public const string DefaultDarkId = "dark";
    public const string DefaultLightId = "light";

    public string Id { get; init; } = DefaultDarkId;
    public string DisplayName { get; init; } = "Dark";
    public string Keyword { get; init; } = "#5EA1FF";
    public string Type { get; init; } = "#4EC9B0";
    public string String { get; init; } = "#CE9178";
    public string Number { get; init; } = "#B5CEA8";
    public string Operator { get; init; } = "#DCDCAA";
    public string Comment { get; init; } = "#6A9955";
    public string BlockBackground { get; init; } = "#1F3B4D";
    public string DiagnosticUnderline { get; init; } = "#FF4D4D";

    public PseudoCodeColorPalette ToPalette() => new(
        Brush(Keyword),
        Brush(Type),
        Brush(String),
        Brush(Number),
        Brush(Operator),
        Brush(Comment),
        Brush(BlockBackground),
        Brush(DiagnosticUnderline));

    public static SyntaxTheme CreateDark() => new();

    public static SyntaxTheme CreateLight() => new()
    {
        Id = DefaultLightId,
        DisplayName = "Light",
        Keyword = "#0645AD",
        Type = "#00796B",
        String = "#A31515",
        Number = "#098658",
        Operator = "#795E26",
        Comment = "#008000",
        BlockBackground = "#EAF3FF",
        DiagnosticUnderline = "#DC2626"
    };

    public static SyntaxTheme FromDto(SyntaxThemeDto dto, SyntaxTheme fallback, List<string> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            diagnostics.Add("Settings: un tema de sintaxis no tiene id. Se uso el tema por defecto.");
            return fallback;
        }

        try
        {
            _ = Color.Parse(dto.Syntax.Keyword);
            _ = Color.Parse(dto.Syntax.Type);
            _ = Color.Parse(dto.Syntax.String);
            _ = Color.Parse(dto.Syntax.Number);
            _ = Color.Parse(dto.Syntax.Operator);
            _ = Color.Parse(dto.Syntax.Comment);
            _ = Color.Parse(dto.Syntax.BlockBackground);
            _ = Color.Parse(dto.Syntax.DiagnosticUnderline);
        }
        catch
        {
            diagnostics.Add($"Settings: el tema de sintaxis '{dto.Id}' tiene un color invalido. Se uso el tema por defecto.");
            return fallback;
        }

        return new SyntaxTheme
        {
            Id = dto.Id.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? dto.Id.Trim() : dto.DisplayName.Trim(),
            Keyword = dto.Syntax.Keyword,
            Type = dto.Syntax.Type,
            String = dto.Syntax.String,
            Number = dto.Syntax.Number,
            Operator = dto.Syntax.Operator,
            Comment = dto.Syntax.Comment,
            BlockBackground = dto.Syntax.BlockBackground,
            DiagnosticUnderline = dto.Syntax.DiagnosticUnderline
        };
    }

    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}

internal sealed class SyntaxThemeDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("syntax")]
    public SyntaxThemeColorsDto Syntax { get; set; } = new();
}

internal sealed class SyntaxThemeColorsDto
{
    [JsonPropertyName("keyword")]
    public string Keyword { get; set; } = "#5EA1FF";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "#4EC9B0";

    [JsonPropertyName("string")]
    public string String { get; set; } = "#CE9178";

    [JsonPropertyName("number")]
    public string Number { get; set; } = "#B5CEA8";

    [JsonPropertyName("operator")]
    public string Operator { get; set; } = "#DCDCAA";

    [JsonPropertyName("comment")]
    public string Comment { get; set; } = "#6A9955";

    [JsonPropertyName("blockBackground")]
    public string BlockBackground { get; set; } = "#1F3B4D";

    [JsonPropertyName("diagnosticUnderline")]
    public string DiagnosticUnderline { get; set; } = "#FF4D4D";
}
