namespace Histshot.Core.Models;

public class ScreenInfo
{
    public string Name { get; init; } = string.Empty;
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double Scaling { get; init; } = 1.0;

    public bool Contains(int x, int y)
    {
        return x >= X && x < X + Width && y >= Y && y < Y + Height;
    }
}
