using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PseudoCode.App;

internal sealed class PseudoLanguageDefinition
{
    public const string DefaultDialectId = "pseint";

    public static readonly string[] RequiredKeywordRoles =
    [
        "algorithmStart", "algorithmEnd", "processStart", "processEnd",
        "declare", "typeSeparator", "write", "read",
        "if", "then", "else", "endIf",
        "while", "do", "endWhile",
        "for", "until", "step", "endFor",
        "switch", "otherwise", "endSwitch",
        "true", "false", "and", "or", "not"
    ];

    public string Id { get; init; } = DefaultDialectId;
    public string DisplayName { get; init; } = "PSeInt";
    public Dictionary<string, string> Keywords { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Types { get; init; } = [];
    public List<CommandInfo> Snippets { get; init; } = [];

    public string Keyword(string role) => Keywords.TryGetValue(role, out var value) ? value : role;

    public bool IsKeyword(string text, string role) =>
        text.Equals(Keyword(role), StringComparison.OrdinalIgnoreCase);

    public bool StartsWithKeyword(string text, string role) =>
        text.StartsWith(Keyword(role) + " ", StringComparison.OrdinalIgnoreCase);

    public string RemoveKeywordPrefix(string text, string role) =>
        text[Keyword(role).Length..].TrimStart();

    public string RegexKeyword(string role) => Regex.Escape(Keyword(role));

    public Regex BuildKeywordRegex()
    {
        var words = Keywords.Values
            .Concat(Types)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderByDescending(value => value.Length)
            .Select(Regex.Escape);
        return new Regex(@"\b(" + string.Join("|", words) + @")\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    public Regex BuildTypeRegex()
    {
        var words = Types
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderByDescending(value => value.Length)
            .Select(Regex.Escape);
        return new Regex(@"\b(" + string.Join("|", words) + @")\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    public Regex BuildBlockLineRegex()
    {
        var roles = new[]
        {
            "algorithmStart", "processStart", "if", "else", "endIf",
            "while", "endWhile", "for", "endFor", "switch", "endSwitch",
            "algorithmEnd", "processEnd"
        };
        var words = roles.Select(Keyword).OrderByDescending(value => value.Length).Select(Regex.Escape);
        return new Regex(@"^\s*(" + string.Join("|", words) + @")\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    public IReadOnlyList<CommandInfo> BuildQuickTemplates() => BuildDefaultSnippets(Keywords, Types)
        .Where(item => item.IsTemplate)
        .ToArray();

    public static PseudoLanguageDefinition CreateDefault()
    {
        var keywords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["algorithmStart"] = "Algoritmo",
            ["algorithmEnd"] = "FinAlgoritmo",
            ["processStart"] = "Proceso",
            ["processEnd"] = "FinProceso",
            ["declare"] = "Definir",
            ["typeSeparator"] = "Como",
            ["write"] = "Escribir",
            ["read"] = "Leer",
            ["if"] = "Si",
            ["then"] = "Entonces",
            ["else"] = "Sino",
            ["endIf"] = "FinSi",
            ["while"] = "Mientras",
            ["do"] = "Hacer",
            ["endWhile"] = "FinMientras",
            ["for"] = "Para",
            ["until"] = "Hasta",
            ["step"] = "Paso",
            ["endFor"] = "FinPara",
            ["switch"] = "Segun",
            ["otherwise"] = "De Otro Modo",
            ["endSwitch"] = "FinSegun",
            ["true"] = "Verdadero",
            ["false"] = "Falso",
            ["and"] = "Y",
            ["or"] = "O",
            ["not"] = "NO"
        };

        var types = new List<string> { "Entero", "Real", "Cadena", "Caracter", "Logico", "Booleano" };
        return new PseudoLanguageDefinition
        {
            Id = DefaultDialectId,
            DisplayName = "PSeInt",
            Keywords = keywords,
            Types = types,
            Snippets = BuildDefaultSnippets(keywords, types)
        };
    }

    public static PseudoLanguageDefinition FromDto(PseudoLanguageDto dto, List<string> diagnostics)
    {
        var fallback = CreateDefault();
        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            diagnostics.Add("Settings: el dialecto no tiene id. Se uso el dialecto PSeInt por defecto.");
            return fallback;
        }

        var keywords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in RequiredKeywordRoles)
        {
            if (dto.Keywords.TryGetValue(role, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                keywords[role] = value.Trim();
            }
            else
            {
                diagnostics.Add($"Settings: falta la palabra requerida '{role}' en el dialecto '{dto.Id}'. Se uso PSeInt por defecto.");
                return fallback;
            }
        }

        if (dto.Types.Count == 0 || dto.Types.Any(string.IsNullOrWhiteSpace))
        {
            diagnostics.Add($"Settings: el dialecto '{dto.Id}' no tiene tipos validos. Se uso PSeInt por defecto.");
            return fallback;
        }

        var types = dto.Types.Select(type => type.Trim()).Where(type => type.Length > 0).ToList();
        return new PseudoLanguageDefinition
        {
            Id = dto.Id.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? dto.Id.Trim() : dto.DisplayName.Trim(),
            Keywords = keywords,
            Types = types,
            Snippets = MergeSnippets(dto.Snippets, BuildDefaultSnippets(keywords, types))
        };
    }

    private static List<CommandInfo> MergeSnippets(IReadOnlyList<CommandInfoDto> customSnippets, IReadOnlyList<CommandInfo> defaults)
    {
        if (customSnippets.Count == 0)
        {
            return defaults.ToList();
        }

        var merged = customSnippets
            .Where(item => !string.IsNullOrWhiteSpace(item.Text) && !string.IsNullOrWhiteSpace(item.InsertText))
            .Select(item => new CommandInfo(
                item.Text,
                item.InsertText,
                item.Description,
                item.IsTemplate || item.InsertText.Contains('\n')))
            .ToList();
        var existing = merged.Select(item => item.Text).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in defaults)
        {
            if (existing.Add(item.Text))
            {
                merged.Add(item);
            }
        }

        return merged;
    }

    private static List<CommandInfo> BuildDefaultSnippets(IReadOnlyDictionary<string, string> keywords, IReadOnlyList<string> types)
    {
        var integerType = FindType(types, "Entero", "Integer") ?? types.First();
        var realType = FindType(types, "Real") ?? integerType;
        var textType = FindType(types, "Cadena", "String", "Texto") ?? integerType;
        var logicalType = FindType(types, "Logico", "Logical", "Booleano", "Boolean") ?? integerType;

        var snippets = new List<CommandInfo>
        {
            new(keywords["algorithmStart"], $"{keywords["algorithmStart"]} MiPrograma\n    \n{keywords["algorithmEnd"]}", "Define el inicio y fin de un algoritmo.", true),
            new(keywords["declare"], $"{keywords["declare"]} variable {keywords["typeSeparator"]} {integerType}", "Declara una o varias variables.", true),
            new(keywords["write"], $"{keywords["write"]} \"Mensaje\", variable", "Muestra texto o valores en la salida.", true),
            new(keywords["read"], $"{keywords["read"]} variable", "Espera un valor en la consola antes de continuar.", true),
            new(keywords["if"], $"{keywords["if"]} condicion {keywords["then"]}\n    \n{keywords["endIf"]}", "Bloque condicional.", true),
            new($"{keywords["if"]}/{keywords["else"]}", $"{keywords["if"]} condicion {keywords["then"]}\n    \n{keywords["else"]}\n    \n{keywords["endIf"]}", "Condicional con alternativa.", true),
            new(keywords["while"], $"{keywords["while"]} condicion {keywords["do"]}\n    \n{keywords["endWhile"]}", "Repite mientras se cumpla una condicion.", true),
            new(keywords["for"], $"{keywords["for"]} i <- 1 {keywords["until"]} 10 {keywords["do"]}\n    \n{keywords["endFor"]}", "Repite con contador.", true),
            new(keywords["switch"], $"{keywords["switch"]} opcion {keywords["do"]}\n    1:\n        \n{keywords["endSwitch"]}", "Seleccion multiple.", true),
            new(integerType, integerType, "Tipo numerico entero."),
            new(realType, realType, "Tipo numerico decimal."),
            new(textType, textType, "Tipo de texto."),
            new(logicalType, logicalType, "Tipo verdadero/falso."),
            new(keywords["true"], keywords["true"], "Valor logico verdadero."),
            new(keywords["false"], keywords["false"], "Valor logico falso."),
            new(keywords["and"], keywords["and"], "Operador logico AND."),
            new(keywords["or"], keywords["or"], "Operador logico OR."),
            new(keywords["not"], keywords["not"], "Negacion logica.")
        };

        return snippets;
    }

    private static string? FindType(IReadOnlyList<string> types, params string[] preferred) =>
        preferred
            .Select(name => types.FirstOrDefault(type => type.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(type => !string.IsNullOrWhiteSpace(type));
}

internal sealed class PseudoLanguageDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("keywords")]
    public Dictionary<string, string> Keywords { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("types")]
    public List<string> Types { get; set; } = [];

    [JsonPropertyName("snippets")]
    public List<CommandInfoDto> Snippets { get; set; } = [];
}

internal sealed class CommandInfoDto
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("insertText")]
    public string InsertText { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("isTemplate")]
    public bool IsTemplate { get; set; }
}
