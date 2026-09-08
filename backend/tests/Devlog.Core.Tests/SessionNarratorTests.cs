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

        // A normal, non-exempt kind - the confidence floor still applies here.
        // See ValidateAndParse_AcceptsUnclear_EvenAtLowConfidence for the
        // exemption this used to block entirely.
        var responseJson = """
        {
          "sessionId": 412,
          "narrative": "Uncertain work.",
          "kind": "feature-work",
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

    /// <summary>
    /// The regression this session's audit exists to fix. The prompt tells the
    /// model to answer "unclear" WITH LOW CONFIDENCE when it cannot support two
    /// pieces of evidence - the MinConfidence gate then rejected exactly that
    /// answer, so "unclear" could never reach the database and every session
    /// defaulted to whichever kind survives the gate, which in practice was
    /// always the first enum value, "feature-work".
    /// </summary>
    [Fact]
    public void ValidateAndParse_AcceptsUnclear_EvenAtLowConfidence()
    {
        var summary = CreateSummary();
        var activities = CreateActivities();
        var commits = CreateCommits();

        var responseJson = """
        {
          "sessionId": 412,
          "narrative": "Scattered activity with no single thread.",
          "kind": "unclear",
          "workstream": null,
          "evidence": ["AuthController.cs", "GitLab"],
          "confidence": 0.40
        }
        """;

        var result = SessionNarratorPrompt.ValidateAndParse(
            responseJson, summary, activities, commits, 0.60, "gpt-oss:20b", 1725257000000);

        Assert.True(result.IsAccepted);
        Assert.NotNull(result.Narrative);
        Assert.Equal("unclear", result.Narrative.Kind);
        Assert.Equal(0.40, result.Narrative.Confidence);
    }

    [Fact]
    public void ValidateAndParse_AcceptsContextThrash_EvenAtLowConfidence()
    {
        var summary = CreateSummary();
        var activities = CreateActivities();
        var commits = CreateCommits();

        var responseJson = """
        {
          "sessionId": 412,
          "narrative": "Jumped between unrelated tabs with no coherent thread.",
          "kind": "context-thrash",
          "workstream": null,
          "evidence": ["AuthController.cs", "GitLab"],
          "confidence": 0.30
        }
        """;

        var result = SessionNarratorPrompt.ValidateAndParse(
            responseJson, summary, activities, commits, 0.60, "gpt-oss:20b", 1725257000000);

        Assert.True(result.IsAccepted);
        Assert.Equal("context-thrash", result.Narrative!.Kind);
    }

    /// <summary>
    /// The exemption is for the confidence floor only, not a bypass for
    /// fabrication - an "unclear" verdict with invented evidence must still be
    /// rejected by the hallucination check.
    /// </summary>
    [Fact]
    public void ValidateAndParse_StillRejectsUnclear_WhenEvidenceIsFabricated()
    {
        var summary = CreateSummary();
        var activities = CreateActivities();
        var commits = CreateCommits();

        var responseJson = """
        {
          "sessionId": 412,
          "narrative": "Something vague happened.",
          "kind": "unclear",
          "workstream": null,
          "evidence": ["Nonexistent Thing One", "Completely Invented Reference"],
          "confidence": 0.30
        }
        """;

        var result = SessionNarratorPrompt.ValidateAndParse(
            responseJson, summary, activities, commits, 0.60, "gpt-oss:20b", 1725257000000);

        Assert.False(result.IsAccepted);
        Assert.Contains("Hallucination", result.RejectionReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildBatchUserContent_SerializesEverySessionInTheBatch()
    {
        var first = new SessionNarrationInput(CreateSummary(sessionId: 412), CreateActivities(), CreateCommits());
        var second = new SessionNarrationInput(CreateSummary(sessionId: 500, project: "billing-service"), [], []);

        var json = SessionNarratorPrompt.BuildBatchUserContent([first, second]);

        Assert.Contains("412", json);
        Assert.Contains("orderbook-api", json);
        Assert.Contains("AuthController.cs", json);
        Assert.Contains("500", json);
        Assert.Contains("billing-service", json);
    }

    /// <summary>
    /// The order guarantee that lets NarrateRunner zip results back onto the
    /// sessions it asked about without trusting the model's own ordering -
    /// the response here deliberately answers session 500 before 412.
    /// </summary>
    [Fact]
    public void ValidateAndParseBatch_ReturnsResults_InInputOrder_NotResponseOrder()
    {
        var first = new SessionNarrationInput(CreateSummary(sessionId: 412), CreateActivities(), CreateCommits());
        var second = new SessionNarrationInput(CreateSummary(sessionId: 500, project: "billing-service"), CreateActivities(), CreateCommits());

        var responseJson = """
        {
          "narratives": [
            {
              "sessionId": 500,
              "narrative": "Worked on billing-service.",
              "kind": "feature-work",
              "workstream": null,
              "evidence": ["AuthController.cs", "GitLab"],
              "confidence": 0.90
            },
            {
              "sessionId": 412,
              "narrative": "Reviewed merge request !59 and fixed the login redirect loop in orderbook-api.",
              "kind": "mr-review",
              "workstream": "US-1569",
              "evidence": ["Merge request !59 in GitLab", "Edited AuthController.cs in orderbook-api"],
              "confidence": 0.95
            }
          ]
        }
        """;

        var results = SessionNarratorPrompt.ValidateAndParseBatch(
            responseJson, [first, second], 0.60, "gpt-oss:20b", 1725257000000);

        Assert.Equal(2, results.Count);
        Assert.True(results[0].IsAccepted);
        Assert.Equal(412, results[0].Narrative!.SessionId);
        Assert.True(results[1].IsAccepted);
        Assert.Equal(500, results[1].Narrative!.SessionId);
    }

    /// <summary>
    /// A session the model dropped from its response is rejected on its own -
    /// the sibling results from the same batch call are still worth keeping
    /// rather than failing the whole batch for one omission.
    /// </summary>
    [Fact]
    public void ValidateAndParseBatch_RejectsOnlyTheMissingSession_KeepsTheRest()
    {
        var first = new SessionNarrationInput(CreateSummary(sessionId: 412), CreateActivities(), CreateCommits());
        var second = new SessionNarrationInput(CreateSummary(sessionId: 500, project: "billing-service"), CreateActivities(), CreateCommits());

        var responseJson = """
        {
          "narratives": [
            {
              "sessionId": 412,
              "narrative": "Reviewed merge request !59 and fixed the login redirect loop in orderbook-api.",
              "kind": "mr-review",
              "workstream": "US-1569",
              "evidence": ["Merge request !59 in GitLab", "Edited AuthController.cs in orderbook-api"],
              "confidence": 0.95
            }
          ]
        }
        """;

        var results = SessionNarratorPrompt.ValidateAndParseBatch(
            responseJson, [first, second], 0.60, "gpt-oss:20b", 1725257000000);

        Assert.Equal(2, results.Count);
        Assert.True(results[0].IsAccepted);
        Assert.False(results[1].IsAccepted);
        Assert.Contains("No narrative returned", results[1].RejectionReason);
    }

    [Fact]
    public void ValidateAndParseBatch_StillAppliesEvidenceCheck_PerSession()
    {
        var first = new SessionNarrationInput(CreateSummary(sessionId: 412), CreateActivities(), CreateCommits());

        var responseJson = """
        {
          "narratives": [
            {
              "sessionId": 412,
              "narrative": "Something vague happened.",
              "kind": "feature-work",
              "workstream": null,
              "evidence": ["Nonexistent Thing One", "Completely Invented Reference"],
              "confidence": 0.90
            }
          ]
        }
        """;

        var results = SessionNarratorPrompt.ValidateAndParseBatch(
            responseJson, [first], 0.60, "gpt-oss:20b", 1725257000000);

        Assert.False(results[0].IsAccepted);
        Assert.Contains("Hallucination", results[0].RejectionReason, StringComparison.OrdinalIgnoreCase);
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
