using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Histshot.Core.Services;
using SkiaSharp;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Histshot.Services;

public class ClipboardService : IClipboardService
{
    public async Task SetImageAsync(SKBitmap bitmap, object? context = null)
    {
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = encoded.AsStream();

        // We intentionally do not dispose bitmapImage: Avalonia's clipboard implementation
        // may need to keep the bitmap alive while the OS takes ownership of the clipboard data.
        var bitmapImage = new Bitmap(stream);

        var topLevel = context as TopLevel ?? FindAnyTopLevel();
        if (topLevel?.Clipboard == null)
        {
            Console.WriteLine("Clipboard is not available: no TopLevel found.");
            return;
        }

        await topLevel.Clipboard.SetBitmapAsync(bitmapImage);
    }

    private static TopLevel? FindAnyTopLevel()
    {
        if (App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow
                ?? desktop.Windows.FirstOrDefault();
        }
        return null;
    }
}
