using ConvenientNote.Notes.Domain;
using ConvenientNote.Notes.Domain.Notes;
using ConvenientNote.Notes.Domain.Workspaces;
using ConvenientNote.Platform.Contracts;

namespace ConvenientNote.Notes.Application;

public sealed class NotesApplicationService(INotesRepository repository, IWorkspaceContext workspaceContext) : ConvenientNote.Notes.Contracts.INotesApi
{

    public async Task<IReadOnlyList<ConvenientNote.Notes.Contracts.NoteSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var workspace = await GetOrCreateDefaultWorkspaceAsync(cancellationToken);
        return workspace.Notes.Where(n => !n.IsDeleted && !KnowledgeMemoMetadata.IsMemo(n)).Select(n => new ConvenientNote.Notes.Contracts.NoteSummary(n.Id.Value, n.Title, n.Content.Length > 160 ? n.Content[..160] : n.Content, n.UpdatedAt)).ToList();
    }

    private readonly SemaphoreSlim _gate = new(1, 1);

    public event EventHandler? WorkspaceChanged;

    private void Notify()
    {
        foreach (var handler in WorkspaceChanged?.GetInvocationList() ?? [])
        {
            try
            {
                ((EventHandler)handler)(this, EventArgs.Empty);
            }
            catch (Exception error)
            {
                System.Diagnostics.Debug.WriteLine(error);
            }
        }
    }

    public async Task<WorkspaceSnapshot> GetOrCreateDefaultWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        var info = await workspaceContext.GetCurrentAsync(cancellationToken);
        return await SnapshotAsync(new WorkspaceId(info.Id), info.Name, cancellationToken);
    }

    public async Task<WorkspaceSnapshot> GetWorkspaceAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default)
    {
        var info = await workspaceContext.GetCurrentAsync(cancellationToken);
        return await SnapshotAsync(workspaceId, info.Name, cancellationToken);
    }

    private async Task<WorkspaceSnapshot> SnapshotAsync(WorkspaceId id, string name, CancellationToken ct)
    {
        var notes = await repository.ListAsync(id.Value, ct);
        return new(id, name, notes.Count == 0 ? DateTimeOffset.UtcNow : notes.Min(n => n.CreatedAt), notes.Count == 0 ? DateTimeOffset.UtcNow : notes.Max(n => n.UpdatedAt), notes.OrderBy(n => n.ZIndex).Select(ToSnapshot).ToList());
    }

    public async Task<NoteSnapshot> CreateNoteAsync(WorkspaceId workspaceId, double x, double y, string? title = null, string boardKey = TodoBoardKeys.Notes, CancellationToken cancellationToken = default)
    {
        if (boardKey != TodoBoardKeys.Notes)
            throw new ArgumentException("Notes module accepts only notes.", nameof(boardKey));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var notes = await repository.ListAsync(workspaceId.Value, cancellationToken);
            var note = Note.Create(title ?? "新笔记", "", new(x, y), new(260, 150), "#FFF8B8", notes.Count == 0 ? 1 : notes.Max(n => n.ZIndex) + 1);
            await repository.SaveAsync(workspaceId.Value, note, cancellationToken);
            Notify();
            return ToSnapshot(note);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ChangeAsync(WorkspaceId workspaceId, NoteId noteId, Action<Note> change, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var note = (await repository.ListAsync(workspaceId.Value, ct)).FirstOrDefault(n => n.Id == noteId) ?? throw new DomainException($"Note '{noteId}' was not found.");
            change(note);
            await repository.SaveAsync(workspaceId.Value, note, ct);
            Notify();
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task MoveNoteAsync(WorkspaceId workspaceId, NoteId noteId, double x, double y, CancellationToken cancellationToken = default) => ChangeAsync(workspaceId, noteId, n => n.MoveTo(new(x, y)), cancellationToken);

    public Task ResizeNoteAsync(WorkspaceId workspaceId, NoteId noteId, double width, double height, CancellationToken cancellationToken = default) => ChangeAsync(workspaceId, noteId, n => n.ResizeTo(new(width, height)), cancellationToken);

    public Task UpdateNoteContentAsync(WorkspaceId workspaceId, NoteId noteId, string content, CancellationToken cancellationToken = default) => ChangeAsync(workspaceId, noteId, n => n.UpdateContent(content), cancellationToken);

    public Task UpdateNoteTitleAsync(WorkspaceId workspaceId, NoteId noteId, string title, CancellationToken cancellationToken = default) => ChangeAsync(workspaceId, noteId, n => n.Rename(title), cancellationToken);

    public Task UpdateRichNoteAsync(WorkspaceId workspaceId, NoteId noteId, string richContent, string plainText, CancellationToken cancellationToken = default) => ChangeAsync(workspaceId, noteId, n => n.UpdateRichContent(richContent, plainText), cancellationToken);

    public Task SetNoteNotebookAsync(WorkspaceId workspaceId, NoteId noteId, NotebookId? notebookId, CancellationToken cancellationToken = default) => ChangeAsync(workspaceId, noteId, n => n.SetNotebook(notebookId), cancellationToken);

    public Task SetNoteTagsAsync(WorkspaceId workspaceId, NoteId noteId, IEnumerable<string> tags, CancellationToken cancellationToken = default) => ChangeAsync(workspaceId, noteId, n => n.SetTags(tags), cancellationToken);

    public Task SetNotePinnedAsync(WorkspaceId workspaceId, NoteId noteId, bool isPinned, CancellationToken cancellationToken = default) => ChangeAsync(workspaceId, noteId, n => n.SetPinned(isPinned), cancellationToken);

    public Task SetNoteFavoriteAsync(WorkspaceId workspaceId, NoteId noteId, bool isFavorite, CancellationToken cancellationToken = default) => ChangeAsync(workspaceId, noteId, n => n.SetFavorite(isFavorite), cancellationToken);

    public Task MoveNoteToTrashAsync(WorkspaceId workspaceId, NoteId noteId, CancellationToken cancellationToken = default) => ChangeAsync(workspaceId, noteId, n => n.MoveToTrash(), cancellationToken);

    public Task RestoreNoteAsync(WorkspaceId workspaceId, NoteId noteId, CancellationToken cancellationToken = default) => ChangeAsync(workspaceId, noteId, n => n.Restore(), cancellationToken);

    public async Task DeleteNoteAsync(WorkspaceId workspaceId, NoteId noteId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await repository.DeleteAsync(workspaceId.Value, noteId, cancellationToken);
            Notify();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MoveNotesAsync(WorkspaceId workspaceId, IReadOnlyCollection<NotePositionUpdate> positionUpdates, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var notes = await repository.ListAsync(workspaceId.Value, cancellationToken);
            var changed = new List<Note>();
            foreach (var update in positionUpdates)
            {
                var note = notes.First(n => n.Id == update.NoteId);
                note.MoveTo(new(update.X, update.Y));
                changed.Add(note);
            }
            await repository.SaveManyAsync(workspaceId.Value, changed, cancellationToken);
            Notify();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<NoteSnapshot> SaveKnowledgeMemoAsync(WorkspaceId workspaceId, string text, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var notes = await repository.ListAsync(workspaceId.Value, cancellationToken);
            var note = notes.Where(n => !n.IsDeleted && n.Tags.Contains(KnowledgeMemoMetadata.Tag, StringComparer.OrdinalIgnoreCase)).OrderByDescending(n => n.UpdatedAt).FirstOrDefault();
            if (note is null)
            {
                note = Note.Create("知识点清单", text, new(0, 0), new(340, 600), "#FFF8B8", notes.Count == 0 ? 1 : notes.Max(n => n.ZIndex) + 1);
                note.SetTags([KnowledgeMemoMetadata.Tag]);
            }
            else
                note.UpdateRichContent("", text);
            await repository.SaveAsync(workspaceId.Value, note, cancellationToken);
            Notify();
            return ToSnapshot(note);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CommitActiveNotesReplacementAsync(WorkspaceId workspaceId, IReadOnlyCollection<Note> importedNotes, CancellationToken cancellationToken, Action replacementCommitted)
    {
        ArgumentNullException.ThrowIfNull(replacementCommitted);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await repository.ReplaceActiveAsync(workspaceId.Value, importedNotes, cancellationToken);
            replacementCommitted();
            Notify();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WorkspaceSnapshot> ReplaceActiveNotesAsync(WorkspaceId workspaceId, IReadOnlyCollection<Note> importedNotes, CancellationToken cancellationToken = default)
    {
        await CommitActiveNotesReplacementAsync(workspaceId, importedNotes, cancellationToken, () => { });
        return await GetWorkspaceAsync(workspaceId, cancellationToken);
    }

    public static NoteSnapshot ToSnapshot(Note n) => new(n.Id, TodoBoardKeys.Notes, n.LegacyMetadata.Priority, n.Title, n.Content, n.Position.X, n.Position.Y, n.Size.Width, n.Size.Height, n.Color, n.ZIndex, n.LegacyMetadata.IsCompleted, n.RichContent, n.NotebookId, n.Tags, n.IsPinned, n.IsFavorite, n.IsDeleted, n.CreatedAt, n.UpdatedAt, n.LegacyMetadata.PlannedDate, n.LegacyMetadata.CompletedAt);
}
