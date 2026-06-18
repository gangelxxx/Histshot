---
id: d8eede49
created: 2026-07-27T14:06:31Z
tags: feature,reminder,history,toolbar
---
Фича напоминаний (reminder): HistoryItem.ReminderAt, sidecar reminder_<имя>.txt (DateTime -o), IHistoryService.SetReminderAsync(id, when?) и SaveAsync теперь возвращает Guid. ReminderDialog (DatePicker+TimePicker, новое = текущее системное время, результат ReminderResult{Remove,When}, null=отмена). ReminderService (Histshot.Services): DispatcherTimer 15с, просроченные → SetReminderAsync(null) + ReminderNotificationWindow (topmost, без декораций WindowDecorations=None, правый нижний угол WorkingArea, автозакрытие 12с); клик → HistoryWindow.HighlightItem(id): ScrollIntoView + IsHighlighted на 3с (Classes.highlighted биндинг на Border.card). Колокольчик: в ToolbarControl внутри CommentPanel (справа, ToolbarControl.ReminderAt/SetReminder/ReminderRequested), проброс через EditorControl.CopyAsync(comment, reminderAt); в карточке истории кнопка с Classes.hasReminder биндингом (синий колокольчик #4D9DE0). 21/21 тестов, publish пересобран.
