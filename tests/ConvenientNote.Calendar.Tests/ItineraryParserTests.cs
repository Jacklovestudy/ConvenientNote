using ConvenientNote.Calendar.Application;
using Xunit;

namespace ConvenientNote.Calendar.Tests;

public class ItineraryParserTests
{
    [Fact]
    public void FullOriginalItineraryHasFourteenDaysThreeTransportsAndFifteenChecklistItems()
    {
        var result = ItineraryParser.Parse(YunnanItineraryFixture.Text, 2025);
        Assert.Equal(YunnanItineraryFixture.Text, result.SourceText);
        var days = result.Items.Where(x => x.IsAllDay && !x.IsChecklist).ToArray();
        Assert.Equal(14, days.Length);
        Assert.Equal(new DateTime(2026, 9, 24), days[0].Start);
        Assert.Equal(new DateTime(2026, 10, 7), days[^1].Start);
        var transports = result.Items.Where(x => !x.IsAllDay).ToArray();
        Assert.Equal(3, transports.Length);
        Assert.Equal(new DateTime(2026, 9, 24, 17, 13, 0), transports[0].Start);
        Assert.Equal(new DateTime(2026, 9, 24, 18, 7, 0), transports[0].End);
        Assert.Equal(new DateTime(2026, 9, 24, 21, 20, 0), transports[1].Start);
        Assert.Equal(new DateTime(2026, 9, 25, 0, 15, 0), transports[1].End);
        Assert.Equal(new DateTime(2026, 10, 7, 9, 20, 0), transports[2].Start);
        Assert.Equal(new DateTime(2026, 10, 7, 12, 15, 0), transports[2].End);
        var checklist = result.Items.Where(x => x.IsChecklist).ToArray();
        Assert.Equal(15, checklist.Length);
        Assert.Equal(32, result.Items.Count);
        Assert.All(checklist, x => Assert.Contains("未指定", x.Details));
        // Every original body line must survive, including notices after hotel blocks.
        var allSavedContent = result.Overview + "\n" + string.Join("\n", days.Select(x => x.Details));
        foreach (var line in YunnanItineraryFixture.Text.Split('\n').Where(x => x.Length > 0 && !x.StartsWith('【')))
            Assert.Contains(line, allSavedContent);
        Assert.Contains("26L与折叠小包", result.Overview);
        Assert.Contains("时间不赶满，徒步不补作业", result.Overview);
        Assert.DoesNotContain("26L与折叠小包", days[^1].Details);
    }

    [Theory]
    [InlineData("08:00：\n早餐。\n09:00 到达车站。")]
    [InlineData("08:00 出发。\n08:30 早餐。\n09:00 到达车站。")]
    [InlineData("08:00：\n大约出发。\n09:00 到达车站。")]
    [InlineData("09:20：\n大约起飞\n12:15：\n到达南京。")]
    [InlineData("08:00 出发。\n晚上：\n21:00 到达酒店。")]
    public void DoesNotCreateExactTransportFromUnrelatedOrUncertainTimes(string body)
    {
        var result = ItineraryParser.Parse("【9月24日｜出发】\n" + body, 2026);
        Assert.All(result.Items, x => Assert.True(x.IsAllDay));
    }

    [Fact]
    public void RejectsOversizedSourceBeforeParsing() =>
        Assert.Throws<ArgumentException>(() => ItineraryParser.Parse("【9月24日｜出发】\n" + new string('行', 200_001), 2026));

    [Theory]
    [InlineData("【2024年2月29日｜出发】", 2026, 2024, 2, 29)]
    [InlineData("【1月1日｜出发】", 1902, 1902, 1, 1)]
    [InlineData("【12月31日｜出发】", 2199, 2199, 12, 31)]
    public void SupportsValidLeapDateAndYearBoundaries(string text, int fallback, int year, int month, int day)
    {
        var result = ItineraryParser.Parse(text, fallback);
        Assert.Equal(new DateTime(year, month, day), result.Items[0].Start);
    }

    [Fact]
    public void PreservesDailyDetailsOverviewAndUnassignedChecklist()
    {
        const string source = "云南旅行计划｜2026.9.24—10.7\n路线：江阴 → 南京\n【9月24日 周四｜下班后出发】\n住宿：机场酒店\n【9月25日 周五｜休息】\n睡到自然醒\n————————\n【背包安排，避免忘记】\n38L主包\n【出发前最后核对】\n□ 确认接机。";
        var result = ItineraryParser.Parse(source, 2025);
        Assert.Equal(source, result.SourceText);
        Assert.Equal(2, result.Items.Count(x => !x.IsChecklist));
        Assert.Equal(new DateTime(2026, 9, 24), result.Items[0].Start);
        Assert.Contains("机场酒店", result.Items[0].Details);
        Assert.DoesNotContain("38L", result.Items[1].Details);
        Assert.Contains("38L主包", result.Overview);
        var todo = Assert.Single(result.Items, x => x.IsChecklist);
        Assert.Contains("未指定", todo.Details);
    }

    [Fact]
    public void ExtractsRailAndOvernightFlightWithoutApproximateAirportTime()
    {
        var result = ItineraryParser.Parse("旅行｜2026.9.24\n【9月24日 周四｜出发】\n17:13 江阴出发。\n18:07 到达南京南站。\n尽量19:30左右到机场。\n21:20 南京 → 丽江，祥鹏航空8L9812。\n次日00:15到达丽江。", 2026);
        var timed = result.Items.Where(x => !x.IsAllDay).ToArray();
        Assert.Equal(2, timed.Length);
        Assert.Equal(new DateTime(2026, 9, 24, 17, 13, 0), timed[0].Start);
        Assert.Equal(new DateTime(2026, 9, 24, 18, 7, 0), timed[0].End);
        Assert.Equal(new DateTime(2026, 9, 25, 0, 15, 0), timed[1].End);
    }

    [Fact]
    public void ExtractsMultilineTransportAndKeepsUncertainTimesInDetails()
    {
        var result = ItineraryParser.Parse("【10月7日 周三｜回家】\n06:00左右：\n起床。\n07:00—07:20左右：\n到达机场。\n09:20：\n昆明航空KY8223起飞。\n12:15：\n计划到达南京禄口机场T1。", 2026);
        var timed = Assert.Single(result.Items, x => !x.IsAllDay);
        Assert.Equal(new DateTime(2026, 10, 7, 9, 20, 0), timed.Start);
        Assert.Equal(new DateTime(2026, 10, 7, 12, 15, 0), timed.End);
    }

    [Fact]
    public void DoesNotInventEndTimesOrTreatMorningRangesAsTransport()
    {
        var result = ItineraryParser.Parse("【9月27日｜出发】\n07:30—09:00：\n早餐。\n09:20 出发。", 2026);
        Assert.All(result.Items, x => Assert.True(x.IsAllDay));
        Assert.Contains("待确认", result.Items[0].Details);
    }

    [Fact]
    public void RollsDecemberIntoNextYear()
    {
        var result = ItineraryParser.Parse("跨年｜2026.12.31—1.1\n【12月31日｜出发】\n休息\n【1月1日｜回家】\n休息", 2025);
        Assert.Equal(new DateTime(2027, 1, 1), result.Items[1].Start);
    }

    [Fact]
    public void SplitsAllFourteenYunnanDaysWithoutConsumingGeneralSections()
    {
        const string source = "云南旅行计划｜2026.9.24—10.7\n路线：江阴 → 南京 → 丽江\n【9月24日 周四｜下班后出发】\n接机\n【9月25日 周五｜丽江休息、轻松逛】\n休息\n【9月26日 周六｜丽江 → 香格里拉】\n车票为准\n【9月27日 周日｜香格里拉 → 飞来寺】\n品松措酒店\n【9月28日 周一｜飞来寺 → 尼农 → 雨崩上村】\n进村当天不追加其他徒步项目。\n【9月29日 周二｜冰湖一日徒步＋换酒店】\n如果客栈\n【9月30日 周三｜神瀑一日徒步＋换酒店】\n雨崩印象精品客栈\n【10月1日 周四｜乘车出雨崩 → 香格里拉】\n尘缘客栈\n【10月2日 周五｜香格里拉 → 茶马客栈 → 虎跳峡徒步】\n核桃园\n【10月3日 周六｜核桃园 → 香格里拉】\n乘车返回\n【10月4日 周日｜无底湖一日游】\n开放情况为准\n【10月5日 周一｜电动车游纳帕海】\n戴头盔\n【10月6日 周二｜香格里拉 → 昆明 → 机场附近】\n确认送机\n【10月7日 周三｜昆明 → 南京 → 无锡】\n留足换乘余量\n【背包安排，避免忘记】\n38L主包\n【出发前最后核对】\n□ 酒店订单\n□ 下载离线地图\n总原则：不硬撑。";
        var result = ItineraryParser.Parse(source, 2025);
        var days = result.Items.Where(x => x.IsAllDay && !x.IsChecklist).ToArray();
        Assert.Equal(14, days.Length);
        Assert.Equal(new DateTime(2026, 10, 7), days[13].Start);
        Assert.Contains("进村当天不追加", days[4].Details);
        Assert.Contains("不硬撑", result.Overview);
        Assert.Equal(2, result.Items.Count(x => x.IsChecklist));
    }

    [Theory]
    [InlineData("")]
    [InlineData("没有日期的文本")]
    [InlineData("【2月30日｜出发】")]
    [InlineData("旅行2026.13.24\n【13月24日｜出发】")]
    [InlineData("旅行2200.1.1\n【1月1日｜出发】")]
    public void RejectsInvalidInput(string text) => Assert.Throws<ArgumentException>(() => ItineraryParser.Parse(text, 2026));
}
