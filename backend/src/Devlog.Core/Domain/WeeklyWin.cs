namespace Devlog.Core.Domain;

/// <summary>
/// A synthesized one-week summary for the Month view — Job C's prose, run once
/// per calendar week instead of once for a whole range. Derived and
/// re-runnable, like <see cref="SessionNarrative"/>, but keyed on
/// (WeekFrom, WeekTo) rather than a session id: a week's boundaries are never
/// reassigned by <c>devlog derive</c> the way session ids are.
/// </summary>
public sealed record WeeklyWin
{
    public required DateOnly WeekFrom { get; init; }
    public required DateOnly WeekTo { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<string> Highlights { get; init; }
    public required int NarrativeCount { get; init; }
    public required long NarrativesMaxGeneratedUtc { get; init; }
    public required string Model { get; init; }
    public required long GeneratedUtc { get; init; }

    /// <summary>
    /// Stale when the week's narratives have changed since this was generated
    /// — a new narrative written, an existing one re-narrated, or a model
    /// change. Mirrors <see cref="SessionNarrative.IsStale"/>.
    /// </summary>
    public bool IsStale(int narrativeCount, long narrativesMaxGeneratedUtc, string model) =>
        narrativeCount != NarrativeCount
        || narrativesMaxGeneratedUtc != NarrativesMaxGeneratedUtc
        || !string.Equals(model, Model, StringComparison.Ordinal);
}
