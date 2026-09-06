using Devlog.Core.Domain;

namespace Devlog.Core.Abstractions;

/// <summary>
/// Reads and writes derived session narratives.
/// </summary>
public interface INarrativeStore
{
    Task<SessionNarrative?> GetByStartUtcAsync(long sessionStartUtc, CancellationToken ct = default);
    Task<List<SessionNarrative>> GetRangeAsync(long fromUtc, long toUtc, CancellationToken ct = default);
    Task<List<SessionNarrative>> GetAllAsync(CancellationToken ct = default);
    Task UpsertAsync(SessionNarrative narrative, CancellationToken ct = default);
    Task RelinkSessionIdsAsync(IReadOnlyList<Session> sessions, CancellationToken ct = default);
    Task DeleteAsync(long sessionStartUtc, CancellationToken ct = default);
    Task ClearAllAsync(CancellationToken ct = default);
}
