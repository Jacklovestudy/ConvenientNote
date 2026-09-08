# Notes module

Notes owns note content, rich text, tags, notebook identity, wall layout, favorites, pins, trash and the knowledge checklist.

- `Domain`: pure note entities and values. `LegacyNoteMetadata` is opaque round-trip data from the former mixed note/todo format; it has no task behavior.
- `Application`: note use cases, note snapshots and repository/media/archive ports. `WorkspaceSnapshot` is a presentation DTO containing only this module's notes, not a shared domain aggregate.
- `Infrastructure`: `notes_records` SQLite table, media files and version-1 `.cnote` archive handling. Replacement uses a transaction and preserves trash; media rollback remains coordinated around the database commit boundary.
- `UI`: WPF pages, view models, document editing and page lifecycle. References Application and shared UI contracts, never Infrastructure.
- `Contracts`: `INotesApi` returns public note summaries; other modules must not reference Notes internals.

The Host supplies `IWorkspaceContext`, `INotesRepository`, `INoteMediaService`, `INotesBackupService` and `INotesBackupPackageStager`, registers one `NotesApplicationService`, and calls `RegisterNotesUI()`.

`SqliteNotesRepository.ImportLegacyAsync(workspaceId, notes)` inserts missing IDs only. Retrying migration never overwrites an already imported record. Original legacy storage remains the Host migration adapter's concern. Normal save and delete operations affect explicit note IDs only; no whole-workspace replacement is performed.

The `testing` board marker and archive fields remain at compatibility/presentation boundaries to read existing backups. Active knowledge-memo notes retain the `__app_knowledge_memo` sentinel and are hidden from normal note lists.
