using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Histshot.Core.Models;
using Histshot.Core.Services;
using SkiaSharp;
using System;

namespace Histshot.Views;

public partial class ToolbarControl : UserControl
{
    private readonly ToolSettings _settings = new();

    public event EventHandler<ToolType>? ToolSelected;
    public event EventHandler<SKColor>? ColorChanged;
    public event EventHandler<float>? ThicknessChanged;
    public event EventHandler<float>? FontSizeChanged;
    public event EventHandler? CopyRequested;
    public event EventHandler? CancelRequested;

    public ToolbarControl()
    {
        InitializeComponent();
        UpdateToolSelection();
        PointerPressed += (_, e) => e.Handled = true;
    }

    public void ShowCloseButton()
    {
        CloseButton.IsVisible = true;
    }

    /// <summary>The comment entered in the panel below the toolbar (empty when none).</summary>
    public string Comment => CommentTextBox.Text?.Trim() ?? string.Empty;

    /// <summary>The reminder time chosen for the capture being edited (null = none).</summary>
    public DateTime? ReminderAt { get; private set; }

    public event EventHandler? ReminderRequested;

    /// <summary>Sets the pending reminder; the bell icon is highlighted while one is set.</summary>
    public void SetReminder(DateTime? when)
    {
        ReminderAt = when;
        ReminderIcon.Fill = when.HasValue
            ? new SolidColorBrush(Color.Parse("#4D9DE0"))
            : Brushes.White;
    }

    private void ReminderButton_Click(object? sender, RoutedEventArgs e)
    {
        ReminderRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CommentTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var hasText = !string.IsNullOrWhiteSpace(CommentTextBox.Text);
        ReminderButton.IsVisible = hasText;

        // The reminder belongs to the comment: clearing the text drops it.
        if (!hasText && ReminderAt.HasValue)
            SetReminder(null);
    }

    private void CommentButton_Click(object? sender, RoutedEventArgs e)
    {
        CommentPanel.IsVisible = !CommentPanel.IsVisible;
        if (CommentPanel.IsVisible)
            CommentTextBox.Focus();
    }

    private void ToolbarBorder_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void ToolButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string tag)
        {
            var tool = Enum.Parse<ToolType>(tag);
            DebugLogger.Log($"ToolbarControl.ToolButton_Click: {tool}");
            _settings.Tool = tool;
            UpdateToolSelection();
            ToolSelected?.Invoke(this, tool);
        }
    }

    private static readonly IBrush ActiveToolBrush = new SolidColorBrush(Color.Parse("#55FFFFFF"));

    private void UpdateToolSelection()
    {
        PencilButton.Background = _settings.Tool == ToolType.Pencil ? ActiveToolBrush : Brushes.Transparent;
        LineButton.Background = _settings.Tool == ToolType.Line ? ActiveToolBrush : Brushes.Transparent;
        ArrowButton.Background = _settings.Tool == ToolType.Arrow ? ActiveToolBrush : Brushes.Transparent;
        RectangleButton.Background = _settings.Tool == ToolType.Rectangle ? ActiveToolBrush : Brushes.Transparent;
        SelectionButton.Background = _settings.Tool == ToolType.Selection ? ActiveToolBrush : Brushes.Transparent;
        TextButton.Background = _settings.Tool == ToolType.Text ? ActiveToolBrush : Brushes.Transparent;

        UpdateSliderVisibility();
    }

    // The font-size slider is shown whenever the Text tool is active OR a text
    // annotation is currently being edited (e.g. opened with the pointer tool), so
    // its size can always be adjusted; otherwise the thickness slider is shown.
    private bool _textEditingActive;

    public void SetTextEditing(bool active, float fontSize = 0f)
    {
        _textEditingActive = active;
        if (active)
            FontSizeSlider.Value = Math.Clamp(fontSize, FontSizeSlider.Minimum, FontSizeSlider.Maximum);
        UpdateSliderVisibility();
    }

    private void UpdateSliderVisibility()
    {
        bool textMode = _settings.Tool == ToolType.Text || _textEditingActive;
        FontSizeSlider.IsVisible = textMode;
        ThicknessSlider.IsVisible = !textMode;
    }

    private void ColorPreview_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        var colors = new[] { SKColors.Red, SKColors.Green, SKColors.Blue, SKColors.Yellow, SKColors.White, SKColors.Black };
        var currentIndex = Array.IndexOf(colors, _settings.Color);
        _settings.Color = colors[(currentIndex + 1) % colors.Length];
        ColorPreview.Background = new SolidColorBrush(ToAvaloniaColor(_settings.Color));
        ColorChanged?.Invoke(this, _settings.Color);
    }

    private void ThicknessSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        _settings.LineThickness = (float)e.NewValue;
        ThicknessChanged?.Invoke(this, _settings.LineThickness);
    }

    private void FontSizeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        _settings.FontSize = (float)e.NewValue;
        FontSizeChanged?.Invoke(this, _settings.FontSize);
    }

    private void CopyButton_Click(object? sender, RoutedEventArgs e)
    {
        CopyRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private static Color ToAvaloniaColor(SKColor color) => new(color.Alpha, color.Red, color.Green, color.Blue);
}

/// <summary>
/// Multi-line comment box: Shift+Enter inserts a newline, plain Enter is swallowed
/// (so it neither inserts a newline nor bubbles up to window-level hotkeys) and is
/// re-raised as <see cref="EnterPressed"/> so hosts can use it as "confirm".
/// </summary>
public class CommentTextBox : TextBox
{
    // Keep the TextBox style key so the Fluent ControlTheme and the
    // "TextBox.commentBox" selectors in this control's styles match this subclass.
    protected override Type StyleKeyOverride => typeof(TextBox);

    /// <summary>Raised when Enter is pressed without Shift (the newline is not inserted).</summary>
    public event EventHandler? EnterPressed;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            EnterPressed?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnKeyDown(e);
    }
}
