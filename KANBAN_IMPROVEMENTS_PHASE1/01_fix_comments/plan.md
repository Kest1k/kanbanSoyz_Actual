# 01 — Починить комментарии в карточке задачи

> **Источник в backlog:** `KANBAN_IMPROVEMENTS_BACKLOG.md` §1.1.
> **Фича:** `fix_comments`
> **Приоритет:** P0 (Phase 1)
> **Затронутые файлы:** `scripts/kanban.js`, `scripts/SOYUZ_UPLOAD_KanbanScreen_script.cs`, `scripts/SOYUZ_UPLOAD_KanbanTask_script.cs`, `scripts/KanbanBoard_HTML.html`, `scripts/kanban.css`, опционально `scripts/ExclamationKanban.cs`.

---

## 1. Техническое описание (4 подпункта из backlog)

### 1.1.A. Кавычки в комментариях отображаются как `&quot;`

**Причина:** двойное HTML-экранирование. Сервер делает `HtmlEnc(text)` в `DoAddComment` ([SOYUZ_UPLOAD_KanbanScreen_script.cs:2613](scripts/SOYUZ_UPLOAD_KanbanScreen_script.cs#L2613)), клиент потом ещё раз делает `tcmChatEsc(c.text)` в `tcmRenderComment` ([kanban.js:459](scripts/kanban.js#L459)). В БД уже лежат записи, прошедшие через `HtmlEnc` — у них на чтении одно `tcmChatEsc` превращает `&quot;` в `&amp;quot;`.

### 1.1.B. Переносы строк (`\n`) не работают, текст «слипается»

**Причина:** в `<div class="tcm-msg-text">` нет `white-space: pre-wrap`, а клиентский экран не превращает `\n` в `<br>`.

### 1.1.C. Возможность редактирования своих комментариев

**Текущий статус:** есть `tcmDeleteComment` ([kanban.js:511](scripts/kanban.js#L511)) и серверный `DoDeleteComment` ([SOYUZ_UPLOAD_KanbanScreen_script.cs:2694](scripts/SOYUZ_UPLOAD_KanbanScreen_script.cs#L2694)), но нет редактирования. Нужно: иконка-карандашик у своих комментариев, inline-редактор, серверный метод `DoEditComment`.

### 1.1.D. Exclamation-уведомления о новом комментарии

**Текущий статус:** `OnBeforeSave` ([SOYUZ_UPLOAD_KanbanTask_script.cs:12](scripts/SOYUZ_UPLOAD_KanbanTask_script.cs#L12)) рассылает Exclamation **только при смене Assignee**. Комментарий через `DoAddComment` сохраняется отдельно (`task.Save()` строка 2637) и не порождает уведомление.

**Кому шлём:**
- Если автор комментария — **исполнитель** → уведомление **создателю задачи** (постановщику).
- Если автор комментария — **создатель задачи** → уведомление **исполнителю**.
- Если автор — **посторонний** (с правами админа) → уведомление и автору задачи, и исполнителю.
- Если автор пишет в свою же задачу (создатель == исполнитель) — не уведомляем.

Используем тот же шаблон `WorkLoads\BASIC\Message\ExclamationKanban`, что и в `SOYUZ_UPLOAD_KanbanTask_script.cs`.

---

## 2. Затронутые файлы (точные относительные пути)

| Путь | Что меняем |
|------|------------|
| `scripts/SOYUZ_UPLOAD_KanbanScreen_script.cs` | `DoAddComment`, `DoGetComments`, новый `DoEditComment`, новый `SendCommentNotification`, новый `DoGetCardCommentCount`, ветка `case "EditComment":` в `Invoke` |
| `scripts/kanban.js` | `tcmRenderComment`, `tcmChatEsc`, `tcmSendComment`, `tcmDeleteComment`, новый `tcmEditComment`, новый `tcmSyncCardCommentCount`, новый `tcmFormatCommentText`, новый `tcmConfirmToast` |
| `scripts/SOYUZ_UPLOAD_KanbanTask_script.cs` | без структурных изменений; справочно — переиспользуем `MSG_TEMPLATE_PATH` как константу. Уведомление о новом комментарии шлём из `SOYUZ_UPLOAD_KanbanScreen_script.cs` напрямую (см. §3.5) |
| `scripts/KanbanBoard_HTML.html` | блок чата (~строки 580–600): добавить `id` на кнопку отправки, ничего не ломать |
| `scripts/kanban.css` | `.tcm-msg-text { white-space: pre-wrap; word-break: break-word; }`, стили `.tcm-msg-edit-btn`, `.tcm-msg-edit-area`, `.tcm-confirm-toast` |
| `docs/01_KanbanScreen_SERVER_SCRIPT.md` | обновить контракт `AddComment`, описать `EditComment`, `GetCardCommentCount` |
| `docs/03_KANBAN_JS_CLIENT_LOGIC.md` | новый раздел «редактирование комментария» |

---

## 3. C# — что и где менять (`SOYUZ_UPLOAD_KanbanScreen_script.cs`)

### 3.1. `DoAddComment` (строки ~2599–2647)

Удалить серверное HTML-экранирование, заменить на минимально-инвазивную нейтрализацию:

```csharp
if( text.Length > 2000 ) text = text.Substring( 0, 2000 );
text = NormalizeCommentText( text );   // вместо HtmlEnc(text)
```

Добавить рядом с `JsonEscape` приватный метод:

```csharp
// Нейтрализация без агрессивного HtmlEnc:
//   '<' и '>' заменяем на типографические аналоги, чтобы исключить XSS
//   и при этом не плодить «&quot;» (кавычки/амперсанд клиент сам экранирует).
private string NormalizeCommentText( string s )
{
    if( string.IsNullOrEmpty( s ) ) return "";
    return s.Replace( "<", "‹" ).Replace( ">", "›" );
}
```

### 3.2. `DoGetComments` (строки ~2649–2692) — обратная совместимость

Перед сериализацией прогнать каждый текст через `DecodeLegacyHtmlEnc`, чтобы старые записи (где сидит `&quot;`) показывались корректно:

```csharp
string aText = "";
items[i].TryGetValue( "text", out aText );
aText = DecodeLegacyHtmlEnc( aText ?? "" );  // ← новое
```

```csharp
private string DecodeLegacyHtmlEnc( string s )
{
    if( string.IsNullOrEmpty( s ) ) return "";
    return s.Replace( "&quot;", "\"" )
            .Replace( "&#39;",  "'"  )
            .Replace( "&lt;",   "‹"  )
            .Replace( "&gt;",   "›"  )
            .Replace( "&amp;",  "&"  );
}
```

> Безопасно: на клиенте всё равно прогоняем через `tcmChatEsc`. Декодирование только при чтении, в БД не пишем.

### 3.3. Новый метод `DoEditComment`

Ветка в `Invoke` (рядом с `DeleteComment`, ~строка 152):

```csharp
case "EditComment": return DoEditComment( inputParams );
```

Реализация (рядом с `DoDeleteComment`):

```csharp
// inputParams: "taskKey|index|newText"
private object DoEditComment( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 3 );
    if( parts.Length < 3 ) return "ERROR:BadFormat";

    var taskKey = parts[0].Trim();
    int index;
    if( !int.TryParse( parts[1].Trim(), out index ) ) return "ERROR:BadIndex";

    var newText = parts[2];
    if( string.IsNullOrEmpty( newText ) ) return "ERROR:EmptyText";
    if( newText.Length > 2000 ) newText = newText.Substring( 0, 2000 );
    newText = NormalizeCommentText( newText );

    var task = GetTaskByKeyOrNull( taskKey );
    if( task == null ) return "ERROR:TaskNotFound";

    var json  = task.GetString( "CommentsJSON" ) ?? "";
    var items = ParseComments( json );
    if( index < 0 || index >= items.Count ) return "ERROR:IndexOutOfRange";

    var currentUser = Service.GetCurrentUser();
    var myKey = currentUser != null
              ? (string.IsNullOrEmpty( currentUser.NameKey ) ? currentUser.AccountId : currentUser.NameKey)
              : "";
    string commentAuthor = "";
    items[index].TryGetValue( "author", out commentAuthor );
    if( commentAuthor != myKey ) return "ERROR:NotOwner";

    items[index]["text"] = newText;
    items[index]["editedAt"] = DateTime.Now.ToString( "dd.MM.yyyy HH:mm" );

    task["CommentsJSON"] = SerializeComments( items );
    task.Save();

    return "{\"text\":\""     + JsonEscape( newText ) + "\","
         + "\"editedAt\":\""  + JsonEscape( items[index]["editedAt"] ) + "\","
         + "\"index\":"       + index + "}";
}
```

### 3.4. Сериализация поля `editedAt`

В `SerializeComments` дописать сохранение `editedAt`. В `DoGetComments` — добавить поле в JSON:

```csharp
string aEdited = "";
items[i].TryGetValue( "editedAt", out aEdited );
sb.Append( "...,\"editedAt\":\"" + JsonEscape( aEdited ?? "" ) + "\",..." );
```

### 3.5. Уведомление о новом комментарии

В конце `DoAddComment`, **после** `task.Save()` и **перед** возвратом JSON, добавить:

```csharp
try { SendCommentNotification( task, currentUser, text ); }
catch( Exception ex ) { Service.HandleException( ex, "AddComment.notify: " + ex.Message ); }
```

Новый метод (рядом с `DoAddComment`):

```csharp
private const string COMMENT_MSG_TEMPLATE_PATH = @"WorkLoads\BASIC\Message\ExclamationKanban";

private void SendCommentNotification( InfoObject task, User author, string text )
{
    if( task == null || author == null ) return;

    var assignee = task.GetUser( "Assignee" );
    var creatorKey = task.GetString( "Creator" ) ?? "";

    var authorKey = string.IsNullOrEmpty( author.NameKey )
                  ? author.AccountId
                  : author.NameKey;

    // Строим список адресатов: исполнитель и/или создатель, исключая автора
    var recipients = new System.Collections.Generic.List<User>();
    if( assignee != null && assignee.Id != author.Id )
        recipients.Add( assignee );

    if( !string.IsNullOrEmpty( creatorKey ) && creatorKey != authorKey )
    {
        var creator = TryFindUserByKey( creatorKey );  // helper, см. ниже
        if( creator != null && creator.Id != author.Id )
        {
            bool already = false;
            for( int i = 0; i < recipients.Count; i++ )
                if( recipients[i].Id == creator.Id ) { already = true; break; }
            if( !already ) recipients.Add( creator );
        }
    }

    if( recipients.Count == 0 ) return;

    var msgTemplate = Service.GetTemplate( COMMENT_MSG_TEMPLATE_PATH );
    if( msgTemplate == null ) return;

    var taskName = task.GetString( "TaskName" ) ?? task.ToString();
    var preview  = TruncDesc( text );
    var subject  = "Новый комментарий в задаче «" + taskName + "»: " + preview;

    foreach( var u in recipients )
    {
        try
        {
            var w = new WorkItem( msgTemplate, u );
            w[ "Subject" ] = subject;
            try { w[ "SilentMode" ] = true; } catch { }
            try { w.MarkAsViewedBy( u ); } catch { }
            w.StatusOperation = WorkItemBase.StatusEnum.Sent;
            try { w.MarkAsViewedBy( u ); } catch { }
        }
        catch( Exception ex )
        {
            Service.HandleException( ex, "SendCommentNotification per-recipient: " + ex.Message );
        }
    }
}

// Вспомогательный поиск User по NameKey/AccountId.
// Совместим с существующей логикой Creator-ключа.
private User TryFindUserByKey( string key )
{
    if( string.IsNullOrEmpty( key ) ) return null;
    try
    {
        foreach( var u in Service.GetAllUsers() )
        {
            var k = string.IsNullOrEmpty( u.NameKey ) ? u.AccountId : u.NameKey;
            if( k == key ) return u;
        }
    }
    catch { }
    return null;
}
```

> ⚠ Метод `Service.GetAllUsers()` — это **гипотетическое** API. Если такого нет в Soyuz-PLM (BIS v3) — ищем через существующий механизм, которым уже пользуется `DoGetHierarchyInfo` для перебора пользователей. **На этапе реализации** обязательно сверить с пользователем / документацией. В качестве fallback можно оставить уведомление **только исполнителю** (для него `User`-объект уже под рукой), а пункт «уведомление создателю» — вынести следующим коммитом, когда подтвердим API.

### 3.6. Новый метод `DoGetCardCommentCount`

Ветка в `Invoke`:

```csharp
case "GetCardCommentCount": return DoGetCardCommentCount( inputParams );
```

```csharp
private object DoGetCardCommentCount( object inputParams )
{
    var nameKey = GetParamStr( inputParams );
    var task    = GetTaskByKeyOrNull( nameKey );
    if( task == null ) return "0";
    return GetCommentCount( task ).ToString();
}
```

---

## 4. JavaScript — что и где менять (`kanban.js`)

### 4.1. `tcmRenderComment` (~444–463)

Заменить рендер текста и блока действий:

```javascript
function tcmRenderComment(c) {
    var div = document.createElement("div");
    div.className = "tcm-msg-item" + (c.isMine ? " tcm-msg-mine" : "");
    div.setAttribute("data-idx", c.index);

    var actions = "";
    if (c.isMine) {
        actions =
            '<button class="tcm-msg-edit-btn" onclick="tcmEditComment(' + c.index + '); return false;" title="Редактировать">&#9998;</button>' +
            '<button class="tcm-msg-del-btn"  onclick="tcmDeleteComment(' + c.index + '); return false;" title="Удалить">&times;</button>';
    }

    var editedMark = "";
    if (c.editedAt) {
        editedMark = ' <span class="tcm-msg-edited" title="Отредактировано ' + tcmChatEsc(c.editedAt) + '">(изм.)</span>';
    }

    div.innerHTML =
        '<div class="tcm-msg-header">' +
        '<span class="tcm-msg-avatar" title="' + tcmChatEsc(c.authorName) + '">' + tcmChatEsc(c.initials || "") + '</span>' +
        '<span class="tcm-msg-author">' + tcmChatEsc(c.authorName) + '</span>' +
        '<span class="tcm-msg-time">' + tcmChatEsc(c.date) + editedMark + '</span>' +
        '</div>' +
        '<div class="tcm-msg-text">' + tcmFormatCommentText(c.text) + '</div>' +
        actions;

    return div;
}

function tcmFormatCommentText(s) {
    if (!s) return "";
    var safe = tcmChatEsc(s);
    safe = safe.replace(/\r\n/g, "\n").replace(/\n/g, "<br>");
    return safe;
}
```

### 4.2. `tcmEditComment` — новый

```javascript
window.tcmEditComment = function (index) {
    if (!_tcmData || !_tcmData.nameKey) return;

    var item = document.querySelector('.tcm-msg-item[data-idx="' + index + '"]');
    if (!item) return;

    var textEl = item.querySelector(".tcm-msg-text");
    if (!textEl) return;

    // Достаём «сырой» текст (с <br> → \n)
    var html = textEl.innerHTML;
    var raw  = html.replace(/<br\s*\/?>/gi, "\n");
    var tmp  = document.createElement("div");
    tmp.innerHTML = raw;
    var plain = tmp.textContent || tmp.innerText || "";

    var edit = document.createElement("div");
    edit.className = "tcm-msg-edit-area";
    edit.innerHTML =
        '<textarea class="tcm-msg-edit-input" maxlength="2000"></textarea>' +
        '<div class="tcm-msg-edit-actions">' +
        '<button type="button" class="tcm-btn tcm-btn-primary" data-act="save">Сохранить</button>' +
        '<button type="button" class="tcm-btn" data-act="cancel">Отмена</button>' +
        '</div>';
    textEl.style.display = "none";
    var existed = item.querySelector(".tcm-msg-edit-area");
    if (existed) item.removeChild(existed);
    item.appendChild(edit);

    var ta = edit.querySelector(".tcm-msg-edit-input");
    ta.value = plain;
    ta.focus();

    edit.querySelector('[data-act="cancel"]').onclick = function () {
        item.removeChild(edit);
        textEl.style.display = "";
    };

    edit.querySelector('[data-act="save"]').onclick = function () {
        var newText = ta.value.replace(/^\s+|\s+$/g, "");
        if (!newText) { alert("Текст не может быть пустым"); return; }
        var safe = newText.replace(/\|/g, " ");   // защита транспорта
        try {
            var res = window.external.InvokeTemplate(
                "EditComment", _tcmData.nameKey + "|" + index + "|" + safe);
            var s = String(res);
            if (s.indexOf("ERROR") === 0) {
                alert("Ошибка: " + s);
                return;
            }
            var parsed = JSON.parse(s);
            textEl.innerHTML = tcmFormatCommentText(parsed.text);
            textEl.style.display = "";
            item.removeChild(edit);

            // Метка «(изм.)» в шапке
            var time = item.querySelector(".tcm-msg-time");
            if (time && parsed.editedAt && time.innerHTML.indexOf("(изм.)") < 0) {
                time.innerHTML += ' <span class="tcm-msg-edited" title="Отредактировано ' +
                                  tcmChatEsc(parsed.editedAt) + '">(изм.)</span>';
            }
        } catch (e) {
            alert("Ошибка: " + (e.message || e));
        }
    };
};
```

### 4.3. `tcmSendComment` — добавить синхронизацию счётчика карточки

После успешного `appendChild`:

```javascript
tcmSyncCardCommentCount(_tcmData.nameKey, _tcmData.id);
```

### 4.4. `tcmDeleteComment` — стилизованный confirm + sync счётчика

```javascript
window.tcmDeleteComment = function (index) {
    if (!_tcmData || !_tcmData.nameKey) return;
    tcmConfirmToast("Удалить комментарий?", function () {
        try {
            var res = window.external.InvokeTemplate(
                "DeleteComment", _tcmData.nameKey + "|" + index);
            var s = String(res);
            if (s === "OK") {
                tcmLoadComments(_tcmData);
                tcmSyncCardCommentCount(_tcmData.nameKey, _tcmData.id);
            } else if (s === "ERROR:NotOwner") {
                tcmShowMsg("Можно удалять только свои комментарии");
            } else {
                tcmShowMsg("Ошибка: " + s);
            }
        } catch (e) {
            tcmShowMsg("Ошибка: " + (e.message || e));
        }
    });
};
```

### 4.5. Helpers `tcmConfirmToast` и `tcmSyncCardCommentCount`

```javascript
function tcmConfirmToast(text, onConfirm) {
    var ov = document.getElementById("tcm-confirm-toast");
    if (!ov) {
        ov = document.createElement("div");
        ov.id = "tcm-confirm-toast";
        ov.className = "tcm-confirm-toast";
        ov.innerHTML =
            '<div class="tcm-confirm-text"></div>' +
            '<div class="tcm-confirm-actions">' +
            '<button type="button" class="tcm-btn tcm-btn-primary" data-act="ok">Удалить</button>' +
            '<button type="button" class="tcm-btn" data-act="cancel">Отмена</button>' +
            '</div>';
        document.body.appendChild(ov);
    }
    ov.querySelector(".tcm-confirm-text").innerHTML = tcmChatEsc(text);
    ov.style.display = "block";

    function close() { ov.style.display = "none"; }
    ov.querySelector('[data-act="ok"]').onclick = function () {
        close();
        if (typeof onConfirm === "function") onConfirm();
    };
    ov.querySelector('[data-act="cancel"]').onclick = close;
}

function tcmSyncCardCommentCount(nameKey, id) {
    try {
        var n = parseInt(String(window.external.InvokeTemplate(
            "GetCardCommentCount", nameKey)), 10) || 0;
        var card = document.getElementById("kbc_" + id);
        if (!card) return;
        var meta = card.querySelector(".kb-card-meta");
        if (!meta) return;
        var badge = meta.querySelector(".kb-comment-badge");
        if (n > 0) {
            if (!badge) {
                badge = document.createElement("span");
                badge.className = "kb-comment-badge";
                meta.appendChild(badge);
            }
            badge.title = "Комментарии: " + n;
            badge.innerHTML = '<i class="fa fa-comment-o"></i> ' + n;
        } else if (badge) {
            meta.removeChild(badge);
        }
    } catch (e) { /* no-op */ }
}
```

---

## 5. HTML — что и где менять (`KanbanBoard_HTML.html`)

В блоке чата (~строка 580):

- Кнопке отправки добавить `id="tcm-chat-send-btn"` (для будущих хоткеев из плана 02).
- Никаких других структурных изменений не требуется: редактирование разворачивается inline в JS.

---

## 6. CSS — что и где менять (`kanban.css`)

```css
.tcm-msg-text {
    white-space: pre-wrap;
    word-break: break-word;
    overflow-wrap: anywhere;
}
.tcm-msg-edited {
    color: #6b7280;
    font-style: italic;
    font-size: 11px;
    margin-left: 4px;
}
.tcm-msg-edit-btn {
    border: 0;
    background: transparent;
    color: #4a6fa5;
    cursor: pointer;
    margin-left: 4px;
    font-size: 12px;
}
.tcm-msg-edit-area {
    margin-top: 6px;
}
.tcm-msg-edit-input {
    width: 100%;
    min-height: 70px;
    box-sizing: border-box;
    border: 1px solid #d1d5db;
    border-radius: 6px;
    padding: 6px 8px;
    font: 12px/1.4 inherit;
    resize: vertical;
}
.tcm-msg-edit-actions {
    margin-top: 4px;
    text-align: right;
}
.tcm-msg-edit-actions .tcm-btn {
    margin-left: 6px;
}
.tcm-confirm-toast {
    display: none;
    position: fixed;
    z-index: 10050;
    left: 50%;
    top: 30%;
    transform: translateX(-50%);
    min-width: 280px;
    padding: 16px 18px;
    background: #fff;
    border: 1px solid #d1d5db;
    border-radius: 8px;
    box-shadow: 0 6px 24px rgba(0, 0, 0, .18);
    font-size: 13px;
    color: #1f2937;
}
.tcm-confirm-text { margin-bottom: 10px; }
.tcm-confirm-actions { text-align: right; }
.tcm-confirm-actions .tcm-btn { margin-left: 6px; }
```

---

## 7. Пошаговый план реализации (по 1 коммиту)

| # | Шаг | Файлы | Smoke |
|---|-----|-------|-------|
| 1 | Удалить `HtmlEnc` в `DoAddComment`, ввести `NormalizeCommentText`, добавить `DecodeLegacyHtmlEnc` в `DoGetComments` | `SOYUZ_UPLOAD_KanbanScreen_script.cs` | Старые комментарии без `&quot;`, новые комментарии с `<>"&` отображаются корректно |
| 2 | Добавить серверный `DoEditComment` + ветку в `Invoke` | `SOYUZ_UPLOAD_KanbanScreen_script.cs` | `InvokeTemplate("EditComment", "k\|0\|new")` возвращает JSON |
| 3 | Добавить серверный `DoGetCardCommentCount` + ветку | `SOYUZ_UPLOAD_KanbanScreen_script.cs` | Метод доступен |
| 4 | Клиент: `tcmFormatCommentText`, `tcmRenderComment` с editedAt, CSS `pre-wrap` | `kanban.js`, `kanban.css` | Перенос строки сохраняется, кавычки чистые |
| 5 | Клиент: `tcmEditComment`, кнопка-карандашик у своих комментариев | `kanban.js`, `kanban.css` | Редактирование своего комментария работает, видна метка «(изм.)» |
| 6 | Клиент: `tcmConfirmToast` вместо `confirm`, `tcmSyncCardCommentCount` после Send/Delete/Edit | `kanban.js`, `kanban.css` | Бейджик карточки обновляется без `RefreshBoard`, стилизованное подтверждение |
| 7 | Серверный `SendCommentNotification` (только исполнителю — fallback) | `SOYUZ_UPLOAD_KanbanScreen_script.cs` | Постановщик пишет комментарий — у исполнителя всплывает Exclamation |
| 8 | Расширить уведомление до создателя задачи (после подтверждения API `Service.GetAllUsers` или эквивалента) | `SOYUZ_UPLOAD_KanbanScreen_script.cs` | Исполнитель пишет — у создателя приходит уведомление |
| 9 | Документация | `docs/01_*.md`, `docs/03_*.md` | — |
| 10 | Snapshot конфигурации | `Kanban Конфигурация-1.0.0.4.pmszcfg` | Деплой на тестовый сервер |

---

## 8. Риски и технические ограничения

1. **Двойной escape «прошлых» комментариев** — митигация в `DoGetComments` через `DecodeLegacyHtmlEnc` (только при чтении).
2. **`editedAt` в JSON** — поле опциональное; старые записи без него корректно сериализуются (пустая строка).
3. **`Service.GetAllUsers()`** — гипотетическое API. Перед коммитом 8 сверяем с пользователем/доками. Fallback — уведомлять только исполнителю.
4. **Атомарность редактирования** — `task.Save()` в `DoEditComment` создаёт короткую транзакцию. Конкурентное редактирование того же комментария двумя сессиями допустимо (последний победил).
5. **IE11**: `replace(/\n/g, "<br>")`, `setAttribute`, `querySelector` — всё работает.
6. **DotLiquid** — только числовое сравнение `{% if t.commentCount > 0 %}`, не трогаем.
7. **`InvokeTemplate("EditComment", "k|i|t")`** — текст комментария может содержать `|`. Защита: `replace(/\|/g, " ")` на клиенте перед отправкой. На сервере `ParsePipeArgs(raw, 3)` — третий аргумент берётся как «всё остальное» (надо проверить, что эта функция корректно склеивает хвост с любыми внутренними `|`).
8. **WorkItem-уведомление** — переиспользуем шаблон `WorkLoads\BASIC\Message\ExclamationKanban`. Если не существует — `Service.GetTemplate` вернёт null, метод тихо завершится (см. `SOYUZ_UPLOAD_KanbanTask_script.cs:56`).
9. **Спам уведомлений** — пользователь может настучать 10 комментариев подряд. Митигация в Phase 2: дросселирование (1 уведомление в 5 минут на одну пару `task × recipient`).
10. **`SilentMode`** — выставляем `true`, чтобы не дублировался штатный pop-up. Visualization через `ExclamationKanban.OnUpdated` уже работает (см. `ExclamationKanban.cs`).

---

## 9. Критерии приёмки

- [ ] Новый комментарий с `<`, `>`, `&`, `"` отображается без `&amp;quot;`.
- [ ] Старые комментарии (записанные с `HtmlEnc`) показываются корректно.
- [ ] Перенос строки (`Shift+Enter`) сохраняется и виден.
- [ ] У своих комментариев виден карандашик; клик по нему открывает inline-редактор.
- [ ] После «Сохранить» текст обновляется, появляется метка «(изм.)» с tooltip-датой.
- [ ] Чужие комментарии не имеют ни карандашика, ни крестика.
- [ ] Подтверждение удаления — стилизованный toast.
- [ ] При попытке (через консоль) редактировать чужой комментарий сервер возвращает `ERROR:NotOwner`.
- [ ] Счётчик комментариев на карточке обновляется без `RefreshBoard` после Send / Delete / Edit (Edit не меняет счётчик — но проверяем стабильность).
- [ ] При добавлении комментария исполнителю (если автор не он) приходит Exclamation-уведомление.
- [ ] Документация обновлена.

---

## 10. Тестовые сценарии

> Тестовый стенд: пользователи `headOfDept` (постановщик) и `regular` (исполнитель), задача T-EDIT-1, обоим выдан доступ.

### Сценарий 1 — экранирование

1. `headOfDept` отправляет: `Тест "кавычек" и <тегов> & амперсанда`.
2. **Ожидаемо:** в чате — точно тот же текст без `&amp;`.

### Сценарий 2 — переносы

1. `Shift+Enter` между двумя строками. Отправить.
2. **Ожидаемо:** видно две строки.

### Сценарий 3 — редактирование своего

1. `regular` нажимает карандашик на своём комментарии.
2. Меняет текст, нажимает «Сохранить».
3. **Ожидаемо:** текст обновился, появилась метка «(изм.)» с tooltip даты редактирования.

### Сценарий 4 — отказ редактировать чужой

1. На чужом комментарии нет карандашика.
2. (negative) Через DevTools вызов `tcmEditComment(0)` для чужого индекса возвращает `ERROR:NotOwner`.

### Сценарий 5 — стилизованное подтверждение

1. Удаление своего комментария → стилизованный toast вместо `confirm`.

### Сценарий 6 — обратная совместимость

1. До деплоя: оставить пару комментариев со старой логикой (с `&quot;` в БД).
2. После деплоя: открыть задачу. **Ожидаемо:** старые комментарии без `&quot;`.

### Сценарий 7 — счётчик карточки

1. У задачи 0 комментариев → нет бейджа.
2. Отправить 1 → бейдж `1` без `RefreshBoard`.
3. Удалить → бейдж исчез.

### Сценарий 8 — Exclamation-уведомление

1. `headOfDept` (постановщик) пишет комментарий в задачу `regular`.
2. У `regular` всплывает уведомление от `ExclamationKanban` с темой «Новый комментарий в задаче «…»: …».
3. (после реализации шага 8) `regular` пишет комментарий → у `headOfDept` всплывает уведомление.

### Сценарий 9 — лимит 200

1. Достигнут лимит — 201-й возвращает `ERROR:LimitReached` (поведение не изменилось).

### Сценарий 10 — пустой текст при редактировании

1. Отредактировать комментарий до пустоты → клиент показывает «Текст не может быть пустым», сервер не вызывается.
