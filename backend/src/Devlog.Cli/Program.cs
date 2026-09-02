using Devlog.Host;
using Devlog.Host.Commands;
using Devlog.Host.Diagnostics;
using Devlog.Infrastructure.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Devlog.Cli;

/// <summary>
/// The <c>devlog</c> command.
/// <para>
/// Reads and rebuilds; never captures. Capture belongs to the tray app, which is
/// a separate process for a reason — it must own a message pump for the Win32
/// hooks, and it must be the only one running.
/// </para>
/// <para>
/// This entry point deliberately cannot start the collector. The old single-exe
/// arrangement fell through to tray mode whenever an argument was not
/// recognised, so a typo started a second collector that then double-recorded
/// every focus change. Here, an unknown command prints help and exits 2.
/// </para>
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        CommandLine.TrySetUtf8Console();

        var command = CommandLine.CommandName(args);

        // Help before anything else touches configuration or the database. The
        // moment you most need the help screen is when one of those is broken.
        if (CommandLine.WantsHelp(args))
        {
            Console.WriteLine(HelpScreen.Render(TryReadStatus()));
            return 0;
        }

        if (command is null || !CommandCatalog.IsKnown(command))
        {
            Console.Error.WriteLine(HelpScreen.Unknown(command ?? string.Empty));
            Console.WriteLine(HelpScreen.Render());
            return 2;
        }

        using var host = BuildHost(args);
        host.Services.GetRequiredService<MigrationRunner>().Run();

        // Null is impossible here: the catalogue and the dispatcher cover the
        // same set, and the command was checked against the catalogue above. If
        // it ever happens, one of them has grown an entry the other lacks —
        // which is worth saying out loud rather than silently doing nothing.
        return DiagnosticCommands.TryRun(host, new CommandLine(args))
            ?? Unhandled(command);
    }

    private static IHost BuildHost(string[] args)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                Args = args,

                // Same reasoning as the tray app: the default is the caller's
                // working directory, which for a command meant to be run from
                // anywhere would mean appsettings.json is essentially never
                // found and every setting silently falls back to its hardcoded
                // default. That bug cost the privacy exclusions once already.
                ContentRootPath = AppContext.BaseDirectory
            });

        builder.Configuration.AddJsonFile(
            "appsettings.local.json", optional: true, reloadOnChange: false);

        builder.AddDevlog(quietConsole: true);

        return builder.Build();
    }

    /// <summary>
    /// The status line on the help screen. Best-effort by design — a missing or
    /// locked database must not stop help from rendering.
    /// </summary>
    private static string? TryReadStatus()
    {
        try
        {
            using var host = BuildHost([]);
            host.Services.GetRequiredService<MigrationRunner>().Run();
            return host.Services.GetRequiredService<StatsReporter>().Summary();
        }
        catch (Exception ex) when (Environment.GetEnvironmentVariable("DEVLOG_DEBUG") is null)
        {
            _ = ex;
            return null;
        }
    }

    private static int Unhandled(string command)
    {
        Console.Error.WriteLine(
            $"\n  `{command}` is listed in the command catalogue but nothing handles it."
            + "\n  This is a bug in devlog, not in what you typed.\n");

        return 70;
    }
}
