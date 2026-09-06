using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Devlog.Core.Abstractions;
using Devlog.Core.Configuration;

namespace Devlog.Infrastructure.Ai;

/// <summary>
/// OpenAI-compatible HTTP client for local LLM inference (Ollama, LM Studio, vLLM).
/// Uses plain HttpClient with no vendor SDK dependencies.
/// </summary>
public sealed class ChatClassifier : IChatClient, IDisposable
{
    private readonly AiOptions _options;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private string? _resolvedEndpoint;

    public ChatClassifier(AiOptions options, HttpClient? client = null)
    {
        _options = options;
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

    public async Task<string?> ResolveEndpointAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            var explicitEndpoint = _options.Endpoint.TrimEnd('/');
            if (await CheckEndpointAsync(explicitEndpoint, ct).ConfigureAwait(false))
            {
                _resolvedEndpoint = explicitEndpoint;
                return explicitEndpoint;
            }
            return null;
        }

        if (_resolvedEndpoint is not null && await CheckEndpointAsync(_resolvedEndpoint, ct).ConfigureAwait(false))
        {
            return _resolvedEndpoint;
        }

        string[] candidates = ["http://127.0.0.1:11434/v1", "http://127.0.0.1:1234/v1"];
        foreach (var endpoint in candidates)
        {
            if (await CheckEndpointAsync(endpoint, ct).ConfigureAwait(false))
            {
                _resolvedEndpoint = endpoint;
                return endpoint;
            }
        }

        _resolvedEndpoint = null;
        return null;
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
        CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return new ChatResult(Reachable: false, Content: null, Model: null, Error: "AI features are disabled in configuration.");
        }

        var endpoint = await ResolveEndpointAsync(ct).ConfigureAwait(false);
        if (endpoint is null)
        {
            return new ChatResult(Reachable: false, Content: null, Model: null, Error: "No reachable OpenAI-compatible provider found.");
        }

        try
        {
            using var schemaDoc = JsonDocument.Parse(jsonSchema);
            var payload = new Dictionary<string, object?>
            {
                ["model"] = _options.Model,
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

            // Only send reasoning_effort for OpenAI reasoning models (o1/o3) that support it.
            // Local providers like Ollama / LM Studio hang or reject when reasoning_effort is passed.
            if (!string.IsNullOrWhiteSpace(reasoningEffort) && (_options.Model.StartsWith("o1", StringComparison.OrdinalIgnoreCase) || _options.Model.StartsWith("o3", StringComparison.OrdinalIgnoreCase)))
            {
                payload["reasoning_effort"] = reasoningEffort;
            }

            var json = JsonSerializer.Serialize(payload);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/chat/completions")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            }

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, _options.RequestTimeoutSeconds)));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            using var resp = await _client.SendAsync(req, linkedCts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var errorBody = await resp.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
                return new ChatResult(Reachable: false, Content: null, Model: null, Error: $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}: {errorBody}");
            }

            var respJson = await resp.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(respJson);

            var root = doc.RootElement;
            var returnedModel = root.TryGetProperty("model", out var modelProp) ? modelProp.GetString() : _options.Model;

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
            return new ChatResult(Reachable: false, Content: null, Model: null, Error: $"Request timed out after {_options.RequestTimeoutSeconds}s");
        }
        catch (Exception ex)
        {
            return new ChatResult(Reachable: false, Content: null, Model: null, Error: ex.InnerException?.Message ?? ex.Message);
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
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }
}
