namespace Devlog.Core.Domain;

/// <summary>
/// A session together with the counts that only exist once it is joined against
/// activities and commits.
/// <para>
/// A <em>read model</em>, deliberately not part of <see cref="Session"/> itself.
/// A session is defined by its own span and key; how many activities formed it
/// and how many commits landed inside it are facts about the rest of the
/// database, and putting them on the entity would mean the sessionizer had to
/// invent values for them at build time.
/// </para>
/// <para>
/// This is the shape both the terminal and the timeline consume, which is the
/// point: one query answers both, so they cannot disagree about what a session
/// was.
/// </para>
/// </summary>
public sealed record SessionSummary
{
    public required Session Session { get; init; }

    /// <summary>How many activities merged into this session.</summary>
    public required int ActivityCount { get; init; }

    public required int CommitCount { get; init; }
    public required int Insertions { get; init; }
    public required int Deletions { get; init; }

    /// <summary>
    /// True when a session produced no commits. Not a failure — usually
    /// debugging or research, and naming it honestly is what stops the brag
    /// document reading as a productivity scoreboard.
    /// </summary>
    public bool IsZeroOutput => CommitCount == 0;
}
