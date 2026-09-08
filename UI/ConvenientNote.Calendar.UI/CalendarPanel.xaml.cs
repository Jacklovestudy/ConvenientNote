using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConvenientNote.ViewModels;

namespace ConvenientNote.Views;

public partial class CalendarPanel : UserControl
{
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(CalendarPanel), new PropertyMetadata(false, OnCompactChanged));
    public bool IsCompact { get => (bool)GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }
    private Point _dragStart;
    private CalendarTaskViewModel? _dragTask;
    private readonly HashSet<CheckBox> _restoringCompletion = [];
    public CalendarPanel() => InitializeComponent();
    private static void OnCompactChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((CalendarPanel)d).UpdateLayoutMode();
    private ScheduleViewModel? _calendarModel;
    private bool _adjustingScroll;
    private double CalendarRowHeight => IsCompact ? 38 : 100;
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateLayoutMode();
        if (DataContext is ScheduleViewModel vm)
        {
            if (_calendarModel is not null) _calendarModel.CalendarPositionReset -= ResetScroll;
            _calendarModel = vm;
            vm.CalendarPositionReset += ResetScroll;
            await vm.RefreshAsync();
        }
    }
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_calendarModel is not null) _calendarModel.CalendarPositionReset -= ResetScroll;
        _calendarModel = null;
    }
    private void ResetScroll(object? sender, EventArgs e) => SetCalendarOffset(0);
    private void SetCalendarOffset(double offset)
    {
        _adjustingScroll = true;
        MonthScroll.ScrollToVerticalOffset(Math.Max(0, offset));
        MonthScroll.UpdateLayout();
        _adjustingScroll = false;
    }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateLayoutMode();
    private void MonthScroll_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta == 0) return;
        var lines = SystemParameters.WheelScrollLines;
        if (lines == 0) return;
        var distance = lines < 0 ? MonthScroll.ViewportHeight : lines * 16d;
        var offset = MonthScroll.VerticalOffset - e.Delta / 120d * distance;
        if (DataContext is ScheduleViewModel vm && (offset < 0 || offset > MonthScroll.ScrollableHeight))
        {
            _adjustingScroll = true;
            offset += vm.ExtendCalendar(offset < 0, CalendarRowHeight);
            MonthScroll.UpdateLayout();
            _adjustingScroll = false;
        }
        SetCalendarOffset(offset);
        UpdateVisibleMonth();
        e.Handled = true;
    }
    private void MonthScroll_Changed(object sender, ScrollChangedEventArgs e)
    {
        if (_adjustingScroll || e.VerticalChange == 0 || DataContext is not ScheduleViewModel vm) return;
        var offset = MonthScroll.VerticalOffset;
        if ((e.VerticalChange > 0 && offset >= MonthScroll.ScrollableHeight - 1)
            || (e.VerticalChange < 0 && offset <= 1))
        {
            _adjustingScroll = true;
            offset += vm.ExtendCalendar(e.VerticalChange < 0, CalendarRowHeight);
            MonthScroll.UpdateLayout();
            SetCalendarOffset(offset);
        }
        UpdateVisibleMonth();
    }
    private void UpdateVisibleMonth()
    {
        if (DataContext is not ScheduleViewModel vm || vm.Days.Count == 0) return;
        var offset = MonthScroll.VerticalOffset;
        foreach (var month in vm.Months)
        {
            var height = month.RowCount * CalendarRowHeight + 40;
            if (offset < height) { vm.UpdateVisibleMonth(month.Month); return; }
            offset -= height;
        }
    }
    private async void CompletionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { IsLoaded: true, DataContext: CalendarTaskViewModel task } checkBox
            || _restoringCompletion.Contains(checkBox) || checkBox.IsChecked == task.IsCompleted
            || DataContext is not ScheduleViewModel vm) return;
        if (vm.IsBusy) { RestoreCompletion(checkBox, task.IsCompleted); return; }

        await vm.ToggleCompletionAsync(task);
        // Successful refresh replaces the row. On a failed save the original row remains;
        // restore that row's persisted state without recursively submitting another write.
        var current = vm.SelectedTasks.FirstOrDefault(t => t.Id == task.Id);
        if (checkBox.IsLoaded && current is not null && ReferenceEquals(checkBox.DataContext, current))
            RestoreCompletion(checkBox, current.IsCompleted);
    }
    private void RestoreCompletion(CheckBox checkBox, bool value)
    {
        _restoringCompletion.Add(checkBox);
        try { checkBox.SetCurrentValue(CheckBox.IsCheckedProperty, value); }
        finally { _restoringCompletion.Remove(checkBox); }
    }
    private void UpdateLayoutMode()
    {
        if (ContentGrid is null) return;
        var stacked = IsCompact || ActualWidth < 760;
        DetailsColumn.Width = stacked ? new GridLength(0) : new GridLength(300);
        Grid.SetColumn(Details, stacked ? 0 : 1);
        Grid.SetRow(Details, stacked ? 1 : 0);
        MonthRow.Height = IsCompact ? new GridLength(270) : stacked ? new GridLength(1.4, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
        DetailsRow.Height = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        MonthGrid.Margin = stacked ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 16, 0);
        OuterGrid.Margin = new Thickness(IsCompact ? 8 : 14);
        Details.Padding = new Thickness(IsCompact ? 6 : 14);
        FullHeader.Visibility = IsCompact ? Visibility.Collapsed : Visibility.Visible;
        CompactHeader.Visibility = IsCompact ? Visibility.Visible : Visibility.Collapsed;
        HolidayFooter.Visibility = IsCompact ? Visibility.Collapsed : Visibility.Visible;
        Footer.Margin = new Thickness(0, IsCompact ? 0 : 8, 0, 0);
        SelectedTitle.FontSize = IsCompact ? 14 : 17;
        SelectedSummary.FontSize = IsCompact ? 10 : 12;
        SelectedSummary.Visibility = IsCompact ? Visibility.Collapsed : Visibility.Visible;
        SelectedSummary.Margin = IsCompact ? new Thickness(0, 2, 0, 4) : new Thickness(0, 6, 0, 12);
        NewTaskInput.MinHeight = IsCompact ? 30 : 32;
        NewTaskInput.Height = IsCompact ? 30 : double.NaN;
        SelectedTaskItems.Margin = new Thickness(0, IsCompact ? 2 : 6, 0, 0);
    }
    private void TaskMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragTask = null;
        if (e.OriginalSource is not DependencyObject source) return;
        while (source is not null && source != sender)
        {
            if (source is TextBox or Button or CheckBox or DatePicker) { _dragTask = null; return; }
            if (source is FrameworkElement element && element.DataContext is CalendarTaskViewModel task) _dragTask = task;
            source = source is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(source);
        }
    }
    private void TaskMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragTask is null) return;
        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var task = _dragTask; _dragTask = null;
        DragDrop.DoDragDrop(this, new DataObject(typeof(CalendarTaskViewModel), task), DragDropEffects.Move);
    }
    private void DayDragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(typeof(CalendarTaskViewModel)) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; }
    private async void DayDrop(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CalendarDayViewModel day } && e.Data.GetData(typeof(CalendarTaskViewModel)) is CalendarTaskViewModel task && DataContext is ScheduleViewModel vm)
        { await vm.RescheduleAsync(task, day.Date); e.Handled = true; }
    }
}
