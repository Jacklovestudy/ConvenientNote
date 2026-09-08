using ConvenientNote.Notes.Domain.Notes;

namespace ConvenientNote.Notes.Application;

public sealed record NotePositionUpdate(
    NoteId NoteId,
    double X,
    double Y);
