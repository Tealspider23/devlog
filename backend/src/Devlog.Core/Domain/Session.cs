namespace Devlog.Core.Domain;

/// <summary>
/// A layer-3 session: a meaningful unit of work built from consecutive
/// activities that share intent.
/// <para>
/// Coding sessions are keyed by project, so two different repositories never
/// merge. Non-coding sessions are keyed by category only, so a run of
/// documentation pages becomes one learning block rather than one session per
/// page.
/// </para>
/// <para>DERIVED and disposable — but see <see cref="SessionOverride"/>.</para>
/// </summary>
public sealed record Session
{
    public long Id { get; init; }

    public required long StartUtc { get; init; }

    public required long EndUtc { get; init; }

    /// <summary>Stable identity across rebuilds; half of the override key.</summary>
    public required string ActivityKey { get; init; }

    public string? Project { get; init; }

    public required ActivityCategory Category { get; init; }

    /// <summary>
    /// Short excursions folded back in — a quick search mid-refactor. Counted
    /// rather than allowed to shatter the session, but excluded from
    /// <see cref="DeepSeconds"/>.
    /// </summary>
    public required int Interruptions { get; init; }

    /// <summary>Producing time only, excluding folded excursions.</summary>
    public required int DeepSeconds { get; init; }

    /// <summary>Set from a <see cref="SessionOverride"/> when you have corrected it.</summary>
    public string? Label { get; init; }

    public int DurationSeconds => (int)((EndUtc - StartUtc) / 1000);

    public DateTimeOffset Start => DateTimeOffset.FromUnixTimeMilliseconds(StartUtc);

    public DateTimeOffset End => DateTimeOffset.FromUnixTimeMilliseconds(EndUtc);
}

/// <summary>
/// A manual correction to a derived session.
/// <para>
/// SOURCE OF TRUTH — never rebuilt. Keyed by <c>(StartUtc, ActivityKey)</c>
/// rather than session id, because ids do not survive a rebuild but your
/// corrections must.
/// </para>
/// </summary>
public sealed record SessionOverride
{
    public required long SessionStartUtc { get; init; }

    public required string ActivityKey { get; init; }

    public ActivityCategory? Category { get; init; }

    public string? Label { get; init; }
}
