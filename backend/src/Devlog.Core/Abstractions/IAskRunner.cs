using Devlog.Core.Ai;

namespace Devlog.Core.Abstractions;

/// <summary>
/// Job G — natural-language query over the user's own data. Split out purely so
/// <c>Devlog.Api</c>'s <c>POST /v1/ask</c> can call it without a project
/// reference to <c>Devlog.Host</c> — which would be circular, since
/// <c>Devlog.Host</c> already references <c>Devlog.Api</c> to map the routes.
/// </summary>
public interface IAskRunner
{
    Task<AskResult> AskAsync(string question, string? model = null, bool quiet = false, CancellationToken ct = default);
}
