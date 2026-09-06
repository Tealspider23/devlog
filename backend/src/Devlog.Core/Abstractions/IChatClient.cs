namespace Devlog.Core.Abstractions;

public sealed record ChatResult(bool Reachable, string? Content, string? Model, string? Error);

public sealed record ToolCallFunction(string Name, string Arguments);

public sealed record ToolCall(string Id, string Type, ToolCallFunction Function);

public sealed record ChatMessage(
    string Role,
    string? Content,
    string? ToolCallId = null,
    IReadOnlyList<ToolCall>? ToolCalls = null);

public sealed record ToolDefinition(
    string Name,
    string Description,
    string ParametersJsonSchema);

public sealed record ToolChatResult(
    bool Reachable,
    ChatMessage? Message,
    string? Model,
    string? Error);

public interface IChatClient
{
    Task<ChatResult> CompleteAsync(
        string systemPrompt,
        string userContent,
        string jsonSchemaName,
        string jsonSchema,
        string reasoningEffort,     // "low" | "medium" | "high"
        CancellationToken ct = default);

    Task<ToolChatResult> CompleteWithToolsAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        string reasoningEffort,
        CancellationToken ct = default);

    Task<bool> IsReachableAsync(CancellationToken ct = default);

    Task<string?> ResolveEndpointAsync(CancellationToken ct = default);
}
