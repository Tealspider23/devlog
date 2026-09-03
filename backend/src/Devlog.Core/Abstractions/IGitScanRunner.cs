using Devlog.Core.Domain;

namespace Devlog.Core.Abstractions;

/// <summary>
/// Walks configured repos on disk for new commits. Split out so
/// <c>Devlog.Api</c>'s <c>POST /v1/scan-git</c> can call it without a project
/// reference to <c>Devlog.Host</c> — same reasoning as <see cref="IDerivationRunner"/>.
/// </summary>
public interface IGitScanRunner
{
    Task<GitScanSummary> RunAsync(CancellationToken ct = default);
}
