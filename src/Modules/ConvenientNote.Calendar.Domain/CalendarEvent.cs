namespace ConvenientNote.Calendar.Domain;

public sealed class CalendarEvent
{
    public Guid Id { get; private set; }
    public string Title { get; private set; } = "";
    public DateTime Start { get; private set; }
    public DateTime End { get; private set; }
    public bool IsAllDay { get; private set; }
    public bool IsCompleted { get; private set; }
    public string Details { get; private set; } = "";
    public Guid? BatchId { get; private set; }
    public string BatchName { get; private set; } = "";
    public bool IsChecklist { get; private set; }
    public bool IsUnscheduled { get; private set; }
    public static CalendarEvent Create(string title, DateTime start, DateTime end, bool isAllDay)
        => Restore(Guid.NewGuid(), title, start, end, isAllDay, false);

    public static CalendarEvent Restore(Guid id, string title, DateTime start, DateTime end, bool isAllDay, bool isCompleted,
        string details = "", Guid? batchId = null, string batchName = "", bool isChecklist = false, bool isUnscheduled = false)
    {
        if (id == Guid.Empty) throw new ArgumentException("日程标识不能为空。");
        if (isUnscheduled && !isChecklist) throw new ArgumentException("只有核对事项可以不指定日期。");
        var normalizedTitle = title?.Trim() ?? "";
        if (normalizedTitle.Length is 0 or > 80) throw new ArgumentException("日程标题应为 1 至 80 个字符。");
        if (end <= start) throw new ArgumentException("结束时间必须晚于开始时间。");
        if (isAllDay && (start.TimeOfDay != TimeSpan.Zero || end.TimeOfDay != TimeSpan.Zero))
            throw new ArgumentException("全天日程必须使用完整日期。");
        return new() { Id = id, Title = normalizedTitle, Start = DateTime.SpecifyKind(start, DateTimeKind.Unspecified),
            End = DateTime.SpecifyKind(end, DateTimeKind.Unspecified), IsAllDay = isAllDay, IsCompleted = isCompleted,
            Details = details, BatchId = batchId, BatchName = batchName, IsChecklist = isChecklist, IsUnscheduled = isUnscheduled };
    }

    public void Edit(string title, DateTime start, DateTime end, bool isAllDay, string details, bool? isUnscheduled = null)
    {
        var validated = Restore(Id, title, start, end, isAllDay, IsCompleted, details, BatchId, BatchName, IsChecklist, isUnscheduled ?? IsUnscheduled);
        Title = validated.Title; Start = validated.Start; End = validated.End;
        IsAllDay = validated.IsAllDay; Details = validated.Details;
        IsUnscheduled = validated.IsUnscheduled;
    }

    public void Reschedule(DateTime? date)
    {
        if (date is null)
        {
            if (!IsChecklist) throw new ArgumentException("日程需要日期；核对事项可以移回待安排。");
            IsUnscheduled = true;
            return;
        }
        var duration = End - Start;
        var start = date.Value.Date.Add(Start.TimeOfDay);
        var end = start.Add(duration);
        Start = start;
        End = end;
        IsUnscheduled = false;
    }

    public void SetCompletion(bool value) => IsCompleted = value;
}
