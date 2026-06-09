# Фича 02 – «Выданное мной» с фильтром по сектору

**Тип:** сервер (`SOYUZ_UPLOAD_KanbanScreen_script.cs`) + клиент (`kanban.js`, чуть-чуть HTML). Схема данных не меняется.
**Риск:** низкий. Точечный фикс существующего поведения scope.

---

## 1. Проблема (как есть сейчас)

Режим «Выданное мной» (`myCreated`) показывает **все** задачи, выданные текущим пользователем, по **всем** секторам сразу. Руководитель отделения, выдавший задачи в разные секторы, не может сузить выдачу до одного сектора.

**Почему так (подтверждено по коду):**
- `GetAllowedUserIdSet(...)` для `myCreated` возвращает `null` (строка **1600**), то есть «разрешены все исполнители без секторного ограничения».
- В `BeforeRender` фильтрация `myCreated` идёт по совпадению `Creator == currentUser` и исключению self-assign (строки **85–91**), но **контекст (сектор) исполнителя не проверяется**.

## 2. Решение

Сделать режимы `myCreated` и «сектор» совместимыми. Клиент отправляет:
- `myCreated` – как раньше (все секторы), либо
- `myCreated:group:<КЛЮЧ_СЕКТОРА>` – выданные мной, но только тем, кто входит в указанный сектор/отделение.

Серверный фильтр в `BeforeRender` дополнительно проверяет контекст исполнителя через уже существующий `IsWithinContext(...)`.

Выпадающий список секторов в тулбаре (`kb-sel-sector`) **уже есть** – переиспользуем его.

---

## 3. Серверная часть – `SOYUZ_UPLOAD_KanbanScreen_script.cs`

### 3.1. Разбор режима в `BeforeRender`

**Якорь:** строки **40–49** (чтение `viewMode`, вычисление `myCreatorKey`).

Было (≈40–49):
```csharp
var viewMode    = (obj.PropertyBag["KbViewMode"] as string) ?? "my";
var allowedIds  = GetAllowedUserIdSet( currentUser, role, viewMode );

string myCreatorKey = "";
if( viewMode == "myCreated" && currentUser != null )
{
    myCreatorKey = !string.IsNullOrEmpty( currentUser.NameKey ) ? currentUser.NameKey
                 : !string.IsNullOrEmpty( currentUser.AccountId ) ? currentUser.AccountId
                 : currentUser.Id.ToString();
}
```

Стало:
```csharp
var viewMode    = (obj.PropertyBag["KbViewMode"] as string) ?? "my";
var allowedIds  = GetAllowedUserIdSet( currentUser, role, viewMode );

// Фича 02: режим может быть "myCreated" или "myCreated:group:<ctx>"
bool   isMyCreated   = viewMode == "myCreated" || viewMode.StartsWith( "myCreated:" );
string myCreatedCtx  = "";   // целевой сектор/отделение, пусто = все
if( isMyCreated )
{
    int gi = viewMode.IndexOf( ":group:" );
    if( gi >= 0 ) myCreatedCtx = viewMode.Substring( gi + ":group:".Length ).Trim();
}

string myCreatorKey = "";
if( isMyCreated && currentUser != null )
{
    myCreatorKey = !string.IsNullOrEmpty( currentUser.NameKey ) ? currentUser.NameKey
                 : !string.IsNullOrEmpty( currentUser.AccountId ) ? currentUser.AccountId
                 : currentUser.Id.ToString();
}
```

### 3.2. Фильтр в цикле по задачам

**Якорь:** строки **85–91** (блок `if( viewMode == "myCreated" )`).

Было:
```csharp
if( viewMode == "myCreated" )
{
    var taskCreator = "";
    try { taskCreator = task.GetString( "Creator" ) ?? ""; } catch { }
    if( taskCreator != myCreatorKey ) continue;
    if( assignee.Id.ToString() == currentUser.Id.ToString() ) continue;
}
```

Стало:
```csharp
if( isMyCreated )
{
    var taskCreator = "";
    try { taskCreator = task.GetString( "Creator" ) ?? ""; } catch { }
    if( taskCreator != myCreatorKey ) continue;
    if( assignee.Id.ToString() == currentUser.Id.ToString() ) continue;

    // Фича 02: дополнительный фильтр по сектору/отделению исполнителя
    if( !string.IsNullOrEmpty( myCreatedCtx ) )
    {
        var asgCtx = GetUserContext( assignee );
        if( string.IsNullOrEmpty( asgCtx ) ) continue;
        if( asgCtx != myCreatedCtx && !IsWithinContext( asgCtx, myCreatedCtx ) ) continue;
    }
}
```

> `GetUserContext` (строка ~1235) и `IsWithinContext` (строка ~1293) уже существуют. `IsWithinContext(asgCtx, myCreatedCtx)` обрабатывает вложенность «сектор входит в отделение» и спец-случаи `500кт`.

### 3.3. `GetAllowedUserIdSet` – пропускать составной myCreated

**Якорь:** строка **1600**.

Было:
```csharp
if( viewMode == "myCreated" ) return null;
```
Стало:
```csharp
if( viewMode == "myCreated" || viewMode.StartsWith( "myCreated:" ) ) return null;
```
(Сектор фильтруется в `BeforeRender`, поэтому здесь по-прежнему «разрешены все», а сужение делает блок 3.2.)

### 3.4. `DoSetViewMode` – проверка

**Якорь:** строки **1308–1313**. Менять не нужно: метод сохраняет любую строку режима в `PropertyBag["KbViewMode"]`. Составной `myCreated:group:610кт` сохранится корректно.

---

## 4. Клиентская часть – `kanban.js`

Цель: когда активен режим «Выданное мной», селектор сектора не сбрасывает режим, а **уточняет** его.

### 4.1. Флаг активности myCreated

В объекте состояния иерархии `_kbH` появляется флаг `myCreated`. В `kbSetMyCreatedMode` (**@1819**) выставляем флаг и **не** трогаем сектор, если он выбран.

Было (≈1819–1830):
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

Стало:
```javascript
window.kbSetMyCreatedMode = function () {
    kbSetSelVal("kb-sel-user", "");
    kbClearSelSearch();
    _kbH.myCreated = true;                 // фича 02: включаем режим «выданные мной»
    if (_kbH.role === "admin" || _kbH.role === "headOfDept") {
        kbFillSectors(kbSelVal("kb-sel-dept") || null);   // оставляем доступным выбор сектора
        kbFillUsers(null, null, "");
    }
    kbUpdateMyCreatedBtn(true);
    kbSendMyCreatedMode();                  // фича 02: учитываем выбранный сектор
};

// Фича 02: собрать режим myCreated с учётом выбранного сектора/отделения
function kbSendMyCreatedMode() {
    var sectorKey = kbSelVal("kb-sel-sector");
    var deptKey   = kbSelVal("kb-sel-dept");
    var ctx = sectorKey || deptKey || "";
    kbSendMode(ctx ? ("myCreated:group:" + ctx) : "myCreated");
}
```

### 4.2. Селекторы сектора/отделения уважают myCreated

`kbApplyMode` (**@1675**) и обработчики `kbOnSectorChange` (**@1628**) / `kbOnDeptChange` (**@1618**) сейчас всегда строят `group:`/`dept`/`all`. Добавляем ранний выход на myCreated.

В начало `kbApplyMode` (сразу после `function kbApplyMode() {`) добавить:
```javascript
        if (_kbH.myCreated) { kbSendMyCreatedMode(); return; }   // фича 02
```

> Так как `kbOnSectorChange` и `kbOnDeptChange` в конце вызывают `kbApplyMode()`, отдельно их править не нужно – они автоматически уйдут в ветку myCreated, пока флаг активен.

### 4.3. Сброс флага при выходе из myCreated

В функциях, переключающих на другие режимы, сбросить флаг. Минимально – в `kbSetMyMode` (**@1662**) и в обработчике кнопки «Все»: добавить строку
```javascript
    _kbH.myCreated = false;   // фича 02
```
в начало каждой из них (и там, где `kbUpdateMyBtn(true)` / `kbUpdateAllBtn(true)`).

### 4.4. Восстановление режима после refresh

`kbRestoreViewMode` (**@1697**), ветка `myCreated` (**@1700**).

Было:
```javascript
if (mode === "myCreated") { kbUpdateMyCreatedBtn(true); return; }
```
Стало:
```javascript
if (mode === "myCreated" || mode.indexOf("myCreated:") === 0) {
    _kbH.myCreated = true;                       // фича 02
    kbUpdateMyCreatedBtn(true);
    var gi = mode.indexOf(":group:");
    if (gi >= 0) {
        var ctx = mode.substring(gi + 7);
        // выставить селектор сектора, если такой пункт есть
        if (typeof kbSetSelVal === "function") kbSetSelVal("kb-sel-sector", ctx);
    }
    return;
}
```

---

## 5. HTML – `KanbanBoard_HTML.html`

Правок по сути не требуется: селектор `kb-sel-sector` (**@35**) уже существует и уже вызывает `kbOnSectorChange()`. Убедитесь только, что в режиме «Выданное мной» он не скрывается принудительно (если в текущей логике где-то `display:none` для секторов в myCreated – снять это ограничение).

---

## 6. Edge cases

- **Обычный пользователь (`regular`)** не имеет секторного селектора – для него `myCreated` остаётся как был (все его выданные задачи; их и так немного).
- **Пустой контекст исполнителя** – задача исключается из секторного фильтра (строка `if( string.IsNullOrEmpty( asgCtx ) ) continue;`). Если нужно наоборот показывать «без сектора» – убрать этот `continue`.
- **Отделение vs сектор**: если выбрано отделение (`600кт`), `IsWithinContext(asgCtx, "600кт")` вернёт true для `610кт`, `620кт` и т.д. – то есть «выданные мной по всему отделению». Это ожидаемо и полезно.
- **Приватные задачи** по-прежнему фильтруются `CanUserSeeTask` раньше (строка 82) – фича их видимость не меняет.

---

## 7. Смок-тест

Подготовка: пользователь-руководитель, выдавший задачи минимум двум исполнителям из **разных** секторов (напр. `610кт` и `620кт`).

- [ ] «Выданное мной» без выбранного сектора – видны задачи всех секторов (как раньше).
- [ ] Выбрать в селекторе сектор `610кт` – остаются только задачи исполнителей из `610кт`.
- [ ] Сменить на `620кт` – остаются только задачи `620кт`.
- [ ] Выбрать отделение `600кт` – видны задачи всех секторов внутри `600кт`.
- [ ] Сбросить сектор (пустой выбор) – снова все выданные.
- [ ] Self-assigned задачи (выданные себе) не попадают в «Выданное мной» (как раньше).
- [ ] F5 / refresh: режим и выбранный сектор восстановились.
- [ ] Переключение на «Мои» / «Все» сбрасывает флаг myCreated (сектор больше не сужает «Выданное мной» после возврата без выбора).
- [ ] Обычный пользователь: «Выданное мной» работает по-старому, без ошибок.

---

## 8. Откат

Вернуть строки 1600, 40–49, 85–91 в исходный вид; убрать `kbSendMyCreatedMode`, флаг `_kbH.myCreated` и правки `kbApplyMode`/`kbRestoreViewMode`/`kbSetMyCreatedMode`. Перекомпилировать. Режим вернётся к «все секторы».
