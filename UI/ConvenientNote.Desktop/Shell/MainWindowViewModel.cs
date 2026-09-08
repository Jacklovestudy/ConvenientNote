using System.Collections.ObjectModel;
using ConvenientNote.Platform.Contracts;
using ConvenientNote.UI.Common;
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
        private readonly IWorkspaceContext _workspaceApplicationService;
        private NavigationItemViewModel? _activeNavigationItem;
        private string _title = "Convenient Note";
        private string _workspaceName = "默认工作区";
        private bool _isNavigationExpanded;
        private bool _isInitialized;

        public MainWindowViewModel(
            IRegionManager regionManager,
            IWorkspaceContext workspaceApplicationService, NavigationCatalog navigationCatalog)
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

            foreach (var item in navigationCatalog.Items) NavigationItems.Add(item);

            _activeNavigationItem = NavigationItems.First(item => item.Section == NavigationSection.Notes);
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

            var workspace = await _workspaceApplicationService.GetCurrentAsync();
            WorkspaceName = workspace.Name;
            Title = $"{workspace.Name} - Convenient Note";

            if (ActiveNavigationItem is not null)
            {
                NavigateTo(ActiveNavigationItem);
            }
        }

        public async Task ReloadWorkspaceIdentityAsync()
        {
            var workspace = await _workspaceApplicationService.GetCurrentAsync();
            WorkspaceName = workspace.Name;
            Title = $"{workspace.Name} - Convenient Note";
        }

        public void ReloadActiveNavigation()
        {
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
