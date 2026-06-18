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
        ["Comment"] = "Comment",
        ["Tip_CommentScreenshot"] = "Add or edit comment",
        ["CommentWatermark"] = "Add a comment…",
        ["Tip_CopyCommentToClipboard"] = "Copy comment text",
        ["Tip_DoubleClickToEdit"] = "Double-click to edit",
        ["SearchComments"] = "Search comments…",
        ["Reminder_Title"] = "Reminder",
        ["Reminder_Date"] = "Date",
        ["Reminder_Time"] = "Time",
        ["Reminder_Remove"] = "Remove",
        ["Tip_Reminder"] = "Remind about this screenshot",
        ["Notification_Title"] = "Screenshot reminder",
        ["ReminderLabel"] = "Reminder",
        ["ClearHistoryDialog_Title"] = "Clear history",
        ["ClearHistoryPrompt"] = "Screenshots from the chosen period will be kept; older ones will be deleted permanently.",
        ["KeepLastDays"] = "Keep screenshots from the last:",
        ["Keep1Day"] = "1 day",
        ["Keep3Days"] = "3 days",
        ["Keep7Days"] = "7 days",
        ["Keep14Days"] = "14 days",
        ["Keep30Days"] = "30 days",
        ["KeepNone"] = "Nothing — delete everything",

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
        ["Tip_CopyToClipboard"] = "Копировать снимок в буфер обмена",
        ["Copy"] = "Копировать",
        ["Tip_DeleteScreenshot"] = "Удалить снимок",
        ["Delete"] = "Удалить",
        ["Comment"] = "Комментарий",
        ["Tip_CommentScreenshot"] = "Добавить или изменить комментарий",
        ["CommentWatermark"] = "Добавить комментарий…",
        ["Tip_CopyCommentToClipboard"] = "Копировать текст комментария",
        ["Tip_DoubleClickToEdit"] = "Двойной клик — редактировать",
        ["SearchComments"] = "Поиск по комментариям…",
        ["Reminder_Title"] = "Напоминание",
        ["Reminder_Date"] = "Дата",
        ["Reminder_Time"] = "Время",
        ["Reminder_Remove"] = "Удалить",
        ["Tip_Reminder"] = "Напомнить о снимке",
        ["Notification_Title"] = "Напоминание о снимке",
        ["ReminderLabel"] = "Напоминание",
        ["ClearHistoryDialog_Title"] = "Очистка истории",
        ["ClearHistoryPrompt"] = "Снимки за выбранный период останутся, более старые будут удалены безвозвратно.",
        ["KeepLastDays"] = "Оставить снимки за последние:",
        ["Keep1Day"] = "1 день",
        ["Keep3Days"] = "3 дня",
        ["Keep7Days"] = "7 дней",
        ["Keep14Days"] = "14 дней",
        ["Keep30Days"] = "30 дней",
        ["KeepNone"] = "Ничего — удалить всё",

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
