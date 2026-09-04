using Devlog.Core.Abstractions;

namespace Devlog.Core.Metrics;

/// <summary>
/// The one place that turns a date range into a digest. <c>devlog digest</c>
/// and <c>GET /v1/digest</c> both call this and nothing else — one query
/// behind two renderers, same as <c>ISessionReader</c> itself. If either
/// surface ever disagreed on totals, this is the only place a fix could live.
/// </summary>
public static class DigestBuilder
{
    /// <param name="from">Inclusive, interpreted as this machine's local calendar date — matches how the timeline endpoint reads <c>?date=</c>.</param>
    /// <param name="to">Inclusive.</param>
    public static async Task<(DigestMetrics Metrics, string Markdown)> BuildAsync(
        ISessionReader reader, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var fromUtc = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue)).ToUnixTimeMilliseconds();
        var toUtc = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue)).ToUnixTimeMilliseconds();

        var sessions = await reader.GetRangeAsync(fromUtc, toUtc, ct);
        var commits = await reader.GetCommitsAsync(fromUtc, toUtc, ct);

        // Only for first-time-language detection — everything before the range,
        // not scoped further, because "first time ever" is the honest claim.
        var commitsBeforeRange = await reader.GetCommitsAsync(0, fromUtc, ct);

        var unclassifiedSeconds = await reader.GetUnclassifiedSecondsAsync(fromUtc, toUtc, ct);

        var metrics = MetricsCalculator.Calculate(from, to, sessions, commits, commitsBeforeRange, unclassifiedSeconds);
        return (metrics, DigestWriter.Write(metrics));
    }
}
