using ConvenientNote.Application.Abstractions;
using ConvenientNote.Domain;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;

namespace ConvenientNote.Application.Workspaces;

public sealed class WorkspaceApplicationService
{
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public event EventHandler? WorkspaceChanged;

    public WorkspaceApplicationService(IWorkspaceRepository workspaceRepository)
    {
        _workspaceRepository = workspaceRepository;
    }

    public async Task<IReadOnlyList<WorkspaceSnapshot>> ListWorkspacesAsync(
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        var workspaces = await _workspaceRepository.ListAsync(cancellationToken);

        return workspaces.Select(ToSnapshot).ToList();
    }

    public async Task<WorkspaceSnapshot> GetOrCreateDefaultWorkspaceAsync(
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        var workspaces = await _workspaceRepository.ListAsync(cancellationToken);
        var existingWorkspace = workspaces.FirstOrDefault();

        if (existingWorkspace is not null)
        {
            return ToSnapshot(existingWorkspace);
        }

        var workspace = Workspace.Create("默认工作区");
        await SaveAndNotifyAsync(workspace, cancellationToken);

        return ToSnapshot(workspace);
    }

    public async Task<WorkspaceSnapshot> CreateWorkspaceAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        var workspace = Workspace.Create(name);
        await SaveAndNotifyAsync(workspace, cancellationToken);

        return ToSnapshot(workspace);
    }

    public async Task<WorkspaceSnapshot> ReplaceActiveNotesAsync(
        WorkspaceId workspaceId,
        IReadOnlyCollection<Note> importedNotes,
        CancellationToken cancellationToken = default)
    {
        return await ReplaceActiveNotesAsync(
            workspaceId,
            importedNotes,
            cancellationToken,
            static () => { });
    }

    public async Task<WorkspaceSnapshot> ReplaceActiveNotesAsync(
        WorkspaceId workspaceId,
        IReadOnlyCollection<Note> importedNotes,
        CancellationToken cancellationToken,
        Action replacementCommitted)
    {
        await CommitActiveNotesReplacementAsync(
            workspaceId,
            importedNotes,
            cancellationToken,
            replacementCommitted);
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);

        return ToSnapshot(workspace);
    }

    public async Task CommitActiveNotesReplacementAsync(
        WorkspaceId workspaceId,
        IReadOnlyCollection<Note> importedNotes,
        CancellationToken cancellationToken,
        Action replacementCommitted)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        ArgumentNullException.ThrowIfNull(importedNotes);
        ArgumentNullException.ThrowIfNull(replacementCommitted);

        await _workspaceRepository.ReplaceActiveNotesAsync(workspaceId, importedNotes, cancellationToken);
        replacementCommitted();
        NotifyWorkspaceChanged();
    }

    public async Task<WorkspaceSnapshot> GetWorkspaceAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);

        return ToSnapshot(workspace);
    }

    public async Task<NoteSnapshot> CreateNoteAsync(
        WorkspaceId workspaceId,
        double x,
        double y,
        string? title = null,
        string boardKey = TodoBoardKeys.DayTodo,
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        var note = workspace.AddNote(
            boardKey,
            title ?? "新待办",
            string.Empty,
            new NotePosition(x, y),
            new NoteSize(260, 150),
            "#FFF8B8");

        await SaveAndNotifyAsync(workspace, cancellationToken);

        return ToSnapshot(note);
    }

    public async Task MoveNoteAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        double x,
        double y,
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.MoveNote(noteId, new NotePosition(x, y));

        await SaveAndNotifyAsync(workspace, cancellationToken);
    }

    public async Task<NoteSnapshot> CreateScheduledTodoAsync(
        WorkspaceId workspaceId, string title, DateTime date,
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        var offset = workspace.Notes.Count(note => note.BoardKey == TodoBoardKeys.DayTodo && note.PlannedDate == date.Date);
        var note = workspace.AddNote(TodoBoardKeys.DayTodo, title, string.Empty,
            new NotePosition(32 + offset % 4 * 290, 32 + offset / 4 * 180), new NoteSize(260, 150), "#FFF8B8");
        workspace.SetNotePlannedDate(note.Id, date);
        await SaveAndNotifyAsync(workspace, cancellationToken);
        return ToSnapshot(note);
    }

    public async Task SetNotePlannedDateAsync(
        WorkspaceId workspaceId, NoteId noteId, DateTime? date,
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.SetNotePlannedDate(noteId, date);
        await SaveAndNotifyAsync(workspace, cancellationToken);
    }

    private async Task<IDisposable> EnterOperationAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        return new OperationLease(_operationGate);
    }

    private sealed class OperationLease(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }

    private async Task SaveAndNotifyAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        await _workspaceRepository.SaveAsync(workspace, cancellationToken);
        NotifyWorkspaceChanged();
    }

    private void NotifyWorkspaceChanged()
    {
        // A refresh failure must not turn a successful database write into an apparent failure.
        foreach (var handler in WorkspaceChanged?.GetInvocationList() ?? [])
        {
            try { ((EventHandler)handler)(this, EventArgs.Empty); }
            catch (Exception error) { System.Diagnostics.Debug.WriteLine(error); }
        }
    }

    public async Task<NoteSnapshot> SaveKnowledgeMemoAsync(WorkspaceId workspaceId, string text,
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        var note = workspace.Notes.Where(n => n.BoardKey == TodoBoardKeys.Notes && !n.IsDeleted &&
                n.Tags.Contains(KnowledgeMemoMetadata.Tag, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(n => n.UpdatedAt).FirstOrDefault();
        if (note is null)
        {
            note = workspace.AddNote(TodoBoardKeys.Notes, "知识点清单", text,
                new NotePosition(0, 0), new NoteSize(340, 600), "#FFF8B8");
            note.SetTags([KnowledgeMemoMetadata.Tag]);
        }
        else workspace.UpdateRichNote(note.Id, string.Empty, text);
        await SaveAndNotifyAsync(workspace, cancellationToken);
        return ToSnapshot(note);
    }

    public async Task MoveNotesAsync(
        WorkspaceId workspaceId,
        IReadOnlyCollection<NotePositionUpdate> positionUpdates,
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        if (positionUpdates.Count == 0)
        {
            return;
        }

        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);

        foreach (var update in positionUpdates)
        {
            workspace.MoveNote(update.NoteId, new NotePosition(update.X, update.Y));
        }

        await SaveAndNotifyAsync(workspace, cancellationToken);
    }

    public async Task ResizeNoteAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        double width,
        double height,
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.ResizeNote(noteId, new NoteSize(width, height));

        await SaveAndNotifyAsync(workspace, cancellationToken);
    }

    public async Task UpdateNoteContentAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        string content,
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.UpdateNoteContent(noteId, content);

        await SaveAndNotifyAsync(workspace, cancellationToken);
    }

    public async Task UpdateNoteTitleAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        string title,
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.RenameNote(noteId, title);

        await SaveAndNotifyAsync(workspace, cancellationToken);
    }

    public async Task SetNoteCompletionAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        bool isCompleted,
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.SetNoteCompletion(noteId, isCompleted);

        await SaveAndNotifyAsync(workspace, cancellationToken);
    }

    public async Task SetNotePriorityAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        string priority,
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.SetNotePriority(noteId, priority);

        await SaveAndNotifyAsync(workspace, cancellationToken);
    }

    public async Task UpdateRichNoteAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        string richContent,
        string plainText,
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.UpdateRichNote(noteId, richContent, plainText);
        await SaveAndNotifyAsync(workspace, cancellationToken);
    }

    public async Task SetNoteNotebookAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        NotebookId? notebookId,
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.SetNoteNotebook(noteId, notebookId);
        await SaveAndNotifyAsync(workspace, cancellationToken);
    }

    public async Task SetNoteTagsAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        IEnumerable<string> tags,
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.SetNoteTags(noteId, tags);
        await SaveAndNotifyAsync(workspace, cancellationToken);
    }

    public async Task SetNotePinnedAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        bool isPinned,
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.SetNotePinned(noteId, isPinned);
        await SaveAndNotifyAsync(workspace, cancellationToken);
    }

    public async Task SetNoteFavoriteAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        bool isFavorite,
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.SetNoteFavorite(noteId, isFavorite);
        await SaveAndNotifyAsync(workspace, cancellationToken);
    }

    public async Task MoveNoteToTrashAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.MoveNoteToTrash(noteId);
        await SaveAndNotifyAsync(workspace, cancellationToken);
    }

    public async Task RestoreNoteAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.RestoreNote(noteId);
        await SaveAndNotifyAsync(workspace, cancellationToken);
    }

    public async Task DeleteNoteAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterOperationAsync(cancellationToken);
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.RemoveNote(noteId);

        await SaveAndNotifyAsync(workspace, cancellationToken);
    }

    private async Task<Workspace> GetRequiredWorkspaceAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken)
    {
        return await _workspaceRepository.GetAsync(workspaceId, cancellationToken)
            ?? throw new DomainException($"Workspace '{workspaceId}' was not found.");
    }

    private static WorkspaceSnapshot ToSnapshot(Workspace workspace)
    {
        var notes = workspace.Notes
            .OrderBy(note => note.ZIndex)
            .Select(ToSnapshot)
            .ToList();

        return new WorkspaceSnapshot(
            workspace.Id,
            workspace.Name,
            workspace.CreatedAt,
            workspace.UpdatedAt,
            notes);
    }

    private static NoteSnapshot ToSnapshot(Note note)
    {
        return new NoteSnapshot(
            note.Id,
            note.BoardKey,
            note.Priority,
            note.Title,
            note.Content,
            note.Position.X,
            note.Position.Y,
            note.Size.Width,
            note.Size.Height,
            note.Color,
            note.ZIndex,
            note.IsCompleted,
            note.RichContent,
            note.NotebookId,
            note.Tags,
            note.IsPinned,
            note.IsFavorite,
            note.IsDeleted,
            note.CreatedAt,
            note.UpdatedAt,
            note.PlannedDate,
            note.CompletedAt);
    }
}
