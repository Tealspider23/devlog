using Devlog.Core.Domain;

namespace Devlog.Core.Ai;

/// <summary>
/// One week's outcome from a <see cref="Abstractions.IWeeklyWinRunner"/> run —
/// accepted with its win, skipped because it was already up to date, or
/// rejected with why. Same "one result, two renderers" shape as
/// <see cref="NarrateResult"/>.
/// </summary>
public sealed record WeekOutcome(
    DateOnly WeekFrom,
    DateOnly WeekTo,
    bool Accepted,
    bool SkippedAsUpToDate,
    WeeklyWin? Win,
    string? RejectionReason);

/// <param name="StoppedEarly">Same reasoning as <see cref="NarrateResult.StoppedEarly"/> — a rate-limited week stops the remaining weeks from being attempted rather than letting each fail the same way.</param>
public sealed record WeeklyWinResult(
    IReadOnlyList<WeekOutcome> Weeks,
    bool StoppedEarly = false,
    string? StopReason = null);
