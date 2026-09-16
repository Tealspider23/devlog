using Devlog.Core.Abstractions;
using Devlog.Core.Ai;
using Devlog.Core.Configuration;
using Devlog.Core.Domain;
using Devlog.Host.Ai;
using Devlog.Infrastructure.Migrations;
using Devlog.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Devlog.Host.Tests;

/// <summary>
/// The regression for the 2026-09-15 incident: a 44-session backlog came back
/// as "all 44 skipped" because a rate-limited batch marked every remaining
/// session rejected instead of stopping. These tests protect the fix against
/// a real SQLite-backed <see cref="SessionReader"/>, with only the chat
/// client faked — the eligibility query and batching are the real code.
/// </summary>
public sealed class NarrateRunnerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SessionReader _reader;
    private readonly NarrativeStore _narrativeStore;

    public NarrateRunnerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"devlog-narrate-test-{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(new DevlogOptions { DatabasePath = _dbPath });
        new MigrationRunner(_factory, NullLogger<MigrationRunner>.Instance).Run();

        _reader = new SessionReader(_factory);
        _narrativeStore = new NarrativeStore(_factory);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + suffix); } catch (IOException) { }
        }
    }

    private static long At(int day, int hour) =>
        new DateTimeOffset(2026, 9, day, hour, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    /// <summary>Eligible: duration >= 300s, >= 2 activities. Durations differ so OrderByDescending picks a known order.</summary>
    private static Session EligibleSession(long id, int day, int durationMinutes) => new()
    {
        Id = id,
        StartUtc = At(day, 9),
        EndUtc = At(day, 9) + durationMinutes * 60_000L,
        ActivityKey = "Code|devlog",
        Project = "devlog",
        Category = ActivityCategory.Coding,
        Interruptions = 0,
        DeepSeconds = durationMinutes * 60
    };

    /// <summary>Title matches the evidence in <see cref="ValidBatchResponseFor"/> so the hallucination check accepts it — real behavior, not a stub bypass.</summary>
    private static Activity TwoActivitiesFor(long sessionId, long startUtc) => new()
    {
        StartUtc = startUtc,
        EndUtc = startUtc + 60_000,
        ProcessName = "Code",
        ActivityKey = "Code|devlog",
        Category = ActivityCategory.Coding,
        Engagement = Engagement.Producing,
        TitleChanges = 0,
        SampleTitle = "AuthController.cs",
        SessionId = sessionId
    };

    private async Task SeedThreeEligibleSessions()
    {
        var sessions = new[]
        {
            EligibleSession(1, day: 2, durationMinutes: 30),
            EligibleSession(2, day: 3, durationMinutes: 20),
            EligibleSession(3, day: 4, durationMinutes: 10),
        };
        await new SessionStore(_factory).ReplaceAllAsync(sessions);

        var activities = sessions.SelectMany(s => new[]
        {
            TwoActivitiesFor(s.Id, s.StartUtc),
            TwoActivitiesFor(s.Id, s.StartUtc + 120_000),
        }).ToArray();
        await new ActivityStore(_factory).ReplaceAllAsync(activities);
    }

    private static string ValidBatchResponseFor(long sessionId) => $$"""
        {
          "narratives": [
            {
              "sessionId": {{sessionId}},
              "narrative": "Worked on devlog. Made real progress in AuthController.cs.",
              "kind": "feature-work",
              "workstream": null,
              "evidence": ["AuthController.cs", "devlog project"],
              "confidence": 0.90
            }
          ]
        }
        """;

    /// <summary>Returns one <see cref="ChatResult"/> per call, in order, from a fixed queue.</summary>
    private sealed class QueuedChatClient(params ChatResult[] results) : IChatClient
    {
        private int _index;

        public Task<ChatResult> CompleteAsync(string systemPrompt, string userContent, string jsonSchemaName, string jsonSchema, string reasoningEffort, CancellationToken ct = default, string? model = null, string? job = null) =>
            Task.FromResult(results[Math.Min(_index++, results.Length - 1)]);

        public Task<ToolChatResult> CompleteWithToolsAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition>? tools, string reasoningEffort, CancellationToken ct = default, string? model = null, string? job = null) =>
            throw new NotSupportedException("Narrate never calls the tool-calling path.");

        public Task<bool> IsReachableAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<string?> ResolveEndpointAsync(CancellationToken ct = default) => Task.FromResult<string?>("http://stub/v1");
        public Task<string?> ResolveEndpointAsync(bool forceProbe, CancellationToken ct = default) => Task.FromResult<string?>("http://stub/v1");
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
    }

    [Fact]
    public async Task WhenABatchIsRateLimited_RunStopsEarly_RatherThanRejectingEverythingRemaining()
    {
        await SeedThreeEligibleSessions();

        // Batch size 1 -> one session per call. Session 1 (longest, 30m) is
        // picked first by OrderByDescending(DurationSeconds), succeeds; the
        // second call (session 2) comes back rate-limited.
        var chatClient = new QueuedChatClient(
            new ChatResult(true, ValidBatchResponseFor(1), "stub-model", null),
            new ChatResult(false, null, null, "HTTP 429 Too Many Requests", ChatFailureKind.RateLimited));

        var options = new AiOptions { NarrateBatchSize = 1 };
        var runner = new NarrateRunner(_reader, _narrativeStore, chatClient, options);

        var result = await runner.RunAsync(sinceArg: "3650d", limitOverride: 10, dryRun: false, force: false);

        Assert.True(result.StoppedEarly);
        Assert.NotNull(result.StopReason);
        Assert.Contains("429", result.StopReason);

        // The regression: only the one batch actually attempted is recorded.
        // Session 3 (never reached) must not appear as rejected — it is
        // simply untouched, to be picked up by the next run.
        Assert.Equal(1, result.AcceptedCount);
        Assert.Equal(0, result.RejectedCount);
        Assert.Single(result.Outcomes);
        Assert.Equal(1, result.Outcomes[0].SessionId);
    }

    [Fact]
    public async Task WhenABatchFailsForAnOrdinaryReason_RunContinues_AndRejectsOnlyThatBatch()
    {
        await SeedThreeEligibleSessions();

        var chatClient = new QueuedChatClient(
            new ChatResult(true, ValidBatchResponseFor(1), "stub-model", null),
            new ChatResult(false, null, null, "HTTP 500 Internal Server Error", ChatFailureKind.Invalid),
            new ChatResult(true, ValidBatchResponseFor(3), "stub-model", null));

        var options = new AiOptions { NarrateBatchSize = 1 };
        var runner = new NarrateRunner(_reader, _narrativeStore, chatClient, options);

        var result = await runner.RunAsync(sinceArg: "3650d", limitOverride: 10, dryRun: false, force: false);

        Assert.False(result.StoppedEarly);
        Assert.Equal(2, result.AcceptedCount);
        Assert.Equal(1, result.RejectedCount);
        Assert.Equal(3, result.Outcomes.Count);
    }
}
