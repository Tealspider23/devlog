using System.Text.RegularExpressions;
using Devlog.Core.Domain;

namespace Devlog.Core.Derivation;

/// <param name="Context">The stable part — project, channel, folder, page.</param>
/// <param name="DefaultCategory">
/// What this process obviously is, when it is obvious. Null for browsers, whose
/// category cannot be known from the process alone and is resolved by the
/// classifier instead.
/// </param>
/// <param name="Detail">The volatile part — the file, the page. Kept for display only.</param>
public readonly record struct ExtractedContext(
    string? Context,
    ActivityCategory? DefaultCategory,
    string? Detail);

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

    public static ExtractedContext Extract(string? processName, string? windowTitle)
    {
        var process = processName?.Trim() ?? string.Empty;
        var title = windowTitle?.Trim() ?? string.Empty;

        if (SiteIdentity.IsBrowser(process))
        {
            return ExtractBrowser(title);
        }

        return process.ToLowerInvariant() switch
        {
            "code" or "code - insiders" => ExtractVsCode(title, process),
            "devenv" => ExtractSuffixed(title, " - Microsoft Visual Studio", ActivityCategory.Coding),
            "windowsterminal" or "wt" or "powershell" or "cmd" or "pwsh" => ExtractTerminal(title),
            "explorer" => ExtractSuffixed(title, " - File Explorer", ActivityCategory.FileManagement),
            "ms-teams" or "teams" or "slack" or "zoom" => ExtractChat(title),
            _ => ExtractGeneric(title, process)
        };
    }

    /// <summary>
    /// VS Code puts the project in the <em>second-to-last</em> segment:
    /// <c>{file} - {project} - Visual Studio Code</c>.
    /// </summary>
    private static ExtractedContext ExtractVsCode(string title, string process)
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
                return new ExtractedContext(project, ActivityCategory.Coding, file);
            }

            // "devlog - Visual Studio Code" — a folder open with no file.
            if (parts.Length == 2)
            {
                return new ExtractedContext(parts[0].Trim(), ActivityCategory.Coding, null);
            }
        }

        // Terminal panels and webviews carry a path but no project segment.
        var fromPath = RepoPathRegex().Match(title);
        if (fromPath.Success)
        {
            return new ExtractedContext(
                fromPath.Groups["project"].Value,
                ActivityCategory.Coding,
                title);
        }

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

        return new ExtractedContext(project, ActivityCategory.Coding, detail);
    }

    private static ExtractedContext ExtractTerminal(string title)
    {
        var match = TerminalCwdRegex().Match(title);
        if (!match.Success)
        {
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
            return new ExtractedContext(repo.Groups["project"].Value, ActivityCategory.Coding, path);
        }

        var leaf = path.TrimEnd('/', '\\').Split('/', '\\').LastOrDefault();

        return new ExtractedContext(
            string.IsNullOrEmpty(leaf) ? path : leaf,
            ActivityCategory.Coding,
            path);
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

    private static ExtractedContext ExtractSuffixed(string title, string suffix, ActivityCategory category)
    {
        var context = title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? title[..^suffix.Length].Trim()
            : title;

        return new ExtractedContext(
            string.IsNullOrEmpty(context) ? null : context,
            category,
            null);
    }

    /// <summary>
    /// Browsers get no default category on purpose — Chrome is Learning on docs,
    /// Coding on a pull request, and Distraction on YouTube, and the process name
    /// cannot tell them apart. The classifier decides.
    /// </summary>
    private static ExtractedContext ExtractBrowser(string title)
    {
        var identity = SiteIdentity.For("chrome", title);
        return new ExtractedContext(identity, DefaultCategory: null, Detail: title);
    }

    private static ExtractedContext ExtractGeneric(string title, string process)
    {
        // Antigravity IDE and anything else naming itself in the title.
        if (title.Contains(" - Antigravity IDE", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractAntigravity(title);
        }

        var fromPath = RepoPathRegex().Match(title);
        if (fromPath.Success)
        {
            return new ExtractedContext(fromPath.Groups["project"].Value, ActivityCategory.Coding, title);
        }

        return new ExtractedContext(
            string.IsNullOrEmpty(title) ? process : title,
            DefaultCategory: null,
            Detail: title);
    }
}
