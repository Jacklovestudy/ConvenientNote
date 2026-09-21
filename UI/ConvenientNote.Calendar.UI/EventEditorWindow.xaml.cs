using System.Windows;
using ConvenientNote.Calendar.Application;
using ConvenientNote.Calendar.Contracts;
using ConvenientNote.ViewModels;

namespace ConvenientNote.Views;
public partial class EventEditorWindow : Window
{
    private readonly CalendarApplicationService _service;
    private readonly CalendarEntry _entry;
    private readonly ItineraryDraftViewModel _draft;
    private bool _saving;
    public EventEditorWindow(CalendarApplicationService service, CalendarEntry entry)
    {
        InitializeComponent(); _service = service; _entry = entry;
        Editor.DataContext = _draft = new(new(entry.Title, entry.PlannedDate ?? entry.End!.Value.AddDays(-1), entry.End!.Value, entry.IsAllDay, entry.Details, entry.IsChecklist, entry.IsChecklist && entry.PlannedDate is null));
        BatchLabel.Text = entry.BatchId is null ? "编辑日期、时间和详细安排" : $"所属行程：{entry.BatchName} · 总说明和原文可在“导入记录”查看";
        Closing += (_, e) => { if (_saving) e.Cancel = true; };
    }
    private async void SaveClicked(object sender, RoutedEventArgs e)
    {
        if (_saving) return;
        try
        {
            var draft = _draft.ToDraft(); _saving = true; Save.IsEnabled = false; Editor.IsEnabled = false;
            await _service.EditEventAsync(_entry.Id, draft.Title, draft.Start, draft.End, draft.IsAllDay, draft.Details, isUnscheduled: draft.IsUnscheduled);
            _saving = false; DialogResult = true;
        }
        catch (Exception ex) { Error.Text = ex.Message; }
        finally { _saving = false; Save.IsEnabled = true; Editor.IsEnabled = true; }
    }
}
