using System.Text.RegularExpressions;
using Devlog.Core.Domain;

namespace Devlog.Core.Capture;

/// <summary>
/// Decides what is never recorded at all.
/// <para>
/// Excluded context is <b>not captured</b>, as opposed to captured-then-filtered.
/// A tool that watches everything you do needs an off switch you can actually
/// trust, and "we stored it but promise not to look" is not that.
/// </para>
/// </summary>
public sealed class ExclusionRules
{
    private readonly HashSet<string> _processes;
    private readonly Regex[] _titlePatterns;

    public static ExclusionRules None { get; } = new(null, null);

    /// <param name="processes">
    /// Process names, with or without the <c>.exe</c> suffix. Matched case-insensitively.
    /// </param>
    /// <param name="titlePatterns">
    /// Regular expressions matched case-insensitively against the raw window title.
    /// </param>
    /// <exception cref="ArgumentException">
    /// A pattern is not valid regex. Thrown at construction — i.e. at startup — on
    /// purpose: a privacy rule that silently fails to apply is the worst outcome here,
    /// so bad config must be loud rather than ignored.
    /// </exception>
    public ExclusionRules(IEnumerable<string>? processes, IEnumerable<string>? titlePatterns)
    {
        _processes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in processes ?? [])
        {
            if (!string.IsNullOrWhiteSpace(p))
            {
                _processes.Add(Normalize(p));
            }
        }

        var compiled = new List<Regex>();
        foreach (var pattern in titlePatterns ?? [])
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            try
            {
                compiled.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled));
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException(
                    $"ExcludedTitlePatterns contains invalid regex '{pattern}'. " +
                    "Fix appsettings.json — devlog will not start with an exclusion rule it cannot apply.",
                    nameof(titlePatterns),
                    ex);
            }
        }

        _titlePatterns = [.. compiled];
    }

    public bool IsExcluded(ForegroundSnapshot snapshot) =>
        IsExcluded(snapshot.ProcessName, snapshot.WindowTitle);

    public bool IsExcluded(string? processName, string? windowTitle)
    {
        if (!string.IsNullOrEmpty(processName) && _processes.Contains(Normalize(processName)))
        {
            return true;
        }

        if (string.IsNullOrEmpty(windowTitle))
        {
            return false;
        }

        foreach (var pattern in _titlePatterns)
        {
            if (pattern.IsMatch(windowTitle))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Strips a trailing <c>.exe</c> so config can use either form.</summary>
    private static string Normalize(string processName) =>
        processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
}
