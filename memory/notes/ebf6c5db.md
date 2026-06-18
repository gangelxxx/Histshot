---
id: ebf6c5db
created: 2026-06-25T03:29:24Z
tags: ui,toolbar,fix,close-button
---
Исправлено отображение кнопки закрытия на ToolbarControl.

Проблема: красная кнопка закрытия, вынесенная за пределы панели отрицательным Margin, обрезалась родительским контейнером/экраном и была видна лишь частично.

Решение:
- Кнопка перенесена внутрь панели, в правый верхний угол (HorizontalAlignment=Right, VerticalAlignment=Top, Margin="0,2,2,0").
- Уменьшен размер кнопки до 14x14 px (CornerRadius=7), иконка крестика 6x6.
- У StackPanel добавлен правый отступ Margin="0,0,16,0", чтобы содержимое не накладывалось на кнопку закрытия.
- Отрицательный Margin убран.

Файл: src/Histshot/Views/ToolbarControl.axaml.

Проверка:
- dotnet build — успешно.
- dotnet publish (Release, win-x64) — успешно, exe обновлён: publish/win-x64/Histshot.exe.
