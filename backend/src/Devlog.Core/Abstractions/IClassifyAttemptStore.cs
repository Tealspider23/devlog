namespace Devlog.Core.Abstractions;

/// <summary>
/// Tracks identities <c>classify-ai</c> has already sent to the model and
/// which it declined to answer confidently (a discard — see
/// <c>IdentityClassifierPrompt.ParseVerdicts</c>'s <c>out discards</c>).
/// <para>
/// Deliberately its own store, not a column on <c>classification_rule</c>:
/// that table's pending rows are deleted and rebuilt from scratch on every
/// derivation (<c>ClassificationRuleStore.RecordSightingsAsync</c>), which
/// runs on every page load. An attempt record has to outlive that or
/// <c>classify-ai</c> — which now runs on every dashboard Refresh — re-sends
/// the same discarded identities forever, spending the shared daily request
/// budget on identities that were never going to get an accepted verdict.
/// </para>
/// </summary>
public interface IClassifyAttemptStore
{
    /// <summary>Site identities attempted within the last <paramref name="withinDays"/> days, to exclude from the next batch.</summary>
    Task<HashSet<string>> GetRecentlyAttemptedAsync(int withinDays, CancellationToken ct = default);

    /// <summary>Records (or bumps) an attempt for an identity the model declined to answer confidently.</summary>
    Task RecordAttemptAsync(string site, long nowUtc, string? reason, CancellationToken ct = default);
}
