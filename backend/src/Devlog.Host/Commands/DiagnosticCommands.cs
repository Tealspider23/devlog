using Devlog.Core.Abstractions;
using Devlog.Core.Domain;
using Devlog.Host.Derivation;
using Devlog.Host.Diagnostics;
using Devlog.Infrastructure.Persistence;

namespace Devlog.Host.Commands;

/// <summary>
/// The one-shot commands. Each builds nothing of its own — they resolve the same
/// services the tray app uses, so a diagnostic can never disagree with what the
/// running collector would do.
/// </summary>
internal static class DiagnosticCommands
{
    /// <summary>
    /// Returns null when no one-shot command was requested, meaning the caller
    /// should fall through to running the tray.
    /// </summary>
    public static int? TryRun(IHost host, CommandLine cli)
    {
        if (cli.Has("--stats"))
        {
            return Stats(host);
        }

        if (cli.Has("--events"))
        {
            return Events(host, cli);
        }

        if (cli.Has("--sessions"))
        {
            return Sessions(host, cli);
        }

        if (cli.Has("--derive"))
        {
            return Derive(host);
        }

        if (cli.Has("--unknowns"))
        {
            return Unknowns(host, cli);
        }

        if (cli.Has("--scan-git"))
        {
            return ScanGit(host);
        }

        if (cli.Has("--commits"))
        {
            return Commits(host, cli);
        }

        if (cli.Has("--config"))
        {
            return Config(host);
        }

        return cli.Has("--classify") ? Classify(host, cli) : null;
    }

    private static int Stats(IHost host)
    {
        CommandLine.TrySetUtf8Console();
        Console.WriteLine(host.Services.GetRequiredService<StatsReporter>().Report());
        return 0;
    }

    private static int Events(IHost host, CommandLine cli)
    {
        CommandLine.TrySetUtf8Console();

        Console.WriteLine(host.Services.GetRequiredService<StatsReporter>()
            .Events(cli.ValueOrDefault("--events", 40), cli.Value("--process")));

        return 0;
    }

    private static int Sessions(IHost host, CommandLine cli)
    {
        CommandLine.TrySetUtf8Console();

        Console.WriteLine(host.Services.GetRequiredService<StatsReporter>()
            .Sessions(cli.ValueOrDefault("--sessions", 40)));

        return 0;
    }

    private static int Derive(IHost host)
    {
        CommandLine.TrySetUtf8Console();

        var result = host.Services.GetRequiredService<DerivationRunner>()
            .RunAsync().GetAwaiter().GetResult();

        Console.WriteLine($"""

            === DERIVATION ===
              raw events      : {result.RawEvents}
              after noise     : {result.AfterNoise}  ({result.RawEvents - result.AfterNoise} dropped)
              activities      : {result.Activities}
              sessions        : {result.Sessions}
              unclassified    : {result.PendingIdentities} identities, {Humanise(result.UnclassifiedSeconds)}
              commits linked  : {result.CommitsLinked}  ({result.CommitsUnattached} unattached)
              elapsed         : {(int)result.Elapsed.TotalMilliseconds}ms

            Unclassified time is reported separately rather than folded into a
            real category, so it cannot quietly flatter the focus ratio.
            Unattached commits are counted, not dropped — usually because they
            predate the collector or landed outside any session's window.
            Run --unknowns to see what is pending, --commits to see linkage.
            """);

        return 0;
    }

    private static int Unknowns(IHost host, CommandLine cli)
    {
        CommandLine.TrySetUtf8Console();

        var rules = host.Services.GetRequiredService<ClassificationRuleStore>()
            .GetAllAsync().GetAwaiter().GetResult();

        var pending = rules
            .Where(r => r.IsPending && r.Scope == RuleScope.Site)
            .OrderByDescending(r => r.TotalSeconds)
            .Take(cli.ValueOrDefault("--unknowns", 30))
            .ToList();

        if (pending.Count == 0)
        {
            Console.WriteLine("\nNothing pending — every identity seen so far has a verdict.\n");
            return 0;
        }

        Console.WriteLine("\n=== PENDING IDENTITIES (most time first) ===\n");

        foreach (var r in pending)
        {
            Console.WriteLine($"  {Humanise(r.TotalSeconds),9}  {r.Hits,4} hits   {r.Site}");
        }

        Console.WriteLine($"""

              {pending.Count} pending. Ordered by time, so answering the top few
              covers most of the unclassified total and the tail can be ignored.

              Answer one:  --classify "<identity>" <Category>
              Categories:  {string.Join(" ", Enum.GetNames<ActivityCategory>())}

              These will be filled in automatically once local-LLM classification
              lands; answering by hand is an override, not a chore.
            """);

        return 0;
    }

    private static int Classify(IHost host, CommandLine cli)
    {
        CommandLine.TrySetUtf8Console();

        var values = cli.ValuesAfter("--classify");

        if (values.Length < 2)
        {
            Console.WriteLine("""
                usage: --classify "<identity>" <Category> [--keyword "<kw>"]
                e.g.   --classify "Model Context Protocol" Learning
                """);
            return 1;
        }

        var site = string.Join(' ', values[..^1]);
        var categoryName = values[^1];

        if (!ActivityCategoryExtensions.TryParse(categoryName, out var category))
        {
            Console.WriteLine(
                $"Unknown category '{categoryName}'. "
                + $"Valid: {string.Join(", ", Enum.GetNames<ActivityCategory>())}");
            return 1;
        }

        var promoted = host.Services.GetRequiredService<ClassificationRuleStore>()
            .ClassifyAsync(
                site,
                category,
                cli.Value("--keyword"),
                source: "manual",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            .GetAwaiter().GetResult();

        Console.WriteLine($"  {site} => {category}");

        if (promoted)
        {
            Console.WriteLine("""

                  This site now has two different verdicts, so it has been marked
                  mixed-use: the previous answer was kept as a page rule, and future
                  pages from this site are classified individually rather than by a
                  single site-wide guess.
                """);
        }

        Console.WriteLine("\n  Run --derive to apply.\n");
        return 0;
    }

    private static int ScanGit(IHost host)
    {
        CommandLine.TrySetUtf8Console();

        var result = host.Services.GetRequiredService<GitScanRunner>()
            .RunAsync().GetAwaiter().GetResult();

        Console.WriteLine($"""

            === GIT SCAN ===
              new commits     : {result.Scanned}
              already known   : {result.Skipped}  (skipped before touching a tree)
              repos failed    : {result.ReposFailed}

            Only new commits are diffed — a repeat scan pays only for what
            has changed since last time. Run --derive next to link them to
            sessions, then --commits to see the result.
            """);

        return 0;
    }

    private static int Commits(IHost host, CommandLine cli)
    {
        CommandLine.TrySetUtf8Console();

        var commits = host.Services.GetRequiredService<ICommitStore>()
            .GetAllAsync().GetAwaiter().GetResult();

        var recent = commits
            .OrderByDescending(c => c.TsUtc)
            .Take(cli.ValueOrDefault("--commits", 20))
            .ToList();

        if (recent.Count == 0)
        {
            Console.WriteLine("\nno commits — run --scan-git first\n");
            return 0;
        }

        Console.WriteLine($"=== LAST {recent.Count} COMMITS ===\n");

        foreach (var c in recent.OrderBy(c => c.TsUtc))
        {
            var when = c.Timestamp.ToLocalTime();
            var linked = c.SessionId is { } id ? $"session {id}" : "unattached";
            var branch = c.Branch ?? "?";
            var message = c.Message is { Length: > 50 } m ? m[..50] + "…" : c.Message ?? "";

            Console.WriteLine(
                $"  {when:MM-dd HH:mm}  {c.Project,-16} {branch,-24} "
                + $"+{c.Insertions}/-{c.Deletions,-5} {linked,-12} {message}");
        }

        var unattached = commits.Count(c => c.SessionId is null);
        Console.WriteLine($"\n  {commits.Count} total, {unattached} unattached\n");

        return 0;
    }

    /// <summary>
    /// What actually loaded, as opposed to what a JSON file says. Exists because
    /// content root defaulting to the process's working directory once made
    /// appsettings.json invisible for any launch that was not from the exe's own
    /// folder — silently, with every setting falling back to its C# default. This
    /// makes that class of bug diagnosable in ten seconds instead of an hour.
    /// </summary>
    private static int Config(IHost host)
    {
        CommandLine.TrySetUtf8Console();

        var env = host.Services.GetRequiredService<IHostEnvironment>();
        var devlog = host.Services.GetRequiredService<Devlog.Core.Configuration.DevlogOptions>();
        var git = host.Services.GetRequiredService<Devlog.Core.Configuration.GitOptions>();

        Console.WriteLine($"""

            === CONFIG ===
              content root       : {env.ContentRootPath}
              database           : {devlog.ResolveDatabasePath()}
              excluded processes : {(devlog.ExcludedProcesses.Length == 0 ? "(none)" : string.Join(", ", devlog.ExcludedProcesses))}
              excluded titles    : {(devlog.ExcludedTitlePatterns.Length == 0 ? "(none)" : string.Join(", ", devlog.ExcludedTitlePatterns))}
              git repos          : {git.Repos.Count}
            """);

        foreach (var r in git.Repos)
        {
            Console.WriteLine($"    {r.Path} -> {r.Project}");
        }

        if (devlog.ExcludedProcesses.Length == 0 && devlog.ExcludedTitlePatterns.Length == 0)
        {
            Console.WriteLine("""

                  Both exclusion lists are empty. If appsettings.json configures
                  some and this still shows none, appsettings.json is not being
                  found — check content root above against where the file lives.
                """);
        }

        return 0;
    }

    private static string Humanise(int seconds) => seconds switch
    {
        <= 0 => "—",
        < 60 => $"{seconds}s",
        < 3600 => $"{seconds / 60}m{seconds % 60:00}s",
        _ => $"{seconds / 3600}h{seconds % 3600 / 60:00}m"
    };
}
