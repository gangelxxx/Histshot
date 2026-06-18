using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Histshot.Core.Services;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace Histshot.Views;

public partial class ImagePreviewWindow : Window
{
    private readonly IReadOnlyList<HistoryItemViewModel> _items = Array.Empty<HistoryItemViewModel>();
    private readonly IClipboardService? _clipboardService;
    private int _index;
    private Bitmap? _bitmap;

    public ImagePreviewWindow()
    {
        InitializeComponent();
    }

    public ImagePreviewWindow(IReadOnlyList<HistoryItemViewModel> items, int index, IClipboardService clipboardService) : this()
    {
        _items = items;
        _index = index;
        _clipboardService = clipboardService;

        KeyDown += OnKeyDown;
        Closed += OnClosed;

        var hasMultiple = _items.Count > 1;
        PrevButton.IsVisible = hasMultiple;
        NextButton.IsVisible = hasMultiple;

        LoadCurrent(sizeToImage: true);
    }

    private void LoadCurrent(bool sizeToImage = false)
    {
        if (_index < 0 || _index >= _items.Count)
        {
            return;
        }

        _bitmap?.Dispose();
        _bitmap = null;
        PreviewImage.Source = null;

        var item = _items[_index];
        Title = item.DisplayText;

        if (!File.Exists(item.ImagePath))
        {
            return;
        }

        try
        {
            _bitmap = new Bitmap(item.ImagePath);
            PreviewImage.Source = _bitmap;
            if (sizeToImage)
            {
                SizeToImage(_bitmap.PixelSize);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load preview image: {ex}");
        }
    }

    private void ShowDelta(int delta)
    {
        if (_items.Count <= 1)
        {
            return;
        }

        _index = (_index + delta + _items.Count) % _items.Count;
        LoadCurrent();
    }

    private void Prev_Click(object? sender, RoutedEventArgs e) => ShowDelta(-1);

    private void Next_Click(object? sender, RoutedEventArgs e) => ShowDelta(1);

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (_clipboardService == null || _index < 0 || _index >= _items.Count)
        {
            return;
        }

        var item = _items[_index];
        if (!File.Exists(item.ImagePath))
        {
            return;
        }

        try
        {
            using var bitmap = SKBitmap.Decode(item.ImagePath);
            if (bitmap != null)
            {
                await _clipboardService.SetImageAsync(bitmap);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to copy preview image: {ex}");
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Left:
                ShowDelta(-1);
                break;
            case Key.Right:
                ShowDelta(1);
                break;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _bitmap?.Dispose();
        _bitmap = null;
    }

    private void SizeToImage(PixelSize size)
    {
        const double maxWidth = 1400, maxHeight = 900, chrome = 16;

        double w = size.Width;
        double h = size.Height;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        double scale = Math.Min(1.0, Math.Min(maxWidth / w, maxHeight / h));
        Width = Math.Max(MinWidth, w * scale + chrome);
        Height = Math.Max(MinHeight, h * scale + chrome);
    }
}
