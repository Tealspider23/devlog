using Devlog.Core.Domain;

namespace Devlog.Core.Configuration;

/// <summary>
/// Every threshold in the derivation pipeline, in one place.
/// <para>
/// All of these are guesses until there is a real workday to tune against — and
/// that is fine, because <c>raw_event</c> stores raw titles and raw idle
/// measurements. Changing any number here costs one re-derivation, never a
/// re-collection.
/// </para>
/// </summary>
public sealed class DerivationOptions
{
    public const string SectionName = "Derivation";

    /// <summary>
    /// Activities shorter than this are folded into their longer neighbour.
    /// Nothing meaningful happens in under a few seconds; this removes the
    /// residue that debouncing at capture could not.
    /// </summary>
    public int MinActivitySeconds { get; set; } = 8;

    /// <summary>
    /// A detour shorter than this returns to the same context and is counted as
    /// an interruption rather than ending the session. Without it, one glance at
    /// a browser would split a two-hour refactor in half.
    /// </summary>
    public int ExcursionSeconds { get; set; } = 120;

    /// <summary>Silence longer than this ends the session outright.</summary>
    public int SessionGapMinutes { get; set; } = 15;

    /// <summary>
    /// Idle beyond this counts as absence. Note this is the <em>only</em> thing
    /// idle time is used for — it cannot separate reading from typing, because
    /// scrolling is input.
    /// </summary>
    public int AwayIdleSeconds { get; set; } = 300;

    /// <summary>Process → category, overriding the builtin defaults.</summary>
    public Dictionary<string, string> CategoryOverrides { get; set; } = [];

    /// <summary>Extra noise processes beyond the builtin list.</summary>
    public string[] NoiseProcesses { get; set; } = [];

    /// <summary>Extra noise window titles beyond the builtin list.</summary>
    public string[] NoiseTitles { get; set; } = [];

    public TimeSpan SessionGap => TimeSpan.FromMinutes(Math.Max(1, SessionGapMinutes));

    public TimeSpan Excursion => TimeSpan.FromSeconds(Math.Max(0, ExcursionSeconds));

    public Dictionary<string, ActivityCategory> ResolveCategoryOverrides()
    {
        var result = new Dictionary<string, ActivityCategory>(StringComparer.OrdinalIgnoreCase);

        foreach (var (process, categoryName) in CategoryOverrides)
        {
            if (ActivityCategoryExtensions.TryParse(categoryName, out var category))
            {
                result[process] = category;
            }
        }

        return result;
    }
}
