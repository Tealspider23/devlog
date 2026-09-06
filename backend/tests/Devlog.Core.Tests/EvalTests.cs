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
}
