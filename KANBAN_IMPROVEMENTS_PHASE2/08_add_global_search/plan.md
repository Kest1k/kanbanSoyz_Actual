# 08 — Глобальный поиск задач (полностью клиентский)

> **Источник в backlog:** `KANBAN_IMPROVEMENTS_BACKLOG.md` §2.5.
> **Фича:** `add_global_search`.
> **Приоритет:** P1 (Phase 2, задача №4 — последняя).
> **Затронутые файлы:** `scripts/kanban.js`, `scripts/KanbanBoard_HTML.html`, `scripts/kanban.css`. **C# файлы НЕ меняем — серверная часть не нужна.**

---

## 1. Техническое описание задачи

### 1.1. Требования backlog

> - Добавить поле поиска в верхнюю панель.
> - Поиск должен работать по названию, описанию, тегам и тексту комментариев.
> - Фильтрация должна происходить **мгновенно по всем видимым колонкам**.

### 1.2. Архитектурное решение

Поиск **полностью клиентский**. Все карточки доски уже отрисованы в DOM. Серверный поиск (`SearchOperation`) **не нужен** — он только дублирует то, что уже в DOM, и расходует серверные ресурсы.

Принцип:
1. `<input id="kb-board-search">` в тулбаре.
2. Событие `oninput` с **debounce 200 мс**.
3. Перебор всех `.kb-card`, сравнение `textContent` (нижний регистр) с подстрокой.
4. `style.display = 'none'` для не подходящих, `style.display = ''` для подходящих.
5. Пересчёт `.kb-cnt` для каждой колонки в формате `vis / total`.
6. `Esc` или кнопка `×` — очистить.

### 1.3. Поля, по которым ищем

`textContent` карточки уже включает:
- `TaskName` (заголовок).
- `TaskDetails` (описание).
- `Tags` (бейджи).
- `Assignee` (имя в бейдже).
- `Creator` (подпись «Поручил: …»).

После выполнения 2.7 и 2.8 в DOM карточки также появятся:
- Бейдж подзадач (например `☑ 3/5`) — числовой, индексирует «есть ли подзадачи».
- Бейдж заметок (например `📝 2`) — числовой.

**Комментарии**: backlog заявляет поиск по комментариям, но они **не в DOM карточки** — текст живёт только в `CommentsJSON` и подгружается только при открытии модалки. В Phase 2 принимаем компромисс: поиск работает по текстовым полям карточки. Поиск по комментариям откладываем (Phase 3 — отдельная задача с серверным методом).

### 1.4. Что НЕ делаем

- **Не создаём** серверный метод `DoSearchTasks` — нет надобности.
- **Не делаем** баннер «Найдено ещё N задач вне фильтра» — не нужен при чистом клиенте.
- **Не ищем** в `CommentsJSON` — Phase 3.
- **Не подсвечиваем** совпадения внутри карточки (mark/highlight) — Phase 3.
- **Не сохраняем** строку поиска при `RefreshBoard` — Phase 3.

---

## 2. Затронутые файлы

| Путь | Что меняем |
|------|------------|
| `scripts/kanban.js` | Новый блок `kbBoardSearch*` + универсальный хелпер `kbRecountColumns` (используется и в §2.6 — если уже добавлен там, проверить, не дублировать). Подвязка к `kbInitHierarchy`. |
| `scripts/KanbanBoard_HTML.html` | Поле `<input id="kb-board-search">` + кнопка очистки в верхнем тулбаре. |
| `scripts/kanban.css` | Стили инпута поиска. |

> **C# файлы не трогаем.**

---

## 3. C# — что и где менять

**Ничего.** Эта задача чисто клиентская.

> Если в будущем потребуется поиск по комментариям (Phase 3), там и появится `DoSearchTasks`. Сейчас сервер не задействован.

---

## 4. JavaScript — что и где менять (`kanban.js`)

### 4.1. Состояние

```javascript
var _kbBoardSearch = { q: "", debounceId: 0 };
```

### 4.2. Инициализация (вызывается из `kbInitHierarchy`, строка ~694)

```javascript
window.kbBoardSearchInit = function () {
    var inp = document.getElementById("kb-board-search");
    if (!inp) return;

    inp.oninput = function () { kbBoardSearchOnInput(this.value); };
    inp.onkeydown = function (e) {
        var code = e.keyCode || e.which;
        if (code === 27) {           // Esc
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
    if (inp) inp.blur();
};
```

### 4.4. Обработчик ввода с debounce 200 мс

```javascript
function kbBoardSearchOnInput(val) {
    _kbBoardSearch.q = (val || "").toLowerCase();
    if (_kbBoardSearch.debounceId) {
        try { window.clearTimeout(_kbBoardSearch.debounceId); } catch (e) {}
    }
    var q = _kbBoardSearch.q;
    _kbBoardSearch.debounceId = window.setTimeout(function () {
        kbBoardSearchApplyClient(q);
    }, 200);
}
```

### 4.5. Главная функция фильтрации

```javascript
function kbBoardSearchApplyClient(q) {
    var board = document.getElementById("kb-board");
    if (!board) return;
    var cards = board.getElementsByClassName("kb-card");
    var i, c, blob, hide;

    if (!q) {
        for (i = 0; i < cards.length; i++) cards[i].style.display = "";
        kbRecountColumns();
        return;
    }

    for (i = 0; i < cards.length; i++) {
        c = cards[i];
        // textContent — IE9+, fallback innerText на всякий случай
        blob = (c.textContent || c.innerText || "").toLowerCase();
        hide = blob.indexOf(q) < 0;
        c.style.display = hide ? "none" : "";
    }
    kbRecountColumns();
}
```

### 4.6. Универсальный пересчёт счётчиков `.kb-cnt`

> **Важно:** этот хелпер также используется задачей §2.6 (фильтр периода) если был добавлен там. Перед добавлением проверить, не существует ли он уже в `kanban.js`. Если да — не дублировать, переиспользовать.

```javascript
window.kbRecountColumns = function () {
    var col, body, vis, total, cards, i, cnt;
    for (col = 0; col < 4; col++) {
        body = document.getElementById("kb-body-" + col);
        if (!body) continue;
        cards = body.getElementsByClassName("kb-card");
        total = cards.length;
        vis = 0;
        for (i = 0; i < cards.length; i++) {
            if (cards[i].style.display !== "none") vis++;
        }
        cnt = document.querySelector("#kb-col-" + col + " .kb-cnt");
        if (!cnt) continue;
        if (vis === total) {
            cnt.innerHTML = String(total);
        } else {
            cnt.innerHTML = vis + " / " + total;
        }
    }
};
```

### 4.7. Подвязка инициализации

В конец `kbInitHierarchy`:

```javascript
if (typeof kbBoardSearchInit === "function") kbBoardSearchInit();
```

---

## 5. HTML — что и где менять (`KanbanBoard_HTML.html`)

В верхний тулбар (рядом с блоком фильтра периода `kb-period-wrap` из §2.6):

```html
<div id="kb-board-search-wrap" class="kb-board-search-wrap">
    <input type="text"
           id="kb-board-search"
           class="kb-board-search-input"
           placeholder="🔍 Поиск по доске"
           autocomplete="off">
    <button type="button"
            id="kb-board-search-clear"
            class="kb-board-search-clear"
            title="Очистить (Esc)">×</button>
</div>
```

> Поле поиска лучше разместить **после** блока периода, потому что период — глобальный фильтр (перерисовывает доску), а поиск — локальный (фильтрует уже отрисованное).

---

## 6. CSS — что и где менять (`kanban.css`)

```css
.kb-board-search-wrap {
    float: right;
    margin-right: 16px;
    margin-top: 8px;
    position: relative;
    display: inline-block;
}
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
    position: absolute;
    right: 4px;
    top: 50%;
    transform: translateY(-50%);
    background: transparent;
    border: 0;
    color: #6b7280;
    font-size: 16px;
    cursor: pointer;
    line-height: 1;
}
.kb-board-search-clear:hover { color: #1f2937; }
```

---

## 7. Пошаговый план реализации (атомарные коммиты)

| # | Шаг | Файлы | Smoke-тест | Сообщение коммита |
|---|-----|-------|------------|-------------------|
| 1 | HTML+CSS: инпут поиска и кнопка `×` в тулбаре | `KanbanBoard_HTML.html`, `kanban.css` | Поле и кнопка видны, не ломают вёрстку | `feat(kanban): board search input in toolbar` |
| 2 | JS: универсальный хелпер `kbRecountColumns` (если ещё не существует от §2.6) | `kanban.js` | Вызов в консоли пересчитывает `.kb-cnt` | `feat(kanban): shared kbRecountColumns helper` |
| 3 | JS: `kbBoardSearchInit` / `Clear` / `OnInput` / `ApplyClient` | `kanban.js` | Ввод фильтрует карточки, Esc и `×` сбрасывают | `feat(kanban): client-side board search with debounce` |
| 4 | JS: подвязка `kbBoardSearchInit` в конец `kbInitHierarchy` | `kanban.js` | После `RefreshBoard` поиск работает заново | `feat(kanban): wire search init into hierarchy bootstrap` |
| 5 | Документация `docs/05_*.md` | docs | — | `docs(kanban): document client board search` |

---

## 8. Возможные риски и технические ограничения

1. **IE11 + 200+ карточек.** `oninput` без debounce заметно лагает. Митигация — `setTimeout` 200 мс. Если на стенде > 500 карточек, поднять до 300 мс.
2. **`textContent` в IE11**: поддерживается с IE9. Fallback `innerText` оставлен на всякий случай.
3. **Регистрозависимость**: `toLowerCase()` обеих сторон. Кириллица работает корректно.
4. **Защита разделителя `|`**: для клиентского поиска не нужна — нет вызова `InvokeTemplate`. Это бонус чистой архитектуры.
5. **Конфликт со счётчиком из `kbRefreshBoard`**: при `RefreshBoard` Liquid пересоздаёт DOM, инициализация `kbBoardSearchInit` вызывается заново, поле очищено — фильтр сбрасывается естественно.
6. **XSS в инпуте**: ничего не выводится в DOM из значения инпута, кроме самого `value`. Безопасно.
7. **Поиск по комментариям отложен**: пользователь увидит, что задача с искомой подстрокой только в комментариях не находится. Митигация — плейсхолдер «Поиск по доске» (без обещания «по комментариям»). В release-notes явно указать ограничение.
8. **Drag&Drop конфликт**: карточки со `style.display = 'none'` не участвуют в drop-zones. Регрессия не ожидается, но проверить smoke-тест на drag в активном фильтре.
9. **Кнопка `×`**: `position: absolute` внутри `position: relative` обёртки. Проверить отсутствие визуального наезда на скроллбар.
10. **Совместимость с фильтром периода (§2.6).** При активном периоде `BeforeRender` уже отдал меньше карточек. Поиск работает поверх отфильтрованного DOM. Счётчик `kbRecountColumns` показывает `vis / total`, где `total` — после фильтра периода (не «все задачи в БД»). Это согласовано с UX.
11. **Бейджи подзадач/заметок (§2.7/§2.8).** Их `textContent` в виде «3/5», «📝 2» попадает в поиск. Это побочный эффект, в основном безвредный (поиск по `2` найдёт задачи с 2 заметками — приемлемо).
12. **Существование `kbRecountColumns`.** Если §2.6 уже добавила хелпер — переиспользовать. Если нет — добавить здесь. Идемпотентность через `window.kbRecountColumns = ...` (последнее присвоение выигрывает).

---

## 9. Критерии приёмки

- [ ] В верхней панели доски виден инпут `🔍 Поиск по доске` и кнопка `×`.
- [ ] Ввод подстроки (≥1 символа) через 200 мс мгновенно скрывает не подходящие карточки.
- [ ] Счётчик каждой колонки `.kb-cnt` показывает `vis / total` при активном фильтре и `total` при пустом.
- [ ] `Esc` в поле очищает фильтр.
- [ ] Кнопка `×` очищает фильтр.
- [ ] Поиск регистронезависим, работает на кириллице и латинице.
- [ ] Поиск ищет по: заголовок, описание, теги, исполнитель, создатель.
- [ ] Никаких сетевых вызовов через `InvokeTemplate` при поиске (проверить трассу).
- [ ] В коде C# нет упоминаний `DoSearchTasks` / `SearchTasks`.
- [ ] Совместим с фильтром периода (§2.6) и подзадачами/заметками (§2.7/§2.8).
- [ ] Документация `docs/05_*.md` обновлена.

---

## 10. Тестовые сценарии

### Сценарий 1 — мгновенный фильтр

1. Открыть доску с 30+ карточками.
2. Ввести `тест` → через 200 мс видны только карточки с подстрокой «тест».
3. Счётчик колонки `3 / 12`.

### Сценарий 2 — очистка по Esc

1. Ввести подстроку, дождаться фильтрации.
2. `Esc` → все карточки видны, поле пусто, счётчики = total.

### Сценарий 3 — очистка по `×`

1. Ввести подстроку.
2. Кликнуть `×` → то же поведение, что Esc.

### Сценарий 4 — поиск по тегам и исполнителю

1. Ввести фамилию `Иванов` → видны только карточки, где Иванов в исполнителе или создателе.
2. Ввести имя тега → видны карточки с этим тегом.

### Сценарий 5 — кириллица

1. Ввести `БЮДЖЕТ` → найдены карточки со словом «бюджет», «Бюджет», «БЮДЖЕТ».

### Сценарий 6 — регрессия Drag&Drop

1. Применить фильтр (видны 3 карточки).
2. Перетащить видимую карточку в другую колонку.
3. Перенос успешен, счётчики обновились.

### Сценарий 7 — регрессия RefreshBoard

1. Применить фильтр.
2. Нажать `F5` (refresh из Phase 1) → доска перерисована, поле очищено, фильтр сброшен.

### Сценарий 8 — отсутствие вызовов сервера

1. Открыть Soyuz-PLM trace / network в IE.
2. Ввести и стереть подстроку 5 раз → в логах нет `InvokeTemplate("SearchTasks", ...)`.

### Сценарий 9 — совместимость с фильтром периода

1. Применить период «март 2026» → доска отфильтрована (например 12 карточек).
2. Ввести `бюджет` → среди этих 12 видны только с «бюджет» (например 2).
3. Счётчик `2 / 12`.

### Сценарий 10 — поиск по числу подзадач

1. Карточка имеет бейдж `☑ 3/5`.
2. Ввести `3/5` → карточка найдена (по `textContent` бейджа). Приемлемо.
