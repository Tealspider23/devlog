using Devlog.Core.Ai;
using Devlog.Core.Domain;

namespace Devlog.Core.Tests;

public class IdentityClassifierTests
{
    [Fact]
    public void BuildUserContent_SerializesIdentitiesAndSampleTitles()
    {
        var inputs = new[]
        {
            new IdentityInput("Dashboard", "chrome", 564, 13, ["Me | Timesheet", "Attendance - Dashboard"])
        };

        var json = IdentityClassifierPrompt.BuildUserContent(inputs);

        Assert.Contains("Dashboard", json);
        Assert.Contains("Timesheet", json);
        Assert.Contains("Attendance", json);
        Assert.Contains("564", json);
    }

    [Fact]
    public void ParseVerdicts_ParsesValidVerdicts()
    {
        var inputs = new[]
        {
            new IdentityInput("GitLab", "chrome", 1200, 10, ["MR !59"]),
            new IdentityInput("Dashboard", "chrome", 600, 5, ["Timesheet"])
        };

        var responseJson = """
        {
          "verdicts": [
            {
              "identity": "GitLab",
              "category": "Coding",
              "confidence": 0.95,
              "reason": "Merge request review and code collaboration"
            },
            {
              "identity": "Dashboard",
              "category": "Personal",
              "confidence": 0.85,
              "reason": "Timesheet entry"
            }
          ]
        }
        """;

        var verdicts = IdentityClassifierPrompt.ParseVerdicts(
            responseJson,
            inputs,
            minConfidence: 0.6,
            out var discards);

        Assert.Equal(2, verdicts.Count);
        Assert.Empty(discards);

        Assert.Equal("GitLab", verdicts[0].Identity);
        Assert.Equal(ActivityCategory.Coding, verdicts[0].Category);
        Assert.Equal(0.95, verdicts[0].Confidence);

        Assert.Equal("Dashboard", verdicts[1].Identity);
        Assert.Equal(ActivityCategory.Personal, verdicts[1].Category);
    }

    [Fact]
    public void ParseVerdicts_DiscardsUnknownCategory()
    {
        var inputs = new[]
        {
            new IdentityInput("Google Search", "chrome", 400, 4, ["Search query"])
        };

        var responseJson = """
        {
          "verdicts": [
            {
              "identity": "Google Search",
              "category": "Unknown",
              "confidence": 0.7,
              "reason": "Mixed use search engine"
            }
          ]
        }
        """;

        var verdicts = IdentityClassifierPrompt.ParseVerdicts(
            responseJson,
            inputs,
            minConfidence: 0.6,
            out var discards);

        Assert.Empty(verdicts);
        Assert.Single(discards);
        Assert.Contains("Unknown", discards[0]);
    }

    [Fact]
    public void ParseVerdicts_DiscardsBelowMinConfidence()
    {
        var inputs = new[]
        {
            new IdentityInput("Portal", "chrome", 300, 2, ["Portal page"])
        };

        var responseJson = """
        {
          "verdicts": [
            {
              "identity": "Portal",
              "category": "Learning",
              "confidence": 0.45,
              "reason": "Unsure if documentation"
            }
          ]
        }
        """;

        var verdicts = IdentityClassifierPrompt.ParseVerdicts(
            responseJson,
            inputs,
            minConfidence: 0.6,
            out var discards);

        Assert.Empty(verdicts);
        Assert.Single(discards);
        Assert.Contains("confidence", discards[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseVerdicts_DiscardsInventedIdentityNotSentInInput()
    {
        var inputs = new[]
        {
            new IdentityInput("VS Code", "Code", 5000, 50, ["main.cs"])
        };

        var responseJson = """
        {
          "verdicts": [
            {
              "identity": "InventedWebsite.com",
              "category": "Coding",
              "confidence": 0.99,
              "reason": "Invented by model"
            }
          ]
        }
        """;

        var verdicts = IdentityClassifierPrompt.ParseVerdicts(
            responseJson,
            inputs,
            minConfidence: 0.6,
            out var discards);

        Assert.Empty(verdicts);
        Assert.Single(discards);
        Assert.Contains("InventedWebsite.com", discards[0]);
    }

    [Fact]
    public void ParseVerdicts_DiscardsInvalidCategoryString()
    {
        var inputs = new[]
        {
            new IdentityInput("App", "app", 100, 1, ["Title"])
        };

        var responseJson = """
        {
          "verdicts": [
            {
              "identity": "App",
              "category": "NonExistentCategory",
              "confidence": 0.9,
              "reason": "Bad category string"
            }
          ]
        }
        """;

        var verdicts = IdentityClassifierPrompt.ParseVerdicts(
            responseJson,
            inputs,
            minConfidence: 0.6,
            out var discards);

        Assert.Empty(verdicts);
        Assert.Single(discards);
        Assert.Contains("NonExistentCategory", discards[0]);
    }
}
