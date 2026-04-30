# 04 — Сортировка задач: приоритет → дата создания (новые сверху)

> **Источник в backlog:** `KANBAN_IMPROVEMENTS_BACKLOG.md` §1.4.
> **Фича:** `improve_task_sorting`
> **Приоритет:** P0 (Phase 1)
> **Затронутые файлы:** `scripts/SOYUZ_UPLOAD_KanbanScreen_script.cs`, `docs/01_KanbanScreen_SERVER_SCRIPT.md`.

---

## 1. Техническое описание задачи

### 1.1. Требования backlog

> - Новые задачи должны появляться **выше** старых
> - Приоритет всегда имеет высший приоритет: `urgent → high → medium → low`
> - Внутри одного приоритета — сортировка по дате создания (новые сверху)

### 1.2. Текущая реализация

В `BeforeRender` ([SOYUZ_UPLOAD_KanbanScreen_script.cs:64–72](scripts/SOYUZ_UPLOAD_KanbanScreen_script.cs#L64)):

```csharp
for( int i = 0; i < STATUS_DONE; i++ )
    raw[i].Sort( (a, b) => {
        var aPrio = a.GetNamedValue("Priority")?.GetValue<string>() ?? "medium";
        var bPrio = b.GetNamedValue("Priority")?.GetValue<string>() ?? "medium";
        int aRank = PriorityRank( aPrio );
        int bRank = PriorityRank( bPrio );
        if( aRank != bRank ) return aRank.CompareTo( bRank );
        return b.Id.CompareTo( a.Id );
    });
```

Текущая логика: **приоритет → Id убывающе**. `Id` строго возрастает при создании (PLM), так что `b.Id.CompareTo(a.Id)` действительно даёт «новые сверху». **Поведение в первом приближении уже соответствует backlog.**

### 1.3. В чём тогда фича?

Задача из backlog — **закрепить и формализовать** сортировку, заменив неявный tie-breaker по `Id` на **явный** tie-breaker по `CreationDate` (атрибут платформы) и убедиться, что это надёжно работает для legacy-задач, у которых `CreationDate` может быть пуст.

Дополнительно:
1. Вынести компаратор в отдельный метод `CompareActiveTasks(a, b)` — лучше тестируется и переиспользуется (например, в `DoSearchTasks` из плана 05).
2. Зафиксировать «новые сверху» в документации, чтобы случайный коммит не сломал.
3. Колонка `Готово` остаётся: `CompletedDate desc` (без изменений).

### 1.4. Что НЕ делаем

- Учёт `DueDate` / просроченности — **не по backlog**. Если потребуется — добавим в Phase 2.
- Ручная сортировка drag-and-drop с сохранением порядка (как в Trello) — Phase 3.

---

## 2. Затронутые файлы

| Путь | Что меняем |
|------|------------|
| `scripts/SOYUZ_UPLOAD_KanbanScreen_script.cs` | Заменить inline-лямбду в `BeforeRender` на вызов нового приватного `CompareActiveTasks(a, b)`; добавить helper `GetTaskCreationDate(task)` |
| `docs/01_KanbanScreen_SERVER_SCRIPT.md` | Описать порядок сортировки в разделе «Первичный рендер доски» |

JS / HTML / CSS не трогаем.

---

## 3. C# — что и где менять (`SOYUZ_UPLOAD_KanbanScreen_script.cs`)

### 3.1. Заменить блок сортировки в `BeforeRender`

Сейчас (строки ~64–72):

```csharp
for( int i = 0; i < STATUS_DONE; i++ )
    raw[i].Sort( (a, b) => {
        var aPrio = a.GetNamedValue("Priority")?.GetValue<string>() ?? "medium";
        var bPrio = b.GetNamedValue("Priority")?.GetValue<string>() ?? "medium";
        int aRank = PriorityRank( aPrio );
        int bRank = PriorityRank( bPrio );
        if( aRank != bRank ) return aRank.CompareTo( bRank );
        return b.Id.CompareTo( a.Id );
    });
```

Заменить на:

```csharp
for( int i = 0; i < STATUS_DONE; i++ )
    raw[i].Sort( CompareActiveTasks );
```

### 3.2. Новый метод `CompareActiveTasks`

Разместить в секции «Сортировка/Helpers» (рядом с `PriorityRank`):

```csharp
// Компаратор для колонок «Надо сделать», «В работе», «Ожидание»:
//   1) Приоритет: urgent → high → medium → low
//   2) Дата создания убывающе (новые сверху)
//   3) Id убывающе (страховка для legacy без CreationDate)
private int CompareActiveTasks( InfoObject a, InfoObject b )
{
    var aPrio = a.GetNamedValue("Priority")?.GetValue<string>() ?? "medium";
    var bPrio = b.GetNamedValue("Priority")?.GetValue<string>() ?? "medium";
    int aRank = PriorityRank( aPrio );
    int bRank = PriorityRank( bPrio );
    if( aRank != bRank ) return aRank.CompareTo( bRank );

    var aCreated = GetTaskCreationDate( a );
    var bCreated = GetTaskCreationDate( b );
    int cmp = bCreated.CompareTo( aCreated );    // новые сверху
    if( cmp != 0 ) return cmp;

    return b.Id.CompareTo( a.Id );               // страховочный tie-breaker
}
```

### 3.3. Helper `GetTaskCreationDate`

```csharp
private DateTime GetTaskCreationDate( InfoObject task )
{
    if( task == null ) return DateTime.MinValue;
    try
    {
        // Системный атрибут CreationDate всегда заполнен после первого Save.
        var dt = task.GetValue<DateTime>( "CreationDate" );
        if( dt != default(DateTime) ) return dt;
    }
    catch { }
    // Fallback: для legacy-задач без CreationDate используем «дату из Id».
    // Id монотонно растёт, поэтому сортировка эквивалентна.
    // Возвращаем искусственный DateTime, чтобы не ломать сравнение.
    return DateTime.MinValue.AddTicks( task.Id );
}
```

> ⚠ Если в Soyuz-PLM (BIS v3) системный атрибут называется иначе (например, `Created`, `DateCreated`, `StorageDate` — встречаются разные имена в платформах) — **уточнить на стенде** и поправить строку имени. Если такого атрибута нет вообще — оставить только fallback по `Id` (который и сейчас работает).

### 3.4. Колонка `Готово` — без изменений

Сортировка `raw[STATUS_DONE].Sort(...)` по `CompletedDate desc` остаётся как есть.

---

## 4. JavaScript / HTML / CSS — без изменений

Карточка корректно отрисовывается в любом порядке — порядок задаётся сервером через DotLiquid `for t in col_N`.

---

## 5. Пошаговый план реализации (по 1 коммиту)

| # | Шаг | Файлы | Smoke |
|---|-----|-------|-------|
| 1 | Добавить `GetTaskCreationDate(task)` (с try/catch + fallback по Id) | `SOYUZ_UPLOAD_KanbanScreen_script.cs` | Сборка ОК. Проверить в логе, что `CreationDate` читается без исключений на тестовых задачах. |
| 2 | Добавить `CompareActiveTasks(a, b)` и заменить inline-лямбду в `BeforeRender` | `SOYUZ_UPLOAD_KanbanScreen_script.cs` | Доска отображается, новые задачи внутри одного приоритета сверху, регрессий нет. |
| 3 | Документация: обновить раздел «Первичный рендер доски» в `docs/01_*.md` | `docs/01_*.md` | — |
| 4 | Snapshot конфигурации | `Kanban Конфигурация-1.0.0.4.pmszcfg` | Деплой на тестовый сервер |

---

## 6. Риски и технические ограничения

1. **Имя атрибута даты создания** в BIS v3 надо подтвердить (`CreationDate` / `Created` / другое). Если не уверены — оставляем `task.Id` как первичный tie-breaker (текущее поведение), а `GetTaskCreationDate` помечаем как опциональный шаг.
2. **`task.GetValue<DateTime>` исключения** при отсутствии значения — обёрнуты в try/catch с fallback.
3. **Производительность.** На каждую пару `(a, b)` в `Sort` теперь 2 чтения `CreationDate`. На колонке из 200 задач это ~ N log N ≈ 1500 пар × 2 = 3000 чтений атрибута. Для устранения — кэш в `Dictionary<long, DateTime>` перед сортировкой:

   ```csharp
   var cdCache = new Dictionary<long, DateTime>();
   foreach( var t in raw[i] ) cdCache[ t.Id ] = GetTaskCreationDate( t );
   raw[i].Sort( (a, b) => {
       /* читать из cdCache[a.Id]/cdCache[b.Id] */
   });
   ```

   В Phase 1 можно начать без кэша; если замер покажет деградацию >50 мс — добавить кэш отдельным коммитом.

4. **DotLiquid** — не задействован.

5. **Регрессия отчётов**: `DoGetReport` строит свои выборки независимо от `BeforeRender`. Не затрагивается.

6. **Drag-and-drop**: при перетаскивании клиент дёргает `MoveTask`, после чего `RefreshBoard` пересчитывает колонки. Новый порядок применяется автоматически.

7. **Stable sort**: `List<T>.Sort` в .NET — НЕ stable. Если у двух задач совпадают приоритет, дата создания и Id — они могут поменяться местами. Митигация: тройной tie-breaker делает совпадение всех трёх крайне маловероятным (Id уникальный).

---

## 7. Критерии приёмки

- [ ] В колонках 0–2 задачи отсортированы по приоритету (`urgent → high → medium → low`).
- [ ] Внутри одного приоритета — новые задачи сверху.
- [ ] Если у двух задач одинаковые приоритет и `CreationDate` — порядок стабилен (по `Id desc`).
- [ ] В колонке `Готово` сортировка `CompletedDate` desc (без изменений).
- [ ] Отчёты не сломаны.
- [ ] Производительность `BeforeRender` на 200+ задач — без заметных регрессий.
- [ ] Документация обновлена.

---

## 8. Тестовые сценарии

### Сценарий 1 — приоритет

1. Создать в `Надо сделать` две задачи с интервалом 1 минута: T1 (medium), T2 (urgent).
2. **Ожидаемо:** T2 (urgent) выше T1, несмотря на то что T1 старее.

### Сценарий 2 — новизна внутри приоритета

1. Создать T3 (medium). Подождать минуту. Создать T4 (medium).
2. **Ожидаемо:** T4 выше T3 в колонке `Надо сделать`.

### Сценарий 3 — drag-and-drop

1. Перетащить T2 в `В работе`.
2. **Ожидаемо:** T2 встаёт в верх колонки `В работе` (urgent + новейшая).

### Сценарий 4 — Готово

1. Перетащить T3 в `Готово`. Дождаться 1 минуты. Перетащить T1 туда же.
2. **Ожидаемо:** T1 выше T3 (по `CompletedDate desc`).

### Сценарий 5 — отчёт

1. Открыть «Отчёт» → `неделя`, `dept`.
2. Числа агрегатов корректны (сортировка не влияет).

### Сценарий 6 — производительность

1. На контейнере с 500 активных задач замерить время `BeforeRender` (через лог / визуально).
2. **Ожидаемо:** прирост < 100 мс.

### Сценарий 7 — legacy без CreationDate

1. Сэмулировать (или взять реальную) задачу без `CreationDate`.
2. **Ожидаемо:** не падает, fallback по `Id` ставит её в правильное место по новизне.
