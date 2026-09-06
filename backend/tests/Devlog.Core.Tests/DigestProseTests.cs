using Devlog.Core.Ai;
using Devlog.Core.Domain;
using Devlog.Core.Metrics;

namespace Devlog.Core.Tests;

public class DigestProseTests
{
    private static DigestFigures CreateFigures() => new()
    {
        Period = "Aug 30 to Sep 4, 2026",
        DeepWork = "22.6h",
        Tracked = "32.6h",
        FocusRatio = "69%",
        Sessions = "193",
        ActiveDays = "6",
        Commits = "30",
        LinesAdded = "16240",
        LinesRemoved = "398",
        LongestBlock = "2h05m on orderbook-api",
        BestDay = "Monday, Aug 31",
        Projects = ["devlog: 14.8h", "orderbook-api: 5h", "orderbook-ui: 4.1h"],
        Languages = ["C#", "TypeScript", "SQL"],
        FirstTimeLanguages = ["SQL", "PowerShell"],
        Tickets = ["US-1569"]
    };

    [Fact]
    public void ValidateNumbers_ReturnsTrue_WhenAllNumbersAreInFigures()
    {
        var figures = CreateFigures();
        var prose = new DigestProse(
            Summary: "Delivered 30 commits across 6 active days, with 22.6h of deep work focused on orderbook-api and devlog (69% focus ratio). Addressed ticket US-1569.",
            Highlights:
            [
                "Completed 193 sessions with 16240 lines added.",
                "Longest block was 2h05m on orderbook-api on Monday, Aug 31."
            ]
        );

        var isValid = DigestProsePrompt.ValidateNumbers(prose, figures, out var offendingToken);

        Assert.True(isValid);
        Assert.Null(offendingToken);
    }

    [Fact]
    public void ValidateNumbers_ReturnsFalse_WhenHallucinatedNumberAppearsInSummary()
    {
        var figures = CreateFigures();
        var prose = new DigestProse(
            Summary: "Delivered 99 commits with 50h of deep work.", // 99 and 50 are not in figures
            Highlights: []
        );

        var isValid = DigestProsePrompt.ValidateNumbers(prose, figures, out var offendingToken);

        Assert.False(isValid);
        Assert.NotNull(offendingToken);
        Assert.True(offendingToken == "99" || offendingToken == "50");
    }

    [Fact]
    public void ValidateNumbers_ReturnsFalse_WhenHallucinatedNumberAppearsInHighlights()
    {
        var figures = CreateFigures();
        var prose = new DigestProse(
            Summary: "Delivered 30 commits across 6 active days.",
            Highlights:
            [
                "Achieved 95% focus ratio." // 95 is not in figures (actual is 69%)
            ]
        );

        var isValid = DigestProsePrompt.ValidateNumbers(prose, figures, out var offendingToken);

        Assert.False(isValid);
        Assert.Equal("95", offendingToken);
    }

    [Fact]
    public void ValidateAndParse_AcceptsValidResponse()
    {
        var figures = CreateFigures();
        var responseJson = """
        {
          "summary": "Completed 30 commits with 22.6h deep work over 6 active days on orderbook-api and devlog.",
          "highlights": [
            "Addressed ticket US-1569.",
            "Longest focus block was 2h05m."
          ]
        }
        """;

        var result = DigestProsePrompt.ValidateAndParse(responseJson, figures);

        Assert.True(result.IsAccepted);
        Assert.NotNull(result.Prose);
        Assert.Equal(2, result.Prose.Highlights.Count);
    }

    [Fact]
    public void ValidateAndParse_RejectsNumericHallucination()
    {
        var figures = CreateFigures();
        var responseJson = """
        {
          "summary": "Completed 100 commits with 40h deep work.",
          "highlights": []
        }
        """;

        var result = DigestProsePrompt.ValidateAndParse(responseJson, figures);

        Assert.False(result.IsAccepted);
        Assert.Contains("Numeric hallucination", result.RejectionReason);
    }
}
