namespace Devlog.Core.Metrics;

/// <summary>
/// A brag document is a calendar question — "what did I do in March" — not a
/// rolling-window one. This is the one definition of "week" and "month" shared
/// by the CLI's <c>--week</c>/<c>--month</c> and <c>GET /v1/digest?range=</c>,
/// so the two surfaces cannot report different ranges for the same word.
/// </summary>
public enum DigestRangeKind
{
    Week,
    Month
}

public static class CalendarRange
{
    /// <param name="today">The caller's local "today" — passed in rather than read here so this stays a pure function.</param>
    public static (DateOnly From, DateOnly To) For(DigestRangeKind kind, DateOnly today) => kind switch
    {
        DigestRangeKind.Week => (MondayOf(today), today),
        DigestRangeKind.Month => (new DateOnly(today.Year, today.Month, 1), today),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    /// <summary>Monday of the week containing <paramref name="date"/>. <see cref="DayOfWeek.Sunday"/> is 0, so Monday is 6 days after it.</summary>
    private static DateOnly MondayOf(DateOnly date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysSinceMonday);
    }
}
