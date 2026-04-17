using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace PseudoCode.App;

internal sealed class DiagnosticUnderlineRenderer : IBackgroundRenderer
{
    private Pen _pen;
    private HashSet<int> _lineNumbers = [];

    public DiagnosticUnderlineRenderer(IBrush brush)
    {
        _pen = new Pen(brush, 1.5);
    }

    public KnownLayer Layer => KnownLayer.Text;

    public void SetBrush(IBrush brush)
    {
        _pen = new Pen(brush, 1.5);
    }

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
