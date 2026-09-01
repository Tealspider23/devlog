using System.Windows.Forms;
using Devlog.Core.Abstractions;
using Devlog.Host.Commands;
using Devlog.Host.HostedServices;
using Devlog.Host.Tray;
using Devlog.Infrastructure.Migrations;
using Devlog.Infrastructure.Persistence;
using Devlog.Infrastructure.Windows;

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
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
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

        builder.AddDevlog();

        using var host = builder.Build();

        // Schema first: nothing may touch the database before WAL is set and
        // migrations have run.
        host.Services.GetRequiredService<MigrationRunner>().Run();

        return DiagnosticCommands.TryRun(host, new CommandLine(args)) ?? RunTray(host);
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

        // Returns once every hosted service has run up to its first await, so
        // CollectorService has already subscribed to the session monitor.
        host.Start();

        ApplicationConfiguration.Initialize();

        using var tray = new TrayIconContext(
            host.Services.GetRequiredService<PauseController>(),
            host.Services.GetRequiredService<ISqliteConnectionFactory>(),
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
