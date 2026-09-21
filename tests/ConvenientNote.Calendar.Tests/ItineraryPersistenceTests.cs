using System.IO;
using ConvenientNote.Calendar.Application;
using ConvenientNote.Calendar.Infrastructure;
using ConvenientNote.Platform.Contracts;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ConvenientNote.Calendar.Tests;

public sealed class ItineraryPersistenceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"trip-{Guid.NewGuid():N}.db");
    private sealed class Context : IWorkspaceContext
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Task<WorkspaceInfo> GetCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceInfo(Id, "test"));
    }

    [Fact]
    public async Task ImportSurvivesRestartRejectsDuplicatesAndUndoKeepsOtherEvents()
    {
        var context = new Context();
        using var service = new CalendarApplicationService(new SqliteCalendarRepository(_path), context);
        var day = new DateTime(2026, 9, 24);
        await service.CreateEventAsync("existing", day, day.AddDays(1), true);
        var preview = new ItineraryPreview("云南旅行", "original source", "背包安排", [new("进村", day, day.AddDays(1), true, "酒店与路线"), new("确认接机", day, day.AddDays(1), true, "待核对", true)]);
        var batch = await service.ImportAsync(preview);
        using var reopened = new CalendarApplicationService(new SqliteCalendarRepository(_path), context);
        var entry = (await reopened.ListAsync()).Single(e => e.Title == "进村");
        Assert.Equal("酒店与路线", entry.Details);
        Assert.Equal(batch, entry.BatchId);
        Assert.Single(await reopened.ListAsync(), e => e.IsChecklist);
        Assert.Equal("original source", Assert.Single(await reopened.ListBatchesAsync()).SourceText);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.ImportAsync(preview));
        await reopened.EditEventAsync(entry.Id, "新标题", day.AddDays(1), day.AddDays(2), true, "新详情");
        Assert.Equal("新详情", (await reopened.ListAsync()).Single(e => e.Id == entry.Id).Details);
        await reopened.UndoImportAsync(batch);
        Assert.Equal("existing", Assert.Single(await reopened.ListAsync()).Title);
        Assert.Empty(await reopened.ListBatchesAsync());
        await reopened.ImportAsync(preview);
    }

    [Fact]
    public async Task InvalidDraftDoesNotPartiallyImportAndOtherWorkspaceCannotUndo()
    {
        var day = new DateTime(2026, 9, 24);
        using var service = new CalendarApplicationService(new SqliteCalendarRepository(_path), new Context());
        var valid = new ItineraryDraft("daily", day, day.AddDays(1), true, "details");
        await Assert.ThrowsAsync<ArgumentException>(() => service.ImportAsync(new("trip", "source", "", [valid, valid with { End = day }])));
        Assert.Empty(await service.ListAsync());
        var id = await service.ImportAsync(new("trip", "source", "", [valid]));
        using var other = new CalendarApplicationService(new SqliteCalendarRepository(_path), new Context());
        await other.UndoImportAsync(id);
        Assert.Single(await service.ListAsync());
    }

    [Fact]
    public async Task LegacySchemaMigratesWithoutLosingEventsAndDetailsSurviveCompletion()
    {
        var context = new Context();
        var id = Guid.NewGuid();
        using (var connection = new SqliteConnection($"Data Source={_path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Calendar_Events (WorkspaceId TEXT NOT NULL,Id TEXT NOT NULL,Title TEXT NOT NULL,StartsAt TEXT NOT NULL,EndsAt TEXT NOT NULL,IsAllDay INTEGER NOT NULL,IsCompleted INTEGER NOT NULL,PRIMARY KEY(WorkspaceId,Id));
                INSERT INTO Calendar_Events VALUES($workspace,$id,'old','2026-09-24T00:00:00','2026-09-25T00:00:00',1,0);
                """;
            command.Parameters.AddWithValue("$workspace", context.Id.ToString("D")); command.Parameters.AddWithValue("$id", id.ToString("D"));
            command.ExecuteNonQuery();
        }
        using var service = new CalendarApplicationService(new SqliteCalendarRepository(_path), context);
        var old = Assert.Single(await service.ListAsync());
        Assert.Equal("", old.Details);
        await service.EditEventAsync(id, "edited", old.PlannedDate!.Value, old.End!.Value, true, "住宿\n保留全文");
        await service.SetCompletionAsync(old, true);
        var edited = Assert.Single(await service.ListAsync());
        Assert.Equal("住宿\n保留全文", edited.Details);
        Assert.True(edited.IsCompleted);
    }

    [Fact]
    public async Task DatabaseFailureRollsBackBatchAndAllEvents()
    {
        var context = new Context();
        using var service = new CalendarApplicationService(new SqliteCalendarRepository(_path), context);
        await service.ListAsync();
        using (var connection = new SqliteConnection($"Data Source={_path}"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_import BEFORE INSERT ON Calendar_Events WHEN NEW.Title='fail' BEGIN SELECT RAISE(ABORT,'test failure'); END";
            command.ExecuteNonQuery();
        }
        var day = new DateTime(2026, 9, 24);
        await Assert.ThrowsAsync<SqliteException>(() => service.ImportAsync(new("trip", "source", "", [new("first", day, day.AddDays(1), true, ""), new("fail", day, day.AddDays(1), true, "")])));
        Assert.Empty(await service.ListAsync()); Assert.Empty(await service.ListBatchesAsync());
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); if (File.Exists(_path)) File.Delete(_path); }
}
