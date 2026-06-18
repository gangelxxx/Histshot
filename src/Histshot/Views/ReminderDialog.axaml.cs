using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace Histshot.Views;

/// <summary>Result of <see cref="ReminderDialog"/>: either a reminder time to set, or a removal request.</summary>
public class ReminderResult
{
    public bool Remove { get; set; }
    public DateTime When { get; set; }
}

/// <summary>
/// Modal dialog for scheduling a reminder: a date plus a time of day.
/// The dialog result is a <see cref="ReminderResult"/>, or null when cancelled.
/// </summary>
public partial class ReminderDialog : Window
{
    public ReminderDialog() : this(null)
    {
    }

    public ReminderDialog(DateTime? existing)
    {
        InitializeComponent();

        // For a new reminder prefill with the current system date and time.
        var initial = existing ?? DateTime.Now;
        ReminderDatePicker.SelectedDate = new DateTimeOffset(initial.Date);
        ReminderTimePicker.SelectedTime = initial.TimeOfDay;

        // The remove button makes sense only when a reminder already exists.
        RemoveButton.IsVisible = existing.HasValue;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void RemoveButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(new ReminderResult { Remove = true });
    }

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (ReminderDatePicker.SelectedDate is not { } date || ReminderTimePicker.SelectedTime is not { } time)
            return;

        Close(new ReminderResult { When = date.DateTime.Date + time });
    }
}
