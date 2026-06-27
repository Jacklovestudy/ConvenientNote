using System.Collections.ObjectModel;
using System.Windows;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Workspaces;
using MaterialDesignThemes.Wpf;
using Prism.Commands;
using Prism.Mvvm;

namespace ConvenientNote
{
    public class MainWindowViewModel : BindableBase
    {
        private readonly WorkspaceApplicationService _workspaceApplicationService;
        private WorkspaceId? _currentWorkspaceId;
        private DateTime _selectedDate = DateTime.Today;
        private string _currentDateTitle = string.Empty;
        private string _quickAddTitle = string.Empty;
        private string _title = "Convenient Note";
        private string _workspaceName = "默认工作区";
        private bool _isNavigationExpanded;

        public MainWindowViewModel(WorkspaceApplicationService workspaceApplicationService)
        {
            _workspaceApplicationService = workspaceApplicationService;

            AddTodoCommand = new DelegateCommand(async () => await AddTodoAsync());
            ToggleNavigationCommand = new DelegateCommand(ToggleNavigation);
            PreviousWeekCommand = new DelegateCommand(() => ShiftSelectedDate(-7));
            NextWeekCommand = new DelegateCommand(() => ShiftSelectedDate(7));
            SelectTodayCommand = new DelegateCommand(() => SelectDate(DateTime.Today));
            SelectDateCommand = new DelegateCommand<DateTabViewModel>(dateTab =>
            {
                if (dateTab is not null)
                {
                    SelectDate(dateTab.Date);
                }
            });

            NavigationItems.Add(new NavigationItemViewModel("Day Todo", "今天要处理的待办", PackIconKind.CalendarToday));
            NavigationItems.Add(new NavigationItemViewModel("最近待办", "最近创建和更新", PackIconKind.History));
            NavigationItems.Add(new NavigationItemViewModel("日程概览", "按日期查看", PackIconKind.CalendarMonth));
            NavigationItems.Add(new NavigationItemViewModel("待办箱", "未完成事项", PackIconKind.Inbox));
            NavigationItems.Add(new NavigationItemViewModel("数据复盘", "完成情况", PackIconKind.ChartLine));
            NavigationItems.Add(new NavigationItemViewModel("已达成", "已完成事项", PackIconKind.CheckCircleOutline));
            NavigationItems.Add(new NavigationItemViewModel("回收站", "删除的项目", PackIconKind.DeleteOutline));

            RefreshDateStrip();
            _ = LoadDefaultWorkspaceAsync();
        }

        public string Title
        {
            get => _title;
            private set => SetProperty(ref _title, value);
        }

        public string WorkspaceName
        {
            get => _workspaceName;
            private set => SetProperty(ref _workspaceName, value);
        }

        public string CurrentDateTitle
        {
            get => _currentDateTitle;
            private set => SetProperty(ref _currentDateTitle, value);
        }

        public string QuickAddTitle
        {
            get => _quickAddTitle;
            set => SetProperty(ref _quickAddTitle, value);
        }

        public bool IsNavigationExpanded
        {
            get => _isNavigationExpanded;
            set
            {
                if (SetProperty(ref _isNavigationExpanded, value))
                {
                    RaisePropertyChanged(nameof(NavigationColumnWidth));
                    RaisePropertyChanged(nameof(NavigationExpandedVisibility));
                    RaisePropertyChanged(nameof(NavigationCollapsedVisibility));
                    RaisePropertyChanged(nameof(NavigationToggleText));
                }
            }
        }

        public GridLength NavigationColumnWidth => IsNavigationExpanded
            ? new GridLength(236)
            : new GridLength(72);

        public Visibility NavigationExpandedVisibility => IsNavigationExpanded
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility NavigationCollapsedVisibility => IsNavigationExpanded
            ? Visibility.Collapsed
            : Visibility.Visible;

        public string NavigationToggleText => IsNavigationExpanded ? "‹" : "›";

        public ObservableCollection<NavigationItemViewModel> NavigationItems { get; } = new();

        public ObservableCollection<DateTabViewModel> DateTabs { get; } = new();

        public ObservableCollection<CanvasTodoViewModel> TodoItems { get; } = new();

        public DelegateCommand AddTodoCommand { get; }

        public DelegateCommand ToggleNavigationCommand { get; }

        public DelegateCommand PreviousWeekCommand { get; }

        public DelegateCommand NextWeekCommand { get; }

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
        }

        private void ToggleNavigation()
        {
            IsNavigationExpanded = !IsNavigationExpanded;
        }

        private async Task LoadDefaultWorkspaceAsync()
        {
            var workspace = await _workspaceApplicationService.GetOrCreateDefaultWorkspaceAsync();

            _currentWorkspaceId = workspace.Id;
            WorkspaceName = workspace.Name;
            Title = $"{workspace.Name} - Convenient Note";

            TodoItems.Clear();
            foreach (var note in workspace.Notes)
            {
                TodoItems.Add(CreateTodoViewModel(note));
            }
        }

        private async Task AddTodoAsync()
        {
            if (_currentWorkspaceId is not { } workspaceId)
            {
                return;
            }

            var title = string.IsNullOrWhiteSpace(QuickAddTitle) ? "新待办" : QuickAddTitle.Trim();
            var index = TodoItems.Count;
            var x = 32 + index % 3 * 290;
            var y = 32 + index / 3 * 180;
            var note = await _workspaceApplicationService.CreateNoteAsync(workspaceId, x, y, title);

            TodoItems.Add(CreateTodoViewModel(note));
            QuickAddTitle = string.Empty;
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
                    }
                });
        }

        private void ShiftSelectedDate(int days)
        {
            SelectDate(_selectedDate.AddDays(days));
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
