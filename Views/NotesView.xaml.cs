using System.Windows.Controls;

namespace ConvenientNote.Views;

public partial class NotesView : UserControl
{
    public NotesView()
    {
        InitializeComponent();
    }

    public Task<bool> FlushAsync() => EditorControl.SaveNowAsync();
}
