namespace Devlog.Api.Contracts;

/// <summary>One day's picture: the sessions in it, the commits in it, and what is still unanswered.</summary>
public sealed record TimelineDto(
    string Date,
    IReadOnlyList<SessionDto> Sessions,
    IReadOnlyList<CommitDto> Commits,
    long UnclassifiedSeconds);

public sealed record SessionDetailDto(
    SessionDto Session,
    IReadOnlyList<ActivityDto> Activities,
    IReadOnlyList<CommitDto> Commits);

/// <summary>An identity from <c>classification_rule</c> still awaiting a verdict.</summary>
public sealed record PendingIdentityDto(string Identity, int Hits, int TotalSeconds);

/// <summary>Body of <c>POST /v1/classify</c> — the manual override path.</summary>
public sealed record ClassifyRequest(string Identity, string Category, string? Keyword);

public sealed record ClassifyResponse(string Identity, string Category, bool PromotedToMixed);

/// <summary>Mirrors <c>DerivationResult</c> for <c>POST /v1/derive</c>.</summary>
public sealed record DeriveResultDto(
    int RawEvents,
    int AfterNoise,
    int Activities,
    int Sessions,
    int PendingIdentities,
    int UnclassifiedSeconds,
    int CommitsLinked,
    int CommitsUnattached,
    double ElapsedMs);

/// <summary>Mirrors <c>GitScanSummary</c> for <c>POST /v1/scan-git</c>.</summary>
public sealed record GitScanResultDto(int Scanned, int Skipped, int ReposFailed);

/// <summary>
/// A digest range's deterministic figures, plus the rendered Markdown. Both
/// come from the same <c>DigestMetrics</c> that <c>devlog digest</c> renders —
/// see <c>Devlog.Core.Metrics.DigestBuilder</c>. The UI draws its cards from
/// the structured fields and its copy button from <c>Markdown</c>, so the two
/// can never say different numbers.
/// </summary>
public sealed record DigestDto(
    string From,
    string To,
    int TrackedSeconds,
    int DeepSeconds,
    double FocusRatio,
    int SessionCount,
    int ActiveDays,
    int InterruptionsTotal,
    double InterruptionsPerActiveDay,
    LongestBlockDto? LongestBlock,
    BestDayDto? BestDay,
    IReadOnlyList<ProjectTimeDto> TimeByProject,
    IReadOnlyList<CategoryTimeDto> TimeByCategory,
    int ZeroOutputSessionCount,
    int ZeroOutputSeconds,
    int CommitCount,
    int Insertions,
    int Deletions,
    IReadOnlyList<string> ProjectsShipped,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> FirstTimeLanguages,
    IReadOnlyList<string> TicketIds,
    int UnattachedCommitsInRange,
    long UnclassifiedSeconds,
    string Markdown)
{
    public static DigestDto FromMetrics(Devlog.Core.Metrics.DigestMetrics m, string markdown) => new(
        m.From.ToString("O"),
        m.To.ToString("O"),
        m.TrackedSeconds,
        m.DeepSeconds,
        m.FocusRatio,
        m.SessionCount,
        m.ActiveDays,
        m.InterruptionsTotal,
        m.InterruptionsPerActiveDay,
        m.LongestBlock is { } lb ? new LongestBlockDto(lb.Start.ToLocalTime().ToString("O"), lb.End.ToLocalTime().ToString("O"), lb.Project, lb.DeepSeconds) : null,
        m.BestDay is { } bd ? new BestDayDto(bd.Date.ToString("O"), bd.DeepSeconds) : null,
        [.. m.TimeByProject.Select(p => new ProjectTimeDto(p.Project, p.Seconds))],
        [.. m.TimeByCategory.Select(c => new CategoryTimeDto(c.Category.ToString(), c.Seconds))],
        m.ZeroOutputSessionCount,
        m.ZeroOutputSeconds,
        m.CommitCount,
        m.Insertions,
        m.Deletions,
        m.ProjectsShipped,
        m.Languages,
        m.FirstTimeLanguages,
        m.TicketIds,
        m.UnattachedCommitsInRange,
        m.UnclassifiedSeconds,
        markdown);
}

public sealed record LongestBlockDto(string StartIso, string EndIso, string? Project, int DeepSeconds);

public sealed record BestDayDto(string Date, int DeepSeconds);

public sealed record ProjectTimeDto(string Project, int Seconds);

public sealed record CategoryTimeDto(string Category, int Seconds);
