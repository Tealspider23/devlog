namespace Devlog.Core.Domain;

public enum RuleScope
{
    /// <summary>Applies to everything from one site or process. The default.</summary>
    Site = 0,

    /// <summary>
    /// Applies to titles from one site containing a keyword. Only created for
    /// sites that turn out to be mixed-use.
    /// </summary>
    Page = 1
}

/// <summary>
/// A learned answer to "what kind of time is this?".
/// <para>
/// SOURCE OF TRUTH — never rebuilt. The premise of the whole classification
/// design is that you answer per <em>thing</em>, once, not per occurrence: three
/// pages of MCP documentation are one question about "Model Context Protocol",
/// not three questions, and never a question again.
/// </para>
/// </summary>
public sealed record ClassificationRule
{
    public long Id { get; init; }

    public required RuleScope Scope { get; init; }

    /// <summary>Site identity for browsers, process name otherwise.</summary>
    public required string Site { get; init; }

    /// <summary>Null for site-scope rules. Matched case-insensitively against the title.</summary>
    public string? Keyword { get; init; }

    /// <summary>Null means pending — seen, counted, not yet answered.</summary>
    public ActivityCategory? Category { get; init; }

    /// <summary>
    /// Where the answer came from: <c>builtin</c>, <c>llm</c>, or <c>manual</c>.
    /// <para>
    /// The table is a cache of verdicts and genuinely does not care who supplied
    /// one — which is what lets a local model fill these in later without any
    /// redesign, while a manual answer stays available to override it.
    /// </para>
    /// </summary>
    public string SourceName { get; init; } = "manual";

    /// <summary>
    /// Set once you answer the same site two different ways. From then on this
    /// site is asked about per page, and keyword rules accumulate beneath it.
    /// YouTube promotes itself the first time you disagree with yourself.
    /// </summary>
    public bool IsMixed { get; init; }

    public int Hits { get; init; }

    /// <summary>Drives the <c>--unknowns</c> ordering, so expensive things get answered first.</summary>
    public int TotalSeconds { get; init; }

    public long? LastSeenUtc { get; init; }

    public long CreatedUtc { get; init; }

    public bool IsPending => Category is null;
}
