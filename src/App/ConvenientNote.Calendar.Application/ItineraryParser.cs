using System.Text.RegularExpressions;

namespace ConvenientNote.Calendar.Application;

public sealed record ItineraryDraft(string Title, DateTime Start, DateTime End, bool IsAllDay, string Details, bool IsChecklist = false, bool IsUnscheduled = false);
public sealed record ItineraryPreview(string Name, string SourceText, string Overview, IReadOnlyList<ItineraryDraft> Items);

/// <summary>Conservative parser for date-headed Chinese itineraries; keeps the source for review.</summary>
public static class ItineraryParser
{
    private static readonly Regex Heading = new(@"^\s*【(?<body>[^】]+)】\s*$");
    private static readonly Regex DateHeading = new(@"^(?:(?<year>\d{4})年)?(?<month>\d{1,2})月(?<day>\d{1,2})日(?<rest>.*)$");
    private static readonly Regex Year = new(@"(?<!\d)(?<year>\d{4})(?:[.年/-])\d{1,2}");
    private static readonly Regex Clock = new(@"(?<!\d)(?<hour>\d{1,2})[:：](?<minute>\d{2})(?!\d)");

    public static ItineraryPreview Parse(string text, int fallbackYear)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("请先粘贴行程文本。", nameof(text));
        if (text.Length > 200_000) throw new ArgumentException("行程文本过长，请控制在 20 万字以内。", nameof(text));
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var firstDaily = Array.FindIndex(lines, l => Heading.Match(l) is { Success: true } h && DateHeading.IsMatch(h.Groups["body"].Value));
        if (firstDaily < 0) throw new ArgumentException("未识别到日期标题，请使用【9月24日｜当天安排】格式。", nameof(text));
        var yearMatch = Year.Match(string.Join("\n", lines.Take(firstDaily + 1)));
        var year = yearMatch.Success ? int.Parse(yearMatch.Groups["year"].Value) : fallbackYear;
        ValidateYear(year);
        var overview = new List<string>();
        var sections = new List<(DateTime Date, string Title, List<string> Body)>();
        var active = -1;
        var previousMonth = 0;
        foreach (var line in lines)
        {
            var heading = Heading.Match(line);
            var date = heading.Success ? DateHeading.Match(heading.Groups["body"].Value) : Match.Empty;
            if (date.Success)
            {
                var month = int.Parse(date.Groups["month"].Value);
                var day = int.Parse(date.Groups["day"].Value);
                if (date.Groups["year"].Success) year = int.Parse(date.Groups["year"].Value);
                else if (previousMonth == 12 && month == 1) year++;
                ValidateYear(year);
                if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
                    throw new ArgumentException($"日期无效：{heading.Groups["body"].Value}", nameof(text));
                previousMonth = month;
                var rest = date.Groups["rest"].Value.Trim();
                var separator = rest.IndexOfAny(['｜', '|']);
                var title = separator >= 0 ? rest[(separator + 1)..].Trim() : Regex.Replace(rest, @"^(周[一二三四五六日天]|星期[一二三四五六日天])\s*", "").Trim();
                if (title.Length == 0) title = $"{month}月{day}日行程";
                sections.Add((new DateTime(year, month, day), title, []));
                active = sections.Count - 1;
            }
            else if (heading.Success)
            {
                active = -1;
                overview.Add(line);
            }
            else if (active >= 0) sections[active].Body.Add(line);
            else overview.Add(line);
        }
        var items = new List<ItineraryDraft>();
        foreach (var section in sections)
        {
            var body = string.Join("\n", section.Body).Trim();
            var transport = ExtractTransport(section.Date, section.Body);
            var details = body;
            if (Clock.IsMatch(body) || body.Contains("以车票为准") || body.Contains("待确认"))
                details += "\n\n时间待确认：仅将明确成对的交通起止时间提取为定时日程；约定时间、单个时间和时间范围保留原文，请在预览中核对。";
            items.Add(new ItineraryDraft(section.Title, section.Date, section.Date.AddDays(1), true, details));
            items.AddRange(transport);
        }
        foreach (var line in lines.Where(l => l.TrimStart().StartsWith('□')))
        {
            var title = line.Trim().TrimStart('□').Trim();
            if (title.Length > 0) items.Add(new ItineraryDraft(title, sections[0].Date, sections[0].Date.AddDays(1), true,
                "截止日期未指定；保存在待安排，可按需指定日期。\n" + line.Trim(), true, true));
        }
        var name = lines.Take(firstDaily).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "旅行行程";
        return new ItineraryPreview(name, text, string.Join("\n", overview).Trim(), items);
    }

    private static IReadOnlyList<ItineraryDraft> ExtractTransport(DateTime date, List<string> lines)
    {
        var result = new List<ItineraryDraft>();
        (DateTime Time, string Description)? departure = null;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            var clock = Clock.Match(line);
            if (!clock.Success)
            {
                // Do not join a departure from one part of the day with an unrelated arrival later.
                if (line.EndsWith('：') || line.EndsWith(':')) departure = null;
                continue;
            }
            if (line.Contains("左右") || line.Contains("尽量") || line.Contains("大约") || line.Contains("约") || Clock.Matches(line).Count != 1)
            {
                departure = null;
                continue;
            }
            var hour = int.Parse(clock.Groups["hour"].Value);
            var minute = int.Parse(clock.Groups["minute"].Value);
            if (hour > 23 || minute > 59) { departure = null; continue; }
            var description = line;
            if (line[(clock.Index + clock.Length)..].Trim(' ', '：', ':').Length == 0)
            {
                for (var j = i + 1; j < lines.Count; j++)
                {
                    if (string.IsNullOrWhiteSpace(lines[j])) continue;
                    if (!Clock.IsMatch(lines[j])) description += "\n" + lines[j].Trim();
                    break;
                }
            }
            if (description.Contains("左右") || description.Contains("尽量") || description.Contains("约") || description.Contains("预计"))
            {
                departure = null;
                continue;
            }
            var time = date.AddHours(hour).AddMinutes(minute);
            if (line[..clock.Index].Contains("次日") || line[..clock.Index].Contains("翌日")) time = time.AddDays(1);
            var arrival = description.Contains("到达") || description.Contains("抵达") || description.Contains("落地");
            var starts = description.Contains("出发") || description.Contains("起飞") || description.Contains('→') || description.Contains("开车");
            if (arrival && departure is { } start)
            {
                if (time > start.Time)
                    result.Add(new ItineraryDraft(CleanTransportTitle(start.Description), start.Time, time, false, start.Description + "\n" + description + "\n请以最终车票或机票为准。"));
                departure = null;
            }
            else departure = starts && !arrival ? (time, description) : null;
        }
        return result;
    }

    private static string CleanTransportTitle(string description)
    {
        var title = Clock.Replace(description, "").Trim(' ', '：', ':', '\n', '。');
        return title.Length == 0 ? "交通" : title.Replace('\n', ' ');
    }

    private static void ValidateYear(int year)
    {
        if (year is < 1902 or > 2199) throw new ArgumentException("行程年份须在 1902—2199 年之间。");
    }
}
