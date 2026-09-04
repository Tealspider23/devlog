using System.Net;
using System.Windows.Forms;
using Devlog.Api;
using Devlog.Core.Abstractions;
using Devlog.Core.Configuration;
using Devlog.Host.Commands;
using Devlog.Host.HostedServices;
using Devlog.Host.Tray;
using Devlog.Infrastructure.Migrations;
using Devlog.Infrastructure.Persistence;
using Devlog.Infrastructure.Windows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Devlog.Host;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Absolute first line, before anything touches Console. See the doc
        // comment on AttachToParentConsoleIfInvokedWithArgs for why this exists:
        // without it, every --stats / --derive / etc. run from an interactive
        // terminal prints nothing at all, with no error.
        CommandLine.AttachToParentConsoleIfInvokedWithArgs(args);

        // ContentRootPath MUST be pinned to the executable's own folder, not left
        // at the default (Directory.GetCurrentDirectory()).
        //
        // The default silently breaks appsettings.json — including privacy
        // settings like ExcludedProcesses and ExcludedTitlePatterns — for any
        // launch that does not happen to have the exe's folder as the working
        // directory: a tray-icon double-click, a Start Menu shortcut, Task
        // Scheduler at logon, or simply running `& $exe` from a repo checkout.
        // In every one of those cases the config providers looked for
        // appsettings.json next to whatever the caller's CWD happened to be,
        // found nothing, and every setting silently fell back to its C#
        // hardcoded default with no error. AppContext.BaseDirectory is the one
        // location that is correct regardless of how the process was started.
        //
        // WebApplication.CreateBuilder rather than the plain generic host: this
        // process now hosts devlog's own API alongside the tray and the
        // collector. Devlog.Cli deliberately stays on the plain generic host —
        // see its own Program.cs — because the CLI must never open a socket.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

        // Not an ASP.NET Core convention name, so it needs adding explicitly.
        // Optional and reloadOnChange:false — most machines will not have one.
        // This is where real local repo paths belong: appsettings.json is
        // published to a public repo, and a machine's actual folder layout has
        // no business being in it.
        builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);

        // Read once, here, purely to configure Kestrel's bind before AddDevlog
        // registers the same values for DI. See the comment on BindApiOptions
        // for why this one section is read twice rather than restructuring
        // AddDevlog's return type.
        var apiOptions = builder.Configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>() ?? new ApiOptions();

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            // Loopback only. This address answers with the entire activity
            // log to anyone who can reach it — 0.0.0.0 or any other address
            // must never appear here, on this machine or anyone else's.
            kestrel.Listen(IPAddress.Loopback, apiOptions.Port);
        });

        builder.AddDevlog();

        using var host = builder.Build();

        // Schema first: nothing may touch the database before WAL is set and
        // migrations have run.
        host.Services.GetRequiredService<MigrationRunner>().Run();

        // "Off" does not mean the port stops listening — Kestrel is already
        // configured above, before args were known. It means nothing beyond
        // /health answers, which needs no separate code path: routes are
        // simply not mapped, so they 404. A real "stop listening" toggle
        // would need to skip web-server startup entirely, which WebApplication
        // does not offer a clean hook for — not worth the complexity for a
        // reflex switch nobody has asked to flip yet.
        if (apiOptions.Enabled)
        {
            host.MapDevlogApi(apiOptions);
        }
        else
        {
            host.MapGet("/health", () => Results.Ok(new { status = "disabled" }));
        }

        // No arguments means tray mode — a logon launch, a shortcut, a
        // double-click. Anything else was meant as a command, and if it is not
        // one, saying so beats silently starting a second collector: that is
        // exactly how a duplicate came to be running and double-recording once
        // already. Commands now live in `devlog`, which is why this only points
        // the way rather than growing its own help screen.
        if (args.Length == 0)
        {
            return RunTray(host);
        }

        var handled = DiagnosticCommands.TryRun(host, new CommandLine(args));

        if (handled is not null)
        {
            return handled.Value;
        }

        Console.Error.WriteLine(
            $"\n  Devlog.Host does not recognise: {string.Join(' ', args)}"
            + "\n  Run it with no arguments to start the collector in the tray,"
            + "\n  or use the `devlog` command for everything else.\n");

        return 2;
    }

    /// <summary>
    /// Runs the host and a WinForms message loop together.
    /// <para>
    /// The message loop is not decoration. <c>SetWinEventHook</c> and
    /// <c>SystemEvents</c> both deliver through the Windows message queue — with
    /// no pump the hooks install cleanly and then never fire, and the collector
    /// silently records nothing.
    /// </para>
    /// </summary>
    private static int RunTray(IHost host)
    {
        var logger = host.Services.GetRequiredService<ILogger<CollectorService>>();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        // Only one collector may run at a time. Two would both hook the desktop
        // and both write the same database, double-recording every focus change.
        //
        // This became likely rather than theoretical once startup registration
        // existed: logon launches one, and any manual start — or a mistyped CLI
        // flag, which falls through to tray mode — would add another. That exact
        // accident has already happened once during development.
        //
        // Local (per-session) rather than Global: capture is per-user by nature,
        // and two different users on one machine should each get their own.
        using var singleInstance = new Mutex(initiallyOwned: true, @"Local\devlog.collector", out var isOnly);

        if (!isOnly)
        {
            logger.LogWarning("Another devlog collector is already running. Exiting rather than double-recording.");
            return 0;
        }

        // Returns once every hosted service has run up to its first await, so
        // CollectorService has already subscribed to the session monitor.
        host.Start();

        ApplicationConfiguration.Initialize();

        using var tray = new TrayIconContext(
            host.Services.GetRequiredService<PauseController>(),
            host.Services.GetRequiredService<ISqliteConnectionFactory>(),
            host.Services.GetRequiredService<ApiOptions>(),
            requestExit: lifetime.StopApplication);

        // Hooks MUST be installed here, on this thread, and nowhere else.
        //
        // SetWinEventHook with WINEVENT_OUTOFCONTEXT delivers callbacks to the
        // thread that installed the hook, and only while that thread is pumping
        // messages. Application.Run below is that pump. Installing from a
        // thread-pool thread — which is where CollectorService.ExecuteAsync ends
        // up after its first await — yields a hook that reports success and then
        // never fires, so capture degrades silently to the idle timer's 20s grid.
        host.Services.GetRequiredService<IActivityWatcher>().Start();
        host.Services.GetRequiredService<SessionSwitchMonitor>().Start();

        logger.LogInformation("devlog is running in the tray");
        Application.Run(tray);

        // Give the collector time to write CollectorStop and flush its buffer.
        host.StopAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        return 0;
    }
}
