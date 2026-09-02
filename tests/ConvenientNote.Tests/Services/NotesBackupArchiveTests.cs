using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using ConvenientNote.Application.Abstractions;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;
using ConvenientNote.Services;
using Xunit;

namespace ConvenientNote.Tests.Services;

public sealed class NotesBackupArchiveTests
{
    private const string CompleteManifestJson = """
        {
          "format": "convenient-note-notes-backup",
          "schemaVersion": 1,
          "appVersion": "1.0.0",
          "exportedAtUtc": "2026-09-01T03:00:00+00:00"
        }
        """;

    public static IEnumerable<object[]> RequiredManifestProperties() =>
        new[] { "format", "schemaVersion", "appVersion", "exportedAtUtc" }
            .Select(static propertyName => new object[] { propertyName });

    [Fact]
    public async Task ExportAsyncPackagesExactlyFiveActiveNotesAndOnlyTheirMedia()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var activeNotes = Enumerable.Range(1, 5)
                .Select(index => CreateNote(
                    Guid.Parse($"10000000-0000-0000-0000-{index:D12}"),
                    TodoBoardKeys.Notes,
                    $"活动笔记 {index}"))
                .ToList();
            var deletedNote = CreateNote(
                Guid.Parse("20000000-0000-0000-0000-000000000001"),
                TodoBoardKeys.Notes,
                "回收站笔记",
                isDeleted: true);
            var todo = CreateNote(
                Guid.Parse("30000000-0000-0000-0000-000000000001"),
                TodoBoardKeys.DayTodo,
                "待办");
            var workspace = CreateWorkspace([.. activeNotes, deletedNote, todo]);
            var mediaRoot = Path.Combine(temporaryDirectory, "Media");
            await WriteMediaAsync(mediaRoot, activeNotes[0].Id, "active.png", [1, 2, 3]);
            await WriteMediaAsync(mediaRoot, deletedNote.Id, "deleted.png", [4, 5, 6]);
            await WriteMediaAsync(mediaRoot, todo.Id, "todo.png", [7, 8, 9]);
            var orphanId = new NoteId(Guid.Parse("40000000-0000-0000-0000-000000000001"));
            await WriteMediaAsync(mediaRoot, orphanId, "orphan.png", [10, 11, 12]);
            var destination = Path.Combine(temporaryDirectory, "notes.cnote");
            var service = new NotesBackupService(
                new WorkspaceApplicationService(new InMemoryRepository(workspace)),
                new NoteMediaService(mediaRoot));

            var result = await service.ExportAsync(destination, CancellationToken.None);

            Assert.Equal(Path.GetFullPath(destination), result.PackagePath);
            Assert.Equal(5, result.NoteCount);
            using var archive = ZipFile.OpenRead(destination);
            Assert.Equal(
                [
                    "manifest.json",
                    $"media/{activeNotes[0].Id.Value:N}/active.png",
                    "notes.json"
                ],
                archive.Entries.Select(entry => entry.FullName).OrderBy(name => name).ToArray());
            var manifestEntry = Assert.Single(archive.Entries, entry => entry.FullName == "manifest.json");
            await using (var manifestStream = manifestEntry.Open())
            {
                var manifest = await JsonSerializer.DeserializeAsync<NotesBackupManifest>(
                    manifestStream,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                Assert.NotNull(manifest);
                Assert.Equal("convenient-note-notes-backup", manifest.Format);
                Assert.Equal(1, manifest.SchemaVersion);
            }

            var notesEntry = Assert.Single(archive.Entries, entry => entry.FullName == "notes.json");
            await using var notesStream = notesEntry.Open();
            var document = await NotesBackupSerializer.ReadDocumentAsync(notesStream);
            Assert.Equal(5, document.Notes.Count);
            Assert.All(document.Notes, note =>
            {
                Assert.Equal(TodoBoardKeys.Notes, note.BoardKey);
                Assert.False(note.IsDeleted);
            });
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task ExportAsyncDoesNotOverwriteDestinationWhenCancellationIsRequested()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var destination = Path.Combine(temporaryDirectory, "notes.cnote");
            await File.WriteAllBytesAsync(destination, [9, 8, 7]);
            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.Cancel();
            var service = new NotesBackupService(
                new WorkspaceApplicationService(new InMemoryRepository(CreateWorkspace([]))),
                new NoteMediaService(Path.Combine(temporaryDirectory, "Media")));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.ExportAsync(destination, cancellationSource.Token));

            Assert.Equal([9, 8, 7], await File.ReadAllBytesAsync(destination));
            Assert.Empty(Directory.EnumerateFiles(temporaryDirectory, "notes.cnote.tmp-*"));
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task ExportAsyncCleanupFailureDoesNotMaskPrimaryMediaReadFailure()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var note = CreateNote(
                Guid.Parse("41000000-0000-0000-0000-000000000001"),
                TodoBoardKeys.Notes,
                "锁定媒体");
            var mediaRoot = Path.Combine(temporaryDirectory, "Media");
            var mediaPath = Path.Combine(mediaRoot, note.Id.Value.ToString("N"), "locked.png");
            Directory.CreateDirectory(Path.GetDirectoryName(mediaPath)!);
            await File.WriteAllBytesAsync(mediaPath, [1, 2, 3]);
            await using var mediaLock = new FileStream(
                mediaPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None,
                bufferSize: 1,
                useAsync: true);
            var service = new NotesBackupService(
                new WorkspaceApplicationService(new InMemoryRepository(CreateWorkspace([note]))),
                new NoteMediaService(mediaRoot),
                _ => throw new InvalidOperationException("temporary cleanup failed"));

            var error = await Assert.ThrowsAsync<IOException>(
                () => service.ExportAsync(Path.Combine(temporaryDirectory, "notes.cnote")));

            Assert.DoesNotContain("temporary cleanup failed", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task InspectAsyncReturnsNotesPreviewForExactFormatAndSchema()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var exportedAt = new DateTimeOffset(2026, 9, 1, 3, 0, 0, TimeSpan.Zero);
            var packagePath = Path.Combine(temporaryDirectory, "inspect.cnote");
            await CreatePackageAsync(
                packagePath,
                CreateDocumentJson([
                    CreateNote(Guid.Parse("50000000-0000-0000-0000-000000000001"), TodoBoardKeys.Notes, "一"),
                    CreateNote(Guid.Parse("50000000-0000-0000-0000-000000000002"), TodoBoardKeys.Notes, "二")
                ]),
                exportedAtUtc: exportedAt);
            var service = CreateInspectionService(temporaryDirectory);

            var preview = await service.InspectAsync(packagePath, CancellationToken.None);

            Assert.Equal(2, preview.NoteCount);
            Assert.Equal(exportedAt, preview.ExportedAtUtc);
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Theory]
    [MemberData(nameof(RequiredManifestProperties))]
    public async Task InspectAsyncRejectsEveryOmittedManifestField(string propertyName)
    {
        // Every schema constructor parameter remains required even when its CLR default is otherwise invalid too.
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var manifest = JsonNode.Parse(CompleteManifestJson)!.AsObject();
            Assert.True(manifest.Remove(propertyName));
            var packagePath = Path.Combine(temporaryDirectory, $"missing-{propertyName}.cnote");
            await CreatePackageWithManifestAsync(packagePath, manifest.ToJsonString(), "{\"notes\":[]}");

            await Assert.ThrowsAsync<InvalidDataException>(
                () => CreateInspectionService(temporaryDirectory).InspectAsync(packagePath));
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task StagedSnapshotRemainsBoundToSelectedPackageAfterSourceReplacement()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var selectedPath = Path.Combine(temporaryDirectory, "selected.cnote");
            var replacementPath = Path.Combine(temporaryDirectory, "replacement.cnote");
            await CreatePackageAsync(
                selectedPath,
                CreateDocumentJson([
                    CreateNote(Guid.Parse("60000000-0000-0000-0000-000000000001"), TodoBoardKeys.Notes, "已选择")
                ]));
            await CreatePackageAsync(
                replacementPath,
                CreateDocumentJson([
                    CreateNote(Guid.Parse("60000000-0000-0000-0000-000000000002"), TodoBoardKeys.Notes, "替换一"),
                    CreateNote(Guid.Parse("60000000-0000-0000-0000-000000000003"), TodoBoardKeys.Notes, "替换二")
                ]));
            var repository = new InMemoryRepository(CreateWorkspace([]));
            var service = new NotesBackupService(
                new WorkspaceApplicationService(repository),
                new NoteMediaService(Path.Combine(temporaryDirectory, "Media")));

            string stagedPath;
            await using (var staged = await new NotesBackupPackageStager().StageAsync(selectedPath))
            {
                stagedPath = staged.PackagePath;
                File.Move(replacementPath, selectedPath, overwrite: true);

                var preview = await service.InspectAsync(staged.PackagePath);
                var result = await service.ImportOverwriteAsync(staged.PackagePath);

                Assert.Equal(1, preview.NoteCount);
                Assert.Equal(1, result.NoteCount);
                var imported = Assert.Single(Assert.Single(await repository.ListAsync()).Notes);
                Assert.Equal("已选择", imported.Title);
                Assert.True(File.Exists(staged.PackagePath));
            }

            Assert.False(File.Exists(stagedPath));
            Assert.False(Directory.Exists(Path.GetDirectoryName(stagedPath)!));
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task StagedSnapshotRejectsMutationUntilDisposed()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(temporaryDirectory, "selected.cnote");
            await CreatePackageAsync(sourcePath, "{\"notes\":[]}");

            string stagedPath;
            await using (var staged = await new NotesBackupPackageStager().StageAsync(sourcePath))
            {
                stagedPath = staged.PackagePath;

                await Assert.ThrowsAnyAsync<IOException>(
                    () => File.WriteAllBytesAsync(staged.PackagePath, [7, 7, 7]));
                Assert.Equal(0, (await CreateInspectionService(temporaryDirectory)
                    .InspectAsync(staged.PackagePath)).NoteCount);
            }

            Assert.False(File.Exists(stagedPath));
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task StageAsyncCleanupFailureDoesNotMaskCopyFailure()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(temporaryDirectory, "source.cnote");
            await File.WriteAllBytesAsync(sourcePath, [1, 2, 3]);
            var expected = new InvalidOperationException("copy failed");

            var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new NotesBackupPackageStager().StageAsync(
                    sourcePath,
                    CancellationToken.None,
                    _ => throw new IOException("cleanup failed"),
                    (_, _, _) => Task.FromException(expected)));

            Assert.Same(expected, actual);
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public void SnapshotCleanupFailureDoesNotMaskSuccessfulScope()
    {
        var snapshotRoot = CreateTemporaryDirectory();
        try
        {
            using (new NotesBackupPackageSnapshot(
                       snapshotRoot,
                       Path.Combine(snapshotRoot, "notes.cnote"),
                       _ => throw new IOException("cleanup failed")))
            {
                Assert.True(true);
            }

            Assert.True(Directory.Exists(snapshotRoot));
        }
        finally
        {
            DeleteDirectory(snapshotRoot);
        }
    }

    [Fact]
    public async Task SnapshotCleanupFailurePreservesUnsupportedSchemaException()
    {
        var snapshotRoot = CreateTemporaryDirectory();
        try
        {
            var exception = await Assert.ThrowsAsync<UnsupportedNotesBackupSchemaException>(async () =>
            {
                using var snapshot = new NotesBackupPackageSnapshot(
                    snapshotRoot,
                    Path.Combine(snapshotRoot, "notes.cnote"),
                    _ => throw new IOException("cleanup failed"));
                await Task.Yield();
                throw new UnsupportedNotesBackupSchemaException(2);
            });

            Assert.Equal(2, exception.SchemaVersion);
            Assert.Equal(
                "备份版本较新，请升级应用后重试",
                NotesBackupImportFailureMessages.GetMessage(exception));
        }
        finally
        {
            DeleteDirectory(snapshotRoot);
        }
    }

    [Fact]
    public async Task StageAsyncCleanupFailurePreservesCancellation()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(temporaryDirectory, "source.cnote");
            await File.WriteAllBytesAsync(sourcePath, [1, 2, 3]);
            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new NotesBackupPackageStager().StageAsync(
                    sourcePath,
                    cancellationSource.Token,
                    _ => throw new IOException("cleanup failed")));
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Theory]
    [InlineData("{invalid notes json", 1, typeof(InvalidDataException))]
    [InlineData("{\"notes\":[]}", 2, typeof(UnsupportedNotesBackupSchemaException))]
    public async Task InspectAsyncRejectsInvalidJsonAndUnsupportedSchema(
        string notesJson,
        int schemaVersion,
        Type exceptionType)
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var packagePath = Path.Combine(temporaryDirectory, "invalid.cnote");
            await CreatePackageAsync(packagePath, notesJson, schemaVersion);

            await Assert.ThrowsAsync(
                exceptionType,
                () => CreateInspectionService(temporaryDirectory).InspectAsync(packagePath));
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task InspectAsyncRejectsDeletedRecordInPackage()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var packagePath = Path.Combine(temporaryDirectory, "deleted-record.cnote");
            var notesJson = CreateDocumentJson([
                CreateNote(Guid.Parse("70000000-0000-0000-0000-000000000001"), TodoBoardKeys.Notes, "活动")
            ]).Replace("\"isDeleted\":false", "\"isDeleted\":true", StringComparison.Ordinal);
            await CreatePackageAsync(packagePath, notesJson);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => CreateInspectionService(temporaryDirectory).InspectAsync(packagePath));
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task InspectAsyncRejectsTraversalWithoutWritingOutsideStagingRoot()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        var escapeName = $"escape-{Guid.NewGuid():N}.png";
        var outsidePath = Path.Combine(Path.GetTempPath(), escapeName);
        try
        {
            var packagePath = Path.Combine(temporaryDirectory, "traversal.cnote");
            await CreatePackageAsync(packagePath, "{\"notes\":[]}", 1, null, $"../{escapeName}");

            await Assert.ThrowsAsync<InvalidDataException>(
                () => CreateInspectionService(temporaryDirectory).InspectAsync(packagePath));

            Assert.False(File.Exists(outsidePath));
        }
        finally
        {
            if (File.Exists(outsidePath))
            {
                File.Delete(outsidePath);
            }

            DeleteDirectory(temporaryDirectory);
        }
    }

    [Theory]
    [InlineData(
        "media/80000000000000000000000000000001/foo",
        "media/80000000000000000000000000000001/foo/")]
    [InlineData(
        "media\\80000000000000000000000000000001\\foo\\",
        "media/80000000000000000000000000000001/foo")]
    public async Task InspectAsyncRejectsCanonicalEntryCollision(string first, string second)
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var packagePath = Path.Combine(temporaryDirectory, "collision.cnote");
            var json = CreateDocumentJson([
                CreateNote(Guid.Parse("80000000-0000-0000-0000-000000000001"), TodoBoardKeys.Notes, "活动")
            ]);
            await CreatePackageAsync(packagePath, json, 1, null, first, second);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => CreateInspectionService(temporaryDirectory).InspectAsync(packagePath));
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InspectAsyncRejectsAncestorFileDirectoryCollision(bool descendantFirst)
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var packagePath = Path.Combine(temporaryDirectory, "ancestor-collision.cnote");
            var json = CreateDocumentJson([
                CreateNote(Guid.Parse("81000000-0000-0000-0000-000000000001"), TodoBoardKeys.Notes, "活动")
            ]);
            var ancestor = "media/81000000000000000000000000000001/foo";
            var descendant = "media/81000000000000000000000000000001/foo/bar.png";
            await CreatePackageAsync(
                packagePath,
                json,
                1,
                null,
                descendantFirst ? descendant : ancestor,
                descendantFirst ? ancestor : descendant);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => CreateInspectionService(temporaryDirectory).InspectAsync(packagePath));
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public void ArchivePathIndexUsesLinearComparerWorkForManySiblingEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "ConvenientNote.Tests", "path-index-root");
        var comparer = new CountingPathComparer();
        var index = new NotesBackupArchivePathIndex(root, comparer);
        const int entryCount = 4096;

        for (var entryIndex = 0; entryIndex < entryCount; entryIndex++)
        {
            index.Add(
                Path.Combine(
                    root,
                    "media",
                    "83000000000000000000000000000001",
                    $"image-{entryIndex:D4}.png"),
                isDirectory: false);
        }

        index.ValidateAncestors();

        Assert.InRange(comparer.OperationCount, 1, entryCount * 20);
    }

    [Fact]
    public async Task InspectAsyncRejectsNoncanonicalGuidSegmentThatResolvesIntoCanonicalDirectory()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var noteId = Guid.Parse("82000000-0000-0000-0000-000000000001");
            var packagePath = Path.Combine(temporaryDirectory, "noncanonical-guid.cnote");
            var json = CreateDocumentJson([
                CreateNote(noteId, TodoBoardKeys.Notes, "活动")
            ]);
            await CreatePackageAsync(
                packagePath,
                json,
                1,
                null,
                $"media/{noteId:D}/../{noteId:N}/image.png");

            await Assert.ThrowsAsync<InvalidDataException>(
                () => CreateInspectionService(temporaryDirectory).InspectAsync(packagePath));
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    private static NotesBackupService CreateInspectionService(string temporaryDirectory) => new(
        new WorkspaceApplicationService(new InMemoryRepository(CreateWorkspace([]))),
        new NoteMediaService(Path.Combine(temporaryDirectory, "InspectionMedia")));

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

    private static async Task CreatePackageAsync(
        string packagePath,
        string notesJson,
        int schemaVersion = 1,
        DateTimeOffset? exportedAtUtc = null,
        params string[] extraEntryNames)
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
                    exportedAtUtc ?? DateTimeOffset.UtcNow),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }

        var notesEntry = archive.CreateEntry("notes.json");
        await using (var writer = new StreamWriter(notesEntry.Open()))
        {
            await writer.WriteAsync(notesJson);
        }

        foreach (var entryName in extraEntryNames)
        {
            var entry = archive.CreateEntry(entryName);
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("entry");
        }
    }

    private static async Task CreatePackageWithManifestAsync(
        string packagePath,
        string manifestJson,
        string notesJson)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var manifestEntry = archive.CreateEntry("manifest.json");
        await using (var writer = new StreamWriter(manifestEntry.Open()))
        {
            await writer.WriteAsync(manifestJson);
        }

        var notesEntry = archive.CreateEntry("notes.json");
        await using (var writer = new StreamWriter(notesEntry.Open()))
        {
            await writer.WriteAsync(notesJson);
        }
    }

    private static Workspace CreateWorkspace(IEnumerable<Note> notes) => new(
        new WorkspaceId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
        "本地工作区",
        new DateTimeOffset(2026, 9, 1, 1, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 1, 2, 0, 0, TimeSpan.Zero),
        notes);

    private static Note CreateNote(Guid id, string boardKey, string title, bool isDeleted = false) => new(
        new NoteId(id),
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

    private static async Task WriteMediaAsync(
        string mediaRoot,
        NoteId noteId,
        string fileName,
        byte[] bytes)
    {
        var path = Path.Combine(mediaRoot, noteId.Value.ToString("N"), fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes);
    }

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

    private sealed class InMemoryRepository(Workspace? workspace) : IWorkspaceRepository
    {
        private Workspace? _workspace = workspace;

        public Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Workspace>>(_workspace is null ? [] : [_workspace]);

        public Task<Workspace?> GetAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_workspace?.Id == workspaceId ? _workspace : null);

        public Task SaveAsync(Workspace workspace, CancellationToken cancellationToken = default)
        {
            _workspace = workspace;
            return Task.CompletedTask;
        }

        public Task ReplaceActiveNotesAsync(
            WorkspaceId workspaceId,
            IReadOnlyCollection<Note> importedNotes,
            CancellationToken cancellationToken = default)
        {
            if (_workspace?.Id != workspaceId)
            {
                throw new InvalidOperationException("Workspace was not found.");
            }

            _workspace = new Workspace(
                _workspace.Id,
                _workspace.Name,
                _workspace.CreatedAt,
                _workspace.UpdatedAt,
                [
                    .. _workspace.Notes.Where(note => note.BoardKey != TodoBoardKeys.Notes || note.IsDeleted),
                    .. importedNotes
                ]);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CountingPathComparer : IEqualityComparer<string>
    {
        public int OperationCount { get; private set; }

        public bool Equals(string? left, string? right)
        {
            OperationCount++;
            return StringComparer.OrdinalIgnoreCase.Equals(left, right);
        }

        public int GetHashCode(string value)
        {
            OperationCount++;
            return StringComparer.OrdinalIgnoreCase.GetHashCode(value);
        }
    }
}
