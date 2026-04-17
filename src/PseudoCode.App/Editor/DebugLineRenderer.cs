using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace PseudoCode.App;

internal sealed class DebugLineRenderer : IBackgroundRenderer
{
    private IBrush _brush;
    private int? _lineNumber;

    public DebugLineRenderer(IBrush brush)
    {
        _brush = brush;
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void SetLine(int? lineNumber)
    {
        _lineNumber = lineNumber > 0 ? lineNumber : null;
    }

    public void SetBrush(IBrush brush)
    {
        _brush = brush;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_lineNumber is null || textView.Document is null || _lineNumber > textView.Document.LineCount)
        {
            return;
        }

        textView.EnsureVisualLines();
        var line = textView.Document.GetLineByNumber(_lineNumber.Value);
        var segment = new TextSegment
        {
            StartOffset = line.Offset,
            Length = Math.Max(1, line.Length)
        };

        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment, true))
        {
            drawingContext.DrawRectangle(_brush, null, new Avalonia.Rect(0, rect.Top, textView.Bounds.Width, rect.Height));
        }
    }
}
