using Devlog.Core.Ai;
using Devlog.Core.Domain;

namespace Devlog.Api.Contracts;

/// <summary>
/// <c>GET /v1/ai/status</c> — the <c>devlog llm</c> screen as data. Never
/// carries the API key itself, same rule the CLI follows: presence only.
/// </summary>
/// <summary>Body of <c>POST /v1/ai/key</c>. A blank/whitespace key clears whatever is stored.</summary>
public sealed record SetAiApiKeyRequestDto(string? ApiKey);

/// <summary>Same rule as <see cref="AiStatusDto"/> — the key value is never echoed back, only whether one is now present.</summary>
public sealed record AiApiKeyResultDto(bool ApiKeyPresent);

public sealed record AiStatusDto(
    bool Enabled,
    string ConfiguredModel,
    bool ApiKeyPresent,
    bool JobClassifyEnabled,
    bool JobNarrateEnabled,
    bool JobDigestEnabled,
    bool JobAskEnabled,
    bool Reachable,
    string? Endpoint,
    bool? OffMachine,
    string? Host,
    int RequestsToday = 0,
    int RequestsPerDay = 0);

public sealed record AiModelsDto(IReadOnlyList<string> Models);

/// <summary>Body of <c>POST /v1/ask</c>. <c>Model</c> is the Chat page's per-query override; null uses the configured default.</summary>
public sealed record AskRequestDto(string Question, string? Model);

public sealed record AskResponseDto(
    bool Success,
    string? Answer,
    string? Model,
    int ToolRounds,
    IReadOnlyList<string> ToolsUsed,
    IReadOnlyList<string> UnverifiedNumbers,
    string? Error)
{
    public static AskResponseDto From(AskResult r) =>
        new(r.Success, r.Answer, r.Model, r.ToolRounds, r.ToolsUsed, r.UnverifiedNumbers, r.Error);
}

public sealed record NarrativeDto(
    string SessionStart,
    string SessionEnd,
    long? SessionId,
    string Narrative,
    string Kind,
    string? Workstream,
    IReadOnlyList<string> Evidence,
    double Confidence,
    string Model)
{
    public static NarrativeDto From(SessionNarrative n) => new(
        DateTimeOffset.FromUnixTimeMilliseconds(n.SessionStartUtc).ToLocalTime().ToString("O"),
        DateTimeOffset.FromUnixTimeMilliseconds(n.SessionEndUtc).ToLocalTime().ToString("O"),
        n.SessionId,
        n.Narrative,
        n.Kind,
        n.Workstream,
        n.Evidence,
        n.Confidence,
        n.Model);
}

/// <summary>Body of <c>POST /v1/narrate</c>. Every field optional — an empty body reruns the CLI's own defaults.</summary>
public sealed record NarrateRequestDto(string? Since, int? Limit, bool? DryRun, bool? Force);

public sealed record NarrateOutcomeDto(
    long SessionId,
    string SessionStart,
    string? Project,
    int DurationSeconds,
    bool Accepted,
    NarrativeDto? Narrative,
    string? RejectionReason);

public sealed record NarrateResultDto(
    bool DryRun,
    int AcceptedCount,
    int RejectedCount,
    IReadOnlyList<NarrateOutcomeDto> Outcomes,
    bool StoppedEarly = false,
    string? StopReason = null)
{
    public static NarrateResultDto From(NarrateResult r) => new(
        r.DryRun,
        r.AcceptedCount,
        r.RejectedCount,
        [.. r.Outcomes.Select(o => new NarrateOutcomeDto(
            o.SessionId,
            DateTimeOffset.FromUnixTimeMilliseconds(o.SessionStartUtc).ToLocalTime().ToString("O"),
            o.Project,
            o.DurationSeconds,
            o.Accepted,
            o.Narrative is null ? null : NarrativeDto.From(o.Narrative),
            o.RejectionReason))],
        r.StoppedEarly,
        r.StopReason);
}

/// <summary>Body of <c>POST /v1/weekly-wins</c>. <c>From</c>/<c>To</c> are the month's own range — the endpoint splits it into weeks itself.</summary>
public sealed record WeeklyWinsRequestDto(string From, string To, bool? Force);

public sealed record WeeklyWinDto(
    string WeekFrom,
    string WeekTo,
    string Summary,
    IReadOnlyList<string> Highlights,
    string Model)
{
    public static WeeklyWinDto From(WeeklyWin w) => new(
        w.WeekFrom.ToString("yyyy-MM-dd"),
        w.WeekTo.ToString("yyyy-MM-dd"),
        w.Summary,
        w.Highlights,
        w.Model);
}

public sealed record WeekOutcomeDto(
    string WeekFrom,
    string WeekTo,
    bool Accepted,
    bool SkippedAsUpToDate,
    WeeklyWinDto? Win,
    string? RejectionReason);

public sealed record WeeklyWinsResultDto(
    IReadOnlyList<WeekOutcomeDto> Weeks,
    bool StoppedEarly = false,
    string? StopReason = null)
{
    public static WeeklyWinsResultDto From(WeeklyWinResult r) => new(
        [.. r.Weeks.Select(w => new WeekOutcomeDto(
            w.WeekFrom.ToString("yyyy-MM-dd"),
            w.WeekTo.ToString("yyyy-MM-dd"),
            w.Accepted,
            w.SkippedAsUpToDate,
            w.Win is null ? null : WeeklyWinDto.From(w.Win),
            w.RejectionReason))],
        r.StoppedEarly,
        r.StopReason);
}
