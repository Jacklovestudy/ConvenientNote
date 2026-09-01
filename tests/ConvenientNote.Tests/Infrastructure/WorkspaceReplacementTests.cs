using System.IO;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;
using ConvenientNote.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ConvenientNote.Tests.Infrastructure;

public sealed class WorkspaceReplacementTests
{
    [Fact]
    public async Task ReplaceAllAsync_ReplacesExistingWorkspaceAndItsNotes()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(directory, "workspace.db");
            var repository = new SqliteWorkspaceRepository(databasePath);
            var oldWorkspace = CreateWorkspace("旧工作区", "旧笔记一", "旧笔记二");
            var importedWorkspace = CreateWorkspace("导入工作区", "导入笔记");

            await repository.SaveAsync(oldWorkspace);
            await repository.ReplaceAllAsync(importedWorkspace);

            var reopenedRepository = new SqliteWorkspaceRepository(databasePath);
            var stored = Assert.Single(await reopenedRepository.ListAsync());
            Assert.Equal(importedWorkspace.Id, stored.Id);
            Assert.Equal("导入工作区", stored.Name);
            var storedNote = Assert.Single(stored.Notes);
            Assert.Equal("导入笔记", storedNote.Title);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReplaceAllAsync_RollsBackDeletionWhenImportedWorkspaceInsertFails()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(directory, "workspace.db");
            var repository = new SqliteWorkspaceRepository(databasePath);
            var oldWorkspace = CreateWorkspace("原工作区", "原笔记");
            var importedWorkspace = CreateWorkspace("无法导入的工作区", "新笔记");
            await repository.ReplaceAllAsync(oldWorkspace);

            await CreateAbortWorkspaceInsertTriggerAsync(databasePath);
            await Assert.ThrowsAnyAsync<Exception>(() => repository.ReplaceAllAsync(importedWorkspace));
            await DropAbortWorkspaceInsertTriggerAsync(databasePath);

            var reopenedRepository = new SqliteWorkspaceRepository(databasePath);
            var stored = Assert.Single(await reopenedRepository.ListAsync());
            Assert.Equal(oldWorkspace.Id, stored.Id);
            var storedNote = Assert.Single(stored.Notes);
            Assert.Equal("原笔记", storedNote.Title);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Workspace CreateWorkspace(string name, params string[] noteTitles)
    {
        var workspace = Workspace.Create(name);
        foreach (var title in noteTitles)
        {
            workspace.AddNote(
                TodoBoardKeys.DayTodo,
                title,
                string.Empty,
                new NotePosition(10, 20),
                new NoteSize(260, 150),
                "#FFF8B8");
        }

        return workspace;
    }

    private static async Task CreateAbortWorkspaceInsertTriggerAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TRIGGER AbortWorkspaceInsert
            BEFORE INSERT ON Workspaces
            BEGIN
                SELECT RAISE(ABORT, 'replacement insert blocked');
            END;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropAbortWorkspaceInsertTriggerAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP TRIGGER AbortWorkspaceInsert;";
        await command.ExecuteNonQueryAsync();
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ConvenientNote.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
