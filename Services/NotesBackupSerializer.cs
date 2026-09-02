using System.IO;
using System.Text.Json;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain;
using ConvenientNote.Domain.Notes;

namespace ConvenientNote.Services;

public static class NotesBackupSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true
    };

    public static NotesBackupDocument CreateDocument(IEnumerable<NoteSnapshot> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);

        return new NotesBackupDocument(
            notes.Where(static note =>
                note.BoardKey == TodoBoardKeys.Notes && !note.IsDeleted)
                .Select(ToBackupNote)
                .ToList());
    }

    public static Task WriteDocumentAsync(
        Stream stream,
        NotesBackupDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(document);
        ValidateDocument(document);

        return JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
    }

    public static async Task<NotesBackupDocument> ReadDocumentAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            var document = await JsonSerializer.DeserializeAsync<NotesBackupDocument>(
                stream,
                JsonOptions,
                cancellationToken);
            ValidateDocument(document);
            _ = ToNotes(document!);
            return document!;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Notes backup JSON is invalid.", exception);
        }
    }

    public static IReadOnlyList<Note> ToNotes(NotesBackupDocument document)
    {
        ValidateDocument(document);

        try
        {
            return document.Notes.Select(ToNote).ToList();
        }
        catch (DomainException exception)
        {
            throw new InvalidDataException("Notes backup contains invalid domain values.", exception);
        }
    }

    private static NotesBackupNote ToBackupNote(NoteSnapshot note)
    {
        return new NotesBackupNote(
            note.Id.Value,
            note.BoardKey,
            note.Priority,
            note.Title,
            note.Content,
            note.X,
            note.Y,
            note.Width,
            note.Height,
            note.Color,
            note.ZIndex,
            note.IsCompleted,
            note.RichContent,
            note.NotebookId?.Value,
            note.Tags.ToList(),
            note.IsPinned,
            note.IsFavorite,
            note.IsDeleted,
            note.CreatedAt,
            note.UpdatedAt);
    }

    private static Note ToNote(NotesBackupNote note)
    {
        if (note.BoardKey != TodoBoardKeys.Notes || note.IsDeleted)
        {
            throw new InvalidDataException("Notes backup can contain only active Notes records.");
        }

        return new Note(
            new NoteId(note.Id),
            note.BoardKey,
            note.Priority,
            note.Title,
            note.Content,
            new NotePosition(note.X, note.Y),
            new NoteSize(note.Width, note.Height),
            note.Color,
            note.ZIndex,
            note.IsCompleted,
            note.CreatedAt,
            note.UpdatedAt,
            note.RichContent,
            note.NotebookId is { } notebookId ? new NotebookId(notebookId) : null,
            note.Tags,
            note.IsPinned,
            note.IsFavorite,
            note.IsDeleted);
    }

    private static void ValidateDocument(NotesBackupDocument? document)
    {
        if (document is null)
        {
            throw new InvalidDataException("Notes backup JSON cannot be null.");
        }

        if (document.Notes is null)
        {
            throw new InvalidDataException("Notes backup must contain notes.");
        }

        var noteIds = new HashSet<Guid>();
        foreach (var note in document.Notes)
        {
            if (note is null || note.Id == Guid.Empty)
            {
                throw new InvalidDataException("Notes backup must contain note IDs.");
            }

            if (!noteIds.Add(note.Id))
            {
                throw new InvalidDataException("Notes backup cannot contain duplicate note IDs.");
            }

            if (note.BoardKey is null || note.Priority is null || note.Title is null ||
                note.Content is null || note.Color is null || note.RichContent is null ||
                note.Tags is null || note.Tags.Any(static tag => tag is null))
            {
                throw new InvalidDataException("Notes backup contains missing note values.");
            }

            if (note.NotebookId == Guid.Empty)
            {
                throw new InvalidDataException("Notes backup contains an invalid notebook ID.");
            }

            if (note.BoardKey != TodoBoardKeys.Notes || note.IsDeleted)
            {
                throw new InvalidDataException("Notes backup can contain only active Notes records.");
            }
        }
    }
}
