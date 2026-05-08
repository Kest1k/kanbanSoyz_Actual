# 04 — Режим «Задачи [сектора/отделения] выданные мной»

> **Затронутые файлы:**
> - `scripts/SOYUZ_UPLOAD_KanbanScreen_script.cs`
> - `scripts/kanban.js`
> - `scripts/KanbanBoard_HTML.html`
> - `scripts/kanban.css`

## Что делаем

Руководители (headOfSector / leadEngineer / headOfDept / admin) выдают задачи подчинённым. Сейчас они могут смотреть «Мои задачи» (как исполнитель) и «Все задачи / Всё отделение / Весь сектор» (полный скоуп подчинённых). Не хватает третьего режима: **«Только задачи, которые я выдал»** — нужно для контроля исполнения собственных поручений.

Цель: добавить кнопку «Выданные мной» в верхнюю панель иерархии. По клику доска отображает только задачи, у которых атрибут `Creator` = текущему пользователю.

## Часть A — Серверная сторона

### A.1. Новый `viewMode` — `myCreated`

Добавим в `BeforeRender` поддержку режима `"myCreated"`. В этом режиме `GetAllowedUserIdSet` возвращает **null** (не фильтруем по исполнителю), а сам цикл по задачам дополнительно отбрасывает задачи, у которых `Creator != myKey`.

**Файл**: `scripts/SOYUZ_UPLOAD_KanbanScreen_script.cs`.

#### A.1.1. В `BeforeRender` (около строки 67)

В цикле `foreach( var task in container.RootInfoObjects )` добавить фильтр **СРАЗУ ПОСЛЕ** проверки `allowedIds`:

```csharp
foreach( var task in container.RootInfoObjects )
{
    var assignee = task.GetUser( "Assignee" );
    if( assignee == null ) continue;

    // allowedIds == null → режим «all» для admin, показываем всё
    if( allowedIds != null && !allowedIds.Contains( assignee.Id.ToString() ) )
        continue;

    // ─── Фильтр «Выданные мной» (режим myCreated) ─────────────────
    if( viewMode == "myCreated" )
    {
        var myKey = !string.IsNullOrEmpty( currentUser.NameKey ) ? currentUser.NameKey
                  : !string.IsNullOrEmpty( currentUser.AccountId ) ? currentUser.AccountId
                  : currentUser.Id.ToString();
        var taskCreator = "";
        try { taskCreator = task.GetString( "Creator" ) ?? ""; } catch { }
        if( taskCreator != myKey ) continue;
    }

    int status = GetStatusIndex( task );
    if( status < 0 || status > 3 ) status = 0;

    // Фильтр по периоду
    if( !IsTaskInPeriod( task, status, periodFrom, periodTo ) ) continue;

    raw[status].Add( task );
}
```

> Кэшировать `myKey` ВНЕ цикла (чтобы не вычислять на каждой задаче). Перенести расчёт перед циклом — поставить рядом с `var allowedIds`:
>
> ```csharp
> string myCreatorKey = "";
> if( viewMode == "myCreated" && currentUser != null )
> {
>     myCreatorKey = !string.IsNullOrEmpty( currentUser.NameKey ) ? currentUser.NameKey
>                  : !string.IsNullOrEmpty( currentUser.AccountId ) ? currentUser.AccountId
>                  : currentUser.Id.ToString();
> }
> ```
>
> И в цикле:
>
> ```csharp
> if( viewMode == "myCreated" )
> {
>     var taskCreator = "";
>     try { taskCreator = task.GetString( "Creator" ) ?? ""; } catch { }
>     if( taskCreator != myCreatorKey ) continue;
> }
> ```

#### A.1.2. В `GetAllowedUserIdSet` (строка ~1436)

Для режима `myCreated` нам **не нужен фильтр по исполнителю** (мы фильтруем по создателю). Но также нужно ограничить скоуп подчинённых — иначе задачи, выданные мной кому-то «вверх по иерархии» или вне моего скоупа, не должны утекать.

Добавить ветку **в начало** функции, после проверки `regular`/`my`:

```csharp
private System.Collections.Generic.HashSet<string> GetAllowedUserIdSet(
    User currentUser, string role, string viewMode )
{
    // «my» и «regular» — только текущий пользователь
    if( role == "regular" || viewMode == "my" )
    {
        var myOnly = new System.Collections.Generic.HashSet<string>();
        myOnly.Add( currentUser.Id.ToString() );
        return myOnly;
    }

    // ─── Режим «Выданные мной» — все исполнители разрешены ─────────
    // (фильтр по Creator делается отдельно в BeforeRender)
    if( viewMode == "myCreated" ) return null;

    if( viewMode == "all" && role == "admin" ) return null;
    // ... остальное без изменений
}
```

> **Замечание по безопасности**: `viewMode = "myCreated"` доступен любой роли, включая `regular`. Но `regular` сидит в первой ветке (`role == "regular"` → возвращаем только себя), туда мы не зашли. Поэтому `"myCreated"` для regular фактически даст задачи, где они сами Assignee И Creator одновременно — нормально, никаких leak-ов.

> Альтернативная защита: вместо `null` сделать «возвращать `null` только для не-`regular`», но это лишний код. Для `regular` режим бесполезен и не вылезает в UI (см. раздел B).

### A.2. Никаких новых атрибутов БД

Используется существующий `task["Creator"]` (Text, NameKey автора). Уже пишется в `DoCreateTask` (line 379) и `DoCreateGroupTask` (line 475). Для legacy-задач без `Creator` — пропускаются (`taskCreator == ""` ≠ `myKey`).

## Часть B — Клиентская сторона

### B.1. HTML (`KanbanBoard_HTML.html`)

В блоке `id="kb-hier-panel"` (после кнопки «Все задачи», около строки 40) добавить новую кнопку:

```html
<button type="button" id="kb-btn-mycreated" onclick="kbSetMyCreatedMode()" class="kb-nav-btn" style="display:none;">
    Выданные мной
</button>
```

> Если фича 02 (`02_header_responsive`) уже сделана и кнопки переведены на классы `kb-nav-btn` — использовать их. Если ещё нет — кнопка с inline-стилем по образцу `kb-btn-all`:
>
> ```html
> <button type="button" id="kb-btn-mycreated" onclick="kbSetMyCreatedMode()" style="display:none; padding:5px 12px; font-size:12px; font-weight:normal;
>     height:30px; border:1px solid #d1d5db; border-radius:6px;
>     background:#fff; color:#374151; cursor:pointer;
>     margin-right:8px; vertical-align:middle;">
>     Выданные мной
> </button>
> ```

### B.2. JS — `kanban.js`

#### B.2.1. Показ кнопки только руководителям

В `kbInitHierarchy` (около строки 1036–1042, рядом с показом `kb-btn-all`) добавить:

```javascript
var mcBtn = document.getElementById("kb-btn-mycreated");
if (mcBtn) {
    // Показываем тем, кто вообще выдаёт задачи: admin / headOfDept / headOfSector / leadEngineer
    mcBtn.style.display = (_kbH.role !== "regular") ? "" : "none";
}
```

#### B.2.2. Новая функция `kbSetMyCreatedMode`

Добавить рядом с `kbSetAllMode` (после строки 1259):

```javascript
window.kbSetMyCreatedMode = function () {
    kbSetSelVal("kb-sel-dept", "");
    kbSetSelVal("kb-sel-sector", "");
    kbSetSelVal("kb-sel-user", "");
    kbClearSelSearch();
    if (_kbH.role === "admin" || _kbH.role === "headOfDept") {
        kbFillSectors(null);
        kbFillUsers(null, null, "");
    }
    kbUpdateMyCreatedBtn(true);
    kbSendMode("myCreated");
};
```

#### B.2.3. Стилизация трёх кнопок (Мои / Все / Выданные)

Сейчас активная кнопка подсвечена синим, неактивная — серая. Добавим хелпер `kbUpdateMyCreatedBtn` и переключение всех трёх кнопок взаимоисключающе.

Найти существующие функции `kbUpdateMyBtn` и `kbUpdateAllBtn` (поищите в `kanban.js`). Добавить рядом:

```javascript
function kbUpdateMyCreatedBtn(active) {
    kbStyleBtn("kb-btn-my", false);
    kbStyleBtn("kb-btn-all", false);
    kbStyleBtn("kb-btn-mycreated", active);
}
```

И **заменить** существующие `kbUpdateMyBtn(true)` / `kbUpdateAllBtn(true)` так, чтобы они тоже сбрасывали `kb-btn-mycreated`:

```javascript
function kbUpdateMyBtn(active) {
    kbStyleBtn("kb-btn-my", active);
    kbStyleBtn("kb-btn-all", false);
    kbStyleBtn("kb-btn-mycreated", false);
}

function kbUpdateAllBtn(active) {
    kbStyleBtn("kb-btn-my", false);
    kbStyleBtn("kb-btn-all", active);
    kbStyleBtn("kb-btn-mycreated", false);
}
```

> Если `kbStyleBtn` принимает второй аргумент-флаг и переключает `kb-nav-btn-primary` (или эквивалент стиля «активная кнопка») — это уже работает. Если нет — найти его и убедиться, что классы совместимы. Пример минимальной реализации:
>
> ```javascript
> function kbStyleBtn(id, active) {
>     var b = document.getElementById(id);
>     if (!b) return;
>     // Удаляем оба возможных стиля; класс kb-nav-btn-primary — для синей подсветки
>     b.className = b.className.replace(/\s*kb-nav-btn-primary/g, "");
>     if (active) b.className += " kb-nav-btn-primary";
> }
> ```

#### B.2.4. Восстановление режима после рендера

В `kbRestoreViewMode` (около строки 1194) добавить ветку для `myCreated` сразу после `mode === "all" || ...`:

```javascript
function kbRestoreViewMode(mode) {
    if (!mode || mode === "my") { kbUpdateMyBtn(true); return; }
    if (mode === "all" || mode === "dept" || mode === "sector") { kbUpdateAllBtn(true); return; }
    if (mode === "myCreated") { kbUpdateMyCreatedBtn(true); return; }
    // ... остальное без изменений
}
```

### B.3. CSS (`kanban.css`)

Если фича 02 ещё не сделана — добавить стили активного состояния. Если сделана — пропустить (стили уже есть как `kb-nav-btn-primary`).

```css
/* ── Кнопка «Выданные мной» — стилистически совместима с My/All ── */
#kb-btn-mycreated {
    /* без изменений по умолчанию: серая рамка, белый фон */
}
```

(Никаких специальных стилей не нужно — кнопка использует общие стили.)

## Часть C — Атомарные коммиты

| # | Шаг | Файлы | Сообщение коммита |
|---|-----|-------|-------------------|
| 1 | C#: фильтр Creator в BeforeRender + ветка в `GetAllowedUserIdSet` | `SOYUZ_UPLOAD_KanbanScreen_script.cs` | `feat(kanban): server filter for tasks I created` |
| 2 | HTML: кнопка `kb-btn-mycreated` | `KanbanBoard_HTML.html` | `feat(kanban): button "issued by me" in toolbar` |
| 3 | JS: `kbSetMyCreatedMode`, восстановление режима, переключение кнопок | `kanban.js` | `feat(kanban): client logic for myCreated mode` |
| 4 | Документация в «Что нового» / «Справка» | `KanbanBoard_HTML.html` | `docs(kanban): whatsnew entry for myCreated mode` |

## Часть D — Проверки и риски

1. **Legacy-задачи без `Creator`** — таких задач может быть много на старых стендах. Они **не попадут в `myCreated`** (Creator пустой). Это норма: это значит «никто не помнит, кто их выдал». Если хочется бэкфилла — отдельная миграция на стенде, не часть этой фичи.
2. **Группа-задачи** (`DoCreateGroupTask`): записывают `Creator` для каждой созданной задачи (line 475). Все они попадут в `myCreated` у автора группы. Корректно.
3. **Совместимость с фильтром периода**: `IsTaskInPeriod` проверяется ПОСЛЕ Creator-фильтра. Никаких изменений в логике периода не нужно.
4. **Combo-режимы**: пользователь не должен иметь возможность одновременно «Выданные мной» + «выбрать сектор/сотрудника». При клике на кнопку — селекты сбрасываются (`kbSetSelVal("kb-sel-...", "")`). Если пользователь после этого ткнёт в селект — текущий код в `kbApplyMode` переключит режим обратно в `user:` или `group:`. Это норма: «Выданные мной» — взаимоисключающий режим.
5. **Заголовок страницы / счётчики**: Колоночные счётчики (`{{ c0 }}`) формируются из `col_0 | size` после фильтрации — будут корректны.
6. **PropertyBag persistence**: `viewMode = "myCreated"` сохранится в `KbViewMode` (через `DoSetViewMode`). При следующем открытии доски пользователь окажется в этом же режиме — ОК.
7. **Безопасность**: пользователь может пытаться вручную через JS-консоль вызвать `InvokeTemplate("SetViewMode", "myCreated")`. Это безопасно: фильтр по `Creator` ограничивает скоуп до собственных созданных задач + базовый фильтр allowedIds (для не-admin) ограничивает по подчинённым. Никакой утечки.

## Часть E — Критерии приёмки

- [ ] У руководителя в шапке появилась кнопка «Выданные мной» рядом с «Все задачи» / «Мои задачи».
- [ ] Клик по кнопке: доска показывает только задачи, у которых `Creator` == NameKey текущего пользователя.
- [ ] Среди этих задач — могут быть и **мои собственные** (если я создал задачу для себя), и **подчинённых** (если я их выдал).
- [ ] Клик по «Мои задачи» — возвращает только задачи, где Я Assignee.
- [ ] Клик по «Все задачи» / «Всё отделение» / «Весь сектор» — возвращает базовый скоуп без Creator-фильтра.
- [ ] Селекты иерархии **сбрасываются** при клике «Выданные мной».
- [ ] При перезагрузке доски (F5) режим сохраняется.
- [ ] У обычного сотрудника (regular) кнопка скрыта.
- [ ] Фильтр по периоду совместим: «Выданные мной» + «За последний месяц» = задачи, выданные мной за последний месяц.
