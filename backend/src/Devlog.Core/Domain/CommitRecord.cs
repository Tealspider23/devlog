namespace Devlog.Core.Domain;

/// <summary>
/// An artifact, not an activity. The collector never sees a commit happen —
/// committing from an IDE panel can produce zero focus events — so this is
/// discovered independently by scanning git history and joined to a session by
/// timestamp overlap.
/// <para>
/// DERIVED but re-scannable: <c>--scan-git</c> rebuilds it from the repos on
/// disk, and <c>--derive</c> re-links it to freshly rebuilt sessions without
/// touching disk. Both follow the same disposable contract as activities and
/// sessions — the git history itself is the real source of truth, not this table.
/// </para>
/// </summary>
public sealed record CommitRecord
{
    /// <summary>Full sha. Primary key — scanning twice must never duplicate a commit.</summary>
    public required string Sha { get; init; }

    /// <summary>The repo path this was scanned from, not necessarily the only clone.</summary>
    public required string Repo { get; init; }

    /// <summary>The logical project — see <see cref="Configuration.RepoConfig"/> for the many-to-one mapping.</summary>
    public required string Project { get; init; }

    public required long TsUtc { get; init; }

    public string? Message { get; init; }

    /// <summary>
    /// The branch that currently contains this commit, preferring the one
    /// checked out at scan time. Null when genuinely ambiguous — never guessed.
    /// </summary>
    public string? Branch { get; init; }

    public required string AuthorEmail { get; init; }

    public int FilesChanged { get; init; }

    public int Insertions { get; init; }

    public int Deletions { get; init; }

    /// <summary>Comma-separated, derived from file extensions.</summary>
    public string? Languages { get; init; }

    /// <summary>
    /// Excluded from scanning entirely — a merge diff is enormous and attributes
    /// other people's work to whoever merged it. Kept as a field rather than
    /// filtered at the query layer, so a future view of "merges I made" stays possible.
    /// </summary>
    public required bool IsMerge { get; init; }

    /// <summary>
    /// Set by linking, not by scanning. Null means genuinely unattached — not
    /// hidden, just not falling inside any session's window.
    /// </summary>
    public long? SessionId { get; init; }

    public DateTimeOffset Timestamp => DateTimeOffset.FromUnixTimeMilliseconds(TsUtc);
}
