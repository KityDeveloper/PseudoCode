using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace PseudoCode.App;

internal sealed record PseudoCodeColorPalette(
    IBrush KeywordBrush,
    IBrush TypeBrush,
    IBrush StringBrush,
    IBrush NumberBrush,
    IBrush OperatorBrush,
    IBrush CommentBrush,
    IBrush BlockBrush,
    IBrush DiagnosticUnderlineBrush,
    IBrush EditorBackgroundBrush);

internal sealed class PseudoCodeColorizer : DocumentColorizingTransformer
{
    private static readonly Regex StringLiteral = new("\"[^\"]*\"", RegexOptions.Compiled);
    private static readonly Regex NumberLiteral = new(@"\b\d+(\.\d+)?\b", RegexOptions.Compiled);
    private static readonly Regex Operator = new(@"(<-|<=|>=|<>|=|<|>|\+|-|\*|/|%)", RegexOptions.Compiled);

    private PseudoLanguageDefinition _language;
    private PseudoCodeColorPalette _palette;
    private Regex _keyword;
    private Regex _typeName;
    private Regex _blockLine;

    public PseudoCodeColorizer(PseudoLanguageDefinition language, PseudoCodeColorPalette palette)
    {
        _language = language;
        _palette = palette;
        _keyword = language.BuildKeywordRegex();
        _typeName = language.BuildTypeRegex();
        _blockLine = language.BuildBlockLineRegex();
    }

    public void SetLanguage(PseudoLanguageDefinition language)
    {
        _language = language;
        _keyword = language.BuildKeywordRegex();
        _typeName = language.BuildTypeRegex();
        _blockLine = language.BuildBlockLineRegex();
    }

    public void SetPalette(PseudoCodeColorPalette newPalette)
    {
        _palette = newPalette;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        var text = CurrentContext.Document.GetText(line);

        if (_blockLine.IsMatch(text))
        {
            ChangeLinePart(line.Offset, line.EndOffset, element =>
            {
                element.TextRunProperties.SetBackgroundBrush(_palette.BlockBrush);
            });
        }

        var commentIndex = FindCommentIndex(text);
        var codeLength = commentIndex >= 0 ? commentIndex : text.Length;

        ApplyMatches(line, text, _keyword, _palette.KeywordBrush, codeLength);
        ApplyMatches(line, text, _typeName, _palette.TypeBrush, codeLength);
        ApplyMatches(line, text, NumberLiteral, _palette.NumberBrush, codeLength);
        ApplyMatches(line, text, Operator, _palette.OperatorBrush, codeLength);
        ApplyMatches(line, text, StringLiteral, _palette.StringBrush, codeLength);

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
}
