# 05 — Фильтр задач по периоду времени

> **Источник в backlog:** `KANBAN_IMPROVEMENTS_BACKLOG.md` §2.6.
> **Фича:** `period_filter`.
> **Приоритет:** P0 (Phase 2, задача №1).
> **Затронутые файлы:** `scripts/SOYUZ_UPLOAD_KanbanScreen_script.cs`, `scripts/kanban.js`, `scripts/KanbanBoard_HTML.html`, `scripts/kanban.css`.

---

## 1. Техническое описание задачи

### 1.1. Требования backlog

> - Возможность фильтровать задачи по дате создания и/или дате завершения.
> - Пример: «с 01.01.2026 по 31.01.2026».
> - Должен работать как на основной доске, так и в отчётах.

### 1.2. Архитектурное решение

Фильтр периода **требует перезапроса данных**, потому что мы не отрисовываем задачи вне выбранного периода — они физически отсутствуют в DOM. Поэтому:

1. UI: два текстовых поля `<input type="text">` с **кастомным календарём** (через существующую функцию `calToggle('id')`, см. `KanbanBoard_HTML.html` строка 552) в верхнем тулбаре + кнопка «Применить» + кнопка «Сбросить». Формат — `ДД.ММ.ГГГГ` (русская локаль). **Нативный `<input type="date">` запрещён** — IE11 рендерит его как обычный текстовый инпут без датапикера.
2. Изменение полей → JS вызывает `window.external.InvokeTemplate("SetPeriodFilter", dateFrom + "|" + dateTo)` где даты в формате `dd.MM.yyyy`.
3. Сервер сохраняет даты (как строки `dd.MM.yyyy`) в `screenObj.PropertyBag["KbPeriodFrom"]` и `["KbPeriodTo"]`.
4. JS вызывает `kbRefreshBoard()` (существующая функция в `kanban.js`, строка ~246).
5. `BeforeRender` читает PropertyBag и при формировании списков `cols[0..3]` отсекает задачи, у которых `task.DateCreated` (или `task.GetValue<DateTime>("CompletedDate")` для колонки «Готово») вне периода.

### 1.3. Логика отсечения

| Колонка | Поле для сравнения с периодом | Способ чтения в C# |
|---------|--------------------------------|---------------------|
| 0 «Надо сделать» | `DateCreated` | `task.DateCreated` (системное свойство) |
| 1 «В работе» | `DateCreated` | `task.DateCreated` |
| 2 «Ожидание» | `DateCreated` | `task.DateCreated` |
| 3 «Готово» | `CompletedDate` (если задано) иначе `DateCreated` | `task.GetValue<DateTime>("CompletedDate")` / `task.DateCreated` |

> **Важно:** `task.GetString("Created")` или `task.GetString("CompletedDate")` использовать **запрещено** — это системные/типизированные даты, читаются строго через `task.DateCreated` и `task.GetValue<DateTime>(...)`.

Логика: «покажи всё, что создано в марте» + «покажи, что закрыто в марте». Задача, созданная в феврале, но завершённая в марте, попадёт в фильтр «март».

### 1.4. Применение в отчётах

Существующий `DoGetReport` в `SOYUZ_UPLOAD_KanbanScreen_script.cs` уже принимает `week/month/quarter` (строки ~1151–1153). Расширяем веткой `custom|from|to` и используем те же даты из PropertyBag, если переданы.

### 1.5. Что НЕ делаем

- Не вводим новые типы атрибутов в InfoType `KanbanTask`. `DateCreated` — системное свойство платформы; `CompletedDate` — уже существующий типизированный атрибут.
- Не делаем «прокатываемый» фильтр по неделям (UX-сахар, Phase 3).
- Не сохраняем фильтр между сессиями экрана. PropertyBag в рамках сессии — приемлемо.

---

## 2. Затронутые файлы

| Путь | Что меняем |
|------|------------|
| `scripts/SOYUZ_UPLOAD_KanbanScreen_script.cs` | Новый Invoke-метод `SetPeriodFilter`. Расширение `BeforeRender`. Хелпер `IsTaskInPeriod`. Расширение `DoGetReport` на ветку `custom`. |
| `scripts/kanban.js` | Блок `kbPeriodFilter*`: init, onchange, apply, reset. Подвязка к `kbInitHierarchy`. **Обновление `calEl()` и `document.onclick`** для поддержки новых ID календарей (`kb-cal-kb-period-from`, `kb-cal-kb-period-to`). |
| `scripts/KanbanBoard_HTML.html` | Два `<input type="date">` + кнопка «Применить» + «Сбросить» в верхнем тулбаре. |
| `scripts/kanban.css` | Стили блока фильтра. |

---

## 3. C# — что и где менять (`SOYUZ_UPLOAD_KanbanScreen_script.cs`)

### 3.1. Ветка в `Invoke`

Рядом со строкой ~135 (`case "SetViewMode":`):

```csharp
case "SetPeriodFilter": return DoSetPeriodFilter( obj, inputParams );
```

### 3.2. Метод `DoSetPeriodFilter`

```csharp
// ─── SetPeriodFilter ───────────────────────────────────────────────
// inputParams: "dd.MM.yyyy|dd.MM.yyyy"  (русский формат, приходит с кастомного календаря)
// Пустые значения = «без фильтра» с этой стороны.
// Возвращает "OK" или "ERROR:Format".
private object DoSetPeriodFilter( InfoObject screenObj, object inputParams )
{
    var raw = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 2 );
    string sFrom = (parts.Length > 0 ? parts[0] : "").Trim();
    string sTo   = (parts.Length > 1 ? parts[1] : "").Trim();

    try
    {
        if( string.IsNullOrEmpty( sFrom ) && string.IsNullOrEmpty( sTo ) )
        {
            screenObj.PropertyBag.Remove( "KbPeriodFrom" );
            screenObj.PropertyBag.Remove( "KbPeriodTo" );
            return "OK";
        }

        if( !string.IsNullOrEmpty( sFrom ) )
        {
            DateTime dFrom;
            if( !DateTime.TryParseExact( sFrom, "dd.MM.yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out dFrom ) )
                return "ERROR:Format";
            screenObj.PropertyBag["KbPeriodFrom"] = sFrom;
        }
        else screenObj.PropertyBag.Remove( "KbPeriodFrom" );

        if( !string.IsNullOrEmpty( sTo ) )
        {
            DateTime dTo;
            if( !DateTime.TryParseExact( sTo, "dd.MM.yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out dTo ) )
                return "ERROR:Format";
            screenObj.PropertyBag["KbPeriodTo"] = sTo;
        }
        else screenObj.PropertyBag.Remove( "KbPeriodTo" );

        return "OK";
    }
    catch( Exception ex )
    {
        Service.HandleException( ex, "KanbanScreen.DoSetPeriodFilter: " + ex.Message );
        return "ERROR:Internal";
    }
}
```

### 3.3. Хелпер `IsTaskInPeriod`

Размещаем рядом с другими хелперами (например после `GetStatusIndex`):

```csharp
// ─── Проверка попадания задачи в период (для BeforeRender) ─────────
// statusIdx: 0..3 (как в GetStatusIndex)
// Колонка «Готово» (3): по CompletedDate если задано, иначе DateCreated.
// Остальные: по DateCreated.
//
// ВАЖНО: системные даты в Soyuz-PLM читаются строго:
//   - DateCreated  = task.DateCreated         (системное свойство)
//   - CompletedDate = task.GetValue<DateTime>("CompletedDate")
// task.GetString("Created") / task.GetString("CompletedDate") НЕ работают для дат.
private bool IsTaskInPeriod( InfoObject task, int statusIdx,
                             DateTime? from, DateTime? to )
{
    if( !from.HasValue && !to.HasValue ) return true;

    DateTime? d = null;
    if( statusIdx == 3 )
    {
        try
        {
            var cd = task.GetValue<DateTime>( "CompletedDate" );
            if( cd != DateTime.MinValue ) d = cd;
        }
        catch { /* атрибут не задан — fallback на DateCreated */ }
    }
    if( !d.HasValue )
    {
        d = task.DateCreated;
    }
    if( !d.HasValue ) return true;   // нет даты — не отсекаем

    var dt = d.Value.Date;
    if( from.HasValue && dt < from.Value.Date ) return false;
    if( to.HasValue && dt > to.Value.Date ) return false;
    return true;
}
```

### 3.4. Изменения в `BeforeRender`

В блоке формирования `cols[0..3]` перед `cols[statusIdx].Add( task )`:

```csharp
// Считываем фильтр периода один раз перед циклом задач (формат dd.MM.yyyy)
DateTime? periodFrom = null, periodTo = null;
{
    var sFrom = screenObj.PropertyBag["KbPeriodFrom"] as string;
    var sTo   = screenObj.PropertyBag["KbPeriodTo"]   as string;
    if( !string.IsNullOrEmpty( sFrom ) )
    {
        DateTime tmp;
        if( DateTime.TryParseExact( sFrom, "dd.MM.yyyy",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out tmp ) )
            periodFrom = tmp;
    }
    if( !string.IsNullOrEmpty( sTo ) )
    {
        DateTime tmp;
        if( DateTime.TryParseExact( sTo, "dd.MM.yyyy",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out tmp ) )
            periodTo = tmp;
    }
}

// ... внутри цикла задач, после вычисления statusIdx и перед cols[statusIdx].Add:
if( !IsTaskInPeriod( task, statusIdx, periodFrom, periodTo ) ) continue;
```

Также в Liquid-контекст (для подсветки активного фильтра в UI):

```csharp
templateInfo["KbPeriodFrom"] = (screenObj.PropertyBag["KbPeriodFrom"] as string) ?? "";
templateInfo["KbPeriodTo"]   = (screenObj.PropertyBag["KbPeriodTo"]   as string) ?? "";
```

### 3.5. Расширение `DoGetReport`

В существующий `switch( period )` (строки ~1151–1153) добавить ветку:

```csharp
case "custom":
    if( parts.Length >= 3 )
    {
        DateTime tmpFrom, tmpTo;
        if( DateTime.TryParseExact( parts[1], "dd.MM.yyyy",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out tmpFrom ) )
            from = tmpFrom;
        if( DateTime.TryParseExact( parts[2], "dd.MM.yyyy",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out tmpTo ) )
            to = tmpTo;
    }
    break;
```

---

## 4. JavaScript — что и где менять (`kanban.js`)

### 4.1. Состояние

```javascript
var _kbPeriod = { from: "", to: "" };
var _kbPeriodDebounce = 0;
```

### 4.2. Инициализация (вызвать из `kbInitHierarchy`, строка ~694)

```javascript
window.kbPeriodFilterInit = function () {
    var inFrom = document.getElementById("kb-period-from");
    var inTo   = document.getElementById("kb-period-to");
    var btnApply = document.getElementById("kb-period-apply");
    var btnReset = document.getElementById("kb-period-reset");
    if (!inFrom || !inTo) return;

    // Восстановление значения из data-атрибутов, заполненных Liquid
    var dataFrom = inFrom.getAttribute("data-init") || "";
    var dataTo   = inTo.getAttribute("data-init")   || "";
    if (dataFrom) inFrom.value = dataFrom;
    if (dataTo)   inTo.value   = dataTo;
    _kbPeriod.from = dataFrom;
    _kbPeriod.to   = dataTo;

    if (btnApply) btnApply.onclick = function () { kbPeriodApply(); return false; };
    if (btnReset) btnReset.onclick = function () { kbPeriodReset(); return false; };

    var onChangeHandler = function () {
        if (_kbPeriodDebounce) {
            try { window.clearTimeout(_kbPeriodDebounce); } catch (e) {}
        }
        _kbPeriodDebounce = window.setTimeout(function () { kbPeriodApply(); }, 600);
    };
    inFrom.onchange = onChangeHandler;
    inTo.onchange   = onChangeHandler;
};
```

### 4.3. Применение фильтра

```javascript
// inFrom.value / inTo.value уже содержат строку формата "dd.MM.yyyy"
// (заполняется кастомным календарём calToggle). Отправляем на сервер БЕЗ изменений.
window.kbPeriodApply = function () {
    var inFrom = document.getElementById("kb-period-from");
    var inTo   = document.getElementById("kb-period-to");
    if (!inFrom || !inTo) return;

    var sFrom = inFrom.value || "";    // "dd.MM.yyyy" или ""
    var sTo   = inTo.value   || "";    // "dd.MM.yyyy" или ""

    // Валидация порядка: парсим dd.MM.yyyy локально для сравнения
    if (sFrom && sTo) {
        var pf = kbParseDmyToTs(sFrom);
        var pt = kbParseDmyToTs(sTo);
        if (pf > 0 && pt > 0 && pf > pt) {
            alert("Дата «С» должна быть раньше даты «По»");
            return;
        }
    }

    var safeFrom = String(sFrom).replace(/\|/g, "");
    var safeTo   = String(sTo).replace(/\|/g, "");

    try {
        var res = window.external.InvokeTemplate("SetPeriodFilter", safeFrom + "|" + safeTo);
        if (String(res || "").indexOf("ERROR") === 0) {
            alert("Ошибка установки фильтра: " + res);
            return;
        }
        _kbPeriod.from = safeFrom;
        _kbPeriod.to   = safeTo;
        kbRefreshBoard();
    } catch (e) { /* no-op */ }
};

// Хелпер: "dd.MM.yyyy" → timestamp (для локального сравнения порядка)
function kbParseDmyToTs(s) {
    if (!s) return 0;
    var m = String(s).match(/^(\d{2})\.(\d{2})\.(\d{4})$/);
    if (!m) return 0;
    var d = new Date(parseInt(m[3], 10), parseInt(m[2], 10) - 1, parseInt(m[1], 10));
    return d.getTime();
}
```

### 4.4. Сброс фильтра

```javascript
window.kbPeriodReset = function () {
    var inFrom = document.getElementById("kb-period-from");
    var inTo   = document.getElementById("kb-period-to");
    if (inFrom) inFrom.value = "";
    if (inTo)   inTo.value   = "";
    _kbPeriod.from = "";
    _kbPeriod.to   = "";
    try {
        window.external.InvokeTemplate("SetPeriodFilter", "|");
        kbRefreshBoard();
    } catch (e) { /* no-op */ }
};
```

### 4.5. Подвязка инициализации

В конец `kbInitHierarchy`:

```javascript
if (typeof kbPeriodFilterInit === "function") kbPeriodFilterInit();
```

### 4.6. Обновление `calEl()` — поддержка новых ID календарей

В HTML (раздел 5) добавляются два новых `<div class="kb-cal">` с ID `kb-cal-kb-period-from` и `kb-cal-kb-period-to`. Существующая функция `calEl()` в `kanban.js` жёстко привязана к старым ID и не знает про новые. **Обязательное обновление:**

```javascript
// Обновлённая calEl() — поддерживает календари фильтра периода
function calEl() {
    if (_calTargetId === "tcm-duedate") return document.getElementById("kb-cal-tcm");
    if (_calTargetId === "kb-period-from") return document.getElementById("kb-cal-kb-period-from");
    if (_calTargetId === "kb-period-to") return document.getElementById("kb-cal-kb-period-to");
    return document.getElementById("kb-cal");
}
```

### 4.7. Обновление `document.onclick` — закрытие новых календарей по клику вне

Глобальный обработчик `document.onclick` в `kanban.js` жёстко привязан к старым ID. Без обновления клик вне нового календаря не закроет попап. **Обязательное обновление:**

```javascript
// Обновлённый document.onclick — поддерживает все календари проекта
document.onclick = function (e) {
    if (_calSkipClose) { _calSkipClose = false; return; }
    var tgt = e.target || e.srcElement;

    // Массив всех возможных календарей
    var cals = [
        document.getElementById("kb-cal"),
        document.getElementById("kb-cal-tcm"),
        document.getElementById("kb-cal-kb-period-from"),
        document.getElementById("kb-cal-kb-period-to")
    ];

    for (var ci = 0; ci < cals.length; ci++) {
        var cal = cals[ci];
        if (!cal || cal.style.display !== "block") continue;
        var node = tgt;
        var inside = false;
        while (node) {
            if (node === cal) { inside = true; break; }
            if (node.id === "kb-new-duedate" || node.id === "tcm-duedate" || node.id === "kb-period-from" || node.id === "kb-period-to") { inside = true; break; }
            if (node.className && node.className.indexOf("kb-date-btn") !== -1) { inside = true; break; }
            node = node.parentNode;
        }
        if (!inside) cal.style.display = "none";
    }
};
```

> **Совместимость с IE11:** код использует только `var`, `indexOf`, `while` — без `forEach`, `Array.prototype.find`, arrow functions или `classList`.

---

## 5. HTML — что и где менять (`KanbanBoard_HTML.html`)

В верхний тулбар (рядом с блоком иерархии `kb-hier-panel`). **Используем существующий кастомный календарь** через `calToggle('id')` — он уже подключён в проекте (см. `KanbanBoard_HTML.html` строка 552 для срока задачи). Поля `readonly` — пользователь редактирует только через календарь, что исключает ошибки ввода.

```html
<div id="kb-period-wrap" class="kb-period-wrap">
    <span class="kb-period-label">Период:</span>

    <div class="kb-date-wrap" style="display:inline-block; vertical-align:middle;">
        <input type="text"
               id="kb-period-from"
               class="form-control input-sm kb-period-input"
               placeholder="ДД.ММ.ГГГГ"
               data-init="{{ KbPeriodFrom }}"
               readonly
               style="padding-right:28px;" />
        <button type="button"
                class="kb-date-btn"
                onclick="calToggle('kb-period-from')"
                title="Выбрать дату начала">
            <i class="fa fa-calendar"></i>
        </button>
        <div id="kb-cal-kb-period-from" class="kb-cal"></div>
    </div>

    <span class="kb-period-sep">—</span>

    <div class="kb-date-wrap" style="display:inline-block; vertical-align:middle;">
        <input type="text"
               id="kb-period-to"
               class="form-control input-sm kb-period-input"
               placeholder="ДД.ММ.ГГГГ"
               data-init="{{ KbPeriodTo }}"
               readonly
               style="padding-right:28px;" />
        <button type="button"
                class="kb-date-btn"
                onclick="calToggle('kb-period-to')"
                title="Выбрать дату конца">
            <i class="fa fa-calendar"></i>
        </button>
        <div id="kb-cal-kb-period-to" class="kb-cal"></div>
    </div>

    <button type="button" id="kb-period-apply" class="kb-period-btn">Применить</button>
    <button type="button" id="kb-period-reset" class="kb-period-btn kb-period-btn-reset">Сбросить</button>
</div>
```

> DotLiquid: `{{ KbPeriodFrom }}` / `{{ KbPeriodTo }}` — пустая строка если не установлено. Формат значения — `dd.MM.yyyy` (то же, что отдаёт `calToggle`).
> **Важно:** проверить, что `calToggle` пишет в инпут именно строку `dd.MM.yyyy` (а не `yyyy-MM-dd`). Если в проекте формат другой — выровнять контракт парсинга на сервере.

---

## 6. CSS — что и где менять (`kanban.css`)

```css
.kb-period-wrap {
    float: right;
    margin-right: 16px;
    margin-top: 8px;
    display: inline-block;
}
.kb-period-label {
    font-size: 12px;
    color: #4b5563;
    margin-right: 6px;
}
.kb-period-input {
    height: 30px;
    padding: 4px 6px;
    font-size: 12px;
    border: 1px solid #d1d5db;
    border-radius: 6px;
    background: #fff;
    color: #1f2937;
    box-sizing: border-box;
    width: 130px;
}
.kb-period-sep {
    margin: 0 4px;
    color: #6b7280;
}
.kb-period-btn {
    height: 30px;
    padding: 0 10px;
    margin-left: 4px;
    font-size: 12px;
    border: 1px solid #d1d5db;
    border-radius: 6px;
    background: #f3f4f6;
    color: #1f2937;
    cursor: pointer;
}
.kb-period-btn:hover { background: #e5e7eb; }
.kb-period-btn-reset {
    background: #fff;
    color: #6b7280;
}
```

---

## 7. Пошаговый план реализации (атомарные коммиты)

| # | Шаг | Файлы | Smoke-тест | Сообщение коммита |
|---|-----|-------|------------|-------------------|
| 1 | C#: хелпер `IsTaskInPeriod` (без подключения) | `SOYUZ_UPLOAD_KanbanScreen_script.cs` | Стенд собирается, поведение доски без изменений | `feat(kanban): add IsTaskInPeriod helper` |
| 2 | C#: метод `DoSetPeriodFilter` + ветка `case "SetPeriodFilter":` | `SOYUZ_UPLOAD_KanbanScreen_script.cs` | `InvokeTemplate("SetPeriodFilter","01.01.2026|31.01.2026")` → `OK`, в PropertyBag появляются ключи | `feat(kanban): server SetPeriodFilter method` |
| 3 | C#: подключение фильтра в `BeforeRender` + проброс в Liquid | `SOYUZ_UPLOAD_KanbanScreen_script.cs` | После установки периода доска показывает только задачи в периоде | `feat(kanban): clamp tasks by period in BeforeRender` |
| 4 | HTML+CSS: блок фильтра в тулбаре | `KanbanBoard_HTML.html`, `kanban.css` | Поля и кнопки видны | `feat(kanban): period filter UI in toolbar` |
| 5 | JS: `kbPeriodFilterInit` + Apply + Reset, подвязка в `kbInitHierarchy` + **обновление `calEl()` и `document.onclick`** (§4.6, §4.7) | `kanban.js` | Календари фильтра открываются/закрываются корректно, изменение полей → `RefreshBoard` → доска отфильтрована | `feat(kanban): wire period filter client logic + calEl update` |
| 6 | C#: `DoGetReport` ветка `custom|from|to` | `SOYUZ_UPLOAD_KanbanScreen_script.cs` | `InvokeTemplate("GetReport","custom|01.03.2026|31.03.2026")` возвращает данные за март | `feat(kanban): GetReport custom date range` |
| 7 | Документация `docs/06_*.md` | docs | — | `docs(kanban): document period filter` |

---

## 8. Возможные риски и технические ограничения

1. **Кастомный календарь.** `<input type="date">` запрещён (IE11 рендерит как обычный текст). Используем существующий `calToggle('id')` + `<div id="kb-cal-...">` (см. HTML строка 552 для `tcm-duedate`). Поля `readonly` — пользователь не может ввести битую дату вручную. Формат — `dd.MM.yyyy`.
2. **PropertyBag и сессия.** Живёт пока экран открыт. Закрытие/открытие сбрасывает фильтр. Согласовано с бэклогом.
3. **Регрессия со счётчиками `.kb-cnt`.** После применения фильтра `BeforeRender` отдаёт меньше задач — счётчик в Liquid обновится автоматически. При активном поиске (§2.5) сработает `kbRecountColumns` поверх отфильтрованного DOM.
4. **Производительность `BeforeRender`.** Парсинг даты периода — один раз до цикла. На 2000 задач — ~50 мс. Приемлемо.
5. **Отсутствие `CompletedDate`.** Fallback на `Created`. Допустимо.
6. **Часовые пояса.** Все даты считаем в `DateTime.Date`. Часовые пояса не учитываем.
7. **`from > to`.** Защита на JS (alert). Сервер просто вернёт пустую доску.
8. **Защита разделителя `|`.** Дата `dd.MM.yyyy` не содержит `|`, но `replace(/\|/g, "")` оставляем для единообразия.
9. **DotLiquid `{{ KbPeriodFrom }}`.** Передаём строкой через `templateInfo[...] = (string)...`.
10. **Отчёты.** Если UI отчёта не передаёт `custom|from|to` — добавить отдельным коммитом.

---

## 9. Критерии приёмки

- [ ] В верхней панели доски виден блок «Период: [date] — [date] [Применить] [Сбросить]».
- [ ] Установка дат + «Применить» (или 600 мс после изменения) перезагружает доску только с задачами в периоде.
- [ ] Только `from` или только `to` корректно отсекают по одной границе.
- [ ] «Сбросить» возвращает доску в полный режим.
- [ ] Колонка «Готово» фильтруется по `CompletedDate`, остальные — по `Created`.
- [ ] При `from > to` выводится предупреждение, фильтр не применяется.
- [ ] `SetPeriodFilter` возвращает `OK` или `ERROR:Format`.
- [ ] При перезагрузке экрана фильтр сбрасывается.
- [ ] `DoGetReport` поддерживает `custom|from|to`.
- [ ] Документация обновлена.

---

## 10. Тестовые сценарии

### Сценарий 1 — фильтр по созданию

1. Доска с 50 задачами (январь–март 2026).
2. `from=01.02.2026`, `to=28.02.2026`. Применить.
3. Видны только задачи февраля.

### Сценарий 2 — фильтр по завершению

1. T1 создана 15.01.2026, переведена в «Готово» 10.03.2026 (`CompletedDate=10.03.2026`).
2. `from=01.03.2026`, `to=31.03.2026` → T1 видна в «Готово».
3. `from=01.01.2026`, `to=31.01.2026` → T1 не видна (закрыта в марте).

### Сценарий 3 — только нижняя граница

1. `from=01.04.2026`, `to=` пусто → видны задачи с 1 апреля и позже.

### Сценарий 4 — только верхняя граница

1. `from=` пусто, `to=28.02.2026` → видны задачи до конца февраля.

### Сценарий 5 — Сброс

1. Применить фильтр → задачи отфильтрованы.
2. «Сбросить» → все задачи видны, поля очищены.

### Сценарий 6 — `from > to`

1. `from=01.04.2026`, `to=01.01.2026` → alert, фильтр не применён.

### Сценарий 7 — Отчёт за период

1. UI отчёта вызывает `InvokeTemplate("GetReport","custom|01.03.2026|31.03.2026")`.
2. Возвращается JSON задач марта.

### Сценарий 8 — Регрессия `RefreshBoard`

1. Установить период.
2. `F5` (refresh) → доска перезагрузилась, фильтр сохранён (PropertyBag).
3. Закрыть и открыть экран → фильтр сброшен.
