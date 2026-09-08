using System.Globalization;

namespace ConvenientNote.Features.Calendar;

public static class CalendarDateInfo
{
    // State Council 2026 public holidays, published 2025-11-04:
    // https://big5.www.gov.cn/gate/big5/www.gov.cn/zhengce/content/202511/content_7047090.htm
    public static string WorkRestLabel(DateTime date)
    {
        if (date.Year != 2026) return "";
        var key = date.Month * 100 + date.Day;
        if (key is 104 or 214 or 228 or 509 or 920 or 1010) return "班";
        return key is >= 101 and <= 103 or >= 215 and <= 223 or >= 404 and <= 406
            or >= 501 and <= 505 or >= 619 and <= 621 or >= 925 and <= 927 or >= 1001 and <= 1007 ? "休" : "";
    }
    private static readonly ChineseLunisolarCalendar Lunar = new();
    private static readonly string[] Months = ["正", "二", "三", "四", "五", "六", "七", "八", "九", "十", "冬", "腊"];
    private static readonly string[] Digits = ["一", "二", "三", "四", "五", "六", "七", "八", "九", "十"];

    public static IReadOnlyList<DateTime> MonthDates(DateTime month)
    {
        var first = new DateTime(month.Year, month.Month, 1);
        var start = first.AddDays(-((int)first.DayOfWeek + 6) % 7);
        return Enumerable.Range(0, 42).Select(i => start.AddDays(i)).ToArray();
    }

    public static string Label(DateTime date)
    {
        var solar = (date.Month, date.Day) switch
        { (1, 1) => "元旦", (5, 1) => "劳动节", (6, 1) => "儿童节", (10, 1) => "国庆节", _ => null };
        if (solar is not null) return solar;
        if (date.Date < Lunar.MinSupportedDateTime.Date || date.Date > Lunar.MaxSupportedDateTime.Date)
            return "农历暂无数据";
        var year = Lunar.GetYear(date);
        var rawMonth = Lunar.GetMonth(date);
        var leap = Lunar.GetLeapMonth(year);
        var isLeap = leap > 0 && rawMonth == leap;
        var month = rawMonth - (leap > 0 && rawMonth >= leap ? 1 : 0);
        var day = Lunar.GetDayOfMonth(date);
        if (!isLeap)
        {
            var festival = (month, day) switch
            { (1, 1) => "春节", (1, 15) => "元宵节", (5, 5) => "端午节", (7, 7) => "七夕", (8, 15) => "中秋节", (9, 9) => "重阳节", (12, 8) => "腊八节", _ => null };
            if (festival is not null) return festival;
            if (month == 12 && day == Lunar.GetDaysInMonth(year, rawMonth)) return "除夕";
        }
        if (day == 1) return (isLeap ? "闰" : "") + Months[month - 1] + "月";
        return day switch { 10 => "初十", 20 => "二十", 30 => "三十", < 10 => "初" + Digits[day - 1], < 20 => "十" + Digits[day - 11], _ => "廿" + Digits[day - 21] };
    }
}
