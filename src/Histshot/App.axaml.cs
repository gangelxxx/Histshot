using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Histshot.Core.Capture;
using Histshot.Core.Models;
using Histshot.Core.Services;
using Histshot.Services;
using Histshot.Views;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Histshot;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>Window/tray icon matching the current OS theme — dark glyph in light (day) mode, white in dark (night) mode.</summary>
    public static WindowIcon? ThemedWindowIcon { get; private set; }

    private SettingsWindow? _settingsWindow;
    private TrayIcon? _trayIcon;
    private WindowIcon? _iconLight; // dark glyph — for the light (day) theme
    private WindowIcon? _iconDark;  // white glyph — for the dark (night) theme
    private NativeMenuItem? _updateMenuItem; // disabled until a downloaded update is ready
    private UpdateService? _updateService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        _iconLight = LoadIcon("app-light.ico");
        _iconDark = LoadIcon("app-dark.ico");

        if (TrayIcon.GetIcons(this) is { Count: > 0 } icons)
        {
            // The menu is built later (BuildTrayMenu), once the saved UI language is known.
            _trayIcon = icons[0];
        }

        // Keep the tray and window icons in sync with the OS light/dark theme.
        ActualThemeVariantChanged += (_, _) => ApplyThemeIcons();
        ApplyThemeIcons();
    }

    private static WindowIcon LoadIcon(string fileName)
        => new(AssetLoader.Open(new Uri($"avares://Histshot/Assets/{fileName}")));

    private void ApplyThemeIcons()
    {
        var icon = ActualThemeVariant == ThemeVariant.Light ? _iconLight : _iconDark;
        ThemedWindowIcon = icon;

        if (_trayIcon != null)
            _trayIcon.Icon = icon;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
                window.Icon = icon;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            DebugLogger.Log($"Unhandled exception: {e.ExceptionObject}");
            if (e.ExceptionObject is Exception ex)
                DebugLogger.Log(ex.StackTrace ?? "No stack trace");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            DebugLogger.Log($"Unobserved task exception: {e.Exception}");
            DebugLogger.Log(e.Exception?.StackTrace ?? "No stack trace");
            e.SetObserved();
        };

        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);
        Services = serviceCollection.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        // Re-evaluate now that the OS theme is fully resolved.
        ApplyThemeIcons();

        var settingsService = Services.GetRequiredService<ISettingsService>();

        // Apply the saved UI language before building the tray menu or opening any window.
        Localization.Localization.Language = settingsService.Settings.Language;
        BuildTrayMenu();

        // Wire up global hotkeys and re-apply them (plus language/menu) whenever settings are saved.
#pragma warning disable CA1416
        var hotkeyService = Services.GetRequiredService<GlobalHotkeyService>();
        hotkeyService.Apply(settingsService.Settings, StartCapture, QuickSaveFullScreen);
        settingsService.Saved += (_, _) =>
        {
            Localization.Localization.Language = settingsService.Settings.Language;
            BuildTrayMenu();
            hotkeyService.Apply(settingsService.Settings, StartCapture, QuickSaveFullScreen);
        };
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime dl)
            dl.Exit += (_, _) => hotkeyService.Dispose();
#pragma warning restore CA1416

        // Auto-update: silently check GitHub at startup and download in the background. The tray
        // "Update" item stays disabled until UpdateReady fires (which may be off the UI thread).
        _updateService = Services.GetRequiredService<UpdateService>();
        _updateService.UpdateReady += (_, _) => Dispatcher.UIThread.Post(EnableUpdateMenuItem);
        _ = _updateService.CheckAndDownloadAsync();

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IScreenCaptureProvider, WindowsScreenCapture>();
        services.AddSingleton<IScreenCaptureService, ScreenCaptureService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IHistoryService, HistoryService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<UpdateService>();
#pragma warning disable CA1416
        services.AddSingleton<GlobalHotkeyService>();
#pragma warning restore CA1416
    }

    private void TrayIcon_Clicked(object? sender, EventArgs e)
    {
        StartCapture();
    }

    private void BuildTrayMenu()
    {
        if (_trayIcon == null)
            return;

        // Always visible; enabled only once an update has been downloaded and is ready to apply.
        // Clicking it applies the staged update and relaunches — to the user, just a restart.
        _updateMenuItem = new NativeMenuItem
        {
            Header = Localization.Localization.Get("Menu_Update"),
            Command = new RelayCommand(() => UpdateMenuItem_Click(null, EventArgs.Empty)),
            IsEnabled = _updateService?.IsUpdateReady ?? false
        };

        _trayIcon.Menu = new NativeMenu
        {
            Items =
            {
                new NativeMenuItem { Header = Localization.Localization.Get("Menu_History"), Command = new RelayCommand(() => HistoryMenuItem_Click(null, EventArgs.Empty)) },
                new NativeMenuItemSeparator(),
                new NativeMenuItem { Header = Localization.Localization.Get("Menu_Settings"), Command = new RelayCommand(() => SettingsMenuItem_Click(null, EventArgs.Empty)) },
                _updateMenuItem,
                new NativeMenuItemSeparator(),
                new NativeMenuItem { Header = Localization.Localization.Get("Menu_Exit"), Command = new RelayCommand(() => ExitMenuItem_Click(null, EventArgs.Empty)) }
            }
        };
    }

    private void EnableUpdateMenuItem()
    {
        if (_updateMenuItem != null)
            _updateMenuItem.IsEnabled = true;
    }

    private void UpdateMenuItem_Click(object? sender, EventArgs e)
    {
        // Applies the downloaded update and relaunches; the process exits and does not return here.
        _updateService?.ApplyAndRestart();
    }

    private void HistoryMenuItem_Click(object? sender, EventArgs e)
    {
        var historyWindow = new HistoryWindow
        {
            ShowInTaskbar = true,
            Icon = ThemedWindowIcon
        };
        historyWindow.Show();
    }

    private void SettingsMenuItem_Click(object? sender, EventArgs e)
    {
        if (_settingsWindow?.IsVisible == true)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow { ShowInTaskbar = true, Icon = ThemedWindowIcon };
        _settingsWindow.Show();
    }

    private void ExitMenuItem_Click(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    // "Quick save full screen": grab every monitor at once and send it straight to the
    // clipboard and history, skipping the selection overlay and editor.
    private async void QuickSaveFullScreen()
    {
        try
        {
            var captureService = Services.GetRequiredService<IScreenCaptureService>();
            using var captured = await captureService.CaptureAllScreensAsync();

            var clipboardService = Services.GetRequiredService<IClipboardService>();
            await clipboardService.SetImageAsync(captured.Bitmap);

            var historyService = Services.GetRequiredService<IHistoryService>();
            await historyService.SaveAsync(captured.Bitmap);
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"Quick save failed: {ex}");
        }
    }

    private async void StartCapture()
    {
        try
        {
            var captureService = Services.GetRequiredService<IScreenCaptureService>();
            var screens = captureService.GetScreens();
            if (screens.Count == 0)
                return;

            var overlays = new List<CaptureOverlayWindow>();

            Action closeAll = () =>
            {
                foreach (var o in overlays)
                {
                    try { o.Close(); } catch { }
                }
            };

            for (int i = 0; i < screens.Count; i++)
            {
                var screen = screens[i];
                var bounds = new PixelRect(screen.X, screen.Y, screen.Width, screen.Height);
                var capturedImage = await captureService.CaptureScreenAsync(screen);

                var overlay = new CaptureOverlayWindow(captureService, bounds, screen.Scaling, closeAll, capturedImage)
                {
                    OnCaptured = result =>
                    {
                        closeAll();

                        var editor = new EditorWindow(result.Image, result.X, result.Y, result.Width, result.Height, result.Scaling)
                        {
                            ShowInTaskbar = true,
                            Icon = ThemedWindowIcon
                        };
                        editor.Show();
                    }
                };
                // Starting a selection on this overlay clears any selection on the others, so only
                // one selection exists across all monitors.
                overlay.SelectionStarted = () =>
                {
                    foreach (var other in overlays)
                    {
                        if (!ReferenceEquals(other, overlay))
                            other.ResetState();
                    }
                };
                overlays.Add(overlay);
            }

            foreach (var overlay in overlays)
            {
                overlay.StartCapture();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start capture: {ex}");
        }
    }

}
