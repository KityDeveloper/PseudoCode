using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PseudoCode.App.Services;

public sealed class PseudoInterpreter
{
    private static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private readonly Dictionary<string, object?> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _output = [];
    private readonly List<string> _diagnostics = [];
    private string[] _lines = [];
    private int _nextLineIndex;
    private Queue<string> _pendingInputVariables = [];
    private string? _waitingInputVariable;

    public ExecutionResult Start(string source)
    {
        _variables.Clear();
        _output.Clear();
        _diagnostics.Clear();
        _pendingInputVariables.Clear();
        _waitingInputVariable = null;

        _lines = source.Replace("\r\n", "\n").Split('\n');
        _nextLineIndex = 0;
        return RunUntilBlocked();
    }

    public ExecutionResult Continue(string input)
    {
        if (_waitingInputVariable is null)
        {
            _diagnostics.Add("No hay ninguna instruccion Leer esperando datos.");
            return BuildResult();
        }

        _variables[_waitingInputVariable] = ParseInput(input);
        _output.Add($"> {input}");
        _waitingInputVariable = null;
        return RunUntilBlocked();
    }

    public ExecutionResult Run(string source) => Start(source);

    private ExecutionResult RunUntilBlocked()
    {
        if (TryRequestNextInput())
        {
            return BuildResult();
        }

        for (; _nextLineIndex < _lines.Length; _nextLineIndex++)
        {
            var lineNumber = _nextLineIndex + 1;
            var line = RemoveComment(_lines[_nextLineIndex]).Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            ExecuteLine(line, lineNumber);

            if (TryRequestNextInput())
            {
                _nextLineIndex++;
                return BuildResult();
            }
        }

        return BuildResult();
    }

    private void ExecuteLine(string line, int lineNumber)
    {
        if (StartsWithAny(line, "Algoritmo ", "Proceso ", "FinAlgoritmo", "FinProceso"))
        {
            return;
        }

        if (line.StartsWith("Definir ", StringComparison.OrdinalIgnoreCase))
        {
            DeclareVariable(line, lineNumber);
            return;
        }

        if (line.StartsWith("Escribir ", StringComparison.OrdinalIgnoreCase))
        {
            Write(line["Escribir ".Length..], lineNumber);
            return;
        }

        if (line.StartsWith("Leer ", StringComparison.OrdinalIgnoreCase))
        {
            Read(line["Leer ".Length..], lineNumber);
            return;
        }

        var assignmentIndex = line.IndexOf("<-", StringComparison.Ordinal);
        if (assignmentIndex > 0)
        {
            Assign(line[..assignmentIndex].Trim(), line[(assignmentIndex + 2)..].Trim(), lineNumber);
            return;
        }

        if (StartsWithAny(line, "Si ", "Sino", "FinSi", "Mientras ", "FinMientras", "Para ", "FinPara", "Segun ", "FinSegun"))
        {
            _diagnostics.Add($"Linea {lineNumber}: la estructura '{line.Split(' ')[0]}' todavia no esta implementada en el ejecutor inicial.");
            return;
        }

        _diagnostics.Add($"Linea {lineNumber}: no entiendo esta instruccion: {line}");
    }

    private void DeclareVariable(string line, int lineNumber)
    {
        var declaration = line["Definir ".Length..];
        var separator = declaration.IndexOf(" Como ", StringComparison.OrdinalIgnoreCase);
        var names = separator >= 0 ? declaration[..separator] : declaration;

        foreach (var rawName in names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Identifier.IsMatch(rawName))
            {
                _diagnostics.Add($"Linea {lineNumber}: '{rawName}' no es un nombre de variable valido.");
                continue;
            }

            _variables.TryAdd(rawName, 0d);
        }
    }

    private void Assign(string name, string expression, int lineNumber)
    {
        if (!Identifier.IsMatch(name))
        {
            _diagnostics.Add($"Linea {lineNumber}: '{name}' no es un nombre de variable valido.");
            return;
        }

        _variables[name] = Evaluate(expression, lineNumber);
    }

    private void Write(string expressionList, int lineNumber)
    {
        var parts = SplitArguments(expressionList);
        var builder = new StringBuilder();

        foreach (var part in parts)
        {
            builder.Append(FormatValue(Evaluate(part, lineNumber)));
        }

        _output.Add(builder.ToString());
    }

    private void Read(string variableList, int lineNumber)
    {
        foreach (var name in variableList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Identifier.IsMatch(name))
            {
                _diagnostics.Add($"Linea {lineNumber}: '{name}' no es un nombre de variable valido.");
                continue;
            }

            _pendingInputVariables.Enqueue(name);
        }
    }

    private bool TryRequestNextInput()
    {
        if (_waitingInputVariable is not null)
        {
            return true;
        }

        if (!_pendingInputVariables.TryDequeue(out var name))
        {
            return false;
        }

        _waitingInputVariable = name;
        _output.Add($"? {name}:");
        return true;
    }

    private ExecutionResult BuildResult() =>
        new(
            _diagnostics.Count == 0 && _waitingInputVariable is null,
            _output.ToArray(),
            _diagnostics.ToArray(),
            new Dictionary<string, object?>(_variables),
            _waitingInputVariable is not null,
            _waitingInputVariable);

    private static object? ParseInput(string input)
    {
        input = input.Trim();
        if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariantNumber))
        {
            return invariantNumber;
        }

        if (double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out var currentNumber))
        {
            return currentNumber;
        }

        if (bool.TryParse(input, out var boolean))
        {
            return boolean;
        }

        return input;
    }

    private object? Evaluate(string expression, int lineNumber)
    {
        expression = expression.Trim();
        if (expression.Length == 0)
        {
            return string.Empty;
        }

        if (IsQuoted(expression))
        {
            return expression[1..^1];
        }

        if (_variables.TryGetValue(expression, out var variableValue))
        {
            return variableValue;
        }

        var evaluableExpression = ReplaceVariables(expression, lineNumber);
        try
        {
            var value = new DataTable().Compute(evaluableExpression, null);
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            _diagnostics.Add($"Linea {lineNumber}: no pude evaluar '{expression}'.");
            return string.Empty;
        }
    }

    private string ReplaceVariables(string expression, int lineNumber)
    {
        return Regex.Replace(expression, @"\b[A-Za-z_][A-Za-z0-9_]*\b", match =>
        {
            if (!_variables.TryGetValue(match.Value, out var value))
            {
                _diagnostics.Add($"Linea {lineNumber}: la variable '{match.Value}' no tiene valor.");
                return "0";
            }

            return value is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture) ?? "0"
                : "0";
        });
    }

    private static string RemoveComment(string line)
    {
        var inString = false;
        for (var index = 0; index < line.Length - 1; index++)
        {
            if (line[index] == '"')
            {
                inString = !inString;
            }

            if (!inString && line[index] == '/' && line[index + 1] == '/')
            {
                return line[..index];
            }
        }

        return line;
    }

    private static IReadOnlyList<string> SplitArguments(string text)
    {
        var parts = new List<string>();
        var start = 0;
        var inString = false;

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '"')
            {
                inString = !inString;
            }

            if (!inString && text[index] == ',')
            {
                parts.Add(text[start..index].Trim());
                start = index + 1;
            }
        }

        parts.Add(text[start..].Trim());
        return parts;
    }

    private static bool StartsWithAny(string text, params string[] prefixes) =>
        prefixes.Any(prefix => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool IsQuoted(string text) => text.Length >= 2 && text[0] == '"' && text[^1] == '"';

    private static string FormatValue(object? value) =>
        value switch
        {
            null => string.Empty,
            double number when Math.Abs(number % 1) < 0.0000001 => number.ToString("0", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
}
