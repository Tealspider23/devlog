namespace Devlog.Core.Domain;

/// <summary>
/// How generated fixture data is distinguished from real capture.
/// <para>
/// Synthetic rows carry this prefix in their window title. That is deliberately
/// a property of the <em>data</em> rather than of whatever generated it: the
/// generator can be deleted, moved, or never present on a given machine, but
/// rows it produced live on in the database and must stay identifiable —
/// separable in reports, and removable outright once real capture makes them
/// redundant.
/// </para>
/// </summary>
public static class SyntheticData
{
    public const string Marker = "[seed]";

    /// <summary>SQL <c>LIKE</c> pattern matching synthetic window titles.</summary>
    public const string LikePattern = Marker + "%";

    public static bool IsSynthetic(string? windowTitle) =>
        windowTitle?.StartsWith(Marker, StringComparison.Ordinal) ?? false;
}
