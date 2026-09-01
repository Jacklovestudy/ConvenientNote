using System.Windows.Controls;

namespace ConvenientNote.Views
{
    public partial class DayTodoView : UserControl, IWorkspaceReplacementParticipant
    {
        public DayTodoView()
        {
            InitializeComponent();
        }

        public Task PrepareForWorkspaceReplacementAsync() => TodoBoard.PrepareForWorkspaceReplacementAsync();

        public void ResumeAfterWorkspaceReplacementFailure() => TodoBoard.ResumeAfterWorkspaceReplacementFailure();
    }
}
