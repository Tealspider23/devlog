namespace Devlog.Api.Contracts;

/// <summary>One day's picture: the sessions in it, the commits in it, and what is still unanswered.</summary>
public sealed record TimelineDto(
    string Date,
    IReadOnlyList<SessionDto> Sessions,
    IReadOnlyList<CommitDto> Commits,
    long UnclassifiedSeconds);

public sealed record SessionDetailDto(
    SessionDto Session,
    IReadOnlyList<ActivityDto> Activities,
    IReadOnlyList<CommitDto> Commits);

/// <summary>An identity from <c>classification_rule</c> still awaiting a verdict.</summary>
public sealed record PendingIdentityDto(string Identity, int Hits, int TotalSeconds);

/// <summary>Body of <c>POST /v1/classify</c> — the manual override path.</summary>
public sealed record ClassifyRequest(string Identity, string Category, string? Keyword);

public sealed record ClassifyResponse(string Identity, string Category, bool PromotedToMixed);

/// <summary>Mirrors <c>Devlog.Host.Derivation.DerivationRunner.DerivationResult</c> for <c>POST /v1/derive</c>.</summary>
public sealed record DeriveResultDto(
    int RawEvents,
    int AfterNoise,
    int Activities,
    int Sessions,
    int PendingIdentities,
    int UnclassifiedSeconds,
    int CommitsLinked,
    int CommitsUnattached,
    double ElapsedMs);
