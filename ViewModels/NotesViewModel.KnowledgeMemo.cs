using ConvenientNote.Application.Workspaces;
using ConvenientNote.Services;

namespace ConvenientNote.ViewModels;

public sealed partial class NotesViewModel
{
    private string _knowledgeMemoText = "";
    public string KnowledgeMemoText
    {
        get => _knowledgeMemoText;
        private set => SetProperty(ref _knowledgeMemoText, value);
    }
    public bool KnowledgeMemoPersisted { get; private set; }

    private void LoadKnowledgeMemo(WorkspaceSnapshot workspace)
    {
        var memo = workspace.Notes.Where(n => !n.IsDeleted && KnowledgeMemoMetadata.IsMemo(n))
            .OrderByDescending(n => n.UpdatedAt).FirstOrDefault();
        KnowledgeMemoPersisted = memo is not null;
        KnowledgeMemoText = memo?.Content ?? KnowledgeChecklist.DefaultText;
    }

    public async Task<bool> SaveKnowledgeMemoAsync(string text)
    {
        if (_workspaceId is not { } workspaceId || !TryBeginWorkspaceMutation(out var operation)) return false;
        using (operation)
        {
            try
            {
                await SerializeMutationAsync(() => _workspaceService.SaveKnowledgeMemoAsync(workspaceId, text));
                KnowledgeMemoText = text;
                KnowledgeMemoPersisted = true;
                return true;
            }
            catch { return false; }
        }
    }

    // Sidebar saves and card/body edits share one writer: the repository saves workspace snapshots.
    private async Task SerializeMutationAsync(Func<Task> action)
    {
        await _saveGate.WaitAsync();
        try { await action(); }
        finally { _saveGate.Release(); }
    }

    private async Task<T> SerializeMutationAsync<T>(Func<Task<T>> action)
    {
        await _saveGate.WaitAsync();
        try { return await action(); }
        finally { _saveGate.Release(); }
    }
}
