using System.IO.Compression;
using System.IO;
using System.Text.Json;
using ConvenientNote.Application.Abstractions;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;
using ConvenientNote.Services;
using Xunit;

namespace ConvenientNote.Tests.Services;

public sealed class WorkspaceBackupArchiveTests
{
    [Fact]
    public async Task ExportAsyncPackagesWorkspaceAndOnlyItsNoteScopedMedia()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var workspace = Workspace.Create("导出工作区");
            var firstNote = workspace.AddNote(
                TodoBoardKeys.Testing,
                "第一条笔记",
                "正文一",
                new NotePosition(10, 20),
                new NoteSize(260, 150),
                "#FFF8B8");
            var secondNote = workspace.AddNote(
                TodoBoardKeys.Testing,
                "第二条笔记",
                "正文二",
                new NotePosition(30, 40),
                new NoteSize(260, 150),
                "#FFF8B8");
            var mediaRoot = Path.Combine(temporaryDirectory, "Media");
            var mediaDirectory = Path.Combine(mediaRoot, firstNote.Id.Value.ToString("N"));
            Directory.CreateDirectory(mediaDirectory);
            await File.WriteAllBytesAsync(Path.Combine(mediaDirectory, "image.png"), [1, 2, 3]);
            var unrelatedDirectory = Path.Combine(mediaRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(unrelatedDirectory);
            await File.WriteAllBytesAsync(Path.Combine(unrelatedDirectory, "outside.png"), [4, 5, 6]);

            var service = new WorkspaceBackupService(
                new WorkspaceApplicationService(new InMemoryRepository(workspace)),
                new NoteMediaService(mediaRoot));
            var destination = Path.Combine(temporaryDirectory, "workspace.cnote");

            var result = await service.ExportAsync(destination);

            Assert.Equal(Path.GetFullPath(destination), result.PackagePath);
            Assert.Equal(2, result.NoteCount);
            using var archive = ZipFile.OpenRead(destination);
            Assert.Equal(1, archive.Entries.Count(entry => entry.FullName == "manifest.json"));
            Assert.Equal(1, archive.Entries.Count(entry => entry.FullName == "workspace.json"));
            Assert.Contains(
                archive.Entries,
                entry => entry.FullName == $"media/{firstNote.Id.Value:N}/image.png");
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("outside.png", StringComparison.Ordinal));
            Assert.DoesNotContain(
                archive.Entries,
                entry => entry.FullName.Contains(secondNote.Id.Value.ToString("N"), StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task ExportAsyncDoesNotOverwriteTheDestinationWhenCancellationIsAlreadyRequested()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var destination = Path.Combine(temporaryDirectory, "workspace.cnote");
            await File.WriteAllBytesAsync(destination, [9, 8, 7]);
            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.Cancel();
            var service = new WorkspaceBackupService(
                new WorkspaceApplicationService(new InMemoryRepository(Workspace.Create("已取消导出"))),
                new NoteMediaService(Path.Combine(temporaryDirectory, "Media")));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.ExportAsync(destination, cancellationSource.Token));

            Assert.Equal([9, 8, 7], await File.ReadAllBytesAsync(destination));
            Assert.Empty(Directory.EnumerateFiles(temporaryDirectory, "workspace.cnote.tmp-*"));
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task InspectAsyncRejectsAnArchiveWithTheWrongFormat()
    {
        var packagePath = Path.Combine(CreateTemporaryDirectory(), "wrong-format.cnote");
        try
        {
            await CreatePackageAsync(packagePath, new WorkspaceBackupManifest(
                "other-backup",
                1,
                "1.0.0",
                DateTimeOffset.UtcNow));
            var service = CreateInspectionService();

            await Assert.ThrowsAsync<InvalidDataException>(() => service.InspectAsync(packagePath));
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(packagePath)!);
        }
    }

    [Fact]
    public async Task InspectAsyncReturnsTheWorkspacePreviewFromAValidArchive()
    {
        var packagePath = Path.Combine(CreateTemporaryDirectory(), "valid.cnote");
        var exportedAtUtc = new DateTimeOffset(2026, 8, 31, 7, 0, 0, TimeSpan.Zero);
        try
        {
            await CreatePackageAsync(
                packagePath,
                new WorkspaceBackupManifest("convenient-note-backup", 1, "1.0.0", exportedAtUtc),
                CreateWorkspaceJsonWithTwoNotes());
            var service = CreateInspectionService();

            var preview = await service.InspectAsync(packagePath);

            Assert.Equal("可检查工作区", preview.WorkspaceName);
            Assert.Equal(2, preview.NoteCount);
            Assert.Equal(exportedAtUtc, preview.ExportedAtUtc);
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(packagePath)!);
        }
    }

    [Fact]
    public async Task InspectAsyncRejectsAnArchiveWithAnUnsupportedSchema()
    {
        var packagePath = Path.Combine(CreateTemporaryDirectory(), "unsupported-schema.cnote");
        try
        {
            await CreatePackageAsync(packagePath, new WorkspaceBackupManifest(
                "convenient-note-backup",
                2,
                "1.0.0",
                DateTimeOffset.UtcNow));
            var service = CreateInspectionService();

            await Assert.ThrowsAsync<UnsupportedWorkspaceBackupSchemaException>(() => service.InspectAsync(packagePath));
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(packagePath)!);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InspectAsyncRejectsBlankManifestAppVersion(string appVersion)
    {
        var packagePath = Path.Combine(CreateTemporaryDirectory(), "missing-app-version.cnote");
        try
        {
            await CreatePackageAsync(
                packagePath,
                new WorkspaceBackupManifest("convenient-note-backup", 1, appVersion, DateTimeOffset.UtcNow));

            await Assert.ThrowsAsync<InvalidDataException>(() => CreateInspectionService().InspectAsync(packagePath));
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(packagePath)!);
        }
    }

    [Fact]
    public async Task InspectAsyncRejectsDefaultManifestExportedAtUtc()
    {
        var packagePath = Path.Combine(CreateTemporaryDirectory(), "missing-exported-at.cnote");
        try
        {
            await CreatePackageAsync(
                packagePath,
                new WorkspaceBackupManifest("convenient-note-backup", 1, "1.0.0", default));

            await Assert.ThrowsAsync<InvalidDataException>(() => CreateInspectionService().InspectAsync(packagePath));
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(packagePath)!);
        }
    }

    [Fact]
    public async Task InspectAsyncClassifiesNewerSchemaForUpgradeGuidance()
    {
        var packagePath = Path.Combine(CreateTemporaryDirectory(), "newer-schema.cnote");
        try
        {
            await CreatePackageAsync(
                packagePath,
                new WorkspaceBackupManifest("convenient-note-backup", 2, "1.0.0", DateTimeOffset.UtcNow));

            await Assert.ThrowsAsync<UnsupportedWorkspaceBackupSchemaException>(
                () => CreateInspectionService().InspectAsync(packagePath));
            Assert.Equal(
                "备份版本较新，请升级应用后重试",
                WorkspaceBackupImportFailureMessages.GetMessage(new UnsupportedWorkspaceBackupSchemaException(2)));
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(packagePath)!);
        }
    }

    [Fact]
    public async Task InspectAsyncTreatsOlderSchemaAsGenericInvalidData()
    {
        var packagePath = Path.Combine(CreateTemporaryDirectory(), "older-schema.cnote");
        try
        {
            await CreatePackageAsync(
                packagePath,
                new WorkspaceBackupManifest("convenient-note-backup", 0, "1.0.0", DateTimeOffset.UtcNow));

            await Assert.ThrowsAsync<InvalidDataException>(() => CreateInspectionService().InspectAsync(packagePath));
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(packagePath)!);
        }
    }

    [Fact]
    public async Task StagedSnapshotKeepsPreviewAndImportBoundToThePackageSelectedBeforeSourceReplacement()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(temporaryDirectory, "selected.cnote");
            var replacementPath = Path.Combine(temporaryDirectory, "replacement.cnote");
            await CreatePackageAsync(
                sourcePath,
                new WorkspaceBackupManifest("convenient-note-backup", 1, "1.0.0", DateTimeOffset.UtcNow),
                CreateWorkspaceJson("selected A"));
            await CreatePackageAsync(
                replacementPath,
                new WorkspaceBackupManifest("convenient-note-backup", 1, "1.0.0", DateTimeOffset.UtcNow),
                CreateWorkspaceJson("replacement B"));
            var repository = new InMemoryRepository();
            var service = new WorkspaceBackupService(
                new WorkspaceApplicationService(repository),
                new NoteMediaService(Path.Combine(temporaryDirectory, "Media")));

            string snapshotPath;
            using (var snapshot = await WorkspaceBackupPackageStager.StageAsync(sourcePath))
            {
                snapshotPath = snapshot.PackagePath;
                File.Move(replacementPath, sourcePath, overwrite: true);

                var preview = await service.InspectAsync(snapshot.PackagePath);
                var result = await service.ImportOverwriteAsync(snapshot.PackagePath);

                Assert.Equal("selected A", preview.WorkspaceName);
                Assert.Equal("selected A", result.WorkspaceName);
                Assert.False(File.Exists(replacementPath));
                Assert.True(File.Exists(snapshot.PackagePath));
            }

            Assert.False(File.Exists(snapshotPath));
            Assert.False(Directory.Exists(Path.GetDirectoryName(snapshotPath)!));
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
            using (new WorkspaceBackupPackageSnapshot(
                       snapshotRoot,
                       Path.Combine(snapshotRoot, "workspace.cnote"),
                       _ => throw new IOException("cleanup refused")))
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
            var exception = await Assert.ThrowsAsync<UnsupportedWorkspaceBackupSchemaException>(async () =>
            {
                using var snapshot = new WorkspaceBackupPackageSnapshot(
                    snapshotRoot,
                    Path.Combine(snapshotRoot, "workspace.cnote"),
                    _ => throw new IOException("cleanup refused"));
                await Task.Yield();
                throw new UnsupportedWorkspaceBackupSchemaException(2);
            });

            Assert.Equal(2, exception.SchemaVersion);
        }
        finally
        {
            DeleteDirectory(snapshotRoot);
        }
    }

    [Fact]
    public async Task SnapshotCleanupFailurePreservesCancellation()
    {
        var snapshotRoot = CreateTemporaryDirectory();
        try
        {
            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                using var snapshot = new WorkspaceBackupPackageSnapshot(
                    snapshotRoot,
                    Path.Combine(snapshotRoot, "workspace.cnote"),
                    _ => throw new IOException("cleanup refused"));
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationSource.Token);
            });
        }
        finally
        {
            DeleteDirectory(snapshotRoot);
        }
    }

    [Fact]
    public async Task StageAsyncCleanupFailurePreservesOriginalCancellation()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(temporaryDirectory, "source.cnote");
            await File.WriteAllBytesAsync(sourcePath, [1, 2, 3]);
            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                WorkspaceBackupPackageStager.StageAsync(
                    sourcePath,
                    cancellationSource.Token,
                    _ => throw new IOException("cleanup refused")));
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task StageAsyncCleanupFailurePreservesOriginalCopyException()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var sourcePath = Path.Combine(temporaryDirectory, "source.cnote");
            await File.WriteAllBytesAsync(sourcePath, [1, 2, 3]);
            var expected = new InvalidOperationException("copy failed");

            var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                WorkspaceBackupPackageStager.StageAsync(
                    sourcePath,
                    CancellationToken.None,
                    _ => throw new IOException("cleanup refused"),
                    (_, _, _) => Task.FromException(expected)));

            Assert.Same(expected, actual);
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task InspectAsyncRejectsCorruptWorkspaceJson()
    {
        var packagePath = Path.Combine(CreateTemporaryDirectory(), "corrupt-workspace.cnote");
        try
        {
            await CreatePackageAsync(
                packagePath,
                new WorkspaceBackupManifest("convenient-note-backup", 1, "1.0.0", DateTimeOffset.UtcNow),
                workspaceJson: "{not valid json");
            var service = CreateInspectionService();

            await Assert.ThrowsAsync<InvalidDataException>(() => service.InspectAsync(packagePath));
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(packagePath)!);
        }
    }

    [Fact]
    public async Task InspectAsyncRejectsTraversalEntriesWithoutWritingOutsideItsTemporaryRoot()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        var escapeFileName = $"escape-{Guid.NewGuid():N}.txt";
        var outsidePath = Path.Combine(Path.GetTempPath(), escapeFileName);
        var packagePath = Path.Combine(temporaryDirectory, "traversal.cnote");
        try
        {
            await CreatePackageAsync(
                packagePath,
                new WorkspaceBackupManifest("convenient-note-backup", 1, "1.0.0", DateTimeOffset.UtcNow),
                extraEntryName: $"../{escapeFileName}");
            var service = CreateInspectionService();

            await Assert.ThrowsAsync<InvalidDataException>(() => service.InspectAsync(packagePath));

            Assert.False(File.Exists(outsidePath));
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task InspectAsyncRejectsMediaThatNormalizesIntoAnUnownedNoteDirectory()
    {
        var packagePath = Path.Combine(CreateTemporaryDirectory(), "cross-note-traversal.cnote");
        try
        {
            const string ownedNoteId = "0bcf861d510c4b8e8a333b591e2655e5";
            const string unownedNoteId = "11111111111111111111111111111111";
            await CreatePackageAsync(
                packagePath,
                new WorkspaceBackupManifest("convenient-note-backup", 1, "1.0.0", DateTimeOffset.UtcNow),
                CreateWorkspaceJsonWithTwoNotes(),
                $"media/{ownedNoteId}/../{unownedNoteId}/file.png");
            var service = CreateInspectionService();

            await Assert.ThrowsAsync<InvalidDataException>(() => service.InspectAsync(packagePath));
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(packagePath)!);
        }
    }

    [Fact]
    public async Task InspectAsyncRejectsMediaThatNormalizesOverWorkspaceJson()
    {
        var packagePath = Path.Combine(CreateTemporaryDirectory(), "root-collision.cnote");
        try
        {
            const string ownedNoteId = "0bcf861d510c4b8e8a333b591e2655e5";
            await CreatePackageAsync(
                packagePath,
                new WorkspaceBackupManifest("convenient-note-backup", 1, "1.0.0", DateTimeOffset.UtcNow),
                CreateWorkspaceJsonWithTwoNotes(),
                $"media/{ownedNoteId}/../../workspace.json");
            var service = CreateInspectionService();

            await Assert.ThrowsAsync<InvalidDataException>(() => service.InspectAsync(packagePath));
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(packagePath)!);
        }
    }

    [Theory]
    [InlineData(
        "media/0bcf861d510c4b8e8a333b591e2655e5/foo",
        "media/0bcf861d510c4b8e8a333b591e2655e5/foo/")]
    [InlineData(
        "media\\0bcf861d510c4b8e8a333b591e2655e5\\foo\\",
        "media/0bcf861d510c4b8e8a333b591e2655e5/foo")]
    public async Task InspectAsyncRejectsMediaEntriesWithTheSameCanonicalDestination(
        string firstEntryName,
        string secondEntryName)
    {
        var packagePath = Path.Combine(CreateTemporaryDirectory(), "canonical-collision.cnote");
        try
        {
            await CreatePackageWithExtraEntriesAsync(
                packagePath,
                new WorkspaceBackupManifest("convenient-note-backup", 1, "1.0.0", DateTimeOffset.UtcNow),
                CreateWorkspaceJsonWithTwoNotes(),
                firstEntryName,
                secondEntryName);
            var service = CreateInspectionService();

            await Assert.ThrowsAsync<InvalidDataException>(() => service.InspectAsync(packagePath));
        }
        finally
        {
            DeleteDirectory(Path.GetDirectoryName(packagePath)!);
        }
    }

    private static WorkspaceBackupService CreateInspectionService() => new(
        new WorkspaceApplicationService(new InMemoryRepository()),
        new NoteMediaService(Path.Combine(Path.GetTempPath(), "ConvenientNote.Tests", Guid.NewGuid().ToString("N"), "Media")));

    private static async Task CreatePackageAsync(
        string packagePath,
        WorkspaceBackupManifest manifest,
        string? workspaceJson = null,
        string? extraEntryName = null)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var manifestEntry = archive.CreateEntry("manifest.json");
        await using (var manifestStream = manifestEntry.Open())
        {
            await JsonSerializer.SerializeAsync(manifestStream, manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }

        var workspaceEntry = archive.CreateEntry("workspace.json");
        await using (var workspaceStream = new StreamWriter(workspaceEntry.Open()))
        {
            await workspaceStream.WriteAsync(workspaceJson ?? CreateValidWorkspaceJson());
        }

        if (extraEntryName is not null)
        {
            var extraEntry = archive.CreateEntry(extraEntryName);
            await using var extraStream = new StreamWriter(extraEntry.Open());
            await extraStream.WriteAsync("must not escape");
        }
    }

    private static async Task CreatePackageWithExtraEntriesAsync(
        string packagePath,
        WorkspaceBackupManifest manifest,
        string workspaceJson,
        params string[] entryNames)
    {
        await CreatePackageAsync(packagePath, manifest, workspaceJson);
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update);
        foreach (var entryName in entryNames)
        {
            var entry = archive.CreateEntry(entryName);
            await using var entryStream = new StreamWriter(entry.Open());
            await entryStream.WriteAsync("collision");
        }
    }

    private static string CreateValidWorkspaceJson()
    {
        var document = new WorkspaceBackupDocument(
            Guid.Parse("d1cba0b4-d126-40a2-85a1-7c5f3b22cf1f"),
            "可检查工作区",
            new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero),
            []);
        return JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static string CreateWorkspaceJson(string workspaceName)
    {
        var timestamp = new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);
        return JsonSerializer.Serialize(new WorkspaceBackupDocument(
            Guid.Parse("d1cba0b4-d126-40a2-85a1-7c5f3b22cf1f"),
            workspaceName,
            timestamp,
            timestamp,
            []), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static string CreateWorkspaceJsonWithTwoNotes()
    {
        var createdAt = new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);
        var document = new WorkspaceBackupDocument(
            Guid.Parse("d1cba0b4-d126-40a2-85a1-7c5f3b22cf1f"),
            "可检查工作区",
            createdAt,
            createdAt,
            [
                CreateBackupNote("0bcf861d-510c-4b8e-8a33-3b591e2655e5", createdAt),
                CreateBackupNote("5c22d99d-4164-4147-89b0-3e7b67d0a1ef", createdAt)
            ]);
        return JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static WorkspaceBackupNote CreateBackupNote(string id, DateTimeOffset timestamp) => new(
        Guid.Parse(id),
        TodoBoardKeys.Testing,
        "blue",
        "可检查笔记",
        "正文",
        0,
        0,
        260,
        150,
        "#FFF8B8",
        1,
        false,
        "{}",
        null,
        [],
        false,
        false,
        false,
        timestamp,
        timestamp);

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

    private sealed class InMemoryRepository : IWorkspaceRepository
    {
        private Workspace? _workspace;

        public InMemoryRepository(Workspace? workspace = null)
        {
            _workspace = workspace;
        }

        public Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Workspace>>(_workspace is null ? [] : [_workspace]);

        public Task<Workspace?> GetAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_workspace?.Id == workspaceId ? _workspace : null);

        public Task SaveAsync(Workspace workspace, CancellationToken cancellationToken = default)
        {
            _workspace = workspace;
            return Task.CompletedTask;
        }

        public Task ReplaceAllAsync(Workspace workspace, CancellationToken cancellationToken = default)
        {
            _workspace = workspace;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default)
        {
            _workspace = null;
            return Task.CompletedTask;
        }
    }
}
