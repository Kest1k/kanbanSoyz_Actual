# 03 — Сохранять отображение фамилии в селекте «Все сотрудники»

> **Затронутые файлы:**
> - `scripts/kanban.js`

## Что делаем

Сейчас в верхней панели руководителя есть селект `id="kb-sel-user"` со списком сотрудников. По умолчанию первая опция — «Все сотрудники» (`value=""`). При выборе конкретного сотрудника визуально на короткое время показывается его фамилия, **но при следующем рендере доски (после `RefreshBoard`)** селект сбрасывается обратно на «Все сотрудники», хотя сервер запомнил режим `user:KEY` в PropertyBag.

Цель: после клика по сотруднику и автоматического `RefreshBoard` — селект **остался показывать фамилию выбранного сотрудника**.

## Часть A — Корневая причина

В `kanban.js`:

1. `kbInitHierarchy()` (строка ~1015) при инициализации делает:
   - `kbFillUsers(null, null)` (строка 1034) — заполняет список БЕЗ контекстного фильтра.
   - Затем `kbRestoreViewMode(_kbH.viewMode || "my")` (строка 1044).

2. В `kbRestoreViewMode("user:KEY")` (строки ~1198–1221):
   ```javascript
   if (mode.indexOf("user:") === 0) {
       var userKey = mode.substring(5);
       // Найти контекст пользователя ...
       var userCtx = "";
       for (var i = 0; i < _kbH.users.length; i++) {
           if (_kbH.users[i].key === userKey) { userCtx = _kbH.users[i].context || ""; break; }
       }
       if (userCtx) {
           var divCtx = kbCtxDiv(userCtx);
           if (_kbH.role === "admin") {
               kbSetSelVal("kb-sel-dept", divCtx || userCtx);
               kbFillSectors(divCtx || userCtx);
           }
           if (_kbH.role === "admin" || _kbH.role === "headOfDept") {
               kbSetSelVal("kb-sel-sector", userCtx);
           }
           kbFillUsers(divCtx || null, userCtx || null);
       }
       var selUser = document.getElementById("kb-sel-user");
       if (selUser) selUser.value = userKey;
       kbUpdateMyBtn(false);
       return;
   }
   ```

**Bug**: после `kbFillUsers(divCtx, userCtx)` без `searchQuery` (третий аргумент `undefined`) функция выполняется как `q = ""` и **первой добавляет** опцию `kbOpt("", "Все сотрудники")` (строка 1101). Затем `selUser.value = userKey` — пробует выставить значение. Если `userKey` существует в опциях — отрабатывает корректно. Но возможны два случая, в которых это **не работает**:

- **Кейс 1**: `userCtx` пустой (контекст пользователя не определён). Тогда внутрь `if (userCtx) { ... }` не зашли, и `kbFillUsers` остаётся с базовой инициализацией из строки 1034 — `kbFillUsers(null, null)`. Это НЕ воспроизводимо для нашего кейса (у руководителей контекст почти всегда определён).
- **Кейс 2**: Опция с нужным `value` отсутствует в списке (например, сотрудник из чужого подразделения). В этом случае `selUser.value = userKey` молча **не применяет** значение, и селект остаётся на первой опции «Все сотрудники».

Кроме того, **есть более тонкий случай**: в **headOfSector / leadEngineer** ветке `if (userCtx) { ... }` не выполняется ни одно из присваиваний (роль не admin/headOfDept). Тогда `kbFillUsers` НЕ перезаливается с правильным контекстом. Но базовый список из `kbFillUsers(null, null)` (строка 1034) для этих ролей всё равно содержит всех видимых пользователей сектора, поэтому `selUser.value = userKey` обычно работает.

## Часть B — Решение

Сделать проверку на успешность присваивания и при необходимости **добавить отсутствующую опцию динамически**, плюс убедиться, что атрибут `value` на `<option>` ровно равен `userKey` (а не URL-encoded или с лишними пробелами).

### B.1. Заменить функцию `kbRestoreViewMode` целиком

В `kanban.js` найти `function kbRestoreViewMode(mode)` (строка ~1194) и заменить её на следующую. Изменения помечены комментариями `// FIX-03`:

```javascript
function kbRestoreViewMode(mode) {
    if (!mode || mode === "my") { kbUpdateMyBtn(true); return; }
    if (mode === "all" || mode === "dept" || mode === "sector") { kbUpdateAllBtn(true); return; }

    if (mode.indexOf("user:") === 0) {
        var userKey = mode.substring(5);
        // Найти контекст пользователя чтобы правильно заполнить секторный/дивизионный список
        var userCtx = "", userName = "";
        for (var i = 0; i < _kbH.users.length; i++) {
            if (_kbH.users[i].key === userKey) {
                userCtx = _kbH.users[i].context || "";
                userName = _kbH.users[i].name || "";
                break;
            }
        }
        if (userCtx) {
            var divCtx = kbCtxDiv(userCtx);
            if (_kbH.role === "admin") {
                kbSetSelVal("kb-sel-dept", divCtx || userCtx);
                kbFillSectors(divCtx || userCtx);
            }
            if (_kbH.role === "admin" || _kbH.role === "headOfDept") {
                kbSetSelVal("kb-sel-sector", userCtx);
            }
            kbFillUsers(divCtx || null, userCtx || null);
        }
        var selUser = document.getElementById("kb-sel-user");
        if (selUser) {
            // FIX-03: гарантируем наличие нужной опции в списке.
            // Если по какой-то причине kbFillUsers не включил выбранного
            // пользователя (фильтр по контексту, поиску и т.п.) — добавляем
            // опцию вручную, чтобы select.value = userKey закрепил отображение.
            if (!kbHasOption(selUser, userKey)) {
                if (userName || userKey) selUser.appendChild(kbOpt(userKey, userName || userKey));
            }
            selUser.value = userKey;
            // FIX-03: если присваивание не сработало (option отсутствует
            // в DOM по какой-то причине), не оставляем селект в положении
            // «Все сотрудники» — это вводит в заблуждение. Добавляем
            // принудительно и повторяем.
            if (selUser.value !== userKey && (userName || userKey)) {
                selUser.appendChild(kbOpt(userKey, userName || userKey));
                selUser.value = userKey;
            }
        }
        kbUpdateMyBtn(false);
        return;
    }

    if (mode.indexOf("group:") === 0) {
        var grpKey = mode.substring(6);
        var isDivision = false;
        for (var j = 0; j < _kbH.divisions.length; j++) {
            if (_kbH.divisions[j].key === grpKey) { isDivision = true; break; }
        }
        if (isDivision) {
            kbSetSelVal("kb-sel-dept", grpKey);
            kbFillSectors(grpKey);
            kbFillUsers(grpKey, null);
        } else {
            var divForSec = kbCtxDiv(grpKey);
            kbSetSelVal("kb-sel-dept", divForSec || "");
            if (divForSec) kbFillSectors(divForSec);
            kbSetSelVal("kb-sel-sector", grpKey);
            kbFillUsers(divForSec || null, grpKey);
        }
        kbUpdateMyBtn(false);
    }
}
```

### B.2. Добавить хелпер `kbHasOption`

Сразу **после** `function kbOpt(value, text)` (строка ~1433):

```javascript
// FIX-03: проверка наличия option с конкретным value
function kbHasOption(sel, value) {
    if (!sel || !sel.options) return false;
    for (var i = 0; i < sel.options.length; i++) {
        if (sel.options[i].value === value) return true;
    }
    return false;
}
```

### B.3. Защита в `kbOnUserChange`

В `window.kbOnUserChange` (строка ~1140) — сейчас функция просто вызывает `kbApplyMode()`. Добавить **немедленный визуальный фиксаж**: после клика, до того как сервер пришлёт ответ и страница перерендерится, в локальном `_kbH.viewMode` запомнить выбранный режим, чтобы не было гонки. Не критично, но улучшает UX.

```javascript
window.kbOnUserChange = function () {
    // FIX-03: запоминаем локально, чтобы при возможном лагe RefreshBoard
    // селект уже не сбрасывался при следующем перерендере JS-инициализации
    var userKey = kbSelVal("kb-sel-user");
    if (userKey) _kbH.viewMode = "user:" + userKey;

    kbUpdateMyBtn(false);
    kbStyleBtn("kb-btn-all", false);
    kbApplyMode();
};
```

## Часть C — Атомарные коммиты

| # | Шаг | Файлы | Сообщение коммита |
|---|-----|-------|-------------------|
| 1 | JS: хелпер `kbHasOption` + защита в `kbRestoreViewMode` от пропавшей опции | `kanban.js` | `fix(kanban): keep employee selection visible after refresh` |
| 2 | JS: оптимистичное обновление `_kbH.viewMode` в `kbOnUserChange` | `kanban.js` | `fix(kanban): optimistic local viewMode update on user pick` |

## Часть D — Проверки и риски

1. **Опция, которой нет в `_kbH.users`**: после ручного добавления через `kbOpt(userKey, userKey || "")` отображаемый текст будет ключом, не фамилией. Это редкий кейс (контекст пользователя не нашёлся) и норма. **Лучше всегда брать `name`** — мы это и делаем (`userName` из цикла поиска).
2. **Пустой `userKey`**: если строка `mode.substring(5)` дала пустую строку — это странный `viewMode`, лучше не трогать. В коде: `if (userName || userKey) ...` — защищает от добавления пустой опции.
3. **Регрессия**: проверить группа-режим (`group:CTX`) — он не трогается.
4. **headOfSector/leadEngineer**: для этих ролей `if (userCtx)` блок не делает ничего полезного (роль не admin/headOfDept). Базовый `kbFillUsers(null, null)` из `kbInitHierarchy` уже содержит всех видимых юзеров сектора. Поэтому `selUser.value = userKey` обычно работает. Но добавленная защита FIX-03 страхует и эту ветку.
5. **`_kbH.viewMode` обновляется локально** в `kbOnUserChange`. На следующем `kbInitHierarchy` он будет переписан **серверной** правдой из `GetHierarchyInfo`. То есть это временная страховка, не дублирование state.

## Часть E — Критерии приёмки

- [ ] Зайти как руководитель (admin / headOfDept / headOfSector / leadEngineer).
- [ ] Выбрать в селекте «kb-sel-user» конкретного сотрудника (например, Иванов И.И.).
- [ ] Доска перерисовывается, отображает только задачи Иванова.
- [ ] Селект **продолжает показывать «Иванов И.И.»** (не сбрасывается на «Все сотрудники»).
- [ ] Нажать F5 / открыть доску заново — селект всё ещё показывает «Иванов И.И.».
- [ ] Нажать «Мои задачи» — селект сбрасывается на «Все сотрудники», доска показывает мои задачи.
- [ ] Снова выбрать Иванова — фамилия закрепляется.
- [ ] Поработать в `group:CTX` режиме (выбрать только сектор без сотрудника) — селекты не дублируются.
