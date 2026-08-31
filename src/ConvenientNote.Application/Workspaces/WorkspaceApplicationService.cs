using ConvenientNote.Application.Abstractions;
using ConvenientNote.Domain;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;

namespace ConvenientNote.Application.Workspaces;

public sealed class WorkspaceApplicationService
{
    private readonly IWorkspaceRepository _workspaceRepository;

    public WorkspaceApplicationService(IWorkspaceRepository workspaceRepository)
    {
        _workspaceRepository = workspaceRepository;
    }

    public async Task<IReadOnlyList<WorkspaceSnapshot>> ListWorkspacesAsync(
        CancellationToken cancellationToken = default)
    {
        var workspaces = await _workspaceRepository.ListAsync(cancellationToken);

        return workspaces.Select(ToSnapshot).ToList();
    }

    public async Task<WorkspaceSnapshot> GetOrCreateDefaultWorkspaceAsync(
        CancellationToken cancellationToken = default)
    {
        var workspaces = await _workspaceRepository.ListAsync(cancellationToken);
        var existingWorkspace = workspaces.FirstOrDefault();

        if (existingWorkspace is not null)
        {
            return ToSnapshot(existingWorkspace);
        }

        var workspace = Workspace.Create("默认工作区");
        await _workspaceRepository.SaveAsync(workspace, cancellationToken);

        return ToSnapshot(workspace);
    }

    public async Task<WorkspaceSnapshot> CreateWorkspaceAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var workspace = Workspace.Create(name);
        await _workspaceRepository.SaveAsync(workspace, cancellationToken);

        return ToSnapshot(workspace);
    }

    public async Task<WorkspaceSnapshot> GetWorkspaceAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
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
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        var note = workspace.AddNote(
            boardKey,
            title ?? "新待办",
            string.Empty,
            new NotePosition(x, y),
            new NoteSize(260, 150),
            "#FFF8B8");

        await _workspaceRepository.SaveAsync(workspace, cancellationToken);

        return ToSnapshot(note);
    }

    public async Task MoveNoteAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        double x,
        double y,
        CancellationToken cancellationToken = default)
    {
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.MoveNote(noteId, new NotePosition(x, y));

        await _workspaceRepository.SaveAsync(workspace, cancellationToken);
    }

    public async Task MoveNotesAsync(
        WorkspaceId workspaceId,
        IReadOnlyCollection<NotePositionUpdate> positionUpdates,
        CancellationToken cancellationToken = default)
    {
        if (positionUpdates.Count == 0)
        {
            return;
        }

        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);

        foreach (var update in positionUpdates)
        {
            workspace.MoveNote(update.NoteId, new NotePosition(update.X, update.Y));
        }

        await _workspaceRepository.SaveAsync(workspace, cancellationToken);
    }

    public async Task ResizeNoteAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        double width,
        double height,
        CancellationToken cancellationToken = default)
    {
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.ResizeNote(noteId, new NoteSize(width, height));

        await _workspaceRepository.SaveAsync(workspace, cancellationToken);
    }

    public async Task UpdateNoteContentAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        string content,
        CancellationToken cancellationToken = default)
    {
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.UpdateNoteContent(noteId, content);

        await _workspaceRepository.SaveAsync(workspace, cancellationToken);
    }

    public async Task UpdateNoteTitleAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        string title,
        CancellationToken cancellationToken = default)
    {
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.RenameNote(noteId, title);

        await _workspaceRepository.SaveAsync(workspace, cancellationToken);
    }

    public async Task SetNoteCompletionAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        bool isCompleted,
        CancellationToken cancellationToken = default)
    {
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.SetNoteCompletion(noteId, isCompleted);

        await _workspaceRepository.SaveAsync(workspace, cancellationToken);
    }

    public async Task SetNotePriorityAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        string priority,
        CancellationToken cancellationToken = default)
    {
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.SetNotePriority(noteId, priority);

        await _workspaceRepository.SaveAsync(workspace, cancellationToken);
    }

    public async Task UpdateRichNoteAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        string richContent,
        string plainText,
        CancellationToken cancellationToken = default)
    {
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.UpdateRichNote(noteId, richContent, plainText);
        await _workspaceRepository.SaveAsync(workspace, cancellationToken);
    }

    public async Task SetNoteNotebookAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        NotebookId? notebookId,
        CancellationToken cancellationToken = default)
    {
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.SetNoteNotebook(noteId, notebookId);
        await _workspaceRepository.SaveAsync(workspace, cancellationToken);
    }

    public async Task SetNoteTagsAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        IEnumerable<string> tags,
        CancellationToken cancellationToken = default)
    {
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.SetNoteTags(noteId, tags);
        await _workspaceRepository.SaveAsync(workspace, cancellationToken);
    }

    public async Task SetNotePinnedAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        bool isPinned,
        CancellationToken cancellationToken = default)
    {
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.SetNotePinned(noteId, isPinned);
        await _workspaceRepository.SaveAsync(workspace, cancellationToken);
    }

    public async Task SetNoteFavoriteAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        bool isFavorite,
        CancellationToken cancellationToken = default)
    {
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.SetNoteFavorite(noteId, isFavorite);
        await _workspaceRepository.SaveAsync(workspace, cancellationToken);
    }

    public async Task MoveNoteToTrashAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        CancellationToken cancellationToken = default)
    {
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.MoveNoteToTrash(noteId);
        await _workspaceRepository.SaveAsync(workspace, cancellationToken);
    }

    public async Task RestoreNoteAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        CancellationToken cancellationToken = default)
    {
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.RestoreNote(noteId);
        await _workspaceRepository.SaveAsync(workspace, cancellationToken);
    }

    public async Task DeleteNoteAsync(
        WorkspaceId workspaceId,
        NoteId noteId,
        CancellationToken cancellationToken = default)
    {
        var workspace = await GetRequiredWorkspaceAsync(workspaceId, cancellationToken);
        workspace.RemoveNote(noteId);

        await _workspaceRepository.SaveAsync(workspace, cancellationToken);
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

        return new WorkspaceSnapshot(workspace.Id, workspace.Name, notes);
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
            note.UpdatedAt);
    }
}
