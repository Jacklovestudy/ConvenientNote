using ConvenientNote.Calendar.Application;
using ConvenientNote.Todos.Contracts;

namespace ConvenientNote;

/// <summary>Composition-owned translation between independently defined module contracts.</summary>
public sealed class TodoScheduleAdapter(ITodoCalendarApi todos) : ITodoScheduleSource
{
    public event EventHandler? Changed { add => todos.Changed += value; remove => todos.Changed -= value; }
    public async Task<IReadOnlyList<ExternalTodo>> ListAsync(CancellationToken cancellationToken = default)
        => (await todos.ListAsync(cancellationToken).ConfigureAwait(false))
            .Select(t => new ExternalTodo(t.Id, t.Title, t.IsCompleted, t.PlannedDate)).ToArray();
    public Task<Guid> CreateAsync(string title, DateTime date, CancellationToken cancellationToken = default)
        => todos.CreateAsync(title, date, cancellationToken);
    public Task SetCompletionAsync(Guid id, bool completed, CancellationToken cancellationToken = default)
        => todos.SetCompletionAsync(id, completed, cancellationToken);
    public Task RescheduleAsync(Guid id, DateTime? date, CancellationToken cancellationToken = default)
        => todos.RescheduleAsync(id, date, cancellationToken);
}
