namespace Devlog.Core.Domain;

/// <summary>
/// The placeholder written in place of an excluded application's identity.
/// <para>
/// Lives in Core rather than beside the collector that writes it because the
/// reporting side needs it too: the marker becomes a site identity like any
/// other, and would otherwise sit in <c>--unknowns</c> forever asking to be
/// classified. It is not awaiting a verdict — it is the privacy rule working.
/// </para>
/// </summary>
public static class PrivacyMarker
{
    public const string Excluded = "[excluded]";

    public static bool IsExcluded(string? identity) =>
        string.Equals(identity, Excluded, StringComparison.Ordinal);
}
