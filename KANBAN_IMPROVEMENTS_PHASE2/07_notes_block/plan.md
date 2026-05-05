# 07 — Блокнот / заметки внутри задачи (с историей)

> **Источник в backlog:** `KANBAN_IMPROVEMENTS_BACKLOG.md` §2.8.
> **Фича:** `notes_block`.
> **Приоритет:** P1 (Phase 2, задача №3).
> **Затронутые файлы:** `scripts/SOYUZ_UPLOAD_KanbanScreen_script.cs`, `scripts/kanban.js`, `scripts/KanbanBoard_HTML.html`, `scripts/kanban.css`.

---

## 1. Техническое описание задачи

### 1.1. Требования backlog

> - Отдельное большое текстовое поле «Заметки» или «Личный блокнот».
> - Сохранение истории изменений (кто и когда писал).

### 1.2. Архитектурное решение

Аналогично подзадачам (§2.7) используем **легковесный JSON в текстовом атрибуте** `NotesJSON`. **Стандартный RichText не используем**, потому что он не сохраняет версионирование по авторам в удобном для канбана виде.

Структура — **append-only массив записей**:

```json
[
  { "id": "n1", "text": "Согласован дедлайн с заказчиком", "author": "Иванов И.И.", "date": "2026-05-04T10:30:00" },
  { "id": "n2", "text": "Подключил третий стенд", "author": "Иванов И.И.", "date": "2026-05-04T15:20:00" },
  { "id": "n3", "text": "Уехал в командировку, продолжит Петров", "author": "Иванов И.И.", "date": "2026-05-05T09:00:00" }
]
```

Поля:
- `id` — `n` + sequential, генерируется на сервере.
- `text` — текст заметки (1–10 000 символов, переносы строк сохраняем).
- `author` — кто написал.
- `date` — когда написал (ISO `yyyy-MM-ddTHH:mm:ss`).

UI:
- В модалке `tcmOverlay` добавляем **новую вкладку** «Блокнот» (между «Чек-лист» и «Обсуждение»). Альтернативно — между «Обсуждение» и «История». Решение: **между «Чек-лист» и «Обсуждение»** — логически «Блокнот» это личная память, ближе к «Основное» и до «коллективного» обсуждения.
- Список заметок сверху (новые первыми) + поле ввода + кнопка «Добавить запись».
- Каждая запись: автор, дата, текст. **Редактирование/удаление недоступно** (заметки — append-only).

### 1.3. Серверные методы

| Метод | inputParams | Действие | Возврат |
|-------|-------------|----------|---------|
| `AddNote` | `taskKey\|text` | Добавить заметку с author=current, date=now | JSON созданной заметки |
| `GetNotes` | `taskKey` | Вернуть массив заметок (новые первыми) | JSON-массив |

> Никаких `EditNote` / `DeleteNote` — это **журнал**, не редактируемый список. Если пользователь хочет «исправить» — добавляет новую заметку с уточнением.

### 1.4. Что НЕ делаем

- Не используем RichText.
- Не редактируем и не удаляем заметки — append-only по требованию backlog «Сохранение истории изменений».
- Не делаем форматирование (bold/italic). Текст plain + переносы строк через `\n`.
- Не делаем поиск внутри блокнота (Phase 3+).

---

## 2. Затронутые файлы

| Путь | Что меняем |
|------|------------|
| `scripts/SOYUZ_UPLOAD_KanbanScreen_script.cs` | Хелперы `ParseNotes` / `SerializeNotes` / `GetNoteCount`. Invoke-методы `AddNote` / `GetNotes`. Передача счётчика заметок в Liquid (для бейджа карточки). |
| `scripts/kanban.js` | Блок `tcmNotes*`: `tcmNotesLoad`, `tcmNotesRender`, `tcmNotesAdd`. Подвязка к `tcmOpen`. |
| `scripts/KanbanBoard_HTML.html` | Новая вкладка `tcm-tab-notes` в модалке. Бейдж `.kb-notes-badge` на `.kb-card`. |
| `scripts/kanban.css` | Стили блокнота (запись, дата, автор, поле ввода). |

> **Конфигурация (pmszcfg):** добавить атрибут `NotesJSON` (тип Text, MaxLength 1 МБ) в InfoType `KanbanTask`, если его нет.

---

## 3. C# — что и где менять (`SOYUZ_UPLOAD_KanbanScreen_script.cs`)

### 3.1. Ветки в `Invoke`

Рядом с подзадачами (после `case "GetSubtasks":`):

```csharp
case "AddNote":  return DoAddNote( inputParams );
case "GetNotes": return DoGetNotes( inputParams );
```

### 3.2. Хелперы

```csharp
// ─── Парсинг JSON-массива заметок ─────────────────────────────────
private System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>>
    ParseNotes( string json )
{
    return ParseComments( json );  // тот же формат [{kv,kv}, ...]
}

private string SerializeNotes(
    System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string>> items )
{
    return SerializeComments( items );
}

private int GetNoteCount( InfoObject task )
{
    try
    {
        var json = task.GetString( "NotesJSON" ) ?? "";
        if( json.Length < 3 ) return 0;
        return ParseNotes( json ).Count;
    }
    catch { return 0; }
}
```

> Если `ParseComments` нельзя переиспользовать (например, он жёстко проверяет имена полей), клонировать в `ParseNotes` / `SerializeNotes` дословно с переименованием.

### 3.3. `DoAddNote`

> **Транзакции БД (обязательно).** В Soyuz-PLM любое изменение JSON-атрибута через `editable.Save()` должно быть обёрнуто в групповую операцию `Service.EnterNewGroupOperation()` + `Service.SaveChanges()`. Без обёртки в многопользовательской среде получим ошибки блокировки.

```csharp
// ─── AddNote ────────────────────────────────────────────────────────
// inputParams: "taskKey|text"
// Возвращает JSON созданной заметки или ERROR.
private object DoAddNote( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 2 );
    if( parts.Length < 2 ) return "ERROR:BadFormat";

    var taskKey = parts[0].Trim();
    var text    = parts[1];                  // не Trim — переносы строк могут быть значимы
    if( string.IsNullOrEmpty( taskKey ) ) return "ERROR:NoKey";
    if( string.IsNullOrEmpty( text.Trim() ) ) return "ERROR:EmptyText";
    if( text.Length > 10000 ) text = text.Substring( 0, 10000 );

    try
    {
        var task = ResolveTaskByKey( taskKey );
        if( task == null ) return "ERROR:NotFound";

        var editable = task.GetEditable();
        var json  = editable.GetString( "NotesJSON" ) ?? "";
        var items = ParseNotes( json );

        // Soft-limit размера: сумма длин text > 200 КБ → отказ
        long total = 0;
        for( int i = 0; i < items.Count; i++ )
        {
            string v;
            if( items[i].TryGetValue( "text", out v ) ) total += v.Length;
        }
        if( total + text.Length > 200000 ) return "ERROR:SizeLimit";

        // Генерация id
        int maxId = 0;
        for( int i = 0; i < items.Count; i++ )
        {
            string v;
            if( items[i].TryGetValue( "id", out v ) && v.StartsWith( "n" ) )
            {
                int n;
                if( int.TryParse( v.Substring( 1 ), out n ) && n > maxId ) maxId = n;
            }
        }
        var newId = "n" + (maxId + 1).ToString();

        var user = Service.GetCurrentUser();
        var now  = DateTime.Now.ToString( "yyyy-MM-ddTHH:mm:ss",
                       System.Globalization.CultureInfo.InvariantCulture );

        var item = new System.Collections.Generic.Dictionary<string, string>();
        item["id"]     = newId;
        item["text"]   = text;
        item["author"] = user.ToString() ?? "";
        item["date"]   = now;
        items.Add( item );

        // ─── Транзакция БД (обязательно) ───
        using( Service.EnterNewGroupOperation() )
        {
            editable["NotesJSON"] = SerializeNotes( items );
            editable.Save();
            Service.SaveChanges();
        }

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
        Service.HandleException( ex, "KanbanScreen.DoAddNote: " + ex.Message );
        return "ERROR:Internal";
    }
}
```

### 3.4. `DoGetNotes`

```csharp
// ─── GetNotes ───────────────────────────────────────────────────────
private object DoGetNotes( object inputParams )
{
    var taskKey = GetParamStr( inputParams ).Trim();
    if( string.IsNullOrEmpty( taskKey ) ) return "[]";
    try
    {
        var task = ResolveTaskByKey( taskKey );
        if( task == null ) return "[]";
        var json = task.GetString( "NotesJSON" ) ?? "";
        return string.IsNullOrEmpty( json ) ? "[]" : json;
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "KanbanScreen.DoGetNotes: " + ex.Message );
        return "[]";
    }
}
```

### 3.5. Передача счётчика в Liquid

В `BeforeRender` рядом с `subtaskTotal`:

```csharp
cardObj["noteCount"] = GetNoteCount( task );
```

---

## 4. JavaScript — что и где менять (`kanban.js`)

### 4.1. Состояние

```javascript
var _tcmNotes = { taskKey: "", items: [] };
```

### 4.2. Загрузка и рендер

В `tcmOpen` после загрузки подзадач:

```javascript
if (typeof tcmNotesLoad === "function") tcmNotesLoad(nameKey);
```

```javascript
window.tcmNotesLoad = function (nameKey) {
    _tcmNotes.taskKey = nameKey;
    _tcmNotes.items   = [];
    var safeKey = String(nameKey).replace(/\|/g, "");
    try {
        var raw = window.external.InvokeTemplate("GetNotes", safeKey);
        _tcmNotes.items = JSON.parse(String(raw || "[]"));
    } catch (e) { _tcmNotes.items = []; }
    tcmNotesRender();
};

window.tcmNotesRender = function () {
    var list = document.getElementById("tcm-notes-list");
    if (!list) return;
    var items = _tcmNotes.items || [];
    // Новые первыми
    var ordered = items.slice().reverse();
    var html = "";
    var i, it, dateLabel;
    for (i = 0; i < ordered.length; i++) {
        it = ordered[i];
        dateLabel = (it.date || "").replace("T", " ");
        html += '<div class="kb-note-item">'
              + '<div class="kb-note-meta">'
              + '<span class="kb-note-author">' + tcmChatEsc(it.author || "") + '</span>'
              + '<span class="kb-note-date">' + tcmChatEsc(dateLabel) + '</span>'
              + '</div>'
              + '<div class="kb-note-text">'
              + tcmChatEsc(it.text || "").replace(/\n/g, "<br>")
              + '</div>'
              + '</div>';
    }
    list.innerHTML = html || '<div class="kb-note-empty">Записей нет. Начните с первой.</div>';

    // Бейдж вкладки
    var badge = document.getElementById("tcm-tab-notes-badge");
    if (badge) {
        if (items.length > 0) {
            badge.innerHTML = String(items.length);
            badge.style.display = "";
        } else {
            badge.style.display = "none";
        }
    }
};
```

### 4.3. Добавление заметки

```javascript
window.tcmNotesAdd = function () {
    var ta = document.getElementById("tcm-notes-input");
    if (!ta) return;
    var text = (ta.value || "");
    if (!text.replace(/\s+/g, "")) return;   // пустой/пробельный — игнор
    text = text.replace(/\|/g, " ").substring(0, 10000);
    var safeKey = String(_tcmNotes.taskKey).replace(/\|/g, "");
    try {
        var raw = window.external.InvokeTemplate("AddNote", safeKey + "|" + text);
        if (String(raw || "").indexOf("ERROR") === 0) {
            if (String(raw).indexOf("SizeLimit") >= 0) {
                alert("Превышен лимит блокнота (200 КБ). Создайте новую задачу.");
            } else {
                alert("Ошибка добавления заметки: " + raw);
            }
            return;
        }
        var obj = JSON.parse(String(raw));
        _tcmNotes.items.push(obj);
        ta.value = "";
        tcmNotesRender();
    } catch (e) { /* no-op */ }
};
```

### 4.4. Подсказка о размере (опционально, soft-warning)

```javascript
function tcmNotesUpdateSize() {
    var items = _tcmNotes.items || [];
    var total = 0;
    var i;
    for (i = 0; i < items.length; i++) total += (items[i].text || "").length;
    var warn = document.getElementById("tcm-notes-size-warn");
    if (!warn) return;
    if (total > 150000) {
        warn.innerHTML = "Размер блокнота " + Math.round(total / 1024) + " КБ из 200 КБ";
        warn.style.display = "";
    } else {
        warn.style.display = "none";
    }
}
```

Вызывать из `tcmNotesRender` после установки `list.innerHTML`.

---

## 5. HTML — что и где менять (`KanbanBoard_HTML.html`)

### 5.1. Кнопка вкладки в `tcm-tabs`

```html
<div class="tcm-tabs">
    <div id="tab-btn-main"  class="tcm-tab active" onclick="tcmSwitchTab('main')">Основное</div>
    <div id="tab-btn-subt"  class="tcm-tab" onclick="tcmSwitchTab('subt')">Чек-лист
        <span id="tcm-tab-subt-badge" class="tcm-tab-badge" style="display:none;">0/0</span>
    </div>
    <div id="tab-btn-notes" class="tcm-tab" onclick="tcmSwitchTab('notes')">Блокнот
        <span id="tcm-tab-notes-badge" class="tcm-tab-badge" style="display:none;">0</span>
    </div>
    <div id="tab-btn-chat"  class="tcm-tab" onclick="tcmSwitchTab('chat')">Обсуждение
        <span id="tcm-tab-chat-badge" class="tcm-tab-badge" style="display:none;">0</span>
    </div>
    <div id="tab-btn-hist"  class="tcm-tab" onclick="tcmSwitchTab('hist')">История</div>
</div>
```

### 5.2. Контент вкладки

```html
<div id="tcm-tab-notes" class="tcm-tab-content">
    <div class="kb-notes-header">
        <span class="kb-notes-title">Блокнот задачи</span>
        <span class="kb-notes-hint">Append-only. Записи нельзя редактировать.</span>
    </div>
    <div class="kb-notes-list" id="tcm-notes-list"></div>
    <div class="kb-notes-add">
        <textarea id="tcm-notes-input" class="form-control"
                  placeholder="Что произошло по задаче?" rows="3" maxlength="10000"></textarea>
        <button type="button" class="tcm-btn tcm-btn-primary" onclick="tcmNotesAdd()">+ Добавить запись</button>
        <div id="tcm-notes-size-warn" class="kb-notes-size-warn" style="display:none;"></div>
    </div>
</div>
```

### 5.3. Бейдж на карточке

```liquid
{% if card.noteCount != 0 %}
<span class="kb-notes-badge" title="Заметок: {{ card.noteCount }}">📝 {{ card.noteCount }}</span>
{% endif %}
```

---

## 6. CSS — что и где менять (`kanban.css`)

```css
/* ─── Блокнот в модалке ─── */
.kb-notes-header {
    display: flex;
    justify-content: space-between;
    align-items: baseline;
    margin-bottom: 8px;
}
.kb-notes-title { font-weight: 600; font-size: 13px; color: #1f2937; }
.kb-notes-hint  { font-size: 11px; color: #9ca3af; }
.kb-notes-list  {
    max-height: 320px;
    overflow-y: auto;
    margin-bottom: 12px;
    border: 1px solid #e5e7eb;
    border-radius: 6px;
    background: #fafafa;
    padding: 8px;
}
.kb-note-empty { color: #9ca3af; font-size: 12px; padding: 8px; text-align: center; }
.kb-note-item {
    background: #fff;
    border: 1px solid #e5e7eb;
    border-radius: 6px;
    padding: 8px 10px;
    margin-bottom: 6px;
}
.kb-note-meta {
    display: flex;
    justify-content: space-between;
    margin-bottom: 4px;
    font-size: 11px;
    color: #6b7280;
}
.kb-note-author { font-weight: 600; color: #374151; }
.kb-note-date   { font-style: italic; }
.kb-note-text {
    font-size: 13px;
    color: #1f2937;
    word-wrap: break-word;
    white-space: pre-wrap;
}
.kb-notes-add {
    display: flex;
    flex-direction: column;
    gap: 6px;
}
.kb-notes-add textarea { resize: vertical; }
.kb-notes-size-warn {
    font-size: 11px;
    color: #b45309;
    background: #fef3c7;
    padding: 4px 8px;
    border-radius: 4px;
}

/* ─── Бейдж заметок на карточке ─── */
.kb-notes-badge {
    display: inline-block;
    padding: 2px 6px;
    margin-left: 4px;
    background: #fef3c7;
    color: #92400e;
    border-radius: 3px;
    font-size: 11px;
}
```

---

## 7. Пошаговый план реализации (атомарные коммиты)

| # | Шаг | Файлы | Smoke-тест | Сообщение коммита |
|---|-----|-------|------------|-------------------|
| 1 | Конфигурация: добавить атрибут `NotesJSON` в `KanbanTask` | `Kanban Конфигурация-1.0.0.5.pmszcfg` | Атрибут есть | `chore(kanban): pmszcfg add NotesJSON attribute` |
| 2 | C#: хелперы `ParseNotes` / `SerializeNotes` / `GetNoteCount` | `SOYUZ_UPLOAD_KanbanScreen_script.cs` | Сборка проходит | `feat(kanban): notes JSON helpers` |
| 3 | C#: `DoAddNote` + `DoGetNotes` + ветки в Invoke | `SOYUZ_UPLOAD_KanbanScreen_script.cs` | `InvokeTemplate("AddNote","KEY|тест")` возвращает JSON, `GetNotes` возвращает массив | `feat(kanban): server AddNote/GetNotes methods` |
| 4 | HTML+CSS: новая вкладка `tcm-tab-notes` | `KanbanBoard_HTML.html`, `kanban.css` | Вкладка видна, переключение работает | `feat(kanban): notes tab UI in modal` |
| 5 | JS: `tcmNotesLoad/Render/Add` + подвязка к `tcmOpen` | `kanban.js` | Запись сохраняется, появляется новой первой | `feat(kanban): notes client logic` |
| 6 | C#+HTML+CSS: проброс `noteCount` в Liquid + бейдж карточки | `SOYUZ_UPLOAD_KanbanScreen_script.cs`, `KanbanBoard_HTML.html`, `kanban.css` | На карточке `📝 3` | `feat(kanban): note count badge on card` |
| 7 | JS: предупреждение о размере 150 КБ | `kanban.js` | После 150 КБ блокнота — warning виден | `feat(kanban): notes size soft-warning` |
| 8 | Документация `docs/08_*.md` | docs | — | `docs(kanban): document notes feature` |

---

## 8. Возможные риски и технические ограничения

1. **Размер `NotesJSON`.** Серверный hard-limit 200 КБ суммарно по `text` (через `ERROR:SizeLimit`). При превышении — пользователь видит alert. UI-warning на 150 КБ.
2. **Конкурентные правки.** Метод `DoAddNote` обёрнут в `Service.EnterNewGroupOperation()` + `Service.SaveChanges()` — исключает ошибки блокировки при одновременных правках. Полная защита от потери записи (serial counter / ETag) — Phase 4+.
3. **Append-only.** В Phase 2 сознательно. Если пользователь сообщит «дайте удалить» — отдельная задача в Phase 3 с soft-delete.
4. **Переносы строк.** Сохраняем `\n` в JSON через `JsonEscape`. На клиенте — `replace(/\n/g, "<br>")` после `tcmChatEsc`. Проверка XSS: `<` в тексте → `&lt;`, потом `<br>` встаёт корректно.
5. **IE11.** `Array.prototype.slice().reverse()` поддерживается. `replace` тоже.
6. **Производительность.** При 100+ заметках в одной задаче рендер `~100 ms`. Приемлемо. Для 1000+ — Phase 3 виртуализация списка.
7. **`tcmChatEsc` существование.** Используется в `kanban.js` для комментариев. Должна быть доступна. Если её нет в области видимости — добавить простой эскейпер прямо рядом.
8. **Защита `|`.** В text может быть `|` — заменяем на пробел перед отправкой.
9. **DotLiquid `card.noteCount != 0`.** Должен быть `int`. Проверить.
10. **Бейдж карточки vs `subtaskTotal`.** Не конфликтуют — разные классы (`.kb-notes-badge` vs `.kb-subt-badge`), разные иконки (📝 vs ☑).

---

## 9. Критерии приёмки

- [ ] В модалке задачи видна вкладка «Блокнот» между «Чек-лист» и «Обсуждение».
- [ ] Список заметок отображается, новые первыми.
- [ ] Каждая запись показывает автора, дату-время, текст с сохранением переносов строк.
- [ ] Кнопка «+ Добавить запись» сохраняет заметку и сбрасывает поле ввода.
- [ ] При превышении 200 КБ суммарного текста — `ERROR:SizeLimit` + alert «Превышен лимит блокнота».
- [ ] При 150+ КБ — soft-warning виден.
- [ ] Заметки **нельзя** редактировать или удалять (нет UI-элементов для этого).
- [ ] На карточке доски виден бейдж `📝 N` если заметок > 0.
- [ ] Бейдж вкладки «Блокнот» показывает количество заметок.
- [ ] Документация `docs/08_*.md` обновлена.

---

## 10. Тестовые сценарии

### Сценарий 1 — добавление и порядок

1. Открыть задачу. Вкладка «Блокнот» пуста.
2. Ввести «Запись 1», «Добавить» → запись появилась.
3. Ввести «Запись 2», «Добавить» → запись 2 сверху, запись 1 ниже.
4. Бейдж вкладки `2`. Бейдж карточки `📝 2`.

### Сценарий 2 — переносы строк

1. Ввести `Строка1\nСтрока2` → в списке отображается с переносом.

### Сценарий 3 — спецсимволы

1. Ввести `<script>alert(1)</script>` → отображается как текст, скрипт не выполняется.

### Сценарий 4 — большая запись

1. Вставить 12 000 символов → сервер обрезает до 10 000.

### Сценарий 5 — размер блокнота

1. Накопить заметок на 160 КБ → виден soft-warning.
2. Накопить 200 КБ → попытка добавить → `ERROR:SizeLimit`, alert.

### Сценарий 6 — закрытие и открытие

1. Добавить 3 заметки.
2. Закрыть модалку, открыть снова → видны те же 3 заметки, в том же порядке.

### Сценарий 7 — разные авторы

1. Пользователь A добавил «От A».
2. Пользователь B открыл задачу → видит запись с автором A.
3. B добавил «От B» → у обеих свои авторы и даты.

### Сценарий 8 — пустой текст

1. Ввести только пробелы / переносы → ничего не сохранено.

### Сценарий 9 — бейдж на карточке после refresh

1. Добавить заметку.
2. Закрыть модалку, нажать F5 → бейдж `📝 1` на карточке.

### Сценарий 10 — нет доступа к редактированию

1. Открыть «Блокнот» — убедиться, что нет кнопок «Редактировать» / «Удалить» возле записей. Только append.
