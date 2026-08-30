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
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
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
