using System.Windows;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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
        private readonly WorkspaceTransferRequestGate _workspaceTransferRequestGate;
        private bool _isNavigationPending;
        private HwndSource? _windowSource;
        public MainWindow(WorkspaceTransferRequestGate workspaceTransferRequestGate)
        {
            _workspaceTransferRequestGate = workspaceTransferRequestGate;
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _windowSource?.AddHook(WindowMessageHook);
        }

        protected override void OnClosed(EventArgs e)
        {
            _windowSource?.RemoveHook(WindowMessageHook);
            _windowSource = null;
            base.OnClosed(e);
        }

        private static IntPtr WindowMessageHook(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int getMinMaxInfo = 0x0024;
            if (message == getMinMaxInfo)
            {
                WindowWorkAreaManager.Apply(windowHandle, lParam);
                handled = true;
            }

            return IntPtr.Zero;
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

        private async void NavigationItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListBoxItem { DataContext: NavigationItemViewModel navigationItem }
                || MainRegionContent.Content is not NotesView { IsEditorOpen: true })
            {
                return;
            }

            e.Handled = true;
            await NavigateAfterSavingAsync(navigationItem);
        }

        private async void NavigationListBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var targetIndex = e.Key switch
            {
                Key.Up => NavigationListBox.SelectedIndex - 1,
                Key.Down => NavigationListBox.SelectedIndex + 1,
                Key.Home => 0,
                Key.End => NavigationListBox.Items.Count - 1,
                Key.PageUp => 0,
                Key.PageDown => NavigationListBox.Items.Count - 1,
                _ => -1
            };
            if (targetIndex < 0
                || targetIndex >= NavigationListBox.Items.Count
                || NavigationListBox.Items[targetIndex] is not NavigationItemViewModel navigationItem
                || MainRegionContent.Content is not NotesView { IsEditorOpen: true })
            {
                return;
            }

            e.Handled = true;
            await NavigateAfterSavingAsync(navigationItem);
        }

        private async void NavigationShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: NavigationSection section }
                || DataContext is not MainWindowViewModel viewModel)
            {
                return;
            }

            var navigationItem = viewModel.NavigationItems.FirstOrDefault(item => item.Section == section);
            if (navigationItem is not null)
            {
                await NavigateAfterSavingAsync(navigationItem);
            }
        }

        private async Task<bool> NavigateAfterSavingAsync(NavigationItemViewModel navigationItem)
        {
            if (_isNavigationPending)
            {
                return false;
            }

            _isNavigationPending = true;
            try
            {
                if (MainRegionContent.Content is NotesView { IsEditorOpen: true } notesView
                    && !await notesView.ReturnToWallAsync())
                {
                    return false;
                }

                if (DataContext is MainWindowViewModel viewModel)
                {
                    viewModel.IsNavigationExpanded = false;
                    viewModel.ActiveNavigationItem = navigationItem;
                }

                return true;
            }
            finally
            {
                _isNavigationPending = false;
            }
        }

        private async void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (WorkspaceTransferCloseGuard.ShouldCancelWindowClose(_workspaceTransferRequestGate))
            {
                e.Cancel = true;
                return;
            }

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
