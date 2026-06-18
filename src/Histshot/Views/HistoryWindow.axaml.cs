using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Histshot.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Histshot.Views;

public partial class HistoryWindow : Window
{
    private readonly IHistoryService _historyService;
    private readonly IClipboardService _clipboardService;
    private List<HistoryItemViewModel> _viewModels = new();

    public HistoryWindow() : this(App.Services.GetRequiredService<IHistoryService>(), App.Services.GetRequiredService<IClipboardService>())
    {
    }

    public HistoryWindow(IHistoryService historyService, IClipboardService clipboardService)
    {
        _historyService = historyService;
        _clipboardService = clipboardService;
        InitializeComponent();
        Closed += OnClosed;
        _historyService.Changed += OnHistoryChanged;
        _ = LoadHistoryAsync();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _historyService.Changed -= OnHistoryChanged;

        if (HistoryItems.ItemsSource is IEnumerable<HistoryItemViewModel> viewModels)
        {
            foreach (var viewModel in viewModels)
            {
                viewModel.Dispose();
            }
        }
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        // May be raised off the UI thread (e.g. after a screenshot is saved); marshal to the UI thread.
        Dispatcher.UIThread.Post(() => _ = LoadHistoryAsync());
    }

    private async void ClearAllButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dialog = new ClearHistoryDialog
        {
            Icon = App.ThemedWindowIcon
        };

        var daysToKeep = await dialog.ShowDialog<int?>(this);
        if (daysToKeep is not int days)
            return;

        // History reload is triggered by the service's Changed event.
        if (days <= 0)
            await _historyService.ClearAsync();
        else
            await _historyService.DeleteOlderThanAsync(DateTime.Now.AddDays(-days));
    }

    private async void CopyButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: HistoryItemViewModel viewModel } && File.Exists(viewModel.ImagePath))
        {
            try
            {
                using var bitmap = SKBitmap.Decode(viewModel.ImagePath);
                if (bitmap != null)
                {
                    await _clipboardService.SetImageAsync(bitmap);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to copy history image: {ex}");
            }
        }
    }

    private async void CopyCommentButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: HistoryItemViewModel viewModel } && viewModel.HasComment)
        {
            try
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                    await clipboard.SetTextAsync(viewModel.Comment);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to copy comment text: {ex}");
            }
        }
    }

    private void OverlayButton_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Keep the press from bubbling to the thumbnail's preview handler.
        e.Handled = true;
    }

    private async void Thumbnail_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: HistoryItemViewModel viewModel } control || !File.Exists(viewModel.ImagePath))
        {
            return;
        }

        var properties = e.GetCurrentPoint(control).Properties;

        if (properties.IsRightButtonPressed)
        {
            // Right click: open in the external default editor (e.g. Paint).
            try
            {
                var launcher = TopLevel.GetTopLevel(this)?.Launcher;
                if (launcher != null)
                {
                    await launcher.LaunchFileInfoAsync(new FileInfo(viewModel.ImagePath));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to open history image: {ex}");
            }
        }
        else if (properties.IsLeftButtonPressed)
        {
            // Left click: open the in-app preview window with carousel navigation.
            try
            {
                var index = _viewModels.IndexOf(viewModel);
                if (index < 0)
                {
                    return;
                }

                var preview = new ImagePreviewWindow(_viewModels, index, _clipboardService)
                {
                    Icon = App.ThemedWindowIcon
                };
                preview.Show(this);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to preview history image: {ex}");
            }
        }
    }

    private async void ReminderButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: HistoryItemViewModel viewModel })
        {
            var dialog = new ReminderDialog(viewModel.ReminderAt)
            {
                Icon = App.ThemedWindowIcon
            };
            var result = await dialog.ShowDialog<ReminderResult?>(this);
            if (result == null)
                return;

            // History reload is triggered by the service's Changed event.
            await _historyService.SetReminderAsync(viewModel.Id, result.Remove ? null : result.When);
        }
    }

    /// <summary>Scrolls to the item's card and flashes it; used after a reminder notification click.</summary>
    public async void HighlightItem(Guid itemId)
    {
        // Wait until the (async) initial load has materialized the view models.
        for (int i = 0; i < 40 && _viewModels.Count == 0; i++)
            await Task.Delay(50);

        var viewModel = _viewModels.FirstOrDefault(vm => vm.Id == itemId);
        if (viewModel == null)
            return;

        HistoryItems.ScrollIntoView(_viewModels.IndexOf(viewModel));

        viewModel.IsHighlighted = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            viewModel.IsHighlighted = false;
        };
        timer.Start();
    }

    private async void DeleteButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: HistoryItemViewModel viewModel })
        {
            // History reload is triggered by the service's Changed event.
            await _historyService.DeleteAsync(viewModel.Id);
        }
    }

    private void CommentButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: HistoryItemViewModel viewModel })
        {
            if (!viewModel.IsEditingComment)
                viewModel.EditText = viewModel.Comment;
            viewModel.IsEditingComment = !viewModel.IsEditingComment;
        }
    }

    private async void SaveCommentButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control { Tag: HistoryItemViewModel viewModel })
        {
            await SaveCommentAsync(viewModel);
        }
    }

    private void CommentArea_DoubleTapped(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: HistoryItemViewModel viewModel } control)
            return;

        viewModel.EditText = viewModel.Comment;
        viewModel.IsEditingComment = true;

        // Focus the editor once it becomes visible and is laid out;
        // put the caret at the end instead of leaving the whole text selected.
        Dispatcher.UIThread.Post(() =>
        {
            var textBox = control.GetVisualAncestors()
                .OfType<Border>()
                .FirstOrDefault(b => b.Classes.Contains("card"))
                ?.GetVisualDescendants()
                .OfType<CommentTextBox>()
                .FirstOrDefault();
            if (textBox != null)
            {
                textBox.Focus();
                var end = textBox.Text?.Length ?? 0;
                textBox.SelectionStart = end;
                textBox.SelectionEnd = end;
                textBox.CaretIndex = end;
            }
        }, DispatcherPriority.Loaded);
    }

    private async void CommentTextBox_EnterPressed(object? sender, EventArgs e)
    {
        if (sender is Control { Tag: HistoryItemViewModel viewModel })
        {
            await SaveCommentAsync(viewModel);
        }
    }

    private async Task SaveCommentAsync(HistoryItemViewModel viewModel)
    {
        // History reload (closing the editor) is triggered by the service's Changed event.
        await _historyService.SetCommentAsync(viewModel.Id, viewModel.EditText.Trim());
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            if (HistoryItems.ItemsSource is IEnumerable<HistoryItemViewModel> oldViewModels)
            {
                foreach (var vm in oldViewModels)
                    vm.Dispose();
            }

            var items = await _historyService.GetRecentAsync(50);
            var viewModels = new List<HistoryItemViewModel>();

            foreach (var item in items)
            {
                Bitmap? thumbnail = null;
                if (File.Exists(item.ThumbnailPath))
                {
                    try
                    {
                        thumbnail = new Bitmap(item.ThumbnailPath);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to load thumbnail: {ex}");
                    }
                }

                viewModels.Add(new HistoryItemViewModel
                {
                    Id = item.Id,
                    CreatedAt = item.CreatedAt,
                    Thumbnail = thumbnail,
                    ImagePath = item.ImagePath,
                    Comment = item.Comment,
                    ReminderAt = item.ReminderAt
                });
            }

            _viewModels = viewModels;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load history: {ex}");
        }
    }

    private void SearchTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchTextBox.Text?.Trim();
        HistoryItems.ItemsSource = string.IsNullOrEmpty(query)
            ? _viewModels
            : _viewModels
                .Where(vm => vm.Comment.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
    }
}

public class HistoryItemViewModel : IDisposable, INotifyPropertyChanged
{
    private string _comment = string.Empty;
    private string _editText = string.Empty;
    private bool _isEditingComment;
    private DateTime? _reminderAt;
    private bool _isHighlighted;

    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public Bitmap? Thumbnail { get; set; }
    public string ImagePath { get; set; } = string.Empty;
    public string DisplayText => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
    public string DateText => CreatedAt.ToString("yyyy-MM-dd");
    public string TimeText => CreatedAt.ToString("HH:mm:ss");

    public string Comment
    {
        get => _comment;
        set
        {
            if (_comment != value)
            {
                _comment = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Comment)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasComment)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowCommentText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowCommentArea)));
            }
        }
    }

    public bool HasComment => !string.IsNullOrWhiteSpace(Comment);

    /// <summary>Comment text is shown only when it exists and is not being edited.</summary>
    public bool ShowCommentText => HasComment && !IsEditingComment;

    /// <summary>The full-width comment area (saved text and/or editor) below the card's main row.</summary>
    public bool ShowCommentArea => HasComment || IsEditingComment;

    public string EditText
    {
        get => _editText;
        set
        {
            if (_editText != value)
            {
                _editText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditText)));
            }
        }
    }

    public bool IsEditingComment
    {
        get => _isEditingComment;
        set
        {
            if (_isEditingComment != value)
            {
                _isEditingComment = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditingComment)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowCommentText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowCommentArea)));
            }
        }
    }

    public DateTime? ReminderAt
    {
        get => _reminderAt;
        set
        {
            if (_reminderAt != value)
            {
                _reminderAt = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReminderAt)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasReminder)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsReminderPending)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReminderText)));
            }
        }
    }

    public bool HasReminder => ReminderAt.HasValue;

    /// <summary>True while a set reminder is still in the future; drives the bell's accent color.</summary>
    public bool IsReminderPending => ReminderAt.HasValue && ReminderAt.Value > DateTime.Now;

    public string ReminderText => ReminderAt.HasValue
        ? $"{Localization.Localization.Get("ReminderLabel")}: {ReminderAt.Value:g}"
        : string.Empty;

    /// <summary>Transient highlight used after a reminder notification click.</summary>
    public bool IsHighlighted
    {
        get => _isHighlighted;
        set
        {
            if (_isHighlighted != value)
            {
                _isHighlighted = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHighlighted)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Dispose()
    {
        Thumbnail?.Dispose();
        Thumbnail = null;
    }
}

