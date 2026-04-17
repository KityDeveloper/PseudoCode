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
        var lines = source.Replace("\r\n", "\n").Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var line = RemoveComment(lines[index]).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            ValidateLine(line, lineNumber, diagnostics, blocks);
        }

        while (blocks.TryPop(out var block))
        {
            diagnostics.Add($"Linea {block.Line}: falta cerrar '{block.Name}'. Causa: el bloque quedo abierto. Solucion: agrega '{_language.Keyword(block.CloseRole)}'.");
        }

        return diagnostics;
    }

    private void ValidateLine(string line, int lineNumber, List<string> diagnostics, Stack<(string Name, string CloseRole, int Line)> blocks)
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
            ValidateDeclaration(_language.RemoveKeywordPrefix(line, "declare"), lineNumber, diagnostics);
            return;
        }

        if (_language.StartsWithKeyword(line, "write"))
        {
            ValidateExpression(_language.RemoveKeywordPrefix(line, "write"), lineNumber, _language.Keyword("write"), diagnostics);
            return;
        }

        if (_language.StartsWithKeyword(line, "read"))
        {
            ValidateIdentifierList(_language.RemoveKeywordPrefix(line, "read"), lineNumber, _language.Keyword("read"), diagnostics);
            return;
        }

        if (Regex.IsMatch(line, $@"^{_language.RegexKeyword("if")}\s+.+\s+{_language.RegexKeyword("then")}$", RegexOptions.IgnoreCase))
        {
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

        if (Regex.IsMatch(line, $@"^{_language.RegexKeyword("while")}\s+.+\s+{_language.RegexKeyword("do")}$", RegexOptions.IgnoreCase))
        {
            blocks.Push((_language.Keyword("while"), "endWhile", lineNumber));
            return;
        }

        if (_language.IsKeyword(line, "endWhile"))
        {
            CloseBlock(lineNumber, "endWhile", diagnostics, blocks);
            return;
        }

        if (Regex.IsMatch(line, $@"^{_language.RegexKeyword("for")}\s+[A-Za-z_][A-Za-z0-9_]*\s*<-\s*.+\s+{_language.RegexKeyword("until")}\s+.+\s+{_language.RegexKeyword("do")}$", RegexOptions.IgnoreCase))
        {
            blocks.Push((_language.Keyword("for"), "endFor", lineNumber));
            return;
        }

        if (_language.IsKeyword(line, "endFor"))
        {
            CloseBlock(lineNumber, "endFor", diagnostics, blocks);
            return;
        }

        if (Regex.IsMatch(line, $@"^{_language.RegexKeyword("switch")}\s+.+\s+{_language.RegexKeyword("do")}$", RegexOptions.IgnoreCase))
        {
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
            return;
        }

        var assignmentIndex = line.IndexOf("<-", StringComparison.Ordinal);
        if (assignmentIndex > 0)
        {
            ValidateAssignment(line, assignmentIndex, lineNumber, diagnostics);
            return;
        }

        diagnostics.Add($"Linea {lineNumber}: instruccion desconocida. Causa: '{line}' no coincide con el dialecto '{_language.DisplayName}'. Solucion: revisa la palabra clave o consulta la ayuda.");
    }

    private void ValidateDeclaration(string declaration, int lineNumber, List<string> diagnostics)
    {
        var separator = declaration.IndexOf($" {_language.Keyword("typeSeparator")} ", StringComparison.OrdinalIgnoreCase);
        var names = separator >= 0 ? declaration[..separator] : declaration;
        ValidateIdentifierList(names, lineNumber, _language.Keyword("declare"), diagnostics);

        if (separator >= 0)
        {
            var typeName = declaration[(separator + _language.Keyword("typeSeparator").Length + 2)..].Trim();
            if (typeName.Length == 0 || !_language.Types.Contains(typeName, StringComparer.OrdinalIgnoreCase))
            {
                diagnostics.Add($"Linea {lineNumber}: tipo '{typeName}' no reconocido. Causa: el tipo esta vacio o no existe. Solucion: usa uno de los tipos del dialecto activo.");
            }
        }
    }

    private static void ValidateIdentifierList(string text, int lineNumber, string instruction, List<string> diagnostics)
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

    private static void ValidateAssignment(string line, int assignmentIndex, int lineNumber, List<string> diagnostics)
    {
        var name = line[..assignmentIndex].Trim();
        var expression = line[(assignmentIndex + 2)..].Trim();
        if (!Identifier.IsMatch(name))
        {
            diagnostics.Add($"Linea {lineNumber}: '{name}' no es un destino de asignacion valido. Causa: el lado izquierdo debe ser una variable. Solucion: usa algo como 'total <- 10'.");
        }

        if (expression.Length == 0)
        {
            diagnostics.Add($"Linea {lineNumber}: asignacion incompleta. Causa: falta la expresion despues de '<-'. Solucion: agrega un valor o calculo.");
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
}
