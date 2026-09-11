using Devlog.Core.Metrics;

namespace Devlog.Core.Tests;

public class CalendarRangeTests
{
    [Fact]
    public void WeeksWithin_SplitsAFullMonthIntoSevenDayWeeks_WithATrailingPartialWeek()
    {
        // September 2026 is 30 days: 4 full weeks (28 days) plus 2 leftover days.
        var weeks = CalendarRange.WeeksWithin(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        Assert.Equal(5, weeks.Count);
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7)), weeks[0]);
        Assert.Equal((new DateOnly(2026, 9, 8), new DateOnly(2026, 9, 14)), weeks[1]);
        Assert.Equal((new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 21)), weeks[2]);
        Assert.Equal((new DateOnly(2026, 9, 22), new DateOnly(2026, 9, 28)), weeks[3]);
        Assert.Equal((new DateOnly(2026, 9, 29), new DateOnly(2026, 9, 30)), weeks[4]);
    }

    [Fact]
    public void WeeksWithin_ClipsToTheRequestedRange_NeverReachingOutsideIt()
    {
        // The current month-to-date range, requested on the 3rd of the month -
        // must not produce a week extending past "to", and must not start
        // before "from" by snapping to the containing calendar week's Monday.
        var weeks = CalendarRange.WeeksWithin(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3));

        var week = Assert.Single(weeks);
        Assert.Equal(new DateOnly(2026, 9, 1), week.From);
        Assert.Equal(new DateOnly(2026, 9, 3), week.To);
    }

    [Fact]
    public void WeeksWithin_SingleDayRange_YieldsOneWeekOfOneDay()
    {
        var weeks = CalendarRange.WeeksWithin(new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 15));

        var week = Assert.Single(weeks);
        Assert.Equal(new DateOnly(2026, 9, 15), week.From);
        Assert.Equal(new DateOnly(2026, 9, 15), week.To);
    }
}
