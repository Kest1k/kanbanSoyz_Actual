# kanban.js — Клиентская логика

**Файл:** `kanban.js`
**Тип:** Vanilla JavaScript (IIFE, без фреймворков)

## Назначение
Клиентский скрипт канбан-доски. Реализует всю интерактивность:
- Drag & Drop задач между колонками
- Модальное окно карточки задачи (просмотр/редактирование)
- Панель создания задачи
- Календарь выбора даты
- Чат (комментарии)
- Вложения (поиск и привязка объектов PLM)
- Фильтры и иерархия подразделений
- Отчёт по задачам

## Архитектура

Весь код обёрнут в IIFE `(function() { ... })()` — нет глобальных переменных.
Функции вызываются из HTML через `onclick` атрибуты.

## Основные модули

### 1. Board Adjust — Подстройка высоты доски
```javascript
function boardAdjust()
```
Переключает CSS-класс `panel-open` на `#kb-board` при открытии/закрытии панели создания.

### 2. Календарь (универсальный)

**Переменные состояния:**
- `_calYear`, `_calMonth` — текущий отображаемый месяц
- `_calTargetId` — ID input-поля, куда записывается выбранная дата
- `_MN` — массив русских названий месяцев

**Функции:**
```javascript
function calOpen(inputId)     // Открыть календарь для конкретного input
function calRender()          // Отрисовать сетку дней
function calPrev() / calNext() // Навигация по месяцам
function calPick(d)           // Выбрать дату → записать в _calTargetId
function calClose()           // Закрыть календарь
```

Календарь используется в двух местах:
- Панель создания задачи (`kb-new-duedate`)
- Модальное окно редактирования (`kb-modal-duedate`)

### 3. Drag & Drop

**Переменные:**
- `_dragCard` — перетаскиваемый DOM-элемент
- `_dragSourceCol` — исходная колонка

**Функции:**
```javascript
function dragStart(ev)        // Начало перетаскивания — сохраняет taskId
function allowDrop(ev)        // Разрешает drop (preventDefault)
function dropCard(ev)         // Обработка drop:
                              //   1. Определяет целевую колонку
                              //   2. Перемещает DOM-элемент
                              //   3. Отправляет AJAX moveTask
                              //   4. Обновляет счётчики колонок
```

**Логика drop:**
- Карточка вставляется в начало целевой колонки
- Если перемещение в колонку «Готово» (status=3) — добавляется дата завершения
- Если из «Готово» — дата завершения убирается
- Счётчики `(N)` в заголовках колонок обновляются

### 4. Модальное окно карточки

**Функции:**
```javascript
function openCard(taskId)     // Открыть модалку:
                              //   1. AJAX getCard → заполнить поля
                              //   2. Загрузить комментарии
                              //   3. Загрузить вложения
                              //   4. Загрузить историю
function closeCard()          // Закрыть модалку
function saveCard()           // Сохранить изменения (AJAX saveCard)
function deleteCard()         // Удалить задачу (AJAX deleteCard + confirm)
```

**Вкладки модалки:**
```javascript
function switchTab(tabName)   // Переключение: 'comments', 'attachments', 'history'
```

### 5. Комментарии (чат)

```javascript
function loadComments(taskId)       // Загрузить список комментариев
function sendComment()              // Отправить новый комментарий
function deleteComment(commentId)   // Удалить комментарий (с confirm)
function renderComments(comments)   // Отрисовать список в DOM
```

**Формат отображения:**
- Аватар с инициалами (цветной кружок)
- Имя автора + время
- Текст комментария
- Кнопка удаления (только для своих)

### 6. Вложения

```javascript
function loadAttachments(taskId)          // Загрузить список вложений
function searchObjects(query)             // Поиск объектов PLM (AJAX searchObjects)
function addAttachment(objectId)          // Привязать объект
function removeAttachment(attachmentId)   // Отвязать объект
function renderAttachments(attachments)   // Отрисовать список
function renderSearchResults(results)     // Отрисовать результаты поиска
```

### 7. Панель создания задачи

```javascript
function toggleCreatePanel()    // Показать/скрыть панель создания
function createTask()           // Создать задачу (AJAX createTask)
function createGroupTasks()     // Групповое создание (AJAX createGroupTasks)
function resetCreateForm()      // Очистить форму
```

**Поля формы создания:**
- `kb-new-title` — название
- `kb-new-details` — описание
- `kb-new-priority` — приоритет (select)
- `kb-new-duedate` — срок (через календарь)
- `kb-new-assignee` — исполнитель (select, только для руководителей)

### 8. Фильтры и иерархия

```javascript
function toggleMode(mode)           // Переключение «Мои задачи» / «Все задачи»
function loadHierarchy()            // Загрузить дерево подразделений
function filterByDepartment(id)     // Фильтр по отделению
function filterBySector(id)         // Фильтр по сектору
function filterByUser(id)           // Фильтр по сотруднику
function reloadBoard()              // Перезагрузить доску с текущими фильтрами
function renderBoard(data)          // Отрисовать все колонки из JSON
```

### 9. Отчёт

```javascript
function openReport()               // Открыть модалку отчёта
function loadReport(period)         // Загрузить данные (AJAX getReport)
function renderReport(data)         // Отрисовать таблицу отчёта
function closeReport()              // Закрыть модалку
```

## AJAX-обёртка

```javascript
function ajax(action, params, callback)
```
- Формирует POST-запрос к текущему URL с `ajaxAction` параметром
- Парсит JSON-ответ
- Вызывает callback с результатом
- При ошибке показывает alert

## Утилиты

```javascript
function escapeHtml(str)            // Экранирование HTML-спецсимволов
function formatDate(dateStr)        // Форматирование даты
function updateColumnCounts()       // Пересчёт счётчиков в заголовках колонок
function getStatusName(status)      // Числовой статус → русское название
function getPriorityBadge(priority) // Приоритет → HTML-бейдж с цветом
```

## Глобальные переменные (внутри IIFE)

| Переменная | Тип | Описание |
|------------|-----|----------|
| `_calYear` | int | Год в календаре |
| `_calMonth` | int | Месяц в календаре (0-11) |
| `_calTargetId` | string | ID целевого input для даты |
| `_MN` | array | Русские названия месяцев |
| `_dragCard` | Element | Перетаскиваемая карточка |
| `_dragSourceCol` | string | ID исходной колонки |
| `_currentTaskId` | string | ID открытой задачи в модалке |
| `_currentTab` | string | Активная вкладка модалки |

## Привязка к HTML

Функции экспортируются в `window` для вызова из `onclick`:
```javascript
window.calOpen = calOpen;
window.calPick = calPick;
// ... и т.д.
```
