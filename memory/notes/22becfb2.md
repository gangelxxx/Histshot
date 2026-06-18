---
id: 22becfb2
created: 2026-07-27T10:44:42Z
tags: feature,history,toolbar,comment
---
Комментарий теперь можно ввести прямо при съёмке: в ToolbarControl добавлена кнопка с иконкой пузыря (CommentButton, перед кнопкой копирования), по клику под панелью появляется CommentPanel с CommentTextBox (корневой Grid тулбара теперь RowDefinitions Auto,Auto). ToolbarControl.Comment возвращает trimmed текст. Проброс: EditorControl.CopyAsync(string? comment = null) → IHistoryService.SaveAsync(bitmap, string? comment = null) — комментарий пишется в sidecar comment_*.txt сразу при сохранении. Вызовы обновлены: CaptureOverlayWindow.OnCopyRequested передаёт _toolbarControl?.Comment, EditorWindow — Toolbar.Comment. Позиционирование тулбара (PositionToolbar в обоих окнах) пересчитывается через LayoutUpdated при появлении панели. Тест SaveAsync_WithComment_PersistsComment добавлен, 17/17 проходят.
