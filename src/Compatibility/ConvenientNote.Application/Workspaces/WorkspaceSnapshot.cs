using ConvenientNote.Domain.Workspaces;

namespace ConvenientNote.Application.Workspaces;

public sealed record WorkspaceSnapshot(
    WorkspaceId Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<NoteSnapshot> Notes);
