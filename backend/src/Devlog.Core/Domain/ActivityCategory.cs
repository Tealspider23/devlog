namespace Devlog.Core.Domain;

/// <summary>
/// What kind of time this was. Stored as text so the database stays readable
/// when debugging with <c>--stats</c> and friends.
/// </summary>
public enum ActivityCategory
{
    /// <summary>Unclassified. The honest default — never guessed silently.</summary>
    Other = 0,

    /// <summary>Editors, IDEs, terminals, PR review.</summary>
    Coding,

    /// <summary>Docs, tutorials, Stack Overflow, reading a repo.</summary>
    Learning,

    /// <summary>Chat — Slack, Teams messages, email.</summary>
    Communication,

    /// <summary>Calls. Distinct from chat because it is not interruptible time.</summary>
    Meeting,

    /// <summary>Explorer, file wrangling.</summary>
    FileManagement,

    /// <summary>
    /// Social, entertainment. Exists so the focus ratio is honest — without it,
    /// football highlights count as Learning and every metric flatters you.
    /// </summary>
    Distraction,

    /// <summary>Shopping, banking, admin. Visible, but excluded from work totals.</summary>
    Personal
}

public static class ActivityCategoryExtensions
{
    /// <summary>Does this category represent output rather than intake?</summary>
    public static bool IsProductive(this ActivityCategory category) =>
        category is ActivityCategory.Coding or ActivityCategory.FileManagement;

    /// <summary>Does this count toward work at all? Personal and Distraction do not.</summary>
    public static bool IsWork(this ActivityCategory category) =>
        category is not (ActivityCategory.Distraction or ActivityCategory.Personal);

    public static bool TryParse(string? value, out ActivityCategory category) =>
        Enum.TryParse(value, ignoreCase: true, out category);
}
