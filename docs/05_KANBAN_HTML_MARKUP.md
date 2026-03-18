# KanbanBoard_HTML — Разметка канбан-доски

**Файл:** `KanbanBoard_HTML.html`
**Тип:** HTML-шаблон (Scriban-синтаксис для серверного рендеринга)

## Назначение
HTML-шаблон экрана канбан-доски. Использует Scriban-синтаксис (`{{ }}`) для серверной подстановки данных. Подключает `kanban.css` и `kanban.js`.

## Структура документа

```
<html>
├── <head>
│   ├── <link> kanban.css
│   └── <meta> viewport
├── <body>
│   └── .kb-wrapper
│       ├── .kb-toolbar          — Панель инструментов
│       ├── .kb-create-panel     — Панель создания (скрыта)
│       ├── .kb-board            — Доска с колонками
│       │   ├── .kb-column#col-0 — «Надо сделать»
│       │   ├── .kb-column#col-1 — «В работе»
│       │   ├── .kb-column#col-2 — «Ожидание»
│       │   └── .kb-column#col-3 — «Готово»
│       ├── .kb-modal-overlay    — Модалка карточки (скрыта)
│       ├── .kb-report-overlay   — Модалка отчёта (скрыта)
│       └── .kb-calendar         — Календарь (скрыт)
└── <script> kanban.js
```

## Секции разметки

### 1. Toolbar (`.kb-toolbar`)

```html
<div class="kb-toolbar">
  <!-- Кнопка «Создать задачу» -->
  <button onclick="toggleCreatePanel()" class="kb-btn kb-btn-primary">
    + Создать задачу
  </button>

  <!-- Переключатель режима (только для руководителей) -->
  {{ if is_manager }}
  <div class="kb-filter-bar">
    <button onclick="toggleMode('my')" class="kb-mode-btn active">Мои задачи</button>
    <button onclick="toggleMode('all')" class="kb-mode-btn">Все задачи</button>
  </div>
  {{ end }}

  <!-- Кнопка отчёта (только для руководителей) -->
  {{ if is_manager }}
  <button onclick="openReport()" class="kb-btn">📊 Отчёт</button>
  {{ end }}
</div>
```

### 2. Панель создания (`.kb-create-panel`)

```html
<div id="kb-create-panel" class="kb-create-panel" style="display:none">
  <div class="kb-create-form">
    <input id="kb-new-title" placeholder="Название задачи" class="kb-input">
    <textarea id="kb-new-details" placeholder="Описание" class="kb-input"></textarea>
    <select id="kb-new-priority" class="kb-select">
      <option value="medium">Обычный</option>
      <option value="low">Низкий</option>
      <option value="high">Высокий</option>
      <option value="urgent">Срочный</option>
    </select>
    <input id="kb-new-duedate" placeholder="Срок" readonly onclick="calOpen('kb-new-duedate')">

    <!-- Выбор исполнителя (только для руководителей) -->
    {{ if is_manager }}
    <select id="kb-new-assignee" class="kb-select">...</select>
    {{ end }}

    <button onclick="createTask()" class="kb-btn kb-btn-primary">Создать</button>
    <button onclick="toggleCreatePanel()" class="kb-btn kb-btn-secondary">Отмена</button>
  </div>
</div>
```

### 3. Доска (`.kb-board`)

Каждая колонка имеет одинаковую структуру:

```html
<div class="kb-column" id="col-0">
  <div class="kb-col-header">
    <span class="kb-col-title">Надо сделать</span>
    <span class="kb-col-count">({{ col_0 | array.size }})</span>
  </div>
  <div class="kb-col-body" ondragover="allowDrop(event)" ondrop="dropCard(event)">
    {{ for task in col_0 }}
      <!-- Карточка задачи (шаблон KanbanTask) -->
      {{ include 'KanbanTask' task }}
    {{ end }}
  </div>
</div>
```

### 4. Шаблон карточки (KanbanTask)

Карточка рендерится через include шаблона `KanbanTask`:

```html
<div class="kb-card {{ if task.overdue }}overdue{{ end }}"
     draggable="true"
     ondragstart="dragStart(event)"
     data-id="{{ task.id }}"
     onclick="openCard('{{ task.id }}')">

  <div class="kb-card-title">{{ task.title }}</div>
  <div class="kb-card-meta">
    <span class="kb-avatar" style="background:{{ task.avatarColor }}">
      {{ task.initials }}
    </span>
    <span>{{ task.assigneeName }}</span>
    <span>{{ task.dueDate }}</span>
  </div>
  <div class="kb-card-badges">
    <span class="kb-badge-{{ task.priority }}">{{ task.priorityName }}</span>
    {{ if task.attachmentCount > 0 }}
      <span>📎 {{ task.attachmentCount }}</span>
    {{ end }}
    {{ if task.commentCount > 0 }}
      <span>💬 {{ task.commentCount }}</span>
    {{ end }}
  </div>
</div>
```

### 5. Модальное окно карточки

```html
<div id="kb-modal-overlay" class="kb-modal-overlay" style="display:none">
  <div class="kb-modal">
    <div class="kb-modal-header">
      <h3 id="kb-modal-title"></h3>
      <button onclick="closeCard()">✕</button>
    </div>
    <div class="kb-modal-body">
      <!-- Поля редактирования -->
      <input id="kb-modal-title-input">
      <textarea id="kb-modal-details"></textarea>
      <select id="kb-modal-priority">...</select>
      <select id="kb-modal-status">...</select>
      <input id="kb-modal-duedate" readonly onclick="calOpen('kb-modal-duedate')">
      <div id="kb-modal-assignee"></div>
      <div id="kb-modal-created"></div>

      <!-- Вкладки -->
      <div class="kb-modal-tabs">
        <button onclick="switchTab('comments')" class="kb-modal-tab active">
          💬 Комментарии
        </button>
        <button onclick="switchTab('attachments')" class="kb-modal-tab">
          📎 Вложения
        </button>
        <button onclick="switchTab('history')" class="kb-modal-tab">
          📋 История
        </button>
      </div>

      <!-- Содержимое вкладок -->
      <div id="kb-tab-comments">...</div>
      <div id="kb-tab-attachments" style="display:none">...</div>
      <div id="kb-tab-history" style="display:none">...</div>
    </div>
    <div class="kb-modal-footer">
      <button onclick="saveCard()" class="kb-btn kb-btn-primary">Сохранить</button>
      <button onclick="deleteCard()" class="kb-btn kb-btn-danger">Удалить</button>
    </div>
  </div>
</div>
```

### 6. Скрытые данные

В конце body передаются серверные данные в JS:

```html
<script>
  var KB_DATA = {
    currentUserId: "{{ current_user_id }}",
    currentUserName: "{{ current_user_name }}",
    isManager: {{ is_manager }},
    userRole: "{{ user_role }}"
  };
</script>
<script src="kanban.js"></script>
```

## Scriban-переменные

| Переменная | Где используется | Описание |
|------------|-----------------|----------|
| `{{ col_0 }}` | Колонка 0 | Массив задач «Надо сделать» |
| `{{ col_1 }}` | Колонка 1 | Массив задач «В работе» |
| `{{ col_2 }}` | Колонка 2 | Массив задач «Ожидание» |
| `{{ col_3 }}` | Колонка 3 | Массив задач «Готово» |
| `{{ is_manager }}` | Toolbar, панель создания | Показ кнопок руководителя |
| `{{ current_user_id }}` | Script-блок | ID для JS |
| `{{ current_user_name }}` | Script-блок | Имя для JS |
| `{{ user_role }}` | Script-блок | Роль для JS |
| `{{ task.* }}` | Карточка | Поля задачи (см. серверный скрипт) |

## Ключевые ID элементов

| ID | Элемент | Назначение |
|----|---------|-----------|
| `kb-board` | div | Контейнер доски |
| `col-0..col-3` | div | Колонки |
| `kb-create-panel` | div | Панель создания |
| `kb-modal-overlay` | div | Оверлей модалки |
| `kb-new-title` | input | Название новой задачи |
| `kb-new-details` | textarea | Описание новой задачи |
| `kb-new-priority` | select | Приоритет новой задачи |
| `kb-new-duedate` | input | Срок новой задачи |
| `kb-new-assignee` | select | Исполнитель новой задачи |
| `kb-modal-title-input` | input | Название в модалке |
| `kb-modal-details` | textarea | Описание в модалке |
| `kb-modal-priority` | select | Приоритет в модалке |
| `kb-modal-status` | select | Статус в модалке |
| `kb-modal-duedate` | input | Срок в модалке |
| `kb-tab-comments` | div | Вкладка комментариев |
| `kb-tab-attachments` | div | Вкладка вложений |
| `kb-tab-history` | div | Вкладка истории |
| `kb-calendar` | div | Контейнер календаря |
