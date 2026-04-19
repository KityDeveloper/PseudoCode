using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using PseudoCode.App.Services;

namespace PseudoCode.App;

internal sealed class AdvancedPseudoInterpreter
{
    private static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private const int MaxExecutionSteps = 100000;
    private const int MaxOutputLines = 5000;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(50);
    private readonly PseudoLanguageDefinition _language;
    private readonly Dictionary<string, object?> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _declaredVariables = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _output = [];
    private readonly List<string> _diagnostics = [];
    private readonly List<string> _inputs = [];
    private IReadOnlyList<Node> _program = [];
    private readonly List<DebugFrame> _debugFrames = [];
    private int _inputIndex;
    private string? _waitingInputVariable;
    private bool _outputLineOpen;
    private int _executionSteps;
    private bool _halted;
    private bool _stoppedByUser;
    private bool _outputLimitReported;
    private CancellationToken _cancellationToken;
    private ManualResetEventSlim? _pauseGate;
    private Action<ExecutionResult>? _progressReporter;
    private DateTime _lastProgressReport = DateTime.MinValue;

    public AdvancedPseudoInterpreter(PseudoLanguageDefinition language)
    {
        _language = language;
    }

    public ExecutionResult Start(
        string source,
        CancellationToken cancellationToken = default,
        ManualResetEventSlim? pauseGate = null,
        Action<ExecutionResult>? progressReporter = null)
    {
        _inputs.Clear();
        ResetExecutionState();
        _cancellationToken = cancellationToken;
        _pauseGate = pauseGate;
        _progressReporter = progressReporter;
        _program = Parse(source);
        if (_diagnostics.Count > 0)
        {
            ReportProgress(force: true);
            return BuildResult();
        }

        ExecuteBlock(_program);
        ReportProgress(force: true);
        return BuildResult();
    }

    public ExecutionResult Continue(string input)
    {
        if (_waitingInputVariable is null)
        {
            _diagnostics.Add($"Linea 1: no hay ninguna instruccion {_language.Keyword("read")} esperando datos. Causa: la ejecucion no esta pausada. Solucion: ejecuta un algoritmo con {_language.Keyword("read")} antes de enviar datos.");
            return BuildResult();
        }

        _inputs.Add(input);
        return ExecuteFromStart();
    }

    public DebugStepResult StartDebug(string source)
    {
        _inputs.Clear();
        ResetExecutionState();
        _program = Parse(source);
        _debugFrames.Clear();
        if (_diagnostics.Count > 0)
        {
            return BuildDebugResult(isFinished: true);
        }

        _debugFrames.Add(new BlockFrame(_program));
        return BuildDebugResult(isFinished: false);
    }

    public DebugStepResult StepDebug()
    {
        if (_waitingInputVariable is not null)
        {
            return BuildDebugResult(isFinished: false);
        }

        ExecuteDebugStep();
        return BuildDebugResult(_debugFrames.Count == 0 && _waitingInputVariable is null);
    }

    public DebugStepResult ContinueDebug(string input)
    {
        if (_waitingInputVariable is null)
        {
            _diagnostics.Add($"Linea 1: no hay ninguna instruccion {_language.Keyword("read")} esperando datos. Causa: la depuracion no esta pausada esperando entrada. Solucion: avanza hasta una instruccion {_language.Keyword("read")}.");
            return BuildDebugResult(isFinished: _debugFrames.Count == 0);
        }

        _inputs.Add(input);
        _waitingInputVariable = null;
        ExecuteDebugStep();
        return BuildDebugResult(_debugFrames.Count == 0 && _waitingInputVariable is null);
    }

    private ExecutionResult ExecuteFromStart()
    {
        ResetExecutionState();
        ExecuteBlock(_program);
        return BuildResult();
    }

    private void ResetExecutionState()
    {
        _variables.Clear();
        _declaredVariables.Clear();
        _output.Clear();
        _diagnostics.Clear();
        _waitingInputVariable = null;
        _outputLineOpen = false;
        _executionSteps = 0;
        _halted = false;
        _stoppedByUser = false;
        _outputLimitReported = false;
        _inputIndex = 0;
        _cancellationToken = default;
        _pauseGate = null;
        _progressReporter = null;
        _lastProgressReport = DateTime.MinValue;
    }

    private IReadOnlyList<Node> Parse(string source)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n')
            .Select((text, index) => new SourceLine(index + 1, NormalizeLine(RemoveComment(text))))
            .Where(line => line.Text.Length > 0)
            .ToArray();
        var index = 0;
        return ParseBlock(lines, ref index, []);
    }

    private List<Node> ParseBlock(IReadOnlyList<SourceLine> lines, ref int index, params string[] terminators)
    {
        var nodes = new List<Node>();
        while (index < lines.Count)
        {
            var text = lines[index].Text;
            if (terminators.Any(term => IsTerminator(text, term)) ||
                terminators.Contains("Case") && (IsSwitchCase(text) || IsOtherwise(text)))
            {
                break;
            }

            nodes.Add(ParseNode(lines, ref index));
        }

        return nodes;
    }

    private bool IsTerminator(string text, string term) => term switch
    {
        "Sino" => _language.IsKeyword(text, "else"),
        "FinSi" => _language.IsKeyword(text, "endIf"),
        "FinMientras" => _language.IsKeyword(text, "endWhile"),
        "FinPara" => _language.IsKeyword(text, "endFor"),
        "FinSegun" => _language.IsKeyword(text, "endSwitch"),
        _ => text.Equals(term, StringComparison.OrdinalIgnoreCase)
    };

    private Node ParseNode(IReadOnlyList<SourceLine> lines, ref int index)
    {
        var line = lines[index];
        var text = line.Text;

        if (_language.IsKeyword(text, "algorithmStart") ||
            _language.IsKeyword(text, "processStart") ||
            _language.StartsWithKeyword(text, "algorithmStart") ||
            _language.StartsWithKeyword(text, "processStart") ||
            _language.IsKeyword(text, "algorithmEnd") ||
            _language.IsKeyword(text, "processEnd"))
        {
            index++;
            return new NoOp(line.Number);
        }

        if (_language.StartsWithKeyword(text, "declare"))
        {
            index++;
            var declaration = _language.RemoveKeywordPrefix(text, "declare");
            var separator = declaration.IndexOf($" {_language.Keyword("typeSeparator")} ", StringComparison.OrdinalIgnoreCase);
            return new Declare(line.Number, SplitNames(separator >= 0 ? declaration[..separator] : declaration));
        }

        if (_language.StartsWithKeyword(text, "write"))
        {
            index++;
            var payload = _language.RemoveKeywordPrefix(text, "write");
            var withoutNewline = TryRemoveTrailingKeyword(ref payload, _language.Keyword("withoutNewline"));
            return new Write(line.Number, SplitArguments(payload), withoutNewline);
        }

        if (_language.StartsWithKeyword(text, "read"))
        {
            index++;
            return new Read(line.Number, SplitNames(_language.RemoveKeywordPrefix(text, "read")));
        }

        if (IsClearScreen(text))
        {
            index++;
            return new Clear(line.Number);
        }

        if (_language.StartsWithKeyword(text, "wait"))
        {
            index++;
            return ParseWait(line.Number, _language.RemoveKeywordPrefix(text, "wait"));
        }

        var ifMatch = Regex.Match(text, $@"^{_language.RegexKeyword("if")}\s+(.+)\s+{_language.RegexKeyword("then")}$", RegexOptions.IgnoreCase);
        if (ifMatch.Success)
        {
            index++;
            var thenBody = ParseBlock(lines, ref index, "Sino", "FinSi");
            var elseBody = new List<Node>();
            if (index < lines.Count && _language.IsKeyword(lines[index].Text, "else"))
            {
                index++;
                elseBody = ParseBlock(lines, ref index, "FinSi");
            }
            Consume(lines, ref index, "FinSi", line.Number, _language.Keyword("if"));
            return new If(line.Number, ifMatch.Groups[1].Value.Trim(), thenBody, elseBody);
        }

        var whileMatch = Regex.Match(text, $@"^{_language.RegexKeyword("while")}\s+(.+)\s+{_language.RegexKeyword("do")}$", RegexOptions.IgnoreCase);
        if (whileMatch.Success)
        {
            index++;
            var body = ParseBlock(lines, ref index, "FinMientras");
            Consume(lines, ref index, "FinMientras", line.Number, _language.Keyword("while"));
            return new While(line.Number, whileMatch.Groups[1].Value.Trim(), body);
        }

        var forMatch = Regex.Match(text, $@"^{_language.RegexKeyword("for")}\s+([A-Za-z_][A-Za-z0-9_]*)\s*<-\s*(.+)\s+{_language.RegexKeyword("until")}\s+(.+)\s+{_language.RegexKeyword("do")}$", RegexOptions.IgnoreCase);
        if (forMatch.Success)
        {
            index++;
            var body = ParseBlock(lines, ref index, "FinPara");
            Consume(lines, ref index, "FinPara", line.Number, _language.Keyword("for"));
            return new For(line.Number, forMatch.Groups[1].Value, forMatch.Groups[2].Value.Trim(), forMatch.Groups[3].Value.Trim(), body);
        }

        var switchMatch = Regex.Match(text, $@"^{_language.RegexKeyword("switch")}\s+(.+)\s+{_language.RegexKeyword("do")}$", RegexOptions.IgnoreCase);
        if (switchMatch.Success)
        {
            return ParseSwitch(lines, ref index, line.Number, switchMatch.Groups[1].Value.Trim());
        }

        var assignmentIndex = text.IndexOf("<-", StringComparison.Ordinal);
        if (assignmentIndex > 0)
        {
            index++;
            return new Assign(line.Number, text[..assignmentIndex].Trim(), text[(assignmentIndex + 2)..].Trim());
        }

        _diagnostics.Add($"Linea {line.Number}: instruccion desconocida. Causa: '{text}' no coincide con el dialecto '{_language.DisplayName}'. Solucion: revisa la palabra clave o consulta la ayuda.");
        index++;
        return new NoOp(line.Number);
    }

    private Node ParseSwitch(IReadOnlyList<SourceLine> lines, ref int index, int lineNumber, string expression)
    {
        index++;
        var cases = new List<SwitchCase>();
        List<Node> defaultBody = [];
        while (index < lines.Count && !_language.IsKeyword(lines[index].Text, "endSwitch"))
        {
            var line = lines[index];
            if (IsOtherwise(line.Text))
            {
                index++;
                defaultBody = ParseBlock(lines, ref index, "FinSegun", "Case");
                continue;
            }
            if (IsSwitchCase(line.Text))
            {
                index++;
                var values = line.Text.TrimEnd(':').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                cases.Add(new SwitchCase(line.Number, values, ParseBlock(lines, ref index, "FinSegun", "Case")));
                continue;
            }
            _diagnostics.Add($"Linea {line.Number}: caso mal formado en {_language.Keyword("switch")}. Causa: los casos deben terminar con ':'. Solucion: usa '1:' o '{_language.Keyword("otherwise")}:'.");
            index++;
        }
        Consume(lines, ref index, "FinSegun", lineNumber, _language.Keyword("switch"));
        return new Switch(lineNumber, expression, cases, defaultBody);
    }

    private void Consume(IReadOnlyList<SourceLine> lines, ref int index, string terminator, int lineNumber, string blockName)
    {
        var expected = terminator switch
        {
            "FinSi" => _language.Keyword("endIf"),
            "FinMientras" => _language.Keyword("endWhile"),
            "FinPara" => _language.Keyword("endFor"),
            "FinSegun" => _language.Keyword("endSwitch"),
            _ => terminator
        };

        if (index < lines.Count && lines[index].Text.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            index++;
            return;
        }
        _diagnostics.Add($"Linea {lineNumber}: falta '{expected}'. Causa: el bloque '{blockName}' quedo abierto. Solucion: agrega '{expected}'.");
    }

    private void ExecuteBlock(IReadOnlyList<Node> nodes)
    {
        foreach (var node in nodes)
        {
            if (_waitingInputVariable is not null || _halted) return;
            Execute(node);
        }
    }

    private void Execute(Node node)
    {
        if (_halted || !CheckExecutionControl(node.Line))
        {
            return;
        }

        _executionSteps++;
        if (_executionSteps > MaxExecutionSteps)
        {
            _halted = true;
            AddRuntime(node.Line, "ejecucion detenida por seguridad", $"supero {MaxExecutionSteps} pasos", $"usa {_language.Keyword("wait")}, {_language.Keyword("clear")} {_language.Keyword("screen")} o reduce los ciclos");
            return;
        }

        switch (node)
        {
            case NoOp:
                return;
            case Declare declare:
                foreach (var name in declare.Names)
                {
                    if (Identifier.IsMatch(name))
                    {
                        _declaredVariables.Add(name);
                        _variables.TryAdd(name, 0d);
                    }
                    else AddRuntime(node.Line, $"'{name}' no es un nombre valido", "usa caracteres no permitidos", "usa letras, numeros y guion bajo");
                }
                return;
            case Assign assign:
                if (!Identifier.IsMatch(assign.Name)) AddRuntime(node.Line, $"'{assign.Name}' no es un destino valido", "el lado izquierdo debe ser variable", "usa 'total <- 10'");
                else if (!EnsureDeclared(assign.Name, node.Line)) return;
                else _variables[assign.Name] = EvaluateValue(assign.Expression, node.Line);
                return;
            case Write write:
                WriteOutput(string.Concat(write.Expressions.Select(expression => FormatValue(EvaluateValue(expression, node.Line)))), write.WithoutNewline);
                return;
            case Read read:
                foreach (var name in read.Names)
                {
                    if (!EnsureDeclared(name, node.Line)) return;
                    AddOutput($"? {name}:");
                    _outputLineOpen = false;
                    if (_inputIndex >= _inputs.Count)
                    {
                        _waitingInputVariable = name;
                        return;
                    }
                    var input = _inputs[_inputIndex++];
                    _variables[name] = ParseInput(input);
                    AddOutput($"> {input}");
                    _outputLineOpen = false;
                }
                return;
            case Clear:
                _output.Clear();
                _outputLineOpen = false;
                _outputLimitReported = false;
                ReportProgress();
                return;
            case Wait wait:
                var duration = Math.Max(0, ToNumber(EvaluateValue(wait.DurationExpression, node.Line)));
                var milliseconds = wait.Unit.Equals("milliseconds", StringComparison.OrdinalIgnoreCase)
                    ? duration
                    : duration * 1000;
                ReportProgress(force: true);
                WaitWithControl(milliseconds, node.Line);
                return;
            case If conditional:
                ExecuteBlock(ToBoolean(EvaluateCondition(conditional.Condition, node.Line)) ? conditional.ThenBody : conditional.ElseBody);
                return;
            case While loop:
                for (var guard = 0; ToBoolean(EvaluateCondition(loop.Condition, node.Line)); guard++)
                {
                    if (guard > 10000) { AddRuntime(node.Line, $"ciclo {_language.Keyword("while")} detenido", "supero 10000 iteraciones", "revisa que la condicion cambie"); return; }
                    ExecuteBlock(loop.Body);
                    if (_waitingInputVariable is not null || _halted) return;
                }
                return;
            case For loop:
                if (!EnsureDeclared(loop.Variable, node.Line)) return;
                var start = ToNumber(EvaluateValue(loop.Start, node.Line));
                var end = ToNumber(EvaluateValue(loop.End, node.Line));
                for (var value = start; value <= end; value++)
                {
                    _variables[loop.Variable] = value;
                    ExecuteBlock(loop.Body);
                    if (_waitingInputVariable is not null || _halted) return;
                }
                return;
            case Switch selection:
                var selected = EvaluateValue(selection.Expression, node.Line);
                foreach (var option in selection.Cases)
                {
                    if (option.Values.Any(value => ValuesEqual(selected, EvaluateValue(value, option.Line))))
                    {
                        ExecuteBlock(option.Body);
                        return;
                    }
                }
                ExecuteBlock(selection.DefaultBody);
                return;
        }
    }

    private void ExecuteDebugStep()
    {
        NormalizeDebugFrames();
        if (_debugFrames.Count == 0)
        {
            return;
        }

        var frame = _debugFrames[^1];
        var node = frame.Current;
        if (node is null)
        {
            NormalizeDebugFrames();
            return;
        }

        switch (node)
        {
            case Read:
                Execute(node);
                if (_waitingInputVariable is null)
                {
                    frame.Advance();
                }
                break;
            case If conditional:
                frame.Advance();
                _debugFrames.Add(new BlockFrame(ToBoolean(EvaluateCondition(conditional.Condition, node.Line)) ? conditional.ThenBody : conditional.ElseBody));
                break;
            case While loop:
                frame.Advance();
                if (ToBoolean(EvaluateCondition(loop.Condition, node.Line)))
                {
                    _debugFrames.Add(new WhileFrame(loop));
                }
                break;
            case For loop:
                frame.Advance();
                if (!EnsureDeclared(loop.Variable, node.Line))
                {
                    break;
                }
                var start = ToNumber(EvaluateValue(loop.Start, node.Line));
                var end = ToNumber(EvaluateValue(loop.End, node.Line));
                if (start <= end)
                {
                    _variables[loop.Variable] = start;
                    _debugFrames.Add(new ForFrame(loop, end, start));
                }
                break;
            case Switch selection:
                frame.Advance();
                var selected = EvaluateValue(selection.Expression, node.Line);
                var selectedBody = selection.DefaultBody;
                foreach (var option in selection.Cases)
                {
                    if (option.Values.Any(value => ValuesEqual(selected, EvaluateValue(value, option.Line))))
                    {
                        selectedBody = option.Body;
                        break;
                    }
                }
                _debugFrames.Add(new BlockFrame(selectedBody));
                break;
            default:
                frame.Advance();
                Execute(node);
                break;
        }

        NormalizeDebugFrames();
    }

    private void NormalizeDebugFrames()
    {
        while (_debugFrames.Count > 0)
        {
            var frame = _debugFrames[^1];
            if (!frame.IsComplete)
            {
                return;
            }

            _debugFrames.RemoveAt(_debugFrames.Count - 1);
            switch (frame)
            {
                case WhileFrame whileFrame:
                    whileFrame.Iteration++;
                    if (whileFrame.Iteration > 10000)
                    {
                        AddRuntime(whileFrame.Loop.Line, $"ciclo {_language.Keyword("while")} detenido", "supero 10000 iteraciones", "revisa que la condicion cambie");
                        continue;
                    }
                    if (ToBoolean(EvaluateCondition(whileFrame.Loop.Condition, whileFrame.Loop.Line)))
                    {
                        whileFrame.Reset();
                        _debugFrames.Add(whileFrame);
                        return;
                    }
                    break;
                case ForFrame forFrame:
                    var next = forFrame.CurrentValue + 1;
                    if (next <= forFrame.End)
                    {
                        _variables[forFrame.Loop.Variable] = next;
                        forFrame.CurrentValue = next;
                        forFrame.Reset();
                        _debugFrames.Add(forFrame);
                        return;
                    }
                    break;
            }
        }
    }

    private DebugStepResult BuildDebugResult(bool isFinished)
    {
        NormalizeDebugFrames();
        return new DebugStepResult(BuildResult(), _debugFrames.LastOrDefault()?.Current?.Line, isFinished || _debugFrames.Count == 0);
    }

    private Wait ParseWait(int lineNumber, string payload)
    {
        payload = payload.Trim();
        var milliseconds = _language.Keyword("milliseconds");
        var seconds = _language.Keyword("seconds");

        if (TryRemoveTrailingKeyword(ref payload, milliseconds))
        {
            return new Wait(lineNumber, payload, "milliseconds");
        }

        if (TryRemoveTrailingWord(ref payload, "Milisegundo") ||
            TryRemoveTrailingWord(ref payload, "Milisegundos"))
        {
            return new Wait(lineNumber, payload, "milliseconds");
        }

        if (TryRemoveTrailingKeyword(ref payload, seconds) ||
            TryRemoveTrailingWord(ref payload, "Segundo") ||
            TryRemoveTrailingWord(ref payload, "Segundos"))
        {
            return new Wait(lineNumber, payload, "seconds");
        }

        return new Wait(lineNumber, payload, "seconds");
    }

    private void WriteOutput(string text, bool withoutNewline)
    {
        if (_outputLineOpen && _output.Count > 0)
        {
            _output[^1] += text;
        }
        else
        {
            AddOutput(text);
        }

        _outputLineOpen = withoutNewline;
        ReportProgress();
    }

    private bool CheckExecutionControl(int line)
    {
        if (_cancellationToken.IsCancellationRequested)
        {
            StopByCancellation(line);
            return false;
        }

        try
        {
            _pauseGate?.Wait(_cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            StopByCancellation(line);
            return false;
        }
    }

    private void WaitWithControl(double milliseconds, int line)
    {
        var remaining = Math.Min(milliseconds, 60000);
        while (remaining > 0 && !_halted)
        {
            if (!CheckExecutionControl(line))
            {
                return;
            }

            var slice = (int)Math.Min(remaining, 100);
            if (_cancellationToken.WaitHandle.WaitOne(slice))
            {
                StopByCancellation(line);
                return;
            }

            remaining -= slice;
        }
    }

    private void StopByCancellation(int line)
    {
        if (_halted)
        {
            return;
        }

        _halted = true;
        _stoppedByUser = true;
    }

    private void AddOutput(string text)
    {
        if (_output.Count >= MaxOutputLines)
        {
            if (!_outputLimitReported)
            {
                _outputLimitReported = true;
                _halted = true;
                AddRuntime(1, "salida detenida por seguridad", $"supero {MaxOutputLines} lineas", $"usa {_language.Keyword("clear")} {_language.Keyword("screen")} dentro de ciclos largos o reduce la cantidad de {_language.Keyword("write")}");
            }

            return;
        }

        _output.Add(text);
        ReportProgress();
    }

    private void ReportProgress(bool force = false)
    {
        if (_progressReporter is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (!force && now - _lastProgressReport < ProgressInterval)
        {
            return;
        }

        _lastProgressReport = now;
        _progressReporter(BuildResult());
    }

    private object? EvaluateValue(string expression, int line)
    {
        expression = expression.Trim();
        if (expression.Length == 0) return string.Empty;
        if (IsQuoted(expression)) return expression[1..^1];
        if (_language.IsKeyword(expression, "true")) return true;
        if (_language.IsKeyword(expression, "false")) return false;
        if (_variables.TryGetValue(expression, out var variable)) return variable;
        if (Identifier.IsMatch(expression) && !EnsureDeclared(expression, line)) return string.Empty;
        try
        {
            return Convert.ToDouble(new DataTable().Compute(ReplaceVariables(expression, line), null), CultureInfo.InvariantCulture);
        }
        catch
        {
            AddRuntime(line, $"no pude evaluar '{expression}'", "la expresion no es valida", "revisa operadores, parentesis y variables");
            return string.Empty;
        }
    }

    private object? EvaluateCondition(string condition, int line)
    {
        condition = condition.Trim();
        var not = _language.Keyword("not");
        if (condition.StartsWith(not + " ", StringComparison.OrdinalIgnoreCase)) return !ToBoolean(EvaluateCondition(condition[not.Length..], line));
        var orParts = SplitLogical(condition, _language.Keyword("or"));
        if (orParts.Count > 1) return orParts.Any(part => ToBoolean(EvaluateCondition(part, line)));
        var andParts = SplitLogical(condition, _language.Keyword("and"));
        if (andParts.Count > 1) return andParts.All(part => ToBoolean(EvaluateCondition(part, line)));
        var comparison = FindComparison(condition);
        if (comparison is null) return EvaluateValue(condition, line);
        var left = EvaluateValue(condition[..comparison.Value.Index], line);
        var right = EvaluateValue(condition[(comparison.Value.Index + comparison.Value.Operator.Length)..], line);
        return Compare(left, right, comparison.Value.Operator);
    }

    private string ReplaceVariables(string expression, int line)
    {
        var result = new System.Text.StringBuilder();
        var start = 0;
        var inString = false;
        for (var index = 0; index < expression.Length; index++)
        {
            if (expression[index] != '"')
            {
                continue;
            }

            if (inString)
            {
                result.Append(expression[start..(index + 1)]);
                start = index + 1;
                inString = false;
            }
            else
            {
                result.Append(ReplaceVariablesInPlainText(expression[start..index], line));
                start = index;
                inString = true;
            }
        }

        if (start < expression.Length)
        {
            var tail = expression[start..];
            result.Append(inString ? tail : ReplaceVariablesInPlainText(tail, line));
        }

        return result.ToString();
    }

    private string ReplaceVariablesInPlainText(string text, int line) =>
        Regex.Replace(text, @"\b[A-Za-z_][A-Za-z0-9_]*\b", match =>
        {
            if (_language.IsKeyword(match.Value, "true")) return "true";
            if (_language.IsKeyword(match.Value, "false")) return "false";
            if (!_variables.TryGetValue(match.Value, out var value))
            {
                EnsureDeclared(match.Value, line);
                return "0";
            }
            return value is bool boolean ? (boolean ? "true" : "false") : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
        });

    private bool EnsureDeclared(string name, int line)
    {
        if (_declaredVariables.Contains(name))
        {
            return true;
        }

        _halted = true;
        AddRuntime(line, $"la variable '{name}' no esta declarada", $"se usa antes de {_language.Keyword("declare")}", $"agrega '{_language.Keyword("declare")} {name} {_language.Keyword("typeSeparator")} Real' antes de usarla");
        return false;
    }

    private ExecutionResult BuildResult() => new(
        _diagnostics.Count == 0 && _waitingInputVariable is null && !_stoppedByUser,
        _output.ToArray(),
        _diagnostics.ToArray(),
        new Dictionary<string, object?>(_variables),
        _waitingInputVariable is not null,
        _waitingInputVariable,
        _stoppedByUser);

    private void AddRuntime(int line, string problem, string cause, string solution) =>
        _diagnostics.Add($"Linea {line}: {problem}. Causa: {cause}. Solucion: {solution}.");

    private static (int Index, string Operator)? FindComparison(string text)
    {
        var inString = false;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '"') { inString = !inString; continue; }
            if (inString) continue;
            foreach (var op in new[] { "<>", "<=", ">=", "=", "<", ">" })
                if (index + op.Length <= text.Length && text.Substring(index, op.Length) == op) return (index, op);
        }
        return null;
    }

    private static bool Compare(object? left, object? right, string op)
    {
        if (left is string || right is string)
        {
            var comparison = string.Compare(Convert.ToString(left, CultureInfo.InvariantCulture), Convert.ToString(right, CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
            return op switch { "=" => comparison == 0, "<>" => comparison != 0, "<" => comparison < 0, "<=" => comparison <= 0, ">" => comparison > 0, ">=" => comparison >= 0, _ => false };
        }
        var l = ToNumber(left);
        var r = ToNumber(right);
        return op switch { "=" => Math.Abs(l - r) < 0.0000001, "<>" => Math.Abs(l - r) >= 0.0000001, "<" => l < r, "<=" => l <= r, ">" => l > r, ">=" => l >= r, _ => false };
    }

    private static bool ValuesEqual(object? left, object? right) =>
        left is string || right is string
            ? string.Equals(Convert.ToString(left, CultureInfo.InvariantCulture), Convert.ToString(right, CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            : Math.Abs(ToNumber(left) - ToNumber(right)) < 0.0000001;

    private static List<string> SplitLogical(string text, string op)
    {
        var parts = new List<string>();
        var start = 0;
        var inString = false;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '"') { inString = !inString; continue; }
            if (!inString && IsWordAt(text, op, index))
            {
                parts.Add(text[start..index].Trim());
                start = index + op.Length;
            }
        }
        parts.Add(text[start..].Trim());
        return parts;
    }

    private static bool IsWordAt(string text, string word, int index)
    {
        if (index + word.Length > text.Length || !text.AsSpan(index, word.Length).Equals(word, StringComparison.OrdinalIgnoreCase)) return false;
        return (index == 0 || !char.IsLetterOrDigit(text[index - 1])) &&
               (index + word.Length == text.Length || !char.IsLetterOrDigit(text[index + word.Length]));
    }

    private static double ToNumber(object? value) =>
        value switch
        {
            double number => number,
            int integer => integer,
            bool boolean => boolean ? 1 : 0,
            _ when double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0
        };

    private static bool ToBoolean(object? value) =>
        value switch
        {
            bool boolean => boolean,
            double number => Math.Abs(number) > 0.0000001,
            string text when bool.TryParse(text, out var boolean) => boolean,
            string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => Math.Abs(number) > 0.0000001,
            string text => !string.IsNullOrWhiteSpace(text),
            _ => value is not null
        };

    private static object? ParseInput(string input)
    {
        input = input.Trim();
        if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariantNumber)) return invariantNumber;
        if (double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out var currentNumber)) return currentNumber;
        if (bool.TryParse(input, out var boolean)) return boolean;
        return input;
    }

    private static IReadOnlyList<string> SplitArguments(string text)
    {
        var parts = new List<string>();
        var start = 0;
        var inString = false;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '"') inString = !inString;
            if (!inString && text[index] == ',')
            {
                parts.Add(text[start..index].Trim());
                start = index + 1;
            }
        }
        parts.Add(text[start..].Trim());
        return parts;
    }

    private static IReadOnlyList<string> SplitNames(string text) => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private bool IsQuoted(string text) => text.Length >= 2 && text[0] == '"' && text[^1] == '"';
    private bool IsSwitchCase(string text) => text.EndsWith(':') && !IsOtherwise(text);
    private bool IsOtherwise(string text) => text.Equals(_language.Keyword("otherwise") + ":", StringComparison.OrdinalIgnoreCase) || text.Equals(_language.Keyword("otherwise"), StringComparison.OrdinalIgnoreCase);
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

    private static string RemoveComment(string line)
    {
        var inString = false;
        for (var index = 0; index < line.Length - 1; index++)
        {
            if (line[index] == '"') inString = !inString;
            if (!inString && line[index] == '/' && line[index + 1] == '/') return line[..index];
        }
        return line;
    }

    private static string FormatValue(object? value) => value switch
    {
        null => string.Empty,
        double number when Math.Abs(number % 1) < 0.0000001 => number.ToString("0", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty
    };

    private sealed record SourceLine(int Number, string Text);
    private abstract record Node(int Line);
    private sealed record NoOp(int Line) : Node(Line);
    private sealed record Declare(int Line, IReadOnlyList<string> Names) : Node(Line);
    private sealed record Assign(int Line, string Name, string Expression) : Node(Line);
    private sealed record Write(int Line, IReadOnlyList<string> Expressions, bool WithoutNewline) : Node(Line);
    private sealed record Read(int Line, IReadOnlyList<string> Names) : Node(Line);
    private sealed record Clear(int Line) : Node(Line);
    private sealed record Wait(int Line, string DurationExpression, string Unit) : Node(Line);
    private sealed record If(int Line, string Condition, IReadOnlyList<Node> ThenBody, IReadOnlyList<Node> ElseBody) : Node(Line);
    private sealed record While(int Line, string Condition, IReadOnlyList<Node> Body) : Node(Line);
    private sealed record For(int Line, string Variable, string Start, string End, IReadOnlyList<Node> Body) : Node(Line);
    private sealed record Switch(int Line, string Expression, IReadOnlyList<SwitchCase> Cases, IReadOnlyList<Node> DefaultBody) : Node(Line);
    private sealed record SwitchCase(int Line, IReadOnlyList<string> Values, IReadOnlyList<Node> Body);

    private abstract class DebugFrame
    {
        protected DebugFrame(IReadOnlyList<Node> nodes)
        {
            Nodes = nodes;
        }

        public IReadOnlyList<Node> Nodes { get; }
        public int Index { get; private set; }
        public Node? Current => Index < Nodes.Count ? Nodes[Index] : null;
        public bool IsComplete => Index >= Nodes.Count;

        public void Advance()
        {
            Index++;
        }

        public void Reset()
        {
            Index = 0;
        }
    }

    private sealed class BlockFrame(IReadOnlyList<Node> nodes) : DebugFrame(nodes);

    private sealed class WhileFrame(While loop) : DebugFrame(loop.Body)
    {
        public While Loop { get; } = loop;
        public int Iteration { get; set; }
    }

    private sealed class ForFrame(For loop, double end, double currentValue) : DebugFrame(loop.Body)
    {
        public For Loop { get; } = loop;
        public double End { get; } = end;
        public double CurrentValue { get; set; } = currentValue;
    }
}
