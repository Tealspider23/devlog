using Devlog.Core.Capture;

namespace Devlog.Core.Tests;

public class ExclusionRulesTests
{
    [Theory]
    [InlineData("KeePass")]
    [InlineData("keepass")]
    [InlineData("KEEPASS")]
    public void ProcessMatch_IsCaseInsensitive(string actual)
    {
        var rules = new ExclusionRules(["KeePass"], null);
        Assert.True(rules.IsExcluded(actual, "anything"));
    }

    [Theory]
    [InlineData("KeePass.exe", "KeePass")]
    [InlineData("KeePass", "KeePass.exe")]
    public void ExeSuffix_IsOptionalOnBothSides(string configured, string actual)
    {
        var rules = new ExclusionRules([configured], null);
        Assert.True(rules.IsExcluded(actual, null));
    }

    [Fact]
    public void TitlePattern_MatchesAnywhereAndIgnoresCase()
    {
        var rules = new ExclusionRules(null, ["incognito"]);

        Assert.True(rules.IsExcluded("chrome", "Some Page - Incognito - Google Chrome"));
        Assert.False(rules.IsExcluded("chrome", "Some Page - Google Chrome"));
    }

    [Fact]
    public void UnmatchedContext_IsNotExcluded()
    {
        var rules = new ExclusionRules(["KeePass"], ["incognito"]);
        Assert.False(rules.IsExcluded("Code", "auth.cs - devlog - Visual Studio Code"));
    }

    [Fact]
    public void None_ExcludesNothing()
    {
        Assert.False(ExclusionRules.None.IsExcluded("KeePass", "Incognito"));
    }

    [Fact]
    public void InvalidRegex_ThrowsAtConstruction()
    {
        // Loud rather than silent, on purpose. A privacy rule that quietly fails
        // to apply is the worst possible outcome, so bad config stops startup.
        var ex = Assert.Throws<ArgumentException>(() => new ExclusionRules(null, ["[unclosed"]));
        Assert.Contains("invalid regex", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BlankEntries_AreIgnoredRatherThanMatchingEverything()
    {
        var rules = new ExclusionRules(["", "  "], ["", "   "]);
        Assert.False(rules.IsExcluded("Code", "auth.cs"));
    }
}
