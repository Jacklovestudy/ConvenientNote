# Modular DDD migration implementation plan

> Execute with superpowers:subagent-driven-development. No commits or pushes are authorized.

**Goal:** Independently owned Notes, Todos, Calendar and ColorPicker modules, each with UI, application, domain, infrastructure and explicit public contracts.

**Architecture:** WPF modular monolith. Host composes modules and coordinates startup and legacy migration. UI references its application and shared desktop contracts; infrastructure implements application ports; domain has no framework or module dependencies. Cross-module integration uses public contracts only.

**Tech stack:** Existing .NET 10, WPF, Prism, SQLite, xUnit. Retain current appearance and existing behavior.

**Spec:** Approved conversation analysis dated 2026-09-08. Notes are identified by legacy BoardKey `testing`, todos by `day-todo`. Preserve IDs, rich content, media, checklist sentinel and backup compatibility. Calendar owns events separately from external todo projections.

## Constraints
- Work in the current clean checkout; do not commit, push or modify the user's live data while developing.
- Shared workspace port is `IWorkspaceContext.GetCurrentAsync(CancellationToken)` returning `WorkspaceInfo(Guid Id, string Name)` in `src/Shared/ConvenientNote.Platform.Contracts`.
- Existing legacy projects may survive only as a migration adapter/test compatibility boundary, not a runtime business dependency of modules.
- Each module owns its SQLite tables; no partial workspace writes to the old repository.
- Module directories live under `src/Modules/<Module>/ConvenientNote.<Module>.<Layer>`.

## Tasks
- [x] Attempt baseline (blocked by the running original executable locking output); establish shared workspace and UI lifecycle contracts. Subsequent builds/tests use isolated output.
- [x] Notes: migrate Features/Notes and Features/Trash into Notes.UI; split independent model/service/repository; preserve editor and backup behavior. Test persistence isolation and old backup import.
- [x] Todos: migrate Features/Todos into Todos.UI; independent TodoItem and storage; expose ITodoCalendarApi DTO contract. Test completion/date behavior and independent persistence.
- [x] Calendar: migrate calendar UI; own event aggregate, application and SQLite repository; external todo adapter through contracts. Test event validation, persistence and todo/date integration.
- [x] ColorPicker: build standalone module with screen sampling, HEX/RGB, cancel, history; test color conversion and persistence.
- [x] Host: replace concrete NotesView/Calendar dependencies in window flow with desktop lifecycle/module contracts; register modules and keep default Notes navigation.
- [x] Migration: initialize shared workspace and migrate legacy notes/todos once before module UI starts. Use idempotent insert-only imports and completion marker only after both imports succeed. Keep original legacy records and media untouched. Test reruns and failure/retry.
- [x] Validation: compile solution; adapt existing regression tests to new constructors/namespaces; add architecture reference checks; run full suite and review changed boundaries.

## Verification commands
`dotnet test ConvenientNote.slnx --artifacts-path .codex-artifacts/verification -v minimal`

Each module may supply an independent test project to avoid shared test project mutation. New behavior tests must fail before implementation and pass afterward. Use temporary directories for all storage checks.

## Execution notes
Current checkout was clean at start. Legacy code remains only if needed for safe import of the existing SQLite/JSON format; document that compatibility boundary instead of disguising it as a current domain.

### Integration decisions
- Host moved to `src/Host/ConvenientNote.Desktop`; executable assembly remains `ConvenientNote` for resource compatibility.
- New data lives in `ConvenientNote.Modules.db` and `Notes/Media`. Retained originals and a WAL-consistent migration snapshot keep recovery independent from new writes.
- Old-format note task fields survive as opaque LegacyNoteMetadata for backup compatibility, without note task behavior.
- Calendar owns native events and uses an optional contract adapter for todos. Existing todo projections are not duplicated into events.
- Weather transport belongs to Todos.Infrastructure through an application port, not shared UI.
- Startup/resource smoke checks passed every navigation route, module image loading and compact calendar provider; rendered output reviewed.
- Fixed a rapid-drawer-reversal cache lifecycle failure with independently owned slide clocks; retained original animation/document assertions and added disabled-transition, template and unload coverage.
### Verification results
- Full solution: 273 passed, 0 failed, 0 skipped (223 existing/regression, 50 module/migration/architecture executions).
- Desktop build: 0 warnings, 0 errors.
- Isolated real WPF startup smoke: all eight navigation routes, default Notes, note media resolution, native calendar persistence and compact provider passed.
- Multi-monitor physical screen picking still requires manual hardware verification; automated tests cover color values, history persistence and failure behavior.
- Changes remain uncommitted; no user live data was used by validation.
