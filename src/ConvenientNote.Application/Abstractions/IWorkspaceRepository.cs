using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;

namespace ConvenientNote.Application.Abstractions;

public interface IWorkspaceRepository
{
    Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken = default);

    Task<Workspace?> GetAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default);

    Task SaveAsync(Workspace workspace, CancellationToken cancellationToken = default);

    Task ReplaceActiveNotesAsync(
        WorkspaceId workspaceId,
        IReadOnlyCollection<Note> importedNotes,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default);
}
