using Devlog.Core.Domain;

namespace Devlog.Core.Abstractions;

/// <summary>
/// Reads the derived timeline back out.
/// <para>
/// This interface exists because until now there was no way to <em>read</em> a
/// session at all. <c>SessionStore</c> and <c>ActivityStore</c> could only
/// replace their tables wholesale and count rows; every actual read lived as
/// inline SQL inside <c>StatsReporter</c>, tangled into the code that formatted
/// console text, and handed back a finished string.
/// </para>
/// <para>
/// That made the promise "the CLI and the API can never disagree about what a
/// session is" impossible to keep, because there was only one of them and it
/// returned prose. One query behind this interface, two renderers in front of
/// it, is what makes the promise real.
/// </para>
/// <para>
/// It lives in Core so <c>Devlog.Api</c> can depend on it while staying on plain
/// <c>net10.0</c> with no reference to the Windows-only infrastructure project —
/// the property that keeps the contracts portable to a server later.
/// </para>
/// </summary>
public interface ISessionReader
{
    /// <summary>
    /// Sessions overlapping a window, oldest first. The timeline's query: a
    /// session that starts before <paramref name="fromUtc"/> and runs into the
    /// window is included, because a day's picture is wrong without the block
    /// you were already inside at midnight.
    /// </summary>
    Task<List<SessionSummary>> GetRangeAsync(long fromUtc, long toUtc, CancellationToken ct = default);

    /// <summary>
    /// The most recent <paramref name="count"/> sessions, returned oldest first
    /// so they read down the page in the order they happened. The terminal's
    /// query.
    /// </summary>
    Task<List<SessionSummary>> GetRecentAsync(int count, CancellationToken ct = default);

    /// <summary>
    /// The activities that formed one session, in order. Powers session detail,
    /// and is the exact input the narration job needs to see a sequence rather
    /// than a set of unrelated verdicts.
    /// </summary>
    Task<List<Activity>> GetActivitiesAsync(long sessionId, CancellationToken ct = default);

    /// <summary>Commits timestamped inside a window, whether or not they attached to a session.</summary>
    Task<List<CommitRecord>> GetCommitsAsync(long fromUtc, long toUtc, CancellationToken ct = default);

    /// <summary>
    /// Seconds of activity still carrying no confident category. Reported on its
    /// own rather than folded into a real bucket, so it cannot quietly flatter
    /// the focus ratio.
    /// </summary>
    Task<long> GetUnclassifiedSecondsAsync(CancellationToken ct = default);
}
