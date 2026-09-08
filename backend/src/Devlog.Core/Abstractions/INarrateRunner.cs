using Devlog.Core.Ai;

namespace Devlog.Core.Abstractions;

/// <summary>
/// Job B — session narration. Split out purely so <c>Devlog.Api</c>'s
/// <c>POST /v1/narrate</c> can call it without a project reference to
/// <c>Devlog.Host</c> — which would be circular, since <c>Devlog.Host</c>
/// already references <c>Devlog.Api</c> to map the routes.
/// </summary>
public interface INarrateRunner
{
    Task<NarrateResult> RunAsync(
        string? sinceArg,
        int? limitOverride,
        bool dryRun,
        bool force,
        CancellationToken ct = default);
}
