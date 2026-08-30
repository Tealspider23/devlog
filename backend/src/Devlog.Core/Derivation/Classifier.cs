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
        };

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
    /// Resolution order: page rule → site rule → builtin → pending.
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
