using System.Net;
using System.Text;
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
