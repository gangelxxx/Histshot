using Histshot.Core.Models;
using SkiaSharp;

namespace Histshot.Core.Services;

public interface IHistoryService
{
    /// <summary>Raised whenever the history changes (item added, deleted or cleared).</summary>
    event EventHandler? Changed;

    Task<Guid> SaveAsync(SKBitmap bitmap, string? comment = null);
    Task<IReadOnlyList<HistoryItem>> GetRecentAsync(int count);
    Task DeleteAsync(Guid id);
    Task ClearAsync();

    /// <summary>Deletes all items created before <paramref name="cutoff"/> (local time).</summary>
    Task DeleteOlderThanAsync(DateTime cutoff);

    /// <summary>Saves a user comment for an item; an empty comment removes it.</summary>
    Task SetCommentAsync(Guid id, string comment);

    /// <summary>Sets or clears (null) a reminder for an item.</summary>
    Task SetReminderAsync(Guid id, DateTime? when);
}
