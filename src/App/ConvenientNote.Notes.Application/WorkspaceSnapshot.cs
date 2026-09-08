using ConvenientNote.Notes.Domain.Workspaces;

namespace ConvenientNote.Notes.Application;

public sealed record WorkspaceSnapshot(
    WorkspaceId Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<NoteSnapshot> Notes);
