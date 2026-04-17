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
    private RuntimeSettings _runtimeSettings;
    private PseudoLanguageDefinition _language;
    private PseudoSyntaxValidator _syntaxValidator;
    private CommandInfo[] _completionItems;
    private CommandInfo[] _quickTemplates;
    private HelpTopic[] _helpTopics;
    private List<RecentDocumentInfo> _recentDocuments;
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
        var window = DocumentationService.BuildWindow(page, _isLightTheme ? LightTheme : DarkTheme);
        window.Show(this);
        return Task.CompletedTask;
    }

    private Task ShowJsonSettingsEditorAsync(string title, Func<JsonConfigTarget, bool> filter)
    {
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

        var editor = new TextBox
        {
            Text = AppSettingsService.ReadOrTemplate(selectedTarget),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Code,Consolas,monospace"),
            FontSize = 13,
            Background = Brush("InsetBackground"),
            Foreground = Brush("TextPrimary"),
            BorderBrush = Brush("BorderBrushMuted")
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(editor, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(editor, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

        void SelectTarget(JsonConfigTarget target)
        {
            selectedTarget = target;
            fileLabel.Text = target.RelativePath;
            editor.Text = AppSettingsService.ReadOrTemplate(target);
            status.Text = File.Exists(target.FullPath)
                ? $"Editando copia de usuario: {target.FullPath}"
                : "Este archivo aun no existe en tu perfil. Guardar creara una copia editable.";
        }

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
        Grid.SetColumnSpan(header, 2);

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
            Padding = new Thickness(12),
            Child = editor
        };
        Grid.SetColumn(editorPanel, 1);
        Grid.SetRow(editorPanel, 1);

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
        Grid.SetColumnSpan(footer, 2);
        Grid.SetRow(footer, 2);

        var window = new Window
        {
            Title = title,
            Width = 980,
            Height = 720,
            MinWidth = 760,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("EditorBackground"),
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("230,*"),
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                Children =
                {
                    header,
                    navigation,
                    editorPanel,
                    footer
                }
            }
        };

        window.Show(this);
        return Task.CompletedTask;
    }

    private Task ShowSettingsConfigurationAsync()
    {
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
        window.Show(this);
        return Task.CompletedTask;
    }

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

        UpdateVariablesList();
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
        ApplyDebugStepResult(document, document.Interpreter.StartDebug(document.Text));
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
        UpdateVariablesList();
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
        var diagnostics = _syntaxValidator.Validate(document.Text);
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
        _language.StartsWithKeyword(text, "algorithmStart") ||
        _language.StartsWithKeyword(text, "processStart") ||
        _language.StartsWithKeyword(text, "if") ||
        _language.StartsWithKeyword(text, "while") ||
        _language.StartsWithKeyword(text, "for") ||
        _language.StartsWithKeyword(text, "switch");

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
