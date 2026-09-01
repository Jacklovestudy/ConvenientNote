using System.Windows.Controls;

namespace ConvenientNote.Views
{
    public partial class InboxView : UserControl, IWorkspaceReplacementParticipant
    {
        public InboxView()
        {
            InitializeComponent();
        }

        public Task PrepareForWorkspaceReplacementAsync() => TodoBoard.PrepareForWorkspaceReplacementAsync();

        public void ResumeAfterWorkspaceReplacementFailure() => TodoBoard.ResumeAfterWorkspaceReplacementFailure();
    }
}
