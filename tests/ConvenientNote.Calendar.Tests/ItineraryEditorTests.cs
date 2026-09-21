using ConvenientNote.ViewModels;
using ConvenientNote.Calendar.Application;
using ConvenientNote.Calendar.Domain;
using ConvenientNote.Platform.Contracts;
using Xunit;

namespace ConvenientNote.Calendar.Tests;

public sealed class ItineraryEditorTests
{
    [Fact]
    public void UnscheduledChecklistCanBeEditedWithoutAssigningItsPlaceholderDate()
    {
        var editor = new ItineraryDraftViewModel(new("核对", new(2026, 9, 24), new(2026, 9, 25), true, "未指定日期", true, true));
        Assert.Equal("待安排", editor.DateLabel);
        Assert.True(editor.ToDraft().IsUnscheduled);
        editor.IsUnscheduled = false;
        Assert.Equal("09-24", editor.DateLabel);
        Assert.False(editor.ToDraft().IsUnscheduled);
    }
    private sealed class Context : IWorkspaceContext
    {
        public Task<WorkspaceInfo> GetCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceInfo(Guid.NewGuid(), "test"));
    }
    private sealed class Repository : ICalendarRepository
    {
        public Task<IReadOnlyList<CalendarEvent>> ListAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CalendarEvent>>([]);
        public Task SaveAsync(Guid id, CalendarEvent item, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid workspaceId, Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }
    [Fact]
    public void LocatingAlreadyLoadedDateResetsScrollAndKeepsOnlyTargetMonth()
    {
        using var service = new CalendarApplicationService(new Repository(), new Context());
        using var model = new ScheduleViewModel(service);
        var date = model.SelectedDate;
        model.ExtendCalendar(false); model.ExtendCalendar(false);
        var resets = 0; model.CalendarPositionReset += (_, _) => resets++;
        model.NavigateToDate(date);
        Assert.Equal(1, resets); Assert.Single(model.Months);
        model.NavigateToDate(new DateTime(2026, 9, 24));
        Assert.Equal(new DateTime(2026, 9, 24), model.SelectedDate);
        Assert.Equal(new DateTime(2026, 9, 1), Assert.Single(model.Months).Month);
    }
    [Fact]
    public void OvernightTransportRetainsEndDateAndEditedDetails()
    {
        var editor = new ItineraryDraftViewModel(new("航班", new(2026, 9, 24, 21, 20, 0), new(2026, 9, 25, 0, 15, 0), false, "酒店"));
        editor.Details = "接机已确认";
        var result = editor.ToDraft();
        Assert.Equal(new DateTime(2026, 9, 25, 0, 15, 0), result.End);
        Assert.Equal("接机已确认", result.Details);
        editor.EndDate = new DateTime(2026, 9, 24);
        Assert.Throws<ArgumentException>(() => editor.ToDraft());
    }

    [Fact]
    public void AllDayEndDateIsInclusiveInEditorAndExclusiveInStorage()
    {
        var editor = new ItineraryDraftViewModel(new("住宿", new(2026, 10, 3), new(2026, 10, 6), true, ""));
        Assert.Equal(new DateTime(2026, 10, 5), editor.EndDate);
        Assert.Equal(new DateTime(2026, 10, 6), editor.ToDraft().End);
    }
}
