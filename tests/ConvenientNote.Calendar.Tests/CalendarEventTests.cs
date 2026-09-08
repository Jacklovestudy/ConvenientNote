using ConvenientNote.Calendar.Domain;
using Xunit;

namespace ConvenientNote.Calendar.Tests;

public sealed class CalendarEventTests
{
    [Fact]
    public void EndMustFollowStart()
    {
        var start = new DateTime(2026, 9, 8, 10, 0, 0);
        Assert.Throws<ArgumentException>(() => CalendarEvent.Create("会议", start, start, false));
        Assert.Throws<ArgumentException>(() => CalendarEvent.Create("会议", start, start.AddMinutes(-1), false));
    }

    [Fact]
    public void ReschedulingRetainsDurationAndIdentity()
    {
        var start = new DateTime(2026, 9, 8, 10, 0, 0);
        var item = CalendarEvent.Create("  会议  ", start, start.AddHours(2), false);
        var id = item.Id;
        item.Reschedule(new DateTime(2026, 9, 11));
        Assert.Equal(id, item.Id);
        Assert.Equal("会议", item.Title);
        Assert.Equal(new DateTime(2026, 9, 11, 10, 0, 0), item.Start);
        Assert.Equal(new DateTime(2026, 9, 11, 12, 0, 0), item.End);
    }
}
