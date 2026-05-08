Ты работаешь в репозитории Soyuz-PLM Kanban на Windows. Платформа фронта: WebBrowser-контрол Trident (IE11 в режиме edge через X-UA-Compatible).

ЖЁСТКИЕ ПРАВИЛА КОДА:
- Только ES5 в kanban.js: var/function. Никаких const/let/=>/template literals.
- C# — стиль .NET Framework, как в существующем SOYUZ_UPLOAD_KanbanScreen_script.cs.
- JS-вызов сервера: window.external.InvokeTemplate("Method", "args|разделённые|пайпом").
- Существующие имена функций/переменных НЕ переименовывать.
- Атрибут Creator (Text, NameKey) уже пишется в DoCreateTask (line 379) и DoCreateGroupTask (line 475). НИКАКИХ новых атрибутов БД не вводить.

ФАЙЛЫ ПРОЕКТА (рабочий каталог C:\MYPROJECTS\kanbanSoyz_Actual):
- scripts/SOYUZ_UPLOAD_KanbanScreen_script.cs
- scripts/kanban.js
- scripts/KanbanBoard_HTML.html
- scripts/kanban.css

ЗАДАНИЕ: Прочитай файл `KANBAN_IMPROVEMENTS_PHASE3(08.05)/04_tasks_issued_by_me/prompt.md` целиком. Цель: добавить режим viewMode="myCreated" — показывает только задачи где Creator == текущий пользователь.

ПРОЦЕСС:
1. Прочитай prompt.md полностью.
2. Прочитай BeforeRender в SOYUZ_UPLOAD_KanbanScreen_script.cs (~22-146) и GetAllowedUserIdSet (~1436-1503).
3. Прочитай в kanban.js функции kbInitHierarchy / kbSetMyMode / kbSetAllMode / kbRestoreViewMode / kbStyleBtn / kbUpdateMyBtn / kbUpdateAllBtn (~1015-1260).
4. Прочитай блок kb-hier-panel в KanbanBoard_HTML.html (~28-57).
5. Делай атомарные коммиты в порядке таблицы «Атомарные коммиты».
6. После всех коммитов: `git log --oneline -5` + `git diff master..HEAD --stat`.

КРИТИЧЕСКИЕ ТОЧКИ:
- Кэшируй myCreatorKey ВНЕ цикла foreach в BeforeRender — иначе будет вычисляться на каждой задаче.
- Кнопка kb-btn-mycreated скрыта для role="regular".
- kbUpdateMyBtn и kbUpdateAllBtn должны сбрасывать kb-btn-mycreated тоже (чтобы три кнопки были взаимоисключающи).
- Если фича 02 (header_responsive) ещё не сделана — кнопка с inline-стилями по образцу kb-btn-all. Если сделана — class="kb-nav-btn".
- ПРОВЕРЬ: существует ли уже функция kbStyleBtn в kanban.js. Если да — используй её. Если нет — создай минимальную (см. prompt.md, раздел B.2.3).

ОГРАНИЧЕНИЯ:
- НЕ делай git push.
- НЕ запускай smoke-тесты.
- НЕ меняй .pmszcfg.
- НЕ трогай логику фильтра периода / GetReport.
- Используй существующий task["Creator"] — никаких task.CreatedByUser или новых атрибутов.

Начинай с режима Plan. Жди подтверждения.
