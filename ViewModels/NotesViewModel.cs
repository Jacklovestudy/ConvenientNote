using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Documents;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;
using ConvenientNote.Services;
using ConvenientNote.Views;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation.Regions;

namespace ConvenientNote.ViewModels;

public sealed class NotesViewModel : BindableBase, INavigationAware
{
    private readonly WorkspaceApplicationService _workspaceService;
    private readonly RichTextDocumentService _documentService;
    private readonly NoteMediaService _mediaService;
    private readonly List<NoteCardViewModel> _allNotes = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private NotesReplacementOperationGate? _notesReplacementOperationGate;
    private WorkspaceId? _workspaceId;
    private string _searchText = string.Empty;
    private NoteCardViewModel? _selectedNote;
    private bool _isEditorOpen;
    private bool _isInitialized;
    private string _saveStatus = "已保存";
    private string _selectedTag = "全部标签";
    private NotebookId? _selectedNotebookFilter = NotebookOptions.AllId;

    public NotesViewModel(
        WorkspaceApplicationService workspaceService,
        RichTextDocumentService documentService,
        NoteMediaService mediaService)
    {
        _workspaceService = workspaceService;
        _documentService = documentService;
        _mediaService = mediaService;
        OpenNoteCommand = new DelegateCommand<NoteCardViewModel>(OpenNote);
        CloseEditorCommand = new DelegateCommand(() => IsEditorOpen = false);
        NewNoteCommand = new DelegateCommand(() => _ = AddNoteAsync());
        SaveNowCommand = new DelegateCommand(() => SaveRequested?.Invoke(this, EventArgs.Empty), () => SelectedNote is not null);
        TogglePinnedCommand = new DelegateCommand(() => _ = TogglePinnedAsync(), () => SelectedNote is not null);
        ToggleFavoriteCommand = new DelegateCommand(() => _ = ToggleFavoriteAsync(), () => SelectedNote is not null);
        MoveToTrashCommand = new DelegateCommand(() => _ = MoveToTrashAsync(), () => SelectedNote is not null);
    }

    public event EventHandler? SelectedNoteChanged;
    public event EventHandler? SaveRequested;

    public ObservableCollection<NoteCardViewModel> FilteredNotes { get; } = new();
    public ObservableCollection<string> AvailableTags { get; } = new() { "全部标签" };
    public IReadOnlyList<NotebookOption> AvailableNotebooks => NotebookOptions.All;
    public IReadOnlyList<NotebookOption> EditableNotebooks => NotebookOptions.Editable;

    public DelegateCommand<NoteCardViewModel> OpenNoteCommand { get; }
    public DelegateCommand CloseEditorCommand { get; }
    public DelegateCommand NewNoteCommand { get; }
    public DelegateCommand SaveNowCommand { get; }
    public DelegateCommand TogglePinnedCommand { get; }
    public DelegateCommand ToggleFavoriteCommand { get; }
    public DelegateCommand MoveToTrashCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                RefreshFilteredNotes();
            }
        }
    }

    public string SelectedTag
    {
        get => _selectedTag;
        set
        {
            if (SetProperty(ref _selectedTag, value ?? "全部标签"))
            {
                RefreshFilteredNotes();
            }
        }
    }

    public NotebookId? SelectedNotebookFilter
    {
        get => _selectedNotebookFilter;
        set
        {
            if (SetProperty(ref _selectedNotebookFilter, value))
            {
                RefreshFilteredNotes();
            }
        }
    }

    public NoteCardViewModel? SelectedNote
    {
        get => _selectedNote;
        private set
        {
            if (SetProperty(ref _selectedNote, value))
            {
                SaveNowCommand.RaiseCanExecuteChanged();
                TogglePinnedCommand.RaiseCanExecuteChanged();
                ToggleFavoriteCommand.RaiseCanExecuteChanged();
                MoveToTrashCommand.RaiseCanExecuteChanged();
                SelectedNoteChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsEditorOpen
    {
        get => _isEditorOpen;
        private set
        {
            if (SetProperty(ref _isEditorOpen, value))
            {
                RaisePropertyChanged(nameof(WallVisibility));
                RaisePropertyChanged(nameof(EditorVisibility));
            }
        }
    }

    public Visibility WallVisibility => IsEditorOpen ? Visibility.Collapsed : Visibility.Visible;
    public Visibility EditorVisibility => IsEditorOpen ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyVisibility => FilteredNotes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public string NotesSummary => $"共 {_allNotes.Count} 条笔记";

    public string SaveStatus
    {
        get => _saveStatus;
        private set => SetProperty(ref _saveStatus, value);
    }

    public RichTextDocumentService DocumentService => _documentService;
    public NoteMediaService MediaService => _mediaService;

    internal void SetNotesReplacementOperationGate(NotesReplacementOperationGate operationGate) =>
        _notesReplacementOperationGate = operationGate;

    internal bool HasNotesReplacementOperationGate => _notesReplacementOperationGate is not null;

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }
        _isInitialized = true;
        await ReloadAsync();
    }

    public async Task SaveDocumentAsync(FlowDocument document)
    {
        if (!TryBeginWorkspaceMutation(out var operation))
        {
            return;
        }

        using (operation)
        {
        if (SelectedNote is not { } note || _workspaceId is not { } workspaceId)
        {
            return;
        }
        var saved = _documentService.Save(document);
        SaveStatus = "正在保存";
        await _saveGate.WaitAsync();
        try
        {
            await _workspaceService.UpdateNoteTitleAsync(workspaceId, note.Id, note.Title);
            await _workspaceService.UpdateRichNoteAsync(workspaceId, note.Id, saved.Json, saved.PlainText);
            await _workspaceService.SetNoteNotebookAsync(workspaceId, note.Id, note.NotebookId);
            await _workspaceService.SetNoteTagsAsync(workspaceId, note.Id, note.Tags);
            note.RichContent = saved.Json;
            note.Content = saved.PlainText;
            await _mediaService.DeleteOrphansAsync(note.Id, saved.MediaPaths);
            SaveStatus = "已保存";
        }
        catch
        {
            SaveStatus = "保存失败，按 Ctrl+S 重试";
            throw;
        }
        finally
        {
            _saveGate.Release();
        }
        }
    }

    public async Task MoveNoteAsync(NoteCardViewModel note)
    {
        if (!TryBeginWorkspaceMutation(out var operation))
        {
            return;
        }

        using (operation)
        {
        if (_workspaceId is { } workspaceId)
        {
            await _workspaceService.MoveNoteAsync(workspaceId, note.Id, note.X, note.Y);
        }
        }
    }

    public void OnNavigatedTo(NavigationContext navigationContext) => _ = InitializeAsync();
    public bool IsNavigationTarget(NavigationContext navigationContext) => true;
    public void OnNavigatedFrom(NavigationContext navigationContext) { }

    private async Task ReloadAsync()
    {
        var workspace = await _workspaceService.GetOrCreateDefaultWorkspaceAsync();
        _workspaceId = workspace.Id;
        _allNotes.Clear();
        _allNotes.AddRange(workspace.Notes
            .Where(static note => note.BoardKey == TodoBoardKeys.Notes && !note.IsDeleted)
            .Select(static note => new NoteCardViewModel(note)));
        RefreshTags();
        RefreshFilteredNotes();
    }

    private async Task AddNoteAsync()
    {
        if (!TryBeginWorkspaceMutation(out var operation))
        {
            return;
        }

        using (operation)
        {
        if (_workspaceId is not { } workspaceId)
        {
            await InitializeAsync();
            if (_workspaceId is not { } loadedWorkspaceId)
            {
                return;
            }
            workspaceId = loadedWorkspaceId;
        }
        var index = _allNotes.Count;
        var x = 32 + (index % 4) * 304;
        var y = 32 + (index / 4) * 204;
        var snapshot = await _workspaceService.CreateNoteAsync(workspaceId, x, y, "新笔记", TodoBoardKeys.Notes);
        var note = new NoteCardViewModel(snapshot);
        _allNotes.Add(note);
        RefreshFilteredNotes();
        OpenNote(note);
        }
    }

    private void OpenNote(NoteCardViewModel? note)
    {
        if (note is null)
        {
            return;
        }
        SelectedNote = note;
        IsEditorOpen = true;
    }

    private async Task TogglePinnedAsync()
    {
        if (!TryBeginWorkspaceMutation(out var operation))
        {
            return;
        }

        using (operation)
        {
        if (SelectedNote is not { } note || _workspaceId is not { } workspaceId) return;
        note.IsPinned = !note.IsPinned;
        await _workspaceService.SetNotePinnedAsync(workspaceId, note.Id, note.IsPinned);
        RefreshFilteredNotes();
        }
    }

    private async Task ToggleFavoriteAsync()
    {
        if (!TryBeginWorkspaceMutation(out var operation))
        {
            return;
        }

        using (operation)
        {
        if (SelectedNote is not { } note || _workspaceId is not { } workspaceId) return;
        note.IsFavorite = !note.IsFavorite;
        await _workspaceService.SetNoteFavoriteAsync(workspaceId, note.Id, note.IsFavorite);
        }
    }

    private async Task MoveToTrashAsync()
    {
        if (!TryBeginWorkspaceMutation(out var operation))
        {
            return;
        }

        using (operation)
        {
        if (SelectedNote is not { } note || _workspaceId is not { } workspaceId) return;
        await _workspaceService.MoveNoteToTrashAsync(workspaceId, note.Id);
        _allNotes.Remove(note);
        SelectedNote = null;
        IsEditorOpen = false;
        RefreshFilteredNotes();
        }
    }

    private bool TryBeginWorkspaceMutation(out IDisposable? operation)
    {
        operation = _notesReplacementOperationGate?.TryBegin();
        return _notesReplacementOperationGate is null || operation is not null;
    }

    private void RefreshFilteredNotes()
    {
        IEnumerable<NoteCardViewModel> query = _allNotes;
        var search = SearchText.Trim();
        if (search.Length > 0)
        {
            query = query.Where(note =>
                note.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                note.Content.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                note.Tags.Any(tag => tag.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }
        if (SelectedTag != "全部标签")
        {
            query = query.Where(note => note.Tags.Contains(SelectedTag, StringComparer.OrdinalIgnoreCase));
        }
        if (SelectedNotebookFilter != NotebookOptions.AllId)
        {
            query = query.Where(note => note.NotebookId == SelectedNotebookFilter);
        }
        FilteredNotes.Clear();
        foreach (var note in query.OrderByDescending(static note => note.IsPinned).ThenByDescending(static note => note.UpdatedAt))
        {
            FilteredNotes.Add(note);
        }
        RaisePropertyChanged(nameof(EmptyVisibility));
        RaisePropertyChanged(nameof(NotesSummary));
    }

    private void RefreshTags()
    {
        AvailableTags.Clear();
        AvailableTags.Add("全部标签");
        foreach (var tag in _allNotes.SelectMany(static note => note.Tags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static tag => tag))
        {
            AvailableTags.Add(tag);
        }
    }
}
