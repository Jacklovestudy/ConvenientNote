using System.Globalization;
using ConvenientNote.Calendar.Application;
using Prism.Mvvm;

namespace ConvenientNote.ViewModels;

public sealed class ItineraryDraftViewModel : BindableBase
{
    public ItineraryDraftViewModel(ItineraryDraft draft)
    {
        Title = draft.Title; StartDate = draft.Start.Date;
        EndDate = draft.IsAllDay ? draft.End.AddDays(-1).Date : draft.End.Date;
        StartTime = draft.Start.ToString("HH:mm"); EndTime = draft.End.ToString("HH:mm");
        IsAllDay = draft.IsAllDay; Details = draft.Details; IsChecklist = draft.IsChecklist;
        IsUnscheduled = draft.IsUnscheduled;
    }
    private string _title = "", _details = "", _startTime = "", _endTime = "";
    private bool _included = true, _allDay, _unscheduled;
    private DateTime? _startDate, _endDate;
    public string Title { get => _title; set => SetProperty(ref _title, value); }
    public string Details { get => _details; set => SetProperty(ref _details, value); }
    public bool Included { get => _included; set => SetProperty(ref _included, value); }
    public bool IsAllDay { get => _allDay; set { SetProperty(ref _allDay, value); RaisePropertyChanged(nameof(Kind)); } }
    public bool IsChecklist { get; }
    public bool IsUnscheduled { get => _unscheduled; set { SetProperty(ref _unscheduled, value); RaisePropertyChanged(nameof(DateLabel)); } }
    public string DateLabel => IsUnscheduled ? "待安排" : StartDate?.ToString("MM-dd") ?? "";
    public string Kind => IsChecklist ? "核对事项" : IsAllDay ? "全天日程" : "定时日程";
    public DateTime? StartDate { get => _startDate; set { SetProperty(ref _startDate, value); RaisePropertyChanged(nameof(DateLabel)); } }
    public DateTime? EndDate { get => _endDate; set => SetProperty(ref _endDate, value); }
    public string StartTime { get => _startTime; set => SetProperty(ref _startTime, value); }
    public string EndTime { get => _endTime; set => SetProperty(ref _endTime, value); }
    public ItineraryDraft ToDraft()
    {
        if (IsChecklist && IsUnscheduled)
        {
            if (string.IsNullOrWhiteSpace(Title) || Title.Trim().Length > 80) throw new ArgumentException("标题须为1至80字。");
            var placeholder = (StartDate ?? DateTime.Today).Date;
            return new(Title.Trim(), placeholder, placeholder.AddDays(1), true, Details, true, true);
        }
        if (StartDate is not { } start || EndDate is not { } end)
            throw new ArgumentException("请填写开始和结束日期。");
        start = start.Date; end = end.Date;
        if (start.Year < 1902 || end.Year > 2199) throw new ArgumentException("日期须在1902年至2199年之间。");
        if (IsAllDay) end = end.AddDays(1);
        else
        {
            if (!TimeOnly.TryParseExact(StartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
                || !TimeOnly.TryParseExact(EndTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
                throw new ArgumentException("时间请填写为 HH:mm，例如09:30。");
            start = start.Add(from.ToTimeSpan()); end = end.Add(to.ToTimeSpan());
        }
        if (end <= start) throw new ArgumentException("结束时间必须晚于开始时间；跨午夜请调整结束日期。");
        if (string.IsNullOrWhiteSpace(Title) || Title.Trim().Length > 80) throw new ArgumentException("标题须为1至80字。");
        return new(Title.Trim(), start, end, IsAllDay, Details, IsChecklist);
    }
}
