namespace Devlog.Core.Domain;

/// <summary>Counts from one full pass of derivation — what <c>--derive</c> and <c>POST /v1/derive</c> both report.</summary>
public sealed record DerivationResult(
    int RawEvents,
    int AfterNoise,
    int Activities,
    int Sessions,
    int PendingIdentities,
    int UnclassifiedSeconds,
    int CommitsLinked,
    int CommitsUnattached,
    TimeSpan Elapsed);
