namespace ConvenientNote.Todos.Contracts;
public sealed record CalendarTodoDto(Guid Id, string Title, bool IsCompleted, DateTime? PlannedDate);
public interface ITodoCalendarApi
{
    event EventHandler? Changed;
    Task<IReadOnlyList<CalendarTodoDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<Guid> CreateAsync(string title, DateTime date, CancellationToken cancellationToken = default);
    Task SetCompletionAsync(Guid id, bool completed, CancellationToken cancellationToken = default);
    Task RescheduleAsync(Guid id, DateTime? date, CancellationToken cancellationToken = default);
}
