# Фича 01 – Умный фильтр (приоритет, теги, текст)

**Тип:** только клиент (`kanban.js`, `KanbanBoard_HTML.html`, `kanban.css`). Сервер и схема данных не меняются.
**Риск:** низкий. Фильтр работает поверх уже отрисованных карточек, ничего не записывает.

---

## 1. Поведение

В тулбаре доски появляется панель фильтра:
- **Приоритет** – чипы-переключатели: Сверхсрочная / Высокий / Средний / Низкий (множественный выбор).
- **Теги** – выпадающий список (наполняется из тегов, реально присутствующих на карточках) + множественный выбор.
- **Текст** – поле живого поиска по названию и описанию карточки.
- **Сброс** – очищает фильтр; счётчик «показано N из M».

Фильтрация – мгновенная, на клиенте: проходим по всем `.kb-card`, считаем совпадение, ставим `display:none/""`. После `kbRefreshBoard()` (полная перезагрузка доски) фильтр восстанавливается из `sessionStorage`.

> Отличие от фичи 03 (Поиск): фильтр работает только по **уже загруженным/видимым** карточкам текущего вида доски и не лезет на сервер. Поиск (фича 03) ищет по всей базе, включая вложения.

---

## 2. Опорные точки в коде (на 09.06.2026)

- `KanbanBoard_HTML.html`:
  - Тулбар / кластер иерархии: строки **13–40** (есть `kb-hier-panel` @21, селекты dept/sector/user @33–39).
  - Карточка задачи (4 блока): Todo **@308**, InProgress **@395**, Waiting **@482**, Done **@563**.
    Класс карточки: `kb-card pri-{{ t.priority }}`, id `kbc_{{ t.id }}`. Внутри: `.kb-card-title` (название), `.kb-card-details[data-full]` (описание), `.kb-card-tags` (чипы тегов).
- `kanban.js`: функции тулбара объявляются как `window.имя = function(){}`. Инициализация иерархии – `kbInitHierarchy()` (вызывается в самом конце, ориентир @3648).

**Проблема:** приоритет на карточке закодирован классом `pri-urgent|pri-high|pri-medium|pri-low`, тегов в виде data-атрибута нет. Чтобы фильтрация была надёжной и не зависела от вёрстки, добавим на карточку явные data-атрибуты `data-priority` и `data-tags`.

---

## 3. Шаг 1. Добавить data-атрибуты на карточку (HTML)

В **каждом из 4 блоков** карточки (`@308`, `@395`, `@482`, `@563`) в открывающий `<div class="kb-card ...">` добавить два атрибута:

```html
data-priority="{{ t.priority }}" data-tags="{{ t.tags }}" data-title="{{ t.title }}"
```

Пример для блока Todo (было):
```html
<div class="kb-card pri-{{ t.priority }}{% if t.isNew == '1' %} kb-card-new{% endif %}{% if t.isPrivate == '1' %} kb-card-private-bg{% endif %}" id="kbc_{{ t.id }}"
```
стало:
```html
<div class="kb-card pri-{{ t.priority }}{% if t.isNew == '1' %} kb-card-new{% endif %}{% if t.isPrivate == '1' %} kb-card-private-bg{% endif %}" id="kbc_{{ t.id }}"
     data-priority="{{ t.priority }}" data-tags="{{ t.tags }}" data-title="{{ t.title }}"
```

> `t.priority`, `t.tags`, `t.title` уже передаются в карточку из `BuildCardData` на сервере (поля `priority`, `tags`, `title` – см. блокнот `Kanban`, раздел BuildCardData). Никаких серверных правок не требуется.

---

## 4. Шаг 2. Разметка панели фильтра (HTML)

Вставить сразу **после** `kb-hier-panel` (после строки ~40, до контейнера колонок доски):

```html
<!-- ░░ Умный фильтр (фича 01) ░░ -->
<div id="kb-filter-bar" class="kb-filter-bar">
    <span class="kb-filter-lbl">Фильтр:</span>

    <div class="kb-filter-prio" id="kb-filter-prio">
        <span class="kb-fp" data-p="urgent" onclick="kbFltTogglePrio('urgent')">Сверхсрочная</span>
        <span class="kb-fp" data-p="high"   onclick="kbFltTogglePrio('high')">Высокий</span>
        <span class="kb-fp" data-p="medium" onclick="kbFltTogglePrio('medium')">Средний</span>
        <span class="kb-fp" data-p="low"    onclick="kbFltTogglePrio('low')">Низкий</span>
    </div>

    <select id="kb-filter-tag" class="kb-filter-tag" onchange="kbFltApply()">
        <option value="">Все теги</option>
    </select>

    <input type="text" id="kb-filter-text" class="kb-filter-text"
           placeholder="&#128269; по названию и описанию..." onkeyup="kbFltApply()">

    <span id="kb-filter-count" class="kb-filter-count"></span>
    <a href="#" class="kb-filter-reset" onclick="kbFltReset(); return false;">Сбросить</a>
</div>
```

---

## 5. Шаг 3. Логика фильтра (kanban.js)

Добавить блок в `kanban.js` (где угодно среди других `window.*`-функций, например рядом с функциями иерархии). Весь код – ES5, совместим с IE11.

```javascript
/* ░░ Умный фильтр (фича 01) ░░ */
var _kbFltPrio = {};   // { urgent:true, ... } активные приоритеты
var _kbFltKey  = "kbFilterState";

window.kbFltTogglePrio = function (p) {
    if (_kbFltPrio[p]) { delete _kbFltPrio[p]; } else { _kbFltPrio[p] = true; }
    var el = document.querySelector('#kb-filter-prio .kb-fp[data-p="' + p + '"]');
    if (el) el.className = "kb-fp" + (_kbFltPrio[p] ? " kb-fp-on" : "");
    kbFltApply();
};

function kbFltCollectTags() {
    // Собираем уникальные теги со всех карточек и наполняем select
    var sel = document.getElementById("kb-filter-tag");
    if (!sel) return;
    var seen = {}, cards = document.querySelectorAll(".kb-card"), i, j, raw, arr;
    for (i = 0; i < cards.length; i++) {
        raw = cards[i].getAttribute("data-tags") || "";
        if (!raw) continue;
        arr = raw.split(",");
        for (j = 0; j < arr.length; j++) {
            var t = arr[j].replace(/^\s+|\s+$/g, "");
            if (t) seen[t] = true;
        }
    }
    var cur = sel.value;
    sel.options.length = 1; // оставить «Все теги»
    var keys = [], k;
    for (k in seen) { if (seen.hasOwnProperty(k)) keys.push(k); }
    keys.sort();
    for (i = 0; i < keys.length; i++) {
        var o = document.createElement("option");
        o.value = keys[i]; o.text = keys[i];
        sel.appendChild(o);
    }
    sel.value = cur; // восстановить выбор, если ещё существует
}

window.kbFltApply = function () {
    var tagSel = document.getElementById("kb-filter-tag");
    var txtEl  = document.getElementById("kb-filter-text");
    var tag = tagSel ? tagSel.value : "";
    var txt = txtEl ? txtEl.value.replace(/^\s+|\s+$/g, "").toLowerCase() : "";

    var anyPrio = false, p;
    for (p in _kbFltPrio) { if (_kbFltPrio.hasOwnProperty(p)) { anyPrio = true; break; } }

    var cards = document.querySelectorAll(".kb-card");
    var shown = 0, total = cards.length, i;
    for (i = 0; i < cards.length; i++) {
        var c = cards[i];
        var ok = true;

        if (anyPrio) {
            var cp = c.getAttribute("data-priority") || "";
            if (!_kbFltPrio[cp]) ok = false;
        }
        if (ok && tag) {
            var ct = "," + (c.getAttribute("data-tags") || "") + ",";
            if (ct.indexOf("," + tag + ",") < 0) ok = false;
        }
        if (ok && txt) {
            var hay = ((c.getAttribute("data-title") || "") + " " +
                       (c.getAttribute("data-full") || "")).toLowerCase();
            // data-full лежит на .kb-card-details, поэтому добираем и из него:
            var det = c.querySelector(".kb-card-details");
            if (det) hay += " " + ((det.getAttribute("data-full") || det.textContent || "")).toLowerCase();
            if (hay.indexOf(txt) < 0) ok = false;
        }

        c.style.display = ok ? "" : "none";
        if (ok) shown++;
    }

    var cnt = document.getElementById("kb-filter-count");
    if (cnt) cnt.innerHTML = (anyPrio || tag || txt) ? ("Показано " + shown + " из " + total) : "";

    kbFltSave();
};

window.kbFltReset = function () {
    _kbFltPrio = {};
    var chips = document.querySelectorAll("#kb-filter-prio .kb-fp");
    for (var i = 0; i < chips.length; i++) chips[i].className = "kb-fp";
    var tagSel = document.getElementById("kb-filter-tag"); if (tagSel) tagSel.value = "";
    var txtEl  = document.getElementById("kb-filter-text"); if (txtEl) txtEl.value = "";
    kbFltApply();
};

function kbFltSave() {
    try {
        var tagSel = document.getElementById("kb-filter-tag");
        var txtEl  = document.getElementById("kb-filter-text");
        var st = { prio: _kbFltPrio,
                   tag: tagSel ? tagSel.value : "",
                   txt: txtEl ? txtEl.value : "" };
        if (window.sessionStorage) sessionStorage.setItem(_kbFltKey, JSON.stringify(st));
    } catch (e) {}
}

function kbFltInit() {
    kbFltCollectTags();
    // восстановить состояние после refresh
    try {
        if (window.sessionStorage) {
            var s = sessionStorage.getItem(_kbFltKey);
            if (s) {
                var st = JSON.parse(s);
                _kbFltPrio = st.prio || {};
                var p;
                for (p in _kbFltPrio) {
                    if (!_kbFltPrio.hasOwnProperty(p)) continue;
                    var el = document.querySelector('#kb-filter-prio .kb-fp[data-p="' + p + '"]');
                    if (el) el.className = "kb-fp kb-fp-on";
                }
                var tagSel = document.getElementById("kb-filter-tag");
                if (tagSel && st.tag) tagSel.value = st.tag;
                var txtEl = document.getElementById("kb-filter-text");
                if (txtEl && st.txt) txtEl.value = st.txt;
            }
        }
    } catch (e) {}
    kbFltApply();
}
```

**Подключить инициализацию:** найти место, где в конце инициализации вызывается `kbInitHierarchy();` (ориентир `kanban.js` @3648) и **сразу после** добавить:
```javascript
    if (typeof kbFltInit === "function") kbFltInit();
```

---

## 6. Шаг 4. Стили (kanban.css)

Добавить в конец `kanban.css`:

```css
/* ░░ Умный фильтр (фича 01) ░░ */
.kb-filter-bar{display:flex;align-items:center;flex-wrap:wrap;gap:6px;
    padding:6px 10px;border-bottom:1px solid #e3e3e3;background:#fafafa;font-size:12px;}
.kb-filter-lbl{font-weight:600;color:#555;margin-right:4px;}
.kb-filter-prio{display:inline-flex;gap:4px;}
.kb-fp{padding:2px 8px;border:1px solid #ccc;border-radius:10px;cursor:pointer;
    user-select:none;background:#fff;color:#444;}
.kb-fp:hover{border-color:#999;}
.kb-fp-on{background:#2d7ff9;border-color:#2d7ff9;color:#fff;}
.kb-filter-tag,.kb-filter-text{height:24px;border:1px solid #ccc;border-radius:4px;
    padding:0 6px;font-size:12px;}
.kb-filter-text{min-width:200px;}
.kb-filter-count{color:#777;margin-left:4px;}
.kb-filter-reset{margin-left:auto;color:#2d7ff9;text-decoration:none;}
.kb-filter-reset:hover{text-decoration:underline;}
```

---

## 7. Edge cases / на что смотреть

- **IE11**: код уже на `var`/`function`, без `=>`/`const`. `Array`-методы `querySelectorAll`, `String.indexOf`, `JSON` – поддерживаются IE11.
- **`sessionStorage`** в IE11 WebBrowser доступен; если по какой-то причине отключён – обёрнуто в `try/catch`, фильтр просто не запомнит состояние.
- **Колонка Done** имеет другую вёрстку метаданных, но класс `.kb-card` и `data-*` одинаковые – фильтр работает единообразно.
- **Перемещение карточки при активном фильтре**: чтобы фильтр не сбрасывался, в `kbDoMoveTask` при активном фильтре (`kbFltActive()`) карточка переносится в целевую колонку прямо в DOM (`kbMoveCardDom`) без `kbRefreshBoard()` – фильтр остаётся, без мерцания. Без фильтра поведение прежнее (полный refresh: пересортировка, даты завершения). Причина выбора: `sessionStorage` на кастом-протоколе `pmsz-plm:` в IE11 часто недоступен, поэтому надёжнее не перезагружать доску, чем надеяться на восстановление состояния.
- **Счётчики колонок** (`.kb-cnt` в шапке) при DOM-переносе пересчитываются через `kbUpdateColCounts()` (число `.kb-card` в каждом `kb-body-X`).
- **Нюанс колонки «Готово»**: при переносе В/ИЗ «Готово» в режиме фильтра карточка сохраняет «активную» вёрстку (без строки «Завершено …») до следующей полной перезагрузки. Статус и даты сохранены на сервере, визуально нормализуется после сброса фильтра.
- **Описание** длинное хранится в `.kb-card-details[data-full]`; код добирает текст и оттуда.

---

## 8. Смок-тест

- [ ] Панель фильтра видна под тулбаром, не ломает вёрстку на узком экране.
- [ ] Клик по чипу приоритета подсвечивает его и оставляет на доске только карточки этого приоритета; повторный клик снимает.
- [ ] Несколько приоритетов одновременно = показываются карточки любого из выбранных (ИЛИ).
- [ ] Список тегов наполнен реальными тегами с карточек; выбор тега оставляет только карточки с этим тегом.
- [ ] Текстовый поиск прячет карточки, где нет подстроки ни в названии, ни в описании (регистронезависимо).
- [ ] Комбинация приоритет + тег + текст работает как И (пересечение).
- [ ] «Показано N из M» корректно.
- [ ] «Сбросить» очищает всё, показываются все карточки.
- [ ] После «Обновить» (F5) фильтр восстановился из sessionStorage (если стораж доступен).
- [ ] **При активном фильтре** перетаскивание карточки между колонками НЕ сбрасывает фильтр: доска не перезагружается, карточка переезжает, счётчики колонок (`.kb-cnt`) обновляются.
- [ ] Без активного фильтра перетаскивание работает как раньше (полная перезагрузка, корректная сортировка и дата завершения).
- [ ] Drag and drop карточек по-прежнему работает (фильтр не мешает DnD).

---

## 9. Откат

Удалить блок `kb-filter-bar` из HTML, блок «Умный фильтр» из `kanban.js` и `kanban.css`, убрать `data-priority/data-tags/data-title` из 4 карточек и вызов `kbFltInit()`. Перекомпилировать.
