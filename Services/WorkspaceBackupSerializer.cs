using System.IO;
using System.Text.Json;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;

namespace ConvenientNote.Services;

public static class WorkspaceBackupSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static WorkspaceBackupDocument CreateDocument(WorkspaceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new WorkspaceBackupDocument(
            snapshot.Id.Value,
            snapshot.Name,
            snapshot.CreatedAt,
            snapshot.UpdatedAt,
            snapshot.Notes.Select(ToBackupNote).ToList());
    }

    public static Task WriteDocumentAsync(
        Stream stream,
        WorkspaceBackupDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(document);

        return JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
    }

    public static async Task<WorkspaceBackupDocument> ReadDocumentAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            var document = await JsonSerializer.DeserializeAsync<WorkspaceBackupDocument>(
                stream,
                JsonOptions,
                cancellationToken);
            ValidateDocument(document);
            _ = ToWorkspace(document!);
            return document!;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Workspace backup JSON is invalid.", exception);
        }
    }

    public static Workspace ToWorkspace(WorkspaceBackupDocument document)
    {
        ValidateDocument(document);

        try
        {
            var notes = document.Notes.Select(ToNote).ToList();
            return new Workspace(
                new WorkspaceId(document.WorkspaceId),
                document.WorkspaceName,
                document.CreatedAt,
                document.UpdatedAt,
                notes);
        }
        catch (DomainException exception)
        {
            throw new InvalidDataException("Workspace backup contains invalid domain values.", exception);
        }
    }

    private static WorkspaceBackupNote ToBackupNote(NoteSnapshot note)
    {
        return new WorkspaceBackupNote(
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

    private static Note ToNote(WorkspaceBackupNote note)
    {
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

    private static void ValidateDocument(WorkspaceBackupDocument? document)
    {
        if (document is null)
        {
            throw new InvalidDataException("Workspace backup JSON cannot be null.");
        }

        if (document.WorkspaceId == Guid.Empty)
        {
            throw new InvalidDataException("Workspace backup must contain a workspace ID.");
        }

        if (document.WorkspaceName is null || document.Notes is null)
        {
            throw new InvalidDataException("Workspace backup contains missing required values.");
        }

        var noteIds = new HashSet<Guid>();
        foreach (var note in document.Notes)
        {
            if (note is null || note.Id == Guid.Empty)
            {
                throw new InvalidDataException("Workspace backup must contain note IDs.");
            }

            if (!noteIds.Add(note.Id))
            {
                throw new InvalidDataException("Workspace backup cannot contain duplicate note IDs.");
            }

            if (note.BoardKey is null || note.Priority is null || note.Title is null ||
                note.Content is null || note.Color is null || note.RichContent is null ||
                note.Tags is null || note.Tags.Any(static tag => tag is null))
            {
                throw new InvalidDataException("Workspace backup contains missing note values.");
            }
        }
    }
}
