namespace Devlog.Host.Commands;

/// <param name="Name">Canonical subcommand, without the leading dashes.</param>
/// <param name="Usage">How it is typed, arguments included.</param>
/// <param name="Summary">One line. If it needs two, the command does too much.</param>
/// <param name="Group">Heading it appears under in the help screen.</param>
public readonly record struct CommandInfo(
    string Name,
    string Usage,
    string Summary,
    string Group);

/// <summary>
/// The single list of what devlog can be asked to do.
/// <para>
/// One table drives three things that must never disagree: the help screen, the
/// check that rejects an unknown command, and the dispatch in
/// <see cref="DiagnosticCommands"/>. Before this existed there was no list at
/// all — an unrecognised flag fell through to tray mode and silently started a
/// second collector, which is how a duplicate came to be running once already.
/// </para>
/// </summary>
public static class CommandCatalog
{
    public const string Inspect = "INSPECT";
    public const string Build = "BUILD";
    public const string Classify = "CLASSIFY";
    public const string Report = "REPORT";
    public const string Ai = "AI";
    public const string Manage = "MANAGE";

    public static readonly CommandInfo[] All =
    [
        new("stats",    "stats",                        "Capture health: rows per day, hook status, database size", Inspect),
        new("events",   "events [n] [--process <name>]","Recent raw events, newest last",                           Inspect),
        new("sessions", "sessions [n]",                 "Derived sessions with their commits and deep time",        Inspect),
        new("commits",  "commits [n]",                  "Commits and which session each attached to",               Inspect),

        new("derive",   "derive",                       "Rebuild activities and sessions from the raw log",         Build),
        new("scan-git", "scan-git [days]",              "Import commits from the configured repos",                 Build),

        new("unknowns", "unknowns [n]",                 "Identities still awaiting a verdict, most time first",     Classify),
        new("classify", "classify <identity> <cat>",    "Answer one by hand; --keyword scopes it to a page",        Classify),

        new("digest",   "digest [--from D] [--to D] [--week|--month] [--prose] [--out FILE]", "Deterministic brag-document Markdown for a date range", Report),

        new("llm",          "llm",                          "AI provider, model, reachability and job status",          Ai),
        new("classify-ai",  "classify-ai [--dry-run] [--limit N]", "Drain pending identities using AI",                Ai),
        new("narrate",      "narrate [--since 7d] [--limit N] [--dry-run] [--force]", "Narrate sessions lacking a summary using AI", Ai),
        new("llm-fixtures", "llm-fixtures [--out <dir>]",   "Export candidate identities and sessions for hand-labelling", Ai),
        new("llm-eval",     "llm-eval [--dir <dir>]",       "Measure AI model accuracy against labelled fixtures",      Ai),

        new("config",     "config",                     "Resolved paths, exclusions and configured repos",          Manage),
        new("startup",    "startup [--enable|--disable]","Launch the collector at logon",                           Manage),
        new("purge-seed", "purge-seed --yes",           "Delete synthetic fixture rows from the raw log",           Manage)
    ];

    public static readonly string[] Groups = [Inspect, Build, Classify, Report, Ai, Manage];

    public static bool IsKnown(string name) =>
        All.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The command closest to what was typed, for a "did you mean" line.
    /// <para>
    /// Edit distance rather than prefix matching, because the typos that
    /// actually happen are transpositions and dropped letters — <c>sesions</c>,
    /// <c>drive</c> — and a prefix test catches none of them.
    /// </para>
    /// <para>
    /// Capped at a third of the word's length so a genuinely different word gets
    /// no suggestion at all. A confident wrong guess is worse than silence when
    /// the full command list is printed directly underneath.
    /// </para>
    /// </summary>
    public static string? ClosestTo(string typed)
    {
        if (string.IsNullOrEmpty(typed))
        {
            return null;
        }

        var budget = Math.Max(2, typed.Length / 3);

        return All
            .Select(c => (c.Name, Distance: EditDistance(typed.ToLowerInvariant(), c.Name)))
            .Where(x => x.Distance <= budget)
            .OrderBy(x => x.Distance)
            .Select(x => x.Name)
            .FirstOrDefault();
    }

    /// <summary>Levenshtein, two rows rather than a full matrix.</summary>
    private static int EditDistance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
