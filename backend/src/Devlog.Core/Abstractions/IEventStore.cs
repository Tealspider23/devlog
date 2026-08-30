using Devlog.Core.Domain;

namespace Devlog.Core.Abstractions;

/// <summary>
/// Append-only writes to the source-of-truth table. There is deliberately no
/// update or delete: <c>raw_event</c> is never rewritten, only derived from.
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// Appends a batch in a single transaction. Batching exists to avoid an
    /// fsync per row — the collector buffers in memory and flushes periodically.
    /// </summary>
    Task AppendAsync(IReadOnlyList<RawEvent> events, CancellationToken ct = default);

    /// <summary>
    /// The most recent event, used on startup to decide whether the previous run
    /// ended cleanly (a trailing <see cref="EventKind.CollectorStop"/>) or crashed.
    /// </summary>
    Task<RawEvent?> GetLatestAsync(CancellationToken ct = default);

    Task<long> CountAsync(CancellationToken ct = default);

    /// <summary>
    /// Events in a time window, oldest first. Derivation reads the whole log by
    /// default — at a few hundred rows a day that is cheap, and reprocessing
    /// everything is what keeps re-derivation genuinely idempotent rather than
    /// incremental and subtly stateful.
    /// </summary>
    Task<List<RawEvent>> GetRangeAsync(
        long? fromUtc = null,
        long? toUtc = null,
        CancellationToken ct = default);
}
