namespace Histshot.Core.Models;

public class HistoryItem
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string ThumbnailPath { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
}
