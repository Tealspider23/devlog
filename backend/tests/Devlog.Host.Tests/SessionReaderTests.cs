using Devlog.Core.Configuration;
using Devlog.Core.Domain;
using Devlog.Infrastructure.Migrations;
using Devlog.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Devlog.Host.Tests;

/// <summary>
/// Integration tests against a real SQLite file, not a stub.
/// <para>
/// The behaviour worth protecting here is SQL semantics — range overlap, the
/// commit subqueries, ordering — and a fake would assert only that the fake
/// works. The database is small enough that a temp file per test costs
/// milliseconds.
/// </para>
/// </summary>
public sealed class SessionReaderTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SessionReader _reader;

    public SessionReaderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"devlog-test-{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(new DevlogOptions { DatabasePath = _dbPath });

        new MigrationRunner(_factory, NullLogger<MigrationRunner>.Instance).Run();

        _reader = new SessionReader(_factory);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + suffix); } catch (IOException) { }
        }
    }

    private static long At(int hour, int minute = 0) =>
        new DateTimeOffset(2026, 9, 2, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    private static Session Session(long id, long startUtc, long endUtc, string? project = "devlog") => new()
    {
        Id = id,
        StartUtc = startUtc,
        EndUtc = endUtc,
        ActivityKey = "Code|devlog",
        Project = project,
        Category = ActivityCategory.Coding,
        Interruptions = 0,
        DeepSeconds = (int)((endUtc - startUtc) / 1000)
    };

    private async Task GivenSessions(params Session[] sessions) =>
        await new SessionStore(_factory).ReplaceAllAsync(sessions);

    [Fact]
    public async Task GetRecent_ReturnsOldestFirst_SoTheyReadInTheOrderTheyHappened()
    {
        await GivenSessions(
            Session(1, At(9), At(10)),
            Session(2, At(11), At(12)),
            Session(3, At(14), At(15)));

        var result = await _reader.GetRecentAsync(10);

        Assert.Equal([1L, 2L, 3L], result.Select(r => r.Session.Id));
    }

    [Fact]
    public async Task GetRecent_LimitTakesTheNewest_ButStillReadsOldestFirst()
    {
        await GivenSessions(
            Session(1, At(9), At(10)),
            Session(2, At(11), At(12)),
            Session(3, At(14), At(15)));

        var result = await _reader.GetRecentAsync(2);

        // Newest two — 2 and 3 — presented in the order they occurred.
        Assert.Equal([2L, 3L], result.Select(r => r.Session.Id));
    }

    /// <summary>
    /// The subtle one, and the reason the range query uses overlap rather than
    /// containment: a session already in progress when the window opens belongs
    /// on the day's picture. Requiring containment would silently drop the block
    /// you were inside at midnight.
    /// </summary>
    [Fact]
    public async Task GetRange_IncludesASessionThatStartedBeforeTheWindow()
    {
        await GivenSessions(
            Session(1, At(7), At(9, 30)),   // straddles the window start
            Session(2, At(10), At(11)),     // wholly inside
            Session(3, At(17), At(19)));    // straddles the window end

        var result = await _reader.GetRangeAsync(At(9), At(18));

        Assert.Equal([1L, 2L, 3L], result.Select(r => r.Session.Id));
    }

    [Fact]
    public async Task GetRange_ExcludesSessionsThatOnlyTouchTheBoundary()
    {
        await GivenSessions(
            Session(1, At(7), At(9)),       // ends exactly when the window opens
            Session(2, At(18), At(19)));    // starts exactly when it closes

        var result = await _reader.GetRangeAsync(At(9), At(18));

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetRange_IsEmptyRatherThanThrowingWhenNothingMatches()
    {
        await GivenSessions(Session(1, At(9), At(10)));

        Assert.Empty(await _reader.GetRangeAsync(At(20), At(22)));
    }

    [Fact]
    public async Task SessionWithNoCommits_ReportsZero_RatherThanVanishing()
    {
        // Correlated subqueries rather than a join, precisely so this row
        // survives. A zero-output session is a real finding — usually debugging
        // or research — not a row to hide.
        await GivenSessions(Session(1, At(9), At(10)));

        var result = await _reader.GetRecentAsync(10);
        var only = Assert.Single(result);

        Assert.Equal(0, only.CommitCount);
        Assert.Equal(0, only.Insertions);
        Assert.True(only.IsZeroOutput);
    }

    [Fact]
    public async Task CommitCountsAndLineTotals_AreRolledUpPerSession()
    {
        await GivenSessions(Session(1, At(9), At(10)), Session(2, At(11), At(12)));

        await new CommitStore(_factory).UpsertAsync(
        [
            Commit("aaa1", At(9, 30), 1, insertions: 10, deletions: 2),
            Commit("bbb2", At(9, 45), 1, insertions: 5, deletions: 3),
            Commit("ccc3", At(11, 5), 2, insertions: 7, deletions: 0)
        ]);

        var result = await _reader.GetRecentAsync(10);

        var first = result.Single(r => r.Session.Id == 1);
        Assert.Equal(2, first.CommitCount);
        Assert.Equal(15, first.Insertions);
        Assert.Equal(5, first.Deletions);

        var second = result.Single(r => r.Session.Id == 2);
        Assert.Equal(1, second.CommitCount);
        Assert.Equal(7, second.Insertions);
    }

    [Fact]
    public async Task GetCommits_FiltersByTime_AndIncludesUnattachedOnes()
    {
        await new CommitStore(_factory).UpsertAsync(
        [
            Commit("aaa1", At(8), sessionId: null, insertions: 1, deletions: 0),
            Commit("bbb2", At(12), sessionId: null, insertions: 2, deletions: 0),
            Commit("ccc3", At(20), sessionId: null, insertions: 3, deletions: 0)
        ]);

        var result = await _reader.GetCommitsAsync(At(9), At(18));

        // Unattached commits are counted, not dropped — they usually predate the
        // collector or landed outside any session's window.
        var only = Assert.Single(result);
        Assert.Equal("bbb2", only.Sha);
        Assert.Null(only.SessionId);
    }

    [Fact]
    public async Task GetActivities_ReturnsOnlyThatSessionsActivities_InOrder()
    {
        await GivenSessions(Session(1, At(9), At(10)), Session(2, At(11), At(12)));

        await new ActivityStore(_factory).ReplaceAllAsync(
        [
            Activity(At(9, 30), At(9, 45), 1, "second"),
            Activity(At(9, 0), At(9, 30), 1, "first"),
            Activity(At(11, 0), At(11, 30), 2, "other session")
        ]);

        var result = await _reader.GetActivitiesAsync(1);

        Assert.Equal(["first", "second"], result.Select(a => a.SampleTitle));
    }

    [Fact]
    public async Task UnclassifiedSeconds_CountsOnlyOtherCategory()
    {
        await GivenSessions(Session(1, At(9), At(12)));

        await new ActivityStore(_factory).ReplaceAllAsync(
        [
            Activity(At(9), At(9, 10), 1, "coding", ActivityCategory.Coding),
            Activity(At(10), At(10, 5), 1, "unknown", ActivityCategory.Other)
        ]);

        Assert.Equal(300, await _reader.GetUnclassifiedSecondsAsync());
    }

    private static CommitRecord Commit(
        string sha, long tsUtc, long? sessionId, int insertions, int deletions) => new()
        {
            Sha = sha,
            Repo = "devlog",
            Project = "devlog",
            TsUtc = tsUtc,
            Message = "test",
            Branch = "master",
            AuthorEmail = "test@example.com",
            FilesChanged = 1,
            Insertions = insertions,
            Deletions = deletions,
            IsMerge = false,
            SessionId = sessionId
        };

    private static Activity Activity(
        long startUtc, long endUtc, long sessionId, string title,
        ActivityCategory category = ActivityCategory.Coding) => new()
        {
            StartUtc = startUtc,
            EndUtc = endUtc,
            ProcessName = "Code",
            ActivityKey = "Code|devlog",
            Category = category,
            Engagement = Engagement.Producing,
            TitleChanges = 0,
            SampleTitle = title,
            SessionId = sessionId
        };
}
