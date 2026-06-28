---
id: ff2bb2bf
created: 2026-06-25T03:04:05Z
tags: ui,toolbar,design,changes
---
Доработан дизайн панели инструментов (ToolbarControl).

Изменения:
- В правом верхнем углу панели добавлена круглая красная кнопка закрытия с белым крестиком (16x16 px, CornerRadius=8). Кнопка размещена с небольшим выступом за верхний правый край панели (Margin="0,-6,-6,0").
- Панель сделана ниже: размер кнопок инструментов уменьшен с 24x24 до 20x20, Padding с 3 до 2, иконки внутри кнопок уменьшены до 14x14, Padding самой панели уменьшен.
- Увеличены отступы между кнопками: StackPanel.Spacing увеличен с 3 до 8.
- Старый белый CancelButton убран из горизонтального ряда; его функциональность перенесена на новую красную кнопку закрытия.

Затронутые файлы:
- src/Histshot/Views/ToolbarControl.axaml — полностью обновлён макет панели и добавлен стиль closeButton.
- src/Histshot/Views/ToolbarControl.axaml.cs — метод ShowCancelButton переименован в ShowCloseButton, обработчик CancelButton_Click переименован в CloseButton_Click.
- src/Histshot/Views/CaptureOverlayWindow.axaml.cs — вызов ShowCancelButton заменён на ShowCloseButton.

Проверка:
- dotnet build — успешно, 0 предупреждений, 0 ошибок.
- dotnet test — 12/12 тестов проходят.
