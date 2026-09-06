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
}
