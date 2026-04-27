using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace PseudoCode.App;

internal sealed class BlockStructureRenderer : IBackgroundRenderer
{
    private sealed record BlockRange(int StartLine, int EndLine, int Depth, int IndentColumns);

    private readonly IBrush[] _guideBrushes =
    [
        new SolidColorBrush(Color.Parse("#5EA1FF"), 0.35),
        new SolidColorBrush(Color.Parse("#6BCB77"), 0.35),
        new SolidColorBrush(Color.Parse("#E9C46A"), 0.35),
        new SolidColorBrush(Color.Parse("#E07A5F"), 0.35)
    ];

    private IBrush _activeBlockBrush = new SolidColorBrush(Color.Parse("#264F78"), 0.18);
    private PseudoLanguageDefinition _language;
    private int _caretLine;
    private int _indentationSize = 4;
    private IReadOnlyList<BlockRange> _blocks = [];

    public BlockStructureRenderer(PseudoLanguageDefinition language)
    {
        _language = language;
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void SetLanguage(PseudoLanguageDefinition language)
    {
        _language = language;
    }

    public void SetActiveBlockBrush(IBrush brush)
    {
        _activeBlockBrush = brush;
    }

    public void SetCaretLine(int lineNumber)
    {
        _caretLine = Math.Max(0, lineNumber);
    }

    public void SetIndentationSize(int indentationSize)
    {
        _indentationSize = Math.Max(1, indentationSize);
    }

    public void UpdateDocument(TextDocument? document)
    {
        _blocks = document is null ? [] : ParseBlocks(document);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (textView.Document is null || _blocks.Count == 0)
        {
            return;
        }

        textView.EnsureVisualLines();
        DrawActiveBlock(textView, drawingContext);
        DrawGuides(textView, drawingContext);
    }

    private void DrawActiveBlock(TextView textView, DrawingContext drawingContext)
    {
        var activeBlock = _blocks
            .Where(block => _caretLine >= block.StartLine && _caretLine <= block.EndLine)
            .OrderByDescending(block => block.Depth)
            .FirstOrDefault();

        if (activeBlock is null)
        {
            return;
        }

        if (activeBlock.EndLine - activeBlock.StartLine < 2)
        {
            return;
        }

        var startLine = textView.Document!.GetLineByNumber(activeBlock.StartLine + 1);
        var endLine = textView.Document.GetLineByNumber(activeBlock.EndLine - 1);
        var segment = new TextSegment
        {
            StartOffset = startLine.Offset,
            Length = Math.Max(1, endLine.EndOffset - startLine.Offset)
        };

        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment, true))
        {
            drawingContext.DrawRectangle(_activeBlockBrush, null, new Rect(0, rect.Top, textView.Bounds.Width, rect.Height));
        }
    }

    private void DrawGuides(TextView textView, DrawingContext drawingContext)
    {
        foreach (var block in _blocks)
        {
            if (block.EndLine <= block.StartLine)
            {
                continue;
            }

            var startLine = textView.Document!.GetLineByNumber(block.StartLine);
            var endLine = textView.Document.GetLineByNumber(block.EndLine);
            var segment = new TextSegment
            {
                StartOffset = startLine.Offset,
                Length = Math.Max(1, endLine.EndOffset - startLine.Offset)
            };
            var rects = BackgroundGeometryBuilder.GetRectsForSegment(textView, segment, true).ToArray();
            if (rects.Length == 0)
            {
                continue;
            }

            var startTop = rects.Min(rect => rect.Top);
            var endBottom = rects.Max(rect => rect.Bottom);
            var guideColumn = Math.Max(1, block.IndentColumns + 1);
            var x = textView.GetVisualPosition(new TextViewPosition(block.StartLine, guideColumn), VisualYPosition.LineTop).X - 5;
            var pen = new Pen(_guideBrushes[(block.Depth - 1) % _guideBrushes.Length], 1);

            drawingContext.DrawLine(pen, new Point(x, startTop), new Point(x, endBottom));
        }
    }

    private IReadOnlyList<BlockRange> ParseBlocks(TextDocument document)
    {
        var blocks = new List<BlockRange>();
        var stack = new Stack<(string Kind, int StartLine, int IndentColumns, int Depth)>();

        for (var lineNumber = 1; lineNumber <= document.LineCount; lineNumber++)
        {
            var line = document.GetLineByNumber(lineNumber);
            var text = document.GetText(line);
            var trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (IsClosingBlock(trimmed, out var closingKind))
            {
                while (stack.Count > 0)
                {
                    var current = stack.Pop();
                    if (current.Kind == closingKind)
                    {
                        blocks.Add(new BlockRange(current.StartLine, lineNumber, current.Depth, current.IndentColumns));
                        break;
                    }
                }

                continue;
            }

            if (IsOpeningBlock(trimmed, out var openingKind))
            {
                var indentColumns = CountIndentColumns(text);
                stack.Push((openingKind, lineNumber, indentColumns, stack.Count + 1));
            }
        }

        return blocks;
    }

    private bool IsOpeningBlock(string trimmedLine, out string kind)
    {
        if (IsKeywordLine(trimmedLine, "if"))
        {
            kind = "if";
            return true;
        }

        if (IsKeywordLine(trimmedLine, "while"))
        {
            kind = "while";
            return true;
        }

        if (IsKeywordLine(trimmedLine, "for"))
        {
            kind = "for";
            return true;
        }

        if (IsKeywordLine(trimmedLine, "switch"))
        {
            kind = "switch";
            return true;
        }

        kind = string.Empty;
        return false;
    }

    private bool IsClosingBlock(string trimmedLine, out string kind)
    {
        if (trimmedLine.Equals(_language.Keyword("endIf"), StringComparison.OrdinalIgnoreCase))
        {
            kind = "if";
            return true;
        }

        if (trimmedLine.Equals(_language.Keyword("endWhile"), StringComparison.OrdinalIgnoreCase))
        {
            kind = "while";
            return true;
        }

        if (trimmedLine.Equals(_language.Keyword("endFor"), StringComparison.OrdinalIgnoreCase))
        {
            kind = "for";
            return true;
        }

        if (trimmedLine.Equals(_language.Keyword("endSwitch"), StringComparison.OrdinalIgnoreCase))
        {
            kind = "switch";
            return true;
        }

        kind = string.Empty;
        return false;
    }

    private bool IsKeywordLine(string trimmedLine, string role)
    {
        var keyword = _language.Keyword(role);
        return trimmedLine.Equals(keyword, StringComparison.OrdinalIgnoreCase) ||
               trimmedLine.StartsWith(keyword + " ", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountIndentColumns(string text)
    {
        var count = 0;
        foreach (var character in text)
        {
            if (character == ' ')
            {
                count++;
                continue;
            }

            if (character == '\t')
            {
                count += 4;
                continue;
            }

            break;
        }

        return count;
    }
}
