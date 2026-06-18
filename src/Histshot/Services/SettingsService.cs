using Histshot.Core.Models;
using Histshot.Core.Services;
using Microsoft.Win32;
using System;
using System.IO;
using System.Runtime.Versioning;
using System.Text.Json;

namespace Histshot.Services;

public class SettingsService : ISettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Histshot", "settings.json");

    public AppSettings Settings { get; private set; }

    public event EventHandler? Saved;

    public SettingsService()
    {
        Settings = Load();
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
#pragma warning disable CA1416
            ApplyAutostart();
#pragma warning restore CA1416
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch { }
    }

    [SupportedOSPlatform("windows")]
    private void ApplyAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null) return;
            const string appName = "Histshot";
            if (Settings.Autostart)
            {
                var exePath = Environment.ProcessPath
                    ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                    ?? "Histshot.exe";
                key.SetValue(appName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(appName, throwOnMissingValue: false);
            }
        }
        catch { }
    }
}
