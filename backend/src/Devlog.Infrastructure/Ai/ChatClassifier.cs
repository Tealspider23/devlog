using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Devlog.Core.Abstractions;
using Devlog.Core.Ai;
using Devlog.Core.Configuration;

namespace Devlog.Infrastructure.Ai;

/// <summary>
/// OpenAI-compatible HTTP client for local LLM inference (Ollama, LM Studio, vLLM)
/// and hosted OpenAI-compatible providers (e.g. Gemini). Uses plain HttpClient
/// with no vendor SDK dependencies.
/// <para>
/// Also the one chokepoint every AI job funnels through, which is why the
/// endpoint probe cache, the per-minute pacing, and the request log all live
/// here rather than being duplicated per job (see Phase 15: a 44-session
/// narrate backlog cost ~30 requests against a 5-per-minute cap because every
/// call re-probed the endpoint and every retry spent more of the budget it
/// was waiting for).
/// </para>
/// </summary>
public sealed class ChatClassifier : IChatClient, IDisposable
{
    private const int MaxTransientRetries = 4;

    private readonly AiOptions _options;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly ILlmRequestLog? _requestLog;
    private readonly TokenBucket _tokenBucket;
    private readonly SemaphoreSlim _rateLimitGate = new(1, 1);

    private string? _resolvedEndpoint;
    private DateTimeOffset _resolvedAtUtc = DateTimeOffset.MinValue;

    public ChatClassifier(AiOptions options, HttpClient? client = null, ILlmRequestLog? requestLog = null)
    {
        _options = options;
        _requestLog = requestLog;
        _tokenBucket = new TokenBucket(Math.Max(1, options.RequestsPerMinute), TimeSpan.FromMinutes(1));

        if (client is not null)
        {
            _client = client;
            _ownsClient = false;
        }
        else
        {
            _client = new HttpClient();
            _ownsClient = true;
        }
    }

    public Task<string?> ResolveEndpointAsync(CancellationToken ct = default) =>
        ResolveEndpointAsync(forceProbe: false, ct);

    public async Task<string?> ResolveEndpointAsync(bool forceProbe, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return null;
        }

        if (!forceProbe && _resolvedEndpoint is not null && !IsProbeStale())
        {
            return _resolvedEndpoint;
        }

        if (!string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            var explicitEndpoint = _options.Endpoint.TrimEnd('/');
            if (await CheckEndpointAsync(explicitEndpoint, ct).ConfigureAwait(false))
            {
                SetResolved(explicitEndpoint);
                return explicitEndpoint;
            }

            _resolvedEndpoint = null;
            return null;
        }

        if (_resolvedEndpoint is not null && await CheckEndpointAsync(_resolvedEndpoint, ct).ConfigureAwait(false))
        {
            SetResolved(_resolvedEndpoint);
            return _resolvedEndpoint;
        }

        string[] candidates = ["http://127.0.0.1:11434/v1", "http://127.0.0.1:1234/v1"];
        foreach (var endpoint in candidates)
        {
            if (await CheckEndpointAsync(endpoint, ct).ConfigureAwait(false))
            {
                SetResolved(endpoint);
                return endpoint;
            }
        }

        _resolvedEndpoint = null;
        return null;
    }

    private bool IsProbeStale() =>
        DateTimeOffset.UtcNow - _resolvedAtUtc > TimeSpan.FromSeconds(Math.Max(1, _options.EndpointProbeTtlSeconds));

    private void SetResolved(string endpoint)
    {
        _resolvedEndpoint = endpoint;
        _resolvedAtUtc = DateTimeOffset.UtcNow;
    }

    public async Task<bool> IsReachableAsync(CancellationToken ct = default)
    {
        var endpoint = await ResolveEndpointAsync(ct).ConfigureAwait(false);
        return endpoint is not null;
    }

    public async Task<ChatResult> CompleteAsync(
        string systemPrompt,
        string userContent,
        string jsonSchemaName,
        string jsonSchema,
        string reasoningEffort,
        CancellationToken ct = default,
        string? model = null,
        string? job = null)
    {
        if (!_options.Enabled)
        {
            return new ChatResult(Reachable: false, Content: null, Model: null, Error: "AI features are disabled in configuration.", FailureKind: ChatFailureKind.Unreachable);
        }

        var endpoint = await ResolveEndpointAsync(ct).ConfigureAwait(false);
        if (endpoint is null)
        {
            return new ChatResult(Reachable: false, Content: null, Model: null, Error: "No reachable OpenAI-compatible provider found.", FailureKind: ChatFailureKind.Unreachable);
        }

        var effectiveModel = string.IsNullOrWhiteSpace(model) ? _options.Model : model;

        try
        {
            using var schemaDoc = JsonDocument.Parse(jsonSchema);
            var payload = new Dictionary<string, object?>
            {
                ["model"] = effectiveModel,
                ["temperature"] = 0,
                ["messages"] = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userContent }
                },
                ["response_format"] = new
                {
                    type = "json_schema",
                    json_schema = new
                    {
                        name = jsonSchemaName,
                        strict = true,
                        schema = schemaDoc.RootElement
                    }
                }
            };

            if (!string.IsNullOrWhiteSpace(reasoningEffort) && SupportsReasoningEffort(effectiveModel))
            {
                payload["reasoning_effort"] = reasoningEffort;
            }

            var json = JsonSerializer.Serialize(payload);
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, _options.RequestTimeoutSeconds)));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var outcome = await SendWithRetryAsync(
                () =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/chat/completions")
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    };
                    if (!string.IsNullOrWhiteSpace(_options.ApiKey))
                    {
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
                    }
                    return req;
                },
                job,
                effectiveModel,
                linkedCts.Token).ConfigureAwait(false);

            using var resp = outcome.Response;

            if (resp is null || !resp.IsSuccessStatusCode)
            {
                var errorBody = resp is not null ? await resp.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false) : (outcome.TransportError ?? "No response");
                var code = resp is not null ? (int)resp.StatusCode : 0;
                var reason = resp?.ReasonPhrase ?? "Unknown";
                var errorText = resp is not null ? $"HTTP {code} {reason}: {errorBody}" : errorBody;
                return new ChatResult(Reachable: false, Content: null, Model: null, Error: errorText, FailureKind: outcome.FailureKind);
            }

            var respJson = await resp.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(respJson);

            var root = doc.RootElement;
            var returnedModel = root.TryGetProperty("model", out var modelProp) ? modelProp.GetString() : effectiveModel;

            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var contentProp))
                {
                    var content = contentProp.GetString();
                    return new ChatResult(Reachable: true, Content: content, Model: returnedModel, Error: null);
                }
            }

            return new ChatResult(Reachable: true, Content: null, Model: returnedModel, Error: "Response JSON did not contain choices[0].message.content");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ChatResult(Reachable: false, Content: null, Model: null, Error: $"Request timed out after {_options.RequestTimeoutSeconds}s", FailureKind: ChatFailureKind.Transient);
        }
        catch (Exception ex)
        {
            return new ChatResult(Reachable: false, Content: null, Model: null, Error: ex.InnerException?.Message ?? ex.Message, FailureKind: ChatFailureKind.Unreachable);
        }
    }

    public async Task<ToolChatResult> CompleteWithToolsAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        string reasoningEffort,
        CancellationToken ct = default,
        string? model = null,
        string? job = null)
    {
        if (!_options.Enabled)
        {
            return new ToolChatResult(Reachable: false, Message: null, Model: null, Error: "AI is disabled in configuration.", FailureKind: ChatFailureKind.Unreachable);
        }

        var endpoint = await ResolveEndpointAsync(ct).ConfigureAwait(false);
        if (endpoint is null)
        {
            return new ToolChatResult(Reachable: false, Message: null, Model: null, Error: "No reachable OpenAI-compatible provider found.", FailureKind: ChatFailureKind.Unreachable);
        }

        var effectiveModel = string.IsNullOrWhiteSpace(model) ? _options.Model : model;

        try
        {
            var formattedMessages = new List<Dictionary<string, object?>>();
            foreach (var m in messages)
            {
                var dict = new Dictionary<string, object?>
                {
                    ["role"] = m.Role
                };

                if (m.Content is not null)
                {
                    dict["content"] = m.Content;
                }

                if (m.ToolCallId is not null)
                {
                    dict["tool_call_id"] = m.ToolCallId;
                }

                if (m.ToolCalls is { Count: > 0 })
                {
                    dict["tool_calls"] = m.ToolCalls.Select(tc =>
                    {
                        var call = new Dictionary<string, object?>
                        {
                            ["id"] = tc.Id,
                            ["type"] = "function",
                            ["function"] = new Dictionary<string, object?>
                            {
                                ["name"] = tc.Function.Name,
                                ["arguments"] = tc.Function.Arguments
                            }
                        };

                        // Only when the provider sent one - an explicit null is
                        // not the same as absent, and is rejected by some. Parsed
                        // back to an element rather than assigned as a string, or
                        // it would go out as one escaped blob and be as good as
                        // missing.
                        if (tc.ExtraContent is not null)
                        {
                            using var extraDoc = JsonDocument.Parse(tc.ExtraContent);
                            call["extra_content"] = extraDoc.RootElement.Clone();
                        }

                        return call;
                    }).ToList();
                }

                formattedMessages.Add(dict);
            }

            var payload = new Dictionary<string, object?>
            {
                ["model"] = effectiveModel,
                ["temperature"] = 0,
                ["messages"] = formattedMessages
            };

            if (tools is { Count: > 0 })
            {
                var toolsList = new List<object>();
                foreach (var t in tools)
                {
                    using var paramDoc = JsonDocument.Parse(t.ParametersJsonSchema);
                    toolsList.Add(new
                    {
                        type = "function",
                        function = new
                        {
                            name = t.Name,
                            description = t.Description,
                            parameters = paramDoc.RootElement.Clone()
                        }
                    });
                }
                payload["tools"] = toolsList;
            }

            if (!string.IsNullOrWhiteSpace(reasoningEffort) && SupportsReasoningEffort(effectiveModel))
            {
                payload["reasoning_effort"] = reasoningEffort;
            }

            var json = JsonSerializer.Serialize(payload);
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, _options.RequestTimeoutSeconds)));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var outcome = await SendWithRetryAsync(
                () =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/chat/completions")
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    };
                    if (!string.IsNullOrWhiteSpace(_options.ApiKey))
                    {
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
                    }
                    return req;
                },
                job,
                effectiveModel,
                linkedCts.Token).ConfigureAwait(false);

            using var resp = outcome.Response;

            if (resp is null || !resp.IsSuccessStatusCode)
            {
                var errorBody = resp is not null ? await resp.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false) : (outcome.TransportError ?? "No response");
                var code = resp is not null ? (int)resp.StatusCode : 0;
                var reason = resp?.ReasonPhrase ?? "Unknown";
                var errorText = resp is not null ? $"HTTP {code} {reason}: {errorBody}" : errorBody;
                return new ToolChatResult(Reachable: false, Message: null, Model: null, Error: errorText, FailureKind: outcome.FailureKind);
            }

            var respJson = await resp.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(respJson);

            var root = doc.RootElement;
            var returnedModel = root.TryGetProperty("model", out var modelProp) ? modelProp.GetString() : effectiveModel;

            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("message", out var msg))
                {
                    string? content = msg.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String
                        ? contentProp.GetString()
                        : null;

                    List<ToolCall>? parsedToolCalls = null;
                    if (msg.TryGetProperty("tool_calls", out var toolCallsProp) && toolCallsProp.ValueKind == JsonValueKind.Array)
                    {
                        parsedToolCalls = new List<ToolCall>();
                        foreach (var tc in toolCallsProp.EnumerateArray())
                        {
                            var id = tc.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                            var type = tc.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "function" : "function";
                            if (tc.TryGetProperty("function", out var fnProp))
                            {
                                var fnName = fnProp.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                                var fnArgs = fnProp.TryGetProperty("arguments", out var argsProp)
                                    ? (argsProp.ValueKind == JsonValueKind.String ? argsProp.GetString() ?? "{}" : argsProp.GetRawText())
                                    : "{}";
                                // Raw, unread - see ToolCall.ExtraContent. Gemini hides a
                                // required thought_signature in here.
                                var extra = tc.TryGetProperty("extra_content", out var extraProp)
                                    ? extraProp.GetRawText()
                                    : null;

                                parsedToolCalls.Add(new ToolCall(id, type, new ToolCallFunction(fnName, fnArgs), extra));
                            }
                        }
                    }

                    var role = msg.TryGetProperty("role", out var roleProp) ? roleProp.GetString() ?? "assistant" : "assistant";
                    var chatMsg = new ChatMessage(role, content, null, parsedToolCalls);
                    return new ToolChatResult(Reachable: true, Message: chatMsg, Model: returnedModel, Error: null);
                }
            }

            return new ToolChatResult(Reachable: true, Message: null, Model: returnedModel, Error: "Response JSON did not contain choices[0].message");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ToolChatResult(Reachable: false, Message: null, Model: null, Error: $"Request timed out after {_options.RequestTimeoutSeconds}s", FailureKind: ChatFailureKind.Transient);
        }
        catch (Exception ex)
        {
            return new ToolChatResult(Reachable: false, Message: null, Model: null, Error: ex.InnerException?.Message ?? ex.Message, FailureKind: ChatFailureKind.Unreachable);
        }
    }

    private sealed record SendOutcome(HttpResponseMessage? Response, ChatFailureKind FailureKind, string? TransportError);

    /// <summary>
    /// The one retry loop every completion call funnels through — shared
    /// rather than duplicated between <see cref="CompleteAsync"/> and
    /// <see cref="CompleteWithToolsAsync"/>, which had already drifted out of
    /// sync once before (see <see cref="SupportsReasoningEffort"/>'s comment).
    /// <para>
    /// Paces every attempt (including retries) through the shared token
    /// bucket, honours a provider's own <c>Retry-After</c> when it sends one,
    /// and logs every real HTTP attempt — not just the logical call — since
    /// a retried request still spends a real unit of the provider's quota.
    /// </para>
    /// <para>
    /// A 429/503 that still fails after every retry is reported as
    /// <see cref="ChatFailureKind.RateLimited"/> without trying to tell a
    /// per-minute limit from a per-day one apart: both mean the same thing to
    /// a caller running several requests in sequence — stop issuing more.
    /// </para>
    /// </summary>
    private async Task<SendOutcome> SendWithRetryAsync(
        Func<HttpRequestMessage> buildRequest,
        string? job,
        string model,
        CancellationToken ct)
    {
        var jobName = job ?? "unknown";
        HttpResponseMessage? resp = null;

        for (int attempt = 0; attempt <= MaxTransientRetries; attempt++)
        {
            await WaitForTokenAsync(ct).ConfigureAwait(false);

            using var req = buildRequest();
            try
            {
                resp = await _client.SendAsync(req, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var message = ex.InnerException?.Message ?? ex.Message;
                await LogAsync(jobName, model, httpStatus: null, ok: false, message, ct).ConfigureAwait(false);
                return new SendOutcome(null, ChatFailureKind.Unreachable, message);
            }

            if (resp.IsSuccessStatusCode)
            {
                await LogAsync(jobName, model, (int)resp.StatusCode, ok: true, error: null, ct).ConfigureAwait(false);
                return new SendOutcome(resp, ChatFailureKind.None, null);
            }

            var isTransient = (int)resp.StatusCode is 429 or 503;
            await LogAsync(jobName, model, (int)resp.StatusCode, ok: false, resp.ReasonPhrase, ct).ConfigureAwait(false);

            if (!isTransient || attempt == MaxTransientRetries)
            {
                return new SendOutcome(resp, isTransient ? ChatFailureKind.RateLimited : ChatFailureKind.Invalid, null);
            }

            var retryAfterRaw = resp.Headers.TryGetValues("Retry-After", out var values) ? values.FirstOrDefault() : null;
            var delay = RetryAfterParser.Parse(retryAfterRaw, DateTimeOffset.UtcNow)
                ?? TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt + 1)));

            resp.Dispose();
            resp = null;
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }

        return new SendOutcome(resp, ChatFailureKind.RateLimited, null);
    }

    private async Task WaitForTokenAsync(CancellationToken ct)
    {
        await _rateLimitGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var wait = _tokenBucket.TimeUntilNextToken();
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct).ConfigureAwait(false);
            }
            _tokenBucket.Consume();
        }
        finally
        {
            _rateLimitGate.Release();
        }
    }

    private async Task LogAsync(string job, string? model, int? httpStatus, bool ok, string? error, CancellationToken ct)
    {
        if (_requestLog is null)
        {
            return;
        }

        try
        {
            // A CancellationToken already fired (e.g. the request's own
            // timeout) would make this write throw and mask the real error —
            // logging must never be why a failure looks different than it is.
            await _requestLog.RecordAsync(job, model, httpStatus, ok, error, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Logging is diagnostic, not load-bearing. A write failure here
            // must never surface as the AI call's own failure.
        }
    }

    /// <summary>
    /// OpenAI reasoning models (o1/o3) and Gemini's OpenAI-compatible surface
    /// both accept <c>reasoning_effort</c> - confirmed live against
    /// gemini-3.6-flash. Local providers (Ollama, LM Studio) hang or reject on
    /// it, so it is withheld from anything not on this list rather than sent
    /// unconditionally. One place, shared by CompleteAsync and
    /// CompleteWithToolsAsync, because two separate copies of this check drifted
    /// out of sync once already - Job A's "low" and Job B's "high" were silent
    /// no-ops against the previous o1/o3-only version.
    /// </summary>
    private static bool SupportsReasoningEffort(string model) =>
        model.StartsWith("o1", StringComparison.OrdinalIgnoreCase)
        || model.StartsWith("o3", StringComparison.OrdinalIgnoreCase)
        || model.StartsWith("gemini", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        var endpoint = await ResolveEndpointAsync(ct).ConfigureAwait(false);
        if (endpoint is null)
        {
            return [];
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}/models");
            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            }

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, _options.ConnectTimeoutSeconds)));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            using var resp = await _client.SendAsync(req, linkedCts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return [];
            }

            var json = await resp.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var ids = new List<string>();
            foreach (var m in data.EnumerateArray())
            {
                if (m.TryGetProperty("id", out var idProp) && idProp.GetString() is { } id)
                {
                    ids.Add(id);
                }
            }

            return ids;
        }
        catch
        {
            return [];
        }
    }

    private async Task<bool> CheckEndpointAsync(string endpoint, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}/models");
            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            }

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, _options.ConnectTimeoutSeconds)));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            using var resp = await _client.SendAsync(req, linkedCts.Token).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _rateLimitGate.Dispose();
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }
}
