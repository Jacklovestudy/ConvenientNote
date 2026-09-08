using ConvenientNote.Todos.Domain;

namespace ConvenientNote.Todos.Application;
public interface ITodoRepository
{
    Task<IReadOnlyList<TodoItem>> ListAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task SaveAsync(Guid workspaceId, IReadOnlyList<TodoItem> items, CancellationToken cancellationToken = default);
}
