# 05 — Глобальный поиск задач на канбан-доске

> **Источник в backlog:** `KANBAN_IMPROVEMENTS_BACKLOG.md` §2.5 (поднят в Phase 1).
> **Фича:** `add_global_search`
> **Приоритет:** P0 (Phase 1)
> **Затронутые файлы:** `scripts/SOYUZ_UPLOAD_KanbanScreen_script.cs`, `scripts/kanban.js`, `scripts/KanbanBoard_HTML.html`, `scripts/kanban.css`.

---

## 1. Техническое описание задачи

### 1.1. Требования backlog

> - Добавить поле поиска в верхнюю панель
> - Поиск должен работать по **названию, описанию, тегам и тексту комментариев**
> - Фильтрация должна происходить мгновенно по всем видимым колонкам

### 1.2. Что делаем

Гибридная схема:

1. **Клиентский фильтр (мгновенный)** — при вводе подстроки скрывает не подходящие карточки в текущем DOM. Отрабатывает на любые подстроки в видимом тексте карточки (заголовок, описание, теги, исполнитель, поручил).
2. **Серверный поиск (по `Enter` или 300 мс debounce)** — расширяет область до `CommentsJSON`, потому что текст комментариев не виден в DOM карточки. Возвращает JSON-список задач, которые надо «подсветить» через open/scroll.
3. **Если совпадение найдено только на сервере** (не среди видимых карточек) — клиент открывает Task modal этой задачи или показывает баннер «Найдено N задач только в комментариях / вне фильтра».

### 1.3. Поля поиска

| Поле | Где |
|------|-----|
| `TaskName` | заголовок |
| `TaskDetails` | описание |
| `Tags` | бейджи `.kb-tag` |
| `Assignee` | бейдж `.kb-assignee-tag[title]` |
| `Creator` | подпись «Поручил: …» |
| **`CommentsJSON`** | только серверный поиск |

Поиск регистронезависимый.

### 1.4. Что НЕ делаем (в Phase 2)

- Фильтр по периоду (это §2.6 backlog — отдельная задача).
- Сохранение строки поиска при `RefreshBoard` (Phase 2).
- Подсветка совпадений внутри карточки (Phase 2).

---

## 2. Затронутые файлы

| Путь | Что меняем |
|------|------------|
| `scripts/SOYUZ_UPLOAD_KanbanScreen_script.cs` | Новый метод `DoSearchTasks` (поиск по 5 текстовым полям + комментариям), новая ветка `case "SearchTasks":` в `Invoke` |
| `scripts/kanban.js` | Новый блок `kbBoardSearch*`: `kbBoardSearchInit`, `kbBoardSearchOnInput`, `kbBoardSearchClear`, `kbBoardSearchApplyClient`, `kbBoardSearchApplyServer`, `kbBoardSearchUpdateColumnCounters`, `kbBoardSearchShowServerBanner`, `kbBoardSearchOpenList`. Подвязка к `kbInitHierarchy` |
| `scripts/KanbanBoard_HTML.html` | Поле `<input id="kb-board-search">` в верхней панели, баннер `kb-board-search-banner`, модалка `kb-search-list-modal` |
| `scripts/kanban.css` | Стили поля, баннера, модалки списка |

---

## 3. C# — что и где менять (`SOYUZ_UPLOAD_KanbanScreen_script.cs`)

### 3.1. Ветка в `Invoke` (рядом со строкой 140 `SearchObjects`)

```csharp
case "SearchTasks": return DoSearchTasks( obj, inputParams );
```

### 3.2. Новый метод `DoSearchTasks`

Размещаем рядом с `DoSearchObjects` (после строки ~2066).

```csharp
// ─── SearchTasks ────────────────────────────────────────────────────
// inputParams: "query"
// Возвращает JSON-массив { nameKey, status, title, assigneeName,
//                          matchInComments }
// Поиск по: TaskName, TaskDetails, Tags, Assignee, Creator, CommentsJSON.
// Ограничен текущим scope пользователя (как BeforeRender).
private object DoSearchTasks( InfoObject screenObj, object inputParams )
{
    var query = GetParamStr( inputParams );
    if( string.IsNullOrEmpty( query ) || query.Length < 2 ) return "[]";
    var q = query.ToLowerInvariant();

    try
    {
        var container = Service.GetDataContainer( "All_Kanban_Tasks_Folder" );
        if( container == null ) return "[]";

        var currentUser = Service.GetCurrentUser();
        var role        = GetUserRole( currentUser );
        var viewMode    = (screenObj.PropertyBag["KbViewMode"] as string) ?? "my";
        var allowedIds  = GetAllowedUserIdSet( currentUser, role, viewMode );

        var sb = new System.Text.StringBuilder();
        sb.Append( "[" );
        int found = 0;

        foreach( var task in container.RootInfoObjects )
        {
            var assignee = task.GetUser( "Assignee" );
            if( assignee == null ) continue;
            if( allowedIds != null && !allowedIds.Contains( assignee.Id.ToString() ) )
                continue;

            string title       = task.GetString( "TaskName" )    ?? "";
            string details     = task.GetString( "TaskDetails" ) ?? "";
            string tags        = task.GetString( "Tags" )        ?? "";
            string aName       = assignee.ToString() ?? "";
            string creatorName = task.GetString( "Creator" )     ?? "";
            string commentsJs  = task.GetString( "CommentsJSON" ) ?? "";

            string baseBlob = (title + "|" + details + "|" + tags + "|" + aName + "|" + creatorName)
                              .ToLowerInvariant();
            bool baseHit     = baseBlob.IndexOf( q, System.StringComparison.Ordinal ) >= 0;
            bool commentsHit = false;
            if( !baseHit )
                commentsHit = commentsJs.ToLowerInvariant()
                              .IndexOf( q, System.StringComparison.Ordinal ) >= 0;
            if( !baseHit && !commentsHit ) continue;

            int status = GetStatusIndex( task );
            if( status < 0 || status > 3 ) status = 0;

            string nameKey = string.IsNullOrEmpty( task.NameKey )
                           ? "__id_" + task.Id.ToString()
                           : task.NameKey;

            if( found > 0 ) sb.Append( "," );
            sb.Append( "{\"nameKey\":\""        + JsonEscape( nameKey )            + "\","
                     + "\"status\":"            + status.ToString()                + ","
                     + "\"title\":\""           + JsonEscape( title )              + "\","
                     + "\"assigneeName\":\""    + JsonEscape( aName )              + "\","
                     + "\"matchInComments\":"   + (commentsHit ? "true" : "false") + "}" );
            found++;
            if( found >= 100 ) break;
        }
        sb.Append( "]" );
        return sb.ToString();
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "KanbanScreen.DoSearchTasks: " + ex.Message );
        return "[]";
    }
}
```

> Контракт: `query` — строка ≥ 2 символов, ответ — JSON-массив до 100 элементов. Поле `matchInComments` — для UI-подсказки «найдено в комментариях».

---

## 4. JavaScript — что и где менять (`kanban.js`)

### 4.1. Состояние

```javascript
var _kbBoardSearch = { q: "", debounceId: 0, lastResults: [] };
```

### 4.2. Инициализация (вызвать из `kbInitHierarchy` или сразу после)

```javascript
window.kbBoardSearchInit = function () {
    var inp = document.getElementById("kb-board-search");
    if (!inp) return;

    inp.oninput = function () { kbBoardSearchOnInput(this.value); };
    inp.onkeydown = function (e) {
        var code = e.keyCode || e.which;
        if (code === 13) {
            kbBoardSearchApplyServer(this.value);
            e.preventDefault();
        } else if (code === 27) {
            kbBoardSearchClear();
            e.preventDefault();
        }
    };

    var clr = document.getElementById("kb-board-search-clear");
    if (clr) clr.onclick = function () { kbBoardSearchClear(); return false; };
};
```

### 4.3. Очистка

```javascript
window.kbBoardSearchClear = function () {
    var inp = document.getElementById("kb-board-search");
    if (inp) inp.value = "";
    _kbBoardSearch.q = "";
    kbBoardSearchApplyClient("");
    kbBoardSearchHideServerBanner();
    if (inp) inp.blur();
};
```

### 4.4. Клиентский фильтр (debounce 300 мс)

```javascript
function kbBoardSearchOnInput(val) {
    _kbBoardSearch.q = (val || "").toLowerCase();
    if (_kbBoardSearch.debounceId) {
        try { window.clearTimeout(_kbBoardSearch.debounceId); } catch (e) {}
    }
    var q = _kbBoardSearch.q;
    _kbBoardSearch.debounceId = window.setTimeout(function () {
        kbBoardSearchApplyClient(q);
        if (q.length >= 2) kbBoardSearchApplyServer(q);
        else kbBoardSearchHideServerBanner();
    }, 300);
}

function kbBoardSearchApplyClient(q) {
    var board = document.getElementById("kb-board");
    if (!board) return;
    var cards = board.getElementsByClassName("kb-card");
    var i, c, blob, hide;
    if (!q) {
        for (i = 0; i < cards.length; i++) cards[i].style.display = "";
        kbBoardSearchUpdateColumnCounters(false);
        return;
    }
    for (i = 0; i < cards.length; i++) {
        c = cards[i];
        blob = (c.textContent || c.innerText || "").toLowerCase();
        hide = blob.indexOf(q) < 0;
        c.style.display = hide ? "none" : "";
    }
    kbBoardSearchUpdateColumnCounters(true);
}

function kbBoardSearchUpdateColumnCounters(isFiltering) {
    var col, body, vis, cards, i;
    for (col = 0; col < 4; col++) {
        body = document.getElementById("kb-body-" + col);
        if (!body) continue;
        cards = body.getElementsByClassName("kb-card");
        vis = 0;
        for (i = 0; i < cards.length; i++) {
            if (cards[i].style.display !== "none") vis++;
        }
        var cnt = document.querySelector("#kb-col-" + col + " .kb-cnt");
        if (cnt) {
            if (!cnt.getAttribute("data-orig")) {
                cnt.setAttribute("data-orig", cnt.innerHTML.replace(/\s+/g, ""));
            }
            cnt.innerHTML = isFiltering
                ? (vis + " / " + cnt.getAttribute("data-orig"))
                : cnt.getAttribute("data-orig");
        }
    }
}
```

### 4.5. Серверный поиск + баннер

```javascript
function kbBoardSearchApplyServer(q) {
    if (!q || q.length < 2) return;
    var safeQ = String(q).replace(/\|/g, " ");
    try {
        var raw = window.external.InvokeTemplate("SearchTasks", safeQ);
        var arr = JSON.parse(String(raw || "[]"));
        _kbBoardSearch.lastResults = arr;

        var board = document.getElementById("kb-board");
        if (!board) return;

        var visibleKeys = {};
        var cards = board.getElementsByClassName("kb-card");
        var i, id;
        for (i = 0; i < cards.length; i++) {
            if (cards[i].style.display !== "none") {
                id = (cards[i].id || "").replace(/^kbc_/, "");
                if (id) visibleKeys[id] = true;
            }
        }

        var extraTotal = 0, inComments = 0;
        for (i = 0; i < arr.length; i++) {
            if (!visibleKeys[arr[i].nameKey]) extraTotal++;
            if (arr[i].matchInComments) inComments++;
        }

        if (extraTotal > 0) kbBoardSearchShowServerBanner(extraTotal, inComments, arr);
        else kbBoardSearchHideServerBanner();
    } catch (e) { /* no-op */ }
}

function kbBoardSearchShowServerBanner(extraTotal, inComments, results) {
    var b = document.getElementById("kb-board-search-banner");
    if (!b) return;
    var msg = "Найдено ещё " + extraTotal + " задач(и) вне текущего фильтра";
    if (inComments > 0) msg += " (в т.ч. в комментариях: " + inComments + ")";
    msg += '. <a href="#" id="kb-board-search-banner-link">Показать списком</a>';
    b.innerHTML = msg;
    b.style.display = "block";
    var lnk = document.getElementById("kb-board-search-banner-link");
    if (lnk) lnk.onclick = function () {
        kbBoardSearchOpenList(results);
        return false;
    };
}

function kbBoardSearchHideServerBanner() {
    var b = document.getElementById("kb-board-search-banner");
    if (b) b.style.display = "none";
}

function kbBoardSearchOpenList(results) {
    var modal = document.getElementById("kb-search-list-modal");
    var body  = document.getElementById("kb-search-list-body");
    if (!modal || !body) return;
    var html = "";
    var labels = ["Надо сделать", "В работе", "Ожидание", "Готово"];
    for (var i = 0; i < results.length; i++) {
        var r = results[i];
        html += '<tr data-key="' + tcmChatEsc(r.nameKey) + '">'
              + '<td>' + tcmChatEsc(r.title || "") + '</td>'
              + '<td>' + tcmChatEsc(r.assigneeName || "") + '</td>'
              + '<td>' + labels[r.status] + '</td>'
              + '<td>' + (r.matchInComments ? '<span class="kb-search-tag-comm">в комментариях</span>' : '') + '</td>'
              + '</tr>';
    }
    body.innerHTML = html || "<tr><td colspan='4'>Ничего не найдено</td></tr>";
    modal.style.display = "block";

    // Делегируем клик по строке на открытие задачи
    body.onclick = function (ev) {
        var tr = ev.target;
        while (tr && tr.tagName && tr.tagName.toLowerCase() !== "tr") tr = tr.parentNode;
        if (!tr) return;
        var key = tr.getAttribute("data-key");
        if (!key) return;
        if (typeof kbBoardSearchCloseList === "function") kbBoardSearchCloseList();
        if (typeof tcmOpen === "function") tcmOpen(key);
    };
}

window.kbBoardSearchCloseList = function () {
    var modal = document.getElementById("kb-search-list-modal");
    if (modal) modal.style.display = "none";
};
```

### 4.6. Подвязка инициализации

В конец `kbInitHierarchy`:

```javascript
if (typeof kbBoardSearchInit === "function") kbBoardSearchInit();
```

---

## 5. HTML — что и где менять (`KanbanBoard_HTML.html`)

### 5.1. Поле поиска в верхней панели

В блок `kb-hier-panel` или сразу после него (~строки 50–60):

```html
<div id="kb-board-search-wrap" style="float:right; margin-right:16px; margin-top:8px;">
    <input type="text" id="kb-board-search" class="kb-board-search-input"
           placeholder="🔍 Поиск (Enter — комментарии)" autocomplete="off">
    <button type="button" id="kb-board-search-clear" class="kb-board-search-clear"
            title="Очистить">×</button>
</div>
<div id="kb-board-search-banner" class="kb-board-search-banner" style="display:none;"></div>
```

### 5.2. Модалка списка результатов

Перед `</body>` или рядом с `tcm-modal`:

```html
<div id="kb-search-list-modal" class="kb-modal" style="display:none;">
    <div class="kb-modal-header">
        Результаты поиска
        <button type="button" class="kb-modal-close"
                onclick="kbBoardSearchCloseList(); return false;">×</button>
    </div>
    <div class="kb-modal-body">
        <table class="kb-search-list">
            <thead>
                <tr><th>Задача</th><th>Исполнитель</th><th>Статус</th><th></th></tr>
            </thead>
            <tbody id="kb-search-list-body"></tbody>
        </table>
    </div>
</div>
```

---

## 6. CSS — что и где менять (`kanban.css`)

```css
.kb-board-search-input {
    width: 240px;
    padding: 5px 28px 5px 10px;
    height: 30px;
    font-size: 12px;
    border: 1px solid #d1d5db;
    border-radius: 6px;
    background: #fff;
    color: #1f2937;
    box-sizing: border-box;
}
.kb-board-search-clear {
    position: relative;
    margin-left: -24px;
    background: transparent;
    border: 0;
    color: #6b7280;
    font-size: 16px;
    cursor: pointer;
    line-height: 1;
}
.kb-board-search-banner {
    margin: 6px 16px 0 16px;
    padding: 6px 10px;
    background: #fff7ed;
    border: 1px solid #fed7aa;
    color: #9a3412;
    border-radius: 6px;
    font-size: 12px;
}
.kb-board-search-banner a { color: #9a3412; text-decoration: underline; }

.kb-modal {
    position: fixed; z-index: 10040;
    left: 50%; top: 10%; transform: translateX(-50%);
    width: 720px; max-width: 90vw; max-height: 80vh;
    background: #fff; border: 1px solid #d1d5db; border-radius: 8px;
    box-shadow: 0 8px 32px rgba(0,0,0,.18);
    overflow: hidden; display: flex; flex-direction: column;
}
.kb-modal-header {
    padding: 10px 14px; background: #f3f4f6;
    border-bottom: 1px solid #d1d5db;
    font-weight: 600; display: flex;
    justify-content: space-between; align-items: center;
}
.kb-modal-close {
    background: transparent; border: 0; font-size: 18px;
    cursor: pointer; color: #4b5563;
}
.kb-modal-body { padding: 12px 14px; overflow: auto; }
.kb-search-list { width: 100%; border-collapse: collapse; font-size: 13px; }
.kb-search-list th, .kb-search-list td {
    padding: 6px 8px; border-bottom: 1px solid #e5e7eb;
}
.kb-search-list tbody tr { cursor: pointer; }
.kb-search-list tbody tr:hover { background: #f9fafb; }
.kb-search-tag-comm {
    display: inline-block;
    padding: 2px 6px;
    background: #dbeafe;
    color: #1e3a8a;
    border-radius: 3px;
    font-size: 11px;
}
```

---

## 7. Пошаговый план реализации (по 1 коммиту)

| # | Шаг | Файлы | Smoke |
|---|-----|-------|-------|
| 1 | Серверный `DoSearchTasks` + ветка `case "SearchTasks":` (включая `CommentsJSON`) | `SOYUZ_UPLOAD_KanbanScreen_script.cs` | `InvokeTemplate("SearchTasks","тест")` возвращает JSON-массив |
| 2 | HTML: поле поиска + кнопка очистки | `KanbanBoard_HTML.html`, `kanban.css` | Поле видно в верхней панели |
| 3 | JS: `kbBoardSearchOnInput` + `kbBoardSearchApplyClient` (мгновенный фильтр) + счётчики колонок | `kanban.js` | Ввод фильтрует видимые карточки, счётчики `vis / total` |
| 4 | JS: `kbBoardSearchApplyServer` + баннер «Найдено ещё N» с пометкой «в комментариях» | `kanban.js` | После 300мс / Enter показывается баннер |
| 5 | JS+HTML+CSS: модалка списка, открытие задачи по клику строки | `kanban.js`, `KanbanBoard_HTML.html`, `kanban.css` | Клик по «Показать списком» → модалка → клик по строке → Task modal |
| 6 | JS: связка с `Esc` и `Enter` в поле; интеграция в `kbInitHierarchy` | `kanban.js` | Esc очищает поле, Enter триггерит серверный поиск |
| 7 | Документация — `docs/01_*.md`, `docs/03_*.md`, `docs/05_*.md` | docs | — |
| 8 | Snapshot `Kanban Конфигурация-1.0.0.4.pmszcfg` | snapshot | Деплой |

---

## 8. Риски и технические ограничения

1. **IE11**: `String.prototype.includes` нет — используем `indexOf`. `setTimeout/clearTimeout`, `textContent`, `setAttribute` — есть.
2. **Транспорт `InvokeTemplate("SearchTasks", q)`**: один аргумент, но защита `replace(/\|/g, " ")` обязательна.
3. **DotLiquid не задействован.**
4. **Производительность `DoSearchTasks`**:
   - Линейный обход контейнера + по комментариям. На ~2000 задач — ориентировочно 200–400 мс.
   - Защита: `if (found >= 100) break;` + `q.Length < 2` ранний выход.
   - В Phase 2: переход на `SearchOperation` с `Contains` по нескольким полям одновременно.
5. **Нет совпадений ни в DOM, ни на сервере**: клиент скрывает все карточки + баннера нет. UX-подсказка «Ничего не найдено» — добавим в Phase 2 поверх board.
6. **Регрессия счётчика колонок**: при первой фильтрации сохраняем `data-orig`. После очистки — восстанавливаем. После `RefreshBoard` Liquid пересоздаёт DOM, `data-orig` пропадает (это OK).
7. **`Esc` в поле поиска** обрабатывается локально (в onkeydown). Глобальный `Esc`-стек закрытий из плана 02 не сработает, потому что фокус в input → blur. Это OK.
8. **Z-index конфликт**: модалка списка `z-index: 10040`. `tcm-modal` должен быть `>= 10050` — проверить в существующем CSS.
9. **XSS**: `JsonEscape` на сервере + `tcmChatEsc` на клиенте. Поле `data-key` экранируем.
10. **`obj.PropertyBag["KbViewMode"]`** в `DoSearchTasks` берём со screen-объекта. Это совпадает с `BeforeRender`.
11. **`CommentsJSON` toLowerInvariant() на каждом цикле** — может быть тяжеловато. Митигация: сначала проверять `baseBlob`, и только если нет — лезть в `CommentsJSON`.

---

## 9. Критерии приёмки

- [ ] В верхней панели доски виден инпут поиска со значком 🔍 и кнопкой `×`.
- [ ] Ввод подстроки ≥2 символов мгновенно скрывает не подходящие карточки и обновляет счётчик колонок в формате `vis / total`.
- [ ] По истечении 300 мс автоматически срабатывает серверный поиск (включая `CommentsJSON`).
- [ ] `Enter` в поле триггерит серверный поиск немедленно.
- [ ] Если найдено в комментариях задачи, не видимой в DOM — баннер «Найдено ещё N задач (в т.ч. в комментариях: M)».
- [ ] Клик по «Показать списком» открывает модалку с табличным списком, помечая «в комментариях».
- [ ] Клик по строке списка открывает Task modal соответствующей задачи.
- [ ] `Esc` в поле очищает фильтр.
- [ ] Поиск регистронезависимый, работает на кириллице.
- [ ] Документация обновлена.

---

## 10. Тестовые сценарии

### Сценарий 1 — мгновенный клиентский фильтр

1. Открыть доску с 30+ карточками.
2. Ввести `тест` → видны только карточки с подстрокой «тест» в любом из полей.
3. Счётчик колонки показывает `3 / 12`.

### Сценарий 2 — поиск по комментариям

1. У задачи T2 в комментариях есть слово «бюджет», но в title/details — нет.
2. Ввести `бюджет` → клиентский фильтр скрыл все карточки (нет совпадений в DOM).
3. Через 300 мс (или Enter) — баннер «Найдено ещё 1 задач (в т.ч. в комментариях: 1)».
4. Клик по «Показать списком» → строка с T2 + бейдж «в комментариях».
5. Клик по строке → открылся Task modal T2.

### Сценарий 3 — фильтр по исполнителю

1. Ввести фамилию `Иванов`.
2. Видны только карточки, где исполнитель — Иванов.

### Сценарий 4 — Enter и Esc

1. Ввести подстроку, нажать `Enter` — баннер обновился немедленно.
2. `Esc` в поле — фильтр сброшен, все карточки видны.

### Сценарий 5 — права scope

1. `regular` ищет подстроку, которая встречается в задачах других пользователей.
2. **Ожидаемо:** не найдено (scope ограничен своими).

### Сценарий 6 — комбинации с режимом

1. `admin` в режиме `dept` ищет задачу из другого отделения.
2. **Ожидаемо:** не находит.
3. Переключается на `all` → находит.

### Сценарий 7 — XSS

1. Ввести `<script>alert(1)</script>`.
2. Ничего не выполняется, поле и список выводят это как текст.

### Сценарий 8 — производительность

1. На контейнере ~500 задач, в каждой 5 комментариев → ~2500 текстовых блоков.
2. Поиск подстроки `а` (часто встречается) — ответ < 600 мс.
3. Защита `found >= 100` срабатывает.
