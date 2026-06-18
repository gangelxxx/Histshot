---
id: 330dacdc
created: 2026-07-27T12:02:25Z
tags: ui,history,avalonia,listbox
---
Окно истории: ListBox заменён на ItemsControl+ScrollViewer (HistoryItems) — в Avalonia нет SelectionMode.None, а ListBox выделял item при любом клике внутри карточки (мерцание). Hover-подсветка карточки (Border.card:pointerover) убрана — карточка всегда #232323. Отступы карточек перенесены в стиль Border.card (Margin 0,0,0,8). Поле комментария в истории: класс commentBox со стилями на PART_BorderElement (серая рамка во всех состояниях), FocusAdorner=null, при двойном клике фокус + курсор в конец (SelectionStart/End/CaretIndex = длина).
