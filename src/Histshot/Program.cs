using Avalonia;
using Optris.Icons.Avalonia;
using Optris.Icons.Avalonia.MaterialDesign;
using System;
using System.Runtime.InteropServices;
using Velopack;

namespace Histshot;

sealed class Program
{
    private const int DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run before anything else: on install/update/uninstall Velopack invokes the app
        // with special hook arguments, runs the requested action, and exits — so this returns
        // immediately during normal launches but never reaches the UI during a hook run.
        VelopackApp.Build().Run();

        if (OperatingSystem.IsWindows())
        {
            SetProcessDpiAwarenessContext((IntPtr)DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        IconProvider.Current.Register<MaterialDesignIconProvider>();

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Software rendering: this tray tool shows a window only briefly during a capture, so a
            // GPU compositor isn't worth its cost — keeping it would hold a GPU context and tick a
            // render loop continuously (~0.7% GPU) even while idle in the tray. CPU rendering drops
            // idle GPU to zero and trims GPU-side memory; the only trade-off is slightly less smooth
            // full-screen overlay interaction on very high-DPI screens.
            .With(new Win32PlatformOptions { RenderingMode = new[] { Win32RenderingMode.Software } })
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
    }
}
