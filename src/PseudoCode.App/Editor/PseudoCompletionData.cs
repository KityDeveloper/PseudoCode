using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace PseudoCode.App;

internal sealed class PseudoCompletionData(CommandInfo item) : ICompletionData
{
    public IImage? Image => null;

    public string Text => item.Text;

    public object Content => item.Text;

    public object Description => item.Description;

    public double Priority => item.IsTemplate ? 1 : 0;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        if (ShouldExpandTemplate())
        {
            CompleteTemplate(textArea, completionSegment);
            return;
        }

        textArea.Document.Replace(completionSegment, item.Text);
    }

    private bool ShouldExpandTemplate() =>
        item.IsTemplate && (item.InsertText.Contains('\n') || item.Text.Contains(' ') || item.Text.Contains('/'));

    private void CompleteTemplate(TextArea textArea, ISegment completionSegment)
    {
        var document = textArea.Document;
        var startOffset = completionSegment.Offset;
        var templateText = ApplyCurrentIndentation(document, startOffset, item.InsertText);

        document.Replace(completionSegment, templateText);

        var selectionRange = FindPlaceholderSelection(templateText);
        if (selectionRange is null)
        {
            textArea.Caret.Offset = Math.Min(startOffset + templateText.Length, document.TextLength);
            return;
        }

        var (selectionStart, selectionLength) = selectionRange.Value;
        var absoluteStart = startOffset + selectionStart;
        var absoluteEnd = absoluteStart + selectionLength;
        textArea.Selection = Selection.Create(textArea, absoluteStart, absoluteEnd);
        textArea.Caret.Offset = absoluteEnd;
    }

    private static string ApplyCurrentIndentation(TextDocument document, int startOffset, string template)
    {
        var line = document.GetLineByOffset(Math.Clamp(startOffset, 0, document.TextLength));
        var lineText = document.GetText(line);
        var currentIndent = Regex.Match(lineText, @"^\s*").Value;
        var newline = document.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var normalized = template.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');

        for (var index = 1; index < lines.Length; index++)
        {
            lines[index] = currentIndent + lines[index];
        }

        return string.Join(newline, lines);
    }

    private static (int Start, int Length)? FindPlaceholderSelection(string text)
    {
        foreach (var placeholder in new[] { "Mensaje", "condicion", "variable", "opcion", "MiPrograma", "numero" })
        {
            var index = text.IndexOf(placeholder, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                return (index, placeholder.Length);
            }
        }

        return null;
    }
}
