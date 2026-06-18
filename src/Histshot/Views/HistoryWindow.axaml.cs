using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Histshot.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using System;
using System.Collections.Generic;
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

        if (HistoryListBox.ItemsSource is IEnumerable<HistoryItemViewModel> viewModels)
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
        // History reload is triggered by the service's Changed event.
        await _historyService.ClearAsync();
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

    private async void DeleteButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: HistoryItemViewModel viewModel })
        {
            // History reload is triggered by the service's Changed event.
            await _historyService.DeleteAsync(viewModel.Id);
        }
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            if (HistoryListBox.ItemsSource is IEnumerable<HistoryItemViewModel> oldViewModels)
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
                    ImagePath = item.ImagePath
                });
            }

            _viewModels = viewModels;
            HistoryListBox.ItemsSource = viewModels;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load history: {ex}");
        }
    }
}

public class HistoryItemViewModel : IDisposable
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public Bitmap? Thumbnail { get; set; }
    public string ImagePath { get; set; } = string.Empty;
    public string DisplayText => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
    public string DateText => CreatedAt.ToString("yyyy-MM-dd");
    public string TimeText => CreatedAt.ToString("HH:mm:ss");

    public void Dispose()
    {
        Thumbnail?.Dispose();
        Thumbnail = null;
    }
}

