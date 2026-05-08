(function () {

        // ── Adjust board height when create panel toggles ────────────────────
        function boardAdjust() {
            var board = document.getElementById("kb-board");
            var panel = document.getElementById("kb-create-panel");
            if (!board || !panel) return;
            if (panel.style.display === "none" || panel.style.display === "") {
                board.className = board.className.replace(" panel-open", "");
            } else {
                if (board.className.indexOf("panel-open") === -1)
                    board.className += " panel-open";
            }
            // Пересчитать высоту доски после смены панели
            setTimeout(function() { if (typeof kbFitBoard === "function") kbFitBoard(); }, 50);
        }

        // ── Calendar (обобщённый: _calTargetId определяет целевой инпут) ────
        var _calYear = 0, _calMonth = 0;
        var _calTargetId = "kb-new-duedate"; // по умолчанию — панель создания
        var _MN = ["Январь", "Февраль", "Март", "Апрель", "Май", "Июнь",
            "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь"];
        var _DN = ["Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс"];

        // Попап-div календаря: kb-cal для create panel, kb-cal-tcm для модала
        var _calSkipClose = false;
        function calEl() {
            if (_calTargetId === "tcm-duedate") return document.getElementById("kb-cal-tcm");
            if (_calTargetId === "kb-period-from") return document.getElementById("kb-cal-kb-period-from");
            if (_calTargetId === "kb-period-to") return document.getElementById("kb-cal-kb-period-to");
            if (_calTargetId === "rpt-period-from") return document.getElementById("kb-cal-rpt-period-from");
            if (_calTargetId === "rpt-period-to") return document.getElementById("kb-cal-rpt-period-to");
            return document.getElementById("kb-cal");
        }

        window.calToggle = function (targetId) {
            _calTargetId = targetId || "kb-new-duedate";
            var cal = calEl();
            if (!cal) return;
            if (cal.style.display === "block") {
                cal.style.display = "none";
            } else {
                // Закрыть другой попап если открыт
                var other = document.getElementById(_calTargetId === "tcm-duedate" ? "kb-cal" : "kb-cal-tcm");
                if (other) other.style.display = "none";
                var inp = document.getElementById(_calTargetId);
                var val = inp ? inp.value : "";
                var now = new Date();
                _calYear = now.getFullYear();
                _calMonth = now.getMonth();
                if (/^\d{2}\.\d{2}\.\d{4}$/.test(val)) {
                    var p = val.split(".");
                    _calYear = parseInt(p[2], 10);
                    _calMonth = parseInt(p[1], 10) - 1;
                }
                calRender();
                cal.style.display = "block";
            }
        };

        window.calPrev = function () {
            _calSkipClose = true;
            _calMonth--;
            if (_calMonth < 0) { _calMonth = 11; _calYear--; }
            calRender();
        };

        window.calNext = function () {
            _calSkipClose = true;
            _calMonth++;
            if (_calMonth > 11) { _calMonth = 0; _calYear++; }
            calRender();
        };

        window.calPickDay = function (day) {
            var mm = (_calMonth + 1 < 10 ? "0" : "") + (_calMonth + 1);
            var dd = (day < 10 ? "0" : "") + day;
            var inp = document.getElementById(_calTargetId);
            if (inp) inp.value = dd + "." + mm + "." + _calYear;
            calEl().style.display = "none";
        };

        window.calClear = function () {
            var inp = document.getElementById(_calTargetId);
            if (inp) inp.value = "";
            calEl().style.display = "none";
        };

        function calRender() {
            var cal = calEl();
            if (!cal) return;
            var first = new Date(_calYear, _calMonth, 1);
            var last = new Date(_calYear, _calMonth + 1, 0);
            var dow = first.getDay();
            dow = (dow === 0) ? 6 : dow - 1;
            var td = new Date(), tD = td.getDate(), tM = td.getMonth(), tY = td.getFullYear();

            var h = "<div class='kb-cal-hdr'>";
            h += "<button class='kb-cal-hdr-btn' onclick='calPrev()'>&#8249;</button>";
            h += "<span class='kb-cal-title'>" + _MN[_calMonth] + " " + _calYear + "</span>";
            h += "<button class='kb-cal-hdr-btn' onclick='calNext()'>&#8250;</button>";
            h += "</div><table class='kb-cal-table'><tr>";
            for (var d = 0; d < 7; d++) h += "<th>" + _DN[d] + "</th>";
            h += "</tr><tr>";
            for (var e = 0; e < dow; e++) h += "<td class='empty'></td>";
            var col = dow;
            for (var day = 1; day <= last.getDate(); day++) {
                var cls = (day === tD && _calMonth === tM && _calYear === tY) ? " class='today'" : "";
                h += "<td" + cls + " onclick='calPickDay(" + day + ")'>" + day + "</td>";
                col++;
                if (col % 7 === 0 && day < last.getDate()) h += "</tr><tr>";
            }
            h += "</tr></table>";
            h += "<div style='text-align:right;margin-top:5px;border-top:1px solid #eee;padding-top:4px;'>";
            h += "<a href='#' onclick='calClear(); return false;' style='font-size:11px;color:#999;'>Очистить</a>";
            h += "</div>";
            cal.innerHTML = h;
        }

        document.onclick = function (e) {
            // calPrev/calNext перерисовывают innerHTML — кнопка исчезает из DOM
            // до того как сюда дойдёт событие, поэтому пропускаем один клик
            if (_calSkipClose) { _calSkipClose = false; return; }
            var tgt = e.target || e.srcElement;

            // Массив всех возможных календарей
            var cals = [
                document.getElementById("kb-cal"),
                document.getElementById("kb-cal-tcm"),
                document.getElementById("kb-cal-kb-period-from"),
                document.getElementById("kb-cal-kb-period-to"),
                document.getElementById("kb-cal-rpt-period-from"),
                document.getElementById("kb-cal-rpt-period-to")
            ];

            for (var ci = 0; ci < cals.length; ci++) {
                var cal = cals[ci];
                if (!cal || cal.style.display !== "block") continue;
                var node = tgt;
                var inside = false;
                while (node) {
                    if (node === cal) { inside = true; break; }
                    if (node.id === "kb-new-duedate" || node.id === "tcm-duedate" || node.id === "kb-period-from" || node.id === "kb-period-to" || node.id === "rpt-period-from" || node.id === "rpt-period-to") { inside = true; break; }
                    if (node.className && node.className.indexOf("kb-date-btn") !== -1) { inside = true; break; }
                    node = node.parentNode;
                }
                if (!inside) cal.style.display = "none";
            }

            // Закрытие dropdown-панели фильтра периода при клике снаружи
            var pPanel = document.getElementById("kb-period-panel");
            if (pPanel && pPanel.style.display === "block") {
                var pNode = tgt;
                var pInside = false;
                while (pNode) {
                    if (pNode.id === "kb-period-wrap" || pNode.id === "kb-period-panel" || pNode.id === "kb-period-toggle") {
                        pInside = true; break;
                    }
                    pNode = pNode.parentNode;
                }
                if (!pInside) pPanel.style.display = "none";
            }
        };

        // ── Kanban ───────────────────────────────────────────────────────────
        var _draggedId = null;

        window.kbDragStart = function (event, taskId) {
            _draggedId = taskId;
            event.dataTransfer.setData("text", taskId);
            event.dataTransfer.effectAllowed = "move";
        };

        window.kbAllowDrop = function (event) {
            event.preventDefault();
            event.stopPropagation();
            var col = kbFindCol(event.target);
            if (col) col.className = col.className.replace(" drag-over", "") + " drag-over";
            event.dataTransfer.dropEffect = "move";
        };

        window.kbDragLeave = function (event) {
            var col = kbFindCol(event.target);
            if (col) col.className = col.className.replace(" drag-over", "");
        };

        var STATUS_DONE = 3;
        window.kbDrop = function (event, newStatus) {
            event.preventDefault();
            event.stopPropagation();

            var col = kbFindCol(event.target);
            if (col) col.className = col.className.replace(" drag-over", "");

            var taskId = _draggedId || event.dataTransfer.getData("text");
            _draggedId = null;
            if (!taskId) return;

            // Определяем старый статус по текущей колонке карточки
            var card = document.getElementById("kbc_" + taskId);
            var oldStatus = -1;
            if (card && card.parentNode) {
                var parentId = card.parentNode.id; // "kb-body-X"
                oldStatus = parseInt(parentId.replace("kb-body-", ""), 10);
            }

            // Подтверждение при перемещении В «Готово»
            if (newStatus === STATUS_DONE && oldStatus !== STATUS_DONE) {
                if (!confirm("Вы уверены, что задача выполнена?\nОна будет перемещена в колонку \"Готово\" и получит дату завершения.")) {
                    return; // Отмена
                }
            }

            // Подтверждение при перемещении ИЗ «Готово»
            if (oldStatus === STATUS_DONE && newStatus !== STATUS_DONE) {
                if (!confirm("Вернуть выполненную задачу в работу?\nДата завершения будет очищена.")) {
                    return; // Отмена
                }
            }

            // Выполняем перемещение
            try {
                var result = window.external.InvokeTemplate("MoveTask", taskId + "|" + newStatus);
                if (result === "OK") {
                    // Всегда обновляем доску — сервер пересортирует по приоритету и дате
                    kbRefreshBoard();
                } else if (result && result.indexOf("ERROR") === 0) {
                    alert("Ошибка перемещения: " + result);
                }
            } catch (e) { alert("Ошибка: " + (e.message || e)); }
        };


        // ── Авто-обновление при возврате фокуса после редактирования задачи ─
        var _taskWasOpened = false;

        window.onfocus = function () {
            if (_taskWasOpened) {
                _taskWasOpened = false;
                kbRefreshBoard();
            }
        };

        window.kbOpenTask = function (taskId) {
            // Убираем бейдж «НОВАЯ» сразу при открытии (не ждём рефреш доски)
            var cardEl = document.getElementById("kbc_" + taskId);
            if (cardEl) {
                cardEl.className = cardEl.className.replace(/\bkb-card-new\b/g, "");
                var badge = cardEl.querySelector(".kb-new-badge");
                if (badge && badge.parentNode) badge.parentNode.removeChild(badge);
            }
            tcmOpen(taskId);
        };

        window.kbDeleteTask = function (taskId) {
            var card = document.getElementById("kbc_" + taskId);
            if (card && card.getAttribute("data-owner") !== "1") {
                alert("Эту задачу удалить нельзя.\nЗадача назначена вам другим пользователем — удалить её может только создатель.");
                return;
            }
            if (!confirm("Удалить задачу?")) return;
            try {
                var result = window.external.InvokeTemplate("DeleteTask", taskId);
                if (result === "OK") {
                    if (card && card.parentNode) { card.parentNode.removeChild(card); updateColCounts(); }
                } else if (result === "ERROR:NotOwner") {
                    alert("Эту задачу удалить нельзя.\nЗадача назначена вам другим пользователем — удалить её может только создатель.");
                } else if (result && result.indexOf("ERROR") === 0) {
                    alert("Ошибка удаления: " + result);
                }
            } catch (e) { alert("Ошибка: " + (e.message || e)); }
        };

        window.kbRefreshBoard = function () {
            try { window.external.InvokeTemplate("RefreshBoard", ""); }
            catch (e) { window.location.href = window.location.href; }
        };

        // Step11: подсветка select при выборе «Сверхсрочная»
        window.kbOnPriorityChange = function () {
            var sel = document.getElementById("kb-new-priority");
            if (!sel) return;
            if (sel.value === "urgent") {
                sel.className = (sel.className || "").replace(/\bkb-sel-urgent\b/g, "") + " kb-sel-urgent";
            } else {
                sel.className = (sel.className || "").replace(/\bkb-sel-urgent\b/g, "").replace(/\s+/g, " ");
            }
        };

        window.showCreateTask = function (status) {
            var panel = document.getElementById("kb-create-panel");
            panel.style.display = "block";
            boardAdjust();
            if (status !== undefined && status !== null)
                document.getElementById("kb-new-status").value = String(status);
            kbInitCreateForm();
            document.getElementById("kb-new-title").focus();
        };

        window.hideCreateTask = function () {
            document.getElementById("kb-create-panel").style.display = "none";
            boardAdjust();
            document.getElementById("kb-new-title").value = "";
            document.getElementById("kb-new-duedate").value = "";
            document.getElementById("kb-new-details").value = "";
            var newTagsEl = document.getElementById("kb-new-tags");
            if (newTagsEl) newTagsEl.value = "";
            document.getElementById("kb-new-status").value = "0";
            document.getElementById("kb-new-priority").value = "medium";
            var prSel = document.getElementById("kb-new-priority");
            if (prSel) prSel.className = (prSel.className || "").replace(/\bkb-sel-urgent\b/g, "");
            var crtUserEl = document.getElementById("kb-crt-user");
            if (crtUserEl) crtUserEl.value = "";
            var crtUserSearchEl = document.getElementById("kb-crt-user-search");
            if (crtUserSearchEl) crtUserSearchEl.value = "";
            // Сброс группового режима
            var modeOneEl = document.getElementById("kb-crt-mode-one");
            if (modeOneEl) modeOneEl.checked = true;
            var singleWrap = document.getElementById("kb-crt-single-wrap");
            if (singleWrap) singleWrap.style.display = "";
            var groupRow = document.getElementById("kb-crt-group-row");
            if (groupRow) groupRow.style.display = "none";
            _kbGrpSelected = {};
            var cal = calEl();
            if (cal) cal.style.display = "none";
            _crtPendingAttachments = [];
            kbCrtRenderAtts();
        };

        // Добавить тег-подсказку в поле ввода панели создания
        window.kbCrtAddTag = function (tag) {
            var inp = document.getElementById("kb-new-tags");
            if (!inp) return;
            tag = (tag || "").replace(/^\s+|\s+$/g, "");
            if (!tag) return;
            var cur = inp.value ? inp.value.replace(/^\s+|\s+$/g, "") : "";
            var parts = cur ? cur.split(",") : [];
            for (var j = 0; j < parts.length; j++) {
                if (parts[j].replace(/^\s+|\s+$/g, "") === tag) return; // дубликат
            }
            inp.value = cur ? cur + ", " + tag : tag;
        };

        window.doCreateTask = function () {
            var title = document.getElementById("kb-new-title").value;
            var status = document.getElementById("kb-new-status").value;
            var priority = document.getElementById("kb-new-priority").value;
            var dueDate = document.getElementById("kb-new-duedate").value;
            var details = document.getElementById("kb-new-details").value;
            var tags = document.getElementById("kb-new-tags") ? document.getElementById("kb-new-tags").value : "";

            title = title ? title.replace(/^\s+|\s+$/g, "") : "";
            dueDate = dueDate ? dueDate.replace(/^\s+|\s+$/g, "") : "";
            details = details ? details.replace(/^\s+|\s+$/g, "") : "";
            tags = tags ? tags.replace(/^\s+|\s+$/g, "").replace(/\|/g, " ") : "";

            if (!title) { alert("Введите название задачи"); return; }
            if (dueDate && !/^\d{2}\.\d{2}\.\d{4}$/.test(dueDate)) {
                alert("Срок укажите в формате ДД.ММ.ГГГГ"); return;
            }
            details = details.replace(/\|/g, " ");

            // ── Групповой режим: CreateGroupTask ──────────────────────────
            var modeGrpEl = document.getElementById("kb-crt-mode-group");
            if (modeGrpEl && modeGrpEl.checked) {
                var keys = [];
                for (var k in _kbGrpSelected) {
                    if (_kbGrpSelected.hasOwnProperty(k)) keys.push(k);
                }
                if (keys.length === 0) { alert("Выберите хотя бы одного получателя"); return; }
                var grpParam = title + "|" + status + "|" + priority + "|" + dueDate + "|" + details + "|" + tags + "|" + keys.join(",");
                try {
                    var grpRes = window.external.InvokeTemplate("CreateGroupTask", grpParam);
                    if (grpRes && grpRes.indexOf("ERROR") === 0) {
                        alert("Ошибка создания задач: " + grpRes);
                    } else {
                        hideCreateTask();
                        kbRefreshBoard();
                    }
                } catch (e) { alert("Ошибка: " + (e.message || e)); }
                return;
            }

            // ── Одиночный режим: CreateTask ───────────────────────────────
            var selfEl = document.getElementById("kb-crt-self");
            var isSelf = selfEl ? selfEl.checked : true;
            var crtUserEl = document.getElementById("kb-crt-user");
            var assignee = (!isSelf && crtUserEl) ? (crtUserEl.value || "") : "";

            var param = title + "|" + status + "|" + priority + "|" + dueDate + "|" + details + "|" + tags + "|" + assignee;
            try {
                var result = window.external.InvokeTemplate("CreateTask", param);
                if (result && result.indexOf("ERROR") === 0) {
                    alert("Ошибка создания задачи: " + result);
                } else {
                    var newTaskKey = String(result || "");
                    if (newTaskKey && _crtPendingAttachments.length > 0) {
                        for (var ai = 0; ai < _crtPendingAttachments.length; ai++) {
                            var att = _crtPendingAttachments[ai];
                            var cmd = (att.type === "container") ? "AddContainer" : "AddAttachment";
                            try { window.external.InvokeTemplate(cmd, newTaskKey + "|" + att.key); } catch (ae) { }
                        }
                    }
                    hideCreateTask();
                    kbRefreshBoard();
                }
            } catch (e) { alert("Ошибка: " + (e.message || e)); }
        };

        function kbFindCol(el) {
            while (el && el.className) {
                if (el.className.indexOf("kb-col") !== -1) return el;
                el = el.parentNode;
            }
            return null;
        }

        function updateColCounts() {
            for (var i = 0; i < 4; i++) {
                var body = document.getElementById("kb-body-" + i);
                var col = document.getElementById("kb-col-" + i);
                if (!body || !col) continue;
                var cards = body.getElementsByClassName("kb-card");
                var cnt = col.getElementsByClassName("kb-cnt")[0];
                if (cnt) cnt.innerHTML = cards.length;
            }
        }

        document.getElementById("kb-new-title").onkeydown = function (e) {
            if ((e.keyCode || e.which) === 13) { doCreateTask(); }
            if ((e.keyCode || e.which) === 27) { hideCreateTask(); }
        };

        // ── Фильтр по периоду ─────────────────────────────────────────
        var _kbPeriod = { from: "", to: "" };
        var _kbPeriodDebounce = 0;

        // Хелпер: "dd.MM.yyyy" → timestamp (для локального сравнения порядка)
        function kbParseDmyToTs(s) {
            if (!s) return 0;
            var m = String(s).match(/^(\d{2})\.(\d{2})\.(\d{4})$/);
            if (!m) return 0;
            var d = new Date(parseInt(m[3], 10), parseInt(m[2], 10) - 1, parseInt(m[1], 10));
            return d.getTime();
        }

        // Форматирование даты "dd.MM.yyyy"
        function kbFmtDmy(d) {
            var dd = (d.getDate() < 10 ? "0" : "") + d.getDate();
            var mm = (d.getMonth() + 1 < 10 ? "0" : "") + (d.getMonth() + 1);
            return dd + "." + mm + "." + d.getFullYear();
        }

        // Обновляет лейбл кнопки-toggle и подсветку активного фильтра
        function kbPeriodUpdateLabel(sFrom, sTo) {
            var btn = document.getElementById("kb-period-toggle");
            var lbl = document.getElementById("kb-period-toggle-label");
            if (!btn || !lbl) return;
            if (!sFrom && !sTo) {
                lbl.innerHTML = "Период";
                btn.className = (btn.className || "").replace(/\s*kb-period-active\s*/g, " ");
                return;
            }
            var txt = "";
            if (sFrom && sTo)      txt = sFrom + " — " + sTo;
            else if (sFrom)        txt = "с " + sFrom;
            else                   txt = "по " + sTo;
            lbl.innerHTML = txt;
            if ((btn.className || "").indexOf("kb-period-active") < 0) {
                btn.className = (btn.className || "") + " kb-period-active";
            }
        }

        window.kbPeriodToggle = function () {
            var p = document.getElementById("kb-period-panel");
            if (!p) return;
            if (p.style.display === "block") {
                p.style.display = "none";
            } else {
                p.style.display = "block";
                // Спрятать открытые попапы календарей при открытии панели
                var c1 = document.getElementById("kb-cal-kb-period-from");
                var c2 = document.getElementById("kb-cal-kb-period-to");
                if (c1) c1.style.display = "none";
                if (c2) c2.style.display = "none";
            }
            return false;
        };

        // Применяет один из пресетов и сразу шлёт на сервер
        window.kbPeriodPreset = function (preset) {
            var to = new Date();
            var from = new Date();
            from.setHours(0, 0, 0, 0);
            to.setHours(0, 0, 0, 0);

            if (preset === "week") {
                // Понедельник текущей недели
                var dow = from.getDay();
                var diff = (dow === 0 ? 6 : dow - 1);
                from.setDate(from.getDate() - diff);
            } else if (preset === "month") {
                from.setDate(1);
            } else if (preset === "3mo") {
                from.setMonth(from.getMonth() - 3);
            } else if (preset === "6mo") {
                from.setMonth(from.getMonth() - 6);
            } else if (preset === "year") {
                from.setFullYear(from.getFullYear() - 1);
            } else {
                return;
            }

            var sFrom = kbFmtDmy(from);
            var sTo   = kbFmtDmy(to);

            var inFrom = document.getElementById("kb-period-from");
            var inTo   = document.getElementById("kb-period-to");
            if (inFrom) inFrom.value = sFrom;
            if (inTo)   inTo.value   = sTo;

            kbPeriodApply();
        };

        // Восстановление состояния фильтра периода после Refresh.
        // ВАЖНО: вызывается отдельно от kbInitHierarchy, потому что
        // kbInitHierarchy делает ранний return для роли "regular" и
        // период не успеет инициализироваться. Кнопки Применить/Сбросить
        // используют inline onclick — биндинг здесь не нужен.
        window.kbPeriodFilterInit = function () {
            var inFrom = document.getElementById("kb-period-from");
            var inTo   = document.getElementById("kb-period-to");
            if (!inFrom || !inTo) return;

            // Восстановление значения из data-атрибутов, заполненных Liquid
            var dataFrom = inFrom.getAttribute("data-init") || "";
            var dataTo   = inTo.getAttribute("data-init")   || "";
            if (dataFrom) inFrom.value = dataFrom;
            if (dataTo)   inTo.value   = dataTo;
            _kbPeriod.from = dataFrom;
            _kbPeriod.to   = dataTo;
            kbPeriodUpdateLabel(dataFrom, dataTo);
        };

        window.kbPeriodApply = function () {
            var inFrom = document.getElementById("kb-period-from");
            var inTo   = document.getElementById("kb-period-to");
            if (!inFrom || !inTo) return;

            var sFrom = inFrom.value || "";    // "dd.MM.yyyy" или ""
            var sTo   = inTo.value   || "";    // "dd.MM.yyyy" или ""

            // Валидация порядка: парсим dd.MM.yyyy локально для сравнения
            if (sFrom && sTo) {
                var pf = kbParseDmyToTs(sFrom);
                var pt = kbParseDmyToTs(sTo);
                if (pf > 0 && pt > 0 && pf > pt) {
                    alert("Дата «С» должна быть раньше даты «По»");
                    return;
                }
            }

            var safeFrom = String(sFrom).replace(/\|/g, "");
            var safeTo   = String(sTo).replace(/\|/g, "");

            try {
                // SetPeriodFilter на сервере сам триггерит Refresh — отдельный
                // kbRefreshBoard НЕ нужен. Это устраняет гонку PropertyBag.
                var res = window.external.InvokeTemplate("SetPeriodFilter", safeFrom + "|" + safeTo);
                if (String(res || "").indexOf("ERROR") === 0) {
                    alert("Ошибка установки фильтра: " + res);
                    return;
                }
                _kbPeriod.from = safeFrom;
                _kbPeriod.to   = safeTo;
                kbPeriodUpdateLabel(safeFrom, safeTo);
                // Закрываем панель после применения
                var p = document.getElementById("kb-period-panel");
                if (p) p.style.display = "none";
            } catch (e) { /* no-op */ }
        };

        window.kbPeriodReset = function () {
            var inFrom = document.getElementById("kb-period-from");
            var inTo   = document.getElementById("kb-period-to");
            if (inFrom) inFrom.value = "";
            if (inTo)   inTo.value   = "";
            _kbPeriod.from = "";
            _kbPeriod.to   = "";
            try {
                window.external.InvokeTemplate("SetPeriodFilter", "|");
                kbPeriodUpdateLabel("", "");
                var p = document.getElementById("kb-period-panel");
                if (p) p.style.display = "none";
            } catch (e) { /* no-op */ }
        };

    }());

    // ═════════════════════════════════════════════════════════════
    // Step 10: Чат / Комментарии — JS-функции
    // ═════════════════════════════════════════════════════════════

    function tcmLoadComments(d) {
        var list = document.getElementById("tcm-chat-list");
        var cnt = document.getElementById("tcm-chat-count");
        if (!list) return;

        list.innerHTML = "";

        var nameKey = (d && d.nameKey) ? d.nameKey : "";
        if (!nameKey) { if (cnt) cnt.innerHTML = ""; return; }

        try {
            var res = window.external.InvokeTemplate("GetComments", nameKey);
            var items = JSON.parse(String(res || "[]"));

            if (cnt) cnt.innerHTML = items.length > 0 ? "(" + items.length + ")" : "";
            var tabBadge = document.getElementById("tcm-tab-chat-badge");
            if (tabBadge) {
                if (items.length > 0) {
                    tabBadge.innerHTML = items.length;
                    tabBadge.style.display = 'inline-block';
                } else {
                    tabBadge.style.display = 'none';
                }
            }

            if (items.length === 0) {
                list.innerHTML = '<div class="tcm-chat-empty">Нет комментариев</div>';
                return;
            }

            for (var i = 0; i < items.length; i++) {
                list.appendChild(tcmRenderComment(items[i]));
            }

            list.scrollTop = list.scrollHeight;
        } catch (e) {
            list.innerHTML = '<div class="tcm-chat-empty">Ошибка загрузки комментариев</div>';
            if (cnt) cnt.innerHTML = "";
        }
    }

    function tcmFormatCommentText(s) {
        if (!s) return "";
        return tcmChatEsc(s).replace(/\n/g, "<br>");
    }

    function tcmRenderComment(c) {
        var div = document.createElement("div");
        div.className = "tcm-msg-item" + (c.isMine ? " tcm-msg-mine" : "");
        div.setAttribute("data-idx", c.index);

        var actionBtns = "";
        if (c.isMine) {
            actionBtns =
                '<button class="tcm-msg-edit-btn" onclick="tcmEditComment(' + c.index + '); return false;" title="Редактировать">&#9998;</button>' +
                '<button class="tcm-msg-del-btn" onclick="tcmDeleteComment(' + c.index + '); return false;" title="Удалить">&times;</button>';
        }

        var editedMark = c.editedAt ? ' <span class="tcm-msg-edited">(изм.)</span>' : '';

        div.innerHTML =
            '<div class="tcm-msg-header">' +
            '<span class="tcm-msg-avatar" title="' + tcmChatEsc(c.authorName) + '">' + tcmChatEsc(c.initials || "") + '</span>' +
            '<span class="tcm-msg-author">' + tcmChatEsc(c.authorName) + '</span>' +
            '<span class="tcm-msg-time">' + tcmChatEsc(c.date) + '</span>' +
            editedMark +
            '</div>' +
            '<div class="tcm-msg-text" data-raw="' + tcmChatEsc(c.text) + '">' + tcmFormatCommentText(c.text) + '</div>' +
            actionBtns;

        return div;
    }

    window.tcmSendComment = function () {
        if (!_tcmData || !_tcmData.nameKey) return;

        var textarea = document.getElementById("tcm-chat-text");
        if (!textarea) return;

        var text = textarea.value.replace(/^\s+|\s+$/g, "");
        if (!text) { alert("Введите текст комментария"); return; }
        if (text.length > 2000) { alert("Комментарий не должен превышать 2000 символов"); return; }


        try {
            var res = window.external.InvokeTemplate("AddComment", _tcmData.nameKey + "|" + text);
            var s = String(res);

            if (s.indexOf("ERROR") === 0) {
                if (s === "ERROR:LimitReached") {
                    alert("Достигнут лимит комментариев (200)");
                } else {
                    alert("Ошибка: " + s);
                }
                return;
            }

            var newComment = JSON.parse(s);
            var list = document.getElementById("tcm-chat-list");
            if (list) {
                var empty = list.querySelector(".tcm-chat-empty");
                if (empty) list.removeChild(empty);

                list.appendChild(tcmRenderComment(newComment));
                list.scrollTop = list.scrollHeight;
            }

            var cnt = document.getElementById("tcm-chat-count");
            if (cnt) {
                var cur = parseInt(cnt.innerHTML.replace(/[^0-9]/g, ""), 10) || 0;
                cnt.innerHTML = "(" + (cur + 1) + ")";
            }

            tcmSyncCardCommentBadge();
            textarea.value = "";
        } catch (e) {
            alert("Ошибка: " + (e.message || e));
        }
    };

    window.tcmDeleteComment = function (index) {
        if (!_tcmData || !_tcmData.nameKey) return;
        if (!confirm("Удалить комментарий?")) return;

        try {
            var res = window.external.InvokeTemplate("DeleteComment", _tcmData.nameKey + "|" + index);
            var s = String(res);
            if (s === "OK") {
                tcmLoadComments(_tcmData);
                tcmSyncCardCommentBadge();
            } else if (s === "ERROR:NotOwner") {
                alert("Вы можете удалять только свои комментарии");
            } else {
                alert("Ошибка: " + s);
            }
        } catch (e) {
            alert("Ошибка: " + (e.message || e));
        }
    };

    window.tcmEditComment = function (index) {
        if (!_tcmData || !_tcmData.nameKey) return;

        var itemDiv = document.querySelector('[data-idx="' + index + '"]');
        if (!itemDiv) return;

        var textDiv = itemDiv.querySelector('.tcm-msg-text');
        if (!textDiv || itemDiv.querySelector('.tcm-msg-edit-area')) return;

        var rawText = textDiv.getAttribute('data-raw') || "";
        textDiv.style.display = 'none';

        var editArea = document.createElement('div');
        editArea.className = 'tcm-msg-edit-area';
        editArea.innerHTML =
            '<textarea class="tcm-edit-textarea" id="tcm-edit-txt-' + index + '" maxlength="2000">' +
            tcmChatEsc(rawText) +
            '</textarea>' +
            '<div class="tcm-edit-actions">' +
            '<button class="tcm-edit-save" onclick="tcmSaveEdit(' + index + '); return false;">Сохранить</button>' +
            '<button class="tcm-edit-cancel" onclick="tcmCancelEdit(' + index + '); return false;">Отмена</button>' +
            '</div>';

        itemDiv.insertBefore(editArea, textDiv.nextSibling);

        var ta = document.getElementById('tcm-edit-txt-' + index);
        if (ta) { ta.focus(); ta.select(); }
    };

    window.tcmSaveEdit = function (index) {
        if (!_tcmData || !_tcmData.nameKey) return;

        var ta = document.getElementById('tcm-edit-txt-' + index);
        if (!ta) return;

        var newText = ta.value.replace(/\r\n/g, "\n").replace(/^\s+|\s+$/g, "");
        if (!newText) { alert("Комментарий не может быть пустым"); return; }
        if (newText.length > 2000) { alert("Комментарий не должен превышать 2000 символов"); return; }

        try {
            var res = window.external.InvokeTemplate("EditComment", _tcmData.nameKey + "|" + index + "|" + newText);
            var s = String(res);

            if (s === "ERROR:NotOwner") { alert("Можно редактировать только свои комментарии"); tcmCancelEdit(index); return; }
            if (s === "ERROR:EmptyText") { alert("Комментарий не может быть пустым"); return; }
            if (s.indexOf("ERROR") === 0) { alert("Ошибка: " + s); return; }

            var updated = JSON.parse(s);
            var itemDiv = document.querySelector('[data-idx="' + index + '"]');
            if (itemDiv) {
                var textDiv = itemDiv.querySelector('.tcm-msg-text');
                if (textDiv) {
                    textDiv.setAttribute('data-raw', tcmChatEsc(updated.text));
                    textDiv.innerHTML = tcmFormatCommentText(updated.text);
                    textDiv.style.display = '';
                }

                var header = itemDiv.querySelector('.tcm-msg-header');
                if (header) {
                    var oldMark = header.querySelector('.tcm-msg-edited');
                    if (oldMark) header.removeChild(oldMark);
                    if (updated.editedAt) {
                        var span = document.createElement('span');
                        span.className = 'tcm-msg-edited';
                        span.innerHTML = '(изм.)';
                        header.appendChild(span);
                    }
                }

                var editArea = itemDiv.querySelector('.tcm-msg-edit-area');
                if (editArea) itemDiv.removeChild(editArea);
            }
        } catch (e) {
            alert("Ошибка: " + (e.message || e));
        }
    };

    window.tcmCancelEdit = function (index) {
        var itemDiv = document.querySelector('[data-idx="' + index + '"]');
        if (!itemDiv) return;

        var textDiv = itemDiv.querySelector('.tcm-msg-text');
        if (textDiv) textDiv.style.display = '';

        var editArea = itemDiv.querySelector('.tcm-msg-edit-area');
        if (editArea) itemDiv.removeChild(editArea);
    };

    function tcmSyncCardCommentBadge() {
        if (!_tcmData || !_tcmData.nameKey) return;
        try {
            var res = window.external.InvokeTemplate("GetCardCommentCount", _tcmData.nameKey);
            var count = parseInt(String(res), 10) || 0;
            var card = document.getElementById("kbc_" + _tcmData.nameKey);
            if (!card) return;
            var meta = card.querySelector(".kb-card-meta");
            if (!meta) return;
            var badge = meta.querySelector(".kb-comment-badge");
            if (count > 0) {
                if (!badge) {
                    badge = document.createElement("span");
                    badge.className = "kb-comment-badge";
                    var attachBadge = meta.querySelector(".kb-attach-badge");
                    if (attachBadge) {
                        meta.insertBefore(badge, attachBadge);
                    } else {
                        meta.appendChild(badge);
                    }
                }
                badge.title = "Комментарии: " + count;
                badge.innerHTML = '<i class="fa fa-comment-o"></i> ' + count;
            } else {
                if (badge) badge.parentNode.removeChild(badge);
            }
        } catch (e) {}
    }

    window.tcmChatKeydown = function (e) {
        if ((e.keyCode === 13 || e.which === 13) && e.ctrlKey) {
            e.preventDefault();
            tcmSendComment();
        }
    };

    function tcmChatEsc(s) {
        if (!s) return "";
        return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;")
            .replace(/>/g, "&gt;").replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    // ── Шаг 11: Подзадачи / чек-лист ────────────────────────────────────
    var _tcmSubtasks = { taskKey: "", items: [] };

    window.tcmSubtasksLoad = function (nameKey) {
        _tcmSubtasks.taskKey = nameKey || "";
        _tcmSubtasks.items   = [];
        var safeKey = String(nameKey || "").replace(/\|/g, "");
        try {
            var raw = window.external.InvokeTemplate("GetSubtasks", safeKey);
            _tcmSubtasks.items = JSON.parse(String(raw || "[]"));
            if (!_tcmSubtasks.items || !_tcmSubtasks.items.length) _tcmSubtasks.items = [];
        } catch (e) { _tcmSubtasks.items = []; }
        tcmSubtasksRender();
    };

    window.tcmSubtasksRender = function () {
        var list = document.getElementById("tcm-subt-list");
        if (!list) return;
        var items = _tcmSubtasks.items || [];
        var html = "", i, it, doneClass, doneTitle, editedNote;
        for (i = 0; i < items.length; i++) {
            it = items[i];
            doneClass = (it.done === "1") ? " kb-subt-done" : "";
            doneTitle = (it.done === "1" && it.doneBy)
                ? ("Выполнено: " + it.doneBy + " (" + (it.doneAt || "") + ")")
                : "";
            editedNote = (it.editedBy)
                ? (" (изм. " + it.editedBy + (it.editedAt ? ", " + it.editedAt : "") + ")")
                : "";

            html += '<div class="kb-subt-item' + doneClass + '" data-id="' + tcmChatEsc(it.id) + '"'
                  + ' draggable="true"'
                  + ' ondragstart="tcmSubtasksDragStart(event, \'' + tcmChatEsc(it.id) + '\')"'
                  + ' ondragend="tcmSubtasksDragEnd(event)"'
                  + ' ondragover="tcmSubtasksDragOver(event)"'
                  + ' ondrop="tcmSubtasksDrop(event, \'' + tcmChatEsc(it.id) + '\')">'
                  + '<span class="kb-subt-drag" title="Перетащить">&#8801;</span>'
                  + '<label class="kb-subt-cb-wrap">'
                  + '<input type="checkbox" class="kb-subt-cb"'
                  + (it.done === "1" ? " checked" : "")
                  + ' onchange="tcmSubtasksToggle(\'' + tcmChatEsc(it.id) + '\')">'
                  + '<span class="kb-subt-cb-fake"></span>'
                  + '</label>'
                  + '<span class="kb-subt-text" title="' + tcmChatEsc(doneTitle + editedNote) + '"'
                  + ' ondblclick="tcmSubtasksBeginEdit(\'' + tcmChatEsc(it.id) + '\')">'
                  + tcmChatEsc(it.text || "") + '</span>'
                  + '<button type="button" class="kb-subt-del" '
                  + 'onclick="tcmSubtasksDelete(\'' + tcmChatEsc(it.id) + '\')" title="Удалить">×</button>'
                  + '</div>';
        }
        list.innerHTML = html || '<div class="kb-subt-empty">Нет подзадач</div>';

        // Инжектируем линию-индикатор для DnD (единственный перемещаемый элемент)
        var dlExist = document.createElement("div");
        dlExist.id = "kb-subt-dropline";
        dlExist.style.display = "none";
        list.appendChild(dlExist);

        // Скрываем индикатор когда мышь уходит за пределы списка
        list.ondragleave = function (event) {
            var related = event.relatedTarget || event.toElement;
            if (!related || !list.contains(related)) {
                var dl = document.getElementById("kb-subt-dropline");
                if (dl) dl.style.display = "none";
            }
        };

        var total = items.length, done = 0;
        for (i = 0; i < items.length; i++) if (items[i].done === "1") done++;

        var pr = document.getElementById("tcm-subt-progress");
        if (pr) pr.innerHTML = total ? (done + " / " + total) : "0 / 0";

        var badge = document.getElementById("tcm-tab-subt-badge");
        if (badge) {
            if (total > 0) {
                badge.innerHTML = done + "/" + total;
                badge.style.display = "";
            } else {
                badge.style.display = "none";
            }
        }
    };

    window.tcmSubtasksAdd = function () {
        var inp = document.getElementById("tcm-subt-input");
        if (!inp) return;
        var text = (inp.value || "").replace(/\|/g, " ").substring(0, 500).replace(/^\s+|\s+$/g, "");
        if (!text) return;
        var safeKey = String(_tcmSubtasks.taskKey).replace(/\|/g, "");
        try {
            var raw = window.external.InvokeTemplate("AddSubtask", safeKey + "|" + text);
            var s = String(raw || "");
            if (s.indexOf("ERROR") === 0) { alert("Ошибка добавления: " + s); return; }
            var obj = JSON.parse(s);
            _tcmSubtasks.items.push(obj);
            inp.value = "";
            tcmSubtasksRender();
            tcmInvalidateRevsCache();
            _tcmRevsLoaded = false;
            var rb = document.getElementById("tcm-revs-body");
            if (rb) rb.innerHTML = "Загрузка...";
        } catch (e) { /* no-op */ }
    };

    window.tcmSubtasksToggle = function (subtaskId) {
        var safeKey = String(_tcmSubtasks.taskKey).replace(/\|/g, "");
        var safeId  = String(subtaskId).replace(/\|/g, "");
        try {
            var raw = window.external.InvokeTemplate("ToggleSubtask", safeKey + "|" + safeId);
            var s = String(raw || "");
            if (s.indexOf("ERROR") === 0) return;
            var obj = JSON.parse(s);
            for (var i = 0; i < _tcmSubtasks.items.length; i++) {
                if (_tcmSubtasks.items[i].id === subtaskId) {
                    _tcmSubtasks.items[i] = obj;
                    break;
                }
            }
            tcmSubtasksRender();
            tcmInvalidateRevsCache();
            _tcmRevsLoaded = false;
            var rb = document.getElementById("tcm-revs-body");
            if (rb) rb.innerHTML = "Загрузка...";
        } catch (e) { /* no-op */ }
    };

    window.tcmSubtasksDelete = function (subtaskId) {
        if (!confirm("Удалить подзадачу?")) return;
        var safeKey = String(_tcmSubtasks.taskKey).replace(/\|/g, "");
        var safeId  = String(subtaskId).replace(/\|/g, "");
        try {
            var res = window.external.InvokeTemplate("DeleteSubtask", safeKey + "|" + safeId);
            if (String(res || "").indexOf("OK") !== 0) return;
            for (var i = 0; i < _tcmSubtasks.items.length; i++) {
                if (_tcmSubtasks.items[i].id === subtaskId) {
                    _tcmSubtasks.items.splice(i, 1);
                    break;
                }
            }
            tcmSubtasksRender();
            tcmInvalidateRevsCache();
            _tcmRevsLoaded = false;
            var rb = document.getElementById("tcm-revs-body");
            if (rb) rb.innerHTML = "Загрузка...";
        } catch (e) { /* no-op */ }
    };

    window.tcmSubtasksKeydown = function (e) {
        var code = e.keyCode || e.which;
        if (code === 13) { tcmSubtasksAdd(); e.preventDefault(); }
    };

    // ── DnD-перестановка пунктов чек-листа ─────────────────────────────
    var _tcmSubtDragId = null;

    window.tcmSubtasksDragStart = function (event, subtaskId) {
        if (_tcmSubtEditingId) { event.preventDefault(); return; }
        _tcmSubtDragId = subtaskId;
        try {
            event.dataTransfer.setData("text", subtaskId);
            event.dataTransfer.effectAllowed = "move";
        } catch (e) { /* IE11 quirk */ }
        // Помечаем перетаскиваемую строку + активируем режим DnD на контейнере
        // (pointer-events:none на дочерних элементах — устраняет мигание)
        var row = document.querySelector('.kb-subt-item[data-id="' + tcmChatEsc(subtaskId) + '"]');
        if (row) row.className += " kb-subt-dragging";
        var list = document.getElementById("tcm-subt-list");
        if (list) list.className += " kb-dnd-active";
    };

    window.tcmSubtasksDragEnd = function (event) {
        _tcmSubtDragId = null;
        var list = document.getElementById("tcm-subt-list");
        if (list) list.className = list.className.replace(/\s*kb-dnd-active/g, "");
        var dl = document.getElementById("kb-subt-dropline");
        if (dl) dl.style.display = "none";
        var rows = document.querySelectorAll(".kb-subt-item");
        for (var i = 0; i < rows.length; i++) {
            rows[i].className = rows[i].className.replace(/\s*kb-subt-dragging/g, "");
        }
    };

    window.tcmSubtasksDragOver = function (event) {
        if (!_tcmSubtDragId) return;
        event.preventDefault();
        event.stopPropagation();
        try { event.dataTransfer.dropEffect = "move"; } catch (e) { }

        var row = event.currentTarget;
        if (!row || !row.className || row.className.indexOf("kb-subt-item") === -1) return;

        var list = document.getElementById("tcm-subt-list");
        var dl = document.getElementById("kb-subt-dropline");
        if (!dl || !list) return;

        // Позиционируем линию-индикатор: сверху строки (before) или снизу (after)
        var listRect = list.getBoundingClientRect();
        var rowRect  = row.getBoundingClientRect();
        var midY     = rowRect.top + rowRect.height / 2;
        var before   = (event.clientY < midY);
        var top      = (rowRect.top - listRect.top) + (before ? 0 : rowRect.height) + list.scrollTop;

        dl.style.top     = top + "px";
        dl.style.display = "block";
    };

    window.tcmSubtasksDrop = function (event, targetId) {
        event.preventDefault();
        event.stopPropagation();
        var srcId = _tcmSubtDragId;
        _tcmSubtDragId = null;

        // Убираем визуальные артефакты
        var list = document.getElementById("tcm-subt-list");
        if (list) list.className = list.className.replace(/\s*kb-dnd-active/g, "");
        var dl = document.getElementById("kb-subt-dropline");
        if (dl) dl.style.display = "none";
        var rows = document.querySelectorAll(".kb-subt-item");
        for (var i = 0; i < rows.length; i++) {
            rows[i].className = rows[i].className.replace(/\s*kb-subt-dragging/g, "");
        }

        if (!srcId || !targetId || srcId === targetId) return;

        var row = event.currentTarget;
        var rect = row.getBoundingClientRect();
        var midY = rect.top + rect.height / 2;
        var before = (event.clientY < midY);

        var items = _tcmSubtasks.items || [];
        var srcIdx = -1, tgtIdx = -1;
        for (var j = 0; j < items.length; j++) {
            if (items[j].id === srcId) srcIdx = j;
            if (items[j].id === targetId) tgtIdx = j;
        }
        if (srcIdx < 0 || tgtIdx < 0) return;

        var moved = items.splice(srcIdx, 1)[0];
        if (srcIdx < tgtIdx) tgtIdx--;
        var insertAt = before ? tgtIdx : tgtIdx + 1;
        items.splice(insertAt, 0, moved);

        tcmSubtasksRender();

        var orderIds = [];
        for (var k = 0; k < items.length; k++) orderIds.push(items[k].id);

        var safeKey = String(_tcmSubtasks.taskKey).replace(/\|/g, "");
        var orderStr = orderIds.join(",").replace(/\|/g, "");
        try {
            var res = window.external.InvokeTemplate("ReorderSubtasks", safeKey + "|" + orderStr);
            var s = String(res || "");
            if (s.indexOf("OK") !== 0) {
                alert("Ошибка перестановки: " + s);
                tcmSubtasksLoad(_tcmSubtasks.taskKey);
                return;
            }
        } catch (e) {
            alert("Ошибка перестановки: " + (e.message || e));
            tcmSubtasksLoad(_tcmSubtasks.taskKey);
        }
    };

    // ── Inline-редактирование текста подзадачи ─────────────────────────
    var _tcmSubtEditingId = null;

    window.tcmSubtasksBeginEdit = function (subtaskId) {
        if (_tcmSubtEditingId) return;
        var row = document.querySelector('.kb-subt-item[data-id="' + tcmChatEsc(subtaskId) + '"]');
        if (!row) return;
        var span = row.querySelector(".kb-subt-text");
        if (!span) return;

        var current = "";
        var items = _tcmSubtasks.items || [];
        for (var i = 0; i < items.length; i++) {
            if (items[i].id === subtaskId) { current = items[i].text || ""; break; }
        }

        _tcmSubtEditingId = subtaskId;
        try { row.setAttribute("draggable", "false"); } catch (e) { }

        var inp = document.createElement("input");
        inp.type = "text";
        inp.className = "form-control input-sm kb-subt-edit-input";
        inp.value = current;
        inp.setAttribute("maxlength", "500");
        inp.onkeydown = function (e) {
            var code = e.keyCode || e.which;
            if (code === 13) { tcmSubtasksCommitEdit(subtaskId, inp.value); e.preventDefault(); }
            else if (code === 27) { tcmSubtasksCancelEdit(); e.preventDefault(); }
        };
        inp.onblur = function () { tcmSubtasksCommitEdit(subtaskId, inp.value); };

        span.parentNode.replaceChild(inp, span);
        inp.focus();
        try { inp.select(); } catch (e) { }
    };

    window.tcmSubtasksCommitEdit = function (subtaskId, newText) {
        if (_tcmSubtEditingId !== subtaskId) return;
        _tcmSubtEditingId = null;

        var clean = String(newText || "").replace(/\|/g, " ").substring(0, 500).replace(/^\s+|\s+$/g, "");
        if (!clean) {
            tcmSubtasksRender();
            return;
        }

        var items = _tcmSubtasks.items || [];
        var current = "";
        for (var i = 0; i < items.length; i++) {
            if (items[i].id === subtaskId) { current = items[i].text || ""; break; }
        }
        if (clean === current) {
            tcmSubtasksRender();
            return;
        }

        var safeKey = String(_tcmSubtasks.taskKey).replace(/\|/g, "");
        var safeId  = String(subtaskId).replace(/\|/g, "");
        try {
            var raw = window.external.InvokeTemplate(
                "EditSubtask", safeKey + "|" + safeId + "|" + clean);
            var s = String(raw || "");
            if (s.indexOf("ERROR") === 0) {
                alert("Ошибка редактирования: " + s);
                tcmSubtasksRender();
                return;
            }
            var obj = JSON.parse(s);
            for (var j = 0; j < items.length; j++) {
                if (items[j].id === subtaskId) { items[j] = obj; break; }
            }
            tcmSubtasksRender();
            if (typeof tcmInvalidateRevsCache === "function") tcmInvalidateRevsCache();
            _tcmRevsLoaded = false;
            var rb = document.getElementById("tcm-revs-body");
            if (rb) rb.innerHTML = "Загрузка...";
        } catch (e) {
            alert("Ошибка редактирования: " + (e.message || e));
            tcmSubtasksRender();
        }
    };

    window.tcmSubtasksCancelEdit = function () {
        _tcmSubtEditingId = null;
        tcmSubtasksRender();
    };

    // ── Ролевая иерархия: каскадные селекторы (шаг 05) ──────────────────
    // Роли: regular → панель скрыта; headOfSector/leadEngineer → [Сотрудник ▼];
    //        headOfDept → [Сектор ▼][Сотрудник ▼]; admin → все три.
    // Кнопка «Мои задачи» — сбрасывает режим на «my».

    var _kbH = {
        role: "regular", myKey: "", myContext: "", viewMode: "my",
        divisions: [], sectors: [], users: []
    };
    var _kbGrpSelected = {};  // ключи выбранных получателей в групповом режиме

    window.kbInitHierarchy = function () {
        var raw;
        try { raw = window.external.InvokeTemplate("GetHierarchyInfo", ""); } catch (e) { return; }
        if (!raw || (typeof raw === "string" && raw.indexOf("ERROR") === 0)) return;
        try { _kbH = JSON.parse(String(raw)); } catch (e) { return; }

        var panel = document.getElementById("kb-hier-panel");
        if (!panel) return;
        if (_kbH.role === "regular") { panel.style.display = "none"; return; }

        panel.style.display = "block";
        kbToggleEl("kb-sel-dept", _kbH.role === "admin");
        kbToggleEl("kb-sel-sector", _kbH.role === "admin" || _kbH.role === "headOfDept");
        kbToggleEl("kb-sel-user", true);
        kbToggleEl("kb-sel-search", true);
        kbToggleEl("kb-hier-label", true);

        if (_kbH.role === "admin") { kbFillDivisions(); }
        if (_kbH.role === "admin" || _kbH.role === "headOfDept") { kbFillSectors(null); }
        kbFillUsers(null, null);

        var allBtn = document.getElementById("kb-btn-all");
        if (allBtn) {
            allBtn.textContent = _kbH.role === "admin" ? "Все задачи"
                : _kbH.role === "headOfDept" ? "Всё отделение"
                    : "Весь сектор";
            allBtn.style.display = "";
        }

        var mcBtn = document.getElementById("kb-btn-mycreated");
        if (mcBtn) {
            mcBtn.style.display = (_kbH.role !== "regular") ? "" : "none";
        }

        kbRestoreViewMode(_kbH.viewMode || "my");
        kbInitCreateForm();

        // Enter в поле поиска верхней панели — применить выбранного сотрудника
        // как фильтр доски. На input() мы только фильтруем визуально
        // (см. kbOnSelSearch), чтобы не дёргать RefreshBoard на каждую клавишу
        // и не сбрасывать режим при пустом запросе.
        var selSearchEl = document.getElementById("kb-sel-search");
        if (selSearchEl && !selSearchEl._kbEnterBound) {
            selSearchEl._kbEnterBound = true;
            selSearchEl.onkeydown = function (e) {
                if ((e.keyCode || e.which) !== 13) return;
                e.preventDefault();
                var userEl = document.getElementById("kb-sel-user");
                var userKey = userEl ? (userEl.value || "") : "";
                if (!userKey) return; // «Нет совпадений» или пусто — не применяем
                kbUpdateMyBtn(false);
                kbStyleBtn("kb-btn-all", false);
                kbApplyMode();
            };
        }

        // Фильтр периода инициализируется отдельно вне kbInitHierarchy
        // (см. вызов в конце файла) — чтобы работал и для роли "regular".
    };

    function kbFillDivisions() {
        var sel = document.getElementById("kb-sel-dept");
        if (!sel) return;
        kbClearSel(sel);
        sel.appendChild(kbOpt("", "Все отделения"));
        for (var i = 0; i < _kbH.divisions.length; i++) {
            var d = _kbH.divisions[i];
            sel.appendChild(kbOpt(d.key, d.name));
        }
    }

    function kbFillSectors(deptKey) {
        var sel = document.getElementById("kb-sel-sector");
        if (!sel) return;
        kbClearSel(sel);
        sel.appendChild(kbOpt("", deptKey ? "Все сектора " + deptKey : "Все сектора"));
        for (var i = 0; i < _kbH.sectors.length; i++) {
            var s = _kbH.sectors[i];
            if (deptKey && !kbBelongsToDept(s.key, deptKey)) continue;
            sel.appendChild(kbOpt(s.key, s.name));
        }
    }

    function kbFillUsers(deptKey, sectorKey, searchQuery) {
        var sel = document.getElementById("kb-sel-user");
        if (!sel) return;
        kbClearSel(sel);
        var q = (searchQuery || "").toLowerCase().replace(/^\s+|\s+$/g, "");
        // Без поиска — показываем плашку «Все сотрудники» первой (чтобы по умолчанию
        // не выбирался случайный сотрудник). С поиском — плашки нет, и первое
        // совпадение автоматически становится выбранным (аналог панели создания).
        if (!q) sel.appendChild(kbOpt("", "Все сотрудники"));
        var matched = 0;
        for (var i = 0; i < _kbH.users.length; i++) {
            var u = _kbH.users[i];
            var ctx = u.context || "";
            if (sectorKey && ctx !== sectorKey) continue;
            if (!sectorKey && deptKey && !kbBelongsToDept(ctx, deptKey)) continue;
            if (q && String(u.name || "").toLowerCase().indexOf(q) === -1) continue;
            sel.appendChild(kbOpt(u.key, u.name));
            matched++;
        }
        if (q && matched === 0) sel.appendChild(kbOpt("", "Нет совпадений"));
    }

    function kbClearSelSearch() {
        var s = document.getElementById("kb-sel-search");
        if (s) s.value = "";
    }

    window.kbOnDeptChange = function () {
        var deptKey = kbSelVal("kb-sel-dept");
        kbClearSelSearch();
        kbFillSectors(deptKey || null);
        kbFillUsers(deptKey || null, null, "");
        kbUpdateMyBtn(false);
        kbStyleBtn("kb-btn-all", false);
        kbApplyMode();
    };

    window.kbOnSectorChange = function () {
        var deptKey = kbSelVal("kb-sel-dept");
        var sectorKey = kbSelVal("kb-sel-sector");
        kbClearSelSearch();
        kbFillUsers(deptKey || null, sectorKey || null, "");
        kbUpdateMyBtn(false);
        kbStyleBtn("kb-btn-all", false);
        kbApplyMode();
    };

    window.kbOnUserChange = function () {
        // FIX-03: запоминаем локально, чтобы при возможном лаге RefreshBoard
        // селект уже не сбрасывался при следующем перерендере JS-инициализации
        var userKey = kbSelVal("kb-sel-user");
        if (userKey) _kbH.viewMode = "user:" + userKey;

        kbUpdateMyBtn(false);
        kbStyleBtn("kb-btn-all", false);
        kbApplyMode();
    };

    window.kbOnSelSearch = function () {
        var deptKey = kbSelVal("kb-sel-dept");
        var sectorKey = kbSelVal("kb-sel-sector");
        var s = document.getElementById("kb-sel-search");
        var q = s ? (s.value || "") : "";
        // Только фильтруем выпадашку визуально — первое совпадение становится
        // выбранным в списке. Доску не трогаем: чтобы применить фильтр по
        // сотруднику, пользователь кликает по дропдауну (или жмёт Enter —
        // см. keydown-обработчик в kbInitHierarchy). Это исключает случайное
        // переключение режима при очистке поиска.
        kbFillUsers(deptKey || null, sectorKey || null, q);
    };

    window.kbSetMyMode = function () {
        kbSetSelVal("kb-sel-dept", "");
        kbSetSelVal("kb-sel-sector", "");
        kbSetSelVal("kb-sel-user", "");
        kbClearSelSearch();
        if (_kbH.role === "admin" || _kbH.role === "headOfDept") {
            kbFillSectors(null);
            kbFillUsers(null, null, "");
        }
        kbUpdateMyBtn(true);
        kbSendMode("my");
    };

    function kbApplyMode() {
        var deptKey = kbSelVal("kb-sel-dept");
        var sectorKey = kbSelVal("kb-sel-sector");
        var userKey = kbSelVal("kb-sel-user");
        var mode;
        if (userKey) { mode = "user:" + userKey; }
        else if (sectorKey) { mode = "group:" + sectorKey; }
        else if (deptKey) { mode = "group:" + deptKey; }
        else {
            if (_kbH.role === "admin") mode = "all";
            else if (_kbH.role === "headOfDept") mode = "dept";
            else if (_kbH.role === "headOfSector" || _kbH.role === "leadEngineer") mode = "sector";
            else mode = "my";
        }
        kbSendMode(mode);
    }

    function kbSendMode(mode) {
        try { window.external.InvokeTemplate("SetViewMode", mode); } catch (e) { }
        try { window.external.InvokeTemplate("RefreshBoard", ""); } catch (e) { }
    }

    function kbRestoreViewMode(mode) {
        if (!mode || mode === "my") { kbUpdateMyBtn(true); return; }
        if (mode === "all" || mode === "dept" || mode === "sector") { kbUpdateAllBtn(true); return; }
        if (mode === "myCreated") { kbUpdateMyCreatedBtn(true); return; }

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
            // Восстановить dept/sector контекст
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
            // FIX-03 v2: полная пересборка селекта — выбранный юзер ставится
            // ПЕРВОЙ опцией, selectedIndex=0. Это надёжнее в IE11/Trident,
            // потому что не зависит от того, что .value применилось корректно
            // или что нужная опция уже есть после kbFillUsers с фильтрами.
            var selUser = document.getElementById("kb-sel-user");
            if (selUser && (userName || userKey)) {
                // Сохраняем текущий список (без "Все сотрудники", без выбранного)
                var saved = [];
                for (var oi = 0; oi < selUser.options.length; oi++) {
                    var ov = selUser.options[oi].value;
                    var ot = selUser.options[oi].text;
                    if (ov === "" || ov === userKey) continue; // пропускаем "Все" и текущего
                    saved.push({ v: ov, t: ot });
                }
                kbClearSel(selUser);
                // 1. Сначала выбранный — он же selectedIndex=0
                var pickedOpt = kbOpt(userKey, userName || userKey);
                pickedOpt.setAttribute("value", userKey); // подстраховка для IE11
                selUser.appendChild(pickedOpt);
                // 2. Плашка "Все сотрудники" вторым пунктом — чтобы можно было сбросить
                selUser.appendChild(kbOpt("", "Все сотрудники"));
                // 3. Остальные пользователи
                for (var si = 0; si < saved.length; si++) {
                    selUser.appendChild(kbOpt(saved[si].v, saved[si].t));
                }
                selUser.selectedIndex = 0;
                selUser.value = userKey; // дублируем — некоторые билды IE требуют оба

                // Повтор через setTimeout — на случай, если что-то на странице
                // после нас сделает kbFillUsers и сбросит selection.
                setTimeout(function () {
                    var s2 = document.getElementById("kb-sel-user");
                    if (!s2) return;
                    var still = false;
                    for (var k = 0; k < s2.options.length; k++) {
                        if (s2.options[k].value === userKey) {
                            if (s2.selectedIndex !== k) s2.selectedIndex = k;
                            still = true;
                            break;
                        }
                    }
                    if (!still) {
                        // Опция исчезла — добавляем заново и выбираем
                        var p = kbOpt(userKey, userName || userKey);
                        p.setAttribute("value", userKey);
                        if (s2.firstChild) s2.insertBefore(p, s2.firstChild);
                        else s2.appendChild(p);
                        s2.selectedIndex = 0;
                        s2.value = userKey;
                    }
                }, 50);
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

    // ── Кнопка «Все задачи» / «Всё отделение» / «Весь сектор» ─────────────
    window.kbSetAllMode = function () {
        kbSetSelVal("kb-sel-dept", "");
        kbSetSelVal("kb-sel-sector", "");
        kbSetSelVal("kb-sel-user", "");
        kbClearSelSearch();
        if (_kbH.role === "admin" || _kbH.role === "headOfDept") {
            kbFillSectors(null);
            kbFillUsers(null, null, "");
        }
        kbUpdateAllBtn(true);
        var mode = _kbH.role === "admin" ? "all"
            : _kbH.role === "headOfDept" ? "dept"
                : "sector";
        kbSendMode(mode);
    };

    window.kbSetMyCreatedMode = function () {
        kbSetSelVal("kb-sel-dept", "");
        kbSetSelVal("kb-sel-sector", "");
        kbSetSelVal("kb-sel-user", "");
        kbClearSelSearch();
        if (_kbH.role === "admin" || _kbH.role === "headOfDept") {
            kbFillSectors(null);
            kbFillUsers(null, null, "");
        }
        kbUpdateMyCreatedBtn(true);
        kbSendMode("myCreated");
    };

    // ── Независимые каскадные селекторы в форме создания задачи ────────────
    // Не привязаны к панели иерархии → не вызывают RefreshBoard.
    function kbInitCreateForm() {
        var row = document.getElementById("kb-crt-assignee-row");
        if (!row) return;
        if (_kbH.role === "regular") { row.style.display = "none"; return; }
        row.style.display = "";

        if (_kbH.role === "admin") { kbFillCrtDepts(); }
        if (_kbH.role === "admin" || _kbH.role === "headOfDept") { kbFillCrtSectors(null); }
        kbFillCrtUsers(null, null);

        // Сбросить в одиночный режим
        var modeOneEl = document.getElementById("kb-crt-mode-one");
        if (modeOneEl) modeOneEl.checked = true;
        var singleWrap = document.getElementById("kb-crt-single-wrap");
        if (singleWrap) singleWrap.style.display = "";
        var groupRow = document.getElementById("kb-crt-group-row");
        if (groupRow) groupRow.style.display = "none";
        _kbGrpSelected = {};
        var grpSearch = document.getElementById("kb-grp-search");
        if (grpSearch) grpSearch.value = "";

        // Сбрасываем чекбокс в «Сам себе» и скрываем селекторы
        var selfEl = document.getElementById("kb-crt-self");
        if (selfEl) selfEl.checked = true;
        // Принудительно скрываем все селекторы при инициализации (Сам себе = вкл)
        var deptWrap = document.getElementById("kb-crt-dept-wrap");
        var sectorWrap = document.getElementById("kb-crt-sector-wrap");
        var userWrap = document.getElementById("kb-crt-user-wrap");
        if (deptWrap) deptWrap.style.display = "none";
        if (sectorWrap) sectorWrap.style.display = "none";
        if (userWrap) userWrap.style.display = "none";
    }

    function kbFillCrtDepts() {
        var sel = document.getElementById("kb-crt-dept");
        if (!sel) return;
        kbClearSel(sel);
        sel.appendChild(kbOpt("", "Все отделения"));
        for (var i = 0; i < _kbH.divisions.length; i++) {
            var d = _kbH.divisions[i];
            sel.appendChild(kbOpt(d.key, d.name));
        }
    }

    function kbFillCrtSectors(deptKey) {
        var sel = document.getElementById("kb-crt-sector");
        if (!sel) return;
        kbClearSel(sel);
        sel.appendChild(kbOpt("", deptKey ? "Все сектора " + deptKey : "Все сектора"));
        for (var i = 0; i < _kbH.sectors.length; i++) {
            var s = _kbH.sectors[i];
            if (deptKey && !kbBelongsToDept(s.key, deptKey)) continue;
            sel.appendChild(kbOpt(s.key, s.name));
        }
    }

    function kbFillCrtUsers(deptKey, sectorKey, searchQuery) {
        var sel = document.getElementById("kb-crt-user");
        if (!sel) return;
        kbClearSel(sel);
        var q = (searchQuery || "").toLowerCase().replace(/^\s+|\s+$/g, "");
        // «Сам себе» — отдельный чекбокс, не опция в дропдауне
        for (var i = 0; i < _kbH.users.length; i++) {
            var u = _kbH.users[i];
            if (u.key === _kbH.myKey) continue;
            var ctx = u.context || "";
            if (sectorKey && ctx !== sectorKey) continue;
            if (!sectorKey && deptKey && !kbBelongsToDept(ctx, deptKey)) continue;
            if (q && String(u.name || "").toLowerCase().indexOf(q) === -1) continue;
            sel.appendChild(kbOpt(u.key, u.name));
        }
    }

    // Чекбокс «Сам себе»: скрывает/показывает каскадные селекторы формы
    window.kbOnSelfChange = function () {
        var selfEl = document.getElementById("kb-crt-self");
        var isSelf = selfEl ? selfEl.checked : true;

        var deptWrap = document.getElementById("kb-crt-dept-wrap");
        var sectorWrap = document.getElementById("kb-crt-sector-wrap");
        var userWrap = document.getElementById("kb-crt-user-wrap");
        var deptEl = document.getElementById("kb-crt-dept");
        var sectorEl = document.getElementById("kb-crt-sector");
        var userEl = document.getElementById("kb-crt-user");
        var searchEl = document.getElementById("kb-crt-user-search");

        if (isSelf) {
            if (deptWrap) deptWrap.style.display = "none";
            if (sectorWrap) sectorWrap.style.display = "none";
            if (userWrap) userWrap.style.display = "none";
            if (searchEl) searchEl.value = "";
        } else {
            if (deptWrap) deptWrap.style.display = (_kbH.role === "admin") ? "inline" : "none";
            if (sectorWrap) sectorWrap.style.display =
                (_kbH.role === "admin" || _kbH.role === "headOfDept") ? "inline" : "none";
            if (userWrap) userWrap.style.display = "inline";
            if (deptEl) deptEl.disabled = false;
            if (sectorEl) sectorEl.disabled = false;
            if (userEl) userEl.disabled = false;
        }
    };

    window.kbOnCrtDeptChange = function () {
        var crtDept = document.getElementById("kb-crt-dept");
        var deptKey = crtDept ? (crtDept.value || "") : "";
        var searchEl = document.getElementById("kb-crt-user-search");
        if (searchEl) searchEl.value = "";
        kbFillCrtSectors(deptKey || null);
        kbFillCrtUsers(deptKey || null, null, "");
        // Не вызывает kbApplyMode/kbSendMode — доска не обновляется
    };

    window.kbOnCrtSectorChange = function () {
        var crtDept = document.getElementById("kb-crt-dept");
        var crtSector = document.getElementById("kb-crt-sector");
        var deptKey = crtDept ? (crtDept.value || "") : "";
        var sectorKey = crtSector ? (crtSector.value || "") : "";
        var searchEl = document.getElementById("kb-crt-user-search");
        if (searchEl) searchEl.value = "";
        kbFillCrtUsers(deptKey || null, sectorKey || null, "");
        // Не вызывает kbApplyMode/kbSendMode — доска не обновляется
    };

    window.kbOnCrtUserSearch = function () {
        var crtDept = document.getElementById("kb-crt-dept");
        var crtSector = document.getElementById("kb-crt-sector");
        var searchEl = document.getElementById("kb-crt-user-search");
        var deptKey = crtDept ? (crtDept.value || "") : "";
        var sectorKey = crtSector ? (crtSector.value || "") : "";
        var q = searchEl ? (searchEl.value || "") : "";
        kbFillCrtUsers(deptKey || null, sectorKey || null, q);
    };

    // Родительское отделение по контексту сектора: "510кт" → "500кт"
    function kbCtxDiv(ctx) {
        var n = kbCtxNum(ctx);
        if (n <= 0) return "";
        var divN = Math.floor(n / 100) * 100;
        return String(divN) + ctx.replace(/^\d+/, "");
    }

    function kbStyleBtn(id, active) {
        var btn = document.getElementById(id);
        if (!btn) return;
        btn.style.background = active ? "#4a6fa5" : "#ffffff";
        btn.style.color = active ? "#ffffff" : "#374151";
        btn.style.borderColor = active ? "#4a6fa5" : "#d1d5db";
        btn.style.fontWeight = active ? "600" : "normal";
    }
    function kbUpdateMyBtn(active) {
        kbStyleBtn("kb-btn-my", active);
        kbStyleBtn("kb-btn-all", false);
        kbStyleBtn("kb-btn-mycreated", false);
    }
    function kbUpdateAllBtn(active) {
        kbStyleBtn("kb-btn-my", false);
        kbStyleBtn("kb-btn-all", active);
        kbStyleBtn("kb-btn-mycreated", false);
    }
    function kbUpdateMyCreatedBtn(active) {
        kbStyleBtn("kb-btn-my", false);
        kbStyleBtn("kb-btn-all", false);
        kbStyleBtn("kb-btn-mycreated", active);
    }

    function kbBelongsToDept(sectorCtx, deptCtx) {
        if (!sectorCtx || !deptCtx) return false;
        if (sectorCtx === deptCtx) return true;
        var sn = kbCtxNum(sectorCtx), dn = kbCtxNum(deptCtx);
        if (sn <= 0 || dn <= 0) return false;
        return Math.floor(sn / 100) === Math.floor(dn / 100);
    }
    function kbCtxNum(ctx) {
        if (!ctx) return 0;
        var m = ctx.match(/^(\d+)/);
        return m ? parseInt(m[1], 10) : 0;
    }
    function kbOpt(value, text) {
        var o = document.createElement("option");
        o.value = value; o.text = text; return o;
    }
    // FIX-03: проверка наличия option с конкретным value
    function kbHasOption(sel, value) {
        if (!sel || !sel.options) return false;
        for (var i = 0; i < sel.options.length; i++) {
            if (sel.options[i].value === value) return true;
        }
        return false;
    }
    function kbClearSel(sel) { while (sel.firstChild) sel.removeChild(sel.firstChild); }
    function kbToggleEl(id, show) {
        var el = document.getElementById(id);
        if (el) el.style.display = show ? "inline-block" : "none";
    }
    function kbSelVal(id) {
        var el = document.getElementById(id);
        return (el && el.style.display !== "none") ? (el.value || "") : "";
    }
    function kbSetSelVal(id, value) {
        var el = document.getElementById(id);
        if (el) el.value = value;
    }

    // ── Шаг 05.1: Групповая задача ────────────────────────────────────────

    // Экранирование для innerHTML и onchange-атрибутов в динамическом HTML
    function kbEscHtml(s) {
        return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;")
            .replace(/>/g, "&gt;").replace(/"/g, "&quot;");
    }
    function kbEscAttr(s) { return String(s).replace(/\\/g, "\\\\").replace(/"/g, "\\\""); }

    // Переключение одиночный ↔ групповой режим
    window.kbOnModeChange = function () {
        var modeGrp = document.getElementById("kb-crt-mode-group");
        var isGroup = modeGrp && modeGrp.checked;
        var singleWrap = document.getElementById("kb-crt-single-wrap");
        var groupRow = document.getElementById("kb-crt-group-row");
        if (singleWrap) singleWrap.style.display = isGroup ? "none" : "";
        if (groupRow) groupRow.style.display = isGroup ? "" : "none";

        if (isGroup) {
            _kbGrpSelected = {};
            // Показать/скрыть фильтры отделения/сектора
            var grpDeptWrap = document.getElementById("kb-grp-dept-wrap");
            var grpSectorWrap = document.getElementById("kb-grp-sector-wrap");
            if (grpDeptWrap) grpDeptWrap.style.display = (_kbH.role === "admin") ? "" : "none";
            if (grpSectorWrap) grpSectorWrap.style.display =
                (_kbH.role === "admin" || _kbH.role === "headOfDept") ? "" : "none";
            // Заполнить фильтры
            if (_kbH.role === "admin") kbFillGrpDepts();
            if (_kbH.role === "admin" || _kbH.role === "headOfDept") kbFillGrpSectors(null);
            // Показать кнопки быстрого выбора групп
            var presetDepts = document.getElementById("kb-grp-preset-depts");
            var presetSectors = document.getElementById("kb-grp-preset-sectors");
            if (presetDepts) presetDepts.style.display = (_kbH.role === "admin") ? "" : "none";
            if (presetSectors) presetSectors.style.display =
                (_kbH.role === "admin" || _kbH.role === "headOfDept") ? "" : "none";
            kbGrpRenderList();
        }
    };

    function kbFillGrpDepts() {
        var sel = document.getElementById("kb-grp-dept");
        if (!sel) return;
        kbClearSel(sel);
        sel.appendChild(kbOpt("", "Все отделения"));
        for (var i = 0; i < _kbH.divisions.length; i++) {
            var d = _kbH.divisions[i];
            sel.appendChild(kbOpt(d.key, d.name));
        }
    }
    function kbFillGrpSectors(deptKey) {
        var sel = document.getElementById("kb-grp-sector");
        if (!sel) return;
        kbClearSel(sel);
        sel.appendChild(kbOpt("", deptKey ? "Все сектора " + deptKey : "Все сектора"));
        for (var i = 0; i < _kbH.sectors.length; i++) {
            var s = _kbH.sectors[i];
            if (deptKey && !kbBelongsToDept(s.key, deptKey)) continue;
            sel.appendChild(kbOpt(s.key, s.name));
        }
    }

    window.kbOnGrpDeptChange = function () {
        var sel = document.getElementById("kb-grp-dept");
        var deptKey = sel ? (sel.value || "") : "";
        kbFillGrpSectors(deptKey || null);
        kbGrpRenderList();
    };
    window.kbOnGrpSectorChange = function () { kbGrpRenderList(); };
    window.kbOnGrpSearch = function () { kbGrpRenderList(); };

    // Возвращает текущие значения фильтров группового режима
    function kbGrpGetFilters() {
        var deptSel = document.getElementById("kb-grp-dept");
        var sectorSel = document.getElementById("kb-grp-sector");
        var searchEl = document.getElementById("kb-grp-search");
        return {
            dept: deptSel ? (deptSel.value || "") : "",
            sector: sectorSel ? (sectorSel.value || "") : "",
            search: searchEl ? (searchEl.value || "").toLowerCase() : ""
        };
    }

    // Рендер списка получателей с чекбоксами (IE11-совместимый innerHTML)
    function kbGrpRenderList() {
        var div = document.getElementById("kb-grp-list");
        if (!div) return;
        var f = kbGrpGetFilters();
        var html = "";
        var shown = 0;

        // Себя — первым в списке с бейджем [сам себе]
        for (var mi = 0; mi < _kbH.users.length; mi++) {
            if (_kbH.users[mi].key !== _kbH.myKey) continue;
            var me = _kbH.users[mi];
            if (f.search && me.name.toLowerCase().indexOf(f.search) === -1) break;
            var mChk = _kbGrpSelected[me.key] ? " checked" : "";
            html += "<label>"
                + "<input type='checkbox' value='" + kbEscAttr(me.key) + "'" + mChk
                + " onchange='kbGrpToggle(\"" + kbEscAttr(me.key) + "\")'"
                + ">"
                + "<span>" + kbEscHtml(me.name)
                + " <span style='color:#3b82f6;font-size:10px;'>[сам себе]</span></span>"
                + "</label>";
            shown++;
            break;
        }

        for (var i = 0; i < _kbH.users.length; i++) {
            var u = _kbH.users[i];
            if (u.key === _kbH.myKey) continue;
            var ctx = u.context || "";
            if (f.dept && !kbBelongsToDept(ctx, f.dept)) continue;
            if (f.sector && ctx !== f.sector) continue;
            if (f.search && u.name.toLowerCase().indexOf(f.search) === -1) continue;
            var checked = _kbGrpSelected[u.key] ? " checked" : "";
            var sr = u.subrole || "";
            var badge = sr === "headOfDept" ? " <span style='color:#3b82f6;font-size:10px;'>[нач.отд]</span>"
                : sr === "headOfSector" ? " <span style='color:#3b82f6;font-size:10px;'>[нач.сект]</span>"
                    : sr === "leadEngineer" ? " <span style='color:#3b82f6;font-size:10px;'>[вед.инж]</span>"
                    : "";
            html += "<label>"
                + "<input type='checkbox' value='" + kbEscAttr(u.key) + "'" + checked
                + " onchange='kbGrpToggle(\"" + kbEscAttr(u.key) + "\")'"
                + ">"
                + "<span>" + kbEscHtml(u.name) + badge + "</span>"
                + "</label>";
            shown++;
        }
        div.innerHTML = shown > 0 ? html : "<span style='color:#999;'>Нет сотрудников</span>";
        kbGrpUpdateCount();
    }

    window.kbGrpToggle = function (key) {
        if (_kbGrpSelected[key]) delete _kbGrpSelected[key];
        else _kbGrpSelected[key] = true;
        kbGrpUpdateCount();
    };

    function kbGrpUpdateCount() {
        var el = document.getElementById("kb-grp-count");
        var n = 0;
        for (var k in _kbGrpSelected) { if (_kbGrpSelected.hasOwnProperty(k)) n++; }
        if (el) el.textContent = "Выбрано: " + n;
    }

    // Быстрый выбор: все нач. отделений / все нач. секторов
    window.kbGrpSelectPreset = function (type) {
        var f = kbGrpGetFilters();
        for (var i = 0; i < _kbH.users.length; i++) {
            var u = _kbH.users[i];
            var sr = u.subrole || "";
            if (type === "depts" && sr !== "headOfDept") continue;
            if (type === "sectors" && sr !== "headOfSector") continue;
            var ctx = u.context || "";
            if (f.dept && !kbBelongsToDept(ctx, f.dept)) continue;
            if (f.sector && ctx !== f.sector) continue;
            _kbGrpSelected[u.key] = true;
        }
        kbGrpRenderList();
    };

    // Выбрать/снять всех видимых в текущем фильтре
    window.kbGrpSelectAll = function () {
        var f = kbGrpGetFilters();
        for (var i = 0; i < _kbH.users.length; i++) {
            var u = _kbH.users[i];
            var ctx = u.context || "";
            if (f.dept && !kbBelongsToDept(ctx, f.dept)) continue;
            if (f.sector && ctx !== f.sector) continue;
            if (f.search && u.name.toLowerCase().indexOf(f.search) === -1) continue;
            _kbGrpSelected[u.key] = true;
        }
        kbGrpRenderList();
    };

    window.kbGrpClearAll = function () {
        var f = kbGrpGetFilters();
        if (f.dept || f.sector || f.search) {
            for (var i = 0; i < _kbH.users.length; i++) {
                var u = _kbH.users[i];
                var ctx = u.context || "";
                if (f.dept && !kbBelongsToDept(ctx, f.dept)) continue;
                if (f.sector && ctx !== f.sector) continue;
                if (f.search && u.name.toLowerCase().indexOf(f.search) === -1) continue;
                delete _kbGrpSelected[u.key];
            }
        } else {
            _kbGrpSelected = {};
        }
        kbGrpRenderList();
    };

    // ── Шаг 06: Отчёты ────────────────────────────────────────────────────

    window.openReport = function () {
        var overlay = document.getElementById("reportOverlay");
        if (!overlay) return;
        rptPopulateScope();
        overlay.className = "rpt-overlay visible";
        loadReport();
    };

    window.closeReport = function () {
        var overlay = document.getElementById("reportOverlay");
        if (overlay) overlay.className = "rpt-overlay";
    };

    // Заполняет scope-селектор на основе иерархии (_kbH)
    function rptPopulateScope() {
        var wrap = document.getElementById("rpt-scope-wrap");
        var sel = document.getElementById("reportScope");
        if (!sel) return;

        // Regular: scope-панель не нужна (сервер сам ограничит "my")
        if (_kbH.role === "regular") {
            if (wrap) wrap.style.display = "none";
            return;
        }
        if (wrap) wrap.style.display = "";

        var cur = sel.value || "";  // сохраняем текущий выбор при обновлении
        kbClearSel(sel);

        // Первая опция — полный охват для данной роли
        if (_kbH.role === "admin") sel.appendChild(kbOpt("all", "Все задачи"));
        else if (_kbH.role === "headOfDept") sel.appendChild(kbOpt("dept", "Всё отделение"));
        else sel.appendChild(kbOpt("sector", "Весь сектор"));

        // Отделения (только admin)
        if (_kbH.role === "admin" && _kbH.divisions.length > 0) {
            var og1 = document.createElement("optgroup");
            og1.label = "— Отделения —";
            for (var d = 0; d < _kbH.divisions.length; d++) {
                var o1 = document.createElement("option");
                o1.value = "group:" + _kbH.divisions[d].key;
                o1.text = _kbH.divisions[d].name;
                og1.appendChild(o1);
            }
            sel.appendChild(og1);
        }

        // Секторы (admin + headOfDept)
        if ((_kbH.role === "admin" || _kbH.role === "headOfDept") && _kbH.sectors.length > 0) {
            var og2 = document.createElement("optgroup");
            og2.label = "— Секторы —";
            for (var s = 0; s < _kbH.sectors.length; s++) {
                var o2 = document.createElement("option");
                o2.value = "group:" + _kbH.sectors[s].key;
                o2.text = _kbH.sectors[s].name;
                og2.appendChild(o2);
            }
            sel.appendChild(og2);
        }

        // Сотрудники (все роли кроме regular)
        if (_kbH.users.length > 0) {
            var og3 = document.createElement("optgroup");
            og3.label = "— Сотрудники —";
            for (var u = 0; u < _kbH.users.length; u++) {
                var o3 = document.createElement("option");
                o3.value = "user:" + _kbH.users[u].key;
                o3.text = _kbH.users[u].name;
                og3.appendChild(o3);
            }
            sel.appendChild(og3);
        }

        // Восстанавливаем предыдущий выбор если опция всё ещё в списке
        if (cur) sel.value = cur;
    }

    window.toggleReportPeriod = function () {
        var periodSel = document.getElementById("reportPeriod");
        var customDates = document.getElementById("rpt-custom-dates");
        if (periodSel && customDates) {
            customDates.style.display = (periodSel.value === "custom") ? "" : "none";
        }
        // Запускаем отчёт сразу, только если это не custom
        // (для custom пользователь заполнит даты и нажмёт «Обновить»)
        if (periodSel && periodSel.value !== "custom") {
            loadReport();
        }
    };

    window.loadReport = function () {
        var periodSel = document.getElementById("reportPeriod");
        var period = periodSel ? periodSel.value : "month";

        var scopeWrap = document.getElementById("rpt-scope-wrap");
        var scopeEl = document.getElementById("reportScope");
        var scope = (scopeWrap && scopeWrap.style.display !== "none" && scopeEl)
            ? (scopeEl.value || "") : "";

        var param;
        if (period === "custom") {
            var rptFrom = document.getElementById("rpt-period-from");
            var rptTo   = document.getElementById("rpt-period-to");
            var sFrom = rptFrom ? (rptFrom.value || "").replace(/^\s+|\s+$/g, "") : "";
            var sTo   = rptTo   ? (rptTo.value   || "").replace(/^\s+|\s+$/g, "") : "";
            if (!sFrom || !sTo) {
                alert("Укажите обе даты произвольного периода");
                return;
            }
            if (!/^\d{2}\.\d{2}\.\d{4}$/.test(sFrom) || !/^\d{2}\.\d{2}\.\d{4}$/.test(sTo)) {
                alert("Даты периода должны быть в формате ДД.ММ.ГГГГ");
                return;
            }
            param = "custom|" + sFrom + "|" + sTo + "|" + scope;
        } else {
            param = period + "|" + scope;
        }

        try {
            var res = window.external.InvokeTemplate("GetReport", param);
            if (!res || (typeof res === "string" && res.indexOf("ERROR") === 0)) {
                alert("Ошибка загрузки отчёта: " + res); return;
            }
            renderReport(JSON.parse(String(res)));
        } catch (e) { alert("Ошибка отчёта: " + (e.message || e)); }
    };

    function renderReport(data) {
        // Сводка
        var sh = "";
        sh += "<div class='rpt-card'><div class='rpt-num'>" + data.totalTasks + "</div><div class='rpt-lbl'>Всего задач</div></div>";
        sh += "<div class='rpt-card completed'><div class='rpt-num'>" + data.completedInPeriod + "</div><div class='rpt-lbl'>Выполнено за период</div></div>";
        sh += "<div class='rpt-card overdue'><div class='rpt-num'>" + data.overdue + "</div><div class='rpt-lbl'>Просрочено</div></div>";
        sh += "<div class='rpt-card rpt-progress'><div class='rpt-num'>" + data.inProgress + "</div><div class='rpt-lbl'>В работе</div></div>";
        sh += "<div class='rpt-card'><div class='rpt-num'>" + data.createdInPeriod + "</div><div class='rpt-lbl'>Создано за период</div></div>";
        document.getElementById("reportSummary").innerHTML = sh;

        // Показывать ли колонку «Сектор/Отд.» (актуально при широком охвате)
        var sc = data.scope || "";
        var showCtx = (sc !== "my" && sc !== "sector" && sc.indexOf("user:") !== 0);
        var thCtx = document.getElementById("rpt-th-ctx");
        if (thCtx) thCtx.style.display = showCtx ? "" : "none";

        // Таблица по исполнителям
        var users = data.byUser || [];
        users.sort(function (a, b) { return b.total - a.total; });
        var bh = "";
        for (var i = 0; i < users.length; i++) {
            var u = users[i];
            var pct = u.total > 0 ? Math.round(u.completed / u.total * 100) : 0;
            bh += "<tr>";
            bh += "<td><b>" + rptEscHtml(u.name) + "</b></td>";
            if (showCtx) bh += "<td style='color:#6b7280;font-size:11px;'>" + rptEscHtml(u.context || "") + "</td>";
            bh += "<td>" + u.total + "</td>";
            bh += "<td style='color:#16a34a;font-weight:600;'>" + u.completed + "</td>";
            bh += "<td style='color:" + (u.overdue > 0 ? "#dc2626" : "#9ca3af") + ";'>" + u.overdue + "</td>";
            bh += "<td>" + u.inProgress + "</td>";
            bh += "<td><div class='rpt-bar'><div class='rpt-bar-fill' style='width:" + pct + "%;'></div></div>";
            bh += "<span style='font-size:11px;color:#64748b;margin-left:4px;'>" + pct + "%</span></td>";
            bh += "</tr>";
        }
        if (users.length === 0) {
            var nc = showCtx ? 7 : 6;
            bh = "<tr><td colspan='" + nc + "' style='text-align:center;color:#9ca3af;padding:20px;'>Нет данных</td></tr>";
        }
        document.getElementById("reportBody").innerHTML = bh;
    }

    function rptEscHtml(s) {
        if (!s) return "";
        return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
    }

    // ── Шаг 07: Карточка задачи ──────────────────────────────────────────

    var _tcmData = null;
    var _tcmRevsLoaded = false;
    var _tcmStNames = ["Надо сделать", "В работе", "Ожидание", "Готово"];
    var _tcmStClasses = ["tcm-s0", "tcm-s1", "tcm-s2", "tcm-s3"];

    window.tcmOpen = function (nameKey, targetTab) {
        var overlay = document.getElementById("tcmOverlay");
        if (!overlay) return;

        _tcmRevsLoaded = false;
        var rb = document.getElementById("tcm-revs-body");
        if (rb) { rb.innerHTML = "Загрузка..."; }
        var attSearch = document.getElementById("tcm-att-search");
        if (attSearch) attSearch.className = "tcm-att-search";
        tcmHideMsg();

        try {
            var res = window.external.InvokeTemplate("GetTaskDetails", nameKey);
            if (!res || (typeof res === "string" && res.indexOf("ERROR") === 0)) {
                alert("Ошибка загрузки карточки: " + res); return;
            }
            _tcmData = JSON.parse(String(res));
            tcmRender(_tcmData);

            // Подзадачи: грузим отдельным вызовом, чтобы не раздувать GetTaskDetails
            if (typeof tcmSubtasksLoad === "function") tcmSubtasksLoad(nameKey);

            // FIX-05: подвязать обработчик авто-роста к textarea (один раз)
            var taDet = document.getElementById("tcm-details");
            if (taDet && !taDet._tcmAutoSizeBound) {
                taDet._tcmAutoSizeBound = true;
                taDet.oninput = function () { tcmAutoSizeDetails(); };
            }

            // СНАЧАЛА делаем оверлей видимым (чтобы у элементов появилась высота)
            overlay.className = "tcm-overlay visible";

            // ЗАТЕМ переключаем вкладку (иначе scrollHeight в чате будет равен 0)
            tcmSwitchTab(targetTab || 'main');
            
        } catch (e) {
            alert("Ошибка: " + (e.message || e));
        }
    };

    window.tcmClose = function () {
        var overlay = document.getElementById("tcmOverlay");
        if (overlay) overlay.className = "tcm-overlay";
        _tcmData = null;
    };

    function tcmRenderTagSuggestions(available) {
        var cont = document.getElementById("tcm-tag-suggestions");
        if (!cont) return;
        cont.innerHTML = "";
        if (!available || available.length === 0) return;
        for (var i = 0; i < available.length; i++) {
            var span = document.createElement("span");
            span.className = "kb-tag";
            span.textContent = available[i];
            span.onclick = (function (tag) {
                return function () {
                    var inp = document.getElementById("tcm-tags");
                    if (!inp || inp.readOnly) return;
                    var cur = inp.value ? inp.value.replace(/^\s+|\s+$/g, "") : "";
                    // Проверить дубликат
                    var parts = cur ? cur.split(",") : [];
                    for (var j = 0; j < parts.length; j++) {
                        if (parts[j].replace(/^\s+|\s+$/g, "") === tag) return;
                    }
                    inp.value = cur ? cur + ", " + tag : tag;
                };
            })(available[i]);
            cont.appendChild(span);
        }
    }

    function tcmRender(d) {
        var st = d.status || 0;
        var snames = d.statusNames || _tcmStNames;

        // Бейдж статуса
        var badge = document.getElementById("tcm-badge");
        if (badge) {
            badge.className = "tcm-badge " + (_tcmStClasses[st] || "tcm-s0");
            badge.innerHTML = tcmEsc(snames[st] || "");
        }

        document.getElementById("tcm-key").value = d.nameKey || "";
        document.getElementById("tcm-title").value = d.title || "";
        // Исполнитель — select с подчинёнными (если canFullEdit и есть subordinates)
        var selAsg = document.getElementById("tcm-assignee");
        kbClearSel(selAsg);
        var subs = d.subordinates || [];
        if (d.canFullEdit && subs.length > 0) {
            selAsg.disabled = false;
            for (var si = 0; si < subs.length; si++) {
                var oa = document.createElement("option");
                oa.value = subs[si].key; oa.text = subs[si].name;
                if (subs[si].key === (d.assigneeKey || "")) oa.selected = true;
                selAsg.appendChild(oa);
            }
        } else {
            // Только текущий исполнитель, disabled
            var oa2 = document.createElement("option");
            oa2.value = d.assigneeKey || ""; oa2.text = d.assignee || "";
            oa2.selected = true;
            selAsg.appendChild(oa2);
            selAsg.disabled = true;
        }
        document.getElementById("tcm-duedate").value = d.dueDate || "";
        document.getElementById("tcm-details").value = d.details || "";
        tcmAutoSizeDetails();   // FIX-05

        // Просроченность
        var overdueEl = document.getElementById("tcm-overdue");
        if (overdueEl) {
            if (d.isOverdue) {
                overdueEl.innerHTML = "Просрочено (до " + tcmEsc(d.dueDate || "") + ")";
                overdueEl.style.display = "";
            } else {
                overdueEl.innerHTML = "";
                overdueEl.style.display = "none";
            }
        }

        // Теги
        var tagsEl = document.getElementById("tcm-tags");
        if (tagsEl) tagsEl.value = d.tags || "";
        tcmRenderTagSuggestions(d.availableTags || []);

        // Статус select
        var selSt = document.getElementById("tcm-status");
        kbClearSel(selSt);
        for (var i = 0; i < snames.length; i++) {
            var os = document.createElement("option");
            os.value = String(i); os.text = snames[i];
            if (i === st) os.selected = true;
            selSt.appendChild(os);
        }

        // Приоритет select
        var selPr = document.getElementById("tcm-priority");
        kbClearSel(selPr);
        var prios = d.priorities || [];
        for (var p = 0; p < prios.length; p++) {
            var op = document.createElement("option");
            op.value = prios[p].key; op.text = prios[p].name;
            if (prios[p].key === d.priorityKey) op.selected = true;
            selPr.appendChild(op);
        }

        // Step11: urgent header + select подсветка
        var header = document.querySelector(".tcm-header");
        if (header) {
            if (d.priorityKey === "Urgent" || d.priorityKey === "urgent") {
                if (header.className.indexOf("tcm-header-urgent") < 0)
                    header.className += " tcm-header-urgent";
            } else {
                header.className = header.className.replace(/\btcm-header-urgent\b/g, "");
            }
        }
        if (selPr) {
            if (d.priorityKey === "Urgent" || d.priorityKey === "urgent") {
                if (selPr.className.indexOf("kb-sel-urgent") < 0)
                    selPr.className += " kb-sel-urgent";
            }
            selPr.onchange = function () {
                if (selPr.value === "Urgent" || selPr.value === "urgent") {
                    selPr.className = (selPr.className || "") + " kb-sel-urgent";
                } else {
                    selPr.className = (selPr.className || "").replace(/\bkb-sel-urgent\b/g, "");
                }
            };
        }

        // Метаданные
        var meta = "";
        if (d.createdAt) meta += "<span><b>Создано:</b> " + tcmEsc(d.createdAt) + "</span>";
        if (d.completedAt) meta += "<span><b>Выполнено:</b> " + tcmEsc(d.completedAt) + "</span>";
        if (d.creatorName && !d.isOwner) meta += "<span><b>Поручил:</b> " + tcmEsc(d.creatorName) + "</span>";
        document.getElementById("tcm-meta").innerHTML = meta;

        // Ограничение редактирования чужих задач
        var canFull = d.canFullEdit !== false;
        document.getElementById("tcm-title").readOnly = !canFull;
        document.getElementById("tcm-details").readOnly = !canFull;
        var tagsRO = document.getElementById("tcm-tags");
        if (tagsRO) tagsRO.readOnly = !canFull;
        document.getElementById("tcm-duedate").readOnly = !canFull;
        document.getElementById("tcm-priority").disabled = !canFull;
        var calBtn = document.getElementById("tcm-cal-btn");
        if (calBtn) calBtn.style.display = canFull ? "" : "none";

        // Кнопка удаления — только для создателя
        var btnDel = document.getElementById("tcm-btn-del");
        if (btnDel) btnDel.style.display = d.isOwner ? "" : "none";

        // Вложения
        tcmRenderAttachments(d);
        tcmLoadComments(d);
    }

    // ═══════════════════════════════════════════════════════════════
    // Step 09: Вложения — JS-функции
    // ═══════════════════════════════════════════════════════════════

    var _tcmAttSearchTimer = null;
    var _crtPendingAttachments = []; // [{key, name, tmpl}] — ожидают прикрепления после создания задачи

    // ═══════════════════════════════════════════════════════════════
    // Вложения в панели создания задачи
    // ═══════════════════════════════════════════════════════════════

    window.kbCrtPickAtt = function () {
        try {
            var res = window.external.InvokeTemplate("PickObjects", "");
            var s = String(res || "");
            if (!s || s === "CANCELLED") return;
            if (s.indexOf("ERROR") === 0) { alert("Ошибка: " + s); return; }
            var items;
            try { items = JSON.parse(s); } catch (e) { alert("Ошибка разбора ответа сервера"); return; }
            if (!items || !items.length) return;
            var added = 0;
            for (var i = 0; i < items.length; i++) {
                var it = items[i];
                if (!it.key) continue;
                var dup = false;
                for (var j = 0; j < _crtPendingAttachments.length; j++) {
                    if (_crtPendingAttachments[j].key === it.key) { dup = true; break; }
                }
                if (dup) continue;
                if (_crtPendingAttachments.length >= 20) break;
                _crtPendingAttachments.push({ key: it.key, name: it.name || it.key, tmpl: it.tmpl || "", type: "object" });
                added++;
            }
            if (added > 0) kbCrtRenderAtts();
        } catch (e) {
            alert("Ошибка открытия диалога: " + (e.message || e));
        }
    };

    window.kbCrtPickCont = function () {
        try {
            var res = window.external.InvokeTemplate("PickContainers", "");
            var s = String(res || "");
            if (!s || s === "CANCELLED") return;
            if (s.indexOf("ERROR") === 0) { alert("Ошибка: " + s); return; }
            var items;
            try { items = JSON.parse(s); } catch (e) { alert("Ошибка разбора ответа сервера"); return; }
            if (!items || !items.length) return;
            var added = 0;
            for (var i = 0; i < items.length; i++) {
                var it = items[i];
                if (!it.key) continue;
                var dup = false;
                for (var j = 0; j < _crtPendingAttachments.length; j++) {
                    if (_crtPendingAttachments[j].key === it.key && _crtPendingAttachments[j].type === "container") { dup = true; break; }
                }
                if (dup) continue;
                if (_crtPendingAttachments.length >= 20) break;
                _crtPendingAttachments.push({ key: it.key, name: it.name || it.key, tmpl: "", type: "container" });
                added++;
            }
            if (added > 0) kbCrtRenderAtts();
        } catch (e) {
            alert("Ошибка открытия диалога: " + (e.message || e));
        }
    };

    function kbCrtRenderAtts() {
        var chips = document.getElementById("kb-crt-att-chips");
        if (!chips) return;
        var html = "";
        for (var i = 0; i < _crtPendingAttachments.length; i++) {
            var it = _crtPendingAttachments[i];
            var icon = (it.type === "container") ? "&#128193; " : "&#128196; ";
            var bg = (it.type === "container") ? "#e8f5e9" : "#e8f0fe";
            var bd = (it.type === "container") ? "#c5e1c5" : "#c5d8fd";
            html += '<span style="display:inline-flex;align-items:center;background:' + bg + ';border:1px solid ' + bd + ';' +
                'border-radius:3px;padding:1px 6px;font-size:11px;margin-left:3px;vertical-align:middle;">' +
                icon + tcmAttEsc(it.name) +
                ' <button type="button" onclick="kbCrtRemoveAtt(' + i + ')" title="Убрать"' +
                ' style="border:none;background:none;cursor:pointer;padding:0 0 0 4px;font-size:13px;color:#888;line-height:1;">&times;</button>' +
                '</span>';
        }
        chips.innerHTML = html;
    }

    window.kbCrtRemoveAtt = function (idx) {
        _crtPendingAttachments.splice(idx, 1);
        kbCrtRenderAtts();
    };

    function tcmRenderAttachments(d) {
        var list = document.getElementById("tcm-att-list");
        var cnt = document.getElementById("tcm-att-count");
        if (!list) return;

        list.innerHTML = "";

        var nameKey = (d && d.nameKey) ? d.nameKey : "";
        if (!nameKey) { if (cnt) cnt.innerHTML = ""; return; }

        try {
            var res = window.external.InvokeTemplate("GetAttachments", nameKey);
            var items = JSON.parse(String(res || "[]"));

            if (cnt) cnt.innerHTML = items.length > 0 ? "(" + items.length + ")" : "";

            if (items.length === 0) {
                list.innerHTML = '<div class="tcm-att-empty">Нет вложений</div>';
                return;
            }

            for (var i = 0; i < items.length; i++) {
                var it = items[i];
                var isContainer = (it.type === "container");
                var icon = isContainer ? "&#128193;" : "&#128196;";
                var div = document.createElement("div");
                div.className = "tcm-att-item";
                div.innerHTML =
                    '<span class="tcm-att-icon">' + icon + '</span>' +
                    '<a href="#" class="tcm-att-name" onclick="tcmAttOpen(\'' + tcmAttEsc(it.key) + '\',\'' + tcmAttEsc(it.type || 'object') + '\'); return false;" title="' + tcmAttEsc(it.name) + '">' + tcmAttEsc(it.name) + '</a>' +
                    '<span class="tcm-att-type">(' + tcmAttEsc(it.tmpl || (isContainer ? "Папка/проект" : "")) + ')</span>' +
                    '<span class="tcm-att-date">' + tcmAttEsc(it.date || "") + '</span>' +
                    '<button class="tcm-att-del" onclick="tcmAttRemove(\'' + tcmAttEsc(it.key) + '\',\'' + tcmAttEsc(it.type || 'object') + '\'); return false;" title="Открепить">&times;</button>';
                list.appendChild(div);
            }
        } catch (e) {
            list.innerHTML = '<div class="tcm-att-empty">Ошибка загрузки вложений</div>';
            if (cnt) cnt.innerHTML = "";
        }
    }

    window.tcmAttOpen = function (objKey, itemType) {
        if (!_tcmData || !_tcmData.nameKey) return;
        var cmd = (itemType === "container") ? "OpenContainer" : "OpenObject";
        try {
            window.external.InvokeTemplate(cmd, _tcmData.nameKey + "|" + objKey);
        } catch (e) {
            alert("Не удалось открыть объект: " + (e.message || e));
        }
    };

    // Инвалидирует кэш истории изменений и перезагружает её, если вкладка открыта
    function tcmInvalidateRevsCache() {
        _tcmRevsLoaded = false;
        var histTab = document.getElementById("tcm-tab-hist");
        if (histTab && histTab.className.indexOf("active") >= 0) {
            _tcmRevsLoaded = true;
            tcmLoadRevs();
        }
    }

    window.tcmAttRemove = function (objKey, itemType) {
        if (!_tcmData || !_tcmData.nameKey) return;
        if (!confirm("Открепить этот объект от задачи?")) return;
        var cmd = (itemType === "container") ? "RemoveContainer" : "RemoveAttachment";
        try {
            var res = window.external.InvokeTemplate(cmd, _tcmData.nameKey + "|" + objKey);
            if (String(res) === "OK") {
                tcmRenderAttachments(_tcmData);
                tcmInvalidateRevsCache();
            } else {
                alert("Ошибка: " + res);
            }
        } catch (e) {
            alert("Ошибка: " + (e.message || e));
        }
    };

    // Открывает нативный PLM-диалог и прикрепляет объект одним вызовом (без повторного поиска)
    window.tcmAttPickNative = function () {
        if (!_tcmData || !_tcmData.nameKey) return;
        try {
            var res = window.external.InvokeTemplate("PickAndAttach", _tcmData.nameKey);
            var s = String(res || "");
            if (!s || s === "CANCELLED") return;
            if (s === "ERROR:AlreadyAttached") { alert("Этот объект уже прикреплён к задаче"); return; }
            if (s === "ERROR:LimitReached") { alert("Достигнут лимит вложений (20)"); return; }
            if (s.indexOf("ERROR") === 0) { alert("Ошибка: " + s); return; }
            tcmRenderAttachments(_tcmData);
            tcmInvalidateRevsCache();
        } catch (e) {
            alert("Ошибка открытия диалога: " + (e.message || e));
        }
    };

    // Открывает нативный PLM-диалог выбора папок/проектов и прикрепляет их к задаче
    window.tcmAttPickNativeCont = function () {
        if (!_tcmData || !_tcmData.nameKey) return;
        try {
            var res = window.external.InvokeTemplate("PickAndAttachContainer", _tcmData.nameKey);
            var s = String(res || "");
            if (!s || s === "CANCELLED") return;
            if (s.indexOf("ERROR") === 0) { alert("Ошибка: " + s); return; }
            tcmRenderAttachments(_tcmData);
            tcmInvalidateRevsCache();
        } catch (e) {
            alert("Ошибка открытия диалога: " + (e.message || e));
        }
    };

    window.tcmAttToggleSearch = function () {
        var panel = document.getElementById("tcm-att-search");
        if (!panel) return;
        if (panel.className.indexOf("open") >= 0) {
            panel.className = "tcm-att-search";
        } else {
            panel.className = "tcm-att-search open";
            var inp = document.getElementById("tcm-att-sinp");
            if (inp) { inp.value = ""; inp.focus(); }
            var res = document.getElementById("tcm-att-results");
            if (res) res.innerHTML = "";
        }
    };

    window.tcmAttSearchKeyup = function () {
        if (_tcmAttSearchTimer) { clearTimeout(_tcmAttSearchTimer); _tcmAttSearchTimer = null; }
        _tcmAttSearchTimer = setTimeout(function () { tcmAttDoSearch(); }, 400);
    };

    function tcmAttDoSearch() {
        var inp = document.getElementById("tcm-att-sinp");
        var res = document.getElementById("tcm-att-results");
        if (!inp || !res) return;

        var query = inp.value.replace(/^\s+|\s+$/g, "");
        if (query.length < 2) { res.innerHTML = ""; return; }

        res.innerHTML = '<div class="tcm-att-loading">Поиск...</div>';

        try {
            var raw = window.external.InvokeTemplate("SearchObjects", query);
            var items = JSON.parse(String(raw || "[]"));

            if (items.length === 0) {
                res.innerHTML = '<div class="tcm-att-loading">Ничего не найдено</div>';
                return;
            }

            var html = "";
            for (var i = 0; i < items.length; i++) {
                var it = items[i];
                html += '<div class="tcm-att-res-item">' +
                    '<span class="tcm-att-res-name" title="' + tcmAttEsc(it.name) + '">' + tcmAttEsc(it.name) + '</span>' +
                    '<span class="tcm-att-res-tmpl">' + tcmAttEsc(it.tmpl || "") + '</span>' +
                    '<button class="tcm-att-res-add" onclick="tcmAttAdd(\'' + tcmAttEsc(it.key) + '\'); return false;">+</button>' +
                    '</div>';
            }
            res.innerHTML = html;
        } catch (e) {
            res.innerHTML = '<div class="tcm-att-loading">Ошибка поиска</div>';
        }
    }

    window.tcmAttAdd = function (objKey) {
        if (!_tcmData || !_tcmData.nameKey) return;

        try {
            var res = window.external.InvokeTemplate("AddAttachment", _tcmData.nameKey + "|" + objKey);
            var s = String(res);
            if (s === "OK") {
                tcmRenderAttachments(_tcmData);
                tcmInvalidateRevsCache();
                var panel = document.getElementById("tcm-att-search");
                if (panel) panel.className = "tcm-att-search";
            } else if (s === "ERROR:AlreadyAttached") {
                alert("Этот объект уже прикреплён к задаче");
            } else if (s === "ERROR:LimitReached") {
                alert("Достигнут лимит вложений (20)");
            } else {
                alert("Ошибка: " + s);
            }
        } catch (e) {
            alert("Ошибка: " + (e.message || e));
        }
    };

    function tcmAttEsc(s) {
        if (!s) return "";
        return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;")
            .replace(/>/g, "&gt;").replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    window.tcmOnStatusChange = function () {
        var st = parseInt(document.getElementById("tcm-status").value, 10) || 0;
        var snames = (_tcmData && _tcmData.statusNames) || _tcmStNames;
        var badge = document.getElementById("tcm-badge");
        if (badge) {
            badge.className = "tcm-badge " + (_tcmStClasses[st] || "tcm-s0");
            badge.innerHTML = tcmEsc(snames[st] || "");
        }
    };

    // tcmSave(opts):
    //   opts.closeAfter (default true)  — закрыть карточку после успешного сохранения
    //   opts.refreshAfter (default true) — обновить доску после успешного сохранения
    // Без аргументов (кнопка «Сохранить», Ctrl+S, tcmDelete) — закрыть и обновить.
    // Enter из tcmHotkey передаёт {closeAfter:false, refreshAfter:false} —
    // классическое «сохранить, продолжить редактирование».
    window.tcmSave = function (opts) {
        opts = opts || {};
        var closeAfter   = opts.closeAfter   !== false;
        var refreshAfter = opts.refreshAfter !== false;

        var nameKey = document.getElementById("tcm-key").value;
        var title = document.getElementById("tcm-title").value;
        var status = document.getElementById("tcm-status").value;
        var prio = document.getElementById("tcm-priority").value;
        var dueDate = document.getElementById("tcm-duedate").value;
        var details = document.getElementById("tcm-details").value;
        var tags = document.getElementById("tcm-tags") ? document.getElementById("tcm-tags").value : "";
        var selAsg = document.getElementById("tcm-assignee");
        var assigneeKey = (selAsg && !selAsg.disabled) ? selAsg.value : "";

        if (!title || !title.replace(/\s/g, "")) {
            tcmShowMsg("err", "Введите название задачи"); return;
        }

        var param = nameKey + "|" + title + "|" + status + "|" + prio + "|" + dueDate + "|" + tags + "|" + assigneeKey + "|" + details;
        try {
            var res = window.external.InvokeTemplate("SaveTask", param);
            if (!res || (typeof res === "string" && res.indexOf("ERROR") === 0)) {
                tcmShowMsg("err", String(res)); return;
            }
            // Обновляем бейдж и сохранённые данные
            var st = parseInt(status, 10) || 0;
            var snames = (_tcmData && _tcmData.statusNames) || _tcmStNames;
            var badge = document.getElementById("tcm-badge");
            if (badge) {
                badge.className = "tcm-badge " + (_tcmStClasses[st] || "tcm-s0");
                badge.innerHTML = tcmEsc(snames[st] || "");
            }
            if (_tcmData) { _tcmData.status = st; }

            if (closeAfter) {
                tcmClose();
            } else {
                tcmShowMsg("ok", "");
            }
            if (refreshAfter && typeof kbRefreshBoard === "function") {
                kbRefreshBoard();
            }
        } catch (e) {
            tcmShowMsg("err", "Ошибка: " + (e.message || e));
        }
    };

    window.tcmDelete = function () {
        if (!confirm("Удалить задачу безвозвратно?")) return;
        var nameKey = document.getElementById("tcm-key").value;
        try {
            var res = window.external.InvokeTemplate("DeleteTask", nameKey);
            if (!res || (typeof res === "string" && res.indexOf("ERROR") === 0)) {
                tcmShowMsg("err", String(res)); return;
            }
            tcmClose();
            try { window.external.InvokeTemplate("RefreshBoard", ""); } catch (e2) { }
        } catch (e) {
            tcmShowMsg("err", "Ошибка: " + (e.message || e));
        }
    };

    window.tcmToggleRevs = function () {
        var body = document.getElementById("tcm-revs-body");
        if (!body) return;
        if (body.className.indexOf("visible") >= 0) {
            body.className = "tcm-revs-body"; return;
        }
        body.className = "tcm-revs-body visible";
        if (!_tcmRevsLoaded) {
            _tcmRevsLoaded = true;
            tcmLoadRevs();
        }
    };

    function tcmLoadRevs() {
        var nameKey = document.getElementById("tcm-key").value;
        var body = document.getElementById("tcm-revs-body");
        try {
            var res = window.external.InvokeTemplate("GetTaskHistory", nameKey);
            var data = res ? JSON.parse(String(res)) : [];
            if (data.length === 0) { body.innerHTML = "Нет изменений"; return; }
            var h = "";
            for (var i = data.length - 1; i >= 0; i--) {
                var entry = data[i];
                h += "<div style='margin-bottom:8px;'>";
                var hdr = tcmEsc(entry.d || "");
                if (entry.a) hdr += " &mdash; " + tcmEsc(entry.a);
                h += "<div style='font-weight:600;color:#546e7a;'>" + hdr + "</div>";
                var cc = entry.c || [];
                for (var j = 0; j < cc.length; j++) {
                    var old = cc[j].o || ""; var nw = cc[j].n || "";
                    h += "<div style='padding-left:12px;color:#78909c;'>&middot; " + tcmEsc(cc[j].f || "");
                    if (old && nw) h += ": " + tcmEsc(old) + " &rarr; " + tcmEsc(nw);
                    else if (nw) h += ": " + tcmEsc(nw);
                    h += "</div>";
                }
                h += "</div>";
            }
            body.innerHTML = h;
        } catch (e) {
            body.innerHTML = "Ошибка загрузки";
        }
    }

    function tcmShowMsg(type, text) {
        tcmHideMsg();
        if (type === "ok") {
            var ok = document.getElementById("tcm-msg-ok");
            if (ok) { ok.style.display = "block"; setTimeout(function () { ok.style.display = "none"; }, 2500); }
        } else {
            var err = document.getElementById("tcm-msg-err");
            if (err) { err.innerHTML = tcmEsc(text); err.style.display = "block"; }
        }
    }

    function tcmHideMsg() {
        var ok = document.getElementById("tcm-msg-ok");
        var err = document.getElementById("tcm-msg-err");
        if (ok) ok.style.display = "none";
        if (err) err.style.display = "none";
    }

    // ── Авто-рост textarea «Описание» по содержимому ─────────────────
    function tcmAutoSizeDetails() {
        var ta = document.getElementById("tcm-details");
        if (!ta) return;
        ta.style.height = "auto";
        var sh = ta.scrollHeight;
        if (!sh || sh < 60) sh = 60;
        ta.style.height = sh + "px";
    }
    window.tcmAutoSizeDetails = tcmAutoSizeDetails;

    function tcmEsc(s) {
        if (!s) return "";
        return String(s).replace(/&/g, "&").replace(/</g, "<").replace(/>/g, ">");
    }

    // ── Динамическая обрезка описания карточек по ширине колонки ─────────────
    function truncateCardDescs() {
        var col = document.querySelector(".kb-col-body");
        var colW = col ? col.offsetWidth : 160;
        // ~7px на символ, 2 строки, зажато в разумные границы
        var maxChars = Math.max(40, Math.min(Math.floor(colW / 7) * 2, 400));
        var cards = document.querySelectorAll(".kb-card-details");
        for (var i = 0; i < cards.length; i++) {
            var el = cards[i];
            var full = el.getAttribute("data-full");
            if (!full) continue;
            if (full.length <= maxChars) {
                el.textContent = full;
            } else {
                var cut = full.lastIndexOf(" ", maxChars - 1);
                el.textContent = (cut > 20 ? full.substring(0, cut) : full.substring(0, maxChars)) + "\u2026";
            }
        }
    }

    window.addEventListener
        ? window.addEventListener("resize", truncateCardDescs)
        : window.attachEvent("onresize", truncateCardDescs);
    truncateCardDescs();

    // Подгонка высоты доски под реальный viewport
    // CSS calc(100vh - 90px) даёт начальную высоту; JS уточняет после отрисовки
    function kbFitBoard() {
        var board = document.getElementById("kb-board");
        if (!board) return;
        var rect = board.getBoundingClientRect();
        var vh = window.innerHeight || document.documentElement.clientHeight || 0;
        if (vh < 100 || rect.top <= 0) return; // DOM ещё не готов — не трогаем
        var h = vh - rect.top - 2;
        if (h < 300) h = 300;
        board.style.height = h + "px";
    }
    // Отложенный вызов — DOM должен успеть отрисоваться
    setTimeout(kbFitBoard, 150);
    setTimeout(kbFitBoard, 500);
    if (window.addEventListener) {
        window.addEventListener("resize", kbFitBoard);
    } else if (window.attachEvent) {
        window.attachEvent("onresize", kbFitBoard);
    }


    // ── Справка ──────────────────────────────────────────────────────────
    window.openHelp = function () {
        var overlay = document.getElementById("helpOverlay");
        if (overlay) overlay.className = "help-overlay visible";
    };

    window.closeHelp = function () {
        var overlay = document.getElementById("helpOverlay");
        if (overlay) overlay.className = "help-overlay";
    };

    window.openWhatsNew = function () {
        var overlay = document.getElementById("whatsNewOverlay");
        if (overlay) overlay.className = "help-overlay visible";
    };

    window.closeWhatsNew = function () {
        var overlay = document.getElementById("whatsNewOverlay");
        if (overlay) overlay.className = "help-overlay";
    };

    window.tcmSwitchTab = function (tabId) {
        var contents = document.querySelectorAll('.tcm-tab-content');
        for (var i = 0; i < contents.length; i++) contents[i].className = 'tcm-tab-content';
        var tabs = document.querySelectorAll('.tcm-tab');
        for (var i = 0; i < tabs.length; i++) tabs[i].className = 'tcm-tab';

        var tContent = document.getElementById('tcm-tab-' + tabId);
        var tBtn = document.getElementById('tab-btn-' + tabId);
        if (tContent) tContent.className = 'tcm-tab-content active';
        if (tBtn) tBtn.className = 'tcm-tab active';

        if (tabId === 'chat') {
            var list = document.getElementById("tcm-chat-list");
            if (list) list.scrollTop = list.scrollHeight;
        }
        if (tabId === 'hist' && !_tcmRevsLoaded) {
            _tcmRevsLoaded = true;
            tcmLoadRevs();
        }
        // FIX-05: при возврате на «Основное» пересчитываем высоту textarea
        if (tabId === "main") {
            setTimeout(function () { tcmAutoSizeDetails(); }, 0);
        }
    };

    // Вызов после того, как все функции определены
    kbInitHierarchy();

    // Инициализация фильтра периода — отдельно от kbInitHierarchy,
    // потому что та делает ранний return для роли "regular".
    if (typeof kbPeriodFilterInit === "function") kbPeriodFilterInit();

    // Авто-открытие карточки из уведомлений
    var autoOpenEl = document.getElementById("kb-auto-open-task");
    if (autoOpenEl && autoOpenEl.value) {
        var rawVal = autoOpenEl.value.replace(/^\s+|\s+$/g, "");
        var parts = rawVal.split('|');
        var targetKey = parts[0];
        var targetTab = parts.length > 1 ? parts[1] : 'main';
        
        try { window.external.InvokeTemplate("ClearAutoOpen", ""); } catch (e) {}
        setTimeout(function () {
            if (typeof tcmOpen === "function") {
                tcmOpen(targetKey, targetTab);
            }
        }, 500);
    }

    // ── Hotkeys: глобальные горячие клавиши доски ───────────────────────
    function kbIsTypingTarget(target) {
        if (!target || !target.tagName) return false;
        var tag = String(target.tagName).toLowerCase();
        if (tag === "input" || tag === "textarea" || tag === "select") return true;
        if (target.isContentEditable) return true;
        return false;
    }

    function kbIsTextareaTarget(target) {
        if (!target || !target.tagName) return false;
        return String(target.tagName).toLowerCase() === "textarea";
    }

    function kbIsOverlayVisible(id) {
        var el = document.getElementById(id);
        return !!(el && el.className && el.className.indexOf("visible") !== -1);
    }

    function kbIsCreatePanelOpen() {
        var el = document.getElementById("kb-create-panel");
        return !!(el && el.style.display && el.style.display !== "none");
    }

    function kbHotkeyEscape() {
        if (kbIsOverlayVisible("whatsNewOverlay")) { closeWhatsNew();  return; }
        if (kbIsOverlayVisible("helpOverlay"))     { closeHelp();      return; }
        if (kbIsOverlayVisible("tcmOverlay"))      { tcmClose();       return; }
        if (kbIsOverlayVisible("reportOverlay"))   { closeReport();    return; }
        if (kbIsCreatePanelOpen())                 { hideCreateTask(); return; }
    }

    function kbOnGlobalKeydown(e) {
        if (!e) e = window.event;
        var target = e.target || e.srcElement;
        var code   = e.keyCode || e.which;

        if (code === 116) {
            if (e.preventDefault) e.preventDefault();
            if (e.stopPropagation) e.stopPropagation();
            e.keyCode = 0;
            e.returnValue = false;
            if (typeof kbRefreshBoard === "function") kbRefreshBoard();
            return false;
        }

        if (code === 112) {
            if (e.preventDefault) e.preventDefault();
            e.keyCode = 0;
            e.returnValue = false;
            if (!kbIsTypingTarget(target) && !kbIsOverlayVisible("reportOverlay") && !kbIsOverlayVisible("whatsNewOverlay") && !kbIsOverlayVisible("tcmOverlay") && !kbIsCreatePanelOpen()) {
                if (kbIsOverlayVisible("helpOverlay")) { closeHelp(); }
                else { openHelp(); }
            }
            return false;
        }

        if (code === 27) {
            if (kbIsTypingTarget(target)) {
                try { target.blur(); } catch (er) {}
            } else {
                kbHotkeyEscape();
                if (e.preventDefault) e.preventDefault();
            }
            return;
        }

        if (code === 13 && !e.shiftKey && !e.altKey && !e.metaKey && !e.ctrlKey) {
            if (kbIsOverlayVisible("tcmOverlay") && !kbIsTextareaTarget(target)) {
                if (target && target.id === "tcm-chat-text") return;
                // Enter — «сохранить, не закрывая, без рефреша доски» (быстрая правка нескольких полей)
                if (typeof tcmSave === "function") tcmSave({ closeAfter: false, refreshAfter: false });
                if (e.preventDefault) e.preventDefault();
                return;
            }
        }

        if (code === 83 && e.ctrlKey && !e.shiftKey && !e.altKey) {
            if (kbIsOverlayVisible("tcmOverlay")) {
                if (e.preventDefault) e.preventDefault();
                // Ctrl+S — save+close+refresh (поведение по умолчанию tcmSave)
                if (typeof tcmSave === "function") tcmSave();
                return false;
            }
        }

        if (code === 78 && !e.ctrlKey && !e.shiftKey && !e.altKey && !e.metaKey) {
            if (!kbIsTypingTarget(target) && !kbIsOverlayVisible("helpOverlay") && !kbIsOverlayVisible("whatsNewOverlay") && !kbIsOverlayVisible("reportOverlay") && !kbIsOverlayVisible("tcmOverlay") && !kbIsCreatePanelOpen()) {
                if (typeof showCreateTask === "function") showCreateTask();
                if (e.preventDefault) e.preventDefault();
                return false;
            }
        }
    }

    if (document.addEventListener) {
        document.addEventListener("keydown", kbOnGlobalKeydown, false);
    } else if (document.attachEvent) {
        document.attachEvent("onkeydown", kbOnGlobalKeydown);
    }

