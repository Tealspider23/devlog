using Devlog.Core.Ai;

namespace Devlog.Core.Abstractions;

/// <summary>
/// Job A — identity classification. Split out purely so <c>Devlog.Api</c>'s
/// <c>POST /v1/classify-ai</c> can call it without a project reference to
/// <c>Devlog.Host</c> — which would be circular, since <c>Devlog.Host</c>
/// already references <c>Devlog.Api</c> to map the routes.
/// </summary>
public interface IClassifyAiRunner
{
    /// <param name="force">Bypasses the recently-attempted exclusion (see <see cref="IClassifyAttemptStore"/>) — sends identities the model already declined to answer confidently again anyway.</param>
    Task<ClassifyAiResult> RunAsync(bool dryRun, int? limitOverride, bool force = false, CancellationToken ct = default);
}
