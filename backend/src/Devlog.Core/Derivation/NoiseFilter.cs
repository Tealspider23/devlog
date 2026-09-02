using System.Text.RegularExpressions;
using Devlog.Core.Domain;

namespace Devlog.Core.Derivation;

/// <summary>
/// Drops shell chrome that steals focus but represents no attention — toast
/// notifications, the lock screen, tray flyouts. Roughly 15% of real rows.
/// <para>
/// <b>This is not privacy filtering.</b> Privacy exclusions run at capture, so
/// the data is never written at all. Noise rules run here, at derivation,
/// because they get retuned every time Windows invents a new popup — and
/// filtering at capture would mean re-collecting the week each time.
/// </para>
/// <para>
/// Noise rows are removed <em>before</em> spans are computed, so the activities
/// on either side merge across the hole rather than being split by it.
/// </para>
/// </summary>
public sealed class NoiseFilter
{
    private static readonly HashSet<string> NoiseProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ShellExperienceHost",      // toast notifications
        "LockApp",                  // lock screen
        "TextInputHost",            // emoji / IME picker
        "SearchHost",               // Start search
        "StartMenuExperienceHost",
        "ApplicationFrameHost",     // UWP host shim, never the real app
        "SystemSettings",

        // Added after surveying the unanswered pile. Both confirmed against real
        // rows: ShellHost titles as "Quick settings", PickerHost as Windows
        // Update's "You're getting an update" toast.
        //
        // LaunchApps was on this list too and has been deliberately left off —
        // it turned out to be a Chrome tab (the company app launcher), not a
        // process, so it is real attention and is categorised, not dropped.
        "ShellHost",
        "PickerHost"
    };

    private static readonly HashSet<string> NoiseTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "System tray overflow window.",
        "Program Manager",          // the desktop itself
        "UnlockingWindow",
        "Windows Default Lock Screen",
        "New notification",
        "Task Switching",
        "Snap Assist",
        "New tab - Google Chrome",
        "New Tab - Microsoft Edge",
        "Search"
    };

    /// <summary>
    /// Titles that vary but mean the same nothing, so the exact-match set above
    /// cannot hold them.
    /// <para>
    /// Browser permission prompts are the motivating case: Chrome renames its
    /// own window to <c>launch.paltechapps.com wants to</c> while a dialog is
    /// up, which reads as a distinct site and earns its own pending identity.
    /// It is not a site — it is a modal on top of one.
    /// </para>
    /// </summary>
    private static readonly Regex[] NoiseTitlePatterns =
    [
        // "<host> wants to" — a permission prompt, one per host visited.
        new(@"\bwants to\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // Chrome's transient status while resolving a protocol handler.
        new(@"^Checking link\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    ];

    private readonly HashSet<string> _processes;
    private readonly HashSet<string> _titles;

    public NoiseFilter(IEnumerable<string>? extraProcesses = null, IEnumerable<string>? extraTitles = null)
    {
        _processes = new HashSet<string>(NoiseProcesses, StringComparer.OrdinalIgnoreCase);
        _titles = new HashSet<string>(NoiseTitles, StringComparer.OrdinalIgnoreCase);

        foreach (var p in extraProcesses ?? [])
        {
            if (!string.IsNullOrWhiteSpace(p))
            {
                _processes.Add(p.Trim());
            }
        }

        foreach (var t in extraTitles ?? [])
        {
            if (!string.IsNullOrWhiteSpace(t))
            {
                _titles.Add(t.Trim());
            }
        }
    }

    public bool IsNoise(RawEvent e)
    {
        // Structural events carry the timeline's boundaries. Dropping them would
        // let durations run straight through a lock or a shutdown.
        if (e.Kind is not (EventKind.FocusChange or EventKind.Heartbeat))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(e.ProcessName) && _processes.Contains(e.ProcessName))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(e.WindowTitle))
        {
            var title = e.WindowTitle.Trim();

            if (_titles.Contains(title))
            {
                return true;
            }

            if (NoiseTitlePatterns.Any(p => p.IsMatch(title)))
            {
                return true;
            }
        }

        // A focus row with neither process nor title describes nothing. Observed
        // during lock transitions and on the desktop.
        if (string.IsNullOrWhiteSpace(e.ProcessName) && string.IsNullOrWhiteSpace(e.WindowTitle))
        {
            return true;
        }

        // Explorer with no title is the desktop, not a folder you were looking at.
        if (string.Equals(e.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(e.WindowTitle))
        {
            return true;
        }

        return false;
    }

    public List<RawEvent> Apply(IEnumerable<RawEvent> events) =>
        [.. events.Where(e => !IsNoise(e))];
}
