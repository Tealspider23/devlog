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

    public AiJobSwitches Jobs { get; set; } = new();
}

public sealed class AiJobSwitches
{
    public bool Classify { get; set; } = true;
    public bool Narrate { get; set; } = true;
    public bool Digest { get; set; } = true;
    public bool Ask { get; set; } = true;
}
