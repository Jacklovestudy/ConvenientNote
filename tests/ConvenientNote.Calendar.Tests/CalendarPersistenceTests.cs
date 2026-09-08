using System.IO;
using ConvenientNote.Calendar.Application;
using ConvenientNote.Calendar.Domain;
using ConvenientNote.Calendar.Infrastructure;
using ConvenientNote.Platform.Contracts;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ConvenientNote.Calendar.Tests;

public sealed class CalendarPersistenceTests
{
    [Fact]
    public async Task CalendarWorksWithoutTodoProviderAndPreservesDurationAcrossConcurrentUpdates()
    {
        using var database = new Database();
        var context = new Context();
        var repository = new SqliteCalendarRepository(database.Path);
        using var service = new CalendarApplicationService(repository, context);
        Assert.False(service.SupportsTodos);
        var start = new DateTime(2026, 9, 8, 9, 30, 0);
        var id = await service.CreateEventAsync("meeting", start, start.AddMinutes(90), false);
        var entry = Assert.Single(await service.ListAsync());
        Assert.False(entry.IsTodo);
        Assert.Equal(id, entry.Id);
        await Task.WhenAll(service.SetCompletionAsync(entry, true), service.RescheduleAsync(entry, start.AddDays(2)));
        var persisted = Assert.Single(await new SqliteCalendarRepository(database.Path).ListAsync(context.Id));
        Assert.True(persisted.IsCompleted);
        Assert.Equal(start.AddDays(2), persisted.Start);
        Assert.Equal(TimeSpan.FromMinutes(90), persisted.End - persisted.Start);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateTodoAsync("todo", DateTime.Today));
        await service.DeleteEventAsync(id);
        Assert.Empty(await service.ListAsync());
    }

    [Fact]
    public async Task RepositoryUpdatesAndDeletesAreScopedToWorkspaceEvenForSameEventId()
    {
        using var database = new Database();
        var repository = new SqliteCalendarRepository(database.Path);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var item = CalendarEvent.Create("all day", new DateTime(2026, 9, 8), new DateTime(2026, 9, 10), true);
        await repository.SaveAsync(first, item);
        await repository.SaveAsync(second, item);
        item.SetCompletion(true);
        await repository.SaveAsync(first, item);
        Assert.True(Assert.Single(await repository.ListAsync(first)).IsCompleted);
        var untouched = Assert.Single(await repository.ListAsync(second));
        Assert.False(untouched.IsCompleted);
        Assert.True(untouched.IsAllDay);
        Assert.Equal(new DateTime(2026, 9, 10), untouched.End);
        await repository.DeleteAsync(first, item.Id);
        Assert.Empty(await repository.ListAsync(first));
        Assert.Single(await repository.ListAsync(second));
    }

    private sealed class Context : IWorkspaceContext
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Task<WorkspaceInfo> GetCurrentAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceInfo(Id, "test"));
    }

    private sealed class Database : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"calendar-{Guid.NewGuid():N}.db");
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(Path)) File.Delete(Path);
        }
    }
}

