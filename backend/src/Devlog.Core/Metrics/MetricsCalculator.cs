using System.Text.RegularExpressions;
using Devlog.Core.Domain;

namespace Devlog.Core.Metrics;

/// <summary>
/// Turns sessions and commits into <see cref="DigestMetrics"/>. Pure — no I/O,
/// no clock, no database — so it is testable the same way every other
/// derivation class in this namespace-adjacent tree is: hand-built input in,
/// asserted output out.
/// <para>
/// This is deliberately a second query behind the "one query, two renderers"
/// rule, not a third data source: everything it reads comes from
/// <c>ISessionReader</c>, the same reader the CLI's <c>sessions</c> command and
/// the Today dashboard already use. If a digest total ever disagrees with
/// those, the bug is here, not in a second copy of the SQL.
/// </para>
/// </summary>
public static partial class MetricsCalculator
{
    [GeneratedRegex(@"\b[A-Z]{2,}-\d+\b")]
    private static partial Regex TicketIdPattern();

    /// <param name="sessions">Sessions overlapping [from, to) — <c>ISessionReader.GetRangeAsync</c>.</param>
    /// <param name="commits">Commits timestamped inside [from, to) — <c>ISessionReader.GetCommitsAsync</c>.</param>
    /// <param name="commitsBeforeRange">
    /// Commits before <paramref name="from"/>, used only to detect first-time
    /// languages. Pass an empty list rather than omitting the check — an empty
    /// history means everything in range is "first-time", which is a fact worth
    /// keeping honest for a fresh install rather than special-cased away.
    /// </param>
    public static DigestMetrics Calculate(
        DateOnly from,
        DateOnly to,
        IReadOnlyList<SessionSummary> sessions,
        IReadOnlyList<CommitRecord> commits,
        IReadOnlyList<CommitRecord> commitsBeforeRange,
        long unclassifiedSeconds)
    {
        var trackedSeconds = sessions.Sum(s => s.Session.DurationSeconds);
        var deepSeconds = sessions.Sum(s => s.Session.DeepSeconds);
        var interruptions = sessions.Sum(s => s.Session.Interruptions);

        var activeDays = sessions
            .Select(s => DateOnly.FromDateTime(s.Session.Start.ToLocalTime().DateTime))
            .Distinct()
            .Count();

        var zeroOutput = sessions.Where(s => s.IsZeroOutput).ToList();

        var longest = sessions
            .OrderByDescending(s => s.Session.DeepSeconds)
            .Select(s => new LongestBlock(s.Session.StartUtc, s.Session.EndUtc, s.Session.Project, s.Session.DeepSeconds))
            .FirstOrDefault();

        var bestDay = sessions
            .GroupBy(s => DateOnly.FromDateTime(s.Session.Start.ToLocalTime().DateTime))
            .Select(g => new BestDay(g.Key, g.Sum(s => s.Session.DeepSeconds)))
            .OrderByDescending(d => d.DeepSeconds)
            .FirstOrDefault();

        var timeByProject = sessions
            .Where(s => s.Session.Project is not null)
            .GroupBy(s => s.Session.Project!)
            .Select(g => new ProjectTime(g.Key, g.Sum(s => s.Session.DurationSeconds)))
            .OrderByDescending(p => p.Seconds)
            .ToList();

        var timeByCategory = sessions
            .GroupBy(s => s.Session.Category)
            .Select(g => new CategoryTime(g.Key, g.Sum(s => s.Session.DurationSeconds)))
            .OrderByDescending(c => c.Seconds)
            .ToList();

        var nonMerge = commits.Where(c => !c.IsMerge).ToList();

        var languagesBefore = commitsBeforeRange
            .Where(c => !c.IsMerge)
            .SelectMany(SplitLanguages)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var languagesInRange = nonMerge.SelectMany(SplitLanguages).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var firstTime = languagesInRange
            .Where(l => !languagesBefore.Contains(l))
            .ToList();

        var ticketIds = nonMerge
            .Where(c => c.Branch is not null)
            .SelectMany(c => TicketIdPattern().Matches(c.Branch!).Select(m => m.Value))
            .Distinct()
            .ToList();

        return new DigestMetrics
        {
            From = from,
            To = to,
            TrackedSeconds = trackedSeconds,
            DeepSeconds = deepSeconds,
            FocusRatio = trackedSeconds > 0 ? (double)deepSeconds / trackedSeconds : 0,
            SessionCount = sessions.Count,
            ActiveDays = activeDays,
            InterruptionsTotal = interruptions,
            InterruptionsPerActiveDay = activeDays > 0 ? (double)interruptions / activeDays : 0,
            LongestBlock = longest,
            BestDay = bestDay,
            TimeByProject = timeByProject,
            TimeByCategory = timeByCategory,
            ZeroOutputSessionCount = zeroOutput.Count,
            ZeroOutputSeconds = zeroOutput.Sum(s => s.Session.DurationSeconds),
            CommitCount = nonMerge.Count,
            Insertions = nonMerge.Sum(c => c.Insertions),
            Deletions = nonMerge.Sum(c => c.Deletions),
            ProjectsShipped = [.. nonMerge.Select(c => c.Project).Distinct().OrderBy(p => p, StringComparer.OrdinalIgnoreCase)],
            Languages = [.. languagesInRange.OrderBy(l => l, StringComparer.OrdinalIgnoreCase)],
            FirstTimeLanguages = firstTime,
            TicketIds = ticketIds,
            UnattachedCommitsInRange = nonMerge.Count(c => c.SessionId is null),
            UnclassifiedSeconds = unclassifiedSeconds,
        };
    }

    private static IEnumerable<string> SplitLanguages(CommitRecord commit) =>
        (commit.Languages ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
