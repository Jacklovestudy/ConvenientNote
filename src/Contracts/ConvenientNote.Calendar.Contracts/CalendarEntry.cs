namespace ConvenientNote.Calendar.Contracts;

public sealed record CalendarEntry(Guid Id, string Title, DateTime? PlannedDate, bool IsCompleted,
    bool IsTodo, DateTime? End = null, bool IsAllDay = true)
{
    public bool OccursOn(DateTime date) => PlannedDate is { } start &&
        (IsTodo ? start.Date == date.Date : start < date.Date.AddDays(1) && End > date.Date);
}

public interface ICalendarApi
{
    event EventHandler? Changed;
    Task<IReadOnlyList<CalendarEntry>> ListAsync(CancellationToken cancellationToken = default);
    Task<Guid> CreateEventAsync(string title, DateTime start, DateTime end, bool isAllDay, CancellationToken cancellationToken = default);
}
