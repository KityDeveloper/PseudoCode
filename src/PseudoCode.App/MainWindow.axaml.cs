using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PseudoCode.App.Services;

namespace PseudoCode.App;

public partial class MainWindow : Window
{
    private readonly PseudoInterpreter _interpreter = new();
    private IStorageFile? _currentFile;
    private bool _hasUnsavedChanges;
    private bool _isLightTheme;
    private bool _isHelpVisible = true;

    public MainWindow()
    {
        InitializeComponent();
        EditorTextBox.Text = SampleProgram;
        _hasUnsavedChanges = false;
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
        _currentFile = null;
        _hasUnsavedChanges = false;
        CurrentPathText.Text = "Sin guardar";
        UpdateWindowState("Nuevo algoritmo");
    }

    private void Run_Click(object? sender, RoutedEventArgs e)
    {
        var result = _interpreter.Run(EditorTextBox.Text ?? string.Empty);
        OutputTextBox.Text = BuildOutputText(result);
        VariablesTextBox.Text = BuildVariablesText(result);
        UpdateWindowState(result.Success ? "Ejecucion completada" : "Ejecucion con diagnosticos");
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

    private void Editor_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _hasUnsavedChanges = true;
        UpdateLineNumbers();
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
        UpdateLineNumbers();
        UpdateWindowState($"Abierto: {file.Name}");
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
        LineNumbersText.Text = string.Join(Environment.NewLine, Enumerable.Range(1, lines));
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

    private void LoadHelpExample(HelpTopic topic)
    {
        EditorTextBox.Text = topic.Example;
        OutputTextBox.Text = string.Empty;
        VariablesTextBox.Text = string.Empty;
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
    }

    private SolidColorBrush Brush(string key) => (SolidColorBrush)Resources[key]!;

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
            "Leer usa una entrada simulada y Escribir muestra texto en la salida.",
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
