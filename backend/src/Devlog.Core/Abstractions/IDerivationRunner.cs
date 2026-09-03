using Devlog.Core.Domain;

namespace Devlog.Core.Abstractions;

/// <summary>
/// Rebuilds the derived half of the database. Split out purely so
/// <c>Devlog.Api</c>'s <c>POST /v1/derive</c> can call it without a project
/// reference to <c>Devlog.Host</c> — which would be circular, since
/// <c>Devlog.Host</c> already references <c>Devlog.Api</c> to map the routes.
/// </summary>
public interface IDerivationRunner
{
    Task<DerivationResult> RunAsync(CancellationToken ct = default);
}
