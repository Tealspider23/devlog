using System.Text;

namespace Devlog.Core.Metrics;

/// <summary>
/// Renders <see cref="DigestMetrics"/> to Markdown.
/// <para>
/// This is the one place the digest's wording lives. <c>devlog digest</c> and
/// <c>GET /v1/digest</c> both call this and nothing else, so the file the CLI
/// writes and the string the UI's copy button copies are the same bytes by
/// construction — verified by diffing them, not by convention.
/// </para>
/// </summary>
public static class DigestWriter
{
    public static string Write(DigestMetrics m)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# devlog — {m.From:MMM d} to {m.To:MMM d, yyyy}");
        sb.AppendLine();

        if (m.SessionCount == 0)
        {
            sb.AppendLine("No tracked activity in this range.");
            return sb.ToString();
        }

        sb.AppendLine(Summary(m));
        sb.AppendLine();

        sb.AppendLine("## Focus");
        sb.AppendLine($"- **{Hours(m.DeepSeconds)} deep work** out of {Hours(m.TrackedSeconds)} tracked ({m.FocusRatio:P0} focus ratio)");
        sb.AppendLine($"- {m.SessionCount} sessions across {m.ActiveDays} active day{Plural(m.ActiveDays)}");
        sb.AppendLine($"- Interrupted {m.InterruptionsPerActiveDay:0.#}×/active day ({m.InterruptionsTotal} total)");

        if (m.LongestBlock is { } lb)
        {
            var project = lb.Project ?? "unclassified";
            sb.AppendLine($"- Longest uninterrupted block: **{Hms(lb.DeepSeconds)} on {project}** ({lb.Start.ToLocalTime():MMM d, HH:mm}–{lb.End.ToLocalTime():HH:mm})");
        }

        if (m.BestDay is { } bd)
        {
            sb.AppendLine($"- Best day: **{bd.Date:dddd, MMM d}** — {Hours(bd.DeepSeconds)} deep work");
        }

        sb.AppendLine();

        sb.AppendLine("## Shipped");

        if (m.CommitCount == 0)
        {
            sb.AppendLine("- No commits in this range.");
        }
        else
        {
            sb.AppendLine($"- **{m.CommitCount} commits**, +{m.Insertions}/-{m.Deletions}, across {m.ProjectsShipped.Count} project{Plural(m.ProjectsShipped.Count)}: {string.Join(", ", m.ProjectsShipped)}");

            if (m.Languages.Count > 0)
            {
                sb.AppendLine($"- Languages: {string.Join(", ", m.Languages)}");
            }

            if (m.FirstTimeLanguages.Count > 0)
            {
                sb.AppendLine($"- First time using: **{string.Join(", ", m.FirstTimeLanguages)}**");
            }

            if (m.TicketIds.Count > 0)
            {
                sb.AppendLine($"- Tickets touched: {string.Join(", ", m.TicketIds)}");
            }
        }

        if (m.ZeroOutputSessionCount > 0)
        {
            sb.AppendLine($"- {m.ZeroOutputSessionCount} session{Plural(m.ZeroOutputSessionCount)} ({Hours(m.ZeroOutputSeconds)}) shipped no commits — usually research or debugging, not wasted time.");
        }

        sb.AppendLine();

        if (m.TimeByProject.Count > 0 || m.UnattributedCodingSeconds > 0)
        {
            sb.AppendLine("## Time by project");

            foreach (var p in m.TimeByProject)
            {
                sb.AppendLine($"- {p.Project}: {Hours(p.Seconds)}");
            }

            // Stated, never dropped. This is coding time devlog could not tie to
            // a repository — a browser tab, a database client, a bare shell. It
            // used to appear above under invented project names like "GitLab"
            // and whole SSMS window titles; naming it honestly is the fix.
            if (m.UnattributedCodingSeconds > 0)
            {
                sb.AppendLine($"- *Coding time not tied to a repo: {Hours(m.UnattributedCodingSeconds)}*");
            }

            sb.AppendLine();
        }

        if (m.TimeByCategory.Count > 0)
        {
            sb.AppendLine("## Time by category");
            foreach (var c in m.TimeByCategory)
            {
                sb.AppendLine($"- {c.Category}: {Hours(c.Seconds)}");
            }
            sb.AppendLine();
        }

        sb.AppendLine(Footer(m));

        return sb.ToString();
    }

    /// <summary>
    /// A one-line quotable summary — the sentence you'd actually paste. Every
    /// figure here is also stated in full below; this is the compressed form.
    /// </summary>
    private static string Summary(DigestMetrics m)
    {
        var parts = new List<string> { $"**{Hours(m.DeepSeconds)} deep work**" };

        if (m.ProjectsShipped.Count > 0)
        {
            parts.Add($"across {m.ProjectsShipped.Count} project{Plural(m.ProjectsShipped.Count)}");
        }

        if (m.CommitCount > 0)
        {
            parts.Add($"{m.CommitCount} commits (+{m.Insertions}/-{m.Deletions})");
        }

        return string.Join(", ", parts) + ".";
    }

    /// <summary>
    /// States what this digest cannot see, deliberately and every time. A
    /// digest that overstates output is worthless; one that silently
    /// understates it is wrong in the other direction, and the easier one to
    /// ship by accident — see the plan's Phase 6.2.
    /// </summary>
    private static string Footer(DigestMetrics m)
    {
        var sb = new StringBuilder("---\n");

        if (m.UnattachedCommitsInRange > 0)
        {
            sb.AppendLine($"*{m.UnattachedCommitsInRange} commit{Plural(m.UnattachedCommitsInRange)} in this range could not be linked to a session — usually because it predates the collector or landed outside any tracked window. Counted here, not hidden.*");
        }

        if (m.UnclassifiedSeconds > 0)
        {
            sb.AppendLine($"*{Hours((int)m.UnclassifiedSeconds)} of tracked time is still unclassified and excluded from the figures above.*");
        }

        sb.AppendLine("*Uncommitted work is not reflected here — devlog currently sees only what reached git. See Phase 8.1.*");

        return sb.ToString();
    }

    private static string Plural(int n) => n == 1 ? "" : "s";

    private static string Hours(int seconds) => $"{seconds / 3600.0:0.#}h";

    /// <summary>Mirrors <c>StatsReporter.FormatHeld</c> / <c>format.ts</c>'s <c>formatDuration</c> — the same shape everywhere a duration is shown.</summary>
    private static string Hms(int seconds) => seconds switch
    {
        <= 0 => "—",
        < 60 => $"{seconds}s",
        < 3600 => $"{seconds / 60}m{seconds % 60:00}s",
        _ => $"{seconds / 3600}h{seconds % 3600 / 60:00}m"
    };
}
