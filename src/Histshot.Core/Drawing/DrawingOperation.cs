using Histshot.Core.Models;
using SkiaSharp;

namespace Histshot.Core.Drawing;

public abstract class DrawingOperation
{
    public SKColor Color { get; set; }
    public float Thickness { get; set; }
}

public class StrokeOperation : DrawingOperation
{
    public List<SKPoint> Points { get; } = new();
    public bool IsArrow { get; set; }
    public ToolType Tool { get; set; }
}

public class TextOperation : DrawingOperation
{
    public string Text { get; set; } = string.Empty;
    public SKPoint Position { get; set; }
    public float FontSize { get; set; } = 18f;

    // Size of the text box the user typed into. Width is the wrap boundary;
    // text reflows to fit it. Height is kept so the editor reopens at the same size.
    public float Width { get; set; } = 200f;
    public float Height { get; set; } = 60f;
}
