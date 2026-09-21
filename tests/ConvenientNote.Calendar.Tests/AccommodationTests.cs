using ConvenientNote.Calendar.Application;
using ConvenientNote.Calendar.Contracts;
using ConvenientNote.ViewModels;
using Xunit;

namespace ConvenientNote.Calendar.Tests;

public sealed class AccommodationTests
{
    [Fact]
    public void FullItineraryExtractsThirteenNightsWithoutAssigningHotelToReturnDay()
    {
        var preview = ItineraryParser.Parse(YunnanItineraryFixture.Text, 2026);
        var days = preview.Items.Where(d => d.IsAllDay && !d.IsChecklist).ToArray();
        var hotels = days.Select(d => AccommodationParser.Parse(d.Details)).ToArray();
        Assert.Equal(13, hotels.Count(h => h is not null));
        Assert.Equal("竹海间民宿（三义国际机场店）", hotels[0]!.Name);
        Assert.Equal("贵香昌聪客栈", hotels[10]!.Name);
        Assert.Equal("贵香昌聪客栈", hotels[11]!.Name);
        Assert.True(hotels[12]!.IsUnconfirmed);
        Assert.Null(hotels[13]);
        Assert.Contains("特价阁楼大床房", hotels[7]!.Notes);
    }

    [Fact]
    public void ExistingSavedDetailsAppearOnDayWithoutReimportAndExcludeOvernightTransport()
    {
        var date = new DateTime(2026, 9, 25);
        var tasks = new[] {
            new CalendarTaskViewModel(new(Guid.NewGuid(), "飞机", date.AddDays(-1).AddHours(21), false, false, date.AddMinutes(15), false, "住宿：旧酒店")),
            new CalendarTaskViewModel(new(Guid.NewGuid(), "休息", date, false, false, date.AddDays(1), true, "住宿：\n心花路放·Dongba Light\n9月25日—26日。\n\n提醒：\n休息")) };
        var day = new CalendarDayViewModel(date, true, false, true, "", tasks);
        Assert.Equal("住 · 心花路放·Dongba Light", day.AccommodationLabel);
        Assert.Single(day.Previews);
        Assert.Equal("+1 项", day.OverflowLabel);
        Assert.DoesNotContain("旧酒店", day.AccommodationDetails);
    }

    [Theory]
    [InlineData("提前确认住宿：酒店接机", null)]
    [InlineData("住宿：\n\n提醒：\n早点休息", null)]
    [InlineData("住宿: 湖边客栈\n\n提醒：别迟到", "湖边客栈")]
    public void OnlyExplicitAccommodationSectionIsUsed(string details, string? expected)
        => Assert.Equal(expected, AccommodationParser.Parse(details)?.Name);
}
