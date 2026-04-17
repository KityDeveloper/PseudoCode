using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;

namespace PseudoCode.App;

internal sealed record DocumentationPage(string Id, string Title, string ResourcePath);

internal static class DocumentationService
{
    public static readonly DocumentationPage Help = new("ayuda", "Documentacion", "avares://PseudoCode.App/Docs/ayuda.md");
    public static readonly DocumentationPage JsonConfig = new("configuracion-json", "Configuracion JSON", "avares://PseudoCode.App/Docs/configuracion-json.md");
    public static readonly DocumentationPage ReleaseNotes = new("notas-version", "Notas de version", "avares://PseudoCode.App/Docs/CHANGELOG.md");

    public static readonly DocumentationPage[] Pages =
    [
        Help,
        JsonConfig,
        ReleaseNotes
    ];

    public static string LoadMarkdown(DocumentationPage page)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(page.ResourcePath));
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            return $"# {page.Title}\n\nNo se pudo cargar este documento.\n\n```text\n{ex.Message}\n```";
        }
    }

    public static Window BuildWindow(DocumentationPage initialPage, IReadOnlyDictionary<string, string> colors)
    {
        var markdownHost = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 8
        };
        RenderMarkdown(markdownHost, LoadMarkdown(initialPage), colors);

        var title = new TextBlock
        {
            Text = initialPage.Title,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush(colors["TextPrimary"]),
            VerticalAlignment = VerticalAlignment.Center
        };

        var list = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(12)
        };

        foreach (var page in Pages)
        {
            var button = new Button
            {
                Content = page.Title,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            button.Click += (_, _) =>
            {
                title.Text = page.Title;
                RenderMarkdown(markdownHost, LoadMarkdown(page), colors);
            };
            list.Children.Add(button);
        }

        var header = new Border
        {
            Background = Brush(colors["PanelBackground"]),
            BorderBrush = Brush(colors["BorderBrushMuted"]),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 0),
            Child = title
        };
        Grid.SetColumnSpan(header, 2);

        var navigation = new Border
        {
            Background = Brush(colors["PanelBackground"]),
            BorderBrush = Brush(colors["BorderBrushMuted"]),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new ScrollViewer { Content = list }
        };
        Grid.SetRow(navigation, 1);

        var document = new ScrollViewer
        {
            Content = markdownHost
        };
        Grid.SetColumn(document, 1);
        Grid.SetRow(document, 1);

        return new Window
        {
            Title = $"PseudoCode - {initialPage.Title}",
            Width = 900,
            Height = 680,
            MinWidth = 680,
            MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush(colors["EditorBackground"]),
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("220,*"),
                RowDefinitions = new RowDefinitions("48,*"),
                Children =
                {
                    header,
                    navigation,
                    document
                }
            }
        };
    }

    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));

    private static void RenderMarkdown(StackPanel host, string markdown, IReadOnlyDictionary<string, string> colors)
    {
        host.Children.Clear();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var inCode = false;
        var code = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                if (inCode)
                {
                    host.Children.Add(CodeBlock(string.Join(Environment.NewLine, code), colors));
                    code.Clear();
                    inCode = false;
                }
                else
                {
                    inCode = true;
                }
                continue;
            }

            if (inCode)
            {
                code.Add(rawLine);
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                host.Children.Add(Text(line[2..], 26, FontWeight.Bold, colors["TextPrimary"]));
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                host.Children.Add(Text(line[3..], 21, FontWeight.SemiBold, colors["TextPrimary"]));
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                host.Children.Add(Text(line[4..], 17, FontWeight.SemiBold, colors["TextPrimary"]));
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                host.Children.Add(Text("• " + StripMarkdown(line[2..]), 14, FontWeight.Normal, colors["TextPrimary"]));
                continue;
            }

            if (line.StartsWith("> ", StringComparison.Ordinal))
            {
                host.Children.Add(Text(StripMarkdown(line[2..]), 14, FontWeight.Normal, colors["TextSecondary"]));
                continue;
            }

            host.Children.Add(Text(StripMarkdown(line), 14, FontWeight.Normal, colors["TextPrimary"]));
        }

        if (code.Count > 0)
        {
            host.Children.Add(CodeBlock(string.Join(Environment.NewLine, code), colors));
        }
    }

    private static TextBlock Text(string text, double fontSize, FontWeight weight, string color) => new()
    {
        Text = text,
        FontSize = fontSize,
        FontWeight = weight,
        Foreground = Brush(color),
        TextWrapping = TextWrapping.Wrap
    };

    private static Border CodeBlock(string text, IReadOnlyDictionary<string, string> colors) => new()
    {
        Background = Brush(colors["InsetBackground"]),
        BorderBrush = Brush(colors["BorderBrushMuted"]),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(12),
        Child = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Cascadia Code,Consolas,monospace"),
            FontSize = 13,
            Foreground = Brush(colors["TextPrimary"]),
            TextWrapping = TextWrapping.Wrap
        }
    };

    private static string StripMarkdown(string text) =>
        text.Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal);
}
