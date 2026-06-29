using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Workspaces;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation.Regions;

namespace ConvenientNote.ViewModels
{
    public enum TodoBoardFilter
    {
        All,
        Active,
        Completed
    }

    public abstract class TodoBoardViewModel : BindableBase, INavigationAware
    {
        private const double MinimumBoardWidth = 1800;
        private const double MinimumBoardHeight = 1100;
        private const double BoardContentPadding = 120;

        private readonly WorkspaceApplicationService _workspaceApplicationService;
        private readonly TodoBoardFilter _filter;
        private readonly List<CanvasTodoViewModel> _allTodoItems = new();
        private WorkspaceId? _currentWorkspaceId;
        private DateTime _selectedDate = DateTime.Today;
        private double _boardWidth = MinimumBoardWidth;
        private double _boardHeight = MinimumBoardHeight;
        private string _currentDateTitle = string.Empty;
        private string _calendarMonthTitle = string.Empty;
        private string _quickAddTitle = string.Empty;
        private string _summary = string.Empty;
        private string _emptyStateTitle = string.Empty;
        private string _emptyStateDescription = string.Empty;

        protected TodoBoardViewModel(
            WorkspaceApplicationService workspaceApplicationService,
            TodoBoardFilter filter,
            string viewTitle,
            string viewDescription,
            bool canAddTodo)
        {
            _workspaceApplicationService = workspaceApplicationService;
            _filter = filter;
            ViewTitle = viewTitle;
            ViewDescription = viewDescription;
            CanAddTodo = canAddTodo;

            AddTodoCommand = new DelegateCommand(
                async () => await AddTodoAsync(),
                () => CanAddTodo);
            PreviousWeekCommand = new DelegateCommand(() => ShiftSelectedDate(-7));
            NextWeekCommand = new DelegateCommand(() => ShiftSelectedDate(7));
            PreviousMonthCommand = new DelegateCommand(() => ShiftSelectedMonth(-1));
            NextMonthCommand = new DelegateCommand(() => ShiftSelectedMonth(1));
            SelectTodayCommand = new DelegateCommand(() => SelectDate(DateTime.Today));
            SelectDateCommand = new DelegateCommand<DateTabViewModel>(dateTab =>
            {
                if (dateTab is not null)
                {
                    SelectDate(dateTab.Date);
                }
            });

            RefreshDateStrip();
            RefreshViewStatus();
        }

        public string ViewTitle { get; }

        public string ViewDescription { get; }

        public bool CanAddTodo { get; }

        public double BoardWidth
        {
            get => _boardWidth;
            private set => SetProperty(ref _boardWidth, value);
        }

        public double BoardHeight
        {
            get => _boardHeight;
            private set => SetProperty(ref _boardHeight, value);
        }

        public string CurrentDateTitle
        {
            get => _currentDateTitle;
            private set => SetProperty(ref _currentDateTitle, value);
        }

        public string CalendarMonthTitle
        {
            get => _calendarMonthTitle;
            private set => SetProperty(ref _calendarMonthTitle, value);
        }

        public string QuickAddTitle
        {
            get => _quickAddTitle;
            set => SetProperty(ref _quickAddTitle, value);
        }

        public string Summary
        {
            get => _summary;
            private set => SetProperty(ref _summary, value);
        }

        public string EmptyStateTitle
        {
            get => _emptyStateTitle;
            private set => SetProperty(ref _emptyStateTitle, value);
        }

        public string EmptyStateDescription
        {
            get => _emptyStateDescription;
            private set => SetProperty(ref _emptyStateDescription, value);
        }

        public Visibility EmptyStateVisibility => TodoItems.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        public ObservableCollection<DateTabViewModel> DateTabs { get; } = new();

        public ObservableCollection<CanvasTodoViewModel> TodoItems { get; } = new();

        public DelegateCommand AddTodoCommand { get; }

        public DelegateCommand PreviousWeekCommand { get; }

        public DelegateCommand NextWeekCommand { get; }

        public DelegateCommand PreviousMonthCommand { get; }

        public DelegateCommand NextMonthCommand { get; }

        public DelegateCommand SelectTodayCommand { get; }

        public DelegateCommand<DateTabViewModel> SelectDateCommand { get; }

        public async Task CommitTodoTitleAsync(CanvasTodoViewModel todo)
        {
            if (_currentWorkspaceId is not { } workspaceId)
            {
                return;
            }

            await _workspaceApplicationService.UpdateNoteTitleAsync(workspaceId, todo.Id, todo.Title);
        }

        public async Task CommitTodoContentAsync(CanvasTodoViewModel todo)
        {
            if (_currentWorkspaceId is not { } workspaceId)
            {
                return;
            }

            await _workspaceApplicationService.UpdateNoteContentAsync(workspaceId, todo.Id, todo.Content);
        }

        public async Task CommitTodoPositionAsync(CanvasTodoViewModel todo)
        {
            if (_currentWorkspaceId is not { } workspaceId)
            {
                return;
            }

            await _workspaceApplicationService.MoveNoteAsync(workspaceId, todo.Id, todo.X, todo.Y);
            RefreshBoardSize();
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            _ = LoadWorkspaceAsync();
        }

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            return true;
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
        }

        protected virtual string GetEmptyStateTitle()
        {
            return _filter switch
            {
                TodoBoardFilter.Completed => "还没有已完成事项",
                _ => "这里暂时没有待办"
            };
        }

        protected virtual string GetEmptyStateDescription()
        {
            return _filter switch
            {
                TodoBoardFilter.Completed => "勾选便签左上角的复选框后，它会出现在这里。",
                _ => "在右上角输入内容并新增，或切换到其他导航视图。"
            };
        }

        private async Task LoadWorkspaceAsync()
        {
            try
            {
                var workspace = await _workspaceApplicationService.GetOrCreateDefaultWorkspaceAsync();
                _currentWorkspaceId = workspace.Id;

                _allTodoItems.Clear();
                foreach (var note in workspace.Notes)
                {
                    _allTodoItems.Add(CreateTodoViewModel(note));
                }

                RefreshVisibleTodos();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private async Task AddTodoAsync()
        {
            if (!CanAddTodo || _currentWorkspaceId is not { } workspaceId)
            {
                return;
            }

            var title = string.IsNullOrWhiteSpace(QuickAddTitle) ? "新待办" : QuickAddTitle.Trim();
            var index = _allTodoItems.Count;
            var x = 32 + index % 3 * 290;
            var y = 32 + index / 3 * 180;
            var note = await _workspaceApplicationService.CreateNoteAsync(workspaceId, x, y, title);

            _allTodoItems.Add(CreateTodoViewModel(note));
            QuickAddTitle = string.Empty;
            RefreshVisibleTodos();
        }

        private CanvasTodoViewModel CreateTodoViewModel(NoteSnapshot note)
        {
            return new CanvasTodoViewModel(
                note,
                async todo =>
                {
                    if (_currentWorkspaceId is { } workspaceId)
                    {
                        await _workspaceApplicationService.SetNoteCompletionAsync(
                            workspaceId,
                            todo.Id,
                            todo.IsCompleted);
                        await LoadWorkspaceAsync();
                    }
                });
        }

        private void RefreshVisibleTodos()
        {
            var visibleTodos = _filter switch
            {
                TodoBoardFilter.Active => _allTodoItems.Where(todo => !todo.IsCompleted),
                TodoBoardFilter.Completed => _allTodoItems.Where(todo => todo.IsCompleted),
                _ => _allTodoItems
            };

            TodoItems.Clear();
            foreach (var todo in visibleTodos.OrderBy(todo => todo.ZIndex))
            {
                TodoItems.Add(todo);
            }

            RefreshBoardSize();
            RefreshViewStatus();
        }

        private void RefreshBoardSize()
        {
            if (TodoItems.Count == 0)
            {
                BoardWidth = MinimumBoardWidth;
                BoardHeight = MinimumBoardHeight;
                return;
            }

            BoardWidth = Math.Max(
                MinimumBoardWidth,
                TodoItems.Max(todo => todo.X + todo.Width + BoardContentPadding));
            BoardHeight = Math.Max(
                MinimumBoardHeight,
                TodoItems.Max(todo => todo.Y + todo.Height + BoardContentPadding));
        }

        private void RefreshViewStatus()
        {
            var completedCount = _allTodoItems.Count(todo => todo.IsCompleted);
            var totalCount = _allTodoItems.Count;

            Summary = _filter == TodoBoardFilter.Completed
                ? $"已完成 {completedCount} / 全部 {totalCount}"
                : $"{ViewTitle} · {TodoItems.Count} 项";

            EmptyStateTitle = GetEmptyStateTitle();
            EmptyStateDescription = GetEmptyStateDescription();
            RaisePropertyChanged(nameof(EmptyStateVisibility));
        }

        private void ShiftSelectedDate(int days)
        {
            SelectDate(_selectedDate.AddDays(days));
        }

        private void ShiftSelectedMonth(int months)
        {
            SelectDate(_selectedDate.AddMonths(months));
        }

        private void SelectDate(DateTime date)
        {
            _selectedDate = date.Date;
            RefreshDateStrip();
        }

        private void RefreshDateStrip()
        {
            var start = _selectedDate.AddDays(-3);

            DateTabs.Clear();
            for (var index = 0; index < 7; index++)
            {
                var date = start.AddDays(index);
                DateTabs.Add(new DateTabViewModel(
                    date,
                    date == DateTime.Today,
                    date == _selectedDate));
            }

            CurrentDateTitle = $"{_selectedDate:MM月dd日} {GetDateCaption(_selectedDate)}";
            CalendarMonthTitle = $"{_selectedDate:yyyy 年 M月}";
        }

        private static string GetDateCaption(DateTime date)
        {
            if (date == DateTime.Today)
            {
                return "今天";
            }

            if (date == DateTime.Today.AddDays(1))
            {
                return "明天";
            }

            if (date == DateTime.Today.AddDays(-1))
            {
                return "昨天";
            }

            return date.ToString("dddd");
        }
    }
}
