# kanban.css — Стили канбан-доски

**Файл:** `kanban.css`
**Тип:** CSS (чистый, без препроцессоров)

## Назначение
Полная стилизация канбан-доски: layout, карточки, модалки, календарь, чат, адаптивность.

## Структура стилей

### 1. Общий layout

```
.kb-wrapper          — Корневой контейнер (flex column, 100vh)
.kb-toolbar          — Верхняя панель инструментов (flex row)
.kb-board            — Контейнер колонок (flex row, flex:1)
.kb-board.panel-open — Уменьшенная высота при открытой панели создания
```

### 2. Колонки

```
.kb-column           — Колонка (flex:1, flex column, min-width:220px)
.kb-col-header       — Заголовок колонки с названием и счётчиком
.kb-col-body         — Скроллируемая область карточек (overflow-y:auto)
```

**Цветовая кодировка заголовков:**
| Колонка | Цвет верхней полосы |
|---------|-------------------|
| Надо сделать (col-0) | `#6c757d` (серый) |
| В работе (col-1) | `#0d6efd` (синий) |
| Ожидание (col-2) | `#ffc107` (жёлтый) |
| Готово (col-3) | `#198754` (зелёный) |

### 3. Карточки задач

```
.kb-card             — Карточка (border-radius:8px, box-shadow, cursor:grab)
.kb-card:hover       — Подсветка при наведении
.kb-card.dragging    — Полупрозрачность при перетаскивании (opacity:0.5)
.kb-card-title       — Название задачи (font-weight:600)
.kb-card-meta        — Метаинформация (дата, исполнитель)
.kb-card-badges      — Контейнер бейджей (приоритет, вложения, комментарии)
```

**Бейджи приоритета:**
```
.kb-badge-low        — Серый (#6c757d)
.kb-badge-medium     — Синий (#0d6efd)
.kb-badge-high       — Оранжевый (#fd7e14)
.kb-badge-urgent     — Красный (#dc3545), пульсирующая анимация
```

**Индикатор просрочки:**
```
.kb-card.overdue     — Красная левая полоса (border-left:3px solid #dc3545)
.kb-overdue-badge    — Бейдж «Просрочено» (красный фон)
```

### 4. Аватары

```
.kb-avatar           — Круглый аватар с инициалами (32x32, border-radius:50%)
.kb-avatar-sm        — Маленький аватар (24x24) для комментариев
```

Цвет аватара генерируется из хеша имени пользователя (палитра из 8 цветов).

### 5. Панель создания задачи

```
.kb-create-panel     — Выдвижная панель (display:none по умолчанию)
.kb-create-form      — Форма внутри панели (grid layout)
.kb-input            — Стилизованные input/textarea
.kb-select           — Стилизованные select
.kb-btn              — Базовая кнопка
.kb-btn-primary      — Синяя кнопка (создать)
.kb-btn-secondary    — Серая кнопка (отмена)
```

### 6. Модальное окно карточки

```
.kb-modal-overlay    — Затемнение фона (rgba(0,0,0,0.5))
.kb-modal            — Модальное окно (max-width:700px, centered)
.kb-modal-header     — Заголовок с кнопкой закрытия
.kb-modal-body       — Тело модалки (flex column)
.kb-modal-tabs       — Вкладки (комментарии/вложения/история)
.kb-modal-tab        — Отдельная вкладка
.kb-modal-tab.active — Активная вкладка (подчёркивание)
.kb-modal-footer     — Кнопки действий (сохранить/удалить)
```

### 7. Календарь

```
.kb-calendar         — Контейнер календаря (position:absolute, z-index:1000)
.kb-cal-header       — Навигация (← месяц год →)
.kb-cal-grid         — Сетка дней (7 колонок)
.kb-cal-day          — Ячейка дня
.kb-cal-day:hover    — Подсветка при наведении
.kb-cal-day.today    — Текущий день (синяя обводка)
.kb-cal-day.selected — Выбранный день (синий фон)
.kb-cal-day.other    — Дни другого месяца (серый текст)
```

### 8. Комментарии (чат)

```
.kb-chat-list        — Список комментариев (overflow-y:auto, max-height:300px)
.kb-chat-item        — Один комментарий (flex row)
.kb-chat-bubble      — Текст комментария (фон, border-radius)
.kb-chat-author      — Имя автора (bold)
.kb-chat-time        — Время (мелкий серый текст)
.kb-chat-input       — Поле ввода нового комментария
.kb-chat-send        — Кнопка отправки
```

### 9. Вложения

```
.kb-attach-list      — Список вложений
.kb-attach-item      — Одно вложение (flex row, иконка + название)
.kb-attach-search    — Поле поиска объектов PLM
.kb-attach-results   — Результаты поиска (dropdown)
```

### 10. Фильтры и иерархия

```
.kb-filter-bar       — Панель фильтров (flex row)
.kb-mode-btn         — Кнопка режима (Мои/Все задачи)
.kb-mode-btn.active  — Активный режим
.kb-hierarchy        — Панель иерархии (дерево подразделений)
.kb-hier-item        — Элемент дерева
.kb-hier-item.active — Выбранный элемент
```

### 11. Отчёт

```
.kb-report-modal     — Модалка отчёта (шире основной — max-width:900px)
.kb-report-table     — Таблица отчёта
.kb-report-summary   — Блок сводки (карточки с числами)
```

### 12. Drag & Drop визуальные эффекты

```
.kb-col-body.drag-over  — Подсветка колонки при наведении (пунктирная рамка)
.kb-card.dragging       — Полупрозрачность перетаскиваемой карточки
```

### 13. Анимации

```
@keyframes kb-pulse     — Пульсация для urgent-бейджа
@keyframes kb-fadeIn    — Плавное появление модалки
@keyframes kb-slideDown — Выезд панели создания сверху
```

### 14. Адаптивность

```css
@media (max-width: 1200px) {
    .kb-column { min-width: 200px; }
}
@media (max-width: 768px) {
    .kb-board { flex-direction: column; }
    .kb-column { min-width: 100%; }
}
```

## Цветовая палитра

| Назначение | Цвет | HEX |
|-----------|------|-----|
| Primary (кнопки, ссылки) | Синий | `#0d6efd` |
| Success (Готово) | Зелёный | `#198754` |
| Warning (Ожидание) | Жёлтый | `#ffc107` |
| Danger (Просрочено, Urgent) | Красный | `#dc3545` |
| Secondary (Надо сделать) | Серый | `#6c757d` |
| High priority | Оранжевый | `#fd7e14` |
| Фон страницы | Светло-серый | `#f8f9fa` |
| Фон карточки | Белый | `#ffffff` |
| Текст основной | Тёмный | `#212529` |
| Текст вторичный | Серый | `#6c757d` |

## Типографика

- Шрифт: `system-ui, -apple-system, "Segoe UI", Roboto, sans-serif`
- Размер базовый: `14px`
- Заголовки колонок: `13px`, uppercase, letter-spacing
- Название карточки: `14px`, font-weight:600
- Мета-информация: `12px`
