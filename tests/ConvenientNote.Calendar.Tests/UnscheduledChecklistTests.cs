using System.IO;
using ConvenientNote.Calendar.Application;
using ConvenientNote.Calendar.Domain;
using ConvenientNote.Calendar.Infrastructure;
using ConvenientNote.Platform.Contracts;
using ConvenientNote.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ConvenientNote.Calendar.Tests;
public sealed class UnscheduledChecklistTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"checklist-{Guid.NewGuid():N}.db");
    private sealed class Context : IWorkspaceContext
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Task<WorkspaceInfo> GetCurrentAsync(CancellationToken ct = default) => Task.FromResult(new WorkspaceInfo(Id, "test"));
    }
    [Fact]
    public async Task ImportLeavesOnlyThreeEventsOnDepartureDayAndChecklistCanBeScheduledAndCleared()
    {
        var context = new Context();
        using var service = new CalendarApplicationService(new SqliteCalendarRepository(_path), context);
        var batch = await service.ImportAsync(ItineraryParser.Parse(YunnanItineraryFixture.Text, 2026));
        var entries = await service.ListAsync();
        Assert.Equal(3, entries.Count(e => e.OccursOn(new(2026, 9, 24))));
        Assert.Equal(15, entries.Count(e => e.PlannedDate is null && e.IsChecklist));
        using var viewModel = new ScheduleViewModel(service);
        await viewModel.RefreshAsync(); viewModel.NavigateToDate(new(2026, 9, 24));
        Assert.Equal(3, viewModel.SelectedTasks.Count);
        Assert.Equal(15, viewModel.UnscheduledTasks.Count);
        var check = entries.First(e => e.IsChecklist);
        await service.RescheduleAsync(check, new DateTime(2026, 9, 23));
        check = (await service.ListAsync()).Single(e => e.Id == check.Id);
        Assert.Equal(new DateTime(2026, 9, 23), check.PlannedDate);
        await service.RescheduleAsync(check, null);
        check = (await service.ListAsync()).Single(e => e.Id == check.Id);
        Assert.Null(check.PlannedDate);
        await service.SetCompletionAsync(check, true);
        using var reopened = new CalendarApplicationService(new SqliteCalendarRepository(_path), context);
        Assert.True((await reopened.ListAsync()).Single(e => e.Id == check.Id).IsCompleted);
        await reopened.UndoImportAsync(batch);
        Assert.Empty(await reopened.ListAsync());
    }
    [Fact]
    public async Task LegacyDefaultDatesMoveToUnscheduledButManuallyMovedDatesRemain()
    {
        var context = new Context(); var repository = new SqliteCalendarRepository(_path);
        var batch = Guid.NewGuid(); var day = new DateTime(2026, 9, 24);
        var daily = CalendarEvent.Restore(Guid.NewGuid(), "出发", day, day.AddDays(1), true, false, "", batch);
        var check = CalendarEvent.Restore(Guid.NewGuid(), "核对", day, day.AddDays(1), true, false, "截止日期未指定；暂放在行程首日，可在导入预览中修改日期。\n□ 核对", batch, "trip", true);
        var moved = CalendarEvent.Restore(Guid.NewGuid(), "已改期", day.AddDays(-1), day, true, false, check.Details, batch, "trip", true);
        foreach (var item in new[] { daily, check, moved }) await repository.SaveAsync(context.Id, item);
        using (var connection = new SqliteConnection($"Data Source={_path}"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Calendar_Events SET Metadata=json_remove(Metadata,'$.IsUnscheduled')";
            command.ExecuteNonQuery();
        }
        using var service = new CalendarApplicationService(repository, context);
        Assert.Null((await service.ListAsync()).Single(e => e.Id == check.Id).PlannedDate);
        Assert.DoesNotContain("暂放在行程首日", (await service.ListAsync()).Single(e => e.Id == check.Id).Details);
        Assert.Equal(day.AddDays(-1), (await service.ListAsync()).Single(e => e.Id == moved.Id).PlannedDate);
        await service.RescheduleAsync((await service.ListAsync()).Single(e => e.Id == check.Id), day);
        Assert.Equal(day, (await service.ListAsync()).Single(e => e.Id == check.Id).PlannedDate);
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); if (File.Exists(_path)) File.Delete(_path); }
}
