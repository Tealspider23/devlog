using System.Text.Json;
using Devlog.Core.Abstractions;
using Devlog.Core.Ai;
using Devlog.Core.Configuration;
using Devlog.Core.Domain;
using Devlog.Core.Metrics;
using Devlog.Host.Ai;
using Devlog.Host.Derivation;
using Devlog.Host.Diagnostics;
using Devlog.Host.Startup;
using Devlog.Infrastructure.Persistence;

namespace Devlog.Host.Commands;

/// <summary>
/// The one-shot commands. Each builds nothing of its own — they resolve the same
/// services the tray app uses, so a diagnostic can never disagree with what the
/// running collector would do.
/// </summary>
public static class DiagnosticCommands
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

        if (cli.Has("--digest"))
        {
            return Digest(host, cli);
        }

        if (cli.Has("--llm"))
        {
            return Llm(host);
        }

        if (cli.Has("--classify-ai"))
        {
            return ClassifyAi(host, cli);
        }

        if (cli.Has("--narrate"))
        {
            return Narrate(host, cli);
        }

        if (cli.Has("--llm-fixtures"))
        {
            return LlmFixtures(host, cli);
        }

        if (cli.Has("--llm-eval"))
        {
            return LlmEval(host, cli);
        }

        if (cli.Has("--ask"))
        {
            return Ask(host, cli);
        }

        if (cli.Has("--config"))
        {
            return Config(host);
        }

        if (cli.Has("--purge-seed"))
        {
            return PurgeSeed(host, cli);
        }

        if (cli.Has("--startup"))
        {
            return Startup(cli);
        }

        if (cli.Has("--unclassify"))
        {
            return Unclassify(host, cli);
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
            .SessionsAsync(cli.ValueOrDefault("--sessions", 40))
            .GetAwaiter().GetResult());

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
            Run `devlog unknowns` to see what is pending, `devlog commits` for linkage.
            """);

        return 0;
    }

    private static int Unknowns(IHost host, CommandLine cli)
    {
        CommandLine.TrySetUtf8Console();

        var rules = host.Services.GetRequiredService<ClassificationRuleStore>()
            .GetAllAsync().GetAwaiter().GetResult();

        // Neither marker is awaiting a verdict. [seed] identities describe
        // fixtures, and [excluded] is the privacy rule working as designed —
        // listing either invites you to classify something that isn't activity.
        var all = rules
            .Where(r => r.IsPending && r.Scope == RuleScope.Site)
            .Where(r => !SyntheticData.IsSynthetic(r.Site) && !PrivacyMarker.IsExcluded(r.Site))
            .OrderByDescending(r => r.TotalSeconds)
            .ToList();

        var limit = cli.ValueOrDefault("--unknowns", 30);
        var pending = all.Take(limit).ToList();

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

        // The total, not the page. Reporting the page made `unknowns 3` claim
        // there were 3 things left to answer when there were nine.
        var shown = pending.Count == all.Count
            ? $"{all.Count} pending."
            : $"{all.Count} pending, {pending.Count} shown.";

        Console.WriteLine($"""

              {shown} Ordered by time, so answering the top few
              covers most of the unclassified total and the tail can be ignored.

              Answer one:  devlog classify "<identity>" <Category>
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
                usage: devlog classify "<identity>" <Category> [--keyword "<kw>"]
                e.g.   devlog classify "Model Context Protocol" Learning
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

        Console.WriteLine("\n  Run `devlog derive` to apply.\n");
        return 0;
    }

    /// <summary>
    /// The correction path for a stored verdict — there was previously no way to
    /// undo one at all. Deletes the row outright rather than marking it deleted:
    /// the identity returns to pending and is reconsidered on the next derive or
    /// classify-ai run, exactly like one that was never answered.
    /// </summary>
    private static int Unclassify(IHost host, CommandLine cli)
    {
        CommandLine.TrySetUtf8Console();

        var values = cli.ValuesAfter("--unclassify");

        if (values.Length < 1)
        {
            Console.WriteLine("""
                usage: devlog unclassify "<identity>" [--keyword "<kw>"]
                e.g.   devlog unclassify "August"
                """);
            return 1;
        }

        var site = string.Join(' ', values);
        var keyword = cli.Value("--keyword");

        var deleted = host.Services.GetRequiredService<ClassificationRuleStore>()
            .DeleteAsync(site, keyword)
            .GetAwaiter().GetResult();

        if (!deleted)
        {
            Console.WriteLine($"  No stored rule for '{site}'{(keyword is null ? "" : $" (keyword '{keyword}')")}.");
            return 1;
        }

        Console.WriteLine($"  Deleted the rule for '{site}'. It returns to pending on the next derive.");
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
            has changed since last time. Run `devlog derive` next to link them
            to sessions, then `devlog commits` to see the result.
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
            Console.WriteLine("\nno commits — run `devlog scan-git` first\n");
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
    /// The brag document. Defaults to the last 7 days; <c>--week</c> and
    /// <c>--month</c> are named shortcuts for the same thing, not a different
    /// code path — everything routes through <see cref="DigestBuilder"/>, the
    /// same generator <c>GET /v1/digest</c> calls.
    /// </summary>
    private static int Digest(IHost host, CommandLine cli)
    {
        CommandLine.TrySetUtf8Console();

        var today = DateOnly.FromDateTime(DateTime.Now);

        var (from, to) = cli switch
        {
            _ when cli.Has("--month") => CalendarRange.For(DigestRangeKind.Month, today),
            _ when cli.Has("--week") => CalendarRange.For(DigestRangeKind.Week, today),
            _ => (
                DateOnly.TryParse(cli.Value("--from"), out var f) ? f : today.AddDays(-6),
                DateOnly.TryParse(cli.Value("--to"), out var t) ? t : today)
        };

        if (from > to)
        {
            Console.WriteLine($"\n  --from ({from:yyyy-MM-dd}) is after --to ({to:yyyy-MM-dd}).\n");
            return 1;
        }

        var reader = host.Services.GetRequiredService<ISessionReader>();
        var (metrics, markdown) = DigestBuilder.BuildAsync(reader, from, to).GetAwaiter().GetResult();

        if (cli.Has("--prose"))
        {
            var proseRunner = host.Services.GetRequiredService<DigestProseRunner>();
            var fromUtc = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue)).ToUnixTimeMilliseconds();
            var toUtc = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue)).ToUnixTimeMilliseconds();

            var (proseMarkdown, note) = proseRunner.GenerateProseAsync(metrics, fromUtc, toUtc).GetAwaiter().GetResult();
            markdown = DigestProseSplice.Apply(markdown, metrics, proseMarkdown, note);
        }

        var outPath = cli.Value("--out");

        if (outPath is null)
        {
            Console.WriteLine(markdown);
            return 0;
        }

        File.WriteAllText(outPath, markdown);
        Console.WriteLine($"\n  Wrote {markdown.Length} characters to {outPath}\n");
        return 0;
    }

    /// <summary>
    /// Controls whether the collector launches at logon. Without it, a reboot or
    /// a crash silently ends capture — which cost a full day of data on
    /// 2026-09-01.
    /// </summary>
    private static int Startup(CommandLine cli)
    {
        CommandLine.TrySetUtf8Console();

        if (cli.Has("--enable"))
        {
            StartupRegistration.Enable();
            Console.WriteLine($"\n  Enabled. devlog will start at logon:\n    {StartupRegistration.DescribeTarget()}\n");
            return 0;
        }

        if (cli.Has("--disable"))
        {
            StartupRegistration.Disable();
            Console.WriteLine("\n  Disabled. devlog will no longer start at logon.\n");
            return 0;
        }

        var current = StartupRegistration.CurrentRegistration();

        if (current is null)
        {
            Console.WriteLine("""

                  Startup: NOT registered — capture will not resume after a reboot.

                  Enable with:  devlog startup --enable
                """);
            return 0;
        }

        Console.WriteLine($"\n  Startup: registered\n    {current}");

        // A stale path is worse than none: it looks configured but launches
        // nothing, and the failure is silent until you notice missing data.
        if (!StartupRegistration.IsRegistered)
        {
            Console.WriteLine($"""

                  WARNING: this does not match the current executable:
                    {StartupRegistration.DescribeTarget()}

                  The registered path is stale — likely from a build into a
                  different output folder. Run `devlog startup --enable` to fix.
                """);
        }

        Console.WriteLine();
        return 0;
    }

    /// <summary>
    /// Removes generated fixture rows once real capture makes them redundant.
    /// <para>
    /// Requires <c>--yes</c> to actually delete. This is the only command in
    /// devlog that destroys source-of-truth data, and a mistyped flag should not
    /// be able to do it — the same class of accident that once started a second
    /// collector because an unrecognised argument fell through to tray mode.
    /// </para>
    /// </summary>
    private static int PurgeSeed(IHost host, CommandLine cli)
    {
        CommandLine.TrySetUtf8Console();

        var maintenance = host.Services.GetRequiredService<MaintenanceStore>();
        var count = maintenance.CountSyntheticAsync().GetAwaiter().GetResult();

        if (count == 0)
        {
            Console.WriteLine("\nNo synthetic rows found — nothing to purge.\n");
            return 0;
        }

        if (!cli.Has("--yes"))
        {
            Console.WriteLine($"""

                {count} synthetic rows would be deleted from raw_event, along with
                any unanswered classification rules that only exist because of them.

                This deletes source-of-truth data and cannot be undone.
                Re-run with --yes to proceed:

                  devlog purge-seed --yes
                """);
            return 1;
        }

        var result = maintenance.PurgeSyntheticAsync().GetAwaiter().GetResult();

        Console.WriteLine($"""

            === PURGED ===
              raw_event rows deleted     : {result.RawEvents}
              pending rules deleted      : {result.PendingRules}
            """);

        // Every activity and session built from those rows is now invalid.
        // Rebuilding from source is simpler and safer than a surgical repair,
        // which is the entire point of keeping derived data disposable.
        var derived = host.Services.GetRequiredService<DerivationRunner>()
            .RunAsync().GetAwaiter().GetResult();

        Console.WriteLine($"""
              re-derived                 : {derived.Activities} activities, {derived.Sessions} sessions
              unclassified remaining     : {derived.PendingIdentities} identities, {Humanise(derived.UnclassifiedSeconds)}
            """);

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
        var api = host.Services.GetRequiredService<Devlog.Core.Configuration.ApiOptions>();
        var tokenPath = Path.Combine(Path.GetDirectoryName(devlog.ResolveDatabasePath())!, "api-token.txt");

        Console.WriteLine($"""

            === CONFIG ===
              content root       : {env.ContentRootPath}
              database           : {devlog.ResolveDatabasePath()}
              excluded processes : {(devlog.ExcludedProcesses.Length == 0 ? "(none)" : string.Join(", ", devlog.ExcludedProcesses))}
              excluded titles    : {(devlog.ExcludedTitlePatterns.Length == 0 ? "(none)" : string.Join(", ", devlog.ExcludedTitlePatterns))}
              git repos          : {git.Repos.Count}

              api                : {(api.Enabled ? "enabled" : "DISABLED — only /health answers")}
              api address        : http://127.0.0.1:{api.Port}  (loopback only, never reachable off this machine)
              api token          : {(File.Exists(tokenPath) ? $"present at {tokenPath}" : "not yet generated — created automatically the first time the collector runs")}
              api dev CORS       : {(string.IsNullOrWhiteSpace(api.DevCorsOrigin) ? "off (production posture)" : api.DevCorsOrigin)}
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

    /// <summary>
    /// AI provider diagnostics: model, endpoint reachability, and job switches.
    /// Follows the config command's privacy convention: reports whether an API key
    /// exists, never its value.
    /// </summary>
    private static int Llm(IHost host)
    {
        CommandLine.TrySetUtf8Console();

        var ai = host.Services.GetRequiredService<AiOptions>();

        Console.WriteLine("\n=== AI (LLM) ===");
        Console.WriteLine($"  enabled          : {(ai.Enabled ? "true" : "FALSE (all AI jobs disabled)")}");
        Console.WriteLine($"  configured model : {ai.Model}");
        Console.WriteLine($"  api key          : {(string.IsNullOrWhiteSpace(ai.ApiKey) ? "(none)" : "present")}");
        Console.WriteLine($"  connect timeout  : {ai.ConnectTimeoutSeconds}s");
        Console.WriteLine($"  request timeout  : {ai.RequestTimeoutSeconds}s");
        Console.WriteLine($"  min confidence   : {ai.MinConfidence:F2}");
        Console.WriteLine($"  batch size       : {ai.ClassifyBatchSize}");
        Console.WriteLine($"  jobs             : classify:{(ai.Jobs.Classify ? "on" : "off")}  narrate:{(ai.Jobs.Narrate ? "on" : "off")}  digest:{(ai.Jobs.Digest ? "on" : "off")}  ask:{(ai.Jobs.Ask ? "on" : "off")}");

        if (!ai.Enabled)
        {
            Console.WriteLine("\n  AI features are disabled in config (Ai:Enabled = false).\n");
            return 0;
        }

        var (resolvedEndpoint, reachable, reportedModel, error) = ProbeProvider(ai);

        Console.WriteLine($"  endpoint         : {resolvedEndpoint ?? "(probing failed - no provider found)"}");
        Console.WriteLine($"  status           : {(reachable ? $"REACHABLE (reported model: {reportedModel})" : $"UNREACHABLE ({error})")}");

        // Stated as a fact on every run, not raised as a warning. With a hosted
        // provider this is the steady state, and an alarm that fires every time
        // is one you stop reading - which would cost exactly the case it exists
        // for. A host that is not this machine is named, so `devlog llm` can
        // never answer "where does my data go" with a URL that looks local.
        if (Uri.TryCreate(resolvedEndpoint, UriKind.Absolute, out var uri))
        {
            var offMachine = !uri.IsLoopback;
            Console.WriteLine(offMachine
                ? $"  data leaves here : YES -> {uri.Host} (session titles, commit messages, branch names)"
                : "  data leaves here : no - provider is on this machine");
        }

        if (!reachable)
        {
            Console.WriteLine("""

                  No reachable OpenAI-compatible provider found.
                  devlog is fully functional without AI: capture, derivation, git
                  correlation, timeline and deterministic digests continue normally.
                  To enable AI features, start Ollama (http://127.0.0.1:11434) or LM Studio (http://127.0.0.1:1234),
                  or configure an explicit endpoint in appsettings.local.json.
                """);
        }
        else
        {
            Console.WriteLine("\n  Provider is ready for AI jobs (classify-ai, narrate, digest --prose, ask).\n");
        }

        return 0;
    }

    /// <summary>
    /// Renders <see cref="ClassifyAiResult"/> to the console. The runner itself
    /// does no I/O — same "one result, two renderers" discipline as
    /// <see cref="Narrate"/> — so this is the only place <c>devlog classify-ai</c>'s
    /// output is composed, and <c>POST /v1/classify-ai</c> renders the identical
    /// result as JSON instead.
    /// </summary>
    private static int ClassifyAi(IHost host, CommandLine cli)
    {
        CommandLine.TrySetUtf8Console();

        var limit = int.TryParse(cli.Value("--limit"), out var l) && l > 0 ? l : (int?)null;
        var dryRun = cli.Has("--dry-run");

        var runner = host.Services.GetRequiredService<ClassifyAiRunner>();
        var result = runner.RunAsync(dryRun, limit).GetAwaiter().GetResult();

        if (result.UnreachableReason is not null)
        {
            Console.WriteLine($"\n{result.UnreachableReason}\n");
            return 0;
        }

        if (result.Verdicts.Count == 0 && result.TotalPendingRemaining == 0)
        {
            Console.WriteLine("\nNothing pending — every identity seen so far has a verdict.\n");
            return 0;
        }

        var mode = result.DryRun ? "PROPOSED VERDICTS (--dry-run, no database writes)" : "CLASSIFIED VERDICTS";
        Console.WriteLine($"\n=== {mode} ===\n");

        foreach (var v in result.Verdicts)
        {
            Console.WriteLine($"  {v.Identity,-30} => {v.Category,-15} (confidence: {v.Confidence:F2}) — {v.Reason}");
        }

        if (result.Discards.Count > 0)
        {
            Console.WriteLine();
            foreach (var d in result.Discards)
            {
                Console.WriteLine($"  [skipped] {d}");
            }
        }

        Console.WriteLine($"""

              {result.ProcessedCount} processed, {result.SkippedCount} skipped/pending, {result.TotalPendingRemaining} total pending remaining.
            """);

        if (!result.DryRun && result.ProcessedCount > 0)
        {
            Console.WriteLine("  Run `devlog derive` to apply newly classified identities to existing sessions.\n");
        }
        else
        {
            Console.WriteLine();
        }

        return 0;
    }

    /// <summary>
    /// Renders <see cref="NarrateResult"/> to the console. The runner itself does
    /// no I/O — same "one result, two renderers" discipline as <c>ISessionReader</c>
    /// — so this is the only place <c>devlog narrate</c>'s output is composed, and
    /// <c>POST /v1/narrate</c> renders the identical result as JSON instead.
    /// </summary>
    private static int Narrate(IHost host, CommandLine cli)
    {
        CommandLine.TrySetUtf8Console();

        var since = cli.Value("--since");
        var limit = int.TryParse(cli.Value("--limit"), out var l) && l > 0 ? l : (int?)null;
        var dryRun = cli.Has("--dry-run");
        var force = cli.Has("--force");

        var runner = host.Services.GetRequiredService<NarrateRunner>();
        var result = runner.RunAsync(since, limit, dryRun, force).GetAwaiter().GetResult();

        if (result.Outcomes.Count == 0)
        {
            Console.WriteLine("\nNo sessions needing narration in the selected window.\n");
            return 0;
        }

        var mode = result.DryRun ? "PROPOSED NARRATIVES (--dry-run, no database writes)" : "SESSION NARRATIVES";
        Console.WriteLine($"\n=== {mode} ({result.Outcomes.Count} sessions) ===\n");

        foreach (var o in result.Outcomes)
        {
            var durationStr = Humanise(o.DurationSeconds);
            var projectStr = string.IsNullOrWhiteSpace(o.Project) ? "" : $" ({o.Project})";

            if (!o.Accepted || o.Narrative is null)
            {
                Console.WriteLine($"  [rejected] Session {o.SessionId}{projectStr} {durationStr}: {o.RejectionReason}");
                continue;
            }

            var n = o.Narrative;
            var wsStr = string.IsNullOrWhiteSpace(n.Workstream) ? "" : $" [{n.Workstream}]";
            Console.WriteLine($"  Session {o.SessionId}{projectStr} {durationStr} -> [{n.Kind}]{wsStr} (confidence: {n.Confidence:F2})");
            Console.WriteLine($"    \"{n.Narrative}\"");
            Console.WriteLine($"    Evidence: {string.Join(" | ", n.Evidence)}");
            Console.WriteLine();
        }

        Console.WriteLine($"\n  Finished: {result.AcceptedCount} accepted, {result.RejectedCount} rejected/skipped.\n");
        return 0;
    }

    private static int LlmFixtures(IHost host, CommandLine cli)
    {
        CommandLine.TrySetUtf8Console();

        var outDir = cli.Value("--out");
        var runner = host.Services.GetRequiredService<LlmFixturesRunner>();
        return runner.RunAsync(outDir).GetAwaiter().GetResult();
    }

    private static int LlmEval(IHost host, CommandLine cli)
    {
        CommandLine.TrySetUtf8Console();

        var dir = cli.Value("--dir");
        var runner = host.Services.GetRequiredService<LlmEvalRunner>();
        return runner.RunAsync(dir).GetAwaiter().GetResult();
    }

    private static (string? Endpoint, bool Reachable, string? ReportedModel, string? Error) ProbeProvider(AiOptions ai)
    {
        var candidates = !string.IsNullOrWhiteSpace(ai.Endpoint)
            ? [ai.Endpoint.TrimEnd('/')]
            : new[] { "http://127.0.0.1:11434/v1", "http://127.0.0.1:1234/v1" };

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, ai.ConnectTimeoutSeconds)) };

        string? lastError = null;
        foreach (var endpoint in candidates)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}/models");
                if (!string.IsNullOrWhiteSpace(ai.ApiKey))
                {
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ai.ApiKey);
                }

                using var resp = client.Send(req);
                if (resp.IsSuccessStatusCode)
                {
                    var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    string? matchedModel = null;
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var m in data.EnumerateArray())
                        {
                            if (m.TryGetProperty("id", out var idProp) && idProp.GetString() is { } id)
                            {
                                var normalizedId = id.StartsWith("models/", StringComparison.OrdinalIgnoreCase) ? id["models/".Length..] : id;
                                var normalizedConfig = ai.Model.StartsWith("models/", StringComparison.OrdinalIgnoreCase) ? ai.Model["models/".Length..] : ai.Model;

                                if (string.Equals(normalizedId, normalizedConfig, StringComparison.OrdinalIgnoreCase))
                                {
                                    matchedModel = id;
                                    break;
                                }
                                matchedModel ??= id;
                            }
                        }
                    }

                    return (endpoint, true, matchedModel ?? ai.Model, null);
                }

                lastError = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
            }
            catch (Exception ex)
            {
                lastError = ex.InnerException?.Message ?? ex.Message;
            }
        }

        return (ai.Endpoint, false, null, lastError ?? "connection refused");
    }

    private static int Ask(IHost host, CommandLine cli)
    {
        CommandLine.TrySetUtf8Console();
        var question = cli.Value("--ask");
        if (string.IsNullOrWhiteSpace(question))
        {
            Console.Error.WriteLine("Error: Missing question. Usage: devlog ask \"<question>\"");
            return 1;
        }

        var quiet = cli.Has("--quiet");
        var runner = host.Services.GetRequiredService<AskRunner>();
        var result = runner.AskAsync(question, quiet: quiet).GetAwaiter().GetResult();

        if (!result.Success)
        {
            Console.Error.WriteLine($"Error: {result.Error}");
            return 1;
        }

        Console.WriteLine(result.Answer);
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
