using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;
using ConvenientNote.Infrastructure.Persistence;
using ConvenientNote.LegacyMigration;
using ConvenientNote.Notes.Infrastructure;
using ConvenientNote.Platform.Infrastructure;
using ConvenientNote.Todos.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ConvenientNote.Migration.Tests;

public sealed class LegacyDataMigrationTests
{
    [Fact]
    public async Task MigrationPreservesEveryOwnedFieldAndOriginalSource()
    {
        using var fixture = new Fixture();
        var source = await fixture.SeedAsync();
        SqliteConnection.ClearAllPools();
        var originalBytes = await File.ReadAllBytesAsync(fixture.SourcePath);
        await fixture.Migration.InitializeAsync();

        var notes = await new SqliteNotesRepository(fixture.Migration.DatabasePath).ListAsync(source.Id.Value);
        Assert.Equal(3, notes.Count);
        foreach (var original in source.Notes.Where(n => n.BoardKey == "testing"))
        {
            var migrated = Assert.Single(notes, n => n.Id.Value == original.Id.Value);
            Assert.Equal(original.Title, migrated.Title);
            Assert.Equal(original.Content, migrated.Content);
            Assert.Equal(original.RichContent, migrated.RichContent);
            Assert.Equal(original.NotebookId?.Value, migrated.NotebookId?.Value);
            Assert.Equal(original.Tags, migrated.Tags);
            Assert.Equal(original.Position.X, migrated.Position.X);
            Assert.Equal(original.Position.Y, migrated.Position.Y);
            Assert.Equal(original.Size.Width, migrated.Size.Width);
            Assert.Equal(original.Size.Height, migrated.Size.Height);
            Assert.Equal(original.Color, migrated.Color);
            Assert.Equal(original.ZIndex, migrated.ZIndex);
            Assert.Equal(original.IsPinned, migrated.IsPinned);
            Assert.Equal(original.IsFavorite, migrated.IsFavorite);
            Assert.Equal(original.IsDeleted, migrated.IsDeleted);
            Assert.Equal(original.CreatedAt, migrated.CreatedAt);
            Assert.Equal(original.UpdatedAt, migrated.UpdatedAt);
        }
        var todo = Assert.Single(await new SqliteTodoRepository(fixture.Migration.DatabasePath).ListAsync(source.Id.Value));
        var legacyTodo = Assert.Single(source.Notes, n => n.BoardKey == "day-todo");
        Assert.Equal(legacyTodo.Id.Value, todo.Id.Value);
        Assert.Equal(legacyTodo.Title, todo.Title);
        Assert.Equal(legacyTodo.Content, todo.Content);
        Assert.Equal(legacyTodo.Priority, todo.Priority);
        Assert.Equal(legacyTodo.PlannedDate, todo.PlannedDate);
        Assert.Equal(legacyTodo.CompletedAt, todo.CompletedAt);
        Assert.True(todo.IsCompleted);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(fixture.SourcePath));
        Assert.True(await fixture.Context.HasMigrationAsync(LegacyDataMigration.Version));
    }

    [Fact]
    public async Task CompletedRerunDoesNotResurrectPermanentDeletionOrOverwriteEdits()
    {
        using var fixture = new Fixture();
        var source = await fixture.SeedAsync();
        await fixture.Migration.InitializeAsync();
        var notes = new SqliteNotesRepository(fixture.Migration.DatabasePath);
        var imported = (await notes.ListAsync(source.Id.Value)).First(n => !n.IsDeleted);
        await notes.DeleteAsync(source.Id.Value, imported.Id);
        var todos = new SqliteTodoRepository(fixture.Migration.DatabasePath);
        var todo = Assert.Single(await todos.ListAsync(source.Id.Value));
        todo.Rename("edited after migration");
        await todos.SaveAsync(source.Id.Value, [todo]);
        await fixture.Migration.InitializeAsync();
        Assert.DoesNotContain(await notes.ListAsync(source.Id.Value), n => n.Id == imported.Id);
        Assert.Equal("edited after migration", Assert.Single(await todos.ListAsync(source.Id.Value)).Title);
    }

    [Fact]
    public async Task OnlineBackupIncludesCommittedWalRecords()
    {
        using var fixture = new Fixture();
        var source = await fixture.SeedAsync();
        await using var connection = new SqliteConnection($"Data Source={fixture.SourcePath};Pooling=False");
        await connection.OpenAsync();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; UPDATE Notes SET Title='committed in WAL' WHERE BoardKey='day-todo';";
            await command.ExecuteNonQueryAsync();
        }
        Assert.True(new FileInfo(fixture.SourcePath + "-wal").Length > 0);
        await fixture.Migration.InitializeAsync();
        var todo = Assert.Single(await new SqliteTodoRepository(fixture.Migration.DatabasePath).ListAsync(source.Id.Value));
        Assert.Equal("committed in WAL", todo.Title);
    }

    [Fact]
    public async Task UnknownBoardRejectsWithoutCompletionOrSourceChanges()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync(unknownBoard: true);
        SqliteConnection.ClearAllPools();
        var original = await File.ReadAllBytesAsync(fixture.SourcePath);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Migration.InitializeAsync());
        Assert.False(await fixture.Context.HasMigrationAsync(LegacyDataMigration.Version));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Migration.InitializeAsync());
        Assert.False(await fixture.Context.HasMigrationAsync(LegacyDataMigration.Version));
        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.SourcePath));
    }

    [Fact]
    public async Task FailedTodoImportLeavesNoMarkerAndRetryPreservesAlreadyImportedNotes()
    {
        using var fixture = new Fixture();
        var source = await fixture.SeedAsync();
        var todos = new SqliteTodoRepository(fixture.Migration.DatabasePath);
        await todos.ListAsync(source.Id.Value);
        await ExecuteAsync(fixture.Migration.DatabasePath, "CREATE TRIGGER reject_todos BEFORE INSERT ON todos_items BEGIN SELECT RAISE(ABORT,'simulated disk failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => fixture.Migration.InitializeAsync());
        Assert.False(await fixture.Context.HasMigrationAsync(LegacyDataMigration.Version));
        var notes = new SqliteNotesRepository(fixture.Migration.DatabasePath);
        var imported = (await notes.ListAsync(source.Id.Value)).First(n => !n.IsDeleted);
        imported.Rename("survives retry");
        await notes.SaveAsync(source.Id.Value, imported);
        await ExecuteAsync(fixture.Migration.DatabasePath, "DROP TRIGGER reject_todos;");
        await fixture.Migration.InitializeAsync();
        Assert.True(await fixture.Context.HasMigrationAsync(LegacyDataMigration.Version));
        Assert.Equal("survives retry", Assert.Single(await notes.ListAsync(source.Id.Value), n => n.Id == imported.Id).Title);
        Assert.Single(await todos.ListAsync(source.Id.Value));
    }

    [Fact]
    public async Task JsonOnlyInstallIsImportedWithoutCreatingOriginalDatabase()
    {
        using var fixture = new Fixture();
        var source = await fixture.SeedAsync();
        var jsonPath = Path.Combine(Path.GetDirectoryName(fixture.SourcePath)!, "workspaces.json");
        await new JsonWorkspaceRepository(jsonPath).SaveAsync(source);
        SqliteConnection.ClearAllPools();
        File.Delete(fixture.SourcePath);
        var original = await File.ReadAllBytesAsync(jsonPath);
        await fixture.Migration.InitializeAsync();
        Assert.False(File.Exists(fixture.SourcePath));
        Assert.Equal(original, await File.ReadAllBytesAsync(jsonPath));
        Assert.Equal(3, (await new SqliteNotesRepository(fixture.Migration.DatabasePath).ListAsync(source.Id.Value)).Count);
        Assert.Single(await new SqliteTodoRepository(fixture.Migration.DatabasePath).ListAsync(source.Id.Value));
        Assert.Equal(source.Id.Value, (await fixture.Context.GetCurrentAsync()).Id);
    }

    [Fact]
    public async Task FreshInitializationHasOneStableWorkspaceAcrossReruns()
    {
        using var fixture = new Fixture();
        await fixture.Migration.InitializeAsync();
        var initial = await fixture.Context.GetCurrentAsync();
        await fixture.Migration.InitializeAsync();
        Assert.Equal(initial, await fixture.Context.GetCurrentAsync());
        Assert.True(await fixture.Context.HasMigrationAsync(LegacyDataMigration.Version));
        Assert.False(File.Exists(fixture.SourcePath));
    }

    [Fact]
    public async Task MediaIsCopiedToIndependentModuleDirectoryAndDeletionCannotDamageRollback()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var directory = Path.GetDirectoryName(fixture.SourcePath)!;
        var source = Path.Combine(directory, "Media", "note-id", "nested", "image.png");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        byte[] content = [1, 4, 9, 16, 25];
        await File.WriteAllBytesAsync(source, content);
        await fixture.Migration.InitializeAsync();
        var destination = Path.Combine(directory, "Notes", "Media", "note-id", "nested", "image.png");
        Assert.Equal(content, await File.ReadAllBytesAsync(destination));
        Assert.Equal(content, await File.ReadAllBytesAsync(source));
        File.Delete(destination);
        await fixture.Migration.InitializeAsync();
        Assert.False(File.Exists(destination));
        Assert.Equal(content, await File.ReadAllBytesAsync(source));
    }

    [Fact]
    public async Task RetryAfterMediaCopyDoesNotOverwriteCompletedFiles()
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        var directory = Path.GetDirectoryName(fixture.SourcePath)!;
        var source = Path.Combine(directory, "Media", "image.png");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        await fixture.Context.HasMigrationAsync(LegacyDataMigration.Version);
        await ExecuteAsync(fixture.Migration.DatabasePath, "CREATE TRIGGER reject_marker BEFORE INSERT ON Platform_Migrations BEGIN SELECT RAISE(ABORT,'interrupted before completion'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => fixture.Migration.InitializeAsync());
        Assert.False(await fixture.Context.HasMigrationAsync(LegacyDataMigration.Version));
        var destination = Path.Combine(directory, "Notes", "Media", "image.png");
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(destination));
        await File.WriteAllBytesAsync(destination, [7, 8, 9]);
        await ExecuteAsync(fixture.Migration.DatabasePath, "DROP TRIGGER reject_marker;");
        await fixture.Migration.InitializeAsync();
        Assert.Equal(new byte[] { 7, 8, 9 }, await File.ReadAllBytesAsync(destination));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(source));
        Assert.True(await fixture.Context.HasMigrationAsync(LegacyDataMigration.Version));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(directory, "Notes", "Media"), "*.migration-*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MediaMigrationRejectsSourceAndDestinationLinks(bool destinationLink)
    {
        using var fixture = new Fixture();
        using var outside = new Fixture();
        var directory = Path.GetDirectoryName(fixture.SourcePath)!;
        var outsideDirectory = Path.GetDirectoryName(outside.SourcePath)!;
        var outsideFile = Path.Combine(outsideDirectory, "image.png");
        await File.WriteAllBytesAsync(outsideFile, [8, 8]);
        var sourceRoot = Path.Combine(directory, "Media");
        Directory.CreateDirectory(sourceRoot);
        var link = destinationLink ? Path.Combine(directory, "Notes") : Path.Combine(sourceRoot, "linked");
        if (destinationLink) await File.WriteAllBytesAsync(Path.Combine(sourceRoot, "image.png"), [1, 2]);
        if (OperatingSystem.IsWindows())
        {
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "/c", "mklink", "/J", link, outsideDirectory }) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)!;
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, outsideDirectory);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Migration.InitializeAsync());
            Assert.False(await fixture.Context.HasMigrationAsync(LegacyDataMigration.Version));
            Assert.Equal(new byte[] { 8, 8 }, await File.ReadAllBytesAsync(outsideFile));
            Assert.False(Directory.Exists(Path.Combine(outsideDirectory, "Media")));
        }
        finally
        {
            // Remove the link itself, never recurse through its target.
            Directory.Delete(link);
        }
    }

    private static async Task ExecuteAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "ConvenientNote-Migration-" + Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(_directory);
        public string SourcePath => Path.Combine(_directory, "ConvenientNote.db");
        public LegacyDataMigration Migration => new(_directory);
        public SqliteWorkspaceContext Context => new(Migration.DatabasePath);
        public async Task<Workspace> SeedAsync(bool unknownBoard = false)
        {
            var created = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.FromHours(8));
            var updated = created.AddDays(4);
            Note Make(string board, string title, bool deleted = false, bool completed = false, string[]? tags = null) => new(
                NoteId.New(), board, "red", title, "plain content", new NotePosition(23, 45), new NoteSize(301, 203), "#AABBCC", 7,
                completed, created, updated, "<FlowDocument><Paragraph>rich content</Paragraph></FlowDocument>", NotebookId.New(), tags ?? ["tag"], true, true, deleted,
                completed ? new DateTime(2026, 9, 8) : null, completed ? updated : null);
            var workspace = new Workspace(WorkspaceId.New(), "source", created, updated,
                [Make("testing", "note"), Make("testing", "deleted", deleted: true), Make("testing", "memo", tags: ["__app_knowledge_memo"]), Make(unknownBoard ? "unsupported" : "day-todo", "todo", completed: true)]);
            await new SqliteWorkspaceRepository(SourcePath).SaveAsync(workspace);
            return workspace;
        }
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            var resolved = Path.GetFullPath(_directory);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("ConvenientNote-Migration-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test cleanup path.");
            Directory.Delete(resolved, recursive: true);
        }
    }
}



