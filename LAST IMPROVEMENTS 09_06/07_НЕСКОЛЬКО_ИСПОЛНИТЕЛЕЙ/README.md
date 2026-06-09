# Фича 07 – Несколько исполнителей (на подумать)

**Тип:** архитектурная. Внедрять в последнюю очередь.
**Риск:** высокий для «честного» мультиассайна; низкий для рекомендованного варианта «Соисполнители».

---

## 1. Вывод аналитики (коротко)

Полноценный «несколько исполнителей в одной карточке» **ломает модель канбана**, где статус принадлежит карточке. Если у задачи два исполнителя, один может закрыть её в «Готово», а второй ещё в «В работе» – единый `KanbanStatus` это не выражает. Именно поэтому в проекте уже сделан механизм **`CreateGroupTask`**: при выдаче на нескольких людей создаются **отдельные карточки-клоны** на каждого (см. `DoCreateGroupTask`, @487). Это правильное решение для «дать одно поручение многим».

Поэтому рекомендуется **не** превращать `Assignee` в множество, а закрыть реальную потребность двумя путями:
- **Вариант A (рекомендуется к внедрению): «Соисполнители / Наблюдатели» (`Watchers`)** – дополнительные люди, которые видят задачу, участвуют в обсуждении и получают уведомления, но **не** владеют статусом. Безопасно, аддитивно.
- **Вариант B (не рекомендуется): честный мультиассайн** – менять тип `Assignee` на множество и переписывать ~10 мест. Дорого и рискованно. Дан как карта работ на случай, если бизнес настаивает.

---

## 2. Почему single-assignee оставляем (обоснование по коду)

`Assignee` – ссылка на одного `User`. Жёстко завязан в:

| Место | Файл / метод | Что делает |
|------|--------------|------------|
| Создание | `DoCreateTask` @389 (`task["Assignee"]=...` @460/477), `DoCreateGroupTask` @487 | один исполнитель на карточку |
| Сохранение | `DoSaveTask` @1969 (`assigneeKeyIn` @1980) | один ключ |
| Перемещение | `DoMoveTask` @268 | «двигать может исполнитель» (один) |
| Видимость/приватность | `CanUserSeeTask` @1184 (`task.GetUser("Assignee")` @1197) | один |
| Scope доски | `BeforeRender` @76 (`task.GetUser("Assignee")`), `GetAllowedUserIdSet` @1589 | сравнение по одному Id |
| Карточка | `DoGetTaskDetails` @1692/@1953 (`assigneeName`, `assigneeKey`) | один |
| Уведомление о назначении | `SOYUZ_UPLOAD_KanbanTask_script.cs` `CheckAssigneeChanged` @24 | дельта одного |
| Уведомление о комментарии | `SendCommentNotification` @4288 (`task.GetUser("Assignee")` @4296) | один получатель |
| Отчёты/Excel | `DoGetReport`, `DoExportToExcel` | группировка по исполнителю |
| UI карточки/доски | `kanban.js` `tcmFill`/`tcmSave`, Liquid `data-assignee` | одно поле |

Перевод `Assignee` в множество требует правок во всех строках выше + мультивыбор в IE11. Вариант A добавляет один атрибут и точечные хуки.

---

## 3. ВАРИАНТ A – «Соисполнители / Наблюдатели» (рекомендуется)

### 3.1. Семантика
- `Assignee` остаётся единственным **ответственным** (двигает статус, владеет задачей).
- `Watchers` – строка стабильных ключей через запятую: соисполнители/наблюдатели.
- Watchers: **видят** задачу (даже приватную и вне обычного scope), **участвуют** в обсуждении, **получают** уведомления о комментариях (и опц. о смене статуса). Статус не двигают (или двигают – по настройке, см. 3.6).

### 3.2. Атрибут
Через Конфигуратор в `KanbanTask`: `Watchers` – тип `Text`.
```csharp
new AttributeDef( Service.GetTemplate("KanbanTask"), "Watchers",
                  AttributeDefBase.DataTypeEnum.Text ).Save();
```

### 3.3. Видимость – `CanUserSeeTask` (@1184)
Сейчас задача видна автору и исполнителю (для приватных) / всем в scope (для обычных). Добавляем watcher как «всегда видит».

В конец `CanUserSeeTask`, перед `return (...)` (@1202), добавить ранний выход:
```csharp
    // Фича 07A: наблюдатель всегда видит задачу
    if( IsWatcher( task, currentUser ) ) return true;
```
И для обычных (не приватных) задач это и так true. Главное – watcher пробивает приватность и scope. Дополнительно в `BeforeRender` (scope-фильтр @80) включить задачи, где текущий пользователь – watcher:

Было (@80–83):
```csharp
                    if( allowedIds != null && !allowedIds.Contains( assignee.Id.ToString() ) )
                        continue;
                    if( !CanUserSeeTask( task, currentUser ) )
                        continue;
```
Стало:
```csharp
                    bool iAmWatcher = IsWatcher( task, currentUser );      // фича 07A
                    if( allowedIds != null && !allowedIds.Contains( assignee.Id.ToString() ) && !iAmWatcher )
                        continue;
                    if( !CanUserSeeTask( task, currentUser ) )
                        continue;
```

Помощник:
```csharp
// ░░ Фича 07A ░░
private bool IsWatcher( InfoObject task, User user )
{
    if( task == null || user == null ) return false;
    string list = "";
    try { list = task.GetString( "Watchers" ) ?? ""; } catch { }
    if( string.IsNullOrEmpty( list ) ) return false;
    var key = GetUserStableKey( user );
    if( string.IsNullOrEmpty( key ) ) return false;
    foreach( var k in list.Split( new char[]{ ',' }, StringSplitOptions.RemoveEmptyEntries ) )
        if( k.Trim() == key ) return true;
    return false;
}
```

### 3.4. Уведомления о комментариях – `SendCommentNotification` (@4288)
Добавить watchers в получателей. После формирования `recipients` (после @4310, до проверки `recipients.Count == 0`):
```csharp
    // Фича 07A: добавить наблюдателей в получатели уведомлений о комментариях
    var watchersStr = "";
    try { watchersStr = task.GetString( "Watchers" ) ?? ""; } catch { }
    if( !string.IsNullOrEmpty( watchersStr ) )
        foreach( var wk in watchersStr.Split( new char[]{ ',' }, StringSplitOptions.RemoveEmptyEntries ) )
        {
            var wu = TryFindUserByKey( wk.Trim() );
            if( wu == null || wu.Id == author.Id ) continue;
            bool dup = false;
            foreach( var r in recipients ) if( r.Id == wu.Id ) { dup = true; break; }
            if( !dup ) recipients.Add( wu );
        }
```
(Если внедрена фича 05, mute по-прежнему отфильтрует замьюченных в цикле рассылки.)

### 3.5. Команды управления watchers
`switch` (рядом с комментариями @215):
```csharp
            case "AddWatcher":    return DoAddWatcher( inputParams );
            case "RemoveWatcher": return DoRemoveWatcher( inputParams );
            case "GetWatchers":   return DoGetWatchers( inputParams );
```
Методы:
```csharp
// ░░ Фича 07A ░░ inputParams: "taskKey|userKey"
private object DoAddWatcher( object inputParams )    { return ChangeWatcher( inputParams, true ); }
private object DoRemoveWatcher( object inputParams ) { return ChangeWatcher( inputParams, false ); }

private object ChangeWatcher( object inputParams, bool add )
{
    var parts = ParsePipeArgs( GetParamStr( inputParams ), 2 );
    var taskKey = parts.Length > 0 ? parts[0].Trim() : "";
    var userKey = parts.Length > 1 ? parts[1].Trim() : "";
    if( string.IsNullOrEmpty( taskKey ) || string.IsNullOrEmpty( userKey ) ) return "ERROR:BadParams";

    var task = GetTaskByKeyOrNull( taskKey );
    if( task == null ) return "ERROR:TaskNotFound";
    if( !CanUserSeeTask( task, Service.GetCurrentUser() ) ) return "ERROR:Forbidden";

    string list = ""; try { list = task.GetString( "Watchers" ) ?? ""; } catch { }
    var set = new System.Collections.Generic.List<string>();
    if( !string.IsNullOrEmpty( list ) )
        foreach( var k in list.Split( new char[]{ ',' }, StringSplitOptions.RemoveEmptyEntries ) )
        { var kk = k.Trim(); if( kk.Length>0 && !set.Contains(kk) ) set.Add(kk); }

    if( add ) { if( !set.Contains( userKey ) ) set.Add( userKey ); }
    else      { set.Remove( userKey ); }

    var ed = task.GetEditable();
    ed["Watchers"] = string.Join( ",", set.ToArray() );
    ed.Save();
    return "OK";
}

// JSON-массив наблюдателей: [{"key":"...","name":"..."}]
private object DoGetWatchers( object inputParams )
{
    var taskKey = GetParamStr( inputParams );
    if( string.IsNullOrEmpty( taskKey ) ) return "[]";
    var task = GetTaskByKeyOrNull( taskKey );
    if( task == null ) return "[]";
    string list = ""; try { list = task.GetString( "Watchers" ) ?? ""; } catch { }
    var sb = new System.Text.StringBuilder(); sb.Append( "[" );
    bool first = true;
    if( !string.IsNullOrEmpty( list ) )
        foreach( var k in list.Split( new char[]{ ',' }, StringSplitOptions.RemoveEmptyEntries ) )
        {
            var u = FindUserByKeyOrNull( k.Trim() );
            var nm = u != null ? ( u.ToString() ?? k.Trim() ) : k.Trim();
            if( !first ) sb.Append( "," );
            sb.Append( "{\"key\":\"" + JsonEscape( k.Trim() ) + "\",\"name\":\"" + JsonEscape( nm ) + "\"}" );
            first = false;
        }
    sb.Append( "]" );
    return sb.ToString();
}
```

### 3.6. (Опц.) Разрешить watcher двигать статус
Если соисполнитель должен иметь право двигать карточку – в `DoMoveTask` (@263–286) в проверку «isOwner» добавить `|| IsWatcher( task, currentUser )`. По умолчанию **не** даём (ответственный один).

### 3.7. Клиент (кратко)
- Во вкладке «Основное» карточки – блок «Соисполнители»: список чипов + кнопка «＋», открывающая выбор пользователя (переиспользовать механизм выбора подчинённых из `subordinates`, который уже приходит в `DoGetTaskDetails` @1906).
- Функции `tcmWatchersRender` / `tcmWatcherAdd` / `tcmWatcherRemove`, вызывающие `GetWatchers` / `AddWatcher` / `RemoveWatcher` (по аналогии с Directum-чипами `tcmDlinks*`, @3340).
- На карточке доски (опц.) бейдж «+N» соисполнителей.

### 3.8. Смок-тест (вариант A)
- [ ] Автор добавляет соисполнителя C к задаче пользователя B.
- [ ] C видит задачу на своей доскe (даже если она приватная или вне его сектора).
- [ ] При новом комментарии уведомление приходит B, автору и C.
- [ ] C может писать комментарии.
- [ ] C не может перетащить карточку в другой статус (если не включали 3.6).
- [ ] Удаление C из соисполнителей убирает задачу с его доски и из его уведомлений.
- [ ] Если внедрена фича 05, C может заглушить обсуждение и перестать получать уведомления.

---

## 4. ВАРИАНТ B – честный мультиассайн (карта работ, не рекомендуется)

Только если бизнес явно требует «одна карточка – несколько равноправных исполнителей со статусом».

1. **Схема:** заменить `Assignee` (LinkToUser) на множество пользователей. Два пути:
   - атрибут-множество пользователей (тип «Множество ссылок на User»), либо
   - хранить `AssigneeKeys` (Text, ключи через запятую) + оставить `Assignee` как «первичного» для обратной совместимости.
2. **Переписать все места из таблицы раздела 2:**
   - `DoCreateTask`/`DoSaveTask` – принимать список ключей, валидировать каждого через `CanAssignUserInScope`.
   - `CanUserSeeTask` – `assignees.Contains(me)`.
   - `BeforeRender`/`GetAllowedUserIdSet` – задача попадает в scope, если **любой** из исполнителей в allowed-наборе (пересечение множеств).
   - `DoMoveTask` – «двигать может любой из исполнителей или создатель».
   - `CheckAssigneeChanged` (KanbanTask) – вычислять дельту множеств (кому добавили), слать уведомление только добавленным.
   - `SendCommentNotification` – рассылать всем исполнителям.
   - `DoGetTaskDetails` – отдавать массив исполнителей; UI – мультивыбор (в IE11 – свой компонент чипов, без сторонних библиотек).
   - Отчёты/Excel – решить, дублировать задачу по каждому исполнителю или вводить категорию «совместные».
3. **Проблема статуса** остаётся концептуально нерешённой: один статус на карточку с несколькими исполнителями. Обычно решается переходом обратно к «клонам» (т.е. к существующему `CreateGroupTask`).

**Рекомендация:** не внедрять B. Потребность «несколько исполнителей» закрывается комбинацией существующего `CreateGroupTask` (раздать клоны) + вариант A (соисполнители-наблюдатели на одной карточке).

---

## 5. Откат (вариант A)

Убрать `IsWatcher`-хуки в `CanUserSeeTask`/`BeforeRender`, добавление watchers в `SendCommentNotification`, методы `DoAddWatcher`/`DoRemoveWatcher`/`DoGetWatchers` и три `case`, клиентские функции и UI. Атрибут `Watchers` можно оставить. Перекомпилировать.
