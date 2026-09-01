using System.IO.Compression;
using System.IO;
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

public sealed class WorkspaceBackupImportTests
{
    [Fact]
    public async Task ExportThenOverwriteImportRestoresRichNoteStateAndImageBytesWithoutRecreatingTheService()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var workspaceId = new WorkspaceId(Guid.Parse("02a34da1-0694-49ba-8e9c-13aa8e1f27d1"));
            var noteId = new NoteId(Guid.Parse("04785755-55bb-4246-84d4-78f769e47ebc"));
            var notebookId = new NotebookId(Guid.Parse("5aecc539-7d14-4f44-ad97-b4038e2e805d"));
            var workspaceCreatedAt = new DateTimeOffset(2026, 8, 1, 9, 30, 0, TimeSpan.FromHours(8));
            var workspaceUpdatedAt = new DateTimeOffset(2026, 8, 30, 21, 45, 0, TimeSpan.FromHours(8));
            var noteCreatedAt = new DateTimeOffset(2026, 8, 2, 10, 15, 0, TimeSpan.FromHours(8));
            var noteUpdatedAt = new DateTimeOffset(2026, 8, 31, 8, 5, 0, TimeSpan.FromHours(8));
            var richContent = "{\"version\":1,\"blocks\":[{\"kind\":\"paragraph\",\"fontSize\":18,\"lineSpacing\":1.5,\"inlines\":[{\"kind\":\"text\",\"text\":\"格式化标题\",\"bold\":true}]},{\"kind\":\"list\",\"items\":[\"第一项\",\"第二项\"],\"lineSpacing\":1.75},{\"kind\":\"image\",\"source\":\"media/0478575555bb424684d478f769e47ebc/inline.png\"}]}";
            var exportedNote = new Note(
                noteId,
                "testing",
                "red",
                "完整恢复笔记",
                "格式化标题\r\n第一项\r\n第二项",
                new NotePosition(123.45, -67.89),
                new NoteSize(456.75, 234.5),
                "#12ABEF",
                42,
                true,
                noteCreatedAt,
                noteUpdatedAt,
                richContent,
                notebookId,
                ["工作", "导入导出"],
                isPinned: true,
                isFavorite: true,
                isDeleted: true);
            var exportedWorkspace = new Workspace(
                workspaceId,
                "富文本迁移工作区",
                workspaceCreatedAt,
                workspaceUpdatedAt,
                [exportedNote]);
            var mediaRoot = Path.Combine(temporaryDirectory, "Media");
            var imagePath = await WriteMediaAsync(mediaRoot, noteId, "inline.png", [137, 80, 78, 71, 13, 10, 26, 10]);
            var databasePath = Path.Combine(temporaryDirectory, "workspace.db");
            var repository = new SqliteWorkspaceRepository(databasePath);
            await repository.ReplaceAllAsync(exportedWorkspace);
            var service = new WorkspaceBackupService(
                new WorkspaceApplicationService(repository),
                new NoteMediaService(mediaRoot));
            var packagePath = Path.Combine(temporaryDirectory, "rich-round-trip.cnote");

            await service.ExportAsync(packagePath);

            await new SqliteWorkspaceRepository(databasePath)
                .ReplaceAllAsync(CreateWorkspace("已变更工作区", "已变更笔记"));
            Directory.Delete(mediaRoot, recursive: true);
            await WriteMediaAsync(mediaRoot, noteId, "inline.png", [0, 1, 2, 3]);

            var result = await service.ImportOverwriteAsync(packagePath);

            var restoredWorkspace = Assert.Single(await new SqliteWorkspaceRepository(databasePath).ListAsync());
            var restoredNote = Assert.Single(restoredWorkspace.Notes);
            Assert.Equal(workspaceId, restoredWorkspace.Id);
            Assert.Equal("富文本迁移工作区", restoredWorkspace.Name);
            Assert.Equal(workspaceCreatedAt, restoredWorkspace.CreatedAt);
            Assert.Equal(workspaceUpdatedAt, restoredWorkspace.UpdatedAt);
            Assert.Equal(noteId, restoredNote.Id);
            Assert.Equal("testing", restoredNote.BoardKey);
            Assert.Equal("red", restoredNote.Priority);
            Assert.Equal("完整恢复笔记", restoredNote.Title);
            Assert.Equal("格式化标题\r\n第一项\r\n第二项", restoredNote.Content);
            Assert.Equal(123.45, restoredNote.Position.X);
            Assert.Equal(-67.89, restoredNote.Position.Y);
            Assert.Equal(456.75, restoredNote.Size.Width);
            Assert.Equal(234.5, restoredNote.Size.Height);
            Assert.Equal("#12ABEF", restoredNote.Color);
            Assert.Equal(42, restoredNote.ZIndex);
            Assert.True(restoredNote.IsCompleted);
            Assert.Equal(richContent, restoredNote.RichContent);
            Assert.Equal(notebookId, restoredNote.NotebookId);
            Assert.Equal(["工作", "导入导出"], restoredNote.Tags);
            Assert.True(restoredNote.IsPinned);
            Assert.True(restoredNote.IsFavorite);
            Assert.True(restoredNote.IsDeleted);
            Assert.Equal(noteCreatedAt, restoredNote.CreatedAt);
            Assert.Equal(noteUpdatedAt, restoredNote.UpdatedAt);
            Assert.Equal([137, 80, 78, 71, 13, 10, 26, 10], await File.ReadAllBytesAsync(imagePath));
            Assert.Equal(workspaceId, result.WorkspaceId);
            Assert.Equal("富文本迁移工作区", result.WorkspaceName);
            Assert.Equal(1, result.NoteCount);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task ImportOverwriteAsyncReplacesTheWorkspaceAndItsMedia()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var currentWorkspace = CreateWorkspace("原工作区", "原笔记");
            var importedWorkspace = CreateWorkspace("导入工作区", "导入笔记");
            var mediaRoot = Path.Combine(temporaryDirectory, "target-media");
            var packagePath = await CreateExportPackageAsync(temporaryDirectory, importedWorkspace);
            await WriteMediaAsync(mediaRoot, currentWorkspace.Notes.Single().Id, "old.png", [1, 2, 3]);

            var repository = new TrackingRepository(currentWorkspace);
            var service = new WorkspaceBackupService(
                new WorkspaceApplicationService(repository),
                new NoteMediaService(mediaRoot));

            var result = await service.ImportOverwriteAsync(packagePath);

            var storedWorkspace = Assert.Single(await repository.ListAsync());
            Assert.Equal(importedWorkspace.Id, storedWorkspace.Id);
            Assert.Equal("导入工作区", storedWorkspace.Name);
            Assert.Equal(importedWorkspace.Notes.Count, storedWorkspace.Notes.Count);
            Assert.Equal(importedWorkspace.Id, result.WorkspaceId);
            Assert.Equal("导入工作区", result.WorkspaceName);
            Assert.Equal(1, result.NoteCount);
            Assert.Equal(
                [9, 8, 7],
                await File.ReadAllBytesAsync(Path.Combine(
                    mediaRoot,
                    importedWorkspace.Notes.Single().Id.Value.ToString("N"),
                    "imported.png")));
            Assert.False(File.Exists(Path.Combine(
                mediaRoot,
                currentWorkspace.Notes.Single().Id.Value.ToString("N"),
                "old.png")));
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task ImportOverwriteAsyncRestoresCurrentMediaWhenRepositoryReplacementFails()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var currentWorkspace = CreateWorkspace("原工作区", "原笔记");
            var importedWorkspace = CreateWorkspace("导入工作区", "导入笔记");
            var mediaRoot = Path.Combine(temporaryDirectory, "target-media");
            var packagePath = await CreateExportPackageAsync(temporaryDirectory, importedWorkspace);
            var currentMediaPath = await WriteMediaAsync(
                mediaRoot,
                currentWorkspace.Notes.Single().Id,
                "old.png",
                [1, 2, 3]);

            var repository = new TrackingRepository(currentWorkspace)
            {
                ReplaceFailure = new InvalidOperationException("replacement failed")
            };
            var service = new WorkspaceBackupService(
                new WorkspaceApplicationService(repository),
                new NoteMediaService(mediaRoot));

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportOverwriteAsync(packagePath));

            var storedWorkspace = Assert.Single(await repository.ListAsync());
            Assert.Equal(currentWorkspace.Id, storedWorkspace.Id);
            Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(currentMediaPath));
            Assert.False(File.Exists(Path.Combine(
                mediaRoot,
                importedWorkspace.Notes.Single().Id.Value.ToString("N"),
                "imported.png")));
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task ImportOverwriteAsyncPreservesCurrentStateWhenTokenIsAlreadyCancelled()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var currentWorkspace = CreateWorkspace("原工作区", "原笔记");
            var importedWorkspace = CreateWorkspace("导入工作区", "导入笔记");
            var mediaRoot = Path.Combine(temporaryDirectory, "target-media");
            var packagePath = await CreateExportPackageAsync(temporaryDirectory, importedWorkspace);
            var currentMediaPath = await WriteMediaAsync(
                mediaRoot,
                currentWorkspace.Notes.Single().Id,
                "old.png",
                [1, 2, 3]);
            var repository = new TrackingRepository(currentWorkspace);
            var service = new WorkspaceBackupService(
                new WorkspaceApplicationService(repository),
                new NoteMediaService(mediaRoot));
            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.ImportOverwriteAsync(packagePath, cancellationSource.Token));

            Assert.Equal(0, repository.ReplaceAllCallCount);
            Assert.Equal(currentWorkspace.Id, Assert.Single(await repository.ListAsync()).Id);
            Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(currentMediaPath));
            Assert.Empty(Directory.EnumerateDirectories(
                temporaryDirectory,
                $"{Path.GetFileName(mediaRoot)}.rollback-*"));
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Fact]
    public async Task ImportOverwriteAsyncClearsExistingMediaWhenThePackageHasNoMedia()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var currentWorkspace = CreateWorkspace("原工作区", "原笔记");
            var importedWorkspace = CreateWorkspace("导入工作区", "导入笔记");
            var mediaRoot = Path.Combine(temporaryDirectory, "target-media");
            var packagePath = await CreateExportPackageAsync(
                temporaryDirectory,
                importedWorkspace,
                includeMedia: false);
            await WriteMediaAsync(mediaRoot, currentWorkspace.Notes.Single().Id, "old.png", [1, 2, 3]);
            var repository = new TrackingRepository(currentWorkspace);
            var service = new WorkspaceBackupService(
                new WorkspaceApplicationService(repository),
                new NoteMediaService(mediaRoot));

            var result = await service.ImportOverwriteAsync(packagePath);

            Assert.Equal(importedWorkspace.Id, Assert.Single(await repository.ListAsync()).Id);
            Assert.Equal(importedWorkspace.Notes.Count, result.NoteCount);
            Assert.True(Directory.Exists(mediaRoot));
            Assert.Empty(Directory.EnumerateFiles(mediaRoot, "*", SearchOption.AllDirectories));
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    [Theory]
    [InlineData("{invalid workspace json", 1)]
    [InlineData("{}", 2)]
    public async Task ImportOverwriteAsyncDoesNotMutateCurrentStateForInvalidPackages(
        string workspaceJson,
        int schemaVersion)
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var currentWorkspace = CreateWorkspace("原工作区", "原笔记");
            var mediaRoot = Path.Combine(temporaryDirectory, "target-media");
            var currentMediaPath = await WriteMediaAsync(
                mediaRoot,
                currentWorkspace.Notes.Single().Id,
                "old.png",
                [1, 2, 3]);
            var packagePath = Path.Combine(temporaryDirectory, $"invalid-{schemaVersion}.cnote");
            await CreatePackageAsync(packagePath, workspaceJson, schemaVersion);
            var repository = new TrackingRepository(currentWorkspace);
            var service = new WorkspaceBackupService(
                new WorkspaceApplicationService(repository),
                new NoteMediaService(mediaRoot));

            if (schemaVersion > 1)
            {
                await Assert.ThrowsAsync<UnsupportedWorkspaceBackupSchemaException>(
                    () => service.ImportOverwriteAsync(packagePath));
            }
            else
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportOverwriteAsync(packagePath));
            }

            Assert.Equal(0, repository.ReplaceAllCallCount);
            Assert.Equal(currentWorkspace.Id, Assert.Single(await repository.ListAsync()).Id);
            Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(currentMediaPath));
        }
        finally
        {
            DeleteDirectory(temporaryDirectory);
        }
    }

    private static Workspace CreateWorkspace(string name, string noteTitle)
    {
        var workspace = Workspace.Create(name);
        workspace.AddNote(
            TodoBoardKeys.Testing,
            noteTitle,
            "正文",
            new NotePosition(0, 0),
            new NoteSize(260, 150),
            "#FFF8B8");
        return workspace;
    }

    private static async Task<string> CreateExportPackageAsync(
        string temporaryDirectory,
        Workspace workspace,
        bool includeMedia = true)
    {
        var sourceMediaRoot = Path.Combine(temporaryDirectory, $"source-media-{Guid.NewGuid():N}");
        if (includeMedia)
        {
            await WriteMediaAsync(sourceMediaRoot, workspace.Notes.Single().Id, "imported.png", [9, 8, 7]);
        }
        var packagePath = Path.Combine(temporaryDirectory, $"import-{Guid.NewGuid():N}.cnote");
        var exportService = new WorkspaceBackupService(
            new WorkspaceApplicationService(new TrackingRepository(workspace)),
            new NoteMediaService(sourceMediaRoot));
        await exportService.ExportAsync(packagePath);
        return packagePath;
    }

    private static async Task CreatePackageAsync(string packagePath, string workspaceJson, int schemaVersion)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var manifestEntry = archive.CreateEntry("manifest.json");
        await using (var manifestStream = manifestEntry.Open())
        {
            await JsonSerializer.SerializeAsync(
                manifestStream,
                new WorkspaceBackupManifest(
                    "convenient-note-backup",
                    schemaVersion,
                    "1.0.0",
                    DateTimeOffset.UtcNow),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }

        var workspaceEntry = archive.CreateEntry("workspace.json");
        await using var workspaceStream = new StreamWriter(workspaceEntry.Open());
        await workspaceStream.WriteAsync(workspaceJson);
    }

    private static async Task<string> WriteMediaAsync(
        string mediaRoot,
        NoteId noteId,
        string fileName,
        byte[] content)
    {
        var path = Path.Combine(mediaRoot, noteId.Value.ToString("N"), fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content);
        return path;
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

    private sealed class TrackingRepository : IWorkspaceRepository
    {
        private Workspace? _workspace;

        public TrackingRepository(Workspace? workspace = null)
        {
            _workspace = workspace;
        }

        public Exception? ReplaceFailure { get; init; }

        public int ReplaceAllCallCount { get; private set; }

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
            ReplaceAllCallCount++;
            if (ReplaceFailure is not null)
            {
                throw ReplaceFailure;
            }

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
