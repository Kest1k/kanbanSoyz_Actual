Ты работаешь в репозитории Soyuz-PLM Kanban на Windows. Платформа фронта: WebBrowser-контрол Trident (IE11 в режиме edge через X-UA-Compatible).

ЖЁСТКИЕ ПРАВИЛА КОДА:
- Только ES5 в kanban.js: var/function. Никаких const/let/=>/template literals/optional chaining/spread.
- Никаких CSS-переменных var(--x).
- C# — стиль .NET Framework, как в существующем SOYUZ_UPLOAD_KanbanScreen_script.cs (using System.Collections.Generic; явно — без LINQ-выкрутасов).
- JSON-парсинг подзадач через существующие ParseSubtasks/SerializeSubtasks (они уже клонируют ParseComments/SerializeComments).
- Все мутации SubtasksJSON ОБЯЗАТЕЛЬНО обновляют SubtasksTotal/SubtasksDone и вызывают AppendSubtaskChangeLog (для правки текста / порядка — оба тоже логируем).
- JS-вызов сервера: window.external.InvokeTemplate("Method", "args|разделённые|пайпом").
- Текст подзадач: text.replace(/\|/g, " ") на клиенте перед отправкой.
- HTML5 Drag-and-Drop работает (см. kbDragStart в kanban.js:166-231) — использовать тот же паттерн.
- IE11: dataTransfer.setData("text", x) НЕ "text/plain". Привязки draggable + ondragstart как inline-атрибуты в строке.

ФАЙЛЫ ПРОЕКТА (рабочий каталог C:\MYPROJECTS\kanbanSoyz_Actual):
- scripts/SOYUZ_UPLOAD_KanbanScreen_script.cs
- scripts/kanban.js
- scripts/KanbanBoard_HTML.html
- scripts/kanban.css

ЗАДАНИЕ: Прочитай файл `KANBAN_IMPROVEMENTS_PHASE3(08.05)/01_checklist_dnd_edit/prompt.md` целиком. Это самая большая фича фазы:
- серверные методы EditSubtask / ReorderSubtasks
- перестановка HTML вкладки «Чек-лист»: поле ввода теперь СВЕРХУ (над списком), а не снизу
- новый рендер строки чек-листа с drag-handle и dblclick-edit
- блок DnD-обработчиков
- блок inline-редактирования
- CSS подсветок + переворот отступов на .kb-subt-add (border-bottom вместо border-top)

ПРОЦЕСС:
1. Прочитай prompt.md полностью. Запомни структуру атомарных коммитов (6 шагов).
2. Прочитай существующие методы DoAddSubtask / DoToggleSubtask / DoDeleteSubtask в SOYUZ_UPLOAD_KanbanScreen_script.cs (~3062-3300) — новые методы делать в том же стиле, рядом, в файле.
3. Прочитай функции tcmSubtasksRender / tcmSubtasksAdd / tcmSubtasksToggle / tcmSubtasksDelete в kanban.js (~870-1002) — изменить рендер и добавить рядом обработчики DnD/edit.
4. Делай атомарные коммиты СТРОГО в порядке таблицы «Атомарные коммиты» prompt.md.
5. После всех коммитов: `git log --oneline -7` + `git diff master..HEAD --stat`.

КРИТИЧЕСКИЕ ТОЧКИ:
- Сигнатура AppendSubtaskChangeLog: проверь её в SOYUZ_UPLOAD_KanbanScreen_script.cs перед использованием. В prompt.md указан вариант (task, action, text), но если фактически другой — адаптируй.
- ParsePipeArgs(raw, 3) для EditSubtask: убедись, что для последнего аргумента (newText) функция сохраняет всё после второго `|` целиком, не дробя дальше. Если режет ровно по N-1 разделителей — ОК.
- Откат при ошибке сервера: после неудачного ReorderSubtasks/EditSubtask вызывать tcmSubtasksLoad(_tcmSubtasks.taskKey) чтобы перечитать состояние.

ОГРАНИЧЕНИЯ:
- НЕ делай git push.
- НЕ запускай smoke-тесты.
- НЕ трогай существующие методы DoAddSubtask / DoToggleSubtask / DoDeleteSubtask / DoGetSubtasks — только добавляй новые методы рядом.
- НЕ меняй существующий tcmSubtasksAdd/Delete/Toggle/Keydown — только tcmSubtasksRender заменяется целиком + добавляются новые блоки.
- НЕ переименовывай _tcmSubtasks / tcmChatEsc — другие места кода зависят.
- НЕ меняй pmszcfg — атрибуты SubtasksJSON / SubtasksTotal / SubtasksDone уже есть с Phase 2.

Начинай с режима Plan: разверни план по 6 коммитам. Жди подтверждения.
