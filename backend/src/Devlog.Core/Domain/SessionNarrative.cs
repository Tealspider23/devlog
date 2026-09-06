namespace Devlog.Core.Domain;

/// <summary>
/// A model-generated narrative explaining what occurred during a session.
/// Keyed on durable SessionStartUtc to survive derived table rebuilds.
/// </summary>
public sealed record SessionNarrative
{
    public required long SessionStartUtc { get; init; }
    public required long SessionEndUtc { get; init; }
    public required int ActivityCount { get; init; }
    public long? SessionId { get; init; }
    public required string Narrative { get; init; }
    public required string Kind { get; init; }
    public string? Workstream { get; init; }
    public required IReadOnlyList<string> Evidence { get; init; }
    public required double Confidence { get; init; }
    public required string Model { get; init; }
    public required long GeneratedUtc { get; init; }

    /// <summary>
    /// Checks if this narrative is stale compared to the current derived session.
    /// </summary>
    public bool IsStale(Session session, int activityCount) =>
        session.EndUtc != SessionEndUtc || activityCount != ActivityCount;
}
