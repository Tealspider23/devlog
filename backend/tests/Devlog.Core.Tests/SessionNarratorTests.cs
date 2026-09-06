using Devlog.Core.Ai;
using Devlog.Core.Domain;

namespace Devlog.Core.Tests;

public class SessionNarratorTests
{
    private static SessionSummary CreateSummary(long sessionId = 412, string project = "orderbook-api")
    {
        var session = new Session
        {
            Id = sessionId,
            StartUtc = 1725255180000, // 2026-09-02T11:03:00 UTC
            EndUtc = 1725256763000,
            ActivityKey = "orderbook-api",
            Project = project,
            Category = ActivityCategory.Coding,
            Interruptions = 5,
            DeepSeconds = 1350,
            Label = null
        };

        return new SessionSummary
        {
            Session = session,
            ActivityCount = 3,
            CommitCount = 1,
            Insertions = 24,
            Deletions = 8
        };
    }

    private static List<Activity> CreateActivities() =>
    [
        new()
        {
            Id = 1,
            StartUtc = 1725255180000,
            EndUtc = 1725255420000,
            ProcessName = "ms-teams",
            ActivityKey = "Microsoft Teams",
            Context = "Priya | orderbook-api | Microsoft Teams",
            Project = null,
            SiteIdentity = "Microsoft Teams",
            Category = ActivityCategory.Communication,
            Engagement = Engagement.Producing,
            TitleChanges = 1,
            SampleTitle = "Priya | orderbook-api | Microsoft Teams",
            SessionId = 412
        },
        new()
        {
            Id = 2,
            StartUtc = 1725255420000,
            EndUtc = 1725255840000,
            ProcessName = "chrome",
            ActivityKey = "GitLab",
            Context = "Fix login redirect (!59) - Merge request",
            Project = null,
            SiteIdentity = "GitLab",
            Category = ActivityCategory.Coding,
            Engagement = Engagement.Producing,
            TitleChanges = 2,
            SampleTitle = "Fix login redirect (!59) - Merge request",
            SessionId = 412
        },
        new()
        {
            Id = 3,
            StartUtc = 1725255840000,
            EndUtc = 1725256763000,
            ProcessName = "Code",
            ActivityKey = "orderbook-api",
            Context = "AuthController.cs - orderbook-api - Visual Studio Code",
            Project = "orderbook-api",
            SiteIdentity = "Code",
            Category = ActivityCategory.Coding,
            Engagement = Engagement.Producing,
            TitleChanges = 5,
            SampleTitle = "AuthController.cs - orderbook-api - Visual Studio Code",
            SessionId = 412
        }
    ];

    private static List<CommitRecord> CreateCommits() =>
    [
        new()
        {
            Sha = "a1b2c3d4e5f6",
            Repo = "orderbook-api",
            Project = "orderbook-api",
            TsUtc = 1725256700000,
            Branch = "fix/US-1569-Bug_Fixing",
            Message = "fix: login redirect loop",
            AuthorEmail = "user@example.com",
            Insertions = 24,
            Deletions = 8,
            FilesChanged = 3,
            IsMerge = false,
            SessionId = 412
        }
    ];

    [Fact]
    public void BuildUserContent_SerializesSessionActivitiesAndCommits()
    {
        var summary = CreateSummary();
        var activities = CreateActivities();
        var commits = CreateCommits();

        var json = SessionNarratorPrompt.BuildUserContent(summary, activities, commits);

        Assert.Contains("412", json);
        Assert.Contains("orderbook-api", json);
        Assert.Contains("Priya", json);
        Assert.Contains("AuthController.cs", json);
        Assert.Contains("fix: login redirect loop", json);
        Assert.Contains("US-1569", json);
    }

    [Fact]
    public void ValidateEvidence_ReturnsTrue_WhenEvidenceIsSupportedByHaystack()
    {
        var summary = CreateSummary();
        var activities = CreateActivities();
        var commits = CreateCommits();

        var evidence = new[]
        {
            "Reviewed merge request !59 in GitLab",
            "Edited AuthController.cs in orderbook-api",
            "Committed fix login redirect loop to branch fix/US-1569-Bug_Fixing"
        };

        var isValid = SessionNarratorPrompt.ValidateEvidence(
            evidence, summary.Session, activities, commits, out var supportedCount);

        Assert.True(isValid);
        Assert.True(supportedCount >= 2);
    }

    [Fact]
    public void ValidateEvidence_ReturnsFalse_WhenEvidenceIsHallucinated()
    {
        var summary = CreateSummary();
        var activities = CreateActivities();
        var commits = CreateCommits();

        var hallucinatedEvidence = new[]
        {
            "Discussed roadmap with Dave on Zoom",
            "Created JIRA ticket BACKLOG-9999",
            "Deployed kubernetes helm chart to production"
        };

        var isValid = SessionNarratorPrompt.ValidateEvidence(
            hallucinatedEvidence, summary.Session, activities, commits, out var supportedCount);

        Assert.False(isValid);
        Assert.Equal(0, supportedCount);
    }

    [Fact]
    public void ValidateAndParse_AcceptsValidModelResponse()
    {
        var summary = CreateSummary();
        var activities = CreateActivities();
        var commits = CreateCommits();

        var responseJson = """
        {
          "sessionId": 412,
          "narrative": "Reviewed merge request !59 and fixed the login redirect loop in orderbook-api.",
          "kind": "mr-review",
          "workstream": "US-1569",
          "evidence": [
            "Merge request !59 in GitLab",
            "Edited AuthController.cs in orderbook-api",
            "Committed fix login redirect loop"
          ],
          "confidence": 0.95
        }
        """;

        var result = SessionNarratorPrompt.ValidateAndParse(
            responseJson, summary, activities, commits, 0.60, "gpt-oss:20b", 1725257000000);

        Assert.True(result.IsAccepted);
        Assert.NotNull(result.Narrative);
        Assert.Equal(412, result.Narrative.SessionId);
        Assert.Equal("mr-review", result.Narrative.Kind);
        Assert.Equal("US-1569", result.Narrative.Workstream);
        Assert.Equal(0.95, result.Narrative.Confidence);
        Assert.Equal("gpt-oss:20b", result.Narrative.Model);
    }

    [Fact]
    public void ValidateAndParse_RejectsSessionIdMismatch()
    {
        var summary = CreateSummary(sessionId: 412);
        var activities = CreateActivities();
        var commits = CreateCommits();

        var responseJson = """
        {
          "sessionId": 999,
          "narrative": "Something else.",
          "kind": "feature-work",
          "workstream": null,
          "evidence": ["AuthController.cs", "GitLab"],
          "confidence": 0.90
        }
        """;

        var result = SessionNarratorPrompt.ValidateAndParse(
            responseJson, summary, activities, commits, 0.60, "gpt-oss:20b", 1725257000000);

        Assert.False(result.IsAccepted);
        Assert.Contains("mismatch", result.RejectionReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateAndParse_RejectsLowConfidence()
    {
        var summary = CreateSummary();
        var activities = CreateActivities();
        var commits = CreateCommits();

        var responseJson = """
        {
          "sessionId": 412,
          "narrative": "Uncertain work.",
          "kind": "unclear",
          "workstream": null,
          "evidence": ["AuthController.cs", "GitLab"],
          "confidence": 0.40
        }
        """;

        var result = SessionNarratorPrompt.ValidateAndParse(
            responseJson, summary, activities, commits, 0.60, "gpt-oss:20b", 1725257000000);

        Assert.False(result.IsAccepted);
        Assert.Contains("below threshold", result.RejectionReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateAndParse_RejectsInvalidKind()
    {
        var summary = CreateSummary();
        var activities = CreateActivities();
        var commits = CreateCommits();

        var responseJson = """
        {
          "sessionId": 412,
          "narrative": "Random kind.",
          "kind": "invented-kind",
          "workstream": null,
          "evidence": ["AuthController.cs", "GitLab"],
          "confidence": 0.90
        }
        """;

        var result = SessionNarratorPrompt.ValidateAndParse(
            responseJson, summary, activities, commits, 0.60, "gpt-oss:20b", 1725257000000);

        Assert.False(result.IsAccepted);
        Assert.Contains("Invalid kind", result.RejectionReason, StringComparison.OrdinalIgnoreCase);
    }
}
