using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Histshot.Core.Drawing;
using Histshot.Core.Models;
using Histshot.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Histshot.Views;

public partial class EditorControl : UserControl
{
    private readonly List<DrawingOperation> _operations = new();
    private readonly List<Control> _shapeControls = new();
    private readonly Dictionary<Control, DrawingOperation> _shapeMap = new();
    private readonly ToolSettings _settings = new();

    private StrokeOperation? _currentStroke;
    private Path? _currentPencilPath;
    private Line? _currentLine;
    private Path? _currentArrowHead;
    private TextBox? _activeTextBox;
    private Point? _textStartPoint;
    private TextOperation? _editingOperation;
    private Control? _editingBlock;
    private Rectangle? _textResizeHandle;
    private bool _isResizingText;
    private Point _textResizeStartPoint;
    private Size _textResizeStartSize;
    private Rectangle? _textMoveHandle;
    private bool _isMovingText;
    private Point _textMoveStartPoint;
    private Point _textMoveStartOrigin;
    private Rectangle? _textBorder;
    private SKColor _activeTextColor;

    private const double DefaultTextWidth = 200;
    private const double DefaultTextHeight = 60;
    private const double MinTextWidth = 40;
    private const double MinTextHeight = 28;

    // Cached cursors so we can swap them on every pointer move without allocating.
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);
    private static readonly Cursor CrossCursor = new(StandardCursorType.Cross);
    private static readonly Cursor IbeamCursor = new(StandardCursorType.Ibeam);

    private Control? _selectedControl;
    private DrawingOperation? _selectedOperation;
    private readonly List<Rectangle> _selectionHandles = new();
    private Shape? _selectionBox;
    private bool _isDraggingHandle;
    private int _draggedHandleIndex = -1;
    private Point _handleDragStartPoint;
    private SKPoint _handleDragStartValue;

    private bool _isDraggingShape;
    private Point _shapeDragStartPoint;
    private List<SKPoint>? _shapeDragStartPoints;

    private double _dipToPixelScale = 1.0;
    private CapturedImage? _capturedImage;

    public event EventHandler? CloseRequested;
    public event EventHandler? EditCanceled;

    public EditorControl()
    {
        InitializeComponent();
    }

    public void SetImage(CapturedImage capturedImage, double dipToPixelScale)
    {
        _capturedImage?.Dispose();
        _capturedImage = capturedImage;
        _dipToPixelScale = dipToPixelScale;
        LoadImage();
    }

    public void UpdateSizes(double dipToPixelScale)
    {
        _dipToPixelScale = dipToPixelScale;
        LoadImage();
        RenderAllShapes();
    }

    public void ClearOperations()
    {
        _operations.Clear();
        _currentStroke = null;
        _currentPencilPath = null;
        _currentLine = null;
        _currentArrowHead = null;
        if (_activeTextBox != null)
        {
            DrawingCanvas.Children.Remove(_activeTextBox);
            _activeTextBox = null;
            _textStartPoint = null;
        }
        RenderAllShapes();
    }

    public void TranslateOperations(Vector offset)
    {
        if (offset.X == 0 && offset.Y == 0)
            return;

        var skOffset = new SKPoint((float)offset.X, (float)offset.Y);
        foreach (var operation in _operations)
        {
            if (operation is StrokeOperation stroke)
            {
                for (int i = 0; i < stroke.Points.Count; i++)
                    stroke.Points[i] = stroke.Points[i] + skOffset;
            }
            else if (operation is TextOperation text)
            {
                text.Position = new SKPoint(text.Position.X + skOffset.X, text.Position.Y + skOffset.Y);
            }
        }

        RenderAllShapes();
    }

    public void SetTool(ToolType tool)
    {
        DebugLogger.Log($"EditorControl.SetTool: {tool}");
        if (_settings.Tool == ToolType.Selection && tool != ToolType.Selection)
            ClearSelection();
        _settings.Tool = tool;
    }

    public void SetColor(SKColor color)
    {
        _settings.Color = color;
        if (_activeTextBox != null)
        {
            _activeTextColor = color;
            var brush = new SolidColorBrush(ToAvaloniaColor(color));
            _activeTextBox.Foreground = brush;
            _activeTextBox.CaretBrush = brush;
        }
        ApplySettingsToSelected();
    }

    public void SetLineThickness(float thickness)
    {
        _settings.LineThickness = thickness;
        ApplySettingsToSelected();
    }

    public void SetFontSize(float fontSize)
    {
        _settings.FontSize = fontSize;
        if (_activeTextBox != null)
            _activeTextBox.FontSize = fontSize;
        ApplySettingsToSelected();
    }

    private void ApplySettingsToSelected()
    {
        if (_selectedOperation == null)
            return;

        if (_selectedOperation is StrokeOperation stroke)
        {
            stroke.Color = _settings.Color;
            stroke.Thickness = _settings.LineThickness;
            UpdateShapeAppearance(stroke);
        }
        else if (_selectedOperation is TextOperation text)
        {
            text.Color = _settings.Color;
            text.FontSize = _settings.FontSize;
            UpdateShapeAppearance(text);
        }
    }

    private void UpdateShapeAppearance(DrawingOperation operation)
    {
        foreach (var pair in _shapeMap)
        {
            if (pair.Value != operation)
                continue;

            var control = pair.Key;
            if (operation is StrokeOperation stroke)
            {
                var brush = new SolidColorBrush(ToAvaloniaColor(stroke.Color));
                if (control is Path path)
                {
                    path.Stroke = brush;
                    path.StrokeThickness = stroke.Thickness;
                    if (stroke.Tool == ToolType.Arrow)
                    {
                        var from = stroke.Points[0];
                        var to = stroke.Points[^1];
                        path.Data = CreateArrowHeadGeometry(from, to, stroke.Color, stroke.Thickness);
                    }
                }
                else if (control is Line line)
                {
                    line.Stroke = brush;
                    line.StrokeThickness = stroke.Thickness;
                }
            }
            else if (operation is TextOperation text && control is TextBlock block)
            {
                block.Foreground = new SolidColorBrush(ToAvaloniaColor(text.Color));
                block.FontSize = text.FontSize;
            }
        }
    }

    public async Task CopyAsync()
    {
        try
        {
            FinalizeTextInput();
            using var result = RenderFinalImage();
            var clipboardService = App.Services.GetRequiredService<IClipboardService>();
            await clipboardService.SetImageAsync(result, TopLevel.GetTopLevel(this));

            var historyService = App.Services.GetRequiredService<IHistoryService>();
            await historyService.SaveAsync(result.Copy());

            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Copy failed: {ex}");
        }
    }

    public void Cancel()
    {
        EditCanceled?.Invoke(this, EventArgs.Empty);
    }

    private void LoadImage()
    {
        if (_capturedImage == null)
            return;

        using var encoded = _capturedImage.Bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using System.IO.Stream stream = encoded.AsStream();
        CapturedImage.Source = new Bitmap(stream);

        var dipWidth = _capturedImage.Width / _dipToPixelScale;
        var dipHeight = _capturedImage.Height / _dipToPixelScale;
        CapturedImage.Width = dipWidth;
        CapturedImage.Height = dipHeight;
        DrawingCanvas.Width = dipWidth;
        DrawingCanvas.Height = dipHeight;
    }

    private void Canvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetPosition(DrawingCanvas);

        // A press reaching the canvas is outside the active text box and its handles
        // (those mark the event handled), so commit any in-progress text edit. Clicking
        // empty space or a non-focusable shape never raises the text box's LostFocus.
        FinalizeTextInput();

        if (_settings.Tool == ToolType.Text)
        {
            ClearSelection();
            var hit = HitTestText(point);
            if (hit != null)
            {
                var (block, op) = hit.Value;
                StartTextInput(new Point(op.Position.X, op.Position.Y), op, block);
            }
            else
            {
                StartTextInput(point);
            }
        }
        else if (_settings.Tool == ToolType.Selection)
        {
            DebugLogger.Log($"Canvas_PointerPressed Selection: {point}, shapeControls={_shapeControls.Count}");
            if (_isDraggingHandle || _isDraggingShape)
                return;

            // Clicking a text annotation opens it for editing.
            var textHit = HitTestText(point);
            if (textHit != null)
            {
                var (block, op) = textHit.Value;
                ClearSelection();
                StartTextInput(new Point(op.Position.X, op.Position.Y), op, block);
                e.Handled = true;
                return;
            }

            // Start dragging the whole selected shape (pencil, line or arrow) when
            // pressing on its body. Endpoint handles have their own press handlers
            // that mark the event handled, so dragging a single endpoint still wins.
            if (_selectedOperation is StrokeOperation selectedStroke && IsPointOnStroke(point, selectedStroke))
            {
                _isDraggingShape = true;
                _shapeDragStartPoint = point;
                _shapeDragStartPoints = selectedStroke.Points.ToList();
                e.Pointer.Capture(DrawingCanvas);
                e.Handled = true;
                return;
            }

            var hit = HitTestShape(point);
            DebugLogger.Log($"HitTestShape result: {hit?.GetType().Name ?? "null"}");
            if (hit != null)
            {
                SelectShape(hit);
            }
            else
            {
                ClearSelection();
            }
        }
        else
        {
            ClearSelection();
            _currentStroke = new StrokeOperation
            {
                Color = _settings.Color,
                Thickness = _settings.LineThickness,
                IsArrow = _settings.Tool == ToolType.Arrow,
                Tool = _settings.Tool
            };
            _currentStroke.Points.Add(ToSkPoint(point));

            switch (_settings.Tool)
            {
                case ToolType.Pencil:
                    _currentPencilPath = CreatePencilPath(_currentStroke);
                    DrawingCanvas.Children.Add(_currentPencilPath);
                    break;
                case ToolType.Line:
                    _currentStroke.Points.Add(ToSkPoint(point));
                    _currentLine = CreateLine(_currentStroke);
                    DrawingCanvas.Children.Add(_currentLine);
                    break;
                case ToolType.Arrow:
                    _currentStroke.Points.Add(ToSkPoint(point));
                    (_currentLine, _currentArrowHead) = CreateArrow(_currentStroke);
                    DrawingCanvas.Children.Add(_currentLine);
                    DrawingCanvas.Children.Add(_currentArrowHead);
                    break;
            }
        }

        e.Handled = true;
    }

    private void Canvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(DrawingCanvas);

        UpdateHoverCursor(point);

        if (_isDraggingShape && _selectedOperation is StrokeOperation shapeStroke)
        {
            var delta = point - _shapeDragStartPoint;
            var skDelta = new SKPoint((float)delta.X, (float)delta.Y);
            shapeStroke.Points.Clear();
            foreach (var p in _shapeDragStartPoints ?? Enumerable.Empty<SKPoint>())
                shapeStroke.Points.Add(p + skDelta);

            switch (shapeStroke.Tool)
            {
                case ToolType.Pencil:
                    if (_selectedControl is Path path)
                        path.Data = CreatePencilGeometry(shapeStroke);
                    UpdatePathSelectionBox(shapeStroke);
                    break;
                case ToolType.Line:
                    if (_selectedControl is Line line)
                    {
                        line.StartPoint = new Point(shapeStroke.Points[0].X, shapeStroke.Points[0].Y);
                        line.EndPoint = new Point(shapeStroke.Points[^1].X, shapeStroke.Points[^1].Y);
                    }
                    UpdateSelectionHandles();
                    break;
                case ToolType.Arrow:
                    UpdateArrowControls(shapeStroke);
                    UpdateSelectionHandles();
                    break;
            }

            e.Handled = true;
            return;
        }

        if (_isDraggingHandle && _selectedOperation is StrokeOperation stroke)
        {
            var newPoint = ClampToCanvas(point);
            var skPoint = ToSkPoint(newPoint);

            if (_draggedHandleIndex == 0)
            {
                stroke.Points[0] = skPoint;
                if (_selectedControl is Line line)
                {
                    line.StartPoint = newPoint;
                }
                else if (_selectedControl is Path arrowHead && _shapeMap.TryGetValue(_selectedControl, out var op) && op is StrokeOperation arrowOp)
                {
                    // For arrow head path, update based on the paired line
                }
            }
            else if (_draggedHandleIndex == 1)
            {
                stroke.Points[^1] = skPoint;
                if (_selectedControl is Line line)
                {
                    line.EndPoint = newPoint;
                }
            }

            // If arrow is selected, find the paired line/head and update both
            if (stroke.Tool == ToolType.Arrow)
            {
                UpdateArrowControls(stroke);
            }

            UpdateSelectionHandles();
        }
        else if (_currentStroke != null && e.GetCurrentPoint(DrawingCanvas).Properties.IsLeftButtonPressed)
        {
            var skPoint = ToSkPoint(point);

            switch (_settings.Tool)
            {
                case ToolType.Pencil:
                    _currentStroke.Points.Add(skPoint);
                    if (_currentPencilPath != null)
                        _currentPencilPath.Data = CreatePencilGeometry(_currentStroke);
                    break;
                case ToolType.Line:
                case ToolType.Arrow:
                    if (_currentStroke.Points.Count > 1)
                        _currentStroke.Points[^1] = skPoint;
                    else
                        _currentStroke.Points.Add(skPoint);

                    if (_currentLine != null)
                    {
                        _currentLine.EndPoint = point;
                    }
                    if (_currentArrowHead != null)
                    {
                        _currentArrowHead.Data = CreateArrowHeadGeometry(
                            _currentStroke.Points[0], _currentStroke.Points[^1],
                            _currentStroke.Color, _currentStroke.Thickness);
                    }
                    break;
            }
        }

        e.Handled = true;
    }

    private void Canvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDraggingHandle)
        {
            _isDraggingHandle = false;
            _draggedHandleIndex = -1;
        }
        else if (_isDraggingShape)
        {
            _isDraggingShape = false;
            e.Pointer.Capture(null);
        }
        else if (_currentStroke != null)
        {
            if (_currentStroke.Points.Count >= 2)
            {
                _operations.Add(_currentStroke);

                if (_currentPencilPath != null)
                {
                    _shapeControls.Add(_currentPencilPath);
                    _shapeMap[_currentPencilPath] = _currentStroke;
                }
                if (_currentLine != null)
                {
                    _shapeControls.Add(_currentLine);
                    _shapeMap[_currentLine] = _currentStroke;
                }
                if (_currentArrowHead != null)
                {
                    _shapeControls.Add(_currentArrowHead);
                    _shapeMap[_currentArrowHead] = _currentStroke;
                }

                // Select the shape we just drew so its color/thickness can be
                // changed straight from the toolbar without first switching to the
                // Selection tool. For an arrow the line control stands in for the
                // whole operation (both line and head map to the same stroke).
                var drawnControl = (Control?)_currentPencilPath ?? _currentLine;
                if (drawnControl != null)
                    SelectShape(drawnControl);
            }
            else
            {
                // Discard single point strokes
                if (_currentPencilPath != null)
                    DrawingCanvas.Children.Remove(_currentPencilPath);
                if (_currentLine != null)
                    DrawingCanvas.Children.Remove(_currentLine);
                if (_currentArrowHead != null)
                    DrawingCanvas.Children.Remove(_currentArrowHead);
            }
        }

        _currentStroke = null;
        _currentPencilPath = null;
        _currentLine = null;
        _currentArrowHead = null;

        e.Handled = true;
    }

    private void StartTextInput(Point point, TextOperation? existing = null, Control? existingBlock = null)
    {
        if (_activeTextBox != null)
        {
            FinalizeTextInput();
        }

        _editingOperation = existing;
        _editingBlock = existingBlock;
        // Hide the rendered text while its content is being edited.
        if (existingBlock != null)
            existingBlock.IsVisible = false;

        _textStartPoint = point;
        _activeTextColor = existing?.Color ?? _settings.Color;
        var textBrush = new SolidColorBrush(ToAvaloniaColor(_activeTextColor));
        _activeTextBox = new TextBox
        {
            Width = existing?.Width ?? DefaultTextWidth,
            Height = existing?.Height ?? DefaultTextHeight,
            Classes = { "annotation" },
            // Transparent fill so the text shows directly over the screenshot;
            // draw it in the annotation color so it matches the final result.
            Background = Brushes.Transparent,
            Foreground = textBrush,
            CaretBrush = textBrush,
            FontSize = existing?.FontSize ?? _settings.FontSize,
            Text = existing?.Text ?? string.Empty,
            PlaceholderText = "Type here...",
            // Wrap the text to the box width and let it grow into multiple lines.
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Top
        };

        Canvas.SetLeft(_activeTextBox, point.X);
        Canvas.SetTop(_activeTextBox, point.Y);
        DrawingCanvas.Children.Add(_activeTextBox);
        _activeTextBox.Focus();
        if (existing != null)
            _activeTextBox.CaretIndex = existing.Text.Length;
        _activeTextBox.LostFocus += (_, _) => FinalizeTextInput();
        _activeTextBox.KeyDown += (s, e) =>
        {
            // Enter commits the text; Shift+Enter inserts a line break.
            if (e.Key == Key.Enter && e.KeyModifiers != KeyModifiers.Shift)
            {
                FinalizeTextInput();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                FinalizeTextInput();
                e.Handled = true;
            }
        };

        AddTextHandles();
    }

    private void AddTextHandles()
    {
        if (_activeTextBox == null)
            return;

        // Dashed outline around the edit area (an Avalonia Border can't draw dashes).
        _textBorder = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.Parse("#3B82F6")),
            StrokeThickness = 1,
            Fill = Brushes.Transparent,
            StrokeDashArray = new AvaloniaList<double> { 4, 2 },
            IsHitTestVisible = false
        };
        DrawingCanvas.Children.Add(_textBorder);

        _textMoveHandle = CreateTextHandle(StandardCursorType.SizeAll);
        _textMoveHandle.PointerPressed += TextMoveHandle_PointerPressed;
        _textMoveHandle.PointerMoved += TextMoveHandle_PointerMoved;
        _textMoveHandle.PointerReleased += TextMoveHandle_PointerReleased;
        DrawingCanvas.Children.Add(_textMoveHandle);

        _textResizeHandle = CreateTextHandle(StandardCursorType.BottomRightCorner);
        _textResizeHandle.PointerPressed += TextResizeHandle_PointerPressed;
        _textResizeHandle.PointerMoved += TextResizeHandle_PointerMoved;
        _textResizeHandle.PointerReleased += TextResizeHandle_PointerReleased;
        DrawingCanvas.Children.Add(_textResizeHandle);

        UpdateTextHandlePositions();
    }

    private static Rectangle CreateTextHandle(StandardCursorType cursor) => new()
    {
        Width = 12,
        Height = 12,
        Fill = Brushes.White,
        Stroke = Brushes.Black,
        StrokeThickness = 1,
        Cursor = new Cursor(cursor)
    };

    private void UpdateTextHandlePositions()
    {
        if (_activeTextBox == null)
            return;

        var left = Canvas.GetLeft(_activeTextBox);
        var top = Canvas.GetTop(_activeTextBox);

        if (_textBorder != null)
        {
            Canvas.SetLeft(_textBorder, left);
            Canvas.SetTop(_textBorder, top);
            _textBorder.Width = _activeTextBox.Width;
            _textBorder.Height = _activeTextBox.Height;
        }

        if (_textMoveHandle != null)
        {
            // Move grip sits at the top-left corner.
            Canvas.SetLeft(_textMoveHandle, left - _textMoveHandle.Width / 2);
            Canvas.SetTop(_textMoveHandle, top - _textMoveHandle.Height / 2);
        }

        if (_textResizeHandle != null)
        {
            // Resize grip sits at the bottom-right corner.
            Canvas.SetLeft(_textResizeHandle, left + _activeTextBox.Width - _textResizeHandle.Width / 2);
            Canvas.SetTop(_textResizeHandle, top + _activeTextBox.Height - _textResizeHandle.Height / 2);
        }
    }

    private void RemoveTextHandles()
    {
        _isResizingText = false;
        _isMovingText = false;

        if (_textBorder != null)
        {
            DrawingCanvas.Children.Remove(_textBorder);
            _textBorder = null;
        }

        if (_textMoveHandle != null)
        {
            _textMoveHandle.PointerPressed -= TextMoveHandle_PointerPressed;
            _textMoveHandle.PointerMoved -= TextMoveHandle_PointerMoved;
            _textMoveHandle.PointerReleased -= TextMoveHandle_PointerReleased;
            DrawingCanvas.Children.Remove(_textMoveHandle);
            _textMoveHandle = null;
        }

        if (_textResizeHandle != null)
        {
            _textResizeHandle.PointerPressed -= TextResizeHandle_PointerPressed;
            _textResizeHandle.PointerMoved -= TextResizeHandle_PointerMoved;
            _textResizeHandle.PointerReleased -= TextResizeHandle_PointerReleased;
            DrawingCanvas.Children.Remove(_textResizeHandle);
            _textResizeHandle = null;
        }
    }

    private void TextMoveHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_activeTextBox == null)
            return;

        _isMovingText = true;
        _textMoveStartPoint = e.GetPosition(DrawingCanvas);
        _textMoveStartOrigin = new Point(Canvas.GetLeft(_activeTextBox), Canvas.GetTop(_activeTextBox));
        e.Pointer.Capture(_textMoveHandle);
        e.Handled = true;
    }

    private void TextMoveHandle_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isMovingText || _activeTextBox == null)
            return;

        var point = e.GetPosition(DrawingCanvas);
        var left = _textMoveStartOrigin.X + (point.X - _textMoveStartPoint.X);
        var top = _textMoveStartOrigin.Y + (point.Y - _textMoveStartPoint.Y);
        Canvas.SetLeft(_activeTextBox, left);
        Canvas.SetTop(_activeTextBox, top);
        _textStartPoint = new Point(left, top);
        UpdateTextHandlePositions();
        e.Handled = true;
    }

    private void TextMoveHandle_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isMovingText)
            return;

        _isMovingText = false;
        e.Pointer.Capture(null);
        e.Handled = true;
        _activeTextBox?.Focus();
    }

    private void TextResizeHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_activeTextBox == null)
            return;

        _isResizingText = true;
        _textResizeStartPoint = e.GetPosition(DrawingCanvas);
        _textResizeStartSize = new Size(_activeTextBox.Width, _activeTextBox.Height);
        e.Pointer.Capture(_textResizeHandle);
        e.Handled = true;
    }

    private void TextResizeHandle_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizingText || _activeTextBox == null)
            return;

        var point = e.GetPosition(DrawingCanvas);
        var width = Math.Max(MinTextWidth, _textResizeStartSize.Width + (point.X - _textResizeStartPoint.X));
        var height = Math.Max(MinTextHeight, _textResizeStartSize.Height + (point.Y - _textResizeStartPoint.Y));
        _activeTextBox.Width = width;
        _activeTextBox.Height = height;
        UpdateTextHandlePositions();
        e.Handled = true;
    }

    private void TextResizeHandle_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isResizingText)
            return;

        _isResizingText = false;
        e.Pointer.Capture(null);
        e.Handled = true;
        _activeTextBox?.Focus();
    }

    private void FinalizeTextInput()
    {
        if (_activeTextBox == null)
            return;

        // Capture state and clear fields up front so the LostFocus handler that
        // fires while we tear down the text box doesn't re-enter this method.
        var textBox = _activeTextBox;
        var text = textBox.Text ?? string.Empty;
        var startPoint = _textStartPoint;
        var editingOperation = _editingOperation;
        var editingBlock = _editingBlock;
        var width = (float)textBox.Width;
        var height = (float)textBox.Height;
        var fontSize = (float)textBox.FontSize;
        var color = _activeTextColor;

        _activeTextBox = null;
        _textStartPoint = null;
        _editingOperation = null;
        _editingBlock = null;

        RemoveTextHandles();
        DrawingCanvas.Children.Remove(textBox);

        if (string.IsNullOrWhiteSpace(text) || !startPoint.HasValue)
        {
            // The text was left empty. If we were editing an existing annotation,
            // an empty result means the user wants to remove it.
            if (editingOperation != null && editingBlock != null)
            {
                _operations.Remove(editingOperation);
                _shapeControls.Remove(editingBlock);
                _shapeMap.Remove(editingBlock);
                DrawingCanvas.Children.Remove(editingBlock);
            }
            return;
        }

        if (editingOperation != null && editingBlock != null)
        {
            // Update the existing annotation in place.
            editingOperation.Text = text;
            editingOperation.Width = width;
            editingOperation.Height = height;
            editingOperation.FontSize = fontSize;
            editingOperation.Color = color;
            editingOperation.Position = ToSkPoint(startPoint.Value);
            if (editingBlock is TextBlock block)
            {
                block.Text = text;
                block.Width = width;
                block.FontSize = fontSize;
                block.Foreground = new SolidColorBrush(ToAvaloniaColor(color));
                Canvas.SetLeft(block, startPoint.Value.X);
                Canvas.SetTop(block, startPoint.Value.Y);
            }
            editingBlock.IsVisible = true;
            return;
        }

        var operation = new TextOperation
        {
            Text = text,
            Position = ToSkPoint(startPoint.Value),
            Color = color,
            FontSize = fontSize,
            Width = width,
            Height = height
        };
        _operations.Add(operation);

        var textBlock = CreateTextBlock(operation);
        _shapeControls.Add(textBlock);
        _shapeMap[textBlock] = operation;
        DrawingCanvas.Children.Add(textBlock);
    }

    private void RenderAllShapes()
    {
        ClearShapes();

        foreach (var operation in _operations)
        {
            AddShapeForOperation(operation);
        }
    }

    private void ClearShapes()
    {
        ClearSelection();
        for (int i = DrawingCanvas.Children.Count - 1; i >= 0; i--)
        {
            if (DrawingCanvas.Children[i] != CapturedImage
                && DrawingCanvas.Children[i] != _activeTextBox
                && DrawingCanvas.Children[i] != _textResizeHandle
                && DrawingCanvas.Children[i] != _textMoveHandle
                && DrawingCanvas.Children[i] != _textBorder)
                DrawingCanvas.Children.RemoveAt(i);
        }
        _shapeControls.Clear();
        _shapeMap.Clear();
    }

    private void AddShapeForOperation(DrawingOperation operation)
    {
        switch (operation)
        {
            case StrokeOperation stroke when stroke.Tool == ToolType.Pencil:
                var pencilPath = CreatePencilPath(stroke);
                _shapeControls.Add(pencilPath);
                _shapeMap[pencilPath] = operation;
                DrawingCanvas.Children.Add(pencilPath);
                break;
            case StrokeOperation stroke when stroke.Tool == ToolType.Line:
                var line = CreateLine(stroke);
                _shapeControls.Add(line);
                _shapeMap[line] = operation;
                DrawingCanvas.Children.Add(line);
                break;
            case StrokeOperation stroke when stroke.Tool == ToolType.Arrow:
                var (arrowLine, arrowHead) = CreateArrow(stroke);
                _shapeControls.Add(arrowLine);
                _shapeControls.Add(arrowHead);
                _shapeMap[arrowLine] = operation;
                _shapeMap[arrowHead] = operation;
                DrawingCanvas.Children.Add(arrowLine);
                DrawingCanvas.Children.Add(arrowHead);
                break;
            case TextOperation text:
                var textBlock = CreateTextBlock(text);
                _shapeControls.Add(textBlock);
                _shapeMap[textBlock] = operation;
                DrawingCanvas.Children.Add(textBlock);
                break;
        }
    }

    private static Path CreatePencilPath(StrokeOperation stroke)
    {
        return new Path
        {
            Stroke = new SolidColorBrush(ToAvaloniaColor(stroke.Color)),
            StrokeThickness = stroke.Thickness,
            StrokeLineCap = PenLineCap.Round,
            Data = CreatePencilGeometry(stroke)
        };
    }

    private static Geometry CreatePencilGeometry(StrokeOperation stroke)
    {
        if (stroke.Points.Count < 2)
            return new StreamGeometry();

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(stroke.Points[0].X, stroke.Points[0].Y), false);
            for (int i = 1; i < stroke.Points.Count; i++)
            {
                context.LineTo(new Point(stroke.Points[i].X, stroke.Points[i].Y));
            }
        }
        return geometry;
    }

    private static Line CreateLine(StrokeOperation stroke)
    {
        var start = stroke.Points[0];
        var end = stroke.Points[^1];
        return new Line
        {
            StartPoint = new Point(start.X, start.Y),
            EndPoint = new Point(end.X, end.Y),
            Stroke = new SolidColorBrush(ToAvaloniaColor(stroke.Color)),
            StrokeThickness = stroke.Thickness,
            StrokeLineCap = PenLineCap.Round
        };
    }

    private static (Line Line, Path Head) CreateArrow(StrokeOperation stroke)
    {
        var from = stroke.Points[0];
        var to = stroke.Points[^1];
        var line = new Line
        {
            StartPoint = new Point(from.X, from.Y),
            EndPoint = new Point(to.X, to.Y),
            Stroke = new SolidColorBrush(ToAvaloniaColor(stroke.Color)),
            StrokeThickness = stroke.Thickness,
            StrokeLineCap = PenLineCap.Round
        };
        var head = new Path
        {
            Stroke = new SolidColorBrush(ToAvaloniaColor(stroke.Color)),
            StrokeThickness = stroke.Thickness,
            StrokeLineCap = PenLineCap.Round,
            Data = CreateArrowHeadGeometry(from, to, stroke.Color, stroke.Thickness)
        };
        return (line, head);
    }

    private static Geometry CreateArrowHeadGeometry(SKPoint from, SKPoint to, SKColor color, float thickness)
    {
        const float arrowLength = 20f;
        const float arrowAngle = 0.5f;
        float angle = MathF.Atan2(to.Y - from.Y, to.X - from.X);

        var left = new SKPoint(
            to.X - arrowLength * MathF.Cos(angle - arrowAngle),
            to.Y - arrowLength * MathF.Sin(angle - arrowAngle));
        var right = new SKPoint(
            to.X - arrowLength * MathF.Cos(angle + arrowAngle),
            to.Y - arrowLength * MathF.Sin(angle + arrowAngle));

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(to.X, to.Y), false);
            context.LineTo(new Point(left.X, left.Y));
            context.BeginFigure(new Point(to.X, to.Y), false);
            context.LineTo(new Point(right.X, right.Y));
        }
        return geometry;
    }

    private static TextBlock CreateTextBlock(TextOperation text)
    {
        var block = new TextBlock
        {
            Text = text.Text,
            Foreground = new SolidColorBrush(ToAvaloniaColor(text.Color)),
            FontSize = text.FontSize,
            TextWrapping = TextWrapping.Wrap
        };
        if (text.Width > 0)
            block.Width = text.Width;
        Canvas.SetLeft(block, text.Position.X);
        Canvas.SetTop(block, text.Position.Y);
        return block;
    }

    private (Control Block, TextOperation Operation)? HitTestText(Point point)
    {
        for (int i = _shapeControls.Count - 1; i >= 0; i--)
        {
            var control = _shapeControls[i];
            if (control == _activeTextBox)
                continue;

            if (_shapeMap.TryGetValue(control, out var operation) && operation is TextOperation textOp
                && control.Bounds.Inflate(4).Contains(point))
            {
                return (control, textOp);
            }
        }
        return null;
    }

    private Control? HitTestShape(Point point)
    {
        DebugLogger.Log($"HitTestShape: point={point}, controls={_shapeControls.Count}");
        const double threshold = 10;

        for (int i = _shapeControls.Count - 1; i >= 0; i--)
        {
            var control = _shapeControls[i];
            if (control == _activeTextBox)
                continue;

            if (_shapeMap.TryGetValue(control, out var operation) && operation is StrokeOperation stroke)
            {
                var distance = DistanceToPolyline(point, stroke.Points);
                DebugLogger.Log($"  control {control.GetType().Name} stroke distance={distance}");
                if (distance <= threshold)
                {
                    DebugLogger.Log($"  HIT {control.GetType().Name}");
                    return control;
                }
            }
            else
            {
                var bounds = control.Bounds.Inflate(threshold);
                DebugLogger.Log($"  control {control.GetType().Name} bounds={control.Bounds} hitBounds={bounds}");
                if (bounds.Contains(point))
                {
                    DebugLogger.Log($"  HIT {control.GetType().Name}");
                    return control;
                }
            }
        }
        return null;
    }

    private static double DistanceToPolyline(Point point, IList<SKPoint> points)
    {
        if (points.Count == 0)
            return double.MaxValue;
        if (points.Count == 1)
            return Distance(point, points[0]);

        var minDistance = double.MaxValue;
        for (int i = 0; i < points.Count - 1; i++)
        {
            var distance = DistanceToSegment(point, points[i], points[i + 1]);
            if (distance < minDistance)
                minDistance = distance;
        }
        return minDistance;
    }

    private static double DistanceToSegment(Point p, SKPoint a, SKPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;

        double t;
        if (lengthSquared == 0)
        {
            t = 0;
        }
        else
        {
            t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared;
            t = Math.Clamp(t, 0, 1);
        }

        var projectionX = a.X + t * dx;
        var projectionY = a.Y + t * dy;
        return Distance(p, new SKPoint((float)projectionX, (float)projectionY));
    }

    private static double Distance(Point p, SKPoint s)
    {
        var dx = p.X - s.X;
        var dy = p.Y - s.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void SelectShape(Control control)
    {
        DebugLogger.Log($"SelectShape: {control.GetType().Name}");
        ClearSelection();
        _selectedControl = control;
        _shapeMap.TryGetValue(control, out _selectedOperation);

        if (_selectedOperation is StrokeOperation stroke)
        {
            if (stroke.Tool == ToolType.Pencil)
            {
                ShowPathSelection(stroke);
            }
            else if (stroke.Tool == ToolType.Line || stroke.Tool == ToolType.Arrow)
            {
                ShowLineHandles(stroke);
            }
        }
    }

    private void ClearSelection()
    {
        _selectedControl = null;
        _selectedOperation = null;
        _isDraggingHandle = false;
        _draggedHandleIndex = -1;

        foreach (var handle in _selectionHandles)
        {
            DrawingCanvas.Children.Remove(handle);
            handle.PointerPressed -= Handle_PointerPressed;
            handle.PointerMoved -= Handle_PointerMoved;
            handle.PointerReleased -= Handle_PointerReleased;
        }
        _selectionHandles.Clear();

        if (_selectionBox != null)
        {
            DrawingCanvas.Children.Remove(_selectionBox);
            _selectionBox = null;
        }
    }

    private void ShowPathSelection(StrokeOperation stroke)
    {
        var bounds = GetStrokeBounds(stroke).Inflate(4);
        _selectionBox = new Rectangle
        {
            Stroke = Brushes.Red,
            StrokeThickness = 1,
            Fill = Brushes.Transparent,
            StrokeDashArray = new AvaloniaList<double> { 4, 2 },
            IsHitTestVisible = false
        };
        Canvas.SetLeft(_selectionBox, bounds.X);
        Canvas.SetTop(_selectionBox, bounds.Y);
        _selectionBox.Width = bounds.Width;
        _selectionBox.Height = bounds.Height;
        DrawingCanvas.Children.Add(_selectionBox);
    }

    private void UpdatePathSelectionBox(StrokeOperation stroke)
    {
        if (_selectionBox == null)
            return;

        var bounds = GetStrokeBounds(stroke).Inflate(4);
        Canvas.SetLeft(_selectionBox, bounds.X);
        Canvas.SetTop(_selectionBox, bounds.Y);
        _selectionBox.Width = bounds.Width;
        _selectionBox.Height = bounds.Height;
    }

    // True when the point lands on the shape's body, so a press there should drag the
    // whole shape. Pencil scribbles are grabbed anywhere inside their bounds; straight
    // line/arrow strokes are grabbed near the segment itself (their bounding box is mostly
    // empty for a diagonal).
    private bool IsPointOnStroke(Point point, StrokeOperation stroke)
    {
        if (stroke.Tool == ToolType.Pencil)
            return GetStrokeBounds(stroke).Inflate(4).Contains(point);

        const double threshold = 10;
        return DistanceToPolyline(point, stroke.Points) <= threshold;
    }

    // Pick the cursor that reflects what a press at this point would do, so the user
    // can tell selecting from moving from drawing before they click.
    private void UpdateHoverCursor(Point point)
    {
        Cursor cursor;
        switch (_settings.Tool)
        {
            case ToolType.Selection:
                if (_isDraggingShape ||
                    (_selectedOperation is StrokeOperation stroke && IsPointOnStroke(point, stroke)))
                    cursor = MoveCursor;       // over the selected shape's body -> drag it whole
                else if (IsOverAnyShape(point))
                    cursor = HandCursor;       // over another shape -> click to select
                else
                    cursor = ArrowCursor;
                break;
            case ToolType.Text:
                cursor = IbeamCursor;
                break;
            default:
                cursor = CrossCursor;          // pencil / line / arrow draw with a crosshair
                break;
        }

        if (DrawingCanvas.Cursor != cursor)
            DrawingCanvas.Cursor = cursor;
    }

    // Lightweight, non-logging hit test used for cursor feedback on every pointer move.
    // (HitTestShape logs per shape, which would hammer the debug log when only hovering.)
    private bool IsOverAnyShape(Point point)
    {
        const double threshold = 10;
        for (int i = _shapeControls.Count - 1; i >= 0; i--)
        {
            var control = _shapeControls[i];
            if (control == _activeTextBox)
                continue;

            if (_shapeMap.TryGetValue(control, out var operation))
            {
                if (operation is StrokeOperation stroke)
                {
                    if (DistanceToPolyline(point, stroke.Points) <= threshold)
                        return true;
                }
                else if (control.Bounds.Inflate(threshold).Contains(point))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static Rect GetStrokeBounds(StrokeOperation stroke)
    {
        if (stroke.Points.Count == 0)
            return new Rect();

        var minX = stroke.Points.Min(p => p.X);
        var minY = stroke.Points.Min(p => p.Y);
        var maxX = stroke.Points.Max(p => p.X);
        var maxY = stroke.Points.Max(p => p.Y);
        var halfThickness = stroke.Thickness / 2.0f;
        return new Rect(
            minX - halfThickness,
            minY - halfThickness,
            maxX - minX + halfThickness * 2,
            maxY - minY + halfThickness * 2);
    }

    private void ShowLineHandles(StrokeOperation stroke)
    {
        var start = new Point(stroke.Points[0].X, stroke.Points[0].Y);
        var end = new Point(stroke.Points[^1].X, stroke.Points[^1].Y);
        _selectionHandles.Add(CreateHandle(start, 0));
        _selectionHandles.Add(CreateHandle(end, 1));
        foreach (var handle in _selectionHandles)
        {
            DrawingCanvas.Children.Add(handle);
        }
    }

    private Rectangle CreateHandle(Point point, int index)
    {
        const double size = 10;
        var handle = new Rectangle
        {
            Width = size,
            Height = size,
            Fill = Brushes.White,
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            Cursor = MoveCursor
        };
        Canvas.SetLeft(handle, point.X - size / 2);
        Canvas.SetTop(handle, point.Y - size / 2);
        handle.Tag = index;
        handle.PointerPressed += Handle_PointerPressed;
        handle.PointerMoved += Handle_PointerMoved;
        handle.PointerReleased += Handle_PointerReleased;
        return handle;
    }

    private void Handle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Rectangle handle && handle.Tag is int index && _selectedOperation is StrokeOperation stroke)
        {
            _isDraggingHandle = true;
            _draggedHandleIndex = index;
            _handleDragStartPoint = e.GetPosition(DrawingCanvas);
            _handleDragStartValue = index == 0 ? stroke.Points[0] : stroke.Points[^1];
            e.Pointer.Capture(handle);
            e.Handled = true;
        }
    }

    private void Handle_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingHandle || _selectedOperation is not StrokeOperation stroke)
            return;

        var point = ClampToCanvas(e.GetPosition(DrawingCanvas));
        var skPoint = ToSkPoint(point);

        if (_draggedHandleIndex == 0)
            stroke.Points[0] = skPoint;
        else if (_draggedHandleIndex == 1)
            stroke.Points[^1] = skPoint;

        if (stroke.Tool == ToolType.Arrow)
        {
            UpdateArrowControls(stroke);
        }
        else if (_selectedControl is Line line)
        {
            if (_draggedHandleIndex == 0)
                line.StartPoint = point;
            else if (_draggedHandleIndex == 1)
                line.EndPoint = point;
        }

        UpdateSelectionHandles();
        e.Handled = true;
    }

    private void Handle_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDraggingHandle = false;
        _draggedHandleIndex = -1;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void UpdateSelectionHandles()
    {
        if (_selectedOperation is not StrokeOperation stroke || _selectionHandles.Count < 2)
            return;

        var start = new Point(stroke.Points[0].X, stroke.Points[0].Y);
        var end = new Point(stroke.Points[^1].X, stroke.Points[^1].Y);

        Canvas.SetLeft(_selectionHandles[0], start.X - 5);
        Canvas.SetTop(_selectionHandles[0], start.Y - 5);
        Canvas.SetLeft(_selectionHandles[1], end.X - 5);
        Canvas.SetTop(_selectionHandles[1], end.Y - 5);
    }

    private void UpdateArrowControls(StrokeOperation stroke)
    {
        if (stroke.Tool != ToolType.Arrow)
            return;

        var from = stroke.Points[0];
        var to = stroke.Points[^1];

        // Find the line and head controls for this operation
        Line? line = null;
        Path? head = null;
        foreach (var pair in _shapeMap)
        {
            if (pair.Value == stroke)
            {
                if (pair.Key is Line l)
                    line = l;
                else if (pair.Key is Path p)
                    head = p;
            }
        }

        if (line != null)
        {
            line.StartPoint = new Point(from.X, from.Y);
            line.EndPoint = new Point(to.X, to.Y);
        }

        if (head != null)
        {
            head.Data = CreateArrowHeadGeometry(from, to, stroke.Color, stroke.Thickness);
        }
    }

    private Point ClampToCanvas(Point point)
    {
        var x = Math.Clamp(point.X, 0, DrawingCanvas.Width);
        var y = Math.Clamp(point.Y, 0, DrawingCanvas.Height);
        return new Point(x, y);
    }

    public void DeleteSelected()
    {
        if (_selectedOperation == null || _selectedControl == null)
            return;

        _operations.Remove(_selectedOperation);

        // For arrow, remove both line and head controls
        if (_selectedOperation is StrokeOperation stroke && stroke.Tool == ToolType.Arrow)
        {
            var toRemove = _shapeMap.Where(p => p.Value == stroke).Select(p => p.Key).ToList();
            foreach (var control in toRemove)
            {
                _shapeControls.Remove(control);
                _shapeMap.Remove(control);
                DrawingCanvas.Children.Remove(control);
            }
        }
        else
        {
            _shapeControls.Remove(_selectedControl);
            _shapeMap.Remove(_selectedControl);
            DrawingCanvas.Children.Remove(_selectedControl);
        }

        ClearSelection();
    }

    private SKBitmap RenderFinalImage()
    {
        if (_capturedImage == null)
            throw new InvalidOperationException("No captured image.");

        var bitmap = new SKBitmap(_capturedImage.Width, _capturedImage.Height);
        using var canvas = new SKCanvas(bitmap);
        canvas.DrawBitmap(_capturedImage.Bitmap, 0, 0);
        canvas.Scale((float)_dipToPixelScale);
        DrawingRenderer.Render(canvas, _operations);
        return bitmap;
    }

    private static SKPoint ToSkPoint(Point point) => new((float)point.X, (float)point.Y);
    private static Color ToAvaloniaColor(SKColor color) => new(color.Alpha, color.Red, color.Green, color.Blue);
}
