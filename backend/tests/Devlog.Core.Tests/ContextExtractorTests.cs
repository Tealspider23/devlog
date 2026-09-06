using Devlog.Core.Configuration;
using Devlog.Core.Derivation;
using Devlog.Core.Domain;

namespace Devlog.Core.Tests;

/// <summary>
/// One case per title format actually observed in Phase 1 capture. Two of these
/// exist because reality disagreed with the original design.
/// </summary>
public class ContextExtractorTests
{
    [Theory]
    [InlineData("Developer telemetry reco… - devlog - Visual Studio Code", "devlog")]
    [InlineData("CollectorService.cs - devlog - Visual Studio Code", "devlog")]
    [InlineData("● NativeMethods.cs - devlog - Visual Studio Code", "devlog")]
    [InlineData("devlog - Visual Studio Code", "devlog")]
    public void VsCode_ProjectIsTheSecondToLastSegment(string title, string expected) =>
        Assert.Equal(expected, ContextExtractor.Extract("Code", title).Context);

    [Fact]
    public void VsCode_HyphenatedProjectNameSurvives()
    {
        // Splitting on " - " rather than "-" is what keeps "orderbook-api" intact.
        var result = ContextExtractor.Extract("Code", "osrm.ts - orderbook-api - Visual Studio Code");
        Assert.Equal("orderbook-api", result.Context);
    }

    /// <summary>
    /// The Claude Code terminal panel carries no project segment at all. Without
    /// the path fallback every one of these rows is silently orphaned.
    /// </summary>
    [Fact]
    public void VsCode_TerminalPanel_RecoversProjectFromPath()
    {
        var result = ContextExtractor.Extract(
            "Code",
            @"✻ [Claude Code] C:\Users\sam\source\repos\devlog\backend\src\Devlog.Host");

        Assert.Equal("devlog", result.Context);
        Assert.Equal(ActivityCategory.Coding, result.DefaultCategory);
    }

    [Theory]
    [InlineData("orderbook-api - Antigravity IDE - Implementation Plan", "orderbook-api")]
    [InlineData("orderbook-ui - Antigravity IDE - osrmService.ts", "orderbook-ui")]
    [InlineData("orderbook-api - Antigravity IDE", "orderbook-api")]
    public void AntigravityIde_ProjectIsFirst_UnlikeVsCode(string title, string expected)
    {
        // The opposite word order from VS Code. Assuming one layout for all IDEs
        // would attribute this time to a project called "Antigravity IDE".
        var result = ContextExtractor.Extract("Antigravity IDE", title);

        Assert.Equal(expected, result.Context);
        Assert.Equal(ActivityCategory.Coding, result.DefaultCategory);
    }

    [Fact]
    public void VisualStudio_StripsItsSuffix() =>
        Assert.Equal("Devlog", ContextExtractor.Extract("devenv", "Devlog - Microsoft Visual Studio").Context);

    /// <summary>
    /// The repository root, not the directory you happen to be standing in —
    /// otherwise one repo fragments into a session per subdirectory.
    /// </summary>
    [Theory]
    [InlineData("dev@DESKTOP-8H2: ~/source/repos/devlog", "devlog")]
    [InlineData("dev@DESKTOP-8H2: ~/source/repos/devlog/backend", "devlog")]
    [InlineData("dev@DESKTOP-8H2: ~/source/repos/devlog/backend/src/Devlog.Core", "devlog")]
    public void Terminal_ResolvesToRepoRootNotCurrentDirectory(string title, string expected) =>
        Assert.Equal(expected, ContextExtractor.Extract("WindowsTerminal", title).Context);

    [Fact]
    public void Explorer_UsesFolderName()
    {
        var result = ContextExtractor.Extract("explorer", "logs - File Explorer");

        Assert.Equal("logs", result.Context);
        Assert.Equal(ActivityCategory.FileManagement, result.DefaultCategory);
    }

    [Fact]
    public void Teams_ChatIsCommunication()
    {
        var result = ContextExtractor.Extract("ms-teams", "Chat | Priya Nair | Microsoft Teams");

        Assert.Equal("Priya Nair", result.Context);
        Assert.Equal(ActivityCategory.Communication, result.DefaultCategory);
    }

    [Fact]
    public void Teams_CallIsMeeting_NotChat()
    {
        // A call is not interruptible time, so it is a different category.
        var result = ContextExtractor.Extract("ms-teams", "Daily Standup | Microsoft Teams");
        Assert.Equal(ActivityCategory.Meeting, result.DefaultCategory);
    }

    /// <summary>
    /// Browsers deliberately get no default category. Chrome is Learning on docs,
    /// Coding on a pull request and Distraction on YouTube, and the process name
    /// cannot tell them apart — so the classifier decides, not this.
    /// </summary>
    [Fact]
    public void Browser_HasNoDefaultCategory()
    {
        var result = ContextExtractor.Extract(
            "chrome", "Understanding MCP servers - Model Context Protocol - Google Chrome");

        Assert.Null(result.DefaultCategory);
        Assert.Equal("Model Context Protocol", result.Context);
    }

    [Fact]
    public void UnknownProcess_FallsBackWithoutThrowing()
    {
        var result = ContextExtractor.Extract("SomeRandomApp", "Some Window");

        Assert.Equal("Some Window", result.Context);
        Assert.Null(result.DefaultCategory);
    }

    [Fact]
    public void NullsAreTolerated()
    {
        var result = ContextExtractor.Extract(null, null);
        Assert.Null(result.DefaultCategory);
    }

    // ------------------------------------------------- project vs context (Phase 6.5)
    //
    // Context is what sessions are keyed by; Project is a repository. They are
    // the same string for VS Code and different everywhere else. Conflating them
    // put "GitLab", "Windows PowerShell" and four raw SQL Server Management
    // Studio window titles into the digest's Time-by-project list, each with
    // real hours attached. Every case below is a title taken from real capture.

    [Theory]
    [InlineData("CollectorService.cs - devlog - Visual Studio Code", "devlog")]
    [InlineData("devlog - Visual Studio Code", "devlog")]
    [InlineData("osrm.ts - orderbook-api - Visual Studio Code", "orderbook-api")]
    public void VsCode_ResolvesARealProject(string title, string expected) =>
        Assert.Equal(expected, ContextExtractor.Extract("Code", title).Project);

    [Fact]
    public void VsCode_TerminalPanelPath_ResolvesAProject()
    {
        var result = ContextExtractor.Extract(
            "Code",
            @"✻ [Claude Code] C:\Users\sam\source\repos\devlog\backend\src\Devlog.Host");

        Assert.Equal("devlog", result.Project);
    }

    [Fact]
    public void Antigravity_ResolvesARealProject() =>
        Assert.Equal(
            "orderbook-api",
            ContextExtractor.Extract("Antigravity IDE", "orderbook-api - Antigravity IDE - Implementation Plan").Project);

    [Fact]
    public void VisualStudio_SolutionNameIsAProject() =>
        Assert.Equal(
            "Devlog",
            ContextExtractor.Extract("devenv", "Devlog - Microsoft Visual Studio").Project);

    [Fact]
    public void Terminal_InsideAKnownRepo_ResolvesAProject()
    {
        var result = ContextExtractor.Extract(
            "WindowsTerminal", "dev@DESKTOP: ~/source/repos/devlog/backend");

        // The repo root, not the subdirectory being stood in.
        Assert.Equal("devlog", result.Project);
    }

    /// <summary>
    /// The real title behind the digest's "Windows PowerShell: 0.3h" row. A
    /// shell with no path is still coding time, but it names no repository.
    /// </summary>
    [Fact]
    public void Terminal_BareShellName_ResolvesNoProject()
    {
        var result = ContextExtractor.Extract("powershell", "Windows PowerShell");

        Assert.Equal("Windows PowerShell", result.Context);
        Assert.Equal(ActivityCategory.Coding, result.DefaultCategory);
        Assert.Null(result.Project);
    }

    /// <summary>
    /// A directory is not a repository. Without this, an afternoon of shell work
    /// in ~/Downloads becomes a line item in a performance review.
    /// </summary>
    [Fact]
    public void Terminal_PathOutsideAnyRepo_ResolvesNoProject()
    {
        var result = ContextExtractor.Extract("WindowsTerminal", "dev@DESKTOP: ~/Downloads");

        Assert.Equal("Downloads", result.Context);
        Assert.Null(result.Project);
    }

    /// <summary>
    /// The real title behind four separate "projects" in the digest — server,
    /// database and the product name, each repeated twice.
    /// </summary>
    [Fact]
    public void Ssms_RawWindowTitle_ResolvesNoProject()
    {
        const string title =
            "SQLQuery1.sql - DBSERVER01.OrderbookDb (CORP\\dev (51))* - "
            + "SQLQuery1.sql - DBSERVER01.OrderbookDb (CORP\\dev (51))* - "
            + "Microsoft SQL Server Management Studio";

        var result = ContextExtractor.Extract("ssms", title);

        // Still grouped by the title, so sessions are unaffected.
        Assert.Equal(title, result.Context);
        Assert.Null(result.Project);
    }

    /// <summary>
    /// Reviewing a merge request is coding, but GitLab is a website, not a repo.
    /// </summary>
    [Theory]
    [InlineData("Orderbook Api · Merge requests · GitLab")]
    [InlineData("Issues · excalidraw/excalidraw · GitHub")]
    public void Browser_NeverResolvesAProject(string title) =>
        Assert.Null(ContextExtractor.Extract("chrome", title).Project);

    [Fact]
    public void Chat_NeverResolvesAProject() =>
        Assert.Null(ContextExtractor.Extract("ms-teams", "Chat | Jane Doe | Microsoft Teams").Project);

    [Fact]
    public void Explorer_FolderIsNotAProject() =>
        Assert.Null(ContextExtractor.Extract("explorer", "Downloads - File Explorer").Project);

    [Fact]
    public void UnknownProcess_ResolvesNoProject() =>
        Assert.Null(ContextExtractor.Extract("SomeRandomApp", "Some Window").Project);

    /// <summary>
    /// The resolver is authoritative about naming, which is what makes two
    /// clones of one service report as one project — the same decision the
    /// commit side already implements.
    /// </summary>
    [Fact]
    public void ConfiguredResolver_OverridesTheNameFoundInThePath()
    {
        var resolver = new ProjectResolver([
            new RepoConfig { Path = @"C:\Users\me\source\repos\team\orderbook-api", Project = "orderbook-api" }
        ]);

        var result = ContextExtractor.Extract(
            "Code",
            @"[Claude Code] C:\Users\me\source\repos\team\orderbook-api\src",
            resolver);

        // The bare regex would have said "team" — the repo sits a level
        // deeper than the pattern assumes. The configured root does not guess.
        Assert.Equal("orderbook-api", result.Project);
    }

    [Fact]
    public void NoResolver_FallsBackToThePathRegexExactlyAsBefore()
    {
        var result = ContextExtractor.Extract(
            "Code", @"[Claude Code] C:\Users\me\source\repos\devlog\backend");

        Assert.Equal("devlog", result.Project);
    }

    // ------------------------------------------- configured-name matching (Phase 6.5)
    //
    // A tab or window whose identity IS a configured project names real work on
    // that repo. `orderbook-ui - Google Chrome` is the running dev server, and
    // discarding it understated every project with browser time against it.
    // Exact match against the configured list only — never a substring.

    private static ProjectResolver Configured() => new([
        new RepoConfig { Path = @"C:\repos\team\orderbook-ui", Project = "orderbook-ui" },
        new RepoConfig { Path = @"C:\repos\devlog", Project = "devlog" }
    ]);

    [Fact]
    public void Browser_TabNamingAConfiguredProject_ResolvesIt()
    {
        var result = ContextExtractor.Extract("chrome", "orderbook-ui - Google Chrome", Configured());

        Assert.Equal("orderbook-ui", result.Context);
        Assert.Equal("orderbook-ui", result.Project);
    }

    [Theory]
    [InlineData("Orderbook Api · Merge requests · GitLab")]
    [InlineData("Issues · excalidraw/excalidraw · GitHub")]
    [InlineData("Understanding MCP servers - Model Context Protocol - Google Chrome")]
    public void Browser_SiteThatIsNotAConfiguredProject_StaysUnattributed(string title) =>
        Assert.Null(ContextExtractor.Extract("chrome", title, Configured()).Project);

    /// <summary>
    /// The SSMS title is the reason this whole change exists. It must stay
    /// unattributed even with a resolver present.
    /// </summary>
    [Fact]
    public void Ssms_StaysUnattributed_EvenWithAResolver()
    {
        const string title =
            "SQLQuery1.sql - DBSERVER01.OrderbookDb (CORP\\dev (51))* - "
            + "Microsoft SQL Server Management Studio";

        Assert.Null(ContextExtractor.Extract("ssms", title, Configured()).Project);
    }

    [Fact]
    public void Terminal_BareShell_StaysUnattributed_EvenWithAResolver() =>
        Assert.Null(ContextExtractor.Extract("powershell", "Windows PowerShell", Configured()).Project);

    /// <summary>
    /// A directory that merely shares a name with a project is still not that
    /// project unless the configured root says so — but an exact match is.
    /// </summary>
    [Fact]
    public void Terminal_DirectoryNamedLikeAConfiguredProject_ResolvesIt()
    {
        var result = ContextExtractor.Extract(
            "WindowsTerminal", "dev@DESKTOP: ~/somewhere/devlog", Configured());

        Assert.Equal("devlog", result.Project);
    }

    [Fact]
    public void Terminal_UnrelatedDirectory_StaysUnattributed() =>
        Assert.Null(ContextExtractor
            .Extract("WindowsTerminal", "dev@DESKTOP: ~/Downloads", Configured()).Project);
}
