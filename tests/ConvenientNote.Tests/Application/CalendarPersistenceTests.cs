using ConvenientNote.Application.Workspaces;
using ConvenientNote.Infrastructure.Persistence;
using ConvenientNote.Domain.Notes;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ConvenientNote.Tests.Application;

public sealed class CalendarPersistenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlannedDateAndCompletionSurviveReloadAndUncompletion(bool json)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ConvenientNoteCalendar", Guid.NewGuid().ToString("N"), json ? "notes.json" : "notes.db");
        var service = new WorkspaceApplicationService(json ? new JsonWorkspaceRepository(path) : new SqliteWorkspaceRepository(path));
        var workspace = await service.GetOrCreateDefaultWorkspaceAsync();
        var note = await service.CreateScheduledTodoAsync(workspace.Id, "复习", new DateTime(2026, 9, 5, 18, 0, 0));
        Assert.Equal(new DateTime(2026, 9, 5), note.PlannedDate);
        await service.SetNoteCompletionAsync(workspace.Id, note.Id, true);
        var completed = Assert.Single((await service.GetWorkspaceAsync(workspace.Id)).Notes);
        Assert.NotNull(completed.CompletedAt);
        await service.UpdateNoteTitleAsync(workspace.Id, note.Id, "复习章节");
        Assert.Equal(completed.CompletedAt, Assert.Single((await service.GetWorkspaceAsync(workspace.Id)).Notes).CompletedAt);
        await service.SetNotePlannedDateAsync(workspace.Id, note.Id, new DateTime(2026, 10, 1));
        await service.SetNoteCompletionAsync(workspace.Id, note.Id, false);
        var reopened = new WorkspaceApplicationService(json ? new JsonWorkspaceRepository(path) : new SqliteWorkspaceRepository(path));
        var saved = Assert.Single((await reopened.GetWorkspaceAsync(workspace.Id)).Notes);
        Assert.Equal(new DateTime(2026, 10, 1), saved.PlannedDate);
        Assert.Null(saved.CompletedAt);
        await reopened.SetNotePlannedDateAsync(workspace.Id, note.Id, null);
        Assert.Null(Assert.Single((await reopened.GetWorkspaceAsync(workspace.Id)).Notes).PlannedDate);
    }

    [Fact]
    public async Task ExistingSqliteSchemaAddsNullableDatesWithoutAssigningOldTodos()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".db");
        var original = new WorkspaceApplicationService(new SqliteWorkspaceRepository(path));
        var workspace = await original.GetOrCreateDefaultWorkspaceAsync();
        var old = await original.CreateNoteAsync(workspace.Id, 0, 0, "旧任务");
        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE Notes DROP COLUMN PlannedDate; ALTER TABLE Notes DROP COLUMN CompletedAt;";
            await command.ExecuteNonQueryAsync();
        }
        var upgraded = new WorkspaceApplicationService(new SqliteWorkspaceRepository(path));
        var saved = Assert.Single((await upgraded.GetWorkspaceAsync(workspace.Id)).Notes);
        Assert.Equal(old.Id, saved.Id);
        Assert.Null(saved.PlannedDate);
        Assert.Null(saved.CompletedAt);
    }

    [Fact]
    public async Task ConcurrentCalendarWritesPreserveBothTasksAndNotifyAfterPersistence()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".db");
        var service = new WorkspaceApplicationService(new SqliteWorkspaceRepository(path));
        var workspace = await service.GetOrCreateDefaultWorkspaceAsync();
        var changes = 0;
        service.WorkspaceChanged += (_, _) => changes++;
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => service.CreateScheduledTodoAsync(workspace.Id, $"任务{i}", DateTime.Today.AddDays(i))));
        Assert.Equal(8, (await service.GetWorkspaceAsync(workspace.Id)).Notes.Count);
        Assert.Equal(8, changes);
    }
}
