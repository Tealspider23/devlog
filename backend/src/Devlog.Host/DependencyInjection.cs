using Devlog.Api;
using Devlog.Core.Abstractions;
using Devlog.Core.Configuration;
using Devlog.Host.Derivation;
using Devlog.Host.Diagnostics;
using Devlog.Host.HostedServices;
using Devlog.Host.Tray;
using Devlog.Infrastructure;
using Serilog;

namespace Devlog.Host;

/// <summary>
/// One place where the application is composed. Program.cs stays a thin entry
/// point that decides which command to run, rather than a growing list of
/// registrations.
/// </summary>
public static class DependencyInjection
{
    /// <param name="quietConsole">
    /// True for the CLI. Serilog's console sink is right for the collector, whose
    /// log lines <em>are</em> its output, and wrong for a one-shot command, where
    /// "[11:32:40 INF] Schema up to date at version 3" lands on top of the report
    /// you actually asked for. The file sink keeps everything either way, so
    /// nothing is lost — only moved out of the way.
    /// </param>
    public static IHostApplicationBuilder AddDevlog(
        this IHostApplicationBuilder builder,
        bool quietConsole = false)
    {
        var options = builder.BindOptions();
        builder.AddDevlogLogging(options, quietConsole);

        var api = builder.BindApiOptions();

        builder.Services.AddDevlogInfrastructure();
        builder.Services.AddDevlogHostServices();
        builder.Services.AddDevlogApi(api);

        return builder;
    }

    /// <summary>
    /// Separate from <see cref="BindOptions"/> only because <c>Devlog.Host</c>'s
    /// <c>Program.cs</c> also needs <see cref="ApiOptions.Port"/> before
    /// <c>AddDevlog</c> runs, to configure Kestrel's loopback bind at builder
    /// time. Binding twice from the same section is harmless — it is
    /// stateless, side-effect-free config parsing — and it avoids reshaping the
    /// return contract of the widely-called <c>AddDevlog</c>.
    /// </summary>
    private static ApiOptions BindApiOptions(this IHostApplicationBuilder builder)
    {
        var api = builder.Configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>() ?? new ApiOptions();
        builder.Services.AddSingleton(api);
        return api;
    }

    /// <summary>
    /// Options are bound eagerly and registered as instances rather than through
    /// <c>IOptions&lt;T&gt;</c>: they are read on the capture hot path, and none
    /// of them are meant to change without a restart.
    /// </summary>
    private static DevlogOptions BindOptions(this IHostApplicationBuilder builder)
    {
        var options = builder.Configuration
            .GetSection(DevlogOptions.SectionName)
            .Get<DevlogOptions>() ?? new DevlogOptions();

        var derivation = builder.Configuration
            .GetSection(DerivationOptions.SectionName)
            .Get<DerivationOptions>() ?? new DerivationOptions();

        var git = builder.Configuration
            .GetSection(GitOptions.SectionName)
            .Get<GitOptions>() ?? new GitOptions();

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(derivation);
        builder.Services.AddSingleton(git);

        return options;
    }

    private static void AddDevlogLogging(
        this IHostApplicationBuilder builder,
        DevlogOptions options,
        bool quietConsole)
    {
        var logDirectory = Path.Combine(
            Path.GetDirectoryName(options.ResolveDatabasePath())!,
            "logs");

        builder.Services.AddSerilog((_, cfg) => cfg
            .ReadFrom.Configuration(builder.Configuration)
            .WriteTo.Console(restrictedToMinimumLevel: quietConsole
                ? Serilog.Events.LogEventLevel.Warning
                : Serilog.Events.LogEventLevel.Information)
            .WriteTo.File(
                Path.Combine(logDirectory, "devlog-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,

                // More than one devlog process touches this file: the collector
                // runs all day while --stats, --derive and --seed come and go, and
                // the UI will join them later. Without shared:true the second
                // writer silently loses its output — exactly how the collector's
                // own startup lines went missing during Phase 1 verification.
                shared: true));
    }

    private static IServiceCollection AddDevlogHostServices(this IServiceCollection services)
    {
        services.AddSingleton<PauseController>();
        services.AddSingleton<StatsReporter>();
        services.AddSingleton<DerivationRunner>();
        services.AddSingleton<GitScanRunner>();

        // Exposed a second way, as the interface Devlog.Core defines, so
        // Devlog.Api's endpoints can depend on it without a reference to this
        // (Windows-only) project. Same singleton instance either way — this is
        // not a second object, just a second door into it.
        services.AddSingleton<IDerivationRunner>(sp => sp.GetRequiredService<DerivationRunner>());
        services.AddSingleton<IGitScanRunner>(sp => sp.GetRequiredService<GitScanRunner>());

        services.AddHostedService<CollectorService>();

        return services;
    }
}
