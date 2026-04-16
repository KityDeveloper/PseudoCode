using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using PseudoCode.App.Services;

namespace PseudoCode.App;

public partial class MainWindow : Window
{
    private const double DefaultEditorFontSize = 15;
    private const double MinEditorFontSize = 10;
    private const double MaxEditorFontSize = 30;
    private const double EditorZoomStep = 1;

    private readonly List<OpenDocument> _openDocuments = [];
    private OpenDocument? _currentDocument;
    private int _newAlgorithmNumber = 1;
    private bool _isSwitchingDocument;
    private bool _showDiagnostics;
    private bool _isLightTheme;
    private bool _isHelpVisible = true;
    private CompletionWindow? _completionWindow;
    private PseudoCodeColorizer? _colorizer;
    private DiagnosticUnderlineRenderer? _diagnosticUnderlineRenderer;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureEditor();
        AddNewDocument();
        BuildEditorTools();
        BuildHelpTopics();
        DragDrop.SetAllowDrop(this, true);
        DragDrop.SetAllowDrop(EditorTextBox, true);
        AddHandler(DragDrop.DragOverEvent, Editor_DragOver);
        AddHandler(DragDrop.DropEvent, Editor_Drop);
        UpdateLineNumbers();
        UpdateWindowState("Listo para escribir pseudocodigo");
        EditorTextBox.Focus();
    }

    private async void OpenFile_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir algoritmo",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Pseudocodigo")
                {
                    Patterns = ["*.psc", "*.pse", "*.txt"]
                },
                FilePickerFileTypes.All
            ]
        });

        foreach (var file in files)
        {
            await LoadFileAsync(file);
        }
    }

    private async void SaveFile_Click(object? sender, RoutedEventArgs e)
    {
        await SaveCurrentFileAsync();
    }

    private async void CloseFile_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentDocument is not null)
        {
            await CloseDocumentAsync(_currentDocument);
        }
    }

    private void NewFile_Click(object? sender, RoutedEventArgs e)
    {
        AddNewDocument();
    }

    private void Run_Click(object? sender, RoutedEventArgs e)
    {
        var document = _currentDocument;
        if (document is null)
        {
            return;
        }

        HideConsoleInput();
        document.Text = EditorTextBox.Text ?? string.Empty;
        var result = document.Interpreter.Start(document.Text);
        document.LastExecutionResult = result;
        ShowExecutionResult(result);
    }

    private void SendConsoleInput_Click(object? sender, RoutedEventArgs e)
    {
        SendConsoleInput();
    }

    private void OutputView_Click(object? sender, RoutedEventArgs e)
    {
        _showDiagnostics = false;
        UpdateOutputPanelView();
    }

    private void DiagnosticsView_Click(object? sender, RoutedEventArgs e)
    {
        _showDiagnostics = true;
        UpdateOutputPanelView();
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
        if (_currentDocument is not null)
        {
            _currentDocument.OutputText = string.Empty;
            _currentDocument.DiagnosticsText = string.Empty;
            _currentDocument.VariablesText = string.Empty;
            _currentDocument.LastExecutionResult = null;
        }
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

    private async void About_Click(object? sender, RoutedEventArgs e)
    {
        var window = new Window
        {
            Title = "Acerca de Kity Dev",
            Width = 420,
            Height = 360,
            MinWidth = 360,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("PanelBackground"),
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://PseudoCode.App/Assets/iconKityDev.png")))
        };

        var icon = new Image
        {
            Source = new Bitmap(AssetLoader.Open(new Uri("avares://PseudoCode.App/Assets/iconKityDev.png"))),
            Width = 92,
            Height = 92,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var content = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(24),
            Children =
            {
                icon,
                new TextBlock
                {
                    Text = "Kity Dev",
                    Foreground = Brush("TextPrimary"),
                    FontSize = 22,
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = "PseudoCode",
                    Foreground = Brush("TextSecondary"),
                    FontSize = 15,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                BuildAboutLink("YouTube", "@KityDev - https://www.youtube.com/@KityDev", "https://www.youtube.com/@KityDev"),
                BuildAboutLink("GitHub", "https://github.com/KityDeveloper", "https://github.com/KityDeveloper"),
                BuildAboutLink("Web", "kity.dev", "https://kity.dev"),
                new Button
                {
                    Content = "Cerrar",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 12, 0, 0),
                    Classes = { "command" }
                }
            }
        };

        if (content.Children[^1] is Button closeButton)
        {
            closeButton.Click += (_, _) => window.Close();
        }

        window.Content = content;
        await window.ShowDialog(this);
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
        if (_isSwitchingDocument || _currentDocument is null)
        {
            return;
        }

        _currentDocument.Text = EditorTextBox.Text ?? string.Empty;
        _currentDocument.HasUnsavedChanges = true;
        _currentDocument.DiagnosticLines.Clear();
        UpdateDiagnosticUnderlines();
        UpdateLineNumbers();
        UpdateVariablesList();
        RenderOpenDocuments();
        UpdateWindowState("Editando");
    }

    private async Task SaveCurrentFileAsync()
    {
        var document = _currentDocument;
        if (document is null)
        {
            return;
        }

        await SaveDocumentAsync(document);
    }

    private async Task<bool> SaveDocumentAsync(OpenDocument document)
    {
        SaveCurrentDocumentState();

        var file = document.File;
        if (file is null)
        {
            file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Guardar algoritmo",
                SuggestedFileName = document.DisplayName,
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
                return false;
            }

            document.File = file;
            document.DisplayName = file.Name;
            document.Location = file.Path.LocalPath;
        }

        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        await writer.WriteAsync(document.Text);
        document.HasUnsavedChanges = false;
        RenderOpenDocuments();
        UpdateWindowState($"Guardado: {file.Name}");
        return true;
    }

    private async Task CloseDocumentAsync(OpenDocument document)
    {
        if (document == _currentDocument)
        {
            SaveCurrentDocumentState();
        }

        if (document.HasUnsavedChanges)
        {
            var choice = await AskCloseUnsavedDocumentAsync(document);
            if (choice == CloseDocumentChoice.Cancel)
            {
                UpdateWindowState($"Cierre cancelado: {document.DisplayName}");
                return;
            }

            if (choice == CloseDocumentChoice.Save && !await SaveDocumentAsync(document))
            {
                UpdateWindowState($"Cierre cancelado: {document.DisplayName}");
                return;
            }
        }

        var closingIndex = _openDocuments.IndexOf(document);
        var wasCurrent = document == _currentDocument;
        _openDocuments.Remove(document);

        if (_openDocuments.Count == 0)
        {
            _currentDocument = null;
            AddNewDocument();
            UpdateWindowState($"Cerrado: {document.DisplayName}");
            return;
        }

        if (wasCurrent)
        {
            var nextIndex = Math.Clamp(closingIndex, 0, _openDocuments.Count - 1);
            _currentDocument = null;
            SwitchDocument(_openDocuments[nextIndex]);
        }
        else
        {
            RenderOpenDocuments();
        }

        UpdateWindowState($"Cerrado: {document.DisplayName}");
    }

    private async Task<CloseDocumentChoice> AskCloseUnsavedDocumentAsync(OpenDocument document)
    {
        var window = new Window
        {
            Title = "Guardar cambios",
            Width = 420,
            Height = 220,
            MinWidth = 380,
            MinHeight = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("PanelBackground")
        };

        var saveButton = new Button
        {
            Content = "Guardar",
            Classes = { "command" },
            MinWidth = 96
        };
        var discardButton = new Button
        {
            Content = "No guardar",
            Classes = { "command" },
            MinWidth = 96
        };
        var cancelButton = new Button
        {
            Content = "Cancelar",
            Classes = { "command" },
            MinWidth = 96,
            IsCancel = true
        };

        saveButton.Click += (_, _) => window.Close(CloseDocumentChoice.Save);
        discardButton.Click += (_, _) => window.Close(CloseDocumentChoice.Discard);
        cancelButton.Click += (_, _) => window.Close(CloseDocumentChoice.Cancel);

        window.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 18,
            Children =
            {
                new TextBlock
                {
                    Text = $"Quieres guardar los cambios en {document.DisplayName} antes de cerrarlo?",
                    Foreground = Brush("TextPrimary"),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 15
                },
                new TextBlock
                {
                    Text = "Si no guardas, los cambios se perderan.",
                    Foreground = Brush("TextSecondary"),
                    TextWrapping = TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Children =
                    {
                        saveButton,
                        discardButton,
                        cancelButton
                    }
                }
            }
        };

        return await window.ShowDialog<CloseDocumentChoice>(this);
    }

    private async Task LoadFileAsync(IStorageFile file)
    {
        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = await reader.ReadToEndAsync();
        var existingDocument = _openDocuments.FirstOrDefault(document => document.File?.Path == file.Path);
        if (existingDocument is not null)
        {
            existingDocument.Text = text;
            existingDocument.HasUnsavedChanges = false;
            SwitchDocument(existingDocument);
            UpdateWindowState($"Abierto: {file.Name}");
            return;
        }

        var document = new OpenDocument(file.Name, text)
        {
            File = file,
            Location = file.Path.LocalPath
        };

        _openDocuments.Add(document);
        SwitchDocument(document);
        UpdateWindowState($"Abierto: {file.Name}");
    }

    private void SendConsoleInput()
    {
        var document = _currentDocument;
        if (document?.LastExecutionResult?.WaitingForInput != true)
        {
            HideConsoleInput();
            return;
        }

        var input = ConsoleInputTextBox.Text ?? string.Empty;
        ConsoleInputTextBox.Text = string.Empty;
        document.LastExecutionResult = document.Interpreter.Continue(input);
        ShowExecutionResult(document.LastExecutionResult);
    }

    private void ShowExecutionResult(ExecutionResult result)
    {
        var outputText = BuildOutputText(result);
        var diagnosticsText = BuildDiagnosticsText(result);
        var variablesText = BuildVariablesText(result);
        VariablesTextBox.Text = variablesText;

        if (_currentDocument is not null)
        {
            _currentDocument.OutputText = outputText;
            _currentDocument.DiagnosticsText = diagnosticsText;
            _currentDocument.VariablesText = variablesText;
            _currentDocument.LastExecutionResult = result;
            _currentDocument.DiagnosticLines = ExtractDiagnosticLineNumbers(result.Diagnostics).ToHashSet();
        }

        UpdateDiagnosticUnderlines();
        UpdateOutputPanelView();

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
        _diagnosticUnderlineRenderer = new DiagnosticUnderlineRenderer();
        EditorTextBox.TextArea.TextView.BackgroundRenderers.Add(_diagnosticUnderlineRenderer);
        ApplyEditorTheme(DarkTheme, PseudoCodeColorizer.DarkPalette);
        EditorTextBox.TextArea.TextEntered += Editor_TextEntered;
        EditorTextBox.TextArea.TextEntering += Editor_TextEntering;
        EditorTextBox.TextArea.KeyDown += Editor_KeyDown;
        EditorTextBox.PointerWheelChanged += Editor_PointerWheelChanged;
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
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && TryHandleEditorZoomKey(e))
        {
            e.Handled = true;
            return;
        }

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

    private void Editor_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (e.Delta.Y > 0)
        {
            ZoomEditor(1);
        }
        else if (e.Delta.Y < 0)
        {
            ZoomEditor(-1);
        }

        e.Handled = true;
    }

    private bool TryHandleEditorZoomKey(KeyEventArgs e)
    {
        var key = e.Key.ToString();
        var keySymbol = e.KeySymbol ?? string.Empty;

        if (key is "Add" or "OemPlus" or "Plus" || keySymbol is "+" or "=")
        {
            ZoomEditor(1);
            return true;
        }

        if (key is "Subtract" or "OemMinus" or "Minus" || keySymbol is "-" or "_")
        {
            ZoomEditor(-1);
            return true;
        }

        if (key is "D0" or "NumPad0" || keySymbol is "0")
        {
            SetEditorFontSize(DefaultEditorFontSize);
            return true;
        }

        return false;
    }

    private void ZoomEditor(int direction)
    {
        SetEditorFontSize(EditorTextBox.FontSize + direction * EditorZoomStep);
    }

    private void SetEditorFontSize(double fontSize)
    {
        EditorTextBox.FontSize = Math.Clamp(fontSize, MinEditorFontSize, MaxEditorFontSize);
        EditorTextBox.TextArea.TextView.Redraw();
        UpdateWindowState($"Zoom editor: {EditorTextBox.FontSize:0}px");
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
        var document = EditorTextBox.Document;
        var offset = GetSafeCaretOffset(document);
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
        var offset = GetSafeCaretOffset(document);
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
        var document = EditorTextBox.Document;
        var offset = GetSafeCaretOffset(document);

        document.Insert(offset, text);
        EditorTextBox.CaretOffset = Math.Min(offset + text.Length, document.TextLength);
    }

    private int GetSafeCaretOffset(TextDocument document)
    {
        return Math.Clamp(EditorTextBox.CaretOffset, 0, document.TextLength);
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
        var hasUnsavedChanges = _currentDocument?.HasUnsavedChanges == true;
        WindowStateText.Text = hasUnsavedChanges ? $"{message} - sin guardar" : message;
        Title = hasUnsavedChanges ? "PseudoCode *" : "PseudoCode";
    }

    private void AddNewDocument()
    {
        var document = new OpenDocument($"Nuevo {_newAlgorithmNumber++}.psc", SampleProgram);
        _openDocuments.Add(document);
        SwitchDocument(document);
        UpdateWindowState($"Nuevo algoritmo: {document.DisplayName}");
    }

    private void SwitchDocument(OpenDocument document)
    {
        if (_currentDocument == document)
        {
            return;
        }

        SaveCurrentDocumentState();
        _currentDocument = document;
        _isSwitchingDocument = true;
        try
        {
            EditorTextBox.Text = document.Text;
            VariablesTextBox.Text = document.VariablesText;
            if (document.LastExecutionResult?.WaitingForInput == true)
            {
                ShowConsoleInput(document.LastExecutionResult.InputVariable ?? "valor");
            }
            else
            {
                HideConsoleInput();
            }
        }
        finally
        {
            _isSwitchingDocument = false;
        }

        UpdateLineNumbers();
        UpdateVariablesList();
        UpdateDiagnosticUnderlines();
        UpdateOutputPanelView();
        RenderOpenDocuments();
        UpdateWindowState($"Activo: {document.DisplayName}");
        EditorTextBox.Focus();
    }

    private void SaveCurrentDocumentState()
    {
        if (_currentDocument is null)
        {
            return;
        }

        _currentDocument.Text = EditorTextBox.Text ?? string.Empty;
        _currentDocument.VariablesText = VariablesTextBox.Text ?? string.Empty;
    }

    private void UpdateOutputPanelView()
    {
        var document = _currentDocument;
        OutputTextBox.Text = document is null
            ? string.Empty
            : _showDiagnostics
                ? document.DiagnosticsText
                : document.OutputText;

        OutputTabButton.Foreground = _showDiagnostics ? Brush("TextSecondary") : Brush("TextPrimary");
        OutputTabButton.FontWeight = _showDiagnostics ? FontWeight.Normal : FontWeight.SemiBold;
        DiagnosticsTabButton.Foreground = _showDiagnostics ? Brush("TextPrimary") : Brush("TextSecondary");
        DiagnosticsTabButton.FontWeight = _showDiagnostics ? FontWeight.SemiBold : FontWeight.Normal;
    }

    private void UpdateDiagnosticUnderlines()
    {
        if (_diagnosticUnderlineRenderer is null)
        {
            return;
        }

        _diagnosticUnderlineRenderer.SetLines(_currentDocument?.DiagnosticLines ?? []);
        EditorTextBox.TextArea.TextView.Redraw();
    }

    private void RenderOpenDocuments()
    {
        OpenFilesPanel.Children.Clear();
        EditorTabsPanel.Children.Clear();

        foreach (var document in _openDocuments)
        {
            OpenFilesPanel.Children.Add(BuildDocumentButton(document, false));
            EditorTabsPanel.Children.Add(BuildDocumentButton(document, true));
        }
    }

    private Control BuildDocumentButton(OpenDocument document, bool isTab)
    {
        var isSelected = document == _currentDocument;
        var displayName = document.HasUnsavedChanges ? $"{document.DisplayName} *" : document.DisplayName;
        var container = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Background = isSelected ? Brush("AccentBlue") : Brush(isTab ? "TabBackground" : "ButtonBackground"),
            MinWidth = isTab ? 140 : 0,
            HorizontalAlignment = isTab ? HorizontalAlignment.Left : HorizontalAlignment.Stretch
        };

        var openButton = new Button
        {
            Content = displayName,
            Background = Brushes.Transparent,
            Foreground = isSelected ? Brushes.White : Brush("TextPrimary"),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Padding = isTab ? new Thickness(14, 8, 8, 8) : new Thickness(8, 7),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        ToolTip.SetTip(openButton, document.Location);
        openButton.Click += (_, _) => SwitchDocument(document);
        container.Children.Add(openButton);

        var closeButton = new Button
        {
            Content = "x",
            Background = Brushes.Transparent,
            Foreground = isSelected ? Brushes.White : Brush("TextSecondary"),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Padding = isTab ? new Thickness(8, 6) : new Thickness(8, 4),
            MinWidth = 28,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        ToolTip.SetTip(closeButton, $"Cerrar {document.DisplayName}");
        closeButton.Click += async (_, e) =>
        {
            e.Handled = true;
            await CloseDocumentAsync(document);
        };
        Grid.SetColumn(closeButton, 1);
        container.Children.Add(closeButton);

        if (isTab)
        {
            container.Children.Add(new Border
            {
                BorderBrush = isSelected ? Brush("AccentBlue") : Brush("BorderBrushMuted"),
                BorderThickness = new Thickness(0, isSelected ? 2 : 0, 1, 0),
                IsHitTestVisible = false
            });
        }
        else
        {
            container.Children.Add(new Border
            {
                BorderBrush = isSelected ? Brush("AccentBlue") : Brush("BorderBrushMuted"),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false
            });
        }

        return container;
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
        SaveCurrentDocumentState();
        var document = new OpenDocument($"{topic.Title}.psc", topic.Example)
        {
            HasUnsavedChanges = true,
            Location = "Ejemplo de ayuda"
        };

        _openDocuments.Add(document);
        SwitchDocument(document);
        UpdateWindowState($"Ejemplo cargado: {topic.Title}");
    }

    private void ApplyTheme(IReadOnlyDictionary<string, string> colors)
    {
        var themeVariant = _isLightTheme ? ThemeVariant.Light : ThemeVariant.Dark;
        RequestedThemeVariant = themeVariant;
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant = themeVariant;
        }

        foreach (var (key, value) in colors)
        {
            if (Resources[key] is SolidColorBrush brush)
            {
                brush.Color = Color.Parse(value);
            }
        }

        ApplyEditorTheme(colors, _isLightTheme ? PseudoCodeColorizer.LightPalette : PseudoCodeColorizer.DarkPalette);
        RenderOpenDocuments();
        UpdateOutputPanelView();
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

    private Grid BuildAboutLink(string label, string text, string uri)
    {
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 6
        };

        row.Children.Add(new TextBlock
        {
            Text = $"{label}:",
            Foreground = Brush("TextPrimary"),
            VerticalAlignment = VerticalAlignment.Center
        });

        var link = new HyperlinkButton
        {
            Content = text,
            NavigateUri = new Uri(uri),
            Foreground = Brush("AccentBlue"),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(link, 1);
        row.Children.Add(link);

        return row;
    }

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

        return builder.ToString();
    }

    private static string BuildDiagnosticsText(ExecutionResult result)
    {
        if (result.Diagnostics.Count == 0)
        {
            return "Sin diagnosticos.";
        }

        var builder = new StringBuilder();
        foreach (var diagnostic in result.Diagnostics)
        {
            builder.AppendLine(diagnostic);
        }

        return builder.ToString();
    }

    private static IEnumerable<int> ExtractDiagnosticLineNumbers(IEnumerable<string> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            var match = Regex.Match(diagnostic, @"^Linea\s+(\d+):", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var lineNumber))
            {
                yield return lineNumber;
            }
        }
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

    private enum CloseDocumentChoice
    {
        Cancel,
        Save,
        Discard
    }

    private sealed class OpenDocument(string displayName, string text)
    {
        public IStorageFile? File { get; set; }
        public string DisplayName { get; set; } = displayName;
        public string Location { get; set; } = "Sin guardar";
        public string Text { get; set; } = text;
        public string OutputText { get; set; } = string.Empty;
        public string DiagnosticsText { get; set; } = "Sin diagnosticos.";
        public string VariablesText { get; set; } = string.Empty;
        public HashSet<int> DiagnosticLines { get; set; } = [];
        public bool HasUnsavedChanges { get; set; }
        public ExecutionResult? LastExecutionResult { get; set; }
        public PseudoInterpreter Interpreter { get; } = new();
    }

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

internal sealed class DiagnosticUnderlineRenderer : IBackgroundRenderer
{
    private readonly Pen _pen = new(Brushes.Red, 1.5);
    private HashSet<int> _lineNumbers = [];

    public KnownLayer Layer => KnownLayer.Text;

    public void SetLines(IEnumerable<int> lineNumbers)
    {
        _lineNumbers = lineNumbers.Where(lineNumber => lineNumber > 0).ToHashSet();
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_lineNumbers.Count == 0 || textView.Document is null)
        {
            return;
        }

        textView.EnsureVisualLines();
        foreach (var lineNumber in _lineNumbers)
        {
            if (lineNumber > textView.Document.LineCount)
            {
                continue;
            }

            var line = textView.Document.GetLineByNumber(lineNumber);
            var length = Math.Max(1, line.Length);
            var segment = new TextSegment
            {
                StartOffset = line.Offset,
                Length = length
            };

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment, false))
            {
                var y = Math.Max(rect.Top, rect.Bottom - 2);
                drawingContext.DrawLine(_pen, new Point(rect.Left, y), new Point(rect.Right, y));
            }
        }
    }
}

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
