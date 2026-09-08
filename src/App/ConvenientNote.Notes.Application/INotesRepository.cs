using ConvenientNote.Notes.Domain.Notes;
namespace ConvenientNote.Notes.Application;

public interface INotesRepository
{
    Task<IReadOnlyList<Note>> ListAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task SaveAsync(Guid workspaceId, Note note, CancellationToken cancellationToken = default);
    Task SaveManyAsync(Guid workspaceId, IReadOnlyCollection<Note> notes, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid workspaceId, NoteId noteId, CancellationToken cancellationToken = default);
    Task ReplaceActiveAsync(Guid workspaceId, IReadOnlyCollection<Note> notes, CancellationToken cancellationToken = default);
    Task ImportLegacyAsync(Guid workspaceId, IReadOnlyCollection<Note> notes, CancellationToken cancellationToken = default);
}
