using Devlog.Core.Configuration;
using Devlog.Core.Domain;

namespace Devlog.Core.Derivation;

/// <summary>
/// Activities → sessions.
/// <para>
/// <b>Project-scoped with excursion folding.</b> Coding sessions are keyed by
/// project, so two different repositories never merge and per-project totals
/// stay meaningful. Non-coding activities are keyed by category alone, so
/// a run of documentation pages becomes one learning block rather than one
/// session per page.
/// </para>
/// <para>
/// A detour shorter than <see cref="DerivationOptions.ExcursionSeconds"/> that
/// returns to the same context is folded back in and counted as an interruption
/// — otherwise a single glance at a browser would split a two-hour refactor in
/// half.
/// </para>
/// </summary>
public sealed class SessionBuilder(DerivationOptions options)
{
    /// <summary>
    /// Builds sessions and stamps each activity with the session that contains
    /// it, returning both.
    /// <para>
    /// Ids are assigned here rather than by the database, so activities can point
    /// at their session without a round-trip — and so a re-derivation of unchanged
    /// input produces byte-identical output, which is what makes idempotency
    /// checkable.
    /// </para>
    /// </summary>
    public (List<Session> Sessions, List<Activity> Activities) Build(
        IReadOnlyList<Activity> activities,
        IReadOnlyCollection<SessionOverride>? overrides = null)
    {
        var ordered = activities.OrderBy(a => a.StartUtc).ToList();
        var sessions = new List<Session>();

        var i = 0;
        var nextId = 1L;

        while (i < ordered.Count)
        {
            var (session, consumed) = BuildOne(ordered, i);

            var id = nextId++;
            sessions.Add(session with { Id = id });

            // Everything the session spans belongs to it, including the folded
            // excursions — they happened inside that stretch of work.
            for (var j = i; j < i + consumed && j < ordered.Count; j++)
            {
                ordered[j] = ordered[j] with { SessionId = id };
            }

            i += consumed;
        }

        var final = overrides is { Count: > 0 } ? ApplyOverrides(sessions, overrides) : sessions;
        return (final, ordered);
    }

    private (Session Session, int Consumed) BuildOne(List<Activity> ordered, int startIndex)
    {
        var first = ordered[startIndex];
        var key = SessionKey(first);

        var endUtc = first.EndUtc;
        var interruptions = 0;
        var deepSeconds = first.Engagement == Engagement.Producing ? first.DurationSeconds : 0;
        var consumed = 1;

        var i = startIndex + 1;
        while (i < ordered.Count)
        {
            var candidate = ordered[i];

            // Silence longer than the gap threshold ends the session outright.
            if (candidate.StartUtc - endUtc > options.SessionGap.TotalMilliseconds)
            {
                break;
            }

            if (SessionKey(candidate) == key)
            {
                endUtc = candidate.EndUtc;
                if (candidate.Engagement == Engagement.Producing)
                {
                    deepSeconds += candidate.DurationSeconds;
                }

                consumed = i - startIndex + 1;
                i++;
                continue;
            }

            // Different key. Look ahead: is this a brief detour that comes back,
            // or a genuine switch to something else?
            var excursion = MeasureExcursion(ordered, i, key);

            if (excursion is null)
            {
                break;
            }

            var (returnIndex, excursionMs) = excursion.Value;

            if (excursionMs > options.Excursion.TotalMilliseconds)
            {
                break;
            }

            // Folded in. The excursion's own time is deliberately excluded from
            // deepSeconds — it was time away from the work, even if brief.
            interruptions++;
            i = returnIndex;
        }

        var session = new Session
        {
            StartUtc = first.StartUtc,
            EndUtc = endUtc,
            ActivityKey = key,

            // Straight from the activity, which sets it only when an extraction
            // rule actually resolved a repository. This used to promote Context
            // whenever the category was Coding, which is how "GitLab", "Windows
            // PowerShell" and raw SSMS window titles became projects in the
            // digest. Nothing non-coding resolves a project, so no category test
            // is needed here any more.
            Project = first.Project,
            Category = first.Category,
            Interruptions = interruptions,
            DeepSeconds = deepSeconds
        };

        return (session, Math.Max(1, consumed));
    }

    /// <summary>
    /// Measures a run of foreign activities. Returns the index where the original
    /// key resumes and how long the detour lasted, or null if it never comes back
    /// — in which case it is not an excursion at all, it is a new session.
    /// </summary>
    private static (int ReturnIndex, long DurationMs)? MeasureExcursion(
        List<Activity> ordered,
        int from,
        string key)
    {
        var startUtc = ordered[from].StartUtc;

        for (var j = from; j < ordered.Count; j++)
        {
            if (SessionKey(ordered[j]) == key)
            {
                return (j, ordered[j].StartUtc - startUtc);
            }
        }

        return null;
    }

    /// <summary>
    /// Coding is keyed by project; everything else by category alone. That is
    /// what keeps consecutive documentation pages in one learning block instead
    /// of producing a session per page.
    /// </summary>
    private static string SessionKey(Activity a) =>
        a.Category == ActivityCategory.Coding
            ? $"Coding{a.Context ?? a.ProcessName ?? "?"}"
            : a.Category.ToString();

    /// <summary>
    /// Applied last, and keyed by <c>(StartUtc, ActivityKey)</c> rather than by
    /// session id — ids do not survive a rebuild, but corrections must.
    /// </summary>
    private static List<Session> ApplyOverrides(
        List<Session> sessions,
        IReadOnlyCollection<SessionOverride> overrides)
    {
        var lookup = overrides.ToDictionary(
            o => (o.SessionStartUtc, o.ActivityKey),
            o => o);

        for (var i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];

            if (lookup.TryGetValue((s.StartUtc, s.ActivityKey), out var o))
            {
                sessions[i] = s with
                {
                    Category = o.Category ?? s.Category,
                    Label = o.Label ?? s.Label
                };
            }
        }

        return sessions;
    }
}
