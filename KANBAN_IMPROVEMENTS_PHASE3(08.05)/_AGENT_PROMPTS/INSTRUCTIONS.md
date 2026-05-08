# Инструкция — копи-пасти прямо в Cline

## Один раз перед стартом

Открой терминал в VS Code (Ctrl+`):

```
cd C:\MYPROJECTS\kanbanSoyz_Actual
git status
```

Если есть незакоммиченное — закоммить или стэшни. Должно быть `nothing to commit, working tree clean`.

## Цикл по каждой фиче (повторять 5 раз)

### Шаг 1. Новая ветка

В терминале:

```
git checkout master
git pull
git checkout -b phase3/<имя-из-файла>
```

Имя ветки бери из заголовка очередного файла промта (`01_feature05_...` → `phase3/05-description-autoexpand`).

### Шаг 2. Новый чат в Cline

Кликни иконку «+ New Task» в панели Cline (или Ctrl+Shift+P → `Cline: New Task`).

### Шаг 3. Скопируй промт

Открой файл по порядку:

1. `01_feature05_description_autoexpand.md` ← **первая, самая маленькая**
2. `02_feature03_employee_select_keep.md`
3. `03_feature01_checklist_dnd_edit.md`
4. `04_feature04_tasks_issued_by_me.md`
5. `05_feature02_header_responsive.md` ← **последняя**

Выдели всё (Ctrl+A) → копируй (Ctrl+C) → вставь в Cline (Ctrl+V).

### Шаг 4. Режим Plan → Act

В Cline снизу справа переключатель `Plan | Act`. Сначала **Plan** — Cline покажет что собирается делать, без правок файлов. Прочти. Если ОК — нажми кнопку «Approve» или просто переключи в **Act** и напиши «Действуй».

Если Plan странный — напиши «Стоп, прочитай ещё раз `prompt.md` фичи Х внимательно, ты пропустил пункт Y».

### Шаг 5. Жди коммитов

Cline сам коммитит атомарными порциями. Когда закончит — напишет «Готово» или подобное.

### Шаг 6. Проверь diff

В терминале:

```
git log --oneline -10
git diff master..HEAD --stat
```

Открой каждый изменённый файл — пробегись глазами. Особенно:
- Нет `const` / `let` / стрелочных функций в `kanban.js`.
- Нет `var()` в CSS.
- Существующие функции не переименованы.

Если что-то не так — исправь сам или попроси Cline.

### Шаг 7. Smoke-тест

Открой `<NN_папка_фичи>/smoke_test.md`. Пройди по чек-листу в живом Soyuz клиенте.

Если что-то не работает — новый ход в том же чате Cline:
```
Шаг X из smoke-теста не работает: <что произошло>. Ожидалось: <что должно>. Прочитай связанный код и предложи фикс.
```

### Шаг 8. Мерж в master

Если smoke-тест прошёл:

```
git checkout master
git merge --no-ff phase3/<имя>
git branch -d phase3/<имя>
```

### Шаг 9. Следующая фича

Возврат к Шагу 1.

## Если совсем сломал — откат

```
git checkout master
git branch -D phase3/<имя-сломанной-ветки>
```

Master не пострадал — продолжай со следующей фичи.

## Финал — после всех 5 фич

```
git log --oneline master --grep="kanban" -20
git push origin master
```

Опционально — закоммить документацию обновлений «Что нового» в KanbanBoard_HTML.html (если Cline не сделал).
