using Devlog.Core.Domain;

namespace Devlog.Core.Ai;

/// <summary>One identity's verdict from a <see cref="Abstractions.IClassifyAiRunner"/> run.</summary>
public sealed record ClassifyAiVerdictOutcome(
    string Identity, ActivityCategory Category, double Confidence, string Reason);

/// <summary>
/// The structured result of a <c>classify-ai</c> run. Replaces the previous
/// bare <c>int</c> + <c>Console.WriteLine</c> shape so the CLI and
/// <c>POST /v1/classify-ai</c> can render the same run without disagreeing —
/// same discipline as <see cref="NarrateResult"/>: one result, two renderers.
/// </summary>
public sealed record ClassifyAiResult(
    bool DryRun,
    bool Reachable,
    int ProcessedCount,
    int SkippedCount,
    int TotalPendingRemaining,
    IReadOnlyList<ClassifyAiVerdictOutcome> Verdicts,
    IReadOnlyList<string> Discards,
    string? UnreachableReason);
