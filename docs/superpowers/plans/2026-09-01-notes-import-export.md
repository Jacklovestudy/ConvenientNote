# Notes-Only Import/Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move import/export into the Notes page and migrate only non-deleted notes plus their images while preserving todos, schedules, completed items, and the recycle bin.

**Architecture:** Replace the unshipped whole-workspace backup contract with a notes-only package (`manifest.json`, `notes.json`, selected media). Add a repository operation that transactionally replaces only active notes in the current workspace. Keep UI mutation draining only around `NotesView`, then recreate only the Notes region view after a successful import.

**Tech Stack:** .NET 10, WPF, Prism regions/DI, EF Core SQLite, `System.IO.Compression`, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-31-workspace-import-export-design.md`

## Global Constraints

- Package format is `convenient-note-notes-backup`, schema version `1`, extension `.cnote`.
- Exported records must satisfy `BoardKey == TodoBoardKeys.Notes && !IsDeleted`.
- Import replaces only current non-deleted notes; non-note records and recycle-bin notes remain unchanged.
- Any imported ID collision with a preserved record fails and rolls the transaction back; no merge or ID remapping.
- Only exported/imported active-note media directories are copied, moved, deleted, or restored.
- The Notes page owns the visible import/export actions; the navigation drawer has none.
- Preview and import use the same immutable staged package.
- No restart, persistent user backup, cloud sync, generic document format, or conflict UI.
- Work directly in the current branch. Do not stage or commit.

---

## File Map

- `Services/NotesBackupModels.cs`: manifest, document, note DTO, preview/export/import results, unsupported-schema exception.
- `Services/NotesBackupSerializer.cs`: DTO JSON read/write, active-note filtering, validation, and domain reconstruction.
- `Services/NotesBackupService.cs`: ZIP export/inspect/import and selective active-note media rollback.
- `Services/NotesBackupPackageStager.cs`: immutable selected-package snapshot with non-masking cleanup.
- `src/ConvenientNote.Application/Abstractions/IWorkspaceRepository.cs`: active-note replacement contract.
- `src/ConvenientNote.Application/Workspaces/WorkspaceApplicationService.cs`: current-workspace active-note replacement facade.
- SQLite/JSON repositories: transactional/atomic active-note-only replacement.
- `Views/NotesView.xaml(.cs)`: menu, dialogs, confirmation, save drain, import, and Notes-only refresh.
- `Views/NotesReplacementOperationGate.cs`: blocks and drains only Notes mutations.
- `Views/WorkspaceTransferRequestGate.cs`: shared reentrancy/close guard.
- `MainWindow.xaml(.cs)`: remove drawer transfer UI; retain only close protection and normal navigation.
- Remove obsolete whole-workspace backup types/services/coordinator and Todo/Trash replacement-gate wiring.

---

### Task 1: Notes-only backup document and exact count

**Files:**
- Create: `Services/NotesBackupModels.cs`
- Create: `Services/NotesBackupSerializer.cs`
- Delete after replacement: `Services/WorkspaceBackupModels.cs`
- Delete after replacement: `Services/WorkspaceBackupSerializer.cs`
- Test: `tests/ConvenientNote.Tests/Services/NotesBackupSerializerTests.cs`
- Delete after replacement: `tests/ConvenientNote.Tests/Services/WorkspaceBackupSerializerTests.cs`

**Interfaces:**
- Produces `NotesBackupManifest(string Format, int SchemaVersion, string AppVersion, DateTimeOffset ExportedAtUtc)`.
- Produces `NotesBackupDocument(IReadOnlyList<NotesBackupNote> Notes)`.
- Produces `NotesBackupPreview(int NoteCount, DateTimeOffset ExportedAtUtc)`, `NotesBackupExportResult(string PackagePath, int NoteCount)`, and `NotesBackupImportResult(int NoteCount)`.
- Produces `NotesBackupSerializer.CreateDocument(IEnumerable<NoteSnapshot>)`, `WriteDocumentAsync`, `ReadDocumentAsync`, and `ToNotes`.

- [ ] **Step 1: Write a failing filter and round-trip test**

Create one workspace snapshot containing five active Notes records, one deleted Notes record, and two DayTodo records. Use literal expected IDs. Assert the document count is exactly `5`, every exported item is active Notes, and complete rich-note fields survive JSON and `ToNotes`.

```csharp
var document = NotesBackupSerializer.CreateDocument(snapshot.Notes);
Assert.Equal(5, document.Notes.Count);
Assert.All(document.Notes, note =>
{
    Assert.Equal(TodoBoardKeys.Notes, note.BoardKey);
    Assert.False(note.IsDeleted);
});
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test tests/ConvenientNote.Tests/ConvenientNote.Tests.csproj --filter "FullyQualifiedName~NotesBackupSerializerTests" --artifacts-path .codex-notes-task1-red
```

Expected: compilation fails because notes-only types do not exist.

- [ ] **Step 3: Implement explicit schema-1 types and filtering**

`NotesBackupNote` mirrors every `NoteSnapshot` field. `CreateDocument` must filter with the exact predicate:

```csharp
notes.Where(static note =>
    note.BoardKey == TodoBoardKeys.Notes && !note.IsDeleted)
```

`ToNotes` reconstructs `Note` through its public constructor and rejects any DTO whose board is not Notes or whose `IsDeleted` is true.

- [ ] **Step 4: Implement strict read validation**

Reject null JSON, null notes, empty/duplicate note IDs, missing strings/tags, invalid domain values, non-Notes board keys, and deleted records with `InvalidDataException`. Pass cancellation tokens through JSON APIs.

- [ ] **Step 5: Run focused and serializer regression tests**

Expected: the five-active-plus-three-unrelated fixture returns exactly five and full-field round trip passes.

- [ ] **Step 6: Inspect without committing**

Run `git diff --check`; confirm no workspace/todo DTO enters the notes package.

---

### Task 2: Transactional active-note replacement

**Files:**
- Modify: `src/ConvenientNote.Application/Abstractions/IWorkspaceRepository.cs`
- Modify: `src/ConvenientNote.Application/Workspaces/WorkspaceApplicationService.cs`
- Modify: `src/ConvenientNote.Infrastructure/Persistence/SqliteWorkspaceRepository.cs`
- Modify: `src/ConvenientNote.Infrastructure/Persistence/JsonWorkspaceRepository.cs`
- Modify repository fakes only for interface compilation.
- Test: `tests/ConvenientNote.Tests/Infrastructure/ActiveNotesReplacementTests.cs`
- Delete after replacement: `tests/ConvenientNote.Tests/Infrastructure/WorkspaceReplacementTests.cs`

**Interfaces:**
- Replaces whole-store `ReplaceAllAsync` with:

```csharp
Task ReplaceActiveNotesAsync(
    WorkspaceId workspaceId,
    IReadOnlyCollection<Note> importedNotes,
    CancellationToken cancellationToken = default);
```

- Application facade returns the refreshed `WorkspaceSnapshot` with the same signature plus return type `Task<WorkspaceSnapshot>`.

- [ ] **Step 1: Write a failing real-SQLite preservation test**

Store a workspace containing old active notes, a deleted note, DayTodo, inbox/testing todos, and completed items. Replace active notes with two imports. Reopen the database and assert:

```csharp
Assert.Equal(importedIds, stored.Notes
    .Where(n => n.BoardKey == TodoBoardKeys.Notes && !n.IsDeleted)
    .Select(n => n.Id));
Assert.Contains(stored.Notes, n => n.Id == deletedId && n.IsDeleted);
Assert.Contains(stored.Notes, n => n.Id == dayTodoId);
```

- [ ] **Step 2: Write failing rollback and collision tests**

Use a temporary SQLite trigger to abort imported note insertion and prove all old active/unrelated/deleted records survive. Separately import an ID already used by a preserved deleted record; assert failure and complete rollback.

- [ ] **Step 3: Verify RED**

Expected: missing `ReplaceActiveNotesAsync` contract.

- [ ] **Step 4: Implement SQLite replacement in one transaction**

Load the target workspace and notes. Compute `activeNotes` with the exact active Notes predicate and `preservedNotes` as its complement. Reject imported IDs that collide with `preservedNotes`. Within one EF transaction remove `activeNotes`, add mapped imported notes under the existing workspace ID, save, and commit.

- [ ] **Step 5: Implement JSON atomic compatibility**

Load records, preserve every record except active Notes in the target workspace, reject collisions with preserved IDs, append imported active Notes, and write through a same-directory temporary file followed by overwrite move.

- [ ] **Step 6: Add the application facade**

Forward the current workspace ID and reconstructed notes to the repository, then reload and return the workspace snapshot. The Application project must not reference root backup DTOs.

- [ ] **Step 7: Run focused, persistence, and full compile tests**

Expected: active-note replacement, rollback, collision, and existing todo/persistence tests pass.

- [ ] **Step 8: Inspect without committing**

Confirm whole-store `ReplaceAllAsync` is gone and no repository path deletes the workspace or unrelated notes.

---

### Task 3: Notes archive and selective media replacement

**Files:**
- Create: `Services/NotesBackupService.cs`
- Create: `Services/NotesBackupPackageStager.cs`
- Delete after replacement: `Services/WorkspaceBackupService.cs`
- Delete after replacement: `Services/WorkspaceBackupPackageStager.cs`
- Rename/update: `Services/WorkspaceBackupImportFailureMessages.cs` to `Services/NotesBackupImportFailureMessages.cs`
- Test: `tests/ConvenientNote.Tests/Services/NotesBackupArchiveTests.cs`
- Test: `tests/ConvenientNote.Tests/Services/NotesBackupImportTests.cs`
- Delete replaced workspace-backup test files.

**Interfaces:**
- `ExportAsync(string destinationPath, CancellationToken)` returns `NotesBackupExportResult`.
- `InspectAsync(string packagePath, CancellationToken)` returns `NotesBackupPreview`.
- `ImportOverwriteAsync(string packagePath, CancellationToken)` returns `NotesBackupImportResult`.
- `NotesBackupPackageStager.StageAsync` returns an immutable staged snapshot used by both inspect and import.

- [ ] **Step 1: Write failing archive-scope tests**

Export a workspace with five active notes, deleted-note media, DayTodo media, and active-note media. Assert `notes.json` contains five and ZIP entries contain only manifest, notes JSON, and active-note media.

- [ ] **Step 2: Write failing selective-import tests**

Import two notes into a real temporary SQLite/media environment containing old active notes, a deleted note, and todos. Assert old active notes/media disappear, imported notes/media appear, and deleted/todo records plus their media bytes remain unchanged.

- [ ] **Step 3: Write failing rollback/no-mutation tests**

Cover repository failure after media installation, invalid JSON, unsupported schema, deleted record in package, traversal, canonical collisions, and imported-ID collision. Assert database and every preserved media directory remain byte-for-byte unchanged.

- [ ] **Step 4: Verify RED**

Expected: notes-only service/package contracts are missing or current service exports unrelated records.

- [ ] **Step 5: Implement notes-only export and inspection**

Use root entries `manifest.json` and `notes.json`, format `convenient-note-notes-backup`, schema `1`. Reuse canonical path containment/collision defenses. Media validation accepts only `media/<exported-note-id>/...`.

- [ ] **Step 6: Implement selective media swap**

Before mutation, read current active-note IDs. For each existing active-note directory, move only that directory into a GUID rollback root. Move staged imported-note directories into `NoteMediaService.MediaRoot`. Never rename or delete the media root itself. On pre-commit failure, remove installed imported directories and restore moved active directories. On success, best-effort delete rollback/import temp without masking the committed result.

- [ ] **Step 7: Invoke active-note transactional replacement**

Reconstruct active domain notes and call `WorkspaceApplicationService.ReplaceActiveNotesAsync` for the existing current workspace ID. Package workspace identity must not replace the local workspace.

- [ ] **Step 8: Preserve immutable staging and cancellation behavior**

Preview and import use the same staged file. Cleanup failures must not mask success, cancellation, copy failure, or unsupported-schema errors. Check cancellation before destructive moves and before final export overwrite.

- [ ] **Step 9: Run archive/import/security tests and full suite**

Expected: exact count is five in the mixed workspace fixture; all unrelated data/media survive successful and failed imports.

- [ ] **Step 10: Inspect without committing**

Confirm the implementation never copies the SQLite database, never swaps the whole media root, and never retains a user backup.

---

### Task 4: Move the feature into the Notes page

**Files:**
- Modify: `App.xaml.cs`
- Modify: `MainWindow.xaml`
- Modify: `MainWindow.xaml.cs`
- Modify: `Views/NotesView.xaml`
- Modify: `Views/NotesView.xaml.cs`
- Modify: `ViewModels/NotesViewModel.cs`
- Modify: `Views/RichNoteEditorControl.xaml.cs`
- Create/rename: `Views/NotesReplacementOperationGate.cs`
- Keep: `Views/WorkspaceTransferRequestGate.cs` as shared reentrancy/close state.
- Delete: `Views/WorkspaceReplacementCoordinator.cs`
- Remove obsolete replacement-gate changes from Todo/Trash root views, controls, and view models.
- Test: `tests/ConvenientNote.Tests/Views/NotesTransferTests.cs`
- Update: `tests/ConvenientNote.Tests/Views/MainWindowWorkspaceTransferTests.cs`

**Interfaces:**
- Notes page menu items raise code-behind handlers `ExportNotesMenuItem_Click` and `ImportNotesMenuItem_Click`.
- Notes mutation gate exposes prepare/drain and safe resume only for Notes operations.
- Shared transfer gate exposes `TryBegin`, `Complete`, and `IsInProgress`; MainWindow closing cancels while true.

- [ ] **Step 1: Write failing real-XAML surface tests**

Parse `NotesView.xaml` and assert a visible control named `笔记导入导出` owns menu items `导出笔记` and `导入笔记`. Parse `MainWindow.xaml` and assert drawer transfer buttons are absent. Verify minimum-width header columns do not use the previous two fixed 126 px transfer buttons.

- [ ] **Step 2: Write failing ownership/count/confirmation tests**

Assert confirmation text says active notes are overwritten while todos and recycle bin are unaffected; preview literal `5` renders `共 5 条笔记`. Assert the safe cancel button is default/cancel and destructive action is not default.

- [ ] **Step 3: Verify RED**

Expected: Notes menu is absent and MainWindow still owns drawer actions.

- [ ] **Step 4: Register notes services and shared gate**

Register `NotesBackupService`, `NotesBackupPackageStager`, and `WorkspaceTransferRequestGate` as singletons. Constructor-inject notes services into `NotesView`; inject only the shared gate into `MainWindow`. Do not use a service locator.

- [ ] **Step 5: Implement compact Notes toolbar menu**

Add one secondary 44 px-high menu trigger immediately before `新建笔记`; menu items use exact labels and automation names. Keep search and filters usable at the 960 px minimum window width. Remove drawer transfer rows and their MainWindow handlers.

- [ ] **Step 6: Implement export from NotesView**

Acquire the transfer gate, flush/drain the active editor, show `.cnote` save dialog, export, and show `导出完成，共导出 N 条笔记`. On save failure show `保存失败，请重试`. Complete the gate in `finally`.

- [ ] **Step 7: Implement import from NotesView**

Stage selected file, inspect staged package, show exactly one confirmation, prepare/drain Notes mutations, disable Notes content, import, and show `导入完成，共恢复 N 条笔记`. Use the same staged file throughout. For higher schema show `备份版本较新，请升级应用后重试`.

- [ ] **Step 8: Refresh only NotesView**

After import success, remove the cached/current `NotesView` from `MainRegion` and request navigation to `nameof(NotesView)`. Do not remove Todo/Schedule/Trash views and do not reload workspace identity/title. Keep the old Notes participant sealed after commit; resume it only when import failed before commit.

- [ ] **Step 9: Simplify obsolete whole-workspace coordination**

Remove Notes replacement gates from `TodoBoardViewModel`, `TrashViewModel`, `TodoBoardControl`, Todo wrapper views, and `TrashView`. Restore their ordinary pre-feature behavior and tests. Keep Rich editor/Notes mutation draining because those writes can race with Notes replacement.

- [ ] **Step 10: Run UI/navigation/close/save tests**

Cover reentrancy, close during transfer with no cached NotesView, export flush failure, immutable package use, pre-commit resume, post-commit stale-view sealing, exact menu surface, and Notes-only region recreation.

- [ ] **Step 11: Inspect without committing**

Confirm import/export UI exists only in NotesView and no Todo/Trash production type references the Notes replacement gate.

---

### Task 5: End-to-end verification and cleanup

**Files:**
- Modify tests only when verification exposes a real missing assertion.

**Interfaces:**
- Consumes Tasks 1–4; produces no production interface.

- [ ] **Step 1: Run the full suite in isolated artifacts**

```powershell
dotnet test tests/ConvenientNote.Tests/ConvenientNote.Tests.csproj --artifacts-path .codex-notes-final --logger "console;verbosity=normal"
```

Expected: all tests pass.

- [ ] **Step 2: Run a clean application build**

```powershell
dotnet build ConvenientNote.csproj --artifacts-path .codex-notes-final-build --verbosity normal
```

Expected: zero warnings and zero errors.

- [ ] **Step 3: Exercise a real SQLite/media round trip**

Use a temporary DB containing five active rich notes, one deleted note, and multiple todo categories. Export, mutate active notes, import in the same process, and assert all five notes/formats/images return while the deleted note, todos, workspace ID/name, and preserved media remain exact.

- [ ] **Step 4: Exercise failure paths**

Verify invalid JSON, high schema, malformed ZIP, path traversal, ID collision, repository failure, cancellation, staging copy failure, and cleanup refusal never change active notes, recycle bin, todos, or preserved media.

- [ ] **Step 5: Independent review**

Review active-note predicates, repository transaction scope, selective media targets, immutable package staging, stale Notes saves, Prism Notes-only removal, close/reentrancy guards, and removal of obsolete whole-workspace code. Address every concrete P0/P1/P2 finding and rerun affected tests.

- [ ] **Step 6: Clean generated artifacts safely**

Resolve each `.codex-notes-*` path, verify it is inside `C:\便签\ConvenientNote`, and remove only those generated directories using PowerShell `Remove-Item -LiteralPath`. Do not touch existing `.codex-backup-*`, user data, or unrelated untracked files.

- [ ] **Step 7: Report uncommitted status**

Run `git diff --check` and `git status --short`. Explicitly report that all changes remain unstaged and uncommitted.
