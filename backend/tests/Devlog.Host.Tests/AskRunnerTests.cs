using Devlog.Core.Abstractions;
using Devlog.Core.Configuration;
using Devlog.Core.Domain;
using Devlog.Host.Ai;
using Devlog.Host.Derivation;
using Devlog.Infrastructure.Migrations;
using Devlog.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Devlog.Host.Tests;

public sealed class AskRunnerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly EventStore _events;
    private readonly ActivityStore _actStore;
    private readonly SessionStore _sessStore;
    private readonly OverrideStore _overStore;
    private readonly ClassificationRuleStore _ruleStore;
    private readonly CommitStore _commitStore;
    private readonly NarrativeStore _narrativeStore;
    private readonly SessionReader _reader;
    private readonly DerivationRunner _derivation;

    public AskRunnerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"devlog-ask-test-{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(new DevlogOptions { DatabasePath = _dbPath });
        new MigrationRunner(_factory, NullLogger<MigrationRunner>.Instance).Run();

        _events = new EventStore(_factory);
        _actStore = new ActivityStore(_factory);
        _sessStore = new SessionStore(_factory);
        _overStore = new OverrideStore(_factory);
        _ruleStore = new ClassificationRuleStore(_factory);
        _commitStore = new CommitStore(_factory);
        _narrativeStore = new NarrativeStore(_factory);
        _reader = new SessionReader(_factory);

        _derivation = new DerivationRunner(
            _events, _actStore, _sessStore, _overStore, _ruleStore, _commitStore, _narrativeStore,
            new DerivationOptions(), new GitOptions(), NullLogger<DerivationRunner>.Instance);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + suffix); } catch (IOException) { }
        }
    }

    private sealed class StubChatClient : IChatClient
    {
        public bool Reachable { get; set; } = true;
        public Func<IReadOnlyList<ChatMessage>, IReadOnlyList<ToolDefinition>?, Task<ToolChatResult>>? Handler { get; set; }

        public Task<ChatResult> CompleteAsync(string systemPrompt, string userContent, string jsonSchemaName, string jsonSchema, string reasoningEffort, CancellationToken ct = default) =>
            Task.FromResult(new ChatResult(Reachable, "{}", "stub", null));

        public Task<ToolChatResult> CompleteWithToolsAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition>? tools, string reasoningEffort, CancellationToken ct = default)
        {
            if (!Reachable)
            {
                return Task.FromResult(new ToolChatResult(false, null, null, "Stub unreachable"));
            }

            if (Handler != null)
            {
                return Handler(messages, tools);
            }

            return Task.FromResult(new ToolChatResult(true, new ChatMessage("assistant", "Direct answer"), "stub", null));
        }

        public Task<bool> IsReachableAsync(CancellationToken ct = default) => Task.FromResult(Reachable);
        public Task<string?> ResolveEndpointAsync(CancellationToken ct = default) => Task.FromResult<string?>("http://127.0.0.1:11434/v1");
    }

    [Fact]
    public async Task AskAsync_Returns_Error_When_Unreachable()
    {
        var stubClient = new StubChatClient { Reachable = false };
        var runner = new AskRunner(stubClient, _reader, _narrativeStore, _ruleStore, new AiOptions(), NullLogger<AskRunner>.Instance);

        var result = await runner.AskAsync("What did I do today?");
        Assert.False(result.Success);
        Assert.Contains("not reachable", result.Error);
    }

    [Fact]
    public async Task AskAsync_Executes_ToolCall_And_Provides_Answer()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _events.AppendAsync(
        [
            new RawEvent
            {
                TsUtc = now - 3600000,
                Kind = EventKind.FocusChange,
                ProcessName = "devenv",
                WindowTitle = "Devlog.sln - Microsoft Visual Studio",
                ExePath = "C:\\vs.exe",
                IdleSeconds = 0
            },
            new RawEvent
            {
                TsUtc = now - 1000,
                Kind = EventKind.FocusChange,
                ProcessName = "devenv",
                WindowTitle = "Devlog.sln - Microsoft Visual Studio",
                ExePath = "C:\\vs.exe",
                IdleSeconds = 0
            }
        ]);

        await _derivation.RunAsync();

        var callCount = 0;
        var stubClient = new StubChatClient
        {
            Handler = (messages, tools) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // Model requests getSessions
                    var toolCall = new ToolCall(
                        "call_1",
                        "function",
                        new ToolCallFunction("getSessions", $"{{\"fromIso\":\"{DateTimeOffset.UtcNow.AddDays(-1):yyyy-MM-ddTHH:mm:ssZ}\",\"toIso\":\"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\"}}"));

                    return Task.FromResult(new ToolChatResult(
                        true,
                        new ChatMessage("assistant", null, null, [toolCall]),
                        "stub-model",
                        null));
                }

                // Turn 2: Assistant uses tool output to answer
                var lastMsg = messages.Last();
                Assert.Equal("tool", lastMsg.Role);
                Assert.Equal("call_1", lastMsg.ToolCallId);

                return Task.FromResult(new ToolChatResult(
                    true,
                    new ChatMessage("assistant", "You worked on 1 session in devlog."),
                    "stub-model",
                    null));
            }
        };

        var runner = new AskRunner(stubClient, _reader, _narrativeStore, _ruleStore, new AiOptions { Model = "stub-model" }, NullLogger<AskRunner>.Instance);

        var result = await runner.AskAsync("What sessions occurred today?");
        Assert.True(result.Success);
        Assert.Equal("You worked on 1 session in devlog.", result.Answer);
        Assert.Equal(2, result.ToolRounds);
        Assert.Contains("getSessions", result.ToolsUsed);
        Assert.Empty(result.UnverifiedNumbers);
    }
}
