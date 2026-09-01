using System.Windows.Controls;

namespace ConvenientNote.Views
{
    public partial class CompletedTodoView : UserControl, IWorkspaceReplacementParticipant
    {
        public CompletedTodoView()
        {
            InitializeComponent();
        }

        public Task PrepareForWorkspaceReplacementAsync() => TodoBoard.PrepareForWorkspaceReplacementAsync();

        public void ResumeAfterWorkspaceReplacementFailure() => TodoBoard.ResumeAfterWorkspaceReplacementFailure();
    }
}
