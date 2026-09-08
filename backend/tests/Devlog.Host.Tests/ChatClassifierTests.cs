using System.Net;
using System.Text;
using Devlog.Core.Abstractions;
using Devlog.Core.Configuration;
using Devlog.Infrastructure.Ai;

namespace Devlog.Host.Tests;

public class ChatClassifierTests
{
    private const string DummySchemaName = "test_schema";
    private const string DummySchema = "{\"type\":\"object\",\"properties\":{\"category\":{\"type\":\"string\"}}}";

    [Fact]
    public async Task WhenEndpointIsUnreachable_CompleteAsync_ReturnsReachableFalse_WithoutThrowing()
    {
        // Custom handler that simulates socket connection refused
        var handler = new StubHttpMessageHandler((req, ct) =>
            throw new HttpRequestException("No connection could be made because the target machine actively refused it."));

        using var client = new HttpClient(handler);
        var options = new AiOptions { Endpoint = "http://127.0.0.1:9999/v1", ConnectTimeoutSeconds = 1 };
        using var classifier = new ChatClassifier(options, client);

        var result = await classifier.CompleteAsync("system", "user", DummySchemaName, DummySchema, "low");

        Assert.False(result.Reachable);
        Assert.Null(result.Content);
        Assert.Null(result.Model);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task WhenEndpointIsUnreachable_IsReachableAsync_ReturnsFalse_WithoutThrowing()
    {
        var handler = new StubHttpMessageHandler((req, ct) =>
            throw new HttpRequestException("Connection refused"));

        using var client = new HttpClient(handler);
        var options = new AiOptions { Endpoint = "http://127.0.0.1:9999/v1", ConnectTimeoutSeconds = 1 };
        using var classifier = new ChatClassifier(options, client);

        var reachable = await classifier.IsReachableAsync();

        Assert.False(reachable);
    }

    [Fact]
    public async Task WhenDisabledInConfig_CompleteAsync_ReturnsDisabledStateImmediately()
    {
        var handler = new StubHttpMessageHandler((req, ct) =>
            throw new InvalidOperationException("Should never make an HTTP call when disabled"));

        using var client = new HttpClient(handler);
        var options = new AiOptions { Enabled = false };
        using var classifier = new ChatClassifier(options, client);

        var result = await classifier.CompleteAsync("system", "user", DummySchemaName, DummySchema, "low");

        Assert.False(result.Reachable);
        Assert.Contains("disabled", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WhenServerReturns500_CompleteAsync_ReturnsReachableFalseWithError()
    {
        var handler = new StubHttpMessageHandler((req, ct) =>
        {
            if (req.RequestUri?.AbsolutePath.EndsWith("/models") == true)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"data\":[{\"id\":\"gpt-oss:20b\"}]}", Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"error\":\"GPU out of memory\"}", Encoding.UTF8, "application/json")
            });
        });

        using var client = new HttpClient(handler);
        var options = new AiOptions { Endpoint = "http://127.0.0.1:11434/v1" };
        using var classifier = new ChatClassifier(options, client);

        var result = await classifier.CompleteAsync("system", "user", DummySchemaName, DummySchema, "low");

        Assert.False(result.Reachable);
        Assert.Null(result.Content);
        Assert.Contains("500", result.Error);
        Assert.Contains("GPU out of memory", result.Error);
    }

    [Fact]
    public async Task WhenServerReturnsValidResponse_CompleteAsync_ParsesContentAndModel()
    {
        string? sentBody = null;
        var handler = new StubHttpMessageHandler(async (req, ct) =>
        {
            if (req.RequestUri?.AbsolutePath.EndsWith("/models") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"data\":[{\"id\":\"gpt-oss:20b\"}]}", Encoding.UTF8, "application/json")
                };
            }

            if (req.Content is not null)
            {
                sentBody = await req.Content.ReadAsStringAsync(ct);
            }

            var responseJson = """
            {
              "id": "chatcmpl-123",
              "object": "chat.completion",
              "model": "gpt-oss:20b",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "{\"category\":\"Coding\"}"
                  },
                  "finish_reason": "stop"
                }
              ]
            }
            """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(handler);
        var options = new AiOptions { Endpoint = "http://127.0.0.1:11434/v1", Model = "gpt-oss:20b" };
        using var classifier = new ChatClassifier(options, client);

        var result = await classifier.CompleteAsync(
            "Classify this identity.",
            "{\"identity\":\"VS Code\"}",
            DummySchemaName,
            DummySchema,
            "low");

        Assert.True(result.Reachable);
        Assert.Equal("{\"category\":\"Coding\"}", result.Content);
        Assert.Equal("gpt-oss:20b", result.Model);
        Assert.Null(result.Error);

        Assert.NotNull(sentBody);
        Assert.Contains("gpt-oss:20b", sentBody);
        Assert.Contains("json_schema", sentBody);
    }

    /// <summary>
    /// The regression for `devlog ask` failing with HTTP 400 on its second turn.
    /// Gemini attaches a required thought_signature under extra_content and
    /// rejects the next request without it, so the field has to survive a full
    /// round trip: parsed off the response, then written back out byte-identical.
    /// Asserting only that it parsed would pass while the bug was live, because
    /// the loss happened on the way back out.
    /// </summary>
    [Fact]
    public async Task ToolCallExtraContent_SurvivesTheRoundTripBackToTheProvider()
    {
        const string signature = "ErA7Cq07ARFNMg-fake-but-opaque-signature-payload";

        var sentBodies = new List<string>();
        var handler = new StubHttpMessageHandler(async (req, ct) =>
        {
            if (req.Content is not null)
            {
                sentBodies.Add(await req.Content.ReadAsStringAsync(ct));
            }

            var responseJson = $$"""
            {
              "model": "gemini-3.6-flash",
              "choices": [{
                "index": 0,
                "message": {
                  "role": "assistant",
                  "tool_calls": [{
                    "id": "call_1",
                    "type": "function",
                    "function": { "name": "getMetrics", "arguments": "{}" },
                    "extra_content": { "google": { "thought_signature": "{{signature}}" } }
                  }]
                },
                "finish_reason": "tool_calls"
              }]
            }
            """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(handler);
        var options = new AiOptions { Endpoint = "http://127.0.0.1:11434/v1", Model = "gemini-3.6-flash" };
        using var classifier = new ChatClassifier(options, client);

        var tools = new List<ToolDefinition>
        {
            new("getMetrics", "metrics for a range", "{\"type\":\"object\",\"properties\":{}}")
        };

        var first = await classifier.CompleteWithToolsAsync(
            [new ChatMessage("user", "how many hours this week?")], tools, "low");

        var toolCall = Assert.Single(first.Message!.ToolCalls!);
        Assert.NotNull(toolCall.ExtraContent);
        Assert.Contains(signature, toolCall.ExtraContent);

        // The turn that actually failed against Gemini: send the assistant's own
        // tool call back, plus the tool's result.
        await classifier.CompleteWithToolsAsync(
            [
                new ChatMessage("user", "how many hours this week?"),
                first.Message,
                new ChatMessage("tool", "{\"trackedSeconds\":3600}", toolCall.Id)
            ],
            tools,
            "low");

        var secondRequest = sentBodies[^1];
        Assert.Contains("extra_content", secondRequest);
        Assert.Contains(signature, secondRequest);

        // Sent as real JSON, not as an escaped string. A quoted blob reaches the
        // provider as unreadable text and is refused exactly like a missing one.
        Assert.DoesNotContain("\\\"thought_signature\\\"", secondRequest);
    }

    /// <summary>
    /// A provider that sends no extra_content must get none back — not an
    /// explicit null, which some reject outright.
    /// </summary>
    [Fact]
    public async Task ToolCallWithoutExtraContent_SerialisesWithNoSuchKey()
    {
        var sentBodies = new List<string>();
        var handler = new StubHttpMessageHandler(async (req, ct) =>
        {
            if (req.Content is not null)
            {
                sentBodies.Add(await req.Content.ReadAsStringAsync(ct));
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "model": "gpt-oss:20b",
                  "choices": [{
                    "index": 0,
                    "message": {
                      "role": "assistant",
                      "tool_calls": [{
                        "id": "call_1",
                        "type": "function",
                        "function": { "name": "getMetrics", "arguments": "{}" }
                      }]
                    }
                  }]
                }
                """, Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(handler);
        var options = new AiOptions { Endpoint = "http://127.0.0.1:11434/v1", Model = "gpt-oss:20b" };
        using var classifier = new ChatClassifier(options, client);

        var first = await classifier.CompleteWithToolsAsync(
            [new ChatMessage("user", "hours?")], [], "low");

        Assert.Null(Assert.Single(first.Message!.ToolCalls!).ExtraContent);

        await classifier.CompleteWithToolsAsync([new ChatMessage("user", "hours?"), first.Message], [], "low");

        Assert.DoesNotContain("extra_content", sentBodies[^1]);
    }

    [Fact]
    public async Task WhenEndpointIsNull_ProbingFindsFirstAvailableCandidate()
    {
        var probedEndpoints = new List<string>();
        var handler = new StubHttpMessageHandler((req, ct) =>
        {
            var uri = req.RequestUri?.ToString() ?? "";
            probedEndpoints.Add(uri);

            // Simulate Ollama (11434) being down and LM Studio (1234) being up
            if (uri.Contains("11434"))
            {
                throw new HttpRequestException("Connection refused");
            }

            if (uri.Contains("1234"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"data\":[{\"id\":\"gpt-oss:20b\"}]}", Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        using var client = new HttpClient(handler);
        var options = new AiOptions { Endpoint = null, ConnectTimeoutSeconds = 1 };
        using var classifier = new ChatClassifier(options, client);

        var endpoint = await classifier.ResolveEndpointAsync();

        Assert.Equal("http://127.0.0.1:1234/v1", endpoint);
        Assert.True(await classifier.IsReachableAsync());
        Assert.Contains(probedEndpoints, u => u.Contains("11434"));
        Assert.Contains(probedEndpoints, u => u.Contains("1234"));
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendFunc) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            sendFunc(request, cancellationToken);
    }
}
