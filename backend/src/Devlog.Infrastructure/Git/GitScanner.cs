using Devlog.Core.Configuration;
using Devlog.Core.Derivation;
using Devlog.Core.Domain;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;

namespace Devlog.Infrastructure.Git;

public sealed record ScanResult(int Scanned, int Skipped, int ReposFailed);

/// <summary>
/// Walks configured local repos and turns your commits into
/// <see cref="CommitRecord"/>s.
/// <para>
/// Three structures keep this cheap even though diff computation is expensive
/// per-commit: a known-sha set skips anything already scanned before touching a
/// tree, so a repeat scan pays only for genuinely new commits; a branch→sha map
/// built by walking local branches once avoids the O(commits × refs) cost of
/// asking per-commit "which branch is this on"; and merge commits are excluded
/// outright, since their diffs are enormous and attribute other people's work to
/// whoever merged.
/// </para>
/// </summary>
public sealed class GitScanner(GitOptions options, ILogger<GitScanner> logger)
{
    public ScanResult Scan(
        HashSet<string> knownShas,
        HashSet<string> authorEmails,
        ProjectResolver resolver,
        Action<CommitRecord> onCommit)
    {
        var sinceUtc = DateTimeOffset.UtcNow - options.ScanLookback;
        int scanned = 0, skipped = 0, failed = 0;

        // Group by configured path so a repo listed once is scanned once, even
        // if (in principle) it were duplicated across two project entries.
        foreach (var repoPath in options.Repos.Select(r => r.Path).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(repoPath))
            {
                logger.LogWarning("Configured repo path does not exist, skipping: {Path}", repoPath);
                failed++;
                continue;
            }

            try
            {
                var (s, k) = ScanRepo(repoPath, sinceUtc, knownShas, authorEmails, resolver, onCommit);
                scanned += s;
                skipped += k;
            }
            catch (RepositoryNotFoundException)
            {
                logger.LogWarning("Configured path is not a git repository: {Path}", repoPath);
                failed++;
            }
        }

        return new ScanResult(scanned, skipped, failed);
    }

    private (int Scanned, int Skipped) ScanRepo(
        string repoPath,
        DateTimeOffset sinceUtc,
        HashSet<string> knownShas,
        HashSet<string> authorEmails,
        ProjectResolver resolver,
        Action<CommitRecord> onCommit)
    {
        using var repo = new Repository(repoPath);

        var branchOf = BuildBranchMap(repo);
        var project = resolver.Resolve(repoPath);

        if (project is null)
        {
            logger.LogWarning(
                "No project configured for repo path, skipping: {Path}", repoPath);
            return (0, 0);
        }

        var filter = new CommitFilter
        {
            IncludeReachableFrom = repo.Head,
            SortBy = CommitSortStrategies.Time
        };

        int scanned = 0, skipped = 0;

        foreach (var commit in repo.Commits.QueryBy(filter))
        {
            if (commit.Author.When.UtcDateTime < sinceUtc)
            {
                break; // Time-sorted, so nothing further back is in range either.
            }

            if (commit.Parents.Count() > 1)
            {
                continue; // Merge commit - excluded, see class remarks.
            }

            if (!authorEmails.Contains(commit.Author.Email))
            {
                continue;
            }

            if (knownShas.Contains(commit.Sha))
            {
                skipped++;
                continue;
            }

            var record = BuildRecord(repo, commit, repoPath, project, branchOf);
            onCommit(record);
            scanned++;
        }

        return (scanned, skipped);
    }

    /// <summary>
    /// Maps every reachable sha to the local branch it belongs to, by walking
    /// each local branch's history once. HEAD's branch is walked first so it
    /// wins when a commit is reachable from more than one branch - the
    /// alternative, asking per-commit which branches contain it, costs
    /// O(commits × branches) instead of O(branches × history) and dominates the
    /// scan time on a repo with more than a handful of branches.
    /// </summary>
    private static Dictionary<string, string> BuildBranchMap(Repository repo)
    {
        var map = new Dictionary<string, string>();

        var localBranches = repo.Branches.Where(b => !b.IsRemote).ToList();
        var headName = repo.Head.FriendlyName;

        var ordered = localBranches
            .OrderByDescending(b => string.Equals(b.FriendlyName, headName, StringComparison.Ordinal));

        foreach (var branch in ordered)
        {
            foreach (var commit in branch.Commits)
            {
                map.TryAdd(commit.Sha, branch.FriendlyName);
            }
        }

        return map;
    }

    private static CommitRecord BuildRecord(
        Repository repo,
        Commit commit,
        string repoPath,
        string project,
        Dictionary<string, string> branchOf)
    {
        var parent = commit.Parents.FirstOrDefault();

        var patch = parent is not null
            ? repo.Diff.Compare<Patch>(parent.Tree, commit.Tree)
            : repo.Diff.Compare<Patch>(null, commit.Tree);

        var changedPaths = patch.Select(p => p.Path).ToList();
        var languages = LanguageDetector.DetectAll(changedPaths);

        return new CommitRecord
        {
            Sha = commit.Sha,
            Repo = repoPath,
            Project = project,
            TsUtc = commit.Author.When.ToUnixTimeMilliseconds(),
            Message = FirstLine(commit.MessageShort),
            Branch = branchOf.GetValueOrDefault(commit.Sha),
            AuthorEmail = commit.Author.Email,
            FilesChanged = patch.Count(),
            Insertions = patch.Sum(p => p.LinesAdded),
            Deletions = patch.Sum(p => p.LinesDeleted),
            Languages = languages.Count > 0 ? string.Join(',', languages) : null,
            IsMerge = false
        };
    }

    private static string? FirstLine(string? message) =>
        string.IsNullOrEmpty(message) ? null : message.Split('\n')[0].Trim();
}
