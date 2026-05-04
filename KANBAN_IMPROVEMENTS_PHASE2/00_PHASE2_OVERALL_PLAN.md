# 00 — Общий план Phase 2 доработок Kanban-доски Soyuz-PLM

> **Документ:** план второй фазы улучшений модуля «Доска задач» (Soyuz-PLM, BIS v3).
> **Источник требований:** `KANBAN_IMPROVEMENTS_BACKLOG.md` §2.5 — §2.8.
> **Дата актуальности:** 04 мая 2026.
> **Автор плана:** AI senior-developer / tech-lead проекта.
> **Состояние:** Phase 1 завершена (1.1–1.4). §2.5 (Глобальный поиск) **в Phase 1 не реализовывался** — переносится в Phase 2 как ПОСЛЕДНЯЯ задача (без какого-либо cleanup'а: сервер чист от `DoSearchTasks`, JS чист от `kbBoardSearch*`).
> **Ветка:** `feature/kanban-phase2-period-subtasks-notes-search`.

---

## 0. Архитектурные ограничения Soyuz-PLM (обязательное к перечтению)

Канбан-модуль реализован на стеке Soyuz-PLM (BIS v3). Файлы в `scripts/`:

| Файл | Роль |
|------|------|
| `SOYUZ_UPLOAD_KanbanScreen_script.cs` | Серверная логика экрана (C#, Invoke-диспетчер) |
| `SOYUZ_UPLOAD_KanbanTask_script.cs` | OnBeforeSave + уведомления (Exclamation) |
| `KanbanBoard_HTML.html` | UI-шаблон (DotLiquid) |
| `kanban.css` | Стили |
| `kanban.js` | Клиентская логика (IE11, ES5, IIFE) |
| `kanbanButton.cs` | Кнопка-команда открытия экрана |
| `ExclamationKanban.cs` | Скрипт уведомлений |

### Жёсткие правила

**C# (Roslyn):**
- Никаких блоков `using` внутри методов. Все `using`-директивы — на уровне файла.
- Типы `ulong` (TID) обрабатываются платформой автоматически.
- Чтение атрибутов: `task.GetString("Key")` / `task.GetUser("Key")`.
- Запись: `task["Key"] = "value"; task.Save();` (через `GetEditable()` где требуется).
- Обработка ошибок: `Service.HandleException( ex, "Method: " + ex.Message );`.
- Возврат из Invoke — `string` или `object`, через `case "Method": return DoMethod(inputParams);`.

**JS (IE11):**
- Запрещено: `let`, `const`, стрелочные функции `=>`, шаблонные строки, `fetch`, нативные `Promise`, `class`, `for…of`, деструктуризация, `String.prototype.includes`, `Array.prototype.includes`, `Object.assign` без полифилла.
- Разрешено: `var`, `function () {}`, `+` для конкатенации, `indexOf`, классические `for`.
- DOM: `getElementById`, `getElementsByClassName`, `querySelector` (есть в IE11).

**Связь JS → C#:** строго `window.external.InvokeTemplate("MethodName", "p1|p2|...")`. Никаких `XMLHttpRequest`, `fetch`, AJAX. Перед отправкой пользовательского ввода — `replace(/\|/g, " ")` для защиты разделителя.

**DotLiquid:**
- Сравнение строк по неравенству — `!= ""`, не `!= "1"`.
- Никаких `where` по двум полям одновременно.

**Деплой/коммиты:** один шаг → один смоук-тест → один атомарный коммит. После каждой завершённой фичи — обновление snapshot-конфигурации `Kanban Конфигурация-1.0.0.X.pmszcfg`.

---

## 1. Состав Phase 2 (новый порядок)

| № | ID backlog | Задача | Папка | Приоритет | Размер | Риск |
|---|------------|--------|-------|-----------|--------|------|
| 1 | 2.6 | Фильтр по периоду (DateFrom / DateTo) | `05_period_filter/` | **P0** | M | Средний |
| 2 | 2.7 | Подзадачи / чекбоксы внутри задачи | `06_subtasks_checkboxes/` | **P0** | L | Средний |
| 3 | 2.8 | Блокнот / заметки внутри задачи | `07_notes_block/` | **P1** | M | Низкий |
| 4 | 2.5 | Глобальный поиск (полностью клиентский) | `08_add_global_search/` | **P1** | S | Низкий |

> **Замечание по нумерации.** Префиксы папок (`05`, `06`, `07`, `08`) продолжают сквозной счётчик из Phase 1 (где было `01`–`05`). Внутренние ссылки на §2.5–2.8 — это ID из backlog, не порядковые номера в Phase 2.

---

## 2. Приоритеты и обоснование порядка реализации

### 2.1. Сначала 2.6 (фильтр по периоду)

- Самая «продуктовая» фича из четырёх — даёт мгновенный демо-эффект «покажи задачи марта». Полезна для презентации генеральному директору.
- Затрагивает `BeforeRender` (серверная фильтрация). Делаем первой, пока цепочка вызовов в C# ещё свежа в голове после Phase 1.
- Нет зависимостей от других задач Phase 2.

### 2.2. Затем 2.7 (подзадачи)

- Самая объёмная: 3 серверных Invoke-метода + парсер JSON + UI во вкладке модалки + бейдж прогресса на карточке.
- Использует паттерн «JSON в текстовом атрибуте», уже проверенный в Phase 1 на комментариях (`CommentsJSON`). Этот паттерн **не нагружает БД** — никаких новых InfoObject или CollectionOfElements.
- Делаем после 2.6, потому что разработчик уже разогрет на серверной работе.

### 2.3. Затем 2.8 (блокнот/заметки)

- Архитектурно близка к 2.7 (тот же паттерн: `NotesJSON` в текстовом атрибуте, парсер/сериализатор по образцу `ParseSubtasks`/`SerializeSubtasks`).
- Backend проще 2.7: только `AddNote` (append-only с историей `text/author/date`), без Toggle/Delete по элементам.
- Делаем сразу после 2.7 — переиспользуем код парсинга.

### 2.4. Завершаем 2.5 (поиск) — последняя

- Чисто клиентская фича: `<input>` в тулбаре + `oninput` с debounce + перебор `.kb-card` + `style.display`. Серверный `SearchOperation` **не нужен** — все карточки уже в DOM.
- Минимальный риск, минимальный объём кода, **никаких новых C# методов**.
- Ставим последней, потому что:
  1. К моменту её реализации в DOM уже будут бейджи прогресса подзадач (из 2.7) и индикаторы заметок (из 2.8) — поиск автоматически охватит и эти текстовые поля через `textContent` карточки.
  2. Удобно протестировать, что фильтр периода (2.6) и поиск (2.5) работают совместно: счётчик `vis / total` обновляется корректно в обеих ситуациях.
  3. Хороший «закрывающий аккорд» Phase 2: после неё канбан явно «научился искать, фильтровать, дробить и помнить».

### 2.5. Карта зависимостей

```text
2.6 (период)              ← независим
2.7 (подзадачи)           ← независим, но внедряет паттерн ParseJSON/SerializeJSON
2.8 (заметки)             ← переиспользует паттерн из 2.7
2.5 (поиск)               ← независим; выигрывает от 2.7+2.8 (больше текста в DOM)
```

Параллелить можно 2.6 и 2.5 (разные слои). Один разработчик — строго последовательно: 2.6 → 2.7 → 2.8 → 2.5.

---

## 3. Стратегия реализации

### 3.1. Порядок коммитов (минимум 13)

```text
feat(kanban): server SetPeriodFilter + IsTaskInPeriod helper          (2.6)
feat(kanban): clamp tasks by period in BeforeRender + Liquid passthru (2.6)
feat(kanban): period filter UI (DateFrom/DateTo) + JS apply/reset     (2.6)
feat(kanban): GetReport custom date range support                     (2.6)
feat(kanban): subtasks – ParseSubtasks/SerializeSubtasks helpers      (2.7)
feat(kanban): subtasks – AddSubtask/ToggleSubtask/DeleteSubtask       (2.7)
feat(kanban): subtasks – modal tab UI + checkboxes                    (2.7)
feat(kanban): subtasks – progress badge "3/5" on .kb-card             (2.7)
feat(kanban): notes – ParseNotes/SerializeNotes helpers               (2.8)
feat(kanban): notes – AddNote with author + date                      (2.8)
feat(kanban): notes – modal tab UI + history rendering                (2.8)
feat(kanban): client-only board search with debounce + counter recount (2.5)
chore(kanban): pmszcfg snapshot 1.0.0.5                               (all)
```

### 3.2. Глобальные риски Phase 2

1. **IE11 + 200+ карточек.** `oninput` без debounce = лаги. Митигация — `setTimeout` 200–250 мс.
2. **Атрибуты `SubtasksJSON` / `NotesJSON`** должны существовать в InfoType `KanbanTask`. Если их нет — добавить в snapshot pmszcfg перед smoke-тестом.
3. **`obj.PropertyBag` для DateFrom/DateTo** живёт в рамках сессии экрана. Закрытие/открытие сбрасывает фильтр — это OK по бэклогу.
4. **Конкурентные правки JSON-атрибутов.** Два пользователя одновременно ставят чекбоксы. Soyuz-PLM использует optimistic locking через `task.Save()` — последний выигрывает. В Phase 2 принимаем; ETag — Phase 4+.
5. **DotLiquid + даты.** Фильтр периода — строго на C# до передачи в Liquid. В шаблоне дату не парсим.
6. **Размер `NotesJSON`** может расти. Soft-limit 200 КБ + предупреждение в UI при > 150 КБ.
7. **Совместимость поиска и фильтра периода.** Поиск работает поверх уже отфильтрованного DOM. Это OK.
8. **Регрессия счётчиков `.kb-cnt`.** И поиск, и фильтр меняют видимость карточек. Единый хелпер `kbRecountColumns` обновляет счётчики.

### 3.3. Definition of Done для Phase 2

- [ ] Все 4 фичи реализованы согласно соответствующим `plan.md`.
- [ ] `kanban.js` и `KanbanBoard_HTML.html` валидны для IE11 (нет `let/const/=>/template literals`).
- [ ] Все методы C# проходят сборку Soyuz-PLM на стенде.
- [ ] Документация в `docs/05_*.md`, `docs/06_*.md`, `docs/07_*.md`, `docs/08_*.md` обновлена.
- [ ] Snapshot `Kanban Конфигурация-1.0.0.5.pmszcfg` обновлён.
- [ ] Все коммиты атомарные, по 1 шагу на коммит.
- [ ] Demo-видео для генерального директора готово (формат `docs for presentation/`).
- [ ] В backlog `KANBAN_IMPROVEMENTS_BACKLOG.md` пункты 2.5–2.8 помечены `done`.

---

## 4. Структура папки Phase 2

```text
KANBAN_IMPROVEMENTS_PHASE2/
├── 00_PHASE2_OVERALL_PLAN.md       ← этот файл
├── 05_period_filter/
│   └── plan.md                     ← пункт 2.6 backlog (DateFrom/DateTo + BeforeRender)
├── 06_subtasks_checkboxes/
│   └── plan.md                     ← пункт 2.7 backlog (SubtasksJSON, 3 Invoke-метода)
├── 07_notes_block/
│   └── plan.md                     ← пункт 2.8 backlog (NotesJSON, append-only с историей)
└── 08_add_global_search/
    └── plan.md                     ← пункт 2.5 backlog (полностью клиентский, последний)
```

---

## 5. Что НЕ делаем в Phase 2

- 3.9 полные права руководителя — Phase 3.
- 3.10 личные задачи — Phase 3.
- 4.11 принятие результата — Phase 4.
- 4.12 уведомление руководителю при `Готово` — Phase 4.
- 4.13 расширенный отчёт с колонкой «Принял» — Phase 4.
- 5.14 recurring — Phase 5.
- 5.15 интеграция с Директум — Phase 5.

---

## 6. Готовность к презентации генеральному директору

После Phase 2 канбан получает:
1. **Фильтр по периоду** — отчёт «Что сделано в марте» одним кликом.
2. **Чек-листы внутри задачи** — формализация прогресса по подэтапам, бейдж `3/5` на карточке.
3. **Личный блокнот** — рабочая память исполнителя по задаче с авторами и датами.
4. **Полнотекстовый поиск** — мгновенный фильтр карточек по любой видимой подстроке.

Нарратив демо: «Доска научилась фильтровать по времени, дробить задачи на шаги, помнить ход работы и мгновенно искать.»
