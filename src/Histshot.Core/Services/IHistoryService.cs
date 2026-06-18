using Histshot.Core.Models;
using SkiaSharp;

namespace Histshot.Core.Services;

public interface IHistoryService
{
    /// <summary>Raised whenever the history changes (item added, deleted or cleared).</summary>
    event EventHandler? Changed;

    Task SaveAsync(SKBitmap bitmap);
    Task<IReadOnlyList<HistoryItem>> GetRecentAsync(int count);
    Task DeleteAsync(Guid id);
    Task ClearAsync();
}
