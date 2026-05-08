Ты работаешь в репозитории Soyuz-PLM Kanban на Windows. Платформа фронта: WebBrowser-контрол Trident (IE11 в режиме edge через X-UA-Compatible).

ЖЁСТКИЕ ПРАВИЛА КОДА:
- Только ES5 в kanban.js: var/function. Никаких const/let/=>/template literals/optional chaining/spread.
- Никаких CSS-переменных var(--x).
- Никаких новых зависимостей.
- JS-вызов сервера: window.external.InvokeTemplate("Method", "args|разделённые|пайпом").
- Существующие имена функций/переменных НЕ переименовывать.

ФАЙЛЫ ПРОЕКТА (рабочий каталог C:\MYPROJECTS\kanbanSoyz_Actual):
- scripts/kanban.js  ← фича только тут

ЗАДАНИЕ: Прочитай файл `KANBAN_IMPROVEMENTS_PHASE3(08.05)/03_employee_select_keep/prompt.md` целиком. Выполни ВСЕ указанные правки строго по нему. Файл содержит точные пути, номера строк, готовые блоки кода и порядок атомарных коммитов.

КОНТЕКСТ КОРНЕВОЙ ПРИЧИНЫ (из prompt.md):
- В kbRestoreViewMode при mode="user:KEY" происходит: kbFillUsers пересоздаёт список, потом selUser.value = userKey. Если опция отсутствует в списке (фильтр по контексту/поиску её исключил) — браузер молча не применяет value, селект сбрасывается на «Все сотрудники».
- Решение: добавить хелпер kbHasOption и при отсутствии — вручную добавить опцию в селект, потом установить value. Дополнительно: в kbOnUserChange оптимистично писать _kbH.viewMode чтобы избежать гонки до RefreshBoard.

ПРОЦЕСС:
1. Прочитай prompt.md полностью.
2. Прочитай kanban.js в зоне функций kbInitHierarchy / kbFillUsers / kbRestoreViewMode / kbOnUserChange / kbOpt (~строки 1015-1450).
3. Делай атомарные коммиты в порядке указанном в разделе «Атомарные коммиты».
4. После всех коммитов — `git log --oneline -3` + `git diff master..HEAD --stat`.

ОГРАНИЧЕНИЯ:
- НЕ делай git push.
- НЕ запускай smoke-тесты.
- НЕ трогай файлы кроме kanban.js.
- НЕ меняй логику kbApplyMode / kbSendMode — только кейс восстановления.
- Если номера строк сдвинулись — ищи функции по имени.

Начинай с режима Plan. Жди подтверждения.
