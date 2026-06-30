using System.Collections.Generic;

namespace Histshot.Localization;

/// <summary>
/// Lightweight in-app localization. UI text is looked up by key at XAML load time
/// via <see cref="LocalizeExtension"/>; windows are recreated when reopened, so
/// changing <see cref="Language"/> and reopening a window applies the new language.
/// </summary>
public static class Localization
{
    /// <summary>Active language code ("en" or "ru"). Set from settings at startup and on save.</summary>
    public static string Language { get; set; } = "en";

    private static readonly Dictionary<string, string> En = new()
    {
        ["Settings_Title"] = "Settings",
        ["Tab_General"] = "General",
        ["Tab_Hotkeys"] = "Hotkeys",
        ["LaunchAtStartup"] = "Launch at startup",
        ["Language"] = "Language",
        ["PrimaryHotkey"] = "Primary hotkey",
        ["QuickSaveFullScreen"] = "Quick save full screen",
        ["Cancel"] = "Cancel",
        ["Save"] = "Save",

        ["History_Title"] = "Histshot History",
        ["ScreenshotHistory"] = "Screenshot History",
        ["ClearHistory"] = "Clear history",
        ["Tip_RemoveAllScreenshots"] = "Remove all screenshots",
        ["Tip_PreviewOrEdit"] = "Left-click: preview · Right-click: open in editor",
        ["Tip_CopyToClipboard"] = "Copy to clipboard",
        ["Copy"] = "Copy",
        ["Tip_DeleteScreenshot"] = "Delete screenshot",
        ["Delete"] = "Delete",

        ["Tool_Pencil"] = "Pencil",
        ["Tool_Line"] = "Line",
        ["Tool_Arrow"] = "Arrow",
        ["Tool_Rectangle"] = "Rectangle",
        ["Tool_Select"] = "Select",
        ["Tool_Text"] = "Text",
        ["Tool_Color"] = "Color",
        ["Tool_Thickness"] = "Thickness",
        ["Tool_FontSize"] = "Font size",

        ["Preview_Title"] = "Histshot Preview",
        ["Tip_Previous"] = "Previous (←)",
        ["Tip_Next"] = "Next (→)",

        ["Menu_History"] = "History",
        ["Menu_Settings"] = "Settings",
        ["Menu_Update"] = "Update",
        ["Menu_Exit"] = "Exit",
    };

    private static readonly Dictionary<string, string> Ru = new()
    {
        ["Settings_Title"] = "Настройки",
        ["Tab_General"] = "Основные",
        ["Tab_Hotkeys"] = "Горячие клавиши",
        ["LaunchAtStartup"] = "Запускать при старте системы",
        ["Language"] = "Язык",
        ["PrimaryHotkey"] = "Основная горячая клавиша",
        ["QuickSaveFullScreen"] = "Быстрое сохранение экрана",
        ["Cancel"] = "Отмена",
        ["Save"] = "Сохранить",

        ["History_Title"] = "История Histshot",
        ["ScreenshotHistory"] = "История снимков",
        ["ClearHistory"] = "Очистить историю",
        ["Tip_RemoveAllScreenshots"] = "Удалить все снимки",
        ["Tip_PreviewOrEdit"] = "ЛКМ: просмотр · ПКМ: открыть в редакторе",
        ["Tip_CopyToClipboard"] = "Копировать в буфер обмена",
        ["Copy"] = "Копировать",
        ["Tip_DeleteScreenshot"] = "Удалить снимок",
        ["Delete"] = "Удалить",

        ["Tool_Pencil"] = "Карандаш",
        ["Tool_Line"] = "Линия",
        ["Tool_Arrow"] = "Стрелка",
        ["Tool_Rectangle"] = "Прямоугольник",
        ["Tool_Select"] = "Выделение",
        ["Tool_Text"] = "Текст",
        ["Tool_Color"] = "Цвет",
        ["Tool_Thickness"] = "Толщина",
        ["Tool_FontSize"] = "Размер шрифта",

        ["Preview_Title"] = "Просмотр Histshot",
        ["Tip_Previous"] = "Предыдущий (←)",
        ["Tip_Next"] = "Следующий (→)",

        ["Menu_History"] = "История",
        ["Menu_Settings"] = "Настройки",
        ["Menu_Update"] = "Обновиться",
        ["Menu_Exit"] = "Выход",
    };

    /// <summary>Returns the translation for <paramref name="key"/> in the active language,
    /// falling back to English and finally to the key itself.</summary>
    public static string Get(string key)
    {
        var table = Language == "ru" ? Ru : En;
        if (table.TryGetValue(key, out var value))
            return value;
        if (En.TryGetValue(key, out var fallback))
            return fallback;
        return key;
    }
}
