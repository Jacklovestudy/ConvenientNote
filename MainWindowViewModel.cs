using System.Collections.ObjectModel;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Views;
using MaterialDesignThemes.Wpf;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation.Regions;

namespace ConvenientNote
{
    public class MainWindowViewModel : BindableBase
    {
        public const string MainRegionName = "MainRegion";

        private readonly IRegionManager _regionManager;
        private readonly WorkspaceApplicationService _workspaceApplicationService;
        private NavigationItemViewModel? _activeNavigationItem;
        private string _title = "Convenient Note";
        private string _workspaceName = "默认工作区";
        private bool _isNavigationExpanded;
        private bool _isInitialized;

        public MainWindowViewModel(
            IRegionManager regionManager,
            WorkspaceApplicationService workspaceApplicationService)
        {
            _regionManager = regionManager;
            _workspaceApplicationService = workspaceApplicationService;

            ToggleNavigationCommand = new DelegateCommand(ToggleNavigation);
            SelectNavigationCommand = new DelegateCommand<NavigationSection?>(section =>
            {
                if (section is { } targetSection)
                {
                    ActiveNavigationItem = NavigationItems.FirstOrDefault(item => item.Section == targetSection);
                }
            });

            NavigationItems.Add(new NavigationItemViewModel(NavigationSection.DayTodo, nameof(DayTodoView), "Day Todo", "今天要处理的待办", PackIconKind.CalendarToday));
            NavigationItems.Add(new NavigationItemViewModel(NavigationSection.Recent, nameof(RecentTodoView), "最近待办", "最近创建和更新", PackIconKind.History));
            NavigationItems.Add(new NavigationItemViewModel(NavigationSection.Schedule, nameof(ScheduleView), "日程概览", "按日期查看", PackIconKind.CalendarMonth));
            NavigationItems.Add(new NavigationItemViewModel(NavigationSection.Inbox, nameof(InboxView), "待办箱", "未完成事项", PackIconKind.Inbox));
            NavigationItems.Add(new NavigationItemViewModel(NavigationSection.Review, nameof(ReviewView), "数据复盘", "完成情况", PackIconKind.ChartLine));
            NavigationItems.Add(new NavigationItemViewModel(NavigationSection.Completed, nameof(CompletedTodoView), "已达成", "已完成事项", PackIconKind.CheckCircleOutline));
            NavigationItems.Add(new NavigationItemViewModel(NavigationSection.Trash, nameof(TrashView), "回收站", "删除的项目", PackIconKind.DeleteOutline));

            _activeNavigationItem = NavigationItems.First();
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

        public bool IsNavigationExpanded
        {
            get => _isNavigationExpanded;
            set => SetProperty(ref _isNavigationExpanded, value);
        }

        public NavigationItemViewModel? ActiveNavigationItem
        {
            get => _activeNavigationItem;
            set
            {
                if (SetProperty(ref _activeNavigationItem, value) && value is not null)
                {
                    NavigateTo(value);
                }
            }
        }

        public ObservableCollection<NavigationItemViewModel> NavigationItems { get; } = new();

        public DelegateCommand ToggleNavigationCommand { get; }

        public DelegateCommand<NavigationSection?> SelectNavigationCommand { get; }

        public async Task InitializeAsync()
        {
            if (_isInitialized)
            {
                return;
            }

            _isInitialized = true;

            var workspace = await _workspaceApplicationService.GetOrCreateDefaultWorkspaceAsync();
            WorkspaceName = workspace.Name;
            Title = $"{workspace.Name} - Convenient Note";

            if (ActiveNavigationItem is not null)
            {
                NavigateTo(ActiveNavigationItem);
            }
        }

        private void ToggleNavigation()
        {
            IsNavigationExpanded = !IsNavigationExpanded;
        }

        private void NavigateTo(NavigationItemViewModel navigationItem)
        {
            _regionManager.RequestNavigate(MainRegionName, navigationItem.ViewName);
            IsNavigationExpanded = false;
        }
    }
}
