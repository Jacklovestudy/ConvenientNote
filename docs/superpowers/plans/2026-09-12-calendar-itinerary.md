# Calendar itinerary import implementation plan

**Goal:** Paste a Chinese travel itinerary, preview and edit daily events, timed transport and checklists, then atomically import and undo the batch.

**Architecture:** Keep calendar-owned imported checklists in the calendar store so completion and batch undo work without depending on the optional todo module. Preserve full source and shared overview in batch metadata. Extend existing event metadata without breaking old records or existing calendar API callers.

**Tech stack:** .NET 10, WPF, Prism, SQLite, xUnit. Approved design: conversation dated 2026-09-12.

**Constraints:** No commits or pushes. Preserve unrelated working-tree changes. No network or AI service required. Never invent exact times for approximate input. Preview precedes all writes.

- [x] Parser: add ItineraryParser with editable drafts; test daily splitting, cross-midnight transport, uncertain times, general notes, checklists and invalid dates.
- [x] Persistence: extend event details; add transactional batch repository with source fingerprints, persistent batch history and workspace-scoped undo. Test round-trip, old schema migration, duplicates and rollback.
- [x] Application: validate all drafts before writes; provide import/history/undo/edit APIs and preserve metadata during rescheduling and completion.
- [x] UI: add import and batch-history window, source/preview steps, selectable editable rows, count feedback and inline errors. Reuse event editor for existing events. Navigate to earliest imported day.
- [x] Verification: run calendar and relevant desktop regression tests, build desktop, review diff and document usage. Leave changes uncommitted.

Verification commands: `dotnet test tests/ConvenientNote.Calendar.Tests`; `dotnet test tests/ConvenientNote.Tests --filter FullyQualifiedName~Calendar`; `dotnet build UI/ConvenientNote.Desktop`.

