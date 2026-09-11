using Devlog.Core.Ai;

namespace Devlog.Core.Abstractions;

/// <summary>
/// Weekly win summaries for the Month view — Job C's prose, run once per
/// calendar week within the requested range instead of once for the whole
/// range. Split out purely so <c>Devlog.Api</c>'s <c>POST /v1/weekly-wins</c>
/// can call it without a project reference to <c>Devlog.Host</c> — which
/// would be circular, since <c>Devlog.Host</c> already references
/// <c>Devlog.Api</c> to map the routes.
/// </summary>
public interface IWeeklyWinRunner
{
    Task<WeeklyWinResult> RunAsync(DateOnly monthFrom, DateOnly monthTo, bool force, CancellationToken ct = default);
}
