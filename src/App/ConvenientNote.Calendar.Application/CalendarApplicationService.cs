using ConvenientNote.Calendar.Contracts;
using ConvenientNote.Calendar.Domain;
using ConvenientNote.Platform.Contracts;

namespace ConvenientNote.Calendar.Application;

public sealed class CalendarApplicationService : ICalendarApi, IDisposable
{
    private readonly ICalendarRepository _repository;
    private readonly IWorkspaceContext _workspace;
    private readonly ITodoScheduleSource? _todos;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public CalendarApplicationService(ICalendarRepository repository, IWorkspaceContext workspace, ITodoScheduleSource? todos = null)
    {
        _repository = repository;
        _workspace = workspace;
        _todos = todos;
        if (_todos is not null) _todos.Changed += OnTodosChanged;
    }

    public event EventHandler? Changed;
    public bool SupportsTodos => _todos is not null;
    private void OnTodosChanged(object? sender, EventArgs e) => NotifyChanged();
    private void NotifyChanged()
    {
        if (Changed is not { } handlers) return;
        foreach (EventHandler handler in handlers.GetInvocationList())
            try { handler(this, EventArgs.Empty); } catch { /* A view refresh cannot roll back a committed write. */ }
    }

    public async Task<IReadOnlyList<CalendarEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        var workspace = await _workspace.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var events = await _repository.ListAsync(workspace.Id, cancellationToken).ConfigureAwait(false);
        var entries = events.Select(e => new CalendarEntry(e.Id, e.Title, e.Start, e.IsCompleted, false, e.End, e.IsAllDay)).ToList();
        if (_todos is not null)
        {
            var todos = await _todos.ListAsync(cancellationToken).ConfigureAwait(false);
            entries.AddRange(todos.Select(t => new CalendarEntry(t.Id, t.Title, t.PlannedDate, t.IsCompleted, true)));
        }
        return entries;
    }

    public async Task<Guid> CreateEventAsync(string title, DateTime start, DateTime end, bool isAllDay, CancellationToken cancellationToken = default)
    {
        var item = CalendarEvent.Create(title, start, end, isAllDay);
        await WriteAsync((id, ct) => _repository.SaveAsync(id, item, ct), cancellationToken).ConfigureAwait(false);
        return item.Id;
    }

    public Task<Guid> CreateTodoAsync(string title, DateTime date, CancellationToken cancellationToken = default)
        => (_todos ?? throw new InvalidOperationException("待办模块未启用。")).CreateAsync(title, date, cancellationToken);

    public Task SetCompletionAsync(CalendarEntry entry, bool completed, CancellationToken cancellationToken = default)
        => entry.IsTodo
            ? (_todos ?? throw new InvalidOperationException("待办模块未启用。")).SetCompletionAsync(entry.Id, completed, cancellationToken)
            : UpdateEventAsync(entry.Id, item => item.SetCompletion(completed), cancellationToken);

    public Task RescheduleAsync(CalendarEntry entry, DateTime? date, CancellationToken cancellationToken = default)
    {
        if (entry.IsTodo) return (_todos ?? throw new InvalidOperationException("待办模块未启用。")).RescheduleAsync(entry.Id, date, cancellationToken);
        if (date is null) throw new ArgumentException("日程需要日期；只有待办可以移回待安排。");
        return UpdateEventAsync(entry.Id, item => item.Reschedule(date.Value), cancellationToken);
    }

    public Task DeleteEventAsync(Guid id, CancellationToken cancellationToken = default)
        => WriteAsync((workspaceId, ct) => _repository.DeleteAsync(workspaceId, id, ct), cancellationToken);

    private Task UpdateEventAsync(Guid id, Action<CalendarEvent> update, CancellationToken ct)
        => WriteAsync(async (workspaceId, token) =>
        {
            var items = await _repository.ListAsync(workspaceId, token).ConfigureAwait(false);
            var item = items.SingleOrDefault(e => e.Id == id) ?? throw new InvalidOperationException("日程不存在，请刷新。");
            update(item);
            await _repository.SaveAsync(workspaceId, item, token).ConfigureAwait(false);
        }, ct);

    private async Task WriteAsync(Func<Guid, CancellationToken, Task> action, CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var workspace = await _workspace.GetCurrentAsync(ct).ConfigureAwait(false);
            await action(workspace.Id, ct).ConfigureAwait(false);
        }
        finally { _writeGate.Release(); }
        NotifyChanged();
    }

    public void Dispose() { if (_todos is not null) _todos.Changed -= OnTodosChanged; }
}
