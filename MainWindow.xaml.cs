using System.Windows;
using System.ComponentModel;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ConvenientNote.Services;
using ConvenientNote.Views;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using Prism.Navigation.Regions;

namespace ConvenientNote
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly DeferredWindowCloseCoordinator _closeCoordinator = new();
        private readonly WorkspaceTransferRequestGate _workspaceTransferRequestGate = new();
        private readonly WorkspaceReplacementCoordinator _workspaceReplacementCoordinator = new();
        private readonly WorkspaceBackupService _workspaceBackupService;
        private bool _isNavigationPending;
        private HwndSource? _windowSource;
        public MainWindow(WorkspaceBackupService workspaceBackupService)
        {
            _workspaceBackupService = workspaceBackupService;
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

        private async void ExportWorkspaceButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_workspaceTransferRequestGate.TryBegin())
            {
                return;
            }

            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "导出数据",
                    Filter = "Convenient Note 备份 (*.cnote)|*.cnote",
                    DefaultExt = ".cnote",
                    AddExtension = true,
                    FileName = CreateDefaultExportFileName()
                };
                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                if (MainRegionContent.Content is NotesView notesView && !await notesView.FlushAsync())
                {
                    ShowSimpleMessage("保存失败，请重试", MessageBoxImage.Error);
                    return;
                }

                await _workspaceBackupService.ExportAsync(dialog.FileName);
                ShowSimpleMessage("导出完成", MessageBoxImage.Information);
            }
            catch
            {
                ShowSimpleMessage("导出失败，请重试", MessageBoxImage.Error);
            }
            finally
            {
                _workspaceTransferRequestGate.Complete();
            }
        }

        private async void ImportWorkspaceButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_workspaceTransferRequestGate.TryBegin())
            {
                return;
            }

            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "导入数据",
                    Filter = "Convenient Note 备份 (*.cnote)|*.cnote",
                    DefaultExt = ".cnote",
                    CheckFileExists = true,
                    Multiselect = false
                };
                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                using var packageSnapshot = await WorkspaceBackupPackageStager.StageAsync(dialog.FileName);
                var preview = await _workspaceBackupService.InspectAsync(packageSnapshot.PackagePath);
                if (!ConfirmWorkspaceImport(preview))
                {
                    return;
                }

                var result = await _workspaceReplacementCoordinator.ExecuteAsync(
                    GetWorkspaceReplacementParticipants(),
                    () => MainRegionContent.IsEnabled = false,
                    () => _workspaceBackupService.ImportOverwriteAsync(packageSnapshot.PackagePath),
                    ReloadMainRegionAfterWorkspaceImport,
                    async () =>
                    {
                        if (DataContext is MainWindowViewModel viewModel)
                        {
                            await viewModel.ReloadWorkspaceIdentityAsync();
                        }
                    },
                    () =>
                    {
                        if (DataContext is MainWindowViewModel viewModel)
                        {
                            viewModel.ReloadActiveNavigation();
                        }
                    },
                    () => MainRegionContent.IsEnabled = true);

                ShowSimpleMessage($"导入完成，共恢复 {result.NoteCount} 条笔记", MessageBoxImage.Information);
            }
            catch (Exception exception)
            {
                ShowSimpleMessage(WorkspaceBackupImportFailureMessages.GetMessage(exception), MessageBoxImage.Error);
            }
            finally
            {
                _workspaceTransferRequestGate.Complete();
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

        private IReadOnlyList<IWorkspaceReplacementParticipant> GetWorkspaceReplacementParticipants()
        {
            var participants = new List<IWorkspaceReplacementParticipant>();
            if (MainRegionContent.Content is IWorkspaceReplacementParticipant currentView)
            {
                participants.Add(currentView);
            }

            var region = RegionManager.GetObservableRegion(MainRegionContent).Value;
            if (region is not null)
            {
                participants.AddRange(region.Views
                    .OfType<IWorkspaceReplacementParticipant>()
                    .Where(view => !participants.Contains(view)));
            }

            return participants;
        }

        private string CreateDefaultExportFileName()
        {
            var workspaceName = DataContext is MainWindowViewModel viewModel
                ? viewModel.WorkspaceName
                : "默认工作区";
            var invalidFileNameCharacters = Path.GetInvalidFileNameChars();
            var safeWorkspaceName = new string(workspaceName
                .Select(character => invalidFileNameCharacters.Contains(character) ? '_' : character)
                .ToArray());
            return $"ConvenientNote-{safeWorkspaceName}-{DateTime.Now:yyyy-MM-dd}.cnote";
        }

        private void ReloadMainRegionAfterWorkspaceImport()
        {
            var region = RegionManager.GetObservableRegion(MainRegionContent).Value;
            if (region is null)
            {
                return;
            }

            foreach (var view in region.Views.Cast<object>().ToList())
            {
                region.Remove(view);
            }
        }

        private bool ConfirmWorkspaceImport(WorkspaceBackupPreview preview)
        {
            var confirmed = false;
            var dialog = new Window
            {
                Title = "确认导入",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight,
                Width = 440,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Background = System.Windows.Media.Brushes.White
            };
            dialog.Content = CreateWorkspaceImportConfirmationContent(
                preview,
                () =>
                {
                    confirmed = true;
                    dialog.DialogResult = true;
                },
                () => dialog.DialogResult = false);
            dialog.ShowDialog();
            return confirmed;
        }

        private static UIElement CreateWorkspaceImportConfirmationContent(
            WorkspaceBackupPreview preview,
            Action confirm,
            Action cancel)
        {
            var container = new Grid { Margin = new Thickness(24) };
            container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            container.Children.Add(new TextBlock
            {
                Text = "导入将覆盖当前全部笔记和图片，此操作无法撤销。",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            var summary = new TextBlock
            {
                Text = $"备份：{preview.WorkspaceName}，共 {preview.NoteCount} 条笔记",
                Margin = new Thickness(0, 10, 0, 18),
                Foreground = System.Windows.Media.Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(summary, 1);
            container.Children.Add(summary);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var cancelButton = new Button
            {
                Content = "取消",
                Padding = new Thickness(12, 6, 12, 6),
                IsCancel = true,
                IsDefault = true
            };
            cancelButton.Click += (_, _) => cancel();
            var confirmButton = new Button
            {
                Content = "覆盖并导入",
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(12, 6, 12, 6)
            };
            confirmButton.Click += (_, _) => confirm();
            actions.Children.Add(cancelButton);
            actions.Children.Add(confirmButton);
            Grid.SetRow(actions, 2);
            container.Children.Add(actions);
            return container;
        }

        private void ShowSimpleMessage(string message, MessageBoxImage icon)
        {
            MessageBox.Show(this, message, "Convenient Note", MessageBoxButton.OK, icon);
        }
    }
}
