using ConvenientNote.Application.Workspaces;
using ConvenientNote.Calendar.Application;
using ConvenientNote.Calendar.Domain;
using ConvenientNote.Platform.Contracts;

namespace ConvenientNote.Tests.Compatibility;

/// <summary>Test-only bridge retaining legacy persistence fault fixtures while exercising the new calendar application.</summary>
internal static class CalendarServiceFixture
{
    public static CalendarApplicationService Create(WorkspaceApplicationService legacy)
        => new(new EventRepository(), new WorkspaceContext(legacy), new TodoSource(legacy));

    private sealed class WorkspaceContext(WorkspaceApplicationService legacy) : IWorkspaceContext
    {
        public async Task<WorkspaceInfo> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            var workspace = await legacy.GetOrCreateDefaultWorkspaceAsync(cancellationToken);
            return new(workspace.Id.Value, workspace.Name);
        }
    }

    private sealed class TodoSource(WorkspaceApplicationService legacy) : ITodoScheduleSource
    {
        public event EventHandler? Changed
        {
            add => legacy.WorkspaceChanged += value;
            remove => legacy.WorkspaceChanged -= value;
        }

        public async Task<IReadOnlyList<ExternalTodo>> ListAsync(CancellationToken cancellationToken = default)
        {
            var workspace = await legacy.GetOrCreateDefaultWorkspaceAsync(cancellationToken);
            return workspace.Notes.Where(n => n.BoardKey == TodoBoardKeys.DayTodo && !n.IsDeleted)
                .Select(n => new ExternalTodo(n.Id.Value, n.Title, n.IsCompleted, n.PlannedDate)).ToArray();
        }

        public async Task<Guid> CreateAsync(string title, DateTime date, CancellationToken cancellationToken = default)
        {
            var workspace = await legacy.GetOrCreateDefaultWorkspaceAsync(cancellationToken);
            return (await legacy.CreateScheduledTodoAsync(workspace.Id, title, date, cancellationToken)).Id.Value;
        }

        public async Task SetCompletionAsync(Guid id, bool completed, CancellationToken cancellationToken = default)
        {
            var workspace = await legacy.GetOrCreateDefaultWorkspaceAsync(cancellationToken);
            await legacy.SetNoteCompletionAsync(workspace.Id, new(id), completed, cancellationToken);
        }

        public async Task RescheduleAsync(Guid id, DateTime? date, CancellationToken cancellationToken = default)
        {
            var workspace = await legacy.GetOrCreateDefaultWorkspaceAsync(cancellationToken);
            await legacy.SetNotePlannedDateAsync(workspace.Id, new(id), date, cancellationToken);
        }
    }

    private sealed class EventRepository : ICalendarRepository
    {
        private readonly Dictionary<(Guid WorkspaceId, Guid Id), CalendarEvent> _events = new();
        public Task<IReadOnlyList<CalendarEvent>> ListAsync(Guid workspaceId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CalendarEvent>>(_events.Where(pair => pair.Key.WorkspaceId == workspaceId).Select(pair => pair.Value).ToArray());
        public Task SaveAsync(Guid workspaceId, CalendarEvent item, CancellationToken cancellationToken = default)
        {
            _events[(workspaceId, item.Id)] = item;
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid workspaceId, Guid id, CancellationToken cancellationToken = default)
        {
            _events.Remove((workspaceId, id));
            return Task.CompletedTask;
        }
    }
}
