using ConvenientNote.Tests.Compatibility;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConvenientNote.Application.Abstractions;
using ConvenientNote.Application.Workspaces;
using ConvenientNote.Domain.Notes;
using ConvenientNote.Domain.Workspaces;
using ConvenientNote.ViewModels;
using ConvenientNote.Views;
using Xunit;

namespace ConvenientNote.Tests.Calendar;

public sealed class CalendarPanelLayoutTests
{
    [Fact]
    public void Month_grid_scrolls_down_and_up_with_mouse_wheel()
    {
        RunSta(() =>
        {
            var panel = new CalendarPanel
            {
                Width = 1000, Height = 500,
                DataContext = new ScheduleViewModel(CalendarServiceFixture.Create(new WorkspaceApplicationService(new Repository())))
            };
            panel.Measure(new Size(1000, 500));
            panel.Arrange(new Rect(0, 0, 1000, 500));
            panel.UpdateLayout();
            var scroll = (ScrollViewer)panel.FindName("MonthScroll");
            Assert.True(scroll.ScrollableHeight > 0);
            var down = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -120)
                { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent };
            scroll.RaiseEvent(down);
            panel.UpdateLayout();
            Assert.True(down.Handled);
            Assert.True(scroll.VerticalOffset > 0);
            scroll.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, 120)
                { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent });
            panel.UpdateLayout();
            Assert.Equal(0, scroll.VerticalOffset);
            var vm = (ScheduleViewModel)panel.DataContext;
            var end = vm.Days[^1].Date;
            scroll.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -2400)
                { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent });
            panel.UpdateLayout();
            Assert.True(vm.Days[^1].Date > end);
            var monthItems = (ItemsControl)panel.FindName("MonthDays");
            var blocks = Descendants<StackPanel>(monthItems).Where(block => block.DataContext is CalendarMonthViewModel).ToArray();
            Assert.True(blocks.Length >= 2);
            var firstBounds = blocks[0].TransformToAncestor(monthItems).TransformBounds(new Rect(blocks[0].RenderSize));
            var secondBounds = blocks[1].TransformToAncestor(monthItems).TransformBounds(new Rect(blocks[1].RenderSize));
            Assert.True(secondBounds.Top - firstBounds.Bottom >= 16, "Month blocks must have a visible gap.");
        });
    }

    [Theory]
    [InlineData(360, 440)] // Minimum desktop window after its 40 DIP caption.
    [InlineData(420, 480)] // Default desktop window after its caption.
    public void Compact_calendar_keeps_lunar_labels_and_first_task_inside_viewport(double width, double height)
    {
        RunSta(() =>
        {
            var service = new WorkspaceApplicationService(new Repository());
            var workspace = service.GetOrCreateDefaultWorkspaceAsync().GetAwaiter().GetResult();
            service.CreateScheduledTodoAsync(workspace.Id, "可见的第一项待办", DateTime.Today).GetAwaiter().GetResult();
            var vm = new ScheduleViewModel(CalendarServiceFixture.Create(service));
            vm.RefreshAsync().GetAwaiter().GetResult();
            var panel = new CalendarPanel { IsCompact = true, DataContext = vm, Width = width, Height = height };
            panel.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse("""
                <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                    xmlns:m="http://materialdesigninxaml.net/winfx/xaml/themes">
                    <ResourceDictionary.MergedDictionaries>
                        <m:BundledTheme BaseTheme="Light" PrimaryColor="Indigo" SecondaryColor="Teal" />
                        <ResourceDictionary Source="pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml" />
                    </ResourceDictionary.MergedDictionaries>
                </ResourceDictionary>
                """));
            panel.Measure(new Size(width, height));
            panel.Arrange(new Rect(0, 0, width, height));
            panel.UpdateLayout();
            var cells = Descendants<Button>(panel).Where(b => b.DataContext is CalendarDayViewModel).ToArray();
            Assert.Equal(DateTime.DaysInMonth(vm.DisplayMonth.Year, vm.DisplayMonth.Month), cells.Length);
            foreach (var cell in cells)
            {
                Assert.True(cell.ActualHeight >= 32, $"Day cell too short: {cell.ActualHeight}");
                var day = (CalendarDayViewModel)cell.DataContext;
                var lunar = Descendants<TextBlock>(cell).Single(t => t.Text == day.LunarLabel);
                var bounds = lunar.TransformToAncestor(cell).TransformBounds(new Rect(lunar.RenderSize));
                Assert.True(bounds.Top >= 0 && bounds.Bottom <= cell.ActualHeight, $"Lunar label clipped: {bounds}, cell {cell.ActualHeight}");
            }
            var title = Descendants<TextBlock>((DependencyObject)panel.FindName("SelectedTaskItems")).Single(t => t.Text == "可见的第一项待办");
            var titleBounds = title.TransformToAncestor(panel).TransformBounds(new Rect(title.RenderSize));
            Assert.True(titleBounds.Bottom <= height, $"First task outside viewport: {titleBounds}");
            var scroll = Ancestor<ScrollViewer>(title);
            Assert.NotNull(scroll);
            var inScroll = title.TransformToAncestor(scroll!).TransformBounds(new Rect(title.RenderSize));
            Assert.True(inScroll.Bottom <= scroll!.ViewportHeight, $"First task clipped by details scroll: {inScroll}, viewport {scroll.ViewportHeight}");
            var checkBox = Descendants<CheckBox>(scroll!).First(c => c.DataContext is CalendarTaskViewModel);
            var checkBounds = checkBox.TransformToAncestor(scroll!).TransformBounds(new Rect(checkBox.RenderSize));
            Assert.True(checkBounds.Bottom <= scroll!.ViewportHeight, $"First completion checkbox clipped: {checkBounds}, viewport {scroll.ViewportHeight}");
        });
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static T? Ancestor<T>(DependencyObject child) where T : DependencyObject
    { for (var current = VisualTreeHelper.GetParent(child); current is not null; current = VisualTreeHelper.GetParent(current)) if (current is T result) return result; return null; }
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private sealed class Repository : IWorkspaceRepository
    {
        private Workspace? _workspace;
        public Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Workspace>>(_workspace is null ? [] : [_workspace]);
        public Task<Workspace?> GetAsync(WorkspaceId id, CancellationToken cancellationToken = default) => Task.FromResult(_workspace);
        public Task SaveAsync(Workspace workspace, CancellationToken cancellationToken = default) { _workspace = workspace; return Task.CompletedTask; }
        public Task DeleteAsync(WorkspaceId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReplaceActiveNotesAsync(WorkspaceId id, IReadOnlyCollection<Note> notes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
