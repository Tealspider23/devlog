namespace Devlog.Core.Abstractions;

/// <summary>
/// The forensic trail an AI job's HTTP request leaves — what devlog's own
/// log file never recorded before Phase 15, which is how a failed narrate
/// run could leave zero evidence of what was sent, what came back, or what
/// it cost. Written once, from <c>ChatClassifier</c>, the one place every
/// job's requests already funnel through — not duplicated per job.
/// </summary>
public interface ILlmRequestLog
{
    Task RecordAsync(string job, string? model, int? httpStatus, bool ok, string? error, CancellationToken ct = default);

    /// <summary>Requests recorded since local midnight — devlog's own count, not the provider's; the two can drift if the key is used elsewhere.</summary>
    Task<int> CountTodayAsync(CancellationToken ct = default);
}
