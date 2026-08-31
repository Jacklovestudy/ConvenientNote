using System.Windows.Controls;
using ConvenientNote.ViewModels;

namespace ConvenientNote.Views;

public partial class NotesView : UserControl
{
    public NotesView()
    {
        InitializeComponent();
    }

    public Task<bool> FlushAsync() => EditorControl.SaveNowAsync();

    public bool IsEditorOpen => DataContext is NotesViewModel { IsEditorOpen: true };

    public Task<bool> ReturnToWallAsync() => EditorControl.ReturnToWallAsync();
}
