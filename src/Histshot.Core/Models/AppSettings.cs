namespace Histshot.Core.Models;

public class AppSettings
{
    public bool Autostart { get; set; }
    public string Language { get; set; } = "en";

    public bool PrimaryHotkeyEnabled { get; set; } = true;
    public string PrimaryHotkey { get; set; } = "Prnt Scrn";

    public bool QuickSaveEnabled { get; set; } = true;
    public string QuickSaveHotkey { get; set; } = "Shift + Prnt Scrn";
}
