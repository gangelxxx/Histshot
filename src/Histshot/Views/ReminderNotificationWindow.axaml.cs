using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using System;

namespace Histshot.Views;

/// <summary>
/// Tray-area reminder notification: a small topmost window near the taskbar corner.
/// Clicking it invokes <see cref="Clicked"/> (open the history item); it closes
/// itself after a timeout.
/// </summary>
public partial class ReminderNotificationWindow : Window
{
    private static readonly TimeSpan AutoCloseDelay = TimeSpan.FromSeconds(12);

    private readonly DispatcherTimer _autoCloseTimer;

    public event EventHandler? Clicked;

    public ReminderNotificationWindow() : this(string.Empty, DateTime.Now)
    {
    }

    public ReminderNotificationWindow(string comment, DateTime reminderAt)
    {
        InitializeComponent();

        CommentText.Text = string.IsNullOrWhiteSpace(comment) ? string.Empty : comment;
        CommentText.IsVisible = !string.IsNullOrWhiteSpace(comment);
        TimeText.Text = reminderAt.ToString("g");

        _autoCloseTimer = new DispatcherTimer { Interval = AutoCloseDelay };
        _autoCloseTimer.Tick += (_, _) =>
        {
            _autoCloseTimer.Stop();
            Close();
        };
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Dock to the bottom-right corner of the work area (above the taskbar),
        // once the final size is known.
        Dispatcher.UIThread.Post(() =>
        {
            var screen = Screens.Primary;
            if (screen != null)
            {
                var area = screen.WorkingArea;
                Position = new Avalonia.PixelPoint(
                    area.Right - (int)(Bounds.Width * screen.Scaling) - 12,
                    area.Bottom - (int)(Bounds.Height * screen.Scaling) - 12);
            }
        }, DispatcherPriority.Loaded);

        _autoCloseTimer.Start();
    }

    private void Root_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _autoCloseTimer.Stop();
        Clicked?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        Close();
    }
}
