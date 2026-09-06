using Devlog.Core.Configuration;
using Devlog.Core.Derivation;
using Devlog.Core.Domain;

namespace Devlog.Core.Tests;

public class CommitLinkerTests
{
    private static readonly long Base = DateTimeOffset
        .Parse("2026-08-30T09:00:00Z").ToUnixTimeMilliseconds();

    private static long At(int seconds) => Base + (seconds * 1000L);

    private static Session Sess(long id, int startSeconds, int endSeconds) => new()
    {
        Id = id,
        StartUtc = At(startSeconds),
        EndUtc = At(endSeconds),
        ActivityKey = "Codingdevlog",
        Project = "devlog",
        Category = ActivityCategory.Coding,
        Interruptions = 0,
        DeepSeconds = endSeconds - startSeconds
    };

    private static CommitRecord Commit(string sha, int atSeconds) => new()
    {
        Sha = sha,
        Repo = @"C:\repos\devlog",
        Project = "devlog",
        TsUtc = At(atSeconds),
        AuthorEmail = "me@example.com",
        IsMerge = false
    };

    private static CommitLinker Linker(int windowMinutes = 30) =>
        new(new GitOptions { CommitAttachWindowMinutes = windowMinutes });

    [Fact]
    public void CommitInsideSession_Attaches()
    {
        var sessions = new[] { Sess(1, 0, 1800) };
        var commits = new[] { Commit("a", 900) };

        var linked = Linker().Link(commits, sessions);

        Assert.Equal(1, linked["a"]);
    }

    [Fact]
    public void CommitJustOutsideSession_AttachesToNearestWithinWindow()
    {
        var sessions = new[] { Sess(1, 0, 1800) };

        // 5 minutes after the session ended - within the 30 min window.
        var commits = new[] { Commit("a", 2100) };

        Assert.Equal(1, Linker().Link(commits, sessions)["a"]);
    }

    [Fact]
    public void CommitBeyondWindow_IsUnattachedNotDropped()
    {
        var sessions = new[] { Sess(1, 0, 1800) };

        // An hour after the session ended - beyond the 30 min window.
        var commits = new[] { Commit("a", 5400) };

        var linked = Linker().Link(commits, sessions);

        // Still present in the result, just mapped to null - never silently
        // removed from consideration.
        Assert.True(linked.ContainsKey("a"));
        Assert.Null(linked["a"]);
    }

    [Fact]
    public void CommitBeforeAnySession_AttachesToNearestWithinWindow()
    {
        var sessions = new[] { Sess(1, 1000, 2000) };
        var commits = new[] { Commit("a", 800) }; // 200s before session start

        Assert.Equal(1, Linker().Link(commits, sessions)["a"]);
    }

    [Fact]
    public void ManyCommitsInOneSession_AllAttachToIt()
    {
        var sessions = new[] { Sess(1, 0, 3600) };
        var commits = new[] { Commit("a", 100), Commit("b", 1800), Commit("c", 3500) };

        var linked = Linker().Link(commits, sessions);

        Assert.All(commits, c => Assert.Equal(1, linked[c.Sha]));
    }

    [Fact]
    public void OneCommitAfterHoursOfWork_StillAttaches()
    {
        // A long session, one commit right at the end.
        var sessions = new[] { Sess(1, 0, 14400) }; // 4 hours
        var commits = new[] { Commit("a", 14350) };

        Assert.Equal(1, Linker().Link(commits, sessions)["a"]);
    }

    [Fact]
    public void CommitBetweenTwoSessions_AttachesToTheCloserOne()
    {
        var sessions = new[]
        {
            Sess(1, 0, 600),      // ends at 600
            Sess(2, 1500, 2000)   // starts at 1500
        };

        // 700 -> 100s from session 1's end, 800s from session 2's start
        var commits = new[] { Commit("a", 700) };

        Assert.Equal(1, Linker().Link(commits, sessions)["a"]);
    }

    [Fact]
    public void NoSessions_EveryCommitUnattached()
    {
        var linked = Linker().Link([Commit("a", 100)], []);
        Assert.Null(linked["a"]);
    }

    [Fact]
    public void EmptyCommitList_ReturnsEmptyMap()
    {
        Assert.Empty(Linker().Link([], [Sess(1, 0, 100)]));
    }

    [Fact]
    public void ZeroWidthWindow_OnlyExactContainmentAttaches()
    {
        var sessions = new[] { Sess(1, 0, 100) };
        var commits = new[] { Commit("a", 50), Commit("b", 150) };

        var linked = Linker(windowMinutes: 0).Link(commits, sessions);

        Assert.Equal(1, linked["a"]);
        Assert.Null(linked["b"]);
    }
}
