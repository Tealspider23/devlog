using Devlog.Core.Metrics;

namespace Devlog.Core.Abstractions;

/// <summary>
/// Job C — opening prose for a digest range, composed around figures
/// <see cref="Metrics.DigestMetrics"/> already computed. Split out purely so
/// <c>Devlog.Api</c>'s <c>GET /v1/digest?prose=true</c> can call it without a
/// project reference to <c>Devlog.Host</c> — which would be circular, since
/// <c>Devlog.Host</c> already references <c>Devlog.Api</c> to map the routes.
/// </summary>
public interface IDigestProseRunner
{
    Task<(string? ProseMarkdown, string? Note)> GenerateProseAsync(
        DigestMetrics metrics,
        long fromUtc,
        long toUtc,
        CancellationToken ct = default);
}
