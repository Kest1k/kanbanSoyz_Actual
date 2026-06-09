# Фича 06 – Повторяемые (рекуррентные) задачи

**Тип:** новые атрибуты `RecurringRule`, `RecurringActive` в `KanbanTask` + сервер (`SOYUZ_UPLOAD_KanbanScreen_script.cs`) + клиент. Опционально – серверная фоновая задача.
**Риск:** высокий (вмешательство в жизненный цикл + создание новых задач).

---

## 1. Концепция (как повтор уживается с канбаном)

В канбане у карточки один статус. «Повторяемость» реализуем как **порождение следующего экземпляра**, а не как «вечную» карточку:

> Когда повторяемую задачу переводят в «Готово», система автоматически создаёт **новую** задачу-копию с тем же названием/исполнителем/правилом и новым сроком. Так доска остаётся чистой: завершённое уходит в Done, новое появляется в «Надо сделать».

Это **режим A (по завершению)** – основной, простой, без серверного планировщика. Для задач «по календарю, независимо от того, выполнена ли прошлая» есть **режим B (фоновая задача по расписанию)** – опционально, раздел 8.

Правило повтора (`RecurringRule`): `none` | `daily` | `weekdays` | `weekly` | `monthly`.

---

## 2. Шаг 1. Создать атрибуты

Через **Конфигуратор** в шаблоне `KanbanTask`:
- `RecurringRule` – тип `Text`.
- `RecurringActive` – тип `Bool` (Да/нет).

Либо скриптом:
```csharp
var t = Service.GetTemplate( "KanbanTask" );
new AttributeDef( t, "RecurringRule",   AttributeDefBase.DataTypeEnum.Text ).Save();
new AttributeDef( t, "RecurringActive", AttributeDefBase.DataTypeEnum.Bool ).Save();
```

---

## 3. Шаг 2. Хук завершения – `DoMoveTask`

**Файл:** `SOYUZ_UPLOAD_KanbanScreen_script.cs`, метод `DoMoveTask` (@242). Блок входа в «Готово» (@341–345):

Было:
```csharp
    if( newStatus == STATUS_DONE && oldStatus != STATUS_DONE )
    {
        // Переход В «Готово» – записываем дату завершения
        task["CompletedDate"] = DateTime.Now;
    }
```
Стало:
```csharp
    if( newStatus == STATUS_DONE && oldStatus != STATUS_DONE )
    {
        // Переход В «Готово» – записываем дату завершения
        task["CompletedDate"] = DateTime.Now;

        // Фича 06: породить следующий экземпляр повторяемой задачи
        try { SpawnNextRecurrence( task ); } catch ( Exception rex )
        { Service.WriteToServerLog( "KanbanRecur", "SpawnNextRecurrence: " + rex.Message ); }
    }
```

> `SpawnNextRecurrence` создаёт и сохраняет **отдельный** новый объект – на сохранение текущей задачи (`task.Save()` @381) это не влияет; ошибки изолированы try/catch.

---

## 4. Шаг 3. Порождение копии + расчёт срока

Добавить методы рядом с `DoCreateTask` (@389). Переиспользуют `SetTaskStatus`, `Service.GetDataContainer`, `Service.GetTemplate`.

```csharp
// ░░ Фича 06 ░░ создать следующий экземпляр повторяемой задачи
private void SpawnNextRecurrence( InfoObject task )
{
    string rule = "";
    try { rule = ( task.GetString( "RecurringRule" ) ?? "" ).Trim(); } catch { }
    bool active = false;
    try { active = task.GetValue<bool>( "RecurringActive" ); } catch { }
    if( !active || string.IsNullOrEmpty( rule ) || rule == "none" ) return;

    // Базовая дата для расчёта = текущий срок (или сегодня)
    DateTime baseDate = DateTime.Today;
    try { var dd = task.GetValue<DateTime>( "DueDate" ); if( dd != default(DateTime) ) baseDate = dd; } catch { }
    DateTime next = ComputeNextDue( rule, baseDate );

    var container = Service.GetDataContainer( "All_Kanban_Tasks_Folder" );
    var template  = Service.GetTemplate( "KanbanTask" );
    if( container == null || template == null ) return;

    var clone = new InfoObject( container, template );
    clone.NameKey = "ktask_" + System.DateTime.Now.Ticks.ToString() + "_r";

    try { clone["TaskName"]    = task.GetString( "TaskName" )    ?? ""; } catch { }
    try { clone["TaskDetails"] = task.GetString( "TaskDetails" ) ?? ""; } catch { }
    try { var pv = task.GetNamedValue( "Priority" ); if( pv != null ) clone["Priority"] = pv; } catch { }
    try { clone["Tags"]    = task.GetString( "Tags" ) ?? ""; } catch { }
    try { var a = task.GetUser( "Assignee" ); if( a != null ) clone["Assignee"] = a; } catch { }
    try { clone["Creator"] = task.GetString( "Creator" ) ?? ""; } catch { }
    try { clone["IsPrivate"] = task.GetValue<bool>( "IsPrivate" ); } catch { }
    try { clone["RecurringRule"]   = rule; } catch { }
    try { clone["RecurringActive"] = true; } catch { }

    SetTaskStatus( clone, 0 );            // «Надо сделать»
    try { clone["DueDate"] = next; } catch { }
    try { clone["SeenByList"] = ""; } catch { }   // у исполнителя загорится «НОВАЯ»

    clone.Save();
}

// Расчёт следующего срока по правилу
private DateTime ComputeNextDue( string rule, DateTime from )
{
    switch( rule )
    {
        case "daily":    return from.AddDays( 1 );
        case "weekly":   return from.AddDays( 7 );
        case "monthly":  return from.AddMonths( 1 );
        case "weekdays":
        {
            var d = from.AddDays( 1 );
            while( d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday )
                d = d.AddDays( 1 );
            return d;
        }
        default:         return from.AddDays( 1 );
    }
}
```

> Уведомление исполнителю о новой задаче сработает **автоматически**: при `clone.Save()` отрабатывает `OnBeforeSave` в `SOYUZ_UPLOAD_KanbanTask_script.cs`, который видит назначенного `Assignee` (PersistedValue == null у нового объекта) и шлёт `ExclamationKanban`. Это желаемое поведение.

---

## 5. Шаг 4. Команда установки правила – `SetRecurrence`

Чтобы не усложнять `DoSaveTask`, правило ставим отдельной командой.

### 5.1. Зарегистрировать `case` (рядом с CreateTask, @166–167)
```csharp
            case "SetRecurrence":    return DoSetRecurrence( inputParams );
```

### 5.2. Метод
```csharp
// ░░ Фича 06 ░░ установить/снять правило повтора. inputParams: "nameKey|rule"
private object DoSetRecurrence( object inputParams )
{
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 2 );
    var nameKey = parts.Length > 0 ? parts[0].Trim() : "";
    var rule    = parts.Length > 1 ? parts[1].Trim() : "none";
    if( string.IsNullOrEmpty( nameKey ) ) return "ERROR:EmptyId";

    var task = GetTaskByKeyOrNull( nameKey );
    if( task == null ) return "ERROR:TaskNotFound";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

    bool active = !( string.IsNullOrEmpty( rule ) || rule == "none" );
    var ed = task.GetEditable();
    try { ed["RecurringRule"]   = active ? rule : ""; } catch { }
    try { ed["RecurringActive"] = active; } catch { }
    ed.Save();
    return "OK";
}
```

### 5.3. Отдать текущее правило в карточку – `DoGetTaskDetails` (@1663)
Прочитать (рядом с `tags`/`notes`):
```csharp
    string recurringRule = "none";
    try { if( task.GetValue<bool>( "RecurringActive" ) ) {
              var rr = task.GetString( "RecurringRule" ) ?? "";
              if( !string.IsNullOrEmpty( rr ) ) recurringRule = rr;
          } } catch { }
```
Добавить в JSON (рядом с `"tags"`/`"notes"`, @1899):
```csharp
    sb.Append( "\"recurringRule\":\"" + JsonEscape( recurringRule ) + "\"," );
```

---

## 6. Шаг 5. Клиент – карточка задачи

### 6.1. HTML: селектор повтора во вкладке «Основное» (`tcm-tab-main` @705)
Вставить в подходящую строку формы (рядом с приоритетом/сроком):
```html
                <div class="tcm-row">
                    <label class="tcm-lbl">Повтор</label>
                    <select id="tcm-recurring" class="tcm-input" onchange="tcmSetRecurrence()">
                        <option value="none">Нет</option>
                        <option value="daily">Ежедневно</option>
                        <option value="weekdays">По будням</option>
                        <option value="weekly">Еженедельно</option>
                        <option value="monthly">Ежемесячно</option>
                    </select>
                    <span class="tcm-recurring-hint">При переводе в «Готово» создастся следующая задача.</span>
                </div>
```

### 6.2. JS: заполнение при открытии (рядом с `tcm-details`/`tcm-notes`, @2552)
```javascript
        var _rEl = document.getElementById("tcm-recurring");
        if (_rEl) _rEl.value = d.recurringRule || "none";
```

### 6.3. JS: обработчик смены (рядом с функциями карточки)
```javascript
/* ░░ Фича 06: повтор ░░ */
window.tcmSetRecurrence = function () {
    var keyEl = document.getElementById("tcm-key");
    var sel   = document.getElementById("tcm-recurring");
    if (!keyEl || !sel || !keyEl.value) return;
    try {
        var res = window.external.InvokeTemplate("SetRecurrence", keyEl.value + "|" + sel.value);
        if (String(res).indexOf("ERROR") === 0) {
            if (typeof tcmShowMsg === "function") tcmShowMsg("err", String(res));
        } else {
            if (typeof tcmShowMsg === "function") tcmShowMsg("ok", "Правило повтора сохранено");
        }
    } catch (e) {}
};
```

---

## 7. Шаг 6 (опц.). Бейдж «↻» на карточке повторяемой задачи

Карточки рендерятся сервером (`BuildCardData` → Liquid). Чтобы показать значок:
1. В `BuildCardData` (метод, где собирается словарь полей карточки `t.*`) добавить поле, например `recurring` = `RecurringActive ? "1" : "0"`.
2. В HTML в 4 блоках карточки (`@308/@395/@482/@563`) добавить рядом с бейджем Directum:
   ```html
   {% if t.recurring == '1' %}<span class="kb-badge-recur" title="Повторяемая задача">&#8635;</span>{% endif %}
   ```
3. Стиль:
   ```css
   .kb-badge-recur{display:inline-block;color:#2d7ff9;font-weight:700;margin-left:4px;}
   ```

> Помечено опциональным, т.к. требует правки `BuildCardData` (точное место найти по сборке полей карточки – там, где формируются `priority`, `tags`, `isNew`).

---

## 8. Шаг 7 (опц.). Режим B – серверная фоновая задача (повтор по календарю)

Если нужно создавать экземпляры **по расписанию**, даже если предыдущий ещё не закрыт (из блокнота `Soyz-PLM`, «Серверная фоновая задача»):

1. В Конфигураторе создать объект **«Автоматическое действие»**, подтип **«Серверная фоновая задача»**.
2. Расписание: «Запуск в назначенное время, затем повтор с интервалом», интервал = 1 день (ночью).
3. На вкладке «Скрипты» переопределить `public override void Invoke()`:
```csharp
public override void Invoke()
{
    var container = Service.GetDataContainer( "All_Kanban_Tasks_Folder" );
    if( container == null ) return;
    foreach( var task in container.RootInfoObjects )
    {
        bool active = false;
        try { active = task.GetValue<bool>( "RecurringActive" ); } catch { }
        if( !active ) continue;

        // Пример: создавать следующий экземпляр, когда срок наступил/прошёл,
        // а активной незакрытой копии на эту дату ещё нет.
        DateTime due = default(DateTime);
        try { due = task.GetValue<DateTime>( "DueDate" ); } catch { }
        if( due == default(DateTime) || due.Date > DateTime.Today ) continue;

        // ... здесь логика анти-дубликата (проверить, нет ли уже копии с next-сроком) ...
        // и вызов аналогичный SpawnNextRecurrence (вынести общий код в shared-метод/библиотеку).
    }
}
```
> Важно: режим B требует защиты от дублей (не плодить копии при каждом ночном проходе). Для большинства инженерных задач достаточно режима A (по завершению) – начните с него.

---

## 9. Edge cases

- **Нет срока у задачи:** базой для расчёта берётся `DateTime.Today` (копия получит «завтра/следующий будний» и т.п.).
- **Зацикливание:** копия создаётся **только** при переходе `oldStatus != Done → Done`. Возврат из Done и повторное закрытие создаст ещё одну копию – это ожидаемо (каждое завершение = новый цикл). Если не нужно – добавить флаг «уже породил» в задачу.
- **Права:** правило ставит любой, кто видит задачу (как Directum). Хотите строже – добавьте проверку `canFullEdit` в `DoSetRecurrence`.
- **Исполнитель копии:** копируется текущий `Assignee`. Если исполнитель уволен/вне scope – задача всё равно создастся (серверное создание копии не проходит `CanAssignUserInScope`); при необходимости добавьте проверку.
- **IE11:** селектор и обработчик – чистый ES5.

---

## 10. Смок-тест (режим A)

- [ ] В карточке во вкладке «Основное» есть селектор «Повтор», по умолчанию «Нет».
- [ ] Выбрать «Ежедневно» – появляется тост «Правило повтора сохранено».
- [ ] Переоткрыть карточку – селектор показывает «Ежедневно» (правило сохранилось).
- [ ] Перетащить задачу в «Готово» – исходная ушла в Done, в «Надо сделать» появилась новая копия с тем же названием/исполнителем и сроком +1 день.
- [ ] У копии правило повтора тоже «Ежедневно».
- [ ] Исполнителю копии пришло уведомление «Новая задача».
- [ ] «По будням»: завершение в пятницу даёт срок копии = понедельник.
- [ ] «Еженедельно»/«Ежемесячно»: срок копии +7 дней / +1 месяц.
- [ ] Снять повтор («Нет») – при следующем завершении копия НЕ создаётся.
- [ ] Обычная (не повторяемая) задача завершается как раньше, без копий.

---

## 11. Откат

Убрать хук в `DoMoveTask`, методы `SpawnNextRecurrence`/`ComputeNextDue`/`DoSetRecurrence`, `case "SetRecurrence"`, чтение/JSON `recurringRule`, UI-селектор и JS. (Если делали режим B – удалить объект «Серверная фоновая задача».) Атрибуты можно оставить. Перекомпилировать.
