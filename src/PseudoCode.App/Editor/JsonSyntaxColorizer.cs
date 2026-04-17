using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace PseudoCode.App;

internal sealed class JsonSyntaxColorizer : DocumentColorizingTransformer
{
    private static readonly Regex PropertyName = new("\"(?:\\\\.|[^\"\\\\])*\"(?=\\s*:)", RegexOptions.Compiled);
    private static readonly Regex StringLiteral = new("\"(?:\\\\.|[^\"\\\\])*\"", RegexOptions.Compiled);
    private static readonly Regex NumberLiteral = new(@"(?<![\w.])-?\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b", RegexOptions.Compiled);
    private static readonly Regex KeywordLiteral = new(@"\b(true|false|null)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Punctuation = new(@"[{}\[\]:,]", RegexOptions.Compiled);

    private readonly IBrush _propertyBrush;
    private readonly IBrush _stringBrush;
    private readonly IBrush _numberBrush;
    private readonly IBrush _keywordBrush;
    private readonly IBrush _punctuationBrush;

    public JsonSyntaxColorizer(bool isLightTheme)
    {
        _propertyBrush = Brush(isLightTheme ? "#0451A5" : "#9CDCFE");
        _stringBrush = Brush(isLightTheme ? "#A31515" : "#CE9178");
        _numberBrush = Brush(isLightTheme ? "#098658" : "#B5CEA8");
        _keywordBrush = Brush(isLightTheme ? "#0000FF" : "#569CD6");
        _punctuationBrush = Brush(isLightTheme ? "#24292F" : "#D4D4D4");
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        var text = CurrentContext.Document.GetText(line);
        ApplyMatches(line, text, StringLiteral, _stringBrush);
        ApplyMatches(line, text, NumberLiteral, _numberBrush);
        ApplyMatches(line, text, KeywordLiteral, _keywordBrush);
        ApplyMatches(line, text, Punctuation, _punctuationBrush);
        ApplyMatches(line, text, PropertyName, _propertyBrush);
    }

    private void ApplyMatches(DocumentLine line, string text, Regex regex, IBrush brush)
    {
        foreach (Match match in regex.Matches(text))
        {
            ChangeLinePart(line.Offset + match.Index, line.Offset + match.Index + match.Length, element =>
            {
                element.TextRunProperties.SetForegroundBrush(brush);
            });
        }
    }

    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));
}
