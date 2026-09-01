using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Text.RegularExpressions;
using ConvenientNote.Services;
using ConvenientNote.Application.Abstractions;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Workspaces;
using ConvenientNote.ViewModels;
using ConvenientNote.Views;
using Prism.Mvvm;
using Xunit;

namespace ConvenientNote.Tests.Views;

public sealed class MainWindowWorkspaceTransferTests
{
    [Fact]
    public void WorkspaceTransferButtonsAreSecondaryDrawerActionsRoutedToCodeBehind()
    {
        RunSta(() =>
        {
            var window = LoadDrawerMarkup();

            var exportButton = Assert.IsType<Button>(window.FindName("ExportWorkspaceButton"));
            var importButton = Assert.IsType<Button>(window.FindName("ImportWorkspaceButton"));
            var exportActions = Assert.IsType<StackPanel>(exportButton.Parent);
            var importActions = Assert.IsType<StackPanel>(importButton.Parent);
            var shortcuts = Assert.IsType<StackPanel>(window.FindName("NavigationShortcutsPanel"));

            Assert.Equal("导出数据", AutomationProperties.GetName(exportButton));
            Assert.Equal("导入数据", AutomationProperties.GetName(importButton));
            Assert.Equal("WorkspaceTransferActionsPanel", exportActions.Name);
            Assert.Same(exportActions, importActions);
            Assert.Equal(0, Grid.GetRow(exportActions));
            Assert.Equal(1, Grid.GetRow(shortcuts));
            Assert.Null(exportButton.Command);
            Assert.Null(importButton.Command);
            Assert.NotNull(typeof(MainWindow).GetMethod(
                "ExportWorkspaceButton_Click",
                BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(typeof(MainWindow).GetMethod(
                "ImportWorkspaceButton_Click",
                BindingFlags.Instance | BindingFlags.NonPublic));
        });
    }

    [Fact]
    public void ImportConfirmationMakesCancelTheKeyboardDefault()
    {
        RunSta(() =>
        {
            var method = typeof(MainWindow).GetMethod(
                "CreateWorkspaceImportConfirmationContent",
                BindingFlags.Static | BindingFlags.NonPublic);
            var content = Assert.IsAssignableFrom<FrameworkElement>(method?.Invoke(
                null,
                [new WorkspaceBackupPreview("测试", 1, DateTimeOffset.UtcNow), (Action)(() => { }), (Action)(() => { })]));
            var buttons = FindDescendants<Button>(content).ToList();
            var cancel = Assert.Single(buttons, button => Equals(button.Content, "取消"));
            var destructive = Assert.Single(buttons, button => Equals(button.Content, "覆盖并导入"));

            Assert.True(cancel.IsCancel);
            Assert.False(destructive.IsDefault);
        });
    }

    [Fact]
    public void PrismAutoWiredMutableViewsReceiveWorkspaceReplacementGates()
    {
        RunSta(() =>
        {
            var repository = new InMemoryWorkspaceRepository();
            var workspaceService = new WorkspaceApplicationService(repository);
            var notesViewModel = new NotesViewModel(
                workspaceService,
                new RichTextDocumentService(),
                new NoteMediaService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
            var trashViewModel = new TrashViewModel(
                workspaceService,
                new NoteMediaService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
            var dayTodoViewModel = new DayTodoViewModel(workspaceService, new OpenMeteoWeatherService());
            ViewModelLocationProvider.SetDefaultViewModelFactory((_, viewModelType) => viewModelType switch
            {
                var type when type == typeof(NotesViewModel) => notesViewModel,
                var type when type == typeof(TrashViewModel) => trashViewModel,
                var type when type == typeof(DayTodoViewModel) => dayTodoViewModel,
                _ => throw new InvalidOperationException($"Unexpected Prism view model: {viewModelType}")
            });

            _ = new NotesView();
            _ = new TrashView();
            _ = new DayTodoView();

            Assert.True(notesViewModel.HasWorkspaceReplacementOperationGate);
            Assert.True(trashViewModel.HasWorkspaceReplacementOperationGate);
            Assert.True(dayTodoViewModel.HasWorkspaceReplacementOperationGate);
        });
    }

    private static Window LoadDrawerMarkup()
    {
        var markup = File.ReadAllText(FindSourceFile("MainWindow.xaml"));
        markup = markup.Replace(
            "xmlns:local=\"clr-namespace:ConvenientNote\"",
            "xmlns:local=\"clr-namespace:ConvenientNote;assembly=ConvenientNote\"");
        markup = markup.Replace("Style=\"{StaticResource TitleBarButtonStyle}\"", string.Empty)
            .Replace("Style=\"{StaticResource TitleBarCloseButtonStyle}\"", string.Empty);
        markup = Regex.Replace(
            markup,
            "\\s+(?:x:Class|prism:ViewModelLocator.AutoWireViewModel|prism:RegionManager.RegionName|Loaded|Closing|Click|PreviewKeyDown|PreviewMouseLeftButtonDown|Handler|Icon)=\"[^\"]*\"",
            string.Empty);
        return Assert.IsType<Window>(XamlReader.Parse(markup));
    }

    private static string FindSourceFile(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate {fileName} from the test output directory.");
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed class InMemoryWorkspaceRepository : IWorkspaceRepository
    {
        public Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Workspace>>([]);

        public Task<Workspace?> GetAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Workspace?>(null);

        public Task SaveAsync(Workspace workspace, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ReplaceAllAsync(Workspace workspace, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
