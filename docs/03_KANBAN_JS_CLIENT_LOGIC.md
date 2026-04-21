# kanban.js — Клиентская логика

**Файл:** `kanban.js`  
**Тип:** Vanilla JavaScript (ES5, IIFE, без фреймворков)

## Назначение
Клиентский скрипт канбан-доски. Реализует:
- drag & drop карточек
- панель создания задачи
- иерархические фильтры и role-aware UI
- модальное окно карточки задачи
- комментарии, вложения, историю
- отчёт и справку

## Архитектура

Весь код обёрнут в IIFE:

```javascript
(function () {
    // ...
})();
```

Во внешний мир экспортируются только функции, которые вызываются из HTML через `onclick`, `ondrop`, `onchange` и т.д.:

```javascript
window.kbDrop = function (event, newStatus) { ... };
window.doCreateTask = function () { ... };
window.tcmOpen = function (nameKey) { ... };
```

Bridge с backend построен на прямых вызовах:

```javascript
window.external.InvokeTemplate("MethodName", "param1|param2");
```

Отдельного AJAX-слоя с `ajaxAction` здесь больше нет.

## Основные блоки

### 1. Календарь
Публичные функции:
- `calToggle(targetId)`
- `calPrev()`
- `calNext()`
- `calPickDay(day)`
- `calClear()`

Состояние хранится в:
- `_calYear`
- `_calMonth`
- `_calTargetId`

Календарь используется и в панели создания, и в модальном окне карточки.

### 2. Drag & Drop
Публичные функции:
- `kbDragStart(event, taskId)`
- `kbAllowDrop(event)`
- `kbDragLeave(event)`
- `kbDrop(event, newStatus)`

При `kbDrop(...)` клиент:
1. определяет исходный и новый статус
2. показывает confirm при входе/выходе из `Done`
3. вызывает `InvokeTemplate("MoveTask", taskId + "|" + newStatus)`
4. при успехе делает `kbRefreshBoard()`

### 3. Панель создания задачи
Публичные функции:
- `showCreateTask(status)`
- `hideCreateTask()`
- `doCreateTask()`
- `kbCrtAddTag(tag)`
- `kbOnPriorityChange()`

Панель поддерживает:
- одиночную постановку
- групповую постановку
- теги
- срок с календарём
- временные вложения при создании

Контракты backend:
- `CreateTask`
- `CreateGroupTask`
- `AddAttachment`
- `AddContainer`

### 4. Иерархия и scope-фильтры
Основной entry point:
- `kbInitHierarchy()`

Он получает JSON от `GetHierarchyInfo` и инициализирует:
- `_kbH.role`
- `_kbH.myKey`
- `_kbH.myContext`
- `_kbH.viewMode`
- `_kbH.divisions`
- `_kbH.sectors`
- `_kbH.users`

Публичные функции фильтрации:
- `kbOnDeptChange()`
- `kbOnSectorChange()`
- `kbOnUserChange()`
- `kbSetMyMode()`
- `kbSetAllMode()`

Смена режима всегда идёт через:
- `SetViewMode`
- затем `RefreshBoard`

### 5. Role-aware UI при создании задачи
Публичные функции:
- `kbOnSelfChange()`
- `kbOnCrtDeptChange()`
- `kbOnCrtSectorChange()`
- `kbOnModeChange()`
- `kbOnGrpDeptChange()`
- `kbOnGrpSectorChange()`
- `kbOnGrpSearch()`
- `kbGrpToggle(key)`
- `kbGrpSelectPreset(type)`
- `kbGrpSelectAll()`
- `kbGrpClearAll()`

Поведение по ролям:
- `regular` — панель назначения скрыта, задача только себе
- `headOfSector` и `leadEngineer` — выбор сотрудника в пределах сектора
- `headOfDept` — выбор сектора и сотрудника в пределах отделения
- `admin` — выбор отделения, сектора и сотрудника

Для `leadEngineer` в UI используется отдельный бейдж `[вед.инж]`, чтобы не путать роль с `[нач.сект]`.

### 6. Модальное окно карточки задачи (`tcm*`)
Публичные функции:
- `tcmOpen(nameKey)`
- `tcmClose()`
- `tcmSave()`
- `tcmDelete()`
- `tcmSendComment()`
- `tcmDeleteComment(index)`
- `tcmToggleRevs()`
- `tcmOnStatusChange()`

При открытии карточки клиент вызывает:
- `GetTaskDetails`
- `GetComments`
- `GetAttachments`
- `GetTaskHistory`

При сохранении:
- `SaveTask`
- затем `RefreshBoard`

### 7. Вложения и поиск PLM-объектов
Публичные функции:
- `kbCrtPickAtt()`
- `kbCrtPickCont()`
- `kbCrtRemoveAtt(idx)`
- `tcmAttOpen(objKey, itemType)`
- `tcmAttRemove(objKey, itemType)`
- `tcmAttPickNative()`
- `tcmAttPickNativeCont()`
- `tcmAttToggleSearch()`
- `tcmAttSearchKeyup()`
- `tcmAttAdd(objKey)`

Используемые backend-методы:
- `PickObjects`
- `PickContainers`
- `GetAttachments`
- `OpenObject`
- `OpenContainer`
- `RemoveAttachment`
- `RemoveContainer`
- `PickAndAttach`
- `PickAndAttachContainer`
- `SearchObjects`
- `AddAttachment`

### 8. Отчёт
Публичные функции:
- `openReport()`
- `closeReport()`
- `loadReport()`

Клиент отправляет:

```javascript
window.external.InvokeTemplate("GetReport", period + "|" + scope);
```

`scope` зависит от текущей роли и выбранного режима (`all`, `dept`, `sector`, `group:CTX`, `user:KEY`).

### 9. Справка
Публичные функции:
- `openHelp()`
- `closeHelp()`

## Важные внутренние структуры

| Переменная | Назначение |
|------------|------------|
| `_kbH` | состояние иерархии и текущей роли |
| `_kbGrpSelected` | выбранные исполнители в групповом режиме |
| `_tcmData` | данные открытой карточки |
| `_crtPendingAttachments` | временные вложения до сохранения новой задачи |
| `_draggedId` | id перетаскиваемой карточки |
| `_taskWasOpened` | флаг refresh после возврата фокуса |

## Ошибки и UX-ограничения

- Все ошибки backend приходят строками `ERROR:...` и показываются через `alert(...)` или `tcmShowMsg(...)`
- Формат даты жёстко ожидается как `ДД.ММ.ГГГГ`
- Код должен оставаться IE11-совместимым: только `var`, обычные функции, конкатенация строк
- `window.*`-функции должны быть определены до их вызова из HTML

## Что изменилось относительно старой версии логики

- Вместо старой `ajaxAction`-архитектуры используется прямой `InvokeTemplate(...)`
- Иерархия работает через `GetHierarchyInfo` / `SetViewMode`
- UI знает новую роль `leadEngineer`
- Групповое создание и серверные ограничения назначения работают согласованно: фронтенд предлагает только допустимых исполнителей, backend дополнительно проверяет scope
