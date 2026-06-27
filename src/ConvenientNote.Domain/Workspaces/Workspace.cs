using ConvenientNote.Domain;
using ConvenientNote.Domain.Notes;

namespace ConvenientNote.Domain.Workspaces;

public sealed class Workspace
{
    private readonly List<Note> _notes;

    public Workspace(
        WorkspaceId id,
        string name,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        IEnumerable<Note>? notes = null)
    {
        Id = id;
        Name = NormalizeName(name);
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        _notes = notes?.ToList() ?? new List<Note>();

        if (_notes.Select(note => note.Id).Distinct().Count() != _notes.Count)
        {
            throw new DomainException("Workspace cannot contain duplicate notes.");
        }
    }

    public WorkspaceId Id { get; }

    public string Name { get; private set; }

    public IReadOnlyCollection<Note> Notes => _notes.AsReadOnly();

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Workspace Create(string name)
    {
        var now = DateTimeOffset.UtcNow;

        return new Workspace(
            WorkspaceId.New(),
            name,
            now,
            now);
    }

    public Note AddNote(
        string title,
        string content,
        NotePosition position,
        NoteSize size,
        string color)
    {
        var nextZIndex = _notes.Count == 0 ? 1 : _notes.Max(note => note.ZIndex) + 1;
        var note = Note.Create(title, content, position, size, color, nextZIndex);

        _notes.Add(note);
        Touch();

        return note;
    }

    public void Rename(string name)
    {
        Name = NormalizeName(name);
        Touch();
    }

    public void MoveNote(NoteId noteId, NotePosition position)
    {
        GetRequiredNote(noteId).MoveTo(position);
        Touch();
    }

    public void ResizeNote(NoteId noteId, NoteSize size)
    {
        GetRequiredNote(noteId).ResizeTo(size);
        Touch();
    }

    public void UpdateNoteContent(NoteId noteId, string content)
    {
        GetRequiredNote(noteId).UpdateContent(content);
        Touch();
    }

    public void RenameNote(NoteId noteId, string title)
    {
        GetRequiredNote(noteId).Rename(title);
        Touch();
    }

    public void SetNoteCompletion(NoteId noteId, bool isCompleted)
    {
        GetRequiredNote(noteId).SetCompletion(isCompleted);
        Touch();
    }

    public void RemoveNote(NoteId noteId)
    {
        var note = GetRequiredNote(noteId);

        _notes.Remove(note);
        Touch();
    }

    public void BringNoteToFront(NoteId noteId)
    {
        var note = GetRequiredNote(noteId);
        var nextZIndex = _notes.Max(current => current.ZIndex) + 1;

        note.SetZIndex(nextZIndex);
        Touch();
    }

    private Note GetRequiredNote(NoteId noteId)
    {
        return _notes.FirstOrDefault(note => note.Id == noteId)
            ?? throw new DomainException($"Note '{noteId}' was not found.");
    }

    private static string NormalizeName(string name)
    {
        var normalized = string.IsNullOrWhiteSpace(name) ? "默认工作区" : name.Trim();

        if (normalized.Length > 80)
        {
            throw new DomainException("Workspace name cannot exceed 80 characters.");
        }

        return normalized;
    }

    private void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
