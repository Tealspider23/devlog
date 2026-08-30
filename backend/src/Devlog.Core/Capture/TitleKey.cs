using System.Text.RegularExpressions;

namespace Devlog.Core.Capture;

/// <summary>
/// Builds the string used to decide whether the foreground actually
/// <em>changed</em>. Comparison only — the raw title is always what gets stored.
/// <para>
/// This exists because of one specific and surprisingly expensive problem:
/// applications put live counters in their window titles.
/// <c>Inbox (14) - Gmail - Chrome</c> becomes <c>Inbox (15)</c> the moment a mail
/// arrives; Teams and Outlook do the same. Treating those as focus changes can
/// produce hundreds of rows a day carrying no information whatsoever — the single
/// biggest source of junk in a naive collector.
/// </para>
/// </summary>
public static partial class TitleKey
{
    /// <summary>Separator that cannot occur in a process name or window title.</summary>
    private const char Separator = '';

    /// <summary>Bracketed counters anywhere in the title: <c>(14)</c>, <c>[3]</c>.</summary>
    [GeneratedRegex(@"[\(\[]\s*\d+\s*[\)\]]", RegexOptions.Compiled)]
    private static partial Regex CounterRegex();

    /// <summary>Runs of whitespace left behind after stripping.</summary>
    [GeneratedRegex(@"\s{2,}", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    /// <summary>
    /// Normalizes a process + title pair for equality comparison.
    /// <para>
    /// Deliberately does <b>not</b> strip the modified-marker that editors use
    /// (<c>●</c> in VS Code, <c>*</c> elsewhere). That marker is real signal — it
    /// separates "actively editing this file" from "has this file open" — and it
    /// toggles a few dozen times a day, not a few hundred.
    /// </para>
    /// </summary>
    public static string For(string? processName, string? windowTitle)
    {
        var title = windowTitle ?? string.Empty;

        if (title.Length > 0)
        {
            title = CounterRegex().Replace(title, string.Empty);
            title = WhitespaceRegex().Replace(title, " ").Trim();
        }

        return string.Concat(processName ?? string.Empty, Separator.ToString(), title);
    }
}
