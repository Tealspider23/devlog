using Devlog.Host.Commands;

namespace Devlog.Host.Tests;

public class CommandCatalogTests
{
    [Theory]
    [InlineData("stats")]
    [InlineData("derive")]
    [InlineData("purge-seed")]
    [InlineData("SESSIONS")]
    public void KnownCommands_AreRecognised_CaseInsensitively(string name) =>
        Assert.True(CommandCatalog.IsKnown(name));

    [Theory]
    [InlineData("banana")]
    [InlineData("")]
    [InlineData("tray")]
    public void UnknownCommands_AreRejected(string name) =>
        Assert.False(CommandCatalog.IsKnown(name));

    [Theory]
    [InlineData("sesions", "sessions")]
    [InlineData("drive", "derive")]
    [InlineData("comits", "commits")]
    [InlineData("unkowns", "unknowns")]
    public void RealisticTypos_GetASuggestion(string typed, string expected)
    {
        // Prefix matching caught none of these — they are transpositions and
        // dropped letters, which is what typos actually are.
        Assert.Equal(expected, CommandCatalog.ClosestTo(typed));
    }

    [Theory]
    [InlineData("banana")]
    [InlineData("install")]
    public void UnrelatedWords_GetNoSuggestion(string typed)
    {
        // A confident wrong guess is worse than silence when the full command
        // list is printed directly underneath.
        Assert.Null(CommandCatalog.ClosestTo(typed));
    }

    [Fact]
    public void EveryCommand_HasAGroupThatTheHelpScreenRenders()
    {
        // A command whose group is not in Groups exists but is invisible.
        Assert.All(CommandCatalog.All, c => Assert.Contains(c.Group, CommandCatalog.Groups));
    }

    [Fact]
    public void EveryCommandsUsage_StartsWithItsName()
    {
        // The help screen shows Usage; if it disagrees with Name, the line it
        // prints is not a line you can type.
        Assert.All(
            CommandCatalog.All,
            c => Assert.StartsWith(c.Name, c.Usage, StringComparison.Ordinal));
    }

    [Fact]
    public void HelpRenders_WithoutAStatusLine()
    {
        // The moment you most need help is when the database is missing or
        // locked, so status must be optional rather than required.
        var help = HelpScreen.Render();

        Assert.Contains("USAGE", help);
        Assert.All(CommandCatalog.All, c => Assert.Contains(c.Name, help));
    }
}

public class CommandLineTests
{
    [Fact]
    public void Subcommand_IsRewrittenAsTheFlagTheDispatcherKnows()
    {
        var cli = new CommandLine(["sessions", "20"]);

        Assert.True(cli.Has("--sessions"));
        Assert.Equal(20, cli.ValueOrDefault("--sessions", 40));
    }

    [Fact]
    public void FlagForm_StillWorksUnchanged()
    {
        // A week of shell history and the whole README use flags. Breaking them
        // to gain a nicer surface would be a poor trade.
        var cli = new CommandLine(["--sessions", "20"]);

        Assert.True(cli.Has("--sessions"));
        Assert.Equal(20, cli.ValueOrDefault("--sessions", 40));
    }

    [Fact]
    public void OnlyTheFirstArgumentIsRewritten()
    {
        // "classify Dashboard Other" must not become "--Dashboard --Other".
        var cli = new CommandLine(["classify", "Dashboard", "Other"]);

        Assert.Equal(["Dashboard", "Other"], cli.ValuesAfter("--classify"));
    }

    [Fact]
    public void SubcommandWithTrailingFlag_KeepsTheFlag()
    {
        var cli = new CommandLine(["startup", "--enable"]);

        Assert.True(cli.Has("--startup"));
        Assert.True(cli.Has("--enable"));
    }

    [Fact]
    public void HelpIsRequested_ByEverySpellingAndByNothingAtAll()
    {
        string[][] asks = [[], ["--help"], ["help"], ["-h"], ["-?"], ["/?"], ["stats", "--help"]];

        Assert.All(asks, a => Assert.True(CommandLine.WantsHelp(a), string.Join(' ', a)));
    }

    [Fact]
    public void RealCommands_AreNotMistakenForHelp()
    {
        string[][] commands = [["stats"], ["--stats"], ["sessions", "20"]];

        Assert.All(commands, a => Assert.False(CommandLine.WantsHelp(a), string.Join(' ', a)));
    }

    [Fact]
    public void CommandName_StripsDashes()
    {
        Assert.Equal("sessions", CommandLine.CommandName(["sessions"]));
        Assert.Equal("sessions", CommandLine.CommandName(["--sessions"]));
        Assert.Null(CommandLine.CommandName([]));
    }

    [Fact]
    public void NormaliseDoesNotMutateTheCallersArray()
    {
        var args = new[] { "sessions", "20" };
        _ = CommandLine.Normalise(args);

        Assert.Equal("sessions", args[0]);
    }
}
