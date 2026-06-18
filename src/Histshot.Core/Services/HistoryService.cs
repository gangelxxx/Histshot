using Histshot.Core.Models;
using Histshot.Core.Services;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Histshot.Core.Services;

public class HistoryService : IHistoryService
{
    private const int DefaultMaxHistoryItems = 100;

    private readonly string _historyFolder;
    private readonly int _maxHistoryItems;
    private readonly List<HistoryItem> _items = new();
    private readonly object _lock = new();

    public event EventHandler? Changed;

    public HistoryService() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Histshot", "History"), DefaultMaxHistoryItems)
    {
    }

    public HistoryService(string historyFolder, int maxHistoryItems = DefaultMaxHistoryItems)
    {
        _historyFolder = historyFolder ?? throw new ArgumentNullException(nameof(historyFolder));
        _maxHistoryItems = maxHistoryItems > 0 ? maxHistoryItems : throw new ArgumentOutOfRangeException(nameof(maxHistoryItems));
        Directory.CreateDirectory(_historyFolder);
        LoadExistingItems();
    }

    public Task DeleteAsync(Guid id)
    {
        bool removed = false;
        lock (_lock)
        {
            var item = _items.FirstOrDefault(x => x.Id == id);
            if (item != null)
            {
                _items.Remove(item);
                TryDeleteFile(item.ImagePath);
                TryDeleteFile(item.ThumbnailPath);
                removed = true;
            }
        }

        if (removed)
            RaiseChanged();

        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        bool hadItems;
        lock (_lock)
        {
            hadItems = _items.Count > 0;
            foreach (var item in _items)
            {
                TryDeleteFile(item.ImagePath);
                TryDeleteFile(item.ThumbnailPath);
            }
            _items.Clear();
        }

        if (hadItems)
            RaiseChanged();

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<HistoryItem>> GetRecentAsync(int count)
    {
        lock (_lock)
        {
            var result = _items
                .OrderByDescending(x => x.CreatedAt)
                .Take(count)
                .ToList();
            return Task.FromResult<IReadOnlyList<HistoryItem>>(result);
        }
    }

    public async Task SaveAsync(SKBitmap bitmap)
    {
        if (bitmap == null)
            throw new ArgumentNullException(nameof(bitmap));

        var id = Guid.NewGuid();
        var timestamp = DateTime.Now;
        var fileName = $"{timestamp:yyyyMMdd_HHmmss}_{id:N}.png";
        var imagePath = Path.Combine(_historyFolder, fileName);
        var thumbnailPath = Path.Combine(_historyFolder, $"thumb_{fileName}");

        await SaveBitmapAsync(bitmap, imagePath);
        await SaveThumbnailAsync(bitmap, thumbnailPath, 120);

        var item = new HistoryItem
        {
            Id = id,
            CreatedAt = timestamp,
            ImagePath = imagePath,
            ThumbnailPath = thumbnailPath
        };

        lock (_lock)
        {
            _items.Add(item);
            PruneOldItemsLocked();
        }

        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private void PruneOldItemsLocked()
    {
        while (_items.Count > _maxHistoryItems)
        {
            var oldest = _items.OrderBy(x => x.CreatedAt).First();
            _items.Remove(oldest);
            TryDeleteFile(oldest.ImagePath);
            TryDeleteFile(oldest.ThumbnailPath);
        }
    }

    private void LoadExistingItems()
    {
        if (!Directory.Exists(_historyFolder))
            return;

        var files = Directory.GetFiles(_historyFolder, "*.png")
            .Where(f => !Path.GetFileName(f).StartsWith("thumb_", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f);

        var loadedItems = new List<HistoryItem>();

        foreach (var imagePath in files)
        {
            var fileName = Path.GetFileNameWithoutExtension(imagePath);
            var parts = fileName.Split('_');

            if (parts.Length >= 3 &&
                DateTime.TryParseExact(
                    $"{parts[0]}_{parts[1]}",
                    "yyyyMMdd_HHmmss",
                    null,
                    System.Globalization.DateTimeStyles.None,
                    out var timestamp) &&
                Guid.TryParse(parts[2], out var id))
            {
                var thumbnailPath = Path.Combine(_historyFolder, $"thumb_{fileName}.png");

                loadedItems.Add(new HistoryItem
                {
                    Id = id,
                    CreatedAt = timestamp,
                    ImagePath = imagePath,
                    ThumbnailPath = thumbnailPath
                });
            }
        }

        lock (_lock)
        {
            _items.AddRange(loadedItems);
            PruneOldItemsLocked();
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to delete file {path}: {ex}");
        }
    }

    private static async Task SaveBitmapAsync(SKBitmap bitmap, string path)
    {
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        await File.WriteAllBytesAsync(path, data.ToArray());
    }

    private static async Task SaveThumbnailAsync(SKBitmap bitmap, string path, int maxSize)
    {
        var scale = Math.Min((float)maxSize / bitmap.Width, (float)maxSize / bitmap.Height);
        var width = Math.Max(1, (int)(bitmap.Width * scale));
        var height = Math.Max(1, (int)(bitmap.Height * scale));

        using var thumbnail = bitmap.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        if (thumbnail == null)
            throw new InvalidOperationException("Failed to resize thumbnail.");

        using var data = thumbnail.Encode(SKEncodedImageFormat.Png, 100);
        await File.WriteAllBytesAsync(path, data.ToArray());
    }
}
