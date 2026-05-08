# 01 — Drag-n-drop и редактирование пунктов чек-листа

> **Затронутые файлы:**
> - `scripts/SOYUZ_UPLOAD_KanbanScreen_script.cs`
> - `scripts/kanban.js`
> - `scripts/KanbanBoard_HTML.html`
> - `scripts/kanban.css`

## Что делаем

В модалке задачи (вкладка «Чек-лист») сейчас пункты можно только добавлять / отмечать выполненными / удалять. Нужно добавить:

1. **Drag-and-drop** перестановку пунктов мышью (как карточки на доске).
2. **Inline-редактирование** текста пункта (двойной клик по тексту → input → Enter сохранить / Esc отменить).

Структура подзадачи в JSON-атрибуте `SubtasksJSON` уже хранит порядок (массив). Новый порядок = новый порядок элементов в массиве. Текст пункта = поле `text` в объекте.

## Контекст из платформы

- HTML5 DnD работает (см. `kanban.js:166` `kbDragStart` для карточек). Используем тот же паттерн для пунктов чек-листа.
- Все мутации `SubtasksJSON` идут через сериализатор `SerializeSubtasks` + `task.Save()`, см. существующие методы `DoAddSubtask` / `DoToggleSubtask` / `DoDeleteSubtask` в `SOYUZ_UPLOAD_KanbanScreen_script.cs:3062+`. Используем тот же паттерн.
- ChangeLog для истории — функция `AppendSubtaskChangeLog( task, action, text )` уже есть, вызываем для записей «Изменён пункт» и «Перемещён пункт».
- IE11/Trident: вместо ES6+ использовать `var`/`function`. Никаких `const`/`let`/стрелочных функций/template-литералов.

## Часть A — серверные методы (C#)

### A.1. Добавить ветки в `Invoke` (около строки 198)

В `SOYUZ_UPLOAD_KanbanScreen_script.cs`, рядом с существующими ветками `case "AddSubtask"` ... `case "GetSubtasks"`:

```csharp
case "EditSubtask":     return DoEditSubtask( inputParams );
case "ReorderSubtasks": return DoReorderSubtasks( inputParams );
```

### A.2. Метод `DoEditSubtask`

Разместить в файле сразу **после** `DoToggleSubtask` (около строки 3226, перед `DoDeleteSubtask`).

```csharp
// ─── EditSubtask ────────────────────────────────────────────────────
// inputParams: "taskKey|subtaskId|newText"
// Меняет text, проставляет editedBy/editedAt. Возвращает JSON обновлённого пункта.
private object DoEditSubtask( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 3 );
    if( parts.Length < 3 ) return "ERROR:BadFormat";

    var taskKey   = parts[0].Trim();
    var subtaskId = parts[1].Trim();
    var newText   = parts[2].Trim();

    if( string.IsNullOrEmpty( taskKey ) )   return "ERROR:NoKey";
    if( string.IsNullOrEmpty( subtaskId ) ) return "ERROR:NoId";
    if( string.IsNullOrEmpty( newText ) )   return "ERROR:EmptyText";
    if( newText.Length > 500 ) newText = newText.Substring( 0, 500 );

    try
    {
        var task = GetTaskByKeyOrNull( taskKey );
        if( task == null ) return "ERROR:NotFound";

        var items = ParseSubtasks( task.GetString( "SubtasksJSON" ) ?? "" );

        int idx = -1;
        for( int i = 0; i < items.Count; i++ )
        {
            string v;
            if( items[i].TryGetValue( "id", out v ) && v == subtaskId ) { idx = i; break; }
        }
        if( idx < 0 ) return "ERROR:NoSuchSubtask";

        string oldText = "";
        items[idx].TryGetValue( "text", out oldText );
        if( oldText == newText )
        {
            // Без изменений — не пишем в лог, не дёргаем Save
            var sbNoop = new System.Text.StringBuilder();
            sbNoop.Append( "{" );
            bool firstNoop = true;
            foreach( var kv in items[idx] )
            {
                if( !firstNoop ) sbNoop.Append( "," );
                sbNoop.Append( "\"" + JsonEscape( kv.Key ) + "\":\"" + JsonEscape( kv.Value ) + "\"" );
                firstNoop = false;
            }
            sbNoop.Append( "}" );
            return sbNoop.ToString();
        }

        items[idx]["text"] = newText;

        var user = Service.GetCurrentUser();
        items[idx]["editedBy"] = user != null ? (user.ToString() ?? "") : "";
        items[idx]["editedAt"] = DateTime.Now.ToString( "yyyy-MM-ddTHH:mm:ss",
                                     System.Globalization.CultureInfo.InvariantCulture );

        task["SubtasksJSON"] = SerializeSubtasks( items );
        // SubtasksTotal/SubtasksDone не меняются при редактировании текста
        AppendSubtaskChangeLog( task, "Изменён пункт", oldText + " → " + newText );
        task.Save();

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
        Service.HandleException( ex, "KanbanScreen.DoEditSubtask: " + ex.Message );
        return "ERROR:Internal";
    }
}
```

### A.3. Метод `DoReorderSubtasks`

Разместить сразу после `DoEditSubtask`, перед `DoDeleteSubtask`.

```csharp
// ─── ReorderSubtasks ────────────────────────────────────────────────
// inputParams: "taskKey|id1,id2,id3,..."
// Принимает новый порядок ID и переупорядочивает массив. Возвращает "OK" или ERROR.
// Если переданный набор ID не совпадает с текущим (по содержимому, не по порядку) —
// возвращает ERROR:Mismatch и не сохраняет ничего.
private object DoReorderSubtasks( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 2 );
    if( parts.Length < 2 ) return "ERROR:BadFormat";

    var taskKey  = parts[0].Trim();
    var orderStr = parts[1].Trim();
    if( string.IsNullOrEmpty( taskKey ) )  return "ERROR:NoKey";
    if( string.IsNullOrEmpty( orderStr ) ) return "ERROR:EmptyOrder";

    var newOrder = orderStr.Split( new char[]{ ',' }, StringSplitOptions.RemoveEmptyEntries );
    if( newOrder.Length == 0 ) return "ERROR:EmptyOrder";

    try
    {
        var task = GetTaskByKeyOrNull( taskKey );
        if( task == null ) return "ERROR:NotFound";

        var items = ParseSubtasks( task.GetString( "SubtasksJSON" ) ?? "" );
        if( items.Count != newOrder.Length ) return "ERROR:Mismatch";

        // Индекс по id для O(1) поиска
        var byId = new System.Collections.Generic.Dictionary<string,
                       System.Collections.Generic.Dictionary<string, string>>( items.Count );
        for( int i = 0; i < items.Count; i++ )
        {
            string v;
            if( !items[i].TryGetValue( "id", out v ) || string.IsNullOrEmpty( v ) ) return "ERROR:CorruptItem";
            if( byId.ContainsKey( v ) ) return "ERROR:DuplicateId";
            byId[v] = items[i];
        }

        var reordered = new System.Collections.Generic.List<
            System.Collections.Generic.Dictionary<string, string>>( items.Count );
        for( int i = 0; i < newOrder.Length; i++ )
        {
            var id = newOrder[i].Trim();
            if( !byId.ContainsKey( id ) ) return "ERROR:UnknownId";
            reordered.Add( byId[id] );
        }

        task["SubtasksJSON"] = SerializeSubtasks( reordered );
        // Total/Done не меняются — пропускаем
        AppendSubtaskChangeLog( task, "Изменён порядок пунктов", "" );
        task.Save();
        return "OK";
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "KanbanScreen.DoReorderSubtasks: " + ex.Message );
        return "ERROR:Internal";
    }
}
```

## Часть B — клиент: HTML

> Сама разметка строки рендерится в JS (см. C.1). HTML меняется только в блоке `#tcm-tab-subt`: переставляем поле ввода наверх + добавляем подсказку.

### B.1. Перестановка: поле ввода — НАВЕРХ, список — под ним

**Изменение по сравнению с Phase 2:** строка ввода `.kb-subt-add` (input + кнопка `+`) переезжает с низа вкладки наверх, чтобы пользователь сразу после клика на «Чек-лист» видел поле ввода и не скроллил вниз. По мере добавления пунктов список растёт под полем ввода.

В `KanbanBoard_HTML.html`, блок `id="tcm-tab-subt"` (около строки 696) — заменить целиком на:

```html
<div id="tcm-tab-subt" class="tcm-tab-content">
    <div class="kb-subt-header">
        <span class="kb-subt-title">Чек-лист</span>
        <span class="kb-subt-progress" id="tcm-subt-progress">0 / 0</span>
    </div>
    <div class="kb-subt-add">
        <input type="text" id="tcm-subt-input" class="form-control input-sm"
            placeholder="Добавить пункт чек-листа..."
            onkeydown="tcmSubtasksKeydown(event)" maxlength="500">
        <button type="button" class="tcm-btn tcm-btn-primary"
            onclick="tcmSubtasksAdd()">+</button>
    </div>
    <div class="kb-subt-hint">Перетащите <i class="fa fa-bars"></i> чтобы изменить порядок. Двойной клик по тексту — редактировать.</div>
    <div class="kb-subt-list" id="tcm-subt-list"></div>
</div>
```

Изменения:
- `.kb-subt-add` теперь **сразу после** `.kb-subt-header` (а не в самом низу).
- `.kb-subt-hint` остаётся ПОД полем ввода, как мини-подсказка.
- `.kb-subt-list` идёт В САМОМ НИЗУ — список растёт вниз, при большом количестве пунктов скроллится внутри списка.

> **Внимание**: тип контейнера `#tcm-tab-subt` сейчас имеет `display: flex; flex-direction: column` (в `kanban.css` блок `.tcm-tab-content`). Сохранение flex-структуры — критично, иначе CSS Phase 2 для `.kb-subt-list { flex: 1 1 auto; min-height: 0; overflow-y: auto; }` не отработает. Никаких изменений в `.tcm-tab-content` — только перестановка дочерних элементов.

## Часть C — клиент: JS

### C.1. Заменить функцию `tcmSubtasksRender`

В `kanban.js` найти функцию `window.tcmSubtasksRender = function ()` (около строки 890) и **заменить целиком** на следующую. Изменения:

- Каждая строка получает `draggable="true"`.
- В строке появляется «рукоятка» `<span class="kb-subt-drag" title="Перетащить">≡</span>` слева, как ручка для drag.
- Текст подзадачи получает `ondblclick="tcmSubtasksBeginEdit('id')"`.
- Привязываем DnD-обработчики к строке.

```javascript
window.tcmSubtasksRender = function () {
    var list = document.getElementById("tcm-subt-list");
    if (!list) return;
    var items = _tcmSubtasks.items || [];
    var html = "", i, it, doneClass, doneTitle, editedNote;
    for (i = 0; i < items.length; i++) {
        it = items[i];
        doneClass = (it.done === "1") ? " kb-subt-done" : "";
        doneTitle = (it.done === "1" && it.doneBy)
            ? ("Выполнено: " + it.doneBy + " (" + (it.doneAt || "") + ")")
            : "";
        editedNote = (it.editedBy)
            ? (" (изм. " + it.editedBy + (it.editedAt ? ", " + it.editedAt : "") + ")")
            : "";

        html += '<div class="kb-subt-item' + doneClass + '" data-id="' + tcmChatEsc(it.id) + '"'
              + ' draggable="true"'
              + ' ondragstart="tcmSubtasksDragStart(event, \'' + tcmChatEsc(it.id) + '\')"'
              + ' ondragend="tcmSubtasksDragEnd(event)"'
              + ' ondragover="tcmSubtasksDragOver(event)"'
              + ' ondragleave="tcmSubtasksDragLeave(event)"'
              + ' ondrop="tcmSubtasksDrop(event, \'' + tcmChatEsc(it.id) + '\')">'
              + '<span class="kb-subt-drag" title="Перетащить">&#8801;</span>'
              + '<label class="kb-subt-cb-wrap">'
              + '<input type="checkbox" class="kb-subt-cb"'
              + (it.done === "1" ? " checked" : "")
              + ' onchange="tcmSubtasksToggle(\'' + tcmChatEsc(it.id) + '\')">'
              + '<span class="kb-subt-cb-fake"></span>'
              + '</label>'
              + '<span class="kb-subt-text" title="' + tcmChatEsc(doneTitle + editedNote) + '"'
              + ' ondblclick="tcmSubtasksBeginEdit(\'' + tcmChatEsc(it.id) + '\')">'
              + tcmChatEsc(it.text || "") + '</span>'
              + '<button type="button" class="kb-subt-del" '
              + 'onclick="tcmSubtasksDelete(\'' + tcmChatEsc(it.id) + '\')" title="Удалить">×</button>'
              + '</div>';
    }
    list.innerHTML = html || '<div class="kb-subt-empty">Нет подзадач</div>';

    var total = items.length, done = 0;
    for (i = 0; i < items.length; i++) if (items[i].done === "1") done++;

    var pr = document.getElementById("tcm-subt-progress");
    if (pr) pr.innerHTML = total ? (done + " / " + total) : "0 / 0";

    var badge = document.getElementById("tcm-tab-subt-badge");
    if (badge) {
        if (total > 0) {
            badge.innerHTML = done + "/" + total;
            badge.style.display = "";
        } else {
            badge.style.display = "none";
        }
    }
};
```

### C.2. Добавить блок DnD-обработчиков

В `kanban.js` сразу **после** функции `tcmSubtasksKeydown` (около строки 1002) вставить:

```javascript
// ── DnD-перестановка пунктов чек-листа ─────────────────────────────
var _tcmSubtDragId = null;

window.tcmSubtasksDragStart = function (event, subtaskId) {
    // Если редактируем — отменяем DnD (input внутри строки)
    if (_tcmSubtEditingId) { event.preventDefault(); return; }
    _tcmSubtDragId = subtaskId;
    try {
        event.dataTransfer.setData("text", subtaskId);
        event.dataTransfer.effectAllowed = "move";
    } catch (e) { /* IE11 quirk */ }
    var row = document.querySelector('.kb-subt-item[data-id="' + tcmChatEsc(subtaskId) + '"]');
    if (row) row.className += " kb-subt-dragging";
};

window.tcmSubtasksDragEnd = function (event) {
    _tcmSubtDragId = null;
    var rows = document.querySelectorAll(".kb-subt-item");
    for (var i = 0; i < rows.length; i++) {
        rows[i].className = rows[i].className
            .replace(/\s*kb-subt-dragging/g, "")
            .replace(/\s*kb-subt-drop-(before|after)/g, "");
    }
};

window.tcmSubtasksDragOver = function (event) {
    if (!_tcmSubtDragId) return;
    event.preventDefault();
    event.stopPropagation();
    try { event.dataTransfer.dropEffect = "move"; } catch (e) { }

    // Подсветить, куда уйдёт пункт: верх или низ строки → before/after
    var row = event.currentTarget;
    if (!row || !row.className || row.className.indexOf("kb-subt-item") === -1) return;
    var rect = row.getBoundingClientRect();
    var midY = rect.top + rect.height / 2;
    var before = (event.clientY < midY);

    // Очистить старые подсветки на этой строке и поставить новую
    row.className = row.className.replace(/\s*kb-subt-drop-(before|after)/g, "")
                  + " kb-subt-drop-" + (before ? "before" : "after");
};

window.tcmSubtasksDragLeave = function (event) {
    var row = event.currentTarget;
    if (!row || !row.className) return;
    row.className = row.className.replace(/\s*kb-subt-drop-(before|after)/g, "");
};

window.tcmSubtasksDrop = function (event, targetId) {
    event.preventDefault();
    event.stopPropagation();
    var srcId = _tcmSubtDragId;
    _tcmSubtDragId = null;

    var rows = document.querySelectorAll(".kb-subt-item");
    for (var i = 0; i < rows.length; i++) {
        rows[i].className = rows[i].className
            .replace(/\s*kb-subt-dragging/g, "")
            .replace(/\s*kb-subt-drop-(before|after)/g, "");
    }

    if (!srcId || !targetId || srcId === targetId) return;

    // Считаем before/after по позиции курсора в момент drop
    var row = event.currentTarget;
    var rect = row.getBoundingClientRect();
    var midY = rect.top + rect.height / 2;
    var before = (event.clientY < midY);

    var items = _tcmSubtasks.items || [];
    var srcIdx = -1, tgtIdx = -1;
    for (var j = 0; j < items.length; j++) {
        if (items[j].id === srcId) srcIdx = j;
        if (items[j].id === targetId) tgtIdx = j;
    }
    if (srcIdx < 0 || tgtIdx < 0) return;

    var moved = items.splice(srcIdx, 1)[0];
    if (srcIdx < tgtIdx) tgtIdx--;          // компенсация после splice
    var insertAt = before ? tgtIdx : tgtIdx + 1;
    items.splice(insertAt, 0, moved);

    // Оптимистично перерисовываем сразу, потом отправляем на сервер
    tcmSubtasksRender();

    var orderIds = [];
    for (var k = 0; k < items.length; k++) orderIds.push(items[k].id);

    var safeKey = String(_tcmSubtasks.taskKey).replace(/\|/g, "");
    var orderStr = orderIds.join(",").replace(/\|/g, "");
    try {
        var res = window.external.InvokeTemplate("ReorderSubtasks", safeKey + "|" + orderStr);
        var s = String(res || "");
        if (s.indexOf("OK") !== 0) {
            // Откат: перезагружаем с сервера, если ошибка
            alert("Ошибка перестановки: " + s);
            tcmSubtasksLoad(_tcmSubtasks.taskKey);
            return;
        }
        // Инвалидация истории — порядок изменения попал в ChangeLog
        if (typeof tcmInvalidateRevsCache === "function") tcmInvalidateRevsCache();
        _tcmRevsLoaded = false;
        var rb = document.getElementById("tcm-revs-body");
        if (rb) rb.innerHTML = "Загрузка...";
    } catch (e) {
        alert("Ошибка перестановки: " + (e.message || e));
        tcmSubtasksLoad(_tcmSubtasks.taskKey);
    }
};
```

### C.3. Добавить блок inline-редактирования

Сразу после блока DnD (т.е. ниже `tcmSubtasksDrop`) вставить:

```javascript
// ── Inline-редактирование текста подзадачи ─────────────────────────
var _tcmSubtEditingId = null;

window.tcmSubtasksBeginEdit = function (subtaskId) {
    if (_tcmSubtEditingId) return; // уже редактируем
    var row = document.querySelector('.kb-subt-item[data-id="' + tcmChatEsc(subtaskId) + '"]');
    if (!row) return;
    var span = row.querySelector(".kb-subt-text");
    if (!span) return;

    var current = "";
    var items = _tcmSubtasks.items || [];
    for (var i = 0; i < items.length; i++) {
        if (items[i].id === subtaskId) { current = items[i].text || ""; break; }
    }

    _tcmSubtEditingId = subtaskId;
    // На время редактирования снимаем draggable, иначе IE может перехватить mousedown
    try { row.setAttribute("draggable", "false"); } catch (e) { }

    var inp = document.createElement("input");
    inp.type = "text";
    inp.className = "form-control input-sm kb-subt-edit-input";
    inp.value = current;
    inp.setAttribute("maxlength", "500");
    inp.onkeydown = function (e) {
        var code = e.keyCode || e.which;
        if (code === 13) { tcmSubtasksCommitEdit(subtaskId, inp.value); e.preventDefault(); }
        else if (code === 27) { tcmSubtasksCancelEdit(); e.preventDefault(); }
    };
    inp.onblur = function () { tcmSubtasksCommitEdit(subtaskId, inp.value); };

    // Заменяем span на input
    span.parentNode.replaceChild(inp, span);
    inp.focus();
    try { inp.select(); } catch (e) { }
};

window.tcmSubtasksCommitEdit = function (subtaskId, newText) {
    if (_tcmSubtEditingId !== subtaskId) return;
    _tcmSubtEditingId = null;

    var clean = String(newText || "").replace(/\|/g, " ").substring(0, 500).replace(/^\s+|\s+$/g, "");
    if (!clean) {
        // Пустой текст — отменяем
        tcmSubtasksRender();
        return;
    }

    var items = _tcmSubtasks.items || [];
    var current = "";
    for (var i = 0; i < items.length; i++) {
        if (items[i].id === subtaskId) { current = items[i].text || ""; break; }
    }
    if (clean === current) {
        // Без изменений — просто рендерим назад
        tcmSubtasksRender();
        return;
    }

    var safeKey = String(_tcmSubtasks.taskKey).replace(/\|/g, "");
    var safeId  = String(subtaskId).replace(/\|/g, "");
    try {
        var raw = window.external.InvokeTemplate(
            "EditSubtask", safeKey + "|" + safeId + "|" + clean);
        var s = String(raw || "");
        if (s.indexOf("ERROR") === 0) {
            alert("Ошибка редактирования: " + s);
            tcmSubtasksRender();
            return;
        }
        var obj = JSON.parse(s);
        for (var j = 0; j < items.length; j++) {
            if (items[j].id === subtaskId) { items[j] = obj; break; }
        }
        tcmSubtasksRender();
        if (typeof tcmInvalidateRevsCache === "function") tcmInvalidateRevsCache();
        _tcmRevsLoaded = false;
        var rb = document.getElementById("tcm-revs-body");
        if (rb) rb.innerHTML = "Загрузка...";
    } catch (e) {
        alert("Ошибка редактирования: " + (e.message || e));
        tcmSubtasksRender();
    }
};

window.tcmSubtasksCancelEdit = function () {
    _tcmSubtEditingId = null;
    tcmSubtasksRender();
};
```

## Часть D — CSS (`kanban.css`)

### D.1. Перевернуть отступы у `.kb-subt-add` (поле ввода теперь сверху, не снизу)

Phase 2 поставил `.kb-subt-add` в самый низ вкладки и стилизовал отделитель как `border-top` сверху. Теперь поле едет наверх — найти существующий блок `.kb-subt-add` (около строки 2382 в `kanban.css`) и **заменить целиком** на:

```css
.kb-subt-add {
    display: -ms-flexbox;
    display: flex;
    margin-bottom: 10px;
    padding-bottom: 10px;
    border-bottom: 1px solid #e5e7eb;
    -ms-flex-negative: 0;
    flex-shrink: 0; /* Фиксируем поле ввода в самом верху вкладки, не сжимается */
    -ms-flex-order: -1;
    order: -1; /* Подстраховка: даже если в HTML случайно остался внизу — flex его поднимет */
}
.kb-subt-add input {
    -ms-flex: 1 1 auto;
    flex: 1 1 auto;
    margin-right: 6px;
}
```

Изменения:
- `margin-top` → `margin-bottom`.
- `padding-top` → `padding-bottom`.
- `border-top` → `border-bottom`.
- Добавлен `order: -1` — флексовая подстраховка, чтобы при любом порядке HTML поле всегда было сверху.

### D.2. Добавить новые стили (DnD, edit, hint)

Дополнить блок `/* ─── Чек-лист (подзадачи) ─── */` (около строки 2233+) следующими стилями. Расположить **в конце блока чек-листа**, перед `/* Бейдж на карточке доски */`:

```css
/* ── Подсказка над списком ── */
.kb-subt-hint {
    font-size: 11px;
    color: #6b7280;
    margin: 0 0 8px 0;
    padding: 0;
}
.kb-subt-hint .fa { color: #4a6fa5; margin: 0 2px; }

/* ── Рукоятка drag ── */
.kb-subt-drag {
    display: inline-block;
    width: 16px;
    flex-shrink: 0;
    -ms-flex-negative: 0;
    color: #9ca3af;
    cursor: grab;
    font-size: 16px;
    line-height: 1;
    user-select: none;
    -ms-user-select: none;
    margin-right: 6px;
    text-align: center;
}
.kb-subt-drag:active { cursor: grabbing; }
.kb-subt-item:hover .kb-subt-drag { color: #4a6fa5; }

/* ── Состояния DnD: подсветка инсерт-линии ── */
.kb-subt-item.kb-subt-dragging { opacity: 0.4; }
.kb-subt-item.kb-subt-drop-before {
    box-shadow: 0 -2px 0 0 #4a6fa5 inset;
    -webkit-box-shadow: 0 -2px 0 0 #4a6fa5 inset;
}
.kb-subt-item.kb-subt-drop-after {
    box-shadow: 0 2px 0 0 #4a6fa5 inset;
    -webkit-box-shadow: 0 2px 0 0 #4a6fa5 inset;
}

/* ── Inline-редактирование текста ── */
.kb-subt-edit-input {
    flex: 1 1 auto;
    -ms-flex: 1 1 auto;
    height: 26px !important;
    padding: 2px 6px !important;
    font-size: 13px !important;
    border: 1px solid #4a6fa5 !important;
    border-radius: 4px !important;
    background: #fff !important;
    box-shadow: 0 0 0 2px rgba(74,111,165,0.15) !important;
    -webkit-box-shadow: 0 0 0 2px rgba(74,111,165,0.15) !important;
}

/* ── Текст пункта: чтобы dblclick был ощутим, добавим лёгкий ховер ── */
.kb-subt-text { cursor: text; }
.kb-subt-text:hover { background: rgba(74,111,165,0.06); border-radius: 3px; }
```

## Часть E — Атомарные коммиты

| # | Шаг | Файлы | Сообщение коммита |
|---|-----|-------|-------------------|
| 1 | C#: `DoEditSubtask` + ветка `case "EditSubtask":` | `SOYUZ_UPLOAD_KanbanScreen_script.cs` | `feat(kanban): server EditSubtask method` |
| 2 | C#: `DoReorderSubtasks` + ветка `case "ReorderSubtasks":` | `SOYUZ_UPLOAD_KanbanScreen_script.cs` | `feat(kanban): server ReorderSubtasks method` |
| 3 | JS: новый рендер строк (drag-handle, dblclick) + хелп-блок в HTML + базовые CSS | `kanban.js`, `KanbanBoard_HTML.html`, `kanban.css` | `feat(kanban): subtasks UI scaffolding for DnD/edit` |
| 4 | JS: блок DnD-обработчиков + CSS подсветки | `kanban.js`, `kanban.css` | `feat(kanban): subtasks drag-and-drop reorder` |
| 5 | JS: блок inline-редактирования + CSS инпута | `kanban.js`, `kanban.css` | `feat(kanban): subtasks inline edit` |
| 6 | Документация в «Что нового» / «Справка» | `KanbanBoard_HTML.html` | `docs(kanban): whatsnew entry for subtasks dnd/edit` |

## Часть F — Проверки и риски

1. **`AppendSubtaskChangeLog`** уже существует в `SOYUZ_UPLOAD_KanbanScreen_script.cs`. Проверить сигнатуру (`task, action, text`) перед использованием — если другая, адаптировать.
2. **`ParsePipeArgs( raw, 3 )`** для `EditSubtask`: убедиться, что разделение по `|` идёт **по первым двум** символам, а оставшаяся часть text сохраняется целиком (включая возможные `|` если попали). В существующем коде это так (`ParsePipeArgs` принимает количество частей), но нужно **подтвердить** чтением реализации `ParsePipeArgs`. Если она режет ровно по N-1 разделителей — ОК; иначе экранировать `|` на клиенте (что уже делается через `replace(/\|/g, " ")`).
3. **DnD на пункте чек-листа vs DnD карточки на доске**: события не конфликтуют, поскольку модалка `tcmOverlay` перекрывает доску.
4. **Двойной клик при выполнении ondblclick** — браузер также может выделить текст. В CSS `.kb-subt-text` оставляем `user-select: text` (как было), это нормально — текст выделится один раз, потом подменится на инпут.
5. **Откат при ошибке сервера**: после неудачного `ReorderSubtasks` или `EditSubtask` — перечитать состояние через `tcmSubtasksLoad(_tcmSubtasks.taskKey)`. Это гарантирует консистентность UI и БД.
6. **IE11**: `event.currentTarget` в обработчиках inline `ondrop` / `ondragover` поддерживается. `getBoundingClientRect()` поддерживается. `querySelector` / `querySelectorAll` поддерживаются.
7. **Атрибуты `editedBy` / `editedAt`** в JSON-объекте подзадачи — новые поля. Существующие записи без них продолжают работать (в JS — `it.editedBy` = `undefined` → `editedNote = ""`).

## Часть G — Критерии приёмки

- [ ] **Поле ввода нового пункта** находится **сверху** вкладки «Чек-лист», сразу под заголовком и счётчиком.
- [ ] Список пунктов растёт **под** полем ввода. После добавления пункт уходит вниз, поле остаётся вверху.
- [ ] При большом количестве пунктов список скроллится сам (внутри вкладки), поле ввода и заголовок зафиксированы сверху.
- [ ] Пункт чек-листа имеет слева «рукоятку» `≡`, при наведении она меняет цвет.
- [ ] Перетаскивание пункта мышью между другими — пункт меняет позицию, история (`tcm-tab-hist`) фиксирует «Изменён порядок пунктов».
- [ ] Подсветка `before/after` работает: при наведении на верх строки — линия сверху, на низ — снизу.
- [ ] Двойной клик по тексту пункта → появляется input с текущим текстом, фокус.
- [ ] Enter в input → текст сохраняется, история фиксирует «Изменён пункт: старый → новый».
- [ ] Esc в input → отмена, текст не меняется.
- [ ] Click вне input (blur) → текст сохраняется (как Enter).
- [ ] Пустой текст после Enter → не сохраняется, остаётся прежний текст.
- [ ] При ошибке сервера: `alert` + перерисовка из БД.
- [ ] Чекбокс `done` и кнопка `×` продолжают работать как раньше.
- [ ] При изменении порядка/текста бейдж задачи `☑ N/M` не меняется (это норма — total/done не двигаются).
