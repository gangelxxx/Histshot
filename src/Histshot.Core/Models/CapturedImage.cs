using SkiaSharp;

namespace Histshot.Core.Models;

public sealed class CapturedImage : IDisposable
{
    public SKBitmap Bitmap { get; }
    public int Width => Bitmap.Width;
    public int Height => Bitmap.Height;

    public CapturedImage(SKBitmap bitmap)
    {
        Bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
    }

    public void Dispose()
    {
        Bitmap.Dispose();
    }
}
