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

    private readonly IBrush _propertyBrush = new SolidColorBrush(Color.Parse("#9CDCFE"));
    private readonly IBrush _stringBrush = new SolidColorBrush(Color.Parse("#CE9178"));
    private readonly IBrush _numberBrush = new SolidColorBrush(Color.Parse("#B5CEA8"));
    private readonly IBrush _keywordBrush = new SolidColorBrush(Color.Parse("#569CD6"));
    private readonly IBrush _punctuationBrush = new SolidColorBrush(Color.Parse("#D4D4D4"));

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
}
