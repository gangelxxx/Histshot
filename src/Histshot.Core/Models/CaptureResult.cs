namespace Histshot.Core.Models;

public sealed class CaptureResult
{
    public CapturedImage Image { get; }
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public double Scaling { get; }

    public CaptureResult(CapturedImage image, int x, int y, int width, int height, double scaling = 1.0)
    {
        Image = image ?? throw new ArgumentNullException(nameof(image));
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Scaling = scaling;
    }
}
