using ConvenientNote.Notes.Domain.Notes;

namespace ConvenientNote.Notes.Application;

public sealed record NoteSnapshot(
    NoteId Id,
    string BoardKey,
    string Priority,
    string Title,
    string Content,
    double X,
    double Y,
    double Width,
    double Height,
    string Color,
    int ZIndex,
    bool IsCompleted,
    string RichContent,
    NotebookId? NotebookId,
    IReadOnlyList<string> Tags,
    bool IsPinned,
    bool IsFavorite,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTime? PlannedDate = null,
    DateTimeOffset? CompletedAt = null);
