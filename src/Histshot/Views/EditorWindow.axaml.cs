using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using Histshot.Core.Models;
using Histshot.Core.Services;
using SkiaSharp;
using System;

namespace Histshot.Views;

public partial class EditorWindow : Window
{
    private readonly CapturedImage _capturedImage;
    private readonly int _captureX;
    private readonly int _captureY;
    private readonly int _captureWidth;
    private readonly int _captureHeight;
    private readonly double _captureScaling;

    public EditorWindow() : this(CreateBlankImage(), 0, 0, 1, 1, 1.0)
    {
    }

    public EditorWindow(CapturedImage capturedImage, int x, int y, int width, int height, double scaling)
    {
        _capturedImage = capturedImage;
        _captureX = x;
        _captureY = y;
        _captureWidth = width;
        _captureHeight = height;
        _captureScaling = scaling;

        InitializeComponent();
        Position = new PixelPoint(x, y);
        Width = width / scaling;
        Height = height / scaling;

        Editor.CloseRequested += (_, _) => Close();
        Closed += OnClosed;
        Opened += OnOpened;
        KeyDown += OnKeyDown;

        Toolbar.ToolSelected += (_, tool) => Editor.SetTool(tool);
        Toolbar.ColorChanged += (_, color) => Editor.SetColor(color);
        Toolbar.ThicknessChanged += (_, thickness) => Editor.SetLineThickness(thickness);
        Toolbar.FontSizeChanged += (_, fontSize) => Editor.SetFontSize(fontSize);
        Toolbar.CopyRequested += async (_, _) => await Editor.CopyAsync();
        Toolbar.CancelRequested += (_, _) => Editor.Cancel();
        Toolbar.LayoutUpdated += (_, _) => PositionToolbar();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var renderScaling = this.RenderScaling;
        DebugLogger.Log($"Editor OnOpened: renderScaling={renderScaling}, Position={Position}, Size={Width}x{Height}");

        Width = _captureWidth / renderScaling;
        Height = _captureHeight / renderScaling;

        Editor.SetImage(_capturedImage, renderScaling);
        Editor.UpdateSizes(renderScaling);

        Dispatcher.UIThread.Post(() =>
        {
            ForceWindowPosition(_captureX, _captureY);
            PositionToolbar();
        }, DispatcherPriority.Loaded);
    }

    private void PositionToolbar()
    {
        const double margin = 8;
        var toolbarSize = Toolbar.DesiredSize;
        if (toolbarSize.Width <= 0 || toolbarSize.Height <= 0)
            return;

        var w = this.Bounds.Width;
        var h = this.Bounds.Height;
        var canvasW = Editor.Bounds.Width;
        var canvasH = Editor.Bounds.Height;

        var spaceBottom = h - canvasH - margin;
        var spaceTop = margin;
        var spaceRight = w - canvasW - margin;
        var spaceLeft = margin;

        double x, y;

        if (spaceBottom >= toolbarSize.Height)
        {
            x = (canvasW - toolbarSize.Width) / 2.0;
            y = canvasH + margin;
        }
        else if (spaceTop >= toolbarSize.Height)
        {
            x = (canvasW - toolbarSize.Width) / 2.0;
            y = -margin - toolbarSize.Height;
        }
        else if (spaceRight >= toolbarSize.Width)
        {
            x = canvasW + margin;
            y = (canvasH - toolbarSize.Height) / 2.0;
        }
        else if (spaceLeft >= toolbarSize.Width)
        {
            x = -margin - toolbarSize.Width;
            y = (canvasH - toolbarSize.Height) / 2.0;
        }
        else
        {
            x = (canvasW - toolbarSize.Width) / 2.0;
            y = canvasH + margin;
        }

        if (x < margin) x = margin;
        if (y < margin) y = margin;
        if (x + toolbarSize.Width > w - margin) x = w - margin - toolbarSize.Width;
        if (y + toolbarSize.Height > h - margin) y = h - margin - toolbarSize.Height;

        Canvas.SetLeft(Toolbar, x);
        Canvas.SetTop(Toolbar, y);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _capturedImage.Dispose();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            Editor.DeleteSelected();
            e.Handled = true;
        }
    }

    private static CapturedImage CreateBlankImage()
    {
        using var bitmap = new SKBitmap(1, 1);
        bitmap.Erase(SKColors.White);
        var copy = bitmap.Copy() ?? throw new InvalidOperationException("Failed to create blank image.");
        return new CapturedImage(copy);
    }

    private void ForceWindowPosition(int x, int y)
    {
        try
        {
            var handle = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero)
                return;

            SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"ForceWindowPosition failed: {ex}");
        }
    }

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
