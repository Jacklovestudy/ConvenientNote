using ConvenientNote.Application.Abstractions;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Workspaces;
using ConvenientNote.ViewModels;
using Xunit;

namespace ConvenientNote.Tests.Calendar;

public sealed class ScheduleViewModelTests
{
    [Fact]
    public void Continuous_calendar_extends_without_gaps_and_bounds_loaded_weeks()
    {
        var vm = new ScheduleViewModel(new WorkspaceApplicationService(new Repository()));
        vm.DisplayMonth = new DateTime(2026, 12, 1);
        var original = vm.Days.ToArray();
        Assert.Equal(0, vm.ExtendCalendar(false));
        Assert.Same(original[0], vm.Days[0]);
        Assert.Equal(62, vm.Days.Count);
        Assert.Equal(640, vm.ExtendCalendar(true));
        Assert.Same(original[0], vm.Days[30]);
        for (var i = 0; i < 30; i++) vm.ExtendCalendar(false);
        Assert.Equal(7, vm.Months.Count);
        Assert.Equal(1, vm.Days[0].Date.Day);
        foreach (var month in vm.Months)
        {
            var days = month.Cells.OfType<CalendarDayViewModel>().ToArray();
            Assert.Equal(DateTime.DaysInMonth(month.Month.Year, month.Month.Month), days.Length);
            Assert.All(days, day => Assert.Equal(month.Month.Month, day.Date.Month));
            Assert.Equal(((int)month.Month.DayOfWeek + 6) % 7, month.Cells.TakeWhile(day => day is null).Count());
        }
        for (var i = 1; i < vm.Days.Count; i++) Assert.Equal(vm.Days[i - 1].Date.AddDays(1), vm.Days[i].Date);
        var loaded = vm.Days.ToArray();
        vm.SelectDate(loaded[80].Date);
        Assert.Same(loaded[0], vm.Days[0]);
        Assert.True(loaded[80].IsSelected);
        vm.TodayCommand.Execute();
        Assert.Equal(DateTime.DaysInMonth(DateTime.Today.Year, DateTime.Today.Month), vm.Days.Count);
        Assert.Equal(DateTime.Today, vm.SelectedDate);
    }

    [Fact]
    public void Selecting_within_month_preserves_date_cells_and_only_updates_selection()
    {
        var vm = new ScheduleViewModel(new WorkspaceApplicationService(new Repository()));
        vm.SelectDate(new DateTime(2026, 9, 7));
        var cells = vm.Days.ToArray();
        var resets = 0;
        vm.Days.CollectionChanged += (_, _) => resets++;
        vm.SelectDateCommand.Execute(cells.Single(d => d.Date == new DateTime(2026, 9, 17)));
        Assert.Equal(0, resets);
        for (var i = 0; i < cells.Length; i++) Assert.Same(cells[i], vm.Days[i]);
        Assert.Equal(new DateTime(2026, 9, 17), Assert.Single(vm.Days, d => d.IsSelected).Date);
        var notifications = 0;
        vm.PropertyChanged += (_, _) => notifications++;
        vm.SelectDate(vm.SelectedDate);
        Assert.Equal(0, notifications);
        vm.SelectDate(new DateTime(2026, 10, 1));
        Assert.Equal(10, vm.DisplayMonth.Month);
        Assert.Equal(new DateTime(2026, 10, 1), Assert.Single(vm.Days, d => d.IsSelected).Date);
    }

    [Fact]
    public async Task Pending_save_disables_all_task_mutations_until_completion()
    {
        var repository = new Repository();
        var service = new WorkspaceApplicationService(repository);
        var workspace = await service.GetOrCreateDefaultWorkspaceAsync();
        await service.CreateScheduledTodoAsync(workspace.Id, "保存中", DateTime.Today);
        var vm = new ScheduleViewModel(service);
        await vm.RefreshAsync();
        var task = Assert.Single(vm.SelectedTasks);
        repository.SaveEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.SaveRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var saving = vm.ToggleCompletionAsync(task);
        await repository.SaveEntered.Task;
        try
        {
            Assert.False(vm.ToggleCompletionCommand.CanExecute(task));
            Assert.False(vm.SaveDateCommand.CanExecute(task));
            Assert.False(vm.ScheduleUnassignedCommand.CanExecute(task));
        }
        finally { repository.SaveRelease.SetResult(); await saving; }
        Assert.True(vm.ToggleCompletionCommand.CanExecute(Assert.Single(vm.SelectedTasks)));
    }

    [Fact]
    public async Task Calendar_excludes_deleted_and_knowledge_notes_and_normalizes_selected_time()
    {
        var service = new WorkspaceApplicationService(new Repository());
        var workspace = await service.GetOrCreateDefaultWorkspaceAsync();
        var date = new DateTime(2024, 2, 29);
        var removed = await service.CreateScheduledTodoAsync(workspace.Id, "已删除", date);
        await service.MoveNoteToTrashAsync(workspace.Id, removed.Id);
        await service.CreateNoteAsync(workspace.Id, 0, 0, "知识点", TodoBoardKeys.Notes);
        await service.CreateScheduledTodoAsync(workspace.Id, "闰日计划", date);
        var vm = new ScheduleViewModel(service);
        vm.SelectDate(date.AddHours(15));
        await vm.RefreshAsync();
        Assert.Equal(date, vm.SelectedDate);
        Assert.Equal("闰日计划", Assert.Single(vm.SelectedTasks).Title);
        Assert.Empty(vm.UnscheduledTasks);
    }

    [Fact]
    public async Task Scheduling_completion_and_external_changes_refresh_selected_day()
    {
        var service = new WorkspaceApplicationService(new Repository());
        var workspace = await service.GetOrCreateDefaultWorkspaceAsync();
        var date = new DateTime(2026, 12, 31);
        var scheduled = await service.CreateScheduledTodoAsync(workspace.Id, "年末整理", date);
        await service.CreateNoteAsync(workspace.Id, 0, 0, "待安排");
        var vm = new ScheduleViewModel(service);
        vm.SelectDate(date);
        await vm.RefreshAsync();
        Assert.Equal("年末整理", Assert.Single(vm.SelectedTasks).Title);
        Assert.Single(vm.UnscheduledTasks);
        await vm.ToggleCompletionAsync(vm.SelectedTasks[0]);
        Assert.True(Assert.Single(vm.SelectedTasks).IsCompleted);
        await vm.RescheduleAsync(vm.SelectedTasks[0], date.AddDays(1));
        Assert.Empty(vm.SelectedTasks);
        vm.SelectDate(date.AddDays(1));
        Assert.Equal(scheduled.Id, Assert.Single(vm.SelectedTasks).Id);
        await service.CreateScheduledTodoAsync(workspace.Id, "新安排", date.AddDays(1));
        await vm.RefreshAsync();
        Assert.Equal(2, vm.SelectedTasks.Count);
    }

    private sealed class Repository : IWorkspaceRepository
    {
        private Workspace? _workspace;
        public TaskCompletionSource? SaveEntered { get; set; }
        public TaskCompletionSource? SaveRelease { get; set; }
        public Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Workspace>>(_workspace is null ? [] : [_workspace]);
        public Task<Workspace?> GetAsync(WorkspaceId id, CancellationToken cancellationToken = default)
            => Task.FromResult(_workspace?.Id == id ? _workspace : null);
        public async Task SaveAsync(Workspace workspace, CancellationToken cancellationToken = default)
        { if (SaveRelease is not null) { SaveEntered!.SetResult(); await SaveRelease.Task; } _workspace = workspace; }
        public Task DeleteAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default)
        { _workspace = null; return Task.CompletedTask; }
        public Task ReplaceActiveNotesAsync(WorkspaceId workspaceId, IReadOnlyCollection<ConvenientNote.Domain.Notes.Note> importedNotes, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
