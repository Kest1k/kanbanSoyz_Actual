# Фича 04 – Вкладка «Блокнот / Заметки» в карточке

**Тип:** новый атрибут `Notes` в шаблоне `KanbanTask` + сервер (`SOYUZ_UPLOAD_KanbanScreen_script.cs`) + клиент (`KanbanBoard_HTML.html`, `kanban.js`).
**Риск:** низкий. Изолированная фича. Задаёт эталонный паттерн «атрибут → GetTaskDetails → SaveTask → новая вкладка», который переиспользуют фичи 05 и 06.

---

## 1. Поведение

В карточке задачи появляется пятая вкладка «Заметки» – свободный текстовый блокнот по задаче. Текст сохраняется вместе с карточкой (кнопка «Сохранить» / автосейв при сохранении карточки). По умолчанию заметки **общие** (хранятся в задаче, видны всем, кто видит задачу). Вариант «личные заметки на пользователя» – в разделе 7.

---

## 2. Шаг 1. Создать атрибут `Notes` в шаблоне `KanbanTask`

Через **Конфигуратор** (рекомендуется):
1. Открыть шаблон `KanbanTask`.
2. Добавить подчинённый объект «Определение атрибута» (`AttributeDef`):
   - **NameKey:** `Notes`
   - **Тип данных:** `Text` (простой текст; большой текст уходит в полнотекстовую колонку, длины достаточно для блокнота).
   - **Вариант отображения:** многострочное текстовое поле (на карточке самого PLM-объекта не обязателен – мы рисуем своё поле в HTML-модалке).
3. Сохранить.

Либо скриптом (см. блокнот `Soyz-PLM`, раздел `AttributeDef`):
```csharp
var t   = Service.GetTemplate( "KanbanTask" );
var def = new AttributeDef( t, "Notes", AttributeDefBase.DataTypeEnum.Text );
def.Save();
```

> После создания атрибута существующие задачи просто имеют пустые `Notes` – миграция не нужна.

---

## 3. Шаг 2. Отдать заметки клиенту – `DoGetTaskDetails`

**Файл:** `SOYUZ_UPLOAD_KanbanScreen_script.cs`, метод `DoGetTaskDetails` (@1663).

### 3.1. Прочитать атрибут (рядом с чтением `tags`, ориентир @1676)
После строки:
```csharp
    try { tags = task.GetString( "Tags" ) ?? ""; } catch { }
```
добавить:
```csharp
    string notes = "";
    try { notes = task.GetString( "Notes" ) ?? ""; } catch { }
```

### 3.2. Добавить в JSON-ответ (ориентир @1899, рядом с `"tags"`)
После строки:
```csharp
    sb.Append( "\"tags\":\""          + JsonEscape( tags )                + "\"," );
```
добавить:
```csharp
    sb.Append( "\"notes\":\""         + JsonEscape( notes )               + "\"," );
```

---

## 4. Шаг 3. Сохранить заметки – `DoSaveTask`

**Файл:** `SOYUZ_UPLOAD_KanbanScreen_script.cs`, метод `DoSaveTask` (@1969).

Текущий формат строки параметров (10 полей, заголовок-комментарий @1966–1968):
```
nameKey|title|status|priorityKey|dueDate|tags|assigneeKey|isPrivate|directumLink|details
```
`details` идёт **последним**, потому что может содержать `|` (Split с лимитом 10 забирает остаток). Поэтому новое поле `notes` вставляем **перед** `details`. Новый формат (11 полей):
```
nameKey|title|status|priorityKey|dueDate|tags|assigneeKey|isPrivate|directumLink|notes|details
```

### 4.1. Обновить разбор параметров (строки **1971–2000**)

Заменить весь блок разбора:
```csharp
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 10 );

    var nameKey       = parts.Length > 0 ? parts[0].Trim() : "";
    var title         = parts.Length > 1 ? parts[1].Trim() : "";
    var statusStr     = parts.Length > 2 ? parts[2].Trim() : "0";
    var prioKey       = parts.Length > 3 ? parts[3].Trim() : "";
    var dueDateStr    = parts.Length > 4 ? parts[4].Trim() : "";
    var tagsStr       = parts.Length > 5 ? parts[5].Trim() : "";
    var assigneeKeyIn = parts.Length > 6 ? parts[6].Trim() : "";
    var isPrivateIn    = "";
    var directumLinkIn = "";
    var detailsStr     = "";
    if( parts.Length > 9 )
    {
        // Новый формат: …|isPrivate|directumLink|details
        isPrivateIn    = parts[7].Trim();
        directumLinkIn = parts[8].Trim();
        detailsStr     = parts[9];    // НЕ Trim – пробелы в конце важны
    }
    else if( parts.Length > 8 )
    {
        // Прежний формат: …|isPrivate|details
        isPrivateIn = parts[7].Trim();
        detailsStr  = parts[8];
    }
    else
    {
        detailsStr  = parts.Length > 7 ? parts[7] : "";
    }
```
на:
```csharp
    var raw   = GetParamStr( inputParams );
    var parts = ParsePipeArgs( raw, 11 );          // фича 04: +поле notes

    var nameKey       = parts.Length > 0 ? parts[0].Trim() : "";
    var title         = parts.Length > 1 ? parts[1].Trim() : "";
    var statusStr     = parts.Length > 2 ? parts[2].Trim() : "0";
    var prioKey       = parts.Length > 3 ? parts[3].Trim() : "";
    var dueDateStr    = parts.Length > 4 ? parts[4].Trim() : "";
    var tagsStr       = parts.Length > 5 ? parts[5].Trim() : "";
    var assigneeKeyIn = parts.Length > 6 ? parts[6].Trim() : "";
    var isPrivateIn    = "";
    var directumLinkIn = "";
    var notesStr       = "";
    var detailsStr     = "";
    if( parts.Length > 10 )
    {
        // Новый формат (фича 04): …|isPrivate|directumLink|notes|details
        isPrivateIn    = parts[7].Trim();
        directumLinkIn = parts[8].Trim();
        notesStr       = parts[9];     // блокнот; пробелы/переносы важны – без Trim
        detailsStr     = parts[10];    // details всегда последний (может содержать |)
    }
    else if( parts.Length > 9 )
    {
        // Совместимость со старым клиентом: …|isPrivate|directumLink|details
        isPrivateIn    = parts[7].Trim();
        directumLinkIn = parts[8].Trim();
        detailsStr     = parts[9];
    }
    else if( parts.Length > 8 )
    {
        isPrivateIn = parts[7].Trim();
        detailsStr  = parts[8];
    }
    else
    {
        detailsStr  = parts.Length > 7 ? parts[7] : "";
    }
```

### 4.2. Записать `Notes`

Заметки делаем редактируемыми **любым, кто видит задачу** (как `DirectumLink`). После строки записи `DirectumLink` (@2102):
```csharp
    task["DirectumLink"] = directumLinkIn;
```
добавить:
```csharp
    // Фича 04: заметки может править любой, кто видит задачу (как Directum/вложения)
    try { task["Notes"] = notesStr; } catch { }
```

> Если нужно ограничить правку заметок только автором/руководителем – перенести эту строку **внутрь** блока `if( canFullEdit ) { ... }` (@2069–2098).

### 4.3. Обновить заголовок-комментарий (@1966–1968)
Привести к новому формату для будущих читателей кода:
```csharp
// SaveTask
// inputParams: "nameKey|title|status|priorityKey|dueDate|tags|assigneeKey|isPrivate|directumLink|notes|details"
// notes – блокнот задачи (фича 04); details идёт последним, т.к. может содержать |
```

---

## 5. Шаг 4. Клиент – `kanban.js`

### 5.1. Заполнить поле при открытии карточки (ориентир @2552)
В функции заполнения модалки, после строки:
```javascript
        document.getElementById("tcm-details").value = d.details || "";
```
добавить:
```javascript
        var _nEl = document.getElementById("tcm-notes");
        if (_nEl) _nEl.value = d.notes || "";
```

### 5.2. Передать заметки при сохранении – `tcmSave` (@3248)
Считать поле (рядом с чтением `details` @3258):
```javascript
        var notes = document.getElementById("tcm-notes") ? document.getElementById("tcm-notes").value : "";
```
Изменить сборку строки параметров (@3270). Было:
```javascript
        var param = nameKey + "|" + title + "|" + status + "|" + prio + "|" + dueDate + "|" + tags + "|" + assigneeKey + "|" + isPrivate + "|" + directumLink + "|" + details;
```
Стало (вставили `notes` перед `details`):
```javascript
        var param = nameKey + "|" + title + "|" + status + "|" + prio + "|" + dueDate + "|" + tags + "|" + assigneeKey + "|" + isPrivate + "|" + directumLink + "|" + notes + "|" + details;
```

> Порядок критичен: `notes` строго перед `details`, иначе серверный Split «съест» заметку в details.

---

## 6. Шаг 5. Клиент – `KanbanBoard_HTML.html`

### 6.1. Кнопка вкладки (ориентир @703, после `tab-btn-hist`)
После строки:
```html
                <div id="tab-btn-hist" class="tcm-tab" onclick="tcmSwitchTab('hist')">История</div>
```
добавить:
```html
                <div id="tab-btn-notes" class="tcm-tab" onclick="tcmSwitchTab('notes')">Заметки</div>
```

### 6.2. Контейнер вкладки (ориентир @822, после `tcm-tab-hist`)
После закрытия блока `<div id="tcm-tab-hist" ...></div>` добавить:
```html
            <div id="tcm-tab-notes" class="tcm-tab-content">
                <div class="tcm-notes-wrap">
                    <textarea id="tcm-notes" class="tcm-area tcm-notes-area"
                              placeholder="Блокнот по задаче: черновики, ссылки, ход мыслей..."></textarea>
                    <div class="tcm-notes-hint">Заметки сохраняются вместе с задачей (кнопка «Сохранить»).</div>
                </div>
            </div>
```

> **JS для переключения не трогаем.** `tcmSwitchTab` (@3622) универсальна: ищет `tcm-tab-<id>` и `tab-btn-<id>` – `notes` подхватится автоматически.

---

## 7. (Опционально) Личные заметки на каждого пользователя

Если заметки должны быть **приватными для каждого пользователя** (а не общими в задаче):
- Не использовать атрибут `Notes`. Хранить в реестре БИС:
  - Сохранение: новый серверный `case "SaveMyNotes"` → `Service.SetUserRegistryValue<string>( "Kanban/Notes/" + taskKey, text )`.
  - Чтение: новый `case "GetMyNotes"` → `Service.GetUserRegistryValue<string>( "Kanban/Notes/" + taskKey, "" )`.
- Клиент грузит заметки отдельным вызовом при открытии вкладки «Заметки» и сохраняет своей кнопкой.
- Плюс: у каждого свой блокнот. Минус: не виден коллегам, +2 серверные команды.

---

## 8. Шаг 6 (опц.). Стили – `kanban.css`
```css
/* ░░ Вкладка Заметки (фича 04) ░░ */
.tcm-notes-wrap{display:flex;flex-direction:column;height:100%;}
.tcm-notes-area{width:100%;min-height:260px;resize:vertical;font-family:inherit;font-size:13px;line-height:1.5;}
.tcm-notes-hint{color:#999;font-size:11px;margin-top:6px;}
```

---

## 9. Смок-тест

- [ ] В карточке появилась вкладка «Заметки» пятой по счёту, переключение работает.
- [ ] Ввести текст в заметки, нажать «Сохранить» – карточка сохранилась без ошибок.
- [ ] Закрыть и снова открыть карточку – заметки на месте.
- [ ] Многострочный текст с переносами и символом `|` внутри заметок сохраняется корректно и **не ломает** описание (details) и наоборот.
- [ ] Описание (`details`) по-прежнему сохраняется правильно (регресс старого формата).
- [ ] Старые задачи (без `Notes`) открываются с пустым блокнотом, без ошибок.
- [ ] Пользователь без полного права (видит, но не автор) может править заметки (если оставили вне `canFullEdit`); проверить выбранную политику.
- [ ] В истории/ChangeLog сохранение не падает.

---

## 10. Откат

Откатить правки `DoGetTaskDetails`, `DoSaveTask` (вернуть лимит 10 и старый разбор), `tcmSave`, fill-блок, убрать кнопку/контейнер вкладки и стили. Атрибут `Notes` можно оставить (не мешает) или удалить определение из шаблона. Перекомпилировать.
