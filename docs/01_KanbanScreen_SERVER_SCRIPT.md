# KanbanScreen — Серверный скрипт экрана

**Файл:** `SOYUZ_UPLOAD_KanbanScreen_script.cs`  
**Тип:** C# серверный скрипт экрана PLM

## Назначение
Главный серверный скрипт канбан-доски. Отвечает за:
- первичный рендер доски (`BeforeRender`)
- определение роли пользователя и области видимости
- создание, групповую постановку, удаление и перемещение задач
- карточку задачи: детали, сохранение, история, вложения, комментарии
- отчёты и иерархические фильтры

## Точка входа

Скрипт использует `Invoke(String methodName, InfoObject obj, Object inputParams)` и диспетчеризует команды по `methodName`.
Ключевые ветки `switch`:

| Категория | Команды |
|-----------|---------|
| Рендер и refresh | `BeforeRender`, `RefreshBoard` |
| CRUD задач | `MoveTask`, `CreateTask`, `CreateGroupTask`, `OpenTask`, `DeleteTask`, `SaveTask` |
| Иерархия | `GetHierarchyInfo`, `SetViewMode` |
| Отчёты | `GetReport` |
| Карточка задачи | `GetTaskDetails`, `GetTaskHistory`, `GetTaskRevisions` |
| Вложения | `GetAttachments`, `AddAttachment`, `RemoveAttachment`, `PickObject`, `PickObjects`, `PickAndAttach`, `PickContainers`, `PickAndAttachContainer`, `AddContainer`, `RemoveContainer`, `OpenObject`, `OpenContainer` |
| Комментарии | `GetComments`, `AddComment`, `DeleteComment` |

## Роли и область видимости

### Источник роли
Роль вычисляется функцией `GetUserRole(User user)` на основе пользовательского атрибута `Commission`.

Поддерживаются роли:

| Role | Как определяется | Scope |
|------|------------------|-------|
| `admin` | `Commission.NameKey` входит в массив `ADMIN_POS` | вся организация |
| `headOfDept` | `Commission.NameKey` входит в `DEPT_HEAD_POS` | отделение |
| `headOfSector` | `Commission.NameKey` входит в `SECTOR_HEAD_POS` | сектор |
| `leadEngineer` | `Commission.NameKey` начинается с `ved` | сектор |
| `regular` | fallback по умолчанию | только свои задачи |

`leadEngineer` введён отдельно от `headOfSector`, чтобы дать ведущим инженерам права на выдачу задач в своём секторе без смешения с ролью начальника сектора в UI.

### Источник организационного контекста
Функция `GetUserContext(User user)` читает атрибут `Context`.  
Примеры значений: `510кт`, `600кт`, `200.2кт`.

Вспомогательные методы:
- `ContextNumber(ctx)` — числовой префикс контекста
- `ContextSuffix(ctx)` — буквенный/дробный хвост
- `GetDivisionContext(ctx)` — родительское отделение
- `IsWithinContext(userCtx, ownerCtx)` — проверка вхождения сектора в отделение

### Режимы просмотра
Текущий режим хранится в `obj.PropertyBag["KbViewMode"]`.

Поддерживаемые значения:
- `my`
- `all`
- `dept`
- `sector`
- `user:KEY`
- `group:CTX`

Набор допустимых пользователей для каждого режима вычисляет `GetAllowedUserIdSet(...)`.

## Первичный рендер доски

В `BeforeRender` скрипт:
1. Берёт контейнер `All_Kanban_Tasks_Folder`
2. Определяет текущего пользователя, роль и `KbViewMode`
3. Строит допустимый набор исполнителей через `GetAllowedUserIdSet`
4. Отбирает задачи по исполнителю
5. Раскладывает задачи по 4 колонкам по статусу
6. Сортирует:
   - колонки 0-2 — по приоритету и новизне
   - колонку `Done` — по `CompletedDate`
7. Передаёт в Liquid-шаблон `col_0..col_3`, `kbRole`, `kbViewMode`, `availableTags`

Карточка для шаблона строится через `BuildCardData(...)`.

## Lifecycle-хуки

### `OnBeforeDisplayInUI(InfoObject obj, IPropertySheetCallback propertySheet)`
Срабатывает у платформы перед отрисовкой экрана.
Используется, чтобы доску можно было корректно открыть по внешней ссылке:

- если `propertySheet.IsDialog == true` или родительская панель — это
  `IPropertiesBrowserPanel` (экран открывается как карточка/в боковой панели),
  скрываются штатные элементы оформления:
  - `ToolStripVisibility = false`
  - `TabStripVisibility = false`

Для «штатного» открытия экрана иерархии (когда `propertySheet` — обычный таб)
условие не срабатывает, оформление остаётся по умолчанию.

### Связка с `GlobalLinkHandler`

В репозитории лежит [`scripts/GlobalLinkHandler.cs`](../scripts/GlobalLinkHandler.cs) —
глобальный обработчик внешних ссылок Soyuz-PLM. Он ловит клик по ссылке на
объект канбан-доски (`Id = 14068UL` на тестовом сервере, `Id = 804663UL` на
основном) и открывает его через `Service.UI.OpenPropertiesPane(io)`. После
открытия обработчик подгружает свежие изменения клиента и вызывает
`RefreshBoard`, чтобы доска не открывалась пустой. Вместе с `OnBeforeDisplayInUI`
это даёт чистое открытие доски без лишней обвязки тулбара/вкладок.

## Контракты основных методов

### `MoveTask`
**Вход:** `"nameKey|newStatus"`  
**Права:** исполнитель, создатель или `admin`  
**Поведение:** меняет статус, при переходе в `Done` ставит `CompletedDate`, при возврате из `Done` очищает её

### `CreateTask`
**Вход:** `"title|status|priority|dueDate|details|tags|assigneeKey"`  
**Поведение:** создаёт одну задачу, по умолчанию назначает текущему пользователю  
**Важно:** если указан `assigneeKey`, сервер валидирует его через `CanAssignUserInScope(...)`

### `CreateGroupTask`
**Вход:** `"title|status|priority|dueDate|details|tags|key1,key2,key3"`  
**Поведение:** создаёт отдельную задачу каждому допустимому исполнителю  
**Важно:** недопустимые исполнители отбрасываются на сервере, а если не осталось ни одного — возвращается `ERROR:NoAllowedAssignees`

### `GetHierarchyInfo`
**Возвращает JSON** с полями:
- `role`
- `myKey`
- `myContext`
- `viewMode`
- `divisions`
- `sectors`
- `users`

Для каждого пользователя также сериализуется `subrole`, который использует клиентский UI.

### `GetReport`
**Вход:** `"period|scope"`  
`period`: `week`, `month`, `quarter`, `all`  
`scope`: режим просмотра или пустая строка для role-default  

### `GetTaskDetails`
Возвращает полный JSON для модального окна:
- базовые поля задачи
- `canFullEdit`
- `creatorName`
- `subordinates`
- `priorities`
- `availableTags`
- `assigneeKey`

Если у пользователя есть право полного редактирования, `subordinates` строится в рамках его допустимого scope.

### `SaveTask`
**Вход:** `"nameKey|title|status|priorityKey|dueDate|tags|assigneeKey|details"`  
**Права:** полный edit только у создателя; остальные могут менять только статус  
**Важно:** смена исполнителя тоже проверяется сервером через `CanAssignUserInScope(...)`

## Серверная валидация назначения

Функция `CanAssignUserInScope(User currentUser, string currentRole, User targetUser)` жёстко ограничивает допустимого исполнителя:
- `admin` — любой пользователь
- `headOfDept` — только сотрудники своего отделения
- `headOfSector` и `leadEngineer` — только сотрудники своего сектора
- `regular` — только сам себе

Это защищает систему от обхода фронтенда и прямых вызовов `InvokeTemplate(...)` с чужими ключами пользователей.

## Безопасность и права

- **Удаление**: только создатель задачи; для legacy-задач без `Creator` сохранён permissive fallback
- **Перемещение**: исполнитель, создатель или `admin`
- **Полное редактирование карточки**: только создатель
- **Смена исполнителя**: только создатель и только в разрешённом scope
- **Фильтрация доски и отчётов**: всегда ограничена допустимым набором пользователей

## Подтверждённые особенности реализации

- В качестве стабильного ключа пользователя используется `NameKey`, иначе `AccountId`, иначе `Id`
- Для задач без `NameKey`, созданных вне доски, используется fallback `__id_<Id>`
- Строковые ошибки возвращаются в формате `ERROR:...`, клиентский JS показывает их напрямую
- В C#-скриптах платформы нельзя полагаться на `??` для `NameKey`, потому что пустая строка не равна `null`
