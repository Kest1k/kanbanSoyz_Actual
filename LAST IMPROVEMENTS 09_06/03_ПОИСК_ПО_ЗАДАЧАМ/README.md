# Фича 03 – Поиск по задачам (название, содержание, вложения)

**Тип:** сервер (`SOYUZ_UPLOAD_KanbanScreen_script.cs`) + клиент (`KanbanBoard_HTML.html`, `kanban.js`, `kanban.css`). Схема данных не меняется.
**Риск:** средний (новый серверный обработчик, обход всей базы задач).

---

## 1. Поведение

В тулбаре поле поиска. По вводу (Enter или через 400 мс паузы) клиент шлёт запрос на сервер, сервер ищет по всем доступным пользователю задачам и возвращает список совпадений. Под полем выпадает список результатов: название, колонка, исполнитель, где нашлось («название / описание / теги / комментарии / вложение: имя»). Клик по результату открывает карточку (`tcmOpen`).

**Где ищем:** `TaskName`, `TaskDetails`, `Tags`, `CommentsJSON` (текст обсуждения), имена вложений (`AttachedObjects`).

**Права:** поиск строго уважает видимость – сервер прогоняет каждую задачу через `CanUserSeeTask` + проверку scope. Чужие и приватные задачи в выдачу не попадают.

> Почему обходим задачи перебором, а не `SearchOperation`/SQL: модуль уже так делает в `BeforeRender` (перебор `container.RootInfoObjects`), это даёт единообразие и, главное, **проверку прав на каждой задаче**. Полнотекстовый `SearchOperation`/прямой SQL (см. блокнот `Soyz-PLM`) возвращает объекты в обход прав доступа, что потребовало бы повторной фильтрации и легко даёт утечки. Перебор безопаснее. Масштаб «канбан-доски подразделения» (сотни-тысячи задач) перебор выдерживает; если задач десятки тысяч – см. раздел 7 (оптимизация).

---

## 2. Серверная часть – `SOYUZ_UPLOAD_KanbanScreen_script.cs`

### 2.1. Зарегистрировать команду в `switch`

**Якорь:** блок маршрутизации, строки **165–228**. Добавить новый `case` (например, сразу после `case "GetReport":` @189):

```csharp
            case "SearchTasks":      return DoSearchTasks( inputParams );
```

> Не путать с уже существующим `case "SearchObjects"` (@198) – тот ищет PLM-объекты для прикрепления вложений, это другое.

### 2.2. Метод поиска + помощники

Добавить рядом с другими `Do*`-методами (например, около `DoGetReport`). Код в стиле файла; переиспользует существующие `GetUserRole`, `GetUserContext`, `IsWithinContext`, `HasSectorScopeRole`, `CanUserSeeTask`, `GetUserStableKey`, `GetStatusIndex`, `JsonEscape`.

```csharp
// ░░ Фича 03: поиск по задачам ░░
// inputParams: строка запроса. Возвращает JSON-массив совпадений (макс. 100).
private object DoSearchTasks( object inputParams )
{
    var q = ( GetParamStr( inputParams ) ?? "" ).Trim();
    if( q.Length < 2 ) return "[]";                 // не ищем по 1 символу
    var ql = q.ToLowerInvariant();

    var container = Service.GetDataContainer( "All_Kanban_Tasks_Folder" );
    if( container == null ) return "[]";

    var me    = Service.GetCurrentUser();
    var role  = GetUserRole( me );
    var myCtx = GetUserContext( me );

    var sb = new System.Text.StringBuilder();
    sb.Append( "[" );
    bool first = true;
    int  found = 0;
    const int LIMIT = 100;

    foreach( var task in container.RootInfoObjects )
    {
        if( found >= LIMIT ) break;
        if( !CanUserSeeTask( task, me ) )          continue;   // приватность
        if( !SearchInScope( task, me, role, myCtx ) ) continue; // scope

        string title    = task.GetString( "TaskName" )    ?? "";
        string details  = task.GetString( "TaskDetails" ) ?? "";
        string tags     = ""; try { tags     = task.GetString( "Tags" )         ?? ""; } catch { }
        string comments = ""; try { comments = task.GetString( "CommentsJSON" ) ?? ""; } catch { }

        string where = null;
        if     ( title.ToLowerInvariant().IndexOf( ql )    >= 0 ) where = "название";
        else if( details.ToLowerInvariant().IndexOf( ql )  >= 0 ) where = "описание";
        else if( tags.ToLowerInvariant().IndexOf( ql )     >= 0 ) where = "теги";
        else if( comments.ToLowerInvariant().IndexOf( ql ) >= 0 ) where = "комментарии";
        else
        {
            var attName = MatchAttachmentName( task, ql );      // вложения
            if( attName != null ) where = "вложение: " + attName;
            else continue;                                      // нигде не нашли
        }

        var key    = string.IsNullOrEmpty( task.NameKey ) ? task.Id.ToString() : task.NameKey;
        int status = GetStatusIndex( task );
        if( status < 0 || status > 3 ) status = 0;
        string asg = "";
        try { var a = task.GetUser( "Assignee" ); if( a != null ) asg = a.ToString() ?? ""; } catch { }

        if( !first ) sb.Append( "," );
        sb.Append( "{\"id\":\""        + JsonEscape( key )   + "\","
                 + "\"title\":\""      + JsonEscape( title ) + "\","
                 + "\"status\":"       + status              + ","
                 + "\"assignee\":\""   + JsonEscape( asg )   + "\","
                 + "\"where\":\""      + JsonEscape( where ) + "\"}" );
        first = false;
        found++;
    }

    sb.Append( "]" );
    return sb.ToString();
}

// Видимость задачи в поиске = та же логика, что и scope доски.
private bool SearchInScope( InfoObject task, User me, string role, string myCtx )
{
    if( me == null ) return false;
    if( role == "admin" ) return true;
    try
    {
        var asg = task.GetUser( "Assignee" );
        if( asg != null && asg.Id == me.Id ) return true;              // я исполнитель

        var creatorKey = task.GetString( "Creator" ) ?? "";
        var myKey      = GetUserStableKey( me );
        if( !string.IsNullOrEmpty( creatorKey ) && creatorKey == myKey ) return true; // я автор

        if( asg != null && ( role == "headOfDept" || HasSectorScopeRole( role ) ) )
        {
            var asgCtx = GetUserContext( asg );
            if( role == "headOfDept" ) return IsWithinContext( asgCtx, myCtx );
            return asgCtx == myCtx;                                    // сектор
        }
    }
    catch { }
    return false;
}

// Поиск подстроки в именах вложений. Перечисление AttachedObjects –
// тем же способом, что в DoGetAttachments (LinkedInfoObjects.SafeToSet()).
private string MatchAttachmentName( InfoObject task, string ql )
{
    try
    {
        var objAttr = task.GetAttribute( "AttachedObjects" );
        if( objAttr != null )
        {
            var set = objAttr.LinkedInfoObjects.SafeToSet();
            if( set != null )
                foreach( InfoObject io in set )
                {
                    var nm = io.ToString() ?? ( io.NameKey ?? "" );
                    if( !string.IsNullOrEmpty( nm ) && nm.ToLowerInvariant().IndexOf( ql ) >= 0 )
                        return nm;
                }
        }
    }
    catch { }
    return null;
}
```

---

## 3. Клиентская часть – `KanbanBoard_HTML.html`

Поле поиска кладём в тулбар. Если внедрена фича 01, удобно положить рядом с `kb-filter-bar`; иначе – после `kb-hier-panel` (после строки ~40).

```html
<!-- ░░ Поиск по задачам (фича 03) ░░ -->
<div id="kb-search" class="kb-search">
    <input type="text" id="kb-search-input" class="kb-search-input" autocomplete="off"
           placeholder="&#128269; Поиск по всем задачам (Enter)..."
           onkeydown="kbSearchKey(event)" onkeyup="kbSearchDebounce()">
    <a href="#" id="kb-search-clear" class="kb-search-clear" style="display:none;"
       onclick="kbSearchClear(); return false;">&times;</a>
    <div id="kb-search-results" class="kb-search-results" style="display:none;"></div>
</div>
```

---

## 4. Клиентская часть – `kanban.js`

Добавить блок (ES5, IE11). `tcmOpen` уже существует (@2431) – открывает карточку.

```javascript
/* ░░ Поиск по задачам (фича 03) ░░ */
var _kbSearchTimer = null;
var _kbSearchStNames = ["Надо сделать", "В работе", "Ожидание", "Готово"];

window.kbSearchKey = function (ev) {
    var e = ev || window.event;
    if (e && e.keyCode === 13) { kbSearchRun(); return; }      // Enter
    if (e && e.keyCode === 27) { kbSearchClear(); return; }    // Esc
};

window.kbSearchDebounce = function () {
    if (_kbSearchTimer) { clearTimeout(_kbSearchTimer); }
    var inp = document.getElementById("kb-search-input");
    var clr = document.getElementById("kb-search-clear");
    if (clr) clr.style.display = (inp && inp.value) ? "" : "none";
    _kbSearchTimer = setTimeout(kbSearchRun, 400);
};

window.kbSearchRun = function () {
    var inp = document.getElementById("kb-search-input");
    var box = document.getElementById("kb-search-results");
    if (!inp || !box) return;
    var q = inp.value.replace(/^\s+|\s+$/g, "");
    if (q.length < 2) { box.style.display = "none"; box.innerHTML = ""; return; }

    var json = "[]";
    try { json = window.external.InvokeTemplate("SearchTasks", q); } catch (e) { json = "[]"; }

    var list;
    try { list = JSON.parse(json); } catch (e2) { list = []; }

    if (!list || !list.length) {
        box.innerHTML = '<div class="kb-sr-empty">Ничего не найдено</div>';
        box.style.display = "";
        return;
    }

    var html = "", i;
    for (i = 0; i < list.length; i++) {
        var r = list[i];
        var st = (typeof r.status === "number") ? r.status : 0;
        html += '<div class="kb-sr-item" onclick="kbSearchOpen(\'' +
                String(r.id).replace(/'/g, "\\'") + '\')">' +
                '<div class="kb-sr-title">' + kbSearchEsc(r.title) + '</div>' +
                '<div class="kb-sr-meta">' +
                    '<span class="kb-sr-col kb-sr-col-' + st + '">' + (_kbSearchStNames[st] || "") + '</span>' +
                    (r.assignee ? '<span class="kb-sr-asg">' + kbSearchEsc(r.assignee) + '</span>' : '') +
                    '<span class="kb-sr-where">' + kbSearchEsc(r.where) + '</span>' +
                '</div></div>';
    }
    box.innerHTML = html;
    box.style.display = "";
};

window.kbSearchOpen = function (id) {
    kbSearchClear();
    if (typeof tcmOpen === "function") tcmOpen(id, "main");
};

window.kbSearchClear = function () {
    var inp = document.getElementById("kb-search-input");
    var box = document.getElementById("kb-search-results");
    var clr = document.getElementById("kb-search-clear");
    if (inp) inp.value = "";
    if (box) { box.style.display = "none"; box.innerHTML = ""; }
    if (clr) clr.style.display = "none";
};

function kbSearchEsc(s) {
    s = String(s == null ? "" : s);
    return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
}

// Скрыть выпадашку по клику вне поиска
document.addEventListener("click", function (e) {
    var wrap = document.getElementById("kb-search");
    var box = document.getElementById("kb-search-results");
    if (!wrap || !box) return;
    var t = e.target || e.srcElement;
    var inside = false;
    while (t) { if (t === wrap) { inside = true; break; } t = t.parentNode; }
    if (!inside) box.style.display = "none";
}, false);
```

> Если в проекте `document.addEventListener` где-то оборачивают для IE11 – используйте тот же приём, что в остальном `kanban.js` (он уже работает в IE11 WebBrowser, `addEventListener` поддерживается).

---

## 5. Стили – `kanban.css`

```css
/* ░░ Поиск по задачам (фича 03) ░░ */
.kb-search{position:relative;display:inline-block;margin-left:8px;}
.kb-search-input{height:26px;width:260px;border:1px solid #ccc;border-radius:4px;padding:0 24px 0 8px;font-size:12px;}
.kb-search-clear{position:absolute;right:6px;top:3px;color:#999;text-decoration:none;font-size:16px;line-height:20px;}
.kb-search-results{position:absolute;z-index:9999;top:30px;left:0;width:380px;max-height:360px;overflow:auto;
    background:#fff;border:1px solid #bbb;border-radius:6px;box-shadow:0 6px 18px rgba(0,0,0,.18);}
.kb-sr-item{padding:7px 10px;border-bottom:1px solid #f0f0f0;cursor:pointer;}
.kb-sr-item:hover{background:#f3f8ff;}
.kb-sr-title{font-size:13px;color:#222;font-weight:600;}
.kb-sr-meta{font-size:11px;color:#777;margin-top:2px;display:flex;gap:8px;flex-wrap:wrap;align-items:center;}
.kb-sr-col{padding:1px 6px;border-radius:8px;color:#fff;}
.kb-sr-col-0{background:#9aa0a6;}.kb-sr-col-1{background:#2d7ff9;}.kb-sr-col-2{background:#e8a33d;}.kb-sr-col-3{background:#3fae5a;}
.kb-sr-where{font-style:italic;color:#999;}
.kb-sr-empty{padding:10px;color:#888;font-size:12px;}
```

---

## 6. Edge cases

- **Минимум 2 символа** – защита от обхода всей базы по одному символу.
- **Лимит 100 результатов** – чтобы JSON не разрастался; при достижении выдача обрезается (можно показать «показаны первые 100»).
- **Регистронезависимость** – обе стороны приведены к `ToLowerInvariant()`.
- **CommentsJSON** содержит служебную разметку JSON, поэтому поиск по комментариям может срабатывать на ключи; для аккуратности можно искать только по значению поля текста – но для MVP подстрочный поиск по всему `CommentsJSON` приемлем.
- **IE11**: `JSON.parse`, `setTimeout`, `clearTimeout`, `addEventListener` поддерживаются. Никаких `let/const/=>`.
- **Производительность**: перебор идёт по `container.RootInfoObjects` (как `BeforeRender`). На каждый вызов – один проход. Дебаунс 400 мс снижает нагрузку.

---

## 7. (Опционально) Масштабирование на десятки тысяч задач

Если перебор станет медленным, заменить тело `DoSearchTasks` на серверный полнотекстовый поиск через `SearchOperation` (`RelationalOperator.Contains` по `TaskName`/`TaskDetails`) либо прямой SQL (`Service.MT.ExecuteReader` + `FREETEXTTABLE`, см. блокнот `Soyz-PLM`). **Обязательно** после получения кандидатов прогнать их через `CanUserSeeTask` + `SearchInScope`, т.к. поисковый движок прав доступа не учитывает.

---

## 8. Смок-тест

- [ ] Ввод 1 символа ничего не ищет; от 2 символов появляется выпадашка.
- [ ] Поиск по слову из **названия** задачи находит её, метка «название».
- [ ] Поиск по слову из **описания** – метка «описание».
- [ ] Поиск по **тегу** – метка «теги».
- [ ] Поиск по тексту из **комментария** – метка «комментарии».
- [ ] Поиск по **имени вложенного файла** – метка «вложение: <имя>».
- [ ] Клик по результату открывает карточку на вкладке «Основное».
- [ ] Крестик и Esc очищают поиск; клик вне выпадашки её прячет.
- [ ] Обычный пользователь не находит чужие задачи; приватные чужие задачи не находятся даже руководителем, если он не автор/исполнитель.
- [ ] Руководитель сектора находит задачи своих подчинённых; начальник отделения – по всему отделению.
- [ ] Дебаунс: быстрый набор не шлёт запрос на каждый символ.

---

## 9. Откат

Убрать `case "SearchTasks"`, методы `DoSearchTasks`/`SearchInScope`/`MatchAttachmentName`, блок HTML `kb-search`, блок JS «Поиск» и стили. Перекомпилировать.
