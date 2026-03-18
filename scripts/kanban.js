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
            return document.getElementById(_calTargetId === "tcm-duedate" ? "kb-cal-tcm" : "kb-cal");
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
            // Закрываем оба попапа если клик вне них
            var cals = [document.getElementById("kb-cal"), document.getElementById("kb-cal-tcm")];
            for (var ci = 0; ci < cals.length; ci++) {
                var cal = cals[ci];
                if (!cal || cal.style.display !== "block") continue;
                var node = tgt;
                var inside = false;
                while (node) {
                    if (node === cal) { inside = true; break; }
                    if (node.id === "kb-new-duedate" || node.id === "tcm-duedate") { inside = true; break; }
                    if (node.className && node.className.indexOf("kb-date-btn") !== -1) { inside = true; break; }
                    node = node.parentNode;
                }
                if (!inside) cal.style.display = "none";
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
            document.getElementById("kb-new-status").value = "0";
            document.getElementById("kb-new-priority").value = "medium";
            var prSel = document.getElementById("kb-new-priority");
            if (prSel) prSel.className = (prSel.className || "").replace(/\bkb-sel-urgent\b/g, "");
            var crtUserEl = document.getElementById("kb-crt-user");
            if (crtUserEl) crtUserEl.value = "";
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

        window.doCreateTask = function () {
            var title = document.getElementById("kb-new-title").value;
            var status = document.getElementById("kb-new-status").value;
            var priority = document.getElementById("kb-new-priority").value;
            var dueDate = document.getElementById("kb-new-duedate").value;
            var details = document.getElementById("kb-new-details").value;

            title = title ? title.replace(/^\s+|\s+$/g, "") : "";
            dueDate = dueDate ? dueDate.replace(/^\s+|\s+$/g, "") : "";
            details = details ? details.replace(/^\s+|\s+$/g, "") : "";

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
                var grpParam = title + "|" + status + "|" + priority + "|" + dueDate + "|" + details + "|" + keys.join(",");
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

            var param = title + "|" + status + "|" + priority + "|" + dueDate + "|" + details + "|" + assignee;
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

    function tcmRenderComment(c) {
        var div = document.createElement("div");
        div.className = "tcm-msg-item" + (c.isMine ? " tcm-msg-mine" : "");

        var delBtn = "";
        if (c.isMine) {
            delBtn = '<button class="tcm-msg-del-btn" onclick="tcmDeleteComment(' + c.index + '); return false;" title="Удалить">&times;</button>';
        }

        div.innerHTML =
            '<div class="tcm-msg-header">' +
            '<span class="tcm-msg-avatar" title="' + tcmChatEsc(c.authorName) + '">' + tcmChatEsc(c.initials || "") + '</span>' +
            '<span class="tcm-msg-author">' + tcmChatEsc(c.authorName) + '</span>' +
            '<span class="tcm-msg-time">' + tcmChatEsc(c.date) + '</span>' +
            '</div>' +
            '<div class="tcm-msg-text">' + tcmChatEsc(c.text) + '</div>' +
            delBtn;

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
            } else if (s === "ERROR:NotOwner") {
                alert("Вы можете удалять только свои комментарии");
            } else {
                alert("Ошибка: " + s);
            }
        } catch (e) {
            alert("Ошибка: " + (e.message || e));
        }
    };

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

    // ── Ролевая иерархия: каскадные селекторы (шаг 05) ──────────────────
    // Роли: regular → панель скрыта; headOfSector → [Сотрудник ▼];
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

        kbRestoreViewMode(_kbH.viewMode || "my");
        kbInitCreateForm();
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

    function kbFillUsers(deptKey, sectorKey) {
        var sel = document.getElementById("kb-sel-user");
        if (!sel) return;
        kbClearSel(sel);
        sel.appendChild(kbOpt("", "Все сотрудники"));
        for (var i = 0; i < _kbH.users.length; i++) {
            var u = _kbH.users[i];
            var ctx = u.context || "";
            if (sectorKey && ctx !== sectorKey) continue;
            if (!sectorKey && deptKey && !kbBelongsToDept(ctx, deptKey)) continue;
            sel.appendChild(kbOpt(u.key, u.name));
        }
    }

    window.kbOnDeptChange = function () {
        var deptKey = kbSelVal("kb-sel-dept");
        kbFillSectors(deptKey || null);
        kbFillUsers(deptKey || null, null);
        kbUpdateMyBtn(false);
        kbStyleBtn("kb-btn-all", false);
        kbApplyMode();
    };

    window.kbOnSectorChange = function () {
        var deptKey = kbSelVal("kb-sel-dept");
        var sectorKey = kbSelVal("kb-sel-sector");
        kbFillUsers(deptKey || null, sectorKey || null);
        kbUpdateMyBtn(false);
        kbStyleBtn("kb-btn-all", false);
        kbApplyMode();
    };

    window.kbOnUserChange = function () {
        kbUpdateMyBtn(false);
        kbStyleBtn("kb-btn-all", false);
        kbApplyMode();
    };

    window.kbSetMyMode = function () {
        kbSetSelVal("kb-sel-dept", "");
        kbSetSelVal("kb-sel-sector", "");
        kbSetSelVal("kb-sel-user", "");
        if (_kbH.role === "admin" || _kbH.role === "headOfDept") {
            kbFillSectors(null);
            kbFillUsers(null, null);
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
            else if (_kbH.role === "headOfSector") mode = "sector";
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

        if (mode.indexOf("user:") === 0) {
            var userKey = mode.substring(5);
            // Найти контекст пользователя чтобы правильно заполнить секторный/дивизионный список
            var userCtx = "";
            for (var i = 0; i < _kbH.users.length; i++) {
                if (_kbH.users[i].key === userKey) { userCtx = _kbH.users[i].context || ""; break; }
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
            var selUser = document.getElementById("kb-sel-user");
            if (selUser) selUser.value = userKey;
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
        if (_kbH.role === "admin" || _kbH.role === "headOfDept") {
            kbFillSectors(null);
            kbFillUsers(null, null);
        }
        kbUpdateAllBtn(true);
        var mode = _kbH.role === "admin" ? "all"
            : _kbH.role === "headOfDept" ? "dept"
                : "sector";
        kbSendMode(mode);
    };

    // ── Независимые каскадные селекторы в форме создания задачи ────────────
    // Не привязаны к панели иерархии → не вызывают RefreshBoard.
    function kbInitCreateForm() {
        var row = document.getElementById("kb-crt-assignee-row");
        if (!row) return;
        if (_kbH.role === "regular") { row.style.display = "none"; return; }
        row.style.display = "";

        var deptWrap = document.getElementById("kb-crt-dept-wrap");
        var sectorWrap = document.getElementById("kb-crt-sector-wrap");
        if (deptWrap) deptWrap.style.display = (_kbH.role === "admin") ? "" : "none";
        if (sectorWrap) sectorWrap.style.display =
            (_kbH.role === "admin" || _kbH.role === "headOfDept") ? "" : "none";

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

        // Сбрасываем чекбокс в «Сам себе» и блокируем селекторы
        var selfEl = document.getElementById("kb-crt-self");
        if (selfEl) selfEl.checked = true;
        kbOnSelfChange();
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

    function kbFillCrtUsers(deptKey, sectorKey) {
        var sel = document.getElementById("kb-crt-user");
        if (!sel) return;
        kbClearSel(sel);
        // «Сам себе» — отдельный чекбокс, не опция в дропдауне
        for (var i = 0; i < _kbH.users.length; i++) {
            var u = _kbH.users[i];
            if (u.key === _kbH.myKey) continue;
            var ctx = u.context || "";
            if (sectorKey && ctx !== sectorKey) continue;
            if (!sectorKey && deptKey && !kbBelongsToDept(ctx, deptKey)) continue;
            sel.appendChild(kbOpt(u.key, u.name));
        }
    }

    // Чекбокс «Сам себе»: блокирует/разблокирует каскадные селекторы формы
    window.kbOnSelfChange = function () {
        var selfEl = document.getElementById("kb-crt-self");
        var isSelf = selfEl ? selfEl.checked : true;
        var ids = ["kb-crt-dept", "kb-crt-sector", "kb-crt-user"];
        for (var i = 0; i < ids.length; i++) {
            var el = document.getElementById(ids[i]);
            if (el) el.disabled = isSelf;
        }
    };

    window.kbOnCrtDeptChange = function () {
        var crtDept = document.getElementById("kb-crt-dept");
        var deptKey = crtDept ? (crtDept.value || "") : "";
        kbFillCrtSectors(deptKey || null);
        kbFillCrtUsers(deptKey || null, null);
        // Не вызывает kbApplyMode/kbSendMode — доска не обновляется
    };

    window.kbOnCrtSectorChange = function () {
        var crtDept = document.getElementById("kb-crt-dept");
        var crtSector = document.getElementById("kb-crt-sector");
        var deptKey = crtDept ? (crtDept.value || "") : "";
        var sectorKey = crtSector ? (crtSector.value || "") : "";
        kbFillCrtUsers(deptKey || null, sectorKey || null);
        // Не вызывает kbApplyMode/kbSendMode — доска не обновляется
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
        if (active) kbStyleBtn("kb-btn-all", false);
    }
    function kbUpdateAllBtn(active) {
        kbStyleBtn("kb-btn-all", active);
        if (active) kbStyleBtn("kb-btn-my", false);
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
            if (u.key === _kbH.myKey) continue;
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
            if (u.key === _kbH.myKey) continue;
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

    window.loadReport = function () {
        var period = document.getElementById("reportPeriod").value;
        var scopeWrap = document.getElementById("rpt-scope-wrap");
        var scopeEl = document.getElementById("reportScope");
        var scope = (scopeWrap && scopeWrap.style.display !== "none" && scopeEl)
            ? (scopeEl.value || "") : "";
        try {
            var res = window.external.InvokeTemplate("GetReport", period + "|" + scope);
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

    window.tcmOpen = function (nameKey) {
        var overlay = document.getElementById("tcmOverlay");
        if (!overlay) return;

        _tcmRevsLoaded = false;
        var rb = document.getElementById("tcm-revs-body");
        if (rb) { rb.className = "tcm-revs-body"; rb.innerHTML = "Загрузка..."; }
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
            overlay.className = "tcm-overlay visible";
        } catch (e) {
            alert("Ошибка: " + (e.message || e));
        }
    };

    window.tcmClose = function () {
        var overlay = document.getElementById("tcmOverlay");
        if (overlay) overlay.className = "tcm-overlay";
        _tcmData = null;
    };

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
        document.getElementById("tcm-assignee").value = d.assignee || "";
        document.getElementById("tcm-duedate").value = d.dueDate || "";
        document.getElementById("tcm-details").value = d.details || "";

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
        var body = document.getElementById("tcm-revs-body");
        if (body && body.className.indexOf("visible") >= 0) {
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

    window.tcmSave = function () {
        var nameKey = document.getElementById("tcm-key").value;
        var title = document.getElementById("tcm-title").value;
        var status = document.getElementById("tcm-status").value;
        var prio = document.getElementById("tcm-priority").value;
        var dueDate = document.getElementById("tcm-duedate").value;
        var details = document.getElementById("tcm-details").value;

        if (!title || !title.replace(/\s/g, "")) {
            tcmShowMsg("err", "Введите название задачи"); return;
        }

        var param = nameKey + "|" + title + "|" + status + "|" + prio + "|" + dueDate + "|" + details;
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
            tcmShowMsg("ok", "");
            // Обновляем доску в фоне
            try { window.external.InvokeTemplate("RefreshBoard", ""); } catch (e2) { }
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

    function tcmEsc(s) {
        if (!s) return "";
        return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
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

    // Вызов после того, как все функции определены
    kbInitHierarchy();

