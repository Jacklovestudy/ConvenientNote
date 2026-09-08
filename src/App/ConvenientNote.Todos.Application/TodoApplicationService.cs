using ConvenientNote.Todos.Domain;
using ConvenientNote.Todos.Contracts;
using ConvenientNote.Platform.Contracts;

namespace ConvenientNote.Todos.Application;
public sealed class TodoApplicationService(ITodoRepository repository, IWorkspaceContext workspaceContext) : ITodoCalendarApi
{
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    public event EventHandler? Changed;
    public async Task<TodoWorkspaceSnapshot> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var workspace = await workspaceContext.GetCurrentAsync(cancellationToken);
        return new(workspace.Id, await repository.ListAsync(workspace.Id, cancellationToken));
    }

    public async Task<TodoItem> CreateTodoAsync(Guid workspaceId, double x, double y, string title, DateTime? plannedDate = null, CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken);
        TodoItem todo;
        try
        {
            var items = await repository.ListAsync(workspaceId, cancellationToken);
            if (plannedDate is { } date)
            {
                var offset = items.Count(item => !item.IsDeleted && item.PlannedDate == date.Date);
                x = 32 + offset % 4 * 290;
                y = 32 + offset / 4 * 180;
            }

            todo = TodoItem.Create(title, x, y, items.Count == 0 ? 0 : items.Max(x => x.ZIndex) + 1);
            todo.Reschedule(plannedDate);
            await repository.SaveAsync(workspaceId, [todo], cancellationToken);
        }
        finally
        {
            _mutationGate.Release();
        }

        NotifyChanged();
        return todo;
    }

    public Task RenameAsync(Guid workspaceId, TodoId id, string title) => MutateAsync(workspaceId, id, t => t.Rename(title));
    public Task UpdateContentAsync(Guid workspaceId, TodoId id, string content) => MutateAsync(workspaceId, id, t => t.UpdateContent(content));
    public Task SetPriorityAsync(Guid workspaceId, TodoId id, string priority) => MutateAsync(workspaceId, id, t => t.SetPriority(priority));
    public Task MoveAsync(Guid workspaceId, TodoId id, double x, double y) => MutateAsync(workspaceId, id, t => t.MoveTo(x, y));
    public Task DeleteAsync(Guid workspaceId, TodoId id) => MutateAsync(workspaceId, id, t => t.Delete());
    public Task SetCompletionAsync(Guid workspaceId, TodoId id, bool completed) => MutateAsync(workspaceId, id, t => t.SetCompletion(completed));
    public async Task MoveManyAsync(Guid workspaceId, IReadOnlyList<TodoPositionUpdate> positions)
    {
        await _mutationGate.WaitAsync();
        try
        {
            var items = (await repository.ListAsync(workspaceId)).ToDictionary(t => t.Id);
            var changed = new List<TodoItem>();
            foreach (var p in positions)
            {
                var item = items.TryGetValue(p.TodoId, out var todo) ? todo : throw new KeyNotFoundException();
                item.MoveTo(p.X, p.Y);
                changed.Add(item);
            }

            await repository.SaveAsync(workspaceId, changed);
        }
        finally
        {
            _mutationGate.Release();
        }

        NotifyChanged();
    }

    private async Task MutateAsync(Guid workspaceId, TodoId id, Action<TodoItem> action, CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var item = (await repository.ListAsync(workspaceId, cancellationToken)).SingleOrDefault(x => x.Id == id && !x.IsDeleted) ?? throw new KeyNotFoundException("Todo was not found.");
            action(item);
            await repository.SaveAsync(workspaceId, [item], cancellationToken);
        }
        finally
        {
            _mutationGate.Release();
        }

        NotifyChanged();
    }

    private void NotifyChanged()
    {
        if (Changed is not { } handlers)
            return;
        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceError("Todo change subscriber failed after save: {0}", exception);
            }
        }
    }

    public async Task<IReadOnlyList<CalendarTodoDto>> ListAsync(CancellationToken cancellationToken = default) => (await GetCurrentAsync(cancellationToken)).Todos.Where(t => !t.IsDeleted).Select(t => new CalendarTodoDto(t.Id.Value, t.Title, t.IsCompleted, t.PlannedDate)).ToArray();
    public async Task<Guid> CreateAsync(string title, DateTime date, CancellationToken cancellationToken = default)
    {
        var workspace = await workspaceContext.GetCurrentAsync(cancellationToken);
        return (await CreateTodoAsync(workspace.Id, 32, 32, title, date, cancellationToken)).Id.Value;
    }

    public async Task SetCompletionAsync(Guid id, bool completed, CancellationToken cancellationToken = default)
    {
        var workspace = await workspaceContext.GetCurrentAsync(cancellationToken);
        await MutateAsync(workspace.Id, new(id), t => t.SetCompletion(completed), cancellationToken);
    }

    public async Task RescheduleAsync(Guid id, DateTime? date, CancellationToken cancellationToken = default)
    {
        var workspace = await workspaceContext.GetCurrentAsync(cancellationToken);
        await MutateAsync(workspace.Id, new(id), t => t.Reschedule(date), cancellationToken);
    }
}
