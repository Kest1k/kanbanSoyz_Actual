# 05 — Растягивание описания задачи по содержимому

> **Затронутые файлы:**
> - `scripts/kanban.css`
> - `scripts/kanban.js`

## Что делаем

В модалке задачи (`tcmOverlay`, вкладка «Основное») поле `<textarea id="tcm-details">` сейчас имеет фиксированную высоту с внутренним скроллом:

```css
#tcm-details-group textarea,
.tcm-body textarea#tcm-details {
    height: 100px !important;
    min-height: 60px !important;
    max-height: 350px !important;
    resize: vertical !important;
    overflow-y: auto !important;
    display: block;
}
```

(см. `kanban.css:2204-2217`)

Цель: высота textarea **подгоняется под содержимое автоматически**, без внутреннего скролла. Если описание длинное — растягивается. Скролл при необходимости — **внешний** (вкладки «Основное» уже имеет `overflow-y: auto` на `.tcm-tab-content.active`).

## Часть A — CSS

В `kanban.css` найти блок `#tcm-details-group` (строка ~2205) и **заменить** на следующий:

```css
/* ── Поле «Описание» в модалке задачи: растягивается по содержимому ── */
#tcm-details-group {
    display: block;
    margin-bottom: 12px;
}
#tcm-details-group textarea,
.tcm-body textarea#tcm-details {
    width: 100% !important;
    min-height: 60px !important;
    /* max-height снят: растёт по содержимому */
    /* overflow-y снят: внутренний скролл не нужен */
    resize: vertical !important;       /* пользователь по-прежнему может вручную */
    overflow-y: hidden !important;     /* для авто-роста: скрытие, JS подбирает scrollHeight */
    box-sizing: border-box !important;
    line-height: 1.4 !important;
    padding: 6px 8px !important;
    display: block;
}
```

Ключевые изменения:
- Удалили `height: 100px !important` (фикс. высота).
- Удалили `max-height: 350px !important`.
- `overflow-y: hidden` (вместо `auto`) — это позволяет авто-роста-функции в JS точно мерить `scrollHeight`.

## Часть B — JS

Авто-рост textarea — стандартный паттерн: после установки `value` (или на каждом `input`) подгоняем `style.height = scrollHeight + 'px'`.

### B.1. Добавить хелпер в `kanban.js`

Расположить рядом с другими `tcm*` функциями (можно над `function tcmEsc` или в начале блока модалки задачи):

```javascript
// ── Авто-рост textarea «Описание» по содержимому ─────────────────
function tcmAutoSizeDetails() {
    var ta = document.getElementById("tcm-details");
    if (!ta) return;
    // Сброс на минимум перед измерением — иначе scrollHeight не уменьшается
    ta.style.height = "auto";
    var sh = ta.scrollHeight;
    if (!sh || sh < 60) sh = 60;
    ta.style.height = sh + "px";
}
window.tcmAutoSizeDetails = tcmAutoSizeDetails;
```

### B.2. Подключение к открытию модалки

После строки `document.getElementById("tcm-details").value = d.details || "";` (около строки 1924) добавить:

```javascript
document.getElementById("tcm-details").value = d.details || "";
tcmAutoSizeDetails();   // FIX-05
```

### B.3. Подключение к вводу пользователем

Чтобы при печати в textarea оно росло вместе с текстом — добавить обработчик `oninput`. Сделать это можно один раз — в обработчике инициализации модалки. Найти место, где `tcm-details` впервые обрабатывается, или просто в `tcmOpen` (около строки 1845, рядом с `tcmSubtasksLoad(nameKey)`):

```javascript
// FIX-05: подвязать обработчик авто-роста к textarea (один раз)
var taDet = document.getElementById("tcm-details");
if (taDet && !taDet._tcmAutoSizeBound) {
    taDet._tcmAutoSizeBound = true;
    taDet.oninput = function () { tcmAutoSizeDetails(); };
}
```

### B.4. Подключение к смене вкладки

Когда модалка открывается, активна вкладка «Основное», и `tcmAutoSizeDetails()` отрабатывает корректно. Но если пользователь сначала открыл вкладку «Чек-лист», потом вернулся в «Основное» — textarea могла быть `display:none`, и `scrollHeight` мог быть посчитан неточно. Для устойчивости — пересчитывать при возврате на вкладку «Основное».

Найти `window.tcmSwitchTab = function (tab)` (поиск по `tcmSwitchTab`). В конце функции добавить:

```javascript
// FIX-05: при возврате на «Основное» пересчитываем высоту textarea
if (tab === "main") {
    setTimeout(function () { tcmAutoSizeDetails(); }, 0);
}
```

> `setTimeout(..., 0)` нужен, чтобы DOM успел применить `display:block` на вкладке перед измерением.

## Часть C — Атомарные коммиты

| # | Шаг | Файлы | Сообщение коммита |
|---|-----|-------|-------------------|
| 1 | CSS: убрать max-height/overflow на `#tcm-details` | `kanban.css` | `style(kanban): remove max-height on task description` |
| 2 | JS: хелпер `tcmAutoSizeDetails` + вызовы при load/input/tab-switch | `kanban.js` | `feat(kanban): auto-grow task description by content` |

## Часть D — Проверки и риски

1. **Очень длинное описание** (тысячи строк): textarea вырастает по высоте всей вкладки. Внешний скролл `.tcm-tab-content.active { overflow-y: auto; }` (см. `kanban.css:2192+`) подхватывает прокрутку — пользователь скроллит модалку целиком. Это и есть желаемое поведение.
2. **Высота меньше 60px**: гарантируется `min-height: 60px`. Пустое описание не сжимается до невидимости.
3. **Ручной resize**: `resize: vertical !important` сохранён. Пользователь может вручную утащить высоту больше расчётной. После следующего `oninput` высота снова подстроится. Это норма.
4. **IE11**: `scrollHeight` стабильно работает в Trident. `style.height = "auto"` поддерживается.
5. **Шрифты ещё не загрузились на момент `tcmOpen`**: маловероятный кейс. Если будет проблема — добавить ещё один `setTimeout(tcmAutoSizeDetails, 100)` после первоначального вызова.
6. **Регрессия в карточках доски** (.kb-card-details): эта правка касается ТОЛЬКО модалки задачи (`#tcm-details`). Превью описания на карточках доски (`.kb-card-details`, обрезается через `text-overflow`) — не трогается.

## Часть E — Критерии приёмки

- [ ] Открыть задачу с **коротким** описанием (1-2 строки). Textarea высотой ~60-80 px, без скролла.
- [ ] Открыть задачу с **длинным** описанием (50+ строк). Textarea растянулась полностью, внутреннего скролла нет, общая прокрутка — у вкладки «Основное».
- [ ] Открыть задачу, начать печатать в описании. Textarea растёт по мере добавления строк.
- [ ] Удалить часть текста — textarea уменьшается соответственно (но не ниже 60 px).
- [ ] Перейти на вкладку «Чек-лист», вернуться на «Основное» — textarea сразу подогнала высоту, текст виден полностью.
- [ ] Кнопка ручного resize в правом нижнем углу (resize:vertical) работает: пользователь может ещё больше растянуть/сжать вручную.
- [ ] Регрессия: на карточке доски превью описания обрезается как раньше (одна строка с многоточием).
