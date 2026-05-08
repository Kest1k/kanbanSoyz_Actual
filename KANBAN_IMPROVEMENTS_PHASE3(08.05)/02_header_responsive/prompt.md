# 02 — Адаптация шапки при сжатии окна по горизонтали

> **Затронутые файлы:**
> - `scripts/KanbanBoard_HTML.html`
> - `scripts/kanban.css`

## Что делаем

Сейчас в шапке (`{% block navigationTitle %}` в `KanbanBoard_HTML.html`, строки 8–125) кнопки «Справка» и «Что нового» прибиты к правому краю через `position:absolute; right:20px/130px; top:8px`. Слева — навигационный список (`<ul class="nav navbar-nav">`), панель иерархии (`#kb-hier-panel`, `float:left`) и фильтр периода (`#kb-period-wrap`, `float:left`). Когда окно сжимают по ширине — все эти элементы наезжают друг на друга.

Цель: при сжатии — **никакого наезда**, элементы могут переноситься на следующую строку или сжиматься, но остаются читаемыми и кликабельными.

Технология: CSS-only, без JS. IE11/Trident-совместимо (`-ms-flexbox`, без `var()`).

## Часть A — HTML (`KanbanBoard_HTML.html`)

Заменить текущие два `<div style="position:absolute;...">` (строки 15–26) на один контейнер справа без `position:absolute`. Это убирает источник наезда. Структура: левая часть (нав + панели) + правая часть (Справка / Что нового), обе обёрнуты в флексовый родитель.

### A.1. Заменить блок строк 8–125 на следующий

Вместо текущей структуры из набора отдельно плавающих элементов — **обёрнуть всё в один flex-контейнер** `kb-nav-row`, у которого `flex-wrap: wrap`. Внутри две группы: `.kb-nav-left` (брендинг + ссылки + панели) и `.kb-nav-right` (Справка / Что нового).

```html
{% block navigationTitle %}
<div class="kb-nav-row">
    <div class="kb-nav-left">
        <div class="navbar-brand collapse navbar-collapse" style="margin-left: 25px">Канбан-доска</div>
        <ul class="nav navbar-nav kb-nav-links">
            <li><a href="#" onclick="showCreateTask(); return false;"><i class="fa fa-plus fa-fw"></i> Новая задача</a></li>
            <li><a href="#" onclick="kbRefreshBoard(); return false;"><i class="fa fa-refresh fa-fw"></i> Обновить</a></li>
            <li><a href="#" onclick="openReport(); return false;"><i class="fa fa-bar-chart fa-fw"></i> Отчёт</a></li>
        </ul>
        <!-- Панель ролевой иерархии: видна только руководителям (инициализируется через JS) -->
        <div id="kb-hier-panel" class="kb-nav-cluster" style="display:none;">
            <button type="button" id="kb-btn-my" onclick="kbSetMyMode()" class="kb-nav-btn kb-nav-btn-primary">
                Мои задачи
            </button>
            <button type="button" id="kb-btn-all" onclick="kbSetAllMode()" class="kb-nav-btn" style="display:none;">
                Все задачи
            </button>
            <span id="kb-hier-label" class="kb-nav-label" style="display:none;">Вид:</span>
            <select id="kb-sel-dept" class="kb-hier-select" onchange="kbOnDeptChange()" style="display:none; width:auto; min-width:140px;"></select>
            <select id="kb-sel-sector" class="kb-hier-select" onchange="kbOnSectorChange()" style="display:none; width:auto; min-width:140px;"></select>
            <select id="kb-sel-user" class="kb-hier-select" onchange="kbOnUserChange()" style="display:none; width:auto; min-width:160px;"></select>
            <input type="text" id="kb-sel-search" class="kb-hier-select" placeholder="&#128269; Поиск..."
                oninput="kbOnSelSearch()" style="display:none; width:140px;">
        </div>
        <!-- Фильтр по периоду (компактный toggle + выпадающая панель) -->
        <div id="kb-period-wrap" class="kb-period-wrap">
            <button type="button" id="kb-period-toggle" class="kb-period-toggle"
                    onclick="kbPeriodToggle()" title="Фильтр по периоду">
                <i class="fa fa-calendar"></i>
                <span id="kb-period-toggle-label">Период</span>
                <i class="fa fa-caret-down" style="margin-left:4px;"></i>
            </button>
            <div id="kb-period-panel" class="kb-period-panel" style="display:none;">
                <!-- ... содержимое панели — БЕЗ ИЗМЕНЕНИЙ, скопировать из текущей версии (строки 67-124) ... -->
            </div>
        </div>
    </div>
    <div class="kb-nav-right">
        <button type="button" onclick="openWhatsNew()" class="kb-nav-btn kb-nav-btn-news">
            <i class="fa fa-bullhorn fa-fw"></i> Что нового
        </button>
        <button type="button" onclick="openHelp()" class="kb-nav-btn kb-nav-btn-help">
            <i class="fa fa-question-circle fa-fw"></i> Справка
        </button>
    </div>
</div>
{% endblock %}
```

> **Важно**: содержимое `<div id="kb-period-panel" class="kb-period-panel">…</div>` (быстрый выбор / свой диапазон / Применить-Сбросить) **НЕ менять**. Скопировать как было из текущих строк 67–124.

> **Inline-стили** на `kb-hier-panel` и его дочерних элементах (`float:left`, `margin-top:8px` и т.п.) **удалить** — они мешают flex-обёртке. Размеры и `display:none` (для скрытия по роли) сохранить через классы и атрибуты `style="display:none;"`.

> **Дополнительные inline-стили** с кнопок «Мои задачи», «Все задачи», «Справка», «Что нового», селектов панели иерархии **удалить** — заменены классами `kb-nav-btn*`, стили из CSS перебивают.

## Часть B — CSS (`kanban.css`)

Добавить новый блок **сразу после блока `.kb-period-wrap` ... `.kb-period-btn-reset`** (после строки 768, перед `/* ══════════ ОТЧЁТ ══════════ */` около строки 770):

```css
/* ══════════════════════════════════════════════════════════════════
   Адаптивная шапка Kanban — Phase 3 #02
   ══════════════════════════════════════════════════════════════════ */
.kb-nav-row {
    display: -ms-flexbox;
    display: flex;
    -ms-flex-wrap: wrap;
    flex-wrap: wrap;
    -ms-flex-align: center;
    align-items: center;
    -ms-flex-pack: justify;
    justify-content: space-between;
    width: 100%;
    gap: 8px;
    padding-right: 12px;
    box-sizing: border-box;
}

.kb-nav-left {
    display: -ms-flexbox;
    display: flex;
    -ms-flex-wrap: wrap;
    flex-wrap: wrap;
    -ms-flex-align: center;
    align-items: center;
    gap: 8px 12px;
    -ms-flex: 1 1 auto;
    flex: 1 1 auto;
    min-width: 0;
}

.kb-nav-right {
    display: -ms-flexbox;
    display: flex;
    -ms-flex-wrap: wrap;
    flex-wrap: wrap;
    -ms-flex-align: center;
    align-items: center;
    gap: 6px;
    -ms-flex: 0 0 auto;
    flex: 0 0 auto;
}

/* navbar-brand уже стилизован bootstrap, не трогаем — только flex-friendly */
.kb-nav-row .navbar-brand {
    -ms-flex: 0 0 auto;
    flex: 0 0 auto;
    margin: 0 !important;
}

/* nav-list: горизонтальный список ссылок без bootstrap-флоатов */
.kb-nav-row .kb-nav-links {
    display: -ms-flexbox;
    display: flex;
    -ms-flex-wrap: wrap;
    flex-wrap: wrap;
    margin: 0;
    padding: 0;
    list-style: none;
    gap: 0 4px;
    float: none;
}
.kb-nav-row .kb-nav-links > li { float: none; display: inline-block; }
.kb-nav-row .kb-nav-links > li > a { padding: 6px 10px; }

/* Кластер «Мои задачи / Все задачи / Селекты» — собственная flex-строка */
.kb-nav-cluster {
    display: -ms-flexbox;
    display: flex;
    -ms-flex-wrap: wrap;
    flex-wrap: wrap;
    -ms-flex-align: center;
    align-items: center;
    gap: 6px;
    float: none !important;
    margin: 0 !important;
}

/* Унифицированная кнопка шапки (My/All/Help/News) */
.kb-nav-btn {
    height: 30px;
    padding: 5px 12px;
    font-size: 12px;
    font-weight: 600;
    border: 1px solid #d1d5db;
    border-radius: 6px;
    background: #fff;
    color: #374151;
    cursor: pointer;
    white-space: nowrap;
    line-height: 18px;
    box-sizing: border-box;
}
.kb-nav-btn:hover { background: #f3f4f6; border-color: #9ca3af; }
.kb-nav-btn-primary {
    background: #4a6fa5;
    color: #fff;
    border-color: #4a6fa5;
}
.kb-nav-btn-primary:hover { background: #3b5998; border-color: #3b5998; }
.kb-nav-btn-news {
    border-color: #dcfce7;
    background: #f0fdf4;
    color: #16a34a;
}
.kb-nav-btn-news:hover { background: #dcfce7; border-color: #86efac; }
.kb-nav-btn-help {
    border-color: #bfdbfe;
    background: #eff6ff;
    color: #1d4ed8;
}
.kb-nav-btn-help:hover { background: #dbeafe; border-color: #93c5fd; }

.kb-nav-label {
    font-size: 12px;
    color: #cce;
    margin-right: 4px;
}

/* Период-wrap: убираем float, используем inline-flex */
.kb-period-wrap {
    float: none !important;
    margin-top: 0 !important;
    margin-left: 0 !important;
    display: inline-flex;
    display: -ms-inline-flexbox;
    -ms-flex-align: center;
    align-items: center;
}

/* Узкий экран: при ширине < 1100px — правая группа уезжает на новую строку
   и панели иерархии могут оборачиваться */
@media (max-width: 1100px) {
    .kb-nav-row { -ms-flex-pack: start; justify-content: flex-start; }
    .kb-nav-right { -ms-flex-order: 99; order: 99; width: 100%; -ms-flex-pack: end; justify-content: flex-end; }
}

/* Очень узкий экран: ссылки уезжают на новую строку под брендом */
@media (max-width: 760px) {
    .kb-nav-row .kb-nav-links { -ms-flex-preferred-size: 100%; flex-basis: 100%; }
    .kb-nav-cluster { -ms-flex-preferred-size: 100%; flex-basis: 100%; }
}
```

## Часть C — Удалить дублирующие inline-стили

Из `KanbanBoard_HTML.html` удалить **все inline `style="..."` атрибуты** на следующих элементах (стили заменены классами):

- `kb-btn-my` — был `padding:5px 12px; ...` → удалить, оставить только `class="kb-nav-btn kb-nav-btn-primary"`.
- `kb-btn-all` — был `padding:5px 12px; ...` → удалить, оставить `class="kb-nav-btn"`. Атрибут `style="display:none;"` (для управления видимостью JS) **сохранить**.
- `kb-hier-label` — был `font-size:12px;color:#cce;...` → удалить, оставить `class="kb-nav-label"` и `style="display:none;"`.
- На селектах `kb-sel-dept`, `kb-sel-sector`, `kb-sel-user`, `kb-sel-search` — оставить `style="display:none; width:auto; min-width:Xpx;"` (видимость + ширина), удалить `vertical-align`, `margin-right`, `margin-left`, `margin-top` — это управляет flex-родитель.
- На двух правых кнопках (Справка, Что нового) — удалить inline-стили целиком, заменив классами `kb-nav-btn-help` / `kb-nav-btn-news`.

## Часть D — Атомарные коммиты

| # | Шаг | Файлы | Сообщение коммита |
|---|-----|-------|-------------------|
| 1 | HTML: обернуть шапку в flex-контейнер `.kb-nav-row` | `KanbanBoard_HTML.html` | `refactor(kanban): wrap navbar in flex container` |
| 2 | CSS: новые правила адаптивной шапки + media-queries | `kanban.css` | `feat(kanban): responsive navbar layout` |
| 3 | HTML: убрать дублирующие inline-стили | `KanbanBoard_HTML.html` | `refactor(kanban): remove inline styles from navbar buttons` |

## Часть E — Проверки и риски

1. **Bootstrap навбар**: `<ul class="nav navbar-nav">` имеет `float:left` от bootstrap. Перебиваем `float:none` через `.kb-nav-row .kb-nav-links { float: none; }`. Проверить, что список не «сложился» в столбец.
2. **JS-инициализация селектов** не должна сломаться: id-шники сохранены (`kb-sel-dept`, `kb-sel-sector`, ...). Только классы и стили изменились.
3. **Видимость по роли**: `kb-hier-panel` стартует с `display:none` и показывается JS через `panel.style.display = "block"` (см. `kanban.js:1025`). Заменить на `panel.style.display = ""` или оставить `"block"` — сейчас `.kb-nav-cluster` это `display:flex`, и `"block"` его перебьёт. **Действие**: в `kanban.js:1025` поменять `panel.style.display = "block";` на `panel.style.display = "";` — это вернёт CSS-значение `display: flex` из класса.
4. **Период-wrap dropdown**: `.kb-period-panel` имеет `position:absolute`, остаётся как есть. Wrap родитель — `position:relative` (уже есть на `.kb-period-wrap` в строке 634). При переходе wrap-родителя в inline-flex **проверить**, что dropdown по-прежнему якорится под кнопкой. Если позиционируется в другом месте — добавить `position:relative` явно на `.kb-period-wrap`.
5. **Отчёт-overlay** и `tcmOverlay` — fixed-позиционирование, на ширину шапки не влияют.

## Часть F — Критерии приёмки

- [ ] При полной ширине окна шапка выглядит как раньше (брендинг — ссылки — иерархия — период | справа: «Что нового» / «Справка»).
- [ ] При ширине ~1100px правая группа («Что нового», «Справка») уезжает на следующую строку и прижимается вправо.
- [ ] При ширине ~760px список нав-ссылок и кластер иерархии уходят на новые строки.
- [ ] Никакие кнопки не наезжают друг на друга при любой ширине от 600px до 1920px.
- [ ] Кнопка «Период» открывает dropdown и он по-прежнему якорится под кнопкой.
- [ ] Кнопка «Мои задачи» / «Все задачи» функционируют, селекты иерархии работают.
- [ ] Регрессия: «Новая задача» / «Обновить» / «Отчёт» в списке кликаются.
