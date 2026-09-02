using Devlog.Core.Domain;

namespace Devlog.Core.Derivation;

/// <param name="Category">The verdict.</param>
/// <param name="Source">Where it came from — for honesty in the UI and in metrics.</param>
/// <param name="IsPending">
/// True when nothing could answer it. The activity still gets a category
/// (<see cref="ActivityCategory.Other"/>) so derivation never blocks, but its
/// time is reported separately rather than quietly inflating a real bucket.
/// </param>
public readonly record struct Classification(
    ActivityCategory Category,
    string Source,
    bool IsPending);

public static class ClassificationSource
{
    public const string Builtin = "builtin";
    public const string Llm = "llm";
    public const string Manual = "manual";
    public const string Pending = "pending";
}

/// <summary>
/// Decides what kind of time an activity was.
/// <para>
/// The design premise: answer per <em>thing</em>, once — never per occurrence.
/// Three pages of MCP documentation are one verdict on "Model Context Protocol",
/// cached forever. That holds whether the answer comes from a builtin rule, from
/// a local model, or from you.
/// </para>
/// </summary>
public sealed class Classifier
{
    /// <summary>
    /// Processes whose category is never in doubt. These need no model and no
    /// human — they are the reason the pending list is mostly browser sites.
    /// </summary>
    private static readonly Dictionary<string, ActivityCategory> BuiltinProcessCategories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Code"] = ActivityCategory.Coding,
            ["Code - Insiders"] = ActivityCategory.Coding,
            ["devenv"] = ActivityCategory.Coding,
            ["Antigravity IDE"] = ActivityCategory.Coding,
            ["rider64"] = ActivityCategory.Coding,
            ["idea64"] = ActivityCategory.Coding,
            ["pycharm64"] = ActivityCategory.Coding,
            ["WindowsTerminal"] = ActivityCategory.Coding,
            ["powershell"] = ActivityCategory.Coding,
            ["pwsh"] = ActivityCategory.Coding,
            ["cmd"] = ActivityCategory.Coding,
            ["ms-teams"] = ActivityCategory.Communication,
            ["Teams"] = ActivityCategory.Communication,
            ["slack"] = ActivityCategory.Communication,
            ["outlook"] = ActivityCategory.Communication,
            ["zoom"] = ActivityCategory.Meeting,
            ["explorer"] = ActivityCategory.FileManagement,
            ["notepad"] = ActivityCategory.Other,

            // Added after surveying real capture — each was sitting in the
            // unanswered pile despite being entirely unambiguous.
            ["ssms"] = ActivityCategory.Coding,          // SQL Server Management Studio
            ["mintty"] = ActivityCategory.Coding,        // Git Bash
            ["SnippingTool"] = ActivityCategory.Other,
        };

    /// <summary>
    /// Keyword rules for identities a process name cannot settle — overwhelmingly
    /// browser sites, since <see cref="ContextExtractor"/> deliberately gives
    /// browsers no default category.
    /// <para>
    /// Every entry here was derived from identities actually observed, not
    /// invented. They exist so the model and the human are only asked about
    /// things that are genuinely ambiguous: a verdict a substring match can
    /// reach is not worth an inference call or your attention.
    /// </para>
    /// <para>
    /// First match wins, so more specific patterns are listed first —
    /// <c>YouTube Music</c> must be tested before any bare <c>YouTube</c> rule
    /// would be, and note there deliberately is no bare <c>YouTube</c> rule: it
    /// is the archetypal mixed-use site and belongs to the promotion mechanism.
    /// </para>
    /// </summary>
    private static readonly (string Keyword, ActivityCategory Category)[] BuiltinKeywordRules =
    [
        // Distraction — specific before general
        ("YouTube Music", ActivityCategory.Distraction),
        ("Wordle", ActivityCategory.Distraction),
        ("Weeb Central", ActivityCategory.Distraction),

        // Personal — property hunting. These arrive as one identity per listing
        // because the page title never names the site, so matching on the shape
        // of the title is the only lever available without URLs.
        ("Sqft", ActivityCategory.Personal),
        ("Property for rent", ActivityCategory.Personal),
        ("Rent Property", ActivityCategory.Personal),
        ("Flats / Studio", ActivityCategory.Personal),
        ("Apartments for rent", ActivityCategory.Personal),

        // Personal — travel
        ("IRCTC", ActivityCategory.Personal),
        ("MakeMyTrip", ActivityCategory.Personal),
        ("Travel Website", ActivityCategory.Personal),
        ("Google Maps", ActivityCategory.Personal),

        // Learning
        ("Documentation", ActivityCategory.Learning),
        ("developer docs", ActivityCategory.Learning),
        ("Wikipedia", ActivityCategory.Learning),
        ("Anthropic Academy", ActivityCategory.Learning),
        ("Machine Learning", ActivityCategory.Learning),
        ("Credly", ActivityCategory.Learning),
        ("Stack Overflow", ActivityCategory.Learning),

        // Coding
        ("GitLab", ActivityCategory.Coding),
        ("Merge request", ActivityCategory.Coding),
        ("Pull Request", ActivityCategory.Coding),
        ("GitHub", ActivityCategory.Coding),
        ("Repository search", ActivityCategory.Coding),

        // Work admin. Deliberately Other rather than forced into Communication:
        // timesheets and attendance are work, but they are not talking to anyone.
        // Worth revisiting if an Admin category is ever added.
        ("Timesheet", ActivityCategory.Other),
        ("Attendance", ActivityCategory.Other),
        ("Employees Directory", ActivityCategory.Other),
        ("Keka", ActivityCategory.Other),
        ("Expenses", ActivityCategory.Other),
        ("LaunchApps", ActivityCategory.Other),   // the intranet app launcher

        // Outlook on the web. The desktop client is settled by process name;
        // in a browser it arrives as the identity "Outlook" with no process to
        // go on.
        ("Outlook", ActivityCategory.Communication),
    ];

    private readonly Dictionary<string, ActivityCategory> _overrides;
    private readonly List<ClassificationRule> _rules;

    public Classifier(
        IEnumerable<ClassificationRule> rules,
        Dictionary<string, ActivityCategory>? categoryOverrides = null)
    {
        _rules = [.. rules];
        _overrides = categoryOverrides ?? [];
    }

    /// <summary>
    /// Resolution order: page rule → site rule → manual override → context
    /// default → builtin process → builtin keyword → pending.
    /// <para>
    /// A learned page rule beats a site rule because it only exists for sites
    /// that turned out to be mixed-use, where the site-level answer is known to
    /// be unreliable.
    /// </para>
    /// </summary>
    public Classification Classify(
        string? processName,
        string? windowTitle,
        string? siteIdentity,
        ActivityCategory? defaultFromContext)
    {
        var identity = siteIdentity ?? processName;

        if (!string.IsNullOrEmpty(identity))
        {
            var pageRule = _rules.FirstOrDefault(r =>
                r.Scope == RuleScope.Page
                && r.Category is not null
                && string.Equals(r.Site, identity, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(r.Keyword)
                && windowTitle?.Contains(r.Keyword, StringComparison.OrdinalIgnoreCase) == true);

            if (pageRule is not null)
            {
                return new Classification(pageRule.Category!.Value, pageRule.SourceName, false);
            }

            var siteRule = _rules.FirstOrDefault(r =>
                r.Scope == RuleScope.Site
                && r.Category is not null
                && string.Equals(r.Site, identity, StringComparison.OrdinalIgnoreCase));

            // A mixed site's own site-level answer is deliberately ignored: it was
            // demoted precisely because it is wrong about half the time.
            if (siteRule is not null && !siteRule.IsMixed)
            {
                return new Classification(siteRule.Category!.Value, siteRule.SourceName, false);
            }
        }

        if (!string.IsNullOrEmpty(processName) && _overrides.TryGetValue(processName, out var overridden))
        {
            return new Classification(overridden, ClassificationSource.Manual, false);
        }

        if (defaultFromContext is { } fromContext)
        {
            return new Classification(fromContext, ClassificationSource.Builtin, false);
        }

        if (!string.IsNullOrEmpty(processName)
            && BuiltinProcessCategories.TryGetValue(processName, out var builtin))
        {
            return new Classification(builtin, ClassificationSource.Builtin, false);
        }

        // Last automatic resort, and the one that covers browser sites. Sits
        // below manual and llm verdicts by construction: those are site or page
        // rules, matched far above, so a substring can never overwrite one.
        //
        // The identity is tried before the raw title. A title carries more text
        // to match on, which is exactly the risk — an incidental word in a page
        // heading should not outrank the site the page belongs to.
        foreach (var (keyword, category) in BuiltinKeywordRules)
        {
            if (identity?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true
                || windowTitle?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true)
            {
                return new Classification(category, ClassificationSource.Builtin, false);
            }
        }

        // Unanswered. Never blocks derivation; surfaces as "unclassified time".
        return new Classification(ActivityCategory.Other, ClassificationSource.Pending, true);
    }

    /// <summary>Identities with no confident answer, for the LLM phase or a manual verdict.</summary>
    public static IEnumerable<string> PendingIdentities(IEnumerable<Activity> activities) =>
        activities
            .Where(a => a.Category == ActivityCategory.Other && a.SiteIdentity is not null)
            .Select(a => a.SiteIdentity!)
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
