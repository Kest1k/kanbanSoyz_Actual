# Инструкция — короткая

## Один раз перед всем

Открой PowerShell в VS Code (Ctrl+`):

```powershell
cd C:\MYPROJECTS\kanbanSoyz_Actual
git status
```

Если есть мусор в `git status` — закоммить или удали. Должно быть чисто.

---

## Цикл по каждой фиче (5 раз)

### 1. Старт ветки

```powershell
git checkout master
git pull
git checkout -b phase3/<имя-фичи>
```

`<имя-фичи>` бери из заголовка очередного промта. Например `05-description-autoexpand`.

### 2. Чат с агентом

- В Cline: новый чат (иконка «+ New Task»).
- Открой промт по очереди:

| Очередь | Файл |
|---|---|
| 1 | `01_feature05_description_autoexpand.md` |
| 2 | `02_feature03_employee_select_keep.md` |
| 3 | `03_feature01_checklist_dnd_edit.md` |
| 4 | `04_feature04_tasks_issued_by_me.md` |
| 5 | `05_feature02_header_responsive.md` |

- Ctrl+A → Ctrl+C → Ctrl+V в Cline.
- Cline в режиме **Plan** опишет план. Прочти. Если ОК — переключи на **Act** и пиши «Действуй».
- Жди коммитов.

### 3. Проверь работу

```powershell
git log --oneline -10
```

Открой `<NN_папка_фичи>/smoke_test.md`. Прогони чек-лист в живом Soyuz клиенте.

**Если что-то не работает** — пиши в тот же чат Cline:
> Шаг X не работает: <что произошло>. Ожидалось: <что>. Фикси.

### 4. Мерж в master

После прохождения smoke-теста:

```powershell
.\merge.ps1
git push
```

Готово. Фича в master + на GitHub.

### 5. Следующая

Возврат к шагу 1 со следующим именем ветки.

---

## Если всё сломал — откат

```powershell
git checkout master
git branch -D phase3/<имя-сломанной>
```

master не пострадал. Берёшь следующий промт или повторяешь сломанный с нуля.

---

## Финал — после всех 5 фич

```powershell
git log --oneline -25
```

Должно быть 5 merge-коммитов + commit'ы фич. Всё уже на GitHub (если пушил после каждой).

---

## Шпаргалка команд

| Что | Команда |
|---|---|
| Где я / что изменилось | `git status` |
| История | `git log --oneline -10` |
| Новая ветка от свежего master | `git checkout master; git pull; git checkout -b phase3/X` |
| Мерж текущей ветки в master + удаление | `.\merge.ps1` |
| Отправка на GitHub | `git push` |
| Откат сломанной ветки | `git checkout master; git branch -D phase3/X` |

---

## Если merge.ps1 ругается «незакоммиченные изменения»

Значит в репо появились новые файлы (`??` в статусе). Решения:

```powershell
git status                       # посмотри что лишнее
git add <файлы>                  # добавь нужное
git commit -m "chore: extra"     # закоммить
# или удали ненужное:
Remove-Item <файл>
# потом снова:
.\merge.ps1
```
