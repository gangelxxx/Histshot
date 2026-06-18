using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Linq;

namespace Histshot.Views;

/// <summary>
/// Modal dialog for clearing the history: the user picks how many recent days
/// of screenshots to keep; everything older is deleted. The dialog result is
/// the number of days to keep (0 = delete everything), or null when cancelled.
/// </summary>
public partial class ClearHistoryDialog : Window
{
    public ClearHistoryDialog()
    {
        InitializeComponent();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void DeleteButton_Click(object? sender, RoutedEventArgs e)
    {
        var tag = OptionsPanel.Children
            .OfType<RadioButton>()
            .FirstOrDefault(r => r.IsChecked == true)
            ?.Tag?.ToString();

        Close(int.TryParse(tag, out var days) ? days : (int?)null);
    }
}
