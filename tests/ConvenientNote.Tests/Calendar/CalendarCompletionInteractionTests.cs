using ConvenientNote.Tests.Compatibility;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ConvenientNote.Application.Abstractions;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;
using ConvenientNote.ViewModels;
using ConvenientNote.Views;
using Xunit;

namespace ConvenientNote.Tests.Calendar;

public sealed class CalendarCompletionInteractionTests
{
    [Fact]
    public void Changing_loaded_checkbox_state_without_Click_persists_completion_and_timestamp()
        => RunSta(() => WithLoadedPanel(false, (panel, service, id, repository) =>
        {
            var check = FindCheckBox(panel);
            Assert.True(check.IsLoaded);
            check.SetCurrentValue(CheckBox.IsCheckedProperty, true);
            WaitUntil(() => LoadedCurrentCheckBox(panel) is { IsChecked: true, DataContext: CalendarTaskViewModel { IsCompleted: true } });
            var completed = Assert.Single(AwaitOnDispatcher(service.GetWorkspaceAsync(id)).Notes);
            Assert.True(completed.IsCompleted);
            Assert.NotNull(completed.CompletedAt);
            LoadedCurrentCheckBox(panel)!.SetCurrentValue(CheckBox.IsCheckedProperty, false);
            WaitUntil(() => LoadedCurrentCheckBox(panel) is { IsChecked: false, DataContext: CalendarTaskViewModel { IsCompleted: false } });
            var reopened = Assert.Single(AwaitOnDispatcher(service.GetWorkspaceAsync(id)).Notes);
            Assert.False(reopened.IsCompleted);
            Assert.Null(reopened.CompletedAt);
        }));

    [Fact]
    public void Initial_binding_of_completed_item_does_not_toggle_persisted_state()
        => RunSta(() => WithLoadedPanel(true, (panel, service, id, repository) =>
        {
            Assert.True(FindCheckBox(panel).IsChecked);
            var note = Assert.Single(AwaitOnDispatcher(service.GetWorkspaceAsync(id)).Notes);
            Assert.True(note.IsCompleted);
            Assert.NotNull(note.CompletedAt);
        }));

    [Fact]
    public void Failed_save_restores_checkbox_without_recursively_retrying()
        => RunSta(() => WithLoadedPanel(false, (panel, service, id, repository) =>
        {
            repository.FailSaves = true;
            var check = FindCheckBox(panel);
            check.SetCurrentValue(CheckBox.IsCheckedProperty, true);
            Pump();
            Assert.False(check.IsChecked);
            Assert.Equal(1, repository.FailedSaveAttempts);
            Assert.Contains("保存失败", ((ScheduleViewModel)panel.DataContext).ErrorMessage);
        }));

    [Fact]
    public void Programmatic_toggle_during_save_is_rolled_back_without_another_write()
        => RunSta(() => WithLoadedPanel(false, (panel, service, id, repository) =>
        {
            var vm = (ScheduleViewModel)panel.DataContext;
            var check = FindCheckBox(panel);
            repository.SaveRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var saving = vm.ToggleCompletionAsync(Assert.Single(vm.SelectedTasks));
            try
            {
                Assert.True(vm.IsBusy);
                check.SetCurrentValue(CheckBox.IsCheckedProperty, true);
                Assert.False(check.IsChecked);
                Assert.Equal(1, repository.HeldSaveAttempts);
            }
            finally
            {
                repository.SaveRelease.SetResult();
                var dispatcher = Dispatcher.CurrentDispatcher;
                var frame = new DispatcherFrame();
                _ = saving.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
                Dispatcher.PushFrame(frame);
                saving.GetAwaiter().GetResult();
            }
            Assert.True(Assert.Single(AwaitOnDispatcher(service.GetWorkspaceAsync(id)).Notes).IsCompleted);
            Assert.Equal(1, repository.HeldSaveAttempts);
        }));

    private static void WithLoadedPanel(bool completed, Action<CalendarPanel, WorkspaceApplicationService, WorkspaceId, Repository> verify)
    {
        var repository = new Repository();
        var service = new WorkspaceApplicationService(repository);
        var workspace = service.GetOrCreateDefaultWorkspaceAsync().GetAwaiter().GetResult();
        var note = service.CreateScheduledTodoAsync(workspace.Id, "可访问性完成操作", DateTime.Today).GetAwaiter().GetResult();
        if (completed) service.SetNoteCompletionAsync(workspace.Id, note.Id, true).GetAwaiter().GetResult();
        var vm = new ScheduleViewModel(CalendarServiceFixture.Create(service));
        vm.RefreshAsync().GetAwaiter().GetResult();
        var panel = new CalendarPanel { IsCompact = true, DataContext = vm };
        var window = new Window { Content = panel, Width = 420, Height = 520, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try { WaitUntil(() => LoadedCurrentCheckBox(panel) is not null); verify(panel, service, workspace.Id, repository); }
        finally { window.Close(); Pump(); }
    }
    private static CheckBox FindCheckBox(DependencyObject root)
        => Descendants(root).OfType<CheckBox>().First(c => c.DataContext is CalendarTaskViewModel);
    private static CheckBox? LoadedCurrentCheckBox(CalendarPanel panel)
    {
        var vm = (ScheduleViewModel)panel.DataContext;
        if (vm.IsBusy) return null;
        var current = vm.SelectedTasks.FirstOrDefault();
        return Descendants(panel).OfType<CheckBox>().FirstOrDefault(c => c.IsLoaded && ReferenceEquals(c.DataContext, current));
    }
    private static T AwaitOnDispatcher<T>(Task<T> task)
    { WaitUntil(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
    private static void WaitUntil(Func<bool> condition)
    {
        if (condition()) return;
        var frame = new DispatcherFrame();
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (condition() || timeout.Elapsed > TimeSpan.FromSeconds(5)) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.True(condition(), "The current task row did not finish loading in the WPF dispatcher.");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try { action(); } catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private sealed class Repository : IWorkspaceRepository
    {
        private Workspace? _workspace;
        public bool FailSaves { get; set; }
        public int FailedSaveAttempts { get; private set; }
        public TaskCompletionSource? SaveRelease { get; set; }
        public int HeldSaveAttempts { get; private set; }
        public Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Workspace>>(_workspace is null ? [] : [_workspace]);
        public Task<Workspace?> GetAsync(WorkspaceId id, CancellationToken cancellationToken = default) => Task.FromResult(_workspace);
        public async Task SaveAsync(Workspace workspace, CancellationToken cancellationToken = default)
        {
            if (FailSaves) { FailedSaveAttempts++; throw new InvalidOperationException("Test write failure"); }
            if (SaveRelease is not null) { HeldSaveAttempts++; await SaveRelease.Task; }
            _workspace = workspace;
        }
        public Task DeleteAsync(WorkspaceId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReplaceActiveNotesAsync(WorkspaceId id, IReadOnlyCollection<Note> notes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
