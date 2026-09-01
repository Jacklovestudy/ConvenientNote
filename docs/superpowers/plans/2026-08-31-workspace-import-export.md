# Workspace Import/Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add one-file `.cnote` export and destructive whole-workspace import, including rich text and images, with immediate in-process UI refresh.

**Architecture:** Serialize a versioned backup DTO rather than EF entities, package it with note-scoped media in a ZIP archive, and validate the whole archive before any destructive operation. Import swaps a staged media directory, replaces all SQLite workspace rows transactionally, restores media on ordinary failure, removes cached Prism views, and recreates the active page without restarting.

**Tech Stack:** .NET 10, WPF, Prism, EF Core SQLite, `System.Text.Json`, `System.IO.Compression`, xUnit

**Spec:** `docs/superpowers/specs/2026-08-31-workspace-import-export-design.md`

## Global Constraints

- Export and import the entire current workspace only.
- Import always overwrites; do not implement merge, deduplication, or an automatic user backup.
- Import must refresh the running application without restart.
- Keep package schema version at exactly `1` for the first release.
- Preserve all note fields, rich-content JSON, fixed notebook IDs, tags, states, timestamps, and note-scoped media paths.
- Validate package identity, schema version, JSON, and archive paths before showing the final overwrite action.
- Preserve all unrelated and existing uncommitted workspace changes.
- Do not run `git commit`, push, or create a pull request unless the user explicitly requests it.

---

## File Map

- Create `Services/WorkspaceBackupModels.cs`: versioned manifest, workspace/note DTOs, preview and result records.
- Create `Services/WorkspaceBackupSerializer.cs`: DTO/snapshot/domain mapping plus JSON validation.
- Create `Services/WorkspaceBackupService.cs`: archive creation, inspection, extraction, media staging, overwrite orchestration.
- Create `Views/WorkspaceTransferRequestGate.cs`: prevents overlapping import/export UI actions.
- Modify `src/ConvenientNote.Application/Workspaces/WorkspaceSnapshot.cs`: include workspace timestamps required by the package.
- Modify `src/ConvenientNote.Application/Abstractions/IWorkspaceRepository.cs`: add whole-store replacement contract.
- Modify `src/ConvenientNote.Application/Workspaces/WorkspaceApplicationService.cs`: expose complete snapshot and replacement operations.
- Modify `src/ConvenientNote.Infrastructure/Persistence/SqliteWorkspaceRepository.cs`: transactional replacement.
- Modify `src/ConvenientNote.Infrastructure/Persistence/JsonWorkspaceRepository.cs`: replacement compatibility for the legacy repository.
- Modify `App.xaml.cs`: register `WorkspaceBackupService`.
- Modify `MainWindow.xaml` and `MainWindow.xaml.cs`: import/export buttons, dialogs, save coordination, region reset, feedback.
- Modify `MainWindowViewModel.cs`: reload workspace title and re-request the active navigation target.
- Add focused tests under `tests/ConvenientNote.Tests/Services`, `Application`, `Infrastructure`, and `Views`.

---

### Task 1: Versioned backup model and serializer

**Files:**
- Create: `Services/WorkspaceBackupModels.cs`
- Create: `Services/WorkspaceBackupSerializer.cs`
- Modify: `src/ConvenientNote.Application/Workspaces/WorkspaceSnapshot.cs`
- Modify: `src/ConvenientNote.Application/Workspaces/WorkspaceApplicationService.cs`
- Test: `tests/ConvenientNote.Tests/Services/WorkspaceBackupSerializerTests.cs`

**Interfaces:**
- Produces: `WorkspaceBackupManifest`, `WorkspaceBackupDocument`, `WorkspaceBackupNote`, `WorkspaceBackupPreview`.
- Produces: `WorkspaceBackupSerializer.CreateDocument(WorkspaceSnapshot)`, `WriteDocumentAsync(Stream, WorkspaceBackupDocument)`, and `ReadDocumentAsync(Stream)`.
- Produces: `WorkspaceBackupSerializer.ToWorkspace(WorkspaceBackupDocument)` returning the exact imported `Workspace` IDs and timestamps.
- Changes: `WorkspaceSnapshot` gains `CreatedAt` and `UpdatedAt` parameters after `Name`.

- [ ] **Step 1: Write a failing complete-field round-trip test**

Create a workspace snapshot containing one deleted, pinned, favorite note with tags, notebook ID, rich JSON, non-default geometry, timestamps, priority, board, and completion state. Assert that `CreateDocument`, JSON write/read, and `ToWorkspace` preserve every field.

```csharp
var document = WorkspaceBackupSerializer.CreateDocument(snapshot);
using var stream = new MemoryStream();
await WorkspaceBackupSerializer.WriteDocumentAsync(stream, document);
stream.Position = 0;
var restored = WorkspaceBackupSerializer.ToWorkspace(
    await WorkspaceBackupSerializer.ReadDocumentAsync(stream));

Assert.Equal(snapshot.Id, restored.Id);
Assert.Equal(snapshot.CreatedAt, restored.CreatedAt);
Assert.Equal(snapshot.Notes[0].RichContent, Assert.Single(restored.Notes).RichContent);
```

- [ ] **Step 2: Run the serializer test and verify RED**

Run:

```powershell
dotnet test tests/ConvenientNote.Tests/ConvenientNote.Tests.csproj --filter "FullyQualifiedName~WorkspaceBackupSerializerTests" --artifacts-path .codex-backup-task1
```

Expected: compilation fails because the backup types do not exist.

- [ ] **Step 3: Implement explicit schema-1 DTOs**

Use records with primitive serialization fields. The manifest must contain:

```csharp
public sealed record WorkspaceBackupManifest(
    string Format,
    int SchemaVersion,
    string AppVersion,
    DateTimeOffset ExportedAtUtc);
```

`WorkspaceBackupDocument` must include workspace ID/name/timestamps and `IReadOnlyList<WorkspaceBackupNote>`. `WorkspaceBackupNote` must mirror every field in `NoteSnapshot`; GUID wrapper types serialize as `Guid` values.

- [ ] **Step 4: Implement strict read validation and domain reconstruction**

`ReadDocumentAsync` must throw `InvalidDataException` for null JSON, missing IDs, duplicate note IDs, or invalid domain values. Schema compatibility belongs to archive manifest validation in Task 3. `ToWorkspace` must use the public `Workspace` and `Note` constructors so imported IDs and timestamps are retained.

- [ ] **Step 5: Extend workspace snapshots with timestamps**

Change the record to:

```csharp
public sealed record WorkspaceSnapshot(
    WorkspaceId Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<NoteSnapshot> Notes);
```

Update `WorkspaceApplicationService.ToSnapshot` and every affected test constructor.

- [ ] **Step 6: Run focused and existing application tests**

Run the Task 1 filter plus `FullyQualifiedName~WorkspaceNotesMetadataTests` and `FullyQualifiedName~NotesViewModelTests`. Expected: PASS.

- [ ] **Step 7: Inspect the diff without committing**

Run `git diff --check` and confirm only Task 1 files plus pre-existing user changes are present.

---

### Task 2: Transactional whole-store replacement

**Files:**
- Modify: `src/ConvenientNote.Application/Abstractions/IWorkspaceRepository.cs`
- Modify: `src/ConvenientNote.Application/Workspaces/WorkspaceApplicationService.cs`
- Modify: `src/ConvenientNote.Infrastructure/Persistence/SqliteWorkspaceRepository.cs`
- Modify: `src/ConvenientNote.Infrastructure/Persistence/JsonWorkspaceRepository.cs`
- Modify: repository fakes in existing tests only as required by compilation
- Test: `tests/ConvenientNote.Tests/Infrastructure/WorkspaceReplacementTests.cs`

**Interfaces:**
- Produces: `Task ReplaceAllAsync(Workspace workspace, CancellationToken cancellationToken = default)` on `IWorkspaceRepository`.
- Produces: `Task<WorkspaceSnapshot> ReplaceAllAsync(Workspace workspace, CancellationToken cancellationToken = default)` on `WorkspaceApplicationService`.
- The Application project consumes only the Domain `Workspace`; it must not reference root-project backup service types.

- [ ] **Step 1: Write a failing SQLite replacement test**

Use a temporary database, save workspace A with two notes, call `ReplaceAllAsync` with workspace B, reopen the repository, and assert only workspace B and its notes exist.

```csharp
await repository.SaveAsync(oldWorkspace);
await repository.ReplaceAllAsync(importedWorkspace);
var stored = Assert.Single(await repository.ListAsync());
Assert.Equal(importedWorkspace.Id, stored.Id);
Assert.Equal(importedWorkspace.Notes.Count, stored.Notes.Count);
```

- [ ] **Step 2: Write a failing rollback test**

After saving workspace A, create a temporary SQLite trigger that aborts inserts into `Workspaces`. Call `ReplaceAllAsync` with workspace B and assert the operation throws; remove the trigger, reopen the repository, and assert workspace A is still present. This proves the delete and insert share one transaction without adding a production-only test seam.

- [ ] **Step 3: Run replacement tests and verify RED**

Expected: compilation fails because `ReplaceAllAsync` is absent.

- [ ] **Step 4: Implement SQLite replacement in one EF transaction**

The implementation must initialize the database, begin a transaction, remove all `WorkspaceEntity` rows, add `ToEntity(workspace)`, save, and commit. Dispose without commit on failure so SQLite rolls back.

- [ ] **Step 5: Implement JSON replacement compatibility**

Write a one-element `WorkspaceRecord` collection to a same-directory temporary file and atomically replace/move it to `_filePath`. This repository is not active in production but must honor the interface.

- [ ] **Step 6: Add the application-service replacement method**

Accept the domain workspace produced by the backup service, invoke repository replacement, and return `ToSnapshot(workspace)`.

- [ ] **Step 7: Run repository, application, and full compile tests**

Expected: replacement and existing persistence tests pass; all in-memory repository fakes compile and preserve their current semantics.

- [ ] **Step 8: Inspect the diff without committing**

Run `git diff --check`. Do not stage or commit.

---

### Task 3: `.cnote` archive export and inspection

**Files:**
- Create: `Services/WorkspaceBackupService.cs`
- Modify: `Services/NoteMediaService.cs`
- Test: `tests/ConvenientNote.Tests/Services/WorkspaceBackupArchiveTests.cs`

**Interfaces:**
- Produces: `Task<WorkspaceBackupExportResult> ExportAsync(string destinationPath, CancellationToken cancellationToken = default)`.
- Produces: `Task<WorkspaceBackupPreview> InspectAsync(string packagePath, CancellationToken cancellationToken = default)`.
- Produces: `NoteMediaService.MediaRoot` as the only source of physical media paths.
- Consumes: serializer and workspace application service from Tasks 1–2.

- [ ] **Step 1: Write a failing export archive test**

Use temporary repository/media roots. Export a workspace with two notes and one file under `Media/<note-id>/image.png`. Open the result with `ZipArchive` and assert exactly one manifest, one workspace JSON, and the note-scoped image entry exist.

- [ ] **Step 2: Write failing inspection validation tests**

Cover wrong `format`, `schemaVersion: 2`, corrupt workspace JSON, and an entry named `../escape.txt`. Assert `InvalidDataException` and verify no file appears outside the extraction root.

- [ ] **Step 3: Run archive tests and verify RED**

Expected: compilation fails because `WorkspaceBackupService` is absent.

- [ ] **Step 4: Implement export through a temporary sibling file**

Write `<destination>.tmp-<guid>`, add `manifest.json`, `workspace.json`, and media directories belonging to workspace note IDs, close the archive, then move to the requested destination with overwrite enabled. Delete the temporary file in `finally`.

- [ ] **Step 5: Implement archive inspection without mutating app data**

Require exactly one root `manifest.json` and `workspace.json`. Normalize every ZIP entry against a newly created temp root and reject it unless the resolved path starts with that root plus a directory separator. Deserialize and validate the workspace, return its name/note count/export time, then delete the inspection temp directory.

- [ ] **Step 6: Run archive and serializer tests**

Expected: PASS, including ZIP traversal rejection.

- [ ] **Step 7: Inspect the diff without committing**

Confirm package generation never opens or copies `ConvenientNote.db` directly.

---

### Task 4: Destructive import with media rollback

**Files:**
- Modify: `Services/WorkspaceBackupService.cs`
- Test: `tests/ConvenientNote.Tests/Services/WorkspaceBackupImportTests.cs`

**Interfaces:**
- Produces: `Task<WorkspaceBackupImportResult> ImportOverwriteAsync(string packagePath, CancellationToken cancellationToken = default)`.
- Result contains `WorkspaceId WorkspaceId`, `string WorkspaceName`, and `int NoteCount`.
- Consumes: `WorkspaceApplicationService.ReplaceAllAsync` and `NoteMediaService.MediaRoot`.

- [ ] **Step 1: Write a failing successful-overwrite test**

Prepare current workspace A/media A and a package for workspace B/media B. Import, then assert the repository contains only B, the media root contains only B files, and the result count matches B.

- [ ] **Step 2: Write a failing repository-error rollback test**

Use a repository fake whose `ReplaceAllAsync` throws. Assert workspace A is still readable and media A is restored after the import failure.

- [ ] **Step 3: Write a failing invalid-package no-mutation test**

Pass corrupt JSON and unsupported schema packages. Assert replacement was never called and current media bytes are unchanged.

- [ ] **Step 4: Run import tests and verify RED**

Expected: the overwrite method is missing.

- [ ] **Step 5: Implement staged extraction and media swap**

Extract and validate to `%TEMP%/ConvenientNote/Import/<guid>`. Prepare a complete staged `Media` directory. Rename the current media root to `Media.rollback-<guid>`, rename staged media to the configured media root, then invoke repository replacement.

- [ ] **Step 6: Implement ordinary-failure restoration**

On replacement failure, delete the new media root and rename the rollback directory back. On success, delete the rollback directory. Every delete/move target must be resolved and verified as either the configured media root, its same-parent rollback directory, or the per-import temp directory.

- [ ] **Step 7: Run import tests**

Expected: successful overwrite, repository rollback, and invalid-package no-mutation tests pass.

- [ ] **Step 8: Inspect the diff without committing**

Confirm no automatic persistent backup is retained and no merge branch exists.

---

### Task 5: WPF controls and in-process refresh

**Files:**
- Create: `Views/WorkspaceTransferRequestGate.cs`
- Modify: `App.xaml.cs`
- Modify: `MainWindow.xaml`
- Modify: `MainWindow.xaml.cs`
- Modify: `MainWindowViewModel.cs`
- Modify: `Views/NotesView.xaml.cs`
- Test: `tests/ConvenientNote.Tests/Views/WorkspaceTransferRequestGateTests.cs`
- Test: `tests/ConvenientNote.Tests/Views/MainWindowWorkspaceTransferTests.cs`

**Interfaces:**
- Produces: `WorkspaceTransferRequestGate.TryBegin()` and `.Complete()`.
- Produces: `MainWindowViewModel.ReloadWorkspaceIdentityAsync()`.
- Produces: `RichNoteEditorControl.CancelPendingSave()` and `NotesView.PrepareForWorkspaceReplacement()` which stop the editor timer before cached views are removed.
- Consumes: `WorkspaceBackupService.ExportAsync`, `.InspectAsync`, and `.ImportOverwriteAsync`.

- [ ] **Step 1: Write failing request-gate tests**

Assert the first request begins, a second concurrent request is rejected, and a request is allowed after `Complete`.

- [ ] **Step 2: Write failing XAML/command surface tests**

Assert the navigation drawer exposes buttons with automation names `导出数据` and `导入数据`, and both route to code-behind handlers rather than navigation commands.

- [ ] **Step 3: Run UI-focused tests and verify RED**

Expected: the gate and named controls are absent.

- [ ] **Step 4: Register and inject the backup service**

Register `WorkspaceBackupService` as a singleton in `App.RegisterTypes`. Add it to `MainWindow` constructor injection; do not use a service locator.

- [ ] **Step 5: Add simple drawer actions**

Place two compact secondary buttons above the existing `已达成` / `回收站` row. Export uses `SaveFileDialog` with `.cnote`; import uses `OpenFileDialog`, calls `InspectAsync`, then shows exactly one confirmation whose destructive action reads `覆盖并导入`.

- [ ] **Step 6: Coordinate active editor state**

Before export, call existing `FlushAsync` and abort with `保存失败，请重试` if it returns false. Before confirmed import, set the main content disabled and call `PrepareForWorkspaceReplacement()` so no dispatcher timer can save an old note after replacement; restore enabled state in `finally`.

- [ ] **Step 7: Recreate cached region views after import**

After a successful overwrite:

```csharp
var region = RegionManager.GetObservableRegion(MainRegionContent).Value!;
foreach (var view in region.Views.Cast<object>().ToList())
{
    region.Remove(view);
}
await viewModel.ReloadWorkspaceIdentityAsync();
viewModel.ReloadActiveNavigation();
```

Keep the same selected navigation section. The new view must resolve a new ViewModel and load the imported workspace ID.

- [ ] **Step 8: Add success/failure feedback and reentrancy protection**

Disable overlapping operations through `WorkspaceTransferRequestGate`. Show `导出完成` after export and `导入完成，共恢复 N 条笔记` after import. Always complete the gate in `finally`.

- [ ] **Step 9: Run WPF, navigation, close, and save tests**

Run filters for `MainWindow`, `NotesViewReturnTests`, `DeferredWindowCloseCoordinatorTests`, and `WorkspaceTransferRequestGateTests`. Expected: PASS.

- [ ] **Step 10: Inspect the diff without committing**

Confirm the drawer remains usable at minimum window size and button labels are not clipped.

---

### Task 6: End-to-end verification and cleanup

**Files:**
- Modify tests only if an end-to-end failure exposes a real missing assertion.

**Interfaces:**
- Consumes all prior tasks; produces no new production interface.

- [ ] **Step 1: Run the full automated test suite in isolated artifacts**

```powershell
dotnet test tests/ConvenientNote.Tests/ConvenientNote.Tests.csproj --artifacts-path .codex-backup-final --logger "console;verbosity=normal"
```

Expected: all tests pass with zero failures.

- [ ] **Step 2: Exercise export/import against temporary app data**

Create a test workspace containing formatted text, a list with custom line spacing, tags, deleted/favorite/pinned states, and an inserted image. Export it, change the workspace, import the package, and verify all values return without restarting the process.

- [ ] **Step 3: Verify live refresh**

After import, inspect the title bar and active navigation page. Confirm the imported workspace name and imported note count are visible and no old note card remains.

- [ ] **Step 4: Verify failure behavior**

Attempt an unsupported-version package and a malformed ZIP. Confirm the current notes and media remain unchanged and only one error message appears.

- [ ] **Step 5: Run an independent code review**

Review package validation, path containment, replacement transaction boundaries, media restoration, dispatcher timers, and Prism view removal. Address every concrete P0/P1/P2 finding and rerun affected tests.

- [ ] **Step 6: Clean only generated test artifacts**

Resolve each `.codex-backup-*` path, verify it is inside `C:\便签\ConvenientNote`, then remove it. Do not remove user data or unrelated untracked files.

- [ ] **Step 7: Report working-tree status without committing**

Run `git status --short`, summarize changed files and verification evidence, and explicitly state that changes remain uncommitted.
