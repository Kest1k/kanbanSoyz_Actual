# Kanban Board for Soyuz-PLM

**Embedded production Kanban for engineering design bureaus in Soyuz-PLM BIS v3**

A modern, role-aware Kanban task management board deeply integrated into the legacy Soyuz-PLM system. No additional software installation required — only server-side C# scripts, HTML templates, and client resources are uploaded to the PLM instance.

## Key Features

- **Drag & drop** tasks between columns: To Do / In Progress / Waiting / Done
- Rich task cards with description, deadline, priority, assignee, change history, in-card chat, and PLM object attachments
- **Hierarchical role-based access** enforced server-side (`Commission` + `Context`):
  - Regular employee: only own tasks
  - Lead engineer (`NameKey` starts with `ved`): sector scope
  - Sector head, Department head, Admin: appropriate broader visibility
- **Server-side validation** prevents assigning tasks outside allowed scope (even if UI is bypassed)
- Group task creation (assign one task to multiple people)
- Custom reports (completed / overdue / in progress by assignee and period)
- Full integration with PLM objects and containers as attachments
- Built-in comments/chat inside task cards
- Optional command button (`kanbanButton.cs`) to open the board directly
- **IE11 compatible** (runs in WebBrowser ActiveX control used by BIS v3)

## Real-World Impact

This board has been successfully deployed **organization-wide** in a real engineering design bureau (конструкторское бюро).

Users and leadership report:
- Significantly better task visibility and planning across projects
- Reduced manual routine work (auto completion dates, notifications on assignee change)
- Improved collaboration between different hierarchy levels
- Stronger auditability — all data stays inside the unified PLM system

The solution was developed iteratively based on direct feedback from engineers and management, with a strong focus on security and process compliance.

## Tech Stack

| Layer      | Technology                                          |
|------------|-----------------------------------------------------|
| Backend    | C# scripting in Soyuz-PLM BIS v3                    |
| Frontend   | HTML + CSS + JavaScript (ES5, IE11 compatible)      |
| Bridge     | `window.external.InvokeTemplate()`                  |
| Templating | Liquid / DotLiquid (server-side HTML rendering)     |

## Architecture Highlights

- Deep integration with Soyuz-PLM data model and native APIs
- Strict **server-enforced security model** (never trust the client)
- Legacy constraints fully respected (IE11, DotLiquid quirks, specific PLM API usage patterns)
- Modular design with clear separation between screen logic, task logic, and UI resources

## Project Structure

```text
scripts/
├── SOYUZ_UPLOAD_KanbanScreen_script.cs   # Main screen C# logic (~2400 lines)
├── SOYUZ_UPLOAD_KanbanTask_script.cs     # Task save & notification logic
├── kanbanButton.cs                       # Optional command to open the board
├── KanbanBoard_HTML.html                 # Main HTML template
├── kanban.css                            # Styles (upload to CSS/)
└── kanban.js                             # Client logic (upload to JS/)

docs/                                     # Detailed technical documentation
└── ...

FULL_CONTEXT_OF_PROJECT.md
KANBAN_IMPROVEMENTS_BACKLOG.md
AGENTS.md
CLAUDE.md
```

## Deployment

Detailed deployment instructions, configuration reference files (`.pmszcfg`), and modular documentation are available in the repository (`docs/` folder and supporting Markdown files).

Quick path:
1. Create required dictionaries (`Ref_KanbanStatus`, `Ref_KanbanPriority`) and task template (`KanbanTask`)
2. Upload CSS/JS resources and HTML template
3. Load the C# screen and task scripts
4. (Optional) Add the `kanbanButton.cs` command

## Roadmap & Planned Improvements

Active development backlog includes:

**High priority**
- Hotkeys (F5 refresh, Esc close, Enter save, Ctrl+Enter send comment)
- Global search across title, description, tags, comments
- Date filters and improved sorting (priority + creation date)
- Subtasks / checklists inside cards

**Manager & reporting features**
- Manager approval workflow in "Done" column
- Notifications to managers when tasks move to Done
- Extended reports with "Accepted by" column
- Personal tasks visible only to creator

**Longer term**
- Recurring tasks on schedule
- Integration hooks with other systems (e.g. Directum)

See `KANBAN_IMPROVEMENTS_BACKLOG.md` for the full prioritized list.

## Why This Project Matters

Many engineering organizations using specialized PLM systems face the challenge of bringing modern task management UX into legacy environments without replacing the core system. This project solves exactly that problem while maintaining strict security, hierarchical access control, and full data traceability — requirements that are critical in real engineering workflows.

## Contributing

Because of the enterprise integration and security-sensitive nature of the code:
- All changes must be tested on a non-production server first
- Security and role-based access logic require extra care
- Please open an issue to discuss significant changes before submitting PRs

Development guidelines are documented in `AGENTS.md` and `CLAUDE.md`.

## License

Currently unlicensed. If you are interested in using or contributing to the project, please open an issue.

## Related

Historical development archive: [kanbanSoyz2](https://github.com/Kest1k/kanbanSoyz2)
