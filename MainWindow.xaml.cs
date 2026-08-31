using System.Windows;
using System.ComponentModel;
using ConvenientNote.Views;
using MaterialDesignThemes.Wpf;
using Prism.Navigation.Regions;

namespace ConvenientNote
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly DeferredWindowCloseCoordinator _closeCoordinator = new();
        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            MaximizeRestoreIcon.Kind = WindowState == WindowState.Maximized
                ? PackIconKind.WindowRestore
                : PackIconKind.WindowMaximize;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                await viewModel.InitializeAsync();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            var notesView = FindNotesView();
            if (_closeCoordinator.CanClose || notesView is null)
            {
                return;
            }

            e.Cancel = true;
            if (!_closeCoordinator.TryBeginFlush())
            {
                return;
            }

            var saved = false;
            try
            {
                saved = await notesView.FlushAsync();
            }
            catch
            {
            }

            _closeCoordinator.CompleteFlush(
                saved,
                close => _ = Dispatcher.BeginInvoke(close),
                Close);
        }

        private NotesView? FindNotesView()
        {
            if (MainRegionContent.Content is NotesView activeNotesView)
            {
                return activeNotesView;
            }

            return RegionManager.GetObservableRegion(MainRegionContent)
                .Value?
                .Views
                .OfType<NotesView>()
                .FirstOrDefault();
        }
    }
}
