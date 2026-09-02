using System.Text.RegularExpressions;

namespace Devlog.Core.Derivation;

/// <summary>
/// Reduces a window title to the <em>thing</em> being looked at, so a category
/// can be decided once per site rather than once per page.
/// <para>
/// This is the load-bearing piece of classification, and it matters just as much
/// when a model does the answering as when a human does: without it, every one of
/// ~300 daily activities needs its own verdict; with it, only each newly-seen
/// identity does — roughly 4 a day, trending to zero. Measured against real
/// capture, 14 Chrome events reduce to 4 identities.
/// </para>
/// <para>
/// Window titles never contain URLs, so the site has to be inferred from the
/// title's own shape. Most sites cooperate by putting their name last.
/// </para>
/// </summary>
public static partial class SiteIdentity
{
    private static readonly string[] BrowserProcesses =
        ["chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc"];

    /// <summary>Trailing browser branding, including Edge's " and 3 more pages" variant.</summary>
    [GeneratedRegex(
        @"(?:\s+and\s+\d+\s+more\s+pages?)?\s*[-—|·]\s*(?:Google\s+Chrome|Mozilla\s+Firefox|Microsoft.?\s*Edge|Brave|Opera|Vivaldi|Arc)(?:\s*[-—|·]\s*.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BrowserSuffixRegex();

    /// <summary>
    /// GitHub repo pages: <c>owner/repo: description</c>, or just <c>owner/repo</c>,
    /// carrying no site suffix at all.
    /// <para>
    /// The colon is optional because a repo with no description — which is every
    /// repo of your own before you write one — titles as bare <c>owner/repo</c>.
    /// Requiring it missed <c>Tealspider23/devlog</c> and <c>excalidraw/excalidraw</c>
    /// entirely, so each earned its own pending identity instead of folding into
    /// the single GitHub verdict this rule exists to produce.
    /// </para>
    /// <para>
    /// Anchored at both ends when there is no colon, so an arbitrary title that
    /// merely opens with a slashed pair — a file path, a fraction — is not
    /// mistaken for a repository.
    /// </para>
    /// </summary>
    [GeneratedRegex(@"^[\w.\-]+/[\w.\-]+(?::|$)", RegexOptions.Compiled)]
    private static partial Regex GitHubRepoRegex();

    /// <summary>A bare <c>owner/repo</c> and nothing else — anchored at both ends.</summary>
    [GeneratedRegex(@"^[\w.\-]+/[\w.\-]+$", RegexOptions.Compiled)]
    private static partial Regex RepoNameRegex();

    /// <summary>The separators sites actually use before their own name.</summary>
    [GeneratedRegex(@"\s+[-—|·]\s+", RegexOptions.Compiled)]
    private static partial Regex SeparatorRegex();

    private const int MaxLength = 60;

    public static bool IsBrowser(string? processName) =>
        processName is not null
        && BrowserProcesses.Contains(processName.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// For non-browsers the process name <em>is</em> the identity — one verdict on
    /// "Antigravity IDE", settled forever.
    /// </summary>
    public static string? For(string? processName, string? windowTitle)
    {
        if (!IsBrowser(processName))
        {
            return string.IsNullOrWhiteSpace(processName) ? null : processName.Trim();
        }

        if (string.IsNullOrWhiteSpace(windowTitle))
        {
            return processName?.Trim();
        }

        var title = windowTitle.Trim();

        var stripped = BrowserSuffixRegex().Replace(title, string.Empty).Trim();
        if (stripped.Length == 0)
        {
            stripped = title;
        }

        // Every repo, issue and PR page collapses to one verdict on GitHub rather
        // than one per repository.
        //
        // Tested against the stripped title, not the raw one: the description-less
        // form ends at the repo name, so ` - Google Chrome` would otherwise sit
        // where the pattern expects end-of-string and the match would be lost.
        if (GitHubRepoRegex().IsMatch(stripped))
        {
            return "GitHub";
        }

        // Sites put their name last: "Understanding MCP servers - Model Context
        // Protocol" identifies as "Model Context Protocol".
        var parts = SeparatorRegex().Split(stripped);
        var candidate = parts.Length > 1 ? parts[^1].Trim() : stripped;

        if (candidate.Length == 0)
        {
            candidate = stripped;
        }

        // GitHub's own tabs put the repo last rather than first:
        // "Issues · excalidraw/excalidraw" and "devlog/docs at master ·
        // Tealspider23/devlog". The head-anchored check above cannot see those,
        // so the resolved candidate is retested — otherwise every repo whose
        // Issues tab you open earns a second identity beside GitHub.
        if (RepoNameRegex().IsMatch(candidate))
        {
            return "GitHub";
        }

        return candidate.Length > MaxLength ? candidate[..MaxLength].TrimEnd() : candidate;
    }
}
