using Histshot.Core.Models;
using SkiaSharp;

namespace Histshot.Core.Capture;

public interface IScreenCaptureProvider
{
    IReadOnlyList<ScreenInfo> GetScreens();
    SKBitmap CaptureRegion(int x, int y, int width, int height);
    SKBitmap CaptureScreen(ScreenInfo screen);
}
