using System.Text.Json;
using System.Text.Json.Serialization;
using Devlog.Core.Ai;
using Devlog.Core.Domain;

namespace Devlog.Core.Tests;

public class EvalTests
{
    [Fact]
    public void Evaluate_AllMatchingVerdicts_Returns100PercentAccuracy()
    {
        var fixtures = new List<IdentityEvalFixture>
        {
            new("GitLab", "chrome", ["MR !59"], "Coding", "Code review site"),
            new("Docs", "chrome", ["API Reference"], "Learning", "Docs")
        };

        var verdicts = new List<ValidatedVerdict>
        {
            new("GitLab", ActivityCategory.Coding, 0.95, "GitLab is for coding"),
            new("Docs", ActivityCategory.Learning, 0.90, "Documentation")
        };

        var report = JobAEvalReport.Evaluate(fixtures, verdicts, []);

        Assert.Equal(2, report.TotalLabelled);
        Assert.Equal(2, report.Correct);
        Assert.Equal(0, report.Mismatches);
        Assert.Equal(0, report.DiscardedOrSkipped);
        Assert.Equal(1.0, report.Accuracy);
    }

    [Fact]
    public void Evaluate_WithMismatchAndDiscards_CalculatesCorrectly()
    {
        var fixtures = new List<IdentityEvalFixture>
        {
            new("GitLab", "chrome", ["MR !59"], "Coding", "Code review site"),
            new("Google Search", "chrome", ["Cats"], "Unknown", "Mixed search"),
            new("Reddit", "chrome", ["r/all"], "Distraction", "Social"),
            new("Unlabelled", "chrome", ["..."], "", "Not labelled yet")
        };

        var verdicts = new List<ValidatedVerdict>
        {
            new("GitLab", ActivityCategory.Coding, 0.95, "GitLab"),
            new("Reddit", ActivityCategory.Learning, 0.85, "Reddit") // Mismatch: Expected Distraction, got Learning
        };

        var discards = new List<string> { "Google Search: confidence 0.40 below threshold" };

        var report = JobAEvalReport.Evaluate(fixtures, verdicts, discards);

        // 3 labelled fixtures evaluated (Unlabelled is skipped)
        Assert.Equal(3, report.TotalLabelled);
        Assert.Equal(2, report.Correct); // GitLab + Google Search (discarded mapped to Unknown)
        Assert.Equal(1, report.Mismatches); // Reddit
        Assert.Equal(1, report.DiscardedOrSkipped);
        Assert.Equal(2.0 / 3.0, report.Accuracy, precision: 3);
    }

    [Fact]
    public void Evaluate_OmittedVerdict_CountsAsMismatchIfExpectedKnownCategory()
    {
        var fixtures = new List<IdentityEvalFixture>
        {
            new("GitLab", "chrome", ["MR !59"], "Coding", "Code review site")
        };

        var report = JobAEvalReport.Evaluate(fixtures, [], []);

        Assert.Equal(1, report.TotalLabelled);
        Assert.Equal(0, report.Correct);
        Assert.Equal(1, report.Mismatches);
        Assert.Equal(0.0, report.Accuracy);
    }

    /// <summary>
    /// TotalLabelled was previously the only count reported, and a fixture with a
    /// blank Expected is silently skipped before it ever reaches this method - a
    /// 30-fixture export with 10 hand-labelled looked identical to a deliberate
    /// 10-fixture run. TotalSupplied is the number actually passed to the eval,
    /// independent of how many were labelled.
    /// </summary>
    [Fact]
    public void Evaluate_ReportsTotalSupplied_SeparatelyFromTotalLabelled()
    {
        var fixtures = new List<IdentityEvalFixture>
        {
            new("GitLab", "chrome", ["MR !59"], "Coding", "Code review site")
        };

        var report = JobAEvalReport.Evaluate(fixtures, [], [], totalSupplied: 30);

        Assert.Equal(30, report.TotalSupplied);
        Assert.Equal(1, report.TotalLabelled);
    }

    [Fact]
    public void Evaluate_WithNoExplicitTotalSupplied_FallsBackToFixtureCount()
    {
        var fixtures = new List<IdentityEvalFixture>
        {
            new("GitLab", "chrome", ["MR !59"], "Coding", "Code review site")
        };

        var report = JobAEvalReport.Evaluate(fixtures, [], []);

        Assert.Equal(1, report.TotalSupplied);
    }

    /// <summary>
    /// What LlmFixturesRunner writes must be exactly what LlmEvalRunner can read
    /// back - nothing previously tested that the two agreed, only a shared record
    /// definition held them together. Uses the same JsonSerializerOptions shape
    /// both classes actually use (WriteIndented, camelCase, nulls omitted for the
    /// writer; case-insensitive for the reader), not a simplified stand-in.
    /// </summary>
    [Fact]
    public void IdentityFixture_ExportedShape_RoundTripsThroughEvalDeserialisation()
    {
        var writerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var readerOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var exported = new IdentityEvalFixture(
            Identity: "jwt.io",
            Process: null,
            SampleTitles: ["JSON Web Tokens - jwt.io"],
            Expected: "",
            Note: "",
            TotalSeconds: 245,
            Hits: 12);

        var json = JsonSerializer.Serialize(new List<IdentityEvalFixture> { exported }, writerOptions);
        var roundTripped = JsonSerializer.Deserialize<List<IdentityEvalFixture>>(json, readerOptions);

        var result = Assert.Single(roundTripped!);
        Assert.Equal(exported.Identity, result.Identity);
        Assert.Equal(exported.SampleTitles, result.SampleTitles);
        Assert.Equal(exported.TotalSeconds, result.TotalSeconds);
        Assert.Equal(exported.Hits, result.Hits);

        // Process was null on export and DefaultIgnoreCondition omits it -
        // confirms the key is genuinely absent, not present-and-null, since the
        // README's example (which always shows "process": "chrome") never
        // matches what real exports contain.
        Assert.DoesNotContain("\"process\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SessionFixture_ExportedShape_RoundTripsThroughEvalDeserialisation()
    {
        var writerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var readerOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var exported = new SessionEvalFixture(
            StartUtc: 1725255180000,
            SessionId: 412,
            ExpectedKind: "",
            ExpectedWorkstream: null,
            Note: "",
            Project: "orderbook-api",
            DurationSeconds: 1583);

        var json = JsonSerializer.Serialize(new List<SessionEvalFixture> { exported }, writerOptions);
        var roundTripped = JsonSerializer.Deserialize<List<SessionEvalFixture>>(json, readerOptions);

        var result = Assert.Single(roundTripped!);
        Assert.Equal(exported.StartUtc, result.StartUtc);
        Assert.Equal(exported.SessionId, result.SessionId);
        Assert.Equal(exported.Project, result.Project);
        Assert.Equal(exported.DurationSeconds, result.DurationSeconds);

        // The field the eval actually keys sessions on. docs/llm-evals/README.md
        // and docs/LLM.md section 9 both still show only "sessionId" in their
        // example - confirming startUtc is really what round-trips is what makes
        // that a documentation gap rather than a behavioural one.
        Assert.Contains("\"startUtc\"", json, StringComparison.OrdinalIgnoreCase);
    }
}
