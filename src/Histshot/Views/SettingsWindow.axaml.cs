using Avalonia.Controls;
using Avalonia.Interactivity;
using Histshot.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Histshot.Views;

public partial class SettingsWindow : Window
{
    private readonly ISettingsService _settingsService;

    private static readonly LanguageItem[] Languages =
    [
        new("en", "English"),
        new("ru", "Russian - Русский"),
    ];

    public SettingsWindow()
    {
        _settingsService = App.Services.GetRequiredService<ISettingsService>();
        InitializeComponent();
        SetupControls();
        LoadSettings();
    }

    private void SetupControls()
    {
        LanguageComboBox.ItemsSource = Languages;
    }

    private void LoadSettings()
    {
        var s = _settingsService.Settings;

        AutostartCheckBox.IsChecked = s.Autostart;

        var langIdx = Array.FindIndex(Languages, l => l.Code == s.Language);
        LanguageComboBox.SelectedIndex = langIdx >= 0 ? langIdx : 0;

        PrimaryHotkeyEnabled.IsChecked = s.PrimaryHotkeyEnabled;
        PrimaryHotkeyBox.Text = s.PrimaryHotkey;
        PrimaryHotkeyBox.IsEnabled = s.PrimaryHotkeyEnabled;

        QuickSaveEnabled.IsChecked = s.QuickSaveEnabled;
        QuickSaveBox.Text = s.QuickSaveHotkey;
        QuickSaveBox.IsEnabled = s.QuickSaveEnabled;
    }

    private void PrimaryHotkeyEnabled_Changed(object? sender, RoutedEventArgs e)
        => PrimaryHotkeyBox.IsEnabled = PrimaryHotkeyEnabled.IsChecked == true;

    private void QuickSaveEnabled_Changed(object? sender, RoutedEventArgs e)
        => QuickSaveBox.IsEnabled = QuickSaveEnabled.IsChecked == true;

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var s = _settingsService.Settings;

        s.Autostart = AutostartCheckBox.IsChecked == true;
        if (LanguageComboBox.SelectedItem is LanguageItem lang)
            s.Language = lang.Code;

        s.PrimaryHotkeyEnabled = PrimaryHotkeyEnabled.IsChecked == true;
        s.PrimaryHotkey = PrimaryHotkeyBox.Text ?? s.PrimaryHotkey;

        s.QuickSaveEnabled = QuickSaveEnabled.IsChecked == true;
        s.QuickSaveHotkey = QuickSaveBox.Text ?? s.QuickSaveHotkey;

        _settingsService.Save();
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
        => Close();
}

public record LanguageItem(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}
