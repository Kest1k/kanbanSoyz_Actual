# 02 — Горячие клавиши на канбан-доске

> **Источник в backlog:** `KANBAN_IMPROVEMENTS_BACKLOG.md` §1.2.
> **Фича:** `add_hotkeys`
> **Приоритет:** P0 (Phase 1)
> **Затронутые файлы:** `scripts/kanban.js`, `scripts/KanbanBoard_HTML.html`, `scripts/kanban.css`.

---

## 1. Техническое описание задачи

В backlog зафиксированы 4 хоткея, плюс дополнительно добавлены по результатам эксплуатации:

| Хоткей | Контекст | Действие |
|--------|----------|----------|
| `F5` | глобально | Полное обновление доски (`kbRefreshBoard`) — с жёсткой блокировкой перезагрузки страницы |
| `F1` | глобально, вне полей ввода | Открыть / закрыть справку (`openHelp` / `closeHelp`) — с блокировкой стандартной справки Windows/IE |
| `Esc` | глобально | Закрыть верхний открытый слой: Что нового → Справка → Отчёт → Карточка → Панель создания |
| `Enter` | внутри открытой карточки задачи (Task modal) | Сохранить изменения (`tcmSave`) — **без закрытия** карточки |
| `Ctrl+S` | внутри открытой карточки задачи (Task modal) | Сохранить изменения и закрыть карточку (`tcmSave` + `tcmClose`) — дублирует кнопку «Сохранить» |
| `Ctrl+Enter` | в поле комментария | Отправить комментарий — **уже реализовано** ([kanban.js:530–535](scripts/kanban.js#L530)), оставляем как есть |
| `N` | глобально, вне полей ввода | Открыть панель создания задачи (`showCreateTask`) |

### Поведение и правила

1. **`F5`**: перехватываем браузерный `F5`, чтобы **не** перезагружать всю PLM-страницу. Вместо этого вызываем `kbRefreshBoard()` (синхронно перерисует доску через `Invoke("Refresh", null)`). Для IE11 WebBrowser Control **обязательно** `e.keyCode = 0; e.returnValue = false;` — иначе будет белый миг.
2. **`F1`**: вызывает `openHelp()` / `closeHelp()`. Блокируем стандартную справку Windows/IE через `e.keyCode = 0; e.returnValue = false;`. Срабатывает только если фокус **не** в поле ввода. Если справка уже открыта — закрывает. Не срабатывает если открыты другие модалки (отчёт, карточка, что нового).
3. **`Esc`**: глобальный «стек закрытий» с учётом z-index. Порядок: whatsNewOverlay (10002) → helpOverlay (10002) → tcmOverlay (10001) → reportOverlay (10000) → create panel. Если фокус в поле ввода — первый `Esc` снимает фокус (blur), второй закрывает верхнюю модалку.
4. **`Enter` (без модификаторов) внутри Task modal**: вызывает `tcmSave()`. Должен работать когда фокус в `<input>`, но **не** в `<textarea>` (там Enter — это перенос строки). Также **не** перехватывается в поле чата (`tcm-chat-text` — там свой обработчик). Карточка сохраняется, но **не закрывается** (появляется зелёная плашка «Сохранено!»).
5. **`Ctrl+S` внутри Task modal**: сохраняет задачу (`tcmSave()`) и закрывает карточку (`tcmClose()`). Работает из любого поля, включая `<textarea>`. Блокируем стандартный диалог «Сохранить страницу» через `e.preventDefault()`.
6. **`Ctrl+Enter` в `tcm-chat-text`**: уже есть в `tcmChatKeydown` — не трогаем.
7. **`N` (без модификаторов) глобально**: вызывает `showCreateTask()`. Срабатывает только если фокус **не** в поле ввода (`kbIsTypingTarget` → false) и ни одна модалка не открыта.
8. **IE11**: только `keyCode/which`, никаких `event.key`. Для F5 и F1 — жёсткая блокировка через `e.keyCode = 0; e.returnValue = false;`.

### Что НЕ делаем (вынесено в Phase 2)

- `1`–`4` для быстрого перемещения карточки в колонку (сложно, нужен контекст выбранной карточки).
- `/` для поиска — связано с фичей 05, активируем там.
- `Delete` для удаления задачи (опасный хоткей, легко нажать случайно — оставляем только через кнопку).

---

## 2. Затронутые файлы

| Путь | Что меняем |
|------|------------|
| `scripts/kanban.js` | Новый блок `kbHotkeys*`: `kbOnGlobalKeydown`, `kbHotkeyEscape`, `kbIsTypingTarget`, `kbIsTextareaTarget`, `kbIsOverlayVisible`, регистрация `document.addEventListener("keydown", ...)` |
| `scripts/KanbanBoard_HTML.html` | В блоке Help-модала (`helpOverlay`) добавить таблицу хоткеев |
| `scripts/kanban.css` | Стили `<kbd>` для красивой подачи в Help |

C# не трогаем.

---

## 3. JavaScript — что и где менять (`kanban.js`)

В конец IIFE (перед `})();`):

```javascript
// ── Hotkeys: глобальные горячие клавиши доски ───────────────────────
function kbIsTypingTarget(target) {
    if (!target || !target.tagName) return false;
    var tag = String(target.tagName).toLowerCase();
    if (tag === "input" || tag === "textarea" || tag === "select") return true;
    if (target.isContentEditable) return true;
    return false;
}

function kbIsTextareaTarget(target) {
    if (!target || !target.tagName) return false;
    return String(target.tagName).toLowerCase() === "textarea";
}

// Вспомогательная функция проверки видимости оверлея
function kbIsOverlayVisible(id) {
    var el = document.getElementById(id);
    return !!(el && el.className && el.className.indexOf("visible") !== -1);
}

function kbIsCreatePanelOpen() {
    var el = document.getElementById("kb-create-panel");
    return !!(el && el.style.display && el.style.display !== "none");
}

function kbHotkeyEscape() {
    // Порядок закрытия зависит от z-index (закрываем сверху вниз)
    if (kbIsOverlayVisible("whatsNewOverlay")) { closeWhatsNew();  return; } // z-index 10002
    if (kbIsOverlayVisible("helpOverlay"))     { closeHelp();      return; } // z-index 10002
    if (kbIsOverlayVisible("tcmOverlay"))      { tcmClose();       return; } // z-index 10001
    if (kbIsOverlayVisible("reportOverlay"))   { closeReport();    return; } // z-index 10000
    if (kbIsCreatePanelOpen())                 { hideCreateTask(); return; }
}

function kbOnGlobalKeydown(e) {
    if (!e) e = window.event;
    var target = e.target || e.srcElement;
    var code   = e.keyCode || e.which;

    // ── F5: блокируем перезагрузку, вызываем kbRefreshBoard ─────────
    if (code === 116) {
        if (e.preventDefault) e.preventDefault();
        if (e.stopPropagation) e.stopPropagation();
        e.keyCode = 0; // Жесткая блокировка для IE WebBrowser Control
        e.returnValue = false;

        if (typeof kbRefreshBoard === "function") kbRefreshBoard();
        return false;
    }

    // ── F1: открыть/закрыть справку ──────────────────────────────────
    if (code === 112) {
        if (e.preventDefault) e.preventDefault();
        e.keyCode = 0; // Блокируем стандартную справку Windows/IE
        e.returnValue = false;

        if (!kbIsTypingTarget(target) && !kbIsOverlayVisible("reportOverlay") && !kbIsOverlayVisible("whatsNewOverlay") && !kbIsOverlayVisible("tcmOverlay") && !kbIsCreatePanelOpen()) {
            if (kbIsOverlayVisible("helpOverlay")) { closeHelp(); }
            else { openHelp(); }
        }
        return false;
    }

    // ── Esc: глобальный стек закрытий ───────────────────────────────
    if (code === 27) {
        if (kbIsTypingTarget(target)) {
            // Снимаем фокус с поля. Модалку закрывать не торопимся.
            try { target.blur(); } catch (er) {}
        } else {
            kbHotkeyEscape();
            if (e.preventDefault) e.preventDefault();
        }
        return;
    }

    // ── Enter в открытой карточке задачи: сохранить (без закрытия) ──
    if (code === 13 && !e.shiftKey && !e.altKey && !e.metaKey && !e.ctrlKey) {
        if (kbIsOverlayVisible("tcmOverlay") && !kbIsTextareaTarget(target)) {
            // Не перехватываем Enter, если фокус в чате (там свой обработчик)
            if (target && target.id === "tcm-chat-text") return;

            if (typeof tcmSave === "function") tcmSave();
            if (e.preventDefault) e.preventDefault();
            return;
        }
    }

    // ── Ctrl+S в открытой карточке: сохранить и закрыть ─────────────
    if (code === 83 && e.ctrlKey && !e.shiftKey && !e.altKey) {
        if (kbIsOverlayVisible("tcmOverlay")) {
            if (e.preventDefault) e.preventDefault();
            if (typeof tcmSave === "function") tcmSave();
            if (typeof tcmClose === "function") tcmClose();
            return false;
        }
    }

    // ── N (без модификаторов): создать задачу ────────────────────────
    if (code === 78 && !e.ctrlKey && !e.shiftKey && !e.altKey && !e.metaKey) {
        if (!kbIsTypingTarget(target) && !kbIsOverlayVisible("helpOverlay") && !kbIsOverlayVisible("whatsNewOverlay") && !kbIsOverlayVisible("reportOverlay") && !kbIsOverlayVisible("tcmOverlay") && !kbIsCreatePanelOpen()) {
            if (typeof showCreateTask === "function") showCreateTask();
            if (e.preventDefault) e.preventDefault();
            return false;
        }
    }
}

// IE11: addEventListener есть; attachEvent — fallback
if (document.addEventListener) {
    document.addEventListener("keydown", kbOnGlobalKeydown, false);
} else if (document.attachEvent) {
    document.attachEvent("onkeydown", kbOnGlobalKeydown);
}
```

> Важно: глобальный listener устанавливается один раз, при загрузке `kanban.js`. Soyuz-PLM перезагружает скрипт при открытии экрана, повторная регистрация на том же `document` создаст дубль — но т.к. это один и тот же скрипт-инстанс, повторного включения IIFE не происходит.

### Ключевые архитектурные решения для IE11 WebBrowser Control

1. **Проверка видимости оверлеев** — в `kanban.js` модалки открываются/закрываются через CSS-класс `visible`, а не через `style.display`. Поэтому проверяем `el.className.indexOf("visible") !== -1`.
2. **Фактические ID оверлеев** из HTML: `helpOverlay`, `reportOverlay`, `tcmOverlay`, `whatsNewOverlay`.
3. **Жёсткая блокировка F5/F1** — `e.preventDefault()` в IE11 WebBrowser Control недостаточно. Обязательно: `e.keyCode = 0; e.returnValue = false;` — иначе хост-приложение обработает клавишу раньше.
4. **Стек Esc с учётом z-index** — whatsNewOverlay и helpOverlay имеют z-index 10002, tcmOverlay — 10001, reportOverlay — 10000. Закрываем сверху вниз.

---

## 4. HTML — что и где менять (`KanbanBoard_HTML.html`)

В Help-модал (`<div id="helpOverlay">`, раздел справки) добавить раздел «Горячие клавиши»:

```html
<h4 style="margin-top:18px;">Горячие клавиши</h4>
<table class="kb-help-table">
    <tr><td><kbd>F5</kbd></td>                          <td>Обновить доску</td></tr>
    <tr><td><kbd>F1</kbd></td>                          <td>Открыть / закрыть справку</td></tr>
    <tr><td><kbd>Esc</kbd></td>                         <td>Закрыть открытое окно (Что нового → Справка → Отчёт → Карточка → Панель создания)</td></tr>
    <tr><td><kbd>Enter</kbd></td>                       <td>Сохранить изменения в карточке (не в textarea)</td></tr>
    <tr><td><kbd>Ctrl</kbd> + <kbd>S</kbd></td>         <td>Сохранить и закрыть карточку задачи (из любого поля)</td></tr>
    <tr><td><kbd>Ctrl</kbd> + <kbd>Enter</kbd></td>     <td>Отправить комментарий в чате задачи</td></tr>
    <tr><td><kbd>N</kbd></td>                           <td>Создать новую задачу</td></tr>
</table>
```

---

## 5. CSS — что и где менять (`kanban.css`)

В конец файла:

```css
.kb-help-table { border-collapse: collapse; margin-top: 8px; }
.kb-help-table td { padding: 4px 10px; vertical-align: middle; font-size: 12px; }
.kb-help-table kbd {
    display: inline-block;
    min-width: 18px;
    padding: 2px 6px;
    font-family: Consolas, "Courier New", monospace;
    font-size: 11px;
    border: 1px solid #b3b3b3;
    border-bottom-width: 2px;
    border-radius: 3px;
    background: #f8f8f8;
    color: #333;
    line-height: 1.2;
    text-align: center;
}
```

---

## 6. Пошаговый план реализации (по 1 коммиту)

| # | Шаг | Файлы | Smoke |
|---|-----|-------|-------|
| 1 | Добавить блок `kbHotkeys*` + регистрация listener; реализовать `F5` и `Esc` (с учётом whatsNewOverlay и z-index) | `kanban.js` | `F5` обновляет доску без белого мигания; `Esc` закрывает модалки в правильном порядке |
| 2 | Реализовать `F1` — справка | `kanban.js` | `F1` открывает/закрывает справку; стандартная справка Windows не появляется |
| 3 | Реализовать `Enter` в открытой карточке задачи (без закрытия) | `kanban.js` | В Task modal: `Enter` в `<input>` сохраняет (зелёная плашка); `Enter` в `<textarea>` — перенос строки |
| 4 | Реализовать `Ctrl+S` — сохранить и закрыть карточку | `kanban.js` | В Task modal: `Ctrl+S` из любого поля (включая textarea) сохраняет и закрывает |
| 5 | Реализовать `N` — новая задача | `kanban.js` | `N` открывает панель создания; не срабатывает в полях ввода |
| 6 | Help: таблица хоткеев + CSS `<kbd>` | `KanbanBoard_HTML.html`, `kanban.css` | Открыть Help — видна таблица |
| 7 | Документация | `docs/03_*.md`, `docs/05_*.md` | — |

---

## 7. Риски и технические ограничения

1. **`F5` и Soyuz-PLM окружение.** Soyuz-PLM использует embedded WebView (IE11 WebBrowser Control), в котором `F5` обычно перезагружает текущий screen. Одного `e.preventDefault()` недостаточно — будет белый миг. Решение: `e.keyCode = 0; e.returnValue = false;` — жёстко блокирует обработку на уровне IE. **Обязательно** проверить на стенде. Если хост-приложение перехватывает `F5` раньше JS — нужно будет:
   - либо отказаться от `F5` и заменить на `Ctrl+R` (тоже понятный),
   - либо запросить у платформы возможность подавить `F5` через `Service.UI` API.
2. **`F1` и стандартная справка Windows/IE.** В IE11 WebBrowser Control `F1` вызывает стандартную справку. Решение: `e.keyCode = 0; e.returnValue = false;` — аналогично F5.
3. **`Enter` в `<input>`** в Task modal: сейчас в title-инпуте панели создания забиндено на `doCreateTask` — но это **другой** инпут. Конфликта нет.
4. **`Enter` в редакторе комментария (textarea)** — глобальный listener проверяет `kbIsTextareaTarget` и **не** триггерит `tcmSave`. ✓
5. **`Enter` в поле чата (`tcm-chat-text`)** — явно проверяем `target.id === "tcm-chat-text"` и пропускаем, т.к. у чата свой обработчик `tcmChatKeydown`. ✓
6. **IE11** — `keyCode` стабильный, `event.key` отсутствует.
7. **`tcmSave`** — вызывает `SaveTask` и потом `RefreshBoard`. Карточка **не закрывается** — появляется зелёная плашка «Сохранено!». Для закрытия используем отдельный вызов `tcmClose()` (только в Ctrl+S).
8. **Регрессия `tcmChatKeydown`**: `Ctrl+Enter` в textarea чата уже работает (kanban.js:530). Глобальный listener не вмешивается — `Ctrl+Enter` ловится первым в textarea-handler через `onkeydown` (inline-атрибут).
9. **Несколько вкладок** — каждый экран регистрирует свой listener на свой document. Конфликта нет.
10. **Esc в режиме редактирования inline-комментария** (фича 01) — не закроет модалку, потому что фокус в textarea → blur. Хороший UX.
11. **whatsNewOverlay** — имеет z-index 10002 (наравне со справкой). Добавлен в стек Esc первым — закроется раньше остальных.
12. **Проверка видимости** — используется `el.className.indexOf("visible") !== -1`, а не `el.style.display`, т.к. именно так работает открытие/закрытие модалок в `kanban.js`.

---

## 8. Критерии приёмки

- [ ] `F5` обновляет доску, **не** перезагружая PLM-страницу (без белого мигания).
- [ ] `F1` открывает справку; повторное нажатие — закрывает.
- [ ] `F1` блокирует стандартную справку Windows/IE.
- [ ] `Esc` в открытом «Что нового» закрывает его.
- [ ] `Esc` в открытом Help закрывает Help.
- [ ] `Esc` в открытом Task modal закрывает Task modal.
- [ ] `Esc` в открытом Отчёте закрывает Отчёт.
- [ ] `Esc` в открытой панели создания закрывает её.
- [ ] `Esc` закрывает модалки в порядке z-index (сверху вниз).
- [ ] `Esc` в `<input>` снимает фокус, не закрывая модалку (нужно второе нажатие).
- [ ] `Enter` в `<input>` Task modal вызывает `tcmSave()` — карточка сохраняется, но **не закрывается** (зелёная плашка «Сохранено!»).
- [ ] `Enter` в `<textarea>` Task modal делает перенос строки (не сохраняет).
- [ ] `Enter` в поле чата (`tcm-chat-text`) не перехватывается глобальным listener.
- [ ] `Ctrl+S` в Task modal сохраняет и закрывает карточку (из любого поля, включая textarea).
- [ ] `Ctrl+S` блокирует стандартный диалог «Сохранить страницу» браузера.
- [ ] `N` (без модификаторов, вне полей ввода и модалок) открывает панель создания задачи.
- [ ] `Ctrl+Enter` в `tcm-chat-text` отправляет комментарий (без регрессии).
- [ ] Help содержит таблицу хоткеев со стилизованными `<kbd>`.

---

## 9. Тестовые сценарии

### Сценарий 1 — `F5`

1. Открыть доску. Изменить состояние через другой клиент (например, добавить задачу).
2. Нажать `F5` → доска обновилась, новая задача видна. PLM-страница **не** перезагружалась (нет белого мигания, навбар на месте).

### Сценарий 2 — стек `Esc`

1. Открыть «Что нового» (кнопка «Что нового»).
2. Открыть Help (кнопка «Справка»).
3. `Esc` → закрылся Help (z-index 10002, наравне с «Что нового», но открыт позже).
4. `Esc` → закрылось «Что нового».

### Сценарий 3 — `Enter` в карточке (сохраняет, но НЕ закрывает)

1. Открыть Task modal задачи.
2. Кликнуть в поле «Заголовок» (input). Поправить текст. Нажать `Enter`.
3. **Ожидаемо:** появилась зелёная плашка «Сохранено!», карточка **осталась открытой**.

### Сценарий 4 — `Enter` в textarea не сохраняет

1. В Task modal кликнуть в textarea «Описание». Нажать `Enter`.
2. **Ожидаемо:** перенос строки. Карточка не сохраняется.

### Сценарий 5 — `Ctrl+Enter` в чате

1. В чате задачи ввести текст. `Ctrl+Enter`.
2. **Ожидаемо:** комментарий отправлен. (без регрессии)

### Сценарий 6 — `Esc` в инпуте

1. В панели создания фокус в поле «Заголовок». Нажать `Esc`.
2. **Ожидаемо:** фокус снят, поле сохраняется, панель открыта.
3. `Esc` повторно → панель закрылась.

### Сценарий 7 — несколько модалок одновременно

1. Открыть Help, потом Отчёт.
2. `Esc` → закрылся Отчёт (он выше в z-index).
3. `Esc` → закрылся Help.

### Сценарий 8 — `Ctrl+S` сохранить и закрыть

1. Открыть Task modal задачи.
2. Изменить описание (textarea). Нажать `Ctrl+S`.
3. **Ожидаемо:** задача сохранилась, модалка закрылась. Стандартный диалог «Сохранить страницу» **не появился**.

### Сценарий 9 — `N` новая задача

1. На доске (фокус не в поле ввода). Нажать `N`.
2. **Ожидаемо:** открылась панель создания задачи.
3. Нажать `Esc` → панель закрылась.

### Сценарий 10 — `F1` справка

1. На доске (фокус не в поле ввода). Нажать `F1`.
2. **Ожидаемо:** открылась справка. Стандартная справка Windows/IE **не появилась**.
3. Нажать `F1` ещё раз → справка закрылась.

### Сценарий 11 — `Enter` в поле чата не перехватывается

1. В Task modal на вкладке «Обсуждение» кликнуть в поле «Написать комментарий...».
2. Нажать `Enter` (без Ctrl).
3. **Ожидаемо:** перенос строки. Комментарий не отправляется, карточка не сохраняется.