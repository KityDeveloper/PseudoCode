using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using PseudoCode.App.Services;

namespace PseudoCode.App;

public partial class MainWindow : Window
{
    private readonly PseudoInterpreter _interpreter = new();
    private IStorageFile? _currentFile;
    private bool _hasUnsavedChanges;
    private bool _isLightTheme;
    private bool _isHelpVisible = true;
    private CompletionWindow? _completionWindow;
    private PseudoCodeColorizer? _colorizer;
    private ExecutionResult? _lastExecutionResult;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureEditor();
        EditorTextBox.Text = SampleProgram;
        _hasUnsavedChanges = false;
        BuildEditorTools();
        BuildHelpTopics();
        DragDrop.SetAllowDrop(this, true);
        DragDrop.SetAllowDrop(EditorTextBox, true);
        AddHandler(DragDrop.DragOverEvent, Editor_DragOver);
        AddHandler(DragDrop.DropEvent, Editor_Drop);
        UpdateLineNumbers();
        UpdateWindowState("Listo para escribir pseudocodigo");
    }

    private async void OpenFile_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir algoritmo",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Pseudocodigo")
                {
                    Patterns = ["*.psc", "*.pse", "*.txt"]
                },
                FilePickerFileTypes.All
            ]
        });

        var file = files.FirstOrDefault();
        if (file is null)
        {
            return;
        }

        await LoadFileAsync(file);
    }

    private async void SaveFile_Click(object? sender, RoutedEventArgs e)
    {
        await SaveCurrentFileAsync();
    }

    private void NewFile_Click(object? sender, RoutedEventArgs e)
    {
        EditorTextBox.Text = SampleProgram;
        OutputTextBox.Text = string.Empty;
        VariablesTextBox.Text = string.Empty;
        HideConsoleInput();
        _lastExecutionResult = null;
        _currentFile = null;
        _hasUnsavedChanges = false;
        CurrentPathText.Text = "Sin guardar";
        UpdateWindowState("Nuevo algoritmo");
    }

    private void Run_Click(object? sender, RoutedEventArgs e)
    {
        HideConsoleInput();
        _lastExecutionResult = _interpreter.Start(EditorTextBox.Text ?? string.Empty);
        ShowExecutionResult(_lastExecutionResult);
    }

    private void SendConsoleInput_Click(object? sender, RoutedEventArgs e)
    {
        SendConsoleInput();
    }

    private void ConsoleInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SendConsoleInput();
            e.Handled = true;
        }
    }

    private void SelectAll_Click(object? sender, RoutedEventArgs e)
    {
        EditorTextBox.Focus();
        EditorTextBox.SelectAll();
        UpdateWindowState("Texto seleccionado");
    }

    private void ClearOutput_Click(object? sender, RoutedEventArgs e)
    {
        OutputTextBox.Text = string.Empty;
        VariablesTextBox.Text = string.Empty;
        HideConsoleInput();
        _lastExecutionResult = null;
        UpdateWindowState("Salida limpia");
    }

    private void ToggleHelp_Click(object? sender, RoutedEventArgs e)
    {
        _isHelpVisible = !_isHelpVisible;
        HelpPanel.IsVisible = _isHelpVisible;
        MainLayout.ColumnDefinitions[3].Width = _isHelpVisible ? new GridLength(6) : new GridLength(0);
        MainLayout.ColumnDefinitions[4].Width = _isHelpVisible ? new GridLength(340) : new GridLength(0);
        UpdateWindowState(_isHelpVisible ? "Ayuda visible" : "Ayuda oculta");
    }

    private void LoadFirstHelpExample_Click(object? sender, RoutedEventArgs e)
    {
        LoadHelpExample(HelpTopics[0]);
    }

    private void ThemeToggle_Click(object? sender, RoutedEventArgs e)
    {
        _isLightTheme = !_isLightTheme;
        ApplyTheme(_isLightTheme ? LightTheme : DarkTheme);
        ThemeToggleButton.Content = _isLightTheme ? "Modo oscuro" : "Modo claro";
        UpdateWindowState(_isLightTheme ? "Modo claro activado" : "Modo oscuro activado");
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        _hasUnsavedChanges = true;
        UpdateLineNumbers();
        UpdateVariablesList();
        UpdateWindowState("Editando");
    }

    private async Task SaveCurrentFileAsync()
    {
        var file = _currentFile;
        if (file is null)
        {
            file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Guardar algoritmo",
                SuggestedFileName = "pseudo-main.psc",
                FileTypeChoices =
                [
                    new FilePickerFileType("Pseudocodigo")
                    {
                        Patterns = ["*.psc"]
                    },
                    FilePickerFileTypes.TextPlain
                ],
                DefaultExtension = "psc",
                ShowOverwritePrompt = true
            });

            if (file is null)
            {
                return;
            }

            _currentFile = file;
            CurrentPathText.Text = file.Path.LocalPath;
        }

        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        await writer.WriteAsync(EditorTextBox.Text ?? string.Empty);
        _hasUnsavedChanges = false;
        UpdateWindowState($"Guardado: {file.Name}");
    }

    private async Task LoadFileAsync(IStorageFile file)
    {
        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        EditorTextBox.Text = await reader.ReadToEndAsync();
        _currentFile = file;
        _hasUnsavedChanges = false;
        CurrentPathText.Text = file.Path.LocalPath;
        OutputTextBox.Text = string.Empty;
        VariablesTextBox.Text = string.Empty;
        HideConsoleInput();
        _lastExecutionResult = null;
        UpdateLineNumbers();
        UpdateWindowState($"Abierto: {file.Name}");
    }

    private void SendConsoleInput()
    {
        if (_lastExecutionResult?.WaitingForInput != true)
        {
            HideConsoleInput();
            return;
        }

        var input = ConsoleInputTextBox.Text ?? string.Empty;
        ConsoleInputTextBox.Text = string.Empty;
        _lastExecutionResult = _interpreter.Continue(input);
        ShowExecutionResult(_lastExecutionResult);
    }

    private void ShowExecutionResult(ExecutionResult result)
    {
        OutputTextBox.Text = BuildOutputText(result);
        VariablesTextBox.Text = BuildVariablesText(result);

        if (result.WaitingForInput)
        {
            ShowConsoleInput(result.InputVariable ?? "valor");
            UpdateWindowState($"Esperando entrada: {result.InputVariable}");
        }
        else
        {
            HideConsoleInput();
            UpdateWindowState(result.Success ? "Ejecucion completada" : "Ejecucion con diagnosticos");
        }
    }

    private void ShowConsoleInput(string variableName)
    {
        ConsoleInputPrompt.Text = $"{variableName}:";
        ConsoleInputPanel.IsVisible = true;
        ConsoleInputTextBox.Focus();
    }

    private void HideConsoleInput()
    {
        ConsoleInputPanel.IsVisible = false;
        ConsoleInputTextBox.Text = string.Empty;
    }

    private void ConfigureEditor()
    {
        EditorTextBox.Options.ConvertTabsToSpaces = true;
        EditorTextBox.Options.IndentationSize = 4;
        _colorizer = new PseudoCodeColorizer(PseudoCodeColorizer.DarkPalette);
        EditorTextBox.TextArea.TextView.LineTransformers.Add(_colorizer);
        ApplyEditorTheme(DarkTheme, PseudoCodeColorizer.DarkPalette);
        EditorTextBox.TextArea.TextEntered += Editor_TextEntered;
        EditorTextBox.TextArea.TextEntering += Editor_TextEntering;
        EditorTextBox.TextArea.KeyDown += Editor_KeyDown;
    }

    private void Editor_TextEntered(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Text))
        {
            return;
        }

        if (char.IsLetter(e.Text[0]))
        {
            ShowCompletion();
        }
    }

    private void Editor_TextEntering(object? sender, TextInputEventArgs e)
    {
        if (_completionWindow is not null && !string.IsNullOrEmpty(e.Text) && !char.IsLetterOrDigit(e.Text[0]))
        {
            _completionWindow.CompletionList.RequestInsertion(e);
        }
    }

    private void Editor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ShowCompletion(force: true);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Tab)
        {
            InsertAtCaret("    ");
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && _completionWindow is null)
        {
            InsertSmartNewLine();
            e.Handled = true;
        }
    }

    private void ShowCompletion(bool force = false)
    {
        var prefix = GetCurrentWord();
        if (!force && prefix.Length < 2)
        {
            return;
        }

        var matches = CompletionItems
            .Where(item => item.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Text.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            .ThenBy(item => item.Text)
            .ToArray();

        if (matches.Length == 0 && !force)
        {
            return;
        }

        _completionWindow?.Close();
        _completionWindow = new CompletionWindow(EditorTextBox.TextArea)
        {
            Width = 360,
            Height = 260
        };
        _completionWindow.Closed += (_, _) => _completionWindow = null;

        var data = _completionWindow.CompletionList.CompletionData;
        foreach (var item in matches.Length == 0 ? CompletionItems : matches)
        {
            data.Add(new PseudoCompletionData(item));
        }

        _completionWindow.Show();
    }

    private string GetCurrentWord()
    {
        var offset = EditorTextBox.CaretOffset;
        var document = EditorTextBox.Document;
        var start = offset;

        while (start > 0)
        {
            var character = document.GetCharAt(start - 1);
            if (!char.IsLetterOrDigit(character) && character != '_')
            {
                break;
            }

            start--;
        }

        return document.GetText(start, offset - start);
    }

    private void InsertSmartNewLine()
    {
        var document = EditorTextBox.Document;
        var offset = EditorTextBox.CaretOffset;
        var line = document.GetLineByOffset(offset);
        var lineText = document.GetText(line.Offset, offset - line.Offset);
        var currentIndent = Regex.Match(lineText, @"^\s*").Value;
        var trimmed = lineText.Trim();
        var nextIndent = currentIndent;

        if (StartsLogicalBlock(trimmed))
        {
            nextIndent += "    ";
        }

        InsertAtCaret(Environment.NewLine + nextIndent);
    }

    private void InsertAtCaret(string text)
    {
        EditorTextBox.Document.Insert(EditorTextBox.CaretOffset, text);
        EditorTextBox.CaretOffset += text.Length;
    }

    private void Editor_DragOver(object? sender, DragEventArgs e)
    {
        var hasFiles = e.DataTransfer.TryGetFiles()?.Any() == true;
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = hasFiles;
    }

    private async void Editor_Drop(object? sender, DragEventArgs e)
    {
        var file = e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().FirstOrDefault();
        if (file is null)
        {
            return;
        }

        await LoadFileAsync(file);
        e.Handled = true;
    }

    private void UpdateLineNumbers()
    {
        var text = EditorTextBox.Text ?? string.Empty;
        var lines = Math.Max(1, text.Count(character => character == '\n') + 1);
        CursorStatusText.Text = $"Lineas: {lines}";
    }

    private void UpdateWindowState(string message)
    {
        WindowStateText.Text = _hasUnsavedChanges ? $"{message} - sin guardar" : message;
        Title = _hasUnsavedChanges ? "PseudoCode *" : "PseudoCode";
    }

    private void BuildHelpTopics()
    {
        foreach (var topic in HelpTopics)
        {
            var description = new TextBlock
            {
                Text = topic.Description,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Foreground = Brush("TextSecondary"),
                Margin = new Avalonia.Thickness(0, 0, 0, 8)
            };

            var preview = new TextBox
            {
                Text = topic.Example,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
                Background = Brush("EditorBackground"),
                Foreground = Brush("TextPrimary"),
                BorderBrush = Brush("BorderBrushMuted"),
                BorderThickness = new Avalonia.Thickness(1),
                FontFamily = new FontFamily("Cascadia Code,Consolas,monospace"),
                FontSize = 12,
                Height = 140,
                Padding = new Avalonia.Thickness(8),
                Margin = new Avalonia.Thickness(0, 0, 0, 8)
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(preview, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(preview, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

            var loadButton = new Button
            {
                Content = "Cargar ejemplo",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };
            loadButton.Classes.Add("command");
            loadButton.Click += (_, _) => LoadHelpExample(topic);

            var content = new StackPanel
            {
                Spacing = 4,
                Margin = new Avalonia.Thickness(8, 6, 8, 10),
                Children =
                {
                    description,
                    preview,
                    loadButton
                }
            };

            HelpTopicsPanel.Children.Add(new Expander
            {
                Header = topic.Title,
                IsExpanded = topic.IsExpanded,
                Foreground = Brush("TextPrimary"),
                Background = Brush("InsetBackground"),
                BorderBrush = Brush("BorderBrushMuted"),
                BorderThickness = new Avalonia.Thickness(1),
                Content = content
            });
        }
    }

    private void BuildEditorTools()
    {
        foreach (var template in CompletionItems.Where(item => item.IsTemplate))
        {
            var button = new Button
            {
                Content = template.Text,
                Margin = new Avalonia.Thickness(0, 0, 6, 6),
                Padding = new Avalonia.Thickness(8, 5),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            button.Classes.Add("command");
            ToolTip.SetTip(button, template.Description);
            button.Click += (_, _) => InsertCommandTemplate(template.InsertText);
            TemplatesPanel.Children.Add(button);
        }

        CommandsList.ItemsSource = CompletionItems
            .Select(item => $"{item.Text} - {item.Description}")
            .ToArray();
        OperatorsList.ItemsSource = new[]
        {
            "<- asignacion",
            "+ suma",
            "- resta",
            "* multiplicacion",
            "/ division",
            "% modulo",
            "= igual",
            "<> diferente",
            "< <= > >= comparaciones",
            "Y, O, NO operadores logicos"
        };
        UpdateVariablesList();
    }

    private void InsertCommandTemplate(string template)
    {
        InsertAtCaret(template);
        EditorTextBox.Focus();
        UpdateWindowState("Plantilla insertada");
    }

    private void UpdateVariablesList()
    {
        var names = ExtractVariables(EditorTextBox.Text ?? string.Empty).ToArray();
        VariablesList.ItemsSource = names.Length == 0 ? new[] { "Sin variables todavia" } : names;
    }

    private static IEnumerable<string> ExtractVariables(string source)
    {
        var variables = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(source, @"(?im)^\s*Definir\s+(.+?)(?:\s+Como\s+\w+)?\s*$"))
        {
            foreach (var name in match.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                {
                    variables.Add(name);
                }
            }
        }

        foreach (Match match in Regex.Matches(source, @"(?m)^\s*([A-Za-z_][A-Za-z0-9_]*)\s*<-"))
        {
            variables.Add(match.Groups[1].Value);
        }

        return variables;
    }

    private void LoadHelpExample(HelpTopic topic)
    {
        EditorTextBox.Text = topic.Example;
        OutputTextBox.Text = string.Empty;
        VariablesTextBox.Text = string.Empty;
        HideConsoleInput();
        _lastExecutionResult = null;
        _currentFile = null;
        _hasUnsavedChanges = true;
        CurrentPathText.Text = "Ejemplo de ayuda";
        UpdateLineNumbers();
        UpdateWindowState($"Ejemplo cargado: {topic.Title}");
    }

    private void ApplyTheme(IReadOnlyDictionary<string, string> colors)
    {
        foreach (var (key, value) in colors)
        {
            if (Resources[key] is SolidColorBrush brush)
            {
                brush.Color = Color.Parse(value);
            }
        }

        ApplyEditorTheme(colors, _isLightTheme ? PseudoCodeColorizer.LightPalette : PseudoCodeColorizer.DarkPalette);
    }

    private void ApplyEditorTheme(IReadOnlyDictionary<string, string> colors, PseudoCodeColorPalette syntaxPalette)
    {
        EditorTextBox.Background = BrushFromTheme(colors, "EditorBackground");
        EditorTextBox.Foreground = BrushFromTheme(colors, "TextPrimary");
        EditorTextBox.TextArea.Caret.CaretBrush = BrushFromTheme(colors, "CaretBrush");
        EditorTextBox.TextArea.SelectionBrush = BrushFromTheme(colors, "SelectionBrush");
        EditorTextBox.TextArea.SelectionForeground = BrushFromTheme(colors, "TextPrimary");
        EditorTextBox.LineNumbersForeground = BrushFromTheme(colors, "LineNumberText");
        EditorTextBox.TextArea.TextView.CurrentLineBackground = BrushFromTheme(colors, "LineNumberBackground");
        EditorTextBox.TextArea.TextView.CurrentLineBorder = new Pen(BrushFromTheme(colors, "BorderBrushMuted"), 1);
        _colorizer?.SetPalette(syntaxPalette);
        EditorTextBox.TextArea.TextView.Redraw();
    }

    private static SolidColorBrush BrushFromTheme(IReadOnlyDictionary<string, string> colors, string key) =>
        new(Color.Parse(colors[key]));

    private SolidColorBrush Brush(string key) => (SolidColorBrush)Resources[key]!;

    private static bool StartsLogicalBlock(string text) =>
        StartsWithAny(text, "Algoritmo ", "Proceso ", "Si ", "Mientras ", "Para ", "Segun ");

    private static bool StartsWithAny(string text, params string[] prefixes) =>
        prefixes.Any(prefix => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static string BuildOutputText(ExecutionResult result)
    {
        var builder = new StringBuilder();

        if (result.Output.Count == 0)
        {
            builder.AppendLine("Sin salida. Usa Escribir para mostrar datos.");
        }
        else
        {
            foreach (var line in result.Output)
            {
                builder.AppendLine(line);
            }
        }

        if (result.Diagnostics.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Diagnosticos:");
            foreach (var diagnostic in result.Diagnostics)
            {
                builder.AppendLine(diagnostic);
            }
        }

        return builder.ToString();
    }

    private static string BuildVariablesText(ExecutionResult result)
    {
        if (result.Variables.Count == 0)
        {
            return "Sin variables.";
        }

        var builder = new StringBuilder();
        foreach (var (name, value) in result.Variables.OrderBy(pair => pair.Key))
        {
            builder.AppendLine($"{name} = {value}");
        }

        return builder.ToString();
    }

    private sealed record HelpTopic(string Title, string Description, string Example, bool IsExpanded = false);

    private static readonly CommandInfo[] CompletionItems =
    [
        new("Algoritmo", "Algoritmo MiPrograma\n    \nFinAlgoritmo", "Define el inicio y fin de un algoritmo.", true),
        new("Definir", "Definir variable Como Entero", "Declara una o varias variables.", true),
        new("Escribir", "Escribir \"Mensaje\", variable", "Muestra texto o valores en la salida.", true),
        new("Leer", "Leer variable", "Espera un valor en la consola antes de continuar.", true),
        new("Si", "Si condicion Entonces\n    \nFinSi", "Bloque condicional.", true),
        new("Si/Sino", "Si condicion Entonces\n    \nSino\n    \nFinSi", "Condicional con alternativa.", true),
        new("Mientras", "Mientras condicion Hacer\n    \nFinMientras", "Repite mientras se cumpla una condicion.", true),
        new("Para", "Para i <- 1 Hasta 10 Hacer\n    \nFinPara", "Repite con contador.", true),
        new("Segun", "Segun opcion Hacer\n    1:\n        \nFinSegun", "Seleccion multiple.", true),
        new("Entero", "Entero", "Tipo numerico entero."),
        new("Real", "Real", "Tipo numerico decimal."),
        new("Cadena", "Cadena", "Tipo de texto."),
        new("Logico", "Logico", "Tipo verdadero/falso."),
        new("Verdadero", "Verdadero", "Valor logico verdadero."),
        new("Falso", "Falso", "Valor logico falso."),
        new("Y", "Y", "Operador logico AND."),
        new("O", "O", "Operador logico OR."),
        new("NO", "NO", "Negacion logica.")
    ];

    private static readonly HelpTopic[] HelpTopics =
    [
        new(
            "Estructura base",
            "La forma minima de un algoritmo: inicio, instrucciones y cierre.",
            """
Algoritmo MiPrograma
    Escribir "Hola desde PseudoCode"
FinAlgoritmo
""",
            true),
        new(
            "Variables",
            "Declara datos con Definir y guarda valores con <-.",
            """
Algoritmo Variables
    Definir edad Como Entero
    Definir nombre Como Cadena

    nombre <- "Ada"
    edad <- 19

    Escribir "Nombre: ", nombre
    Escribir "Edad: ", edad
FinAlgoritmo
"""),
        new(
            "Entrada y salida",
            "Leer pausa la ejecucion hasta que escribas un valor en la consola.",
            """
Algoritmo EntradaSalida
    Definir numero Como Entero

    Leer numero
    Escribir "Numero recibido: ", numero
FinAlgoritmo
"""),
        new(
            "Operaciones",
            "Puedes combinar numeros y variables en expresiones aritmeticas.",
            """
Algoritmo Operaciones
    Definir a, b, total Como Entero

    a <- 8
    b <- 4
    total <- a + b * 2

    Escribir "Resultado: ", total
FinAlgoritmo
""")
    ];

    private static readonly IReadOnlyDictionary<string, string> DarkTheme = new Dictionary<string, string>
    {
        ["PanelBackground"] = "#252526",
        ["EditorBackground"] = "#1E1E1E",
        ["ActivityBackground"] = "#181818",
        ["TopBarBackground"] = "#2D2D30",
        ["TabBackground"] = "#1E1E1E",
        ["InsetBackground"] = "#1F1F1F",
        ["LineNumberBackground"] = "#1A1A1A",
        ["OutputBackground"] = "#111111",
        ["VariablesBackground"] = "#151515",
        ["ButtonBackground"] = "#2D2D30",
        ["ButtonHoverBackground"] = "#37373D",
        ["ButtonBorder"] = "#4B4B4B",
        ["ButtonHoverBorder"] = "#5D5D5D",
        ["BorderBrushMuted"] = "#3C3C3C",
        ["TextPrimary"] = "#D4D4D4",
        ["TextSecondary"] = "#9DA3AA",
        ["LineNumberText"] = "#858585",
        ["CaretBrush"] = "#FFFFFF",
        ["SelectionBrush"] = "#264F78",
        ["AccentBlue"] = "#007ACC"
    };

    private static readonly IReadOnlyDictionary<string, string> LightTheme = new Dictionary<string, string>
    {
        ["PanelBackground"] = "#F3F4F6",
        ["EditorBackground"] = "#FFFFFF",
        ["ActivityBackground"] = "#E5E7EB",
        ["TopBarBackground"] = "#F8FAFC",
        ["TabBackground"] = "#FFFFFF",
        ["InsetBackground"] = "#FFFFFF",
        ["LineNumberBackground"] = "#F1F5F9",
        ["OutputBackground"] = "#FFFFFF",
        ["VariablesBackground"] = "#F8FAFC",
        ["ButtonBackground"] = "#FFFFFF",
        ["ButtonHoverBackground"] = "#E5E7EB",
        ["ButtonBorder"] = "#CBD5E1",
        ["ButtonHoverBorder"] = "#94A3B8",
        ["BorderBrushMuted"] = "#CBD5E1",
        ["TextPrimary"] = "#111827",
        ["TextSecondary"] = "#475569",
        ["LineNumberText"] = "#64748B",
        ["CaretBrush"] = "#111827",
        ["SelectionBrush"] = "#BFDBFE",
        ["AccentBlue"] = "#2563EB"
    };

    private const string SampleProgram = """
Algoritmo Saludo
    Definir nombre Como Cadena
    Definir edad Como Entero

    nombre <- "Ada"
    edad <- 18 + 1

    Escribir "Hola ", nombre
    Escribir "Edad: ", edad
FinAlgoritmo
""";
}

internal sealed record CommandInfo(string Text, string InsertText, string Description, bool IsTemplate = false);

internal sealed class PseudoCompletionData(CommandInfo item) : ICompletionData
{
    public IImage? Image => null;

    public string Text => item.Text;

    public object Content => item.Text;

    public object Description => item.Description;

    public double Priority => item.IsTemplate ? 1 : 0;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment, item.InsertText);
    }
}

internal sealed record PseudoCodeColorPalette(
    IBrush KeywordBrush,
    IBrush TypeBrush,
    IBrush StringBrush,
    IBrush NumberBrush,
    IBrush OperatorBrush,
    IBrush CommentBrush,
    IBrush BlockBrush);

internal sealed class PseudoCodeColorizer(PseudoCodeColorPalette palette) : DocumentColorizingTransformer
{
    private static readonly Regex StringLiteral = new("\"[^\"]*\"", RegexOptions.Compiled);
    private static readonly Regex NumberLiteral = new(@"\b\d+(\.\d+)?\b", RegexOptions.Compiled);
    private static readonly Regex TypeName = new(@"\b(Entero|Real|Cadena|Logico|Caracter)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Operator = new(@"(<-|<=|>=|<>|=|<|>|\+|-|\*|/|%)", RegexOptions.Compiled);
    private static readonly Regex Keyword = new(@"\b(Algoritmo|Proceso|FinAlgoritmo|FinProceso|Definir|Como|Escribir|Leer|Si|Entonces|Sino|FinSi|Mientras|Hacer|FinMientras|Para|Hasta|Con|Paso|FinPara|Segun|FinSegun|Verdadero|Falso|Y|O|NO)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BlockLine = new(@"^\s*(Algoritmo|Proceso|Si|Sino|FinSi|Mientras|FinMientras|Para|FinPara|Segun|FinSegun|FinAlgoritmo|FinProceso)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static readonly PseudoCodeColorPalette DarkPalette = new(
        Brush("#5EA1FF"),
        Brush("#4EC9B0"),
        Brush("#CE9178"),
        Brush("#B5CEA8"),
        Brush("#DCDCAA"),
        Brush("#6A9955"),
        Brush("#1F3B4D"));

    public static readonly PseudoCodeColorPalette LightPalette = new(
        Brush("#0645AD"),
        Brush("#00796B"),
        Brush("#A31515"),
        Brush("#098658"),
        Brush("#795E26"),
        Brush("#008000"),
        Brush("#EAF3FF"));

    private PseudoCodeColorPalette _palette = palette;

    public void SetPalette(PseudoCodeColorPalette newPalette)
    {
        _palette = newPalette;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        var text = CurrentContext.Document.GetText(line);

        if (BlockLine.IsMatch(text))
        {
            ChangeLinePart(line.Offset, line.EndOffset, element =>
            {
                element.TextRunProperties.SetBackgroundBrush(_palette.BlockBrush);
            });
        }

        var commentIndex = FindCommentIndex(text);
        var codeLength = commentIndex >= 0 ? commentIndex : text.Length;

        ApplyMatches(line, text, Keyword, _palette.KeywordBrush, codeLength);
        ApplyMatches(line, text, TypeName, _palette.TypeBrush, codeLength);
        ApplyMatches(line, text, StringLiteral, _palette.StringBrush, codeLength);
        ApplyMatches(line, text, NumberLiteral, _palette.NumberBrush, codeLength);
        ApplyMatches(line, text, Operator, _palette.OperatorBrush, codeLength);

        if (commentIndex >= 0)
        {
            ChangeLinePart(line.Offset + commentIndex, line.EndOffset, element =>
            {
                element.TextRunProperties.SetForegroundBrush(_palette.CommentBrush);
            });
        }
    }

    private void ApplyMatches(DocumentLine line, string text, Regex regex, IBrush brush, int codeLength)
    {
        foreach (Match match in regex.Matches(text[..codeLength]))
        {
            ChangeLinePart(line.Offset + match.Index, line.Offset + match.Index + match.Length, element =>
            {
                element.TextRunProperties.SetForegroundBrush(brush);
            });
        }
    }

    private static int FindCommentIndex(string text)
    {
        var inString = false;
        for (var index = 0; index < text.Length - 1; index++)
        {
            if (text[index] == '"')
            {
                inString = !inString;
            }

            if (!inString && text[index] == '/' && text[index + 1] == '/')
            {
                return index;
            }
        }

        return -1;
    }

    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}
