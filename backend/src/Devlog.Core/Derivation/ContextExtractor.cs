using System.Text.RegularExpressions;
using Devlog.Core.Domain;

namespace Devlog.Core.Derivation;

/// <param name="Context">
/// The stable part — project, channel, folder, page. What makes two moments "the
/// same thing", and therefore what sessions are keyed by. Not a project name: for
/// a browser it is a site, for an unrecognised app it is the raw title.
/// </param>
/// <param name="DefaultCategory">
/// What this process obviously is, when it is obvious. Null for browsers, whose
/// category cannot be known from the process alone and is resolved by the
/// classifier instead.
/// </param>
/// <param name="Detail">The volatile part — the file, the page. Kept for display only.</param>
/// <param name="Project">
/// The repository this time belongs to, and <b>only</b> when a rule genuinely
/// resolved one — never a fallback guess.
/// <para>
/// Separate from <paramref name="Context"/> because conflating them put junk in
/// the one document this project exists to produce: a digest listing "GitLab",
/// "Windows PowerShell" and a raw SQL Server Management Studio window title as
/// projects, because each was Coding-categorised and its context was promoted
/// unconditionally. Time accounting was right; the labels were nonsense.
/// </para>
/// <para>
/// Null is the honest answer for a browser tab, an unrecognised app, or a
/// terminal sitting in a directory that is not a known repo. Coding time with no
/// project is reported as exactly that.
/// </para>
/// </param>
public readonly record struct ExtractedContext(
    string? Context,
    ActivityCategory? DefaultCategory,
    string? Detail,
    string? Project = null);

/// <summary>
/// Pulls the stable identity out of a volatile window title, so that a 90-minute
/// refactor across twelve files stays one activity instead of shattering into
/// twelve.
/// <para>
/// Every rule here was written against titles actually observed in Phase 1
/// capture, not invented. Two of them exist specifically because reality
/// disagreed with the original design: VS Code's terminal panel carries no
/// project segment at all, and Antigravity IDE puts the project <em>first</em>
/// where VS Code puts it second.
/// </para>
/// </summary>
public static partial class ContextExtractor
{
    private const string Sep = " - ";

    /// <summary>
    /// Recovers a project from a filesystem path when the title has no project
    /// segment — e.g. <c>✻ [Claude Code] C:\...\source\repos\devlog\backend\...</c>.
    /// Without this, every terminal-panel row is silently orphaned.
    /// </summary>
    /// <remarks>
    /// The marker list is deliberately narrow. <c>source</c> and <c>src</c> were
    /// in an earlier version and both were actively harmful: <c>~/source/repos/devlog</c>
    /// matches <c>/source/</c> first and yields a project called "repos", while
    /// <c>…/devlog/backend/src/Devlog.Host</c> yields "Devlog.Host". Regex
    /// alternation takes the leftmost match, not the most specific one, so the
    /// only fix is to not list ambiguous segments at all.
    /// </remarks>
    [GeneratedRegex(
        @"[\\/](?:repos|repositories|projects|workspace|dev)[\\/](?<project>[^\\/]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RepoPathRegex();

    /// <summary>Windows Terminal: <c>user@HOST: ~/source/repos/devlog</c>.</summary>
    [GeneratedRegex(@"^[^:]*:\s*(?<path>.+)$", RegexOptions.Compiled)]
    private static partial Regex TerminalCwdRegex();

    /// <summary>Teams and Slack: <c>Chat | Jane Doe | Microsoft Teams</c>.</summary>
    [GeneratedRegex(@"\|\s*(?<channel>[^|]+?)\s*\|", RegexOptions.Compiled)]
    private static partial Regex ChannelRegex();

    /// <param name="resolver">
    /// Optional. When a rule finds a real filesystem path, this answers first —
    /// mapping it to the <em>configured</em> project via the longest matching repo
    /// root, which is also what implements the many-clones-one-project decision
    /// from Phase 3. Passing it makes attention and output share one definition of
    /// "project" instead of two that can disagree. Null falls back to the path
    /// regex, so a machine with no configured repos behaves exactly as before.
    /// </param>
    public static ExtractedContext Extract(
        string? processName, string? windowTitle, ProjectResolver? resolver = null)
    {
        var process = processName?.Trim() ?? string.Empty;
        var title = windowTitle?.Trim() ?? string.Empty;

        if (SiteIdentity.IsBrowser(process))
        {
            return ExtractBrowser(title, resolver);
        }

        return process.ToLowerInvariant() switch
        {
            "code" or "code - insiders" => ExtractVsCode(title, process, resolver),
            "devenv" => ExtractSuffixed(title, " - Microsoft Visual Studio", ActivityCategory.Coding, isProject: true),
            "windowsterminal" or "wt" or "powershell" or "cmd" or "pwsh" => ExtractTerminal(title, resolver),
            "explorer" => ExtractSuffixed(title, " - File Explorer", ActivityCategory.FileManagement),
            "ms-teams" or "teams" or "slack" or "zoom" => ExtractChat(title),
            _ => ExtractGeneric(title, process, resolver)
        };
    }

    /// <summary>Where an absolute path starts inside a longer string — <c>C:\…</c> or <c>~/…</c>.</summary>
    [GeneratedRegex(@"(?:[A-Za-z]:[\\/]|~[\\/])", RegexOptions.Compiled)]
    private static partial Regex PathStartRegex();

    /// <summary>
    /// The configured project for a path when one is known, else the name the
    /// path regex found. Both are genuine repo resolutions — the resolver is
    /// preferred only because it is authoritative about naming, which is what
    /// makes two clones of one service report under a single project.
    /// </summary>
    /// <param name="titleOrPath">
    /// May be a whole window title. <see cref="ProjectResolver.Resolve"/> matches
    /// a configured root as a <em>prefix</em>, so a title like
    /// <c>[Claude Code] C:\…\devlog</c> would never match — the path has to be
    /// cut out of it first.
    /// </param>
    private static string ProjectFromPath(ProjectResolver? resolver, string titleOrPath, string regexFallback)
    {
        if (resolver is null)
        {
            return regexFallback;
        }

        var start = PathStartRegex().Match(titleOrPath);
        var path = start.Success ? titleOrPath[start.Index..] : titleOrPath;

        return resolver.Resolve(path) ?? regexFallback;
    }

    /// <summary>
    /// VS Code puts the project in the <em>second-to-last</em> segment:
    /// <c>{file} - {project} - Visual Studio Code</c>.
    /// </summary>
    private static ExtractedContext ExtractVsCode(string title, string process, ProjectResolver? resolver)
    {
        const string marker = " - Visual Studio Code";

        if (title.EndsWith(marker, StringComparison.OrdinalIgnoreCase))
        {
            // Split on " - " rather than "-", so hyphenated project names such as
            // "orderbook-api" survive intact.
            var parts = title.Split(Sep, StringSplitOptions.None);

            if (parts.Length >= 3)
            {
                var project = parts[^2].Trim();
                var file = string.Join(Sep, parts[..^2]).TrimStart('●', '*', ' ');
                return new ExtractedContext(project, ActivityCategory.Coding, file, project);
            }

            // "devlog - Visual Studio Code" — a folder open with no file.
            if (parts.Length == 2)
            {
                var project = parts[0].Trim();
                return new ExtractedContext(project, ActivityCategory.Coding, null, project);
            }
        }

        // Terminal panels and webviews carry a path but no project segment.
        var fromPath = RepoPathRegex().Match(title);
        if (fromPath.Success)
        {
            var found = fromPath.Groups["project"].Value;

            return new ExtractedContext(
                found,
                ActivityCategory.Coding,
                title,
                ProjectFromPath(resolver, title, found));
        }

        // Still Coding — it is VS Code — but nothing here names a repository, so
        // no project is claimed.
        return new ExtractedContext(process, ActivityCategory.Coding, title);
    }

    /// <summary>
    /// Antigravity IDE puts the project <em>first</em>:
    /// <c>{project} - Antigravity IDE[ - {file}]</c>. Both variants observed.
    /// </summary>
    private static ExtractedContext ExtractAntigravity(string title)
    {
        var parts = title.Split(Sep, StringSplitOptions.None);
        var project = parts[0].Trim();
        var detail = parts.Length >= 3 ? string.Join(Sep, parts[2..]) : null;

        return new ExtractedContext(project, ActivityCategory.Coding, detail, project);
    }

    private static ExtractedContext ExtractTerminal(string title, ProjectResolver? resolver)
    {
        var match = TerminalCwdRegex().Match(title);
        if (!match.Success)
        {
            // A bare shell name — "Windows PowerShell" with no path at all.
            // Coding time, but it names no repository.
            return new ExtractedContext(title, ActivityCategory.Coding, null);
        }

        var path = match.Groups["path"].Value.Trim();

        // The repository root, not the directory you happen to be standing in.
        // Without this, `~/source/repos/devlog/backend` attributes the time to a
        // project called "backend", and one repo fragments into a session per
        // subdirectory you cd into.
        var repo = RepoPathRegex().Match(path);
        if (repo.Success)
        {
            var found = repo.Groups["project"].Value;
            return new ExtractedContext(found, ActivityCategory.Coding, path, ProjectFromPath(resolver, path, found));
        }

        var leaf = string.IsNullOrEmpty(path.TrimEnd('/', '\\').Split('/', '\\').LastOrDefault())
            ? path
            : path.TrimEnd('/', '\\').Split('/', '\\')[^1];

        // The leaf stays the context — it is still the thing being worked in, and
        // sessions key on it — but a bare directory name is NOT a project.
        // Treating one as a project turns ~/Downloads into a line item in a
        // performance review. Only a configured root, by path or by exact name,
        // counts.
        return new ExtractedContext(
            leaf,
            ActivityCategory.Coding,
            path,
            resolver?.Resolve(path) ?? resolver?.ResolveByName(leaf));
    }

    private static ExtractedContext ExtractChat(string title)
    {
        // A call is not interruptible time, so it is a different category to chat.
        var isMeeting = title.Contains("Meeting", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Call", StringComparison.OrdinalIgnoreCase)
            || title.Contains("Standup", StringComparison.OrdinalIgnoreCase);

        var category = isMeeting ? ActivityCategory.Meeting : ActivityCategory.Communication;

        var match = ChannelRegex().Match(title);
        var context = match.Success ? match.Groups["channel"].Value : title;

        return new ExtractedContext(context, category, title);
    }

    /// <param name="isProject">
    /// True only for Visual Studio, where the leading segment is the solution
    /// name — a real project. Explorer's folder name is not.
    /// </param>
    private static ExtractedContext ExtractSuffixed(
        string title, string suffix, ActivityCategory category, bool isProject = false)
    {
        var stripped = title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? title[..^suffix.Length].Trim()
            : title;

        var context = string.IsNullOrEmpty(stripped) ? null : stripped;

        // Only claim a project when the suffix actually matched: an unmatched
        // title is some other window of the same process, not a solution name.
        var matched = title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

        return new ExtractedContext(
            context,
            category,
            null,
            isProject && matched ? context : null);
    }

    /// <summary>
    /// Browsers get no default category on purpose — Chrome is Learning on docs,
    /// Coding on a pull request, and Distraction on YouTube, and the process name
    /// cannot tell them apart. The classifier decides.
    /// </summary>
    private static ExtractedContext ExtractBrowser(string title, ProjectResolver? resolver)
    {
        var identity = SiteIdentity.For("chrome", title);

        // A tab whose identity IS a configured project — `palpool-ui - Google
        // Chrome`, the running dev server — names real work on that repo, and
        // dropping it understated every project with browser time against it.
        //
        // Exact match against the configured list only. `GitLab` and `GitHub`
        // are not configured projects, so they stay unattributed, which is what
        // this whole change exists to achieve.
        return new ExtractedContext(
            identity,
            DefaultCategory: null,
            Detail: title,
            Project: resolver?.ResolveByName(identity));
    }

    private static ExtractedContext ExtractGeneric(string title, string process, ProjectResolver? resolver)
    {
        // Antigravity IDE and anything else naming itself in the title.
        if (title.Contains(" - Antigravity IDE", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractAntigravity(title);
        }

        var fromPath = RepoPathRegex().Match(title);
        if (fromPath.Success)
        {
            var found = fromPath.Groups["project"].Value;
            return new ExtractedContext(found, ActivityCategory.Coding, title, ProjectFromPath(resolver, title, found));
        }

        // The last resort: the raw window title as context, and a project only if
        // that title is exactly a configured project name.
        //
        // This branch is where SQL Server Management Studio lands, and claiming a
        // project unconditionally here is what put entire SSMS window titles --
        // server name, database, and the product name twice -- into the digest's
        // "Time by project" list. Those match nothing in the configured list, so
        // they now correctly get none.
        var context = string.IsNullOrEmpty(title) ? process : title;

        return new ExtractedContext(
            context,
            DefaultCategory: null,
            Detail: title,
            Project: resolver?.ResolveByName(context));
    }
}
