# Kanban Board for Soyuz-PLM

**Production-grade embedded Kanban for engineering design bureaus — deeply integrated into Soyuz-PLM BIS v3**

A modern, secure, role-aware Kanban task board that runs *inside* the legacy Soyuz-PLM client with zero additional software. Built for real конструкторские бюро (design engineering organizations) with complex hierarchical workflows, strict access control, and full traceability.

> **Deployed organization-wide** in a production engineering bureau with positive feedback from engineers and leadership.

## Why This Project Stands Out

- **Real production impact**: Used daily by engineers working on complex technical projects. Not a demo or side project.
- **Non-trivial engineering**: Deep integration with legacy PLM (IE11 WebBrowser control, DotLiquid, native C# scripting, strict server-side security).
- **Active, high-value development**: Multiple major features shipped in May–June 2026 alone (local file check-out/check-in, Directum links, PC file attachment with rich engineering format support, subtasks DnD+edit, private tasks, Excel export, "issued by me" mode).
- **Security-first design**: Server-enforced RBAC that cannot be bypassed via UI; private tasks invisible even to leadership; full audit history.

## Key Capabilities

### Core Workflow
- Drag & drop between columns (To Do / In Progress / Waiting / Done)
- Rich task cards: description, deadline, priority, assignee, history, in-card chat, PLM attachments
- **Hierarchical RBAC** enforced server-side (`Commission` + `Context` + NameKey prefixes):
  - Regular employee → only own tasks
  - Lead engineer (`ved*`) / Sector head → sector scope
  - Department head / Admin → broader visibility
- Server-side validation prevents out-of-scope assignments even if UI is bypassed
- Group task assignment (one task → multiple people)
- Custom reports (completed/overdue/in-progress by person and period)
- Optional toolbar button to open the board directly

### Recent Major Features (2026)

**Local file workflow (June)**
- Attach files directly from your computer (multi-select)
- Two smart storage modes: "Inside task" (SimpleDocument) or "To PLM storage" (Technical Document + version)
- Full **check-out / check-in** from the task card: lock, download to %TEMP%, open in native app (Word/Excel/CAD), edit, then "Save back to PLM" or cancel
- Rich file type detection and icons for SolidWorks, Kompas-3D, AutoCAD, Inventor, CATIA, Creo, neutral formats, PDFs, images, etc.
- Limit raised to 50 attachments per task

**Directum integration (June)**
- Add multiple Directum links (tasks, documents, folders) directly in the task card
- One-click open: Soyuz silently fetches .isb from server and launches via ISBExec (no browser, no manual download)
- Orange "D" badge on board cards when links exist
- Full change history; any viewer can add/open links (same permission model as attachments)

**Subtasks & personal organization (May)**
- Checklists inside tasks with drag-and-drop reordering and inline editing
- Completed items automatically move to bottom of list

**Manager & visibility features (May)**
- "Issued by me" (myCreated) mode — quickly see all tasks you created
- Private tasks visible *only* to the creator (even leadership cannot see them)
- Mandatory comment + notification when returning a task from "Done" that was assigned by someone else

**Reporting & export (May)**
- One-click **Export current board view to Excel** (respects active filters, view mode, and period)
- Formatted workbook with headers, borders, overdue highlighting, opened directly on the user's machine

**UX & Polish**
- "What's New" button that pulses until the latest version is viewed (version auto-detected from top changelog entry; seen state stored in PLM user registry)
- Responsive toolbar, improved dialogs for IE11, many stability fixes

All features are **IE11-compatible** and respect the strict security model of the host PLM system.

## Technical Challenges Solved

- Running a modern Kanban UX inside a 20+ year old PLM client (IE11 ActiveX WebBrowser)
- Complex server-side C# business logic + legacy DotLiquid templating
- True zero-trust security model (all critical decisions on server)
- Deep integration with PLM object model, containers, attachments, history, and external systems (Directum)
- Handling real engineering file formats and local editing workflows without breaking PLM licensing or visibility rules

## Tech Stack

| Layer       | Technology                                              |
|-------------|---------------------------------------------------------|
| Backend     | C# scripting in Soyuz-PLM BIS v3 (multiple .cs modules) |
| Frontend    | HTML + CSS + JavaScript (ES5, IE11 compatible)          |
| Templating  | Liquid / DotLiquid                                      |
| Bridge      | `window.external.InvokeTemplate()`                      |
| Integration | Directum (.isb launch), local file check-out/check-in   |

## Project Structure

```
scripts/
├── SOYUZ_UPLOAD_KanbanScreen_script.cs      # Core screen logic + most Invoke handlers
├── SOYUZ_UPLOAD_KanbanTask_script.cs        # Task create/save/move logic
├── kanbanButton.cs                          # Optional toolbar command
├── KanbanBoard_HTML.html                    # Main template
├── kanban.js                                # ~1800+ lines of client logic
├── kanban.css
├── SimpleDocumentKanban_PreCheck.cs         # Visibility rules for local files
├── ExclamationKanban.cs                     # Notifications
├── GlobalLinkHandler.cs                     # Directum link handling
└── ...

docs/                                        # Detailed modular documentation
FULL_CONTEXT_OF_PROJECT.md
KANBAN_IMPROVEMENTS_BACKLOG.md
```

## Deployment

See `docs/` and supporting Markdown files for complete instructions and configuration import files (`.pmszcfg`).

Quick path:
1. Create dictionaries + task template
2. Upload resources and scripts
3. Load into Soyuz-PLM

## Current Status & Roadmap

The board is in active production use. Many high-value items from the original backlog have been delivered in the last 6–8 weeks.

**Recently completed (high impact):**
- Local file attach + full check-out/check-in workflow
- Directum object links with native one-click open
- Subtasks with DnD reorder + inline edit
- Private tasks + manager notifications on Done return
- "Issued by me" mode + Excel export
- "What's New" with auto-versioning and persistent seen state

**Remaining high-priority items** (see `KANBAN_IMPROVEMENTS_BACKLOG.md`):
- Global search across title/description/tags/comments
- Manager approval workflow in "Done" column
- Extended reports with "Accepted by" column
- Recurring tasks
- Further manager rights polish

## Why This Project Deserves Support (Codex for OSS)

This is a **real, production, security-sensitive enterprise tool** used in engineering organizations. The maintainer is a single developer carrying the full load of:
- Complex C# backend logic tightly coupled to a legacy PLM system
- Legacy frontend constraints (IE11)
- Continuous feature delivery based on real user feedback from engineers and leadership

Codex / ChatGPT Pro + API credits would directly accelerate delivery of the remaining high-value roadmap items and allow exploration of AI-assisted features (smart assignee suggestions, natural language task creation, intelligent reporting) while maintaining the strict security and compatibility requirements of the environment.

Supporting this project helps bring modern, usable task management to engineers working on complex technical projects inside traditional PLM systems — a common and painful reality in many organizations.

## Contributing

Enterprise integration + security-sensitive code means:
- All changes must be tested on a non-production server
- Security and RBAC logic require extra care
- Please open an issue before large changes

## License

Currently unlicensed. Interested parties — please open an issue.

## Links

- Historical development: [kanbanSoyz2](https://github.com/Kest1k/kanbanSoyz2)
- Full context & backlog: see files in repo root
