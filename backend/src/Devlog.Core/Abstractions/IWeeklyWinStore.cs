using Devlog.Core.Domain;

namespace Devlog.Core.Abstractions;

/// <summary>
/// Reads and writes derived weekly-win summaries. Same shape as
/// <see cref="INarrativeStore"/>: a plain store, no business logic.
/// </summary>
public interface IWeeklyWinStore
{
    Task<WeeklyWin?> GetAsync(DateOnly weekFrom, DateOnly weekTo, CancellationToken ct = default);

    /// <summary>Whichever weeks already have a stored win within the range — no model call, a plain read, same shape as <see cref="INarrativeStore.GetRangeAsync"/>.</summary>
    Task<List<WeeklyWin>> GetRangeAsync(DateOnly monthFrom, DateOnly monthTo, CancellationToken ct = default);

    Task UpsertAsync(WeeklyWin win, CancellationToken ct = default);
}
