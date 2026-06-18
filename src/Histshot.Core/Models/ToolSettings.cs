using SkiaSharp;

namespace Histshot.Core.Models;

public class ToolSettings
{
    public ToolType Tool { get; set; } = ToolType.Pencil;
    public SKColor Color { get; set; } = SKColors.Red;
    public float LineThickness { get; set; } = 3f;
    public float FontSize { get; set; } = 18f;
}
