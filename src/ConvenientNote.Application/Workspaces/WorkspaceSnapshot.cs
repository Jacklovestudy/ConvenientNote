using ConvenientNote.Domain.Workspaces;

namespace ConvenientNote.Application.Workspaces;

public sealed record WorkspaceSnapshot(
    WorkspaceId Id,
    string Name,
    IReadOnlyList<NoteSnapshot> Notes);
