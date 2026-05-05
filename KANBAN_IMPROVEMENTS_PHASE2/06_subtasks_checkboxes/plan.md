# 06 — Подзадачи / чекбоксы внутри задачи

> **Источник в backlog:** `KANBAN_IMPROVEMENTS_BACKLOG.md` §2.7.
> **Фича:** `subtasks_checkboxes`.
> **Приоритет:** P0 (Phase 2, задача №2).
> **Затронутые файлы:** `scripts/SOYUZ_UPLOAD_KanbanScreen_script.cs`, `scripts/kanban.js`, `scripts/KanbanBoard_HTML.html`, `scripts/kanban.css`.

---

## 1. Техническое описание задачи

### 1.1. Требования backlog

> - В карточке задачи добавить возможность создавать список подзадач с чекбоксами.
> - Подзадачи должны сохраняться и отображаться в карточке.

### 1.2. Архитектурное решение

**НЕ создаём** новые `InfoObject` / `CollectionOfElements` — это перегрузит БД (на каждую задачу N подзадач = N+1 объектов). Вместо этого используем **легковесный JSON в текстовом атрибуте** `SubtasksJSON` — тот же паттерн, что для комментариев (`CommentsJSON`, см. `SOYUZ_UPLOAD_KanbanScreen_script.cs:2520+`, метод `ParseComments`).

Структура:

```json
[
  { "id": "s1", "text": "Согласовать ТЗ", "done": "1", "createdBy": "Иванов И.И.", "createdAt": "2026-05-04T10:30:00", "doneAt": "2026-05-04T11:15:00", "doneBy": "Иванов И.И." },
  { "id": "s2", "text": "Подготовить демо", "done": "0", "createdBy": "Петров П.П.", "createdAt": "2026-05-04T10:31:00" }
]
```

Поля:
- `id` — строка, генерируется на сервере при добавлении (`s` + sequential). Стабильна между правками.
- `text` — содержимое подзадачи (1–500 символов).
- `done` — `"0"` / `"1"` (строка для совместимости с парсером комментариев).
- `createdBy`, `createdAt` — кто и когда добавил.
- `doneBy`, `doneAt` — заполняются при `ToggleSubtask` в положение «выполнено».

UI:
- В модалке `tcmOverlay` добавляем **новую вкладку** «Чек-лист» (между «Основное» и «Обсуждение»). Альтернатива (если решено не плодить вкладки) — отдельный блок внутри «Основное» под полем «Описание». Выбор: **новая вкладка** — потому что чек-лист потенциально длинный и заслуживает отдельного скролла.
- На карточке `.kb-card` — бейдж `☑ 3/5` (выполнено/всего), скрывается если подзадач 0.

### 1.3. Серверные методы

| Метод | inputParams | Действие | Возврат |
|-------|-------------|----------|---------|
| `AddSubtask` | `taskKey\|text` | Добавить подзадачу с автогенерируемым `id` | JSON созданной подзадачи или `ERROR:*` |
| `ToggleSubtask` | `taskKey\|subtaskId` | Перевернуть `done` 0↔1, обновить `doneBy/doneAt` | JSON обновлённой подзадачи или `ERROR:*` |
| `DeleteSubtask` | `taskKey\|subtaskId` | Удалить элемент из массива | `OK` или `ERROR:*` |
| `GetSubtasks` | `taskKey` | Вернуть весь JSON-массив (для рендера в модалке) | JSON-массив |

> `GetSubtasks` нужен, потому что при открытии модалки `tcmOpen(nameKey)` атрибут `SubtasksJSON` уже доступен через серверный кэш — но мы подгружаем его отдельным вызовом, чтобы не раздувать `GetTaskDetails`.

### 1.4. Что НЕ делаем

- Не делаем drag&drop переупорядочивания подзадач (Phase 3).
- Не делаем deadlines/assignees внутри подзадач (Phase 3+).
- Не дублируем подзадачи в индекс/поиск.

---

## 2. Затронутые файлы

| Путь | Что меняем |
|------|------------|
| `scripts/SOYUZ_UPLOAD_KanbanScreen_script.cs` | Хелперы `ParseSubtasks` / `SerializeSubtasks` / `GetSubtaskCount` / `GetSubtaskDoneCount`. Invoke-методы `AddSubtask` / `ToggleSubtask` / `DeleteSubtask` / `GetSubtasks`. Передача счётчиков подзадач в Liquid для бейджа карточки. |
| `scripts/kanban.js` | Блок `tcmSubtasks*`: `tcmSubtasksLoad`, `tcmSubtasksRender`, `tcmSubtasksAdd`, `tcmSubtasksToggle`, `tcmSubtasksDelete`. Подвязка к `tcmSwitchTab('subt')` и `tcmOpen`. |
| `scripts/KanbanBoard_HTML.html` | Новая вкладка `tcm-tab-subt` в модалке. Бейдж `.kb-subt-badge` на `.kb-card`. |
| `scripts/kanban.css` | Стили чек-листа (чекбокс, текст, кнопка удалить, бейдж карточки). |

> **Конфигурация (pmszcfg):** добавить атрибут `SubtasksJSON` (тип Text, MaxLength 1 МБ) в InfoType `KanbanTask`, если его нет.

---

## 3. C# — что и где менять (`SOYUZ_UPLOAD_KanbanScreen_script.cs`)

### 3.1. Ветки в `Invoke`

Рядом с Comments-методами (~строки 154–158):

```csharp
case "AddSubtask":    return DoAddSubtask( inputParams );
case "ToggleSubtask": return DoToggleSubtask( inputParams );
case "DeleteSubtask": return DoDeleteSubtask( inputParams );
case "GetSubtasks":   return DoGetSubtasks( inputParams );
```

### 3.2. Хелперы парсинга / сериализации

Размещаем рядом с `ParseComments` (~строка 2524). Можно использовать тот же универсальный парсер (если он generic), либо клонировать с переименованием — для атомарности и читаемости.

```csharp
// ─── Парсинг JSON-массива подзадач ────────────────────────────────
private System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>>
    ParseSubtasks( string json )
{
    // Идентичен ParseComments по алгоритму (он universal по форме [{kv,kv}, ...])
    return ParseComments( json );  // переиспользуем, формат тот же
}

private string SerializeSubtasks(
    System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>> items )
{
    return SerializeComments( items );  // тот же сериализатор
}

private int GetSubtaskCount( InfoObject task )
{
    try
    {
        var json = task.GetString( "SubtasksJSON" ) ?? "";
        if( json.Length < 3 ) return 0;
        return ParseSubtasks( json ).Count;
    }
    catch { return 0; }
}

private int GetSubtaskDoneCount( InfoObject task )
{
    try
    {
        var json = task.GetString( "SubtasksJSON" ) ?? "";
        if( json.Length < 3 ) return 0;
        var items = ParseSubtasks( json );
        int n = 0;
        for( int i = 0; i < items.Count; i++ )
        {
            string v;
            if( items[i].TryGetValue( "done", out v ) && v == "1" ) n++;
        }
        return n;
    }
    catch { return 0; }
}
```

> Если `ParseComments` строго привязан к именам полей комментариев — клонировать в отдельные `ParseSubtasks` / `SerializeSubtasks` без изменений алгоритма, только переименование.

### 3.3. `DoAddSubtask`

> **Транзакции БД (обязательно).** В Soyuz-PLM любое изменение JSON-атрибута через `editable.Save()` должно быть обёрнуто в групповую операцию `Service.EnterNewGroupOperation()` + `Service.SaveChanges()`. Без обёртки при частых кликах по чекбоксам в многопользовательской среде получим ошибки блокировки.

```csharp
// ─── AddSubtask ─────────────────────────────────────────────────────
// inputParams: "taskKey|text"
// Возвращает JSON созданного объекта или ERROR.
private object DoAddSubtask( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 2 );
    if( parts.Length < 2 ) return "ERROR:BadFormat";

    var taskKey = parts[0].Trim();
    var text    = parts[1].Trim();
    if( string.IsNullOrEmpty( taskKey ) ) return "ERROR:NoKey";
    if( string.IsNullOrEmpty( text ) )    return "ERROR:EmptyText";
    if( text.Length > 500 ) text = text.Substring( 0, 500 );

    try
    {
        var task = GetTaskByKeyOrNull( taskKey );
        if( task == null ) return "ERROR:NotFound";

        var editable = task.GetEditable();
        var json  = editable.GetString( "SubtasksJSON" ) ?? "";
        var items = ParseSubtasks( json );

        // Генерация id: s + (max(id)+1) или s + count+1
        int maxId = 0;
        for( int i = 0; i < items.Count; i++ )
        {
            string v;
            if( items[i].TryGetValue( "id", out v ) && v.StartsWith( "s" ) )
            {
                int n;
                if( int.TryParse( v.Substring( 1 ), out n ) && n > maxId ) maxId = n;
            }
        }
        var newId = "s" + (maxId + 1).ToString();

        var user = Service.GetCurrentUser();
        var now  = DateTime.Now.ToString( "yyyy-MM-ddTHH:mm:ss",
                       System.Globalization.CultureInfo.InvariantCulture );

        var item = new System.Collections.Generic.Dictionary<string, string>();
        item["id"]        = newId;
        item["text"]      = text;
        item["done"]      = "0";
        item["createdBy"] = user.ToString() ?? "";
        item["createdAt"] = now;
        items.Add( item );

        // ─── Транзакция БД (обязательно для конкурентных правок) ───
        using( Service.EnterNewGroupOperation() )
        {
            editable["SubtasksJSON"] = SerializeSubtasks( items );
            editable.Save();
            Service.SaveChanges();
        }

        // Возвращаем JSON одного объекта
        var sb = new System.Text.StringBuilder();
        sb.Append( "{" );
        bool first = true;
        foreach( var kv in item )
        {
            if( !first ) sb.Append( "," );
            sb.Append( "\"" + JsonEscape( kv.Key ) + "\":\"" + JsonEscape( kv.Value ) + "\"" );
            first = false;
        }
        sb.Append( "}" );
        return sb.ToString();
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "KanbanScreen.DoAddSubtask: " + ex.Message );
        return "ERROR:Internal";
    }
}
```

### 3.4. `DoToggleSubtask`

```csharp
// ─── ToggleSubtask ──────────────────────────────────────────────────
// inputParams: "taskKey|subtaskId"
// Переворачивает done 0↔1, обновляет doneBy/doneAt.
private object DoToggleSubtask( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 2 );
    if( parts.Length < 2 ) return "ERROR:BadFormat";

    var taskKey   = parts[0].Trim();
    var subtaskId = parts[1].Trim();

    try
    {
        var task = GetTaskByKeyOrNull( taskKey );
        if( task == null ) return "ERROR:NotFound";

        var editable = task.GetEditable();
        var items    = ParseSubtasks( editable.GetString( "SubtasksJSON" ) ?? "" );

        int idx = -1;
        for( int i = 0; i < items.Count; i++ )
        {
            string v;
            if( items[i].TryGetValue( "id", out v ) && v == subtaskId ) { idx = i; break; }
        }
        if( idx < 0 ) return "ERROR:NoSuchSubtask";

        string oldDone;
        items[idx].TryGetValue( "done", out oldDone );
        bool newDone = !(oldDone == "1");
        items[idx]["done"] = newDone ? "1" : "0";

        if( newDone )
        {
            var user = Service.GetCurrentUser();
            items[idx]["doneBy"] = user.ToString() ?? "";
            items[idx]["doneAt"] = DateTime.Now.ToString( "yyyy-MM-ddTHH:mm:ss",
                                       System.Globalization.CultureInfo.InvariantCulture );
        }
        else
        {
            items[idx].Remove( "doneBy" );
            items[idx].Remove( "doneAt" );
        }

        // ─── Транзакция БД (обязательно — частые клики по чекбоксам) ───
        using( Service.EnterNewGroupOperation() )
        {
            editable["SubtasksJSON"] = SerializeSubtasks( items );
            editable.Save();
            Service.SaveChanges();
        }

        // Возвращаем обновлённый объект
        var sb = new System.Text.StringBuilder();
        sb.Append( "{" );
        bool first = true;
        foreach( var kv in items[idx] )
        {
            if( !first ) sb.Append( "," );
            sb.Append( "\"" + JsonEscape( kv.Key ) + "\":\"" + JsonEscape( kv.Value ) + "\"" );
            first = false;
        }
        sb.Append( "}" );
        return sb.ToString();
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "KanbanScreen.DoToggleSubtask: " + ex.Message );
        return "ERROR:Internal";
    }
}
```

### 3.5. `DoDeleteSubtask`

```csharp
// ─── DeleteSubtask ──────────────────────────────────────────────────
private object DoDeleteSubtask( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 2 );
    if( parts.Length < 2 ) return "ERROR:BadFormat";

    var taskKey   = parts[0].Trim();
    var subtaskId = parts[1].Trim();

    try
    {
        var task = GetTaskByKeyOrNull( taskKey );
        if( task == null ) return "ERROR:NotFound";

        var editable = task.GetEditable();
        var items    = ParseSubtasks( editable.GetString( "SubtasksJSON" ) ?? "" );

        int idx = -1;
        for( int i = 0; i < items.Count; i++ )
        {
            string v;
            if( items[i].TryGetValue( "id", out v ) && v == subtaskId ) { idx = i; break; }
        }
        if( idx < 0 ) return "ERROR:NoSuchSubtask";

        items.RemoveAt( idx );

        // ─── Транзакция БД (обязательно) ───
        using( Service.EnterNewGroupOperation() )
        {
            editable["SubtasksJSON"] = items.Count > 0 ? SerializeSubtasks( items ) : "";
            editable.Save();
            Service.SaveChanges();
        }
        return "OK";
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "KanbanScreen.DoDeleteSubtask: " + ex.Message );
        return "ERROR:Internal";
    }
}
```

### 3.6. `DoGetSubtasks`

```csharp
// ─── GetSubtasks ────────────────────────────────────────────────────
private object DoGetSubtasks( object inputParams )
{
    var taskKey = GetParamStr( inputParams ).Trim();
    if( string.IsNullOrEmpty( taskKey ) ) return "[]";
    try
    {
        var task = GetTaskByKeyOrNull( taskKey );
        if( task == null ) return "[]";
        var json = task.GetString( "SubtasksJSON" ) ?? "";
        return string.IsNullOrEmpty( json ) ? "[]" : json;
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "KanbanScreen.DoGetSubtasks: " + ex.Message );
        return "[]";
    }
}
```

### 3.7. Передача счётчиков в Liquid (для бейджа карточки)

В `BeforeRender`, при формировании объекта карточки для шаблона, рядом с `commentCount`:

```csharp
cardObj["subtaskTotal"] = GetSubtaskCount( task );
cardObj["subtaskDone"]  = GetSubtaskDoneCount( task );
```

В Liquid `KanbanBoard_HTML.html` — рендер бейджа на карточке (см. п.5).

---

## 4. JavaScript — что и где менять (`kanban.js`)

### 4.1. Состояние

```javascript
var _tcmSubtasks = { taskKey: "", items: [] };
```

### 4.2. Загрузка и рендер при открытии модалки

Подключить в `tcmOpen` (строка ~1466) после загрузки основной информации:

```javascript
// в конце tcmOpen, после загрузки таба:
if (typeof tcmSubtasksLoad === "function") tcmSubtasksLoad(nameKey);
```

```javascript
window.tcmSubtasksLoad = function (nameKey) {
    _tcmSubtasks.taskKey = nameKey;
    _tcmSubtasks.items   = [];
    var safeKey = String(nameKey).replace(/\|/g, "");
    try {
        var raw = window.external.InvokeTemplate("GetSubtasks", safeKey);
        _tcmSubtasks.items = JSON.parse(String(raw || "[]"));
    } catch (e) { _tcmSubtasks.items = []; }
    tcmSubtasksRender();
};

window.tcmSubtasksRender = function () {
    var list = document.getElementById("tcm-subt-list");
    if (!list) return;
    var html = "";
    var items = _tcmSubtasks.items || [];
    var i, it, doneClass, doneTitle;
    for (i = 0; i < items.length; i++) {
        it = items[i];
        doneClass = (it.done === "1") ? " kb-subt-done" : "";
        doneTitle = (it.done === "1" && it.doneBy)
            ? ("Выполнено: " + it.doneBy + " (" + (it.doneAt || "") + ")")
            : "";
        html += '<div class="kb-subt-item' + doneClass + '" data-id="' + tcmChatEsc(it.id) + '">'
              + '<input type="checkbox" class="kb-subt-cb"'
              + (it.done === "1" ? " checked" : "")
              + ' onclick="tcmSubtasksToggle(\'' + tcmChatEsc(it.id) + '\')">'
              + '<span class="kb-subt-text" title="' + tcmChatEsc(doneTitle) + '">'
              + tcmChatEsc(it.text || "") + '</span>'
              + '<button type="button" class="kb-subt-del" '
              + 'onclick="tcmSubtasksDelete(\'' + tcmChatEsc(it.id) + '\')" title="Удалить">×</button>'
              + '</div>';
    }
    list.innerHTML = html || '<div class="kb-subt-empty">Нет подзадач</div>';

    // Сводка прогресса
    var total = items.length;
    var done = 0;
    for (i = 0; i < items.length; i++) if (items[i].done === "1") done++;
    var pr = document.getElementById("tcm-subt-progress");
    if (pr) pr.innerHTML = total ? (done + " / " + total) : "0 / 0";
};
```

### 4.3. Добавление подзадачи

```javascript
window.tcmSubtasksAdd = function () {
    var inp = document.getElementById("tcm-subt-input");
    if (!inp) return;
    var text = (inp.value || "").replace(/\|/g, " ").substring(0, 500);
    if (!text) return;
    var safeKey = String(_tcmSubtasks.taskKey).replace(/\|/g, "");
    try {
        var raw = window.external.InvokeTemplate("AddSubtask", safeKey + "|" + text);
        if (String(raw || "").indexOf("ERROR") === 0) {
            alert("Ошибка добавления: " + raw);
            return;
        }
        var obj = JSON.parse(String(raw));
        _tcmSubtasks.items.push(obj);
        inp.value = "";
        tcmSubtasksRender();
    } catch (e) { /* no-op */ }
};
```

### 4.4. Toggle и Delete

```javascript
window.tcmSubtasksToggle = function (subtaskId) {
    var safeKey = String(_tcmSubtasks.taskKey).replace(/\|/g, "");
    var safeId  = String(subtaskId).replace(/\|/g, "");
    try {
        var raw = window.external.InvokeTemplate("ToggleSubtask", safeKey + "|" + safeId);
        if (String(raw || "").indexOf("ERROR") === 0) return;
        var obj = JSON.parse(String(raw));
        var i;
        for (i = 0; i < _tcmSubtasks.items.length; i++) {
            if (_tcmSubtasks.items[i].id === subtaskId) {
                _tcmSubtasks.items[i] = obj;
                break;
            }
        }
        tcmSubtasksRender();
    } catch (e) { /* no-op */ }
};

window.tcmSubtasksDelete = function (subtaskId) {
    if (!confirm("Удалить подзадачу?")) return;
    var safeKey = String(_tcmSubtasks.taskKey).replace(/\|/g, "");
    var safeId  = String(subtaskId).replace(/\|/g, "");
    try {
        var res = window.external.InvokeTemplate("DeleteSubtask", safeKey + "|" + safeId);
        if (String(res || "").indexOf("OK") !== 0) return;
        var i;
        for (i = 0; i < _tcmSubtasks.items.length; i++) {
            if (_tcmSubtasks.items[i].id === subtaskId) {
                _tcmSubtasks.items.splice(i, 1);
                break;
            }
        }
        tcmSubtasksRender();
    } catch (e) { /* no-op */ }
};
```

### 4.5. Подключение к Enter в инпуте

```javascript
// В onkeydown инпута tcm-subt-input:
function tcmSubtasksKeydown(e) {
    var code = e.keyCode || e.which;
    if (code === 13) { tcmSubtasksAdd(); e.preventDefault(); }
}
```

---

## 5. HTML — что и где менять (`KanbanBoard_HTML.html`)

### 5.1. Кнопка вкладки в `tcm-tabs` (строка ~524)

```html
<div class="tcm-tabs">
    <div id="tab-btn-main" class="tcm-tab active" onclick="tcmSwitchTab('main')">Основное</div>
    <div id="tab-btn-subt" class="tcm-tab" onclick="tcmSwitchTab('subt')">Чек-лист
        <span id="tcm-tab-subt-badge" class="tcm-tab-badge" style="display:none;">0/0</span>
    </div>
    <div id="tab-btn-chat" class="tcm-tab" onclick="tcmSwitchTab('chat')">Обсуждение
        <span id="tcm-tab-chat-badge" class="tcm-tab-badge" style="display:none;">0</span>
    </div>
    <div id="tab-btn-hist" class="tcm-tab" onclick="tcmSwitchTab('hist')">История</div>
</div>
```

### 5.2. Контент вкладки (после `tcm-tab-main`, до `tcm-tab-chat`)

```html
<div id="tcm-tab-subt" class="tcm-tab-content">
    <div class="kb-subt-header">
        <span class="kb-subt-title">Чек-лист</span>
        <span class="kb-subt-progress" id="tcm-subt-progress">0 / 0</span>
    </div>
    <div class="kb-subt-list" id="tcm-subt-list"></div>
    <div class="kb-subt-add">
        <input type="text" id="tcm-subt-input" class="form-control input-sm"
               placeholder="Добавить пункт чек-листа..."
               onkeydown="tcmSubtasksKeydown(event)" maxlength="500">
        <button type="button" class="tcm-btn tcm-btn-primary"
                onclick="tcmSubtasksAdd()">+</button>
    </div>
</div>
```

### 5.3. Бейдж на карточке (в Liquid-блоке `.kb-card`)

```liquid
{% if card.subtaskTotal != 0 %}
<span class="kb-subt-badge" title="Чек-лист: {{ card.subtaskDone }} из {{ card.subtaskTotal }}">
    ☑ {{ card.subtaskDone }}/{{ card.subtaskTotal }}
</span>
{% endif %}
```

> DotLiquid: `!= 0` корректно сравнивает число с числом. Для строк — `!= ""`.

### 5.4. Обновление бейджа вкладки в JS

В `tcmSubtasksRender` после подсчёта `done/total`:

```javascript
var badge = document.getElementById("tcm-tab-subt-badge");
if (badge) {
    if (total > 0) {
        badge.innerHTML = done + "/" + total;
        badge.style.display = "";
    } else {
        badge.style.display = "none";
    }
}
```

---

## 6. CSS — что и где менять (`kanban.css`)

```css
/* ─── Чек-лист в модалке ─── */
.kb-subt-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 8px;
}
.kb-subt-title { font-weight: 600; font-size: 13px; color: #1f2937; }
.kb-subt-progress {
    font-size: 12px;
    color: #4b5563;
    background: #f3f4f6;
    padding: 2px 8px;
    border-radius: 10px;
}
.kb-subt-list { margin-bottom: 8px; max-height: 320px; overflow-y: auto; }
.kb-subt-empty { color: #9ca3af; font-size: 12px; padding: 8px; text-align: center; }
.kb-subt-item {
    display: flex;
    align-items: center;
    padding: 6px 8px;
    border-bottom: 1px solid #f3f4f6;
    font-size: 13px;
}
.kb-subt-item:hover { background: #f9fafb; }
.kb-subt-cb { margin-right: 8px; cursor: pointer; }
.kb-subt-text { flex: 1; }
.kb-subt-done .kb-subt-text {
    text-decoration: line-through;
    color: #9ca3af;
}
.kb-subt-del {
    background: transparent;
    border: 0;
    color: #ef4444;
    font-size: 16px;
    cursor: pointer;
    padding: 0 6px;
    visibility: hidden;
}
.kb-subt-item:hover .kb-subt-del { visibility: visible; }
.kb-subt-add {
    display: flex;
    gap: 6px;
    margin-top: 8px;
}
.kb-subt-add input { flex: 1; }

/* ─── Бейдж на карточке доски ─── */
.kb-subt-badge {
    display: inline-block;
    padding: 2px 6px;
    margin-left: 4px;
    background: #e0f2fe;
    color: #075985;
    border-radius: 3px;
    font-size: 11px;
}
```

---

## 7. Пошаговый план реализации (атомарные коммиты)

| # | Шаг | Файлы | Smoke-тест | Сообщение коммита |
|---|-----|-------|------------|-------------------|
| 1 | Конфигурация: добавить атрибут `SubtasksJSON` в `KanbanTask` (если отсутствует) | `Kanban Конфигурация-1.0.0.5.pmszcfg` | Атрибут есть в InfoType | `chore(kanban): pmszcfg add SubtasksJSON attribute` |
| 2 | C#: хелперы `ParseSubtasks` / `SerializeSubtasks` / `GetSubtaskCount` / `GetSubtaskDoneCount` | `SOYUZ_UPLOAD_KanbanScreen_script.cs` | Сборка стенда проходит | `feat(kanban): subtasks JSON helpers` |
| 3 | C#: `DoAddSubtask` + ветка `case "AddSubtask":` | `SOYUZ_UPLOAD_KanbanScreen_script.cs` | `InvokeTemplate("AddSubtask","KEY|тест")` возвращает JSON | `feat(kanban): server AddSubtask method` |
| 4 | C#: `DoToggleSubtask` + `DoDeleteSubtask` + `DoGetSubtasks` + ветки в Invoke | `SOYUZ_UPLOAD_KanbanScreen_script.cs` | Toggle переворачивает done, Delete удаляет, Get возвращает массив | `feat(kanban): server Toggle/Delete/GetSubtasks` |
| 5 | HTML+CSS: новая вкладка `tcm-tab-subt` + контейнеры | `KanbanBoard_HTML.html`, `kanban.css` | Вкладка видна в модалке, переключение работает | `feat(kanban): subtasks tab UI in modal` |
| 6 | JS: `tcmSubtasksLoad/Render/Add/Toggle/Delete` + подвязка к `tcmOpen` | `kanban.js` | В вкладке: добавить, отметить, удалить — всё сохраняется | `feat(kanban): subtasks client logic` |
| 7 | C#+HTML+CSS: проброс `subtaskTotal/Done` в Liquid + бейдж на карточке | `SOYUZ_UPLOAD_KanbanScreen_script.cs`, `KanbanBoard_HTML.html`, `kanban.css` | На карточке отображается `☑ 3/5` | `feat(kanban): subtask progress badge on card` |
| 8 | JS: бейдж на вкладке `tcm-tab-subt-badge` | `kanban.js` | В шапке вкладки видно `2/5` | `feat(kanban): subtask tab badge` |
| 9 | Документация `docs/07_*.md` | docs | — | `docs(kanban): document subtasks feature` |

---

## 8. Возможные риски и технические ограничения

1. **Конкурентные правки `SubtasksJSON`.** Два пользователя одновременно ставят чекбокс — последний `task.Save()` выигрывает, может потеряться правка. Все методы `Add/Toggle/Delete` обёрнуты в `Service.EnterNewGroupOperation()` + `Service.SaveChanges()` — это исключает ошибки блокировки при частых кликах. Полная защита от race-condition (ETag/RowVersion) — Phase 4+.
2. **Размер `SubtasksJSON`.** При 1000 подзадачах JSON разрастается. Soft-limit 200 элементов на задачу, hard-limit 1 МБ на атрибут (платформенный).
3. **IE11 + `JSON.parse`.** Доступен. Используем безопасно.
4. **Парсер `ParseComments`.** Если он строго привязан к именам полей комментариев — клонировать в отдельный `ParseSubtasks` без изменений алгоритма. Проверить на стенде.
5. **XSS.** Серверный `JsonEscape` + клиентский `tcmChatEsc` обязательны.
6. **Защита `|`.** `text.replace(/\|/g, " ")` перед отправкой. Серверный `ParsePipeArgs` пилит ровно по первому `|`.
7. **Genаrация `id`.** На сервере, не на клиенте. Гарантия уникальности и стабильности между правками.
8. **Liquid `!= 0`.** DotLiquid требует строгое сравнение типов: `card.subtaskTotal` должен быть `int`, не строка. Проверить на стенде.
9. **Регрессия счётчика комментариев.** Изменения близко к `CommentsJSON` коду — не сломать существующие методы. Клонирование вместо переиспользования снижает риск.
10. **Удаление подзадачи без подтверждения.** Сейчас confirm. Альтернатива (Phase 3) — Soft-delete с возможностью восстановить.

---

## 9. Критерии приёмки

- [ ] В модалке задачи видна вкладка «Чек-лист» между «Основное» и «Обсуждение».
- [ ] При открытии задачи список подзадач загружается из `SubtasksJSON`.
- [ ] Ввод текста + Enter (или кнопка `+`) добавляет подзадачу. Текст ≤ 500 символов.
- [ ] Чекбокс переключает состояние, обновляются `doneBy`/`doneAt`.
- [ ] Кнопка `×` удаляет подзадачу с подтверждением.
- [ ] Прогресс `done / total` виден в шапке вкладки и в бейдже вкладки.
- [ ] На карточке доски отображается бейдж `☑ done/total` если подзадач > 0.
- [ ] При обновлении доски бейдж пересчитывается.
- [ ] При наведении на текст выполненной подзадачи виден `title` с автором и датой выполнения.
- [ ] Все методы возвращают `OK` или `ERROR:*`. Никаких unhandled exceptions.
- [ ] Документация `docs/07_*.md` обновлена.

---

## 10. Тестовые сценарии

### Сценарий 1 — добавление и отметка

1. Открыть задачу. Перейти на «Чек-лист».
2. Ввести «Согласовать ТЗ», Enter → пункт появился, чекбокс пуст. Прогресс `0 / 1`.
3. Ввести «Подготовить демо», Enter → прогресс `0 / 2`.
4. Отметить первый чекбокс → прогресс `1 / 2`. Текст перечёркнут. Бейдж карточки `☑ 1/2`.

### Сценарий 2 — удаление

1. В пункте 1 кликнуть `×`. Подтвердить.
2. Пункт исчез. Прогресс `0 / 1`. Бейдж карточки `☑ 0/1`.

### Сценарий 3 — снятие отметки

1. Отметить пункт → done=1, doneBy/doneAt заполнены.
2. Снять отметку → done=0, doneBy/doneAt удалены из JSON (проверить через `GetSubtasks`).

### Сценарий 4 — закрытие и повторное открытие

1. Добавить 3 пункта, отметить 2.
2. Закрыть модалку (`Esc` из Phase 1).
3. Открыть задачу снова → видны те же 3 пункта, 2 отмечены.

### Сценарий 5 — длинный текст

1. Ввести текст 600 символов → сервер обрезает до 500 (`if text.Length > 500`).

### Сценарий 6 — спецсимволы

1. Ввести `"hello | world\n` → `|` заменяется на пробел, кавычки/переносы корректно сохранены и отрисованы (через `JsonEscape` + `tcmChatEsc`).

### Сценарий 7 — конкурентные правки

1. Пользователь A открыл задачу, видит 3 пункта.
2. Пользователь B добавил 4-й пункт.
3. Пользователь A обновил → его правка сохраняется, но 4-й пункт пропадает (last-write-wins). **Это известное ограничение Phase 2.**

### Сценарий 8 — бейдж карточки

1. На доске у задачи 5 подзадач, 3 выполнено.
2. На `.kb-card` виден бейдж `☑ 3/5`.
3. После toggle через модалку и закрытия модалки `kbRefreshBoard` обновляет бейдж.

### Сценарий 9 — пустой текст

1. Ввести только пробелы, Enter → ничего не добавлено (сервер `EmptyText`).

### Сценарий 10 — регрессия комментариев

1. Использование вкладки «Обсуждение» работает как до изменений.
