using System.IO;
using System.IO.Compression;
using System.Text.Json;
using ConvenientNote.Application.Abstractions;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;
using ConvenientNote.Infrastructure.Persistence;
using ConvenientNote.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ConvenientNote.Tests.Services;

public sealed class NotesBackupImportTests
{
    private static readonly WorkspaceId LocalWorkspaceId = new(
        Guid.Parse("90000000-0000-0000-0000-000000000001"));
    private static readonly NoteId OldActiveOneId = new(
        Guid.Parse("91000000-0000-0000-0000-000000000001"));
    private static readonly NoteId OldActiveTwoId = new(
        Guid.Parse("91000000-0000-0000-0000-000000000002"));
    private static readonly NoteId DeletedId = new(
        Guid.Parse("92000000-0000-0000-0000-000000000001"));
    private static readonly NoteId TodoId = new(
        Guid.Parse("93000000-0000-0000-0000-000000000001"));
    private static readonly NoteId ImportedOneId = new(
        Guid.Parse("94000000-0000-0000-0000-000000000001"));
    private static readonly NoteId ImportedTwoId = new(
        Guid.Parse("94000000-0000-0000-0000-000000000002"));

    [Fact]
    public async Task ImportOverwriteAsyncReplacesOnlyActiveNotesAndTheirMediaInRealSqliteWorkspace()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(temporaryDirectory, "workspace.db");
            var mediaRoot = Path.Combine(temporaryDirectory, "Media");
            var repository = new SqliteWorkspaceRepository(databasePath);
            await SeedOnlyWorkspaceAsync(repository, CreateCurrentWorkspace());
            await WriteMediaAsync(mediaRoot, OldActiveOneId, "old-one.png", [1, 1, 1]);
            await WriteMediaAsync(mediaRoot, OldActiveTwoId, "old-two.png", [2, 2, 2]);
            await WriteMediaAsync(mediaRoot, DeletedId, "deleted.png", [3, 3, 3]);
            await WriteMediaAsync(mediaRoot, TodoId, "todo.png", [4, 4, 4]);
            var packagePath = await CreateImportPackageAsync(temporaryDirectory);
            var service = new NotesBackupService(
                new WorkspaceApplicationService(repository),
                new NoteMediaService(mediaRoot));

            var result = await service.ImportOverwriteAsync(packagePath, CancellationToken.None);

            var stored = Assert.Single(await new SqliteWorkspaceRepository(databasePath).ListAsync());
            Assert.Equal(LocalWorkspaceId, stored.Id);
            Assert.Equal("本地工作区", stored.Name);
            Assert.Equal(2, result.NoteCount);
            Assert.Equal(
                [ImportedOneId, ImportedTwoId],
                stored.Notes
                    .Where(IsActiveNote)
                    .OrderBy(note => note.Id.Value)
                    .Select(note => note.Id)
                    .ToArray());
            Assert.Contains(stored.Notes, note => note.Id == DeletedId && note.IsDeleted && note.Title == "回收站笔记");
            Assert.Contains(stored.Notes, note => note.Id == TodoId && note.BoardKey == TodoBoardKeys.DayTodo && note.Title == "日待办");
            Assert.False(Directory.Exists(NoteDirectory(mediaRoot, OldActiveOneId)));
            Assert.False(Directory.Exists(NoteDirectory(mediaRoot, OldActiveTwoId)));
            Assert.Equal([9, 4, 1], await File.ReadAllBytesAsync(Path.Combine(NoteDirectory(mediaRoot, ImportedOneId), "one.png")));
            Assert.Equal([9, 4, 2], await File.ReadAllBytesAsync(Path.Combine(NoteDirectory(mediaRoot, ImportedTwoId), "nested", "two.png")));
            Assert.Equal([3, 3, 3], await File.ReadAllBytesAsync(Path.Combine(NoteDirectory(mediaRoot, DeletedId), "deleted.png")));
            Assert.Equal([4, 4, 4], await File.ReadAllBytesAsync(Path.Combine(NoteDirectory(mediaRoot, TodoId), "todo.png")));
            Assert.Empty(Directory.EnumerateDirectories(temporaryDirectory, "Media.rollback-*"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task ImportOverwriteAsyncInstallsMediaFromStagingBesideTheConfiguredMediaRoot()
    {
        // Returning import extraction to the process TEMP tree must make the simulated same-volume move reject it.
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(temporaryDirectory, "workspace.db");
            var mediaParent = Path.Combine(temporaryDirectory, "SimulatedMediaVolume");
            var mediaRoot = Path.Combine(mediaParent, "Media");
            var repository = new SqliteWorkspaceRepository(databasePath);
            await SeedOnlyWorkspaceAsync(repository, CreateCurrentWorkspace());
            await SeedCurrentMediaAsync(mediaRoot);
            var packagePath = await CreateImportPackageAsync(temporaryDirectory);
            var observedMoves = new List<(string Source, string Destination)>();
            var service = new NotesBackupService(
                new WorkspaceApplicationService(repository),
                new NoteMediaService(mediaRoot),
                File.Delete,
                (source, destination) =>
                {
                    if (!IsPathWithin(source, mediaParent))
                    {
                        throw new IOException("simulated cross-volume Directory.Move rejection");
                    }

                    observedMoves.Add((Path.GetFullPath(source), Path.GetFullPath(destination)));
                    Directory.Move(source, destination);
                });

            var result = await service.ImportOverwriteAsync(packagePath, CancellationToken.None);

            Assert.Equal(2, result.NoteCount);
            Assert.All(observedMoves, move => Assert.True(IsPathWithin(move.Source, mediaParent)));
            Assert.Contains(
                observedMoves,
                move => Path.GetFileName(move.Destination) == ImportedOneId.Value.ToString("N") &&
                        Path.GetDirectoryName(move.Source) is { } sourceParent &&
                        Path.GetFileName(sourceParent) == "media" &&
                        IsPathWithin(sourceParent, mediaParent));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task ImportOverwriteAsyncRestoresRealDatabaseAndAllMediaWhenRepositoryFailsAfterInstallation()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(temporaryDirectory, "workspace.db");
            var mediaRoot = Path.Combine(temporaryDirectory, "Media");
            var repository = new SqliteWorkspaceRepository(databasePath);
            await SeedOnlyWorkspaceAsync(repository, CreateCurrentWorkspace());
            await SeedCurrentMediaAsync(mediaRoot);
            SqliteConnection.ClearAllPools();
            var databaseBefore = await File.ReadAllBytesAsync(databasePath);
            var mediaBefore = SnapshotMedia(mediaRoot);
            var packagePath = await CreateImportPackageAsync(temporaryDirectory);
            var failingRepository = new FailingReplaceRepository(repository);
            var service = new NotesBackupService(
                new WorkspaceApplicationService(failingRepository),
                new NoteMediaService(mediaRoot));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ImportOverwriteAsync(packagePath, CancellationToken.None));

            SqliteConnection.ClearAllPools();
            Assert.Equal(databaseBefore, await File.ReadAllBytesAsync(databasePath));
            Assert.Equal(mediaBefore, SnapshotMedia(mediaRoot));
            Assert.Equal(1, failingRepository.ReplaceCallCount);
            Assert.Empty(Directory.EnumerateDirectories(temporaryDirectory, "Media.rollback-*"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task ImportOverwriteAsyncRestoresMediaWhenRealSqliteTransactionRollsBack()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(temporaryDirectory, "workspace.db");
            var mediaRoot = Path.Combine(temporaryDirectory, "Media");
            var repository = new SqliteWorkspaceRepository(databasePath);
            await SeedOnlyWorkspaceAsync(repository, CreateCurrentWorkspace());
            await SeedCurrentMediaAsync(mediaRoot);
            await CreateAbortImportedNoteTriggerAsync(databasePath);
            SqliteConnection.ClearAllPools();
            var databaseBefore = await File.ReadAllBytesAsync(databasePath);
            var mediaBefore = SnapshotMedia(mediaRoot);
            var packagePath = await CreateImportPackageAsync(temporaryDirectory);
            var service = new NotesBackupService(
                new WorkspaceApplicationService(repository),
                new NoteMediaService(mediaRoot));

            await Assert.ThrowsAnyAsync<Exception>(
                () => service.ImportOverwriteAsync(packagePath, CancellationToken.None));

            SqliteConnection.ClearAllPools();
            Assert.Equal(databaseBefore, await File.ReadAllBytesAsync(databasePath));
            Assert.Equal(mediaBefore, SnapshotMedia(mediaRoot));
            var stored = Assert.Single(await new SqliteWorkspaceRepository(databasePath).ListAsync());
            Assert.Equal(
                [OldActiveOneId, OldActiveTwoId],
                stored.Notes.Where(IsActiveNote).OrderBy(note => note.Id.Value).Select(note => note.Id).ToArray());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task ImportOverwriteAsyncAttemptsEveryRestoreAndKeepsRollbackWhenOneRestoreFails()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(temporaryDirectory, "workspace.db");
            var mediaRoot = Path.Combine(temporaryDirectory, "Media");
            var repository = new SqliteWorkspaceRepository(databasePath);
            await SeedOnlyWorkspaceAsync(repository, CreateCurrentWorkspace());
            await SeedCurrentMediaAsync(mediaRoot);
            var packagePath = await CreateImportPackageAsync(temporaryDirectory);
            var service = new NotesBackupService(
                new WorkspaceApplicationService(new BlockingRestoreRepository(repository, mediaRoot)),
                new NoteMediaService(mediaRoot));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ImportOverwriteAsync(packagePath, CancellationToken.None));

            Assert.Contains("could not be restored", error.Message, StringComparison.Ordinal);
            Assert.Equal(
                [1, 1, 1],
                await File.ReadAllBytesAsync(Path.Combine(NoteDirectory(mediaRoot, OldActiveOneId), "old-one.png")));
            Assert.False(Directory.Exists(NoteDirectory(mediaRoot, ImportedOneId)));
            Assert.False(Directory.Exists(NoteDirectory(mediaRoot, ImportedTwoId)));
            var rollbackRoot = Assert.Single(Directory.EnumerateDirectories(temporaryDirectory, "Media.rollback-*"));
            Assert.Equal(
                [2, 2, 2],
                await File.ReadAllBytesAsync(Path.Combine(NoteDirectory(rollbackRoot, OldActiveTwoId), "old-two.png")));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task ImportOverwriteAsyncReturnsCommittedResultWithoutPostCommitRepositoryReload()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(temporaryDirectory, "workspace.db");
            var mediaRoot = Path.Combine(temporaryDirectory, "Media");
            var repository = new SqliteWorkspaceRepository(databasePath);
            await SeedOnlyWorkspaceAsync(repository, CreateCurrentWorkspace());
            await SeedCurrentMediaAsync(mediaRoot);
            var packagePath = await CreateImportPackageAsync(temporaryDirectory);
            var boundaryEvents = new List<string>();
            var service = new NotesBackupService(
                new WorkspaceApplicationService(new PostCommitReadFailureRepository(repository, boundaryEvents)),
                new NoteMediaService(mediaRoot));

            var result = await service.ImportOverwriteAsync(packagePath, CancellationToken.None);

            Assert.Equal(2, result.NoteCount);
            Assert.Equal(["repository-replacement-complete"], boundaryEvents);
            var stored = Assert.Single(await new SqliteWorkspaceRepository(databasePath).ListAsync());
            Assert.Equal(
                [ImportedOneId, ImportedTwoId],
                stored.Notes.Where(IsActiveNote).OrderBy(note => note.Id.Value).Select(note => note.Id).ToArray());
            Assert.False(Directory.Exists(NoteDirectory(mediaRoot, OldActiveOneId)));
            Assert.False(Directory.Exists(NoteDirectory(mediaRoot, OldActiveTwoId)));
            Assert.Equal(
                [9, 4, 1],
                await File.ReadAllBytesAsync(Path.Combine(NoteDirectory(mediaRoot, ImportedOneId), "one.png")));
            Assert.Equal(
                [9, 4, 2],
                await File.ReadAllBytesAsync(Path.Combine(NoteDirectory(mediaRoot, ImportedTwoId), "nested", "two.png")));
            Assert.Empty(Directory.EnumerateDirectories(temporaryDirectory, "Media.rollback-*"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task ImportOverwriteAsyncSignalsCommitCallbackAtTheCommitBoundaryWithoutReload()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(temporaryDirectory, "workspace.db");
            var mediaRoot = Path.Combine(temporaryDirectory, "Media");
            var repository = new SqliteWorkspaceRepository(databasePath);
            await SeedOnlyWorkspaceAsync(repository, CreateCurrentWorkspace());
            await SeedCurrentMediaAsync(mediaRoot);
            var packagePath = await CreateImportPackageAsync(temporaryDirectory);
            var boundaryEvents = new List<string>();
            var failingRepository = new PostCommitReadFailureRepository(repository, boundaryEvents);
            var service = new NotesBackupService(
                new WorkspaceApplicationService(failingRepository),
                new NoteMediaService(mediaRoot));
            var callbackCount = 0;

            var result = await service.ImportOverwriteAsync(
                packagePath,
                () =>
                {
                    callbackCount++;
                    boundaryEvents.Add("service-callback");
                    var committed = Assert.Single(
                        new SqliteWorkspaceRepository(databasePath).ListAsync().GetAwaiter().GetResult());
                    Assert.Equal(
                        [ImportedOneId, ImportedTwoId],
                        committed.Notes
                            .Where(IsActiveNote)
                            .OrderBy(note => note.Id.Value)
                            .Select(note => note.Id)
                            .ToArray());
                },
                CancellationToken.None);

            Assert.Equal(2, result.NoteCount);
            Assert.Equal(1, callbackCount);
            Assert.Equal(
                ["repository-replacement-complete", "service-callback"],
                boundaryEvents);
            var stored = Assert.Single(await new SqliteWorkspaceRepository(databasePath).ListAsync());
            Assert.Equal(
                [ImportedOneId, ImportedTwoId],
                stored.Notes.Where(IsActiveNote).OrderBy(note => note.Id.Value).Select(note => note.Id).ToArray());
            Assert.False(Directory.Exists(NoteDirectory(mediaRoot, OldActiveOneId)));
            Assert.False(Directory.Exists(NoteDirectory(mediaRoot, OldActiveTwoId)));
            Assert.Equal(
                [9, 4, 1],
                await File.ReadAllBytesAsync(Path.Combine(NoteDirectory(mediaRoot, ImportedOneId), "one.png")));
            Assert.Equal(
                [9, 4, 2],
                await File.ReadAllBytesAsync(Path.Combine(NoteDirectory(mediaRoot, ImportedTwoId), "nested", "two.png")));
            Assert.Empty(Directory.EnumerateDirectories(temporaryDirectory, "Media.rollback-*"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task ImportOverwriteAsyncDoesNotMutateRealDatabaseOrMediaForInvalidPackages()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(temporaryDirectory, "workspace.db");
            var mediaRoot = Path.Combine(temporaryDirectory, "Media");
            var repository = new SqliteWorkspaceRepository(databasePath);
            await SeedOnlyWorkspaceAsync(repository, CreateCurrentWorkspace());
            await SeedCurrentMediaAsync(mediaRoot);
            SqliteConnection.ClearAllPools();
            var databaseBefore = await File.ReadAllBytesAsync(databasePath);
            var mediaBefore = SnapshotMedia(mediaRoot);
            var validJson = CreateDocumentJson([CreateNote(ImportedOneId, TodoBoardKeys.Notes, "导入")]);
            var deletedJson = validJson.Replace("\"isDeleted\":false", "\"isDeleted\":true", StringComparison.Ordinal);
            var validId = ImportedOneId.Value.ToString("N");
            var cases = new[]
            {
                new InvalidPackageCase("invalid-json", "{invalid", 1, Array.Empty<string>(), typeof(InvalidDataException)),
                new InvalidPackageCase("new-schema", "{\"notes\":[]}", 2, Array.Empty<string>(), typeof(UnsupportedNotesBackupSchemaException)),
                new InvalidPackageCase("deleted", deletedJson, 1, Array.Empty<string>(), typeof(InvalidDataException)),
                new InvalidPackageCase("traversal", validJson, 1, [$"media/{validId}/../../escape.png"], typeof(InvalidDataException)),
                new InvalidPackageCase("collision", validJson, 1, [$"media/{validId}/foo", $"media/{validId}/foo/"], typeof(InvalidDataException))
            };
            var service = new NotesBackupService(
                new WorkspaceApplicationService(repository),
                new NoteMediaService(mediaRoot));

            foreach (var invalidCase in cases)
            {
                var packagePath = Path.Combine(temporaryDirectory, $"{invalidCase.Name}.cnote");
                await CreateRawPackageAsync(
                    packagePath,
                    invalidCase.NotesJson,
                    invalidCase.SchemaVersion,
                    invalidCase.ExtraEntries);

                await Assert.ThrowsAsync(
                    invalidCase.ExceptionType,
                    () => service.ImportOverwriteAsync(packagePath, CancellationToken.None));

                SqliteConnection.ClearAllPools();
                Assert.Equal(databaseBefore, await File.ReadAllBytesAsync(databasePath));
                Assert.Equal(mediaBefore, SnapshotMedia(mediaRoot));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task ImportOverwriteAsyncRejectsEveryArchiveResourceLimitBeforeMutation()
    {
        // Removing any metadata limit must let its matching package replace the real workspace.
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(temporaryDirectory, "workspace.db");
            var mediaRoot = Path.Combine(temporaryDirectory, "Media");
            var repository = new SqliteWorkspaceRepository(databasePath);
            await SeedOnlyWorkspaceAsync(repository, CreateCurrentWorkspace());
            await SeedCurrentMediaAsync(mediaRoot);
            var packagePath = Path.Combine(temporaryDirectory, "resource-limits.cnote");
            await CreateResourceLimitPackageAsync(packagePath);
            SqliteConnection.ClearAllPools();
            var databaseBefore = await File.ReadAllBytesAsync(databasePath);
            var mediaBefore = SnapshotMedia(mediaRoot);
            var cases = new[]
            {
                ("entry-count", NotesBackupArchiveLimits.Default with { MaximumEntryCount = 2 }),
                ("manifest-bytes", NotesBackupArchiveLimits.Default with { MaximumManifestBytes = 32 }),
                ("notes-json-bytes", NotesBackupArchiveLimits.Default with { MaximumNotesJsonBytes = 64 }),
                ("note-count", NotesBackupArchiveLimits.Default with { MaximumNoteCount = 0 }),
                ("media-entry-bytes", NotesBackupArchiveLimits.Default with { MaximumMediaEntryBytes = 128 }),
                ("total-expanded-bytes", NotesBackupArchiveLimits.Default with { MaximumTotalExpandedBytes = 128 }),
                ("compression-ratio", NotesBackupArchiveLimits.Default with { MaximumCompressionRatio = 2 }),
                ("normalized-path-depth", NotesBackupArchiveLimits.Default with { MaximumNormalizedPathDepth = 2 })
            };

            foreach (var (name, limits) in cases)
            {
                var service = new NotesBackupService(
                    new WorkspaceApplicationService(repository),
                    new NoteMediaService(mediaRoot),
                    File.Delete,
                    limits: limits);

                var error = await Assert.ThrowsAsync<InvalidDataException>(
                    () => service.ImportOverwriteAsync(packagePath, CancellationToken.None));

                Assert.Contains("limit", error.Message, StringComparison.OrdinalIgnoreCase);
                SqliteConnection.ClearAllPools();
                Assert.Equal(databaseBefore, await File.ReadAllBytesAsync(databasePath));
                Assert.Equal(mediaBefore, SnapshotMedia(mediaRoot));
                Assert.True(File.Exists(packagePath), name);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task ImportOverwriteAsyncEnforcesStreamingLimitsWhenArchiveMetadataUnderreportsSize()
    {
        // Trusting only the declared ZIP length must let the 4 KiB media payload bypass the 128-byte limit.
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(temporaryDirectory, "workspace.db");
            var mediaRoot = Path.Combine(temporaryDirectory, "Media");
            var repository = new SqliteWorkspaceRepository(databasePath);
            await SeedOnlyWorkspaceAsync(repository, CreateCurrentWorkspace());
            await SeedCurrentMediaAsync(mediaRoot);
            var packagePath = Path.Combine(temporaryDirectory, "forged-length.cnote");
            await CreateResourceLimitPackageAsync(packagePath);
            SqliteConnection.ClearAllPools();
            var databaseBefore = await File.ReadAllBytesAsync(databasePath);
            var mediaBefore = SnapshotMedia(mediaRoot);
            var service = new NotesBackupService(
                new WorkspaceApplicationService(repository),
                new NoteMediaService(mediaRoot),
                File.Delete,
                limits: NotesBackupArchiveLimits.Default with
                {
                    MaximumMediaEntryBytes = 128,
                    MaximumTotalExpandedBytes = 1024
                },
                getDeclaredExpandedLength: static _ => 0);

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => service.ImportOverwriteAsync(packagePath, CancellationToken.None));

            Assert.Contains("stream", error.Message, StringComparison.OrdinalIgnoreCase);
            SqliteConnection.ClearAllPools();
            Assert.Equal(databaseBefore, await File.ReadAllBytesAsync(databasePath));
            Assert.Equal(mediaBefore, SnapshotMedia(mediaRoot));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task ImportOverwriteAsyncRejectsMalformedZipWithoutMutatingRealDatabaseOrMedia()
    {
        // Regression guard: swallowing or accepting ZipFile.OpenRead failures would allow malformed packages through.
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(temporaryDirectory, "workspace.db");
            var mediaRoot = Path.Combine(temporaryDirectory, "Media");
            var repository = new SqliteWorkspaceRepository(databasePath);
            await SeedOnlyWorkspaceAsync(repository, CreateCurrentWorkspace());
            await SeedCurrentMediaAsync(mediaRoot);
            SqliteConnection.ClearAllPools();
            var databaseBefore = await File.ReadAllBytesAsync(databasePath);
            var mediaBefore = SnapshotMedia(mediaRoot);
            var packagePath = Path.Combine(temporaryDirectory, "malformed.cnote");
            await File.WriteAllBytesAsync(packagePath, [0x4E, 0x4F, 0x54, 0x5F, 0x41, 0x5F, 0x5A, 0x49, 0x50]);
            var service = new NotesBackupService(
                new WorkspaceApplicationService(repository),
                new NoteMediaService(mediaRoot));

            await Assert.ThrowsAsync<InvalidDataException>(
                () => service.ImportOverwriteAsync(packagePath, CancellationToken.None));

            SqliteConnection.ClearAllPools();
            Assert.Equal(databaseBefore, await File.ReadAllBytesAsync(databasePath));
            Assert.Equal(mediaBefore, SnapshotMedia(mediaRoot));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task ExportThenImportAsyncRestoresFiveRichActiveNotesAndPreservesLocalStateInRealSqliteWorkspace()
    {
        // Regression guard: exporting preserved records, losing rich fields/media, or replacing workspace identity must fail.
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(temporaryDirectory, "workspace.db");
            var mediaRoot = Path.Combine(temporaryDirectory, "Media");
            var repository = new SqliteWorkspaceRepository(databasePath);
            var activeNotes = new[]
            {
                new Note(
                    new NoteId(Guid.Parse("a1000001-0000-0000-0000-000000000001")),
                    TodoBoardKeys.Notes,
                    "red",
                    "格式笔记一",
                    "字体、颜色、行距和列表正文",
                    new NotePosition(123.45, -67.89),
                    new NoteSize(456.75, 234.5),
                    "#12ABEF",
                    42,
                    true,
                    new DateTimeOffset(2026, 7, 2, 10, 15, 0, TimeSpan.FromHours(8)),
                    new DateTimeOffset(2026, 8, 31, 8, 5, 0, TimeSpan.FromHours(8)),
                    richContent: "{\"version\":1,\"blocks\":[{\"kind\":\"paragraph\",\"fontSize\":18,\"foreground\":\"#FF2244\",\"lineSpacing\":1.5,\"inlines\":[{\"kind\":\"text\",\"text\":\"格式内容\",\"bold\":true}]},{\"kind\":\"list\",\"items\":[\"一\",\"二\"]}]}",
                    notebookId: new NotebookId(Guid.Parse("a2000001-0000-0000-0000-000000000001")),
                    tags: ["工作", "导入导出"],
                    isPinned: true,
                    isFavorite: true),
                new Note(
                    new NoteId(Guid.Parse("a1000002-0000-0000-0000-000000000002")),
                    TodoBoardKeys.Notes,
                    "blue",
                    "图片笔记二",
                    "第二条正文",
                    new NotePosition(-10.5, 220.25),
                    new NoteSize(320, 180),
                    "#FFF8B8",
                    7,
                    false,
                    new DateTimeOffset(2026, 7, 3, 11, 0, 0, TimeSpan.FromHours(8)),
                    new DateTimeOffset(2026, 8, 30, 9, 30, 0, TimeSpan.FromHours(8)),
                    richContent: "{\"version\":1,\"blocks\":[{\"kind\":\"paragraph\",\"fontSize\":14,\"foreground\":\"#335577\",\"lineSpacing\":1.2,\"inlines\":[{\"kind\":\"image\",\"src\":\"inline.png\"}]}]}",
                    notebookId: new NotebookId(Guid.Parse("a2000002-0000-0000-0000-000000000002")),
                    tags: ["图片"],
                    isFavorite: true),
                new Note(
                    new NoteId(Guid.Parse("a1000003-0000-0000-0000-000000000003")),
                    TodoBoardKeys.Notes,
                    "green",
                    "嵌套媒体笔记三",
                    "第三条正文",
                    new NotePosition(0, 0),
                    new NoteSize(260, 150),
                    "#D1FAE5",
                    8,
                    false,
                    new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.FromHours(8)),
                    new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.FromHours(8)),
                    richContent: "{\"version\":1,\"blocks\":[{\"kind\":\"list\",\"lineSpacing\":1.8,\"items\":[\"alpha\",\"beta\"]}]}",
                    tags: ["媒体", "嵌套"],
                    isPinned: true),
                new Note(
                    new NoteId(Guid.Parse("a1000004-0000-0000-0000-000000000004")),
                    TodoBoardKeys.Notes,
                    "red",
                    "引用笔记四",
                    "第四条正文",
                    new NotePosition(9.5, 10.75),
                    new NoteSize(410, 210),
                    "#FEF3C7",
                    9,
                    true,
                    new DateTimeOffset(2026, 7, 5, 13, 0, 0, TimeSpan.FromHours(8)),
                    new DateTimeOffset(2026, 8, 28, 11, 0, 0, TimeSpan.FromHours(8)),
                    richContent: "{\"version\":1,\"blocks\":[{\"kind\":\"paragraph\",\"fontSize\":22,\"foreground\":\"#7C2D12\",\"lineSpacing\":2.0,\"inlines\":[{\"kind\":\"text\",\"text\":\"引用\",\"italic\":true}]}]}",
                    notebookId: new NotebookId(Guid.Parse("a2000004-0000-0000-0000-000000000004")),
                    tags: ["完成", "引用"]),
                new Note(
                    new NoteId(Guid.Parse("a1000005-0000-0000-0000-000000000005")),
                    TodoBoardKeys.Notes,
                    "blue",
                    "清单笔记五",
                    "第五条正文",
                    new NotePosition(500, 600),
                    new NoteSize(280, 190),
                    "#E0E7FF",
                    10,
                    false,
                    new DateTimeOffset(2026, 7, 6, 14, 0, 0, TimeSpan.FromHours(8)),
                    new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.FromHours(8)),
                    richContent: "{\"version\":1,\"blocks\":[{\"kind\":\"list\",\"fontSize\":16,\"foreground\":\"#1D4ED8\",\"lineSpacing\":1.35,\"items\":[\"待办 A\",\"待办 B\"]}]}",
                    tags: ["清单"],
                    isPinned: true,
                    isFavorite: true)
            };
            var deletedNote = new Note(
                new NoteId(Guid.Parse("a1000010-0000-0000-0000-000000000010")),
                TodoBoardKeys.Notes,
                "green",
                "回收站笔记",
                "不可替换的回收站正文",
                new NotePosition(11, 12),
                new NoteSize(300, 190),
                "#FEE2E2",
                60,
                false,
                new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.FromHours(8)),
                new DateTimeOffset(2026, 6, 2, 8, 0, 0, TimeSpan.FromHours(8)),
                richContent: "{\"version\":1,\"blocks\":[{\"kind\":\"paragraph\",\"text\":\"回收站\"}]}",
                tags: ["保留"],
                isDeleted: true);
            var dayTodo = new Note(
                new NoteId(Guid.Parse("a1000020-0000-0000-0000-000000000020")),
                TodoBoardKeys.DayTodo,
                "blue",
                "今日待办",
                "待办正文",
                new NotePosition(21, 22),
                new NoteSize(220, 130),
                "#DBEAFE",
                70,
                false,
                new DateTimeOffset(2026, 6, 3, 8, 0, 0, TimeSpan.FromHours(8)),
                new DateTimeOffset(2026, 6, 4, 8, 0, 0, TimeSpan.FromHours(8)),
                richContent: "{\"version\":1,\"blocks\":[]}",
                tags: ["待办"]);
            var inboxTodo = new Note(
                new NoteId(Guid.Parse("a1000021-0000-0000-0000-000000000021")),
                "inbox-todo",
                "red",
                "收件箱待办",
                "收件箱正文",
                new NotePosition(31, 32),
                new NoteSize(230, 140),
                "#FCE7F3",
                71,
                false,
                new DateTimeOffset(2026, 6, 5, 8, 0, 0, TimeSpan.FromHours(8)),
                new DateTimeOffset(2026, 6, 6, 8, 0, 0, TimeSpan.FromHours(8)),
                richContent: "{\"version\":1,\"blocks\":[]}",
                tags: ["收件箱"],
                isPinned: true);
            var completedTodo = new Note(
                new NoteId(Guid.Parse("a1000022-0000-0000-0000-000000000022")),
                "completed-todo",
                "green",
                "已完成待办",
                "已完成正文",
                new NotePosition(41, 42),
                new NoteSize(240, 150),
                "#DCFCE7",
                72,
                true,
                new DateTimeOffset(2026, 6, 7, 8, 0, 0, TimeSpan.FromHours(8)),
                new DateTimeOffset(2026, 6, 8, 8, 0, 0, TimeSpan.FromHours(8)),
                richContent: "{\"version\":1,\"blocks\":[]}",
                tags: ["已完成"],
                isFavorite: true);
            var workspace = new Workspace(
                LocalWorkspaceId,
                "本地工作区 - 回环",
                new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.FromHours(8)),
                new DateTimeOffset(2026, 5, 2, 8, 0, 0, TimeSpan.FromHours(8)),
                [.. activeNotes, deletedNote, dayTodo, inboxTodo, completedTodo]);
            await SeedOnlyWorkspaceAsync(repository, workspace);
            await WriteMediaAsync(mediaRoot, activeNotes[0].Id, "cover.png", [1, 2, 3]);
            await WriteMediaAsync(mediaRoot, activeNotes[1].Id, "inline.png", [4, 5, 6]);
            await WriteMediaAsync(mediaRoot, activeNotes[2].Id, Path.Combine("nested", "photo.png"), [7, 8, 9]);
            await WriteMediaAsync(mediaRoot, activeNotes[3].Id, "quote.jpg", [10, 11, 12]);
            await WriteMediaAsync(mediaRoot, activeNotes[4].Id, "list.png", [13, 14, 15]);
            await WriteMediaAsync(mediaRoot, deletedNote.Id, "deleted.png", [16, 17, 18]);
            await WriteMediaAsync(mediaRoot, dayTodo.Id, "day.png", [19, 20, 21]);
            await WriteMediaAsync(mediaRoot, inboxTodo.Id, "inbox.png", [22, 23, 24]);
            await WriteMediaAsync(mediaRoot, completedTodo.Id, "completed.png", [25, 26, 27]);
            var service = new NotesBackupService(
                new WorkspaceApplicationService(repository),
                new NoteMediaService(mediaRoot));
            var packagePath = Path.Combine(temporaryDirectory, "round-trip.cnote");

            var export = await service.ExportAsync(packagePath, CancellationToken.None);

            Assert.Equal(5, export.NoteCount);
            await repository.ReplaceActiveNotesAsync(
                workspace.Id,
                [
                    new Note(
                        new NoteId(Guid.Parse("a1000099-0000-0000-0000-000000000099")),
                        TodoBoardKeys.Notes,
                        "blue",
                        "导出后本地修改",
                        "应被导入覆盖",
                        new NotePosition(1, 1),
                        new NoteSize(120, 100),
                        "#FFFFFF",
                        1,
                        false,
                        new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.FromHours(8)),
                        new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.FromHours(8)))
                ]);
            foreach (var activeNote in activeNotes)
            {
                Directory.Delete(NoteDirectory(mediaRoot, activeNote.Id), recursive: true);
            }

            var mutatedNoteId = new NoteId(Guid.Parse("a1000099-0000-0000-0000-000000000099"));
            await WriteMediaAsync(mediaRoot, mutatedNoteId, "mutated.png", [99, 98, 97]);

            var imported = await service.ImportOverwriteAsync(packagePath, CancellationToken.None);

            Assert.Equal(5, imported.NoteCount);
            SqliteConnection.ClearAllPools();
            var restored = Assert.Single(await new SqliteWorkspaceRepository(databasePath).ListAsync());
            Assert.Equal(LocalWorkspaceId, restored.Id);
            Assert.Equal("本地工作区 - 回环", restored.Name);
            Assert.Equal(new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.FromHours(8)), restored.CreatedAt);
            Assert.Equal(new DateTimeOffset(2026, 5, 2, 8, 0, 0, TimeSpan.FromHours(8)), restored.UpdatedAt);
            var restoredActive = restored.Notes.Where(IsActiveNote).OrderBy(note => note.Id.Value).ToArray();
            Assert.Equal(
                activeNotes.OrderBy(note => note.Id.Value).Select(note => note.Id).ToArray(),
                restoredActive.Select(note => note.Id).ToArray());
            foreach (var expected in activeNotes)
            {
                AssertExactNote(expected, Assert.Single(restoredActive, actual => actual.Id == expected.Id));
            }

            AssertExactNote(deletedNote, Assert.Single(restored.Notes, note => note.Id == deletedNote.Id));
            AssertExactNote(dayTodo, Assert.Single(restored.Notes, note => note.Id == dayTodo.Id));
            AssertExactNote(inboxTodo, Assert.Single(restored.Notes, note => note.Id == inboxTodo.Id));
            AssertExactNote(completedTodo, Assert.Single(restored.Notes, note => note.Id == completedTodo.Id));
            Assert.DoesNotContain(restored.Notes, note => note.Id == mutatedNoteId);
            Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(Path.Combine(NoteDirectory(mediaRoot, activeNotes[0].Id), "cover.png")));
            Assert.Equal([4, 5, 6], await File.ReadAllBytesAsync(Path.Combine(NoteDirectory(mediaRoot, activeNotes[1].Id), "inline.png")));
            Assert.Equal([7, 8, 9], await File.ReadAllBytesAsync(Path.Combine(NoteDirectory(mediaRoot, activeNotes[2].Id), "nested", "photo.png")));
            Assert.Equal([10, 11, 12], await File.ReadAllBytesAsync(Path.Combine(NoteDirectory(mediaRoot, activeNotes[3].Id), "quote.jpg")));
            Assert.Equal([13, 14, 15], await File.ReadAllBytesAsync(Path.Combine(NoteDirectory(mediaRoot, activeNotes[4].Id), "list.png")));
            Assert.Equal([16, 17, 18], await File.ReadAllBytesAsync(Path.Combine(NoteDirectory(mediaRoot, deletedNote.Id), "deleted.png")));
            Assert.Equal([19, 20, 21], await File.ReadAllBytesAsync(Path.Combine(NoteDirectory(mediaRoot, dayTodo.Id), "day.png")));
            Assert.Equal([22, 23, 24], await File.ReadAllBytesAsync(Path.Combine(NoteDirectory(mediaRoot, inboxTodo.Id), "inbox.png")));
            Assert.Equal([25, 26, 27], await File.ReadAllBytesAsync(Path.Combine(NoteDirectory(mediaRoot, completedTodo.Id), "completed.png")));
            Assert.False(Directory.Exists(NoteDirectory(mediaRoot, mutatedNoteId)));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task ImportOverwriteAsyncRejectsImportedIdCollisionBeforeMutation()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(temporaryDirectory, "workspace.db");
            var mediaRoot = Path.Combine(temporaryDirectory, "Media");
            var repository = new SqliteWorkspaceRepository(databasePath);
            await SeedOnlyWorkspaceAsync(repository, CreateCurrentWorkspace());
            await SeedCurrentMediaAsync(mediaRoot);
            SqliteConnection.ClearAllPools();
            var databaseBefore = await File.ReadAllBytesAsync(databasePath);
            var mediaBefore = SnapshotMedia(mediaRoot);
            var packagePath = Path.Combine(temporaryDirectory, "id-collision.cnote");
            await CreateRawPackageAsync(
                packagePath,
                CreateDocumentJson([CreateNote(DeletedId, TodoBoardKeys.Notes, "冲突导入")]),
                1,
                []);
            var service = new NotesBackupService(
                new WorkspaceApplicationService(repository),
                new NoteMediaService(mediaRoot));

            await Assert.ThrowsAnyAsync<Exception>(
                () => service.ImportOverwriteAsync(packagePath, CancellationToken.None));

            SqliteConnection.ClearAllPools();
            Assert.Equal(databaseBefore, await File.ReadAllBytesAsync(databasePath));
            Assert.Equal(mediaBefore, SnapshotMedia(mediaRoot));
            Assert.Empty(Directory.EnumerateDirectories(temporaryDirectory, "Media.rollback-*"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task ImportOverwriteAsyncHonorsCancellationBeforeDestructiveMediaMoves()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(temporaryDirectory, "workspace.db");
            var mediaRoot = Path.Combine(temporaryDirectory, "Media");
            var repository = new SqliteWorkspaceRepository(databasePath);
            await SeedOnlyWorkspaceAsync(repository, CreateCurrentWorkspace());
            await SeedCurrentMediaAsync(mediaRoot);
            var packagePath = await CreateImportPackageAsync(temporaryDirectory);
            SqliteConnection.ClearAllPools();
            var databaseBefore = await File.ReadAllBytesAsync(databasePath);
            var mediaBefore = SnapshotMedia(mediaRoot);
            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.Cancel();
            var service = new NotesBackupService(
                new WorkspaceApplicationService(repository),
                new NoteMediaService(mediaRoot));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.ImportOverwriteAsync(packagePath, cancellationSource.Token));

            SqliteConnection.ClearAllPools();
            Assert.Equal(databaseBefore, await File.ReadAllBytesAsync(databasePath));
            Assert.Equal(mediaBefore, SnapshotMedia(mediaRoot));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreMoveCancellationRemovesOnlyANewEmptyMediaRoot(bool mediaRootAlreadyExisted)
    {
        // Leaving a newly-created empty root, or deleting a pre-existing root, must make one row fail.
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var mediaRoot = Path.Combine(temporaryDirectory, "Media");
            if (mediaRootAlreadyExisted)
            {
                Directory.CreateDirectory(mediaRoot);
            }

            var packagePath = await CreateImportPackageAsync(temporaryDirectory);
            using var cancellationSource = new CancellationTokenSource();
            var repository = new CancelAfterListRepository(CreateCurrentWorkspace(), cancellationSource);
            var service = new NotesBackupService(
                new WorkspaceApplicationService(repository),
                new NoteMediaService(mediaRoot));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.ImportOverwriteAsync(packagePath, cancellationSource.Token));

            Assert.Equal(mediaRootAlreadyExisted, Directory.Exists(mediaRoot));
            Assert.Equal(0, repository.ReplaceCallCount);
            Assert.Empty(Directory.EnumerateDirectories(temporaryDirectory, "Media.import-*"));
            Assert.Empty(Directory.EnumerateDirectories(temporaryDirectory, "Media.rollback-*"));
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task ImportOverwriteAsyncRejectsOrphanMediaForImportedNoteWithoutPackagedMedia()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(temporaryDirectory, "workspace.db");
            var mediaRoot = Path.Combine(temporaryDirectory, "Media");
            var repository = new SqliteWorkspaceRepository(databasePath);
            await SeedOnlyWorkspaceAsync(repository, CreateCurrentWorkspace());
            await SeedCurrentMediaAsync(mediaRoot);
            await WriteMediaAsync(mediaRoot, ImportedOneId, "orphan.png", [6, 6, 6]);
            var packagePath = Path.Combine(temporaryDirectory, "no-packaged-media.cnote");
            await CreateRawPackageAsync(
                packagePath,
                CreateDocumentJson([CreateNote(ImportedOneId, TodoBoardKeys.Notes, "无包内媒体")]),
                1,
                []);
            SqliteConnection.ClearAllPools();
            var databaseBefore = await File.ReadAllBytesAsync(databasePath);
            var mediaBefore = SnapshotMedia(mediaRoot);
            var service = new NotesBackupService(
                new WorkspaceApplicationService(repository),
                new NoteMediaService(mediaRoot));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ImportOverwriteAsync(packagePath, CancellationToken.None));

            SqliteConnection.ClearAllPools();
            Assert.Equal(databaseBefore, await File.ReadAllBytesAsync(databasePath));
            Assert.Equal(mediaBefore, SnapshotMedia(mediaRoot));
            Assert.Empty(Directory.EnumerateDirectories(temporaryDirectory, "Media.rollback-*"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(temporaryDirectory);
        }
    }

    private static bool IsActiveNote(Note note) =>
        note.BoardKey == TodoBoardKeys.Notes && !note.IsDeleted;

    private static void AssertExactNote(Note expected, Note actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.BoardKey, actual.BoardKey);
        Assert.Equal(expected.Priority, actual.Priority);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Content, actual.Content);
        Assert.Equal(expected.Position.X, actual.Position.X);
        Assert.Equal(expected.Position.Y, actual.Position.Y);
        Assert.Equal(expected.Size.Width, actual.Size.Width);
        Assert.Equal(expected.Size.Height, actual.Size.Height);
        Assert.Equal(expected.Color, actual.Color);
        Assert.Equal(expected.ZIndex, actual.ZIndex);
        Assert.Equal(expected.IsCompleted, actual.IsCompleted);
        Assert.Equal(expected.RichContent, actual.RichContent);
        Assert.Equal(expected.NotebookId, actual.NotebookId);
        Assert.Equal(expected.Tags, actual.Tags);
        Assert.Equal(expected.IsPinned, actual.IsPinned);
        Assert.Equal(expected.IsFavorite, actual.IsFavorite);
        Assert.Equal(expected.IsDeleted, actual.IsDeleted);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.UpdatedAt, actual.UpdatedAt);
    }

    private static async Task SeedOnlyWorkspaceAsync(
        SqliteWorkspaceRepository repository,
        Workspace workspace)
    {
        foreach (var existingWorkspace in await repository.ListAsync())
        {
            await repository.DeleteAsync(existingWorkspace.Id);
        }

        await repository.SaveAsync(workspace);
    }

    private static Workspace CreateCurrentWorkspace() => new(
        LocalWorkspaceId,
        "本地工作区",
        new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero),
        [
            CreateNote(OldActiveOneId, TodoBoardKeys.Notes, "旧活动笔记一"),
            CreateNote(OldActiveTwoId, TodoBoardKeys.Notes, "旧活动笔记二"),
            CreateNote(DeletedId, TodoBoardKeys.Notes, "回收站笔记", isDeleted: true),
            CreateNote(TodoId, TodoBoardKeys.DayTodo, "日待办")
        ]);

    private static Workspace CreateImportedWorkspace() => new(
        new WorkspaceId(Guid.Parse("99000000-0000-0000-0000-000000000001")),
        "包内工作区身份不得导入",
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        [
            CreateNote(ImportedOneId, TodoBoardKeys.Notes, "导入笔记一"),
            CreateNote(ImportedTwoId, TodoBoardKeys.Notes, "导入笔记二")
        ]);

    private static Note CreateNote(NoteId id, string boardKey, string title, bool isDeleted = false) => new(
        id,
        boardKey,
        "blue",
        title,
        $"{title} 正文",
        new NotePosition(10, 20),
        new NoteSize(260, 150),
        "#FFF8B8",
        1,
        false,
        new DateTimeOffset(2026, 8, 1, 1, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 2, 1, 0, 0, TimeSpan.Zero),
        isDeleted: isDeleted);

    private static async Task SeedCurrentMediaAsync(string mediaRoot)
    {
        await WriteMediaAsync(mediaRoot, OldActiveOneId, "old-one.png", [1, 1, 1]);
        await WriteMediaAsync(mediaRoot, OldActiveTwoId, "old-two.png", [2, 2, 2]);
        await WriteMediaAsync(mediaRoot, DeletedId, "deleted.png", [3, 3, 3]);
        await WriteMediaAsync(mediaRoot, TodoId, "todo.png", [4, 4, 4]);
    }

    private static async Task<string> CreateImportPackageAsync(string temporaryDirectory)
    {
        var sourceMediaRoot = Path.Combine(temporaryDirectory, $"SourceMedia-{Guid.NewGuid():N}");
        await WriteMediaAsync(sourceMediaRoot, ImportedOneId, "one.png", [9, 4, 1]);
        await WriteMediaAsync(sourceMediaRoot, ImportedTwoId, Path.Combine("nested", "two.png"), [9, 4, 2]);
        var packagePath = Path.Combine(temporaryDirectory, $"import-{Guid.NewGuid():N}.cnote");
        var service = new NotesBackupService(
            new WorkspaceApplicationService(new InMemoryRepository(CreateImportedWorkspace())),
            new NoteMediaService(sourceMediaRoot));
        await service.ExportAsync(packagePath, CancellationToken.None);
        return packagePath;
    }

    private static string CreateDocumentJson(IEnumerable<Note> notes)
    {
        var snapshots = notes.Select(note => new NoteSnapshot(
            note.Id,
            note.BoardKey,
            note.Priority,
            note.Title,
            note.Content,
            note.Position.X,
            note.Position.Y,
            note.Size.Width,
            note.Size.Height,
            note.Color,
            note.ZIndex,
            note.IsCompleted,
            note.RichContent,
            note.NotebookId,
            note.Tags,
            note.IsPinned,
            note.IsFavorite,
            note.IsDeleted,
            note.CreatedAt,
            note.UpdatedAt));
        return JsonSerializer.Serialize(
            NotesBackupSerializer.CreateDocument(snapshots),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static async Task CreateRawPackageAsync(
        string packagePath,
        string notesJson,
        int schemaVersion,
        IReadOnlyCollection<string> extraEntries)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var manifestEntry = archive.CreateEntry("manifest.json");
        await using (var manifestStream = manifestEntry.Open())
        {
            await JsonSerializer.SerializeAsync(
                manifestStream,
                new NotesBackupManifest(
                    "convenient-note-notes-backup",
                    schemaVersion,
                    "1.0.0",
                    DateTimeOffset.UtcNow),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }

        var notesEntry = archive.CreateEntry("notes.json");
        await using (var writer = new StreamWriter(notesEntry.Open()))
        {
            await writer.WriteAsync(notesJson);
        }

        foreach (var entryName in extraEntries)
        {
            var entry = archive.CreateEntry(entryName);
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("entry");
        }
    }

    private static async Task CreateResourceLimitPackageAsync(string packagePath)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.NoCompression);
        await using (var manifestStream = manifestEntry.Open())
        {
            await JsonSerializer.SerializeAsync(
                manifestStream,
                new NotesBackupManifest(
                    "convenient-note-notes-backup",
                    1,
                    "1.0.0",
                    DateTimeOffset.UtcNow),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }

        var notesEntry = archive.CreateEntry("notes.json", CompressionLevel.NoCompression);
        await using (var writer = new StreamWriter(notesEntry.Open()))
        {
            await writer.WriteAsync(CreateDocumentJson([
                CreateNote(ImportedOneId, TodoBoardKeys.Notes, "资源限制导入")
            ]));
        }

        var mediaEntry = archive.CreateEntry(
            $"media/{ImportedOneId.Value:N}/payload.bin",
            CompressionLevel.Optimal);
        await using var mediaStream = mediaEntry.Open();
        await mediaStream.WriteAsync(new byte[4096]);
    }

    private static async Task WriteMediaAsync(
        string mediaRoot,
        NoteId noteId,
        string relativePath,
        byte[] bytes)
    {
        var path = Path.Combine(mediaRoot, noteId.Value.ToString("N"), relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes);
    }

    private static async Task CreateAbortImportedNoteTriggerAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE TRIGGER AbortNotesBackupInsert
            BEFORE INSERT ON Notes
            WHEN NEW.Id = '{ImportedOneId.Value}'
            BEGIN
                SELECT RAISE(ABORT, 'notes backup insert blocked');
            END;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static string NoteDirectory(string mediaRoot, NoteId noteId) =>
        Path.Combine(mediaRoot, noteId.Value.ToString("N"));

    private static bool IsPathWithin(string path, string root)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static (string Path, string Bytes)[] SnapshotMedia(string mediaRoot) =>
        Directory.Exists(mediaRoot)
            ? Directory.EnumerateFiles(mediaRoot, "*", SearchOption.AllDirectories)
                .Select(path => (
                    Path.GetRelativePath(mediaRoot, path).Replace('\\', '/'),
                    Convert.ToBase64String(File.ReadAllBytes(path))))
                .OrderBy(item => item.Item1, StringComparer.Ordinal)
                .ToArray()
            : [];

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ConvenientNote.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed record InvalidPackageCase(
        string Name,
        string NotesJson,
        int SchemaVersion,
        IReadOnlyCollection<string> ExtraEntries,
        Type ExceptionType);

    private sealed class InMemoryRepository(Workspace workspace) : IWorkspaceRepository
    {
        private Workspace _workspace = workspace;

        public Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Workspace>>([_workspace]);

        public Task<Workspace?> GetAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Workspace?>(_workspace.Id == workspaceId ? _workspace : null);

        public Task SaveAsync(Workspace workspace, CancellationToken cancellationToken = default)
        {
            _workspace = workspace;
            return Task.CompletedTask;
        }

        public Task ReplaceActiveNotesAsync(
            WorkspaceId workspaceId,
            IReadOnlyCollection<Note> importedNotes,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FailingReplaceRepository(IWorkspaceRepository inner) : IWorkspaceRepository
    {
        public int ReplaceCallCount { get; private set; }

        public Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken = default) =>
            inner.ListAsync(cancellationToken);

        public Task<Workspace?> GetAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) =>
            inner.GetAsync(workspaceId, cancellationToken);

        public Task SaveAsync(Workspace workspace, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(workspace, cancellationToken);

        public Task ReplaceActiveNotesAsync(
            WorkspaceId workspaceId,
            IReadOnlyCollection<Note> importedNotes,
            CancellationToken cancellationToken = default)
        {
            ReplaceCallCount++;
            throw new InvalidOperationException("replacement failed after media installation");
        }

        public Task DeleteAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(workspaceId, cancellationToken);
    }

    private sealed class BlockingRestoreRepository(IWorkspaceRepository inner, string mediaRoot)
        : IWorkspaceRepository
    {
        public Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken = default) =>
            inner.ListAsync(cancellationToken);

        public Task<Workspace?> GetAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) =>
            inner.GetAsync(workspaceId, cancellationToken);

        public Task SaveAsync(Workspace workspace, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(workspace, cancellationToken);

        public Task ReplaceActiveNotesAsync(
            WorkspaceId workspaceId,
            IReadOnlyCollection<Note> importedNotes,
            CancellationToken cancellationToken = default)
        {
            var blockingDirectory = NoteDirectory(mediaRoot, OldActiveTwoId);
            Directory.CreateDirectory(blockingDirectory);
            File.WriteAllBytes(Path.Combine(blockingDirectory, "blocker.png"), [8, 8, 8]);
            throw new InvalidOperationException("replacement failed before commit");
        }

        public Task DeleteAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(workspaceId, cancellationToken);
    }

    private sealed class PostCommitReadFailureRepository(
        IWorkspaceRepository inner,
        ICollection<string>? boundaryEvents = null) : IWorkspaceRepository
    {
        private bool _replacementCommitted;

        public Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken = default) =>
            inner.ListAsync(cancellationToken);

        public Task<Workspace?> GetAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default)
        {
            if (!_replacementCommitted)
            {
                return inner.GetAsync(workspaceId, cancellationToken);
            }

            boundaryEvents?.Add("post-commit-reload");
            return Task.FromException<Workspace?>(new IOException("post-commit reload failed"));
        }

        public Task SaveAsync(Workspace workspace, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(workspace, cancellationToken);

        public async Task ReplaceActiveNotesAsync(
            WorkspaceId workspaceId,
            IReadOnlyCollection<Note> importedNotes,
            CancellationToken cancellationToken = default)
        {
            await inner.ReplaceActiveNotesAsync(workspaceId, importedNotes, cancellationToken);
            _replacementCommitted = true;
            boundaryEvents?.Add("repository-replacement-complete");
        }

        public Task DeleteAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(workspaceId, cancellationToken);
    }

    private sealed class CancelAfterListRepository(
        Workspace workspace,
        CancellationTokenSource cancellationSource) : IWorkspaceRepository
    {
        public int ReplaceCallCount { get; private set; }

        public Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken = default)
        {
            cancellationSource.Cancel();
            return Task.FromResult<IReadOnlyList<Workspace>>([workspace]);
        }

        public Task<Workspace?> GetAsync(
            WorkspaceId workspaceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Workspace?>(workspace.Id == workspaceId ? workspace : null);

        public Task SaveAsync(Workspace savedWorkspace, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ReplaceActiveNotesAsync(
            WorkspaceId workspaceId,
            IReadOnlyCollection<Note> importedNotes,
            CancellationToken cancellationToken = default)
        {
            ReplaceCallCount++;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
