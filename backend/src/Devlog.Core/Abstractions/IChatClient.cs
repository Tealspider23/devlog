namespace Devlog.Core.Abstractions;

/// <summary>
/// Why a call failed, distinct from the plain <c>Reachable</c> bool a caller
/// already had. <see cref="RateLimited"/> is the one a loop must act on
/// differently from the rest: retrying it just spends more of the same
/// budget it is waiting for, so a caller running several requests in
/// sequence (narrate's batches, a month of weekly wins) should stop issuing
/// new ones rather than let every remaining request fail the same way.
/// </summary>
public enum ChatFailureKind
{
    None = 0,
    Transient,
    RateLimited,
    Unreachable,
    Invalid,
}

public sealed record ChatResult(bool Reachable, string? Content, string? Model, string? Error, ChatFailureKind FailureKind = ChatFailureKind.None);

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
    string? Error,
    ChatFailureKind FailureKind = ChatFailureKind.None);

public interface IChatClient
{
    /// <param name="model">Overrides the configured model for this call only — the Chat page's per-query picker. Null uses the configured default.</param>
    /// <param name="job">Which AI job is calling — "narrate", "classify", "digest", "ask", "weekly-win". Tags the request log and the pacing/retry decisions; not sent to the provider.</param>
    Task<ChatResult> CompleteAsync(
        string systemPrompt,
        string userContent,
        string jsonSchemaName,
        string jsonSchema,
        string reasoningEffort,     // "low" | "medium" | "high"
        CancellationToken ct = default,
        string? model = null,
        string? job = null);

    /// <param name="model">Overrides the configured model for this call only — the Chat page's per-query picker. Null uses the configured default.</param>
    /// <param name="job">Which AI job is calling — see <see cref="CompleteAsync"/>.</param>
    Task<ToolChatResult> CompleteWithToolsAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        string reasoningEffort,
        CancellationToken ct = default,
        string? model = null,
        string? job = null);

    Task<bool> IsReachableAsync(CancellationToken ct = default);

    Task<string?> ResolveEndpointAsync(CancellationToken ct = default);

    /// <param name="forceProbe">
    /// Skips the cached, already-validated endpoint and re-checks live. Set
    /// only by something whose entire job is reporting current reachability —
    /// <c>GET /v1/ai/status</c> and <c>devlog llm</c> — never by a job runner,
    /// which should trust the cache like every other caller.
    /// </param>
    Task<string?> ResolveEndpointAsync(bool forceProbe, CancellationToken ct = default);

    /// <summary>
    /// The provider's own <c>/models</c> list, so the Chat page's model picker
    /// offers what the endpoint actually has rather than a hardcoded list.
    /// Empty, not an exception, when unreachable — mirrors <see cref="IsReachableAsync"/>.
    /// </summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default);
}
