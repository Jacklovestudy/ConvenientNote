namespace ConvenientNote.Notes.Domain.Notes;

/// <summary>
/// Opaque data retained for lossless import/export of the previous mixed record format.
/// Notes never interpret these values as task rules or expose task mutation methods.
/// </summary>
public sealed record LegacyNoteMetadata
{
    public LegacyNoteMetadata(string priority = "blue", bool isCompleted = false,
        DateTime? plannedDate = null, DateTimeOffset? completedAt = null)
    {
        Priority = string.IsNullOrWhiteSpace(priority) ? "blue" : priority.Trim().ToLowerInvariant();
        if (Priority is not ("red" or "green" or "blue"))
        {
            throw new DomainException("Legacy priority must be red, green, or blue.");
        }
        IsCompleted = isCompleted;
        PlannedDate = plannedDate;
        CompletedAt = completedAt;
    }

    public string Priority { get; }
    public bool IsCompleted { get; }
    public DateTime? PlannedDate { get; }
    public DateTimeOffset? CompletedAt { get; }
}
