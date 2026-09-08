using Devlog.Core.Domain;

namespace Devlog.Core.Ai;

/// <summary>
/// One session's outcome from a <see cref="Abstractions.INarrateRunner"/> run —
/// accepted with its narrative, or rejected/skipped with why.
/// </summary>
public sealed record NarrateOutcome(
    long SessionId,
    long SessionStartUtc,
    string? Project,
    int DurationSeconds,
    bool Accepted,
    SessionNarrative? Narrative,
    string? RejectionReason);

/// <summary>
/// The structured result of a <c>narrate</c> run. Replaces the previous
/// bare <c>int</c> + <c>Console.WriteLine</c> shape so the CLI and
/// <c>POST /v1/narrate</c> can render the same run without disagreeing —
/// same discipline as <c>ISessionReader</c>: one result, two renderers.
/// </summary>
public sealed record NarrateResult(
    bool DryRun,
    int AcceptedCount,
    int RejectedCount,
    IReadOnlyList<NarrateOutcome> Outcomes);
