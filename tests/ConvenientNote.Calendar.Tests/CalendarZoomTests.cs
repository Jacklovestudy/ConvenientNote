using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConvenientNote.Calendar.Application;
using ConvenientNote.Calendar.Domain;
using ConvenientNote.Platform.Contracts;
using ConvenientNote.ViewModels;
using ConvenientNote.Views;
using Xunit;

namespace ConvenientNote.Calendar.Tests;
public sealed class CalendarZoomTests
{
    private sealed class Context : IWorkspaceContext
    {
        public Task<WorkspaceInfo> GetCurrentAsync(CancellationToken ct = default) => Task.FromResult(new WorkspaceInfo(Guid.NewGuid(), "test"));
    }
    private sealed class Repository : ICalendarRepository
    {
        public Task<IReadOnlyList<CalendarEvent>> ListAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CalendarEvent>>([CalendarEvent.Create("旅行", new(2026, 9, 24), new(2026, 9, 25), true)]);
        public Task SaveAsync(Guid id, CalendarEvent item, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid workspace, Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }
    [Fact]
    public void ControlWheelZoomsWithinBoundsWithoutResizingDetailsAndScheduledDaysHaveOutline()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var service = new CalendarApplicationService(new Repository(), new Context());
                using var vm = new ScheduleViewModel(service);
                vm.RefreshAsync().GetAwaiter().GetResult(); vm.NavigateToDate(new(2026, 9, 23));
                var panel = new CalendarPanel { DataContext = vm };
                void Layout() { panel.Measure(new Size(1200, 650)); panel.Arrange(new Rect(0, 0, 1200, 650)); panel.UpdateLayout(); }
                Layout();
                var detailsWidth = ((FrameworkElement)panel.FindName("Details")).ActualWidth;
                var day = Descendants<Button>(panel).Single(b => b.DataContext is CalendarDayViewModel d && d.Date == new DateTime(2026, 9, 24));
                var empty = Descendants<Button>(panel).Single(b => b.DataContext is CalendarDayViewModel d && d.Date == new DateTime(2026, 9, 22));
                Assert.True(day.BorderThickness.Left > empty.BorderThickness.Left);
                var originalBounds = day.TransformToAncestor(panel).TransformBounds(new Rect(day.RenderSize));
                panel.HandleCalendarWheel(120, ModifierKeys.Control, 200); Layout();
                Assert.Equal(1.1, panel.ZoomFactor, 6);
                var zoomedBounds = day.TransformToAncestor(panel).TransformBounds(new Rect(day.RenderSize));
                Assert.Equal(originalBounds.Height * 1.1, zoomedBounds.Height, 3);
                Assert.Equal(20, ((ScrollViewer)panel.FindName("MonthScroll")).VerticalOffset, 3);
                Assert.Equal(detailsWidth, ((FrameworkElement)panel.FindName("Details")).ActualWidth);
                panel.HandleCalendarWheel(-120, ModifierKeys.None, 200); Layout();
                Assert.Equal(1.1, panel.ZoomFactor, 6);
                for (var i = 0; i < 30; i++) panel.HandleCalendarWheel(120, ModifierKeys.Control, 200);
                Assert.Equal(2, panel.ZoomFactor);
                for (var i = 0; i < 40; i++) panel.HandleCalendarWheel(-120, ModifierKeys.Control, 200);
                Assert.Equal(0.6, panel.ZoomFactor);
                Assert.Equal(new DateTime(2026, 9, 23), vm.SelectedDate);
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T item) yield return item;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
