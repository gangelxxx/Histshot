using System.Text;
using Histshot.Core.Models;
using SkiaSharp;

namespace Histshot.Core.Drawing;

public static class DrawingRenderer
{
    public static void Render(SKCanvas canvas, IEnumerable<DrawingOperation> operations)
    {
        foreach (var operation in operations)
        {
            RenderOperation(canvas, operation);
        }
    }

    private static void RenderOperation(SKCanvas canvas, DrawingOperation operation)
    {
        switch (operation)
        {
            case StrokeOperation stroke:
                RenderStroke(canvas, stroke);
                break;
            case TextOperation text:
                RenderText(canvas, text);
                break;
            default:
                throw new NotSupportedException($"Drawing operation '{operation.GetType().Name}' is not supported.");
        }
    }

    private static void RenderStroke(SKCanvas canvas, StrokeOperation stroke)
    {
        if (stroke.Points.Count < 2)
            return;

        if (stroke.Tool == ToolType.Rectangle)
        {
            RenderRectangle(canvas, stroke);
            return;
        }

        using var paint = new SKPaint
        {
            Color = stroke.Color,
            StrokeWidth = stroke.Thickness,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };

        using var path = new SKPath();
        path.MoveTo(stroke.Points[0]);
        for (int i = 1; i < stroke.Points.Count; i++)
        {
            path.LineTo(stroke.Points[i]);
        }

        canvas.DrawPath(path, paint);

        if (stroke.IsArrow)
        {
            DrawArrowHead(canvas, stroke.Points[^2], stroke.Points[^1], stroke.Color, stroke.Thickness);
        }
    }

    private static void RenderRectangle(SKCanvas canvas, StrokeOperation stroke)
    {
        var a = stroke.Points[0];
        var b = stroke.Points[^1];

        using var paint = new SKPaint
        {
            Color = stroke.Color,
            StrokeWidth = stroke.Thickness,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeJoin = SKStrokeJoin.Miter
        };

        var rect = new SKRect(
            MathF.Min(a.X, b.X),
            MathF.Min(a.Y, b.Y),
            MathF.Max(a.X, b.X),
            MathF.Max(a.Y, b.Y));
        canvas.DrawRect(rect, paint);
    }

    private static void DrawArrowHead(SKCanvas canvas, SKPoint from, SKPoint to, SKColor color, float thickness)
    {
        const float arrowLength = 20f;
        const float arrowAngle = 0.5f;

        float angle = MathF.Atan2(to.Y - from.Y, to.X - from.X);

        var left = new SKPoint(
            to.X - arrowLength * MathF.Cos(angle - arrowAngle),
            to.Y - arrowLength * MathF.Sin(angle - arrowAngle));

        var right = new SKPoint(
            to.X - arrowLength * MathF.Cos(angle + arrowAngle),
            to.Y - arrowLength * MathF.Sin(angle + arrowAngle));

        using var paint = new SKPaint
        {
            Color = color,
            StrokeWidth = thickness,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };

        canvas.DrawLine(to, left, paint);
        canvas.DrawLine(to, right, paint);
    }

    private static void RenderText(SKCanvas canvas, TextOperation text)
    {
        using var font = new SKFont(SKTypeface.Default, text.FontSize);
        using var paint = new SKPaint
        {
            Color = text.Color,
            IsAntialias = true
        };

        font.GetFontMetrics(out var metrics);
        float lineHeight = metrics.Descent - metrics.Ascent + metrics.Leading;
        float maxWidth = text.Width > 0 ? text.Width : float.MaxValue;

        float baselineY = text.Position.Y - metrics.Ascent;
        foreach (var line in WrapText(text.Text, font, maxWidth))
        {
            canvas.DrawText(line, text.Position.X, baselineY, SKTextAlign.Left, font, paint);
            baselineY += lineHeight;
        }
    }

    private static IEnumerable<string> WrapText(string text, SKFont font, float maxWidth)
    {
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (maxWidth == float.MaxValue || font.MeasureText(line) <= maxWidth)
            {
                yield return line;
                continue;
            }

            var current = new StringBuilder();
            foreach (var word in line.Split(' '))
            {
                if (current.Length == 0)
                {
                    current.Append(word);
                    continue;
                }

                if (font.MeasureText($"{current} {word}") <= maxWidth)
                {
                    current.Append(' ').Append(word);
                }
                else
                {
                    yield return current.ToString();
                    current.Clear();
                    current.Append(word);
                }
            }

            yield return current.ToString();
        }
    }
}
