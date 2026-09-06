using Devlog.Core.Configuration;
using Devlog.Core.Domain;
using Devlog.Infrastructure.Migrations;
using Devlog.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Devlog.Host.Tests;

public sealed class NarrativeStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly NarrativeStore _store;

    public NarrativeStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"devlog-narrative-test-{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(new DevlogOptions { DatabasePath = _dbPath });

        new MigrationRunner(_factory, NullLogger<MigrationRunner>.Instance).Run();

        _store = new NarrativeStore(_factory);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + suffix); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task UpsertAndGet_StoresAndRetrievesNarrative()
    {
        var narrative = new SessionNarrative
        {
            SessionStartUtc = 1725255180000,
            SessionEndUtc = 1725256763000,
            ActivityCount = 3,
            SessionId = 412,
            Narrative = "Reviewed merge request !59 and fixed login redirect.",
            Kind = "mr-review",
            Workstream = "US-1569",
            Evidence = ["MR !59 in GitLab", "AuthController.cs in orderbook-api"],
            Confidence = 0.95,
            Model = "gpt-oss:20b",
            GeneratedUtc = 1725257000000
        };

        await _store.UpsertAsync(narrative);

        var retrieved = await _store.GetByStartUtcAsync(narrative.SessionStartUtc);

        Assert.NotNull(retrieved);
        Assert.Equal(narrative.SessionStartUtc, retrieved.SessionStartUtc);
        Assert.Equal(narrative.SessionEndUtc, retrieved.SessionEndUtc);
        Assert.Equal(narrative.ActivityCount, retrieved.ActivityCount);
        Assert.Equal(narrative.SessionId, retrieved.SessionId);
        Assert.Equal(narrative.Narrative, retrieved.Narrative);
        Assert.Equal(narrative.Kind, retrieved.Kind);
        Assert.Equal(narrative.Workstream, retrieved.Workstream);
        Assert.Equal(2, retrieved.Evidence.Count);
        Assert.Contains("MR !59 in GitLab", retrieved.Evidence);
        Assert.Equal(0.95, retrieved.Confidence);
        Assert.Equal("gpt-oss:20b", retrieved.Model);
    }

    [Fact]
    public async Task Upsert_OnConflict_UpdatesExistingNarrative()
    {
        var initial = new SessionNarrative
        {
            SessionStartUtc = 1725255180000,
            SessionEndUtc = 1725256763000,
            ActivityCount = 3,
            SessionId = 412,
            Narrative = "Initial draft",
            Kind = "feature-work",
            Workstream = null,
            Evidence = ["Code"],
            Confidence = 0.70,
            Model = "gpt-oss:20b",
            GeneratedUtc = 1725257000000
        };

        await _store.UpsertAsync(initial);

        var updated = new SessionNarrative
        {
            SessionStartUtc = 1725255180000,
            SessionEndUtc = 1725256763000,
            ActivityCount = 3,
            SessionId = 412,
            Narrative = "Updated detailed narrative",
            Kind = "mr-review",
            Workstream = "US-1569",
            Evidence = ["MR !59", "AuthController.cs"],
            Confidence = 0.96,
            Model = "gpt-oss:20b",
            GeneratedUtc = 1725257100000
        };

        await _store.UpsertAsync(updated);

        var retrieved = await _store.GetByStartUtcAsync(1725255180000);
        Assert.NotNull(retrieved);
        Assert.Equal("Updated detailed narrative", retrieved.Narrative);
        Assert.Equal("mr-review", retrieved.Kind);
        Assert.Equal(0.96, retrieved.Confidence);
    }

    [Fact]
    public async Task GetRange_ReturnsNarrativesWithinWindow()
    {
        var n1 = new SessionNarrative
        {
            SessionStartUtc = 1000,
            SessionEndUtc = 1500,
            ActivityCount = 2,
            SessionId = 1,
            Narrative = "N1",
            Kind = "feature-work",
            Workstream = null,
            Evidence = ["E1", "E2"],
            Confidence = 0.9,
            Model = "m",
            GeneratedUtc = 2000
        };

        var n2 = new SessionNarrative
        {
            SessionStartUtc = 2000,
            SessionEndUtc = 2500,
            ActivityCount = 2,
            SessionId = 2,
            Narrative = "N2",
            Kind = "bugfix",
            Workstream = null,
            Evidence = ["E1", "E2"],
            Confidence = 0.9,
            Model = "m",
            GeneratedUtc = 3000
        };

        await _store.UpsertAsync(n1);
        await _store.UpsertAsync(n2);

        var range = await _store.GetRangeAsync(500, 1500);
        Assert.Single(range);
        Assert.Equal(1000, range[0].SessionStartUtc);

        var all = await _store.GetAllAsync();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task RelinkSessionIds_Updates_SessionIds_From_Matching_StartUtc()
    {
        var narrative = new SessionNarrative
        {
            SessionStartUtc = 5000,
            SessionEndUtc = 6000,
            ActivityCount = 4,
            SessionId = 99,
            Narrative = "Working on tests",
            Kind = "feature-work",
            Workstream = null,
            Evidence = ["test.cs"],
            Confidence = 0.9,
            Model = "m",
            GeneratedUtc = 7000
        };

        await _store.UpsertAsync(narrative);

        // Simulate re-derivation renumbering session id to 105
        var freshSessions = new List<Session>
        {
            new()
            {
                Id = 105,
                StartUtc = 5000,
                EndUtc = 6000,
                ActivityKey = "test",
                Category = ActivityCategory.Coding,
                Interruptions = 0,
                DeepSeconds = 1000
            }
        };

        await _store.RelinkSessionIdsAsync(freshSessions);

        var updated = await _store.GetByStartUtcAsync(5000);
        Assert.NotNull(updated);
        Assert.Equal(105, updated.SessionId);
    }
}
