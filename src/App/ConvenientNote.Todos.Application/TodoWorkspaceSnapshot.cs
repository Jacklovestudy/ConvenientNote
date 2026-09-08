using ConvenientNote.Todos.Domain;

namespace ConvenientNote.Todos.Application;
public sealed record TodoWorkspaceSnapshot(Guid Id, IReadOnlyList<TodoItem> Todos);
