using Devlog.Core.Domain;

namespace Devlog.Core.Abstractions;

/// <summary>
/// The read/answer surface of the verdict cache, split out so <c>Devlog.Api</c>
/// can depend on it without a reference to the Windows-only infrastructure
/// project. <c>ClassificationRuleStore</c> implements this in addition to its
/// own concrete surface — <c>RecordSightingsAsync</c> stays internal to
/// derivation and is deliberately not part of this interface.
/// </summary>
public interface IClassificationRuleStore
{
    /// <summary>Every rule, answered or not. The <c>--unknowns</c> and <c>GET /v1/unknowns</c> query.</summary>
    Task<List<ClassificationRule>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Records a manual verdict. Returns true when this answer disagreed with an
    /// existing one for the same site, which promotes it to mixed-use — see
    /// <see cref="ClassificationRule.IsMixed"/>.
    /// </summary>
    Task<bool> ClassifyAsync(
        string site,
        ActivityCategory category,
        string? keyword,
        string source,
        long nowUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Returns up to <paramref name="limit"/> sample titles seen for the given site identity,
    /// longest first. Used to give the AI classifier real context without scanning full history.
    /// </summary>
    Task<List<string>> GetSampleTitlesAsync(string site, int limit = 3, CancellationToken ct = default);

    /// <summary>
    /// Deletes one stored rule, of any source, returning it to pending. There was
    /// previously no way to undo a verdict at all — Job A can write a rule for an
    /// identity <c>SiteIdentity</c> should never have produced (a page title with
    /// no site behind it), and once written it is permanent: answered rows are
    /// never touched by <c>RecordSightingsAsync</c>'s pending-rebuild. This is the
    /// correction path. Returns true if a row existed and was removed.
    /// </summary>
    Task<bool> DeleteAsync(string site, string? keyword, CancellationToken ct = default);
}
