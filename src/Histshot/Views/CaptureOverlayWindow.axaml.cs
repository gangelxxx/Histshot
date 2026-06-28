using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Histshot.Core.Models;
using Histshot.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Histshot.Views;

public partial class CaptureOverlayWindow : Window
{
    private const int HandleSize = 10;
    private const int HandleHitPadding = 16;

    private readonly IScreenCaptureService _captureService;
    private readonly PixelRect _screenBounds;
    private readonly double _scaling;

    private Point _startPoint;
    private Rect _initialRect;
    private SelectionRectangle? _selection;
    private readonly List<Rectangle> _dimmingRects = new();
    private readonly List<Rectangle> _handles = new();
    private Image _backgroundImage;
    private Bitmap? _backgroundBitmap;
    private EditorControl? _editorControl;
    private ToolbarControl? _toolbarControl;
    private CapturedImage? _capturedImage;

    private bool _isSelecting;
    private bool _isMoving;
    private bool _isResizing;
    private int _resizeHandle = -1;
    private bool _isFinalized;
    private bool _isEditMode;
    private Action? _closeAllOverlays;
    private ToolType _currentTool = ToolType.Pencil;

    public Action<CaptureResult>? OnCaptured { get; set; }

    // Raised when the user begins a new selection on this overlay, so sibling overlays on other
    // monitors can clear theirs — only one selection should exist across all screens at a time.
    public Action? SelectionStarted { get; set; }

    public CaptureOverlayWindow() : this(App.Services.GetRequiredService<IScreenCaptureService>(), new PixelRect(0, 0, 1, 1), 1.0, null, null)
    {
    }

    public CaptureOverlayWindow(IScreenCaptureService captureService, PixelRect screenBounds, double scaling, Action? closeAllOverlays, CapturedImage? capturedImage = null)
    {
        _captureService = captureService;
        _screenBounds = screenBounds;
        _scaling = scaling;
        _closeAllOverlays = closeAllOverlays;
        _capturedImage = capturedImage;
        InitializeComponent();

        var canvas = new Canvas { ClipToBounds = false };
        Content = canvas;

        _backgroundImage = new Image
        {
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };
        canvas.Children.Add(_backgroundImage);

        for (int i = 0; i < 4; i++)
        {
            var dimming = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)),
                IsHitTestVisible = false
            };
            _dimmingRects.Add(dimming);
            canvas.Children.Add(dimming);
        }

        for (int i = 0; i < 8; i++)
        {
            var handle = new Rectangle
            {
                Width = HandleSize,
                Height = HandleSize,
                Fill = Brushes.White,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                // Keep handles above the editor image and the selection frame so they stay
                // visible and grabbable while editing.
                ZIndex = 1,
                IsVisible = false
            };
            _handles.Add(handle);
            canvas.Children.Add(handle);
        }

        _editorControl = new EditorControl();
        _editorControl.CloseRequested += OnEditorCloseRequested;
        _editorControl.EditCanceled += OnEditorEditCanceled;
        _editorControl.IsVisible = false;
        canvas.Children.Add(_editorControl);

        _toolbarControl = new ToolbarControl();
        _toolbarControl.ToolSelected += OnToolSelected;
        _toolbarControl.ColorChanged += OnColorChanged;
        _toolbarControl.ThicknessChanged += OnThicknessChanged;
        _toolbarControl.FontSizeChanged += OnFontSizeChanged;
        _toolbarControl.CopyRequested += OnCopyRequested;
        _toolbarControl.CancelRequested += OnCancelRequested;
        _toolbarControl.LayoutUpdated += (_, _) => PositionToolbar();
        _toolbarControl.IsVisible = false;
        canvas.Children.Add(_toolbarControl);

        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _capturedImage?.Dispose();
        _capturedImage = null;
        _backgroundBitmap?.Dispose();
        _backgroundBitmap = null;
    }

    public void StartCapture()
    {
        DebugLogger.Log($"=== StartCapture screenBounds={_screenBounds}, monitorScaling={_scaling}, capture={_capturedImage?.Width}x{_capturedImage?.Height} ===");

        // The window is first shown at a default location/size on the primary monitor and only
        // then moved & resized to fill the target monitor. Keep it invisible until it covers the
        // screen so the user never sees it "grow into place".
        Opacity = 0;

        Show();
        Activate();
        LogState("after Show()");

        // Position and size the window in physical pixels directly on the target monitor.
        Dispatcher.UIThread.Post(() =>
        {
            LogState("Loaded tick (before SetWindowPos)");
            // Moving the window onto a monitor with a different DPI raises WM_DPICHANGED,
            // after which Avalonia shrinks the window to the DPI-scaled "suggested" rect
            // (e.g. 2560x1600 -> 2240x1400 going 2.0 -> 1.75), so a single call leaves the
            // overlay smaller than the screen. The first call relocates and triggers the DPI
            // change; the second (no DPI change now, same monitor) makes the full-monitor
            // size stick.
            ForcePhysicalBounds("#1");
            ForcePhysicalBounds("#2");
            ApplyContentLayout("Loaded");

            // Re-assert after Avalonia finishes any deferred relayout from the DPI change,
            // in case the shrink lands after this tick on some setups, then reveal the overlay.
            Dispatcher.UIThread.Post(() =>
            {
                ForcePhysicalBounds("#3 deferred");
                ApplyContentLayout("deferred");
                Opacity = 1;
            }, DispatcherPriority.Background);
        }, DispatcherPriority.Loaded);
    }

    private void ForcePhysicalBounds(string tag)
    {
        var handle = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
        {
            // After a cross-DPI move Avalonia leaves ClientSize computed at the *old* DPI
            // (e.g. 1280 DIP for a 2560px window at 1.75 instead of 1462.857), so the visual
            // tree paints only part of the window. A same-size SetWindowPos raises no WM_SIZE,
            // so it never recomputes. Nudge the size by 1px and back: the corrective resize
            // forces a fresh WM_SIZE at the now-settled DPI, and ClientSize lands correct.
            SetWindowPos(handle, IntPtr.Zero, _screenBounds.X, _screenBounds.Y, _screenBounds.Width - 1, _screenBounds.Height - 1, SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            SetWindowPos(handle, IntPtr.Zero, _screenBounds.X, _screenBounds.Y, _screenBounds.Width, _screenBounds.Height, SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
        LogState($"after SetWindowPos {tag}");
    }

    private void ApplyContentLayout(string tag)
    {
        // Size content against the scaling Avalonia is actually rendering with. The native
        // window is forced to the monitor's physical size above, so Width = physical /
        // RenderScaling fills it exactly regardless of whether Avalonia adopted the
        // monitor's DPI or kept the primary monitor's.
        var renderScaling = this.RenderScaling;
        Width = _screenBounds.Width / renderScaling;
        Height = _screenBounds.Height / renderScaling;
        LoadBackgroundImage(renderScaling);

        LogState($"ApplyContentLayout {tag}");
        DebugLogger.Log($"    -> set Width={Width}, Height={Height}, bgImage={_backgroundImage?.Width}x{_backgroundImage?.Height}");
        UpdateDimming();
    }

    private void LogState(string tag)
    {
        var handle = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        var phys = "n/a";
        if (handle != IntPtr.Zero && GetWindowRect(handle, out var r))
            phys = $"({r.Left},{r.Top})-({r.Right},{r.Bottom}) {r.Right - r.Left}x{r.Bottom - r.Top}px";
        DebugLogger.Log($"  [{tag}] physWnd={phys}, RenderScaling={this.RenderScaling}, Bounds={this.Bounds}, ClientSize={this.ClientSize}, Width={this.Width}, Height={this.Height}");
    }

    // Use the scaling Avalonia is actually rendering this window with. It can differ from
    // the monitor's effective DPI (_scaling) when the window was created on another monitor,
    // so all DIP<->pixel math must use this value to stay consistent with the layout.
    private double CurrentScaling => this.RenderScaling;

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out WinRect lpRect);

    private struct WinRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private void LoadBackgroundImage(double renderScaling)
    {
        if (_capturedImage == null || _backgroundImage == null)
            return;

        try
        {
            EnsureBackgroundBitmap();
            _backgroundImage.Width = _capturedImage.Width / renderScaling;
            _backgroundImage.Height = _capturedImage.Height / renderScaling;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load background image: {ex}");
        }
    }

    // Build the Avalonia bitmap once by copying the captured pixels directly. The previous
    // approach encoded the screenshot to PNG and decoded it back on every layout pass, which
    // for a 4K capture cost hundreds of ms and made the overlay feel slow to appear.
    private void EnsureBackgroundBitmap()
    {
        if (_backgroundBitmap != null || _capturedImage == null || _backgroundImage == null)
            return;

        var skBitmap = _capturedImage.Bitmap;
        var writeable = new WriteableBitmap(
            new PixelSize(skBitmap.Width, skBitmap.Height),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormats.Bgra8888,
            Avalonia.Platform.AlphaFormat.Opaque);

        using (var fb = writeable.Lock())
        {
            int srcStride = skBitmap.RowBytes;
            int dstStride = fb.RowBytes;
            int rowBytes = Math.Min(srcStride, dstStride);
            var row = new byte[rowBytes];
            var src = skBitmap.GetPixels();
            for (int y = 0; y < skBitmap.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(src, y * srcStride), row, 0, rowBytes);
                Marshal.Copy(row, 0, IntPtr.Add(fb.Address, y * dstStride), rowBytes);
            }
        }

        _backgroundBitmap = writeable;
        _backgroundImage.Source = _backgroundBitmap;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        UpdateDimming();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var point = e.GetPosition(this);

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            // Cancel closes the overlays on ALL monitors, not just this one — otherwise the
            // dimming stays on the other screens. Mirrors the Escape-key behaviour.
            _closeAllOverlays?.Invoke();
            base.OnPointerPressed(e);
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            base.OnPointerPressed(e);
            return;
        }

        _startPoint = point;

        // In edit mode, clicking outside the selection exits edit mode so the user can resize/reset.
        if (_isEditMode && !IsInsideSelection(point))
        {
            ExitEditMode();
        }

        // First check handles so that clicking a handle starts resizing instead of resetting.
        var handle = HitTestHandle(point);
        if (handle >= 0 && _selection != null)
        {
            _isResizing = true;
            _resizeHandle = handle;
            _initialRect = GetSelectionRect();
            e.Handled = true;
            return;
        }

        // If toolbar is visible and click is outside selection (and not on a handle), reset.
        if (_isFinalized && _selection != null && !IsInsideSelection(point))
        {
            _isFinalized = false;
            ClearSelection();
        }

        if (_selection != null && IsInsideSelection(point))
        {
            _isMoving = true;
            _initialRect = GetSelectionRect();
            e.Handled = true;
            return;
        }

        // Start a new selection. Clear any selection on the other monitors' overlays first.
        SelectionStarted?.Invoke();
        _isSelecting = true;
        _isFinalized = false;
        _isEditMode = false;
        HideEditor();
        ClearSelection();
        _selection = new SelectionRectangle
        {
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
            StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 6, 4 },
            StrokeLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };
        ((Canvas?)Content)?.Children.Add(_selection);
        Canvas.SetLeft(_selection, point.X);
        Canvas.SetTop(_selection, point.Y);
        _selection.Width = 0;
        _selection.Height = 0;
        UpdateHandles();
        UpdateDimming();
        e.Handled = true;

        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        UpdateCursor(point);

        if (_isSelecting && _selection != null)
        {
            var left = Math.Min(_startPoint.X, point.X);
            var top = Math.Min(_startPoint.Y, point.Y);
            var width = Math.Abs(point.X - _startPoint.X);
            var height = Math.Abs(point.Y - _startPoint.Y);
            SetSelectionRect(new Rect(left, top, width, height));
        }
        else if (_isMoving && _selection != null)
        {
            var dx = point.X - _startPoint.X;
            var dy = point.Y - _startPoint.Y;
            var rect = _initialRect.Translate(new Vector(dx, dy));
            SetSelectionRect(ClampRectToWindow(rect));
        }
        else if (_isResizing && _selection != null)
        {
            var dx = point.X - _startPoint.X;
            var dy = point.Y - _startPoint.Y;
            var rect = ResizeRect(_initialRect, _resizeHandle, dx, dy);
            SetSelectionRect(ClampRectToWindow(rect));
        }

        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_isResizing || _isMoving)
        {
            _isResizing = false;
            _isMoving = false;
            _resizeHandle = -1;
            var offset = _initialRect.TopLeft - GetSelectionRect().TopLeft;
            EnterEditMode(false, offset);
            base.OnPointerReleased(e);
            return;
        }

        if (_isSelecting)
        {
            _isSelecting = false;
            var rect = GetSelectionRect();
            if (rect.Width > 5 && rect.Height > 5)
            {
                EnterEditMode();
            }
            else
            {
                ClearSelection();
            }
        }

        base.OnPointerReleased(e);
    }

    private void EnterEditMode(bool clearOperations = true, Vector? offset = null)
    {
        _isEditMode = true;
        _isFinalized = true;
        ShowHandles();
        _ = LoadEditorImageAsync(clearOperations, offset);
    }

    private void HideDimming()
    {
        foreach (var dimming in _dimmingRects)
            dimming.IsVisible = false;
    }

    private void ShowDimming()
    {
        UpdateDimming();
    }

    private async Task LoadEditorImageAsync(bool clearOperations = true, Vector? offset = null)
    {
        try
        {
            var result = await CaptureSelectionAsync();
            if (result == null || _editorControl == null)
                return;

            var renderScaling = CurrentScaling;
            DebugLogger.Log($"EnterEditMode: selection={GetSelectionRect()}, renderScaling={renderScaling}");
            if (clearOperations)
                _editorControl.ClearOperations();
            _editorControl.SetImage(result.Image, renderScaling);
            if (offset.HasValue)
                _editorControl.TranslateOperations(offset.Value);
            PositionEditorControl();
            _editorControl.IsVisible = true;
            _toolbarControl?.ShowCloseButton();
            if (_toolbarControl != null)
                _toolbarControl.IsVisible = true;
            PositionToolbar();
            DebugLogger.Log($"EnterEditMode: editorControl bounds={_editorControl.Bounds}, desired={GetSelectionRect().Width}x{GetSelectionRect().Height}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"LoadEditorImageAsync failed: {ex}");
        }
    }

    // Reset this overlay to the empty (no selection) state. Called on sibling overlays when the
    // user starts a selection on another monitor.
    public void ResetState()
    {
        _isSelecting = false;
        _isMoving = false;
        _isResizing = false;
        _resizeHandle = -1;
        _isFinalized = false;
        _isEditMode = false;
        HideEditor();
        ClearSelection();
        HideHandles();
    }

    private void ExitEditMode()
    {
        _isEditMode = false;
        _isFinalized = true;
        HideEditor();
        if (_selection != null)
            _selection.IsVisible = true;
        ShowHandles();
        UpdateDimming();
    }

    private void HideEditor()
    {
        if (_editorControl != null)
            _editorControl.IsVisible = false;
        if (_toolbarControl != null)
            _toolbarControl.IsVisible = false;
    }

    private void OnEditorEditCanceled(object? sender, EventArgs e)
    {
        ExitEditMode();
    }

    private void OnEditorCloseRequested(object? sender, EventArgs e)
    {
        _closeAllOverlays?.Invoke();
    }

    private void PositionEditorControl()
    {
        if (_editorControl == null || _selection == null)
            return;

        var rect = GetSelectionRect();
        Canvas.SetLeft(_editorControl, rect.X);
        Canvas.SetTop(_editorControl, rect.Y);
    }

    private void PositionToolbar()
    {
        if (_toolbarControl == null || _editorControl == null || _selection == null)
            return;

        var rect = GetSelectionRect();
        var toolbarSize = _toolbarControl.DesiredSize;
        if (toolbarSize.Width <= 0 || toolbarSize.Height <= 0)
            return;

        const double margin = 8;
        var windowW = this.Bounds.Width;
        var windowH = this.Bounds.Height;

        // Available space on each side of the selection
        var spaceBottom = windowH - rect.Bottom - margin;
        var spaceTop = rect.Y - margin;
        var spaceRight = windowW - rect.Right - margin;
        var spaceLeft = rect.X - margin;

        double x, y;

        // Prefer bottom, then top, then right, then left
        if (spaceBottom >= toolbarSize.Height)
        {
            x = rect.X + (rect.Width - toolbarSize.Width) / 2.0;
            y = rect.Bottom + margin;
        }
        else if (spaceTop >= toolbarSize.Height)
        {
            x = rect.X + (rect.Width - toolbarSize.Width) / 2.0;
            y = rect.Y - margin - toolbarSize.Height;
        }
        else if (spaceRight >= toolbarSize.Width)
        {
            x = rect.Right + margin;
            y = rect.Y + (rect.Height - toolbarSize.Height) / 2.0;
        }
        else if (spaceLeft >= toolbarSize.Width)
        {
            x = rect.X - margin - toolbarSize.Width;
            y = rect.Y + (rect.Height - toolbarSize.Height) / 2.0;
        }
        else
        {
            // Fallback to bottom center, may be partially off-screen
            x = rect.X + (rect.Width - toolbarSize.Width) / 2.0;
            y = rect.Bottom + margin;
        }

        // Clamp to window bounds
        if (x < margin) x = margin;
        if (y < margin) y = margin;
        if (x + toolbarSize.Width > windowW - margin) x = windowW - margin - toolbarSize.Width;
        if (y + toolbarSize.Height > windowH - margin) y = windowH - margin - toolbarSize.Height;

        Canvas.SetLeft(_toolbarControl, x);
        Canvas.SetTop(_toolbarControl, y);

        DebugLogger.Log($"PositionToolbar: selection={rect}, toolbarSize={toolbarSize.Width}x{toolbarSize.Height}, pos={x},{y}");
    }

    private void OnToolSelected(object? sender, ToolType tool)
    {
        _currentTool = tool;
        _editorControl?.SetTool(tool);
        UpdateCursor(new Point(-1, -1));
    }

    private void OnColorChanged(object? sender, SkiaSharp.SKColor color)
    {
        _editorControl?.SetColor(color);
    }

    private void OnThicknessChanged(object? sender, float thickness)
    {
        _editorControl?.SetLineThickness(thickness);
    }

    private void OnFontSizeChanged(object? sender, float fontSize)
    {
        _editorControl?.SetFontSize(fontSize);
    }

    private async void OnCopyRequested(object? sender, EventArgs e)
    {
        if (_editorControl == null)
            return;

        await _editorControl.CopyAsync();
    }

    private void OnCancelRequested(object? sender, EventArgs e)
    {
        // The toolbar's cancel button aborts the whole capture, closing the overlays on all
        // monitors — same as pressing Escape — rather than merely leaving edit mode.
        _closeAllOverlays?.Invoke();
    }

    private Task<CaptureResult?> CaptureSelectionAsync()
    {
        if (_selection == null)
            return Task.FromResult<CaptureResult?>(null);

        var rect = GetSelectionRect();
        var renderScaling = CurrentScaling;
        var physicalX = (int)(_screenBounds.X + rect.X * renderScaling);
        var physicalY = (int)(_screenBounds.Y + rect.Y * renderScaling);
        var physicalWidth = (int)(rect.Width * renderScaling);
        var physicalHeight = (int)(rect.Height * renderScaling);

        CapturedImage image;
        if (_capturedImage != null)
        {
            // Crop from the pre-captured screen image instead of hitting the screen again.
            var relativeX = physicalX - _screenBounds.X;
            var relativeY = physicalY - _screenBounds.Y;
            var croppedBitmap = new SKBitmap(physicalWidth, physicalHeight);
            using (var canvas = new SKCanvas(croppedBitmap))
            {
                canvas.DrawBitmap(_capturedImage.Bitmap, -relativeX, -relativeY);
            }
            image = new CapturedImage(croppedBitmap);
        }
        else
        {
            image = _captureService.CaptureRegionAsync(physicalX, physicalY, physicalWidth, physicalHeight).GetAwaiter().GetResult();
        }

        return Task.FromResult<CaptureResult?>(new CaptureResult(image, physicalX, physicalY, physicalWidth, physicalHeight, renderScaling));
    }

    private void SetSelectionRect(Rect rect)
    {
        if (_selection == null)
            return;

        Canvas.SetLeft(_selection, rect.X);
        Canvas.SetTop(_selection, rect.Y);
        _selection.Width = rect.Width;
        _selection.Height = rect.Height;
        UpdateHandles();
        UpdateDimming();
        if (_isEditMode)
        {
            PositionEditorControl();
            PositionToolbar();
        }
    }

    private Rect GetSelectionRect()
    {
        if (_selection == null)
            return new Rect();

        return new Rect(
            Canvas.GetLeft(_selection),
            Canvas.GetTop(_selection),
            _selection.Width,
            _selection.Height);
    }

    private void UpdateHandles()
    {
        var rect = GetSelectionRect();
        for (int i = 0; i < _handles.Count; i++)
        {
            var handle = _handles[i];
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                handle.IsVisible = false;
                continue;
            }

            handle.IsVisible = true;
            var pos = GetHandlePosition(rect, i);
            Canvas.SetLeft(handle, pos.X - HandleSize / 2.0);
            Canvas.SetTop(handle, pos.Y - HandleSize / 2.0);
        }
    }

    private void HideHandles()
    {
        foreach (var handle in _handles)
            handle.IsVisible = false;
    }

    private void ShowHandles()
    {
        UpdateHandles();
    }

    private Point GetHandlePosition(Rect rect, int handle)
    {
        return handle switch
        {
            0 => new Point(rect.Left, rect.Top),
            1 => new Point(rect.Left + rect.Width / 2, rect.Top),
            2 => new Point(rect.Right, rect.Top),
            3 => new Point(rect.Left, rect.Top + rect.Height / 2),
            4 => new Point(rect.Right, rect.Top + rect.Height / 2),
            5 => new Point(rect.Left, rect.Bottom),
            6 => new Point(rect.Left + rect.Width / 2, rect.Bottom),
            7 => new Point(rect.Right, rect.Bottom),
            _ => new Point()
        };
    }

    private int HitTestHandle(Point point)
    {
        var rect = GetSelectionRect();
        if (rect.Width <= 0 || rect.Height <= 0)
            return -1;

        for (int i = 0; i < _handles.Count; i++)
        {
            var pos = GetHandlePosition(rect, i);
            var hitRect = new Rect(
                pos.X - HandleSize / 2.0 - HandleHitPadding,
                pos.Y - HandleSize / 2.0 - HandleHitPadding,
                HandleSize + HandleHitPadding * 2,
                HandleSize + HandleHitPadding * 2);
            if (hitRect.Contains(point))
                return i;
        }

        return -1;
    }

    private bool IsInsideSelection(Point point)
    {
        var rect = GetSelectionRect();
        return rect.Width > 0 && rect.Height > 0 && rect.Contains(point);
    }

    private Rect ResizeRect(Rect initial, int handle, double dx, double dy)
    {
        var left = initial.Left;
        var top = initial.Top;
        var right = initial.Right;
        var bottom = initial.Bottom;

        if (handle == 0 || handle == 3 || handle == 5) left += dx;
        if (handle == 2 || handle == 4 || handle == 7) right += dx;
        if (handle == 0 || handle == 1 || handle == 2) top += dy;
        if (handle == 5 || handle == 6 || handle == 7) bottom += dy;

        if (right < left + 1) right = left + 1;
        if (bottom < top + 1) bottom = top + 1;

        return new Rect(left, top, right - left, bottom - top);
    }

    private Rect ClampRectToWindow(Rect rect)
    {
        var w = this.Bounds.Width;
        var h = this.Bounds.Height;

        if (rect.X < 0) rect = rect.WithX(0);
        if (rect.Y < 0) rect = rect.WithY(0);
        if (rect.Right > w) rect = rect.WithWidth(Math.Max(0, w - rect.X));
        if (rect.Bottom > h) rect = rect.WithHeight(Math.Max(0, h - rect.Y));

        return rect;
    }

    private void ClearSelection()
    {
        if (_selection != null)
        {
            ((Canvas?)Content)?.Children.Remove(_selection);
            _selection = null;
        }
        UpdateHandles();
        UpdateDimming();
    }

    private void UpdateDimming()
    {
        var w = this.Bounds.Width;
        var h = this.Bounds.Height;
        var rect = GetSelectionRect();

        if (rect.Width <= 0 || rect.Height <= 0)
        {
            SetDimmingRect(0, new Rect(0, 0, w, h));
            SetDimmingRect(1, new Rect(0, 0, 0, 0));
            SetDimmingRect(2, new Rect(0, 0, 0, 0));
            SetDimmingRect(3, new Rect(0, 0, 0, 0));
            return;
        }

        var leftW = Math.Max(0, rect.X);
        var rightX = Math.Min(w, rect.Right);
        var rightW = Math.Max(0, w - rightX);
        var topH = Math.Max(0, rect.Y);
        var bottomY = Math.Min(h, rect.Bottom);
        var bottomH = Math.Max(0, h - bottomY);

        SetDimmingRect(0, new Rect(0, 0, leftW, h));
        SetDimmingRect(1, new Rect(rightX, 0, rightW, h));
        SetDimmingRect(2, new Rect(rect.X, 0, rect.Width, topH));
        SetDimmingRect(3, new Rect(rect.X, bottomY, rect.Width, bottomH));
    }

    private void SetDimmingRect(int index, Rect rect)
    {
        var dimming = _dimmingRects[index];
        Canvas.SetLeft(dimming, rect.X);
        Canvas.SetTop(dimming, rect.Y);
        dimming.Width = rect.Width;
        dimming.Height = rect.Height;
    }

    private void UpdateCursor(Point point)
    {
        if (_isSelecting || _isMoving || _isResizing)
            return;

        var handle = HitTestHandle(point);
        if (handle >= 0)
        {
            Cursor = handle switch
            {
                0 or 7 => new Cursor(StandardCursorType.TopLeftCorner),
                2 or 5 => new Cursor(StandardCursorType.TopRightCorner),
                1 or 6 => new Cursor(StandardCursorType.SizeNorthSouth),
                3 or 4 => new Cursor(StandardCursorType.SizeWestEast),
                _ => new Cursor(StandardCursorType.Cross)
            };
            return;
        }

        Cursor = _currentTool switch
        {
            ToolType.Selection => new Cursor(StandardCursorType.Arrow),
            ToolType.Text => new Cursor(StandardCursorType.Ibeam),
            _ => new Cursor(StandardCursorType.Cross)
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _closeAllOverlays?.Invoke();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && _isEditMode)
        {
            _editorControl?.DeleteSelected();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control)
        {
            OnCopyRequested(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

}

// Thin dashed selection border. Appearance (stroke, dash pattern) is configured where
// the instance is created; kept as a Rectangle subclass so it composes with the resize
// handles exactly as before.
public sealed class SelectionRectangle : Avalonia.Controls.Shapes.Rectangle
{
}
