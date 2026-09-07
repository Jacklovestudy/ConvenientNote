using System;
using System.Linq;
using ConvenientNote.Features.Calendar;
using Xunit;

namespace ConvenientNote.Tests.Calendar;

public sealed class CalendarDateTests
{
    [Theory]
    [InlineData(2024, 2, 2024, 1, 29)]
    [InlineData(2026, 1, 2025, 12, 29)]
    [InlineData(2026, 12, 2026, 11, 30)]
    public void Month_grid_is_six_complete_Monday_weeks(int year, int month, int y, int m, int d)
    {
        var dates = CalendarDateInfo.MonthDates(new DateTime(year, month, 1));
        Assert.Equal(42, dates.Count);
        Assert.Equal(new DateTime(y, m, d), dates[0]);
        Assert.Equal(DayOfWeek.Sunday, dates[^1].DayOfWeek);
        Assert.Equal(42, dates.Distinct().Count());
    }

    [Theory]
    [InlineData(2026, 2, 17, "春节")]
    [InlineData(2024, 2, 10, "春节")]
    [InlineData(2023, 3, 22, "闰二月")]
    public void Lunar_labels_handle_new_year_and_leap_month(int y, int m, int d, string expected)
        => Assert.Equal(expected, CalendarDateInfo.Label(new DateTime(y, m, d)));

    [Fact]
    public void Unsupported_lunar_dates_are_explicit()
        => Assert.Equal("农历暂无数据", CalendarDateInfo.Label(new DateTime(2200, 2, 2)));

    [Theory]
    [InlineData(2026, 9, 20, "班")]
    [InlineData(2026, 9, 25, "休")]
    [InlineData(2027, 9, 25, "")]
    public void Official_schedule_is_used_only_for_known_year(int y, int m, int d, string expected)
        => Assert.Equal(expected, CalendarDateInfo.WorkRestLabel(new DateTime(y, m, d)));
}
