---
id: ad41dd7d
created: 2026-07-27T12:49:51Z
tags: feature,history,search,ui
---
Окно истории: добавлен поиск по тексту комментариев — SearchTextBox в шапке (ApplyFilter фильтрует _viewModels по substring OrdinalIgnoreCase, вызывается из TextChanged и после каждой загрузки). Поле с классами commentBox+searchBox; searchBox:not(:empty) показывает кнопку очистки через InnerRightContent с Command={Binding [TextBox].Clear} (встроенный класс clearButton темы показывает крестик только при :focus — не подошёл). Диалог очистки истории переделан с NumericUpDown на RadioButton-ы (1/3/7/14/30 дней, 0=удалить всё) — NumericUpDown во Fluent выглядел сломанным при малой ширине.
