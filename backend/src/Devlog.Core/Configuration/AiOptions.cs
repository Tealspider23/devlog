namespace Devlog.Core.Configuration;

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public bool Enabled { get; set; } = true;

    /// <summary>Explicit endpoint. Null falls through to probing - see AiProvider.</summary>
    public string? Endpoint { get; set; }

    public string Model { get; set; } = "gpt-oss:20b";

    public string? ApiKey { get; set; }

    /// <summary>Short on purpose: discovering an unreachable endpoint must be fast.</summary>
    public int ConnectTimeoutSeconds { get; set; } = 3;

    public int RequestTimeoutSeconds { get; set; } = 120;

    /// <summary>Below this, a verdict is discarded and the thing stays pending.</summary>
    public double MinConfidence { get; set; } = 0.6;

    public int ClassifyBatchSize { get; set; } = 10;

    /// <summary>
    /// Sessions narrated per model call. Each call costs one request against
    /// the provider's daily request quota regardless of size — Gemini's free
    /// tier caps at 20/day, shared across every AI job — so this is the lever
    /// that turns "20 sessions/day" into "20 batches/day". Raised from 10 to
    /// 25 after a 44-session backlog cost ~30 requests to fail outright
    /// against a 5-request-per-minute cap: at 25, the same backlog costs 2
    /// requests, not 5. Still well under the ~128k context window even for
    /// verbose sessions — the real limiter here was request count, not size.
    /// </summary>
    public int NarrateBatchSize { get; set; } = 25;

    /// <summary>
    /// Requests per minute, shared across every AI job via one token bucket
    /// in <c>ChatClassifier</c> — matches Gemini's free-tier RPM cap. A
    /// continuous refill, not a fixed interval: a single Ask question can
    /// spend several rounds immediately while the bucket is full.
    /// </summary>
    public int RequestsPerMinute { get; set; } = 5;

    /// <summary>
    /// Requests per day devlog will track and surface (Settings, `devlog llm`)
    /// as "used today" — matches Gemini's free-tier RPD cap. Informational
    /// only; devlog does not refuse to send a request once this is reached,
    /// since the provider's own 429 already enforces it and a hard local cap
    /// would just be a second, possibly-wrong copy of the same rule.
    /// </summary>
    public int RequestsPerDay { get; set; } = 20;

    /// <summary>
    /// How long a successfully-probed endpoint is trusted before the next
    /// call re-checks it live. Every model call used to re-probe
    /// unconditionally via a GET /models — doubling the request cost of
    /// every job for an endpoint that was already known good.
    /// </summary>
    public int EndpointProbeTtlSeconds { get; set; } = 300;

    /// <summary>
    /// Identities <c>classify-ai</c> already sent and the model declined to
    /// answer confidently are excluded from the pending pool for this many
    /// days, so Refresh (which runs classify-ai on every click) stops
    /// re-sending the same discarded identities and spending the shared
    /// daily budget on ones that were never going to get an accepted verdict.
    /// </summary>
    public int ClassifyRetryAfterDays { get; set; } = 7;

    public AiJobSwitches Jobs { get; set; } = new();
}

public sealed class AiJobSwitches
{
    public bool Classify { get; set; } = true;
    public bool Narrate { get; set; } = true;
    public bool Digest { get; set; } = true;
    public bool Ask { get; set; } = true;
}
