# FULL CONTEXT OF PROJECT

> This document is the **single entry point** for any AI agent working on this project.
> Read it first. It contains everything needed to start safely.

---

## 1. What this project is

A production Kanban board module for **Soyuz-PLM (BIS v3)** — an enterprise PLM system.

Runtime is defined by 5 files in `scripts/`:

| File | Role |
|------|------|
| `SOYUZ_UPLOAD_KanbanScreen_script.cs` | Backend screen logic (C#, ~2400 lines) |
| `SOYUZ_UPLOAD_KanbanTask_script.cs` | `OnBeforeSave` task logic + notifications (~170 lines) |
| `KanbanBoard_HTML.html` | UI template with Liquid bindings |
| `kanban.css` | Styles (loaded as resource on server) |
| `kanban.js` | Client JS logic (loaded as resource on server) |

---

## 2. Mandatory rules for any agent

### 2.1 Communication

- **User communicates in Russian.** Respond in Russian unless asked otherwise.
- Code comments may be in Russian or English (follow existing style in each file).

### 2.2 Safety rules

1. **NEVER push to GitHub** without an explicit user command.
2. **NEVER invent/guess PLM API or method names.** If you don't know the exact API — ask the user or check docs. Compilation errors from non-existent methods are worse than pausing to ask.
3. **After every change:** user validates on the test server before committing.
4. **One small step → one smoke test → one commit.** Don't batch unrelated changes.

### 2.3 Before any code change

- Read the target file in `scripts/` first (don't modify code you haven't read).
- After changes, update docs if the change is significant.

---

## 3. Token-efficient reading order

### 3.1 Fast start (minimum tokens)

1. **This file** — you're reading it now
2. Only the target runtime file in `scripts/`:
   - backend task → `SOYUZ_UPLOAD_KanbanScreen_script.cs`
   - frontend task → `kanban.js` + `KanbanBoard_HTML.html`
   - styles → `kanban.css`
   - notification/save lifecycle → `SOYUZ_UPLOAD_KanbanTask_script.cs`

**This is enough for most tasks.**

### 3.2 Deep context

Add as needed:
3. `docs/00_PROJECT_OVERVIEW.md` — architecture overview
4. `docs/01_KanbanScreen_SERVER_SCRIPT.md` — backend API reference
5. `docs/02_KanbanTask_TEMPLATE_SCRIPT.md` — task lifecycle
6. `docs/03_KANBAN_JS_CLIENT_LOGIC.md` — JS functions reference
7. `docs/04_KANBAN_CSS_STYLES.md` — CSS structure
8. `docs/05_KANBAN_HTML_MARKUP.md` — HTML/Liquid template

### 3.3 Historical context

Development history (iterations, old versions): [kanbanSoyz2](https://github.com/Kest1k/kanbanSoyz2)

---

## 4. Repository map

```
kanbanSoyz_Actual/
├── scripts/
│   ├── SOYUZ_UPLOAD_KanbanScreen_script.cs
│   ├── SOYUZ_UPLOAD_KanbanTask_script.cs
│   ├── KanbanBoard_HTML.html
│   ├── kanban.css
│   └── kanban.js
├── docs/
│   ├── 00_PROJECT_OVERVIEW.md
│   ├── 01_KanbanScreen_SERVER_SCRIPT.md
│   ├── 02_KanbanTask_TEMPLATE_SCRIPT.md
│   ├── 03_KANBAN_JS_CLIENT_LOGIC.md
│   ├── 04_KANBAN_CSS_STYLES.md
│   ├── 05_KANBAN_HTML_MARKUP.md
│   └── HTML/                   ← HTML-версия документации
├── AGENTS.md                   ← quality gate и стандарты работы
├── FULL_CONTEXT_OF_PROJECT.md  ← THIS FILE
├── README.md
└── Kanban Конфигурация-1.0.0.2.pmszcfg  ← экспорт конфигурации PLM
```

---

## 5. Functional scope

| Feature | Status |
|---------|--------|
| 4 columns (Todo, InProgress, Waiting, Done) | done |
| CompletedDate auto-set | done |
| Assignee notifications | done |
| Roles + hierarchy + view modes | done |
| Group task creation | done (no deferred attachments in group mode) |
| Reports (week/month/quarter/all) | done |
| Task modal + permissions + history | done |
| Attachments: InfoObject + DataContainer | done |
| Comments/chat | done |
| Urgent priority | done |
| All_Kanban_Tasks_Folder hidden for non-admin | done (PreCheckOperation) |

---

## 6. Runtime architecture

### 6.1 Backend (`SOYUZ_UPLOAD_KanbanScreen_script.cs`)

- Entry: `Invoke(InfoObject obj, string methodName, string inputParams)`
- `BeforeRender` prepares board columns and role/view-filtered data

| Category | Methods |
|----------|---------|
| Lifecycle | `BeforeRender`, `RefreshBoard` |
| Task CRUD | `CreateTask`, `CreateGroupTask`, `MoveTask`, `DeleteTask` |
| Task modal | `OpenTask`, `GetTaskDetails`, `SaveTask`, `GetTaskHistory` |
| Hierarchy | `GetHierarchyInfo`, `SetViewMode` |
| Reports | `GetReport` |
| Attachments | `AddAttachment`, `RemoveAttachment`, `GetAttachments`, `PickObject`, `PickObjects`, `PickAndAttach`, `PickContainers`, `PickAndAttachContainer`, `AddContainer`, `RemoveContainer`, `OpenObject`, `OpenContainer` |
| Comments | `AddComment`, `GetComments`, `DeleteComment` |

### 6.2 Frontend (`kanban.js` + `KanbanBoard_HTML.html`)

- JS → C# bridge: `window.external.InvokeTemplate('KanbanScreen', methodName, params)`
- Liquid template engine (DotLiquid) for server-side rendering of cards
- All functions called from HTML must be on `window.*`

### 6.3 Task template (`SOYUZ_UPLOAD_KanbanTask_script.cs`)

- `OnBeforeSave(InfoObject obj)` — fires on every task save
- Sends notification when Assignee changes
- Contains `DeclineSurnameGenitive()` for Russian name declension

---

## 7. PLM data model

### 7.1 Container and template

- Task container: `All_Kanban_Tasks_Folder` (template `All_Kanban_Tasks`)
- Task template: `KanbanTask`
- Folder visibility: hidden for non-admin/non-configurator via `PreCheckOperation` (`user.IsAdministrator || user.IsConfigurator`)

### 7.2 Dictionaries

- `Ref_KanbanStatus`: `Todo`, `InProgress`, `Waiting`, `Done`
- `Ref_KanbanPriority`: `High`, `Medium`, `Low`, `Urgent`

### 7.3 Task attributes

| Attribute | Purpose |
|-----------|---------|
| `TaskName` | Title |
| `TaskDetails` | Description |
| `KanbanStatus` | Column status (enumeration) |
| `Priority` | Priority level (enumeration) |
| `DueDate` | Deadline |
| `Assignee` | Assigned user |
| `CompletedDate` | Auto-set when moved to Done |
| `Creator` | Task creator (for permissions) |
| `ChangeLog` | Detailed history of changes |
| `AttachedObjects` | Links to InfoObject attachments |
| `AttachedContainers` | Links to DataContainer attachments |
| `CommentsJSON` | Chat/comments storage |

### 7.4 User attributes

- `Comission` — determines role (admin/headOfDept/headOfSector/regular)
- `Context` — determines organizational unit (department/sector)

---

## 8. Roles and permissions

Role derived from user `Comission`:

| Role | Visibility |
|------|------------|
| `admin` | Full org scope |
| `headOfDept` | Department scope |
| `headOfSector` | Sector scope |
| `regular` | Own tasks only |

View mode stored in `obj.PropertyBag["KbViewMode"]`.
Values: `"my"`, `"all"`, `"dept"`, `"sector"`, `"user:KEY"`, `"group:CTX"`

---

## 9. Confirmed PLM API patterns

> Verified working in Soyuz-PLM (BIS v3). Do not invent alternatives.

```csharp
// Object selection
Service.UI.SelectInfoObject(SelectInfoObjectParams)    → InfoObject
Service.UI.BrowseForInfoObjects(MultiSelectInfoObjectParams) → IEnumerable<InfoObject>
Service.UI.SelectDataContainer(SelectDataContainerParams)  → DataContainer
Service.UI.SelectDataContainers(MultiSelectDataContainerParams) → IEnumerable<DataContainer>
// ⚠ BrowseForDataContainers does NOT exist

// Data modification
var editable = task.GetEditable();
attr.LinkedDataContainers.SafeToSet()
Service.GetDataContainer(uint id)
Service.EnterNewGroupOperation()

// User key (NameKey can be empty string, not null — don't use ??):
string key = string.IsNullOrEmpty(user.NameKey) ? user.AccountId : user.NameKey;
```

---

## 10. Critical coding gotchas

### DotLiquid
- String comparison like `{% if t.someField != "1" %}` is **unreliable** in BIS v3.
- Use empty/non-empty checks: `{% if t.someField != "" %}`.

### JavaScript (IE11 environment)
- **Forbidden:** `let`, `const`, `=>`, `fetch`, template literals, `Promise`, `class`, `for...of`, destructuring.
- **Use:** `var`, `function name(){}`, `XMLHttpRequest`, string concatenation.
- `window.kbXxx = function(){}` does **NOT** hoist — define before use.

### C# scripting
- No `using` directives — namespaces pre-imported by platform.
- Error handling: `Service.HandleException(ex, "message")`.
- Always `GetEditable()` before writing attributes.

---

## 11. Common mistakes to avoid

1. Inventing PLM API method names instead of checking docs or asking.
2. Using `??` for NameKey (use `string.IsNullOrEmpty`).
3. Using modern JS syntax (let/const/arrow) — IE11 only.
4. Forgetting `GetEditable()` before writing attributes.
5. DotLiquid string value comparisons (use empty/non-empty).
6. Breaking JS↔C# transport contracts without synchronized changes.
7. Batching unrelated changes into large commits without smoke validation.
