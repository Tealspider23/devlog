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

    /// <summary>GitHub repo pages: <c>owner/repo: description</c>, carrying no site suffix at all.</summary>
    [GeneratedRegex(@"^[\w.\-]+/[\w.\-]+:", RegexOptions.Compiled)]
    private static partial Regex GitHubRepoRegex();

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

        // Every repo, issue and PR page collapses to one verdict on GitHub rather
        // than one per repository.
        if (GitHubRepoRegex().IsMatch(title))
        {
            return "GitHub";
        }

        var stripped = BrowserSuffixRegex().Replace(title, string.Empty).Trim();
        if (stripped.Length == 0)
        {
            stripped = title;
        }

        // Sites put their name last: "Understanding MCP servers - Model Context
        // Protocol" identifies as "Model Context Protocol".
        var parts = SeparatorRegex().Split(stripped);
        var candidate = parts.Length > 1 ? parts[^1].Trim() : stripped;

        if (candidate.Length == 0)
        {
            candidate = stripped;
        }

        return candidate.Length > MaxLength ? candidate[..MaxLength].TrimEnd() : candidate;
    }
}
