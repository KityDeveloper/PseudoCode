using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
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
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using PseudoCode.App.Services;

namespace PseudoCode.App;

public partial class MainWindow : Window
{
    private sealed record AboutAnimation(string Name, string[] Frames);

    private const double DefaultEditorFontSize = 15;
    private const double MinEditorFontSize = 10;
    private const double MaxEditorFontSize = 30;
    private const double EditorZoomStep = 1;
    private const double DefaultInterfaceScale = 1;
    private const double MinInterfaceScale = 0.8;
    private const double MaxInterfaceScale = 1.4;
    private const double InterfaceScaleStep = 0.1;
    private const int MouseClickDiagnosticsLimit = 18;
    private const int ContextMenuDiagnosticsLimit = 80;
    private static readonly string[] ThemeColorKeys =
    [
        "keyword",
        "type",
        "string",
        "number",
        "operator",
        "comment",
        "blockBackground",
        "diagnosticUnderline",
        "editorBackground"
    ];

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true
    };
    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private readonly List<OpenDocument> _openDocuments = [];
    private readonly Dictionary<string, Window> _singleInstanceWindows = new(StringComparer.OrdinalIgnoreCase);
    private RuntimeSettings _runtimeSettings;
    private PseudoLanguageDefinition _language;
    private PseudoSyntaxValidator _syntaxValidator;
    private CommandInfo[] _completionItems;
    private CommandInfo[] _quickTemplates;
    private HelpTopic[] _helpTopics;
    private List<RecentDocumentInfo> _recentDocuments;
    private OpenDocument? _currentDocument;
    private bool _formatChordArmed;
    private double _interfaceScale = DefaultInterfaceScale;
    private int _newAlgorithmNumber = 1;
    private bool _isSwitchingDocument;
    private bool _isPromptingWindowClose;
    private bool _allowWindowClose;
    private bool _showDiagnostics;
    private bool _isLightTheme;
    private bool _isHelpVisible = true;
    private bool _isRunningCode;
    private bool _isExecutionPaused;
    private CancellationTokenSource? _executionCancellation;
    private readonly ManualResetEventSlim _executionPauseGate = new(true);
    private int _executionRunId;
    private CompletionWindow? _completionWindow;
    private PseudoCodeColorizer? _colorizer;
    private DiagnosticUnderlineRenderer? _diagnosticUnderlineRenderer;
    private DebugLineRenderer? _debugLineRenderer;
    private readonly Queue<string> _mouseClickDiagnosticsLines = new();
    private readonly Queue<string> _contextMenuDiagnosticsLines = new();
    private bool _copiedWholeLine;
    private Point? _lastContextMenuOpenPoint;

    public MainWindow()
    {
        _runtimeSettings = AppSettingsService.Load();
        _language = _runtimeSettings.Language;
        _syntaxValidator = new PseudoSyntaxValidator(_language);
        _completionItems = _language.Snippets.ToArray();
        _quickTemplates = _language.BuildQuickTemplates().ToArray();
        _helpTopics = BuildDefaultHelpTopics(_language);
        _recentDocuments = RecentDocumentsService.Load(_runtimeSettings.UserSettingsPath);
        InitializeComponent();
        SetInterfaceScale(_runtimeSettings.InterfaceScale, persist: false, updateStatus: false);
        ConfigureEditor();
        BuildEditorTools();
        BuildHelpTopics();
        RenderRecentDocumentsMenu();
        Closing += MainWindow_Closing;
        DragDrop.SetAllowDrop(this, true);
        DragDrop.SetAllowDrop(EditorTextBox, true);
        AddHandler(DragDrop.DragOverEvent, Editor_DragOver);
        AddHandler(DragDrop.DropEvent, Editor_Drop);
        AddHandler(InputElement.PointerPressedEvent, MainWindow_PointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(InputElement.PointerReleasedEvent, MainWindow_PointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(InputElement.PointerMovedEvent, MainWindow_PointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(InputElement.KeyDownEvent, MainWindow_DiagnosticKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        ConfigureMouseClickDiagnostics();
        UpdateLineNumbers();
        UpdateEmptyWorkspaceState();
        UpdateExecutionControls();
        UpdateWindowState(_runtimeSettings.Diagnostics.Count == 0
            ? $"Listo - dialecto {_language.DisplayName}"
            : $"Listo con avisos de settings - {_language.DisplayName}");
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

    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowWindowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_isPromptingWindowClose)
        {
            return;
        }

        _isPromptingWindowClose = true;
        try
        {
            if (await ConfirmCloseWindowAsync())
            {
                _allowWindowClose = true;
                Close();
            }
        }
        finally
        {
            _isPromptingWindowClose = false;
        }
    }

    private void NewFile_Click(object? sender, RoutedEventArgs e)
    {
        AddNewDocument();
    }

    private async void Run_Click(object? sender, RoutedEventArgs e)
    {
        var document = _currentDocument;
        if (document is null || _isRunningCode)
        {
            return;
        }

        StopDebug(document);
        HideConsoleInput();
        document.Text = EditorTextBox.Text ?? string.Empty;
        if (UpdateLiveSyntaxDiagnostics(document))
        {
            ClearExecutionViewForValidationFailure(document);
            _showDiagnostics = true;
            UpdateDiagnosticUnderlines();
            UpdateOutputPanelView();
            UpdateWindowState("Corrige los errores de sintaxis");
            return;
        }

        _isRunningCode = true;
        _isExecutionPaused = false;
        _executionPauseGate.Set();
        _executionCancellation?.Dispose();
        _executionCancellation = new CancellationTokenSource();
        ClearExecutionViewForRun(document);
        UpdateExecutionControls();
        UpdateWindowState("Ejecutando...");
        var runId = ++_executionRunId;
        try
        {
            var source = document.Text;
            var cancellation = _executionCancellation;
            var result = await Task.Run(() => document.Interpreter.Start(
                source,
                cancellation.Token,
                _executionPauseGate,
                progress => Dispatcher.UIThread.Post(() => ApplyLiveExecutionResult(document, progress, runId), DispatcherPriority.Background)));
            document.LastExecutionResult = result;

            if (_currentDocument == document)
            {
                ShowExecutionResult(result);
                if (result.Diagnostics.Count > 0)
                {
                    _showDiagnostics = true;
                    UpdateOutputPanelView();
                    UpdateWindowState("Ejecucion detenida con diagnosticos");
                }
            }
        }
        finally
        {
            _executionRunId++;
            _isRunningCode = false;
            _isExecutionPaused = false;
            _executionPauseGate.Set();
            _executionCancellation?.Dispose();
            _executionCancellation = null;
            UpdateExecutionControls();
        }
    }

    private void ApplyLiveExecutionResult(OpenDocument document, ExecutionResult result, int runId)
    {
        if (!_isRunningCode || runId != _executionRunId || _currentDocument != document)
        {
            return;
        }

        var outputText = BuildOutputText(result);
        var diagnosticsText = BuildDiagnosticsText(result);
        var variablesText = BuildVariablesText(result);
        document.OutputText = outputText;
        document.DiagnosticsText = diagnosticsText;
        document.Diagnostics = ParseDiagnostics(result.Diagnostics).ToList();
        document.VariablesText = variablesText;
        document.LastExecutionResult = result;
        document.DiagnosticLines = document.Diagnostics.Select(diagnostic => diagnostic.Line).ToHashSet();
        VariablesTextBox.Text = variablesText;

        if (document.Diagnostics.Count > 0)
        {
            _showDiagnostics = true;
            UpdateDiagnosticUnderlines();
        }

        if (result.WaitingForInput)
        {
            ShowConsoleInput(result.InputVariable ?? "valor");
        }

        UpdateOutputPanelView();
    }

    private void ClearExecutionViewForRun(OpenDocument document)
    {
        document.LastExecutionResult = null;
        document.OutputText = "Ejecutando...";
        document.DiagnosticsText = "Sin diagnosticos.";
        document.Diagnostics.Clear();
        document.DiagnosticLines.Clear();
        document.VariablesText = string.Empty;
        VariablesTextBox.Text = string.Empty;
        _showDiagnostics = false;
        HighlightDebugLine(null);
        UpdateDiagnosticUnderlines();
        UpdateOutputPanelView();
    }

    private void ClearExecutionViewForValidationFailure(OpenDocument document)
    {
        document.LastExecutionResult = null;
        document.OutputText = string.Empty;
        document.VariablesText = string.Empty;
        VariablesTextBox.Text = string.Empty;
        HideConsoleInput();
        HighlightDebugLine(null);
    }

    private void PauseExecution_Click(object? sender, RoutedEventArgs e)
    {
        if (!_isRunningCode)
        {
            return;
        }

        _isExecutionPaused = !_isExecutionPaused;
        if (_isExecutionPaused)
        {
            _executionPauseGate.Reset();
            UpdateWindowState("Ejecucion pausada");
        }
        else
        {
            _executionPauseGate.Set();
            UpdateWindowState("Ejecutando...");
        }

        UpdateExecutionControls();
    }

    private void StopExecution_Click(object? sender, RoutedEventArgs e)
    {
        if (_isRunningCode)
        {
            _executionPauseGate.Set();
            _executionCancellation?.Cancel();
            _isExecutionPaused = false;
            UpdateExecutionControls();
            UpdateWindowState("Deteniendo ejecucion...");
            return;
        }

        if (_currentDocument?.IsDebugging == true)
        {
            StopDebug(_currentDocument);
            UpdateExecutionControls();
            UpdateWindowState("Depuracion detenida");
            return;
        }

        if (_currentDocument is null)
        {
            return;
        }
    }

    private void StartDebug_Click(object? sender, RoutedEventArgs e)
    {
        if (_isRunningCode)
        {
            UpdateWindowState("Deten la ejecucion antes de depurar");
            return;
        }

        StartDebugSession();
    }

    private void StepDebug_Click(object? sender, RoutedEventArgs e)
    {
        if (_isRunningCode)
        {
            UpdateWindowState("Deten la ejecucion antes de avanzar paso a paso");
            return;
        }

        StepDebugSession();
    }

    private void StopDebug_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentDocument is not null)
        {
            StopDebug(_currentDocument);
            UpdateWindowState("Depuracion detenida");
        }
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

    private async void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F10)
        {
            StepDebugSession();
            e.Handled = true;
            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (IsSaveShortcut(e))
        {
            e.Handled = true;
            await SaveCurrentFileAsync();
            return;
        }

        if (e.Key == Key.N)
        {
            AddNewDocument();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.L)
        {
            e.Handled = true;
            await ShowGoToLineDialogAsync();
            return;
        }

        if (TryHandleFormatShortcut(e))
        {
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

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        CloseCustomEditorContextMenuAfterAction();
        await CopySelectionAsync();
    }

    private async void Cut_Click(object? sender, RoutedEventArgs e)
    {
        CloseCustomEditorContextMenuAfterAction();
        await CutSelectionAsync();
    }

    private async void Paste_Click(object? sender, RoutedEventArgs e)
    {
        CloseCustomEditorContextMenuAfterAction();
        await PasteClipboardAsync();
    }

    private async void CopyDiagnostics_Click(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is null)
        {
            return;
        }

        var text = BuildInputDiagnosticsReport();
        if (text.Length == 0)
        {
            return;
        }

        await Clipboard.SetTextAsync(text);
        UpdateWindowState("Reporte de diagnostico copiado");
    }

    private void ClearDiagnostics_Click(object? sender, RoutedEventArgs e)
    {
        _mouseClickDiagnosticsLines.Clear();
        _contextMenuDiagnosticsLines.Clear();
        MouseClickDiagnosticsTextBox.Text = BuildDiagnosticsPanelPlaceholder();
        UpdateWindowState("Diagnostico limpiado");
    }

    private void DuplicateLine_Click(object? sender, RoutedEventArgs e)
    {
        CloseCustomEditorContextMenuAfterAction();
        DuplicateCurrentLine();
    }

    private void ToggleComment_Click(object? sender, RoutedEventArgs e)
    {
        CloseCustomEditorContextMenuAfterAction();
        ToggleLineComments();
    }

    private void FormatDocument_Click(object? sender, RoutedEventArgs e)
    {
        CloseCustomEditorContextMenuAfterAction();
        FormatCurrentDocument();
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
            _currentDocument.Diagnostics.Clear();
            _currentDocument.DiagnosticLines.Clear();
            _currentDocument.VariablesText = string.Empty;
            _currentDocument.LastExecutionResult = null;
        }
        UpdateDiagnosticUnderlines();
        UpdateOutputPanelView();
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
        LoadHelpExample(_helpTopics[0]);
    }

    private async void Documentation_Click(object? sender, RoutedEventArgs e)
    {
        await ShowDocumentationAsync(DocumentationService.Help);
    }

    private async void JsonConfiguration_Click(object? sender, RoutedEventArgs e)
    {
        await ShowDocumentationAsync(DocumentationService.JsonConfig);
    }

    private async void PseudoLanguageDocumentation_Click(object? sender, RoutedEventArgs e)
    {
        await ShowDocumentationAsync(DocumentationService.PseudoLanguage);
    }

    private async void SettingsConfiguration_Click(object? sender, RoutedEventArgs e)
    {
        await ShowSettingsConfigurationAsync();
    }

    private async void SettingsJson_Click(object? sender, RoutedEventArgs e)
    {
        await ShowJsonSettingsEditorAsync("Settings JSON", target => target.RelativePath.Equals("default-settings.json", StringComparison.OrdinalIgnoreCase));
    }

    private async void SourceThemes_Click(object? sender, RoutedEventArgs e)
    {
        await ShowJsonSettingsEditorAsync("Temas para el codigo fuente", target => target.RelativePath.StartsWith("syntax-themes/", StringComparison.OrdinalIgnoreCase));
    }

    private async void SyntaxConfiguration_Click(object? sender, RoutedEventArgs e)
    {
        await ShowJsonSettingsEditorAsync("Configurar sintaxis", target => target.RelativePath.StartsWith("dialects/", StringComparison.OrdinalIgnoreCase));
    }

    private async void ReleaseNotes_Click(object? sender, RoutedEventArgs e)
    {
        await ShowDocumentationAsync(DocumentationService.ReleaseNotes);
    }

    private Task ShowDocumentationAsync(DocumentationPage page)
    {
        var key = $"documentation:{page.Id}";
        if (ActivateSingleInstanceWindow(key))
        {
            return Task.CompletedTask;
        }

        var window = DocumentationService.BuildWindow(page, _isLightTheme ? LightTheme : DarkTheme);
        ShowSingleInstanceWindow(key, window);
        return Task.CompletedTask;
    }

    private Task ShowJsonSettingsEditorAsync(string title, Func<JsonConfigTarget, bool> filter)
    {
        var key = $"json-editor:{title}";
        if (ActivateSingleInstanceWindow(key))
        {
            return Task.CompletedTask;
        }

        var targets = AppSettingsService.GetConfigTargets(_runtimeSettings).Where(filter).ToArray();
        if (targets.Length == 0)
        {
            UpdateWindowState("No hay archivos JSON configurables para esta seccion");
            return Task.CompletedTask;
        }

        var selectedTarget = targets[0];
        var status = new TextBlock
        {
            Text = "Edita el JSON y guarda. Los cambios se aplican al momento.",
            Foreground = Brush("TextSecondary"),
            TextWrapping = TextWrapping.Wrap
        };

        var fileLabel = new TextBlock
        {
            Text = selectedTarget.RelativePath,
            Foreground = Brush("TextPrimary"),
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };

        var editor = new TextEditor
        {
            Text = AppSettingsService.ReadOrTemplate(selectedTarget),
            ShowLineNumbers = true,
            FontFamily = new FontFamily("Cascadia Code,Consolas,monospace"),
            FontSize = 13,
            Background = Brush("InsetBackground"),
            Foreground = Brush("TextPrimary"),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        editor.Options.ConvertTabsToSpaces = true;
        editor.Options.IndentationSize = 2;
        editor.TextArea.TextView.LineTransformers.Add(new JsonSyntaxColorizer(_isLightTheme));

        var codePreview = new TextEditor
        {
            Text = BuildPreviewCode(_language),
            IsReadOnly = true,
            ShowLineNumbers = true,
            FontFamily = new FontFamily("Cascadia Code,Consolas,monospace"),
            FontSize = 13,
            Background = Brush("InsetBackground"),
            Foreground = Brush("TextPrimary"),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        var codePreviewColorizer = new PseudoCodeColorizer(_language, _runtimeSettings.DarkSyntaxTheme.ToPalette());
        var codePreviewDiagnosticRenderer = new DiagnosticUnderlineRenderer(_runtimeSettings.DarkSyntaxTheme.ToPalette().DiagnosticUnderlineBrush);
        codePreviewDiagnosticRenderer.SetLines([4]);
        codePreview.TextArea.TextView.LineTransformers.Add(codePreviewColorizer);
        codePreview.TextArea.TextView.BackgroundRenderers.Add(codePreviewDiagnosticRenderer);

        var codePreviewTitle = new TextBlock
        {
            Text = "Vista previa del codigo",
            Foreground = Brush("TextPrimary"),
            FontWeight = FontWeight.SemiBold
        };

        var codePreviewPanel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            IsVisible = IsPreviewTarget(selectedTarget),
            Children =
            {
                new Border
                {
                    Background = Brush("PanelBackground"),
                    BorderBrush = Brush("BorderBrushMuted"),
                    BorderThickness = new Thickness(0, 1, 0, 1),
                    Padding = new Thickness(10, 6),
                    Child = codePreviewTitle
                },
                WithGridRow(codePreview, 1)
            }
        };
        var codePreviewSplitter = new GridSplitter
        {
            Height = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brush("BorderBrushMuted"),
            ShowsPreview = true,
            IsVisible = IsPreviewTarget(selectedTarget)
        };
        var editorPanelRows = new RowDefinitions(IsPreviewTarget(selectedTarget) ? "*,6,210" : "*,0,0");

        var isSyncingVisualEditor = false;
        var selectedColorKey = "keyword";
        var colorRows = new Dictionary<string, (TextBox TextBox, Border Preview)>(StringComparer.OrdinalIgnoreCase);
        var colorPicker = new ColorView
        {
            Color = Color.Parse("#5EA1FF"),
            IsAlphaEnabled = false,
            IsAlphaVisible = false,
            IsColorModelVisible = false,
            IsColorComponentsVisible = false,
            IsAccentColorsVisible = true,
            IsColorPreviewVisible = true,
            Height = 320
        };

        var themeRowsPanel = new StackPanel
        {
            Spacing = 10
        };
        var themeToolsPanel = new StackPanel
        {
            Spacing = 12
        };
        var visualThemeEditor = new Grid
        {
            Margin = new Thickness(12),
            IsVisible = IsThemeTarget(selectedTarget),
            ColumnDefinitions = new ColumnDefinitions("*,280"),
            ColumnSpacing = 18,
            Children =
            {
                themeRowsPanel,
                WithGridColumn(new Border
                {
                    BorderBrush = Brush("BorderBrushMuted"),
                    BorderThickness = new Thickness(1, 0, 0, 0),
                    Padding = new Thickness(16, 0, 0, 0),
                    Child = themeToolsPanel
                }, 1)
            }
        };
        var visualTextEditor = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(16, 14),
            IsVisible = !IsThemeTarget(selectedTarget)
        };
        var visualEditorHost = new Grid
        {
            Children =
            {
                visualThemeEditor,
                visualTextEditor
            }
        };

        foreach (var colorKey in ThemeColorKeys)
        {
            var label = new TextBlock
            {
                Text = ThemeColorDisplayName(colorKey),
                Foreground = Brush("TextPrimary"),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap
            };
            ToolTip.SetTip(label, colorKey);
            var input = new TextBox
            {
                Width = 118,
                Text = "#000000",
                FontFamily = new FontFamily("Cascadia Code,Consolas,monospace"),
                FontSize = 12,
                Background = Brush("InsetBackground"),
                Foreground = Brush("TextPrimary"),
                BorderBrush = Brush("BorderBrushMuted")
            };
            input.PointerPressed += (_, _) => input.Focus(NavigationMethod.Pointer);
            var preview = new Border
            {
                Width = 28,
                Height = 24,
                CornerRadius = new CornerRadius(4),
                BorderBrush = Brush("BorderBrushMuted"),
                BorderThickness = new Thickness(1),
                Background = Brushes.Transparent
            };
            colorRows[colorKey] = (input, preview);
            input.GotFocus += (_, _) => SelectThemeColor(colorKey);
            preview.PointerPressed += (_, args) =>
            {
                SelectThemeColor(colorKey);
                args.Handled = true;
            };
            input.LostFocus += (_, _) =>
            {
                if (isSyncingVisualEditor)
                {
                    return;
                }

                ApplyThemeColorFromTextBox(colorKey);
            };
            input.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter)
                {
                    ApplyThemeColorFromTextBox(colorKey);
                    args.Handled = true;
                }
            };

            var colorRow = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("160,118,34"),
                ColumnSpacing = 10,
                Children =
                {
                    label,
                    WithGridColumn(input, 1),
                    WithGridColumn(preview, 2)
                }
            };
            colorRow.PointerPressed += (_, _) => SelectThemeColor(colorKey);
            themeRowsPanel.Children.Add(colorRow);
        }

        themeToolsPanel.Children.Add(new TextBlock
        {
            Text = "Selector de color",
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("TextPrimary"),
            Margin = new Thickness(0, 0, 0, 0)
        });
        themeToolsPanel.Children.Add(new TextBlock
        {
            Text = "Escribe #RRGGBB en el campo o elige un color aqui.",
            Foreground = Brush("TextSecondary"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        });
        themeToolsPanel.Children.Add(BuildQuickColorPalette(color => SetThemeColor(selectedColorKey, color), compact: true));
        themeToolsPanel.Children.Add(new TextBlock
        {
            Text = "Color libre",
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("TextPrimary")
        });
        themeToolsPanel.Children.Add(colorPicker);

        colorPicker.ColorChanged += (_, args) =>
        {
            if (isSyncingVisualEditor)
            {
                return;
            }

            SetThemeColor(selectedColorKey, ToHex(args.NewColor));
        };

        editor.TextChanged += (_, _) =>
        {
            if (!isSyncingVisualEditor)
            {
                RefreshVisualThemeEditor();
                RebuildTextSettingsEditor();
                UpdateCodePreview();
            }
        };

        void SelectTarget(JsonConfigTarget target)
        {
            selectedTarget = target;
            fileLabel.Text = target.RelativePath;
            editor.Text = AppSettingsService.ReadOrTemplate(target);
            visualThemeEditor.IsVisible = IsThemeTarget(target);
            visualTextEditor.IsVisible = !IsThemeTarget(target);
            SetCodePreviewVisibility(IsPreviewTarget(target));
            EnsureThemeDefaultsFromTemplate();
            RefreshVisualThemeEditor();
            UpdateCodePreview();
            RebuildTextSettingsEditor();
            status.Text = File.Exists(target.FullPath)
                ? $"Editando copia de usuario: {target.FullPath}"
                : "Este archivo aun no existe en tu perfil. Guardar creara una copia editable.";
        }

        void SelectThemeColor(string colorKey)
        {
            selectedColorKey = colorKey;
            if (!TryGetThemeColor(editor.Text, colorKey, out var value) || !TryParseColor(value, out var color))
            {
                return;
            }

            isSyncingVisualEditor = true;
            colorPicker.Color = color;
            isSyncingVisualEditor = false;
        }

        void ApplyThemeColorFromTextBox(string colorKey)
        {
            if (!colorRows.TryGetValue(colorKey, out var row))
            {
                return;
            }

            var value = NormalizeHex(row.TextBox.Text ?? string.Empty);
            if (value is null)
            {
                status.Text = $"Color invalido en '{colorKey}'. Usa formato #RRGGBB.";
                RefreshVisualThemeEditor();
                return;
            }

            SetThemeColor(colorKey, value);
        }

        void SetThemeColor(string colorKey, string color)
        {
            var updated = TrySetThemeColor(editor.Text, colorKey, color);
            if (updated is null)
            {
                status.Text = $"No pude actualizar '{colorKey}'. Revisa que sea un tema JSON valido.";
                return;
            }

            isSyncingVisualEditor = true;
            editor.Text = updated;
            if (colorRows.TryGetValue(colorKey, out var row))
            {
                row.TextBox.Text = color;
                row.Preview.Background = new SolidColorBrush(Color.Parse(color));
            }
            colorPicker.Color = Color.Parse(color);
            isSyncingVisualEditor = false;
            UpdateCodePreview();
            status.Text = $"Color actualizado: {colorKey} = {color}";
        }

        void RefreshVisualThemeEditor()
        {
            if (!IsThemeTarget(selectedTarget))
            {
                return;
            }

            isSyncingVisualEditor = true;
            foreach (var colorKey in ThemeColorKeys)
            {
                if (!colorRows.TryGetValue(colorKey, out var row))
                {
                    continue;
                }

                if (TryGetThemeColor(editor.Text, colorKey, out var value) && TryParseColor(value, out var color))
                {
                    var hex = ToHex(color);
                    row.TextBox.Text = hex;
                    row.Preview.Background = new SolidColorBrush(color);
                }
                else
                {
                    row.TextBox.Text = string.Empty;
                    row.Preview.Background = Brushes.Transparent;
                }
            }

            if (TryGetThemeColor(editor.Text, selectedColorKey, out var selectedColor) && TryParseColor(selectedColor, out var parsed))
            {
                colorPicker.Color = parsed;
            }
            isSyncingVisualEditor = false;
            UpdateCodePreview();
        }

        void EnsureThemeDefaultsFromTemplate()
        {
            if (!IsThemeTarget(selectedTarget))
            {
                return;
            }

            var text = editor.Text ?? string.Empty;
            var changed = false;
            foreach (var colorKey in ThemeColorKeys)
            {
                if (TryGetThemeColor(text, colorKey, out _))
                {
                    continue;
                }

                var fallback = TryGetThemeColor(selectedTarget.Template, colorKey, out var templateValue)
                    ? NormalizeHex(templateValue)
                    : DefaultThemeColor(colorKey);

                if (fallback is null)
                {
                    continue;
                }

                var updated = TrySetThemeColor(text, colorKey, fallback);
                if (updated is null)
                {
                    continue;
                }

                text = updated;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            isSyncingVisualEditor = true;
            editor.Text = text;
            isSyncingVisualEditor = false;
        }

        void RebuildTextSettingsEditor()
        {
            if (IsThemeTarget(selectedTarget) || isSyncingVisualEditor)
            {
                return;
            }

            visualTextEditor.Children.Clear();
            if (!TryParseJsonObject(editor.Text ?? string.Empty, out var root))
            {
                visualTextEditor.Children.Add(new TextBlock
                {
                    Text = "JSON invalido. Corrige el texto para volver al editor visual.",
                    Foreground = Brush("TextSecondary"),
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            if (selectedTarget.RelativePath.Equals("default-settings.json", StringComparison.OrdinalIgnoreCase))
            {
                visualTextEditor.Children.Add(BuildVisualSection("Lenguaje", "Selecciona el dialecto activo que usara editor, ayuda, validacion y ejecucion."));
                visualTextEditor.Children.Add(BuildVisualTextField("Dialect active", "language.activeDialect", GetJsonString(root, "language", "activeDialect"), value => SetJsonString(["language", "activeDialect"], value)));
                visualTextEditor.Children.Add(BuildVisualSection("Editor", "Ajusta el tamano del texto y los temas de sintaxis por modo de interfaz."));
                visualTextEditor.Children.Add(BuildVisualTextField("Font size", "editor.fontSize", GetJsonNumber(root, DefaultEditorFontSize, "editor", "fontSize"), value => SetJsonNumber(["editor", "fontSize"], value, MinEditorFontSize, MaxEditorFontSize)));
                visualTextEditor.Children.Add(BuildVisualTextField("Interface scale", "editor.interfaceScale", GetJsonNumber(root, DefaultInterfaceScale, "editor", "interfaceScale"), value => SetJsonNumber(["editor", "interfaceScale"], value, MinInterfaceScale, MaxInterfaceScale)));
                visualTextEditor.Children.Add(BuildVisualTextField("Dark syntax theme", "editor.syntaxThemeDark", GetJsonString(root, "editor", "syntaxThemeDark"), value => SetJsonString(["editor", "syntaxThemeDark"], value)));
                visualTextEditor.Children.Add(BuildVisualTextField("Light syntax theme", "editor.syntaxThemeLight", GetJsonString(root, "editor", "syntaxThemeLight"), value => SetJsonString(["editor", "syntaxThemeLight"], value)));
                visualTextEditor.Children.Add(BuildVisualSection("Diagnostic", "Habilita diagnosticos y selecciona que datos mostrar en cada evento."));
                visualTextEditor.Children.Add(BuildVisualTextField("Diagnostic enabled", "diagnostic.enabled", GetJsonBoolean(root, false, "diagnostic", "enabled"), value => SetJsonBool(["diagnostic", "enabled"], value)));
                visualTextEditor.Children.Add(BuildVisualTextField("Clicks", "diagnostic.features.clicks", GetJsonBoolean(root, false, "diagnostic", "features", "clicks"), value => SetJsonBool(["diagnostic", "features", "clicks"], value)));
                visualTextEditor.Children.Add(BuildVisualTextField("Keystrokes", "diagnostic.features.keystrokes", GetJsonBoolean(root, false, "diagnostic", "features", "keystrokes"), value => SetJsonBool(["diagnostic", "features", "keystrokes"], value)));
                visualTextEditor.Children.Add(BuildVisualTextField("Mouse position", "diagnostic.features.mousePosition", GetJsonBoolean(root, true, "diagnostic", "features", "mousePosition"), value => SetJsonBool(["diagnostic", "features", "mousePosition"], value)));
                return;
            }

            if (selectedTarget.RelativePath.StartsWith("dialects/", StringComparison.OrdinalIgnoreCase))
            {
                visualTextEditor.Children.Add(BuildVisualSection("Dialect", "Datos generales del dialecto."));
                visualTextEditor.Children.Add(BuildVisualTextField("ID", "id", GetJsonString(root, "id"), value => SetJsonString(["id"], value)));
                visualTextEditor.Children.Add(BuildVisualTextField("Display name", "displayName", GetJsonString(root, "displayName"), value => SetJsonString(["displayName"], value)));
                visualTextEditor.Children.Add(BuildVisualTextField("Types", "types", string.Join(", ", GetJsonStringArray(root, "types")), value => SetJsonArray(["types"], value)));

                visualTextEditor.Children.Add(BuildVisualSection("Keywords", "Cada rol tiene una sola palabra activa. Cambiarla reemplaza la anterior."));
                if (root["keywords"] is JsonObject keywords)
                {
                    foreach (var keyword in PseudoLanguageDefinition.RequiredKeywordRoles.Concat(PseudoLanguageDefinition.OptionalKeywordRoles))
                    {
                        var value = keywords[keyword]?.GetValue<string>() ?? string.Empty;
                        visualTextEditor.Children.Add(BuildVisualTextField(KeywordDisplayName(keyword), $"keywords.{keyword}", value, next => SetJsonString(["keywords", keyword], next)));
                    }
                }
                else
                {
                    visualTextEditor.Children.Add(new TextBlock
                    {
                        Text = "No hay seccion keywords. Puedes crearla desde el JSON.",
                        Foreground = Brush("TextSecondary"),
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                return;
            }

            visualTextEditor.Children.Add(new TextBlock
            {
                Text = "Este archivo se edita como JSON.",
                Foreground = Brush("TextSecondary"),
                TextWrapping = TextWrapping.Wrap
            });

            void SetJsonString(string[] path, string value)
            {
                var updated = TrySetJsonString(editor.Text ?? string.Empty, path, value);
                if (updated is null)
                {
                    status.Text = $"No pude actualizar {string.Join('.', path)}.";
                    return;
                }

                isSyncingVisualEditor = true;
                editor.Text = updated;
                isSyncingVisualEditor = false;
                UpdateCodePreview();
            }

            void SetJsonArray(string[] path, string value)
            {
                var items = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var updated = TrySetJsonStringArray(editor.Text ?? string.Empty, path, items);
                if (updated is null)
                {
                    status.Text = $"No pude actualizar {string.Join('.', path)}.";
                    return;
                }

                isSyncingVisualEditor = true;
                editor.Text = updated;
                isSyncingVisualEditor = false;
                UpdateCodePreview();
            }

            void SetJsonNumber(string[] path, string value, double min, double max)
            {
                if (!double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var number))
                {
                    status.Text = $"{string.Join('.', path)} debe ser un numero.";
                    return;
                }

                var updated = TrySetJsonNumber(editor.Text ?? string.Empty, path, Math.Clamp(number, min, max));
                if (updated is null)
                {
                    status.Text = $"No pude actualizar {string.Join('.', path)}.";
                    return;
                }

                isSyncingVisualEditor = true;
                editor.Text = updated;
                isSyncingVisualEditor = false;
                UpdateCodePreview();
            }

            void SetJsonBool(string[] path, string value)
            {
                if (!bool.TryParse(value, out var parsed))
                {
                    status.Text = $"{string.Join('.', path)} debe ser true o false.";
                    return;
                }

                var updated = TrySetJsonBool(editor.Text ?? string.Empty, path, parsed);
                if (updated is null)
                {
                    status.Text = $"No pude actualizar {string.Join('.', path)}.";
                    return;
                }

                isSyncingVisualEditor = true;
                editor.Text = updated;
                isSyncingVisualEditor = false;
                UpdateCodePreview();
            }
        }

        void UpdateCodePreview()
        {
            if (!IsPreviewTarget(selectedTarget))
            {
                return;
            }

            var previewLanguage = IsDialectTarget(selectedTarget)
                ? BuildPreviewLanguage(editor.Text ?? string.Empty)
                : _language;
            var palette = IsThemeTarget(selectedTarget)
                ? BuildThemePreviewPalette(editor.Text ?? string.Empty)
                : (_isLightTheme ? _runtimeSettings.LightSyntaxTheme : _runtimeSettings.DarkSyntaxTheme).ToPalette();

            codePreviewTitle.Text = IsDialectTarget(selectedTarget)
                ? "Vista previa con esta sintaxis"
                : "Vista previa del codigo";
            codePreview.Text = BuildPreviewCode(previewLanguage);
            codePreviewColorizer.SetLanguage(previewLanguage);
            codePreviewColorizer.SetPalette(palette);
            codePreviewDiagnosticRenderer.SetBrush(palette.DiagnosticUnderlineBrush);
            codePreviewDiagnosticRenderer.SetLines([4]);
            codePreview.Background = palette.EditorBackgroundBrush;
            codePreview.TextArea.Background = palette.EditorBackgroundBrush;
            codePreview.Foreground = BuildReadableTextBrush(palette.EditorBackgroundBrush);
            codePreview.LineNumbersForeground = BuildMutedTextBrush(palette.EditorBackgroundBrush);
            codePreview.TextArea.Caret.CaretBrush = BuildReadableTextBrush(palette.EditorBackgroundBrush);
            codePreview.TextArea.TextView.Redraw();
        }

        void SetCodePreviewVisibility(bool isVisible)
        {
            codePreviewPanel.IsVisible = isVisible;
            codePreviewSplitter.IsVisible = isVisible;
            editorPanelRows[1].Height = new GridLength(isVisible ? 6 : 0);
            editorPanelRows[2].Height = new GridLength(isVisible ? 210 : 0);
        }

        EnsureThemeDefaultsFromTemplate();
        RefreshVisualThemeEditor();
        UpdateCodePreview();
        RebuildTextSettingsEditor();

        var targetList = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(12)
        };

        foreach (var target in targets)
        {
            var button = new Button
            {
                Content = target.Title,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            button.Click += (_, _) => SelectTarget(target);
            targetList.Children.Add(button);
        }

        var validateButton = new Button { Content = "Validar JSON", Classes = { "command" } };
        validateButton.Click += (_, _) =>
        {
            status.Text = AppSettingsService.IsValidJson(editor.Text ?? string.Empty, out var error)
                ? "JSON valido."
                : $"JSON invalido: {error}";
        };

        var saveButton = new Button { Content = "Guardar y aplicar", Classes = { "command" } };
        saveButton.Click += (_, _) =>
        {
            var json = editor.Text ?? string.Empty;
            if (!AppSettingsService.IsValidJson(json, out var error))
            {
                status.Text = $"No se guardo. JSON invalido: {error}";
                return;
            }

            try
            {
                var result = AppSettingsService.SaveAndSelectTarget(_runtimeSettings, selectedTarget, json, _isLightTheme);
                ReloadRuntimeSettings();
                status.Text = $"{result.Message} Archivo: {result.SavedPath}";
            }
            catch (Exception ex)
            {
                status.Text = $"No se pudo aplicar: {ex.Message}";
            }
        };

        var loadButton = new Button { Content = "Cargar JSON...", Classes = { "command" } };
        loadButton.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Cargar JSON",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("JSON")
                    {
                        Patterns = ["*.json"]
                    },
                    FilePickerFileTypes.All
                ]
            });

            var file = files.FirstOrDefault();
            if (file is null)
            {
                return;
            }

            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            editor.Text = await reader.ReadToEndAsync();
            status.Text = AppSettingsService.TryReadJsonId(editor.Text ?? string.Empty, out var id, out _)
                ? $"JSON cargado desde: {file.Name}. Usa Guardar y aplicar para seleccionarlo como '{id}'."
                : $"JSON cargado desde: {file.Name}. Usa Guardar y aplicar para copiarlo a {selectedTarget.RelativePath}.";
        };

        var resetButton = new Button { Content = "Usar plantilla", Classes = { "command" } };
        resetButton.Click += (_, _) =>
        {
            editor.Text = selectedTarget.Template.Trim() + Environment.NewLine;
            status.Text = "Plantilla cargada en el editor. Usa Guardar y aplicar para activarla.";
        };

        var openFolderButton = new Button { Content = "Abrir carpeta", Classes = { "command" } };
        openFolderButton.Click += async (_, _) =>
        {
            if (!await OpenFolderAsync(_runtimeSettings.UserSettingsPath) && Clipboard is not null)
            {
                await Clipboard.SetTextAsync(_runtimeSettings.UserSettingsPath);
                status.Text = "No pude abrir la carpeta; copie la ruta al portapapeles.";
            }
        };

        var header = new Border
        {
            Background = Brush("PanelBackground"),
            BorderBrush = Brush("BorderBrushMuted"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 12),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 20,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brush("TextPrimary")
                    },
                    fileLabel
                }
            }
        };
        Grid.SetColumnSpan(header, 5);

        var navigation = new Border
        {
            Background = Brush("PanelBackground"),
            BorderBrush = Brush("BorderBrushMuted"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new ScrollViewer { Content = targetList }
        };
        Grid.SetRow(navigation, 1);

        var editorPanel = new Border
        {
            Padding = new Thickness(12, 0, 12, 0),
            Child = new Grid
            {
                RowDefinitions = editorPanelRows,
                Children =
                {
                    editor,
                    WithGridRow(codePreviewSplitter, 1),
                    WithGridRow(codePreviewPanel, 2)
                }
            }
        };
        Grid.SetColumn(editorPanel, 2);
        Grid.SetRow(editorPanel, 1);

        var visualThemePanel = new Border
        {
            Background = Brush("PanelBackground"),
            BorderBrush = Brush("BorderBrushMuted"),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = new ScrollViewer
            {
                Content = visualEditorHost,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            }
        };
        Grid.SetColumn(visualThemePanel, 4);
        Grid.SetRow(visualThemePanel, 1);

        var navigationSplitter = new GridSplitter
        {
            Width = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brush("BorderBrushMuted"),
            ShowsPreview = true
        };
        Grid.SetColumn(navigationSplitter, 1);
        Grid.SetRow(navigationSplitter, 1);

        var visualSplitter = new GridSplitter
        {
            Width = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brush("BorderBrushMuted"),
            ShowsPreview = true
        };
        Grid.SetColumn(visualSplitter, 3);
        Grid.SetRow(visualSplitter, 1);

        var actionButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                loadButton,
                resetButton,
                validateButton,
                saveButton,
                openFolderButton
            }
        };
        Grid.SetColumn(actionButtons, 1);

        var footer = new Border
        {
            Background = Brush("PanelBackground"),
            BorderBrush = Brush("BorderBrushMuted"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    status,
                    actionButtons
                }
            }
        };
        Grid.SetColumnSpan(footer, 5);
        Grid.SetRow(footer, 2);

        var window = new Window
        {
            Title = title,
            Width = 1320,
            Height = 720,
            MinWidth = 1120,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("EditorBackground"),
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("230,6,*,6,720"),
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                Children =
                {
                    header,
                    navigation,
                    navigationSplitter,
                    editorPanel,
                    visualSplitter,
                    visualThemePanel,
                    footer
                }
            }
        };

        ShowSingleInstanceWindow(key, window);
        return Task.CompletedTask;
    }

    private Task ShowSettingsConfigurationAsync()
    {
        const string key = "settings-overview";
        if (ActivateSingleInstanceWindow(key))
        {
            return Task.CompletedTask;
        }

        var colors = _isLightTheme ? LightTheme : DarkTheme;
        var window = new Window
        {
            Title = "Configurar temas y dialectos",
            Width = 760,
            Height = 620,
            MinWidth = 620,
            MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("EditorBackground")
        };

        var status = new TextBlock
        {
            Text = "Edita los JSON y guarda desde el editor para aplicar los cambios al momento.",
            Foreground = Brush("TextSecondary"),
            TextWrapping = TextWrapping.Wrap
        };

        var pathBox = new TextBox
        {
            Text = _runtimeSettings.UserSettingsPath,
            IsReadOnly = true,
            Background = Brush("InsetBackground"),
            Foreground = Brush("TextPrimary"),
            BorderBrush = Brush("BorderBrushMuted")
        };

        var createButton = new Button { Content = "Crear plantillas JSON", Classes = { "command" } };
        createButton.Click += (_, _) =>
        {
            var files = AppSettingsService.CreateUserTemplateFiles(_runtimeSettings);
            status.Text = files.Count == 0
                ? "Las plantillas ya existian. Puedes editarlas en la carpeta de settings."
                : $"Plantillas creadas: {files.Count}. Puedes editarlas y guardarlas desde Configuracion.";
        };

        var copyButton = new Button { Content = "Copiar ruta", Classes = { "command" } };
        copyButton.Click += async (_, _) =>
        {
            if (Clipboard is not null)
            {
                await Clipboard.SetTextAsync(_runtimeSettings.UserSettingsPath);
                status.Text = "Ruta copiada al portapapeles.";
            }
        };

        var openButton = new Button { Content = "Abrir carpeta", Classes = { "command" } };
        openButton.Click += async (_, _) =>
        {
            if (!await OpenFolderAsync(_runtimeSettings.UserSettingsPath) && Clipboard is not null)
            {
                await Clipboard.SetTextAsync(_runtimeSettings.UserSettingsPath);
                status.Text = "No pude abrir la carpeta; copie la ruta al portapapeles.";
            }
        };

        var docsButton = new Button { Content = "Ver guia JSON", Classes = { "command" } };
        docsButton.Click += async (_, _) => await ShowDocumentationAsync(DocumentationService.JsonConfig);

        var content = new StackPanel
        {
            Spacing = 14,
            Margin = new Thickness(24),
            Children =
            {
                new TextBlock
                {
                    Text = "Configurar temas y dialectos",
                    FontSize = 22,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brush("TextPrimary")
                },
                new TextBlock
                {
                    Text = "Los dialectos reemplazan las palabras activas del lenguaje. Los temas cambian solo los colores de sintaxis del editor.",
                    Foreground = Brush("TextSecondary"),
                    TextWrapping = TextWrapping.Wrap
                },
                BuildSettingsInfo("Dialecto activo", $"{_language.DisplayName} ({_language.Id})"),
                BuildSettingsInfo("Tema oscuro", $"{_runtimeSettings.DarkSyntaxTheme.DisplayName} ({_runtimeSettings.DarkSyntaxTheme.Id})"),
                BuildSettingsInfo("Tema claro", $"{_runtimeSettings.LightSyntaxTheme.DisplayName} ({_runtimeSettings.LightSyntaxTheme.Id})"),
                BuildSettingsInfo("Carpeta de usuario", string.Empty),
                pathBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        createButton,
                        openButton,
                        copyButton,
                        docsButton
                    }
                },
                new TextBlock
                {
                    Text = "Archivos esperados",
                    FontSize = 16,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brush("TextPrimary")
                },
                BuildCodePreview(
                    "default-settings.json\n" +
                    "dialects/custom.json\n" +
                    "syntax-themes/custom-dark.json"),
                new TextBlock
                {
                    Text = "Ejemplo rapido: cambia activeDialect a custom, edita dialects/custom.json y reemplaza write de Escribir a Mostrar. En ese dialecto Escribir dejara de ser valido.",
                    Foreground = Brush("TextSecondary"),
                    TextWrapping = TextWrapping.Wrap
                },
                status
            }
        };

        window.Content = new ScrollViewer
        {
            Content = content
        };
        ShowSingleInstanceWindow(key, window);
        return Task.CompletedTask;
    }

    private bool ActivateSingleInstanceWindow(string key)
    {
        if (!_singleInstanceWindows.TryGetValue(key, out var window))
        {
            return false;
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        return true;
    }

    private void ShowSingleInstanceWindow(string key, Window window)
    {
        _singleInstanceWindows[key] = window;
        window.Closed += (_, _) =>
        {
            if (_singleInstanceWindows.TryGetValue(key, out var current) && ReferenceEquals(current, window))
            {
                _singleInstanceWindows.Remove(key);
            }
        };
        window.Show(this);
        window.Activate();
    }

    private TextBlock BuildVisualSection(string title, string description) => new()
    {
        Text = $"{title}\n{description}",
        Foreground = Brush("TextPrimary"),
        FontWeight = FontWeight.SemiBold,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 0)
    };

    private Grid BuildVisualTextField(string label, string settingPath, string value, Action<string> commit)
    {
        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = Brush("TextPrimary"),
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        var pathBlock = new TextBlock
        {
            Text = settingPath,
            Foreground = Brush("TextSecondary"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        var input = new TextBox
        {
            Text = value,
            Background = Brush("InsetBackground"),
            Foreground = Brush("TextPrimary"),
            BorderBrush = Brush("BorderBrushMuted"),
            FontFamily = new FontFamily("Cascadia Code,Consolas,monospace"),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        input.PointerPressed += (_, _) => input.Focus(NavigationMethod.Pointer);
        input.LostFocus += (_, _) => commit(input.Text ?? string.Empty);
        input.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                commit(input.Text ?? string.Empty);
                args.Handled = true;
            }
        };

        return new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            RowSpacing = 4,
            Children =
            {
                labelBlock,
                WithGridRow(pathBlock, 1),
                WithGridRow(input, 2)
            }
        };
    }

    private StackPanel BuildQuickColorPalette(Action<string> applyColor, bool compact = false)
    {
        var colors = new (string Label, string Hex)[]
        {
            ("Negro", "#000000"),
            ("Blanco", "#FFFFFF"),
            ("Gris 900", "#111827"),
            ("Gris 700", "#374151"),
            ("Gris 300", "#D1D5DB"),
            ("Rojo", "#EF4444"),
            ("Verde", "#22C55E"),
            ("Azul", "#3B82F6"),
            ("Amarillo", "#FACC15"),
            ("Morado", "#8B5CF6")
        };

        var panel = new StackPanel
        {
            Spacing = 8
        };
        panel.Children.Add(new TextBlock
        {
            Text = "Colores rapidos",
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("TextPrimary")
        });

        var wrap = new WrapPanel
        {
            Orientation = Orientation.Horizontal
        };

        foreach (var (label, hex) in colors)
        {
            if (compact)
            {
                var swatch = new Button
                {
                    Width = 30,
                    Height = 30,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 8, 8),
                    Background = new SolidColorBrush(Color.Parse(hex)),
                    BorderBrush = Brush("BorderBrushMuted"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4)
                };
                ToolTip.SetTip(swatch, $"{label} {hex}");
                swatch.Click += (_, _) => applyColor(hex);
                wrap.Children.Add(swatch);
                continue;
            }

            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new Border
                        {
                            Width = 18,
                            Height = 18,
                            CornerRadius = new CornerRadius(3),
                            BorderBrush = Brush("BorderBrushMuted"),
                            BorderThickness = new Thickness(1),
                            Background = new SolidColorBrush(Color.Parse(hex))
                        },
                        new TextBlock
                        {
                            Text = label,
                            Foreground = Brush("TextPrimary"),
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                },
                Classes = { "command" },
                Padding = new Thickness(8, 4),
                Margin = new Thickness(0, 0, 6, 6)
            };
            ToolTip.SetTip(button, hex);
            button.Click += (_, _) => applyColor(hex);
            wrap.Children.Add(button);
        }

        panel.Children.Add(wrap);
        return panel;
    }

    private static T WithGridColumn<T>(T control, int column)
        where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static T WithGridRow<T>(T control, int row)
        where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    private static bool IsThemeTarget(JsonConfigTarget target) =>
        target.RelativePath.StartsWith("syntax-themes/", StringComparison.OrdinalIgnoreCase);

    private static bool IsDialectTarget(JsonConfigTarget target) =>
        target.RelativePath.StartsWith("dialects/", StringComparison.OrdinalIgnoreCase);

    private static bool IsPreviewTarget(JsonConfigTarget target) =>
        IsThemeTarget(target) || IsDialectTarget(target);

    private static bool TryParseJsonObject(string json, out JsonObject root)
    {
        try
        {
            root = JsonNode.Parse(json) as JsonObject ?? [];
            return true;
        }
        catch
        {
            root = [];
            return false;
        }
    }

    private static string GetJsonString(JsonObject root, params string[] path)
    {
        JsonNode? node = root;
        foreach (var segment in path)
        {
            node = node?[segment];
        }

        return node?.GetValue<string>() ?? string.Empty;
    }

    private static string GetJsonNumber(JsonObject root, double fallback, params string[] path)
    {
        JsonNode? node = root;
        foreach (var segment in path)
        {
            node = node?[segment];
        }

        return node is JsonValue value && value.TryGetValue<double>(out var number)
            ? number.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : fallback.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string GetJsonBoolean(JsonObject root, bool fallback, params string[] path)
    {
        JsonNode? node = root;
        foreach (var segment in path)
        {
            node = node?[segment];
        }

        return node is JsonValue value && value.TryGetValue<bool>(out var boolean)
            ? boolean.ToString().ToLowerInvariant()
            : fallback.ToString().ToLowerInvariant();
    }

    private static IEnumerable<string> GetJsonStringArray(JsonObject root, params string[] path)
    {
        JsonNode? node = root;
        foreach (var segment in path)
        {
            node = node?[segment];
        }

        return node is JsonArray array
            ? array.Select(item => item?.GetValue<string>() ?? string.Empty).Where(item => item.Length > 0)
            : [];
    }

    private static string? TrySetJsonString(string json, IReadOnlyList<string> path, string value)
    {
        if (!TryParseJsonObject(json, out var root) || path.Count == 0)
        {
            return null;
        }

        var parent = EnsureJsonParent(root, path);
        parent[path[^1]] = value;
        return root.ToJsonString(JsonWriteOptions) + Environment.NewLine;
    }

    private static string? TrySetJsonStringArray(string json, IReadOnlyList<string> path, IEnumerable<string> values)
    {
        if (!TryParseJsonObject(json, out var root) || path.Count == 0)
        {
            return null;
        }

        var parent = EnsureJsonParent(root, path);
        parent[path[^1]] = new JsonArray(values.Select(value => JsonValue.Create(value)).ToArray<JsonNode?>());
        return root.ToJsonString(JsonWriteOptions) + Environment.NewLine;
    }

    private static string? TrySetJsonNumber(string json, IReadOnlyList<string> path, double value)
    {
        if (!TryParseJsonObject(json, out var root) || path.Count == 0)
        {
            return null;
        }

        var parent = EnsureJsonParent(root, path);
        parent[path[^1]] = value;
        return root.ToJsonString(JsonWriteOptions) + Environment.NewLine;
    }

    private static string? TrySetJsonBool(string json, IReadOnlyList<string> path, bool value)
    {
        if (!TryParseJsonObject(json, out var root) || path.Count == 0)
        {
            return null;
        }

        var parent = EnsureJsonParent(root, path);
        parent[path[^1]] = value;
        return root.ToJsonString(JsonWriteOptions) + Environment.NewLine;
    }

    private static JsonObject EnsureJsonParent(JsonObject root, IReadOnlyList<string> path)
    {
        var current = root;
        for (var index = 0; index < path.Count - 1; index++)
        {
            if (current[path[index]] is not JsonObject next)
            {
                next = [];
                current[path[index]] = next;
            }

            current = next;
        }

        return current;
    }

    private static bool TryGetThemeColor(string json, string colorKey, out string value)
    {
        try
        {
            var node = JsonNode.Parse(json);
            value = node?["syntax"]?[colorKey]?.GetValue<string>() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            value = string.Empty;
            return false;
        }
    }

    private static string? TrySetThemeColor(string json, string colorKey, string color)
    {
        try
        {
            var node = JsonNode.Parse(json) as JsonObject;
            if (node is null)
            {
                return null;
            }

            if (node["syntax"] is not JsonObject syntax)
            {
                syntax = [];
                node["syntax"] = syntax;
            }

            syntax[colorKey] = color;
            return node.ToJsonString(JsonWriteOptions) + Environment.NewLine;
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeHex(string value)
    {
        value = value.Trim();
        if (!value.StartsWith('#'))
        {
            value = "#" + value;
        }

        if (value.Length == 4)
        {
            value = $"#{value[1]}{value[1]}{value[2]}{value[2]}{value[3]}{value[3]}";
        }

        return TryParseColor(value, out var color) ? ToHex(color) : null;
    }

    private static bool TryParseColor(string value, out Color color)
    {
        try
        {
            color = Color.Parse(value);
            return true;
        }
        catch
        {
            color = default;
            return false;
        }
    }

    private static string ToHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string ThemeColorDisplayName(string colorKey) => colorKey switch
    {
        "keyword" => "Keyword",
        "type" => "Type",
        "string" => "String",
        "number" => "Number",
        "operator" => "Operator",
        "comment" => "Comment",
        "blockBackground" => "Block background",
        "diagnosticUnderline" => "Diagnostic underline",
        "editorBackground" => "Editor background",
        _ => colorKey
    };

    private static string KeywordDisplayName(string role) => role switch
    {
        "algorithmStart" => "Algorithm start",
        "algorithmEnd" => "Algorithm end",
        "processStart" => "Process start",
        "processEnd" => "Process end",
        "declare" => "Declare",
        "typeSeparator" => "Type separator",
        "write" => "Write",
        "read" => "Read",
        "if" => "If",
        "then" => "Then",
        "else" => "Else",
        "endIf" => "End if",
        "while" => "While",
        "do" => "Do",
        "endWhile" => "End while",
        "for" => "For",
        "until" => "Until",
        "step" => "Step",
        "endFor" => "End for",
        "switch" => "Switch",
        "otherwise" => "Otherwise",
        "endSwitch" => "End switch",
        "true" => "True",
        "false" => "False",
        "and" => "And",
        "or" => "Or",
        "not" => "Not",
        "clear" => "Clear",
        "screen" => "Screen",
        "wait" => "Wait",
        "seconds" => "Seconds",
        "milliseconds" => "Milliseconds",
        "withoutNewline" => "Without newline",
        _ => role
    };

    private static string? DefaultThemeColor(string colorKey) => colorKey switch
    {
        "keyword" => "#5EA1FF",
        "type" => "#4EC9B0",
        "string" => "#CE9178",
        "number" => "#B5CEA8",
        "operator" => "#DCDCAA",
        "comment" => "#6A9955",
        "blockBackground" => "#1F3B4D",
        "diagnosticUnderline" => "#FF4D4D",
        "editorBackground" => "#1E1E1E",
        _ => null
    };

    private void ReloadRuntimeSettings()
    {
        SaveCurrentDocumentState();
        _runtimeSettings = AppSettingsService.Load();
        _language = _runtimeSettings.Language;
        _syntaxValidator = new PseudoSyntaxValidator(_language);
        _completionItems = _language.Snippets.ToArray();
        _quickTemplates = _language.BuildQuickTemplates().ToArray();
        _helpTopics = BuildDefaultHelpTopics(_language);

        foreach (var document in _openDocuments)
        {
            document.ApplyLanguage(_language);
            _syntaxValidator.Validate(document.Text);
            UpdateLiveSyntaxDiagnostics(document);
        }

        _completionWindow?.Close();
        _colorizer?.SetLanguage(_language);
        ApplyTheme(_isLightTheme ? LightTheme : DarkTheme);

        HelpTopicsPanel.Children.Clear();
        TemplatesPanel.Children.Clear();
        BuildHelpTopics();
        BuildEditorTools();
        ConfigureMouseClickDiagnostics();

        if (_currentDocument is not null)
        {
            VariablesTextBox.Text = string.Empty;
            OutputTextBox.Text = string.Empty;
            SwitchDocument(_currentDocument);
            UpdateLiveSyntaxDiagnostics(_currentDocument);
        }

        UpdateDiagnosticUnderlines();
        RenderOpenDocuments();
        UpdateOutputPanelView();
        UpdateWindowState($"Configuracion aplicada: {_language.DisplayName}");
    }

    private void ConfigureMouseClickDiagnostics()
    {
        var enabled = IsDiagnosticPanelEnabled();
        MouseClickDiagnosticsPanel.IsVisible = enabled;
        _mouseClickDiagnosticsLines.Clear();
        _contextMenuDiagnosticsLines.Clear();
        if (enabled)
        {
            MouseClickDiagnosticsTextBox.Text = BuildDiagnosticsPanelPlaceholder();
        }
    }

    private void MainWindow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Pointer.Type == PointerType.Mouse)
        {
            var sourceControl = e.Source as Control;
            var mousePoint = e.GetCurrentPoint(this);
            var insideEditor = IsPointInsideEditor(mousePoint.Position);

            if (mousePoint.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed && EditorContextMenuPopup.IsVisible && IsPointInsideContextMenuPopup(mousePoint.Position))
            {
                var item = GetContextMenuItemAtWindowPoint(mousePoint.Position);
                if (item is not null)
                {
                    item.Focus();
                    LogContextMenuDiagnostic($"pointer-pressed left menu-hit item={DescribeControl(item)} window={FormatPoint(mousePoint.Position)}");
                }

                e.Handled = true;
                return;
            }

            if (mousePoint.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
            {
                LogContextMenuDiagnostic(
                    $"pointer-pressed right inside-editor={insideEditor} window={FormatPoint(mousePoint.Position)} source={DescribeControl(sourceControl)} path={DescribeControlPath(sourceControl)}");
                LogPointerContextSnapshot("pointer-pressed", e, sourceControl, insideEditor);
            }

            if (mousePoint.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed && insideEditor)
            {
                if (_currentDocument is null)
                {
                    LogContextMenuDiagnostic("pointer-pressed skipped-menu no-current-document");
                    HideCustomEditorContextMenu();
                    return;
                }

                MoveCaretToRightClickPosition(e);
                EditorTextBox.Focus();
                LogContextMenuDiagnostic("pointer-pressed using-press-to-toggle-menu");
                ToggleCustomEditorContextMenu(e);
                e.Handled = true;
                return;
            }
            else if (mousePoint.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed && EditorContextMenuPopup.IsVisible && !IsPointInsideContextMenuPopup(mousePoint.Position))
            {
                LogContextMenuDiagnostic($"pointer-pressed left closing-menu source={DescribeControl(sourceControl)} window={FormatPoint(mousePoint.Position)}");
                HideCustomEditorContextMenu();
            }
        }

        if (!IsMouseClickDiagnosticsEnabled() || e.Pointer.Type != PointerType.Mouse)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        var target = e.Source as Control;
        var line = BuildMouseDiagnosticLine(point, target?.Name ?? e.Source?.GetType().Name ?? "Control");
        EnqueueMouseDiagnostic(line);
    }

    private void MainWindow_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Mouse)
        {
            return;
        }

        var sourceControl = e.Source as Control;
        var point = e.GetCurrentPoint(this);
        var insideEditor = IsPointInsideEditor(point.Position);

        if (point.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonReleased)
        {
            LogContextMenuDiagnostic(
                $"pointer-released right inside-editor={insideEditor} window={FormatPoint(point.Position)} source={DescribeControl(sourceControl)} path={DescribeControlPath(sourceControl)} menu-visible={EditorContextMenuPopup.IsVisible}");
            LogPointerContextSnapshot("pointer-released", e, sourceControl, insideEditor);
        }

        if (point.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased && EditorContextMenuPopup.IsVisible && IsPointInsideContextMenuPopup(point.Position))
        {
            var item = GetContextMenuItemAtWindowPoint(point.Position);
            if (item is not null)
            {
                LogContextMenuDiagnostic($"pointer-released left menu-execute item={DescribeControl(item)} window={FormatPoint(point.Position)}");
                ExecuteContextMenuItem(item);
            }

            e.Handled = true;
            return;
        }

        if (point.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased && EditorContextMenuPopup.IsVisible && !IsPointInsideContextMenuPopup(point.Position))
        {
            LogContextMenuDiagnostic($"pointer-released left closing-menu source={DescribeControl(sourceControl)} window={FormatPoint(point.Position)}");
            HideCustomEditorContextMenu();
        }
    }

    private void MainWindow_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Mouse || !EditorContextMenuPopup.IsVisible)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (!IsPointInsideContextMenuPopup(point.Position))
        {
            return;
        }

        var item = GetContextMenuItemAtWindowPoint(point.Position);
        if (item is not null && !item.IsFocused)
        {
            item.Focus();
        }
    }

    private void MainWindow_DiagnosticKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_runtimeSettings.Diagnostic.Enabled || !_runtimeSettings.Diagnostic.Features.Keystrokes)
        {
            return;
        }

        var sourceControl = e.Source as Control;
        var sourceName = sourceControl?.Name ?? e.Source?.GetType().Name ?? "Control";
        var keyLabel = BuildKeystrokeLabel(e);
        EnqueueMouseDiagnostic($"{DateTime.Now:HH:mm:ss}  Key  {keyLabel}  {sourceName}");
    }

    private static string FormatMouseButton(PointerUpdateKind kind) => kind switch
    {
        PointerUpdateKind.LeftButtonPressed => "Left",
        PointerUpdateKind.RightButtonPressed => "Right",
        PointerUpdateKind.MiddleButtonPressed => "Middle",
        PointerUpdateKind.XButton1Pressed => "X1",
        PointerUpdateKind.XButton2Pressed => "X2",
        _ => "Mouse"
    };

    private bool IsMouseClickDiagnosticsEnabled() =>
        _runtimeSettings.Diagnostic.Enabled && _runtimeSettings.Diagnostic.Features.Clicks;

    private bool IsDiagnosticPanelEnabled() =>
        _runtimeSettings.Diagnostic.Enabled &&
        (_runtimeSettings.Diagnostic.Features.Clicks || _runtimeSettings.Diagnostic.Features.Keystrokes);

    private string BuildMouseDiagnosticLine(PointerPoint point, string sourceName)
    {
        var parts = new List<string>(4)
        {
            DateTime.Now.ToString("HH:mm:ss"),
            "Click",
            FormatMouseButton(point.Properties.PointerUpdateKind)
        };
        if (_runtimeSettings.Diagnostic.Features.MousePosition)
        {
            var position = point.Position;
            parts.Add($"X:{position.X,5:0} Y:{position.Y,5:0}");
        }

        parts.Add(sourceName);
        return string.Join("  ", parts);
    }

    private static string BuildKeystrokeLabel(KeyEventArgs e)
    {
        var parts = new List<string>(4);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        parts.Add(e.Key.ToString());
        return string.Join("+", parts);
    }

    private async void About_Click(object? sender, RoutedEventArgs e)
    {
        var window = new Window
        {
            Title = "Acerca de PseudoCode",
            Width = 560,
            Height = 640,
            MinWidth = 560,
            MinHeight = 640,
            MaxWidth = 560,
            MaxHeight = 640,
            CanResize = false,
            CanMinimize = false,
            CanMaximize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("PanelBackground"),
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://PseudoCode.App/Assets/logoPseudoCode.png")))
        };

        var appIcon = new Image
        {
            Source = new Bitmap(AssetLoader.Open(new Uri("avares://PseudoCode.App/Assets/logoPseudoCode.png"))),
            Width = 250,
            Height = 135,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var authorAnimations = BuildAuthorAnimations();
        var authorAnimationIndex = 0;
        var authorFrameIndex = 0;
        var authorFrameCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        Bitmap LoadAuthorFrame(string frame)
        {
            if (authorFrameCache.TryGetValue(frame, out var bitmap))
            {
                return bitmap;
            }

            bitmap = new Bitmap(AssetLoader.Open(new Uri($"avares://PseudoCode.App/Assets/KityDev/{frame}")));
            authorFrameCache[frame] = bitmap;
            return bitmap;
        }

        var authorLogo = new Image
        {
            Source = LoadAuthorFrame(authorAnimations[authorAnimationIndex].Frames[0]),
            Width = 190,
            Height = 190,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(authorLogo, "Click para cambiar animacion");

        var authorAnimationLabel = new TextBlock
        {
            Text = $"Animacion: {authorAnimations[authorAnimationIndex].Name}",
            Foreground = Brush("TextSecondary"),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var authorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(240)
        };
        authorTimer.Tick += (_, _) =>
        {
            var animation = authorAnimations[authorAnimationIndex];
            authorFrameIndex = (authorFrameIndex + 1) % animation.Frames.Length;
            authorLogo.Source = LoadAuthorFrame(animation.Frames[authorFrameIndex]);
        };
        authorTimer.Start();

        authorLogo.PointerPressed += (_, args) =>
        {
            authorAnimationIndex = (authorAnimationIndex + 1) % authorAnimations.Length;
            authorFrameIndex = 0;
            var animation = authorAnimations[authorAnimationIndex];
            authorLogo.Source = LoadAuthorFrame(animation.Frames[authorFrameIndex]);
            authorAnimationLabel.Text = $"Animacion: {animation.Name}";
            args.Handled = true;
        };

        var content = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(28),
            MaxWidth = 500,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                appIcon,
                new TextBlock
                {
                    Text = $"Version: {AppInfoService.Version}",
                    Foreground = Brush("TextSecondary"),
                    FontSize = 15,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new Border
                {
                    Height = 1,
                    Background = Brush("BorderBrushMuted"),
                    Margin = new Thickness(0, 10, 0, 4)
                },
                new TextBlock
                {
                    Text = "Autor",
                    Foreground = Brush("TextPrimary"),
                    FontSize = 17,
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                authorLogo,
                authorAnimationLabel,
                new TextBlock
                {
                    Text = AppInfoService.Author,
                    Foreground = Brush("TextSecondary"),
                    FontSize = 15,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                BuildAboutLink("YouTube", "@KityDev - https://www.youtube.com/@KityDev", AppInfoService.YouTubeUrl),
                BuildAboutLink("GitHub", AppInfoService.GitHubUrl, AppInfoService.GitHubUrl),
                BuildAboutLink("Web", "kity.dev", AppInfoService.WebUrl)
            }
        };

        window.Closed += (_, _) =>
        {
            authorTimer.Stop();
            foreach (var bitmap in authorFrameCache.Values)
            {
                bitmap.Dispose();
            }
        };

        window.Content = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
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
        _currentDocument.IsDebugging = false;
        _currentDocument.DebugLine = null;
        _currentDocument.DiagnosticLines.Clear();
        UpdateLiveSyntaxDiagnostics(_currentDocument);
        UpdateDiagnosticUnderlines();
        HighlightDebugLine(null);
        UpdateLineNumbers();
        UpdateOutputPanelView();
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
        AddRecentDocument(file.Path.LocalPath, file.Name);
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
            UpdateExecutionControls();
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

    private async Task<bool> ConfirmCloseWindowAsync()
    {
        SaveCurrentDocumentState();

        foreach (var document in _openDocuments.ToArray())
        {
            if (!document.HasUnsavedChanges)
            {
                continue;
            }

            var choice = await AskCloseUnsavedDocumentAsync(document);
            if (choice == CloseDocumentChoice.Cancel)
            {
                UpdateWindowState("Cierre de la app cancelado");
                return false;
            }

            if (choice == CloseDocumentChoice.Save && !await SaveDocumentAsync(document))
            {
                UpdateWindowState("Cierre de la app cancelado");
                return false;
            }
        }

        return true;
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
        UpdateExecutionControls();
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
            AddRecentDocument(file.Path.LocalPath, file.Name);
            UpdateWindowState($"Abierto: {file.Name}");
            return;
        }

        var document = new OpenDocument(file.Name, text, _language)
        {
            File = file,
            Location = file.Path.LocalPath
        };

        _openDocuments.Add(document);
        SwitchDocument(document);
        AddRecentDocument(file.Path.LocalPath, file.Name);
        UpdateWindowState($"Abierto: {file.Name}");
    }

    private async Task OpenRecentDocumentAsync(RecentDocumentInfo recent)
    {
        if (!File.Exists(recent.Path))
        {
            _recentDocuments = RecentDocumentsService.Remove(_runtimeSettings.UserSettingsPath, _recentDocuments, recent.Path);
            RenderRecentDocumentsMenu();
            UpdateWindowState($"Reciente no encontrado: {recent.DisplayName}");
            return;
        }

        var file = await StorageProvider.TryGetFileFromPathAsync(new Uri(recent.Path));
        if (file is null)
        {
            _recentDocuments = RecentDocumentsService.Remove(_runtimeSettings.UserSettingsPath, _recentDocuments, recent.Path);
            RenderRecentDocumentsMenu();
            UpdateWindowState($"No pude abrir reciente: {recent.DisplayName}");
            return;
        }

        await LoadFileAsync(file);
    }

    private void AddRecentDocument(string path, string displayName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _recentDocuments = RecentDocumentsService.Add(_runtimeSettings.UserSettingsPath, _recentDocuments, path, displayName);
        RenderRecentDocumentsMenu();
    }

    private void RenderRecentDocumentsMenu()
    {
        if (RecentFilesMenu is null)
        {
            return;
        }

        var items = new List<Control>();
        if (_recentDocuments.Count == 0)
        {
            items.Add(new MenuItem
            {
                Header = "Sin documentos recientes",
                IsEnabled = false
            });
        }
        else
        {
            foreach (var recent in _recentDocuments)
            {
                var item = new MenuItem
                {
                    Header = recent.DisplayName
                };
                ToolTip.SetTip(item, recent.Path);
                item.Click += async (_, _) => await OpenRecentDocumentAsync(recent);
                items.Add(item);
            }

            items.Add(new Separator());
            var clearItem = new MenuItem
            {
                Header = "Limpiar recientes"
            };
            clearItem.Click += (_, _) =>
            {
                RecentDocumentsService.Clear(_runtimeSettings.UserSettingsPath);
                _recentDocuments.Clear();
                RenderRecentDocumentsMenu();
                UpdateWindowState("Documentos recientes limpiados");
            };
            items.Add(clearItem);
        }

        RecentFilesMenu.ItemsSource = items;
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
        if (document.IsDebugging)
        {
            ApplyDebugStepResult(document, document.Interpreter.ContinueDebug(input));
        }
        else
        {
            document.LastExecutionResult = document.Interpreter.Continue(input);
            ShowExecutionResult(document.LastExecutionResult);
        }
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
            _currentDocument.Diagnostics = ParseDiagnostics(result.Diagnostics).ToList();
            _currentDocument.VariablesText = variablesText;
            _currentDocument.LastExecutionResult = result;
            _currentDocument.DiagnosticLines = _currentDocument.Diagnostics.Select(diagnostic => diagnostic.Line).ToHashSet();
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
            if (result.StoppedByUser)
            {
                UpdateWindowState("Ejecucion detenida");
            }
            else
            {
                UpdateWindowState(result.Success ? "Ejecucion completada" : "Ejecucion con diagnosticos");
            }
        }
    }

    private void StartDebugSession()
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
            ClearExecutionViewForValidationFailure(document);
            _showDiagnostics = true;
            UpdateDiagnosticUnderlines();
            UpdateOutputPanelView();
            UpdateWindowState("Corrige los errores antes de depurar");
            return;
        }

        if (document.Diagnostics.Count > 0)
        {
            document.IsDebugging = false;
            document.DebugLine = null;
            HighlightDebugLine(null);
            _showDiagnostics = true;
            UpdateDiagnosticUnderlines();
            UpdateOutputPanelView();
            UpdateExecutionControls();
            UpdateWindowState("No se puede depurar mientras existan errores");
            return;
        }

        document.IsDebugging = true;
        UpdateExecutionControls();
        var step = document.Interpreter.StartDebug(document.Text);
        ApplyDebugStepResult(document, step);
        if (step.Execution.Diagnostics.Count > 0)
        {
            document.IsDebugging = false;
            _showDiagnostics = true;
            HighlightDebugLine(null);
            UpdateOutputPanelView();
            UpdateExecutionControls();
            UpdateWindowState("No se inicio depuracion: hay errores");
            return;
        }

        UpdateExecutionControls();
        UpdateWindowState("Depuracion iniciada - F10 para avanzar");
    }

    private void StepDebugSession()
    {
        var document = _currentDocument;
        if (document is null)
        {
            return;
        }

        if (!document.IsDebugging)
        {
            StartDebugSession();
            return;
        }

        ApplyDebugStepResult(document, document.Interpreter.StepDebug());
    }

    private void ApplyDebugStepResult(OpenDocument document, DebugStepResult step)
    {
        document.IsDebugging = !step.IsFinished;
        document.DebugLine = step.CurrentLine;
        ShowExecutionResult(step.Execution);
        HighlightDebugLine(document.DebugLine);
        UpdateExecutionControls();

        if (step.Execution.WaitingForInput)
        {
            UpdateWindowState($"Depuracion esperando entrada: {step.Execution.InputVariable}");
            return;
        }

        UpdateWindowState(step.IsFinished ? "Depuracion completada" : $"Depuracion en linea {step.CurrentLine}");
    }

    private void StopDebug(OpenDocument document)
    {
        document.IsDebugging = false;
        document.DebugLine = null;
        HighlightDebugLine(null);
        HideConsoleInput();
        UpdateExecutionControls();
    }

    private void HighlightDebugLine(int? lineNumber)
    {
        _debugLineRenderer?.SetLine(lineNumber);
        if (lineNumber is not { } line || EditorTextBox.Document is null || line > EditorTextBox.Document.LineCount)
        {
            EditorTextBox.TextArea.TextView.Redraw();
            return;
        }

        var documentLine = EditorTextBox.Document.GetLineByNumber(line);
        EditorTextBox.Select(documentLine.Offset, Math.Max(1, documentLine.Length));
        EditorTextBox.CaretOffset = documentLine.Offset;
        EditorTextBox.ScrollToLine(line);
        EditorTextBox.TextArea.TextView.Redraw();
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
        var palette = _runtimeSettings.DarkSyntaxTheme.ToPalette();
        _colorizer = new PseudoCodeColorizer(_language, palette);
        EditorTextBox.TextArea.TextView.LineTransformers.Add(_colorizer);
        _diagnosticUnderlineRenderer = new DiagnosticUnderlineRenderer(palette.DiagnosticUnderlineBrush);
        EditorTextBox.TextArea.TextView.BackgroundRenderers.Add(_diagnosticUnderlineRenderer);
        _debugLineRenderer = new DebugLineRenderer(BuildDebugLineBrush(palette));
        EditorTextBox.TextArea.TextView.BackgroundRenderers.Add(_debugLineRenderer);
        SetEditorFontSize(_runtimeSettings.EditorFontSize, persist: false, updateStatus: false);
        ApplyEditorTheme(DarkTheme, palette);
        ConfigureEditorContextMenu();
        EditorTextBox.TextArea.TextEntered += Editor_TextEntered;
        EditorTextBox.TextArea.TextEntering += Editor_TextEntering;
        EditorTextBox.TextArea.AddHandler(
            InputElement.KeyDownEvent,
            Editor_KeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        EditorTextBox.AddHandler(
            InputElement.PointerWheelChangedEvent,
            Editor_PointerWheelChanged,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private void ConfigureEditorContextMenu()
    {
        EditorTextBox.ContextMenu = null;
        EditorTextBox.TextArea.ContextMenu = null;
        EditorTextBox.TextArea.TextView.ContextMenu = null;
    }

    private void MoveCaretToRightClickPosition(PointerEventArgs e)
    {
        var document = EditorTextBox.Document;
        if (document is null)
        {
            LogContextMenuDiagnostic("caret-move skipped document-null");
            return;
        }

        var textView = EditorTextBox.TextArea.TextView;
        var pointInTextView = e.GetPosition(textView);
        var viewPosition = textView.GetPositionFloor(pointInTextView);
        if (viewPosition is null)
        {
            LogContextMenuDiagnostic(
                $"caret-move skipped no-view-position text-view={FormatPoint(pointInTextView)} text-area={FormatPoint(e.GetPosition(EditorTextBox.TextArea))} editor={FormatPoint(e.GetPosition(EditorTextBox))}");
            return;
        }

        var offset = document.GetOffset(viewPosition.Value.Location);
        var safeOffset = Math.Clamp(offset, 0, document.TextLength);
        EditorTextBox.CaretOffset = safeOffset;
        EditorTextBox.Select(safeOffset, 0);
        LogContextMenuDiagnostic(
            $"caret-move offset={safeOffset} line={document.GetLineByOffset(safeOffset).LineNumber} text-view={FormatPoint(pointInTextView)}");
    }

    private void ToggleCustomEditorContextMenu(PointerEventArgs e)
    {
        if (EditorContextMenuPopup.IsVisible)
        {
            LogContextMenuDiagnostic("toggle-menu closing existing popup");
            HideCustomEditorContextMenu();
            return;
        }

        var editorPoint = e.GetPosition(EditorTextBox);
        var editorOrigin = EditorTextBox.TranslatePoint(new Point(0, 0), EditorContextMenuOverlay) ?? default;
        var overlayPoint = new Point(editorOrigin.X + editorPoint.X, editorOrigin.Y + editorPoint.Y);
        _lastContextMenuOpenPoint = overlayPoint;
        LogContextMenuDiagnostic(
            $"toggle-menu opening editor={FormatPoint(editorPoint)} editor-origin-in-overlay={FormatPoint(editorOrigin)} overlay={FormatPoint(overlayPoint)}");
        ShowCustomEditorContextMenu(overlayPoint);
    }

    private void ShowCustomEditorContextMenu(Point point)
    {
        EditorTextBox.IsHitTestVisible = false;
        EditorContextMenuOverlay.IsVisible = true;
        EditorContextMenuPopup.IsVisible = true;
        PositionCustomEditorContextMenu(point, deferIfNeeded: true);
    }

    private void HideCustomEditorContextMenu()
    {
        _lastContextMenuOpenPoint = null;
        EditorTextBox.IsHitTestVisible = true;
        EditorTextBox.Focusable = true;
        EditorContextMenuPopup.IsVisible = false;
        EditorContextMenuOverlay.IsVisible = false;
        LogContextMenuDiagnostic("hide-menu");
    }

    private void EditorContextMenuOverlay_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source == EditorContextMenuOverlay)
        {
            HideCustomEditorContextMenu();
            e.Handled = true;
        }
    }

    private void CloseCustomEditorContextMenuAfterAction()
    {
        HideCustomEditorContextMenu();
    }

    private void PositionCustomEditorContextMenu(Point point, bool deferIfNeeded)
    {
        EditorContextMenuPopup.Measure(Bounds.Size);
        var popupSize = EditorContextMenuPopup.DesiredSize;
        var overlayWidth = EditorContextMenuOverlay.Bounds.Width;
        var overlayHeight = EditorContextMenuOverlay.Bounds.Height;

        if (deferIfNeeded && (overlayWidth <= 0 || overlayHeight <= 0))
        {
            LogContextMenuDiagnostic(
                $"show-menu deferred requested={FormatPoint(point)} overlay-bounds={FormatRect(new Rect(EditorContextMenuOverlay.Bounds.Position, EditorContextMenuOverlay.Bounds.Size))}");
            Dispatcher.UIThread.Post(() => PositionCustomEditorContextMenu(point, deferIfNeeded: false), DispatcherPriority.Render);
            return;
        }

        var maxX = Math.Max(0, overlayWidth - popupSize.Width - 4);
        var maxY = Math.Max(0, overlayHeight - popupSize.Height - 4);
        var x = Math.Clamp(point.X, 0, maxX);
        var y = Math.Clamp(point.Y, 0, maxY);

        Canvas.SetLeft(EditorContextMenuPopup, x);
        Canvas.SetTop(EditorContextMenuPopup, y);
        LogContextMenuDiagnostic(
            $"show-menu requested={FormatPoint(point)} final={FormatPoint(new Point(x, y))} overlay-bounds={FormatRect(new Rect(EditorContextMenuOverlay.Bounds.Position, EditorContextMenuOverlay.Bounds.Size))} popup-size={popupSize.Width:0}x{popupSize.Height:0}");
        Dispatcher.UIThread.Post(() =>
        {
            EditorTextBox.Focusable = false;
            FocusContextMenuItemAtOpenPoint();
            LogContextMenuDiagnostic(
                $"show-menu post-focus popup-visible={EditorContextMenuPopup.IsVisible} overlay-visible={EditorContextMenuOverlay.IsVisible} first-item-focused={EditorContextMenuFirstItem.IsFocused} popup-bounds-in-window={FormatRect(GetBoundsInWindow(EditorContextMenuPopup))}");
        }, DispatcherPriority.Input);
    }

    private void FocusContextMenuItemAtOpenPoint()
    {
        var target = GetContextMenuItemAtOpenPoint() ?? EditorContextMenuFirstItem;
        target.Focus();
        LogContextMenuDiagnostic($"focus-menu-item target={DescribeControl(target)}");
    }

    private Button? GetContextMenuItemAtOpenPoint()
    {
        if (_lastContextMenuOpenPoint is not { } openPoint)
        {
            return null;
        }

        var popupLeft = Canvas.GetLeft(EditorContextMenuPopup);
        var popupTop = Canvas.GetTop(EditorContextMenuPopup);
        if (double.IsNaN(popupLeft) || double.IsNaN(popupTop))
        {
            return null;
        }

        var pointInPopup = new Point(openPoint.X - popupLeft, openPoint.Y - popupTop);
        foreach (var item in EnumerateContextMenuItems())
        {
            var itemRect = new Rect(item.Bounds.Position, item.Bounds.Size);
            if (itemRect.Contains(pointInPopup))
            {
                return item;
            }
        }

        return null;
    }

    private IEnumerable<Button> EnumerateContextMenuItems()
    {
        yield return EditorContextMenuFirstItem;
        yield return EditorContextMenuCutItem;
        yield return EditorContextMenuPasteItem;
        yield return EditorContextMenuDuplicateItem;
        yield return EditorContextMenuToggleCommentItem;
        yield return EditorContextMenuFormatItem;
    }

    private Button? GetContextMenuItemAtWindowPoint(Point windowPoint)
    {
        foreach (var item in EnumerateContextMenuItems())
        {
            if (GetBoundsInWindow(item).Contains(windowPoint))
            {
                return item;
            }
        }

        return null;
    }

    private void ExecuteContextMenuItem(Button item)
    {
        if (ReferenceEquals(item, EditorContextMenuFirstItem))
        {
            Copy_Click(item, new RoutedEventArgs());
            return;
        }

        if (ReferenceEquals(item, EditorContextMenuCutItem))
        {
            Cut_Click(item, new RoutedEventArgs());
            return;
        }

        if (ReferenceEquals(item, EditorContextMenuPasteItem))
        {
            Paste_Click(item, new RoutedEventArgs());
            return;
        }

        if (ReferenceEquals(item, EditorContextMenuDuplicateItem))
        {
            DuplicateLine_Click(item, new RoutedEventArgs());
            return;
        }

        if (ReferenceEquals(item, EditorContextMenuToggleCommentItem))
        {
            ToggleComment_Click(item, new RoutedEventArgs());
            return;
        }

        if (ReferenceEquals(item, EditorContextMenuFormatItem))
        {
            FormatDocument_Click(item, new RoutedEventArgs());
        }
    }

    private void LogContextMenuDiagnostic(string message)
    {
        if (!_runtimeSettings.Diagnostic.Enabled)
        {
            return;
        }

        _contextMenuDiagnosticsLines.Enqueue($"{DateTime.Now:HH:mm:ss.fff}  {message}");
        while (_contextMenuDiagnosticsLines.Count > ContextMenuDiagnosticsLimit)
        {
            _contextMenuDiagnosticsLines.Dequeue();
        }

        UpdateDiagnosticsTextBox();
    }

    private void EnqueueMouseDiagnostic(string line)
    {
        _mouseClickDiagnosticsLines.Enqueue(line);
        while (_mouseClickDiagnosticsLines.Count > MouseClickDiagnosticsLimit)
        {
            _mouseClickDiagnosticsLines.Dequeue();
        }

        UpdateDiagnosticsTextBox();
    }

    private void UpdateDiagnosticsTextBox()
    {
        var lines = _contextMenuDiagnosticsLines
            .Concat(_mouseClickDiagnosticsLines)
            .TakeLast(ContextMenuDiagnosticsLimit + MouseClickDiagnosticsLimit)
            .ToArray();
        MouseClickDiagnosticsTextBox.Text = lines.Length == 0
            ? BuildDiagnosticsPanelPlaceholder()
            : string.Join(Environment.NewLine, lines.Reverse());
    }

    private string BuildDiagnosticsPanelPlaceholder() =>
        "Modo diagnostico activo. Haz click derecho dentro del editor y luego pulsa 'Copiar reporte'.";

    private string BuildInputDiagnosticsReport()
    {
        var builder = new StringBuilder();
        builder.AppendLine("=== PseudoCode Input Diagnostics ===");
        builder.AppendLine($"TimeUtc: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC");
        builder.AppendLine($"OS: {Environment.OSVersion}");
        builder.AppendLine($"Framework: {Environment.Version}");
        builder.AppendLine($"Diagnostic.Enabled: {_runtimeSettings.Diagnostic.Enabled}");
        builder.AppendLine($"Diagnostic.Clicks: {_runtimeSettings.Diagnostic.Features.Clicks}");
        builder.AppendLine($"Diagnostic.Keystrokes: {_runtimeSettings.Diagnostic.Features.Keystrokes}");
        builder.AppendLine($"Diagnostic.MousePosition: {_runtimeSettings.Diagnostic.Features.MousePosition}");
        builder.AppendLine($"Window.Bounds: {FormatRect(Bounds)}");
        builder.AppendLine($"Editor.BoundsLocal: {FormatRect(new Rect(EditorTextBox.Bounds.Position, EditorTextBox.Bounds.Size))}");
        builder.AppendLine($"Editor.BoundsInWindow: {FormatRect(GetBoundsInWindow(EditorTextBox))}");
        builder.AppendLine($"TextArea.BoundsInWindow: {FormatRect(GetBoundsInWindow(EditorTextBox.TextArea))}");
        builder.AppendLine($"TextView.BoundsInWindow: {FormatRect(GetBoundsInWindow(EditorTextBox.TextArea.TextView))}");
        builder.AppendLine($"Overlay.Visible: {EditorContextMenuOverlay.IsVisible}");
        builder.AppendLine($"Overlay.Bounds: {FormatRect(new Rect(EditorContextMenuOverlay.Bounds.Position, EditorContextMenuOverlay.Bounds.Size))}");
        builder.AppendLine($"Overlay.BoundsInWindow: {FormatRect(GetBoundsInWindow(EditorContextMenuOverlay))}");
        builder.AppendLine($"Popup.Visible: {EditorContextMenuPopup.IsVisible}");
        builder.AppendLine($"Popup.Bounds: {FormatRect(new Rect(EditorContextMenuPopup.Bounds.Position, EditorContextMenuPopup.Bounds.Size))}");
        builder.AppendLine($"Popup.BoundsInWindow: {FormatRect(GetBoundsInWindow(EditorContextMenuPopup))}");
        builder.AppendLine($"Popup.CanvasLeft: {Canvas.GetLeft(EditorContextMenuPopup):0.##}");
        builder.AppendLine($"Popup.CanvasTop: {Canvas.GetTop(EditorContextMenuPopup):0.##}");
        builder.AppendLine($"Popup.Focus: {EditorContextMenuPopup.IsFocused}");
        builder.AppendLine($"FirstItem.Focus: {EditorContextMenuFirstItem.IsFocused}");
        builder.AppendLine($"EditorOriginInOverlay: {FormatPoint(EditorTextBox.TranslatePoint(new Point(0, 0), EditorContextMenuOverlay) ?? default)}");
        builder.AppendLine($"EditorOriginInWindow: {FormatPoint(EditorTextBox.TranslatePoint(new Point(0, 0), this) ?? default)}");
        builder.AppendLine($"Popup.ZIndex: {EditorContextMenuPopup.ZIndex}");
        builder.AppendLine($"Overlay.ZIndex: {EditorContextMenuOverlay.ZIndex}");

        if (EditorTextBox.Document is not null)
        {
            var caretOffset = Math.Clamp(EditorTextBox.CaretOffset, 0, EditorTextBox.Document.TextLength);
            var caretLine = EditorTextBox.Document.GetLineByOffset(caretOffset).LineNumber;
            builder.AppendLine($"Caret.Offset: {caretOffset}");
            builder.AppendLine($"Caret.Line: {caretLine}");
            builder.AppendLine($"Document.LineCount: {EditorTextBox.Document.LineCount}");
        }

        builder.AppendLine();
        builder.AppendLine("LastContextMenuEvents:");
        AppendDiagnosticsSection(builder, _contextMenuDiagnosticsLines);
        builder.AppendLine();
        builder.AppendLine("LastMouseAndKeyEvents:");
        AppendDiagnosticsSection(builder, _mouseClickDiagnosticsLines);
        return builder.ToString().TrimEnd();
    }

    private static void AppendDiagnosticsSection(StringBuilder builder, IEnumerable<string> lines)
    {
        var any = false;
        foreach (var line in lines)
        {
            builder.AppendLine(line);
            any = true;
        }

        if (!any)
        {
            builder.AppendLine("(empty)");
        }
    }

    private Rect GetBoundsInWindow(Control control)
    {
        var origin = control.TranslatePoint(new Point(0, 0), this);
        return origin is null
            ? default
            : new Rect(origin.Value, control.Bounds.Size);
    }

    private static string FormatRect(Rect rect) =>
        $"x={rect.X:0.##} y={rect.Y:0.##} w={rect.Width:0.##} h={rect.Height:0.##}";

    private static string FormatPoint(Point point) =>
        $"x={point.X:0.##} y={point.Y:0.##}";

    private static string DescribeControl(Control? control) =>
        control?.Name ?? control?.GetType().Name ?? "null";

    private static string DescribeControlPath(Control? control)
    {
        if (control is null)
        {
            return "null";
        }

        var parts = new List<string>(8);
        var current = control;
        while (current is not null && parts.Count < 8)
        {
            parts.Add(DescribeControl(current));
            current = current.Parent as Control;
        }

        return string.Join(" <- ", parts);
    }

    private void LogPointerContextSnapshot(string stage, PointerEventArgs e, Control? sourceControl, bool insideEditor)
    {
        var inEditor = e.GetPosition(EditorTextBox);
        var inTextArea = e.GetPosition(EditorTextBox.TextArea);
        var inTextView = e.GetPosition(EditorTextBox.TextArea.TextView);
        var inOverlay = e.GetPosition(EditorContextMenuOverlay);
        LogContextMenuDiagnostic(
            $"{stage} snapshot inside-editor={insideEditor} source={DescribeControl(sourceControl)} editor={FormatPoint(inEditor)} text-area={FormatPoint(inTextArea)} text-view={FormatPoint(inTextView)} overlay={FormatPoint(inOverlay)}");
    }

    private bool IsPointInsideEditor(Point windowPoint)
    {
        var editorOrigin = EditorTextBox.TranslatePoint(new Point(0, 0), this);
        if (editorOrigin is null)
        {
            return false;
        }

        var editorRect = new Rect(editorOrigin.Value, EditorTextBox.Bounds.Size);
        return editorRect.Contains(windowPoint);
    }

    private bool IsContextMenuSource(Control? control)
    {
        var current = control;
        while (current is not null)
        {
            if (ReferenceEquals(current, EditorContextMenuPopup) || ReferenceEquals(current, EditorContextMenuOverlay))
            {
                return true;
            }

            current = current.Parent as Control;
        }

        return false;
    }

    private bool IsPointInsideContextMenuPopup(Point windowPoint)
    {
        if (!EditorContextMenuPopup.IsVisible)
        {
            return false;
        }

        return GetBoundsInWindow(EditorContextMenuPopup).Contains(windowPoint);
    }

    private void Editor_TextEntered(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Text))
        {
            return;
        }

        if (IsCaretInsideCommentOrString())
        {
            _completionWindow?.Close();
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

    private async void Editor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (IsSaveShortcut(e))
        {
            e.Handled = true;
            await SaveCurrentFileAsync();
            return;
        }

        if (TryHandleFormatShortcut(e))
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            if (IsToggleCommentShortcut(e))
            {
                ToggleLineComments();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.L)
            {
                e.Handled = true;
                await ShowGoToLineDialogAsync();
                return;
            }

            if (e.Key == Key.C)
            {
                e.Handled = true;
                await CopySelectionAsync();
                return;
            }

            if (e.Key == Key.X)
            {
                e.Handled = true;
                await CutSelectionAsync();
                return;
            }

            if (e.Key == Key.V)
            {
                e.Handled = true;
                await PasteClipboardAsync();
                return;
            }

            if (e.Key == Key.D && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                DuplicateCurrentLine();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ShowCompletion(force: true);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Tab)
        {
            if (_completionWindow is not null)
            {
                _completionWindow.CompletionList.RequestInsertion(e);
                e.Handled = true;
                return;
            }

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

    private async Task ShowGoToLineDialogAsync()
    {
        if (EditorTextBox.Document is null || EditorTextBox.Document.LineCount == 0)
        {
            UpdateWindowState("No hay lineas para navegar");
            return;
        }

        var currentLine = EditorTextBox.Document.GetLineByOffset(GetSafeCaretOffset(EditorTextBox.Document)).LineNumber;
        var maxLine = EditorTextBox.Document.LineCount;
        var requestedLine = await AskGoToLineAsync(currentLine, maxLine);
        if (requestedLine is not { } line)
        {
            return;
        }

        GoToLine(line);
    }

    private async Task<int?> AskGoToLineAsync(int currentLine, int maxLine)
    {
        var window = new Window
        {
            Title = "Ir a linea",
            Width = 360,
            Height = 190,
            MinWidth = 340,
            MinHeight = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("PanelBackground")
        };

        var input = new TextBox
        {
            Text = currentLine.ToString(),
            Background = Brush("InsetBackground"),
            Foreground = Brush("TextPrimary"),
            BorderBrush = Brush("BorderBrushMuted"),
            FontFamily = new FontFamily("Cascadia Code,Consolas,monospace")
        };
        var status = new TextBlock
        {
            Foreground = Brush("TextSecondary"),
            Text = $"Linea actual: {currentLine}. Rango valido: 1..{maxLine}."
        };

        void Accept()
        {
            if (!int.TryParse(input.Text, out var line))
            {
                status.Text = "Escribe un numero valido.";
                return;
            }

            if (line < 1 || line > maxLine)
            {
                status.Text = $"Linea fuera de rango. Usa 1..{maxLine}.";
                return;
            }

            window.Close(line);
        }

        var goButton = new Button
        {
            Content = "Ir",
            Classes = { "command" },
            MinWidth = 80
        };
        goButton.Click += (_, _) => Accept();

        var cancelButton = new Button
        {
            Content = "Cancelar",
            Classes = { "command" },
            MinWidth = 80
        };
        cancelButton.Click += (_, _) => window.Close(null);

        input.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                Accept();
                args.Handled = true;
            }
            else if (args.Key == Key.Escape)
            {
                window.Close(null);
                args.Handled = true;
            }
        };

        window.Opened += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };

        window.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "Ir a linea",
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brush("TextPrimary")
                },
                input,
                status,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children =
                    {
                        goButton,
                        cancelButton
                    }
                }
            }
        };

        var result = await window.ShowDialog<int?>(this);
        return result;
    }

    private void GoToLine(int requestedLine)
    {
        if (EditorTextBox.Document is null || EditorTextBox.Document.LineCount == 0)
        {
            return;
        }

        var line = Math.Clamp(requestedLine, 1, EditorTextBox.Document.LineCount);
        var documentLine = EditorTextBox.Document.GetLineByNumber(line);
        EditorTextBox.Focus();
        EditorTextBox.Select(documentLine.Offset, Math.Max(1, documentLine.Length));
        EditorTextBox.CaretOffset = documentLine.Offset;
        EditorTextBox.ScrollToLine(line);
        UpdateWindowState($"Linea {line}");
    }

    private void Editor_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        e.Handled = true;

        if (e.Delta.Y > 0)
        {
            ZoomEditor(1);
        }
        else if (e.Delta.Y < 0)
        {
            ZoomEditor(-1);
        }
    }

    private void ZoomInterface(int direction)
    {
        SetInterfaceScale(_interfaceScale + direction * InterfaceScaleStep);
    }

    private void SetInterfaceScale(double scale, bool persist = true, bool updateStatus = true)
    {
        var nextScale = Math.Clamp(scale, MinInterfaceScale, MaxInterfaceScale);
        var changed = Math.Abs(_interfaceScale - nextScale) > 0.001;
        _interfaceScale = nextScale;
        AppScaleHost.LayoutTransform = new ScaleTransform(_interfaceScale, _interfaceScale);
        if (persist && changed)
        {
            AppSettingsService.SaveInterfaceScale(_runtimeSettings.UserSettingsPath, nextScale);
        }

        if (updateStatus)
        {
            UpdateWindowState($"Zoom interfaz: {_interfaceScale:P0}");
        }
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

    private static bool IsSaveShortcut(KeyEventArgs e)
    {
        return e.Key == Key.G
            && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && !e.KeyModifiers.HasFlag(KeyModifiers.Alt);
    }

    private static bool IsToggleCommentShortcut(KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            return false;
        }

        var key = e.Key.ToString();
        var keySymbol = e.KeySymbol ?? string.Empty;
        return key is "Oem2" or "Divide" || keySymbol == "/";
    }

    private bool TryHandleFormatShortcut(KeyEventArgs e)
    {
        var hasControl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (!hasControl)
        {
            _formatChordArmed = false;
            return false;
        }

        if (e.Key == Key.K)
        {
            _formatChordArmed = true;
            UpdateWindowState("Atajo de formato iniciado: presiona Ctrl+D");
            e.Handled = true;
            return true;
        }

        if (_formatChordArmed && e.Key == Key.D)
        {
            _formatChordArmed = false;
            FormatCurrentDocument();
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _formatChordArmed = false;
            FormatCurrentDocument();
            e.Handled = true;
            return true;
        }

        _formatChordArmed = false;
        return false;
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

    private void SetEditorFontSize(double fontSize, bool persist = true, bool updateStatus = true)
    {
        var nextFontSize = Math.Clamp(fontSize, MinEditorFontSize, MaxEditorFontSize);
        var changed = Math.Abs(EditorTextBox.FontSize - nextFontSize) > 0.001;
        EditorTextBox.FontSize = nextFontSize;
        EditorTextBox.TextArea.TextView.Redraw();
        if (persist && changed)
        {
            AppSettingsService.SaveEditorFontSize(_runtimeSettings.UserSettingsPath, nextFontSize);
        }

        if (updateStatus)
        {
            UpdateWindowState($"Zoom editor: {EditorTextBox.FontSize:0}px");
        }
    }

    private void ShowCompletion(bool force = false)
    {
        if (IsCaretInsideCommentOrString())
        {
            _completionWindow?.Close();
            return;
        }

        var prefix = GetCurrentWord();
        if (!force && prefix.Length < 2)
        {
            return;
        }

        var completionItems = BuildCompletionItems();
        var matches = completionItems
            .Where(item => item.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Text.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(item => item.Description.StartsWith("Variable", StringComparison.OrdinalIgnoreCase))
            .ThenBy(item => item.Text)
            .ToArray();

        if (matches.Length == 0 && !force)
        {
            return;
        }

        _completionWindow?.Close();
        _completionWindow = new CompletionWindow(EditorTextBox.TextArea)
        {
            Width = 300,
            Height = 180,
            Opacity = 0.92
        };
        _completionWindow.Closed += (_, _) => _completionWindow = null;

        var data = _completionWindow.CompletionList.CompletionData;
        foreach (var item in matches.Length == 0 ? completionItems : matches)
        {
            data.Add(new PseudoCompletionData(item));
        }

        _completionWindow.Show();
    }

    private CommandInfo[] BuildCompletionItems()
    {
        var merged = new Dictionary<string, CommandInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in _completionItems)
        {
            merged[item.Text] = item;
        }

        var source = EditorTextBox.Document?.Text ?? string.Empty;
        foreach (var variable in ExtractVariables(source))
        {
            merged.TryAdd(variable, new CommandInfo(variable, variable, "Variable del documento actual."));
        }

        return merged.Values.ToArray();
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

    private bool IsCaretInsideCommentOrString()
    {
        var document = EditorTextBox.Document;
        var offset = GetSafeCaretOffset(document);
        var line = document.GetLineByOffset(offset);
        var lineOffset = offset - line.Offset;
        var lineText = document.GetText(line);

        var inString = false;
        for (var index = 0; index < Math.Min(lineOffset, lineText.Length); index++)
        {
            if (lineText[index] == '"')
            {
                inString = !inString;
                continue;
            }

            if (!inString && index + 1 < lineText.Length && lineText[index] == '/' && lineText[index + 1] == '/')
            {
                return true;
            }
        }

        return inString;
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

        if (ShouldOutdentCurrentLineOnEnter(trimmed, currentIndent, line.LineNumber, document))
        {
            nextIndent = currentIndent.Length >= 4 ? currentIndent[4..] : string.Empty;
            document.Replace(line.Offset, currentIndent.Length, nextIndent);
            var newOffset = Math.Max(line.Offset + nextIndent.Length + trimmed.Length, offset - (currentIndent.Length - nextIndent.Length));
            EditorTextBox.CaretOffset = Math.Clamp(newOffset, 0, document.TextLength);
        }

        if (StartsLogicalBlock(trimmed) || IsMidBlock(trimmed) || IsSwitchCaseLabel(trimmed))
        {
            nextIndent += "    ";
        }

        InsertAtCaret(Environment.NewLine + nextIndent);
    }

    private bool ShouldOutdentCurrentLineOnEnter(string trimmed, string currentIndent, int lineNumber, TextDocument document)
    {
        if (currentIndent.Length < 4 || trimmed.Length == 0)
        {
            return false;
        }

        if (!IsClosingBlock(trimmed) && !IsMidBlock(trimmed) && !IsSwitchCaseLabel(trimmed))
        {
            return false;
        }

        var previousIndentLength = GetPreviousNonEmptyIndentLength(lineNumber, document);
        return previousIndentLength >= 0 && currentIndent.Length >= previousIndentLength;
    }

    private static int GetPreviousNonEmptyIndentLength(int lineNumber, TextDocument document)
    {
        for (var index = lineNumber - 1; index >= 1; index--)
        {
            var line = document.GetLineByNumber(index);
            var text = document.GetText(line.Offset, line.Length);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            return Regex.Match(text, @"^\s*").Value.Length;
        }

        return 0;
    }

    private void FormatCurrentDocument()
    {
        var document = EditorTextBox.Document;
        if (document is null)
        {
            return;
        }

        var caretLine = document.GetLineByOffset(GetSafeCaretOffset(document)).LineNumber;
        var formatted = FormatPseudoCode(EditorTextBox.Text ?? string.Empty);
        if (string.Equals(EditorTextBox.Text, formatted, StringComparison.Ordinal))
        {
            UpdateWindowState("El documento ya esta formateado");
            return;
        }

        EditorTextBox.Text = formatted;
        if (_currentDocument is not null)
        {
            _currentDocument.Text = formatted;
            _currentDocument.HasUnsavedChanges = true;
        }

        var targetLine = Math.Clamp(caretLine, 1, Math.Max(1, EditorTextBox.Document.LineCount));
        var targetDocumentLine = EditorTextBox.Document.GetLineByNumber(targetLine);
        EditorTextBox.CaretOffset = targetDocumentLine.Offset;
        EditorTextBox.ScrollToLine(targetLine);
        EditorTextBox.Focus();
        if (_currentDocument is not null)
        {
            UpdateLiveSyntaxDiagnostics(_currentDocument);
        }
        RenderOpenDocuments();
        UpdateWindowState("Documento formateado");
    }

    private string FormatPseudoCode(string text)
    {
        var usesCrLf = text.Contains("\r\n", StringComparison.Ordinal);
        var newline = usesCrLf ? "\r\n" : "\n";
        var endsWithNewline = text.EndsWith('\n') || text.EndsWith('\r');
        var lines = Regex.Split(text, "\r\n|\n|\r");
        var formatted = new List<string>(lines.Length);
        var indentLevel = 0;

        var lineCount = endsWithNewline && lines.Length > 0 ? lines.Length - 1 : lines.Length;
        for (var index = 0; index < lineCount; index++)
        {
            var rawLine = lines[index];
            var trimmed = rawLine.Trim();
            if (trimmed.Length == 0)
            {
                formatted.Add(string.Empty);
                continue;
            }

            if (IsClosingBlock(trimmed) || IsMidBlock(trimmed))
            {
                indentLevel = Math.Max(0, indentLevel - 1);
            }

            var lineIndent = indentLevel;
            if (IsSwitchCaseLabel(trimmed))
            {
                lineIndent = indentLevel > 1 ? indentLevel - 1 : indentLevel;
            }

            formatted.Add(new string(' ', lineIndent * 4) + trimmed);

            if (StartsLogicalBlock(trimmed) || IsMidBlock(trimmed))
            {
                indentLevel = lineIndent + 1;
            }
            else if (IsSwitchCaseLabel(trimmed))
            {
                indentLevel = lineIndent + 1;
            }
        }

        var result = string.Join(newline, formatted);
        return endsWithNewline ? result + newline : result;
    }

    private void InsertAtCaret(string text)
    {
        var document = EditorTextBox.Document;
        var offset = GetSafeCaretOffset(document);

        document.Insert(offset, text);
        EditorTextBox.CaretOffset = Math.Min(offset + text.Length, document.TextLength);
    }

    private async Task CopySelectionAsync()
    {
        var document = EditorTextBox.Document;
        if (Clipboard is null || document is null)
        {
            return;
        }

        var selectedText = EditorTextBox.SelectedText;
        _copiedWholeLine = string.IsNullOrEmpty(selectedText);
        if (_copiedWholeLine)
        {
            selectedText = GetCurrentLineClipboardText(document);
        }

        if (string.IsNullOrEmpty(selectedText))
        {
            return;
        }

        await Clipboard.SetTextAsync(selectedText);
        UpdateWindowState("Texto copiado");
    }

    private async Task CutSelectionAsync()
    {
        var document = EditorTextBox.Document;
        if (Clipboard is null || document is null)
        {
            return;
        }

        var selectionStart = EditorTextBox.SelectionStart;
        var selectionLength = EditorTextBox.SelectionLength;
        if (selectionLength > 0)
        {
            _copiedWholeLine = false;
            var selectedText = EditorTextBox.SelectedText;
            if (string.IsNullOrEmpty(selectedText))
            {
                return;
            }

            await Clipboard.SetTextAsync(selectedText);
            document.Remove(selectionStart, selectionLength);
            EditorTextBox.CaretOffset = selectionStart;
            EditorTextBox.Focus();
            UpdateWindowState("Texto cortado");
            return;
        }

        var lineRange = GetCurrentLineRangeForCut(document);
        if (lineRange.Length <= 0)
        {
            return;
        }

        _copiedWholeLine = true;
        var lineText = document.GetText(lineRange.Start, lineRange.Length);
        if (string.IsNullOrEmpty(lineText))
        {
            return;
        }

        await Clipboard.SetTextAsync(lineText);
        document.Remove(lineRange.Start, lineRange.Length);
        EditorTextBox.CaretOffset = Math.Clamp(lineRange.Start, 0, document.TextLength);
        EditorTextBox.Focus();
        UpdateWindowState("Texto cortado");
    }

    private async Task PasteClipboardAsync()
    {
        if (Clipboard is null || EditorTextBox.Document is null)
        {
            return;
        }

#pragma warning disable CS0618
        var text = await Clipboard.GetTextAsync();
#pragma warning restore CS0618
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (_copiedWholeLine && EditorTextBox.SelectionLength == 0 && HasAnyLineEnding(text))
        {
            PasteWholeLineBelow(text);
            UpdateWindowState("Linea pegada");
            return;
        }

        ReplaceSelection(text);
        UpdateWindowState("Texto pegado");
    }

    private void ReplaceSelection(string text)
    {
        var document = EditorTextBox.Document;
        if (document is null)
        {
            return;
        }

        var selectionStart = EditorTextBox.SelectionStart;
        var selectionLength = EditorTextBox.SelectionLength;
        if (selectionLength > 0)
        {
            document.Remove(selectionStart, selectionLength);
        }

        document.Insert(selectionStart, text);
        EditorTextBox.CaretOffset = Math.Min(selectionStart + text.Length, document.TextLength);
        EditorTextBox.Focus();
    }

    private void DuplicateCurrentLine()
    {
        var document = EditorTextBox.Document;
        if (document is null || document.LineCount == 0)
        {
            return;
        }

        var selectionStart = EditorTextBox.SelectionStart;
        var selectionLength = EditorTextBox.SelectionLength;
        if (selectionLength > 0)
        {
            var selectedText = document.GetText(selectionStart, selectionLength);
            if (selectedText.Length == 0)
            {
                return;
            }

            document.Insert(selectionStart + selectionLength, selectedText);
            EditorTextBox.Select(selectionStart + selectionLength, selectedText.Length);
            EditorTextBox.CaretOffset = selectionStart + selectionLength + selectedText.Length;
            EditorTextBox.Focus();
            UpdateWindowState("Seleccion duplicada");
            return;
        }

        var caretOffset = GetSafeCaretOffset(document);
        var payload = BuildDuplicateLinePayload(document);
        if (payload.Text.Length == 0)
        {
            return;
        }

        document.Insert(payload.InsertOffset, payload.Text);
        EditorTextBox.CaretOffset = Math.Clamp(caretOffset + payload.Text.Length, 0, document.TextLength);
        EditorTextBox.ScrollToLine(Math.Min(document.GetLineByOffset(EditorTextBox.CaretOffset).LineNumber, document.LineCount));
        EditorTextBox.Focus();
        UpdateWindowState("Linea duplicada");
    }

    private void ToggleLineComments()
    {
        var document = EditorTextBox.Document;
        if (document is null || document.LineCount == 0)
        {
            return;
        }

        var (startLine, endLine) = GetSelectedLineRange(document);
        if (startLine <= 0 || endLine <= 0)
        {
            return;
        }

        var allCommented = true;
        for (var lineNumber = startLine; lineNumber <= endLine; lineNumber++)
        {
            var line = document.GetLineByNumber(lineNumber);
            var text = document.GetText(line.Offset, line.Length);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var firstNonWhitespace = GetFirstNonWhitespaceIndex(text);
            if (firstNonWhitespace < 0 || !text[firstNonWhitespace..].StartsWith("//", StringComparison.Ordinal))
            {
                allCommented = false;
                break;
            }
        }

        for (var lineNumber = endLine; lineNumber >= startLine; lineNumber--)
        {
            var line = document.GetLineByNumber(lineNumber);
            var text = document.GetText(line.Offset, line.Length);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var firstNonWhitespace = GetFirstNonWhitespaceIndex(text);
            if (firstNonWhitespace < 0)
            {
                continue;
            }

            var insertOffset = line.Offset + firstNonWhitespace;
            if (allCommented)
            {
                if (text[firstNonWhitespace..].StartsWith("//", StringComparison.Ordinal))
                {
                    document.Remove(insertOffset, 2);
                }
            }
            else
            {
                document.Insert(insertOffset, "//");
            }
        }

        var targetLine = Math.Clamp(startLine, 1, document.LineCount);
        var targetDocumentLine = document.GetLineByNumber(targetLine);
        EditorTextBox.Select(targetDocumentLine.Offset, 0);
        EditorTextBox.CaretOffset = targetDocumentLine.Offset;
        EditorTextBox.ScrollToLine(targetLine);
        EditorTextBox.Focus();
        UpdateWindowState(allCommented ? "Lineas descomentadas" : "Lineas comentadas");
    }

    private (int StartLine, int EndLine) GetSelectedLineRange(TextDocument document)
    {
        if (document.LineCount == 0)
        {
            return (0, 0);
        }

        if (EditorTextBox.SelectionLength <= 0)
        {
            var lineNumber = document.GetLineByOffset(GetSafeCaretOffset(document)).LineNumber;
            return (lineNumber, lineNumber);
        }

        var selectionStart = Math.Clamp(EditorTextBox.SelectionStart, 0, document.TextLength);
        var selectionEnd = Math.Clamp(selectionStart + EditorTextBox.SelectionLength, 0, document.TextLength);
        var startLine = document.GetLineByOffset(selectionStart).LineNumber;
        var endOffset = selectionEnd;
        if (selectionEnd > selectionStart && selectionEnd > 0)
        {
            endOffset = selectionEnd - 1;
        }

        var endLine = document.GetLineByOffset(Math.Clamp(endOffset, 0, document.TextLength)).LineNumber;
        return (startLine, endLine);
    }

    private static int GetFirstNonWhitespaceIndex(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (!char.IsWhiteSpace(text[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private string GetCurrentLineClipboardText(TextDocument document)
    {
        if (document.LineCount == 0)
        {
            return string.Empty;
        }

        var caretOffset = GetSafeCaretOffset(document);
        var line = document.GetLineByOffset(caretOffset);
        var delimiterLength = GetTrailingDelimiterLength(document, line.EndOffset);
        if (delimiterLength > 0)
        {
            return document.GetText(line.Offset, line.Length + delimiterLength);
        }

        var lineText = document.GetText(line.Offset, line.Length);
        return lineText + DetectDocumentNewLine(document);
    }

    private (string Text, int InsertOffset) BuildDuplicateLinePayload(TextDocument document)
    {
        if (document.LineCount == 0)
        {
            return (string.Empty, 0);
        }

        var caretOffset = GetSafeCaretOffset(document);
        var line = document.GetLineByOffset(caretOffset);
        var delimiterLength = GetTrailingDelimiterLength(document, line.EndOffset);
        if (delimiterLength > 0)
        {
            var text = document.GetText(line.Offset, line.Length + delimiterLength);
            return (text, line.Offset + line.Length + delimiterLength);
        }

        var lineText = document.GetText(line.Offset, line.Length);
        return (DetectDocumentNewLine(document) + lineText, line.EndOffset);
    }

    private (int Start, int Length) GetCurrentLineRangeForCut(TextDocument document)
    {
        if (document.LineCount == 0)
        {
            return (0, 0);
        }

        var caretOffset = GetSafeCaretOffset(document);
        var line = document.GetLineByOffset(caretOffset);
        var delimiterLength = GetTrailingDelimiterLength(document, line.EndOffset);
        if (delimiterLength > 0)
        {
            return (line.Offset, line.Length + delimiterLength);
        }

        var leadingDelimiterLength = GetLeadingDelimiterLength(document, line.Offset);
        if (leadingDelimiterLength > 0)
        {
            return (line.Offset - leadingDelimiterLength, line.Length + leadingDelimiterLength);
        }

        return (line.Offset, line.Length);
    }

    private static int GetTrailingDelimiterLength(TextDocument document, int lineEndOffset)
    {
        if (lineEndOffset >= document.TextLength)
        {
            return 0;
        }

        var next = document.GetCharAt(lineEndOffset);
        if (next == '\r')
        {
            return lineEndOffset + 1 < document.TextLength && document.GetCharAt(lineEndOffset + 1) == '\n' ? 2 : 1;
        }

        return next == '\n' ? 1 : 0;
    }

    private static int GetLeadingDelimiterLength(TextDocument document, int lineOffset)
    {
        if (lineOffset <= 0)
        {
            return 0;
        }

        var previous = document.GetCharAt(lineOffset - 1);
        if (previous == '\n')
        {
            return lineOffset >= 2 && document.GetCharAt(lineOffset - 2) == '\r' ? 2 : 1;
        }

        return previous == '\r' ? 1 : 0;
    }

    private void PasteWholeLineBelow(string text)
    {
        var document = EditorTextBox.Document;
        if (document is null || document.LineCount == 0)
        {
            return;
        }

        var caretOffset = GetSafeCaretOffset(document);
        var line = document.GetLineByOffset(caretOffset);
        var trailingDelimiterLength = GetTrailingDelimiterLength(document, line.EndOffset);
        if (trailingDelimiterLength > 0)
        {
            var insertOffset = line.EndOffset + trailingDelimiterLength;
            document.Insert(insertOffset, text);
            EditorTextBox.CaretOffset = Math.Clamp(insertOffset + text.Length, 0, document.TextLength);
            EditorTextBox.Focus();
            return;
        }

        // Ultima linea sin salto final: inserta una nueva linea y luego el contenido pegado sin su EOL final.
        var normalized = RemoveFinalLineEnding(text);
        var prefix = DetectDocumentNewLine(document);
        var payload = prefix + normalized;
        document.Insert(line.EndOffset, payload);
        EditorTextBox.CaretOffset = Math.Clamp(line.EndOffset + payload.Length, 0, document.TextLength);
        EditorTextBox.Focus();
    }

    private static bool HasAnyLineEnding(string text) =>
        text.Contains('\n') || text.Contains('\r');

    private static string RemoveFinalLineEnding(string text)
    {
        if (text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return text[..^2];
        }

        if (text.EndsWith('\n') || text.EndsWith('\r'))
        {
            return text[..^1];
        }

        return text;
    }

    private static string DetectDocumentNewLine(TextDocument document)
    {
        var text = document.Text;
        if (text.Contains("\r\n", StringComparison.Ordinal))
        {
            return "\r\n";
        }

        if (text.Contains('\n'))
        {
            return "\n";
        }

        if (text.Contains('\r'))
        {
            return "\r";
        }

        return Environment.NewLine;
    }

    private int GetSafeCaretOffset(TextDocument document)
    {
        return Math.Clamp(EditorTextBox.CaretOffset, 0, document.TextLength);
    }

    private void Editor_DragOver(object? sender, DragEventArgs e)
    {
        try
        {
            var hasFiles = e.DataTransfer.TryGetFiles()?.Any() == true;
            var hasText = e.DataTransfer.Contains(DataFormat.Text);
            e.DragEffects = hasFiles ? DragDropEffects.Copy : hasText ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = hasFiles || hasText;
        }
        catch (Exception ex)
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            UpdateWindowState($"Drag cancelado: {ex.Message}");
        }
    }

    private async void Editor_Drop(object? sender, DragEventArgs e)
    {
        try
        {
            var file = e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().FirstOrDefault();
            if (file is not null)
            {
                await LoadFileAsync(file);
                e.Handled = true;
                return;
            }

            var text = e.DataTransfer.TryGetText();
            if (!string.IsNullOrEmpty(text))
            {
                InsertAtCaret(text);
                e.Handled = true;
                UpdateWindowState("Texto insertado");
            }
        }
        catch (Exception ex)
        {
            e.Handled = true;
            UpdateWindowState($"No pude soltar el contenido: {ex.Message}");
        }
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

    private void UpdateExecutionControls()
    {
        var isDebugging = _currentDocument?.IsDebugging == true;
        RunExecutionButton.IsEnabled = !_isRunningCode && !isDebugging && _currentDocument is not null;
        PauseExecutionButton.IsEnabled = _isRunningCode;
        StopExecutionButton.IsEnabled = _isRunningCode || isDebugging;
        TopExecutionControls.IsVisible = _isRunningCode || isDebugging;
        TopPauseExecutionButton.IsVisible = _isRunningCode;

        var pauseText = _isExecutionPaused ? "▶ Continuar" : "Ⅱ Pausa";
        PauseExecutionButton.Content = pauseText;
        TopPauseExecutionButton.Content = pauseText;
    }

    private void AddNewDocument()
    {
        var document = new OpenDocument($"Nuevo {_newAlgorithmNumber++}.psc", BuildSampleProgram(_language), _language);
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
        HighlightDebugLine(document.DebugLine);
        UpdateDiagnosticUnderlines();
        UpdateOutputPanelView();
        RenderOpenDocuments();
        UpdateEmptyWorkspaceState();
        UpdateExecutionControls();
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
        OutputTextBox.Text = document?.OutputText ?? string.Empty;
        OutputTextBox.IsVisible = !_showDiagnostics;
        DiagnosticProblemsScroll.IsVisible = _showDiagnostics;
        RenderDiagnosticProblems(document);

        var count = document?.Diagnostics.Count ?? 0;
        DiagnosticCountBadge.IsVisible = count > 0;
        DiagnosticCountText.Text = count > 99 ? "99+" : count.ToString();

        OutputTabButton.Foreground = _showDiagnostics ? Brush("TextSecondary") : Brush("TextPrimary");
        OutputTabButton.FontWeight = _showDiagnostics ? FontWeight.Normal : FontWeight.SemiBold;
        DiagnosticsTabButton.Foreground = _showDiagnostics ? Brush("TextPrimary") : Brush("TextSecondary");
        DiagnosticsTabButton.FontWeight = _showDiagnostics ? FontWeight.SemiBold : FontWeight.Normal;
    }

    private bool UpdateLiveSyntaxDiagnostics(OpenDocument document)
    {
        var diagnostics = _syntaxValidator.Validate(document.Text);
        document.DiagnosticsText = diagnostics.Count == 0 ? "Sin diagnosticos." : string.Join(Environment.NewLine, diagnostics);
        document.Diagnostics = ParseDiagnostics(diagnostics).ToList();
        document.DiagnosticLines = document.Diagnostics.Select(diagnostic => diagnostic.Line).ToHashSet();
        return diagnostics.Count > 0;
    }

    private void RenderDiagnosticProblems(OpenDocument? document)
    {
        DiagnosticProblemsPanel.Children.Clear();
        var diagnostics = document?.Diagnostics ?? [];
        if (diagnostics.Count == 0)
        {
            DiagnosticProblemsPanel.Children.Add(new TextBlock
            {
                Text = "Sin problemas.",
                Foreground = Brush("TextSecondary"),
                Margin = new Thickness(4)
            });
            return;
        }

        foreach (var diagnostic in diagnostics)
        {
            DiagnosticProblemsPanel.Children.Add(BuildDiagnosticProblemButton(diagnostic));
        }
    }

    private Button BuildDiagnosticProblemButton(DiagnosticItem diagnostic)
    {
        var lineBadge = new Border
        {
            Background = Brush("AccentBlue"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = $"Linea {diagnostic.Line}",
                Foreground = Brushes.White,
                FontWeight = FontWeight.SemiBold,
                FontSize = 12
            }
        };

        var detail = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock
                {
                    Text = diagnostic.Message,
                    Foreground = Brush("TextPrimary"),
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = diagnostic.Cause,
                    Foreground = Brush("TextSecondary"),
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = diagnostic.Solution,
                    Foreground = Brush("TextSecondary"),
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };

        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Brush("PanelBackground"),
            BorderBrush = Brush("BorderBrushMuted"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 10,
                Children =
                {
                    lineBadge,
                    WithGridColumn(detail, 1)
                }
            }
        };
        button.Click += (_, _) => GoToDiagnostic(diagnostic);
        return button;
    }

    private void GoToDiagnostic(DiagnosticItem diagnostic)
    {
        if (_currentDocument is null || EditorTextBox.Document is null)
        {
            return;
        }

        var line = Math.Clamp(diagnostic.Line, 1, EditorTextBox.Document.LineCount);
        var documentLine = EditorTextBox.Document.GetLineByNumber(line);
        EditorTextBox.Focus();
        EditorTextBox.Select(documentLine.Offset, Math.Max(1, documentLine.Length));
        EditorTextBox.CaretOffset = documentLine.Offset;
        EditorTextBox.ScrollToLine(line);
        UpdateWindowState($"Problema en linea {line}");
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
        UpdateExecutionControls();
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
        foreach (var topic in _helpTopics)
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
        foreach (var template in _quickTemplates)
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

        CommandsList.ItemsSource = _completionItems
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
            $"{_language.Keyword("and")}, {_language.Keyword("or")}, {_language.Keyword("not")} operadores logicos"
        };
    }

    private void InsertCommandTemplate(string template)
    {
        InsertAtCaret(template);
        EditorTextBox.Focus();
        UpdateWindowState("Plantilla insertada");
    }

    private IEnumerable<string> ExtractVariables(string source)
    {
        var variables = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var declare = Regex.Escape(_language.Keyword("declare"));
        var typeSeparator = Regex.Escape(_language.Keyword("typeSeparator"));
        foreach (Match match in Regex.Matches(source, $@"(?im)^\s*{declare}\s+(.+?)(?:\s+{typeSeparator}\s+\w+)?\s*$"))
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
        var document = new OpenDocument($"{topic.Title}.psc", topic.Example, _language)
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

        ApplyEditorTheme(colors, _isLightTheme ? _runtimeSettings.LightSyntaxTheme.ToPalette() : _runtimeSettings.DarkSyntaxTheme.ToPalette());
        RenderOpenDocuments();
        UpdateOutputPanelView();
    }

    private void ApplyEditorTheme(IReadOnlyDictionary<string, string> colors, PseudoCodeColorPalette syntaxPalette)
    {
        EditorTextBox.Background = syntaxPalette.EditorBackgroundBrush;
        EditorTextBox.TextArea.Background = syntaxPalette.EditorBackgroundBrush;
        EditorTextBox.Foreground = BrushFromTheme(colors, "TextPrimary");
        EditorTextBox.TextArea.Caret.CaretBrush = BrushFromTheme(colors, "CaretBrush");
        EditorTextBox.TextArea.SelectionBrush = BrushFromTheme(colors, "SelectionBrush");
        EditorTextBox.TextArea.SelectionForeground = BrushFromTheme(colors, "TextPrimary");
        EditorTextBox.LineNumbersForeground = BrushFromTheme(colors, "LineNumberText");
        EditorTextBox.TextArea.TextView.CurrentLineBackground = BrushFromTheme(colors, "LineNumberBackground");
        EditorTextBox.TextArea.TextView.CurrentLineBorder = new Pen(BrushFromTheme(colors, "BorderBrushMuted"), 1);
        _colorizer?.SetPalette(syntaxPalette);
        _diagnosticUnderlineRenderer?.SetBrush(syntaxPalette.DiagnosticUnderlineBrush);
        _debugLineRenderer?.SetBrush(BuildDebugLineBrush(syntaxPalette));
        EditorTextBox.TextArea.TextView.Redraw();
    }

    private static SolidColorBrush BrushFromTheme(IReadOnlyDictionary<string, string> colors, string key) =>
        new(Color.Parse(colors[key]));

    private static SolidColorBrush BuildDebugLineBrush(PseudoCodeColorPalette syntaxPalette)
    {
        if (syntaxPalette.EditorBackgroundBrush is not SolidColorBrush background)
        {
            return new SolidColorBrush(Color.FromArgb(70, 76, 175, 80));
        }

        var color = background.Color;
        var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255;
        return luminance > 0.55
            ? new SolidColorBrush(Color.FromArgb(45, 76, 175, 80))
            : new SolidColorBrush(Color.FromArgb(85, 76, 175, 80));
    }

    private static PseudoCodeColorPalette BuildThemePreviewPalette(string json)
    {
        string ColorFor(string key)
        {
            if (TryGetThemeColor(json, key, out var value) && NormalizeHex(value) is { } normalized)
            {
                return normalized;
            }

            return DefaultThemeColor(key) ?? "#1E1E1E";
        }

        return new PseudoCodeColorPalette(
            BrushFromHex(ColorFor("keyword")),
            BrushFromHex(ColorFor("type")),
            BrushFromHex(ColorFor("string")),
            BrushFromHex(ColorFor("number")),
            BrushFromHex(ColorFor("operator")),
            BrushFromHex(ColorFor("comment")),
            BrushFromHex(ColorFor("blockBackground")),
            BrushFromHex(ColorFor("diagnosticUnderline")),
            BrushFromHex(ColorFor("editorBackground")));
    }

    private static PseudoLanguageDefinition BuildPreviewLanguage(string json)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<PseudoLanguageDto>(json, JsonReadOptions);
            return dto is null
                ? PseudoLanguageDefinition.CreateDefault()
                : PseudoLanguageDefinition.FromDto(dto, []);
        }
        catch
        {
            return PseudoLanguageDefinition.CreateDefault();
        }
    }

    private static string BuildPreviewCode(PseudoLanguageDefinition language)
    {
        var type = language.Types.FirstOrDefault() ?? "Entero";
        return
            $"{language.Keyword("algorithmStart")} VistaPrevia\n" +
            $"    {language.Keyword("declare")} numero {language.Keyword("typeSeparator")} {type}\n" +
            $"    numero <- 10 + 2\n" +
            $"    {language.Keyword("if")} numero >= 10 {language.Keyword("then")}\n" +
            $"        {language.Keyword("write")} \"Resultado: \", numero // comentario\n" +
            $"    {language.Keyword("else")}\n" +
            $"        {language.Keyword("write")} \"Menor\"\n" +
            $"    {language.Keyword("endIf")}\n" +
            $"{language.Keyword("algorithmEnd")}\n";
    }

    private static SolidColorBrush BrushFromHex(string color) => new(Color.Parse(color));

    private static SolidColorBrush BuildReadableTextBrush(IBrush background)
    {
        var color = background is SolidColorBrush solid ? solid.Color : Color.Parse("#1E1E1E");
        return IsLightColor(color)
            ? new SolidColorBrush(Color.Parse("#1F2937"))
            : new SolidColorBrush(Color.Parse("#E5E7EB"));
    }

    private static SolidColorBrush BuildMutedTextBrush(IBrush background)
    {
        var color = background is SolidColorBrush solid ? solid.Color : Color.Parse("#1E1E1E");
        return IsLightColor(color)
            ? new SolidColorBrush(Color.Parse("#6B7280"))
            : new SolidColorBrush(Color.Parse("#9CA3AF"));
    }

    private static bool IsLightColor(Color color)
    {
        var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255;
        return luminance > 0.55;
    }

    private SolidColorBrush Brush(string key) => (SolidColorBrush)Resources[key]!;

    private static AboutAnimation[] BuildAuthorAnimations() =>
    [
        new("Normal",
        [
            "2-KityDev-Normal.png",
            "2-KityDev-Normal.png",
            "2-KityDev-Normal.png",
            "2-KityDev-Normal-closeEyes.png",
            "2-KityDev-Normal.png",
            "2-KityDev-Normal-OpenMouthpng.png",
            "2-KityDev-Normal.png",
            "2-KityDev-Normal.png",
            "2-KityDev-Normal-OpenMouth_closeEyes.png",
            "2-KityDev-Normal.png"
        ]),
        new("Happy",
        [
            "2-KityDev-Happy.png",
            "2-KityDev-Happy.png",
            "2-KityDev-Happy-CloseEyes.png",
            "2-KityDev-Happy.png",
            "2-KityDev-Happy.png",
            "KityDev-openmouth.png",
            "2-KityDev-Happy.png",
            "2-KityDev-Happy-CloseEyes.png"
        ]),
        new("Thinking",
        [
            "2-KityDev-Thinking.png",
            "2-KityDev-Thinking.png",
            "2-KityDev-Thinking-closeEyes.png",
            "2-KityDev-Thinking.png",
            "2-KityDev-Thinking-OpenMouthpng.png",
            "2-KityDev-Thinking.png",
            "2-KityDev-Thinking-OpenMouth_closeEyes.png",
            "2-KityDev-Thinking.png"
        ]),
        new("Classic",
        [
            "KityDev-cousemouth.png",
            "KityDev-cousemouth.png",
            "KityDev-closeEyesClosemouth.png",
            "KityDev-cousemouthpng.png",
            "KityDev-openmouth.png",
            "KityDev-closeEyesOpenmouth.png",
            "KityDev-cousemouth.png"
        ]),
        new("Celebration",
        [
            "KityDev-MaCaras-Celebration_OpenEyes-CloseMouth.png",
            "KityDev-MaCaras-Celebration_OpenEyes-OpenMouth.png",
            "KityDev-MaCaras-Celebration_OpenEyes-CloseMouth.png",
            "KityDev-MaCaras-Celebration_CloseEyes-CloseMouth.png",
            "KityDev-MaCaras-Celebration_OpenEyes-CloseMouth.png",
            "KityDev-MaCaras-Celebration_CloseEyes-OpenMouth.png"
        ])
    ];

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
            Foreground = Brush("AccentBlue"),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        link.Click += async (_, _) => await OpenExternalLinkAsync(uri);
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

    private async Task OpenExternalLinkAsync(string uri)
    {
        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is not null && await launcher.LaunchUriAsync(new Uri(uri)))
        {
            return;
        }

        if (Clipboard is not null)
        {
            await Clipboard.SetTextAsync(uri);
        }
        UpdateWindowState("No pude abrir el navegador; copie el link");
    }

    private async Task<bool> OpenFolderAsync(string path)
    {
        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is null)
        {
            return false;
        }

        var folderUri = new Uri(Path.GetFullPath(path) + Path.DirectorySeparatorChar);
        return await launcher.LaunchUriAsync(folderUri);
    }

    private Grid BuildSettingsInfo(string label, string value)
    {
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(150)),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10
        };

        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brush("TextSecondary"),
            FontWeight = FontWeight.SemiBold
        });

        var valueText = new TextBlock
        {
            Text = value,
            Foreground = Brush("TextPrimary"),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(valueText, 1);
        row.Children.Add(valueText);

        return row;
    }

    private Border BuildCodePreview(string text) => new()
    {
        Background = Brush("InsetBackground"),
        BorderBrush = Brush("BorderBrushMuted"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(12),
        Child = new TextBlock
        {
            Text = text,
            Foreground = Brush("TextPrimary"),
            FontFamily = new FontFamily("Cascadia Code,Consolas,monospace"),
            TextWrapping = TextWrapping.Wrap
        }
    };

    private bool StartsLogicalBlock(string text) =>
        KeywordLineStarts(text, "algorithmStart") ||
        KeywordLineStarts(text, "processStart") ||
        KeywordLineStarts(text, "if") ||
        KeywordLineStarts(text, "while") ||
        KeywordLineStarts(text, "for") ||
        KeywordLineStarts(text, "switch");

    private bool IsClosingBlock(string text) =>
        KeywordLineStarts(text, "algorithmEnd") ||
        KeywordLineStarts(text, "processEnd") ||
        KeywordLineStarts(text, "endIf") ||
        KeywordLineStarts(text, "endWhile") ||
        KeywordLineStarts(text, "endFor") ||
        KeywordLineStarts(text, "endSwitch");

    private bool IsMidBlock(string text) =>
        KeywordLineStarts(text, "else");

    private bool IsSwitchCaseLabel(string text)
    {
        var withoutComment = StripInlineComment(text).Trim();
        var comparable = withoutComment.TrimEnd(':').Trim();
        if (KeywordLineStarts(comparable, "otherwise"))
        {
            return true;
        }

        return withoutComment.EndsWith(':') &&
               !withoutComment.Contains("<-", StringComparison.Ordinal) &&
               !StartsLogicalBlock(withoutComment) &&
               !IsClosingBlock(withoutComment);
    }

    private bool KeywordLineStarts(string text, string role)
    {
        var keyword = _language.Keyword(role);
        return text.Equals(keyword, StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith(keyword + " ", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripInlineComment(string text)
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
                return text[..index];
            }
        }

        return text;
    }

    private string BuildOutputText(ExecutionResult result)
    {
        var builder = new StringBuilder();

        if (result.Output.Count == 0)
        {
            builder.AppendLine($"Sin salida. Usa {_language.Keyword("write")} para mostrar datos.");
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

    private static IEnumerable<DiagnosticItem> ParseDiagnostics(IEnumerable<string> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            var match = Regex.Match(
                diagnostic,
                @"^Linea\s+(?<line>\d+):\s*(?<message>.*?)(?:\.\s*Causa:\s*(?<cause>.*?))?(?:\.\s*Solucion:\s*(?<solution>.*?))?\.?$",
                RegexOptions.IgnoreCase);

            if (!match.Success || !int.TryParse(match.Groups["line"].Value, out var line))
            {
                yield return new DiagnosticItem(1, diagnostic, "Causa: no se pudo determinar la linea exacta.", "Solucion: revisa el mensaje completo.");
                continue;
            }

            var message = match.Groups["message"].Value.Trim();
            var cause = match.Groups["cause"].Success
                ? "Causa: " + match.Groups["cause"].Value.Trim().TrimEnd('.')
                : "Causa: revisa la instruccion marcada.";
            var solution = match.Groups["solution"].Success
                ? "Solucion: " + match.Groups["solution"].Value.Trim().TrimEnd('.')
                : "Solucion: ajusta la linea segun el dialecto activo.";

            yield return new DiagnosticItem(line, message.TrimEnd('.'), cause, solution);
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

    private static HelpTopic[] BuildDefaultHelpTopics(PseudoLanguageDefinition language)
    {
        var algorithm = language.Keyword("algorithmStart");
        var endAlgorithm = language.Keyword("algorithmEnd");
        var declare = language.Keyword("declare");
        var typeSeparator = language.Keyword("typeSeparator");
        var write = language.Keyword("write");
        var read = language.Keyword("read");
        var clear = language.Keyword("clear");
        var screen = language.Keyword("screen");
        var wait = language.Keyword("wait");
        var seconds = language.Keyword("seconds");
        var withoutNewline = language.Keyword("withoutNewline");
        var integerType = FindLanguageType(language, "Entero", "Integer") ?? language.Types.FirstOrDefault() ?? "Entero";
        var textType = FindLanguageType(language, "Cadena", "String", "Texto") ?? integerType;

        return
        [
            new(
                "Estructura base",
                "La forma minima de un algoritmo: inicio, instrucciones y cierre.",
                $"""
{algorithm} MiPrograma
    {write} "Hola desde PseudoCode"
{endAlgorithm}
""",
                true),
            new(
                "Variables",
                $"Declara datos con {declare} y guarda valores con <-.",
                $"""
{algorithm} Variables
    {declare} edad {typeSeparator} {integerType}
    {declare} nombre {typeSeparator} {textType}

    nombre <- "Ada"
    edad <- 19

    {write} "Nombre: ", nombre
    {write} "Edad: ", edad
{endAlgorithm}
"""),
            new(
                "Entrada y salida",
                $"{read} pausa la ejecucion hasta que escribas un valor en la consola.",
                $"""
{algorithm} EntradaSalida
    {declare} numero {typeSeparator} {integerType}

    {read} numero
    {write} "Numero recibido: ", numero
{endAlgorithm}
"""),
            new(
                "Salida avanzada",
                $"{withoutNewline} escribe sin cambiar de linea; {clear} {screen} limpia la salida y {wait} pausa la ejecucion.",
                $"""
{algorithm} SalidaAvanzada
    {write} "Hola " {withoutNewline};
    {write} "mundo"
    {wait} 1 {seconds};
    {clear} {screen}
    {write} "Salida limpia"
{endAlgorithm}
"""),
            new(
                "Operaciones",
                "Puedes combinar numeros y variables en expresiones aritmeticas.",
                $"""
{algorithm} Operaciones
    {declare} a, b, total {typeSeparator} {integerType}

    a <- 8
    b <- 4
    total <- a + b * 2

    {write} "Resultado: ", total
{endAlgorithm}
""")
        ];
    }

    private static string? FindLanguageType(PseudoLanguageDefinition language, params string[] preferred) =>
        preferred
            .Select(name => language.Types.FirstOrDefault(type => type.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(type => !string.IsNullOrWhiteSpace(type));

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

    private static string BuildSampleProgram(PseudoLanguageDefinition language) =>
        $"""
{language.Keyword("algorithmStart")}

{language.Keyword("algorithmEnd")}
""";
}
