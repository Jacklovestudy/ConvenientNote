using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConvenientNote.Calendar.Application;
using ConvenientNote.Calendar.Infrastructure;
using ConvenientNote.Calendar.Domain;
using ConvenientNote.Platform.Contracts;
using ConvenientNote.ViewModels;
using ConvenientNote.Views;
using Xunit;

namespace ConvenientNote.Calendar.Tests;

public sealed class ItineraryWindowTests
{
    private sealed class DisplayRepository : ICalendarRepository
    {
        public Task<IReadOnlyList<CalendarEvent>> ListAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CalendarEvent>>(ItineraryParser.Parse(YunnanItineraryFixture.Text, 2026).Items
                .Select(d => CalendarEvent.Restore(Guid.NewGuid(), d.Title, d.Start, d.End, d.IsAllDay, false, d.Details, null, "", d.IsChecklist, d.IsUnscheduled)).ToArray());
        public Task SaveAsync(Guid id, CalendarEvent item, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid workspaceId, Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class Context : IWorkspaceContext
    {
        public Task<WorkspaceInfo> GetCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceInfo(Guid.NewGuid(), "ui test"));
    }

    [Fact]
    public void PreviewBindsSelectedDetailsAndInvalidatesWhenSourceChanges()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var service = new CalendarApplicationService(new SqliteCalendarRepository(Path.Combine(Path.GetTempPath(), "unused-calendar-ui.db")), new Context());
                var window = new ItineraryImportWindow(service);
                window.Resources.MergedDictionaries.Add(new MaterialDesignThemes.Wpf.BundledTheme
                {
                    BaseTheme = MaterialDesignThemes.Wpf.BaseTheme.Light,
                    PrimaryColor = MaterialDesignColors.PrimaryColor.Indigo,
                    SecondaryColor = MaterialDesignColors.SecondaryColor.Teal
                });
                window.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml")
                });
                var source = (TextBox)window.FindName("SourceInput");
                source.Text = "云南旅行计划｜2026.9.24—10.7\n【9月24日 周四｜下班后出发】\n21:20 南京 → 丽江，祥鹏航空8L9812。\n次日00:15到达丽江。\n住宿：竹海间民宿，提前确认接机。\n【9月25日 周五｜丽江休息】\n睡到自然醒。\n【出发前最后核对】\n□ 确认接机。";
                ((Button)window.FindName("ParseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var grid = (DataGrid)window.FindName("DraftGrid");
                Assert.Equal(4, grid.Items.Count);
                grid.SelectedIndex = 1;
                var editor = (ItineraryDraftEditor)window.FindName("DraftEditor");
                var model = Assert.IsType<ItineraryDraftViewModel>(editor.DataContext);
                Assert.Equal(new DateTime(2026, 9, 25), model.EndDate);
                model.Included = false;
                Assert.Contains("已选 3 项", ((TextBlock)window.FindName("CountLabel")).Text);
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1016, 650)); content.Arrange(new Rect(0, 0, 1016, 650)); content.UpdateLayout();
                Assert.True(editor.ActualWidth > 300);
                var headers = Descendants<DataGridColumnHeader>(grid).Where(h => h.Content is string).ToArray();
                Assert.Equal(4, headers.Length);
                foreach (var header in headers)
                {
                    var text = Descendants<TextBlock>(header).First(t => t.Text == (string)header.Content);
                    var bounds = text.TransformToAncestor(header).TransformBounds(new Rect(text.RenderSize));
                    Assert.True(bounds.Top >= 0 && bounds.Bottom <= header.ActualHeight + 0.5,
                        $"Header {header.Content}: text bounds {bounds}, header height {header.ActualHeight}, padding {header.Padding}");
                    Assert.True(header.ActualHeight >= text.ActualHeight + header.Padding.Top + header.Padding.Bottom,
                        $"Header {header.Content} clips its themed content: height={header.ActualHeight}, text={text.ActualHeight}, padding={header.Padding}");
                }
                var bitmap = new RenderTargetBitmap(1016, 650, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(AppContext.BaseDirectory, "itinerary-preview.png"))) encoder.Save(file);
                source.Text += "\n补充说明";
                Assert.False(((TabItem)window.FindName("PreviewTab")).IsEnabled);
                using var displayService = new CalendarApplicationService(new DisplayRepository(), new Context());
                using var calendarModel = new ScheduleViewModel(displayService);
                calendarModel.RefreshAsync().GetAwaiter().GetResult();
                calendarModel.NavigateToDate(new DateTime(2026, 9, 25));
                var panel = new CalendarPanel { DataContext = calendarModel };
                panel.Resources.MergedDictionaries.Add(window.Resources);
                panel.Measure(new Size(1200, 800)); panel.Arrange(new Rect(0, 0, 1200, 800)); panel.UpdateLayout();
                Assert.Contains(Descendants<TextBlock>(panel), t => t.Text == "当晚住宿" && t.ActualHeight > 0);
                Assert.Contains(Descendants<TextBlock>(panel), t => t.Text.StartsWith("住 · 心花路放") && t.ActualHeight > 0);
                var calendarBitmap = new RenderTargetBitmap(1200, 800, 96, 96, PixelFormats.Pbgra32);
                calendarBitmap.Render(panel);
                var calendarEncoder = new PngBitmapEncoder(); calendarEncoder.Frames.Add(BitmapFrame.Create(calendarBitmap));
                using (var file = File.Create(Path.Combine(AppContext.BaseDirectory, "calendar-accommodation.png"))) calendarEncoder.Save(file);
                panel.HandleCalendarWheel(240, System.Windows.Input.ModifierKeys.Control, 300);
                panel.UpdateLayout();
                var zoomBitmap = new RenderTargetBitmap(1200, 800, 96, 96, PixelFormats.Pbgra32);
                zoomBitmap.Render(panel);
                var zoomEncoder = new PngBitmapEncoder(); zoomEncoder.Frames.Add(BitmapFrame.Create(zoomBitmap));
                using (var file = File.Create(Path.Combine(AppContext.BaseDirectory, "calendar-zoom.png"))) zoomEncoder.Save(file);
                window.Close();
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


