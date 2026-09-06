namespace Devlog.Core.Abstractions;

public sealed record ChatResult(bool Reachable, string? Content, string? Model, string? Error);

public interface IChatClient
{
    Task<ChatResult> CompleteAsync(
        string systemPrompt,
        string userContent,
        string jsonSchemaName,
        string jsonSchema,
        string reasoningEffort,     // "low" | "medium" | "high"
        CancellationToken ct = default);

    Task<bool> IsReachableAsync(CancellationToken ct = default);

    Task<string?> ResolveEndpointAsync(CancellationToken ct = default);
}
