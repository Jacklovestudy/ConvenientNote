using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ConvenientNote.Calendar.Application;
using ConvenientNote.ViewModels;

namespace ConvenientNote.Views;

public partial class ItineraryImportWindow : Window
{
    private readonly CalendarApplicationService _service;
    private ItineraryPreview? _preview;
    private ItineraryDraftViewModel[] _drafts = [];
    private bool _busy;
    private readonly Guid? _initialBatch;
    public DateTime? NavigateDate { get; private set; }
    public ItineraryImportWindow(CalendarApplicationService service, bool history = false, Guid? batchId = null)
    {
        InitializeComponent(); _service = service; _initialBatch = batchId;
        YearInput.Text = DateTime.Today.Year.ToString();
        SourceInput.TextChanged += SourceChanged;
        YearInput.TextChanged += SourceChanged;
        if (history) Tabs.SelectedItem = HistoryTab;
        Closing += (_, e) => { if (_busy) e.Cancel = true; };
    }
    private void SourceChanged(object sender, TextChangedEventArgs e)
    {
        if (_preview is null) return;
        _preview = null; PreviewTab.IsEnabled = false;
        Status.Text = "原文或年份已修改，请重新解析预览。";
    }
    private async void WindowLoaded(object sender, RoutedEventArgs e)
        => await RunAsync(async () => { await LoadHistoryAsync(); if (_initialBatch is { } id) BatchList.SelectedItem = BatchList.Items.Cast<CalendarImportBatch>().FirstOrDefault(b => b.Id == id); });

    private void ParseClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(YearInput.Text, out var year)) throw new ArgumentException("请填写四位年份。");
            var preview = ItineraryParser.Parse(SourceInput.Text, year);
            foreach (var draft in _drafts) draft.PropertyChanged -= DraftChanged;
            _preview = preview; _drafts = preview.Items.Select(d => new ItineraryDraftViewModel(d)).ToArray();
            foreach (var draft in _drafts) draft.PropertyChanged += DraftChanged;
            NameInput.Text = preview.Name; DraftGrid.ItemsSource = _drafts; DraftGrid.SelectedIndex = 0;
            PreviewTab.IsEnabled = true; Tabs.SelectedItem = PreviewTab; Status.Text = ""; UpdateCount();
        }
        catch (Exception ex) { Status.Text = ex.Message; PreviewTab.IsEnabled = false; }
    }
    private void DraftChanged(object? sender, PropertyChangedEventArgs e) => UpdateCount();
    private void UpdateCount()
    {
        var selected = _drafts.Where(d => d.Included).ToArray();
        CountLabel.Text = $"已选 {selected.Length} 项：{selected.Count(d => !d.IsChecklist && d.IsAllDay)} 个全天日程、{selected.Count(d => !d.IsAllDay && !d.IsChecklist)} 个定时日程、{selected.Count(d => d.IsChecklist)} 个核对事项";
    }
    private void DraftSelected(object sender, SelectionChangedEventArgs e) => DraftEditor.DataContext = DraftGrid.SelectedItem;
    private async void ImportClicked(object sender, RoutedEventArgs e)
        => await RunAsync(async () =>
        {
            if (_preview is null) return;
            DraftGrid.CommitEdit(DataGridEditingUnit.Cell, true); DraftGrid.CommitEdit(DataGridEditingUnit.Row, true);
            var items = _drafts.Where(d => d.Included).Select(d => d.ToDraft()).ToArray();
            if (items.Length == 0) throw new ArgumentException("请至少勾选一项安排。");
            await _service.ImportAsync(_preview with { Name = NameInput.Text, Items = items });
            NavigateDate = items.Where(d => !d.IsUnscheduled).Select(d => (DateTime?)d.Start.Date).Min();
            _busy = false; DialogResult = true;
        });
    private async Task LoadHistoryAsync()
    {
        BatchList.ItemsSource = await _service.ListBatchesAsync();
        BatchList.SelectedIndex = BatchList.Items.Count > 0 ? 0 : -1;
        if (BatchList.Items.Count == 0) OverviewText.Text = "还没有导入记录。可以从“粘贴行程”开始。";
    }
    private void BatchSelected(object sender, SelectionChangedEventArgs e)
    {
        var batch = BatchList.SelectedItem as CalendarImportBatch;
        OverviewText.Text = batch is null ? "" : string.IsNullOrWhiteSpace(batch.Overview) ? "没有单独的总说明，可查看完整原文。" : batch.Overview;
        OriginalText.Text = batch?.SourceText ?? "";
    }
    private async void UndoClicked(object sender, RoutedEventArgs e)
    {
        if (BatchList.SelectedItem is not CalendarImportBatch batch) { Status.Text = "请先选择一个批次。"; return; }
        if (MessageBox.Show(this, $"撤销“{batch.Name}”？\n本批次的日程和核对事项将删除，包括导入后编辑的内容。其他日程保留。", "撤销导入", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await RunAsync(async () => { await _service.UndoImportAsync(batch.Id); await LoadHistoryAsync(); Status.Text = "已撤销所选批次。"; });
    }
    private async void LocateClicked(object sender, RoutedEventArgs e)
        => await RunAsync(async () =>
        {
            if (BatchList.SelectedItem is not CalendarImportBatch batch) throw new ArgumentException("请先选择一个批次。");
            NavigateDate = (await _service.ListAsync()).Where(i => i.BatchId == batch.Id).Select(i => i.PlannedDate).Min();
            if (NavigateDate is null) throw new InvalidOperationException("这份行程没有已安排日期的项目；核对事项可在日历的待安排中查看。");
            _busy = false; DialogResult = true;
        });
    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true; Tabs.IsEnabled = false; Status.Text = "正在处理…";
        try { await action(); if (Status.Text == "正在处理…") Status.Text = ""; }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { _busy = false; Tabs.IsEnabled = true; }
    }
}
