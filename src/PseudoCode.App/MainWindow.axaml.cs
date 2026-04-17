using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
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
    private bool _showDiagnostics;
    private bool _isLightTheme;
    private bool _isHelpVisible = true;
    private CompletionWindow? _completionWindow;
    private PseudoCodeColorizer? _colorizer;
    private DiagnosticUnderlineRenderer? _diagnosticUnderlineRenderer;
    private DebugLineRenderer? _debugLineRenderer;

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
        ConfigureEditor();
        BuildEditorTools();
        BuildHelpTopics();
        RenderRecentDocumentsMenu();
        DragDrop.SetAllowDrop(this, true);
        DragDrop.SetAllowDrop(EditorTextBox, true);
        AddHandler(DragDrop.DragOverEvent, Editor_DragOver);
        AddHandler(DragDrop.DropEvent, Editor_Drop);
        UpdateLineNumbers();
        UpdateEmptyWorkspaceState();
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

        StopDebug(document);
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
        if (result.Diagnostics.Count > 0)
        {
            _showDiagnostics = true;
            UpdateOutputPanelView();
            UpdateWindowState("No se ejecuto: hay errores");
        }
    }

    private void StartDebug_Click(object? sender, RoutedEventArgs e)
    {
        StartDebugSession();
    }

    private void StepDebug_Click(object? sender, RoutedEventArgs e)
    {
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

    private void Window_KeyDown(object? sender, KeyEventArgs e)
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

        if (e.Key == Key.N)
        {
            AddNewDocument();
            e.Handled = true;
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

    private void FormatDocument_Click(object? sender, RoutedEventArgs e)
    {
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
        codePreview.TextArea.TextView.LineTransformers.Add(codePreviewColorizer);

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
            Height = 260
        };

        var visualThemeEditor = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(12),
            IsVisible = IsThemeTarget(selectedTarget)
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
            var selectButton = new Button
            {
                Content = "Editar",
                Padding = new Thickness(10, 3),
                Classes = { "command" }
            };

            colorRows[colorKey] = (input, preview);
            input.GotFocus += (_, _) => SelectThemeColor(colorKey);
            selectButton.Click += (_, _) => SelectThemeColor(colorKey);
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

            visualThemeEditor.Children.Add(new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("160,118,34,72"),
                ColumnSpacing = 10,
                Children =
                {
                    label,
                    WithGridColumn(input, 1),
                    WithGridColumn(preview, 2),
                    WithGridColumn(selectButton, 3)
                }
            });
        }

        visualThemeEditor.Children.Add(new TextBlock
        {
            Text = "Selector de color",
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("TextPrimary"),
            Margin = new Thickness(0, 8, 0, 0)
        });
        visualThemeEditor.Children.Add(colorPicker);

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
                visualTextEditor.Children.Add(BuildVisualSection("Editor", "Selecciona los temas de sintaxis por modo de interfaz."));
                visualTextEditor.Children.Add(BuildVisualTextField("Dark syntax theme", "editor.syntaxThemeDark", GetJsonString(root, "editor", "syntaxThemeDark"), value => SetJsonString(["editor", "syntaxThemeDark"], value)));
                visualTextEditor.Children.Add(BuildVisualTextField("Light syntax theme", "editor.syntaxThemeLight", GetJsonString(root, "editor", "syntaxThemeLight"), value => SetJsonString(["editor", "syntaxThemeLight"], value)));
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
                    foreach (var keyword in PseudoLanguageDefinition.RequiredKeywordRoles)
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
            Width = 1220,
            Height = 720,
            MinWidth = 1040,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("EditorBackground"),
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("230,6,*,6,480"),
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

        var authorLogo = new Image
        {
            Source = new Bitmap(AssetLoader.Open(new Uri("avares://PseudoCode.App/Assets/iconKityDev.png"))),
            Width = 170,
            Height = 170,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center
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
                new TextBlock
                {
                    Text = AppInfoService.Author,
                    Foreground = Brush("TextSecondary"),
                    FontSize = 15,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                BuildAboutLink("YouTube", "@KityDev - https://www.youtube.com/@KityDev", AppInfoService.YouTubeUrl),
                BuildAboutLink("GitHub", AppInfoService.GitHubUrl, AppInfoService.GitHubUrl),
                BuildAboutLink("Web", "kity.dev", AppInfoService.WebUrl),
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
            UpdateWindowState(result.Success ? "Ejecucion completada" : "Ejecucion con diagnosticos");
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
            _showDiagnostics = true;
            UpdateDiagnosticUnderlines();
            UpdateOutputPanelView();
            UpdateWindowState("Corrige los errores antes de depurar");
            return;
        }

        document.IsDebugging = true;
        var step = document.Interpreter.StartDebug(document.Text);
        ApplyDebugStepResult(document, step);
        if (step.Execution.Diagnostics.Count > 0)
        {
            document.IsDebugging = false;
            _showDiagnostics = true;
            HighlightDebugLine(null);
            UpdateOutputPanelView();
            UpdateWindowState("No se inicio depuracion: hay errores");
            return;
        }

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
        ApplyEditorTheme(DarkTheme, palette);
        ConfigureEditorContextMenu();
        EditorTextBox.TextArea.TextEntered += Editor_TextEntered;
        EditorTextBox.TextArea.TextEntering += Editor_TextEntering;
        EditorTextBox.TextArea.KeyDown += Editor_KeyDown;
        EditorTextBox.PointerWheelChanged += Editor_PointerWheelChanged;
    }

    private void ConfigureEditorContextMenu()
    {
        var formatItem = new MenuItem
        {
            Header = "Formatear documento (Ctrl+K, Ctrl+D)"
        };
        formatItem.Click += FormatDocument_Click;

        EditorTextBox.ContextMenu = new ContextMenu
        {
            Items =
            {
                formatItem
            }
        };
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
        if (TryHandleFormatShortcut(e))
        {
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

        var matches = _completionItems
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
        foreach (var item in matches.Length == 0 ? _completionItems : matches)
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
