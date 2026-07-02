using ConvenientNote.Domain.Notes;

namespace ConvenientNote.Application.Workspaces;

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
    bool IsCompleted);
