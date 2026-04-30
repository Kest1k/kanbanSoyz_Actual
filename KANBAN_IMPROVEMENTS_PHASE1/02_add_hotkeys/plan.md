# 02 — Горячие клавиши на канбан-доске

> **Источник в backlog:** `KANBAN_IMPROVEMENTS_BACKLOG.md` §1.2.
> **Фича:** `add_hotkeys`
> **Приоритет:** P0 (Phase 1)
> **Затронутые файлы:** `scripts/kanban.js`, `scripts/KanbanBoard_HTML.html`, `scripts/kanban.css`.

---

## 1. Техническое описание задачи

В backlog зафиксированы 4 хоткея:

| Хоткей | Контекст | Действие |
|--------|----------|----------|
| `F5` | глобально | Полное обновление доски (`kbRefreshBoard`) |
| `Esc` | глобально | Закрыть верхний открытый слой: Help → Report → Task modal → Create panel |
| `Enter` | внутри открытой карточки задачи (Task modal) | Сохранить изменения (`tcmSave`) |
| `Ctrl+Enter` | в поле комментария | Отправить комментарий — **уже реализовано** ([kanban.js:530–535](scripts/kanban.js#L530)), оставляем как есть |

### Поведение и правила

1. **`F5`**: перехватываем браузерный `F5`, чтобы **не** перезагружать всю PLM-страницу. Вместо этого вызываем `kbRefreshBoard()` (синхронно перерисует доску через `Invoke("Refresh", null)`).
2. **`Esc`**: глобальный «стек закрытий». Если фокус в поле ввода — первый `Esc` снимает фокус (blur), второй закрывает верхнюю модалку.
3. **`Enter` (без модификаторов) внутри Task modal**: вызывает `tcmSave()`. Должен работать когда фокус в `<input>`, но **не** в `<textarea>` (там Enter — это перенос строки).
4. **`Ctrl+Enter` в `tcm-chat-text`**: уже есть в `tcmChatKeydown` — не трогаем.
5. **IE11**: только `keyCode/which`, никаких `event.key`.

### Что НЕ делаем (вынесено в Phase 2)

- `N` для новой задачи, `1`–`4` для колонок (не значатся в backlog).
- `?` для Help (нет в backlog).
- `/` для поиска — связано с фичей 05, активируем там.

---

## 2. Затронутые файлы

| Путь | Что меняем |
|------|------------|
| `scripts/kanban.js` | Новый блок `kbHotkeys*`: `kbOnGlobalKeydown`, `kbHotkeyEscape`, `kbIsTypingTarget`, `kbIsTextareaTarget`, регистрация `document.addEventListener("keydown", ...)` |
| `scripts/KanbanBoard_HTML.html` | В блоке Help-модала (`openHelp`) добавить таблицу хоткеев |
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

function kbIsHelpOpen() {
    var el = document.getElementById("kb-help-modal");
    return !!(el && el.style.display && el.style.display !== "none");
}
function kbIsReportOpen() {
    var el = document.getElementById("kb-report-modal");
    return !!(el && el.style.display && el.style.display !== "none");
}
function kbIsTcmOpen() {
    var el = document.getElementById("tcm-modal");
    return !!(el && el.style.display && el.style.display !== "none");
}
function kbIsCreatePanelOpen() {
    var el = document.getElementById("kb-create-panel");
    return !!(el && el.style.display && el.style.display !== "none");
}

function kbHotkeyEscape() {
    if (kbIsHelpOpen())        { closeHelp();      return; }
    if (kbIsReportOpen())      { closeReport();    return; }
    if (kbIsTcmOpen())         { tcmClose();       return; }
    if (kbIsCreatePanelOpen()) { hideCreateTask(); return; }
}

function kbOnGlobalKeydown(e) {
    if (!e) e = window.event;
    var target = e.target || e.srcElement;
    var code   = e.keyCode || e.which;

    // ── F5: блокируем перезагрузку, вызываем kbRefreshBoard ─────────
    if (code === 116) {
        try { e.preventDefault(); } catch (er) {}
        try { e.stopPropagation(); } catch (er) {}
        if (typeof kbRefreshBoard === "function") kbRefreshBoard();
        return false;
    }

    // ── Esc: глобальный стек закрытий ───────────────────────────────
    if (code === 27) {
        if (kbIsTypingTarget(target)) {
            // Снимаем фокус с поля; модалку не закрываем — даём
            // пользователю шанс продолжить или нажать Esc ещё раз.
            try { target.blur(); } catch (er) {}
        } else {
            kbHotkeyEscape();
            try { e.preventDefault(); } catch (er) {}
        }
        return;
    }

    // ── Enter в открытой карточке задачи: сохранить ─────────────────
    // Условия: открыта tcm-modal, фокус НЕ в textarea, без Shift/Alt/Meta.
    // Ctrl+Enter в textarea чата уже обрабатывается tcmChatKeydown.
    if (code === 13 && !e.shiftKey && !e.altKey && !e.metaKey && !e.ctrlKey) {
        if (kbIsTcmOpen() && !kbIsTextareaTarget(target)) {
            if (typeof tcmSave === "function") tcmSave();
            try { e.preventDefault(); } catch (er) {}
            return;
        }
    }
}

// IE11: addEventListener есть; attachEvent — fallback на embedded WebView
if (document.addEventListener) {
    document.addEventListener("keydown", kbOnGlobalKeydown, false);
} else if (document.attachEvent) {
    document.attachEvent("onkeydown", kbOnGlobalKeydown);
}
```

> Важно: глобальный listener устанавливаем один раз, при загрузке `kanban.js`. Soyuz-PLM перезагружает скрипт при открытии экрана, повторная регистрация на том же `document` создаст дубль — но т.к. это один и тот же скрипт-инстанс, повторного включения IIFE не происходит.

---

## 4. HTML — что и где менять (`KanbanBoard_HTML.html`)

В Help-модал (`<div id="kb-help-modal">`, ~строки 640–700) добавить раздел:

```html
<h4 style="margin-top:18px;">Горячие клавиши</h4>
<table class="kb-help-table">
    <tr><td><kbd>F5</kbd></td>                          <td>Обновить доску</td></tr>
    <tr><td><kbd>Esc</kbd></td>                         <td>Закрыть открытое окно (Help → Отчёт → Карточка → Панель создания)</td></tr>
    <tr><td><kbd>Enter</kbd></td>                       <td>Сохранить изменения в открытой карточке задачи</td></tr>
    <tr><td><kbd>Ctrl</kbd> + <kbd>Enter</kbd></td>     <td>Отправить комментарий в чате задачи</td></tr>
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
| 1 | Добавить блок `kbHotkeys*` + регистрация listener; реализовать `F5` и `Esc` | `kanban.js` | `F5` обновляет доску без перезагрузки PLM-страницы; `Esc` закрывает открытое окно |
| 2 | Реализовать `Enter` в открытой карточке задачи | `kanban.js` | В Task modal: `Enter` в `<input>` сохраняет; `Enter` в `<textarea>` — перенос строки |
| 3 | Help: таблица хоткеев + CSS `<kbd>` | `KanbanBoard_HTML.html`, `kanban.css` | Открыть Help — видна таблица |
| 4 | Документация | `docs/03_*.md`, `docs/05_*.md` | — |

---

## 7. Риски и технические ограничения

1. **`F5` и Soyuz-PLM окружение.** Soyuz-PLM использует embedded WebView, в котором `F5` обычно перезагружает текущий screen. Перехват через `e.preventDefault()` + `e.stopPropagation()` должен сработать, но **обязательно** проверить на стенде. Если хост-приложение перехватывает `F5` раньше JS — нужно будет:
   - либо отказаться от `F5` и заменить на `Ctrl+R` (тоже понятный),
   - либо запросить у платформы возможность подавить `F5` через `Service.UI` API.
2. **`Enter` в `<input>`** в Task modal: сейчас в title-инпуте это уже забиндено на `doCreateTask` ([kanban.js:401](scripts/kanban.js#L401)) — но это **другой** инпут (в панели создания, не в Task modal). Конфликта нет.
3. **`Enter` в редакторе комментария (textarea)** — наш глобальный listener смотрит на `kbIsTextareaTarget` и **не** триггерит `tcmSave`. ✓
4. **IE11** — `keyCode` стабильный, `event.key` отсутствует.
5. **`tcmSave`** — функция, которая вызывает `SaveTask` и потом `RefreshBoard`. Она уже корректно валидирует и показывает ошибки (`tcmShowMsg`). Никаких изменений в самой функции не нужно.
6. **Регрессия `tcmChatKeydown`**: `Ctrl+Enter` в textarea чата уже работает (kanban.js:530). Наш глобальный listener не вмешивается — `Ctrl+Enter` ловится первым в textarea-handler через `onkeydown` (inline-атрибут). Если порядок неудобен, явно проверим в smoke.
7. **Несколько вкладок** — каждый экран регистрирует свой listener на свой document. Конфликта нет.
8. **Esc в режиме редактирования inline-комментария** (фича 01) — не закроет модалку, потому что фокус в textarea → blur. Хороший UX.

---

## 8. Критерии приёмки

- [ ] `F5` обновляет доску, **не** перезагружая PLM-страницу.
- [ ] `Esc` в открытом Help закрывает Help.
- [ ] `Esc` в открытом Task modal закрывает Task modal.
- [ ] `Esc` в открытой панели создания закрывает её.
- [ ] `Esc` со стека закрывает верхний слой при множественных открытиях (Help → Task modal: Esc закрывает Task modal первым).
- [ ] `Esc` в `<input>` снимает фокус, не закрывая модалку (нужно второе нажатие).
- [ ] `Enter` в `<input>` Task modal вызывает `tcmSave()`.
- [ ] `Enter` в `<textarea>` Task modal делает перенос строки (не сохраняет).
- [ ] `Ctrl+Enter` в `tcm-chat-text` отправляет комментарий (без регрессии).
- [ ] Help содержит таблицу хоткеев со стилизованными `<kbd>`.

---

## 9. Тестовые сценарии

### Сценарий 1 — `F5`

1. Открыть доску. Изменить состояние через другой клиент (например, добавить задачу).
2. Нажать `F5` → доска обновилась, новая задача видна. PLM-страница **не** перезагружалась (нет белого мигания, навбар на месте).

### Сценарий 2 — стек `Esc`

1. Открыть Help (через кнопку «Справка»).
2. Открыть Task modal (двойной клик по карточке).
3. `Esc` → закрылся Task modal.
4. `Esc` → закрылся Help.

### Сценарий 3 — `Enter` в карточке

1. Открыть Task modal задачи.
2. Кликнуть в поле «Заголовок» (input). Поправить текст. Нажать `Enter`.
3. **Ожидаемо:** карточка сохранилась, модалка закрылась (это поведение `tcmSave` → `RefreshBoard`).

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
