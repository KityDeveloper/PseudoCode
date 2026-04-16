using System.Data;
using System.Globalization;
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
    private const double DefaultInterfaceScale = 1;
    private const double MinInterfaceScale = 0.8;
    private const double MaxInterfaceScale = 1.4;
    private const double InterfaceScaleStep = 0.1;

    private readonly List<OpenDocument> _openDocuments = [];
    private OpenDocument? _currentDocument;
    private double _interfaceScale = DefaultInterfaceScale;
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
        BuildEditorTools();
        BuildHelpTopics();
        DragDrop.SetAllowDrop(this, true);
        DragDrop.SetAllowDrop(EditorTextBox, true);
        AddHandler(DragDrop.DragOverEvent, Editor_DragOver);
        AddHandler(DragDrop.DropEvent, Editor_Drop);
        UpdateLineNumbers();
        UpdateEmptyWorkspaceState();
        UpdateWindowState("Listo para escribir pseudocodigo");
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
        if (UpdateLiveSyntaxDiagnostics(document))
        {
            _showDiagnostics = true;
            UpdateDiagnosticUnderlines();
            UpdateOutputPanelView();
            UpdateWindowState("Corrige los errores de sintaxis");
            return;
        }

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

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (e.Key == Key.N)
        {
            AddNewDocument();
            e.Handled = true;
            return;
        }

        if (IsIncreaseKey(e))
        {
            ZoomInterface(1);
            e.Handled = true;
            return;
        }

        if (IsDecreaseKey(e))
        {
            ZoomInterface(-1);
            e.Handled = true;
            return;
        }

        if (IsResetKey(e))
        {
            SetInterfaceScale(DefaultInterfaceScale);
            e.Handled = true;
        }
    }

    private void EditorTabs_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var horizontalDelta = e.Delta.X != 0 ? e.Delta.X : e.Delta.Y;
        if (horizontalDelta == 0)
        {
            return;
        }

        var nextOffset = Math.Clamp(
            EditorTabsScrollViewer.Offset.X - horizontalDelta * 48,
            0,
            EditorTabsScrollViewer.ScrollBarMaximum.X);
        EditorTabsScrollViewer.Offset = new Vector(nextOffset, EditorTabsScrollViewer.Offset.Y);
        e.Handled = true;
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
            MinWidth = 420,
            MinHeight = 360,
            MaxWidth = 420,
            MaxHeight = 360,
            CanResize = false,
            CanMinimize = false,
            CanMaximize = false,
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
        UpdateLiveSyntaxDiagnostics(_currentDocument);
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
            ClearEditorWorkspace();
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

    private void ClearEditorWorkspace()
    {
        _isSwitchingDocument = true;
        try
        {
            EditorTextBox.Text = string.Empty;
            OutputTextBox.Text = string.Empty;
            VariablesTextBox.Text = string.Empty;
            HideConsoleInput();
        }
        finally
        {
            _isSwitchingDocument = false;
        }

        RenderOpenDocuments();
        UpdateDiagnosticUnderlines();
        UpdateOutputPanelView();
        UpdateLineNumbers();
        UpdateEmptyWorkspaceState();
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

    private void ZoomInterface(int direction)
    {
        SetInterfaceScale(_interfaceScale + direction * InterfaceScaleStep);
    }

    private void SetInterfaceScale(double scale)
    {
        _interfaceScale = Math.Clamp(scale, MinInterfaceScale, MaxInterfaceScale);
        AppScaleHost.LayoutTransform = new ScaleTransform(_interfaceScale, _interfaceScale);
        UpdateWindowState($"Zoom interfaz: {_interfaceScale:P0}");
    }

    private static bool IsIncreaseKey(KeyEventArgs e)
    {
        var key = e.Key.ToString();
        var keySymbol = e.KeySymbol ?? string.Empty;
        return key is "Add" or "OemPlus" or "Plus" || keySymbol is "+" or "=";
    }

    private static bool IsDecreaseKey(KeyEventArgs e)
    {
        var key = e.Key.ToString();
        var keySymbol = e.KeySymbol ?? string.Empty;
        return key is "Subtract" or "OemMinus" or "Minus" || keySymbol is "-" or "_";
    }

    private static bool IsResetKey(KeyEventArgs e)
    {
        var key = e.Key.ToString();
        var keySymbol = e.KeySymbol ?? string.Empty;
        return key is "D0" or "NumPad0" || keySymbol is "0";
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
        UpdateEmptyWorkspaceState();
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

    private bool UpdateLiveSyntaxDiagnostics(OpenDocument document)
    {
        var diagnostics = PseudoSyntaxValidator.Validate(document.Text);
        document.DiagnosticsText = diagnostics.Count == 0 ? "Sin diagnosticos." : string.Join(Environment.NewLine, diagnostics);
        document.DiagnosticLines = ExtractDiagnosticLineNumbers(diagnostics).ToHashSet();
        return diagnostics.Count > 0;
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

        UpdateEmptyWorkspaceState();
    }

    private void UpdateEmptyWorkspaceState()
    {
        EmptyWorkspacePanel.IsVisible = _openDocuments.Count == 0;
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
        var copyLinkItem = new MenuItem
        {
            Header = "Copiar link"
        };
        copyLinkItem.Click += async (_, _) =>
        {
            if (Clipboard is not null)
            {
                await Clipboard.SetTextAsync(uri);
                UpdateWindowState("Link copiado");
            }
        };
        link.ContextMenu = new ContextMenu
        {
            ItemsSource = new[] { copyLinkItem }
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
        public AdvancedPseudoInterpreter Interpreter { get; } = new();
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
Algoritmo

FinAlgoritmo
""";
}

internal sealed record CommandInfo(string Text, string InsertText, string Description, bool IsTemplate = false);

internal sealed class AdvancedPseudoInterpreter
{
    private static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private readonly Dictionary<string, object?> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _output = [];
    private readonly List<string> _diagnostics = [];
    private readonly List<string> _inputs = [];
    private IReadOnlyList<Node> _program = [];
    private int _inputIndex;
    private string? _waitingInputVariable;

    public ExecutionResult Start(string source)
    {
        _inputs.Clear();
        _program = Parse(source);
        return ExecuteFromStart();
    }

    public ExecutionResult Continue(string input)
    {
        if (_waitingInputVariable is null)
        {
            _diagnostics.Add("Linea 1: no hay ninguna instruccion Leer esperando datos. Causa: la ejecucion no esta pausada. Solucion: ejecuta un algoritmo con Leer antes de enviar datos.");
            return BuildResult();
        }

        _inputs.Add(input);
        return ExecuteFromStart();
    }

    private ExecutionResult ExecuteFromStart()
    {
        _variables.Clear();
        _output.Clear();
        _diagnostics.Clear();
        _waitingInputVariable = null;
        _inputIndex = 0;
        ExecuteBlock(_program);
        return BuildResult();
    }

    private IReadOnlyList<Node> Parse(string source)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n')
            .Select((text, index) => new SourceLine(index + 1, RemoveComment(text).Trim()))
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
            if (terminators.Any(term => text.Equals(term, StringComparison.OrdinalIgnoreCase)) ||
                terminators.Contains("Case") &&
                (IsSwitchCase(text) || text.Equals("De Otro Modo:", StringComparison.OrdinalIgnoreCase) || text.Equals("De Otro Modo", StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }

            nodes.Add(ParseNode(lines, ref index));
        }

        return nodes;
    }

    private Node ParseNode(IReadOnlyList<SourceLine> lines, ref int index)
    {
        var line = lines[index];
        var text = line.Text;

        if (text.Equals("Algoritmo", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Proceso", StringComparison.OrdinalIgnoreCase) ||
            StartsWithAny(text, "Algoritmo ", "Proceso ") ||
            text.Equals("FinAlgoritmo", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("FinProceso", StringComparison.OrdinalIgnoreCase))
        {
            index++;
            return new NoOp(line.Number);
        }

        if (text.StartsWith("Definir ", StringComparison.OrdinalIgnoreCase))
        {
            index++;
            var declaration = text["Definir ".Length..];
            var separator = declaration.IndexOf(" Como ", StringComparison.OrdinalIgnoreCase);
            return new Declare(line.Number, SplitNames(separator >= 0 ? declaration[..separator] : declaration));
        }

        if (text.StartsWith("Escribir ", StringComparison.OrdinalIgnoreCase))
        {
            index++;
            return new Write(line.Number, SplitArguments(text["Escribir ".Length..]));
        }

        if (text.StartsWith("Leer ", StringComparison.OrdinalIgnoreCase))
        {
            index++;
            return new Read(line.Number, SplitNames(text["Leer ".Length..]));
        }

        var ifMatch = Regex.Match(text, @"^Si\s+(.+)\s+Entonces$", RegexOptions.IgnoreCase);
        if (ifMatch.Success)
        {
            index++;
            var thenBody = ParseBlock(lines, ref index, "Sino", "FinSi");
            var elseBody = new List<Node>();
            if (index < lines.Count && lines[index].Text.Equals("Sino", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                elseBody = ParseBlock(lines, ref index, "FinSi");
            }
            Consume(lines, ref index, "FinSi", line.Number, "Si");
            return new If(line.Number, ifMatch.Groups[1].Value.Trim(), thenBody, elseBody);
        }

        var whileMatch = Regex.Match(text, @"^Mientras\s+(.+)\s+Hacer$", RegexOptions.IgnoreCase);
        if (whileMatch.Success)
        {
            index++;
            var body = ParseBlock(lines, ref index, "FinMientras");
            Consume(lines, ref index, "FinMientras", line.Number, "Mientras");
            return new While(line.Number, whileMatch.Groups[1].Value.Trim(), body);
        }

        var forMatch = Regex.Match(text, @"^Para\s+([A-Za-z_][A-Za-z0-9_]*)\s*<-\s*(.+)\s+Hasta\s+(.+)\s+Hacer$", RegexOptions.IgnoreCase);
        if (forMatch.Success)
        {
            index++;
            var body = ParseBlock(lines, ref index, "FinPara");
            Consume(lines, ref index, "FinPara", line.Number, "Para");
            return new For(line.Number, forMatch.Groups[1].Value, forMatch.Groups[2].Value.Trim(), forMatch.Groups[3].Value.Trim(), body);
        }

        var switchMatch = Regex.Match(text, @"^Segun\s+(.+)\s+Hacer$", RegexOptions.IgnoreCase);
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

        _diagnostics.Add($"Linea {line.Number}: instruccion desconocida. Causa: '{text}' no coincide con el pseudocodigo soportado. Solucion: revisa la palabra clave o consulta la ayuda.");
        index++;
        return new NoOp(line.Number);
    }

    private Node ParseSwitch(IReadOnlyList<SourceLine> lines, ref int index, int lineNumber, string expression)
    {
        index++;
        var cases = new List<SwitchCase>();
        List<Node> defaultBody = [];
        while (index < lines.Count && !lines[index].Text.Equals("FinSegun", StringComparison.OrdinalIgnoreCase))
        {
            var line = lines[index];
            if (line.Text.Equals("De Otro Modo:", StringComparison.OrdinalIgnoreCase) || line.Text.Equals("De Otro Modo", StringComparison.OrdinalIgnoreCase))
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
            _diagnostics.Add($"Linea {line.Number}: caso mal formado en Segun. Causa: los casos deben terminar con ':'. Solucion: usa '1:' o 'De Otro Modo:'.");
            index++;
        }
        Consume(lines, ref index, "FinSegun", lineNumber, "Segun");
        return new Switch(lineNumber, expression, cases, defaultBody);
    }

    private void Consume(IReadOnlyList<SourceLine> lines, ref int index, string terminator, int lineNumber, string blockName)
    {
        if (index < lines.Count && lines[index].Text.Equals(terminator, StringComparison.OrdinalIgnoreCase))
        {
            index++;
            return;
        }
        _diagnostics.Add($"Linea {lineNumber}: falta '{terminator}'. Causa: el bloque '{blockName}' quedo abierto. Solucion: agrega '{terminator}'.");
    }

    private void ExecuteBlock(IReadOnlyList<Node> nodes)
    {
        foreach (var node in nodes)
        {
            if (_waitingInputVariable is not null) return;
            Execute(node);
        }
    }

    private void Execute(Node node)
    {
        switch (node)
        {
            case NoOp:
                return;
            case Declare declare:
                foreach (var name in declare.Names)
                {
                    if (Identifier.IsMatch(name)) _variables.TryAdd(name, 0d);
                    else AddRuntime(node.Line, $"'{name}' no es un nombre valido", "usa caracteres no permitidos", "usa letras, numeros y guion bajo");
                }
                return;
            case Assign assign:
                if (!Identifier.IsMatch(assign.Name)) AddRuntime(node.Line, $"'{assign.Name}' no es un destino valido", "el lado izquierdo debe ser variable", "usa 'total <- 10'");
                else _variables[assign.Name] = EvaluateValue(assign.Expression, node.Line);
                return;
            case Write write:
                _output.Add(string.Concat(write.Expressions.Select(expression => FormatValue(EvaluateValue(expression, node.Line)))));
                return;
            case Read read:
                foreach (var name in read.Names)
                {
                    _output.Add($"? {name}:");
                    if (_inputIndex >= _inputs.Count)
                    {
                        _waitingInputVariable = name;
                        return;
                    }
                    var input = _inputs[_inputIndex++];
                    _variables[name] = ParseInput(input);
                    _output.Add($"> {input}");
                }
                return;
            case If conditional:
                ExecuteBlock(ToBoolean(EvaluateCondition(conditional.Condition, node.Line)) ? conditional.ThenBody : conditional.ElseBody);
                return;
            case While loop:
                for (var guard = 0; ToBoolean(EvaluateCondition(loop.Condition, node.Line)); guard++)
                {
                    if (guard > 10000) { AddRuntime(node.Line, "ciclo Mientras detenido", "supero 10000 iteraciones", "revisa que la condicion cambie"); return; }
                    ExecuteBlock(loop.Body);
                    if (_waitingInputVariable is not null) return;
                }
                return;
            case For loop:
                var start = ToNumber(EvaluateValue(loop.Start, node.Line));
                var end = ToNumber(EvaluateValue(loop.End, node.Line));
                for (var value = start; value <= end; value++)
                {
                    _variables[loop.Variable] = value;
                    ExecuteBlock(loop.Body);
                    if (_waitingInputVariable is not null) return;
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

    private object? EvaluateValue(string expression, int line)
    {
        expression = expression.Trim();
        if (expression.Length == 0) return string.Empty;
        if (IsQuoted(expression)) return expression[1..^1];
        if (expression.Equals("Verdadero", StringComparison.OrdinalIgnoreCase)) return true;
        if (expression.Equals("Falso", StringComparison.OrdinalIgnoreCase)) return false;
        if (_variables.TryGetValue(expression, out var variable)) return variable;
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
        if (condition.StartsWith("NO ", StringComparison.OrdinalIgnoreCase)) return !ToBoolean(EvaluateCondition(condition[3..], line));
        var orParts = SplitLogical(condition, "O");
        if (orParts.Count > 1) return orParts.Any(part => ToBoolean(EvaluateCondition(part, line)));
        var andParts = SplitLogical(condition, "Y");
        if (andParts.Count > 1) return andParts.All(part => ToBoolean(EvaluateCondition(part, line)));
        var comparison = FindComparison(condition);
        if (comparison is null) return EvaluateValue(condition, line);
        var left = EvaluateValue(condition[..comparison.Value.Index], line);
        var right = EvaluateValue(condition[(comparison.Value.Index + comparison.Value.Operator.Length)..], line);
        return Compare(left, right, comparison.Value.Operator);
    }

    private string ReplaceVariables(string expression, int line) =>
        Regex.Replace(expression, @"\b[A-Za-z_][A-Za-z0-9_]*\b", match =>
        {
            if (match.Value.Equals("Verdadero", StringComparison.OrdinalIgnoreCase)) return "true";
            if (match.Value.Equals("Falso", StringComparison.OrdinalIgnoreCase)) return "false";
            if (!_variables.TryGetValue(match.Value, out var value))
            {
                AddRuntime(line, $"la variable '{match.Value}' no tiene valor", "no fue definida/asignada antes de usarse", "declara o asigna la variable primero");
                return "0";
            }
            return value is bool boolean ? (boolean ? "true" : "false") : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
        });

    private ExecutionResult BuildResult() => new(
        _diagnostics.Count == 0 && _waitingInputVariable is null,
        _output.ToArray(),
        _diagnostics.ToArray(),
        new Dictionary<string, object?>(_variables),
        _waitingInputVariable is not null,
        _waitingInputVariable);

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
    private static bool StartsWithAny(string text, params string[] prefixes) => prefixes.Any(prefix => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    private static bool IsQuoted(string text) => text.Length >= 2 && text[0] == '"' && text[^1] == '"';
    private static bool IsSwitchCase(string text) => text.EndsWith(':') && !text.Equals("De Otro Modo:", StringComparison.OrdinalIgnoreCase);
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
    private sealed record Write(int Line, IReadOnlyList<string> Expressions) : Node(Line);
    private sealed record Read(int Line, IReadOnlyList<string> Names) : Node(Line);
    private sealed record If(int Line, string Condition, IReadOnlyList<Node> ThenBody, IReadOnlyList<Node> ElseBody) : Node(Line);
    private sealed record While(int Line, string Condition, IReadOnlyList<Node> Body) : Node(Line);
    private sealed record For(int Line, string Variable, string Start, string End, IReadOnlyList<Node> Body) : Node(Line);
    private sealed record Switch(int Line, string Expression, IReadOnlyList<SwitchCase> Cases, IReadOnlyList<Node> DefaultBody) : Node(Line);
    private sealed record SwitchCase(int Line, IReadOnlyList<string> Values, IReadOnlyList<Node> Body);
}

internal static class PseudoSyntaxValidator
{
    private static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly HashSet<string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        "Entero", "Real", "Cadena", "Caracter", "Logico", "Booleano"
    };

    public static IReadOnlyList<string> Validate(string source)
    {
        var diagnostics = new List<string>();
        var blocks = new Stack<(string Name, int Line)>();
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
            diagnostics.Add($"Linea {block.Line}: falta cerrar '{block.Name}'. Causa: el bloque quedo abierto. Solucion: agrega su cierre correspondiente.");
        }

        return diagnostics;
    }

    private static void ValidateLine(string line, int lineNumber, List<string> diagnostics, Stack<(string Name, int Line)> blocks)
    {
        if (line.Count(character => character == '"') % 2 != 0)
        {
            diagnostics.Add($"Linea {lineNumber}: comillas sin cerrar. Causa: falta una comilla doble. Solucion: cierra el texto con \".");
            return;
        }

        if (line.Equals("Algoritmo", StringComparison.OrdinalIgnoreCase) ||
            line.Equals("Proceso", StringComparison.OrdinalIgnoreCase) ||
            StartsWithAny(line, "Algoritmo ", "Proceso "))
        {
            blocks.Push((line.StartsWith("Proceso ", StringComparison.OrdinalIgnoreCase) ? "Proceso" : "Algoritmo", lineNumber));
            return;
        }

        if (line.Equals("FinAlgoritmo", StringComparison.OrdinalIgnoreCase) || line.Equals("FinProceso", StringComparison.OrdinalIgnoreCase))
        {
            CloseBlock(lineNumber, line.StartsWith("FinProceso", StringComparison.OrdinalIgnoreCase) ? "Proceso" : "Algoritmo", diagnostics, blocks);
            return;
        }

        if (line.StartsWith("Definir ", StringComparison.OrdinalIgnoreCase))
        {
            ValidateDeclaration(line["Definir ".Length..], lineNumber, diagnostics);
            return;
        }

        if (line.StartsWith("Escribir ", StringComparison.OrdinalIgnoreCase))
        {
            ValidateExpression(line["Escribir ".Length..], lineNumber, "Escribir", diagnostics);
            return;
        }

        if (line.StartsWith("Leer ", StringComparison.OrdinalIgnoreCase))
        {
            ValidateIdentifierList(line["Leer ".Length..], lineNumber, "Leer", diagnostics);
            return;
        }

        if (Regex.IsMatch(line, @"^Si\s+.+\s+Entonces$", RegexOptions.IgnoreCase))
        {
            blocks.Push(("Si", lineNumber));
            return;
        }

        if (line.Equals("Sino", StringComparison.OrdinalIgnoreCase))
        {
            if (!blocks.Any(block => block.Name.Equals("Si", StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add($"Linea {lineNumber}: 'Sino' no corresponde a ningun 'Si'. Causa: falta abrir un bloque Si. Solucion: usa 'Si condicion Entonces' antes de 'Sino'.");
            }
            return;
        }

        if (line.Equals("FinSi", StringComparison.OrdinalIgnoreCase))
        {
            CloseBlock(lineNumber, "Si", diagnostics, blocks);
            return;
        }

        if (Regex.IsMatch(line, @"^Mientras\s+.+\s+Hacer$", RegexOptions.IgnoreCase))
        {
            blocks.Push(("Mientras", lineNumber));
            return;
        }

        if (line.Equals("FinMientras", StringComparison.OrdinalIgnoreCase))
        {
            CloseBlock(lineNumber, "Mientras", diagnostics, blocks);
            return;
        }

        if (Regex.IsMatch(line, @"^Para\s+[A-Za-z_][A-Za-z0-9_]*\s*<-\s*.+\s+Hasta\s+.+\s+Hacer$", RegexOptions.IgnoreCase))
        {
            blocks.Push(("Para", lineNumber));
            return;
        }

        if (line.Equals("FinPara", StringComparison.OrdinalIgnoreCase))
        {
            CloseBlock(lineNumber, "Para", diagnostics, blocks);
            return;
        }

        if (Regex.IsMatch(line, @"^Segun\s+.+\s+Hacer$", RegexOptions.IgnoreCase))
        {
            blocks.Push(("Segun", lineNumber));
            return;
        }

        if (line.Equals("FinSegun", StringComparison.OrdinalIgnoreCase))
        {
            CloseBlock(lineNumber, "Segun", diagnostics, blocks);
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

        diagnostics.Add($"Linea {lineNumber}: instruccion desconocida. Causa: '{line}' no coincide con el pseudocodigo soportado. Solucion: revisa la palabra clave o consulta la ayuda.");
    }

    private static void ValidateDeclaration(string declaration, int lineNumber, List<string> diagnostics)
    {
        var separator = declaration.IndexOf(" Como ", StringComparison.OrdinalIgnoreCase);
        var names = separator >= 0 ? declaration[..separator] : declaration;
        ValidateIdentifierList(names, lineNumber, "Definir", diagnostics);

        if (separator >= 0)
        {
            var typeName = declaration[(separator + " Como ".Length)..].Trim();
            if (typeName.Length == 0 || !Types.Contains(typeName))
            {
                diagnostics.Add($"Linea {lineNumber}: tipo '{typeName}' no reconocido. Causa: el tipo esta vacio o no existe. Solucion: usa Entero, Real, Cadena o Logico.");
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

    private static void CloseBlock(int lineNumber, string expected, List<string> diagnostics, Stack<(string Name, int Line)> blocks)
    {
        if (!blocks.TryPop(out var opened))
        {
            diagnostics.Add($"Linea {lineNumber}: cierre '{expected}' sin bloque abierto. Causa: sobra un cierre. Solucion: elimina este cierre o agrega el bloque inicial.");
            return;
        }

        if (!opened.Name.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add($"Linea {lineNumber}: cierre incorrecto. Causa: se esperaba cerrar '{opened.Name}', pero aparece '{expected}'. Solucion: cambia el cierre o revisa el orden de los bloques.");
        }
    }

    private static bool StartsWithAny(string text, params string[] prefixes) =>
        prefixes.Any(prefix => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

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
