using Histshot.Core.Capture;
using Histshot.Core.Models;

namespace Histshot.Core.Services;

public class ScreenCaptureService : IScreenCaptureService
{
    private readonly IScreenCaptureProvider _provider;

    public ScreenCaptureService(IScreenCaptureProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public IReadOnlyList<ScreenInfo> GetScreens()
    {
        return _provider.GetScreens();
    }

    public Task<CapturedImage> CaptureRegionAsync(int x, int y, int width, int height)
    {
        var bitmap = _provider.CaptureRegion(x, y, width, height);
        return Task.FromResult(new CapturedImage(bitmap));
    }

    public Task<CapturedImage> CaptureScreenAsync(ScreenInfo screen)
    {
        var bitmap = _provider.CaptureScreen(screen);
        return Task.FromResult(new CapturedImage(bitmap));
    }

    public Task<CapturedImage> CaptureAllScreensAsync()
    {
        var screens = _provider.GetScreens();
        if (screens.Count == 0)
        {
            throw new InvalidOperationException("No screens detected.");
        }

        int minX = screens.Min(s => s.X);
        int minY = screens.Min(s => s.Y);
        int maxX = screens.Max(s => s.X + s.Width);
        int maxY = screens.Max(s => s.Y + s.Height);

        return CaptureRegionAsync(minX, minY, maxX - minX, maxY - minY);
    }
}
