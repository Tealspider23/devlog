using Devlog.Core.Domain;
using Devlog.Core.Metrics;

namespace Devlog.Core.Tests;

public class MetricsCalculatorTests
{
    private static readonly DateOnly From = new(2026, 8, 30);
    private static readonly DateOnly To = new(2026, 9, 4);

    private static readonly long Base = DateTimeOffset
        .Parse("2026-08-30T09:00:00Z").ToUnixTimeMilliseconds();

    private static long At(int daysOffset, int seconds) =>
        Base + (daysOffset * 86400L + seconds) * 1000L;

    private static SessionSummary Sess(
        int dayOffset, int startSeconds, int endSeconds,
        string? project = "devlog", ActivityCategory category = ActivityCategory.Coding,
        int interruptions = 0, int commitCount = 0, int insertions = 0, int deletions = 0) => new()
    {
        Session = new Session
        {
            Id = 1,
            StartUtc = At(dayOffset, startSeconds),
            EndUtc = At(dayOffset, endSeconds),
            ActivityKey = "CodingDevlog",
            Project = project,
            Category = category,
            Interruptions = interruptions,
            DeepSeconds = endSeconds - startSeconds
        },
        ActivityCount = 1,
        CommitCount = commitCount,
        Insertions = insertions,
        Deletions = deletions
    };

    private static CommitRecord Commit(
        int dayOffset, int atSeconds, string project = "devlog",
        string? branch = null, string? languages = null, bool isMerge = false, long? sessionId = 1) => new()
    {
        Sha = Guid.NewGuid().ToString("N"),
        Repo = @"C:\repos\devlog",
        Project = project,
        TsUtc = At(dayOffset, atSeconds),
        Branch = branch,
        Languages = languages,
        AuthorEmail = "me@example.com",
        IsMerge = isMerge,
        SessionId = sessionId
    };

    private static DigestMetrics Calculate(
        IReadOnlyList<SessionSummary> sessions,
        IReadOnlyList<CommitRecord>? commits = null,
        IReadOnlyList<CommitRecord>? commitsBeforeRange = null,
        long unclassifiedSeconds = 0) =>
        MetricsCalculator.Calculate(From, To, sessions, commits ?? [], commitsBeforeRange ?? [], unclassifiedSeconds);

    [Fact]
    public void EmptyRange_ReturnsHonestZerosNotDivideByZero()
    {
        var m = Calculate([]);

        Assert.Equal(0, m.SessionCount);
        Assert.Equal(0, m.TrackedSeconds);
        Assert.Equal(0, m.FocusRatio);
        Assert.Equal(0, m.ActiveDays);
        Assert.Equal(0, m.InterruptionsPerActiveDay);
        Assert.Null(m.LongestBlock);
        Assert.Null(m.BestDay);
        Assert.Empty(m.TimeByProject);
        Assert.Empty(m.TicketIds);
    }

    [Fact]
    public void FocusRatio_IsDeepOverTracked()
    {
        // 1800s session, 900s of it deep (half the session is an excursion).
        var session = Sess(0, 0, 1800) with
        {
            Session = Sess(0, 0, 1800).Session with { DeepSeconds = 900 }
        };

        var m = Calculate([session]);

        Assert.Equal(1800, m.TrackedSeconds);
        Assert.Equal(900, m.DeepSeconds);
        Assert.Equal(0.5, m.FocusRatio);
    }

    [Fact]
    public void LongestBlock_IsTheSessionWithMostDeepSeconds_NotMostDuration()
    {
        // Session 1 runs longer in wall-clock time but has less actual deep work.
        var long1 = Sess(0, 0, 7200, project: "orderbook-ui") with
        {
            Session = Sess(0, 0, 7200, project: "orderbook-ui").Session with { DeepSeconds = 1000 }
        };
        var short2 = Sess(0, 8000, 9800, project: "devlog") with
        {
            Session = Sess(0, 8000, 9800, project: "devlog").Session with { DeepSeconds = 1500 }
        };

        var m = Calculate([long1, short2]);

        Assert.NotNull(m.LongestBlock);
        Assert.Equal("devlog", m.LongestBlock!.Project);
        Assert.Equal(1500, m.LongestBlock.DeepSeconds);
    }

    [Fact]
    public void BestDay_IsTheCalendarDayWithMostDeepSecondsSummed()
    {
        var day0a = Sess(0, 0, 1000);
        var day0b = Sess(0, 2000, 2500);
        var day1 = Sess(1, 0, 4000); // one big session on day 1

        var m = Calculate([day0a, day0b, day1]);

        Assert.NotNull(m.BestDay);
        Assert.Equal(From.AddDays(1), m.BestDay!.Date);
    }

    [Fact]
    public void ZeroOutputSessions_AreCountedByCommitCount()
    {
        var withCommit = Sess(0, 0, 1000, commitCount: 1);
        var withoutCommit = Sess(0, 2000, 2600, commitCount: 0);

        var m = Calculate([withCommit, withoutCommit]);

        Assert.Equal(1, m.ZeroOutputSessionCount);
        Assert.Equal(600, m.ZeroOutputSeconds);
    }

    [Fact]
    public void TicketIds_AreExtractedFromBranchNames()
    {
        var commits = new[]
        {
            Commit(0, 100, branch: "fix/US-1569-Bug_Fixing"),
            Commit(0, 200, branch: "feature/db-models"),
            Commit(0, 300, branch: "fix/US-1569-followup"), // duplicate ticket, must not repeat
        };

        var m = Calculate([Sess(0, 0, 1000)], commits);

        Assert.Equal(["US-1569"], m.TicketIds);
    }

    [Fact]
    public void MergeCommits_AreExcludedFromEveryCommitFigure()
    {
        var commits = new[]
        {
            Commit(0, 100, languages: "C#", isMerge: false),
            Commit(0, 200, languages: "Rust", isMerge: true),
        };

        var m = Calculate([Sess(0, 0, 1000)], commits);

        Assert.Equal(1, m.CommitCount);
        Assert.DoesNotContain("Rust", m.Languages);
        Assert.Contains("C#", m.Languages);
    }

    [Fact]
    public void FirstTimeLanguage_IsOnePresentInRangeButAbsentBefore()
    {
        var before = new[] { Commit(0, 0, languages: "C#") };
        var inRange = new[]
        {
            Commit(0, 100, languages: "C#"),
            Commit(0, 200, languages: "Rust"),
        };

        var m = Calculate([Sess(0, 0, 1000)], inRange, before);

        Assert.Equal(["Rust"], m.FirstTimeLanguages);
    }

    [Fact]
    public void NoPriorHistory_EverythingInRangeCountsAsFirstTime()
    {
        var inRange = new[] { Commit(0, 100, languages: "C#,TypeScript") };

        var m = Calculate([Sess(0, 0, 1000)], inRange, commitsBeforeRange: []);

        Assert.Equal(2, m.FirstTimeLanguages.Count);
        Assert.Contains("C#", m.FirstTimeLanguages);
        Assert.Contains("TypeScript", m.FirstTimeLanguages);
    }

    [Fact]
    public void UnattachedCommitsInRange_CountsNullSessionId()
    {
        var commits = new[]
        {
            Commit(0, 100, sessionId: 1),
            Commit(0, 200, sessionId: null),
            Commit(0, 300, sessionId: null),
        };

        var m = Calculate([Sess(0, 0, 1000)], commits);

        Assert.Equal(2, m.UnattachedCommitsInRange);
    }

    [Fact]
    public void UnclassifiedSeconds_PassesThroughUnchanged()
    {
        var m = Calculate([Sess(0, 0, 1000)], unclassifiedSeconds: 4321);

        Assert.Equal(4321, m.UnclassifiedSeconds);
    }

    [Fact]
    public void TimeByProject_GroupsAndSumsByProject_SkippingUnclassified()
    {
        var sessions = new[]
        {
            Sess(0, 0, 1000, project: "devlog"),
            Sess(0, 1000, 1600, project: "devlog"),
            Sess(0, 1600, 2000, project: "orderbook-ui"),
            Sess(0, 2000, 2100, project: null),
        };

        var m = Calculate(sessions);

        Assert.Equal(2, m.TimeByProject.Count);
        Assert.Equal(1600, m.TimeByProject.Single(p => p.Project == "devlog").Seconds);
        Assert.Equal(400, m.TimeByProject.Single(p => p.Project == "orderbook-ui").Seconds);
    }

    [Fact]
    public void DigestWriter_HandlesEmptyRangeWithoutThrowing()
    {
        var markdown = DigestWriter.Write(Calculate([]));

        Assert.Contains("No tracked activity", markdown);
    }

    [Fact]
    public void DigestWriter_MentionsUnattachedAndUnclassified_WhenPresent()
    {
        var commits = new[] { Commit(0, 100, sessionId: null) };
        var m = Calculate([Sess(0, 0, 1000)], commits, unclassifiedSeconds: 600);

        var markdown = DigestWriter.Write(m);

        Assert.Contains("could not be linked", markdown);
        Assert.Contains("still unclassified", markdown);
    }
}
