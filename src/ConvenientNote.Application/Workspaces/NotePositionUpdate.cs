using ConvenientNote.Domain.Notes;

namespace ConvenientNote.Application.Workspaces;

public sealed record NotePositionUpdate(
    NoteId NoteId,
    double X,
    double Y);
