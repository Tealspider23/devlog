using Devlog.Core.Derivation;
using Devlog.Core.Domain;

namespace Devlog.Core.Tests;

public class ClassifierTests
{
    private static ClassificationRule Site(
        string site,
        ActivityCategory? category,
        bool mixed = false,
        string source = "manual") => new()
        {
            Scope = RuleScope.Site,
            Site = site,
            Category = category,
            IsMixed = mixed,
            SourceName = source
        };

    private static ClassificationRule Page(string site, string keyword, ActivityCategory category) => new()
    {
        Scope = RuleScope.Page,
        Site = site,
        Keyword = keyword,
        Category = category,
        SourceName = "manual"
    };

    [Fact]
    public void SiteRule_AnswersEveryPageFromThatSite()
    {
        // The premise: one verdict on "Model Context Protocol" settles all three
        // pages, and never asks again.
        var classifier = new Classifier([Site("Model Context Protocol", ActivityCategory.Learning)]);

        foreach (var title in new[]
        {
            "Understanding MCP servers - Model Context Protocol - Google Chrome",
            "What is the Model Context Protocol (MCP)? - Model Context Protocol - Google Chrome",
            "Build an MCP server - Model Context Protocol - Google Chrome"
        })
        {
            var result = classifier.Classify("chrome", title, "Model Context Protocol", null);

            Assert.Equal(ActivityCategory.Learning, result.Category);
            Assert.False(result.IsPending);
        }
    }

    [Fact]
    public void PageRule_BeatsSiteRule()
    {
        // Page rules only exist for sites that turned out to be mixed-use, where
        // the site-level answer is known to be unreliable.
        var classifier = new Classifier(
        [
            Site("YouTube", ActivityCategory.Learning),
            Page("YouTube", "Premier League", ActivityCategory.Distraction)
        ]);

        var football = classifier.Classify(
            "chrome", "Premier League highlights - YouTube", "YouTube", null);

        Assert.Equal(ActivityCategory.Distraction, football.Category);
    }

    [Fact]
    public void MixedSite_IgnoresItsOwnSiteLevelAnswer()
    {
        // It was demoted precisely because it is wrong about half the time.
        var classifier = new Classifier(
        [
            Site("YouTube", ActivityCategory.Learning, mixed: true),
            Page("YouTube", "Rust", ActivityCategory.Learning)
        ]);

        var unmatched = classifier.Classify(
            "chrome", "Cooking with cast iron - YouTube", "YouTube", null);

        Assert.True(unmatched.IsPending);

        var matched = classifier.Classify(
            "chrome", "Rust Lifetimes Explained - YouTube", "YouTube", null);

        Assert.Equal(ActivityCategory.Learning, matched.Category);
        Assert.False(matched.IsPending);
    }

    [Fact]
    public void BuiltinProcess_NeedsNoRule()
    {
        // This is why the unanswered list is mostly browser sites: editors,
        // terminals and chat apps are never in doubt.
        var classifier = new Classifier([]);

        Assert.Equal(
            ActivityCategory.Coding,
            classifier.Classify("Code", "a.cs - devlog - Visual Studio Code", "Code", null).Category);

        Assert.Equal(
            ActivityCategory.Communication,
            classifier.Classify("ms-teams", "Chat | X | Microsoft Teams", "ms-teams", null).Category);
    }

    [Fact]
    public void ContextDefault_IsUsedBeforeBuiltinLookup()
    {
        var classifier = new Classifier([]);

        var result = classifier.Classify(
            "ms-teams", "Daily Standup | Microsoft Teams", "ms-teams", ActivityCategory.Meeting);

        Assert.Equal(ActivityCategory.Meeting, result.Category);
    }

    [Fact]
    public void ConfigOverride_BeatsBuiltin()
    {
        var classifier = new Classifier(
            [],
            new Dictionary<string, ActivityCategory>(StringComparer.OrdinalIgnoreCase)
            {
                ["explorer"] = ActivityCategory.Coding
            });

        Assert.Equal(
            ActivityCategory.Coding,
            classifier.Classify("explorer", "src - File Explorer", "explorer", null).Category);
    }

    [Theory]
    [InlineData("Acme / Orderbook Api · GitLab", ActivityCategory.Coding)]
    [InlineData("Fix login redirect (!59) · Merge request", ActivityCategory.Coding)]
    [InlineData("Redis Streams | Documentation", ActivityCategory.Learning)]
    [InlineData("Deadlock (computer science) - Wikipedia", ActivityCategory.Learning)]
    [InlineData("750 Sqft- 5th floor (out of 14)", ActivityCategory.Personal)]
    [InlineData("Property for rent in Gachibowli", ActivityCategory.Personal)]
    [InlineData("IRCTC Next Generation eTicketing System", ActivityCategory.Personal)]
    [InlineData("Wordle - The New York Times", ActivityCategory.Distraction)]
    [InlineData("Me | Timesheet", ActivityCategory.Other)]
    [InlineData("Me | Expenses | Pending", ActivityCategory.Other)]
    [InlineData("LaunchApps", ActivityCategory.Other)]
    [InlineData("Inbox - Amit Behera - Outlook", ActivityCategory.Communication)]
    public void BuiltinKeyword_AnswersWhatAProcessNameCannot(string title, ActivityCategory expected)
    {
        // These are real identities from the unanswered pile. A verdict a
        // substring can reach is not worth an inference call or your attention.
        var classifier = new Classifier([]);
        var result = classifier.Classify("chrome", title, SiteIdentity.For("chrome", title), null);

        Assert.Equal(expected, result.Category);
        Assert.False(result.IsPending);
    }

    [Fact]
    public void BuiltinKeyword_LosesToManualAndLlm()
    {
        // Precedence is the whole reason keyword matching is safe to add: it is
        // the last automatic resort, so it can never overwrite a real verdict.
        const string title = "Rust ownership - Documentation - Google Chrome";
        const string identity = "Documentation";

        var manual = new Classifier([Site(identity, ActivityCategory.Coding)])
            .Classify("chrome", title, identity, null);

        Assert.Equal(ActivityCategory.Coding, manual.Category);
        Assert.Equal("manual", manual.Source);

        var llm = new Classifier([Site(identity, ActivityCategory.Distraction, source: "llm")])
            .Classify("chrome", title, identity, null);

        Assert.Equal(ActivityCategory.Distraction, llm.Category);
        Assert.Equal("llm", llm.Source);
    }

    [Fact]
    public void BareYouTube_StaysPending_SoItCanPromoteItself()
    {
        // Deliberately absent from the keyword table. YouTube is the archetypal
        // mixed-use site and belongs to the promotion mechanism — answering it
        // with a blanket substring would defeat the point of having one.
        var result = new Classifier([])
            .Classify("chrome", "Cooking with cast iron - YouTube", "YouTube", null);

        Assert.True(result.IsPending);
    }

    [Fact]
    public void YouTubeMusic_IsMatchedBeforeAnyBroaderYouTubeRule()
    {
        // First match wins, so specific patterns must precede general ones.
        var result = new Classifier([])
            .Classify("chrome", "Radiohead - YouTube Music", "YouTube Music", null);

        Assert.Equal(ActivityCategory.Distraction, result.Category);
    }

    /// <summary>
    /// Unanswered must never block derivation, and must be distinguishable from a
    /// deliberate verdict of Other — otherwise settled things go back on the
    /// unanswered list forever.
    /// </summary>
    [Fact]
    public void UnknownSite_IsPendingButStillCategorised()
    {
        var result = new Classifier([])
            .Classify("chrome", "Some Blog - Google Chrome", "Some Blog", null);

        Assert.True(result.IsPending);
        Assert.Equal(ActivityCategory.Other, result.Category);
        Assert.Equal(ClassificationSource.Pending, result.Source);
    }

    [Fact]
    public void DeliberateOther_IsNotPending()
    {
        var classifier = new Classifier([Site("Some Blog", ActivityCategory.Other)]);
        var result = classifier.Classify("chrome", "Some Blog - Google Chrome", "Some Blog", null);

        Assert.False(result.IsPending);
        Assert.Equal(ActivityCategory.Other, result.Category);
    }

    [Fact]
    public void PendingRule_WithNullCategory_DoesNotResolve()
    {
        // Sighting rows are written with a null category. They record that
        // something was seen, not what it was.
        var classifier = new Classifier([Site("Some Blog", null)]);

        Assert.True(classifier.Classify("chrome", "x - Google Chrome", "Some Blog", null).IsPending);
    }

    [Fact]
    public void SourceIsReported_SoTheUiCanBeHonestAboutProvenance()
    {
        var classifier = new Classifier([Site("Docs", ActivityCategory.Learning, source: "llm")]);
        var result = classifier.Classify("chrome", "x - Docs - Google Chrome", "Docs", null);

        Assert.Equal("llm", result.Source);
    }
}
