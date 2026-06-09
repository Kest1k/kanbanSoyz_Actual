# Фича 05 – Отключение уведомлений в обсуждении задачи

**Тип:** новый атрибут `CommentMutedBy` в шаблоне `KanbanTask` + сервер (`SOYUZ_UPLOAD_KanbanScreen_script.cs`) + клиент (`KanbanBoard_HTML.html`, `kanban.js`).
**Риск:** средний (вклинивается в путь рассылки уведомлений).

---

## 1. Поведение

В карточке на вкладке «Обсуждение» – переключатель «колокольчик». Если пользователь его выключает, он **перестаёт получать уведомления о новых комментариях именно по этой задаче**. На другие задачи и на уведомления о назначении это не влияет.

**Модель хранения:** атрибут задачи `CommentMutedBy` – строка стабильных ключей пользователей через запятую, которые «заглушили» обсуждение этой задачи. Это персонально (каждый сам себя добавляет/убирает) и при этом просто (один атрибут, без отдельных объектов).

> Фича сознательно ограничена **обсуждением** (комментариями), как и просил заказчик. Путь уведомления о смене исполнителя (`SOYUZ_UPLOAD_KanbanTask_script.cs`) не трогаем – см. раздел 7, если захотите расширить.

---

## 2. Шаг 1. Создать атрибут `CommentMutedBy`

Через **Конфигуратор**: в шаблоне `KanbanTask` добавить `AttributeDef`:
- **NameKey:** `CommentMutedBy`
- **Тип:** `Text`

Либо скриптом:
```csharp
var t   = Service.GetTemplate( "KanbanTask" );
var def = new AttributeDef( t, "CommentMutedBy", AttributeDefBase.DataTypeEnum.Text );
def.Save();
```

---

## 3. Шаг 2. Подавить рассылку замьюченным – `SendCommentNotification`

**Файл:** `SOYUZ_UPLOAD_KanbanScreen_script.cs`. Метод `SendCommentNotification` (@4288). Цикл рассылки – @4318.

### 3.1. Добавить проверку в цикле рассылки
Было (@4318–4319):
```csharp
    foreach( var u in recipients )
        SendWorkItemNotify( tmpl, u, subject, preview, task.NameKey );
```
Стало:
```csharp
    foreach( var u in recipients )
    {
        if( IsCommentsMutedBy( task, u ) ) continue;   // фича 05: пользователь заглушил эту задачу
        SendWorkItemNotify( tmpl, u, subject, preview, task.NameKey );
    }
```

### 3.2. Помощник проверки mute (добавить рядом, например после `SendWorkItemNotify` @4356)
```csharp
// ░░ Фича 05 ░░ проверка «заглушено ли обсуждение задачи пользователем»
private bool IsCommentsMutedBy( InfoObject task, User user )
{
    if( task == null || user == null ) return false;
    string list = "";
    try { list = task.GetString( "CommentMutedBy" ) ?? ""; } catch { }
    if( string.IsNullOrEmpty( list ) ) return false;
    var key = GetUserStableKey( user );
    if( string.IsNullOrEmpty( key ) ) return false;
    foreach( var k in list.Split( new char[]{ ',' }, StringSplitOptions.RemoveEmptyEntries ) )
        if( k.Trim() == key ) return true;
    return false;
}
```

---

## 4. Шаг 3. Команды переключения/чтения mute

**Файл:** `SOYUZ_UPLOAD_KanbanScreen_script.cs`.

### 4.1. Зарегистрировать команды в `switch` (рядом с комментариями, @215–219)
```csharp
            case "ToggleCommentMute": return DoToggleCommentMute( inputParams );
            case "GetCommentMute":    return DoGetCommentMute( inputParams );
```

### 4.2. Реализация методов
```csharp
// ░░ Фича 05 ░░ переключить mute обсуждения текущим пользователем. Возвращает "1" (замьючено) / "0".
private object DoToggleCommentMute( object inputParams )
{
    var taskKey = GetParamStr( inputParams );
    if( string.IsNullOrEmpty( taskKey ) ) return "ERROR:EmptyId";
    var task = GetTaskByKeyOrNull( taskKey );
    if( task == null ) return "ERROR:TaskNotFound";
    var me = Service.GetCurrentUser();
    if( !CanUserSeeTask( task, me ) ) return "ERROR:Forbidden";
    var key = GetUserStableKey( me );
    if( string.IsNullOrEmpty( key ) ) return "ERROR:NoUser";

    string list = "";
    try { list = task.GetString( "CommentMutedBy" ) ?? ""; } catch { }

    var set = new System.Collections.Generic.List<string>();
    if( !string.IsNullOrEmpty( list ) )
        foreach( var k in list.Split( new char[]{ ',' }, StringSplitOptions.RemoveEmptyEntries ) )
        {
            var kk = k.Trim();
            if( kk.Length > 0 && !set.Contains( kk ) ) set.Add( kk );
        }

    bool nowMuted;
    if( set.Contains( key ) ) { set.Remove( key ); nowMuted = false; }
    else                      { set.Add( key );    nowMuted = true;  }

    var ed = task.GetEditable();
    ed["CommentMutedBy"] = string.Join( ",", set.ToArray() );
    ed.Save();
    return nowMuted ? "1" : "0";
}

// ░░ Фича 05 ░░ узнать текущее состояние mute для текущего пользователя. "1"/"0".
private object DoGetCommentMute( object inputParams )
{
    var taskKey = GetParamStr( inputParams );
    if( string.IsNullOrEmpty( taskKey ) ) return "0";
    var task = GetTaskByKeyOrNull( taskKey );
    if( task == null ) return "0";
    return IsCommentsMutedBy( task, Service.GetCurrentUser() ) ? "1" : "0";
}
```

---

## 5. Шаг 4. UI – `KanbanBoard_HTML.html`

Вкладка обсуждения: `tcm-tab-chat` (@811), внутри `tcm-chat` (@812). Вставить переключатель сразу после `<div class="tcm-chat" id="tcm-chat">`:

```html
                <div class="tcm-chat-bar">
                    <button type="button" id="tcm-mute-btn" class="tcm-mute-btn"
                            onclick="tcmToggleMute()" title="Уведомления о новых комментариях по этой задаче">
                        <span id="tcm-mute-ico">&#128276;</span>
                        <span id="tcm-mute-lbl">Уведомления вкл.</span>
                    </button>
                </div>
```

---

## 6. Шаг 5. Логика – `kanban.js`

### 6.1. Функции mute (добавить рядом с функциями карточки)
```javascript
/* ░░ Фича 05: mute обсуждения ░░ */
window.tcmToggleMute = function () {
    var key = document.getElementById("tcm-key") ? document.getElementById("tcm-key").value : "";
    if (!key) return;
    var res = "";
    try { res = window.external.InvokeTemplate("ToggleCommentMute", key); } catch (e) { return; }
    if (String(res).indexOf("ERROR") === 0) {
        if (typeof tcmShowMsg === "function") tcmShowMsg("err", String(res));
        return;
    }
    tcmRenderMute(res === "1");
};

function tcmRenderMute(muted) {
    var ico = document.getElementById("tcm-mute-ico");
    var lbl = document.getElementById("tcm-mute-lbl");
    var btn = document.getElementById("tcm-mute-btn");
    if (ico) ico.innerHTML = muted ? "&#128277;" : "&#128276;"; // 🔕 / 🔔
    if (lbl) lbl.innerHTML = muted ? "Уведомления выкл." : "Уведомления вкл.";
    if (btn) btn.className = "tcm-mute-btn" + (muted ? " tcm-mute-on" : "");
}

window.tcmLoadMute = function () {
    var key = document.getElementById("tcm-key") ? document.getElementById("tcm-key").value : "";
    if (!key) return;
    var res = "0";
    try { res = window.external.InvokeTemplate("GetCommentMute", key); } catch (e) { res = "0"; }
    tcmRenderMute(res === "1");
};
```

### 6.2. Загружать состояние при открытии вкладки «Обсуждение»
`tcmSwitchTab` (@3622), блок `if (tabId === 'chat') { ... }` (@3633). Дополнить:
```javascript
        if (tabId === 'chat') {
            var list = document.getElementById("tcm-chat-list");
            if (list) list.scrollTop = list.scrollHeight;
            if (typeof tcmLoadMute === "function") tcmLoadMute();   // фича 05
        }
```

---

## 7. Шаг 6 (опц.). Стили – `kanban.css`
```css
/* ░░ Mute обсуждения (фича 05) ░░ */
.tcm-chat-bar{display:flex;justify-content:flex-end;padding:4px 0;}
.tcm-mute-btn{display:inline-flex;align-items:center;gap:6px;border:1px solid #ccc;background:#fff;
    border-radius:14px;padding:3px 10px;font-size:12px;cursor:pointer;color:#444;}
.tcm-mute-btn:hover{border-color:#999;}
.tcm-mute-on{background:#fdecec;border-color:#e3a3a3;color:#b23b3b;}
```

---

## 8. (Опционально) Расширения

- **Заглушить уведомление о назначении тоже:** в `SOYUZ_UPLOAD_KanbanTask_script.cs`, метод `CheckAssigneeChanged`, перед созданием `WorkItem` (@92) добавить `if( IsCommentsMutedBy( obj, newAssignee ) ) return;`. Но семантически назначение задачи обычно важно даже при mute обсуждения – по умолчанию **не** делаем.
- **Глобальное «не беспокоить» на пользователя:** хранить флаг в реестре БИС `Service.SetUserRegistryValue<bool>("Kanban/MuteAllComments", true)` и проверять его в `IsCommentsMutedBy` первым условием. Полезно для «в отпуске».

---

## 9. Смок-тест

Двое: Автор (A) и Исполнитель (B).
- [ ] B открывает задачу → «Обсуждение»: видит «Уведомления вкл.» (🔔).
- [ ] A пишет комментарий → B получает popup-уведомление (как раньше).
- [ ] B жмёт колокольчик → стало «Уведомления выкл.» (🔕).
- [ ] A пишет ещё комментарий → B **не** получает уведомление; A (если ему пишет B) получает как обычно.
- [ ] B переоткрывает карточку → состояние mute сохранилось (🔕).
- [ ] B включает обратно → уведомления снова приходят.
- [ ] Mute по задаче №1 не влияет на уведомления по задаче №2.
- [ ] Уведомление о **назначении** задачи приходит независимо от mute обсуждения (если не включали опцию из раздела 8).
- [ ] Сами комментарии в обсуждении по-прежнему видны (mute гасит только всплывающие уведомления, не сам чат).

---

## 10. Откат

Вернуть цикл рассылки в `SendCommentNotification`, убрать `IsCommentsMutedBy`, `DoToggleCommentMute`, `DoGetCommentMute`, два `case`, UI-кнопку, JS-функции и стили. Атрибут `CommentMutedBy` можно оставить. Перекомпилировать.
