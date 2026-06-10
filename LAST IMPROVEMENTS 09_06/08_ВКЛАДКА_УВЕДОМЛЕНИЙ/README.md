# Фича 08 – «Полученные уведомления» (входящие исполнителя на доске)

**Тип:** сервер (`SOYUZ_UPLOAD_KanbanScreen_script.cs`) + клиент (`KanbanBoard_HTML.html`, `kanban.js`, `kanban.css`). Опционально – правка двух мест отправки уведомлений ради типа-маркера (`SOYUZ_UPLOAD_KanbanScreen_script.cs` `SendWorkItemNotify`, `SOYUZ_UPLOAD_KanbanTask_script.cs` `CheckAssigneeChanged`). Схема данных PLM **не** меняется.
**Риск:** средний (новый серверный обход нагрузок пользователя + новый overlay в тулбаре; в путь рассылки добавляется только один безопасный токен в `Params`).

---

## 1. Поведение

В тулбаре доски – кнопка **«Уведомления»** (в стиле `kb-nav-btn`) с бейджем количества непрочитанных. Клик открывает выпадающий список (overlay), общий **для одной доски одного исполнителя** – это не вкладка карточки, а board-level панель, как и просил заказчик («общая для одной доски одного исполнителя»).

В списке – всё, что пришло текущему пользователю: назначения задач и новые комментарии. Каждая строка: иконка типа (📌 назначение / 💬 комментарий), название задачи, краткий текст (превью/тема), дата, индикатор «не прочитано». Newest first.

**Клик по строке:**
- назначение → `tcmOpen(taskKey, 'main')` – карточка на вкладке «Основное»;
- комментарий → `tcmOpen(taskKey, 'chat')` – карточка сразу на вкладке «Обсуждение».

После клика строка помечается прочитанной (серверная нагрузка → `MarkAsViewedByCurrentUser`), бейдж пересчитывается. Есть кнопка «Прочитать все».

**Права:** список строится **только из собственных нагрузок текущего пользователя** (`Service.GetWorkItems(currentUser.Id, ...)`). Пользователь физически не может увидеть чужие уведомления – PLM отдаёт нагрузки, адресованные только ему. Дополнительно каждая нагрузка перед выдачей проверяется на принадлежность шаблону `ExclamationKanban`.

> Почему источник истины – именно нагрузки `ExclamationKanban`, а не отдельный лог: они **уже создаются** и для назначения (`SOYUZ_UPLOAD_KanbanTask_script.cs` → `CheckAssigneeChanged`), и для комментария (`SOYUZ_UPLOAD_KanbanScreen_script.cs` → `SendCommentNotification` → `SendWorkItemNotify`). В них уже лежат `Subject`, `Params=taskKey`, `DateCreated` и системное состояние «прочитано» (`IsNewForCurrentUser`). Заводить параллельный per-user лог означало бы дублировать данные и риск рассинхрона. Нагрузки – единый источник, который к тому же сам собой чистится штатными механизмами ПРС.

---

## 2. Что нашлось в блокнотах и в коде (обоснование API)

### 2.1. PLM API для перечисления нагрузок пользователя (блокнот `Soyz-PLM`)

Подтверждённый способ получить активные нагрузки **текущего** пользователя из скрипта:

```csharp
// Soyz-PLM, "Workflow_and_Processes.md":
// static WorkItem[] Service.GetWorkItems( ulong userId, bool active )
WorkItem[] items = Service.GetWorkItems( Service.GetCurrentUser().Id, true );
```

Цитата из источника: «`static WorkItem[] Service.GetWorkItems( ulong userId, bool active )` … свои берутся из кэша». То есть для **своих** нагрузок обращения к серверу не требуется – быстрый путь.

Чтение полей нагрузки (там же + `Scripting_and_API_Part1.md`, `Other_and_General_FAQ.md`):
- `workItem.GetValue<string>("Subject")` – тема/текст;
- `string p = workItem.Params;` – системное свойство (у нас тут лежит `taskKey`);
- `DateTime dt = workItem.DateCreated;` – дата создания;
- `bool isUnread = workItem.IsNewForCurrentUser;` – «не просмотрено текущим пользователем»;
- `workItem.MarkAsViewedByCurrentUser();` – пометить прочитанным (уже используется в `ExclamationKanban.cs`).

Фильтр по шаблону (LINQ по кэшу – рекомендуемый блокнотом паттерн):
```csharp
var template = Service.GetTemplate( @"WorkLoads\BASIC\Message\ExclamationKanban" );
var mine = Service.GetWorkItems( me.Id, true ).Where( w => w.Template == template );
```

> Альтернатива из блокнота – `SearchOperation(EntityIdentifier.WorkItem)` с `SearchWorkItemFilterItem.PropertyId = SystemPropertyId.RecipientUser`. Нужна только если потребуется выбирать **закрытые** нагрузки или нагрузки за пределами кэша. Для MVP это избыточно: `GetWorkItems(me.Id, true)` уже отдаёт ровно «папку оповещений» текущего пользователя. Серверный `SearchOperation` оставляем как опцию масштабирования (раздел 8).

### 2.2. Реальный код отправки (прочитано из файлов)

- `SOYUZ_UPLOAD_KanbanScreen_script.cs`, `SendWorkItemNotify(...)`:
  ```csharp
  var w = new WorkItem( tmpl, recipient );
  w[ "Subject" ] = subject;          // "Новый комментарий в задаче «...»: <превью>"
  try { w[ "TaskDetails" ] = details; } catch { }
  try { w[ "SilentMode"  ] = true;    } catch { }
  w.Params     = taskKey;            // ← только ключ задачи, без типа
  w.IsBySystem = true;
  w.Send();
  ```
- `SOYUZ_UPLOAD_KanbanTask_script.cs`, `CheckAssigneeChanged(...)`:
  ```csharp
  var w = new WorkItem( msgTemplate, newAssignee );
  w[ "Subject" ] = "Новая задача «" + taskName + "» от " + senderName + extra;
  try { w[ "SilentMode" ] = true; } catch { }
  w.Params = obj.NameKey;            // ← только ключ задачи, без типа
  w.StatusOperation = WorkItemBase.StatusEnum.Sent;
  ```
- `ExclamationKanban.cs`, `OnUpdated(...)`: тип определяется **по префиксу темы** –
  ```csharp
  bool isComment = subject.StartsWith("Новый комментарий", StringComparison.OrdinalIgnoreCase);
  // ...
  bool isComm = subject.IndexOf("Новый комментарий", StringComparison.OrdinalIgnoreCase) >= 0;
  string targetTab = isComm ? "chat" : "main";   // куда открывать карточку
  ```

**Вывод:** тип уведомления (назначение vs комментарий) сегодня хранится **неявно** – через текст `Subject`. Для инбокса это работает, но хрупко (зависит от русского префикса). Ниже – устойчивая схема с явным маркером, обратно совместимая.

---

## 3. Схема типа-маркера (assignment vs comment)

**Решение:** дописать тип в конец `Params` через разделитель `|`:
- назначение: `Params = taskKey + "|task"`
- комментарий: `Params = taskKey + "|comment"`

Это безопасно, потому что:
- `taskKey` (NameKey задачи) разделителя `|` не содержит;
- `ExclamationKanban.cs` уже **берёт только** `taskKey = (obj.Params ?? "").Trim();` и определяет вкладку независимо – по `Subject`. Если убрать хвост `|...` перед использованием, popup продолжит работать без изменений (см. 3.2).

### 3.1. Правки отправки (опциональны, но рекомендуются)

`SendWorkItemNotify` (комментарий):
```csharp
-   w.Params     = taskKey;
+   w.Params     = taskKey + "|comment";   // фича 08: явный тип
```

`CheckAssigneeChanged` (назначение):
```csharp
-   w.Params = obj.NameKey;
+   w.Params = obj.NameKey + "|task";      // фича 08: явный тип
```

### 3.2. Совместимость с popup `ExclamationKanban.cs`

`OnUpdated` сейчас делает `var taskKey = (obj.Params ?? "").Trim();` и затем передаёт его в `OpenBoardFromNotification(taskKey, targetTab)`, где из ключа собирается `AutoOpenTask = taskKey + "|" + targetTab`. Если `Params` станет `taskKey|comment`, ключ «загрязнится».

Поэтому при внедрении 3.1 нужно одной строкой обрезать хвост в `ExclamationKanban.cs` (только разбор ключа, логику типа не трогаем – она и так по `Subject`):
```csharp
-   var taskKey = (obj.Params ?? "").Trim();
+   var raw     = (obj.Params ?? "").Trim();
+   var taskKey = raw.IndexOf('|') >= 0 ? raw.Substring(0, raw.IndexOf('|')) : raw;
```

> Это **единственная** правка во вспомогательном скрипте, и она строго аддитивна: старые нагрузки (где `Params` без `|`) обрабатываются как раньше. Если заказчик не хочет трогать `ExclamationKanban.cs` / `KanbanTask`, см. fallback 3.3 – инбокс работает и без маркера.

### 3.3. Fallback без правки отправки (обратная совместимость со старыми нагрузками)

Серверный `GetMyNotifications` определяет тип так:
1. если `Params` содержит `|comment` → `comment`; если `|task` → `task`;
2. иначе (старые нагрузки или отказ от 3.1) – **по `Subject`**: начинается на «Новый комментарий» → `comment`, иначе → `task`.

Так инбокс корректен и для будущих (с маркером), и для уже разосланных (без маркера) уведомлений. Это та же эвристика, что уже живёт в `ExclamationKanban.cs`, поэтому поведение согласовано.

---

## 4. Серверная часть – `SOYUZ_UPLOAD_KanbanScreen_script.cs`

### 4.1. Зарегистрировать команды в `switch`

**Якорь:** блок маршрутизации `Invoke` (около `case "GetWhatsNewSeen"` / `case "ClearAutoOpen"`, ~строки 257–259). Добавить:

```csharp
            case "GetMyNotifications":  return DoGetMyNotifications( inputParams );
            case "MarkNotifRead":       return DoMarkNotifRead( inputParams );
            case "MarkAllNotifRead":    return DoMarkAllNotifRead();
```

### 4.2. Методы (в стиле файла; переиспользуют `JsonEscape`, `GetParamStr`, шаблон `COMMENT_MSG_TEMPLATE_PATH`)

```csharp
// ░░ Фича 08: входящие уведомления исполнителя ░░
// Возвращает JSON-массив собственных нагрузок ExclamationKanban текущего
// пользователя, newest first, не более LIMIT. Каждая запись:
// { id, type, taskKey, title, text, date, seen }
private object DoGetMyNotifications( object inputParams )
{
    const int LIMIT = 100;
    var me = Service.GetCurrentUser();
    if( me == null ) return "[]";

    var tmpl = Service.GetTemplate( COMMENT_MSG_TEMPLATE_PATH ); // ExclamationKanban
    if( tmpl == null ) return "[]";

    // Только свои активные нагрузки – из кэша пользователя (Soyz-PLM: GetWorkItems).
    WorkItem[] items;
    try { items = Service.GetWorkItems( me.Id, true ); }
    catch { return "[]"; }
    if( items == null || items.Length == 0 ) return "[]";

    // Отбираем канбан-нагрузки и сортируем по дате убыванием.
    var list = new System.Collections.Generic.List<WorkItem>();
    foreach( var w in items )
    {
        if( w == null ) continue;
        try { if( w.Template != tmpl ) continue; } catch { continue; }
        list.Add( w );
    }
    list.Sort( delegate( WorkItem a, WorkItem b )
    {
        DateTime da = DateTime.MinValue, db = DateTime.MinValue;
        try { da = a.DateCreated; } catch { }
        try { db = b.DateCreated; } catch { }
        return db.CompareTo( da ); // newest first
    } );

    var sb = new System.Text.StringBuilder();
    sb.Append( "[" );
    bool first = true;
    int  count = 0;
    foreach( var w in list )
    {
        if( count >= LIMIT ) break;

        string subject = "";
        try { subject = w.GetValue<string>( "Subject" ) ?? ""; } catch { }

        string rawParams = "";
        try { rawParams = ( w.Params ?? "" ).Trim(); } catch { }

        string taskKey = rawParams;
        string type    = "task";
        int pipe = rawParams.IndexOf( '|' );
        if( pipe >= 0 )
        {
            taskKey  = rawParams.Substring( 0, pipe );
            var tail = rawParams.Substring( pipe + 1 ).ToLowerInvariant();
            type = ( tail.IndexOf( "comment" ) >= 0 ) ? "comment" : "task";
        }
        else
        {
            // Fallback для старых нагрузок без маркера – по теме (как ExclamationKanban.cs).
            type = subject.StartsWith( "Новый комментарий", StringComparison.OrdinalIgnoreCase )
                 ? "comment" : "task";
        }
        if( string.IsNullOrEmpty( taskKey ) ) continue; // нечего открывать

        // title/text: из Subject. У комментария Subject = "Новый комментарий в задаче «Имя»: текст".
        string title = subject;
        string text  = "";
        int q1 = subject.IndexOf( '«' );   // «
        int q2 = subject.IndexOf( '»' );   // »
        if( q1 >= 0 && q2 > q1 )
            title = subject.Substring( q1 + 1, q2 - q1 - 1 );
        int colon = subject.IndexOf( "»: " );
        if( colon >= 0 ) text = subject.Substring( colon + 3 );

        bool seen = true;
        try { seen = !w.IsNewForCurrentUser; } catch { seen = true; }

        string date = "";
        try { date = w.DateCreated.ToString( "yyyy-MM-dd HH:mm" ); } catch { }

        string id = "";
        try { id = w.Id.ToString(); } catch { }

        if( !first ) sb.Append( "," );
        sb.Append( "{\"id\":\""      + JsonEscape( id )      + "\","
                 + "\"type\":\""     + JsonEscape( type )    + "\","
                 + "\"taskKey\":\""  + JsonEscape( taskKey ) + "\","
                 + "\"title\":\""    + JsonEscape( title )   + "\","
                 + "\"text\":\""     + JsonEscape( text )    + "\","
                 + "\"date\":\""     + JsonEscape( date )    + "\","
                 + "\"seen\":"       + ( seen ? "true" : "false" ) + "}" );
        first = false;
        count++;
    }
    sb.Append( "]" );
    return sb.ToString();
}

// Пометить одну нагрузку прочитанной. inputParams = WorkItem.Id (строка).
// Гейтинг: ищем нагрузку только среди СВОИХ нагрузок текущего пользователя,
// поэтому чужую пометить нельзя.
private object DoMarkNotifRead( object inputParams )
{
    var idStr = ( GetParamStr( inputParams ) ?? "" ).Trim();
    if( idStr.Length == 0 ) return "0";
    var me = Service.GetCurrentUser();
    if( me == null ) return "0";

    WorkItem[] items;
    try { items = Service.GetWorkItems( me.Id, true ); } catch { return "0"; }
    if( items == null ) return "0";

    foreach( var w in items )
    {
        if( w == null ) continue;
        string wid = "";
        try { wid = w.Id.ToString(); } catch { continue; }
        if( wid == idStr )
        {
            try { w.MarkAsViewedByCurrentUser(); } catch { return "0"; }
            return "1";
        }
    }
    return "0"; // не наша / не найдена
}

// Пометить прочитанными все канбан-нагрузки текущего пользователя.
// Возвращает число помеченных.
private object DoMarkAllNotifRead()
{
    var me = Service.GetCurrentUser();
    if( me == null ) return "0";
    var tmpl = Service.GetTemplate( COMMENT_MSG_TEMPLATE_PATH );
    if( tmpl == null ) return "0";

    WorkItem[] items;
    try { items = Service.GetWorkItems( me.Id, true ); } catch { return "0"; }
    if( items == null ) return "0";

    int n = 0;
    foreach( var w in items )
    {
        if( w == null ) continue;
        try { if( w.Template != tmpl ) continue; } catch { continue; }
        try
        {
            if( w.IsNewForCurrentUser )
            {
                w.MarkAsViewedByCurrentUser();
                n++;
            }
        }
        catch { }
    }
    return n.ToString();
}
```

> Поля `title`/`text` извлекаются из `Subject` точно теми же кавычками-ёлочками `«`/`»`, что использует `ExclamationKanban.cs` при разборе темы – формат гарантированно совпадает. Если ёлочек нет (нестандартная тема), `title` = вся тема, `text` пуст – деградация мягкая.

---

## 5. Клиент – `KanbanBoard_HTML.html`

Кнопку кладём в `kb-nav-right`, рядом с «Что нового» / «Справка» (якорь – блок `<div class="kb-nav-right">`, ~строки 102–109). Сам overlay – в конец того же контейнера (позиционируется абсолютно).

```html
        <!-- ░░ Уведомления (фича 08) ░░ -->
        <div class="kb-notif-wrap">
            <button type="button" id="kb-notif-btn" class="kb-nav-btn kb-nav-btn-news"
                    onclick="kbNotifToggle(); return false;">
                <i class="fa fa-bell fa-fw"></i> Уведомления
                <span id="kb-notif-badge" class="kb-notif-badge" style="display:none;">0</span>
            </button>
            <div id="kb-notif-panel" class="kb-notif-panel" style="display:none;">
                <div class="kb-notif-head">
                    <span>Полученные уведомления</span>
                    <a href="#" class="kb-notif-readall" onclick="kbNotifReadAll(); return false;">Прочитать все</a>
                </div>
                <div id="kb-notif-list" class="kb-notif-list"></div>
            </div>
        </div>
```

---

## 6. Клиент – `kanban.js`

ES5 / IE11 (Trident). Никаких `let/const/=>`. `tcmOpen(nameKey, targetTab)` уже существует (`window.tcmOpen` ~@2461). `window.external.InvokeTemplate` – мост к серверу.

```javascript
/* ░░ Уведомления исполнителя (фича 08) ░░ */
var _kbNotifOpen   = false;
var _kbNotifPollTm = null;

function kbNotifEsc(s){
    s = String(s == null ? "" : s);
    return s.replace(/&/g,"&amp;").replace(/</g,"&lt;").replace(/>/g,"&gt;").replace(/"/g,"&quot;");
}

// Загрузить список и отрисовать (если панель открыта) + обновить бейдж.
window.kbNotifLoad = function (renderList) {
    var json = "[]";
    try { json = window.external.InvokeTemplate("GetMyNotifications", ""); } catch (e) { json = "[]"; }
    var list;
    try { list = JSON.parse(json); } catch (e2) { list = []; }
    if (!list) list = [];

    // бейдж = число непрочитанных
    var unread = 0, i;
    for (i = 0; i < list.length; i++) { if (list[i] && !list[i].seen) unread++; }
    kbNotifBadge(unread);

    if (renderList) kbNotifRender(list);
    return list;
};

function kbNotifBadge(n){
    var b = document.getElementById("kb-notif-badge");
    if (!b) return;
    if (n > 0) { b.innerHTML = (n > 99 ? "99+" : String(n)); b.style.display = ""; }
    else       { b.style.display = "none"; }
}

function kbNotifRender(list){
    var box = document.getElementById("kb-notif-list");
    if (!box) return;
    if (!list.length){
        box.innerHTML = '<div class="kb-notif-empty">Уведомлений нет</div>';
        return;
    }
    var html = "", i;
    for (i = 0; i < list.length; i++){
        var r = list[i]; if (!r) continue;
        var isComment = (r.type === "comment");
        var ico = isComment ? "💬" : "📌"; // 💬 / 📌
        var cls = "kb-notif-item" + (r.seen ? "" : " kb-notif-unread");
        var key = String(r.taskKey).replace(/'/g, "\\'");
        var id  = String(r.id).replace(/'/g, "\\'");
        var tab = isComment ? "chat" : "main";
        html += '<div class="' + cls + '" onclick="kbNotifOpen(\'' + id + '\',\'' + key + '\',\'' + tab + '\')">' +
                    '<span class="kb-notif-ico">' + ico + '</span>' +
                    '<span class="kb-notif-body">' +
                        '<span class="kb-notif-title">' + kbNotifEsc(r.title) + '</span>' +
                        (r.text ? '<span class="kb-notif-text">' + kbNotifEsc(r.text) + '</span>' : '') +
                        '<span class="kb-notif-date">' + kbNotifEsc(r.date) + '</span>' +
                    '</span>' +
                '</div>';
    }
    box.innerHTML = html;
}

window.kbNotifToggle = function () {
    var panel = document.getElementById("kb-notif-panel");
    if (!panel) return;
    _kbNotifOpen = (panel.style.display === "none" || panel.style.display === "");
    if (_kbNotifOpen){
        kbNotifLoad(true);
        panel.style.display = "block";
    } else {
        panel.style.display = "none";
    }
};

window.kbNotifOpen = function (id, taskKey, tab) {
    // пометить прочитанным (best effort), затем открыть карточку
    try { window.external.InvokeTemplate("MarkNotifRead", String(id)); } catch (e) {}
    var panel = document.getElementById("kb-notif-panel");
    if (panel) panel.style.display = "none";
    _kbNotifOpen = false;
    kbNotifLoad(false); // пересчитать бейдж
    if (typeof tcmOpen === "function" && taskKey) tcmOpen(taskKey, tab || "main");
};

window.kbNotifReadAll = function () {
    try { window.external.InvokeTemplate("MarkAllNotifRead", ""); } catch (e) {}
    kbNotifLoad(true); // перерисовать + сбросить бейдж
};

// Периодический опрос бейджа (без открытия панели). 60 c.
function kbNotifStartPoll(){
    if (_kbNotifPollTm) return;
    kbNotifLoad(false);
    _kbNotifPollTm = setInterval(function(){ kbNotifLoad(_kbNotifOpen); }, 60000);
}

// Закрытие панели по клику вне неё. IE11: addEventListener есть в WebBrowser,
// но на всякий случай – с attachEvent-фоллбеком (legacy Trident).
function kbNotifBindOutside(){
    var handler = function (e){
        var wrap  = document.getElementById("kb-notif-wrap") ||
                    (document.getElementsByClassName ? document.getElementsByClassName("kb-notif-wrap")[0] : null);
        var panel = document.getElementById("kb-notif-panel");
        if (!panel || panel.style.display === "none") return;
        var t = e.target || e.srcElement, inside = false;
        while (t){ if (t.className && String(t.className).indexOf("kb-notif-wrap") >= 0){ inside = true; break; } t = t.parentNode; }
        if (!inside){ panel.style.display = "none"; _kbNotifOpen = false; }
    };
    if (document.addEventListener) document.addEventListener("click", handler, false);
    else if (document.attachEvent) document.attachEvent("onclick", handler); // IE legacy
}

// Инициализация – вызвать там же, где инициализируется доска (после отрисовки тулбара).
window.kbNotifInit = function () {
    kbNotifBindOutside();
    kbNotifStartPoll();
};
```

> **Где вызвать `kbNotifInit()`:** в существующей точке инициализации доски (там же, откуда стартуют прочие тулбар-обработчики – ищите функцию, которая вешает кнопки режимов `kb-btn-my` и т.п.). Если такой единой точки нет, повесить на загрузку: `if (document.addEventListener) document.addEventListener("DOMContentLoaded", kbNotifInit, false);` плюс `attachEvent("onload", ...)` фоллбек.

> Класс на обёртке `kb-notif-wrap` нужен и как `id`, и как `className` – в HTML выше добавьте `id="kb-notif-wrap"` к `<div class="kb-notif-wrap">` (детектор «клик вне» опирается на оба).

---

## 7. Стили – `kanban.css`

В стиле существующих `kb-nav-btn` (@932+) и бейджей (`kb-new-badge` @2135).

```css
/* ░░ Уведомления исполнителя (фича 08) ░░ */
.kb-notif-wrap{position:relative;display:inline-block;}
.kb-notif-badge{display:inline-block;min-width:16px;height:16px;line-height:16px;padding:0 4px;
    margin-left:4px;border-radius:8px;background:#dc2626;color:#fff;font-size:11px;font-weight:700;text-align:center;}
.kb-notif-panel{position:absolute;z-index:9999;top:34px;right:0;width:380px;max-height:440px;overflow:auto;
    background:#fff;border:1px solid #cbd5e1;border-radius:8px;box-shadow:0 8px 24px rgba(0,0,0,.18);}
.kb-notif-head{display:flex;justify-content:space-between;align-items:center;
    padding:8px 12px;border-bottom:1px solid #eef2f7;font-weight:600;color:#334155;font-size:13px;}
.kb-notif-readall{font-size:12px;color:#2563eb;text-decoration:none;font-weight:500;}
.kb-notif-readall:hover{text-decoration:underline;}
.kb-notif-list{padding:2px 0;}
.kb-notif-item{display:flex;gap:8px;padding:8px 12px;border-bottom:1px solid #f1f5f9;cursor:pointer;}
.kb-notif-item:hover{background:#f3f8ff;}
.kb-notif-unread{background:#eff6ff;}
.kb-notif-unread .kb-notif-title{font-weight:700;}
.kb-notif-ico{flex:0 0 auto;font-size:15px;line-height:18px;}
.kb-notif-body{display:flex;flex-direction:column;min-width:0;}
.kb-notif-title{font-size:13px;color:#1e293b;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.kb-notif-text{font-size:12px;color:#64748b;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;margin-top:1px;}
.kb-notif-date{font-size:11px;color:#94a3b8;margin-top:2px;}
.kb-notif-empty{padding:16px 12px;color:#94a3b8;font-size:13px;text-align:center;}
```

---

## 8. Сосуществование с существующими механизмами

- **Popup `ExclamationKanban`** – не трогаем (кроме одной строки обрезки `Params`, и только если внедряем маркер 3.1). Инбокс читает те же нагрузки, но **отдельно** их `MarkAsViewedByCurrentUser` вызывает только по клику пользователя – popup и инбокс оба зовут один и тот же штатный метод, конфликта нет (идемпотентно).
- **Бейдж «НОВАЯ» (`SeenByList`)** – это про **карточку задачи на доске**, отдельная сущность (атрибут `KanbanTask.SeenByList`). Инбокс его не читает и не пишет. Открытие карточки из инбокса идёт через тот же `tcmOpen`, который и так дописывает пользователя в `SeenByList` при показе карточки – поведение остаётся прежним.
- **Подавление tray (`KanbanTrayFilter`)** – не затрагивается: мы не меняем routing нагрузок, `IsBySystem`/`SilentMode` остаются.

---

## 9. Масштабирование / опции

- **Закрытые/архивные уведомления.** `GetWorkItems(me.Id, true)` отдаёт только активные. Если понадобится история закрытых – заменить выборку на `SearchOperation(EntityIdentifier.WorkItem)` с `SearchWorkItemFilterItem.PropertyId = SystemPropertyId.RecipientUser` (+ фильтр по шаблону `ExclamationKanban`), как описано в блокноте `Soyz-PLM`. Права при этом всё равно фильтровать по `RecipientUser == me`.
- **Группировка по задачам.** Можно схлопывать несколько комментариев одной задачи в одну строку «N новых сообщений» – на стороне `kanban.js` (по `taskKey`), сервер не меняется.
- **Live-обновление.** Сейчас опрос раз в 60 c + при открытии панели. Если нужен мгновенный бейдж после прихода – уменьшить интервал или дёргать `kbNotifLoad(false)` из существующего `RefreshRequested`-цикла доски.

---

## 10. Edge cases

- **Нет нагрузок** → `"[]"` → «Уведомлений нет», бейдж скрыт.
- **Старые нагрузки без маркера** → тип определяется по `Subject` (fallback 3.3); открываются на правильной вкладке.
- **Пустой `taskKey`** (нагрузка без `Params`) → строка пропускается (нечего открывать).
- **Нестандартная тема без ёлочек** → `title` = вся тема, `text` пуст; клик всё равно работает.
- **Чужой `id` в `MarkNotifRead`** → не найден среди своих нагрузок → `"0"`, ничего не помечается (гейтинг по `GetWorkItems(me.Id)`).
- **IE11/Trident:** только ES5. `JSON.parse`, `setInterval`, `addEventListener` поддерживаются в WebBrowser-хосте; для «клика вне» добавлен `attachEvent`-фоллбек. Эмодзи задаются суррогатными парами (`💬`), а не литералами – безопаснее для legacy-кодировок.
- **Лимит 100** – защита от разрастания JSON; новейшие сверху, поэтому усечение режет только самые старые.
- **Двойной клик** по строке → повторный `MarkNotifRead` идемпотентен (нагрузка уже viewed), `tcmOpen` просто переоткроет карточку.

---

## 11. Смок-тест

Двое: Автор (A) и Исполнитель (B).
- [ ] A назначает задачу B → у B бейдж «Уведомления» показывает +1 (в течение опроса / после открытия панели).
- [ ] B открывает панель → видит строку 📌 с названием задачи, датой, выделена как непрочитанная.
- [ ] Клик по строке-назначению → открывается карточка на вкладке «Основное»; строка стала прочитанной; бейдж уменьшился.
- [ ] A пишет комментарий в задаче B → у B появляется строка 💬 с превью текста.
- [ ] Клик по строке-комментарию → карточка открывается сразу на вкладке «Обсуждение».
- [ ] «Прочитать все» → все строки гаснут, бейдж скрывается.
- [ ] B не видит уведомлений, адресованных другому пользователю (проверка на двух учётках).
- [ ] Старое уведомление (созданное до внедрения маркера) открывается на верной вкладке (fallback по теме).
- [ ] Popup `ExclamationKanban` по-прежнему всплывает и открывает доску (если внедряли 3.1 – ключ задачи не «загрязнён» хвостом `|comment`).
- [ ] Бейдж «НОВАЯ» на карточке доски работает как раньше (инбокс на него не влияет).
- [ ] Клик вне панели её закрывает (в т.ч. в IE-режиме хоста).

---

## 12. Откат

1. Убрать три `case` (`GetMyNotifications`, `MarkNotifRead`, `MarkAllNotifRead`) и методы `DoGetMyNotifications`/`DoMarkNotifRead`/`DoMarkAllNotifRead`.
2. Удалить блок HTML `kb-notif-wrap`, JS-блок «Уведомления исполнителя» и стили `kb-notif-*`.
3. Если внедряли маркер 3.1: вернуть `w.Params = taskKey;` / `w.Params = obj.NameKey;` и строку разбора `taskKey` в `ExclamationKanban.cs`. Старые нагрузки с хвостом `|...` при этом продолжат открываться (popup и так берёт подстроку до `|` после правки – либо вернуть и её).
4. Перекомпилировать. Схема PLM не менялась – чистить нечего.
