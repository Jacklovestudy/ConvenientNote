using System.IO;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;
using ConvenientNote.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ConvenientNote.Tests.Infrastructure;

public sealed class ActiveNotesReplacementTests
{
    [Fact]
    public async Task ApplicationServiceReplaceActiveNotesAsync_ReturnsReloadedWorkspaceSnapshot()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var repository = new SqliteWorkspaceRepository(Path.Combine(directory, "workspace.db"));
            var workspace = CreateWorkspaceWithMixedNotes();
            var importedNotes = new[]
            {
                CreateNote(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), TodoBoardKeys.Notes, "经应用服务导入的笔记")
            };
            await repository.SaveAsync(workspace);
            var service = new WorkspaceApplicationService(repository);

            var snapshot = await service.ReplaceActiveNotesAsync(workspace.Id, importedNotes);

            Assert.Equal(workspace.Id, snapshot.Id);
            Assert.Equal(workspace.Name, snapshot.Name);
            Assert.Equal(workspace.CreatedAt, snapshot.CreatedAt);
            Assert.Equal(workspace.UpdatedAt, snapshot.UpdatedAt);
            Assert.Equal(
                importedNotes.Select(note => note.Id),
                snapshot.Notes
                    .Where(note => note.BoardKey == TodoBoardKeys.Notes && !note.IsDeleted)
                    .Select(note => note.Id));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReplaceActiveNotesAsync_ReplacesOnlyActiveNotesAndPreservesWorkspaceMetadata()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(directory, "workspace.db");
            var repository = new SqliteWorkspaceRepository(databasePath);
            var workspace = CreateWorkspaceWithMixedNotes();
            var importedNotes = new[]
            {
                CreateNote(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), TodoBoardKeys.Notes, "导入笔记一"),
                CreateNote(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), TodoBoardKeys.Notes, "导入笔记二")
            };

            await repository.SaveAsync(workspace);
            await repository.ReplaceActiveNotesAsync(workspace.Id, importedNotes);

            var reopenedRepository = new SqliteWorkspaceRepository(databasePath);
            var stored = await reopenedRepository.GetAsync(workspace.Id);

            Assert.NotNull(stored);
            Assert.Equal(workspace.Id, stored.Id);
            Assert.Equal(workspace.Name, stored.Name);
            Assert.Equal(workspace.CreatedAt, stored.CreatedAt);
            Assert.Equal(workspace.UpdatedAt, stored.UpdatedAt);
            Assert.Equal(importedNotes.Select(note => note.Id).OrderBy(id => id.Value),
                stored.Notes.Where(IsActiveNote).Select(note => note.Id).OrderBy(id => id.Value));

            AssertNotesEqual(stored, workspace.Notes.Where(note => !IsActiveNote(note)));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReplaceActiveNotesAsync_RollsBackWhenImportedInsertionIsAborted()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(directory, "workspace.db");
            var repository = new SqliteWorkspaceRepository(databasePath);
            var workspace = CreateWorkspaceWithMixedNotes();
            var importedNotes = new[]
            {
                CreateNote(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), TodoBoardKeys.Notes, "会被阻止的导入笔记")
            };

            await repository.SaveAsync(workspace);
            await CreateAbortNoteInsertTriggerAsync(databasePath);

            await Assert.ThrowsAnyAsync<Exception>(() => repository.ReplaceActiveNotesAsync(workspace.Id, importedNotes));
            await DropAbortNoteInsertTriggerAsync(databasePath);

            var reopenedRepository = new SqliteWorkspaceRepository(databasePath);
            var stored = await reopenedRepository.GetAsync(workspace.Id);

            Assert.NotNull(stored);
            Assert.Equal(workspace.Notes.Select(note => note.Id).OrderBy(id => id.Value),
                stored.Notes.Select(note => note.Id).OrderBy(id => id.Value));
            AssertNotesEqual(stored, workspace.Notes);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReplaceActiveNotesAsync_RejectsIdCollisionWithDeletedNoteAndRollsBack()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(directory, "workspace.db");
            var repository = new SqliteWorkspaceRepository(databasePath);
            var workspace = CreateWorkspaceWithMixedNotes();
            var deletedNote = Assert.Single(workspace.Notes, note => note.IsDeleted);
            var importedNotes = new[]
            {
                CreateNote(deletedNote.Id.Value, TodoBoardKeys.Notes, "冲突导入笔记")
            };

            await repository.SaveAsync(workspace);

            await Assert.ThrowsAnyAsync<Exception>(() => repository.ReplaceActiveNotesAsync(workspace.Id, importedNotes));

            var reopenedRepository = new SqliteWorkspaceRepository(databasePath);
            var stored = await reopenedRepository.GetAsync(workspace.Id);

            Assert.NotNull(stored);
            Assert.Equal(workspace.Notes.Select(note => note.Id).OrderBy(id => id.Value),
                stored.Notes.Select(note => note.Id).OrderBy(id => id.Value));
            AssertNotesEqual(stored, workspace.Notes);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Workspace CreateWorkspaceWithMixedNotes()
    {
        var createdAt = new DateTimeOffset(2026, 8, 1, 2, 3, 4, TimeSpan.Zero);
        var updatedAt = new DateTimeOffset(2026, 8, 2, 3, 4, 5, TimeSpan.Zero);
        return new Workspace(
            new WorkspaceId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            "保留元数据的工作区",
            createdAt,
            updatedAt,
            [
                CreateNote(Guid.Parse("22222222-2222-2222-2222-222222222222"), TodoBoardKeys.Notes, "旧活动笔记一"),
                CreateNote(Guid.Parse("33333333-3333-3333-3333-333333333333"), TodoBoardKeys.Notes, "旧活动笔记二"),
                CreateNote(Guid.Parse("44444444-4444-4444-4444-444444444444"), TodoBoardKeys.Notes, "回收站笔记", isDeleted: true),
                CreateNote(Guid.Parse("55555555-5555-5555-5555-555555555555"), TodoBoardKeys.DayTodo, "日待办"),
                CreateNote(Guid.Parse("66666666-6666-6666-6666-666666666666"), "inbox", "收件箱待办"),
                CreateNote(Guid.Parse("77777777-7777-7777-7777-777777777777"), TodoBoardKeys.DayTodo, "已完成待办", isCompleted: true)
            ]);
    }

    private static Note CreateNote(
        Guid id,
        string boardKey,
        string title,
        bool isCompleted = false,
        bool isDeleted = false)
    {
        var createdAt = new DateTimeOffset(2026, 8, 3, 4, 5, 6, TimeSpan.Zero);
        var updatedAt = new DateTimeOffset(2026, 8, 4, 5, 6, 7, TimeSpan.Zero);
        return new Note(
            new NoteId(id),
            boardKey,
            "green",
            title,
            $"{title} 正文",
            new NotePosition(12.5, 34.5),
            new NoteSize(280, 160),
            "#ABCDEF",
            7,
            isCompleted,
            createdAt,
            updatedAt,
            "{\"type\":\"doc\"}",
            null,
            ["标签一", "标签二"],
            isPinned: true,
            isFavorite: true,
            isDeleted: isDeleted);
    }

    private static void AssertNotesEqual(
        Workspace stored,
        IEnumerable<Note> expectedNotes)
    {
        foreach (var expected in expectedNotes)
        {
            var actual = Assert.Single(stored.Notes, note => note.Id == expected.Id);
            Assert.Equal(expected.BoardKey, actual.BoardKey);
            Assert.Equal(expected.Priority, actual.Priority);
            Assert.Equal(expected.Title, actual.Title);
            Assert.Equal(expected.Content, actual.Content);
            Assert.Equal(expected.Position, actual.Position);
            Assert.Equal(expected.Size, actual.Size);
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
    }

    private static bool IsActiveNote(Note note) => note.BoardKey == TodoBoardKeys.Notes && !note.IsDeleted;

    private static async Task CreateAbortNoteInsertTriggerAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TRIGGER AbortImportedNoteInsert
            BEFORE INSERT ON Notes
            BEGIN
                SELECT RAISE(ABORT, 'active-note replacement insert blocked');
            END;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropAbortNoteInsertTriggerAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP TRIGGER AbortImportedNoteInsert;";
        await command.ExecuteNonQueryAsync();
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ConvenientNote.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
