using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;
using ConvenientNote.Features.Calendar;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation.Regions;

namespace ConvenientNote.ViewModels;

public sealed class CalendarTaskViewModel : BindableBase
{
    private DateTime? _editDate;
    public CalendarTaskViewModel(NoteSnapshot note) { Note = note; _editDate = note.PlannedDate; }
    public NoteSnapshot Note { get; }
    public NoteId Id => Note.Id;
    public string Title => Note.Title;
    public bool IsCompleted => Note.IsCompleted;
    public DateTime? EditDate { get => _editDate; set => SetProperty(ref _editDate, value); }
    public string CompletionText => IsCompleted ? "标为未完成" : "完成待办";
}

public sealed class CalendarDayViewModel(DateTime date, bool isCurrentMonth, bool isToday, bool isSelected,
    string lunarLabel, IReadOnlyList<CalendarTaskViewModel> tasks) : BindableBase
{
    private bool _isSelected = isSelected;
    public DateTime Date { get; } = date;
    public bool IsCurrentMonth { get; } = isCurrentMonth;
    public bool IsToday { get; } = isToday;
    public bool IsSelected { get => _isSelected; internal set => SetProperty(ref _isSelected, value); }
    public string LunarLabel { get; } = lunarLabel;
    public IReadOnlyList<CalendarTaskViewModel> Tasks { get; } = tasks;
    public string DayNumber => Date.Day.ToString();
    public string WorkRestLabel => CalendarDateInfo.WorkRestLabel(Date);
    public bool IsWeekend => Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    public bool HasTasks => Tasks.Count > 0;
    public IReadOnlyList<CalendarTaskViewModel> Previews => Tasks.Take(2).ToArray();
    public string OverflowLabel => Tasks.Count > 2 ? $"+{Tasks.Count - 2} 项" : "";
    public string Dots => Tasks.Count == 0 ? "" : new string('●', Math.Min(Tasks.Count, 3));
    public string AccessibleLabel => $"{Date:yyyy年M月d日}，{LunarLabel}，{Tasks.Count} 项待办";
}

public sealed class CalendarMonthViewModel
{
    public CalendarMonthViewModel(DateTime month, IReadOnlyList<CalendarDayViewModel> days)
    {
        Month = month;
        var leading = ((int)month.DayOfWeek + 6) % 7;
        var cells = Enumerable.Repeat<CalendarDayViewModel?>(null, leading).Concat(days).ToList();
        while (cells.Count % 7 != 0) cells.Add(null);
        Cells = cells;
    }
    public DateTime Month { get; }
    public string Title => Month.ToString("yyyy 年 M 月");
    public IReadOnlyList<CalendarDayViewModel?> Cells { get; }
    public int RowCount => Cells.Count / 7;
}

public sealed class ScheduleViewModel : BindableBase, INavigationAware
{
    private readonly WorkspaceApplicationService _service;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private WorkspaceId? _workspaceId;
    private IReadOnlyList<NoteSnapshot> _notes = [];
    private CalendarTaskViewModel[] _tasks = [];
    private DateTime _selectedDate = DateTime.Today;
    private DateTime _displayMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private string _newTitle = "";
    private string _errorMessage = "";
    private bool _isBusy;
    public ScheduleViewModel(WorkspaceApplicationService service)
    {
        _service = service;
        PreviousMonthCommand = new DelegateCommand(() => ShiftMonth(-1));
        NextMonthCommand = new DelegateCommand(() => ShiftMonth(1));
        TodayCommand = new DelegateCommand(() => { SelectDate(DateTime.Today); ResetCalendarRange(); });
        SelectDateCommand = new DelegateCommand<CalendarDayViewModel>(day => { if (day is not null) SelectDate(day.Date); });
        AddTaskCommand = new DelegateCommand(async () => await AddTaskAsync(), () => !IsBusy && !string.IsNullOrWhiteSpace(NewTitle));
        ToggleCompletionCommand = new DelegateCommand<CalendarTaskViewModel>(async task => { if (task is not null) await ToggleCompletionAsync(task); }, task => !IsBusy && task is not null);
        SaveDateCommand = new DelegateCommand<CalendarTaskViewModel>(async task => { if (task is not null) await RescheduleAsync(task, task.EditDate); }, task => !IsBusy && task is not null);
        ScheduleUnassignedCommand = new DelegateCommand<CalendarTaskViewModel>(async task => { if (task is not null) await RescheduleAsync(task, SelectedDate); }, task => !IsBusy && task is not null);
        DesktopModeCommand = new DelegateCommand(() => DesktopModeRequested?.Invoke(this, EventArgs.Empty));
        RefreshCommand = new DelegateCommand(async () => await RefreshAsync());
        _service.WorkspaceChanged += OnWorkspaceChanged;
        Rebuild();
    }
    public event EventHandler? DesktopModeRequested;
    public event EventHandler? CalendarPositionReset;
    public string ViewTitle => "日历";
    public string ViewDescription => "把待办放进每一天";
    public ObservableCollection<CalendarDayViewModel> Days { get; } = [];
    public ObservableCollection<CalendarMonthViewModel> Months { get; } = [];
    public ObservableCollection<CalendarTaskViewModel> SelectedTasks { get; } = [];
    public ObservableCollection<CalendarTaskViewModel> UnscheduledTasks { get; } = [];
    public string[] WeekDays { get; } = ["一", "二", "三", "四", "五", "六", "日"];
    public string MonthTitle => DisplayMonth.ToString("yyyy 年 M 月");
    public string SelectedDateTitle => SelectedDate.ToString("M月d日 · dddd", CultureInfo.GetCultureInfo("zh-CN"));
    public string SelectedLunarLabel => CalendarDateInfo.Label(SelectedDate);
    public string SelectedSummary => SelectedTasks.Count == 0 ? "这天还没有安排，写下第一件事吧。" : $"{SelectedTasks.Count} 项安排 · {SelectedTasks.Count(t => t.IsCompleted)} 项已完成";
    public string HolidayNotice => DisplayMonth.Year == 2026 ? "休 / 班 · 2026 国务院办公厅节假日安排" : "本年度节假日调休安排暂无数据";
    public string UnscheduledTitle => $"待安排 · {UnscheduledTasks.Count}";
    public DateTime SelectedDate { get => _selectedDate; set => SelectDate(value); }
    public DateTime DisplayMonth { get => _displayMonth; set { var month = new DateTime(value.Year, value.Month, 1); if (month.Year < 1902 || month.Year > 2199) return; if (SetProperty(ref _displayMonth, month)) ResetCalendarRange(); } }
    public string NewTitle { get => _newTitle; set { SetProperty(ref _newTitle, value); AddTaskCommand.RaiseCanExecuteChanged(); } }
    public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; private set { SetProperty(ref _isBusy, value); AddTaskCommand.RaiseCanExecuteChanged(); ToggleCompletionCommand.RaiseCanExecuteChanged(); SaveDateCommand.RaiseCanExecuteChanged(); ScheduleUnassignedCommand.RaiseCanExecuteChanged(); } }
    public DelegateCommand PreviousMonthCommand { get; }
    public DelegateCommand NextMonthCommand { get; }
    public DelegateCommand TodayCommand { get; }
    public DelegateCommand<CalendarDayViewModel> SelectDateCommand { get; }
    public DelegateCommand AddTaskCommand { get; }
    public DelegateCommand<CalendarTaskViewModel> ToggleCompletionCommand { get; }
    public DelegateCommand<CalendarTaskViewModel> SaveDateCommand { get; }
    public DelegateCommand<CalendarTaskViewModel> ScheduleUnassignedCommand { get; }
    public DelegateCommand DesktopModeCommand { get; }
    public DelegateCommand RefreshCommand { get; }
    public void SelectDate(DateTime date)
    {
        date = date.Date;
        if (date.Year < 1902 || date.Year > 2199) return;
        var month = new DateTime(date.Year, date.Month, 1);
        if (date == _selectedDate && month == _displayMonth) return;
        var monthChanged = month != _displayMonth;
        _selectedDate = date;
        _displayMonth = month;
        RaisePropertyChanged(nameof(SelectedDate));
        if (!Days.Any(day => day.Date == date))
        {
            RaisePropertyChanged(nameof(DisplayMonth));
            ResetCalendarRange();
        }
        else
        {
            if (monthChanged)
            {
                RaisePropertyChanged(nameof(DisplayMonth));
                RaisePropertyChanged(nameof(MonthTitle));
                RaisePropertyChanged(nameof(HolidayNotice));
            }
            foreach (var day in Days) day.IsSelected = day.Date == date;
            UpdateSelectedTasks();
        }
    }
    private void ShiftMonth(int delta) { var next = DisplayMonth.AddMonths(delta); DisplayMonth = next; }
    public async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            var workspace = _workspaceId is null ? await _service.GetOrCreateDefaultWorkspaceAsync() : await _service.GetWorkspaceAsync(_workspaceId.Value);
            _workspaceId = workspace.Id;
            _notes = workspace.Notes.Where(n => !n.IsDeleted && n.BoardKey == TodoBoardKeys.DayTodo).ToArray();
            Rebuild();
            if (ErrorMessage.StartsWith("日程读取失败", StringComparison.Ordinal)) ErrorMessage = "";
        }
        catch (Exception ex) { ErrorMessage = "日程读取失败，请重试：" + ex.Message; }
        finally { _refreshGate.Release(); }
    }
    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke(new Action(() => _ = RefreshAsync()));
        else _ = RefreshAsync();
    }
    public Task AddTaskAsync() => WriteAsync(async id =>
    {
        var title = NewTitle.Trim();
        if (title.Length == 0) return;
        await _service.CreateScheduledTodoAsync(id, title, SelectedDate);
        NewTitle = "";
    });
    public Task ToggleCompletionAsync(CalendarTaskViewModel task) => WriteAsync(id => _service.SetNoteCompletionAsync(id, task.Id, !task.IsCompleted));
    public Task RescheduleAsync(CalendarTaskViewModel task, DateTime? date) => WriteAsync(id => _service.SetNotePlannedDateAsync(id, task.Id, date?.Date));
    private async Task WriteAsync(Func<WorkspaceId, Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = "";
        try { if (_workspaceId is null) await RefreshAsync(); if (_workspaceId is not null) { await action(_workspaceId.Value); await RefreshAsync(); } }
        catch (Exception ex) { ErrorMessage = "保存失败，内容已保留。请重试：" + ex.Message; }
        finally { IsBusy = false; }
    }
    private void Rebuild()
    {
        var tasks = _tasks = _notes.Select(n => new CalendarTaskViewModel(n)).OrderBy(t => t.IsCompleted).ThenBy(t => t.Note.CreatedAt).ToArray();
        var dates = Days.Count == 0 ? Enumerable.Range(0, DateTime.DaysInMonth(DisplayMonth.Year, DisplayMonth.Month)).Select(i => DisplayMonth.AddDays(i)).ToArray() : Days.Select(day => day.Date).ToArray();
        Days.Clear();
        foreach (var date in dates) Days.Add(CreateDay(date));
        Months.Clear();
        foreach (var group in Days.GroupBy(day => new DateTime(day.Date.Year, day.Date.Month, 1)))
            Months.Add(new CalendarMonthViewModel(group.Key, group.ToArray()));
        UpdateSelectedTasks();
        UnscheduledTasks.Clear();
        foreach (var task in tasks.Where(t => t.Note.PlannedDate is null && !t.IsCompleted)) UnscheduledTasks.Add(task);
        foreach (var name in new[] { nameof(MonthTitle), nameof(UnscheduledTitle), nameof(HolidayNotice) }) RaisePropertyChanged(name);
    }
    private CalendarDayViewModel CreateDay(DateTime date) => new(date, true, date == DateTime.Today,
        date == SelectedDate, CalendarDateInfo.Label(date), _tasks.Where(t => t.Note.PlannedDate?.Date == date).ToArray());

    private void ResetCalendarRange()
    {
        Days.Clear();
        Rebuild();
        CalendarPositionReset?.Invoke(this, EventArgs.Empty);
    }

    // Retain complete month blocks and existing date objects while extending.
    // Return the pixel adjustment for content added/removed above the viewport.
    public double ExtendCalendar(bool earlier, double rowHeight = 100)
    {
        if (Months.Count == 0) return 0;
        var month = (earlier ? Months[0].Month : Months[^1].Month).AddMonths(earlier ? -1 : 1);
        if (month.Year < 1902 || month.Year > 2199) return 0;
        var days = Enumerable.Range(0, DateTime.DaysInMonth(month.Year, month.Month)).Select(i => CreateDay(month.AddDays(i))).ToArray();
        var block = new CalendarMonthViewModel(month, days);
        double shift = 0;
        if (earlier)
        {
            for (var i = days.Length - 1; i >= 0; i--) Days.Insert(0, days[i]);
            Months.Insert(0, block);
            shift = block.RowCount * rowHeight + 40;
        }
        else
        {
            foreach (var day in days) Days.Add(day);
            Months.Add(block);
        }
        if (Months.Count > 7)
        {
            var removed = earlier ? Months[^1] : Months[0];
            Months.Remove(removed);
            foreach (var day in removed.Cells.OfType<CalendarDayViewModel>()) Days.Remove(day);
            if (!earlier) shift -= removed.RowCount * rowHeight + 40;
        }
        return shift;
    }

    public void UpdateVisibleMonth(DateTime date)
    {
        var month = new DateTime(date.Year, date.Month, 1);
        if (month == _displayMonth || month.Year < 1902 || month.Year > 2199) return;
        _displayMonth = month;
        RaisePropertyChanged(nameof(DisplayMonth));
        RaisePropertyChanged(nameof(MonthTitle));
        RaisePropertyChanged(nameof(HolidayNotice));
    }
    private void UpdateSelectedTasks()
    {
        SelectedTasks.Clear();
        foreach (var task in _tasks.Where(t => t.Note.PlannedDate?.Date == SelectedDate)) SelectedTasks.Add(task);
        foreach (var name in new[] { nameof(SelectedDateTitle), nameof(SelectedLunarLabel), nameof(SelectedSummary) }) RaisePropertyChanged(name);
    }
    public void OnNavigatedTo(NavigationContext navigationContext) => _ = RefreshAsync();
    public bool IsNavigationTarget(NavigationContext navigationContext) => true;
    public void OnNavigatedFrom(NavigationContext navigationContext) { }
}

