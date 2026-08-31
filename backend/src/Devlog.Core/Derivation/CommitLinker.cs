using Devlog.Core.Configuration;
using Devlog.Core.Domain;

namespace Devlog.Core.Derivation;

/// <summary>
/// Attaches commits to the sessions that produced them.
/// <para>
/// A commit is not an activity — the collector never sees one happen — so the
/// link is made after the fact, by timestamp. A commit falling inside a
/// session's span attaches directly; one landing just outside attaches to the
/// nearest session within <see cref="GitOptions.CommitAttachWindow"/>; anything
/// further is left unattached but never dropped.
/// </para>
/// <para>
/// Pure and dependency-free, like <see cref="ActivityBuilder"/> and
/// <see cref="SessionBuilder"/> — no I/O, so it is testable without a database.
/// </para>
/// </summary>
public sealed class CommitLinker(GitOptions options)
{
    /// <summary>
    /// Returns each commit's sha mapped to the session it attaches to, or null
    /// when nothing was close enough. The caller writes this back with
    /// <see cref="Abstractions.ICommitStore.RelinkAsync"/> — linking never
    /// touches disk or re-scans anything.
    /// </summary>
    public Dictionary<string, long?> Link(
        IReadOnlyList<CommitRecord> commits,
        IReadOnlyList<Session> sessions)
    {
        // Sessions arrive already ordered from SessionBuilder. Binary search
        // keeps this O(commits * log sessions) - irrelevant today at a few
        // hundred rows, but it is the difference between fine and slow a year
        // of history from now.
        var starts = new long[sessions.Count];
        for (var i = 0; i < sessions.Count; i++)
        {
            starts[i] = sessions[i].StartUtc;
        }

        var result = new Dictionary<string, long?>(commits.Count);
        var windowMs = (long)options.CommitAttachWindow.TotalMilliseconds;

        foreach (var commit in commits)
        {
            result[commit.Sha] = FindSession(commit.TsUtc, sessions, starts, windowMs)?.Id;
        }

        return result;
    }

    private static Session? FindSession(
        long commitTsUtc,
        IReadOnlyList<Session> sessions,
        long[] starts,
        long windowMs)
    {
        if (sessions.Count == 0)
        {
            return null;
        }

        // Rightmost session whose start is <= the commit time.
        var index = Array.BinarySearch(starts, commitTsUtc);
        if (index < 0)
        {
            index = ~index - 1;
        }

        if (index >= 0 && index < sessions.Count)
        {
            var candidate = sessions[index];
            if (commitTsUtc >= candidate.StartUtc && commitTsUtc <= candidate.EndUtc)
            {
                return candidate;
            }
        }

        // Not inside any session. Check the two nearest neighbours for the
        // attach window - a commit made moments after the last recorded
        // activity should not be stranded as unattached.
        Session? nearest = null;
        var nearestDistance = long.MaxValue;

        foreach (var i in new[] { index, index + 1 })
        {
            if (i < 0 || i >= sessions.Count)
            {
                continue;
            }

            var s = sessions[i];
            var distance = commitTsUtc < s.StartUtc
                ? s.StartUtc - commitTsUtc
                : commitTsUtc - s.EndUtc;

            if (distance <= windowMs && distance < nearestDistance)
            {
                nearest = s;
                nearestDistance = distance;
            }
        }

        return nearest;
    }
}
