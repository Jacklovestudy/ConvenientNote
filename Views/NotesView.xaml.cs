using System.Windows.Controls;
using ConvenientNote.ViewModels;

namespace ConvenientNote.Views;

public partial class NotesView : UserControl, IWorkspaceReplacementParticipant
{
    private readonly WorkspaceReplacementOperationGate _mutationGate = new();

    public NotesView()
    {
        DataContextChanged += NotesView_DataContextChanged;
        InitializeComponent();
        AttachWorkspaceReplacementGate(DataContext);
    }

    public Task<bool> FlushAsync() => EditorControl.SaveNowAsync();

    public bool IsEditorOpen => DataContext is NotesViewModel { IsEditorOpen: true };

    public Task<bool> ReturnToWallAsync() => EditorControl.ReturnToWallAsync();

    public async Task PrepareForWorkspaceReplacementAsync()
    {
        var editorDrain = EditorControl.CancelPendingSaveAsync();
        var viewModelDrain = _mutationGate.PrepareAndDrainAsync();
        IsEnabled = false;
        await Task.WhenAll(editorDrain, viewModelDrain);
    }

    public void ResumeAfterWorkspaceReplacementFailure()
    {
        EditorControl.ResumePendingSave();
        _mutationGate.CancelPreparation();
        IsEnabled = true;
    }

    private void NotesView_DataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        AttachWorkspaceReplacementGate(e.NewValue);
    }

    private void AttachWorkspaceReplacementGate(object? dataContext)
    {
        if (dataContext is NotesViewModel viewModel)
        {
            viewModel.SetWorkspaceReplacementOperationGate(_mutationGate);
        }
    }
}
