using ConvenientNote.Todos.Domain;

namespace ConvenientNote.Todos.Application;
public sealed record TodoPositionUpdate(TodoId TodoId, double X, double Y);
