using System.Windows;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ConvenientNote.Views;
using ConvenientNote.UI.Common;
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
        private bool _closeRequestInProgress;
        private HwndSource? _windowSource;
        private Rect _fullBounds;
        private WindowState _fullState;
        private bool _switchingLayout;
        public bool IsCompactCalendar => CompactLayout.Visibility == Visibility.Visible;

        public void InitializeCompactMode(ICompactModeProvider calendar)
        {
            calendar.Requested += async (_, _) => await EnterCompactCalendarAsync(calendar);
        }

        public async Task EnterCompactCalendarAsync(ICompactModeProvider calendar)
        {
            if (IsCompactCalendar || _switchingLayout) return;
            _switchingLayout = true;
            try
            {
                if (!await PrepareForDesktopAsync()) return;
                _fullState = WindowState;
                _fullBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
                WindowState = WindowState.Normal;
                MinWidth = 360;
                MinHeight = 540;
                Width = 440;
                Height = 620;
                FullLayout.Visibility = Visibility.Collapsed;
                FullBranding.Visibility = Visibility.Collapsed;
                CompactBranding.Visibility = Visibility.Visible;
                CompactCalendarContent.Content = calendar.CreateContent();
                CompactLayout.Visibility = Visibility.Visible;
            }
            finally { _switchingLayout = false; }
        }

        public void ExitCompactCalendar()
        {
            if (!IsCompactCalendar) return;
            CompactLayout.Visibility = Visibility.Collapsed;
            CompactCalendarContent.Content = null;
            FullLayout.Visibility = Visibility.Visible;
            FullBranding.Visibility = Visibility.Visible;
            CompactBranding.Visibility = Visibility.Collapsed;
            MinWidth = 960;
            MinHeight = 620;
            Left = _fullBounds.Left;
            Top = _fullBounds.Top;
            Width = _fullBounds.Width;
            Height = _fullBounds.Height;
            WindowState = _fullState;
        }

        private void ExitCompactCalendar_Click(object sender, RoutedEventArgs e) => ExitCompactCalendar();
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
                || MainRegionContent.Content is not IPageLifecycle { RequiresNavigationPreparation: true })
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
                || MainRegionContent.Content is not IPageLifecycle { RequiresNavigationPreparation: true })
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
                if (MainRegionContent.Content is IPageLifecycle { RequiresNavigationPreparation: true } notesView
                    && !await notesView.PrepareToLeaveAsync())
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

            // The deferred close after a successful save has already been confirmed.
            if (_closeCoordinator.CanClose)
            {
                return;
            }

            if (_closeRequestInProgress)
            {
                e.Cancel = true;
                return;
            }

            _closeRequestInProgress = true;
            if (MessageBox.Show(this, "确定退出 Convenient Note 吗？", "退出确认",
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                _closeRequestInProgress = false;
                e.Cancel = true;
                return;
            }

            var notesView = FindActiveLifecycle();
            if (notesView is null) return;

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

            if (!saved) _closeRequestInProgress = false;

            _closeCoordinator.CompleteFlush(
                saved,
                close => _ = Dispatcher.BeginInvoke(close),
                Close);
        }

        public async Task<bool> PrepareForDesktopAsync()
        {
            if (WorkspaceTransferCloseGuard.ShouldCancelWindowClose(_workspaceTransferRequestGate)) return false;
            var notesView = FindActiveLifecycle();
            return notesView is null || await notesView.FlushAsync();
        }

        private IPageLifecycle? FindActiveLifecycle()
        {
            if (MainRegionContent.Content is IPageLifecycle activeNotesView)
            {
                return activeNotesView;
            }

            return RegionManager.GetObservableRegion(MainRegionContent)
                .Value?
                .Views
                .OfType<IPageLifecycle>()
                .FirstOrDefault();
        }

    }
}
