using Devlog.Core.Domain;

namespace Devlog.Core.Abstractions;

public interface ICommitStore
{
    /// <summary>
    /// Every known sha, for the scanner to skip commits it has already recorded
    /// before touching a tree. This is what makes a repeat scan pay only for
    /// genuinely new commits instead of re-diffing the whole lookback window.
    /// </summary>
    Task<HashSet<string>> GetKnownShasAsync(CancellationToken ct = default);

    /// <summary>
    /// Inserts newly scanned commits, or updates their branch/session linkage if
    /// the sha already exists. A sha is scanned at most once for its diff stats —
    /// re-runs only ever touch branch and session_id.
    /// </summary>
    Task UpsertAsync(IReadOnlyList<CommitRecord> commits, CancellationToken ct = default);

    /// <summary>All commits, ordered by time, for linking and for <c>--commits</c>.</summary>
    Task<List<CommitRecord>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Rewrites every row's <c>session_id</c> — the cheap half of a re-derive.</summary>
    Task RelinkAsync(IReadOnlyDictionary<string, long?> shaToSessionId, CancellationToken ct = default);
}
