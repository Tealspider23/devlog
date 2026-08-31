using Devlog.Core.Abstractions;
using Devlog.Core.Configuration;
using Devlog.Core.Derivation;
using Devlog.Core.Domain;
using Devlog.Infrastructure.Git;

namespace Devlog.Host.Derivation;

public sealed record GitScanSummary(int Scanned, int Skipped, int ReposFailed);

/// <summary>
/// The disk-touching half of git enrichment. Kept separate from
/// <see cref="DerivationRunner"/>, which only ever reads what this already
/// wrote — <c>--derive</c> is meant to be cheap and offline, and folding a git
/// scan into it would break that.
/// </summary>
public sealed class GitScanRunner(
    ICommitStore commitStore,
    GitScanner scanner,
    GitOptions options,
    ILogger<GitScanRunner> logger)
{
    public async Task<GitScanSummary> RunAsync(CancellationToken ct = default)
    {
        var known = await commitStore.GetKnownShasAsync(ct).ConfigureAwait(false);
        var authors = AuthorIdentity.Resolve(options);
        var resolver = new ProjectResolver(options.Repos);

        if (authors.Count == 0)
        {
            logger.LogWarning(
                "No author identity resolved from any configured repo. "
                + "Nothing will be scanned — configure Git:Repos or Git:AuthorEmails.");
        }

        var newCommits = new List<CommitRecord>();

        var result = scanner.Scan(known, authors, resolver, newCommits.Add);

        await commitStore.UpsertAsync(newCommits, ct).ConfigureAwait(false);

        logger.LogInformation(
            "Scanned {New} new commits, skipped {Skipped} already known, {Failed} repos failed",
            result.Scanned, result.Skipped, result.ReposFailed);

        return new GitScanSummary(result.Scanned, result.Skipped, result.ReposFailed);
    }
}
