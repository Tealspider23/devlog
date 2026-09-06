using Devlog.Core.Configuration;
using Devlog.Core.Derivation;
using Devlog.Core.Domain;

namespace Devlog.Core.Tests;

public class SessionBuilderTests
{
    private static readonly long Base = DateTimeOffset
        .Parse("2026-08-30T09:00:00Z").ToUnixTimeMilliseconds();

    private static long At(int seconds) => Base + (seconds * 1000L);

    private static Activity Act(
        int startSeconds,
        int durationSeconds,
        ActivityCategory category,
        string? context,
        Engagement engagement = Engagement.Producing) => new()
        {
            StartUtc = At(startSeconds),
            EndUtc = At(startSeconds + durationSeconds),
            ProcessName = category == ActivityCategory.Coding ? "Code" : "chrome",
            ActivityKey = $"{category}{context}",
            Context = context,

            // Mirrors what ContextExtractor really produces: a VS Code title
            // resolves a repository, a browser title does not. Tests that need
            // coding-with-no-project (SSMS, a bare shell) override this with
            // `with { Project = null }` rather than relying on the default.
            Project = category == ActivityCategory.Coding ? context : null,

            Category = category,
            Engagement = engagement,
            TitleChanges = 0
        };

    private static SessionBuilder Build(DerivationOptions? o = null) => new(o ?? new DerivationOptions());

    // ------------------------------------------------------------ excursion folding

    /// <summary>
    /// The behaviour chosen over both strict project-scoping and time-scoping: a
    /// quick search mid-refactor is an interruption, not the end of the work.
    /// </summary>
    [Fact]
    public void ShortExcursion_IsFoldedBackInAsAnInterruption()
    {
        var activities = new[]
        {
            Act(0, 1800, ActivityCategory.Coding, "devlog"),
            Act(1800, 45, ActivityCategory.Learning, "Stack Overflow"),  // 45s lookup
            Act(1845, 1800, ActivityCategory.Coding, "devlog")
        };

        var (sessions, _) = Build().Build(activities);

        Assert.Single(sessions);
        Assert.Equal(1, sessions[0].Interruptions);
        Assert.Equal("devlog", sessions[0].Project);

        // The excursion happened inside the session, but is not deep work.
        Assert.Equal(3600, sessions[0].DeepSeconds);
    }

    [Fact]
    public void LongDetour_EndsTheSessionInsteadOfFolding()
    {
        var activities = new[]
        {
            Act(0, 1800, ActivityCategory.Coding, "devlog"),
            Act(1800, 600, ActivityCategory.Learning, "Model Context Protocol"), // 10 min
            Act(2400, 1800, ActivityCategory.Coding, "devlog")
        };

        var (sessions, _) = Build().Build(activities);

        Assert.Equal(3, sessions.Count);
        Assert.Equal(ActivityCategory.Learning, sessions[1].Category);
    }

    // ------------------------------------------------------------------- boundaries

    [Fact]
    public void DifferentProjects_NeverMerge()
    {
        var activities = new[]
        {
            Act(0, 1800, ActivityCategory.Coding, "devlog"),
            Act(1800, 1800, ActivityCategory.Coding, "orderbook")
        };

        var (sessions, _) = Build().Build(activities);

        Assert.Equal(2, sessions.Count);
        Assert.Equal("devlog", sessions[0].Project);
        Assert.Equal("orderbook", sessions[1].Project);
    }

    [Fact]
    public void GapBeyondThreshold_EndsTheSession()
    {
        var activities = new[]
        {
            Act(0, 600, ActivityCategory.Coding, "devlog"),
            Act(3600, 600, ActivityCategory.Coding, "devlog")   // an hour later
        };

        Assert.Equal(2, Build().Build(activities).Sessions.Count);
    }

    [Fact]
    public void ConsecutiveLearningPages_FormOneBlock()
    {
        // Non-coding is keyed by category alone, so three documentation pages are
        // one learning session rather than three sessions of one page each.
        var activities = new[]
        {
            Act(0, 120, ActivityCategory.Learning, "Model Context Protocol"),
            Act(120, 180, ActivityCategory.Learning, "GitHub"),
            Act(300, 240, ActivityCategory.Learning, "Stack Overflow")
        };

        var (sessions, _) = Build().Build(activities);

        Assert.Single(sessions);
        Assert.Equal(ActivityCategory.Learning, sessions[0].Category);
        Assert.Equal(540, sessions[0].DurationSeconds);
    }

    // ----------------------------------------------------------------- deep seconds

    [Fact]
    public void DeepSeconds_CountsOnlyProducingTime()
    {
        var activities = new[]
        {
            Act(0, 600, ActivityCategory.Coding, "devlog"),
            Act(600, 300, ActivityCategory.Coding, "devlog", Engagement.Idle),
            Act(900, 600, ActivityCategory.Coding, "devlog")
        };

        var (sessions, _) = Build().Build(activities);

        Assert.Single(sessions);
        Assert.Equal(1200, sessions[0].DeepSeconds);
        Assert.Equal(1500, sessions[0].DurationSeconds);
    }

    // -------------------------------------------------------------------- linkage

    [Fact]
    public void ActivitiesAreStampedWithTheirSession()
    {
        var activities = new[]
        {
            Act(0, 600, ActivityCategory.Coding, "devlog"),
            Act(600, 600, ActivityCategory.Coding, "devlog"),
            Act(1200, 600, ActivityCategory.Coding, "orderbook")
        };

        var (sessions, stamped) = Build().Build(activities);

        Assert.All(stamped, a => Assert.NotNull(a.SessionId));
        Assert.Equal(stamped[0].SessionId, stamped[1].SessionId);
        Assert.NotEqual(stamped[1].SessionId, stamped[2].SessionId);
        Assert.All(sessions, s => Assert.True(s.Id > 0));
    }

    // ------------------------------------------------------------------- overrides

    /// <summary>
    /// Overrides are keyed by identity rather than by session id, because ids do
    /// not survive a rebuild but corrections must.
    /// </summary>
    [Fact]
    public void Override_SurvivesRebuildAndWins()
    {
        var activities = new[] { Act(0, 600, ActivityCategory.Coding, "devlog") };

        var first = Build().Build(activities).Sessions[0];

        var overrides = new[]
        {
            new SessionOverride
            {
                SessionStartUtc = first.StartUtc,
                ActivityKey = first.ActivityKey,
                Category = ActivityCategory.Learning,
                Label = "spike: evaluating MCP"
            }
        };

        var rebuilt = Build().Build(activities, overrides).Sessions[0];

        Assert.Equal(ActivityCategory.Learning, rebuilt.Category);
        Assert.Equal("spike: evaluating MCP", rebuilt.Label);
    }

    // ---------------------------------------------------------------- idempotency

    [Fact]
    public void RebuildingUnchangedInput_ProducesIdenticalOutput()
    {
        var activities = new[]
        {
            Act(0, 600, ActivityCategory.Coding, "devlog"),
            Act(600, 45, ActivityCategory.Learning, "GitHub"),
            Act(645, 600, ActivityCategory.Coding, "devlog"),
            Act(1245, 900, ActivityCategory.Coding, "orderbook")
        };

        var a = Build().Build(activities).Sessions;
        var b = Build().Build(activities).Sessions;

        Assert.Equal(a.Count, b.Count);
        Assert.Equal(a, b);
    }

    // ------------------------------------------------------ project attribution (Phase 6.5)

    /// <summary>
    /// The project comes from the activity, which sets it only when an
    /// extraction rule genuinely resolved a repository. It is no longer inferred
    /// from the context just because the category is Coding — that inference is
    /// what listed "GitLab" and raw SSMS window titles as projects in the digest.
    /// </summary>
    [Fact]
    public void CodingWithNoResolvedRepo_HasNoProject()
    {
        // SSMS: coding time, real context (so sessions still group by it), but
        // nothing here names a repository.
        var ssms = Act(0, 1800, ActivityCategory.Coding, "SQLQuery1.sql - DBSERVER01...")
            with { Project = null };

        var (sessions, _) = Build().Build([ssms]);

        Assert.Single(sessions);
        Assert.Equal(ActivityCategory.Coding, sessions[0].Category);
        Assert.Null(sessions[0].Project);
    }

    [Fact]
    public void ProjectComesFromTheActivity_NotFromTheContext()
    {
        // Context and project deliberately differ: two clones of one service
        // resolve to a single configured project name.
        var activity = Act(0, 1800, ActivityCategory.Coding, "orderbook")
            with { Project = "orderbook-api" };

        var (sessions, _) = Build().Build([activity]);

        Assert.Equal("orderbook-api", sessions[0].Project);
    }

    /// <summary>
    /// The safety property behind this change: sessions are keyed by context, so
    /// splitting project out of it must not move a single boundary.
    /// </summary>
    [Fact]
    public void ProjectlessCodingSessions_StillSplitOnContextChange()
    {
        var activities = new[]
        {
            Act(0, 1800, ActivityCategory.Coding, "SSMS window A") with { Project = null },
            Act(1800, 1800, ActivityCategory.Coding, "SSMS window B") with { Project = null }
        };

        var (sessions, _) = Build().Build(activities);

        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, s => Assert.Null(s.Project));
    }
}
