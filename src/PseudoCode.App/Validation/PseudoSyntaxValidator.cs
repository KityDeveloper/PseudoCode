using System.Text.RegularExpressions;

namespace PseudoCode.App;

internal sealed class PseudoSyntaxValidator
{
    private static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private readonly PseudoLanguageDefinition _language;

    public PseudoSyntaxValidator(PseudoLanguageDefinition language)
    {
        _language = language;
    }

    public IReadOnlyList<string> Validate(string source)
    {
        var diagnostics = new List<string>();
        var blocks = new Stack<(string Name, string CloseRole, int Line)>();
        var declaredVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = source.Replace("\r\n", "\n").Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var line = NormalizeLine(RemoveComment(lines[index]));
            if (line.Length == 0)
            {
                continue;
            }

            ValidateLine(line, lineNumber, diagnostics, blocks, declaredVariables);
        }

        while (blocks.TryPop(out var block))
        {
            diagnostics.Add($"Linea {block.Line}: falta cerrar '{block.Name}'. Causa: el bloque quedo abierto. Solucion: agrega '{_language.Keyword(block.CloseRole)}'.");
        }

        return diagnostics;
    }

    private void ValidateLine(
        string line,
        int lineNumber,
        List<string> diagnostics,
        Stack<(string Name, string CloseRole, int Line)> blocks,
        HashSet<string> declaredVariables)
    {
        if (line.Count(character => character == '"') % 2 != 0)
        {
            diagnostics.Add($"Linea {lineNumber}: comillas sin cerrar. Causa: falta una comilla doble. Solucion: cierra el texto con \".");
            return;
        }

        if (_language.IsKeyword(line, "algorithmStart") ||
            _language.IsKeyword(line, "processStart") ||
            _language.StartsWithKeyword(line, "algorithmStart") ||
            _language.StartsWithKeyword(line, "processStart"))
        {
            var isProcess = _language.IsKeyword(line, "processStart") || _language.StartsWithKeyword(line, "processStart");
            blocks.Push((isProcess ? _language.Keyword("processStart") : _language.Keyword("algorithmStart"), isProcess ? "processEnd" : "algorithmEnd", lineNumber));
            return;
        }

        if (_language.IsKeyword(line, "algorithmEnd"))
        {
            CloseBlock(lineNumber, "algorithmEnd", diagnostics, blocks);
            return;
        }

        if (_language.IsKeyword(line, "processEnd"))
        {
            CloseBlock(lineNumber, "processEnd", diagnostics, blocks);
            return;
        }

        if (_language.StartsWithKeyword(line, "declare"))
        {
            ValidateDeclaration(_language.RemoveKeywordPrefix(line, "declare"), lineNumber, diagnostics, declaredVariables);
            return;
        }

        if (_language.StartsWithKeyword(line, "write"))
        {
            var payload = _language.RemoveKeywordPrefix(line, "write");
            _ = TryRemoveTrailingKeyword(ref payload, _language.Keyword("withoutNewline"));
            ValidateExpression(payload, lineNumber, _language.Keyword("write"), diagnostics);
            ValidateExpressionVariables(payload, lineNumber, diagnostics, declaredVariables);
            return;
        }

        if (_language.StartsWithKeyword(line, "read"))
        {
            ValidateIdentifierList(_language.RemoveKeywordPrefix(line, "read"), lineNumber, _language.Keyword("read"), diagnostics, declaredVariables, requireDeclared: true);
            return;
        }

        if (IsClearScreen(line))
        {
            return;
        }

        if (_language.StartsWithKeyword(line, "wait"))
        {
            ValidateWait(_language.RemoveKeywordPrefix(line, "wait"), lineNumber, diagnostics, declaredVariables);
            return;
        }

        var ifMatch = Regex.Match(line, $@"^{_language.RegexKeyword("if")}\s+(.+)\s+{_language.RegexKeyword("then")}$", RegexOptions.IgnoreCase);
        if (ifMatch.Success)
        {
            ValidateExpressionVariables(ifMatch.Groups[1].Value, lineNumber, diagnostics, declaredVariables);
            blocks.Push((_language.Keyword("if"), "endIf", lineNumber));
            return;
        }

        if (_language.IsKeyword(line, "else"))
        {
            if (!blocks.Any(block => block.CloseRole.Equals("endIf", StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add($"Linea {lineNumber}: '{_language.Keyword("else")}' no corresponde a ningun '{_language.Keyword("if")}'. Causa: falta abrir un bloque. Solucion: usa '{_language.Keyword("if")} condicion {_language.Keyword("then")}' antes de '{_language.Keyword("else")}'.");
            }
            return;
        }

        if (_language.IsKeyword(line, "endIf"))
        {
            CloseBlock(lineNumber, "endIf", diagnostics, blocks);
            return;
        }

        var whileMatch = Regex.Match(line, $@"^{_language.RegexKeyword("while")}\s+(.+)\s+{_language.RegexKeyword("do")}$", RegexOptions.IgnoreCase);
        if (whileMatch.Success)
        {
            ValidateExpressionVariables(whileMatch.Groups[1].Value, lineNumber, diagnostics, declaredVariables);
            blocks.Push((_language.Keyword("while"), "endWhile", lineNumber));
            return;
        }

        if (_language.IsKeyword(line, "endWhile"))
        {
            CloseBlock(lineNumber, "endWhile", diagnostics, blocks);
            return;
        }

        var forMatch = Regex.Match(line, $@"^{_language.RegexKeyword("for")}\s+([A-Za-z_][A-Za-z0-9_]*)\s*<-\s*(.+)\s+{_language.RegexKeyword("until")}\s+(.+)\s+{_language.RegexKeyword("do")}$", RegexOptions.IgnoreCase);
        if (forMatch.Success)
        {
            ValidateDeclaredVariable(forMatch.Groups[1].Value, lineNumber, diagnostics, declaredVariables);
            ValidateExpressionVariables(forMatch.Groups[2].Value, lineNumber, diagnostics, declaredVariables);
            ValidateExpressionVariables(forMatch.Groups[3].Value, lineNumber, diagnostics, declaredVariables);
            blocks.Push((_language.Keyword("for"), "endFor", lineNumber));
            return;
        }

        if (_language.IsKeyword(line, "endFor"))
        {
            CloseBlock(lineNumber, "endFor", diagnostics, blocks);
            return;
        }

        var switchMatch = Regex.Match(line, $@"^{_language.RegexKeyword("switch")}\s+(.+)\s+{_language.RegexKeyword("do")}$", RegexOptions.IgnoreCase);
        if (switchMatch.Success)
        {
            ValidateExpressionVariables(switchMatch.Groups[1].Value, lineNumber, diagnostics, declaredVariables);
            blocks.Push((_language.Keyword("switch"), "endSwitch", lineNumber));
            return;
        }

        if (_language.IsKeyword(line, "endSwitch"))
        {
            CloseBlock(lineNumber, "endSwitch", diagnostics, blocks);
            return;
        }

        if (Regex.IsMatch(line, @"^.+:\s*$"))
        {
            var label = line.TrimEnd(':').Trim();
            if (!_language.IsKeyword(label, "otherwise"))
            {
                ValidateExpressionVariables(label, lineNumber, diagnostics, declaredVariables);
            }
            return;
        }

        var assignmentIndex = line.IndexOf("<-", StringComparison.Ordinal);
        if (assignmentIndex > 0)
        {
            ValidateAssignment(line, assignmentIndex, lineNumber, diagnostics, declaredVariables);
            return;
        }

        diagnostics.Add($"Linea {lineNumber}: instruccion desconocida. Causa: '{line}' no coincide con el dialecto '{_language.DisplayName}'. Solucion: revisa la palabra clave o consulta la ayuda.");
    }

    private void ValidateDeclaration(string declaration, int lineNumber, List<string> diagnostics, HashSet<string> declaredVariables)
    {
        var separator = declaration.IndexOf($" {_language.Keyword("typeSeparator")} ", StringComparison.OrdinalIgnoreCase);
        var names = separator >= 0 ? declaration[..separator] : declaration;
        ValidateIdentifierList(names, lineNumber, _language.Keyword("declare"), diagnostics, declaredVariables);

        if (separator >= 0)
        {
            var typeName = declaration[(separator + _language.Keyword("typeSeparator").Length + 2)..].Trim();
            if (typeName.Length == 0 || !_language.Types.Contains(typeName, StringComparer.OrdinalIgnoreCase))
            {
                diagnostics.Add($"Linea {lineNumber}: tipo '{typeName}' no reconocido. Causa: el tipo esta vacio o no existe. Solucion: usa uno de los tipos del dialecto activo.");
            }
        }
    }

    private void ValidateIdentifierList(
        string text,
        int lineNumber,
        string instruction,
        List<string> diagnostics,
        HashSet<string> declaredVariables,
        bool requireDeclared = false)
    {
        var names = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Length == 0)
        {
            diagnostics.Add($"Linea {lineNumber}: '{instruction}' necesita al menos una variable. Causa: la lista esta vacia. Solucion: agrega un nombre valido.");
            return;
        }

        foreach (var name in names)
        {
            if (!Identifier.IsMatch(name))
            {
                diagnostics.Add($"Linea {lineNumber}: '{name}' no es un nombre de variable valido. Causa: usa caracteres no permitidos o inicia con numero. Solucion: usa letras, numeros y guion bajo, empezando con letra.");
                continue;
            }

            if (requireDeclared)
            {
                ValidateDeclaredVariable(name, lineNumber, diagnostics, declaredVariables);
            }
            else
            {
                declaredVariables.Add(name);
            }
        }
    }

    private static void ValidateExpression(string text, int lineNumber, string instruction, List<string> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            diagnostics.Add($"Linea {lineNumber}: '{instruction}' necesita una expresion. Causa: no hay nada para procesar. Solucion: agrega texto, variable o expresion.");
        }
    }

    private void ValidateAssignment(string line, int assignmentIndex, int lineNumber, List<string> diagnostics, HashSet<string> declaredVariables)
    {
        var name = line[..assignmentIndex].Trim();
        var expression = line[(assignmentIndex + 2)..].Trim();
        if (!Identifier.IsMatch(name))
        {
            diagnostics.Add($"Linea {lineNumber}: '{name}' no es un destino de asignacion valido. Causa: el lado izquierdo debe ser una variable. Solucion: usa algo como 'total <- 10'.");
        }
        else
        {
            ValidateDeclaredVariable(name, lineNumber, diagnostics, declaredVariables);
        }

        if (expression.Length == 0)
        {
            diagnostics.Add($"Linea {lineNumber}: asignacion incompleta. Causa: falta la expresion despues de '<-'. Solucion: agrega un valor o calculo.");
        }
        else
        {
            ValidateExpressionVariables(expression, lineNumber, diagnostics, declaredVariables);
        }
    }

    private void ValidateWait(string payload, int lineNumber, List<string> diagnostics, HashSet<string> declaredVariables)
    {
        payload = payload.Trim();
        _ = TryRemoveTrailingKeyword(ref payload, _language.Keyword("milliseconds")) ||
            TryRemoveTrailingWord(ref payload, "Milisegundo") ||
            TryRemoveTrailingWord(ref payload, "Milisegundos") ||
            TryRemoveTrailingKeyword(ref payload, _language.Keyword("seconds")) ||
            TryRemoveTrailingWord(ref payload, "Segundo") ||
            TryRemoveTrailingWord(ref payload, "Segundos");

        if (payload.Length == 0)
        {
            diagnostics.Add($"Linea {lineNumber}: '{_language.Keyword("wait")}' necesita una duracion. Causa: no hay tiempo indicado. Solucion: usa '{_language.Keyword("wait")} 1 {_language.Keyword("seconds")}'.");
        }
        else
        {
            ValidateExpressionVariables(payload, lineNumber, diagnostics, declaredVariables);
        }
    }

    private void ValidateDeclaredVariable(string name, int lineNumber, List<string> diagnostics, HashSet<string> declaredVariables)
    {
        if (!declaredVariables.Contains(name))
        {
            diagnostics.Add($"Linea {lineNumber}: la variable '{name}' no esta declarada. Causa: se usa antes de una instruccion '{_language.Keyword("declare")}'. Solucion: agrega '{_language.Keyword("declare")} {name} {_language.Keyword("typeSeparator")} Real' antes de usarla.");
        }
    }

    private void ValidateExpressionVariables(string expression, int lineNumber, List<string> diagnostics, HashSet<string> declaredVariables)
    {
        foreach (var name in FindIdentifiersOutsideStrings(expression).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IsExpressionKeyword(name))
            {
                continue;
            }

            ValidateDeclaredVariable(name, lineNumber, diagnostics, declaredVariables);
        }
    }

    private bool IsExpressionKeyword(string name) =>
        _language.IsKeyword(name, "true") ||
        _language.IsKeyword(name, "false") ||
        _language.IsKeyword(name, "and") ||
        _language.IsKeyword(name, "or") ||
        _language.IsKeyword(name, "not");

    private static IEnumerable<string> FindIdentifiersOutsideStrings(string expression)
    {
        var start = 0;
        var inString = false;
        for (var index = 0; index < expression.Length; index++)
        {
            if (expression[index] != '"')
            {
                continue;
            }

            if (!inString && start < index)
            {
                foreach (Match match in Regex.Matches(expression[start..index], @"\b[A-Za-z_][A-Za-z0-9_]*\b"))
                {
                    yield return match.Value;
                }
            }

            inString = !inString;
            start = index + 1;
        }

        if (!inString && start < expression.Length)
        {
            foreach (Match match in Regex.Matches(expression[start..], @"\b[A-Za-z_][A-Za-z0-9_]*\b"))
            {
                yield return match.Value;
            }
        }
    }

    private void CloseBlock(int lineNumber, string expectedCloseRole, List<string> diagnostics, Stack<(string Name, string CloseRole, int Line)> blocks)
    {
        if (!blocks.TryPop(out var opened))
        {
            diagnostics.Add($"Linea {lineNumber}: cierre '{_language.Keyword(expectedCloseRole)}' sin bloque abierto. Causa: sobra un cierre. Solucion: elimina este cierre o agrega el bloque inicial.");
            return;
        }

        if (!opened.CloseRole.Equals(expectedCloseRole, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add($"Linea {lineNumber}: cierre incorrecto. Causa: se esperaba cerrar '{opened.Name}', pero aparece '{_language.Keyword(expectedCloseRole)}'. Solucion: cambia el cierre o revisa el orden de los bloques.");
        }
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

    private bool IsClearScreen(string text) =>
        _language.IsKeyword(text, "clear") ||
        text.Equals($"{_language.Keyword("clear")} {_language.Keyword("screen")}", StringComparison.OrdinalIgnoreCase);

    private static bool TryRemoveTrailingKeyword(ref string text, string keyword) => TryRemoveTrailingWord(ref text, keyword);

    private static bool TryRemoveTrailingWord(ref string text, string word)
    {
        text = text.Trim();
        if (!text.EndsWith(word, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var start = text.Length - word.Length;
        if (start > 0 && !char.IsWhiteSpace(text[start - 1]))
        {
            return false;
        }

        text = text[..start].TrimEnd();
        return true;
    }

    private static string NormalizeLine(string line) => StripTrailingSemicolon(line.Trim());

    private static string StripTrailingSemicolon(string text)
    {
        var inString = false;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '"') inString = !inString;
        }

        return !inString && text.EndsWith(';') ? text[..^1].TrimEnd() : text;
    }
}
