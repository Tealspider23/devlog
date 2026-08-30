namespace Devlog.Core.Domain;

/// <summary>
/// A layer-1 <em>event</em>: a moment at which something changed.
/// <para>
/// SOURCE OF TRUTH. Append-only, never rewritten. Everything else in the system
/// is derived from this table and can be thrown away and rebuilt.
/// </para>
/// <para>
/// Stores <b>raw</b> titles and <b>raw</b> idle seconds — never a pre-computed
/// category or idle boolean. That is what makes thresholds and normalization
/// rules retunable later without re-collecting a week of your life.
/// </para>
/// </summary>
public sealed record RawEvent
{
    /// <summary>Autoincrement row id. Zero until persisted.</summary>
    public long Id { get; init; }

    /// <summary>
    /// Unix milliseconds, UTC. Never a local <c>DateTime</c> — timezone and DST
    /// bugs in time-series data are miserable to unpick after the fact.
    /// </summary>
    public required long TsUtc { get; init; }

    public required EventKind Kind { get; init; }

    public string? ProcessName { get; init; }

    /// <summary>Raw, unnormalized, exactly as the OS reported it.</summary>
    public string? WindowTitle { get; init; }

    /// <summary>Best-effort; null for elevated processes we can't inspect.</summary>
    public string? ExePath { get; init; }

    /// <summary>Seconds since last input at capture time. A measurement, not a flag.</summary>
    public required int IdleSeconds { get; init; }

    public DateTimeOffset Timestamp => DateTimeOffset.FromUnixTimeMilliseconds(TsUtc);

    public static RawEvent From(
        EventKind kind,
        DateTimeOffset atUtc,
        ForegroundSnapshot? snapshot = null) => new()
        {
            TsUtc = atUtc.ToUnixTimeMilliseconds(),
            Kind = kind,
            ProcessName = snapshot?.ProcessName,
            WindowTitle = snapshot?.WindowTitle,
            ExePath = snapshot?.ExePath,
            IdleSeconds = snapshot?.IdleSeconds ?? 0
        };
}
