using Devlog.Core.Configuration;

namespace Devlog.Core.Derivation;

/// <summary>
/// Maps a filesystem path to the logical project it belongs to, using the
/// configured repos as the source of truth rather than pattern-matching the path.
/// <para>
/// This exists because <see cref="ContextExtractor"/>'s path fallback regex gets
/// the wrong answer for a real layout: <c>repos/palpool/palpool-api</c> yields
/// <c>palpool</c>, because the repo sits one level deeper than the pattern
/// assumes. A resolver seeded from configured repo roots does not need to guess.
/// </para>
/// <para>
/// It is also what implements the many-to-one decision: two clones of the same
/// service, configured with the same project name, resolve to one project and
/// their commits and attention time combine.
/// </para>
/// </summary>
public sealed class ProjectResolver
{
    private readonly List<(string RootNormalized, string Project)> _roots;

    public ProjectResolver(IEnumerable<RepoConfig> repos)
    {
        // Longest root first, so a nested repo root (if one is ever configured)
        // wins over a shorter ancestor rather than the reverse.
        _roots = [.. repos
            .Select(r => (RootNormalized: Normalize(r.Path), r.Project))
            .OrderByDescending(r => r.RootNormalized.Length)];
    }

    /// <summary>
    /// Resolves a path to its configured project, or null when the path is
    /// under no known repo root. Callers fall back to
    /// <see cref="ContextExtractor"/>'s path regex in that case — nothing
    /// regresses for a machine with no repos configured.
    /// </summary>
    public string? Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalized = Normalize(path);

        foreach (var (root, project) in _roots)
        {
            if (normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return project;
            }
        }

        return null;
    }

    /// <summary>Every distinct project this resolver knows about.</summary>
    public IReadOnlyCollection<string> KnownProjects =>
        [.. _roots.Select(r => r.Project).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Forward slashes and a trailing separator, so <c>C:\repos\x</c> and
    /// <c>C:\repos\x\</c> compare equal and <c>StartsWith</c> cannot match a
    /// sibling with a shared prefix (<c>palpool</c> vs <c>palpool-ui</c>).
    /// </summary>
    private static string Normalize(string path)
    {
        var slashed = path.Replace('\\', '/').TrimEnd('/');
        return slashed + "/";
    }
}
