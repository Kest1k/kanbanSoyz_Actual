Ты работаешь в репозитории Soyuz-PLM Kanban на Windows. Платформа фронта: WebBrowser-контрол Trident (IE11 в режиме edge через X-UA-Compatible).

ЖЁСТКИЕ ПРАВИЛА КОДА:
- IE11-совместимый CSS: для flex использовать display: -ms-flexbox; display: flex; и -ms-flex-* префиксы. Никаких CSS-переменных var(--x). gap в IE11 НЕ работает в flex-контейнере (только в grid) — значит вместо gap использовать margin на дочерних элементах ИЛИ принять что в IE11 промежутки будут чуть иначе.
- В нашем случае проверим: IE11 в режиме edge через X-UA-Compatible поддерживает gap частично. ЕСЛИ при тесте увидишь что промежутки не работают — заменить gap на margin-right у дочерних элементов.
- Никаких новых зависимостей, никакого Bootstrap-апгрейда.

ФАЙЛЫ ПРОЕКТА (рабочий каталог C:\MYPROJECTS\kanbanSoyz_Actual):
- scripts/KanbanBoard_HTML.html
- scripts/kanban.css
- scripts/kanban.js  ← одна строка, см. ниже

ЗАДАНИЕ: Прочитай файл `KANBAN_IMPROVEMENTS_PHASE3(08.05)/02_header_responsive/prompt.md` целиком. Цель: убрать наезд кнопок шапки при сжатии окна, обернув всё в flex-row с flex-wrap.

ВАЖНО: эту фичу делаем ПОСЛЕДНЕЙ — потому что фича 04 (tasks_issued_by_me) уже добавила в шапку кнопку kb-btn-mycreated. Она ДОЛЖНА попасть в новую flex-структуру наравне с kb-btn-my / kb-btn-all.

ПРОЦЕСС:
1. Прочитай prompt.md полностью.
2. Прочитай navigationTitle блок в KanbanBoard_HTML.html (~8-125).
3. Прочитай блок .kb-period-wrap в kanban.css (~629-768).
4. Прочитай в kanban.js строку где panel.style.display = "block" (~1025) — её нужно поменять на "" чтобы не перебивала flex.
5. ПРОВЕРЬ: есть ли в текущем DOM кнопка kb-btn-mycreated (от фичи 04). Если есть — добавь ей класс kb-nav-btn в новой структуре, рядом с kb-btn-my / kb-btn-all.
6. Делай атомарные коммиты в порядке таблицы «Атомарные коммиты».
7. После всех коммитов: `git log --oneline -5` + `git diff master..HEAD --stat`.

КРИТИЧЕСКИЕ ТОЧКИ:
- Содержимое <div id="kb-period-panel">…</div> (быстрый выбор / свой диапазон / кнопки Применить-Сбросить) — НЕ менять. Скопировать как есть.
- Все inline-стили на kb-btn-my / kb-btn-all / kb-btn-mycreated / kb-hier-label / на правых кнопках — удалить, заменить классами kb-nav-btn / kb-nav-btn-primary / kb-nav-btn-help / kb-nav-btn-news.
- Атрибут style="display:none;" (для скрытия кнопок по роли через JS) — СОХРАНИТЬ, JS им управляет.
- На селектах kb-sel-* оставить только display:none + width:auto + min-width — остальные inline-стили (vertical-align, margin-*) удалить.
- В kanban.js: panel.style.display = "block" → panel.style.display = "" (чтобы CSS-класс kb-nav-cluster с display:flex не перебивался).
- Position: relative на .kb-period-wrap должен сохраниться, иначе dropdown улетит — проверь после правки.

ОГРАНИЧЕНИЯ:
- НЕ делай git push.
- НЕ запускай smoke-тесты.
- НЕ трогай tcmOverlay / модалку задачи / .kb-board / колонки.
- НЕ переписывай функционал кнопок — только обёртка/стили.
- НЕ удаляй id-шники элементов — JS на них завязан.

Начинай с режима Plan. Жди подтверждения.
