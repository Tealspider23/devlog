using Devlog.Core.Derivation;

namespace Devlog.Core.Tests;

public class SiteIdentityTests
{
    /// <summary>
    /// The exact Chrome titles captured on 2026-08-30 between 21:54 and 21:58.
    /// Real data, not invented — the collapse ratio is the entire premise of
    /// classifying per thing rather than per occurrence, so it is asserted
    /// against reality.
    /// </summary>
    private static readonly string[] RealChromeTitles =
    [
        "open source projects - github - Google Search - Google Chrome",
        "trimstray/the-book-of-secret-knowledge: A collection of inspiring lists, manuals",
        "trimstray/the-book-of-secret-knowledge: A collection of inspiring lists, manuals",
        "New tab - Google Chrome",
        "GitHub - Google Chrome",
        "Repository search results - Google Chrome",
        "anthropics/claude-code: Claude Code is an agentic coding tool that lives in your terminal",
        "Repository search results - Google Chrome",
        "thedotmack/claude-mem: Persistent Context Across Sessions for Every Agent",
        "thedotmack/claude-mem: Persistent Context Across Sessions for Every Agent",
        "Understanding MCP servers - Model Context Protocol - Google Chrome",
        "What is the Model Context Protocol (MCP)? - Model Context Protocol - Google Chrome",
        "Build an MCP server - Model Context Protocol - Google Chrome",
        "Elite Graduate Resume Project Architecture - Google Gemini - Google Chrome"
    ];

    [Fact]
    public void RealCapture_CollapsesFourteenTitlesToSixIdentities()
    {
        var identities = RealChromeTitles
            .Select(t => SiteIdentity.For("chrome", t)!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["GitHub", "Google Gemini", "Google Search", "Model Context Protocol", "New tab", "Repository search results"],
            identities);

        // 14 events -> 6 questions, and 5 once "New tab" is dropped as noise.
        // A ratio anywhere near 1:1 would mean the premise has failed.
        Assert.True(identities.Length * 2 < RealChromeTitles.Length);
    }

    [Theory]
    [InlineData("Understanding MCP servers - Model Context Protocol - Google Chrome")]
    [InlineData("What is the Model Context Protocol (MCP)? - Model Context Protocol - Google Chrome")]
    [InlineData("Build an MCP server - Model Context Protocol - Google Chrome")]
    public void ThreeMcpPages_AreOneIdentity(string title) =>
        Assert.Equal("Model Context Protocol", SiteIdentity.For("chrome", title));

    [Theory]
    [InlineData("anthropics/claude-code: Claude Code is an agentic coding tool")]
    [InlineData("thedotmack/claude-mem: Persistent Context Across Sessions")]
    [InlineData("trimstray/the-book-of-secret-knowledge: A collection")]
    [InlineData("dotnet/runtime: .NET is a cross-platform runtime")]
    public void GitHubRepoPages_AllIdentifyAsGitHub(string title) =>
        Assert.Equal("GitHub", SiteIdentity.For("chrome", title));

    [Fact]
    public void GitHubRepoPattern_WinsEvenWithoutBrowserSuffix()
    {
        // Repo pages are long enough that Chrome drops its own branding.
        Assert.Equal("GitHub", SiteIdentity.For("chrome", "owner/repo: some description here"));
    }

    [Theory]
    [InlineData("Tealspider23/devlog")]
    [InlineData("excalidraw/excalidraw")]
    [InlineData("Tealspider23/devlog - Google Chrome")]
    public void GitHubRepoWithNoDescription_StillIdentifiesAsGitHub(string title)
    {
        // A repo with no description titles as bare "owner/repo" — which is every
        // repo of your own before you write one. Requiring the colon missed these
        // entirely and each earned its own pending identity.
        Assert.Equal("GitHub", SiteIdentity.For("chrome", title));
    }

    [Theory]
    [InlineData("Issues · excalidraw/excalidraw - Google Chrome")]
    [InlineData("devlog/docs at master · Tealspider23/devlog - Google Chrome")]
    [InlineData("Pull requests · dotnet/runtime - Google Chrome")]
    public void GitHubTabs_PutTheRepoLast_AndStillIdentifyAsGitHub(string title)
    {
        // Issues, Pull requests and file views all title as "<page> · owner/repo",
        // which the head-anchored pattern cannot see. Each would otherwise earn a
        // second identity beside GitHub.
        Assert.Equal("GitHub", SiteIdentity.For("chrome", title));
    }

    [Fact]
    public void SlashInTitle_IsNotMistakenForARepo()
    {
        // The bare form is anchored at both ends precisely so an ordinary title
        // that happens to open with a slashed pair is left alone.
        Assert.Equal("Wikipedia", SiteIdentity.For("chrome", "AC/DC - Wikipedia - Google Chrome"));
    }

    [Theory]
    [InlineData("chrome", "Some Page - Google Chrome", "Some Page")]
    [InlineData("firefox", "Some Page — Mozilla Firefox", "Some Page")]
    [InlineData("msedge", "Some Page - Microsoft Edge", "Some Page")]
    public void BrowserBranding_IsStripped(string process, string title, string expected) =>
        Assert.Equal(expected, SiteIdentity.For(process, title));

    [Fact]
    public void EdgeMultiTabSuffix_IsStripped()
    {
        Assert.Equal(
            "Some Page",
            SiteIdentity.For("msedge", "Some Page and 4 more pages - Microsoft Edge"));
    }

    [Fact]
    public void TitleWithNoSeparator_IsUsedWhole() =>
        Assert.Equal("Gmail", SiteIdentity.For("chrome", "Gmail - Google Chrome"));

    [Fact]
    public void NonBrowser_IdentifiesAsTheProcess()
    {
        // One verdict on "Antigravity IDE", settled forever - no title parsing.
        Assert.Equal(
            "Antigravity IDE",
            SiteIdentity.For("Antigravity IDE", "orderbook-api - Antigravity IDE - Implementation Plan"));

        Assert.Equal("Code", SiteIdentity.For("Code", "auth.cs - devlog - Visual Studio Code"));
    }

    [Fact]
    public void MissingTitle_FallsBackToProcess() =>
        Assert.Equal("chrome", SiteIdentity.For("chrome", null));

    [Fact]
    public void MissingProcess_IsNull() =>
        Assert.Null(SiteIdentity.For(null, "anything"));

    [Fact]
    public void VeryLongIdentity_IsTruncated()
    {
        var long_ = new string('x', 200);
        var result = SiteIdentity.For("chrome", long_);

        Assert.NotNull(result);
        Assert.True(result!.Length <= 60);
    }

    [Fact]
    public void IsBrowser_RecognisesTheCommonOnes()
    {
        Assert.True(SiteIdentity.IsBrowser("chrome"));
        Assert.True(SiteIdentity.IsBrowser("MSEDGE"));
        Assert.False(SiteIdentity.IsBrowser("Code"));
        Assert.False(SiteIdentity.IsBrowser(null));
    }
}
