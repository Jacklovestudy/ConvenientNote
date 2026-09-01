using System.Windows.Controls;

namespace ConvenientNote.Views
{
    public partial class TrashView : UserControl, IWorkspaceReplacementParticipant
    {
        private readonly WorkspaceReplacementOperationGate _mutationGate = new();

        public TrashView()
        {
            DataContextChanged += TrashView_DataContextChanged;
            InitializeComponent();
            AttachWorkspaceReplacementGate(DataContext);
        }

        public async Task PrepareForWorkspaceReplacementAsync()
        {
            var drain = _mutationGate.PrepareAndDrainAsync();
            IsEnabled = false;
            await drain;
        }

        public void ResumeAfterWorkspaceReplacementFailure()
        {
            _mutationGate.CancelPreparation();
            IsEnabled = true;
        }

        private void TrashView_DataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            AttachWorkspaceReplacementGate(e.NewValue);
        }

        private void AttachWorkspaceReplacementGate(object? dataContext)
        {
            if (dataContext is ViewModels.TrashViewModel viewModel)
            {
                viewModel.SetWorkspaceReplacementOperationGate(_mutationGate);
            }
        }
    }
}
