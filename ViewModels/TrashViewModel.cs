using System.Collections.ObjectModel;
using System.Windows;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Workspaces;
using ConvenientNote.Services;
using ConvenientNote.Views;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation.Regions;

namespace ConvenientNote.ViewModels;

public sealed class TrashViewModel : BindableBase, INavigationAware
{
    private readonly WorkspaceApplicationService _workspaceService;
    private readonly NoteMediaService _mediaService;
    private WorkspaceId? _workspaceId;
    private WorkspaceReplacementOperationGate? _workspaceReplacementOperationGate;

    public TrashViewModel(WorkspaceApplicationService workspaceService, NoteMediaService mediaService)
    {
        _workspaceService = workspaceService;
        _mediaService = mediaService;
        RestoreCommand = new DelegateCommand<NoteCardViewModel>(note => _ = RestoreAsync(note));
        DeleteForeverCommand = new DelegateCommand<NoteCardViewModel>(note => _ = DeleteForeverAsync(note));
    }

    public string ViewTitle => "回收站";
    public string ViewDescription => "删除的笔记会保留在这里，恢复或彻底删除由你决定。";
    public ObservableCollection<NoteCardViewModel> DeletedNotes { get; } = new();
    public Visibility EmptyVisibility => DeletedNotes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public DelegateCommand<NoteCardViewModel> RestoreCommand { get; }
    public DelegateCommand<NoteCardViewModel> DeleteForeverCommand { get; }

    internal void SetWorkspaceReplacementOperationGate(WorkspaceReplacementOperationGate operationGate) =>
        _workspaceReplacementOperationGate = operationGate;

    internal bool HasWorkspaceReplacementOperationGate => _workspaceReplacementOperationGate is not null;

    public async Task InitializeAsync()
    {
        var workspace = await _workspaceService.GetOrCreateDefaultWorkspaceAsync();
        _workspaceId = workspace.Id;
        DeletedNotes.Clear();
        foreach (var note in workspace.Notes.Where(static note => note.BoardKey == TodoBoardKeys.Notes && note.IsDeleted).OrderByDescending(static note => note.UpdatedAt))
        {
            DeletedNotes.Add(new NoteCardViewModel(note));
        }
        RaisePropertyChanged(nameof(EmptyVisibility));
    }

    public async Task RestoreAsync(NoteCardViewModel? note)
    {
        if (!TryBeginWorkspaceMutation(out var operation))
        {
            return;
        }

        using (operation)
        {
        if (note is null || _workspaceId is not { } workspaceId) return;
        await _workspaceService.RestoreNoteAsync(workspaceId, note.Id);
        DeletedNotes.Remove(note);
        RaisePropertyChanged(nameof(EmptyVisibility));
        }
    }

    public async Task DeleteForeverAsync(NoteCardViewModel? note)
    {
        if (!TryBeginWorkspaceMutation(out var operation))
        {
            return;
        }

        using (operation)
        {
        if (note is null || _workspaceId is not { } workspaceId) return;
        await _workspaceService.DeleteNoteAsync(workspaceId, note.Id);
        await _mediaService.DeleteAllAsync(note.Id);
        DeletedNotes.Remove(note);
        RaisePropertyChanged(nameof(EmptyVisibility));
        }
    }

    private bool TryBeginWorkspaceMutation(out IDisposable? operation)
    {
        operation = _workspaceReplacementOperationGate?.TryBegin();
        return _workspaceReplacementOperationGate is null || operation is not null;
    }

    public void OnNavigatedTo(NavigationContext navigationContext) => _ = InitializeAsync();
    public bool IsNavigationTarget(NavigationContext navigationContext) => true;
    public void OnNavigatedFrom(NavigationContext navigationContext) { }
}
