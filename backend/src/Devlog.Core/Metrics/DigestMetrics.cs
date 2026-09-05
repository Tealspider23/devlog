using Devlog.Core.Domain;

namespace Devlog.Core.Metrics;

/// <summary>
/// One project's share of a digest range.
/// </summary>
public sealed record ProjectTime(string Project, int Seconds);

/// <summary>
/// One category's share of a digest range.
/// </summary>
public sealed record CategoryTime(ActivityCategory Category, int Seconds);

/// <summary>
/// The single longest uninterrupted session in a digest range — the "what was
/// the best block of work" answer, not a list.
/// </summary>
public sealed record LongestBlock(long StartUtc, long EndUtc, string? Project, int DeepSeconds)
{
    public DateTimeOffset Start => DateTimeOffset.FromUnixTimeMilliseconds(StartUtc);
    public DateTimeOffset End => DateTimeOffset.FromUnixTimeMilliseconds(EndUtc);
}

/// <summary>
/// The single best day in a digest range, by deep work.
/// </summary>
public sealed record BestDay(DateOnly Date, int DeepSeconds);

/// <summary>
/// The deterministic content of a brag document, computed once and rendered
/// twice — see <see cref="DigestWriter"/> for the CLI/API renderer, and
/// <c>Devlog.Api.Contracts.DigestDto</c> for the shape the UI reads to draw its
/// own cards from the same numbers.
/// <para>
/// Every field here is a fact about the range. Nothing here is prose and
/// nothing here is opinion — that is Job C's job (<c>docs/LLM.md</c>), and the
/// model is never trusted to compute a number a person might quote.
/// </para>
/// </summary>
public sealed record DigestMetrics
{
    public required DateOnly From { get; init; }

    public required DateOnly To { get; init; }

    public required int TrackedSeconds { get; init; }

    public required int DeepSeconds { get; init; }

    /// <summary>Deep ÷ tracked, or 0 when nothing was tracked — never a divide-by-zero.</summary>
    public required double FocusRatio { get; init; }

    public required int SessionCount { get; init; }

    /// <summary>Distinct calendar days with at least one session.</summary>
    public required int ActiveDays { get; init; }

    public required int InterruptionsTotal { get; init; }

    /// <summary>Interruptions ÷ active days, or 0 with no active days.</summary>
    public required double InterruptionsPerActiveDay { get; init; }

    public LongestBlock? LongestBlock { get; init; }

    public BestDay? BestDay { get; init; }

    public required IReadOnlyList<ProjectTime> TimeByProject { get; init; }

    public required IReadOnlyList<CategoryTime> TimeByCategory { get; init; }

    /// <summary>
    /// Coding time that resolved to no repository — a browser tab, SQL Server
    /// Management Studio, a bare shell.
    /// <para>
    /// Reported rather than dropped. <see cref="TimeByProject"/> necessarily
    /// excludes it, and letting hours disappear from a breakdown with no
    /// explanation would trade the old wrong labels for a quiet omission.
    /// </para>
    /// <para>
    /// Scoped to Coding deliberately: an unscoped "no project" bucket would
    /// sweep in every Learning and Communication session, which never have one
    /// by design, and the number would mean nothing.
    /// </para>
    /// </summary>
    public required int UnattributedCodingSeconds { get; init; }

    /// <summary>Sessions that shipped nothing — not a failure, usually research or debugging.</summary>
    public required int ZeroOutputSessionCount { get; init; }

    public required int ZeroOutputSeconds { get; init; }

    public required int CommitCount { get; init; }

    public required int Insertions { get; init; }

    public required int Deletions { get; init; }

    public required IReadOnlyList<string> ProjectsShipped { get; init; }

    public required IReadOnlyList<string> Languages { get; init; }

    /// <summary>
    /// A language that appears in this range but in no commit before it —
    /// e.g. the first commit ever touching Rust. Empty, not null, when nothing
    /// qualifies or there is no prior history to compare against.
    /// </summary>
    public required IReadOnlyList<string> FirstTimeLanguages { get; init; }

    /// <summary>Ticket ids pulled from branch names, e.g. <c>US-1569</c> from <c>fix/US-1569-Bug_Fixing</c>.</summary>
    public required IReadOnlyList<string> TicketIds { get; init; }

    /// <summary>How many commits before this range were unattached — the "58 of 83" honesty figure.</summary>
    public required int UnattachedCommitsInRange { get; init; }

    public required long UnclassifiedSeconds { get; init; }
}
