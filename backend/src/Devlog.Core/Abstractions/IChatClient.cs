namespace Devlog.Core.Abstractions;

public sealed record ChatResult(bool Reachable, string? Content, string? Model, string? Error);

public sealed record ToolCallFunction(string Name, string Arguments);

/// <param name="ExtraContent">
/// Vendor fields that came back attached to this tool call, as raw JSON, to be
/// sent back untouched on the next turn.
/// <para>
/// Not optional decoration. Gemini's thinking models return a
/// <c>thought_signature</c> here — an encrypted trace of the reasoning behind the
/// call — and rejects the following request outright if it is missing:
/// <c>400 Function call is missing a thought_signature in functionCall parts</c>.
/// The first turn therefore succeeds and the second fails, which is why this
/// surfaced only once Job G ran a real two-step tool loop.
/// </para>
/// <para>
/// Held as opaque JSON on purpose. devlog has no reason to understand what a
/// provider attaches here, and parsing it into a typed shape would mean a code
/// change every time a provider adds a field. Carrying it through unread costs
/// nothing and cannot be wrong.
/// </para>
/// </param>
public sealed record ToolCall(string Id, string Type, ToolCallFunction Function, string? ExtraContent = null);

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
    /// <param name="model">Overrides the configured model for this call only — the Chat page's per-query picker. Null uses the configured default.</param>
    Task<ChatResult> CompleteAsync(
        string systemPrompt,
        string userContent,
        string jsonSchemaName,
        string jsonSchema,
        string reasoningEffort,     // "low" | "medium" | "high"
        CancellationToken ct = default,
        string? model = null);

    /// <param name="model">Overrides the configured model for this call only — the Chat page's per-query picker. Null uses the configured default.</param>
    Task<ToolChatResult> CompleteWithToolsAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        string reasoningEffort,
        CancellationToken ct = default,
        string? model = null);

    Task<bool> IsReachableAsync(CancellationToken ct = default);

    Task<string?> ResolveEndpointAsync(CancellationToken ct = default);

    /// <summary>
    /// The provider's own <c>/models</c> list, so the Chat page's model picker
    /// offers what the endpoint actually has rather than a hardcoded list.
    /// Empty, not an exception, when unreachable — mirrors <see cref="IsReachableAsync"/>.
    /// </summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default);
}
