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
                TryDeleteFile(GetCommentPath(item.ImagePath));
                TryDeleteFile(GetReminderPath(item.ImagePath));
                removed = true;
            }
        }

        if (removed)
            RaiseChanged();

        return Task.CompletedTask;
    }

    public Task DeleteOlderThanAsync(DateTime cutoff)
    {
        bool removed = false;
        lock (_lock)
        {
            var oldItems = _items.Where(x => x.CreatedAt < cutoff).ToList();
            foreach (var item in oldItems)
            {
                _items.Remove(item);
                TryDeleteFile(item.ImagePath);
                TryDeleteFile(item.ThumbnailPath);
                TryDeleteFile(GetCommentPath(item.ImagePath));
                TryDeleteFile(GetReminderPath(item.ImagePath));
            }
            removed = oldItems.Count > 0;
        }

        if (removed)
            RaiseChanged();

        return Task.CompletedTask;
    }

    public Task SetCommentAsync(Guid id, string comment)
    {
        bool updated = false;
        comment ??= string.Empty;
        lock (_lock)
        {
            var item = _items.FirstOrDefault(x => x.Id == id);
            if (item != null)
            {
                item.Comment = comment;
                var commentPath = GetCommentPath(item.ImagePath);
                try
                {
                    if (comment.Length > 0)
                        File.WriteAllText(commentPath, comment);
                    else
                        TryDeleteFile(commentPath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to save comment {commentPath}: {ex}");
                }
                updated = true;
            }
        }

        if (updated)
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
                TryDeleteFile(GetCommentPath(item.ImagePath));
                TryDeleteFile(GetReminderPath(item.ImagePath));
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

    public async Task<Guid> SaveAsync(SKBitmap bitmap, string? comment = null)
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

        if (!string.IsNullOrEmpty(comment))
            File.WriteAllText(GetCommentPath(imagePath), comment);

        var item = new HistoryItem
        {
            Id = id,
            CreatedAt = timestamp,
            ImagePath = imagePath,
            ThumbnailPath = thumbnailPath,
            Comment = comment ?? string.Empty
        };

        lock (_lock)
        {
            _items.Add(item);
            PruneOldItemsLocked();
        }

        RaiseChanged();
        return id;
    }

    public Task SetReminderAsync(Guid id, DateTime? when)
    {
        bool updated = false;
        lock (_lock)
        {
            var item = _items.FirstOrDefault(x => x.Id == id);
            if (item != null)
            {
                item.ReminderAt = when;
                var reminderPath = GetReminderPath(item.ImagePath);
                try
                {
                    if (when.HasValue)
                        File.WriteAllText(reminderPath, when.Value.ToString("o"));
                    else
                        TryDeleteFile(reminderPath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to save reminder {reminderPath}: {ex}");
                }
                updated = true;
            }
        }

        if (updated)
            RaiseChanged();

        return Task.CompletedTask;
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
            TryDeleteFile(GetCommentPath(oldest.ImagePath));
            TryDeleteFile(GetReminderPath(oldest.ImagePath));
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
                    ThumbnailPath = thumbnailPath,
                    Comment = LoadComment(imagePath),
                    ReminderAt = LoadReminder(imagePath)
                });
            }
        }

        lock (_lock)
        {
            _items.AddRange(loadedItems);
            PruneOldItemsLocked();
        }
    }

    private static string GetCommentPath(string imagePath) =>
        Path.Combine(
            Path.GetDirectoryName(imagePath) ?? string.Empty,
            $"comment_{Path.GetFileNameWithoutExtension(imagePath)}.txt");

    private static string GetReminderPath(string imagePath) =>
        Path.Combine(
            Path.GetDirectoryName(imagePath) ?? string.Empty,
            $"reminder_{Path.GetFileNameWithoutExtension(imagePath)}.txt");

    private static DateTime? LoadReminder(string imagePath)
    {
        var reminderPath = GetReminderPath(imagePath);
        try
        {
            if (File.Exists(reminderPath) &&
                DateTime.TryParse(File.ReadAllText(reminderPath), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var when))
            {
                return when;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load reminder {reminderPath}: {ex}");
        }
        return null;
    }

    private static string LoadComment(string imagePath)
    {
        var commentPath = GetCommentPath(imagePath);
        try
        {
            return File.Exists(commentPath) ? File.ReadAllText(commentPath) : string.Empty;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load comment {commentPath}: {ex}");
            return string.Empty;
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
