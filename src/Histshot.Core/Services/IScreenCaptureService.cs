using Histshot.Core.Models;

namespace Histshot.Core.Services;

public interface IScreenCaptureService
{
    IReadOnlyList<ScreenInfo> GetScreens();
    Task<CapturedImage> CaptureRegionAsync(int x, int y, int width, int height);
    Task<CapturedImage> CaptureScreenAsync(ScreenInfo screen);
    Task<CapturedImage> CaptureAllScreensAsync();
}
