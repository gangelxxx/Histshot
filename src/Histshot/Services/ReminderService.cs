using Avalonia.Threading;
using Histshot.Core.Models;
using Histshot.Core.Services;
using Histshot.Views;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Histshot.Services;

/// <summary>
/// Periodically checks history items for due reminders. When a reminder comes due,
/// it is cleared and a tray-area notification is shown; clicking the notification
/// opens the history window scrolled to (and highlighting) the item's card.
/// </summary>
public class ReminderService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);

    private readonly IHistoryService _historyService;
    private readonly IClipboardService _clipboardService;
    private readonly HashSet<Guid> _fired = new();
    private DispatcherTimer? _timer;

    public ReminderService(IHistoryService historyService, IClipboardService clipboardService)
    {
        _historyService = historyService;
        _clipboardService = clipboardService;
    }

    public void Start()
    {
        _timer = new DispatcherTimer { Interval = CheckInterval };
        _timer.Tick += async (_, _) => await CheckDueRemindersAsync();
        _timer.Start();

        // Catch reminders that came due while the app was not running.
        Dispatcher.UIThread.Post(async () => await CheckDueRemindersAsync());
    }

    private async System.Threading.Tasks.Task CheckDueRemindersAsync()
    {
        try
        {
            var now = DateTime.Now;
            var items = await _historyService.GetRecentAsync(int.MaxValue);
            var due = items
                .Where(x => x.ReminderAt.HasValue && x.ReminderAt.Value <= now && !_fired.Contains(x.Id))
                .ToList();

            foreach (var item in due)
            {
                _fired.Add(item.Id);
                // Persist the removal; the Changed event reloads any open history window.
                await _historyService.SetReminderAsync(item.Id, null);
                ShowNotification(item);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Reminder check failed: {ex}");
        }
    }

    private void ShowNotification(HistoryItem item)
    {
        var notification = new ReminderNotificationWindow(item.Comment, item.ReminderAt ?? DateTime.Now);
        notification.Clicked += (_, _) => OpenHistoryAt(item.Id);
        notification.Show();
    }

    private void OpenHistoryAt(Guid itemId)
    {
        var historyWindow = new HistoryWindow(_historyService, _clipboardService)
        {
            ShowInTaskbar = true,
            Icon = App.ThemedWindowIcon
        };
        historyWindow.Show();
        historyWindow.HighlightItem(itemId);
    }
}
