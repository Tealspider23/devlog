namespace Devlog.Core.Configuration;

/// <summary>
/// One local clone, mapped to the logical project it belongs to.
/// <para>
/// The mapping is many-to-one on purpose: window titles cannot distinguish two
/// clones of the same service (e.g. a repo checked out under two different
/// parent folders), so both are configured with the same <see cref="Project"/>
/// and their commits and attention time combine.
/// </para>
/// </summary>
public sealed class RepoConfig
{
    public required string Path { get; set; }

    public required string Project { get; set; }
}

/// <summary>
/// Everything Phase 3 needs. Kept separate from <see cref="DevlogOptions"/> and
/// <see cref="DerivationOptions"/> because it governs a different pipeline —
/// on-demand git scanning, never the capture hot path.
/// </summary>
public sealed class GitOptions
{
    public const string SectionName = "Git";

    /// <summary>
    /// Local clones to scan. A clone with no configured project is skipped
    /// rather than guessed — silently attributing commits to the wrong or
    /// unintended project would be worse than not scanning it at all.
    /// </summary>
    public List<RepoConfig> Repos { get; set; } = [];

    /// <summary>
    /// Additional author identities beyond each repo's own <c>user.email</c> and
    /// the global config — for historical addresses, or repos cloned before an
    /// identity change. Matched case-insensitively.
    /// </summary>
    public string[] AuthorEmails { get; set; } = [];

    /// <summary>
    /// How far back <c>--scan-git</c> looks. Diff computation is the expensive
    /// part of a scan, so this bounds it rather than walking full history.
    /// </summary>
    public int ScanLookbackDays { get; set; } = 90;

    /// <summary>
    /// How far a commit may sit from a session and still attach to it. Keeps a
    /// commit made moments after a session's last recorded activity from being
    /// stranded as unattached.
    /// </summary>
    public int CommitAttachWindowMinutes { get; set; } = 30;

    public TimeSpan ScanLookback => TimeSpan.FromDays(Math.Max(1, ScanLookbackDays));

    public TimeSpan CommitAttachWindow => TimeSpan.FromMinutes(Math.Max(0, CommitAttachWindowMinutes));
}
