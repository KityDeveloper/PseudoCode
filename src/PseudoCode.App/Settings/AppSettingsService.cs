using System.Text.Json;
using System.Text.Json.Serialization;

namespace PseudoCode.App;

internal sealed record RuntimeSettings(
    PseudoLanguageDefinition Language,
    SyntaxTheme DarkSyntaxTheme,
    SyntaxTheme LightSyntaxTheme,
    IReadOnlyList<string> Diagnostics,
    string UserSettingsPath);

internal sealed record JsonConfigTarget(string Title, string RelativePath, string FullPath, string Template);

internal static class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public static RuntimeSettings Load()
    {
        var diagnostics = new List<string>();
        var baseSettingsPath = Path.Combine(AppContext.BaseDirectory, "settings");
        var userSettingsPath = GetUserSettingsPath();
        EnsureUserSettingsFolders(userSettingsPath);

        var settings = LoadSettingsFile(Path.Combine(baseSettingsPath, "default-settings.json"), diagnostics) ?? AppSettingsDto.CreateDefault();
        var userSettingsFile = Path.Combine(userSettingsPath, "default-settings.json");
        if (File.Exists(userSettingsFile))
        {
            settings = LoadSettingsFile(userSettingsFile, diagnostics) ?? settings;
        }

        var language = LoadDialect(settings.Language.ActiveDialect, baseSettingsPath, userSettingsPath, diagnostics);
        var dark = LoadSyntaxTheme(settings.Editor.SyntaxThemeDark, baseSettingsPath, userSettingsPath, SyntaxTheme.CreateDark(), diagnostics);
        var light = LoadSyntaxTheme(settings.Editor.SyntaxThemeLight, baseSettingsPath, userSettingsPath, SyntaxTheme.CreateLight(), diagnostics);

        return new RuntimeSettings(language, dark, light, diagnostics, userSettingsPath);
    }

    public static IReadOnlyList<JsonConfigTarget> GetConfigTargets(RuntimeSettings settings) =>
    [
        new("Settings", "default-settings.json", Path.Combine(settings.UserSettingsPath, "default-settings.json"), DefaultSettingsTemplate),
        new("Dialecto custom", "dialects/custom.json", Path.Combine(settings.UserSettingsPath, "dialects", "custom.json"), CustomDialectTemplate),
        new("Dialecto PSeInt compatible", "dialects/pseint-compatible.json", Path.Combine(settings.UserSettingsPath, "dialects", "pseint-compatible.json"), PseIntCompatibleDialectTemplate),
        new("Dialecto simple ejemplo", "dialects/simple.json", Path.Combine(settings.UserSettingsPath, "dialects", "simple.json"), SimpleDialectTemplate),
        new("Dialecto English ejemplo", "dialects/english.json", Path.Combine(settings.UserSettingsPath, "dialects", "english.json"), EnglishDialectTemplate),
        new("Tema custom dark", "syntax-themes/custom-dark.json", Path.Combine(settings.UserSettingsPath, "syntax-themes", "custom-dark.json"), CustomDarkThemeTemplate),
        new("Tema high contrast", "syntax-themes/high-contrast.json", Path.Combine(settings.UserSettingsPath, "syntax-themes", "high-contrast.json"), HighContrastThemeTemplate),
        new("Tema sunset", "syntax-themes/sunset.json", Path.Combine(settings.UserSettingsPath, "syntax-themes", "sunset.json"), SunsetThemeTemplate)
    ];

    public static IReadOnlyList<string> CreateUserTemplateFiles(RuntimeSettings settings)
    {
        var written = new List<string>();
        EnsureUserSettingsFolders(settings.UserSettingsPath);
        foreach (var target in GetConfigTargets(settings))
        {
            WriteIfMissing(target.FullPath, target.Template, written);
        }

        return written;
    }

    public static bool IsValidJson(string json, out string error)
    {
        try
        {
            using var _ = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string ReadOrTemplate(JsonConfigTarget target) =>
        File.Exists(target.FullPath) ? File.ReadAllText(target.FullPath) : target.Template.Trim() + Environment.NewLine;

    public static void SaveTarget(JsonConfigTarget target, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target.FullPath)!);
        File.WriteAllText(target.FullPath, json.TrimEnd() + Environment.NewLine);
    }

    private static void EnsureUserSettingsFolders(string userSettingsPath)
    {
        Directory.CreateDirectory(userSettingsPath);
        Directory.CreateDirectory(Path.Combine(userSettingsPath, "dialects"));
        Directory.CreateDirectory(Path.Combine(userSettingsPath, "syntax-themes"));
    }

    private static void WriteIfMissing(string path, string content, List<string> written)
    {
        if (File.Exists(path))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.Trim() + Environment.NewLine);
        written.Add(path);
    }

    private static AppSettingsDto? LoadSettingsFile(string path, List<string> diagnostics)
    {
        if (!File.Exists(path))
        {
            diagnostics.Add($"Settings: no existe '{path}'. Se usaron defaults internos.");
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettingsDto>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Settings: no se pudo leer '{path}'. {ex.Message}");
            return null;
        }
    }

    private static PseudoLanguageDefinition LoadDialect(string id, string baseSettingsPath, string userSettingsPath, List<string> diagnostics)
    {
        id = string.IsNullOrWhiteSpace(id) ? PseudoLanguageDefinition.DefaultDialectId : id.Trim();
        var userPath = Path.Combine(userSettingsPath, "dialects", $"{id}.json");
        var basePath = Path.Combine(baseSettingsPath, "dialects", $"{id}.json");
        var path = File.Exists(userPath) ? userPath : basePath;

        if (!File.Exists(path))
        {
            diagnostics.Add($"Settings: no se encontro el dialecto '{id}'. Se uso PSeInt por defecto.");
            return PseudoLanguageDefinition.CreateDefault();
        }

        try
        {
            var dto = JsonSerializer.Deserialize<PseudoLanguageDto>(File.ReadAllText(path), JsonOptions);
            return dto is null ? PseudoLanguageDefinition.CreateDefault() : PseudoLanguageDefinition.FromDto(dto, diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Settings: no se pudo cargar el dialecto '{id}'. {ex.Message}");
            return PseudoLanguageDefinition.CreateDefault();
        }
    }

    private static SyntaxTheme LoadSyntaxTheme(string id, string baseSettingsPath, string userSettingsPath, SyntaxTheme fallback, List<string> diagnostics)
    {
        id = string.IsNullOrWhiteSpace(id) ? fallback.Id : id.Trim();
        var userPath = Path.Combine(userSettingsPath, "syntax-themes", $"{id}.json");
        var basePath = Path.Combine(baseSettingsPath, "syntax-themes", $"{id}.json");
        var path = File.Exists(userPath) ? userPath : basePath;

        if (!File.Exists(path))
        {
            diagnostics.Add($"Settings: no se encontro el tema de sintaxis '{id}'. Se uso '{fallback.Id}'.");
            return fallback;
        }

        try
        {
            var dto = JsonSerializer.Deserialize<SyntaxThemeDto>(File.ReadAllText(path), JsonOptions);
            return dto is null ? fallback : SyntaxTheme.FromDto(dto, fallback, diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Settings: no se pudo cargar el tema de sintaxis '{id}'. {ex.Message}");
            return fallback;
        }
    }

    private static string GetUserSettingsPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PseudoCode", "settings");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "PseudoCode", "settings");
        }

        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return string.IsNullOrWhiteSpace(configHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "PseudoCode", "settings")
            : Path.Combine(configHome, "PseudoCode", "settings");
    }

    public const string DefaultSettingsTemplate = """
    {
      "language": {
        "activeDialect": "custom"
      },
      "editor": {
        "syntaxThemeDark": "custom-dark",
        "syntaxThemeLight": "light"
      }
    }
    """;

    public const string CustomDialectTemplate = """
    {
      "id": "custom",
      "displayName": "Custom",
      "keywords": {
        "algorithmStart": "Algoritmo",
        "algorithmEnd": "FinAlgoritmo",
        "processStart": "Proceso",
        "processEnd": "FinProceso",
        "declare": "Definir",
        "typeSeparator": "Como",
        "write": "Mostrar",
        "read": "Pedir",
        "if": "Si",
        "then": "Entonces",
        "else": "Sino",
        "endIf": "FinSi",
        "while": "Mientras",
        "do": "Hacer",
        "endWhile": "FinMientras",
        "for": "Para",
        "until": "Hasta",
        "step": "Paso",
        "endFor": "FinPara",
        "switch": "Segun",
        "otherwise": "De Otro Modo",
        "endSwitch": "FinSegun",
        "true": "Verdadero",
        "false": "Falso",
        "and": "Y",
        "or": "O",
        "not": "NO"
      },
      "types": ["Entero", "Real", "Cadena", "Caracter", "Logico", "Booleano"],
      "snippets": []
    }
    """;

    public const string PseIntCompatibleDialectTemplate = """
    {
      "id": "pseint-compatible",
      "displayName": "PSeInt Compatible",
      "keywords": {
        "algorithmStart": "Algoritmo",
        "algorithmEnd": "FinAlgoritmo",
        "processStart": "Proceso",
        "processEnd": "FinProceso",
        "declare": "Definir",
        "typeSeparator": "Como",
        "write": "Escribir",
        "read": "Leer",
        "if": "Si",
        "then": "Entonces",
        "else": "Sino",
        "endIf": "FinSi",
        "while": "Mientras",
        "do": "Hacer",
        "endWhile": "FinMientras",
        "for": "Para",
        "until": "Hasta",
        "step": "Paso",
        "endFor": "FinPara",
        "switch": "Segun",
        "otherwise": "De Otro Modo",
        "endSwitch": "FinSegun",
        "true": "Verdadero",
        "false": "Falso",
        "and": "Y",
        "or": "O",
        "not": "NO"
      },
      "types": ["Entero", "Real", "Cadena", "Caracter", "Logico", "Booleano"],
      "snippets": []
    }
    """;

    public const string SimpleDialectTemplate = """
    {
      "id": "simple",
      "displayName": "Simple",
      "keywords": {
        "algorithmStart": "Inicio",
        "algorithmEnd": "Fin",
        "processStart": "Proceso",
        "processEnd": "FinProceso",
        "declare": "Variable",
        "typeSeparator": "Tipo",
        "write": "Mostrar",
        "read": "Pedir",
        "if": "Si",
        "then": "Entonces",
        "else": "Sino",
        "endIf": "FinSi",
        "while": "Mientras",
        "do": "Hacer",
        "endWhile": "FinMientras",
        "for": "Para",
        "until": "Hasta",
        "step": "Paso",
        "endFor": "FinPara",
        "switch": "Segun",
        "otherwise": "Otro",
        "endSwitch": "FinSegun",
        "true": "Verdadero",
        "false": "Falso",
        "and": "Y",
        "or": "O",
        "not": "NO"
      },
      "types": ["Entero", "Real", "Texto", "Logico"],
      "snippets": []
    }
    """;

    public const string EnglishDialectTemplate = """
    {
      "id": "english",
      "displayName": "English",
      "keywords": {
        "algorithmStart": "Algorithm",
        "algorithmEnd": "EndAlgorithm",
        "processStart": "Process",
        "processEnd": "EndProcess",
        "declare": "Define",
        "typeSeparator": "As",
        "write": "Write",
        "read": "Read",
        "if": "If",
        "then": "Then",
        "else": "Else",
        "endIf": "EndIf",
        "while": "While",
        "do": "Do",
        "endWhile": "EndWhile",
        "for": "For",
        "until": "To",
        "step": "Step",
        "endFor": "EndFor",
        "switch": "Switch",
        "otherwise": "Otherwise",
        "endSwitch": "EndSwitch",
        "true": "True",
        "false": "False",
        "and": "AND",
        "or": "OR",
        "not": "NOT"
      },
      "types": ["Integer", "Real", "String", "Character", "Logical", "Boolean"],
      "snippets": []
    }
    """;

    public const string CustomDarkThemeTemplate = """
    {
      "id": "custom-dark",
      "displayName": "Custom Dark",
      "syntax": {
        "keyword": "#5EA1FF",
        "type": "#4EC9B0",
        "string": "#CE9178",
        "number": "#B5CEA8",
        "operator": "#DCDCAA",
        "comment": "#6A9955",
        "blockBackground": "#1F3B4D",
        "diagnosticUnderline": "#FF4D4D"
      }
    }
    """;

    public const string HighContrastThemeTemplate = """
    {
      "id": "high-contrast",
      "displayName": "High Contrast",
      "syntax": {
        "keyword": "#00B7FF",
        "type": "#00D084",
        "string": "#FFD166",
        "number": "#EF476F",
        "operator": "#F8F8F2",
        "comment": "#8BE9A6",
        "blockBackground": "#14324A",
        "diagnosticUnderline": "#FF2E2E"
      }
    }
    """;

    public const string SunsetThemeTemplate = """
    {
      "id": "sunset",
      "displayName": "Sunset",
      "syntax": {
        "keyword": "#FF6B6B",
        "type": "#2EC4B6",
        "string": "#FFD166",
        "number": "#8AC926",
        "operator": "#E0E0E0",
        "comment": "#7BD88F",
        "blockBackground": "#30343F",
        "diagnosticUnderline": "#EF233C"
      }
    }
    """;
}

internal sealed class AppSettingsDto
{
    [JsonPropertyName("language")]
    public LanguageSettingsDto Language { get; set; } = new();

    [JsonPropertyName("editor")]
    public EditorSettingsDto Editor { get; set; } = new();

    public static AppSettingsDto CreateDefault() => new();
}

internal sealed class LanguageSettingsDto
{
    [JsonPropertyName("activeDialect")]
    public string ActiveDialect { get; set; } = PseudoLanguageDefinition.DefaultDialectId;
}

internal sealed class EditorSettingsDto
{
    [JsonPropertyName("syntaxThemeDark")]
    public string SyntaxThemeDark { get; set; } = SyntaxTheme.DefaultDarkId;

    [JsonPropertyName("syntaxThemeLight")]
    public string SyntaxThemeLight { get; set; } = SyntaxTheme.DefaultLightId;
}
