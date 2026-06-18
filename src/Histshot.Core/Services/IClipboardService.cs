using SkiaSharp;

namespace Histshot.Core.Services;

public interface IClipboardService
{
    Task SetImageAsync(SKBitmap bitmap, object? context = null);
}
