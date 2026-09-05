namespace Devlog.Core.Domain;

/// <summary>
/// A layer-2 activity: a maximal continuous interval during which the activity
/// key stayed constant.
/// <para>
/// DERIVED and disposable. Dropped and rebuilt wholesale from <c>raw_event</c>
/// every time a rule or threshold changes.
/// </para>
/// </summary>
public sealed record Activity
{
    public long Id { get; init; }

    public required long StartUtc { get; init; }

    public required long EndUtc { get; init; }

    public string? ProcessName { get; init; }

    /// <summary>Process + extracted context — what makes two moments "the same thing".</summary>
    public required string ActivityKey { get; init; }

    /// <summary>The stable part: repo, channel, folder, page. Not necessarily a project — see <see cref="Project"/>.</summary>
    public string? Context { get; init; }

    /// <summary>
    /// The repository this belongs to, set only when an extraction rule genuinely
    /// resolved one — null for a browser tab, an unrecognised app, or a terminal
    /// in a directory that is not a known repo.
    /// <para>
    /// Deliberately distinct from <see cref="Context"/>. Promoting context to
    /// project unconditionally is what made the digest list "GitLab", "Windows
    /// PowerShell" and a raw SSMS window title among its projects.
    /// </para>
    /// </summary>
    public string? Project { get; init; }

    /// <summary>
    /// What was being looked at, for classification purposes — a site name for
    /// browsers, the process name otherwise. This is what you answer questions
    /// about, once, rather than per occurrence.
    /// </summary>
    public string? SiteIdentity { get; init; }

    public required ActivityCategory Category { get; init; }

    public required Engagement Engagement { get; init; }

    /// <summary>
    /// How many times the raw title changed inside this activity — files opened
    /// during a refactor, pages read on one site. Volatile detail, collapsed.
    /// </summary>
    public required int TitleChanges { get; init; }

    /// <summary>One representative raw title, for display and debugging.</summary>
    public string? SampleTitle { get; init; }

    public long? SessionId { get; init; }

    public int DurationSeconds => (int)((EndUtc - StartUtc) / 1000);

    public DateTimeOffset Start => DateTimeOffset.FromUnixTimeMilliseconds(StartUtc);

    public DateTimeOffset End => DateTimeOffset.FromUnixTimeMilliseconds(EndUtc);
}
