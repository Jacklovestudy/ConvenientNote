using ConvenientNote.Calendar.Domain;

namespace ConvenientNote.Calendar.Application;

public interface ICalendarRepository
{
    Task<IReadOnlyList<CalendarEvent>> ListAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task SaveAsync(Guid workspaceId, CalendarEvent item, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid workspaceId, Guid id, CancellationToken cancellationToken = default);
}

public sealed record CalendarImportBatch(Guid Id, string Name, string Fingerprint, string SourceText,
    string Overview, DateTime ImportedAt, int ItemCount);

public interface ICalendarBatchRepository
{
    Task ImportAsync(Guid workspaceId, CalendarImportBatch batch, IReadOnlyList<CalendarEvent> items, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CalendarImportBatch>> ListBatchesAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task UndoImportAsync(Guid workspaceId, Guid batchId, CancellationToken cancellationToken = default);
}

public sealed record ExternalTodo(Guid Id, string Title, bool IsCompleted, DateTime? PlannedDate);

/// <summary>Optional integration port. The calendar domain never depends on a todo model.</summary>
public interface ITodoScheduleSource
{
    event EventHandler? Changed;
    Task<IReadOnlyList<ExternalTodo>> ListAsync(CancellationToken cancellationToken = default);
    Task<Guid> CreateAsync(string title, DateTime date, CancellationToken cancellationToken = default);
    Task SetCompletionAsync(Guid id, bool completed, CancellationToken cancellationToken = default);
    Task RescheduleAsync(Guid id, DateTime? date, CancellationToken cancellationToken = default);
}
