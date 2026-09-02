using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;
using ConvenientNote.Services;
using MaterialDesignThemes.Wpf;
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
        private const double InitialTodoOffset = 32;
        private const double TodoHorizontalGap = 30;
        private const double TodoVerticalGap = 24;
        private const double MinimumArrangeViewportWidth = 600;

        private readonly WorkspaceApplicationService _workspaceApplicationService;
        private readonly OpenMeteoWeatherService _weatherService;
        private readonly string _boardKey;
        private readonly TodoBoardFilter _filter;
        private readonly List<CanvasTodoViewModel> _allTodoItems = new();
        private readonly HashSet<NoteId> _deletingTodoIds = new();
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
        private string _weatherText = "天气加载中";
        private string _weatherToolTip = "正在获取天气";
        private PackIconKind _weatherIconKind = PackIconKind.WeatherSunny;
        private bool _isWeatherLoading;
        private bool _hasLoadedWeather;
        private bool _isArrangingTodos;

        protected TodoBoardViewModel(
            WorkspaceApplicationService workspaceApplicationService,
            OpenMeteoWeatherService weatherService,
            string boardKey,
            TodoBoardFilter filter,
            string viewTitle,
            string viewDescription,
            bool canAddTodo)
        {
            _workspaceApplicationService = workspaceApplicationService;
            _weatherService = weatherService;
            _boardKey = boardKey;
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

        public string WeatherText
        {
            get => _weatherText;
            private set => SetProperty(ref _weatherText, value);
        }

        public string WeatherToolTip
        {
            get => _weatherToolTip;
            private set => SetProperty(ref _weatherToolTip, value);
        }

        public PackIconKind WeatherIconKind
        {
            get => _weatherIconKind;
            private set => SetProperty(ref _weatherIconKind, value);
        }

        public Visibility EmptyStateVisibility => TodoItems.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        public bool CanArrangeTodos => TodoItems.Count > 1 && !_isArrangingTodos;

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
            if (_deletingTodoIds.Contains(todo.Id) ||
                _currentWorkspaceId is not { } workspaceId)
            {
                return;
            }

            await _workspaceApplicationService.UpdateNoteTitleAsync(workspaceId, todo.Id, todo.Title);
        }

        public async Task CommitTodoContentAsync(CanvasTodoViewModel todo)
        {
            if (_deletingTodoIds.Contains(todo.Id) ||
                _currentWorkspaceId is not { } workspaceId)
            {
                return;
            }

            await _workspaceApplicationService.UpdateNoteContentAsync(workspaceId, todo.Id, todo.Content);
        }

        public async Task CommitTodoPriorityAsync(CanvasTodoViewModel todo)
        {
            if (_currentWorkspaceId is not { } workspaceId)
            {
                return;
            }

            await _workspaceApplicationService.SetNotePriorityAsync(workspaceId, todo.Id, todo.Priority);
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

        public async Task DeleteTodoAsync(CanvasTodoViewModel todo)
        {
            if (_currentWorkspaceId is not { } workspaceId ||
                !_deletingTodoIds.Add(todo.Id))
            {
                return;
            }

            try
            {
                await _workspaceApplicationService.DeleteNoteAsync(workspaceId, todo.Id);
            }
            catch (Exception ex)
            {
                _deletingTodoIds.Remove(todo.Id);
                Debug.WriteLine(ex);
                return;
            }

            await LoadWorkspaceAsync();
        }

        public async Task<bool> ArrangeTodosAsync(double viewportWidth)
        {
            if (!CanArrangeTodos || _currentWorkspaceId is not { } workspaceId)
            {
                return false;
            }

            _isArrangingTodos = true;
            RaisePropertyChanged(nameof(CanArrangeTodos));

            var originalPositions = TodoItems
                .Select(todo => new NotePositionUpdate(todo.Id, todo.X, todo.Y))
                .ToList();

            var maximumTodoWidth = TodoItems.Max(todo => todo.Width);
            var maximumTodoHeight = TodoItems.Max(todo => todo.Height);
            var rowHeight = maximumTodoHeight + TodoVerticalGap;
            var orderedTodos = TodoItems
                .OrderBy(todo => Math.Round(todo.Y / rowHeight))
                .ThenBy(todo => todo.X)
                .ThenBy(todo => todo.Y)
                .ToList();

            var usableViewportWidth = Math.Max(MinimumArrangeViewportWidth, viewportWidth);
            var columnWidth = maximumTodoWidth + TodoHorizontalGap;
            var columnCount = Math.Max(
                1,
                (int)Math.Floor(
                    (usableViewportWidth - (InitialTodoOffset * 2) + TodoHorizontalGap) /
                    columnWidth));

            var arrangedPositions = new List<NotePositionUpdate>(orderedTodos.Count);

            for (var index = 0; index < orderedTodos.Count; index++)
            {
                var todo = orderedTodos[index];
                var column = index % columnCount;
                var row = index / columnCount;
                var x = InitialTodoOffset + column * columnWidth;
                var y = InitialTodoOffset + row * rowHeight;

                todo.MoveTo(x, y);
                arrangedPositions.Add(new NotePositionUpdate(todo.Id, x, y));
            }

            RefreshBoardSize();

            try
            {
                await _workspaceApplicationService.MoveNotesAsync(
                    workspaceId,
                    arrangedPositions);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);

                foreach (var originalPosition in originalPositions)
                {
                    var todo = TodoItems.FirstOrDefault(
                        current => current.Id == originalPosition.NoteId);
                    todo?.MoveTo(originalPosition.X, originalPosition.Y);
                }

                RefreshBoardSize();
                return false;
            }
            finally
            {
                _isArrangingTodos = false;
                RaisePropertyChanged(nameof(CanArrangeTodos));
            }
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            _ = LoadWorkspaceAsync();

            if (!_hasLoadedWeather)
            {
                _hasLoadedWeather = true;
                _ = RefreshWeatherAsync();
            }
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
            var latestTodo = TodoItems.LastOrDefault();
            var x = latestTodo is null
                ? InitialTodoOffset
                : latestTodo.X + latestTodo.Width + TodoHorizontalGap;
            var y = latestTodo?.Y ?? InitialTodoOffset;
            var note = await _workspaceApplicationService.CreateNoteAsync(workspaceId, x, y, title, _boardKey);

            _allTodoItems.Add(CreateTodoViewModel(note));
            QuickAddTitle = string.Empty;
            RefreshVisibleTodos();
        }

        private async Task RefreshWeatherAsync()
        {
            if (_isWeatherLoading)
            {
                return;
            }

            _isWeatherLoading = true;
            WeatherText = "天气加载中";
            WeatherToolTip = "正在获取天气";

            try
            {
                var weather = await _weatherService.GetCurrentWeatherAsync();
                var locationName = GetCompactLocationName(weather.LocationName);
                var description = GetWeatherDescription(weather.WeatherCode);

                WeatherText = $"{locationName} · {description} {Math.Round(weather.TemperatureC):0}°";
                WeatherIconKind = GetWeatherIcon(weather.WeatherCode, weather.IsDay);
                WeatherToolTip =
                    $"{weather.LocationName}\n" +
                    $"{description}，{weather.TemperatureC:0.#}°C\n" +
                    $"体感 {weather.ApparentTemperatureC:0.#}°C，风速 {weather.WindSpeedKmh:0.#} km/h\n" +
                    $"更新时间 {weather.Time}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                WeatherText = "天气获取失败";
                WeatherIconKind = PackIconKind.WeatherPartlyCloudy;
                WeatherToolTip = "天气获取失败";
            }
            finally
            {
                _isWeatherLoading = false;
            }
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
            var boardTodos = _allTodoItems.Where(todo => todo.BoardKey == _boardKey);
            var visibleTodos = _filter switch
            {
                TodoBoardFilter.Active => boardTodos.Where(todo => !todo.IsCompleted),
                TodoBoardFilter.Completed => boardTodos.Where(todo => todo.IsCompleted),
                _ => boardTodos
            };

            TodoItems.Clear();
            foreach (var todo in visibleTodos.OrderBy(todo => todo.ZIndex))
            {
                TodoItems.Add(todo);
            }

            RefreshBoardSize();
            RefreshViewStatus();
            RaisePropertyChanged(nameof(CanArrangeTodos));
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

        private static PackIconKind GetWeatherIcon(int weatherCode, bool isDay)
        {
            return weatherCode switch
            {
                0 => isDay ? PackIconKind.WeatherSunny : PackIconKind.WeatherNight,
                >= 1 and <= 2 => isDay ? PackIconKind.WeatherPartlyCloudy : PackIconKind.WeatherNightPartlyCloudy,
                3 => PackIconKind.WeatherCloudy,
                45 or 48 => PackIconKind.WeatherFog,
                >= 51 and <= 57 => PackIconKind.WeatherRainy,
                >= 61 and <= 67 => PackIconKind.WeatherPouring,
                >= 71 and <= 77 => PackIconKind.WeatherSnowy,
                >= 80 and <= 82 => PackIconKind.WeatherPouring,
                >= 85 and <= 86 => PackIconKind.WeatherSnowyHeavy,
                >= 95 and <= 99 => PackIconKind.WeatherLightningRainy,
                _ => PackIconKind.WeatherPartlyCloudy
            };
        }

        private static string GetWeatherDescription(int weatherCode)
        {
            return weatherCode switch
            {
                0 => "晴",
                1 => "大部晴朗",
                2 => "局部多云",
                3 => "阴",
                45 or 48 => "雾",
                51 or 53 or 55 => "毛毛雨",
                56 or 57 => "冻毛毛雨",
                61 or 63 or 65 => "雨",
                66 or 67 => "冻雨",
                71 or 73 or 75 => "雪",
                77 => "雪粒",
                80 or 81 or 82 => "阵雨",
                85 or 86 => "阵雪",
                95 => "雷暴",
                96 or 99 => "雷暴伴冰雹",
                _ => "未知天气"
            };
        }

        private static string GetCompactLocationName(string locationName)
        {
            var firstPart = locationName
                .Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            return string.IsNullOrWhiteSpace(firstPart)
                ? "当前位置"
                : firstPart;
        }

    }
}
